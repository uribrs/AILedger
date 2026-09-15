using AILedger.Cli.ContextBriefing;
using AILedger.Cli.Providers;
using AILedger.Cli.Routing;
using AILedger.Core.Contracts;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.WorkItems;

internal sealed class WorkItemCliCommands(
    CliCommandExecutor executor,
    ContextBriefingCliCommands contextCommands)
{
    public IEnumerable<CliCommandRegistration> Registrations()
    {
        yield return new CliCommandRegistration(
            ["work add"],
            CliCommandOptions.Set(
                "root", "task", "actor", "id", "title", "owner", "depends-on", "scope",
                "not-split-because", "base-ref", "cognitive-root", "without-brief", "with-stale-brief",
                "cause", "correlation"),
            isReadOnly: false,
            AddAsync);
        yield return Mutation(
            "work complete",
            ["root", "task", "actor", "id", "without-verification", "cause", "correlation"],
            input => new CompleteWorkItemCommand(
                Actor(input), Cause(input), Correlation(input), new WorkItemId(input.Required("id")),
                input.Optional("without-verification")));
        yield return Mutation(
            "work block",
            ["root", "task", "actor", "id", "reason", "escalation", "cause", "correlation"],
            input => new BlockWorkItemCommand(
                Actor(input), Cause(input), Correlation(input), new WorkItemId(input.Required("id")),
                input.Required("reason"),
                OptionalId(input.Optional("escalation"), value => new EscalationId(value))));
        yield return Mutation(
            "work unblock",
            ["root", "task", "actor", "id", "cause", "correlation"],
            input => new UnblockWorkItemCommand(
                Actor(input), Cause(input), Correlation(input), new WorkItemId(input.Required("id"))));
        yield return Mutation(
            "work abandon",
            ["root", "task", "actor", "id", "reason", "cause", "correlation"],
            input => new AbandonWorkItemCommand(
                Actor(input), Cause(input), Correlation(input), new WorkItemId(input.Required("id")),
                input.Required("reason")));
    }

    private async Task AddAsync(CliCommandInvocation invocation, CancellationToken cancellationToken)
    {
        var input = invocation.Input;
        var scopes = input.Many("scope")
            .Select(ProviderGrantResolver.ResolveExistingScope)
            .Distinct(ProviderGrantResolver.PathComparer)
            .ToArray();
        ProviderGrantResolver.EnsureLedgerIsOutsideProviderDirectories(invocation.LedgerRoot, scopes);
        await executor.ExecuteAsync(
            invocation,
            new AddWorkItemCommand(
                Actor(input), Cause(input), Correlation(input), new WorkItemId(input.Required("id")),
                input.Required("title"), OptionalId(input.Optional("owner"), value => new ActorId(value)),
                input.Many("depends-on").Select(value => new ClaimId(value)).ToArray(), scopes,
                OptionalId(input.Optional("not-split-because"), value => new AlternativeId(value)),
                ResolveBaseRef(input, scopes),
                await contextCommands.CurrentSkillsAsync(invocation, cancellationToken).ConfigureAwait(false),
                input.Optional("without-brief"),
                OptionalId(input.Optional("with-stale-brief"), value => new EvidenceId(value))),
            cancellationToken).ConfigureAwait(false);
    }

    private CliCommandRegistration Mutation(
        string name,
        string[] options,
        Func<CommandLine, LedgerCommand> create) => new(
        [name],
        CliCommandOptions.Set(options),
        isReadOnly: false,
        (invocation, cancellationToken) => executor.ExecuteAsync(
            invocation, create(invocation.Input), cancellationToken));

    private static string? ResolveBaseRef(CommandLine input, IReadOnlyList<string> scopes)
    {
        if (input.Optional("base-ref") is { } supplied)
        {
            return string.IsNullOrWhiteSpace(supplied) ? null : supplied.Trim();
        }
        if (scopes.Count == 0)
        {
            return null;
        }

        var directory = ProviderGrantResolver.ProviderDirectoryForScope(scopes[0]);
        return KernelVersion.Git(directory, "rev-parse HEAD") is { Length: > 0 } head ? head : null;
    }
}
