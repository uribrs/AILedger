using System.Text.Json;
using AILedger.Cli;
using AILedger.Cli.Dispatch;
using AILedger.Cli.Orchestration;
using AILedger.Core.Contracts;
using AILedger.Storage;
using AILedger.Tests.Findings;
using AILedger.Tests.Support;

namespace AILedger.Tests.Orchestration;

public sealed class ProviderStartupIntegrationTests
{
    [Theory]
    [InlineData("claude", false)] [InlineData("codex", false)]
    [InlineData("claude", true)] [InlineData("codex", true)]
    public async Task BuiltCandidateRetainsStartupFailureAndRestartDoesNotLaunchFindings(string provider, bool missing)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var ledger = new FindingsFixture(); using var work = new TemporaryDirectory(); using var config = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(work.Path, ".git"));
        var home = Directory.CreateDirectory(Path.Combine(config.Path, "provider-home")).FullName;
        await File.WriteAllTextAsync(Path.Combine(home, "auth.json"), "{}");
        var environment = new Dictionary<string, string> { ["CODEX_HOME"] = home };
        await TrustedDriverIntake.CreateAsync(ledger.Service(), ledger.TaskId, ledger.Actor, "intake", "Startup fixture",
            "Inspect this controlled fixture. " + new string('x', 1000), [], ["fixture"], default);
        var profiles = Path.Combine(config.Path, "profiles.json");
        await File.WriteAllTextAsync(profiles, JsonSerializer.Serialize(new { schema_version = 1,
            agents = new[] { new { work = "discovery", subject = "lead", provider }, new { work = "findings", subject = "lead", provider } },
            preparation = new { roles = new[] { new { actor = "lead", role = "PlanningLead", capabilities = RoleDefaults.For(RoleKind.PlanningLead).Select(c => c.ToString()).ToArray() } }, work = Array.Empty<object>() } }));
        var executable = Path.Combine(config.Path, "provider");
        var marker = Path.Combine(work.Path, "launches");
        if (!missing)
        {
            await File.WriteAllTextAsync(executable, FakeProvider(marker));
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        string[] args = ["orchestrate", "run", "--root", ledger.Root, "--lesson-root", Path.Combine(ledger.Root, "lessons"),
            "--task", ledger.TaskId.Value, "--actor", "operator", "--profiles", profiles, "--working-directory", work.Path,
            "--cognitive-root", ContextBrief.CognitiveRoot(), "--executable", executable];
        var first = await AcceptanceCompletionTests.RunCliAsync(args, null, environment);
        Assert.True(first.Exit == 4, first.Output + first.Error);
        using var output = JsonDocument.Parse(first.Output);
        Assert.Contains(missing ? "not found" : "controlled startup refusal", output.RootElement.GetProperty("diagnostic").GetString());
        var state = await ledger.StateAsync();
        Assert.Equal(TaskStage.Discovery, state.Stage);
        Assert.Empty(state.WorkItems);
        Assert.Empty(state.Artifacts.Values.Where(a => a.Kind == GovernedArtifactKind.OrchestrationPlan));
        if (missing) Assert.Empty(state.Runs);
        else
        {
            var run = Assert.Single(state.Runs.Values);
            Assert.Equal(AgentRunStatus.Failed, run.Status);
            var retained = JsonSerializer.Deserialize<AgentRunResult>(await File.ReadAllTextAsync(Path.Combine(ledger.Directory, "runs", run.Id.Value + ".json")), LedgerJson.CreateOptions())!;
            Assert.Equal(37, retained.ExitCode);
            Assert.Contains("controlled startup refusal", retained.StandardError);
            Assert.Equal(ProviderProcessFailureKind.InputWrite, retained.ProcessFailure!.Kind);
            Assert.True(retained.ProcessFailure.CleanupConfirmed);
            Assert.Null(run.OutputTokens);
        }
        var again = await AcceptanceCompletionTests.RunCliAsync(args, null, environment);
        Assert.True(again.Exit == 4, again.Output + again.Error);
        Assert.Equal(state.Version, (await ledger.StateAsync()).Version);
        if (!missing) Assert.Single(await File.ReadAllLinesAsync(marker));
        if (!missing)
        {
            await ledger.ExecuteAsync(new AddEvidenceCommand(ledger.Actor, null, "diagnosis", new("diagnostic"), "source-read",
                "fixture:startup", "Additional diagnosis changes the task basis but does not demonstrate termination or repair infrastructure", [], []));
            var changed = await AcceptanceCompletionTests.RunCliAsync(args, null, environment);
            Assert.True(changed.Exit == 4, changed.Output + changed.Error);
            Assert.Single(await File.ReadAllLinesAsync(marker));
            Assert.Single((await ledger.StateAsync()).Runs);
        }
        var snapshot = JsonSerializer.Deserialize<CoordinationSnapshot>(await File.ReadAllTextAsync(Path.Combine(ledger.Directory, "coordination-v1.json")), LedgerJson.CreateOptions())!;
        var journal = JsonSerializer.Deserialize<DriverJournal>(snapshot.Payload!, LedgerJson.CreateOptions())!;
        var intent = Assert.Single(journal.Intents);
        Assert.Equal(CognitiveWorkKind.Discovery, intent.Request.CognitiveWork);
        Assert.Equal(missing ? DispatchFailureKind.PreparationUnavailable : DispatchFailureKind.ProviderTransport, intent.Receipt!.Failure!.Kind);
        Assert.Empty(intent.Receipt.CognitiveReceipts!);
        var before = await File.ReadAllTextAsync(Path.Combine(ledger.Directory, "coordination-v1.json"));
        var measured = await AcceptanceCompletionTests.RunCliAsync(["retrospective", "build", "--root", ledger.Root,
            "--task", ledger.TaskId.Value, "--orchestration"], null, environment);
        Assert.True(measured.Exit == 0, measured.Output + measured.Error);
        using var report = JsonDocument.Parse(measured.Output);
        var observations = report.RootElement.GetProperty("orchestration");
        Assert.Equal("observed", observations.GetProperty("journalStatus").GetString());
        Assert.Single(observations.GetProperty("observations").EnumerateArray().Where(r => r.GetProperty("kind").GetString() == "dispatch"));
        Assert.NotEmpty(observations.GetProperty("unknownMeasurements").EnumerateArray());
        Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(ledger.Directory, "coordination-v1.json")));
    }

    private static string FakeProvider(string marker) => "#!/usr/bin/python3\n" + $$$"""
        import os, sys, json
        if '--version' in sys.argv:
            print('fixture-1'); sys.exit(0)
        if '--help' in sys.argv:
            print('--print --output-format --session-id --resume --permission-prompts --settings --strict-mcp-config --disable-slash-commands --strict-config --sandbox --cd --add-dir --output-schema --json SESSION_ID'); sys.exit(0)
        if 'app-server' in sys.argv:
            input(); print(json.dumps({'id':1,'result':{}}), flush=True)
            input(); input()
            source = os.path.join(os.path.realpath(os.environ['CODEX_HOME']), 'hooks.json')
            definition = json.load(open(source))['hooks']['PreToolUse'][0]
            hook = {'handlerType':'command','eventName':'preToolUse','matcher':definition['matcher'],
                'command':definition['hooks'][0]['command'],'source':'user','async':False,'enabled':True,'isManaged':False,
                'sourcePath':source,'key':source+':pre_tool_use:0:0','currentHash':'sha256:'+'a'*64}
            print(json.dumps({'id':2,'result':{'data':[{'cwd':os.path.realpath(os.getcwd()),'errors':[],'hooks':[hook]}]}}), flush=True)
            sys.stdin.read(); sys.exit(0)
        with open({{{JsonSerializer.Serialize(marker)}}}, 'a') as stream: stream.write('launch\n')
        print('controlled startup refusal', file=sys.stderr)
        sys.exit(37)
        """ + "\n";
}
