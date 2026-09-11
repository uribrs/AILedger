using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;
using System.Text.Json;

namespace AILedger.Tests.Cli;

// TaskDebtTests pins the counts; these pin what `status` actually prints. The two are not the same
// check: a projection nothing writes into the output is a projection no operator sees, and no kernel
// test can tell the difference. The service is built without a lesson store, so recall reaches
// nothing and the clean task really is clean.
public sealed class TaskDebtCliTests
{
    // The whole point of writing the node conditionally: a task that owes nothing prints exactly what
    // it printed before this existed.
    [Fact]
    public async Task StatusOmitsTheOwedBlockWhenTheTaskOwesNothing()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var application = Create(output, error);
        var common = Common(root.Path);
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        output.GetStringBuilder().Clear();

        var exit = await application.RunAsync(["status", .. common], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.ToString());
        using var document = JsonDocument.Parse(output.ToString());
        Assert.False(document.RootElement.TryGetProperty("owed", out _));
    }

    // Counts, and one fact: whether an archived task still owes a retrospective. A field whose value
    // is fixed by the condition under which the block is written says nothing, and a derived verdict
    // is worse than that: it is a number an agent can move without doing the work. So the property
    // set is asserted exactly, not merely searched.
    [Fact]
    public async Task StatusReportsTheCountsAndNoVerdictWhenTheTaskOwesSomething()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var application = Create(output, error);
        var common = Common(root.Path);
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        await application.RunAsync(
            ["claim", "add", .. common, "--id", "C1", "--statement", "An unearned assumption"],
            CancellationToken.None);
        output.GetStringBuilder().Clear();

        var exit = await application.RunAsync(["status", .. common], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, error.ToString());
        using var document = JsonDocument.Parse(output.ToString());
        var owed = document.RootElement.GetProperty("owed");
        Assert.Equal(
            new[]
            {
                "openClaims",
                "openClaimsWithSupportingEvidence",
                "workItemsAwaitingVerification",
                "lessonsRecalled",
                "lessonsCited",
                "retrospectiveOwed"
            },
            owed.EnumerateObject().Select(property => property.Name).ToArray());
        // Every count is a number and the one fact is a boolean. Whether an archived task carries a
        // retrospective is a yes or a no, and a 0/1 count would read as a measure of something.
        Assert.All(
            owed.EnumerateObject().Where(property => property.Name != "retrospectiveOwed"),
            property => Assert.Equal(JsonValueKind.Number, property.Value.ValueKind));
        Assert.Equal(JsonValueKind.False, owed.GetProperty("retrospectiveOwed").ValueKind);
        Assert.Equal(1, owed.GetProperty("openClaims").GetInt32());
        Assert.Equal(0, owed.GetProperty("openClaimsWithSupportingEvidence").GetInt32());
    }

    private static string[] Common(string root) =>
        ["--root", root, "--task", "T1", "--actor", "operator"];

    private static CliApplication Create(TextWriter output, TextWriter error) => new(
        output,
        error,
        Service,
        _ => throw new InvalidOperationException("These tests launch no provider."),
        new ContextAssembler());

    private static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }
}
