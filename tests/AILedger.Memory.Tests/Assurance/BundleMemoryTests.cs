using System.Text.Json;
using System.Text.Json.Nodes;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Memory.Contracts;
using AILedger.Memory.Evaluation;
using AILedger.Memory.Ingestion;
using AILedger.Memory.Normalization;
using AILedger.Memory.Retrieval;
using AILedger.Memory.Storage;
using AILedger.Memory.Tests.Storage;
using AILedger.Memory.Tests.Support;
using AILedger.Storage;
using Microsoft.Data.Sqlite;

namespace AILedger.Memory.Tests.Assurance;

public sealed class BundleMemoryTests
{
    // M13: unknown reverted-candidate fields must never activate a second member.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task R5_OldAndRevertedCoverageReplayAsBaseline(bool explicitNull, bool taskWide)
    {
        using var directory = new TemporaryDirectory();
        var history = new History();
        history.Add(new RunStarted(new AgentRun(new RunId("legacy"), History.Operator,
            taskWide ? null : new WorkItemId("A"), "claude", null,
            AgentRunStatus.Active, history.NextTime, null)));
        var json = JsonNode.Parse(Serialize(history.Events[^1]))!.AsObject();
        var run = json["data"]!["run"]!.AsObject();
        run.Remove("subjectRole");
        run.Remove("providerSessionId");
        if (explicitNull) run["assurance"] = null;
        else run.Remove("assurance");
        run["additionalWorkItemIds"] = new JsonArray("B");
        var restored = JsonSerializer.Deserialize<LedgerEvent>(json.ToJsonString(), LedgerJson.CreateOptions())!;
        history.Events[^1] = restored;
        var canonical = history.Replay(new TaskReducer().Apply);
        var shadow = history.Replay(HistoricalLedgerProjector.Apply);
        Assert.Equal(taskWide ? WorkItemStatus.Proposed : WorkItemStatus.Active, canonical.WorkItems[new("A")].Status);
        Assert.Equal(WorkItemStatus.Proposed, canonical.WorkItems[new("B")].Status);
        AssertProjectionEqual(canonical.WorkItems[new("A")], shadow.WorkItems[new("A")]);
        AssertProjectionEqual(canonical.WorkItems[new("B")], shadow.WorkItems[new("B")]);
        var delta = await history.ReadAsync(directory.Path);
        var document = Assert.Single(delta.Upserts, item => item.RunId == "legacy");
        Assert.Equal(taskWide ? Array.Empty<string>() : ["A"], document.CoveredWorkItemIds);
        Assert.DoesNotContain(document.Relations, relation => relation.TargetId == "B");
    }

    // M18/M23: use the same serialized event stream for both reducers and ingestion.
    [Theory]
    [InlineData(AgentRunStatus.Failed)]
    [InlineData(AgentRunStatus.Cancelled)]
    public async Task AllMembersActivateAndBlockedMemberSurvivesFailure(AgentRunStatus terminal)
    {
        using var directory = new TemporaryDirectory();
        var history = new History();
        history.Workers();
        history.StartAssurance("Vab", "A", "B");
        var active = history.Replay(new TaskReducer().Apply);
        Assert.Equal(WorkItemStatus.Active, active.WorkItems[new("A")].Status);
        Assert.Equal(WorkItemStatus.Active, active.WorkItems[new("B")].Status);
        history.Add(new EvidenceAdded(new Evidence(new("EB"), "test", "fixture:dependency", "B dependency rejected", [], [new("CB")], new(History.Operator, history.NextTime, "evidence.add"))));
        history.Add(new ClaimResolved(new("CB"), ClaimStatus.Rejected, [new("EB")]));
        history.Add(new WorkItemInvalidated(new("B"), new("CB"), WorkItemStatus.Blocked));
        history.Add(new RunCompleted(new("Vab"), terminal, "verifier-session", history.NextTime,
            OutputTokens: 123, TokensInUncached: 456));
        var canonical = history.Replay(new TaskReducer().Apply);
        var shadow = history.Replay(HistoricalLedgerProjector.Apply);
        Assert.Equal(WorkItemStatus.Paused, canonical.WorkItems[new("A")].Status);
        Assert.Equal(WorkItemStatus.Blocked, canonical.WorkItems[new("B")].Status);
        foreach (var id in new[] { "A", "B", "C", "D" })
            AssertProjectionEqual(canonical.WorkItems[new(id)], shadow.WorkItems[new(id)]);
        AssertProjectionEqual(canonical.Runs[new("Vab")], shadow.Runs[new("Vab")]);
        Assert.Equal(123, shadow.Runs.Values.Sum(run => run.OutputTokens ?? 0));
        var delta = await history.ReadAsync(directory.Path);
        var runDocument = Assert.Single(delta.Upserts, document => document.Kind == MemoryDocumentKind.RunSummary && document.RunId == "Vab");
        Assert.Equal(new[] { "A", "B" }, runDocument.CoveredWorkItemIds);
        Assert.Equal(new[] { "A", "B" }, runDocument.Relations.Where(relation => relation.Kind == DocumentRelationKind.BelongsTo).Select(relation => relation.TargetId).Order());
        foreach (var id in new[] { "A", "B" })
        {
            var work = Assert.Single(delta.Upserts, document => document.Kind == MemoryDocumentKind.WorkSummary && document.WorkItemId == id);
            Assert.Contains(work.Citations, citation => citation.EventId == history.Events[^1].EventId.Value);
        }
    }

    [Fact]
    public async Task RepointAndUnblockRestoresAllMemberProjectionBeforeNewAssurance()
    {
        using var directory = new TemporaryDirectory();
        var history = new History();
        history.Workers();
        history.StartAssurance("Vab", "A", "B");
        history.Add(new ClaimAdded(new Claim(new("replacement"), "replacement dependency", ClaimStatus.Open,
            [], null, new(History.Operator, history.NextTime, "claim.add"))));
        history.Add(new ClaimResolved(new("CB"), ClaimStatus.Superseded, [], new("replacement"), SupersessionOutcome.Correction));
        history.Add(new WorkItemInvalidated(new("B"), new("CB"), WorkItemStatus.Blocked));
        history.Add(new RunCompleted(new("Vab"), AgentRunStatus.Failed, "failed-session", history.NextTime));
        history.Add(new EvidenceAdded(new Evidence(new("replacement-proof"), "test", "fixture:repair",
            "replacement earned", [new("replacement")], [], new(History.Operator, history.NextTime, "evidence.add"))));
        history.Add(new ClaimResolved(new("replacement"), ClaimStatus.Validated, [new("replacement-proof")]));
        history.Add(new ClaimDependenciesRepointed(new("CB"), new("replacement")));
        history.Add(new WorkItemUnblocked(new("B")));
        history.StartAssurance("Vrepair", "A", "B");
        var canonical = history.Replay(new TaskReducer().Apply);
        var shadow = history.Replay(HistoricalLedgerProjector.Apply);
        foreach (var id in new[] { "A", "B" })
        {
            Assert.Equal(WorkItemStatus.Active, canonical.WorkItems[new(id)].Status);
            AssertProjectionEqual(canonical.WorkItems[new(id)], shadow.WorkItems[new(id)]);
        }
        Assert.Equal(new[] { new ClaimId("replacement") }, shadow.WorkItems[new("B")].DependsOnClaims);
        var delta = await history.ReadAsync(directory.Path);
        Assert.Equal(new[] { "A", "B" }, Assert.Single(delta.Upserts, document => document.RunId == "Vrepair").CoveredWorkItemIds);
    }

    [Fact]
    public void TerminalStaleMemberCannotBeRevivedByBundle()
    {
        var history = new History();
        history.Workers();
        history.Add(new EvidenceAdded(new Evidence(new("EB"), "test", "fixture:dependency", "B rejected",
            [], [new("CB")], new(History.Operator, history.NextTime, "evidence.add"))));
        history.Add(new ClaimResolved(new("CB"), ClaimStatus.Rejected, [new("EB")]));
        history.Add(new WorkItemInvalidated(new("B"), new("CB"), WorkItemStatus.Stale));
        var canonical = history.Replay(new TaskReducer().Apply);
        var shadow = history.Replay(HistoricalLedgerProjector.Apply);
        Assert.Equal(WorkItemStatus.Stale, shadow.WorkItems[new("B")].Status);
        AssertProjectionEqual(canonical.WorkItems[new("B")], shadow.WorkItems[new("B")]);
        history.StartAssurance("Vinvalid", "A", "B");
        var error = Assert.Throws<GovernanceException>(() => new TaskReducer().Apply(canonical, history.Events[^1]));
        Assert.Contains("Assurance member 'B':", error.Message, StringComparison.Ordinal);
        Assert.Contains("Stale", error.Message, StringComparison.Ordinal);
        Assert.Equal(WorkItemStatus.Paused, canonical.WorkItems[new("A")].Status);
        Assert.DoesNotContain(new RunId("Vinvalid"), canonical.Runs.Keys);
    }

    // R1 (partial-applicability), M20: historical coverage is immutable; [] is not missing.
    [Fact]
    public async Task R1_PartialRepairRetainsUnaffectedApplicability()
    {
        using var directory = new TemporaryDirectory();
        var history = new History();
        history.Artifact("Vab", ["A", "B"], []);
        history.Artifact("Vd", ["D"], []);
        var first = await history.ReadAsync(directory.Path);
        history.Artifact("Va", ["A"], [new(new("A"), [new("Vab")])]);
        var partial = await history.ReadAsync(directory.Path, first.NextCheckpoint);
        Assert.Equal(new[] { "B" }, Artifact(partial, "Vab").ApplicableWorkItemIds);
        Assert.Equal(MemoryLifecycle.Active, Artifact(partial, "Vab").Lifecycle);
        Assert.NotEqual(Artifact(first, "Vab").ContentHash, Artifact(partial, "Vab").ContentHash);
        Assert.NotEqual(Artifact(first, "Vab").SourceVersion, Artifact(partial, "Vab").SourceVersion);
        history.Artifact("Vbc", ["B", "C"], [new(new("B"), [new("Vab")]), new(new("C"), [])]);
        var incremental = await history.ReadAsync(directory.Path, partial.NextCheckpoint);
        Assert.Empty(Artifact(incremental, "Vab").ApplicableWorkItemIds);
        Assert.DoesNotContain(incremental.Upserts, document => document.Id == Artifact(first, "Vd").Id);
        var final = await history.ReadAsync(directory.Path);
        Assert.Equal(new[] { "A", "B" }, Artifact(final, "Vab").CoveredWorkItemIds);
        Assert.Empty(Artifact(final, "Vab").ApplicableWorkItemIds);
        Assert.Equal(MemoryLifecycle.Superseded, Artifact(final, "Vab").Lifecycle);
        Assert.Equal(new[] { "A" }, Artifact(final, "Va").ApplicableWorkItemIds);
        Assert.Equal(new[] { "B", "C" }, Artifact(final, "Vbc").ApplicableWorkItemIds);
        Assert.Equal(new[] { "D" }, Artifact(final, "Vd").ApplicableWorkItemIds);
        var factory = new SqliteMemoryProjectionStoreFactory(Path.Combine(directory.Path, "memory.sqlite"));
        await using (var rebuild = await factory.BeginRebuildAsync())
        {
            await StoreAsync(rebuild.Store, first);
            await StoreAsync(rebuild.Store, partial);
            await StoreAsync(rebuild.Store, incremental);
            await rebuild.ReplaceCurrentAsync();
        }
        var store = await factory.OpenCurrentAsync();
        var old = (await store.InspectAsync(Artifact(final, "Vab").Id))!;
        Assert.Equal(new[] { "A", "B" }, old.CoveredWorkItemIds);
        Assert.Empty(old.ApplicableWorkItemIds);
        var current = await new HybridMemorySearcher(store).SearchAsync(new MemoryQuery { Text = "bundlefinding", UseEmbeddings = false });
        Assert.DoesNotContain(current.Results, result => result.DocumentId == old.Id);
        var historical = await new HybridMemorySearcher(store).SearchAsync(new MemoryQuery { Text = "bundlefinding", IncludeSuperseded = true, UseEmbeddings = false });
        Assert.Equal(new[] { "A", "B" }, Assert.Single(historical.Results, result => result.DocumentId == old.Id).CoveredWorkItemIds);
    }

    [Fact]
    public async Task CoverageRoundTripsThroughMemoryDebtAndViews()
    {
        using var directory = new TemporaryDirectory();
        var history = new History();
        history.Workers();
        history.StartAssurance("Vab", "A", "B");
        var delta = await history.ReadAsync(directory.Path);
        var run = Assert.Single(delta.Upserts, document => document.RunId == "Vab");
        var factory = new SqliteMemoryProjectionStoreFactory(Path.Combine(directory.Path, "memory.sqlite"));
        await using (var rebuild = await factory.BeginRebuildAsync())
        {
            await StoreAsync(rebuild.Store, delta);
            await rebuild.ReplaceCurrentAsync();
        }
        var store = await factory.OpenCurrentAsync();
        Assert.Equal(2, (await store.GetStatisticsAsync()).SchemaVersion);
        Assert.Equal(delta.Upserts.Count, (await store.GetStatisticsAsync()).DocumentCount);
        var stored = (await store.InspectAsync(run.Id))!;
        Assert.Equal(new[] { "A", "B" }, stored.CoveredWorkItemIds);
        Assert.Equal(new[] { "A", "B" }, stored.Relations.Where(relation => relation.Kind == DocumentRelationKind.BelongsTo).Select(relation => relation.TargetId).Order());
        var lexical = await new HybridMemorySearcher(store).SearchAsync(new MemoryQuery { Text = "Vab", UseEmbeddings = false });
        Assert.Equal(new[] { "A", "B" }, Assert.Single(lexical.Results, result => result.DocumentId == run.Id).CoveredWorkItemIds);
        var identity = new EmbeddingIdentity { Provider = "fixture", Model = "fixture", Dimensions = 2, Version = "1" };
        await using (var transaction = await store.BeginUpdateAsync())
        {
            await transaction.SetActiveEmbeddingIdentityAsync(identity);
            await transaction.StoreEmbeddingsAsync([new DocumentEmbedding
            {
                DocumentId = run.Id, ContentHash = run.ContentHash, Identity = identity,
                ChunkIndex = 0, ChunkCount = 1, Vector = new float[] { 1, 0 }, EmbeddedAt = DateTimeOffset.UnixEpoch
            }]);
            await transaction.CommitAsync();
        }
        var semantic = await new HybridMemorySearcher(store, new Generator(identity)).SearchAsync(new MemoryQuery { Text = "absentsemanticword" });
        Assert.True(semantic.SemanticSearchUsed);
        Assert.Equal(new[] { "A", "B" }, Assert.Single(semantic.Results).CoveredWorkItemIds);
        // The baseline deliberately accepts lessons only; retain the same stored metadata.
        var baseline = await new TagRecencyBaselineSearcher([stored with { Kind = MemoryDocumentKind.Lesson, Tags = ["bundle"] }])
            .SearchAsync(new MemoryQuery { Text = "bundle" });
        Assert.Equal(new[] { "A", "B" }, Assert.Single(baseline.Results).CoveredWorkItemIds);
    }

    [Fact]
    public async Task OldNormalizerCheckpointRefreshesUnchangedHistory()
    {
        using var directory = new TemporaryDirectory();
        var history = new History();
        history.Workers();
        history.StartAssurance("Vab", "A", "B");
        var first = await history.ReadAsync(directory.Path);
        var checkpoint = Assert.IsType<EventHistoryCheckpoint>(first.NextCheckpoint);
        var old = checkpoint with
        {
            SourceFingerprint = MemoryIdentity.HashParts("event-history-v1", checkpoint.FileContentHash, "1")
        };
        var refreshed = await history.ReadAsync(directory.Path, old);
        Assert.NotEmpty(refreshed.Upserts);
        Assert.All(refreshed.Upserts, document => Assert.NotEqual("1", document.NormalizerVersion));
        Assert.Equal(new[] { "A", "B" }, Assert.Single(refreshed.Upserts, document => document.RunId == "Vab").CoveredWorkItemIds);
    }

    [Fact]
    public async Task SchemaOneRequiresExplicitAtomicRebuild()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "memory.sqlite");
        await SqlAsync(path, "CREATE TABLE memory_metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL); INSERT INTO memory_metadata VALUES ('schema_version', '1');");
        var original = await File.ReadAllBytesAsync(path);
        var factory = new SqliteMemoryProjectionStoreFactory(path);
        var error = await Assert.ThrowsAsync<NotSupportedException>(() => factory.OpenCurrentAsync());
        Assert.Contains("1", error.Message, StringComparison.Ordinal);
        Assert.Contains("rebuild", error.Message, StringComparison.OrdinalIgnoreCase);
        await using (var failed = await factory.BeginRebuildAsync())
        {
            Assert.Equal(2, failed.Store.SupportedSchemaVersion);
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
        }
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        await using (var rebuild = await factory.BeginRebuildAsync())
        {
            await rebuild.ReplaceCurrentAsync();
        }
        Assert.Equal(2, (await (await factory.OpenCurrentAsync()).GetStatisticsAsync()).SchemaVersion);
    }

    [Theory]
    [InlineData((object?)null)]
    [InlineData("[]")]
    public async Task MissingOriginalCoverageFallsBackButExplicitEmptyApplicabilityDoesNot(string? coverage)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "memory.sqlite");
        var factory = new SqliteMemoryProjectionStoreFactory(path);
        var document = ProjectionStoreTests.Document("legacy", "legacycoverage") with
        { WorkItemId = "A", CoveredWorkItemIds = ["A"], ApplicableWorkItemIds = [] };
        await using (var rebuild = await factory.BeginRebuildAsync())
        {
            await StoreAsync(rebuild.Store, Delta(document));
            await rebuild.ReplaceCurrentAsync();
        }
        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE documents SET covered_work_item_ids_json = $coverage, applicable_work_item_ids_json = '[]'";
            command.Parameters.AddWithValue("$coverage", (object?)coverage ?? DBNull.Value);
            await command.ExecuteNonQueryAsync();
        }
        var restored = (await (await factory.OpenCurrentAsync()).InspectAsync(document.Id))!;
        Assert.Equal(new[] { "A" }, restored.CoveredWorkItemIds);
        Assert.Empty(restored.ApplicableWorkItemIds);
    }

    private static void AssertProjectionEqual<T>(T expected, T actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected, LedgerJson.CreateOptions()),
            JsonSerializer.Serialize(actual, LedgerJson.CreateOptions()));

    private static MemoryDocument Artifact(SourceDelta delta, string id) =>
        Assert.Single(delta.Upserts, document => document.Kind == MemoryDocumentKind.Artifact && document.Citations.Any(citation => citation.RecordId == id));

    private static async Task StoreAsync(IMemoryProjectionStore store, SourceDelta delta)
    {
        await using var transaction = await store.BeginUpdateAsync();
        await transaction.ApplyAsync(delta);
        await transaction.CommitAsync();
    }

    private static SourceDelta Delta(MemoryDocument document) => new()
    {
        Source = new SourceDescriptor { Id = "events", Kind = CanonicalSourceKind.EventHistory, CanonicalPath = "events.jsonl" },
        NextCheckpoint = new EventHistoryCheckpoint { SourceId = "events", Kind = CanonicalSourceKind.EventHistory,
            SourceFingerprint = "fixture", ObservedAt = DateTimeOffset.UnixEpoch, FileLength = 1, FileContentHash = "fixture", EventCount = 1 },
        Upserts = [document]
    };

    private static async Task SqlAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class Generator(EmbeddingIdentity identity) : IEmbeddingGenerator
    {
        public string Provider => identity.Provider;
        public string Model => identity.Model;
        public Task<EmbeddingBatch> GenerateAsync(EmbeddingRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EmbeddingBatch { Identity = identity, Vectors = [new float[] { 1, 0 }] });
    }

    private static string Serialize(LedgerEvent item) => JsonSerializer.Serialize(item, LedgerJson.CreateOptions());

    private sealed class History
    {
        public static ActorId Operator => new("operator");
        public List<LedgerEvent> Events { get; } = [];
        public DateTimeOffset NextTime => DateTimeOffset.UnixEpoch.AddSeconds(Events.Count + 1);

        public History()
        {
            Add(new TaskOpened("Bundle memory", "Projection fixture"));
            Add(new RoleAssigned(new RoleAssignment(Operator, RoleKind.Operator, Enum.GetValues<Capability>(), new(Operator, NextTime, "task.open"))));
            Add(new RoleAssigned(new RoleAssignment(new("verifier"), RoleKind.Verifier, [], new(Operator, NextTime, "actor.assign-role"))));
            Add(new ClaimAdded(new Claim(new("CB"), "B dependency", ClaimStatus.Open, [], null, new(Operator, NextTime, "claim.add"))));
            foreach (var id in new[] { "A", "B", "C", "D" })
                Add(new WorkItemAdded(new WorkItem(new(id), id, null, WorkItemStatus.Proposed, id == "B" ? [new("CB")] : [], [Path.Combine(Path.GetTempPath(), "bundle-memory", id)])));
        }

        public void Add(LedgerEventData data) => Events.Add(new LedgerEvent(1, new($"EV{Events.Count + 1}"), new("T1"), Operator, NextTime, null, "memory-fixture", data));

        public void Workers()
        {
            foreach (var id in new[] { "A", "B", "C", "D" })
            {
                Add(new RunStarted(new AgentRun(new($"worker-{id}"), Operator, new(id), "codex", null, AgentRunStatus.Active, NextTime, null, SubjectRole: RoleKind.Worker)));
                Add(new RunCompleted(new($"worker-{id}"), AgentRunStatus.Completed, $"session-{id}", NextTime));
            }
        }

        public void StartAssurance(string runId, params string[] ids) => Add(new RunStarted(new AgentRun(
            new(runId), new("verifier"), new(ids[0]), "claude", null, AgentRunStatus.Active, NextTime, null,
            LaunchedBy: Operator, SubjectRole: RoleKind.Verifier, Assurance: Binding(ids))));

        public void Artifact(string id, string[] members, ArtifactMemberReplacement[] replacements)
        {
            // Production ingestion must retain findings even when their producer later fails.
            // Artifact authority/admission is tested independently by W6.
            StartAssurance("producer-" + id, members);
            var rows = members.Select(member => replacements.FirstOrDefault(row => row.WorkItemId.Value == member)
                ?? new ArtifactMemberReplacement(new(member), [])).ToArray();
            Add(new ArtifactRecorded(new GovernedArtifact(new(id), GovernedArtifactKind.VerifierOutput,
                id, "bundlefinding", new(members[0]), new("producer-" + id), null, new(Operator, NextTime, "artifact.record"), Binding(members), rows)));
            Add(new RunCompleted(new("producer-" + id), AgentRunStatus.Failed, "failed-" + id, NextTime));
        }

        private static AssuranceBinding Binding(string[] ids) => new(1, ids.Select(id => new WorkItemId(id)).ToArray(),
            ids.Select(id => new AssuranceWorkVersion(new(id), new($"worker-{id}"))).ToArray(), new string('a', 64));

        public GovernedTaskState Replay(Func<GovernedTaskState?, LedgerEvent, GovernedTaskState> apply)
        {
            GovernedTaskState? state = null;
            foreach (var item in Events) state = apply(state, JsonSerializer.Deserialize<LedgerEvent>(Serialize(item), LedgerJson.CreateOptions())!);
            return state!;
        }

        public async Task<SourceDelta> ReadAsync(string directory, SourceCheckpoint? checkpoint = null)
        {
            var path = Path.Combine(directory, "events.jsonl");
            await File.WriteAllTextAsync(path, string.Join('\n', Events.Select(Serialize)) + "\n");
            return await new EventHistorySourceReader().ReadAsync(new SourceDescriptor
            { Id = "events", Kind = CanonicalSourceKind.EventHistory, CanonicalPath = path, TaskId = "T1" }, checkpoint);
        }
    }
}
