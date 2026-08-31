# Requirements Document

## Introduction

This spec covers a bounded technical-debt cleanup across the backend, the client and the diagram modules, along the three axes the request names: **consistency**, **orphaned code** and **duplicate logic**.

A cleanup spec has a characteristic failure mode: it becomes a standing invitation to change anything, and then it is never finished because it was never bounded. This spec is written against measurements taken from the tree at `5844814d` rather than against impressions, and every requirement below names the finding that motivates it. Where a measurement contradicted the expectation, it is recorded as it came out — including two cases where the debt turned out not to exist.

The governing constraint is that this work changes no behaviour. The value of a cleanup lies entirely in what it makes cheaper later, so a cleanup that costs a regression has negative value. Requirement 1 makes that a gate rather than an aspiration.

## Alignment with Product Vision

- [structure.md](../../steering/structure.md)'s **`EtAlii.Adp` (Library)** — "Contains all reusable helper classes, mechanisms and extension methods" — is the stated home for anything this spec centralizes. The library exists and already holds `ShortGuid`; several findings below are helpers that should have gone there and did not.
- [tech.md](../../steering/tech.md)'s **"Treat an InspectCode finding the way an `.editorconfig` finding is treated"**: fix it, or record a deliberate decision not to. This spec applies that rule to findings gathered by hand, and Requirement 10 applies it in the one direction the steering doc did not anticipate — correcting a steering claim that measurement has overtaken.
- [tech.md](../../steering/tech.md)'s **"Expect a backlog ... and do not treat it as a gate on unrelated work"**: this spec is explicitly not a gate. Requirement 11 sequences it around in-flight specs rather than in front of them.

## What this spec is not

Stated up front, because each of these is a plausible reading of "cleanup" that would make the work unbounded:

- **Not a behaviour change.** No bug fixes, no new features, no changed output. A defect found while cleaning is recorded and routed per [CLAUDE.md](../../../CLAUDE.md)'s bug rule, not fixed inside this spec's commits.
- **Not a re-opening of approved specs.** Where a module's shape was approved through a spec, converging it is a change to that spec's territory and needs its own approval.
- **Not a rewrite of the 48 stub modules' identity data.** Their origins, titles and descriptions are catalog content, governed by [docs/diagrams.md](../../../docs/diagrams.md).
- **Not a performance exercise.** Structure only.
- **Not a dependency upgrade or a build-system change.**

## Requirements

### Requirement 1 — Behaviour preservation is the gate, not the goal

**User Story:** As a maintainer, I want every cleanup change proven behaviour-preserving, so that I am never trading a working system for a tidier one.

#### Acceptance Criteria

1.1. WHEN any change in this spec is proposed for merge THEN `dotnet test --solution EtAlii.Adp.slnx` SHALL report `failed: 0`, and the exit code SHALL be checked rather than the output grepped — a zero-test run exits 5 and prints no failures.
1.2. The test total SHALL NOT fall below the pre-change total, EXCEPT where a requirement below names a deliberate consolidation, in which case the drop SHALL equal the number of tests consolidated and SHALL be stated in the commit message.
1.3. `dotnet format style --verify-no-changes --severity info` SHALL exit zero.
1.4. WHEN a refactor cannot be shown behaviour-preserving by the existing suite THEN a characterisation test SHALL be written before the refactor, capturing current behaviour, so the refactor has something to be checked against.
1.5. No change in this spec SHALL alter a `.proto` contract, a file format, or any on-disk artifact a user's project contains.

### Requirement 2 — One callback-disposable, not three

**User Story:** As a developer, I want the trivial "dispose runs this callback" type to exist once, so that I stop meeting it under a different name in each module.

**Finding:** `WardleyElementUnsubscriber` and `C4ElementUnsubscriber` are the same six-line class, identical apart from the module name: a sealed class taking an `Action` and invoking it from `Dispose()`. `PipelineChangeUnsubscriber` is a thirty-line variant of the same idea.

#### Acceptance Criteria

2.1. A single callback-disposable type SHALL live in the `EtAlii.Adp` library, per structure.md's definition of that project.
2.2. The wardley and c4 copies SHALL be deleted and their use sites SHALL resolve to the shared type.
2.3. `PipelineChangeUnsubscriber` SHALL be examined and EITHER reduced to the shared type IF its extra behaviour is incidental, OR kept with a comment naming what it does that the shared type does not. A third option — leaving it unexamined — is not available.
2.4. The shared type SHALL carry its own unit test, which none of the three copies currently has.

### Requirement 3 — One diagram stream hook, not five

**User Story:** As a client developer, I want the streaming, retry and lifecycle logic in one hook, so that a fix to reconnection behaviour reaches every diagram type instead of one.

**Finding:** `useC4Stream`, `useWardleyStream`, `usePipelineStream`, `useMindmapStream` and `useAnsibleStream` are 117-140 lines each. Between the wardley and ansible copies, 59 of 109 non-blank lines are identical once the module name is normalized away — including the `AbortController` wiring, the 500ms retry delay, the `loading`/`failed`/`model` state triple, and the compare-by-value `pathKey` trick with its `eslint-disable` comment. They are not identical copies; they are copies that have drifted, which is the more expensive kind.

#### Acceptance Criteria

3.1. A shared hook, generic in the model type, SHALL own the transport, client construction, subscription lifecycle, cancellation, retry, and the `loading`/`failed`/`model` states.
3.2. Each module's hook SHALL retain only what is genuinely its own: its model type, its empty-model value, and its response-to-model mapping.
3.3. WHERE the five copies disagree today, the shared hook SHALL adopt one behaviour, and acceptance SHALL include a list of every behavioural difference found and which one was chosen. Silently picking whichever copy was refactored first is the failure mode this criterion exists to prevent.
3.4. The retry interval, currently a bare `500` repeated in five files, SHALL become a named constant in one place.

### Requirement 4 — Collapse the 48 duplicated stub tests

**User Story:** As a maintainer, I want the per-stub-module assertions expressed once, so that changing what a diagram definition must satisfy is one edit rather than 48.

**Finding:** 48 of the 50 `DiagramTests.cs` files are structurally identical — the same two tests, differing only in string literals. 47 of the 53 `Diagram.cs` files are likewise identical once comments are stripped.

There is a precedent for where this belongs. The "every definition carries a description" check was recently moved out of a per-module test into `DiagramDiscoveryStartupTests`, which builds the real host and is the one place that sees every deployed module without core naming any of them. The origin and title assertions are the same shape of check and have the same natural home.

#### Acceptance Criteria

4.1. The assertions "origin matches the cataloged tag" and "title matches the cataloged name" SHALL be expressed once, against the live deployed catalog, in the same place as the existing description check.
4.2. A failure SHALL name the offending diagram origin, so the collapsed test is no harder to diagnose than the 48 it replaces. This is the property that makes the consolidation safe, and it SHALL be verified by deliberately breaking one definition and reading the failure message.
4.3. The consolidation SHALL be reported under Requirement 1.2 as a deliberate test-count change.
4.4. WHETHER the now-empty per-module test projects are deleted or kept as scaffolding for each module's eventual implementation SHALL be an explicit decision recorded in the design document, not a side effect. Deleting 48 projects and re-creating them one at a time later may cost more than it saves; this criterion asks for the decision, not for a particular answer.

### Requirement 5 — One shape for module registration

**User Story:** As a developer opening an unfamiliar module, I want its service registration in a predictable place, so that I do not have to discover each module's local convention.

**Finding:** `wardley-map` and `mindmap` split registration across `ServiceCollection.AddX.cs` and `ServiceCollection.AddXCommands.cs`; `c4`, `azure-pipeline` and `ansible-structure` keep it in one file. Both shapes work. The cost is that neither is predictable.

#### Acceptance Criteria

5.1. One shape SHALL be chosen, and the choice SHALL be recorded in [tech.md](../../steering/tech.md) so the next module follows it without re-deciding.
5.2. The five implemented modules SHALL be brought to that shape.
5.3. WHERE splitting is genuinely warranted by size, the recorded rule SHALL say at what point to split, rather than leaving it to taste.

### Requirement 6 — One shape for the diagram definition declaration

**User Story:** As a developer adding a diagram type, I want one declaration form to copy, so that the 49th module does not introduce a 50th variation.

**Finding:** 4 of 53 `Diagram.cs` files expose a named `DiagramDefinition` property alongside `Definitions`; the other 49 declare the array inline. 5 pass a `Build:` delegate; 4 declare a `DocumentExtension` constant. The variation tracks whether a module is implemented — but nothing says so, and the stub form gives no hint of what the implemented form will need.

#### Acceptance Criteria

6.1. The two legitimate shapes — stub and implemented — SHALL be documented in tech.md as exactly two, with the trigger for moving from one to the other.
6.2. The named-property form SHALL be used consistently by implemented modules, since a module's own registrations need to name their definition without indexing into an array.
6.3. Divergence beyond those two shapes SHALL be removed.

### Requirement 7 — Reduce the parallel-role duplication in backend module infrastructure

**User Story:** As a maintainer, I want the shared skeleton of the per-module infrastructure factored out, so that the sixth diagram module starts from a base rather than from a copy of the fifth.

**Finding:** every implemented module repeats the same roles — `Session`, `SessionFactory`, `ContextSourceResolver`, `DocumentStore` plus its interface, `Parser`, `Writer`, `Validator`, `RuleSet`, `ElementMapper`, `ToolboxProvider`, `ContextActionProvider`, `ContextPropertyProvider`. Measured overlap between pairs: `ContextSourceResolver` shares 58 of 158 non-blank lines (c4 vs wardley); `SessionFactory` shares 17 of 39 (wardley vs azure-pipeline). That is real duplication, but it is partial — roughly 35-45%, not the near-total overlap of Requirements 2 and 3.

#### Acceptance Criteria

7.1. This requirement SHALL be approached role by role, not as one refactor, and each role SHALL be justified independently.
7.2. FOR each role, the shared portion SHALL be extracted ONLY IF the extraction leaves each module's remaining code readable on its own. An abstraction that forces a reader to hold a base class in mind to understand a module is a worse outcome than the duplication it replaced, and this criterion SHALL be applied as a veto.
7.3. WHERE a role is found to be parallel but genuinely different, that SHALL be recorded as a decision not to extract, with the reason, so it is not re-litigated at the next module.
7.4. `ansible-structure` has no `DocumentStore` and no unsubscriber while the other four do. This SHALL be checked: it is either a legitimate consequence of that module reading a project tree rather than a single document, or a gap. The answer SHALL be recorded either way.
7.5. No extraction under this requirement SHALL cross the module boundary in the wrong direction — core SHALL NOT come to name a module, per the pluggability rule that governs discovery.

### Requirement 8 — Orphaned code: report the negative result honestly

**User Story:** As a maintainer, I want to know whether dead code actually exists, so that I stop paying attention to a problem I do not have.

**Finding, stated as measured:** a scan of all 710 declared types found 155 mentioned in only one file. Of those, 137 are xUnit test classes, discovered by reflection and therefore expected to be single-file. Of the remaining 18, 14 are `ServiceCollection*Extension` static classes, referenced by method name rather than type name — false positives. The final four (`C4CommandHandler`, `C4ViewBlock`, `MindmapSize`, `WardleyEdit`) were each checked by hand, and all four are used within their declaring file.

**There is no orphaned type in this codebase.** Two further claims in circulation were also checked and are stale: committed `.bld` build artifacts (measured: 0 tracked files), and an unused `WardleyTestDiagramDefinitionCatalog` (now referenced, wired up at `d125bfb6`).

#### Acceptance Criteria

8.1. This requirement SHALL be satisfied by recording the negative result, not by finding something to delete. A cleanup spec that must produce deletions in order to feel complete will produce bad deletions.
8.2. The scan method SHALL be written down so it can be re-run, including the two false-positive classes that make a naive scan report 155 orphans where there are none.
8.3. The two stale claims SHALL be withdrawn wherever they are still recorded as open work.
8.4. WHERE orphan analysis at a finer granularity than the type — unused public members, unreferenced generated protobuf messages, unused client exports — is wanted, it SHALL be scoped as its own effort. This spec's scan was type-level and SHALL NOT be reported as more than that.

### Requirement 9 — Remove the last two nested types

**User Story:** As a developer, I want the "never nest a type" rule to be true rather than aspirational, so that it can be enforced on new code without argument.

**Finding:** exactly two nested type declarations remain: one in `_Model/C4Document.cs`, one in `C4.Tests/Examples.Tests.cs`.

#### Acceptance Criteria

9.1. Both SHALL be lifted to namespace level and given names that stand alone, per tech.md's guidance that a nested `Entry` becomes `ContextSelectionEntry`.
9.2. The record in `C4Document.cs` SHALL land in the module's `_Model` folder, per the POCO rule.
9.3. WHEN both are lifted THEN the count SHALL be zero, and the rule SHALL become enforceable on new code.

### Requirement 10 — Correct the steering document the measurements have overtaken

**User Story:** As a developer reading the steering docs, I want their factual claims to be true, so that I can trust the rest of what they say.

**Finding:** [tech.md](../../steering/tech.md) states that "roughly a hundred nested types remain from before the rule" and that "they are being lifted out". The measured count is 2. The lifting was substantially completed and the document was never updated, so it now overstates its own backlog roughly fiftyfold.

This matters beyond the number. A steering document telling a developer to expect a hundred offenders is telling them the rule is widely broken and their own violation would be unremarkable. The true state — two left — supports the opposite conclusion.

#### Acceptance Criteria

10.1. The claim SHALL be corrected to the measured count.
10.2. WHEN Requirement 9 lands THEN the passage SHALL be rewritten from "a backlog is being worked off" to "this rule holds; keep it holding".
10.3. Any other steering claim this spec's measurements touch SHALL be checked the same way rather than assumed current.

### Requirement 11 — Sequence this work around in-flight specs, not in front of them

**User Story:** As someone with 20 active specs, I want cleanup that touches every module not to collide with the work that is actually shipping features.

**Finding:** this spec touches all five implemented modules and, under Requirement 4, 48 test projects. There are 20 active specs, several mid-implementation. A tree-wide refactor landing under them produces conflicts in files their authors never touched — the most expensive kind to resolve, because neither side understands the other's change.

#### Acceptance Criteria

11.1. Each requirement SHALL be landable independently, in its own worktree and its own merge. There SHALL NOT be a single "cleanup" branch touching everything.
11.2. Requirements SHALL be ordered by blast radius, smallest first: Requirements 2 and 9 touch a handful of files; Requirement 4 touches 48 projects. The small ones establish that the process works before the large one is attempted.
11.3. BEFORE a requirement touching a module lands, the specs currently active against that module SHALL be checked, and the merge SHALL be timed against them.
11.4. This spec SHALL NOT block any feature spec. WHERE cleanup and feature work conflict, the feature work wins and the cleanup rebases.
11.5. Per [CLAUDE.md](../../../CLAUDE.md), each piece SHALL be done in its own worktree with a short directory name, since the repository sits close enough to Windows' `MAX_PATH` limit that a long worktree name breaks the build in a way that reports as `Zero tests ran` rather than as a failure.

### Requirement 12 — Close the client test-coverage gap this survey exposed

**User Story:** As a client developer, I want the stream hooks tested wherever they exist, so that Requirement 3's extraction has something to be verified against.

**Finding:** `useMindmapStream` and `useAnsibleStream` have tests. `useC4Stream`, `useWardleyStream` and `usePipelineStream` do not. Requirement 3 proposes replacing all five with one shared hook — a refactor across untested code in three of five cases.

#### Acceptance Criteria

12.1. The gap SHALL be closed BEFORE Requirement 3's extraction, not after, so that the extraction is verified rather than merely believed.
12.2. The new tests SHALL characterise current behaviour including the drift, so that Requirement 3.3's list of behavioural differences is derived from tests rather than from reading.
12.3. This is the one place where this spec deliberately adds tests rather than consolidating them, and the addition SHALL be reported under Requirement 1.2.

## Evidence

Measurements taken against `5844814d`, with the method recorded so each can be re-run.

| Finding | Measured | Requirement |
|---|---|---|
| `DiagramTests.cs` files structurally identical (literals normalized) | 48 of 50 | 4 |
| `Diagram.cs` files structurally identical (comments stripped) | 47 of 53 | 4, 6 |
| `WardleyElementUnsubscriber` vs `C4ElementUnsubscriber` | identical but for the name | 2 |
| Client stream hooks: identical non-blank lines, wardley vs ansible | 59 of 109 | 3 |
| `ContextSourceResolver`: shared non-blank lines, c4 vs wardley | 58 of 158 | 7 |
| `SessionFactory`: shared non-blank lines, wardley vs azure-pipeline | 17 of 39 | 7 |
| Declared types scanned | 710 | 8 |
| Types mentioned in exactly one file | 155 | 8 |
| ...of those, genuinely orphaned after manual check | **0** | 8 |
| Tracked `.bld` build artifacts | **0** (claim stale) | 8 |
| Nested type declarations remaining | **2** | 9 |
| Nested types claimed by tech.md | "roughly a hundred" (stale) | 10 |
| Client stream hooks without tests | 3 of 5 | 12 |
| Active specs this work must sequence around | 20 | 11 |

## Open questions for the design phase

1. **Requirement 4.4** — delete the 48 emptied test projects, or keep them as scaffolding? A real trade-off that the requirements phase deliberately does not settle.
2. **Requirement 7** — how much of the per-module skeleton is genuinely shared rather than merely similar? The 35-45% measured overlap is exactly the range where extraction can cost more than it saves, and 7.2 gives the veto teeth.
3. **Requirement 5.1** — which registration shape wins? The single-file form is the majority, but the split form is what the two most recently implemented modules chose, which may mean it is the better shape rather than the deviant one.
