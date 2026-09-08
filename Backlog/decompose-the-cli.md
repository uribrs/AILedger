`CliApplication` is where the god object went

`CommandHandler` was decomposed into sixteen named rule units and is now 234 lines. the pressure did
not go away; it moved. `src/AILedger.Cli/CliApplication.cs` is 1,467 lines and dispatches 35
commands from one switch statement.

    case "task open":        case "claim add":         case "work complete":
    case "who":              case "claim resolve":     case "work block":
    case "audit":            case "evidence add":      case "work unblock":
    ...

it is now the second-largest file in the repository, behind `TaskTransitionValidator`, and the two
of them are the whole of what is left to decompose.

## why it is worth doing and worth doing second

lower stakes than the validator. the CLI holds no governance rule — it parses arguments, builds a
command, and prints. a mistake here is a broken flag, not a task that cannot be replayed. so it is
the safe one, and it should be scheduled *after* `mirror-the-replay-validator`, not before: the
validator is the one where the file size is actively hiding a correctness invariant.

## the scope argument, corrected

my first draft of this entry claimed the split would let two agents work the CLI concurrently, and
that this needed subdirectories. both halves were wrong.

`ScopeOccupancyRules.PathsOverlap` compares paths as text: a file inside a held directory overlaps,
a directory containing a held file overlaps, and **two files in one directory do not**. so flat
files in one folder are already distinct scopes as far as the kernel is concerned. no subdirectory
layout is needed for it.

what blocks it is not the layout and not the kernel. it is `CliApplication.cs:1055`:

    Directory.Exists(scope) ? scope : ResolveExistingDirectory(Path.GetDirectoryName(scope)!)

every `--scope` that names a file is silently widened to its parent directory. the comment on
`ScopeOccupancyRules` says so outright — D1 accepted a scope that may name a file, and D1 is
unlanded until that line is lifted.

so there are two separable pieces here, and the smaller one is worth more:

- **lift the widening** so `--scope src/AILedger.Cli/CliApplication.cs` means the file. one line,
  no decomposition required, and it lands an already-accepted decision.
- **split the file**, which then makes the per-file scopes meaningful rather than one enormous one.

the first is a change to the CLI's own guard and can ship on its own. the second is tidying that
becomes useful once the first exists.

## the shape

group by the noun, matching the rule units and the command grammar that already exists:

    Commands/TaskCommands.cs        task open, status, history, who, audit
    Commands/ClaimCommands.cs       claim add, claim resolve, evidence add
    Commands/DecisionCommands.cs    decision propose/resolve, alternative record
    Commands/WorkCommands.cs        work add/complete/block/unblock/abandon
    Commands/RunCommands.cs         run start/complete, provider launch/resume
    Commands/StageCommands.cs       stage transition
    Commands/ArtifactCommands.cs    artifact record/show/list
    Commands/LessonCommands.cs      lesson mark, lessons
    Commands/GovernanceCommands.cs  constraint, challenge, escalation

the switch stays, one line per case, delegating. `CommandLine.cs`, `RoleDefaults.cs`,
`ExecutableResolver.cs` and `CognitiveArtifactLoader.cs` already sit outside and stay where they are.

## the rule it must not break

nothing about the two-copy discipline applies here, and that is the point of the entry — this is
ordinary tidying with a scope payoff, not a change to how the kernel decides anything. if a
decomposition of the CLI starts moving a validation, the validation was in the wrong layer and that
is a separate finding.

## cost

mechanical. one file split by an existing grouping, no behaviour change, and
`CliApplicationTests.cs` (1,843 lines) already covers the surface, so the suite is the proof.
