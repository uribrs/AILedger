using System.Collections;
using Xunit;
using Xunit.Abstractions;

namespace GovernedTests.Fixtures;

public sealed class SharedFixture : IAsyncLifetime
{
    public bool Initialized { get; private set; }
    public Task InitializeAsync() { Initialized = true; return Task.CompletedTask; }
    public Task DisposeAsync() => Task.CompletedTask;
}

public sealed class LifecycleCases(SharedFixture fixture, ITestOutputHelper output) :
    IClassFixture<SharedFixture>, IAsyncLifetime
{
    private bool initialized;

    public Task InitializeAsync() { initialized = true; return Task.CompletedTask; }
    public Task DisposeAsync() { Assert.True(initialized); return Task.CompletedTask; }

    [Fact]
    public void FixtureAndOutputHelper()
    {
        Assert.True(fixture.Initialized);
        Assert.True(initialized);
        output.WriteLine("Real xUnit output helper supplied.");
    }

    [Theory]
    [InlineData(DayOfWeek.Monday)]
    [InlineData(null)]
    public void NullableEnum(DayOfWeek? value) => Assert.True(value is null or DayOfWeek.Monday);

    public static IEnumerable<object[]> Rows => [new object[] { 2 }, new object[] { 3 }];

    [Theory]
    [MemberData(nameof(Rows))]
    public void MemberRows(int value) => Assert.InRange(value, 2, 3);

    [Theory]
    [ClassData(typeof(ClassRows))]
    public void ClassRows(int value) => Assert.Equal(4, value);

    [Fact(Skip = "Verify skip reporting.")]
    public void Skipped() => Assert.Fail("A skipped test must not run.");
}

public sealed class ClassRows : IEnumerable<object[]>
{
    public IEnumerator<object[]> GetEnumerator() => ((IEnumerable<object[]>)[new object[] { 4 }]).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class FailingCases
{
    [Fact]
    public void DeliberateFailure() => Assert.Fail("Intentional runner failure probe.");
}

public sealed class FailingCleanup : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => throw new InvalidOperationException("Intentional fixture cleanup failure.");
}

public sealed class CleanupCases : IClassFixture<FailingCleanup>
{
    [Fact]
    public void PassesBeforeFixtureCleanup() => Assert.True(true);
}
