using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Tests.Stages;

// StageTransitionPolicy.IsBackward decides whether a transition is a step back through the pipeline,
// and it decides it from the enum's ordinal rather than from a table of its own. That is cheap to
// state and easy to get subtly wrong at the one edge that reads backwards from its name, so the
// whole truth table is pinned here.
//
// The edges are derived from the graph through LegalTargets, never retyped, so an edge added to
// AllowedTransitions arrives here as a failure rather than as silence. The classification is the
// part that is written out: it is what the test exists to assert.
public sealed class StageDirectionTests
{
    private static readonly (TaskStage From, TaskStage To)[] ExpectedBackward =
    [
        (TaskStage.Research, TaskStage.Discovery),
        (TaskStage.Design, TaskStage.Research),
        (TaskStage.Scope, TaskStage.Design),
        (TaskStage.Ready, TaskStage.Scope),
        (TaskStage.Execution, TaskStage.Research),
        (TaskStage.Execution, TaskStage.Design),
        (TaskStage.Execution, TaskStage.Scope),
        (TaskStage.Verification, TaskStage.Execution),
        (TaskStage.Repair, TaskStage.Execution),
        // Repair is declared after Verification, so closing a repair cycle is the backward edge and
        // entering repair is the forward one. This is the pair the ordinal rule is most likely to be
        // misread on, which is why both directions are listed rather than assumed.
        (TaskStage.Repair, TaskStage.Verification),
        (TaskStage.Review, TaskStage.Repair),
        (TaskStage.Learn, TaskStage.Review)
    ];

    private static readonly (TaskStage From, TaskStage To)[] ExpectedForward =
    [
        (TaskStage.Discovery, TaskStage.Research),
        (TaskStage.Research, TaskStage.Design),
        (TaskStage.Design, TaskStage.Scope),
        (TaskStage.Scope, TaskStage.Ready),
        (TaskStage.Ready, TaskStage.Execution),
        (TaskStage.Execution, TaskStage.Verification),
        (TaskStage.Verification, TaskStage.Repair),
        (TaskStage.Verification, TaskStage.Review),
        (TaskStage.Review, TaskStage.Learn),
        (TaskStage.Learn, TaskStage.Archive)
    ];

    [Fact]
    public void EveryLegalEdgeIsClassifiedExactlyAsTheTruthTableSaysItIs()
    {
        var edges = LegalEdges();

        Assert.Equal(22, edges.Length);

        var backward = edges.Where(edge => StageTransitionPolicy.IsBackward(edge.From, edge.To));
        var forward = edges.Where(edge => !StageTransitionPolicy.IsBackward(edge.From, edge.To));

        Assert.Equal(Describe(ExpectedBackward), Describe(backward));
        Assert.Equal(Describe(ExpectedForward), Describe(forward));
    }

    // No legal edge stays in place, so no legal edge is neither forward nor backward. EnsureAllowed
    // refuses current == target separately; this pins that the direction rule agrees with it.
    [Fact]
    public void NoLegalEdgeIsASelfTransition()
    {
        Assert.Empty(LegalEdges().Where(edge => edge.From == edge.To));
    }

    private static (TaskStage From, TaskStage To)[] LegalEdges() =>
        Enum.GetValues<TaskStage>()
            .SelectMany(
                stage => StageTransitionPolicy.LegalTargets(stage),
                (stage, target) => (From: stage, To: target))
            .ToArray();

    private static string[] Describe(IEnumerable<(TaskStage From, TaskStage To)> edges) =>
        edges.Select(edge => $"{edge.From}->{edge.To}").OrderBy(text => text, StringComparer.Ordinal).ToArray();
}
