using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AILedger.Core.Domain;

namespace AILedger.Cli.Services;

internal sealed class ServiceEngine(HttpMessageHandler handler) : IDisposable
{
    public const string OwnerLabel = "ailedger.service";
    private readonly HttpClient _http = new(handler) { BaseAddress = new Uri("http://localhost/"), Timeout = TimeSpan.FromSeconds(30) };

    public async Task<string> ImageAsync(string image, CancellationToken token)
    {
        using var response = await _http.GetAsync($"images/{Uri.EscapeDataString(image)}/json", token).ConfigureAwait(false);
        using var json = await ReadAsync(response, token).ConfigureAwait(false);
        var id = json.RootElement.GetProperty("Id").GetString() ?? "";
        if (!Regex.IsMatch(id, "^sha256:[a-f0-9]{64}$"))
            throw new GovernanceException("Docker did not return an immutable local image ID; build/pull the image on the host first.");
        return id;
    }

    public async Task<string> CreateAsync(ServicePlan plan, CancellationToken token)
    {
        var port = $"{plan.Profile.ContainerPort}/tcp";
        var body = new
        {
            Image = plan.ImageId,
            Labels = new Dictionary<string, string> { [OwnerLabel] = plan.Owner },
            ExposedPorts = new Dictionary<string, object> { [port] = new { } },
            HostConfig = new
            {
                NetworkMode = "bridge", PublishAllPorts = false, ReadonlyRootfs = true,
                CapDrop = new[] { "ALL" }, SecurityOpt = new[] { "no-new-privileges" },
                Memory = 512L * 1024 * 1024, PidsLimit = 128, NanoCpus = 1_000_000_000L,
                RestartPolicy = new { Name = "no" },
                Tmpfs = new Dictionary<string, string> { ["/tmp"] = "rw,nosuid,nodev,size=64m" },
                LogConfig = new { Type = "json-file", Config = new Dictionary<string, string> { ["max-size"] = "5m", ["max-file"] = "1" } },
                PortBindings = new Dictionary<string, object>
                {
                    [port] = new[] { new { HostIp = "127.0.0.1", HostPort = plan.Profile.HostPort == 0 ? "" : plan.Profile.HostPort.ToString(System.Globalization.CultureInfo.InvariantCulture) } }
                }
            }
        };
        // Docker's API uses PascalCase; do not use the web serializer defaults here.
        using var response = await _http.PostAsync($"containers/create?name={plan.ContainerName}",
            new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"), token).ConfigureAwait(false);
        using var json = await ReadAsync(response, token).ConfigureAwait(false);
        return json.RootElement.GetProperty("Id").GetString() ?? throw new InvalidDataException("Docker returned no container ID.");
    }

    public async Task StartAsync(string id, CancellationToken token)
    {
        using var response = await _http.PostAsync($"containers/{Uri.EscapeDataString(id)}/start", null, token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, token).ConfigureAwait(false);
    }

    public async Task<JsonDocument?> InspectAsync(ServiceRecord record, CancellationToken token)
    {
        using var response = await _http.GetAsync($"containers/{Uri.EscapeDataString(record.ContainerId ?? record.Plan.ContainerName)}/json", token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        var json = await ReadAsync(response, token).ConfigureAwait(false);
        var root = json.RootElement;
        if (!root.GetProperty("Config").GetProperty("Labels").TryGetProperty(OwnerLabel, out var label) ||
            label.GetString() != record.Plan.Owner || root.GetProperty("Image").GetString() != record.Plan.ImageId)
        {
            json.Dispose();
            throw new GovernanceException("Service ownership/image mismatch; refusing to use or remove this container.");
        }
        return json;
    }

    public static string Endpoint(JsonElement container, ServicePlan plan)
    {
        if (!container.GetProperty("State").GetProperty("Running").GetBoolean())
            throw new GovernanceException("Service container is not running.");
        var host = container.GetProperty("HostConfig");
        if (host.GetProperty("NetworkMode").GetString() != "bridge" || host.GetProperty("PublishAllPorts").GetBoolean())
            throw new GovernanceException("Service network configuration differs from the reviewed plan.");
        string? endpoint = null;
        foreach (var port in container.GetProperty("NetworkSettings").GetProperty("Ports").EnumerateObject())
        {
            if (port.Value.ValueKind == JsonValueKind.Null) continue;
            if (port.Name != $"{plan.Profile.ContainerPort}/tcp" || port.Value.GetArrayLength() != 1)
                throw new GovernanceException("Service has an unexpected published port.");
            var binding = port.Value[0];
            if (binding.GetProperty("HostIp").GetString() != "127.0.0.1" ||
                !int.TryParse(binding.GetProperty("HostPort").GetString(), out var number) || number is < 1 or > 65535 ||
                plan.Profile.HostPort != 0 && number != plan.Profile.HostPort)
                throw new GovernanceException("Service port is not the reviewed IPv4 loopback binding.");
            endpoint = $"http://127.0.0.1:{number}";
        }
        return endpoint ?? throw new GovernanceException("Service has no published endpoint.");
    }

    public async Task RetainLogsAsync(string id, string path, CancellationToken token)
    {
        using var response = await _http.GetAsync($"containers/{Uri.EscapeDataString(id)}/logs?stdout=true&stderr=true&tail=2000",
            HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        await EnsureSuccessAsync(response, token).ConfigureAwait(false);
        await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 8192, true);
        var buffer = new byte[8192];
        var remaining = 5 * 1024 * 1024;
        while (remaining > 0)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, Math.Min(remaining, buffer.Length)), token).ConfigureAwait(false);
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            remaining -= read;
        }
    }

    public async Task RemoveAsync(string id, CancellationToken token)
    {
        using var response = await _http.DeleteAsync($"containers/{Uri.EscapeDataString(id)}?force=true&v=true", token).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NotFound) await EnsureSuccessAsync(response, token).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken token)
    {
        await EnsureSuccessAsync(response, token).ConfigureAwait(false);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        throw new GovernanceException($"Docker service request failed ({(int)response.StatusCode}): {body[..Math.Min(500, body.Length)]}");
    }

    public void Dispose() => _http.Dispose();
}
