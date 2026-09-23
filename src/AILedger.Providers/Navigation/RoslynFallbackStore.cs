using System.Text.Json;

namespace AILedger.Providers.Navigation;

// Coordination between the MCP bridge and short-lived provider hook processes. This is a
// workflow guard, not a security boundary against a process deliberately editing its own files.
internal sealed class RoslynFallbackStore(string directory)
{
    internal void Revoke(string scope)
    {
        foreach (var (path, receipt) in Read())
        {
            if (RoslynSolutionPaths.Comparer.Equals(receipt.Scope, scope))
            {
                File.Delete(path);
            }
        }
    }

    internal string Record(string scope, string operation, string reason)
    {
        scope = RoslynSolutionPaths.Canonicalize(scope);
        Revoke(scope);
        Directory.CreateDirectory(directory);
        var id = Guid.NewGuid().ToString("N");
        var receipt = new Receipt(scope, operation, reason[..Math.Min(reason.Length, 500)], DateTimeOffset.UtcNow);
        var temporary = Path.Combine(directory, id + ".tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(receipt));
        File.Move(temporary, Path.Combine(directory, id + ".json"));
        return id;
    }

    internal string? Consume(IReadOnlyList<string> targets)
    {
        if (targets.Count == 0)
        {
            return null;
        }

        foreach (var (path, receipt) in Read())
        {
            if (DateTimeOffset.UtcNow - receipt.CreatedAt > TimeSpan.FromMinutes(10) ||
                receipt.CreatedAt > DateTimeOffset.UtcNow ||
                !targets.All(target => RoslynSolutionPaths.Contains(receipt.Scope, target)))
            {
                continue;
            }

            try
            {
                // Rename claims the receipt atomically: parallel hooks cannot both spend it.
                File.Move(path, path + ".used");
                return $"Recorded Roslyn {receipt.Operation} failure: {receipt.Reason}. One CLI fallback consumed.";
            }
            catch (IOException) when (!File.Exists(path))
            {
                // Another hook already consumed it.
            }
        }

        return null;
    }

    private IEnumerable<(string Path, Receipt Receipt)> Read()
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            Receipt? receipt;
            try
            {
                receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(path));
            }
            catch (FileNotFoundException)
            {
                continue;
            }

            if (receipt is not null)
            {
                yield return (path, receipt);
            }
        }
    }

    private sealed record Receipt(string Scope, string Operation, string Reason, DateTimeOffset CreatedAt);
}
