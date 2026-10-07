using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using AILedger.Core.Contracts;
using AILedger.Core.Findings;
using AILedger.Core.Alternatives;
using AILedger.Core.ClaimDispositions;
using AILedger.Core.Artifacts;
using AILedger.Core.Authority;

namespace AILedger.Cli.Findings;

// The provider receives a relay address, never a writable binding/configuration file. The
// listening socket and the authority both live in the launching process. A different relay
// invocation cannot change this host's task, actor, run, destination or grants.
internal sealed class ProviderFindingsSession : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly byte[] _secret = RandomNumberGenerator.GetBytes(32);
    private readonly FindingsMcpHost _host;
    private readonly Task _accept;
    internal IReadOnlyList<AILedger.Cli.Dispatch.HostHandoffReceipt> CognitiveReceipts => _host.Cognitive?.Receipts ?? [];
    internal bool CognitiveHandoffUnknown => _host.Cognitive?.HasUnknown ?? false;
    internal ProviderFindingsEndpoint Endpoint { get; }

    internal ProviderFindingsSession(FindingsHostConfiguration configuration, IFindingsRecorder recorder,
        string command, IReadOnlyList<string> commandPrefix,
        Func<CancellationToken, Task<FindingsHostConfiguration>>? readConfiguration = null,
        AILedger.Core.Assurance.IAssuranceService? assurance = null,
        AILedger.Cli.Cognitive.CognitiveHandoffSession? cognitive = null)
    {
        _host = new(configuration, recorder, readConfiguration ?? (_ => Task.FromResult(configuration)), assurance) { Cognitive = cognitive };
        _listener.Start(8);
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Endpoint = new(command, commandPrefix.Concat(new[] { "findings", "relay",
            port.ToString(System.Globalization.CultureInfo.InvariantCulture), Convert.ToHexString(_secret) }).ToArray(), assurance?.Operations, configuration.AllowDeclareProducerOutcome, cognitive is not null);
        _accept = AcceptAsync();
    }

    internal static ProviderFindingsSession Start(IGovernedTaskService service, string ledgerRoot,
        TaskId task, ActorId subject, RunId run, EventId? cause, string provider,
        RoleAssignment assignment, DateTimeOffset expiresAt, string? cognitiveRoot = null,
        AILedger.Core.Assurance.IAssuranceService? assurance = null, CognitiveWorkKind? cognitiveWork = null)
    {
        if (assignment.ActorId != subject || service is not IAgentSessionServiceFactory factory)
            throw new InvalidOperationException("Provider recording requires a trusted, expiring session service binding.");
        var authority = new AgentSessionAuthority(task, assignment, run, provider, expiresAt);
        service = factory.BindAgentSession(authority);
        if (service is not IFindingsRecorder recorder || service is not IAlternativesRecorder || service is not IArtifactSubmitter || service is not IClaimDispositionsRecorder)
            throw new InvalidOperationException("Provider recording requires IFindingsRecorder, IAlternativesRecorder, IArtifactSubmitter and IClaimDispositionsRecorder services.");
        var configuration = new FindingsHostConfiguration(ledgerRoot, task.Value, subject.Value, run.Value,
            Path.Combine(ledgerRoot, task.Value, "telemetry"), RunId: run.Value, CausationId: cause?.Value,
            AllowRecordFindings: true, Provider: provider, AllowRecordAlternatives: true, AllowSubmitArtifact: true, AllowRecordClaimDispositions: true, AllowInspect: true, CognitiveRoot: cognitiveRoot, AllowDeclareProducerOutcome: assignment.Role is RoleKind.Worker or RoleKind.Researcher, AllowCognitiveHandoffs: true);
        // Neither a requested/preassigned session ID nor a future stream observation is a current
        // observation. Keep session null for this immutable connection; join via the actual run.
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var muxer = Path.GetFullPath(Path.Combine(runtime, "..", "..", "..",
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        async Task<FindingsHostConfiguration> ReadBinding(CancellationToken token)
        {
            var state = await service.GetStateAsync(task, token).ConfigureAwait(false);
            authority.EnsureCurrent(state, DateTimeOffset.UtcNow);
            return configuration;
        }
        return new(configuration, recorder, muxer, [typeof(CliApplication).Assembly.Location], ReadBinding, assurance,
            new AILedger.Cli.Cognitive.CognitiveHandoffSession(service, new(task, subject, run, run.Value), cognitiveWork));
    }

    private async Task AcceptAsync()
    {
        var connections = new List<Task>();
        using var capacity = new SemaphoreSlim(8);
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                await capacity.WaitAsync(_lifetime.Token).ConfigureAwait(false);
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_lifetime.Token).ConfigureAwait(false); }
                catch { capacity.Release(); throw; }
                connections.RemoveAll(task => task.IsCompletedSuccessfully);
                connections.Add(ServeAsync(client, capacity));
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { await Task.WhenAll(connections).ConfigureAwait(false); }
    }

    private async Task ServeAsync(TcpClient client, SemaphoreSlim capacity)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                using var admission = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                admission.CancelAfter(TimeSpan.FromSeconds(5));
                var supplied = new byte[_secret.Length];
                await stream.ReadExactlyAsync(supplied, admission.Token).ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(supplied, _secret)) return;
                await new FindingsMcpServer(_host, stream, stream, Console.Error)
                    .RunAsync(_lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
            {
                // A rejected/abandoned relay never enters the application. No invented receipt.
            }
            finally { capacity.Release(); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        try { await _accept.ConfigureAwait(false); }
        finally
        {
            _listener.Stop();
            _lifetime.Dispose();
            CryptographicOperations.ZeroMemory(_secret);
        }
    }
}
