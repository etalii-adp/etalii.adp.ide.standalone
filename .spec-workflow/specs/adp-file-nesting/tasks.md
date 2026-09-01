# Tasks Document

> **Phase B lands before Phase C, and that is the single most important thing in this document.** The example registrations are renamed in Phase C. The tests that prove the rename preserved their meaning are written in Phase B, **against the current names**, before any file moves. A test written afterwards asserts that the rename did what it did; a test written before asserts that the rename did not change what these files mean. The phases are separated for that reason alone, and doing them in the other order produces a green suite that guarantees nothing.
>
> **26 of the 39 tracked registrations are opened by no test today.** Only c4's two example sets have coverage; azure-pipeline, wardley-map and ansible-structure have none. So the safety net in Phase B is closing a gap wider than the rename it protects.
>
> **Requirement 8.4 has no coverage anywhere and cannot get it from an example.** Every one of the 39 registrations names a MIME that is in the catalog - measured across all of them, not sampled. The unknown-type branch needs a fixture created for it (task 6), not an example found for it.
>
> **`code-level.adp` is not that fixture.** It declares `c4/code`, which *is* in the catalog - the implemented c4 module declares it with no `Extension` - so it resolves a definition and fails the `HasDocumentSibling` guard instead. Same end state, different branch. Task 7 tests the branch it actually exercises. An earlier draft of the design got this wrong, and the wrong version would have left 8.4 uncovered while looking covered.
>
> **One question stays a question.** The design raises whether a known type that keeps no body deserves its own requirement. That is the reviewer's call, and **no task below answers it**. If implementing makes the answer obvious, that is a finding to report rather than a licence to add a criterion.
>
> **Requirement 11.2 protects users, not this repository.** Renaming the example sets is an authoring act performed once in a commit. Nothing in these tasks may produce a code path that renames a user's registrations except the rename command a user invokes.
>
> **The .NET path facts are the corrected ones.** `Path.GetExtension(".adp")` returns `.adp` and `Path.GetFileNameWithoutExtension(".adp")` returns the empty string - the opposite of what Requirement 4.3 originally stated, measured and since corrected in the requirements. Extension-based reasoning about a bare `.adp` is therefore safe; **name**-based reasoning is the hazard, because the base name is empty. Task 3 pins both values so the correction cannot decay back.
>
> **Sequencing with `modular-text-editors`.** That spec's integration tests exercise the same c4 reference example this spec renames. Fixtures on both sides are written against whatever names `develop` holds at implementation time; neither spec assumes a landing order, and neither blocks the other.
>
> **Gates.** Backend: `dotnet test --solution EtAlii.Adp.slnx` and `dotnet format style --verify-no-changes --severity info`, both checked **by exit code**, since a zero-test run exits 5 while printing no failures. Any group touching `src/client` also gates on `npm test` and `npm run typecheck`.
>
> **Worktree.** One dedicated worktree with a SHORT name - `.claude/worktrees/nest/`. A long name pushes the deepest project paths past `MAX_PATH`, and the symptom is `Zero tests ran` rather than a build failure.
>
> **Recent steering rules apply.** An agent commits under its own name; a commit pathspec is no broader than the change itself; `_Model/` for POCOs, one entity per file; Serilog through a `private static readonly ILogger` per class; every test carries `// Arrange.` / `// Act.` / `// Assert.`.
>
> **Bugs.** Per CLAUDE.md, any bug found leaves a guard behind - a test where one can express it, an entry in `tests.md` where only a running app can.

## Phase A — naming and derivation

- [x] 1. `DiagramRegistrationName`
  - File: `src/backend/EtAlii.Adp.Backend/Hierarchy/_Model/DiagramRegistrationName.cs` (new)
  - Parse and compose the three forms: unqualified, qualified, and the folder-scoped bare extension
  - Purpose: Requirement 1's convention, held by one type instead of spread across callers
  - _Leverage: `DiagramFileName.Sanitise`, so a qualifier is validated exactly as a base name already is_
  - _Requirements: 1.1, 1.2, 1.3, 1.4_
  - _Prompt: Implement the task for spec adp-file-nesting, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Model the registration naming convention | Restrictions: a folder-scoped name is excluded from sibling derivation by construction rather than by a check every caller remembers (Requirement 4.4); a qualifier goes through the existing sanitiser, not a second one | Success: every form parses and composes back to itself, and a qualifier containing a separator is refused. Mark in progress, log when done, then mark complete._

- [x] 2. Body derivation, and the ambiguity
  - File: `src/backend/EtAlii.Adp.Backend/Hierarchy/DiagramFilePair.cs` (modify)
  - `SiblingPathFor` gains the qualified case; the ordered rule from the design; the ambiguity reported alongside the resolved path
  - Purpose: Requirement 2 - the sharpest technical problem in the spec
  - _Requirements: 2.1, 2.2, 2.3, 2.5_
  - _Prompt: Implement the task for spec adp-file-nesting, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Make derivation survive the qualified name | Restrictions: the order is header, then unqualified, then qualified, then longest-match with a diagnostic - the header stays authoritative per Requirement 2.3; `ResolveWithin`'s path-escape guard is preserved unchanged, because a body header is user-editable text and following it outside the project root would turn a registration into an arbitrary-file read | Success: `test.first.adp` resolves to `test.dsl`, `test.adp` resolves exactly as it does today, and the `my.config.dsl` ambiguity is covered in BOTH directions - with the longer file present and absent. Mark in progress, log when done, then mark complete._

- [x] 3. Pin the corrected .NET path behaviour
  - File: `src/backend/EtAlii.Adp.Backend.Tests/Unit Tests/Hierarchy/DiagramFilePair.Tests.cs` (modify)
  - Assert both measured values, and audit the call sites that reason about a name rather than an extension
  - Purpose: Requirement 4.3, which asks for confirmation by test rather than by assertion - and whose original assertion was wrong
  - _Requirements: 4.3, 4.4_
  - _Prompt: Implement the task for spec adp-file-nesting, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Pin the path behaviour and audit what depends on it | Restrictions: assert the MEASURED values - `Path.GetExtension(".adp")` is `.adp` and `Path.GetFileNameWithoutExtension(".adp")` is empty - not the ones the requirement originally stated; the audit follows the corrected fact, so it looks for name-based reasoning rather than extension-based | Success: both values are pinned, and `StripExtension(".adp")` producing an empty base name is guarded per Requirement 4.4. Mark in progress, log when done, then mark complete._

- [x] 4. Ownership for the many-to-one case
  - File: `src/backend/EtAlii.Adp.Backend/Hierarchy/DiagramFilePair.cs` (modify)
  - _Requirements: 2.4, 6.2_
  - _Prompt: Implement the task for spec adp-file-nesting, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Restate ownership for several registrations over one subject | Restrictions: the rule is that a body is only ever carried by an operation on the SUBJECT, never by one on a registration - that is the protection `IsOwned` gives today, expressed for a set rather than a pair | Success: deleting one registration removes neither the subject nor a body another registration references. Mark in progress, log when done, then mark complete._

## Phase B — the safety net, written against the CURRENT names

> Nothing in this phase renames anything. Every test here runs green against `develop` as it stands before Phase C begins, and that is how it is verified: **run Phase B to green, commit it, and only then start Phase C.**

- [x] 5. `ExampleRegistrationTests` over every tracked example
  - File: `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/ExampleRegistration.Tests.cs` (new)
  - One theory walking every `.adp` under `src/diagrams/*/examples/**`: each resolves to a definition in the catalog, and a registration with a body resolves to a file that exists
  - Purpose: 26 of 39 registrations currently have no test opening them
  - _Requirements: 11.1, 12.3_
  - _Prompt: Implement the task for spec adp-file-nesting, first run spec-workflow-guide to get the workflow guide then implement the task: Role: QA-minded C# developer | Task: Build the safety net against the current example names | Restrictions: walk the tree rather than hard-coding a list, so an example set added by a later spec is covered on arrival; this task renames NOTHING and must pass on develop as it stands before any rename exists | Success: all 39 registrations are exercised and the suite is green before Phase C starts. Mark in progress, log when done, then mark complete._

- [x] 6. A fixture for the unknown-MIME branch
  - File: `src/backend/EtAlii.Adp.Backend.Tests/Fixtures/` (new)
  - A registration whose first line names no definition in the catalog
  - Purpose: Requirement 8.4, which has no coverage anywhere and cannot get it from an example
  - _Requirements: 8.4_
  - _Prompt: Implement the task for spec adp-file-nesting, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Create the unknown-type fixture and test the branch | Restrictions: this must be a CREATED fixture - all 39 example registrations name a MIME that is in the catalog, measured across every one, so no example exercises this branch; an unknown type resolves to no definition and therefore no body, and is NOT an error | Success: the unknown-MIME branch has coverage that does not depend on any example. Mark in progress, log when done, then mark complete._

- [x] 7. The bodyless-known-type regression
  - File: `src/backend/EtAlii.Adp.Backend.Tests/Unit Tests/Hierarchy/DiagramFilePair.Tests.cs` (modify)
  - `code-level.adp` and `c4/code`: a definition IS found, and `HasDocumentSibling` is false
  - Purpose: pin the branch this case actually exercises, distinct from task 6's
  - _Requirements: 8.4 (by contrast), 11.1_
  - _Prompt: Implement the task for spec adp-file-nesting, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Test the known-type-with-no-extension branch | Restrictions: assert that a definition IS resolved and that `HasDocumentSibling` is what returns null - a test asserting only "no body" would pass for the unknown-type case too and prove nothing about which branch ran; do NOT decide whether this case deserves its own requirement, which is a question the design raised for the reviewer | Success: the two branches are distinguished by assertion, not by end state. Mark in progress, log when done, then mark complete._

## Phase C — the example rename

> Begins only once Phase B is green and committed.

- [x] 8. Rename the c4 example registrations
  - File: `src/diagrams/c4/examples/reference/architecture/`, `src/diagrams/c4/examples/industrial-plant/architecture/` (renamed)
  - The five header-pointed registrations gain qualifiers; `courier.adp` keeps its name as the unqualified default; `code-level.adp` is untouched
  - _Requirements: 1.1, 1.2, 11.2, 11.3_
  - _Prompt: Implement the task for spec adp-file-nesting, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer | Task: Rename the c4 example registrations into the qualified form | Restrictions: the `body:` headers STAY - Requirement 2.3 makes them authoritative and this is where they do real work, so replacing a fact with an inference is a downgrade even where the inference is currently right; `code-level.adp` is not renamed and not touched; use `git mv` so the rename is visible as a rename | Success: task 5's suite is still green afterwards, and a task asserts the five headers are still present and still authoritative. Mark in progress, log when done, then mark complete._

- [x] 9. Confirm the remaining example sets
  - File: `src/diagrams/azure-pipeline/examples/`, `wardley-map/examples/`, `ansible-structure/examples/` (reviewed)
  - _Requirements: 11.1, 11.2_
  - _Prompt: Implement the task for spec adp-file-nesting, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer | Task: Check the other three sets against the convention | Restrictions: these are all single-registration subjects, which Requirement 1.2 keeps unchanged - so the expected outcome is that nothing is renamed, and confirming that is the task; the ansible example keeps `structure.adp` rather than adopting the bare form, because a dot-prefixed file is hidden on Unix-like systems and an example a reader cannot see is a poor example | Success: no file is renamed in these three sets, and the reason is recorded. Mark in progress, log when done, then mark complete._

## Phase D — the hierarchy

- [x] 10. `HierarchyNesting`
  - File: `src/backend/EtAlii.Adp.Backend/Hierarchy/HierarchyNesting.cs` (new)
  - Pure function: which entries are registrations, which subject each names, which are orphans
  - _Requirements: 3.2, 3.4, 3.5, 8.1, 8.2_
  - _Prompt: Implement the task for spec adp-file-nesting, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Assign parents without touching the filesystem | Restrictions: a registration appears under its subject and NOWHERE else (Requirement 3.5) - nesting relocates it rather than duplicating it; an orphan appears where it sits on disk rather than being hidden; the ordering is stable and stated, with the unqualified form first | Success: the `templates.adp` / `templates.yml` / `templates/` case assigns to the FILE rather than the directory, which is the case a name-based implementation gets wrong. Mark in progress, log when done, then mark complete._

- [x] 11. The two additive proto changes, and the client stubs
  - File: `src/api/hierarchy.proto` (modify), `src/client/src/generated/` (regenerated)
  - Correct `has_children`'s comment; add `EntryUpdated.parent_id`; run `npm run generate`
  - _Requirements: 3.1, 10.3_
  - _Prompt: Implement the task for spec adp-file-nesting, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer | Task: Make the contract changes and regenerate | Restrictions: both changes are additive - no field removed, no number reused, no enum value added - so an older client keeps working (Requirement 11.4); `EntryUpdated.parent_id` carries a re-parent, because remove-then-create would discard entry identity and any selection a client held on it; regenerating the stubs is part of THIS task, since a stale generated file fails silently | Success: `npm run typecheck` passes against the regenerated stubs. Mark in progress, log when done, then mark complete._

- [x] 12. `HierarchyModel`: nesting, and the watch messages
  - File: `src/backend/EtAlii.Adp.Backend/Hierarchy/HierarchyModel.cs` (modify)
  - _Requirements: 3.1, 10.1, 10.2, 10.3, 10.4_
  - _Prompt: Implement the task for spec adp-file-nesting, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Apply nesting when listing and when a watch event arrives | Restrictions: a subject's `has_children` is pushed as `EntryUpdated` when it gains its first or loses its last registration; ordering is preserved, so a client applying pushed changes in order arrives at the same tree as one listing fresh (Requirement 10.4) | Success: creating a registration nests it immediately without a reload, and creating a missing subject re-parents its orphan. Mark in progress, log when done, then mark complete._

## Phase E — rename and delete

- [x] 13. Rename, in both directions
  - File: `src/backend/EtAlii.Adp.Backend/Hierarchy/Commands/RenameEntryCommandHandler.cs` (modify)
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6_
  - _Prompt: Implement the task for spec adp-file-nesting, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Invert the rename direction and make the set atomic | Restrictions: renaming a subject renames every registration under it, preserving each qualifier; renaming a registration changes only its qualifier and refuses a change to the subject portion with an explanation rather than silently re-pointing it; atomicity is achieved by checking EVERY target before moving anything, so the failure mode is a refusal rather than a half-renamed set - a half-renamed set is worse than a refused rename because it orphans some registrations while appearing to have succeeded | Success: a collision refuses and NOTHING has moved, asserted by checking the filesystem after the refusal. Mark in progress, log when done, then mark complete._

- [x] 14. Delete, and the cascade
  - File: `src/backend/EtAlii.Adp.Backend/Hierarchy/Commands/DeleteEntryCommandHandler.cs` (modify)
  - _Requirements: 6.1, 6.2, 6.3_
  - _Prompt: Implement the task for spec adp-file-nesting, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Cascade a subject delete and report the count first | Restrictions: deleting one registration deletes neither the subject nor a body another registration references; check move and copy against the nesting model and record any gap - this spec adds no move, it requires that move not be left silently broken | Success: deleting a subject with three registrations says three before it runs. Mark in progress, log when done, then mark complete._

## Phase F — the client

- [x] 15. The nine folder-gated sites, audited together
  - File: `src/client/src/shell/panels/ExplorerTreePanel.tsx` (modify)
  - Lines 63-64 sort comparator, 219 child loading, 325 icon selection, 593 activation-toggle, 716 and 735 keyboard navigation, 886-887 the expandable gate, 893 `aria-expanded`, 954 children render
  - Purpose: Requirement 9.1 asks for an exhaustive audit, and the count is nine rather than the six originally listed
  - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 7.6_
  - _Prompt: Implement the task for spec adp-file-nesting, first run spec-workflow-guide to get the workflow guide then implement the task: Role: TypeScript developer | Task: Change all nine sites together | Restrictions: the list above is the audit - work from it rather than from whichever site breaks first in manual testing; three of the nine (sort, icon, activation-toggle) fail as "looks slightly wrong" rather than "does not work", which is exactly why a symptom-driven pass misses them; `aria-expanded` is emitted for an expandable file, not only for folders | Success: expansion, collapse, child loading and keyboard navigation work for an expandable file exactly as for a folder. Mark in progress, log when done, then mark complete._

- [x] 16. Activation
  - File: `src/client/src/shell/panels/ExplorerTreePanel.tsx` (modify)
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5_
  - _Prompt: Implement the task for spec adp-file-nesting, first run spec-workflow-guide to get the workflow guide then implement the task: Role: TypeScript developer | Task: Open the right diagram on activation | Restrictions: activation stays the tree's existing double-click gesture - single-click remains selection, because selection is what the context service, the property grid and the context menu all read, and this spec changes the activation gesture for no node type; a subject with NO registration behaves exactly as it does today | Success: one registration opens directly, several open the default by the stable order, and a nested registration activated directly opens itself. Mark in progress, log when done, then mark complete._

- [-] 17. Client tests
  - File: `src/client/src/shell/panels/ExplorerTreePanel.test.tsx` (modify)
  - _Requirements: 12.5_
  - _Prompt: Implement the task for spec adp-file-nesting, first run spec-workflow-guide to get the workflow guide then implement the task: Role: TypeScript developer | Task: Cover the expandable-file behaviour | Restrictions: cover expansion, collapse, child loading, keyboard expand/collapse/first-child/parent, and `aria-expanded` for an expandable file | Success: `npm test` and `npm run typecheck` both pass. Mark in progress, log when done, then mark complete._

## Phase G — verification

- [ ] 18. Extend the example suite with the nesting assertions
  - File: `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/ExampleRegistration.Tests.cs` (modify)
  - _Requirements: 12.3, 12.4_
  - _Prompt: Implement the task for spec adp-file-nesting, first run spec-workflow-guide to get the workflow guide then implement the task: Role: QA-minded C# developer | Task: Add the nesting assertions to the suite written in task 5 | Restrictions: each registration nests under the subject it names; the unqualified form is the default; `code-level.adp` remains an unnested entry with no body; the five `body:` headers are still present and still authoritative after the rename | Success: the suite that was green before the rename is green after it, now asserting more. Mark in progress, log when done, then mark complete._

- [ ] 19. Run the gates
  - File: (verification only)
  - _Requirements: 12.1, 12.2_
  - _Prompt: Implement the task for spec adp-file-nesting, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer | Task: Run every gate and make each pass | Restrictions: `dotnet test --solution EtAlii.Adp.slnx` and `dotnet format style --verify-no-changes --severity info` are both checked BY EXIT CODE, since a zero-test run exits 5 while printing no failures; `npm test` and `npm run typecheck` from `src/client` because this spec touches the client | Success: all four gates pass, checked by exit code rather than by reading output. Mark in progress, log when done, then mark complete._
