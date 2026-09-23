namespace AILedger.Cli.Verification;

/// <summary>
/// Everything container verification reads from the machine, in one place so a test can replace
/// it: the invoker's environment, the transport to the Docker engine, the process that runs the
/// profile command, and the clock. Production uses the real ones. No test binds a unix socket
/// (PALT10); the engine transport is an injected <see cref="HttpMessageHandler"/>.
/// </summary>
internal sealed class VerificationHost
{
    public static VerificationHost Default { get; } = new();

    public Func<string, string?> GetEnvironmentVariable { get; init; } = Environment.GetEnvironmentVariable;

    /// <summary>Creates the engine transport for one unix socket path.</summary>
    public Func<string, HttpMessageHandler> CreateEngineHandler { get; init; } = UnixSocketEngineHandler.Create;

    public IVerificationProcessSpawner Spawner { get; init; } = new ShellVerificationProcessSpawner();

    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>
    /// Contract S2: the invoker's DOCKER_HOST when it is set, otherwise the Colima default socket
    /// under the invoker's home directory.
    /// </summary>
    public string DockerHost()
    {
        var configured = GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var home = GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return $"unix://{Path.Combine(home, ".colima", "default", "docker.sock")}";
    }
}
