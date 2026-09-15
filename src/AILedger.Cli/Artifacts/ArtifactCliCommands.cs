using System.Text;
using AILedger.Cli.Routing;
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
            ["artifact record"],
            CliCommandOptions.Set(
                "root", "task", "actor", "id", "kind", "title", "body-stdin", "work", "run",
                "supersedes", "cause", "correlation"),
            isReadOnly: false,
            RecordAsync);
        yield return new CliCommandRegistration(
            ["artifact show"], CliCommandOptions.Set("root", "task", "actor", "id", "json"),
            isReadOnly: true, ShowAsync);
        yield return new CliCommandRegistration(
            ["artifact list"], CliCommandOptions.Set("root", "task", "actor", "work", "kind"),
            isReadOnly: true, ListAsync);
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

        var body = await ReadBodyAsync(cancellationToken).ConfigureAwait(false);
        await executor.ExecuteAsync(
            invocation,
            new RecordArtifactCommand(
                Actor(input), Cause(input), Correlation(input), artifactId,
                EnumValue<GovernedArtifactKind>(input, "kind"), input.Required("title"), body,
                OptionalId(input.Optional("work"), value => new WorkItemId(value)),
                OptionalExistingRun(input),
                OptionalId(input.Optional("supersedes"), value => new ArtifactId(value))),
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
        var workItemId = OptionalId(invocation.Input.Optional("work"), value => new WorkItemId(value));
        var kind = invocation.Input.Optional("kind") is { } kindValue
            ? ParseEnum<GovernedArtifactKind>(kindValue)
            : (GovernedArtifactKind?)null;
        var rows = state.Artifacts.Values
            .Where(artifact => workItemId is null || artifact.WorkItemId == workItemId)
            .Where(artifact => kind is null || artifact.Kind == kind)
            .OrderBy(artifact => artifact.ArtifactId.Value, StringComparer.Ordinal)
            .Select(Metadata)
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

    private static object Metadata(GovernedArtifact artifact) => new
    {
        artifact.ArtifactId,
        artifact.Kind,
        artifact.Title,
        artifact.WorkItemId,
        artifact.ProducerRunId,
        artifact.SupersedesArtifactId,
        artifact.Provenance
    };
}
