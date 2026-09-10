using System.Text.Json;
using AILedger.Core.Application;
using AILedger.Core.Contracts;

namespace AILedger.Cli;

// What a coordinating session cost, read off the transcript its harness already wrote. Nothing here
// instruments anything: the Claude Code harness writes a per-request usage record to
// `~/.claude/projects/<slug>/<session-id>.jsonl` under `message.usage`, carrying `input_tokens`,
// `output_tokens`, `cache_creation_input_tokens` and `cache_read_input_tokens` with the model on the
// same record (C3, E3). D6 accepts that reading it is the difference between measuring the largest
// cost centre in this repository and leaving it dark on a false claim about the platform.
//
// It lives here and not in Core, and it is reached only through an explicit `--coordinator-transcript`
// path (PD2). Two reasons, and both are about what a projection may do:
//
//   - Core projections consume supplied data and read no file. A retrospective that went looking
//     under a user's home directory would answer differently depending on who invoked it, and a read
//     that cannot be compared with itself is not a measurement.
//   - An explicit path is auditable and sandbox-visible. An ambient one is neither.
//
// Every failure is a named absence and never a zero and never an estimate: a codex-hosted
// coordinator writes no usage record at all, and a zero would read as a coordinator that cost
// nothing. The identity half of the check is deliberately split — this reader reports the harness
// session the transcript claims, and CoordinatorMeasurement compares it against the session the
// report is about, because only the ledger side knows what that session recorded (attention item R4).
//
// Two controls stand between a named path and a token total, and the second exists because the first
// is not sufficient on its own (VC1). The identity check proves only that a file *claims* a harness
// session id, and a file claiming any id with any counts can be written anywhere by anyone; a
// matching transcript copied to a temporary directory was accepted and charged. So the read is
// confined to the directory the harness itself writes to, and a path outside it is refused by the
// containment check before anything is opened. Provenance first, then identity.
//
// What the pair establishes is where the file came from, not who wrote it, and the two together do
// not add up to authenticity (C7). The measurement therefore carries a statement of its own control
// beside the four buckets, so a reader of a coordinator's cost sees what the figure rests on without
// reading this file — ProvenanceStatement below.
internal static class CoordinatorUsageReader
{
    // The harnesses whose transcript this reader knows how to parse. A session opened in anything
    // else reports an absence naming its harness rather than being parsed hopefully.
    private static readonly string[] SupportedHarnesses = ["claude-code", "claude"];

    // Where the Claude Code harness writes its per-request transcripts: one directory per project
    // slug under the user's own configuration directory, holding `<session-id>.jsonl` (C3, E3). The
    // same shape as the adapter's `~/.codex` resolution, and for the same reason — a per-user tool's
    // own directory is where its own record lives.
    public static string DefaultHarnessTranscriptRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    public static async Task<CoordinatorUsageRead> ReadAsync(
        GovernedTaskState state,
        string? sessionOption,
        string? transcriptOption,
        string harnessTranscriptRoot,
        CancellationToken cancellationToken)
    {
        var sessionId = string.IsNullOrWhiteSpace(sessionOption)
            ? (CoordinatorSessionId?)null
            : new CoordinatorSessionId(sessionOption.Trim());
        var path = string.IsNullOrWhiteSpace(transcriptOption) ? null : transcriptOption.Trim();

        if (sessionId is null && path is null)
        {
            // Nothing was asked for, so nothing was read. The projection reports this as an absence
            // that was never looked for, which is distinct from every absence below.
            return new CoordinatorUsageRead(null, null, null);
        }

        if (sessionId is not { } selected)
        {
            return Absent(null,
                "noCoordinatorSessionSelected: a transcript was named with no '--coordinator-session', " +
                "so there is no session to check its identity against.");
        }

        if (!state.CoordinatorSessions.TryGetValue(selected, out var session))
        {
            return Absent(selected, $"coordinatorSessionUnknown: this task holds no session '{selected}'.");
        }

        if (!SupportedHarnesses.Contains(session.Harness, StringComparer.OrdinalIgnoreCase))
        {
            return Absent(selected,
                $"unsupportedHarness: session '{selected}' ran in '{session.Harness}', which writes no " +
                "usage record this reader can parse. Its token cost stays unmeasured rather than " +
                "being estimated.");
        }

        if (path is null)
        {
            return Absent(selected,
                "noTranscriptSupplied: '--coordinator-session' was given with no " +
                "'--coordinator-transcript', so no usage record was read.");
        }

        // Before existence, because provenance is the stronger statement and a refusal here should
        // not depend on whether the named file happens to be there.
        if (Contain(path, harnessTranscriptRoot) is { } refusal)
        {
            return Absent(selected, refusal);
        }

        if (!File.Exists(path))
        {
            return Absent(selected, $"transcriptMissing: no file at '{path}'.");
        }

        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Absent(selected, $"transcriptUnreadable: '{path}' could not be read — {exception.Message}");
        }

        return Summarize(selected, path, harnessTranscriptRoot, lines);
    }

    // What admitted this file, in the report rather than only in this source. A reader of a
    // coordinator's token cost cannot otherwise tell what the figure rests on, and what it rests on
    // is narrower than it looks: the containment check establishes where the file is, and nothing
    // establishes who wrote it (C7). Said in the output because the operator reading a cost figure is
    // not reading this file.
    private static string ProvenanceStatement(string path, string root) =>
        $"harnessDirectoryProvenance: '{path}' was read from '{root}', the directory the harness " +
        "writes its own transcripts to, and the harness session id it carries was checked against " +
        "the one this session recorded. That is provenance of location and not authenticity: anyone " +
        "who can write into that directory can put a file there claiming any harness session id and " +
        "any token counts, or leave a link there to a file elsewhere. These four figures are as " +
        "trustworthy as write access to that directory, and no more.";

    // Summed over the per-request records the transcript carries, in the four buckets AgentRun uses.
    // input_tokens maps to the uncached bucket and not to a total: Anthropic reports it as uncached
    // tokens only, with both cache figures additive on top, which is the mapping RunCostReader holds
    // for the same provider (C6).
    //
    // A record the harness marked as a sidechain is excluded. Those are the harness's own subagents,
    // which are a different cognition from the coordinator this session brackets — and in this
    // repository a dispatched agent is a governed run with its own cost record, so counting a
    // sidechain here would double-count one and mis-attribute the other. The exclusion is counted
    // and not silent, because a file whose every usage row is a sidechain is a file this reader
    // emptied rather than a file the harness left empty.
    //
    // A line this reader cannot parse is counted too, and the count now travels with the total
    // rather than being reported only when nothing at all parsed. Four buckets summed over a file
    // with skipped lines in it are a floor; the ordinary way to get one is to point the reader at
    // the live session's own transcript, which the harness is still appending to.
    //
    // The invariant this holds is one sentence: input this reader cannot read produces a named
    // absence and never a number. Three ways it used to produce a number instead, all fixed here,
    // and all of them the same mistake in a different place — a defect turned into a plausible total
    // rather than into a refusal:
    //
    //   - a token value that was missing, negative, non-integral or out of Int64 range was summed as
    //     zero, so a row the harness never wrote and a row reporting nothing became one figure. All
    //     four counters are present as non-negative integers on every usage row the supported
    //     harness writes, measured over 3,158 rows, so a row missing one is corruption and not a
    //     schema variant (ZC2, ZE2);
    //   - the four additions were unchecked, so a long enough transcript of valid values could wrap
    //     into a smaller or negative total;
    //   - only the first session identity was kept and later rows were summed regardless, so two
    //     transcripts concatenated into one file charged both conversations under the first one's id
    //     — and the identity check on the other side of the boundary passed, because it only ever
    //     saw that first id. No transcript this harness wrote carries two identities across 17
    //     files, so a second one means the file is not one transcript (ZC2, ZE2).
    private static CoordinatorUsageRead Summarize(
        CoordinatorSessionId sessionId,
        string path,
        string harnessRoot,
        IReadOnlyList<string> lines)
    {
        long uncached = 0, output = 0, cacheWrite = 0, cacheRead = 0;
        var records = 0;
        var unreadable = 0;
        var sidechains = 0;
        string? model = null;
        string? harnessSessionId = null;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                unreadable++;
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                // The identity is a property of the file and not of a row, so it is checked on every
                // row that states one — before the sidechain skip, because a concatenation is a fact
                // about the file whichever kind of row reveals it. Many rows state none at all, and
                // those are passed over rather than read as a disagreement.
                if ((Text(root, "sessionId") ?? Text(root, "session_id")) is { } rowSession)
                {
                    if (harnessSessionId is null)
                    {
                        harnessSessionId = rowSession;
                    }
                    else if (!string.Equals(harnessSessionId, rowSession, StringComparison.Ordinal))
                    {
                        return Absent(sessionId,
                            $"transcriptCarriesTwoSessionIdentities: '{path}' states harness session " +
                            $"'{harnessSessionId}' on an earlier row and '{rowSession}' on a later one, so it " +
                            "is not one conversation's transcript. Summing it would charge two " +
                            "conversations to whichever identity happened to appear first. The whole read " +
                            "is refused rather than partly reported.");
                    }
                }

                var message = Property(root, "message");
                var usage = Property(message, "usage");
                if (Flag(root, "isSidechain"))
                {
                    // Counted rather than merely skipped: a transcript whose usage rows are all
                    // sidechains holds per-request records and this reader excluded every one of
                    // them, which is a different fact from a file that holds none.
                    if (usage is not null)
                    {
                        sidechains++;
                    }

                    continue;
                }

                if (usage is null)
                {
                    continue;
                }

                if (Tokens(usage, "input_tokens") is not { } input ||
                    Tokens(usage, "output_tokens") is not { } produced ||
                    Tokens(usage, "cache_creation_input_tokens") is not { } written ||
                    Tokens(usage, "cache_read_input_tokens") is not { } cached)
                {
                    return Absent(sessionId,
                        $"transcriptTokenValueUnreadable: a usage record in '{path}' carries a token count " +
                        "that is absent, negative, non-integral or outside the range of the bucket it " +
                        "belongs in. Every usage record this harness writes carries all four as " +
                        "non-negative integers, so this row is not one of them. Its token cost stays " +
                        "unmeasured rather than being reported with that row summed as zero.");
                }

                try
                {
                    checked
                    {
                        uncached += input;
                        output += produced;
                        cacheWrite += written;
                        cacheRead += cached;
                    }
                }
                catch (OverflowException)
                {
                    return Absent(sessionId,
                        $"transcriptTokenTotalOverflowed: summing the usage records in '{path}' left the " +
                        "range of the four buckets, so no total can be reported. A wrapped total would be " +
                        "smaller than the figure it came from, or negative, and would read as a measurement.");
                }

                records++;
                model ??= Text(message, "model");
            }
        }

        // The file name is the harness's session id in this transcript layout, and it is the fallback
        // when no row stated one. A transcript that names its session nowhere reports none, and the
        // projection refuses to charge it — an unidentifiable transcript is exactly the shape a
        // mis-attributed one takes.
        harnessSessionId ??= Path.GetFileNameWithoutExtension(path);

        if (records == 0)
        {
            // Two different facts, and the wrong one used to be reported for both. A file whose
            // every usage row is a sidechain does hold per-request usage records; this reader
            // excluded them, and a reader told the file holds none stops looking.
            return Absent(sessionId, sidechains > 0
                ? $"transcriptHoldsOnlySidechainUsage: every one of the {sidechains} per-request " +
                  $"usage record(s) in '{path}' is marked as a harness sidechain, so this reader " +
                  "excluded them all — they are the harness's own subagents and a different " +
                  "cognition from the coordinator this session brackets" +
                  (unreadable > 0 ? $", and {unreadable} row(s) could not be parsed." : ".") +
                  " Its token cost stays unmeasured rather than being reported as zero."
                : $"transcriptCarriesNoUsage: '{path}' holds no per-request usage record" +
                  (unreadable > 0 ? $" and {unreadable} row(s) could not be parsed." : ".") +
                  " Its token cost stays unmeasured rather than being reported as zero.");
        }

        return new CoordinatorUsageRead(
            sessionId,
            new CoordinatorUsageRecord(
                path, model, harnessSessionId, uncached, output, cacheWrite, cacheRead, records,
                ProvenanceStatement(path, harnessRoot),
                // Carried rather than discarded once a row parsed. The four buckets are a floor
                // whenever this is above zero, and the case that produces it is the ordinary one:
                // a live transcript the harness is still appending to ends in a half-written line.
                unreadable),
            null);
    }

    private static CoordinatorUsageRead Absent(CoordinatorSessionId? sessionId, string reason) =>
        new(sessionId, null, reason);

    // The containment control: null when the named path lies inside the directory the harness writes
    // to, and the absence reason naming this control when it does not (VC1).
    //
    // What is checked. The path the caller named, resolved in full, must lie under the directory the
    // harness itself writes to. That is the whole of it, and it is checked because the identity check
    // looks like it already covers this and does not: a transcript is telemetry from outside the
    // ledger, anyone able to write a file can write one claiming a matching harness session id and
    // any four token totals they like, and the identity check would pass it. Without this control the
    // largest cost figure in the report is a number the operator sets by choosing a path (VC1).
    //
    // What is not checked, and this is a boundary rather than an oversight. The path is compared
    // lexically after normalisation, so links along it are not resolved: a symlink planted inside the
    // harness directory pointing at a file outside it is followed and read. That is deliberate, and
    // RALT3 records the reasoning that C7 and E7 later validated — planting such a link needs write
    // access to the harness directory, and that same access writes a fabricated transcript into the
    // directory directly, under any session id and any token counts. Resolving links would close one
    // of two equally open doors while making the control read as authenticity, which it does not
    // establish and cannot: no check on this side of the boundary recovers from write access to a
    // user's own harness directory.
    //
    // So what this control proves is provenance of location — the file is where the harness writes —
    // and the measurement says so in its own output rather than leaving a reader to infer it from
    // here. See ProvenanceStatement above.
    private static string? Contain(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return "harnessDirectoryUnknown: this process resolved no harness transcript directory, " +
                   "so the harness-directory containment check has nothing to confine the read to and " +
                   $"refuses '{path}'.";
        }

        string full;
        string boundary;
        try
        {
            full = Path.GetFullPath(path);
            boundary = Path.GetFullPath(root);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return $"transcriptOutsideHarnessDirectory: '{path}' could not be resolved to a full path, " +
                   "so the harness-directory containment check cannot place it under " +
                   $"'{root}' and refuses it — {exception.Message}";
        }

        if (!Path.EndsInDirectorySeparator(boundary))
        {
            boundary += Path.DirectorySeparatorChar;
        }

        // Ordinal off Windows, where two paths differing in case are two paths. A path the operator
        // typed in the wrong case is refused rather than widened into a match, which is the safe
        // direction for a control: the refusal names itself and the path is retypable.
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return full.StartsWith(boundary, comparison)
            ? null
            : $"transcriptOutsideHarnessDirectory: '{full}' is not under '{boundary}', the directory " +
              "the harness writes its transcripts to, so the harness-directory containment check " +
              "refused it before the file was opened. The identity check does not stand in for this: " +
              "it proves only that a file claims a harness session id, and a file claiming any id " +
              "with any token counts can be written anywhere. Reported as an absence rather than as " +
              "a total.";
    }

    // JsonElement.TryGetProperty throws unless the element is an object, and a transcript row is one
    // only by the harness's convention. Every read here goes through this, for the reason
    // RunCostReader gives: a read over telemetry must not be the thing that throws.
    private static JsonElement? Property(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } owner && owner.TryGetProperty(name, out var value)
            ? value
            : null;

    private static string? Text(JsonElement? element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static bool Flag(JsonElement? element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.True };

    // One token counter, or null when this reader cannot read it. Zero is a measurement — a request
    // that read nothing from cache reported zero cache reads — and an absent, negative, non-integral
    // or out-of-range value is not, so the two are different returns rather than both being zero.
    // The caller refuses the whole read on null; that is the only honest thing to do with a row whose
    // counters are not counters.
    private static long? Tokens(JsonElement? element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Number } value &&
        value.TryGetInt64(out var count) && count >= 0
            ? count
            : null;
}
