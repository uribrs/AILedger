using System.Reflection;

namespace AILedger.TestSupport;

internal static class RepositoryLayout
{
    // The source checkout is authoritative, even if the test host runs in another Git tree.
    // Keep this metadata in test assemblies only; no production discovery behavior changes.
    public static string Root { get; } = ValidateRoot(
        typeof(RepositoryLayout).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "AILedger.TestRepositoryRoot")?.Value);

    internal static string ValidateRoot(string? root)
    {
        if (!string.IsNullOrWhiteSpace(root) && Path.IsPathFullyQualified(root) &&
            File.Exists(Path.Combine(root, "AILedger.sln")) &&
            (Directory.Exists(Path.Combine(root, ".git")) || File.Exists(Path.Combine(root, ".git"))))
        {
            return Path.GetFullPath(root);
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the AILedger source checkout recorded by the test build: '{root}'. " +
            "Expected AILedger.sln and a .git file or directory. Keep the checkout available; " +
            "rebuild the tests if it moved. Runtime working directories are not source identity.");
    }
}
