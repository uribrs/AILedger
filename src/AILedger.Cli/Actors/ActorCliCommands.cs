using AILedger.Cli.Routing;
using AILedger.Core.Contracts;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Actors;

internal static class ActorCliCommands
{
    public static CliCommandRegistration Registration(CliCommandExecutor executor) => new(
        ["actor attach"],
        CliCommandOptions.Set(
            "root", "task", "actor", "target", "role", "capability", "cause", "correlation"),
        isReadOnly: false,
        (invocation, cancellationToken) => ExecuteAsync(executor, invocation, cancellationToken));

    private static Task ExecuteAsync(
        CliCommandExecutor executor,
        CliCommandInvocation invocation,
        CancellationToken cancellationToken)
    {
        var input = invocation.Input;
        var role = EnumValue<RoleKind>(input, "role");
        var capabilities = input.Many("capability").Count == 0
            ? RoleDefaults.For(role)
            : input.Many("capability").Select(ParseEnum<Capability>).Distinct().OrderBy(value => value).ToArray();
        RoleDefaults.EnsureSafe(role, capabilities);
        return executor.ExecuteAsync(
            invocation,
            new AssignRoleCommand(
                Actor(input), Cause(input), Correlation(input), new ActorId(input.Required("target")),
                role, capabilities),
            cancellationToken);
    }
}
