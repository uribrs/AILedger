using System.Text.Json;
using AILedger.Cli.Verification;
using AILedger.Core.Contracts;
using AILedger.Tests.Support;
using static AILedger.Tests.Cli.CliApplicationTestSupport;

namespace AILedger.Tests.Verification;

public sealed class VerificationIoFailureTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LogOpenFailureDoesNotStartCommand(bool failStdout)
    {
        using var directory = new TemporaryDirectory();
        var marker = Path.Combine(directory.Path, "started");
        var invalidLog = Directory.CreateDirectory(Path.Combine(directory.Path, "invalid.log")).FullName;
        var validLog = Path.Combine(directory.Path, "valid.log");
        var start = new VerificationProcessStart("printf started > started", directory.Path,
            new Dictionary<string, string>(), failStdout ? invalidLog : validLog,
            failStdout ? validLog : invalidLog);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new ShellVerificationProcessSpawner().RunAsync(start, CancellationToken.None));
        // Give a mistakenly launched child time to expose the original orphan-process defect.
        await Task.Delay(500);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task UnreadableResultRetainsOtherResultsCleanupAndFailureEvidence()
    {
        var spawner = new ScriptedSpawner((start, _) =>
        {
            var output = start.Environment["AILEDGER_VERIFICATION_OUTPUT"];
            File.WriteAllText(Path.Combine(output, "valid.trx"), "valid result");
            File.CreateSymbolicLink(Path.Combine(output, "broken.trx"), Path.Combine(output, "missing"));
            return Task.FromResult<int?>(0);
        });
        using var fixture = new VerificationFixture(spawner);
        await fixture.OpenAsync();
        fixture.WriteRedisProfile();
        fixture.Engine.Owned.Add("owned");

        Assert.Equal(3, await fixture.RunAsync("IO1", VerificationCliCommands.Confirmation(VerificationFixture.RedisCommand)));
        using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.TaskPath, "verification/IO1/result.json")));
        Assert.Equal("failed", result.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, result.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Single(result.RootElement.GetProperty("resultFiles").EnumerateArray());
        Assert.Contains("broken.trx", result.RootElement.GetProperty("errors").ToString());
        Assert.Single(result.RootElement.GetProperty("cleanup").GetProperty("removed").EnumerateArray());
        var state = await Service(fixture.Root).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.NotNull(state);
        Assert.Contains("failed", state.Evidence[new EvidenceId("IO1")].Summary);
    }

    [Fact]
    public async Task UnwritableErrorLogDoesNotLoseStartFailureEvidence()
    {
        var spawner = new ScriptedSpawner((start, _) =>
        {
            Directory.CreateDirectory(start.StandardErrorPath);
            throw new IOException("deliberate start failure");
        });
        using var fixture = new VerificationFixture(spawner);
        await fixture.OpenAsync();
        fixture.WriteRedisProfile();
        fixture.Engine.Owned.Add("owned");

        Assert.Equal(3, await fixture.RunAsync("IO2", VerificationCliCommands.Confirmation(VerificationFixture.RedisCommand)));
        using var result = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.TaskPath, "verification/IO2/result.json")));
        Assert.Contains("deliberate start failure", result.RootElement.GetProperty("errors").ToString());
        Assert.Contains("stderr.log", result.RootElement.GetProperty("errors").ToString());
        Assert.Single(result.RootElement.GetProperty("cleanup").GetProperty("removed").EnumerateArray());
        var state = await Service(fixture.Root).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.NotNull(state);
        Assert.Contains("failed", state.Evidence[new EvidenceId("IO2")].Summary);
    }

    [Fact]
    public async Task UnwritableResultRecordsFailureWithoutClaimingAResultDigest()
    {
        var spawner = new ScriptedSpawner((start, _) =>
        {
            Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(start.StandardOutputPath)!, "result.json"));
            return Task.FromResult<int?>(0);
        });
        using var fixture = new VerificationFixture(spawner);
        await fixture.OpenAsync();
        fixture.WriteRedisProfile();
        fixture.Engine.Owned.Add("owned");

        fixture.Output.GetStringBuilder().Clear();
        Assert.Equal(3, await fixture.RunAsync("IO3", VerificationCliCommands.Confirmation(VerificationFixture.RedisCommand)));
        var state = await Service(fixture.Root).GetStateAsync(new TaskId("T1"), CancellationToken.None);
        Assert.NotNull(state);
        var evidence = state.Evidence[new EvidenceId("IO3")];
        Assert.Contains("failed", evidence.Summary);
        Assert.Contains("removed 1 containers", evidence.Summary);
        Assert.Contains("result.json", evidence.Summary);
        Assert.Contains("unavailable", evidence.Citation);
        Assert.DoesNotContain("sha256:", evidence.Citation);
        using var response = JsonDocument.Parse(fixture.Output.ToString());
        Assert.Equal("failed", response.RootElement.GetProperty("status").GetString());
        Assert.False(response.RootElement.TryGetProperty("resultPath", out _));
        Assert.False(response.RootElement.TryGetProperty("resultSha256", out _));
    }
}
