namespace AILedger.Memory.Tests.Support;

internal static class RepositoryLayout
{
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(System.IO.Path.Combine(directory.FullName, "AILedger.sln")) &&
                    (Directory.Exists(System.IO.Path.Combine(directory.FullName, ".git")) ||
                     File.Exists(System.IO.Path.Combine(directory.FullName, ".git"))))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Could not locate the AILedger repository root.");
    }
}
