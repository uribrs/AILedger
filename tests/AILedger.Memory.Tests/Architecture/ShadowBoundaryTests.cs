using System.Reflection;
using System.Xml.Linq;
using AILedger.Memory.Tests.Support;

namespace AILedger.Memory.Tests.Architecture;

public sealed class ShadowBoundaryTests
{
    private static readonly string[] KernelProjectPaths =
    [
        "src/AILedger.Core/AILedger.Core.csproj",
        "src/AILedger.Storage/AILedger.Storage.csproj",
        "src/AILedger.Providers/AILedger.Providers.csproj",
        "src/AILedger.Cli/AILedger.Cli.csproj"
    ];

    [Fact]
    public void R1_KernelProjectsDoNotReferenceOrInvokeMemory()
    {
        foreach (var relativePath in KernelProjectPaths)
        {
            var projectPath = Path.Combine(RepositoryLayout.Root, relativePath);
            var project = XDocument.Load(projectPath);
            var references = project.Descendants("ProjectReference")
                .Select(element => (string?)element.Attribute("Include"))
                .Where(value => value is not null)
                .Cast<string>();

            Assert.DoesNotContain(
                references,
                reference => reference.Contains("AILedger.Memory", StringComparison.OrdinalIgnoreCase));

            var projectDirectory = Path.GetDirectoryName(projectPath)!;
            foreach (var sourcePath in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
            {
                var source = File.ReadAllText(sourcePath);
                Assert.DoesNotContain("AILedger.Memory", source, StringComparison.Ordinal);
                Assert.DoesNotContain("ailedger-memory", source, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void R1_BuiltKernelAssembliesDoNotReferenceMemory()
    {
        Assembly[] kernelAssemblies =
        [
            typeof(AILedger.Core.Contracts.LedgerEvent).Assembly,
            typeof(AILedger.Storage.FileGovernedTaskService).Assembly,
            typeof(AILedger.Providers.Adapters.CodexAgentAdapter).Assembly,
            typeof(AILedger.Cli.CliApplication).Assembly
        ];

        foreach (var assembly in kernelAssemblies)
        {
            Assert.DoesNotContain(
                assembly.GetReferencedAssemblies(),
                reference => reference.Name?.StartsWith("AILedger.Memory", StringComparison.Ordinal) == true);
        }
    }

    [Fact]
    public void MemoryProductionSourcesDoNotUseGovernedReadsOrWriterLocks()
    {
        var forbidden = new[]
        {
            "IGovernedTaskService",
            "GetHistoryAsync",
            "GetStateAsync",
            "TaskMutationLock",
            ".writer.lock"
        };

        foreach (var project in new[] { "AILedger.Memory", "AILedger.Memory.Cli" })
        {
            var projectDirectory = Path.Combine(RepositoryLayout.Root, "src", project);
            foreach (var sourcePath in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
            {
                var source = File.ReadAllText(sourcePath);
                foreach (var token in forbidden)
                {
                    Assert.DoesNotContain(token, source, StringComparison.Ordinal);
                }
            }
        }
    }
}
