using AILedger.Cli.Verification;
using AILedger.Tests.Support;

namespace AILedger.Tests.Verification;

public sealed class WorktreeFingerprintTests
{
    [Theory]
    [InlineData(".ailedger-output")]
    [InlineData("src/Worker/.ailedger-output")]
    public async Task UntrackedRuntimeLocksAndOutputDoNotChangeSourceDigest(string output)
    {
        using var directory = new TemporaryDirectory();
        GitCheckout.Init(directory.Path);
        GitCheckout.CommitFile(directory.Path, "source.cs", "class Source {}");
        var before = await WorktreeFingerprint.ComputeAsync(directory.Path, default);
        var runtime = Directory.CreateDirectory(Path.Combine(directory.Path, output)).FullName;
        await using var held = new FileStream(Path.Combine(runtime, ".lock"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        await File.WriteAllTextAsync(Path.Combine(runtime, "execution_notes.md"), "worker output");
        Assert.Equal(before, await WorktreeFingerprint.ComputeAsync(directory.Path, default));

        await File.WriteAllTextAsync(Path.Combine(directory.Path, "source.cs"), "class Changed {}");
        Assert.NotEqual(before, await WorktreeFingerprint.ComputeAsync(directory.Path, default));
    }

    [Theory]
    [InlineData(".ailedger-output", true)]
    [InlineData("src/Worker/.ailedger-output", true)]
    [InlineData(".ailedger-output-other", false)]
    [InlineData("src", false)]
    public async Task TrackedRuntimeFilesAndOtherUntrackedFilesRemainMeasured(string folder, bool tracked)
    {
        using var directory = new TemporaryDirectory();
        GitCheckout.Init(directory.Path);
        Directory.CreateDirectory(Path.Combine(directory.Path, folder));
        var relative = Path.Combine(folder, "source.cs");
        var path = Path.Combine(directory.Path, relative);
        if (tracked) GitCheckout.CommitFile(directory.Path, relative, "class Source {}");
        else await File.WriteAllTextAsync(path, "class Source {}");
        var before = await WorktreeFingerprint.ComputeAsync(directory.Path, default);
        await File.WriteAllTextAsync(path, "class Changed {}");
        Assert.NotEqual(before, await WorktreeFingerprint.ComputeAsync(directory.Path, default));

        await using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsAsync<IOException>(() => WorktreeFingerprint.ComputeAsync(directory.Path, default));
    }
}
