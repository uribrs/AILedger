using System.Text.RegularExpressions;

namespace AILedger.Core.Application;

// Which rule refused, as far as the message can say. The first line of the message, with each
// quoted literal in it replaced by one placeholder, so two refusals differing only in the id they
// name key as the one lesson they are. Nothing narrower is available: RefusalRecord carries the
// kernel's text and no rule id, and giving it one would change a record written on a failure path
// by every command in the kernel. The raw message is kept on the row this key produces.
//
// The first line is taken before the substitution, not after. That makes one thing load-bearing
// about every rule sentence this kernel raises: none may open a quoted literal that it closes on a
// later line. Opened and not closed on the first line, the literal does not match, the identifier it
// holds survives into the key, and one rule becomes one key per record — the counter prints nothing
// and the measure drops the rule. Nothing written today does that, and RefusalRuleKeyTests pins the
// behaviour so that the author of the first multi-line rule sentence is told by a test rather than
// by a missing row.
//
// The order is this way round because a refusal may print
// diagnostic lines below its sentence and those lines name records without quoting them. The
// directional claim-resolution refusal is the first that does, and unquoted ids pass through the
// placeholder untouched: keyed on the whole message, its two appended lines differ for every record
// pair, so one rule refusing one actor sixteen times became sixteen keys, sixteen first-time rows
// and no repeat at all. The rule's own sentence is the first line by construction; everything a
// refusal adds below it is about the particular records, which is what the key must not carry.
//
// It lives here, alone and public, because two callers now need one answer and not two readings of
// it: CoordinatorMeasurement's measure 9 groups a finished task's refusals by it, and the kernel's
// own refusal path counts an actor's repetition of one rule by it while the command is still in
// flight. A copy in the second caller would drift from the first the moment either changed,
// nothing would fail, and the count would be wrong in the flattering direction — the same failure
// the normalisation below exists to prevent.
public static class RefusalRuleKey
{
    // An apostrophe opens a quoted literal only where a word does not continue into it. Every id
    // this kernel quotes is opened after a space, a newline or an opening delimiter, and the only
    // apostrophes that follow a letter are possessives in the rule's own prose — "another actor's
    // behalf" at RunDispatchRules.cs:45, "a lesson mark's verify" at LessonMarkRules.cs:202. Without
    // the lookbehind the possessive is read as an opening quote, the pairing then runs one
    // apostrophe out of step, and the ids this key exists to remove survive verbatim: one rule
    // refusing one actor about two subjects becomes two keys, the repetition counter prints nothing,
    // and the measure keeps only keys with a repeat, so the rule leaves the retrospective rather
    // than merely undercounting. Both failures are in the flattering direction and neither is
    // visible from outside. Sized over the code and not over the journal: 27 of the 427
    // GovernanceException messages in this kernel carry a possessive and 2 of them mis-key today
    // (E31). The journal showed one (E27), which is true of the corpus and is the wrong instrument —
    // a corpus holds only what has already fired, and RunDispatchRules.cs:45 never has.
    //
    // This is the last patch to this normalisation. Three findings have landed on it — unquoted ids,
    // a literal spanning a newline, and this one — and what they share is that a rule's identity is
    // being read out of its prose. D13 accepts the line below because the defect is live and silent,
    // and opens the replacement as its own task: a stable rule id emitted at the throw site, which
    // no wording can mislead.
    private static readonly Regex QuotedLiteral =
        new("(?<![A-Za-z])'[^']*'", RegexOptions.CultureInvariant);

    public static string Of(string message) => QuotedLiteral.Replace(FirstLine(message), "'X'");

    // Both line endings, because the kernel builds a multi-line refusal with Environment.NewLine:
    // a journal written on Windows carries a carriage return that a cut at the newline alone would
    // leave on the end of the key, and one rule refused on two platforms would be two keys.
    private static string FirstLine(string message)
    {
        var end = message.IndexOfAny(['\r', '\n']);
        return end < 0 ? message : message[..end];
    }
}
