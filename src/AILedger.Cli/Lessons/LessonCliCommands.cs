using AILedger.Cli.Routing;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Lessons;

internal sealed class LessonCliCommands(CliCommandExecutor executor)
{
    public IEnumerable<CliCommandRegistration> Registrations()
    {
        yield return new CliCommandRegistration(
            ["lesson mark"],
            CliCommandOptions.Set(
                "root", "task", "actor", "kind", "source", "class", "repo", "tag", "supersedes",
                "verify", "do-not", "lesson-actor", "lesson-kind", "audience", "verify-expects",
                "cause", "correlation"),
            isReadOnly: false,
            MarkAsync);
        yield return new CliCommandRegistration(
            ["lesson recheck"],
            CliCommandOptions.Set("repo", "id", "confirm", "working-directory", "timeout-seconds"),
            isReadOnly: true,
            RecheckAsync);
    }

    private Task MarkAsync(CliCommandInvocation invocation, CancellationToken cancellationToken)
    {
        var input = invocation.Input;
        return executor.ExecuteAsync(
            invocation,
            new MarkLessonBearingCommand(
                Actor(input), Cause(input), Correlation(input), MarkableSourceKind(input),
                input.Required("source"),
                OptionalId(input.Optional("supersedes"), value => new LessonId(value)),
                EnumValue<LessonClass>(input, "class"), input.Required("repo"), input.Many("tag"),
                input.Required("verify"), input.Required("do-not"),
                EnumValue<LessonActor>(input, "lesson-actor"),
                OptionalEnumValue<LessonKind>(input, "lesson-kind"),
                input.Many("audience").Select(ParseEnum<RoleKind>).ToArray(),
                OptionalEnumValue<VerifyExpectation>(input, "verify-expects")),
            cancellationToken);
    }

    private async Task RecheckAsync(CliCommandInvocation invocation, CancellationToken cancellationToken)
    {
        var input = invocation.Input;
        var repo = input.Required("repo");
        var workingDirectory = Path.GetFullPath(
            input.Optional("working-directory") ?? Environment.CurrentDirectory);
        if (!Directory.Exists(workingDirectory))
        {
            throw new CliUsageException($"Working directory '{workingDirectory}' does not exist.");
        }

        var lessons = await new FileLessonStore(invocation.LessonRoot).ReadAsync(cancellationToken)
            .ConfigureAwait(false);
        var confirmation = input.Optional("confirm");
        if (confirmation is null)
        {
            await executor.WriteJsonAsync(LessonRecheck.List(
                lessons, repo, workingDirectory, input.Many("id"))).ConfigureAwait(false);
            return;
        }

        var lesson = LessonRecheck.RequireConfirmed(lessons, repo, input.Many("id"), confirmation);
        var timeout = TimeSpan.FromSeconds(PositiveInt(
            input.Optional("timeout-seconds"), (int)LessonRecheck.DefaultTimeout.TotalSeconds));
        var report = await LessonRecheck.RunAsync(
            lesson, repo, workingDirectory, timeout, cancellationToken).ConfigureAwait(false);
        await executor.WriteJsonAsync(report).ConfigureAwait(false);
    }

    private static LessonSourceKind MarkableSourceKind(CommandLine input)
    {
        var kind = EnumValue<LessonSourceKind>(input, "kind");
        if (kind == LessonSourceKind.Imported)
        {
            throw new GovernanceException(
                "'imported' is not a markable source kind; it belongs to lessons carried in from the pre-kernel ledger.");
        }

        return kind;
    }

    private static int PositiveInt(string? value, int fallback) =>
        value is null ? fallback : int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : throw new CliUsageException("Timeout must be a positive integer.");
}
