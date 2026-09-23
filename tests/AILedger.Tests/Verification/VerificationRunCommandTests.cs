using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using AILedger.Cli.Verification;
using AILedger.Core.Contracts;
using AILedger.Tests.Support;
using static AILedger.Tests.Cli.CliApplicationTestSupport;

namespace AILedger.Tests.Verification;

public sealed class VerificationRunCommandTests
{
    // R1 (child-cannot-run-verification). --actor is asserted, not authenticated (PC22), so a governed
    // child can name any actor. Each case below breaks exactly the rule it names and, where it can,
    // every later rule as well, so the message proves the S4 order and not only that something
    // refused. The spawner fails the test if it is ever called: an exit code cannot tell a refusal
    // before the process from a refusal after one.
    [Theory]
    [InlineData("worker-actor")]
    [InlineData("unknown-actor")]
    [InlineData("not-git")]
    [InlineData("ancestor-only-profile")]
    [InlineData("invalid-profile")]
    [InlineData("ambiguous-profile")]
    [InlineData("confirmation-mismatch")]
    [InlineData("evidence-exists")]
    [InlineData("unknown-claim")]
    [InlineData("socket-missing")]
    public async Task R1_RefusesBeforeAnyProcess(string rule)
    {
        var spawner = ScriptedSpawner.NeverCalled();
        // Every case but the last-but-one has no socket file, so refusal 6 would also fire if the
        // order were wrong.
        using var fixture = new VerificationFixture(spawner, createSocketFile: false, git: rule != "not-git");
        await fixture.OpenAsync();
        await fixture.Application.RunAsync(
            ["actor", "attach", .. fixture.Common(), "--target", "claude-work", "--role", "worker"],
            CancellationToken.None);
        await fixture.Application.RunAsync(
            ["evidence", "add", .. fixture.Common(), "--id", "PV-taken", "--source-type", "test-run",
             "--citation", "c", "--summary", "s"], CancellationToken.None);

        switch (rule)
        {
            case "invalid-profile":
                fixture.WriteProfile("""
                    { "schemaVersion": 1, "profiles": { "redis-integration": { "command": "x", "image": "redis" } } }
                    """);
                break;
            case "ambiguous-profile":
                fixture.WriteProfile("""
                    { "schemaVersion": 1, "profiles": { "one": { "command": "a" }, "two": { "command": "b" } } }
                    """);
                break;
            case "ancestor-only-profile":
                // m1 (PC42): a valid profile above the repository root, none at the root.
                fixture.WriteRedisProfile(directory: Path.GetDirectoryName(fixture.Checkout));
                break;
            default:
                fixture.WriteRedisProfile();
                break;
        }

        var right = VerificationCliCommands.Confirmation(VerificationFixture.RedisCommand);
        (string Actor, string Confirm, string Id, string[] Extra, string Expected) refusal = rule switch
        {
            "worker-actor" => ("claude-work", new string('0', 64), "PV-taken", [],
                "actor 'claude-work' is not an operator"),
            "unknown-actor" => ("stranger", new string('0', 64), "PV-taken", [],
                "actor 'stranger' is not an operator"),
            "not-git" => ("operator", new string('0', 64), "PV-taken", [], "is not a git work tree"),
            "ancestor-only-profile" => ("operator", new string('0', 64), "PV-taken", [],
                "no ailedger.verification.json at the repository root"),
            "invalid-profile" => ("operator", new string('0', 64), "PV-taken", [],
                "field 'profiles.redis-integration.image' is not a known property"),
            "ambiguous-profile" => ("operator", new string('0', 64), "PV-taken", [],
                "declares 2 profiles; name one with --profile"),
            "confirmation-mismatch" => ("operator", new string('0', 64), "PV-taken", [],
                "the confirmation does not match"),
            "evidence-exists" => ("operator", right, "PV-taken", [], "evidence 'PV-taken' already exists"),
            "unknown-claim" => ("operator", right, "PV1", ["--supports", "NOPE"], "claim 'NOPE' does not exist"),
            "socket-missing" => ("operator", right, "PV1", [],
                "Colima is not started, or DOCKER_HOST names another path"),
            _ => throw new ArgumentOutOfRangeException(nameof(rule))
        };

        var exit = await fixture.RunAsync(
            refusal.Id, refusal.Confirm, refusal.Actor, CancellationToken.None, refusal.Extra);

        Assert.Equal(1, exit);
        Assert.Contains(refusal.Expected, fixture.Error.ToString(), StringComparison.Ordinal);
        Assert.Empty(spawner.Starts);
        Assert.False(Directory.Exists(Path.Combine(fixture.TaskPath, "verification")));
        Assert.Empty(fixture.Engine.Requests);
        var state = await Service(fixture.Root).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Equal(new[] { new EvidenceId("PV-taken") }, state!.Evidence.Keys);
    }

    // R1, the half a sandboxed child actually meets: the socket file exists but this process may not
    // connect to it (PC11). The ping is the only engine call; nothing is started and nothing is
    // cleaned, because there is no process whose containers could exist.
    [Fact]
    public async Task R1_AnUnreachableSocketRefusesWithTheSandboxCause()
    {
        var spawner = ScriptedSpawner.NeverCalled();
        using var fixture = new VerificationFixture(spawner);
        fixture.Engine.Override = _ => throw new HttpRequestException(
            "denied", new SocketException((int)SocketError.AccessDenied));
        await fixture.OpenAsync();
        fixture.WriteRedisProfile();

        var exit = await fixture.RunAsync(
            "PV1", VerificationCliCommands.Confirmation(VerificationFixture.RedisCommand));

        Assert.Equal(1, exit);
        Assert.Contains("it is probably sandboxed; run from a host shell", fixture.Error.ToString(), StringComparison.Ordinal);
        Assert.Empty(spawner.Starts);
        var only = Assert.Single(fixture.Engine.Requests);
        Assert.Equal((HttpMethod.Get, "/_ping"), only);
        Assert.False(Directory.Exists(Path.Combine(fixture.TaskPath, "verification")));
        var state = await Service(fixture.Root).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Empty(state!.Evidence);
    }

    // S4 without --confirm: the listing the operator reads. No authority is needed, nothing runs and
    // nothing is created, and the confirmation is the digest of the exact UTF-8 command text.
    [Fact]
    public async Task WithoutConfirmItPrintsThePlanAndRunsNothing()
    {
        var spawner = ScriptedSpawner.NeverCalled();
        using var fixture = new VerificationFixture(spawner, createSocketFile: false);
        await fixture.OpenAsync();
        fixture.WriteRedisProfile();

        // task open wrote its own JSON first; the plan is what follows it.
        var before = fixture.Output.ToString().Length;

        var exit = await fixture.RunAsync("PV1", confirm: null, actor: "stranger");

        Assert.Equal(0, exit);
        using var plan = JsonDocument.Parse(fixture.Output.ToString()[before..]);
        var root = plan.RootElement;
        var profilePath = Path.Combine(fixture.Checkout, "ailedger.verification.json");
        Assert.Equal(profilePath, root.GetProperty("profilePath").GetString());
        Assert.Equal(VerificationFixture.Sha256(File.ReadAllBytes(profilePath)), root.GetProperty("profileSha256").GetString());
        Assert.Equal("redis-integration", root.GetProperty("profileName").GetString());
        // Readable, not HTML-escaped: the operator checks this text by eye before confirming it.
        Assert.Equal(VerificationFixture.RedisCommand, root.GetProperty("command").GetString());
        Assert.Equal(VerificationFixture.Sha256(VerificationFixture.RedisCommand), root.GetProperty("confirmation").GetString());
        Assert.Equal(
            new[] { "AILEDGER_VERIFICATION_OUTPUT", "AILEDGER_VERIFICATION_RUN", "DOCKER_HOST", "TESTCONTAINERS_RYUK_DISABLED" },
            root.GetProperty("environment").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Empty(spawner.Starts);
        Assert.Empty(fixture.Engine.Requests);
        Assert.False(Directory.Exists(Path.Combine(fixture.TaskPath, "verification")));
    }

    // S5 and S6 (SC5): every result field, the digest in the citation recomputed from the file bytes,
    // and the candidate and git provenance matching the checkout that ran.
    [Fact]
    public async Task APassedRunRetainsEveryResultFieldAndCitesTheFileDigest()
    {
        var spawner = new ScriptedSpawner((start, _) =>
        {
            var output = start.Environment["AILEDGER_VERIFICATION_OUTPUT"];
            File.WriteAllText(Path.Combine(output, "redis-integration.trx"), "<TestRun/>");
            File.WriteAllText(Path.Combine(output, "notes.txt"), "not a result file");
            File.WriteAllText(start.StandardOutputPath, "Passed!");
            File.WriteAllText(start.StandardErrorPath, string.Empty);
            return Task.FromResult<int?>(0);
        });
        using var fixture = new VerificationFixture(spawner);
        fixture.Engine.Owned.AddRange(["owned-1", "owned-2"]);
        await fixture.OpenAsync();
        await fixture.Application.RunAsync(
            ["claim", "add", .. fixture.Common(), "--id", "PC15", "--statement", "The pilot passes host-side"],
            CancellationToken.None);
        fixture.WriteRedisProfile();
        File.WriteAllText(Path.Combine(fixture.Checkout, "untracked.txt"), "dirty");
        var head = System.Text.Encoding.UTF8.GetString(GitCheckout.Git(fixture.Checkout, "rev-parse", "HEAD")).Trim();
        var root = fixture.RepositoryRoot;
        var worktree = GitCheckout.ExpectedWorktreeDigest(root);

        var exit = await fixture.RunAsync(
            "PV1", VerificationCliCommands.Confirmation(VerificationFixture.RedisCommand),
            extra: ["--supports", "PC15"]);

        Assert.Equal(0, exit);
        var start = Assert.Single(spawner.Starts);
        Assert.Equal(VerificationFixture.RedisCommand, start.Command);
        // m1: the process runs in the repository root git reports, links resolved.
        Assert.Equal(root, start.WorkingDirectory);

        var resultPath = Path.Combine(fixture.TaskPath, "verification", "PV1", "result.json");
        var bytes = File.ReadAllBytes(resultPath);
        using var document = JsonDocument.Parse(bytes);
        var result = document.RootElement;
        // S5 schema version 2: gitStatusSha256 is gone; the measured worktree digests replace it (M1).
        string[] fields =
        [
            "schemaVersion", "taskId", "evidenceId", "verificationRun", "assertedCandidateId", "checkout",
            "repositoryRoot", "gitHead", "worktreeSha256Before", "worktreeSha256After", "profilePath",
            "profileSha256", "profileName", "command", "environment", "startedAt", "endedAt", "exitCode",
            "status", "stdoutPath", "stderrPath", "resultFiles", "cleanup", "kernelVersion"
        ];
        Assert.Equal(fields, result.EnumerateObject().Select(property => property.Name));
        Assert.Equal(2, result.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("T1", result.GetProperty("taskId").GetString());
        Assert.Equal("PV1", result.GetProperty("evidenceId").GetString());
        Assert.Equal("T1.PV1", result.GetProperty("verificationRun").GetString());
        // Asserted, as given: the kernel checks its format only.
        Assert.Equal(VerificationFixture.Candidate, result.GetProperty("assertedCandidateId").GetString());
        Assert.Equal(fixture.Checkout, result.GetProperty("checkout").GetString());
        Assert.Equal(root, result.GetProperty("repositoryRoot").GetString());
        Assert.Equal(head, result.GetProperty("gitHead").GetString());
        // S12, recomputed here from the contract text, over the dirty tree that includes the untracked
        // file and the untracked profile.
        Assert.Equal(worktree, result.GetProperty("worktreeSha256Before").GetString());
        Assert.Equal(worktree, result.GetProperty("worktreeSha256After").GetString());
        var profilePath = Path.Combine(root, "ailedger.verification.json");
        Assert.Equal(profilePath, result.GetProperty("profilePath").GetString());
        Assert.Equal(VerificationFixture.Sha256(File.ReadAllBytes(profilePath)), result.GetProperty("profileSha256").GetString());
        Assert.Equal("redis-integration", result.GetProperty("profileName").GetString());
        Assert.Equal(VerificationFixture.RedisCommand, result.GetProperty("command").GetString());

        // S2: exactly the four variables, keys unchanged by any naming policy, and the same values the
        // process was given.
        var environment = result.GetProperty("environment").EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString());
        Assert.Equal(4, environment.Count);
        Assert.Equal($"unix://{fixture.SocketPath}", environment["DOCKER_HOST"]);
        Assert.Equal("true", environment["TESTCONTAINERS_RYUK_DISABLED"]);
        Assert.Equal("T1.PV1", environment["AILEDGER_VERIFICATION_RUN"]);
        Assert.Equal(Path.Combine(fixture.TaskPath, "verification", "PV1", "results"), environment["AILEDGER_VERIFICATION_OUTPUT"]);
        Assert.Equal(environment, start.Environment.ToDictionary(pair => pair.Key, pair => (string?)pair.Value));
        Assert.DoesNotContain("TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE", start.Environment.Keys);

        Assert.Equal("2026-09-23T12:00:00+00:00", result.GetProperty("startedAt").GetString());
        Assert.Equal("2026-09-23T12:00:01+00:00", result.GetProperty("endedAt").GetString());
        Assert.Equal(0, result.GetProperty("exitCode").GetInt32());
        Assert.Equal("passed", result.GetProperty("status").GetString());
        Assert.Equal("verification/PV1/stdout.log", result.GetProperty("stdoutPath").GetString());
        Assert.Equal("verification/PV1/stderr.log", result.GetProperty("stderrPath").GetString());

        var resultFile = Assert.Single(result.GetProperty("resultFiles").EnumerateArray());
        var trx = Path.Combine(fixture.TaskPath, "verification", "PV1", "results", "redis-integration.trx");
        Assert.Equal("verification/PV1/results/redis-integration.trx", resultFile.GetProperty("path").GetString());
        Assert.Equal(VerificationFixture.Sha256(File.ReadAllBytes(trx)), resultFile.GetProperty("sha256").GetString());
        Assert.Equal(new FileInfo(trx).Length, resultFile.GetProperty("bytes").GetInt64());

        var cleanup = result.GetProperty("cleanup");
        Assert.Equal("ailedger.run=T1.PV1", cleanup.GetProperty("label").GetString());
        Assert.Equal(new[] { "owned-1", "owned-2" }, cleanup.GetProperty("removed").EnumerateArray().Select(id => id.GetString()));
        Assert.Empty(cleanup.GetProperty("failed").EnumerateArray());
        Assert.Equal(JsonValueKind.Object, result.GetProperty("kernelVersion").ValueKind);

        // S6: one ordinary evidence entry, as the invoker, citing the exact bytes on disk.
        var state = await Service(fixture.Root).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        var evidence = state!.Evidence[new EvidenceId("PV1")];
        Assert.Equal("container-verification", evidence.SourceType);
        Assert.Equal($"verification/PV1/result.json sha256:{VerificationFixture.Sha256(bytes)}", evidence.Citation);
        Assert.Equal(
            $"passed; exit 0; profile redis-integration; asserted candidate {VerificationFixture.Candidate}; " +
            $"git {head}; worktree {worktree[..12]} unchanged; 1 result files; removed 2 containers",
            evidence.Summary);
        Assert.Equal(new[] { new ClaimId("PC15") }, evidence.Supports);
        Assert.Empty(evidence.Refutes);
        Assert.Equal(new ActorId("operator"), evidence.Provenance.ActorId);

        // S11: the empty reservation marker stays beside the result.
        var marker = new FileInfo(Path.Combine(fixture.TaskPath, "verification", "PV1", ".reservation"));
        Assert.True(marker.Exists);
        Assert.Equal(0, marker.Length);
    }

    // R6 (result-binds-measured-bytes), M1 (PC40). Two runs on one HEAD, the same tracked file modified
    // with different content of the same length. The v2 status hash listed only " M path", so it was
    // equal for both; the measured worktree digest must differ.
    [Fact]
    public async Task R6_ContentChangeOnSameHeadChangesWorktreeDigest()
    {
        var spawner = ScriptedSpawner.Exits(0);
        using var fixture = new VerificationFixture(spawner, createSocketFile: false);
        GitCheckout.CommitFile(fixture.Checkout, "tracked.txt", "base");
        await fixture.OpenAsync();
        fixture.WriteRedisProfile(docker: false, command: "true");
        var confirm = VerificationCliCommands.Confirmation("true");

        File.WriteAllText(Path.Combine(fixture.Checkout, "tracked.txt"), "one!");
        var statusOne = GitCheckout.Git(fixture.Checkout, "status", "--porcelain=v1", "-z", "--untracked-files=all");
        Assert.Equal(0, await fixture.RunAsync("PV1", confirm));
        File.WriteAllText(Path.Combine(fixture.Checkout, "tracked.txt"), "two!");
        var statusTwo = GitCheckout.Git(fixture.Checkout, "status", "--porcelain=v1", "-z", "--untracked-files=all");
        Assert.Equal(0, await fixture.RunAsync("PV2", confirm));

        Assert.Equal(statusOne, statusTwo);
        using var one = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture.TaskPath, "verification", "PV1", "result.json")));
        using var two = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(fixture.TaskPath, "verification", "PV2", "result.json")));
        Assert.Equal(one.RootElement.GetProperty("gitHead").GetString(), two.RootElement.GetProperty("gitHead").GetString());
        Assert.NotEqual(
            one.RootElement.GetProperty("worktreeSha256Before").GetString(),
            two.RootElement.GetProperty("worktreeSha256Before").GetString());
    }

    // R6, M1: a process that edits a tracked file during the run leaves a trace. The status is still the
    // exit code's; the after digest differs and the summary says changed.
    [Fact]
    public async Task R6_EditDuringRunShowsInAfterDigest()
    {
        using var fixture = new VerificationFixture(new ScriptedSpawner((start, _) =>
        {
            File.WriteAllText(Path.Combine(start.WorkingDirectory, "tracked.txt"), "edited by the run");
            return Task.FromResult<int?>(0);
        }), createSocketFile: false);
        GitCheckout.CommitFile(fixture.Checkout, "tracked.txt", "base");
        await fixture.OpenAsync();
        fixture.WriteRedisProfile(docker: false, command: "true");
        var before = GitCheckout.ExpectedWorktreeDigest(fixture.RepositoryRoot);

        Assert.Equal(0, await fixture.RunAsync("PV1", VerificationCliCommands.Confirmation("true")));

        var after = GitCheckout.ExpectedWorktreeDigest(fixture.RepositoryRoot);
        Assert.NotEqual(before, after);
        using var document = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(fixture.TaskPath, "verification", "PV1", "result.json")));
        var result = document.RootElement;
        Assert.Equal("passed", result.GetProperty("status").GetString());
        Assert.Equal(before, result.GetProperty("worktreeSha256Before").GetString());
        Assert.Equal(after, result.GetProperty("worktreeSha256After").GetString());
        var state = await Service(fixture.Root).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.Contains(
            $"; worktree {before[..12]} changed;", state!.Evidence[new EvidenceId("PV1")].Summary, StringComparison.Ordinal);
    }

    // R6, M2 (PC41): a timeout CancelAfter cannot hold is a usage error, in both modes, before any file
    // or directory exists. On v2 the confirmed case threw after the result directory was created and
    // burned the id. The bounds themselves are accepted.
    [Theory]
    [InlineData("86401", true)]
    [InlineData("99999999", true)]
    [InlineData("0", true)]
    [InlineData("86401", false)]
    [InlineData("99999999", false)]
    public async Task R6_OverlongTimeoutRefusedBeforeAnyDirectory(string timeout, bool confirmed)
    {
        var spawner = ScriptedSpawner.NeverCalled();
        using var fixture = new VerificationFixture(spawner);
        await fixture.OpenAsync();
        fixture.WriteRedisProfile();

        var exit = await fixture.RunAsync(
            "PV1", confirmed ? VerificationCliCommands.Confirmation(VerificationFixture.RedisCommand) : null,
            extra: ["--timeout-seconds", timeout]);

        Assert.Equal(2, exit);
        Assert.Contains("--timeout-seconds must be an integer from 1 to 86400.", fixture.Error.ToString(), StringComparison.Ordinal);
        Assert.Empty(spawner.Starts);
        Assert.Empty(fixture.Engine.Requests);
        Assert.False(Directory.Exists(Path.Combine(fixture.TaskPath, "verification")));
    }

    [Fact]
    public async Task R6_TheLargestTimeoutIsAccepted()
    {
        var spawner = ScriptedSpawner.Exits(0);
        using var fixture = new VerificationFixture(spawner);
        await fixture.OpenAsync();
        fixture.WriteRedisProfile();

        var exit = await fixture.RunAsync(
            "PV1", VerificationCliCommands.Confirmation(VerificationFixture.RedisCommand),
            extra: ["--timeout-seconds", "86400"]);

        Assert.Equal(0, exit);
        Assert.Single(spawner.Starts);
    }

    // R2 (owned-cleanup-on-every-exit), m4 (PC45): a spawner that cannot start the process is past the
    // commit point, so the confirmed run is still recorded as failed with evidence, the reason is kept
    // in the error log, and cleanup runs.
    [Fact]
    public async Task R2_SpawnStartFailureCleansAndRecords()
    {
        var spawner = new ScriptedSpawner((_, _) => throw new IOException("the shell could not be started"));
        using var fixture = new VerificationFixture(spawner);
        fixture.Engine.Owned.Add("owned-1");
        await fixture.OpenAsync();
        fixture.WriteRedisProfile();

        var exit = await fixture.RunAsync(
            "PV1", VerificationCliCommands.Confirmation(VerificationFixture.RedisCommand));

        Assert.Equal(3, exit);
        Assert.Single(spawner.Starts);
        var directory = Path.Combine(fixture.TaskPath, "verification", "PV1");
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "result.json")));
        var result = document.RootElement;
        Assert.Equal("failed", result.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("exitCode").ValueKind);
        Assert.Contains(
            "verification run: the shell could not be started",
            File.ReadAllText(Path.Combine(directory, "stderr.log")), StringComparison.Ordinal);
        Assert.Equal(new[] { "owned-1" }, result.GetProperty("cleanup").GetProperty("removed").EnumerateArray().Select(id => id.GetString()));
        Assert.Contains((HttpMethod.Delete, "/containers/owned-1?force=true&v=true"), fixture.Engine.Requests);
        var state = await Service(fixture.Root).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.StartsWith("failed; exit none;", state!.Evidence[new EvidenceId("PV1")].Summary, StringComparison.Ordinal);
    }

    // m1 and A7 (root-rules-agree): in a real git worktree, whose .git is a file, the listing's S1 walk
    // and the confirmed run's git top level name the same profile, and the run executes at that root.
    // A profile in an intermediate directory is never used by either.
    [Fact]
    public async Task R4_ListingAndRunUseTheSameRootProfileInAWorktree()
    {
        var spawner = ScriptedSpawner.Exits(0);
        using var fixture = new VerificationFixture(spawner, createSocketFile: false);
        var worktree = Path.Combine(Path.GetDirectoryName(fixture.Checkout)!, "worktree");
        GitCheckout.Git(fixture.Checkout, "worktree", "add", "-q", "-b", "pilot", worktree);
        Assert.True(File.Exists(Path.Combine(worktree, ".git")));
        var nested = Directory.CreateDirectory(Path.Combine(worktree, "tests", "Platform")).FullName;
        await fixture.OpenAsync();
        fixture.WriteRedisProfile(docker: false, command: "true", directory: worktree);
        // A different, valid profile in an intermediate directory: using it would change the command.
        fixture.WriteRedisProfile(docker: false, command: "false", directory: Path.Combine(worktree, "tests"));
        var root = GitCheckout.TopLevel(nested);
        var before = fixture.Output.ToString().Length;

        string[] common = ["verification", "run", .. fixture.Common(), "--id", "PV1", "--checkout", nested,
            "--candidate", VerificationFixture.Candidate];
        Assert.Equal(0, await fixture.Application.RunAsync(common, CancellationToken.None));
        using var plan = JsonDocument.Parse(fixture.Output.ToString()[before..]);
        Assert.Equal(Path.Combine(worktree, "ailedger.verification.json"), plan.RootElement.GetProperty("profilePath").GetString());
        Assert.Equal("true", plan.RootElement.GetProperty("command").GetString());

        Assert.Equal(0, await fixture.Application.RunAsync(
            [.. common, "--confirm", plan.RootElement.GetProperty("confirmation").GetString()!], CancellationToken.None));

        var start = Assert.Single(spawner.Starts);
        Assert.Equal("true", start.Command);
        Assert.Equal(root, start.WorkingDirectory);
        using var document = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(fixture.TaskPath, "verification", "PV1", "result.json")));
        Assert.Equal(root, document.RootElement.GetProperty("repositoryRoot").GetString());
        Assert.Equal(Path.Combine(root, "ailedger.verification.json"), document.RootElement.GetProperty("profilePath").GetString());
        Assert.Equal(plan.RootElement.GetProperty("profileSha256").GetString(), document.RootElement.GetProperty("profileSha256").GetString());
    }

    // A profile that does not require docker gets two variables, never touches the engine, and records
    // cleanup as an explicit null (S5). A nonzero exit is failed, recorded, and exits 3.
    [Fact]
    public async Task AFailedDockerFreeRunRecordsNullCleanupAndTouchesNoEngine()
    {
        var spawner = ScriptedSpawner.Exits(1);
        using var fixture = new VerificationFixture(spawner, createSocketFile: false);
        await fixture.OpenAsync();
        fixture.WriteRedisProfile(docker: false, command: "exit 1");

        var exit = await fixture.RunAsync("PV1", VerificationCliCommands.Confirmation("exit 1"));

        Assert.Equal(3, exit);
        Assert.Equal(
            new[] { "AILEDGER_VERIFICATION_OUTPUT", "AILEDGER_VERIFICATION_RUN" },
            Assert.Single(spawner.Starts).Environment.Keys.Order(StringComparer.Ordinal));
        Assert.Empty(fixture.Engine.SocketPaths);
        using var document = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(fixture.TaskPath, "verification", "PV1", "result.json")));
        Assert.Equal("failed", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("cleanup").ValueKind);
        var state = await Service(fixture.Root).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.StartsWith("failed; exit 1; profile redis-integration;", state!.Evidence[new EvidenceId("PV1")].Summary, StringComparison.Ordinal);
    }

    // R5 (cancel-timeout-cleanup), timeout half. The process outlives its deadline; the command still
    // cleans the owned containers on its own token and records the result and evidence as timed-out.
    [Fact]
    public async Task R5_TimeoutKillsTreeCleansAndRecords()
    {
        var spawner = ScriptedSpawner.Hangs();
        using var fixture = new VerificationFixture(spawner);
        fixture.Engine.Owned.Add("owned-1");
        await fixture.OpenAsync();
        fixture.WriteRedisProfile();

        var watch = Stopwatch.StartNew();
        var exit = await fixture.RunAsync(
            "PV1", VerificationCliCommands.Confirmation(VerificationFixture.RedisCommand),
            extra: ["--timeout-seconds", "1"]);

        Assert.Equal(3, exit);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"The timeout did not end the run: {watch.Elapsed}.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(fixture.TaskPath, "verification", "PV1", "result.json")));
        var result = document.RootElement;
        Assert.Equal("timed-out", result.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("exitCode").ValueKind);
        Assert.Equal(new[] { "owned-1" },result.GetProperty("cleanup").GetProperty("removed").EnumerateArray().Select(id => id.GetString()));
        Assert.Contains((HttpMethod.Delete, "/containers/owned-1?force=true&v=true"), fixture.Engine.Requests);
        var state = await Service(fixture.Root).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.StartsWith("timed-out; exit none;", state!.Evidence[new EvidenceId("PV1")].Summary, StringComparison.Ordinal);
    }

    // R5, cancellation half: the invoker's token fires mid-run. Cleanup and the evidence entry must not
    // use that token, or the run that most needs its containers removed would lose them and its record.
    [Fact]
    public async Task R5_CancellationCleansAndRecords()
    {
        using var cancellation = new CancellationTokenSource();
        var spawner = new ScriptedSpawner(async (_, token) =>
        {
            cancellation.Cancel();
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
            }

            return null;
        });
        using var fixture = new VerificationFixture(spawner);
        fixture.Engine.Owned.Add("owned-1");
        await fixture.OpenAsync();
        fixture.WriteRedisProfile();

        var exit = await fixture.RunAsync(
            "PV1", VerificationCliCommands.Confirmation(VerificationFixture.RedisCommand),
            cancellationToken: cancellation.Token);

        Assert.Equal(130, exit);
        using var document = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(fixture.TaskPath, "verification", "PV1", "result.json")));
        Assert.Equal("cancelled", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(new[] { "owned-1" },document.RootElement.GetProperty("cleanup").GetProperty("removed").EnumerateArray().Select(id => id.GetString()));
        var state = await Service(fixture.Root).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.StartsWith("cancelled; exit none;", state!.Evidence[new EvidenceId("PV1")].Summary, StringComparison.Ordinal);
    }

    // R5, the production spawner: the kill reaches a grandchild, not only the shell. The shell starts a
    // background sleep and records its pid, then waits; after cancellation that pid must be gone.
    [Fact]
    public async Task R5_ShellSpawnerKillsTheWholeProcessTree()
    {
        using var directory = new TemporaryDirectory();
        var pidFile = Path.Combine(directory.Path, "grandchild.pid");
        using var cancellation = new CancellationTokenSource();
        var run = new ShellVerificationProcessSpawner().RunAsync(
            new VerificationProcessStart(
                $"sleep 120 & echo $! > '{pidFile}'; wait",
                directory.Path,
                new Dictionary<string, string>(),
                Path.Combine(directory.Path, "stdout.log"),
                Path.Combine(directory.Path, "stderr.log")),
            cancellation.Token);
        await WaitUntilAsync(
            () => File.Exists(pidFile) && File.ReadAllText(pidFile).Trim().Length > 0,
            "The shell never started its background child.");
        var grandchild = int.Parse(File.ReadAllText(pidFile).Trim(), System.Globalization.CultureInfo.InvariantCulture);

        cancellation.Cancel();
        var exitCode = await run.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Null(exitCode);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (IsAlive(grandchild) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.False(IsAlive(grandchild), $"Grandchild {grandchild} survived the kill.");
    }

    [Fact]
    public void WithNoDockerHostTheColimaDefaultUnderHomeIsUsed()
    {
        var host = new VerificationHost
        {
            GetEnvironmentVariable = name => name == "HOME" ? "/Users/someone" : null
        };

        Assert.Equal("unix:///Users/someone/.colima/default/docker.sock", host.DockerHost());
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
