using System.Collections;
using System.Reflection;
using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Tests.Core;

// TaskRetrospective reports what governance did on one task and what it cost. It refuses nothing and
// scores nothing, so these tests are about the counts being honest rather than about any gate — and
// in particular about the three places where an absent measurement must not read as a zero: cost,
// brief delivery and the refusal journal.
public sealed class TaskRetrospectiveTests
{
    private static readonly LessonId Inherited = new("earlier-task:imported:C9");
    private static readonly TaskId Task = new("retro-task");

    // Every entry in notMeasured is derived from a state-checkable condition. These two are not
    // conditional: the coordinating session holds no run at all, so its spend is invisible here by
    // construction, and nothing in the log ever says whether the delivered result works.
    [Fact]
    public void TheCoordinatorsCostAndTheOutcomeAreAlwaysReportedAsNotMeasured()
    {
        var log = new Log();

        var report = log.Build();

        Assert.Contains("coordinatorCost", report.NotMeasured);
        Assert.Contains("outcomeQuality", report.NotMeasured);
    }

    // C4: only five of 299 runs in this repository carry any cost field, because the provider stream
    // was written to stdout and dropped before build 459b575. A total of zero would read as
    // "governance was free"; a null total with the count of runs that measured nothing beside it
    // reads as "nobody measured".
    [Fact]
    public void ARunThatMeasuredNoCostIsReportedAsUnmeasuredAndNotAsZero()
    {
        var log = new Log();
        log.WithRun("R1", "claude", RoleKind.Worker, minutes: 30, manifestHash: "hash-1");

        var report = log.Build();

        Assert.Equal(0, report.Runs.CostRecorded);
        Assert.Equal(0, report.Cost.RunsMeasured);
        Assert.Equal(1, report.Cost.RunsUnmeasured);
        Assert.Null(report.Cost.OutputTokens.Total);
        Assert.Null(report.Cost.Turns.Total);
        Assert.Null(report.Cost.TokensInUncached.Total);
        Assert.Null(report.Cost.MillisecondsToFirstLedgerWrite.Total);
        Assert.Contains("governanceCost.tokens", report.NotMeasured);
    }

    [Fact]
    public void ARunThatMeasuredCostIsTotalledAndNoLongerCountsAsUnmeasured()
    {
        var log = new Log();
        log.WithRun("R1", "claude", RoleKind.Worker, minutes: 30, manifestHash: "hash-1",
            turns: 66, outputTokens: 12_000, tokensInUncached: 4_000);
        log.WithRun("R2", "codex", RoleKind.Verifier, minutes: 20, manifestHash: "hash-2",
            outputTokens: 8_000, tokensInUncached: 1_000);

        var report = log.Build();

        Assert.Equal(2, report.Runs.CostRecorded);
        Assert.Equal(2, report.Cost.RunsMeasured);
        Assert.Equal(0, report.Cost.RunsUnmeasured);
        Assert.Equal(20_000, report.Cost.OutputTokens.Total);
        Assert.Equal(5_000, report.Cost.TokensInUncached.Total);
        Assert.DoesNotContain("governanceCost.tokens", report.NotMeasured);
    }

    // Turns is not one unit across providers: claude states num_turns and codex states no turn count
    // at all, so a total over a task both ran describes only part of itself. The count of runs the
    // total came from is what keeps it readable rather than comparable.
    [Fact]
    public void TheTurnTotalCarriesTheCountOfRunsItWasMeasuredFrom()
    {
        var log = new Log();
        log.WithRun("R1", "claude", RoleKind.Worker, minutes: 30, manifestHash: "h1",
            turns: 66, outputTokens: 1);
        log.WithRun("R2", "codex", RoleKind.Verifier, minutes: 30, manifestHash: "h2",
            outputTokens: 1);

        var report = log.Build();

        Assert.Equal(2, report.Cost.RunsMeasured);
        Assert.Equal(1, report.Cost.Turns.RunsMeasured);
        Assert.Equal(66, report.Cost.Turns.Total);
    }

    // VC2: the six cost fields are independent — RunRules:242-247 states it and enforces nothing
    // across them — so one any-field count cannot stand for all of them. It used to: a run that
    // reported output tokens and no input buckets made an unmeasured bucket total look as though it
    // came from every measured run.
    [Fact]
    public void EachCostTotalCarriesTheCountOfTheRunsThatMeasuredThatFieldAndNotAnyField()
    {
        var log = new Log();
        log.WithRun("R1", "claude", RoleKind.Worker, minutes: 30, manifestHash: "h1",
            outputTokens: 12_000);
        log.WithRun("R2", "codex", RoleKind.Verifier, minutes: 20, manifestHash: "h2",
            tokensInCacheRead: 900_000);

        var report = log.Build();

        // Both runs reported some cost, and no total was measured from both of them.
        Assert.Equal(2, report.Cost.RunsMeasured);
        Assert.Equal(1, report.Cost.OutputTokens.RunsMeasured);
        Assert.Equal(12_000, report.Cost.OutputTokens.Total);
        Assert.Equal(1, report.Cost.TokensInCacheRead.RunsMeasured);
        Assert.Equal(900_000, report.Cost.TokensInCacheRead.Total);
        Assert.Equal(0, report.Cost.TokensInUncached.RunsMeasured);
        Assert.Null(report.Cost.TokensInUncached.Total);
        Assert.Equal(0, report.Cost.Turns.RunsMeasured);
        Assert.Null(report.Cost.Turns.Total);
    }

    // VC3: the launcher takes this duration rather than the provider reporting it, so it means the
    // same thing for both — and it was read as evidence that a run's cost was measured and then left
    // out of the output.
    [Fact]
    public void TheDurationToTheFirstLedgerWriteIsReportedWithItsOwnMeasuredFromCount()
    {
        var log = new Log();
        log.WithRun("R1", "claude", RoleKind.Worker, minutes: 30, manifestHash: "h1",
            millisecondsToFirstLedgerWrite: 42_000);
        log.WithRun("R2", "codex", RoleKind.Verifier, minutes: 20, manifestHash: "h2",
            millisecondsToFirstLedgerWrite: 8_000, outputTokens: 5);
        log.WithRun("R3", "codex", RoleKind.Worker, minutes: 20, manifestHash: "h3",
            outputTokens: 5);

        var report = log.Build();

        Assert.Equal(2, report.Cost.MillisecondsToFirstLedgerWrite.RunsMeasured);
        Assert.Equal(50_000, report.Cost.MillisecondsToFirstLedgerWrite.Total);
        // Three runs reported some cost, two reported this duration, and the two populations are
        // stated separately rather than one standing in for the other.
        Assert.Equal(3, report.Cost.RunsMeasured);
        Assert.Equal(2, report.Cost.OutputTokens.RunsMeasured);
    }

    // The same defect from the other side: a run that measured a duration and no tokens made the
    // any-field count non-zero, which took the token entry out of notMeasured while every token
    // total beside it was still null.
    [Fact]
    public void ARunThatMeasuredOnlyItsFirstLedgerWriteStillReportsTheTokenTotalsAsNotMeasured()
    {
        var log = new Log();
        log.WithRun("R1", "claude", RoleKind.Worker, minutes: 30, manifestHash: "h1",
            millisecondsToFirstLedgerWrite: 42_000, turns: 12);

        var report = log.Build();

        Assert.Equal(1, report.Cost.RunsMeasured);
        Assert.Equal(1, report.Cost.MillisecondsToFirstLedgerWrite.RunsMeasured);
        Assert.Null(report.Cost.OutputTokens.Total);
        Assert.Contains("governanceCost.tokens", report.NotMeasured);
    }

    // A single token bucket is enough to make the token total measured, and only the buckets count:
    // turns and the first-write duration are not tokens and must not clear this entry.
    [Fact]
    public void OneMeasuredTokenBucketIsEnoughToTakeTheTokenEntryOutOfNotMeasured()
    {
        var log = new Log();
        log.WithRun("R1", "claude", RoleKind.Worker, minutes: 30, manifestHash: "h1",
            tokensInCacheWrite: 700);

        Assert.DoesNotContain("governanceCost.tokens", log.Build().NotMeasured);
    }

    // C2 and E2: decision.invalidated carries the id of the claim whose rejection caused it and
    // state keeps only the resulting status. A projection built over state alone can say a decision
    // is invalidated and cannot say what invalidated it.
    [Fact]
    public void ARejectedClaimThatInvalidatedADecisionIsJoinedFromTheLogAndNotFromState()
    {
        var log = new Log();
        log.Append(new DecisionInvalidated(new DecisionId("D2"), new ClaimId("C8")));

        var report = log.Build();

        var chain = Assert.Single(report.CausalChains);
        Assert.Equal("rejectedClaimInvalidatedDecision", chain.Kind);
        Assert.Equal("C8", chain.From);
        Assert.Equal("D2", chain.To);
    }

    [Fact]
    public void ARejectedClaimThatInvalidatedAWorkItemCarriesTheResultingStatus()
    {
        var log = new Log();
        log.Append(new WorkItemInvalidated(
            new WorkItemId("W3"), new ClaimId("C8"), WorkItemStatus.Blocked));

        var report = log.Build();

        var chain = Assert.Single(report.CausalChains);
        Assert.Equal("rejectedClaimInvalidatedWorkItem", chain.Kind);
        Assert.Equal("C8", chain.From);
        Assert.Equal("W3", chain.To);
        Assert.Equal("blocked", chain.Detail);
    }

    [Fact]
    public void AChallengeThatOverturnedADecisionIsJoinedFromTheLog()
    {
        var log = new Log();
        log.Append(new DecisionOverturned(new DecisionId("D2"), new ChallengeId("XC1")));

        var report = log.Build();

        var chain = Assert.Single(report.CausalChains);
        Assert.Equal("challengeOverturnedDecision", chain.Kind);
        Assert.Equal("XC1", chain.From);
        Assert.Equal("D2", chain.To);
    }

    // The supersession outcome sits on claim.resolved, not on the repointing event beside it, so it
    // is joined across two events rather than read off one.
    [Fact]
    public void ARepointedSupersessionCarriesTheOutcomeJoinedFromTheResolutionEvent()
    {
        var log = new Log();
        log.Append(new ClaimResolved(
            new ClaimId("C10"), ClaimStatus.Superseded, [], new ClaimId("C21"),
            SupersessionOutcome.Refinement));
        log.Append(new ClaimDependenciesRepointed(new ClaimId("C10"), new ClaimId("C21")));

        var report = log.Build();

        var chain = Assert.Single(report.CausalChains, item =>
            item.Kind == "claimSupersededRepointedDependents");
        Assert.Equal("C10", chain.From);
        Assert.Equal("C21", chain.To);
        Assert.Equal("refinement", chain.Detail);
    }

    // C3: the clearly-supported subset comes from TaskDebt and is never recomputed. A second, looser
    // model of a rule that already exists is the defect KC1 and KC2 both were, and a retrospective
    // that disagreed with `status` about what a task owes would leave neither wrong on its own terms.
    [Fact]
    public void TheOpenClaimsCarryingSupportingEvidenceAgreeWithTheDebtProjection()
    {
        var log = new Log();
        log.WithClaim("C1");
        log.WithClaim("C2");
        // Evidence on both sides. A looser recount that asked only "does anything support this"
        // would include it; the debt projection excludes it because it needs a judgement, and this
        // claim is what makes the two answers differ rather than merely agree.
        log.WithClaim("C3");
        log.WithEvidence("E1", supports: "C1");
        log.WithEvidence("E2", refutes: "C2");
        log.WithEvidence("E3", supports: "C3");
        log.WithEvidence("E4", refutes: "C3");

        var report = log.Build();

        Assert.Equal(
            TaskDebt.Compute(log.State).OpenClaimsWithSupportingEvidence,
            report.Epistemic.OpenClaimsWithSupportingEvidence);
        Assert.Equal(1, report.Epistemic.OpenClaimsWithSupportingEvidence);
        Assert.Equal(3, report.Epistemic.Claims.Open);
    }

    // VC1: the open count was recomputed here while the supported subset beside it was read from the
    // debt projection. Both come from TaskDebt now.
    //
    // This assertion names the shared source instead of a literal, and it is a drift guard rather
    // than a failing-today test: the recomputation it replaced returned the same number, which is
    // what makes the duplication invisible until a change to what `status` calls an open claim moves
    // one of the two. Then this fires. The literal below is here so that the test still says what
    // the number is.
    [Fact]
    public void TheOpenClaimCountComesFromTheDebtProjectionRatherThanASecondCountOfTheSameThing()
    {
        var log = new Log();
        log.WithClaim("C1");
        log.WithClaim("C2");
        log.WithClaim("C3");
        log.WithEvidence("E1", supports: "C1");
        // An open claim that something already refutes. It is the claim a change to what `status`
        // calls open debt would most plausibly move, so it is what gives the assertion above
        // something to catch.
        log.WithEvidence("E2", refutes: "C2");

        var report = log.Build();

        Assert.Equal(TaskDebt.Compute(log.State).OpenClaims, report.Epistemic.Claims.Open);
        Assert.Equal(3, report.Epistemic.Claims.Open);
    }

    [Fact]
    public void EvidenceIsSplitByTheDirectionItPointsRatherThanCountedAsOneTotal()
    {
        var log = new Log();
        log.WithClaim("C1");
        log.WithClaim("C2");
        log.WithEvidence("E1", supports: "C1");
        log.WithEvidence("E2", refutes: "C2");
        log.WithEvidence("E3", supports: "C1", refutes: "C2");
        log.WithEvidence("E4");

        var report = log.Build();

        Assert.Equal(4, report.Epistemic.Evidence.Total);
        Assert.Equal(1, report.Epistemic.Evidence.SupportsOnly);
        Assert.Equal(1, report.Epistemic.Evidence.RefutesOnly);
        Assert.Equal(1, report.Epistemic.Evidence.Both);
        Assert.Equal(1, report.Epistemic.Evidence.Neither);
    }

    // C7: a task whose claims all rest on source reads, with no test run and no live run anywhere,
    // demonstrated nothing empirical. This count is the fact that says so, and this is the shape it
    // has to have for that reading to be possible at all.
    [Fact]
    public void EvidenceIsCountedByHowItWasObtained()
    {
        var log = new Log();
        log.WithEvidence("E1", sourceType: "source-read");
        log.WithEvidence("E2", sourceType: "source-read");
        log.WithEvidence("E3", sourceType: "test-run");

        var report = log.Build();

        Assert.Equal(2, report.Epistemic.Evidence.BySourceType["source-read"]);
        Assert.Equal(1, report.Epistemic.Evidence.BySourceType["test-run"]);

        // No key for a kind this task never recorded. An absent key means the task obtained no
        // evidence that way, which is unambiguous — unlike a null cost field, so this map needs no
        // zeroes to be honest and gets none, for the reason the event count map gets none.
        Assert.DoesNotContain("live-run", report.Epistemic.Evidence.BySourceType.Keys);
    }

    // The map counts the same records as Total, so the two agree in sum whatever the spellings were.
    // A dropped spelling shows up here as the disagreement it is.
    [Fact]
    public void NoSpellingIsDroppedSoTheCountsAgreeWithTheEvidenceTotal()
    {
        var log = new Log();
        var spellings = CorpusSourceTypeSpellings;
        for (var index = 0; index < spellings.Length; index++)
        {
            log.WithEvidence($"E{index + 1}", sourceType: spellings[index]);
        }

        var report = log.Build();

        Assert.Equal(spellings.Length, report.Epistemic.Evidence.Total);
        Assert.Equal(report.Epistemic.Evidence.Total, report.Epistemic.Evidence.BySourceType.Values.Sum());
    }

    // D2: canonicalising the spelling is normalisation and stays a count. Nine of the seventeen
    // spellings E8 found in the corpus name three kinds between them, and the surviving key in each
    // group is a spelling the corpus already uses rather than a name invented here.
    [Theory]
    [InlineData("doc-read", "doc-read")]
    [InlineData("vendor-doc", "doc-read")]
    [InlineData("official-doc", "doc-read")]
    [InlineData("vendor-adjacent-doc", "doc-read")]
    [InlineData("local-probe", "local-probe")]
    [InlineData("live-probe", "local-probe")]
    [InlineData("community-consensus", "community-consensus")]
    [InlineData("community-report", "community-consensus")]
    [InlineData("community-evidence", "community-consensus")]
    [InlineData("source-read", "source-read")]
    [InlineData("test-run", "test-run")]
    [InlineData("live-run", "live-run")]
    public void ASpellingOfAKnownKindIsCountedUnderThatKind(string spelling, string expectedKey)
    {
        var log = new Log();
        log.WithEvidence("E1", sourceType: spelling);

        var report = log.Build();

        Assert.Equal(1, report.Epistemic.Evidence.BySourceType[expectedKey]);
        Assert.Single(report.Epistemic.Evidence.BySourceType);
    }

    // The four kinds that carry almost the whole corpus survive as themselves, and the four
    // documentation spellings arrive as one key rather than four. Both halves in one task, because
    // that is how a real record reads: a mixture, and the reading C7 wants is which kinds are absent.
    [Fact]
    public void TheFourKindsThatCarryTheCorpusStayThemselvesWhileTheDocSpellingsCollapse()
    {
        var log = new Log();
        log.WithEvidence("E1", sourceType: "source-read");
        log.WithEvidence("E2", sourceType: "local-probe");
        log.WithEvidence("E3", sourceType: "test-run");
        log.WithEvidence("E4", sourceType: "live-run");
        log.WithEvidence("E5", sourceType: "vendor-doc");
        log.WithEvidence("E6", sourceType: "official-doc");
        log.WithEvidence("E7", sourceType: "doc-read");
        log.WithEvidence("E8", sourceType: "vendor-adjacent-doc");

        var report = log.Build();

        Assert.Equal(1, report.Epistemic.Evidence.BySourceType["source-read"]);
        Assert.Equal(1, report.Epistemic.Evidence.BySourceType["local-probe"]);
        Assert.Equal(1, report.Epistemic.Evidence.BySourceType["test-run"]);
        Assert.Equal(1, report.Epistemic.Evidence.BySourceType["live-run"]);
        Assert.Equal(4, report.Epistemic.Evidence.BySourceType["doc-read"]);
        Assert.Equal(5, report.Epistemic.Evidence.BySourceType.Count);
    }

    // Case is spelling, so it is folded for the spellings this projection claims to know. The corpus
    // is entirely lower case today (E8), which is why this changes no count and is here anyway: a
    // second spelling of a known kind must not open a second key.
    [Fact]
    public void ASpellingOfAKnownKindIsMatchedWithoutRegardToCase()
    {
        var log = new Log();
        log.WithEvidence("E1", sourceType: "source-read");
        log.WithEvidence("E2", sourceType: "Source-Read");

        var report = log.Build();

        Assert.Equal(2, report.Epistemic.Evidence.BySourceType["source-read"]);
        Assert.Single(report.Epistemic.Evidence.BySourceType);
    }

    // The field is free text, so a spelling this projection does not know is drift in the vocabulary
    // rather than noise, and drift is the data. A bucket named `other` would report which records
    // drifted and hide which way they went; dropping them would break the agreement with Total.
    [Fact]
    public void AnUnrecognisedSpellingSurvivesAsItselfRatherThanBeingBucketedAway()
    {
        var log = new Log();
        log.WithEvidence("E1", sourceType: "command-output");
        log.WithEvidence("E2", sourceType: "operator-statement");
        log.WithEvidence("E3", sourceType: "source-read");

        var report = log.Build();

        Assert.Equal(1, report.Epistemic.Evidence.BySourceType["command-output"]);
        Assert.Equal(1, report.Epistemic.Evidence.BySourceType["operator-statement"]);
        Assert.DoesNotContain("other", report.Epistemic.Evidence.BySourceType.Keys);
        Assert.DoesNotContain("unknown", report.Epistemic.Evidence.BySourceType.Keys);
        Assert.DoesNotContain("unrecorded", report.Epistemic.Evidence.BySourceType.Keys);
        Assert.Equal(3, report.Epistemic.Evidence.BySourceType.Values.Sum());
    }

    // D2 draws the line here: the counts are reported and the kinds are not ranked into tiers of
    // evidential strength. Alphabetical order is the evidence that no ordering was imposed — a map
    // whose keys came out strongest-first would be ALT2's score with the number left off.
    [Fact]
    public void TheSourceTypeCountsAreOrderedAlphabeticallyAndNotByEvidentialStrength()
    {
        var log = new Log();
        var spellings = CorpusSourceTypeSpellings;
        for (var index = 0; index < spellings.Length; index++)
        {
            log.WithEvidence($"E{index + 1}", sourceType: spellings[index]);
        }

        var keys = log.Build().Epistemic.Evidence.BySourceType.Keys.ToArray();

        Assert.Equal(keys.OrderBy(key => key, StringComparer.Ordinal).ToArray(), keys);

        // The operator's own hierarchy puts source and documentation reads first and community
        // consensus fourth (RULES.md, the Evidence Hierarchy). Alphabetically community-consensus
        // comes before both, so this fails the moment that hierarchy is applied here rather than by
        // the reader.
        Assert.True(
            Array.IndexOf(keys, "community-consensus") < Array.IndexOf(keys, "source-read"),
            "The keys are ordered by evidential strength rather than alphabetically.");
    }

    // The seventeen spellings E8 tallied across every state.json under .ailedger/tasks. Held here as
    // one list so a spelling added to the corpus is added in one place.
    private static string[] CorpusSourceTypeSpellings =>
    [
        "source-read", "local-probe", "test-run", "live-run", "command-output", "vendor-doc",
        "doc-read", "official-doc", "live-probe", "vendor-adjacent-doc", "test-attempt",
        "operator-statement", "ledger-state", "cross-task", "community-report", "community-evidence",
        "community-consensus"
    ];

    // An operator's filing run is a real run with no agent behind it. It holds no manifest and ends
    // in the instant it started, so it satisfies both halves of the died-before-briefing test while
    // describing the opposite thing.
    [Fact]
    public void AnOperatorFilingRunIsClassifiedAsOneAndNotAsALaunchThatDied()
    {
        var log = new Log();
        log.WithRun("R2", "none", RoleKind.Operator, minutes: 0);

        var report = log.Build();

        Assert.Equal(1, report.Runs.OperatorFilingRuns);
        Assert.Equal(0, report.Runs.DiedBeforeBriefing);
        Assert.Equal(1, report.Runs.ByProvider["none"]);
    }

    // The duration is load-bearing, not decorative: a null manifest hash also means the run predates
    // that field, so the hash alone cannot tell a launch that failed early from one recorded before
    // delivery was tracked at all.
    [Fact]
    public void ALaunchWithNoManifestThatDiedInsideAMinuteIsClassifiedAsDyingBeforeItsBrief()
    {
        var log = new Log();
        log.WithRun("R1", "codex", RoleKind.Worker, minutes: 0, status: AgentRunStatus.Failed);

        Assert.Equal(1, log.Build().Runs.DiedBeforeBriefing);
    }

    [Fact]
    public void ALongRunWithNoManifestIsNotClassifiedAsDyingBeforeItsBrief()
    {
        var log = new Log();
        log.WithRun("R1", "codex", RoleKind.Worker, minutes: 40, status: AgentRunStatus.Completed);

        var report = log.Build();

        Assert.Equal(0, report.Runs.DiedBeforeBriefing);
        Assert.Equal(0, report.Runs.BriefDelivered);
    }

    // Zero briefs across every run is the state a task recorded before ManifestHash existed is in,
    // and it is not distinguishable from a task whose every launch failed before building one.
    [Fact]
    public void ATaskWhereNoRunDeliveredABriefReportsBriefDeliveryAsNotMeasured()
    {
        var log = new Log();
        log.WithRun("R1", "codex", RoleKind.Worker, minutes: 40);

        Assert.Contains("runs.briefDelivered", log.Build().NotMeasured);
    }

    [Fact]
    public void ATaskWhereABriefReachedTheAdapterDoesNotReportBriefDeliveryAsNotMeasured()
    {
        var log = new Log();
        log.WithRun("R1", "codex", RoleKind.Worker, minutes: 40, manifestHash: "hash-1");

        var report = log.Build();

        Assert.Equal(1, report.Runs.BriefDelivered);
        Assert.DoesNotContain("runs.briefDelivered", report.NotMeasured);
    }

    // A null subject role predates the field rather than meaning no role held the run, so it is
    // keyed as unrecorded and named in notMeasured. Reading the actor's present role instead would
    // answer a different question, as the completion gate records for itself.
    [Fact]
    public void ARunRecordedBeforeSubjectRoleExistedIsKeyedAsUnrecordedAndNamedAsNotMeasured()
    {
        var log = new Log();
        log.WithRun("R1", "codex", role: null, minutes: 40, manifestHash: "hash-1");

        var report = log.Build();

        Assert.Equal(1, report.Runs.BySubjectRole["unrecorded"]);
        Assert.Contains("runs.bySubjectRole", report.NotMeasured);
    }

    // Absent journal and empty journal are two different facts. A task that predates the refusal
    // journal recorded no refusals because nothing was writing them down.
    [Fact]
    public void ATaskWithNoRefusalJournalReportsRefusalsAsNotMeasuredRatherThanAsZero()
    {
        var report = new Log().Build(refusals: null);

        Assert.Null(report.Refusals);
        Assert.Contains("refusals", report.NotMeasured);
    }

    [Fact]
    public void ATaskWithAnEmptyRefusalJournalReportsZeroRefusalsAndNotAnAbsentJournal()
    {
        var report = new Log().Build(refusals: []);

        Assert.NotNull(report.Refusals);
        Assert.Equal(0, report.Refusals!.Total);
        Assert.DoesNotContain("refusals", report.NotMeasured);
    }

    [Fact]
    public void RefusalsAreCountedBySiteActorAndCommand()
    {
        var report = new Log().Build(refusals:
        [
            new RetrospectiveRefusal(new ActorId("operator"), "ResolveClaimCommand", "service"),
            new RetrospectiveRefusal(new ActorId("operator"), "ResolveClaimCommand", "service"),
            new RetrospectiveRefusal(new ActorId("claude-impl"), "AddWorkItemCommand", "provider-launch")
        ]);

        Assert.Equal(3, report.Refusals!.Total);
        Assert.Equal(2, report.Refusals.BySite["service"]);
        Assert.Equal(1, report.Refusals.BySite["provider-launch"]);
        Assert.Equal(2, report.Refusals.ByActor["operator"]);
        Assert.Equal(2, report.Refusals.ByCommand["ResolveClaimCommand"]);
    }

    // A waiver leaves no durable trace in state — the pending waiver is transient and internal,
    // which is why TaskDebt declines to count waivers at all. The log is the only place they survive.
    [Fact]
    public void AStagePrerequisiteWaiverIsCountedFromTheLogWithItsReasonVerbatim()
    {
        var log = new Log();
        log.Append(new StagePrerequisitesWaived(TaskStage.Verification, "Documentation-only change"));
        log.Append(new StageTransitioned(TaskStage.Execution, TaskStage.Verification));

        var report = log.Build();

        Assert.Equal(1, report.Stages.Waivers);
        Assert.Equal(1, report.Stages.Transitions);
        Assert.Equal("Documentation-only change", Assert.Single(report.Stages.WaiverReasons));
    }

    // work.completed carries withoutVerificationReason and the item it produced records only that it
    // is completed, so the waiver is another thing only the log can answer.
    [Fact]
    public void AWaivedCompletionCarriesItsReasonFromTheLog()
    {
        var log = new Log();
        log.WithWorkItem("W1", WorkItemStatus.Completed, scope: ["src", "tests"]);
        log.Append(new WorkItemCompleted(new WorkItemId("W1"), "No verifier run was warranted"));

        var item = Assert.Single(log.Build().WorkItems);

        Assert.Equal("W1", item.Id);
        Assert.Equal(2, item.ScopeCount);
        Assert.Equal("No verifier run was warranted", item.CompletedWithoutVerificationReason);
    }

    // The three verification questions the completion gate itself asks. HasCompletedWorkingRun
    // travels beside VerifierRanAfterLatestWork because the second reads false for an item that has
    // done no work at all, and without the first that is indistinguishable from missing verification.
    [Fact]
    public void AnItemVerifiedByTheProviderThatWroteItIsReportedAsSuchAndNotScored()
    {
        var log = new Log();
        log.WithWorkItem("W1", WorkItemStatus.Active, scope: ["src"]);
        log.WithRun("R1", "claude", RoleKind.Worker, minutes: 30, workItem: "W1", manifestHash: "h1");
        log.WithRun("RV1", "claude", RoleKind.Verifier, minutes: 20, workItem: "W1",
            manifestHash: "h2", startMinutesIn: 40);

        var item = Assert.Single(log.Build().WorkItems);

        Assert.True(item.HasCompletedWorkingRun);
        Assert.True(item.VerifierRanAfterLatestWork);
        Assert.Equal("claude", item.ProviderThatVerifiedItsOwnWork);
    }

    [Fact]
    public void AnItemWithNoWorkAtAllIsDistinguishableFromOneMissingItsVerification()
    {
        var log = new Log();
        log.WithWorkItem("W1", WorkItemStatus.Proposed, scope: ["src"]);

        var item = Assert.Single(log.Build().WorkItems);

        Assert.False(item.HasCompletedWorkingRun);
        Assert.False(item.VerifierRanAfterLatestWork);
    }

    // Only lessons this task inherited. A lesson this task minted at archive is citable from then on,
    // and counting a citation of one as learning from an earlier task is the defect KC4 was.
    [Fact]
    public void ACitationOfAnInheritedLessonIsAChainAndACitationOfAMintedOneIsNot()
    {
        var log = new Log();
        log.WithRecalledLesson();
        log.WithMintedLesson("retro-task:imported:C9");
        log.WithClaim("C1", fromLesson: Inherited);
        log.WithClaim("C2", fromLesson: new LessonId("retro-task:imported:C9"));

        var report = log.Build();

        var chain = Assert.Single(report.CausalChains, item => item.Kind == "lessonCitedByRecord");
        Assert.Equal(Inherited.Value, chain.From);
        Assert.Equal("C1", chain.To);
        Assert.Equal("claim", chain.Detail);
        Assert.Equal(1, report.Lessons.Recalled);
        Assert.Equal(1, report.Lessons.Cited);
        Assert.Equal(1, report.Lessons.Minted);
    }

    // A dimension that returns the same answer for a task that learned and a task that had nothing
    // to learn from is measuring whether anyone typed the flag, not learning.
    [Fact]
    public void ATaskHandedNoLessonsReportsLearningBehaviourAsNotMeasured()
    {
        Assert.Contains("learningBehaviour", new Log().Build().NotMeasured);
    }

    [Fact]
    public void ATaskHandedALessonDoesNotReportLearningBehaviourAsNotMeasured()
    {
        var log = new Log();
        log.WithRecalledLesson();

        Assert.DoesNotContain("learningBehaviour", log.Build().NotMeasured);
    }

    // Two different numbers, and both belong. They differed by 2x on every task the hand run
    // measured, and neither includes the coordinating session.
    [Fact]
    public void WallClockAndAgentMinutesAreReportedSeparatelyAndDoNotAgree()
    {
        var log = new Log();
        log.WithRun("R1", "claude", RoleKind.Worker, minutes: 30, manifestHash: "h1");
        log.WithRun("R2", "codex", RoleKind.Verifier, minutes: 30, manifestHash: "h2",
            startMinutesIn: 30);
        log.Append(new StageTransitioned(TaskStage.Execution, TaskStage.Verification), minutesIn: 180);

        var report = log.Build();

        Assert.Equal(60, report.AgentMinutes);
        Assert.Equal(3.0, report.WallClockHours);
    }

    [Fact]
    public void AnActiveRunContributesNoAgentMinutesAndIsVisibleInTheStatusCounts()
    {
        var log = new Log();
        log.WithRun("R1", "claude", RoleKind.Worker, minutes: 30, manifestHash: "h1");
        log.WithActiveRun("R2", "codex", RoleKind.Verifier);

        var report = log.Build();

        Assert.Equal(30, report.AgentMinutes);
        Assert.Equal(1, report.Runs.ByStatus["active"]);
        Assert.Equal(1, report.Runs.ByStatus["completed"]);
        Assert.Equal(2, report.Runs.Total);
    }

    // An escalation still open has no end, so its duration is null rather than measured against a
    // clock this projection deliberately does not take: a read that answered differently on every
    // invocation could not be compared with itself.
    [Fact]
    public void AnOpenEscalationHasNoMeasuredDurationAndAResolvedOneDoes()
    {
        var log = new Log();
        log.WithEscalation("X1", EscalationStatus.Open, workItem: "W1");
        log.WithEscalation("X2", EscalationStatus.Resolved, workItem: null);
        log.Append(new EscalationResolved(
            new EscalationId("X2"), EscalationStatus.Resolved, "Take the narrow fix",
            new ActorId("operator")), minutesIn: 24);

        var report = log.Build();

        var open = Assert.Single(report.Escalations, escalation => escalation.Id == "X1");
        var resolved = Assert.Single(report.Escalations, escalation => escalation.Id == "X2");
        Assert.Null(open.OpenHours);
        Assert.Equal("W1", open.WorkItem);
        Assert.Equal(0.4, resolved.OpenHours);
        Assert.Null(resolved.WorkItem);
    }

    // The count map names the event types the log names, taken from the discriminators the
    // serializer is registered with rather than from a second table beside them.
    [Fact]
    public void EventsAreCountedUnderTheLogsOwnTypeNames()
    {
        var log = new Log();
        log.WithClaim("C1");
        log.WithClaim("C2");

        var report = log.Build();

        Assert.Equal(1, report.Events["task.opened"]);
        Assert.Equal(1, report.Events["actor.role-assigned"]);
        Assert.Equal(2, report.Events["claim.added"]);
        Assert.DoesNotContain("claim.resolved", report.Events.Keys);
        Assert.Equal(report.Events.Values.Sum(), log.History.Count);
    }

    [Fact]
    public void AuthorshipCountsTheEventsEachActorWroteAndRunsAreCountedBesideIt()
    {
        var log = new Log();
        log.WithRole("claude-impl", RoleKind.Worker);
        log.WithClaim("C1", actor: "claude-impl");
        log.WithClaim("C2", actor: "claude-impl");
        log.WithRun("R1", "claude", RoleKind.Worker, minutes: 30, actor: "claude-impl",
            manifestHash: "h1");

        var report = log.Build();

        // Two claims and the run.started that carries its own actor. The operator wrote the task
        // opening, both role assignments and nothing else — which is the shape the hand run found
        // inverted on every real task, where the coordinating operator wrote most of the record.
        Assert.Equal(3, report.Authorship["claude-impl"]);
        Assert.Equal(3, report.Authorship["operator"]);
        Assert.Equal(1, report.Runs.ByActor["claude-impl"]);
    }

    // ALT2: the moment this projection makes a judgement it becomes a number an agent can move
    // without doing the work, which is worse than no number at all. This walks the whole report
    // record graph, so a field added later under any of these names fails here rather than shipping.
    [Fact]
    public void NothingInTheReportIsAScoreAGradeOrAnOverallNumber()
    {
        string[] judgements = ["score", "grade", "rating", "rank", "overall", "quality", "effectiveness"];

        var offenders = PropertyNames(typeof(TaskRetrospectiveReport), new HashSet<Type>())
            .Where(name => judgements.Any(word =>
                name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static IEnumerable<string> PropertyNames(Type type, ISet<Type> seen)
    {
        if (!seen.Add(type))
        {
            yield break;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            yield return property.Name;

            foreach (var nested in PropertyNames(Element(property.PropertyType), seen))
            {
                yield return nested;
            }
        }
    }

    // Records and collections of records both need walking; anything else in the graph is a leaf.
    private static Type Element(Type type)
    {
        var candidate = Nullable.GetUnderlyingType(type) ?? type;
        if (candidate.IsPrimitive || candidate.IsEnum || candidate == typeof(string))
        {
            return typeof(object);
        }

        if (typeof(IEnumerable).IsAssignableFrom(candidate) && candidate.IsGenericType)
        {
            var arguments = candidate.GetGenericArguments();
            return arguments[^1];
        }

        return candidate;
    }

    // The task's own record, composed for the projection to read.
    //
    // Two routes in, deliberately. Claims, evidence and lessons are driven through the reducer, so
    // the state and the history the projection reads are the ones a real command would have
    // produced. Runs, work items and escalations are composed onto state directly and their causing
    // events appended to the history by hand: a run must begin at its own event timestamp and a
    // legal escalation needs a whole authority ceremony, and what is under test here is the count,
    // not the route. That is the same division TaskDebtTests makes for the same reason.
    private sealed class Log
    {
        private static readonly DateTimeOffset Start = new(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);
        private readonly TaskReducer _reducer = new();
        private readonly ActorId _operator = new("operator");

        private GovernedTaskState? _state;

        public Log()
        {
            Reduce(new TaskOpened("Retrospective", "Goal"), _operator);
            Reduce(new RoleAssigned(new RoleAssignment(
                _operator, RoleKind.Operator, Enum.GetValues<Capability>(),
                new Provenance(_operator, Start, "task.open"))), _operator);
        }

        public GovernedTaskState State => _state!;

        public List<LedgerEvent> History { get; } = [];

        public TaskRetrospectiveReport Build(IReadOnlyList<RetrospectiveRefusal>? refusals = null) =>
            TaskRetrospective.Build(State, History, refusals);

        // An event the projection reads out of the log without the reducer having to accept it.
        public void Append(LedgerEventData data, int minutesIn = 0, string actor = "operator") =>
            History.Add(Envelope(data, new ActorId(actor), minutesIn));

        // Assigned before any claim it is meant to author: a claim added by an actor holding no role
        // is refused on replay, and recall is refused once a second role exists.
        public void WithRole(string actor, RoleKind role) =>
            Reduce(new RoleAssigned(new RoleAssignment(
                new ActorId(actor), role,
                [Capability.AddClaim, Capability.AddEvidence, Capability.BuildContext],
                new Provenance(_operator, Start, "actor.assign-role"))), _operator);

        public void WithClaim(string id, LessonId? fromLesson = null, string actor = "operator") =>
            Reduce(new ClaimAdded(new Claim(
                new ClaimId(id), $"Claim {id}", ClaimStatus.Open, [], null,
                new Provenance(new ActorId(actor), Start, "claim.add"), null, fromLesson)),
                new ActorId(actor));

        // The source type defaults to source-read, which is what the corpus records most (E8), so a
        // test about the direction evidence points does not have to state how it was obtained.
        public void WithEvidence(
            string id,
            string? supports = null,
            string? refutes = null,
            string sourceType = "source-read") =>
            Reduce(new EvidenceAdded(new Evidence(
                new EvidenceId(id), sourceType, $"a.cs:{id}", "summary",
                supports is null ? [] : [new ClaimId(supports)],
                refutes is null ? [] : [new ClaimId(refutes)],
                new Provenance(_operator, Start, "evidence.add"))), _operator);

        public void WithRecalledLesson() =>
            Reduce(new LessonRecalled(Lesson(Inherited, new TaskId("earlier-task"))), _operator);

        // Minting legally requires the Learn stage and a whole close-out, so the lesson is placed on
        // state directly. What is under test is which population it falls in, not the route.
        public void WithMintedLesson(string id)
        {
            var lesson = Lesson(new LessonId(id), Task);
            _state = State with
            {
                Lessons = new Dictionary<LessonId, Lesson>(State.Lessons) { [lesson.Id] = lesson }
            };
        }

        public void WithWorkItem(string id, WorkItemStatus status, IReadOnlyList<string> scope)
        {
            var item = new WorkItem(
                new WorkItemId(id), $"Item {id}", _operator, status, [], scope);
            _state = State with
            {
                WorkItems = new Dictionary<WorkItemId, WorkItem>(State.WorkItems) { [item.Id] = item }
            };
            Append(new WorkItemAdded(item));
        }

        public void WithEscalation(string id, EscalationStatus status, string? workItem)
        {
            var escalation = new Escalation(
                new EscalationId(id), EscalationKind.BusinessDecision, "Narrow fix or rework?",
                status, workItem is null ? null : new WorkItemId(workItem),
                ["Narrow fix", "Rework"], "Narrow fix", [], null, null,
                new Provenance(_operator, Start, "escalation.raise"));
            _state = State with
            {
                Escalations = new Dictionary<EscalationId, Escalation>(State.Escalations)
                {
                    [escalation.Id] = escalation
                }
            };
            Append(new EscalationRaised(escalation));
        }

        public void WithActiveRun(string id, string provider, RoleKind? role) =>
            AddRun(new AgentRun(
                new RunId(id), _operator, null, provider, $"s-{id}", AgentRunStatus.Active,
                Start, null, null, null, null, null, role));

        public void WithRun(
            string id,
            string provider,
            RoleKind? role,
            int minutes,
            string? workItem = null,
            string? manifestHash = null,
            string actor = "operator",
            int startMinutesIn = 0,
            AgentRunStatus status = AgentRunStatus.Completed,
            int? turns = null,
            long? outputTokens = null,
            long? millisecondsToFirstLedgerWrite = null,
            long? tokensInUncached = null,
            long? tokensInCacheWrite = null,
            long? tokensInCacheRead = null)
        {
            var startedAt = Start.AddMinutes(startMinutesIn);
            AddRun(new AgentRun(
                new RunId(id), new ActorId(actor),
                workItem is null ? null : new WorkItemId(workItem),
                provider, $"s-{id}", status, startedAt, startedAt.AddMinutes(minutes),
                null, null, null, null, role, manifestHash,
                manifestHash is null ? null : 1,
                turns, outputTokens, millisecondsToFirstLedgerWrite,
                tokensInUncached, tokensInCacheWrite, tokensInCacheRead));
        }

        private void AddRun(AgentRun run)
        {
            _state = State with
            {
                Runs = new Dictionary<RunId, AgentRun>(State.Runs) { [run.Id] = run }
            };
            Append(new RunStarted(run), (int)(run.StartedAt - Start).TotalMinutes, run.ActorId.Value);
        }

        private static Lesson Lesson(LessonId id, TaskId sourceTask) =>
            new(id, sourceTask, LessonSourceKind.Imported, "C9", "Inherited belief", "Outcome",
                ["adapter.cs:104"], new Provenance(new ActorId("operator"), Start, "lesson.import"),
                null, LessonClass.Refuted, "AILedger", ["adapter"], "grep -n session adapter.cs",
                "Do not drop it", LessonActor.Verifier);

        private void Reduce(LedgerEventData data, ActorId actor)
        {
            var @event = Envelope(data, actor, 0);
            History.Add(@event);
            _state = _reducer.Apply(_state, @event);
        }

        private LedgerEvent Envelope(LedgerEventData data, ActorId actor, int minutesIn) =>
            new(GovernedTaskState.CurrentSchemaVersion,
                new EventId($"{Task.Value}:{History.Count + 1:D10}"),
                Task, actor, Start.AddMinutes(minutesIn), null, "retrospective", data);
    }
}
