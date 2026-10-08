using System.Net;
using System.Text.Json;
using AILedger.Cli;
using AILedger.Cli.Services;
using AILedger.Cli.Verification;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using static AILedger.Tests.Cli.CliApplicationTestSupport;

namespace AILedger.Tests.Verification;

public sealed class ServiceLifecycleTests
{
    [Fact]
    public async Task ActiveRuntimeLockAndOutputChangesDoNotBlockServiceStart()
    {
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        var confirm = await fixture.PreviewAsync();
        var runtime = Directory.CreateDirectory(Path.Combine(fixture.Inner.Checkout,
            "src", ".ailedger-output", "run-active")).FullName;
        await using var held = new FileStream(Path.Combine(runtime, ".lock"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        await File.WriteAllTextAsync(Path.Combine(runtime, "notes.md"), "new worker output");
        Assert.Equal(0, await fixture.StartAsync(confirm));
        Assert.Equal(1, fixture.Engine.Creates);
    }

    [Fact]
    public async Task PreviewHasNoContainerSideEffectsAndConfirmedLifecycleIsRetainedAndBriefed()
    {
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        var confirm = await fixture.PreviewAsync();
        Assert.Equal(0, fixture.Engine.Creates);
        Assert.False(Directory.Exists(fixture.Directory));
        Assert.Equal(0, await fixture.StartAsync(confirm));
        var created = fixture.Engine.Created!.Value;
        Assert.Equal(fixture.Engine.Image, created.GetProperty("Image").GetString());
        var config = created.GetProperty("HostConfig");
        Assert.Equal("127.0.0.1", config.GetProperty("PortBindings").GetProperty("8080/tcp")[0].GetProperty("HostIp").GetString());
        Assert.Equal("bridge", config.GetProperty("NetworkMode").GetString());
        Assert.False(config.GetProperty("PublishAllPorts").GetBoolean());
        Assert.True(config.GetProperty("ReadonlyRootfs").GetBoolean());
        Assert.False(config.TryGetProperty("Binds", out _));
        Assert.False(created.TryGetProperty("Env", out _));
        Assert.Single(config.GetProperty("CapDrop").EnumerateArray());
        var state = await fixture.StateAsync();
        Assert.Contains(ServiceCliCommands.StartedEvidence("S1"), state.Evidence.Keys);
        var brief = await fixture.BriefAsync();
        var service = Assert.Single(brief.Artifacts.Where(a => a.Id == "host-services"));
        Assert.Contains("http://127.0.0.1:19876", service.Content);
        Assert.Contains(fixture.Engine.Image, service.Content);

        Assert.Equal(0, await fixture.CommandAsync("stop"));
        Assert.Equal(1, fixture.Engine.Deletes);
        Assert.True(File.Exists(Path.Combine(fixture.Directory, "container.log")));
        Assert.True(File.Exists(Path.Combine(fixture.Directory, "stop.json")));
        var stopped = await ServiceRecord.ReadAsync(fixture.Directory, default);
        Assert.Equal(ServiceRecord.Hash(await File.ReadAllBytesAsync(Path.Combine(fixture.Directory, "container.log"))), stopped.LogSha256);
        Assert.DoesNotContain((await fixture.BriefAsync()).Artifacts, a => a.Id == "host-services");
        Assert.Equal(0, await fixture.CommandAsync("stop"));
        Assert.Equal(1, fixture.Engine.Deletes);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("profile")]
    [InlineData("image")]
    public async Task ConfirmationBindsSourceProfileAndResolvedImage(string mutation)
    {
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        var confirm = await fixture.PreviewAsync();
        if (mutation == "source") await File.WriteAllTextAsync(Path.Combine(fixture.Inner.Checkout, "dirty.txt"), "new");
        if (mutation == "profile") fixture.WriteProfile(19877);
        if (mutation == "image") fixture.Engine.Image = "sha256:" + new string('b', 64);
        Assert.Equal(1, await fixture.StartAsync(confirm));
        Assert.Contains("confirmation differs", fixture.Inner.Error.ToString());
        Assert.Equal(0, fixture.Engine.Creates);
        Assert.False(Directory.Exists(fixture.Directory));
    }

    [Theory]
    [InlineData("wildcard")]
    [InlineData("extra-port")]
    [InlineData("exited")]
    [InlineData("readiness")]
    [InlineData("lost-create-response")]
    public async Task FailedStartsRemoveOnlyTheOwnedContainerAndRetainFailure(string failure)
    {
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        var confirm = await fixture.PreviewAsync();
        fixture.Engine.Failure = failure;
        fixture.ProbeStatus = failure == "readiness" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
        Assert.Equal(1, await fixture.StartAsync(confirm));
        Assert.Equal(1, fixture.Engine.Creates);
        Assert.Equal(1, fixture.Engine.Deletes);
        Assert.Equal("failed", (await ServiceRecord.ReadAsync(fixture.Directory, default)).Status);
        Assert.True(File.Exists(Path.Combine(fixture.Directory, "start.json")));
        Assert.DoesNotContain((await fixture.BriefAsync()).Artifacts, a => a.Id == "host-services");
    }

    [Fact]
    public async Task ForeignOwnershipCannotBeRemovedOrOfferedAsAnEndpoint()
    {
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        Assert.Equal(0, await fixture.StartAsync(await fixture.PreviewAsync()));
        fixture.Engine.Failure = "foreign-owner";
        Assert.Equal(1, await fixture.CommandAsync("inspect"));
        Assert.Equal(1, await fixture.CommandAsync("stop"));
        Assert.Equal(0, fixture.Engine.Deletes);
        Assert.Equal("cleanup-failed", (await ServiceRecord.ReadAsync(fixture.Directory, default)).Status);
        Assert.DoesNotContain((await fixture.BriefAsync()).Artifacts, a => a.Id == "host-services");
    }

    [Fact]
    public async Task FailedCleanupCanBeRetriedWithoutLosingItsIdentity()
    {
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        Assert.Equal(0, await fixture.StartAsync(await fixture.PreviewAsync()));
        fixture.Engine.Failure = "delete";
        Assert.Equal(1, await fixture.CommandAsync("stop"));
        fixture.Engine.Failure = null;
        Assert.Equal(0, await fixture.CommandAsync("stop"));
        Assert.False(fixture.Engine.Present);
        Assert.Equal("stopped", (await ServiceRecord.ReadAsync(fixture.Directory, default)).Status);
    }

    [Fact]
    public async Task LogFailureDoesNotPreventCleanupAndIsRetained()
    {
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        Assert.Equal(0, await fixture.StartAsync(await fixture.PreviewAsync()));
        fixture.Engine.Failure = "logs";
        Assert.Equal(0, await fixture.CommandAsync("stop"));
        Assert.False(fixture.Engine.Present);
        Assert.True(File.Exists(Path.Combine(fixture.Directory, "log-retention-error.txt")));
        Assert.Contains("500", (await ServiceRecord.ReadAsync(fixture.Directory, default)).LogError);
    }

    [Fact]
    public async Task RetainedIdCannotStartTwiceAndStopHonorsOperationLock()
    {
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        var confirm = await fixture.PreviewAsync();
        Assert.Equal(0, await fixture.StartAsync(confirm));
        Assert.Equal(1, await fixture.StartAsync(confirm));
        using (new FileStream(Path.Combine(fixture.Directory, ".operation"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal(1, await fixture.CommandAsync("stop"));
        Assert.True(fixture.Engine.Present);
        Assert.Equal(1, fixture.Engine.Creates);
        Assert.Equal(0, await fixture.CommandAsync("stop"));
    }

    [Fact]
    public async Task WorkersCannotManageServicesAndReviewersReceiveNoMutableServiceBrief()
    {
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        Assert.Equal(0, await fixture.Inner.Application.RunAsync(
            ["actor", "attach", .. fixture.Inner.Common(), "--target", "worker", "--role", "worker"], default));
        foreach (var command in new[] { "start", "stop", "inspect" })
            Assert.Equal(1, await fixture.CommandAsync(command, actor: "worker"));
        Assert.Equal(0, fixture.Engine.Creates);
        Assert.Equal(0, await fixture.StartAsync(await fixture.PreviewAsync()));
        var state = await fixture.StateAsync();
        var manifest = (await fixture.BriefAsync()) with { Role = RoleKind.CodeReviewer,
            Artifacts = [] };
        var review = await ServiceBriefing.AppendAsync(manifest, state, fixture.Inner.Root, default);
        Assert.Empty(review.Artifacts);
    }

    [Theory]
    [InlineData("\"hostPort\":65536,")]
    [InlineData("\"readinessPath\":\"//outside.example/\",")]
    [InlineData("\"readinessPath\":\"/\\\\outside\",")]
    [InlineData("\"hostIp\":\"0.0.0.0\",")]
    [InlineData("\"networkMode\":\"host\",")]
    [InlineData("\"mounts\":[],")]
    [InlineData("\"image\":\"duplicate\",")]
    public async Task UnsafeOrUnknownProfileFieldsAreRefusedBeforeCreate(string extra)
    {
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        File.WriteAllText(Path.Combine(fixture.Inner.Checkout, ServiceProfiles.FileName),
            "{\"schemaVersion\":1,\"profiles\":{\"mock\":{" + extra + "\"image\":\"mock:latest\",\"containerPort\":8080}}}");
        Assert.Equal(1, await fixture.StartAsync(null));
        Assert.Equal(0, fixture.Engine.Creates);
    }

    [Fact]
    public async Task MutatedOrUnrecordedServiceReceiptIsNotBriefed()
    {
        using var fixture = new Fixture();
        await fixture.OpenAsync();
        Assert.Equal(0, await fixture.StartAsync(await fixture.PreviewAsync()));
        var receipt = await ServiceRecord.ReadAsync(fixture.Directory, default);
        await (receipt with { Endpoint = "http://127.0.0.1:12345" }).SaveAsync(fixture.Directory, default);
        Assert.DoesNotContain((await fixture.BriefAsync()).Artifacts, a => a.Id == "host-services");
    }

    private sealed class Fixture : IDisposable
    {
        public VerificationFixture Inner { get; } = new(ScriptedSpawner.NeverCalled());
        public Engine Engine { get; } = new();
        public HttpStatusCode ProbeStatus { get; set; } = HttpStatusCode.OK;
        public CliApplication App { get; }
        public string Directory => Path.Combine(Inner.TaskPath, "services", "S1");

        public Fixture()
        {
            App = new CliApplication(Inner.Output, Inner.Error, Service,
                _ => throw new InvalidOperationException(), new ContextAssembler())
            {
                VerificationHost = new VerificationHost
                {
                    GetEnvironmentVariable = name => name == "DOCKER_HOST" ? $"unix://{Inner.SocketPath}" : null,
                    CreateEngineHandler = _ => new Handler(Engine.RespondAsync),
                    CreateServiceProbeHandler = () => new Handler(_ => Task.FromResult(new HttpResponseMessage(ProbeStatus)))
                }
            };
            WriteProfile();
        }

        public void WriteProfile(int port = 19876) => File.WriteAllText(Path.Combine(Inner.Checkout, ServiceProfiles.FileName),
            JsonSerializer.Serialize(new ServiceProfiles(1, new() { ["mock"] = new("mock:latest", 8080, port, StartupSeconds: 1) }), ServiceProfiles.Json));
        public Task OpenAsync() => Inner.OpenAsync();
        public async Task<GovernedTaskState> StateAsync() => (await Service(Inner.Root).GetStateAsync(new TaskId("T1"), default))!;
        public async Task<ContextManifest> BriefAsync()
        {
            var state = await StateAsync();
            var manifest = new ContextAssembler().Build(state, new ActorId("operator"), null, [], DateTimeOffset.UtcNow);
            return await ServiceBriefing.AppendAsync(manifest, state, Inner.Root, default);
        }
        public Task<int> StartAsync(string? confirmation) => App.RunAsync(
            ["service", "start", .. Inner.Common(), "--id", "S1", "--checkout", Inner.Checkout, "--profile", "mock",
                .. (confirmation is null ? Array.Empty<string>() : new[] { "--confirm", confirmation })], default);
        public Task<int> CommandAsync(string command, string actor = "operator") => App.RunAsync(
            ["service", command, .. Inner.Common(actor), "--id", "S1"], default);
        public async Task<string> PreviewAsync()
        {
            var offset = Inner.Output.ToString().Length;
            Assert.Equal(0, await StartAsync(null));
            using var json = JsonDocument.Parse(Inner.Output.ToString()[offset..]);
            return json.RootElement.GetProperty("confirmation").GetString()!;
        }
        public void Dispose() => Inner.Dispose();
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }

    private sealed class Engine
    {
        public string Image { get; set; } = "sha256:" + new string('a', 64);
        public string? Failure { get; set; }
        public int Creates { get; private set; }
        public int Deletes { get; private set; }
        public bool Present { get; private set; }
        public JsonElement? Created { get; private set; }

        public async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/_ping") return Text("OK");
            if (path.StartsWith("/images/", StringComparison.Ordinal)) return Json(new { Id = Image });
            if (path == "/containers/create")
            {
                Creates++;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Created = body.RootElement.Clone();
                Present = true;
                if (Failure == "lost-create-response") throw new HttpRequestException("response lost after create");
                return Json(new { Id = "owned-id" });
            }
            if (path.EndsWith("/start", StringComparison.Ordinal)) return new(HttpStatusCode.NoContent);
            if (path.EndsWith("/json", StringComparison.Ordinal))
            {
                if (!Present) return new(HttpStatusCode.NotFound);
                var owner = Failure == "foreign-owner" ? "other" : Created!.Value.GetProperty("Labels").GetProperty(ServiceEngine.OwnerLabel).GetString();
                var ports = new Dictionary<string, object> { ["8080/tcp"] = new[] { new { HostIp = Failure == "wildcard" ? "0.0.0.0" : "127.0.0.1", HostPort = "19876" } } };
                if (Failure == "extra-port") ports["9999/tcp"] = new[] { new { HostIp = "0.0.0.0", HostPort = "19999" } };
                return Json(new { Id = "owned-id", Image, Config = new { Labels = new Dictionary<string, string?> { [ServiceEngine.OwnerLabel] = owner } },
                    State = new { Running = Failure != "exited" }, HostConfig = new { NetworkMode = "bridge", PublishAllPorts = false }, NetworkSettings = new { Ports = ports } });
            }
            if (path.EndsWith("/logs", StringComparison.Ordinal)) return Text("service logs", Failure == "logs" ? HttpStatusCode.InternalServerError : HttpStatusCode.OK);
            if (request.Method == HttpMethod.Delete)
            {
                Assert.Equal("/containers/owned-id", path);
                if (Failure == "delete") return Text("unavailable", HttpStatusCode.InternalServerError);
                Deletes++; Present = false; return new(HttpStatusCode.NoContent);
            }
            throw new InvalidOperationException(request.RequestUri.ToString());
        }
        private static HttpResponseMessage Json(object value) => Text(JsonSerializer.Serialize(value));
        private static HttpResponseMessage Text(string text, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(text) };
    }
}
