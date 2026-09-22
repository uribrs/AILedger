using System.Text;
using System.Text.Json;
using AILedger.Cli.Routing;
using AILedger.Cli.Runs;
using AILedger.Core.Contracts;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.Artifacts;

internal sealed class ArtifactCliCommands(
    CliCommandExecutor executor,
    TextReader processStandardInput)
{
    private const int MaximumArtifactBodyBytes = 1024 * 1024;

    public IEnumerable<CliCommandRegistration> Registrations()
    {
        yield return new CliCommandRegistration(
            ["artifact recon-template"], CliCommandOptions.Set("root", "task"),
            isReadOnly: true, ReconTemplateAsync);
        yield return new CliCommandRegistration(
            ["artifact record"],
            CliCommandOptions.Set(
                "root", "task", "actor", "id", "kind", "title", "body-stdin", "work", "also-work", "run",
                "supersedes", "cause", "correlation"),
            isReadOnly: false,
            RecordAsync);
        yield return new CliCommandRegistration(
            ["artifact show"], CliCommandOptions.Set("root", "task", "actor", "id", "json"),
            isReadOnly: true, ShowAsync);
        yield return new CliCommandRegistration(
            ["artifact list"], CliCommandOptions.Set("root", "task", "actor", "work", "also-work", "kind"),
            isReadOnly: true, ListAsync);
    }

    private async Task ReconTemplateAsync(CliCommandInvocation invocation, CancellationToken cancellationToken)
    {
        var state = await CliCommandExecutor.RequireStateAsync(invocation, cancellationToken).ConfigureAwait(false);
        // Templates retain null domains so authors can fill every assessment explicitly.
        var template = JsonSerializer.Serialize(
            InternalReconDocuments.CreateTemplate(state), new JsonSerializerOptions { WriteIndented = true });
        await executor.WriteLineAsync(template).ConfigureAwait(false);
    }

    private async Task RecordAsync(CliCommandInvocation invocation, CancellationToken cancellationToken)
    {
        var input = invocation.Input;
        _ = Task(input);
        var artifactId = new ArtifactId(input.Required("id"));
        if (!input.Flag("body-stdin"))
        {
            throw new CliUsageException("Artifact record requires '--body-stdin'.");
        }
        if (!Console.IsInputRedirected && ReferenceEquals(Console.In, processStandardInput))
        {
            throw new CliUsageException("Artifact record requires redirected standard input for '--body-stdin'.");
        }

        var coverage = AssuranceCliInput.Selection(input);
        var body = await ReadBodyAsync(cancellationToken).ConfigureAwait(false);
        await executor.ExecuteAsync(
            invocation,
            new RecordArtifactCommand(
                Actor(input), Cause(input), Correlation(input), artifactId,
                EnumValue<GovernedArtifactKind>(input, "kind"), input.Required("title"), body,
                OptionalId(input.Optional("work"), value => new WorkItemId(value)),
                OptionalExistingRun(input),
                OptionalId(input.Optional("supersedes"), value => new ArtifactId(value)), coverage),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ShowAsync(CliCommandInvocation invocation, CancellationToken cancellationToken)
    {
        var state = await CliCommandExecutor.RequireStateAsync(invocation, cancellationToken).ConfigureAwait(false);
        var artifactId = new ArtifactId(invocation.Input.Required("id"));
        if (!state.Artifacts.TryGetValue(artifactId, out var artifact))
        {
            throw new CliUsageException($"Artifact '{artifactId}' was not found.");
        }

        if (invocation.Input.Flag("json"))
        {
            await executor.WriteJsonAsync(artifact).ConfigureAwait(false);
            return;
        }

        await executor.WriteAsync(artifact.Content).ConfigureAwait(false);
    }

    private async Task ListAsync(CliCommandInvocation invocation, CancellationToken cancellationToken)
    {
        var state = await CliCommandExecutor.RequireStateAsync(invocation, cancellationToken).ConfigureAwait(false);
        var selection = AssuranceCliInput.Selection(invocation.Input);
        if (selection is null && AssuranceCliInput.Anchor(invocation.Input) is { } anchor)
        {
            selection = WorkCoverage.Effective(anchor, null);
        }
        var kind = invocation.Input.Optional("kind") is { } kindValue
            ? ParseEnum<GovernedArtifactKind>(kindValue)
            : (GovernedArtifactKind?)null;
        var rows = state.Artifacts.Values
            .Where(artifact => selection is null ||
                WorkCoverage.Effective(artifact.WorkItemId, artifact.Assurance).Intersect(selection).Any())
            .Where(artifact => kind is null || artifact.Kind == kind)
            .OrderBy(artifact => artifact.ArtifactId.Value, StringComparer.Ordinal)
            .Select(artifact => Metadata(state, artifact))
            .ToArray();

        await executor.WriteJsonAsync(new { state.TaskId, Artifacts = rows }).ConfigureAwait(false);
    }

    private static async Task<string> ReadBodyAsync(CancellationToken cancellationToken)
    {
        var body = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await Console.In.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            body.Append(buffer, 0, read);
            if (body.Length > MaximumArtifactBodyBytes)
            {
                throw new CliUsageException(
                    $"Artifact body exceeds the {MaximumArtifactBodyBytes}-byte UTF-8 limit.");
            }
        }

        if (body.Length == 0)
        {
            throw new CliUsageException("Artifact body cannot be empty.");
        }

        var content = body.ToString();
        int byteCount;
        try
        {
            byteCount = new UTF8Encoding(false, true).GetByteCount(content);
        }
        catch (EncoderFallbackException exception)
        {
            throw new CliUsageException($"Artifact body is not valid UTF-8 text: {exception.Message}");
        }

        if (byteCount > MaximumArtifactBodyBytes)
        {
            throw new CliUsageException(
                $"Artifact body exceeds the {MaximumArtifactBodyBytes}-byte UTF-8 limit.");
        }

        return content;
    }

    private static object Metadata(GovernedTaskState state, GovernedArtifact artifact) => new
    {
        artifact.ArtifactId,
        artifact.Kind,
        artifact.Title,
        artifact.WorkItemId,
        artifact.ProducerRunId,
        ProducerStatus = artifact.ProducerRunId is { } runId && state.Runs.TryGetValue(runId, out var producer)
            ? producer.Status : (AgentRunStatus?)null,
        artifact.SupersedesArtifactId,
        artifact.Provenance,
        artifact.Assurance,
        artifact.MemberReplacements,
        CoveredWorkItemIds = WorkCoverage.Effective(artifact.WorkItemId, artifact.Assurance),
        ApplicableWorkItemIds = ArtifactApplicability.CurrentMembers(state, artifact)
    };
}
