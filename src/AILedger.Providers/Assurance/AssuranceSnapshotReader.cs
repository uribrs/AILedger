using System.Text;
using AILedger.Core.Artifacts;
using AILedger.Core.Assurance;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Providers.Assurance;

// Paths locate bytes; only the captured bytes and transitive dependency digests identify a candidate.
internal static class AssuranceSnapshotReader
{
    internal static async Task<AssuranceSnapshot> CaptureAsync(AssurancePolicy policy, string areaId, CancellationToken token)
    {
        var snapshots = new Dictionary<string, AssuranceSnapshot>(StringComparer.Ordinal);
        return await CaptureAreaAsync(policy, areaId, snapshots, new HashSet<string>(), token).ConfigureAwait(false);
    }

    private static async Task<AssuranceSnapshot> CaptureAreaAsync(AssurancePolicy policy, string id,
        Dictionary<string, AssuranceSnapshot> snapshots, HashSet<string> visiting, CancellationToken token)
    {
        if (snapshots.TryGetValue(id, out var existing)) return existing;
        Require(visiting.Add(id), "invalid_policy", "Area dependencies contain a cycle.");
        var area = policy.Areas.SingleOrDefault(a => a.Id == id)
            ?? throw new AssuranceRefusal("invalid_request", "Unknown area; use a granted area ID.");
        var inputs = new List<AssuranceInput>();
        foreach (var (kind, paths) in new[] { ("candidate", area.CandidatePaths), ("requirement", area.RequirementPaths), ("source", area.SourcePaths) })
            foreach (var path in paths.Order(StringComparer.Ordinal))
                inputs.Add(await ReadAsync(policy.CandidateRoot, kind, path, token).ConfigureAwait(false));
        Require(inputs.Sum(i => i.Bytes) <= 64 * 1024, "capacity_exceeded", "One area is limited to 64 KiB of exact UTF-8 inputs. Split the bounded scope explicitly.");
        var dependencies = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var dependency in area.DependsOnAreas.Order(StringComparer.Ordinal))
            dependencies.Add(dependency, (await CaptureAreaAsync(policy, dependency, snapshots, visiting, token).ConfigureAwait(false)).BindingSha256);
        var candidate = Hash(inputs.Where(i => i.Kind == "candidate").Select(i => new { i.Path, i.Sha256, i.Bytes }).ToArray());
        var requirements = Hash(new { Inputs = inputs.Where(i => i.Kind == "requirement").Select(i => new { i.Path, i.Sha256, i.Bytes }).ToArray(), area.Criteria });
        var binding = Hash(new { Area = id, Candidate = candidate, Requirements = requirements,
            Sources = inputs.Where(i => i.Kind == "source").Select(i => new { i.Path, i.Sha256, i.Bytes }).ToArray(), Dependencies = dependencies, policy.Implementer });
        var snapshot = new AssuranceSnapshot(id, candidate, requirements, binding, inputs, dependencies,
            area.Criteria, policy.Implementer, "external_candidate_host_snapshot; implementation execution is not observed");
        snapshots.Add(id, snapshot); visiting.Remove(id);
        return snapshot;
    }

    private static async Task<AssuranceInput> ReadAsync(string root, string kind, string path, CancellationToken token)
    {
        Require(!Path.IsPathFullyQualified(path) && !path.Split('/','\\').Any(p => p is ".." or "." or ""),
            "invalid_policy", "Input paths must be canonical relative paths without traversal.");
        var absolute = Path.GetFullPath(Path.Combine(root, path));
        for (var current = new FileInfo(absolute) as FileSystemInfo; current is not null;
             current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent)
            Require(current.LinkTarget is null || current.FullName is "/tmp" or "/var", "source_unavailable", "Resolve symlinked input roots and paths before binding assurance.");
        await using var stream = new FileStream(absolute, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        Require(stream.Length <= 64 * 1024, "capacity_exceeded", "An input exceeds 64 KiB.");
        var buffer = new byte[64 * 1024 + 1];
        var length = await stream.ReadAtLeastAsync(buffer, buffer.Length, false, token).ConfigureAwait(false);
        Require(length <= 64 * 1024, "capacity_exceeded", "An input grew beyond 64 KiB.");
        var content = new UTF8Encoding(false, true).GetString(buffer, 0, length);
        return new(kind, path, ArtifactSubmissionIdentity.ContentHash(content), length, content);
    }
}
