using AILedger.Cli.Routing;

namespace AILedger.Cli.Versioning;

internal static class VersionCliCommands
{
    public static CliCommandRegistration Registration(CliCommandExecutor executor) => new(
        ["version"],
        CliCommandOptions.Set(),
        isReadOnly: true,
        (_, _) => executor.WriteLineAsync(KernelVersion.Describe()));
}
