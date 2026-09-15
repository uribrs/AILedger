namespace AILedger.Core.Contracts;

// What the lesson is about, which is not the same question as what kind of failure it records
// (LessonClass) or which record it was minted from (LessonSourceKind). Domain is a fact about the
// software the task was building. Workflow is a fact about how this kernel and its pipeline behave,
// which is worth carrying to a later task even when that task is in another repository building
// something unrelated. Absent reads as Domain: every lesson minted before this field existed is one.
public enum LessonKind
{
    Domain,
    Workflow
}
