make the plan's named tests a refusal, not a hope

the orchestrator's plan already names a test per attention item, by path and test name:

    | R2 | stale-artifact-satisfies-stage | ... | test:tests/.../StagePrerequisiteTests.cs::R2_SupersededArtifactsDoNotSatisfyStagePrerequisites |

the skill is explicit that the worker delivers it and it is not the verifier's to discover. nothing
checks that it exists.

## what today showed

three implementation runs delivered a behaviour change with no test at all, and the suite stayed
green because the test that would have failed was never written. the variable was not the agent — it
was whether my constraint enumerated the cases. a contract saying "with tests" got none twice; a
contract naming five cases got five, twice.

that is a discipline on the operator, and it is exactly the kind of discipline this kernel exists to
stop depending on.

## the gate

an attention item is a first-class record: an id, a name, a failure mode, and the artifact that
resolves it — `test:<path>::<name>` or `guard:<path>:<line>`.

- `work complete` refuses while any attention item against that item is undisposed
- disposing one as `handled` requires the named artifact to exist. a test id that no file contains
  is a refusal, not a judgement call — the kernel can check a symbol in a file without knowing
  anything about the work
- `accepted-risk` and `not-applicable` need a reason, the way every other cheap path here does
- `unresolved` blocks completion, which is what makes the other three mean something

## why this one and not a lint

the check is mechanical and the kernel is the only place that knows the item is being completed. a
lint would run on a tree; this runs at the moment someone claims the work is done, which is the
moment the claim is worth testing.

it also gives the verifier's Attention Item Disposition table something to be checked against
rather than merely written. today that table is prose the kernel cannot read.

## cost

one record, one command to declare an item, one arm on `work complete`, and a file-and-symbol
existence check. the plan already produces the data in the right shape; nothing consumes it.
