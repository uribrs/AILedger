using AILedger.Cli;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Storage;
using AILedger.Tests.Support;

namespace AILedger.Tests.Cli;

// The surface the operator actually types. Two validated claims are pinned here: C10, that a body
// cannot travel as an option value because the parser reads any value opening with two dashes as
// the next option name; and C11, that status must not print the bodies, because it serialises the
// whole state to standard output and one task of real documents more than triples what it prints.
[Collection(StandardInput.Collection)]
public sealed class ArtifactCommandTests
{
    private const string Body = "---\ntitle: Prompt contract\n---\n\n# Goal\n\n  Keep the indentation\n";

    [Fact]
    public async Task TheBodyArrivesOnStandardInputAndIsWrittenBackVerbatim()
    {
        using var root = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var application = Create(output, error);
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await application.RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);

        int recordExit;
        using (new StandardInput(Body))
        {
            recordExit = await application.RunAsync(
                ["artifact", "record", .. common, "--id", "A1", "--kind", "user-request",
                 "--title", "Prompt contract", "--body-stdin"], CancellationToken.None);
        }

        var showExit = await application.RunAsync(
            ["artifact", "show", "--root", root.Path, "--task", "T1", "--id", "A1"], CancellationToken.None);

        Assert.Equal(0, recordExit);
        Assert.Equal(0, showExit);
        Assert.Equal(string.Empty, error.ToString());
        // Front matter, blank lines and leading spaces all survive the round trip. A document that
        // comes back reformatted is not the document the work was done against.
        Assert.Contains(Body, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListPrintsTheMetadataAndStatusCarriesNoBodies()
    {
        using var root = new TemporaryDirectory();
        var error = new StringWriter();
        string[] common = ["--root", root.Path, "--task", "T1", "--actor", "operator"];
        await Create(TextWriter.Null, error).RunAsync(
            ["task", "open", .. common, "--title", "Task", "--goal", "Goal"], CancellationToken.None);
        using (new StandardInput(Body))
        {
            await Create(TextWriter.Null, error).RunAsync(
                ["artifact", "record", .. common, "--id", "A1", "--kind", "user-request",
                 "--title", "Prompt contract", "--body-stdin"], CancellationToken.None);
        }

        var listOutput = new StringWriter();
        await Create(listOutput, error).RunAsync(
            ["artifact", "list", "--root", root.Path, "--task", "T1"], CancellationToken.None);
        var statusOutput = new StringWriter();
        await Create(statusOutput, error).RunAsync(
            ["status", "--root", root.Path, "--task", "T1"], CancellationToken.None);

        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("A1", listOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("Prompt contract", listOutput.ToString(), StringComparison.Ordinal);
        // The rows are metadata: what exists, of what kind, superseding what. The body is what
        // 'artifact show' is for.
        Assert.DoesNotContain("Keep the indentation", listOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("A1", statusOutput.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Keep the indentation", statusOutput.ToString(), StringComparison.Ordinal);
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
