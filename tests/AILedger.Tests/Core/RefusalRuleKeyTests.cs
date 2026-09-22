using AILedger.Core.Application;

namespace AILedger.Tests.Core;

// Which rule refused, read out of the rule's own prose. Two callers now depend on one answer — the
// kernel's repetition counter while a command is still in flight, and measure 9 after the task is
// over — so the key is tested here on its own rather than only through either of them.
//
// Every message below is quoted from the throw site named beside it. A key test written against an
// invented message pins the invention, and the defect this file was opened for was a real message
// that no test had ever been given.
public sealed class RefusalRuleKeyTests
{
    // An apostrophe inside a word is a possessive, not an opening quote. Read as one, the pairing
    // runs a quote out of step and the ids this key exists to remove survive it — so one rule
    // refusing one actor about two different subjects keys as two rules, the counter prints nothing,
    // and the measure keeps only keys with a repeat, so the rule leaves the retrospective rather
    // than merely undercounting. Both messages here carry a possessive ahead of a quoted id.
    [Fact]
    public void APossessiveInTheRulesOwnProseIsNotReadAsAnOpeningQuote()
    {
        // src/AILedger.Core/Runs/Logic/RunDispatchRules.cs:45
        const string dispatchForAlice =
            "Only an operator can start a run on another actor's behalf; 'worker' cannot " +
            "dispatch for 'alice'.";
        const string dispatchForBob =
            "Only an operator can start a run on another actor's behalf; 'worker' cannot " +
            "dispatch for 'bob'.";
        // src/AILedger.Core/Lessons/Logic/LessonMarkRules.cs:202
        const string lessonVerify =
            "A lesson mark's verify must be a runnable command, or 'none' followed by " +
            "a dash or colon and a reason.";

        Assert.Equal(
            "Only an operator can start a run on another actor's behalf; 'X' cannot dispatch for 'X'.",
            RefusalRuleKey.Of(dispatchForAlice));
        Assert.Equal(
            "A lesson mark's verify must be a runnable command, or 'X' followed by a dash or colon " +
            "and a reason.",
            RefusalRuleKey.Of(lessonVerify));
        // The consequence, asserted apart from the two strings above: one rule refusing one actor
        // about two subjects is one rule.
        Assert.Equal(RefusalRuleKey.Of(dispatchForAlice), RefusalRuleKey.Of(dispatchForBob));
    }

    // And the ordinary shape is untouched. Nearly every refusal this kernel raises quotes its ids
    // after a space or an opening delimiter, so the lookbehind that skips a possessive must reach
    // none of them, and the messages below are the shapes that must not move.
    //
    // Sized over the code and not over the journal. The journal showed one key changing, and that is
    // a true fact about the corpus and the wrong instrument for this defect: a corpus shows only what
    // has already fired, and a latent defect has by definition not fired. Measured over the source
    // instead, 27 of the 427 GovernanceException messages in src/ carry a possessive and 2 of them
    // mis-key today — LessonMarkRules.cs:202, which the corpus did see, and RunDispatchRules.cs:45,
    // which it could not, because that rule has never been raised (E31). Both are pinned above.
    //
    // The last message here is the one that holds both shapes at once — a quoted id, and a
    // possessive further along the same sentence that must stay where it is.
    [Fact]
    public void AnOrdinaryQuotedIdKeysExactlyAsItDidBefore()
    {
        // src/AILedger.Core/Domain/AuthorizationPolicy.cs:12
        Assert.Equal(
            "Actor 'X' has no assigned role.",
            RefusalRuleKey.Of("Actor 'stranger' has no assigned role."));
        // src/AILedger.Core/WorkItems/Lifecycle/WorkItemLifecycleRules.cs:253
        Assert.Equal(
            "Work item 'X' has no completed verifier run and cannot be completed. An operator may " +
            "complete it without one by recording why.",
            RefusalRuleKey.Of(
                "Work item 'W1' has no completed verifier run and cannot be completed. An operator " +
                "may complete it without one by recording why."));
        // src/AILedger.Core/WorkItems/Lifecycle/WorkItemLifecycleRules.cs:245
        Assert.Equal(
            "Work item 'X' has no completed run by a working role and cannot be completed. A Worker " +
            "or Researcher run does the work; a coordinating role's run does not. An operator may " +
            "complete it without one by recording why.",
            RefusalRuleKey.Of(
                "Work item 'W4' has no completed run by a working role and cannot be completed. A " +
                "Worker or Researcher run does the work; a coordinating role's run does not. An " +
                "operator may complete it without one by recording why."));
    }

    // The first line is cut before the literals are substituted, and that order is what makes one
    // invariant load-bearing: no rule sentence may open a quoted literal that it closes on a later
    // line. The cut removes the closing quote, the opened literal then matches nothing, and the
    // identifier it was holding survives into the key — so one rule refusing about two records keys
    // as two rules, which is the exact failure the substitution exists to prevent, reached by the one
    // route it cannot cover.
    //
    // Nothing in the kernel writes such a sentence today: the only multi-line refusals are the
    // directional diagnostic and the repetition counter, and both put their newline outside any
    // literal. This is here so the invariant is stated by a test rather than held by accident, and so
    // that the author of the first multi-line rule sentence finds out from a red case. The two
    // messages differ only in the record each names, which is what makes the second assertion the
    // consequence and not a restatement of the first.
    [Fact]
    public void ALiteralOpenedOnTheFirstLineAndClosedBelowItIsNotSubstituted()
    {
        var namingW1 =
            "A rule sentence that opens a literal on 'W1" + Environment.NewLine +
            "and closes it here' instead.";
        var namingW2 =
            "A rule sentence that opens a literal on 'W2" + Environment.NewLine +
            "and closes it here' instead.";

        Assert.Equal("A rule sentence that opens a literal on 'W1", RefusalRuleKey.Of(namingW1));
        Assert.NotEqual(RefusalRuleKey.Of(namingW1), RefusalRuleKey.Of(namingW2));
    }
}
