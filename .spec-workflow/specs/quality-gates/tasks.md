# Tasks Document

> **Order matters in one place only.** Tasks 1–7 are the C4 rule reconciliation and must run in
> order, because each rule widening changes what the fixtures report and task 7 pins the result.
> Tasks 8–11 (interoperability without a JDK) depend on 1–7 for their baselines. Tasks 12–16
> (the style gate) and task 17 (worktrees) are independent of everything else and may land at any
> time, in either order.
>
> **Two tasks are deliberately not code.** Task 11 corrects a record and task 17 is a triage;
> both are required by the requirements and both are done by editing files, not by writing
> classes. They are tasks so they cannot be forgotten.

## Phase A — the C4 rules agree with Structurizr

- [x] 1. Add the rule ids and the mirror table
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4Rules.cs` (modify), `C4StructurizrMirror.cs` (new)
  - Add the six new ids: `c4.missing-deployment-description`, `c4.missing-deployment-technology`, `c4.missing-infrastructure-description`, `c4.missing-infrastructure-technology`, `c4.disconnected-element`, `c4.element-not-on-any-view`. Create the mirror table mapping ADP ids to Structurizr rule ids, with two explicit lists beside it: ADP-only rules (the editing-surface ones, kept) and Structurizr-only rules that are deliberately not implemented, each with its reason
  - Purpose: Requirement 1.7's traceability, and the data task 7's test consumes
  - _Leverage: `C4Rules.cs` (the existing ten ids and their `c4.` prefix convention)_
  - _Requirements: 1.1, 1.7, 1.8_
  - _Prompt: Implement the task for spec quality-gates, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer familiar with the C4 model | Task: Add the six rule ids and create C4StructurizrMirror, a static table of ADP-to-Structurizr rule correspondences plus the two explicit "no counterpart" lists, per design.md's Mirror table | Restrictions: no behaviour change yet - this task adds data only; every entry in the not-implemented list carries a reason naming what would have to exist first; do not invent a Structurizr rule id, use only those observed in design.md | Success: solution builds; every id in C4Rules appears in the mirror table or in the ADP-only list. Mark the task in progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 2. Restore descriptions on deployment and infrastructure nodes
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4RuleSet.cs` (modify)
  - Widen `c4.missing-description` back to deployment nodes and out to infrastructure nodes. Remove the narrowing comment that cites the worked example, since the reasoning behind it was wrong
  - Purpose: Requirement 1.2 - this is a regression being reversed, not a new rule
  - _Leverage: `C4RuleSet.cs` (the existing missing-description rule and its static-kinds condition)_
  - _Requirements: 1.2, 1.9_
  - _Prompt: Implement the task for spec quality-gates, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Widen c4.missing-description to cover deployment and infrastructure nodes, reversing the narrowing made during c4-diagrams task 1 | Restrictions: keep the severity a Warning, per Requirement 1.9 - Structurizr prints these as ERROR but they are recommendations, and that divergence is a recorded decision; do not re-narrow anything to keep a test quiet; fixtures reporting new problems is the rule working (Requirement 1.4) | Success: builds; big-bank-plc.dsl now reports deployment-node descriptions. Mark in progress, log when done, then mark complete._

- [x] 3. Widen the relationship technology rule
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4RuleSet.cs` (modify)
  - Widen `c4.missing-protocol` from container-to-container to every relationship, matching `model.relationship.technology`. Replace the "in-process calls have no protocol" comment - Structurizr inspects for one on every relationship and the worked example is not clean by its own tooling
  - Purpose: Requirement 1.3 - the second regression being reversed
  - _Leverage: `C4RuleSet.cs`_
  - _Requirements: 1.3, 1.4, 1.9_
  - _Prompt: Implement the task for spec quality-gates, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Widen c4.missing-protocol to every relationship per Requirement 1.3 | Restrictions: Warning severity; update fixture expectations rather than re-narrowing (Requirement 1.4); the reference example under examples/reference already names a technology on every relationship and must stay clean | Success: builds; ExamplesTests still passes for the reference project. Mark in progress, log when done, then mark complete._

- [x] 4. Add the deployment and infrastructure technology rules
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4RuleSet.cs` (modify)
  - Implement `c4.missing-deployment-technology` and `c4.missing-infrastructure-technology`, mirroring `model.deploymentnode.technology` and `model.infrastructurenode.technology`
  - Purpose: Requirement 1.1 - two of the gaps where Structurizr has a rule and ADP has none
  - _Leverage: `C4RuleSet.cs` (the existing missing-technology rule)_
  - _Requirements: 1.1, 1.9_
  - _Prompt: Implement the task for spec quality-gates, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Add the two node-technology rules per design.md | Restrictions: Warning severity; locate each problem by element id so the errors panel can select it; do not add a rule for component technology - Structurizr has none, and the probe in requirements.md confirms it | Success: builds; the AWS deployment fixture reports the nodes that lack a technology. Mark in progress, log when done, then mark complete._

- [x] 5. Add the disconnected-element rule
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4RuleSet.cs` (modify)
  - Implement `c4.disconnected-element`: an element in no relationship at all. Suppressed entirely while the model declares no views, per design.md's error-handling item 4
  - Purpose: Requirement 1.5
  - _Leverage: `C4RuleSet.cs`, `C4Workspace.Relationships`_
  - _Requirements: 1.5_
  - _Prompt: Implement the task for spec quality-gates, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Add c4.disconnected-element, suppressed while the model has no views | Restrictions: the suppression is load-bearing - a model being built up incrementally must not be buried in warnings, and a tool that greets a new file with problems teaches people to ignore problems; do not count a relationship where the element is only the destination as "not connected" | Success: builds; a model with one lonely system and no views reports nothing, and the same model with a view reports it. Mark in progress, log when done, then mark complete._

- [x] 6. Add the not-on-any-view rule
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4RuleSet.cs` (modify)
  - Implement `c4.element-not-on-any-view`, mirroring `model.element.noview`. Suppressed while the model declares no views, and also for an element declared in the most recent edit
  - Purpose: Requirement 1.6
  - _Leverage: `C4RuleSet.cs`, `C4RuleSet.MembersOf` (which already answers "is this element on that view")_
  - _Requirements: 1.6_
  - _Prompt: Implement the task for spec quality-gates, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Add c4.element-not-on-any-view with both suppressions from design.md's error-handling item 4 | Restrictions: reuse MembersOf rather than reimplementing view membership - the two disagreeing would be worse than the rule not existing; the "just added" suppression must not need edit history, derive it from the element being the last declared or drop that half and say so in the log | Success: builds; the reference example stays clean, and an element excluded from every view is reported. Mark in progress, log when done, then mark complete._

- [x] 7. Test the new rules, and pin the correspondence
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4.Tests/C4RuleSet.Tests.cs` (modify), `C4StructurizrMirror.Tests.cs` (new)
  - One tripping model and one clean model per new rule, in the style of the existing nineteen. Plus: the mirror table is total - every id in `C4Rules` is either mapped or explicitly ADP-only, so a rule cannot be added without a decision about its counterpart
  - Purpose: the guard for Phase A; Requirement 1.10's first half
  - _Leverage: `C4RuleSet.Tests.cs` (the existing per-rule test pairs)_
  - _Requirements: 1.1-1.7_
  - _Prompt: Implement the task for spec quality-gates, first run spec-workflow-guide to get the workflow guide then implement the task: Role: QA-minded C# developer | Task: Test each new rule and assert the mirror table is total | Restrictions: each test names the Structurizr rule it mirrors in a comment, so a reader can check the claim; do not assert exact message strings beyond the substring that carries the meaning | Success: all tests pass; removing any mirror entry fails the totality test. Mark in progress, log when done, then mark complete._

## Phase B — the interoperability guarantee holds without a JDK

- [ ] 8. Record Structurizr's verdicts as committed baselines
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4.Tests/Fixtures/verdicts/*.inspect.txt` (new)
  - One verdict per fixture in `C4DocumentTests.Corpus()`, holding the CLI's `inspect` output verbatim under a two-line header naming the CLI version and the date
  - Purpose: Requirements 3.2, 3.3 - the data the everyday run reads instead of invoking Java
  - _Leverage: `C4DocumentTests.Corpus()` (the fixture list, so a fixture cannot be added without its verdict being noticed)_
  - _Requirements: 3.2, 3.3_
  - _Prompt: Implement the task for spec quality-gates, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer with a JDK and the Structurizr CLI | Task: Generate a verdict file per fixture using `inspect`, committing the output verbatim with the header design.md specifies | Restrictions: verbatim - a transformed baseline is a second implementation to keep correct; record the actual CLI version, do not guess it; these files are test data and nothing in the product may read them | Success: one verdict per corpus fixture, each readable as a diff. Mark in progress, log when done, then mark complete._

- [ ] 9. Read a verdict, and compare it to what ADP says
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4.Tests/C4Verdict.cs` (new), `C4Reconciliation.Tests.cs` (new)
  - `C4Verdict.Read` parses `LEVEL | rule | message` into a comparable set. The reconciliation test asserts, per fixture, that ADP's reported rule ids restricted to the mirrored set equal Structurizr's restricted to the same set
  - Purpose: Requirement 1.10 and 3.1 - the guarantee, checked with no JDK anywhere
  - _Leverage: task 1's mirror table, task 8's verdicts, `ExamplesTests` model-loading helpers_
  - _Requirements: 1.10, 3.1, 3.5_
  - _Prompt: Implement the task for spec quality-gates, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement C4Verdict and the reconciliation test per design.md | Restrictions: compare only the mirrored rules in both directions - ADP-only and Structurizr-only rules are decisions, not disagreements; a missing or unreadable verdict fails the test naming the file and the regeneration command, it must never skip; do not compare messages, only rule ids | Success: passes with no JDK; corrupting a verdict fails it with a legible message. Mark in progress, log when done, then mark complete._

- [ ] 10. Keep the baselines honest, and make the skips visible
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4.Tests/C4Interop.Tests.cs` (modify)
  - A CLI-gated test that regenerates every verdict and asserts it matches what is committed. Widen the skip messages to name the environment variable and the command; state the ceiling on environment-gated skips the design asks for
  - Purpose: Requirements 3.4, 3.6, 3.7, 3.8
  - _Leverage: `C4InteropTests` (already runs the CLI and already skips without one)_
  - _Requirements: 3.4, 3.6, 3.7, 3.8_
  - _Prompt: Implement the task for spec quality-gates, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Add the verdict-currency test and improve the skip reporting per Requirements 3.4 and 3.6-3.8 | Restrictions: the currency test is the only thing allowed to need a JDK; record where the CLI comes from (Maven Central coordinates) per Requirement 3.8, since a previous session wrongly concluded it was unreachable; do not let a stale baseline fail the everyday run - that is a different fault with a different owner | Success: with a CLI, a hand-edited verdict fails the currency test; without one, everything else still passes. Mark in progress, log when done, then mark complete._

- [x] 11. Correct the record
  - File: `.spec-workflow/specs/c4-diagrams/design.md` (modify), `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4.Tests/Fixtures/readme.md` (modify)
  - Replace the deviations claiming `c4.missing-protocol` and `c4.missing-description` were over-reaching with the finding that the narrowing was itself the error. Restate the two defect-table entries in the fixtures readme. Note that the `c4.mixed-abstraction-levels` removal stands on Structurizr's inspector having no such rule, not on the worked-example argument. Record the general lesson in a form that applies to future diagram types
  - Purpose: Requirement 2 in full - a wrong reason corrected in place, not silently deleted
  - _Leverage: the correction style already used for the "the sandbox filters structurizr" error_
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_
  - _Prompt: Implement the task for spec quality-gates, first run spec-workflow-guide to get the workflow guide then implement the task: Role: technical writer with the codebase in hand | Task: Correct the recorded reasoning per Requirement 2 | Restrictions: correct in place with the correction visible - do not delete the wrong reasoning, the mistake is the useful part; state the general lesson so it applies beyond C4: a published example demonstrates what is permitted, never what is sufficient; do not touch c4-diagrams' approved requirements.md, which is out of scope per requirements.md | Success: no document still claims the narrowings were correct. Mark in progress, log when done, then mark complete._

## Phase C — Structurizr renders what ADP writes

- [ ] 12. Export every fixture, and baseline what comes out
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4.Tests/C4Interop.Tests.cs` (modify), `Fixtures/exports/*` (new)
  - With a CLI, export every fixture and every ADP-authored document to one diagram format and assert the export succeeds and each view's elements appear in it. Commit the exports as baselines so the everyday run checks them without Java. Retire the manual Structurizr Lite entry from `tests.md`, keeping only what is genuinely visual
  - Purpose: Requirement 4 in full
  - _Leverage: `C4InteropTests` (the CLI invocation and the skip gate), `tests.md`_
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5_
  - _Prompt: Implement the task for spec quality-gates, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Add export coverage and its baselines, then retire the manual Lite entry per Requirement 4.3 | Restrictions: assert the view's elements are present, not merely that the command exited zero - a view that renders empty is the defect this exists to catch, and the c4/deployment template shipped exactly that bug once; what stays in tests.md must be narrowed to styling, theme resolution and layout quality, and must say what replaced the rest | Success: an empty view fails the check; the everyday run needs no JDK. Mark in progress, log when done, then mark complete._

## Phase D — the style gate passes

- [x] 13. Resolve the editorconfig contradiction
  - File: `src/.editorconfig` (modify)
  - The file says `dotnet_separate_import_directive_groups` is commented out and then enables it on the next line. Set it `false`, honouring the note's stated reasoning, and keep `dotnet_sort_system_directives_first = true`. Rewrite the note so it and the setting agree
  - Purpose: Requirement 5.4 - and the cause of 115 of the gate's failures
  - _Leverage: `src/.editorconfig` (the existing downgrade-with-a-note convention)_
  - _Requirements: 5.2, 5.3, 5.4_
  - _Prompt: Implement the task for spec quality-gates, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer familiar with EditorConfig and .NET analyzers | Task: Make the note and the setting agree per Requirement 5.4 | Restrictions: the blank-line-between-groups rule is the opinionated half and the codebase does not follow it - disable that, keep sorting, which is mechanical and uncontroversial; the note must say what the rule wanted, what the codebase does instead, and why the codebase won | Success: the IMPORTS count drops to sorting-only violations. Mark in progress, log when done, then mark complete._

- [x] 14. Sort the usings
  - File: every `.cs` file the gate still names (mechanical)
  - Run `dotnet format style` to fix the remaining `IMPORTS` violations, in its own commit
  - Purpose: Requirement 5.1, 5.5
  - _Leverage: `dotnet format`_
  - _Requirements: 5.1, 5.5_
  - _Prompt: Implement the task for spec quality-gates, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer | Task: Auto-fix the remaining import ordering, committing separately from anything behavioural | Restrictions: this commit contains nothing but the reformatting - a mechanical diff across ~190 files must not be where a reader looks for a logic change; run the full test suite afterwards, since a using change can alter overload resolution | Success: zero IMPORTS diagnostics; 1221+ tests still pass. Mark in progress, log when done, then mark complete._

- [x] 15. Downgrade the two rules that fight a convention
  - File: `src/.editorconfig` (modify), `.spec-workflow/steering/structure.md` (modify)
  - Downgrade `IDE0130` to `none` and `IDE0046` to `silent`, each with a note. Add the `_Model/`-and-`Commands/`-in-parent-namespace convention to structure.md, where it is currently only implicit
  - Purpose: Requirements 5.2, 5.3, 5.6
  - _Leverage: `src/.editorconfig`'s existing notes for downgraded rules_
  - _Requirements: 5.2, 5.3, 5.6_
  - _Prompt: Implement the task for spec quality-gates, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer | Task: Downgrade IDE0130 and IDE0046 with notes, and document the namespace convention in structure.md | Restrictions: the IDE0130 note must carry the evidence - 95 in _Model, 16 in Commands, 14 in History, 6 in Support - so a reader can judge whether it is a convention or drift; structure.md is a steering document, keep it short and state the rule rather than arguing it | Success: both diagnostics gone from the gate; structure.md states the convention. Mark in progress, log when done, then mark complete._

- [x] 16. Fix the nine singletons, and write down what keeps the gate green
  - File: the nine files the gate names (modify), `CLAUDE.md` (modify)
  - Fix `IDE0017`, `IDE0032`, `IDE0053`, `IDE0059`, `IDE0066`, `IDE0270`, `IDE0330` and `IDE1006` in the code. Check `IDE1006` first against the Serilog `_logger` convention - if that is what it is, widen the existing downgrade instead. Then add the pre-merge step to CLAUDE.md beside the existing instruction to run the command
  - Purpose: Requirements 5.1, 5.7 - the gate exits zero, and something keeps it that way
  - _Leverage: CLAUDE.md's existing "always allow this command to run" instruction_
  - _Requirements: 5.1, 5.7_
  - _Prompt: Implement the task for spec quality-gates, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer | Task: Fix the singleton diagnostics and document the pre-merge step | Restrictions: a one-off is not evidence of a convention - fix these rather than downgrade them, unless IDE1006 turns out to be the logger naming already downgraded; CLAUDE.md must say plainly that this is a human step because there is no CI, per design.md - do not describe it as automation | Success: `dotnet format style --verify-no-changes --severity info` exits zero on a clean checkout. Mark in progress, log when done, then mark complete._

## Phase E — worktrees

- [x] 17. Triage the worktrees and write the retirement rule
  - File: `CLAUDE.md` (modify), plus `git worktree remove` for those that qualify
  - Classify each of the sixteen worktrees as removed, kept with a stated reason, or needs an owner's decision. Remove those whose branch is merged into develop and whose working tree is clean. Resolve or remove the detached one. Add the retirement step to CLAUDE.md beside the existing creation rule
  - Purpose: Requirement 6 in full
  - _Leverage: `git worktree list --porcelain`, `git merge-base --is-ancestor`_
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6_
  - _Prompt: Implement the task for spec quality-gates, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer comfortable with git plumbing | Task: Triage all sixteen worktrees per Requirement 6 and add the retirement rule to CLAUDE.md | Restrictions: this is housekeeping and housekeeping must not be destructive - a worktree with uncommitted changes or unmerged commits is never removed, and anything that would destroy work is raised for a decision instead; record the classification rather than performing it silently; note that removal can fail with "Filename too long" on deep node_modules paths, which is a reason to report rather than to force | Success: every worktree is classified and the classification is in the implementation log. Mark in progress, log when done, then mark complete._
