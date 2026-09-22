using System.Reflection;
using AILedger.Core.Contracts;
using AILedger.Providers.Adapters;
using Xunit;

namespace GovernedTests.Fixtures;

public sealed class BriefingCases
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScopedRunFindsAndQuotesTheShippedCommand(bool assurance)
    {
        var root = Path.Combine(Path.GetTempPath(), $"governed-brief's-{Guid.NewGuid():N}");
        var working = Path.Combine(root, "src", "Scoped");
        Directory.CreateDirectory(working);
        Directory.CreateDirectory(Path.Combine(root, "scripts"));
        var script = Path.Combine(root, "scripts", "test-governed.sh");
        try
        {
            File.WriteAllText(script, "#!/bin/sh\n");
            var request = Request(working, assurance);
            Assert.DoesNotContain("sh '", Brief(request)); // A helper in another repository is not enough.
            File.WriteAllText(Path.Combine(root, "AILedger.sln"), "");
            Assert.Contains("sh '" + script.Replace("'", "'\"'\"'") + "'", Brief(request));
            Assert.Contains("stop that operation", Brief(request));
            File.Delete(script);
            Assert.DoesNotContain("reusable governed .NET test command", Brief(request));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string Brief(AgentLaunchRequest request)
    {
        var type = typeof(ClaudeAgentAdapter).Assembly.GetType(
            "AILedger.Providers.Adapters.GovernedExecutionBriefing", throwOnError: true)!;
        return (string)type.GetMethod("For", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [request, request.LedgerRoot])!;
    }

    private static AgentLaunchRequest Request(string working, bool assurance) => new(
        new RunId("R1"), new TaskId("T1"), new ActorId("worker"), new WorkItemId("W1"),
        AgentLaunchMode.New, "claude", "/provider", working, Path.Combine(working, "ledger"),
        "ailedger", "{}", null, PermissionProfile.WorkspaceGoverned, null, null, [],
        new Dictionary<string, string>(), TimeSpan.FromMinutes(1),
        Assurance: assurance ? new AssuranceBinding(1, [new WorkItemId("W1")],
            [new AssuranceWorkVersion(new WorkItemId("W1"), new RunId("WR1"))], new string('a', 64)) : null);
}
