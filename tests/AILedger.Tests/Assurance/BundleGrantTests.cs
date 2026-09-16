using AILedger.Core.Contracts;
using Xunit;

namespace AILedger.Tests.Assurance;

public sealed partial class BundleAssuranceTests
{
    [Fact]
    public async Task FileScopeUsesItsParentAndPreservesNarrowUnion()
    {
        using var f = await BundleFixture.CreateAsync(fileScope: "nested");
        await f.Verify("VAB", "A", "B");
        var request = Assert.Single(f.Adapter.Requests);
        Assert.Equal(Path.Combine(f.Repository, "A"), request.WorkingDirectory);
        Assert.Contains(Path.Combine(f.Repository, "B"), request.AdditionalDirectories);
        Assert.DoesNotContain(f.Repository, request.AdditionalDirectories);
    }

    [Fact]
    public async Task ImplicitRootGrantRefusesButExplicitAuthorizedAncestorWorks()
    {
        using var f = await BundleFixture.CreateAsync(fileScope: "root");
        await RefusesWithoutMutation(f, () => f.Launch("BAD", ["A", "B"]), "Assurance coverage:", "implicit repository-root");
        Assert.True(await f.Launch("VAB", ["A", "B"], extra: ["--working-directory", f.Repository]) == 0, f.Error.ToString());
        Assert.Equal(f.Repository, Assert.Single(f.Adapter.Requests).WorkingDirectory);
        Assert.Equal(AgentRunStatus.Completed, (await f.State()).Runs[new RunId("VAB")].Status);
    }

    [Fact]
    public async Task BundleSymlinkCannotGrantUnrelatedDirectory()
    {
        using var f = await BundleFixture.CreateAsync();
        var link = Path.Combine(f.Repository, "A", "escape");
        Directory.CreateSymbolicLink(link, Path.Combine(f.Repository, "C"));
        await RefusesWithoutMutation(f, () => f.Launch("BAD", ["A", "B"], extra: ["--add-dir", link]), "outside", "scope");
    }
}
