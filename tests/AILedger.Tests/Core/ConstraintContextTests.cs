using System.Text.Json;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Core;

public sealed class ConstraintContextTests
{
    [Fact]
    public void ActiveConstraintsEnterContextAndSupersededOnesDoNot()
    {
        var task = EscalationTests.Prepare(out var lead);
        task.Apply(new AddConstraintCommand(task.OperatorId, null, task.NextCorrelation(),
            new ConstraintId("K1"), "Keep persistence local and inspectable", "dossier v2.1", []));
        task.Apply(new AddConstraintCommand(task.OperatorId, null, task.NextCorrelation(),
            new ConstraintId("K2"), "Ship the full migration roadmap", "early draft", []));
        task.Apply(new SupersedeConstraintCommand(task.OperatorId, null, task.NextCorrelation(), new ConstraintId("K2")));

        var manifest = new ContextAssembler().Build(task.State, lead, null, [], DateTimeOffset.UnixEpoch);

        Assert.Contains(manifest.Artifacts,
            item => item.Kind == ContextArtifactKind.Constraint && item.Id == "K1");
        Assert.DoesNotContain(manifest.Artifacts,
            item => item.Kind == ContextArtifactKind.Constraint && item.Id == "K2");
    }

    // R4 (constraint-artifact-id-collision): the loader used to append a hardcoded
    // "operator-authority" Constraint. Context dedupe is GroupBy((Kind, Id)).First() over a
    // content-ordered list, so a governed constraint sharing that id could be silently replaced.
    [Fact]
    public async Task R4_StateConstraintsReplaceTheHardcodedContextArtifact()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var application = new CliApplication(
            output,
            TextWriter.Null,
            path =>
            {
                var reducer = new TaskReducer();
                return new FileGovernedTaskService(path, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
            },
            _ => throw new InvalidOperationException("No provider adapter is used by this test."),
            new ContextAssembler());
        var common = new[] { "--root", root.Path, "--task", "T1", "--actor", "operator" };
        await application.RunAsync(["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["constraint", "add", .. common, "--id", "operator-authority",
             "--statement", "Governed text supplied by the operator", "--source", "task state"],
            CancellationToken.None);
        output.GetStringBuilder().Clear();

        var exit = await application.RunAsync(
            ["context", "build", .. common, "--cognitive-root", FindCognitiveRoot()], CancellationToken.None);

        using var document = JsonDocument.Parse(output.ToString());
        var constraints = document.RootElement.GetProperty("artifacts").EnumerateArray()
            .Where(artifact => artifact.GetProperty("kind").GetString() == "constraint")
            .ToArray();
        Assert.Equal(0, exit);
        var artifact = Assert.Single(constraints, item => item.GetProperty("id").GetString() == "operator-authority");
        Assert.Contains("Governed text supplied by the operator",
            artifact.GetProperty("content").GetString(), StringComparison.Ordinal);
    }

    private static string FindCognitiveRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "cognitive");
            if (File.Exists(Path.Combine(candidate, "manifest.json")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Test could not locate the cognitive root.");
    }
}
