using System.Reflection;
using AILedger.TestSupport;

namespace AILedger.Tests.Support;

public sealed class RepositoryLayoutTests
{
    [Fact]
    public void RootUsesTheBuildCheckoutAndContainsTheRealTestInputs()
    {
        var metadata = typeof(RepositoryLayout).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
        var sourceRoot = Assert.Single(metadata, entry => entry.Key == "AILedger.TestRepositoryRoot").Value;

        Assert.Equal(Path.GetFullPath(sourceRoot!), RepositoryLayout.Root);
        Assert.Equal(ContextBrief.CognitiveRoot(), Environment.GetEnvironmentVariable("AILEDGER_COGNITIVE_ROOT"));
        Assert.True(File.Exists(Path.Combine(RepositoryLayout.Root, "cognitive", "manifest.json")));
        Assert.True(File.Exists(Path.Combine(RepositoryLayout.Root,
            "src", "AILedger.Core", "AILedger.Core.csproj")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RecognizesCloneAndWorktreeMarkers(bool worktree)
    {
        using var root = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "AILedger.sln"), "", CancellationToken.None);
        var git = Path.Combine(root.Path, ".git");
        if (worktree)
        {
            await File.WriteAllTextAsync(git, "gitdir: /unused/metadata", CancellationToken.None);
        }
        else
        {
            Directory.CreateDirectory(git);
        }

        Assert.Equal(root.Path, RepositoryLayout.ValidateRoot(root.Path));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task MissingCheckoutMarkersFailWithAnActionableError(bool solution, bool git)
    {
        using var root = new TemporaryDirectory();
        if (solution)
        {
            await File.WriteAllTextAsync(Path.Combine(root.Path, "AILedger.sln"), "", CancellationToken.None);
        }
        if (git)
        {
            Directory.CreateDirectory(Path.Combine(root.Path, ".git"));
        }

        var error = Assert.Throws<DirectoryNotFoundException>(() => RepositoryLayout.ValidateRoot(root.Path));

        Assert.Contains(root.Path, error.Message, StringComparison.Ordinal);
        Assert.Contains("rebuild the tests if it moved", error.Message, StringComparison.Ordinal);
    }
}
