using System.Text.Json;
using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Cli;

// Assigned and engaged are two different questions, and the stage arms ask both: Ready is refused on
// assignment, while Verification, Review and Learn are refused on engagement. `who` reports both, so
// an operator sees the staffing a transition will require before the refusal is the first place the
// missing role appears. These are the four behaviours claim WC5 says a probe proved and a test has to.
public sealed class WhoRoleCoverageTests
{
    private static readonly JsonSerializerOptions Json = LedgerJson.CreateOptions();

    [Fact]
    public async Task WhoReportsARoleAsEngagedOnlyByARunThatCompleted()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        string[] common = ["--root", root.Path, "--task", "T1"];
        await Ok(error, ["task", "open", .. common, "--actor", "operator", "--title", "Task", "--goal", "Goal"]);
        await Ok(error, ["work", "add", .. common, "--actor", "operator", "--id", "W1",
            "--title", "Work", "--owner", "operator"]);
        await Ok(error, ["actor", "attach", .. common, "--actor", "operator", "--target", "scout",
            "--role", "researcher"]);
        await Ok(error, ["actor", "attach", .. common, "--actor", "operator", "--target", "lead",
            "--role", "implementation-lead"]);
        await Ok(error, ["actor", "attach", .. common, "--actor", "operator", "--target", "hand",
            "--role", "worker"]);

        // A completed run is engagement, and it is reported with the run that proves it.
        await Ok(error, ["run", "start", .. common, "--actor", "operator", "--subject", "scout",
            "--run", "R-done", "--provider", "codex", "--session", "s-done"]);
        await Ok(error, ["run", "complete", .. common, "--actor", "operator", "--run", "R-done",
            "--status", "completed", "--session", "s-done"]);
        // A run that died read nothing, so it engages nothing.
        await Ok(error, ["run", "start", .. common, "--actor", "operator", "--subject", "lead",
            "--run", "R-failed", "--provider", "codex"]);
        await Ok(error, ["run", "complete", .. common, "--actor", "operator", "--run", "R-failed",
            "--status", "failed"]);
        // Neither does a run that is still going: the pass has not finished.
        await Ok(error, ["run", "start", .. common, "--actor", "operator", "--subject", "hand",
            "--run", "R-active", "--work", "W1", "--provider", "codex", "--session", "s-active"]);

        var coverage = await Coverage(error, common);

        Assert.Equal(string.Empty, error.ToString());
        // Ordered by role, so the rows read the way the pipeline runs rather than by actor id.
        Assert.Equal(
            ["Operator", "ImplementationLead", "Researcher", "Worker"],
            coverage.Select(row => row.Role));

        var researcher = Assert.Single(coverage, row => row.Role == "Researcher");
        Assert.Equal(["scout"], researcher.Assigned);
        Assert.True(researcher.Engaged);
        var engagement = Assert.Single(researcher.EngagedBy);
        Assert.Equal("scout", engagement.Actor);
        Assert.Equal(["R-done"], engagement.Runs);

        var lead = Assert.Single(coverage, row => row.Role == "ImplementationLead");
        Assert.Equal(["lead"], lead.Assigned);
        Assert.False(lead.Engaged);
        Assert.Empty(lead.EngagedBy);

        var worker = Assert.Single(coverage, row => row.Role == "Worker");
        Assert.Equal(["hand"], worker.Assigned);
        Assert.False(worker.Engaged);

        // The operator is staffed and has run nothing, which is the ordinary shape of a task that
        // dispatches rather than works.
        var @operator = Assert.Single(coverage, row => row.Role == "Operator");
        Assert.Equal(["operator"], @operator.Assigned);
        Assert.False(@operator.Engaged);
    }

    // The case a later refactor would silently drop. Engagement is read from the run's own captured
    // subject role, so reassigning the actor afterwards cannot take the pass away — the row survives
    // with nobody holding the role.
    [Fact]
    public async Task ARoleStaysEngagedAfterItsActorIsReassigned()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        string[] common = ["--root", root.Path, "--task", "T1"];
        await Ok(error, ["task", "open", .. common, "--actor", "operator", "--title", "Task", "--goal", "Goal"]);
        await Ok(error, ["actor", "attach", .. common, "--actor", "operator", "--target", "scout",
            "--role", "researcher"]);
        await Ok(error, ["run", "start", .. common, "--actor", "operator", "--subject", "scout",
            "--run", "R-done", "--provider", "codex", "--session", "s-done"]);
        await Ok(error, ["run", "complete", .. common, "--actor", "operator", "--run", "R-done",
            "--status", "completed", "--session", "s-done"]);

        await Ok(error, ["actor", "attach", .. common, "--actor", "operator", "--target", "scout",
            "--role", "worker"]);
        var coverage = await Coverage(error, common);

        Assert.Equal(string.Empty, error.ToString());
        var researcher = Assert.Single(coverage, row => row.Role == "Researcher");
        Assert.Empty(researcher.Assigned);
        Assert.True(researcher.Engaged);
        Assert.Equal("scout", Assert.Single(researcher.EngagedBy).Actor);

        // And the role it now holds is assigned without being engaged: the run it completed was
        // a researcher's.
        var worker = Assert.Single(coverage, row => row.Role == "Worker");
        Assert.Equal(["scout"], worker.Assigned);
        Assert.False(worker.Engaged);
    }

    private sealed record Row(string Role, string[] Assigned, bool Engaged, Engagement[] EngagedBy);

    private sealed record Engagement(string Actor, string[] Runs);

    private static async Task<Row[]> Coverage(StringWriter error, string[] common)
    {
        var output = new StringWriter();
        var exit = await Create(output, error).RunAsync(["who", .. common], CancellationToken.None);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        return document.RootElement.GetProperty("roleCoverage").Deserialize<Row[]>(Json)!;
    }

    private static async Task Ok(StringWriter error, string[] args)
    {
        var exit = await Create(TextWriter.Null, error).RunAsync(args, CancellationToken.None);

        Assert.Equal(0, exit);
    }

    private static CliApplication Create(TextWriter output, TextWriter error) => new(
        output,
        error,
        Service,
        _ => throw new InvalidOperationException("Provider adapter is not used by this test."),
        new ContextAssembler());

    private static IGovernedTaskService Service(string root)
    {
        var reducer = new TaskReducer();
        return new FileGovernedTaskService(root, new CommandHandler(reducer, new AuthorizationPolicy()), reducer);
    }
}
