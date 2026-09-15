using System.Text.Json;
using AILedger.Cli.Routing;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Cli.Routing.CliInput;

namespace AILedger.Cli.ContextBriefing;

internal sealed class ContextBriefingCliCommands(
    CliCommandExecutor executor,
    IContextAssembler contextAssembler,
    CognitiveArtifactLoader artifactLoader,
    JsonSerializerOptions json)
{
    public CliCommandRegistration Registration() => new(
        ["context build"],
        CliCommandOptions.Set("root", "task", "actor", "work", "cognitive-root", "output"),
        isReadOnly: false,
        BuildAsync);

    public async Task<ContextManifest> CreateAsync(
        IGovernedTaskService service,
        CommandLine input,
        ActorId actorId,
        CancellationToken cancellationToken)
    {
        var state = await service.GetStateAsync(Task(input), cancellationToken).ConfigureAwait(false)
            ?? throw new CliUsageException($"Task '{Task(input)}' was not found.");
        var artifacts = await artifactLoader.LoadAsync(
            input.Optional("cognitive-root"), cancellationToken).ConfigureAwait(false);
        var workItem = OptionalId(input.Optional("work"), value => new WorkItemId(value));
        return contextAssembler.Build(state, actorId, workItem, artifacts, DateTimeOffset.UtcNow);
    }

    public async Task<IReadOnlyList<ContextSkill>?> CurrentSkillsAsync(
        GovernedTaskState state,
        CommandLine input,
        CancellationToken cancellationToken)
    {
        if (!state.Roles.TryGetValue(Actor(input), out var assignment))
        {
            return null;
        }

        try
        {
            var artifacts = await artifactLoader.LoadAsync(
                input.Optional("cognitive-root"), cancellationToken).ConfigureAwait(false);
            return contextAssembler.SkillsServed(assignment.Role, artifacts);
        }
        catch (Exception exception) when (
            exception is DirectoryNotFoundException or FileNotFoundException or InvalidDataException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<ContextSkill>?> CurrentSkillsAsync(
        CliCommandInvocation invocation,
        CancellationToken cancellationToken)
    {
        var state = await invocation.Service.GetStateAsync(Task(invocation.Input), cancellationToken)
            .ConfigureAwait(false);
        return state is null
            ? null
            : await CurrentSkillsAsync(state, invocation.Input, cancellationToken).ConfigureAwait(false);
    }

    private async Task BuildAsync(CliCommandInvocation invocation, CancellationToken cancellationToken)
    {
        var input = invocation.Input;
        var state = await CliCommandExecutor.RequireStateAsync(invocation, cancellationToken).ConfigureAwait(false);
        var artifacts = await artifactLoader.LoadAsync(
            input.Optional("cognitive-root"), cancellationToken).ConfigureAwait(false);
        var manifest = contextAssembler.Build(
            state, Actor(input), OptionalId(input.Optional("work"), value => new WorkItemId(value)),
            artifacts, DateTimeOffset.UtcNow);
        var skills = ContextSkills.From(manifest.Artifacts);
        var body = JsonSerializer.Serialize(manifest, json) + Environment.NewLine;
        var outputPath = input.Optional("output");
        if (outputPath is null)
        {
            await executor.WriteAsync(body).ConfigureAwait(false);
        }
        else
        {
            var fullPath = Path.GetFullPath(outputPath);
            await File.WriteAllTextAsync(fullPath, body, cancellationToken).ConfigureAwait(false);
            await executor.WriteLineAsync(fullPath).ConfigureAwait(false);
        }

        try
        {
            await invocation.Service.ExecuteAsync(Task(input), new RecordContextBuiltCommand(
                Actor(input), Cause(input), Correlation(input), manifest.WorkItemId, skills),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ContextAlreadyBriefedException)
        {
        }
    }
}
