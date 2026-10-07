# Requirements Document

## Introduction

**Every JetBrains Rider warning in the C# code of this repository is to be corrected, and none is to come back.** The user ruled on 2026-10-07 that in C# projects all Rider warnings are corrected. That rule is now principle VI of the etalii.adp constitution and a section of [tech.md](../../steering/tech.md). This specification is the one-off work that brings the existing code under it: clear what the inspection reports today, and leave behind a check that fails when a new finding appears.

The team develops in Rider, so these are the squiggles and the *Problems* entries a developer sees every day. `dotnet format`, the gate that already runs, does not see them at all, which is how they accumulated.

## What was measured, and with which instrument

Measured on `develop` at `724f7837` on 2026-10-07, in a newly created worktree, after `dotnet build EtAlii.Adp.slnx` exited zero with no warnings.

The instrument is `jb inspectcode` 2026.2.2 (JetBrains.ReSharper.GlobalTools), the engine Rider runs, over `src/backend/EtAlii.Adp.slnx`, with `--no-build`, `--dotnetcoresdk=10.0.203` and an explicit `--toolset-path`, once at `--severity=SUGGESTION` and once at `--severity=HINT`. The two runs agree on every error, warning and suggestion. The solution holds every C# project of the repository, the modules under `src/diagrams/`, `src/designers/` and `src/editors/` included.

**The run could see the code.** It inspected 1978 `.cs` files and reported **zero** C# compiler errors and zero unresolved symbols. That figure is what makes the counts below mean something: an earlier run of this tool in this repository reported zero warnings while carrying 327 unresolved-symbol errors.

| Severity | Findings | Files | Inspections |
|---|---|---|---|
| Error | 4 | 1 | 1 |
| Warning | 89 | 58 | 11 |
| Suggestion | 799 | 420 | 41 |
| Hint | 2455 | 758 | 55 |

### The four errors are one deliberate fixture

All four are XML errors in `Fixtures/cpm/Broken.csproj` of `EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests`. That file is malformed on purpose: it is the input of a test for a project file that cannot be parsed. It is not a defect and cannot be corrected without destroying the test.

### The 89 warnings

| Count | Inspection | What it says |
|---|---|---|
| 51 | `NotAccessedPositionalProperty.Global` | A positional property of a record is never read |
| 18 | `UnusedAutoPropertyAccessor.Global` | An auto-property accessor is never used |
| 6 | `NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract` | The left side of `??` is never null according to its annotations |
| 3 | `InconsistentlySynchronizedField` | A field is used both inside and outside a lock |
| 2 | `CollectionNeverUpdated.Local` | A collection is never updated |
| 2 | `PossibleLossOfFraction` | An integer division loses its fraction |
| 2 | `UnusedMember.Local` | A private member is never used |
| 2 | `UnusedVariable` | A local variable is never used |
| 1 | `CollectionNeverQueried.Local` | A collection's content is never queried |
| 1 | `NotAccessedVariable` | A local variable is assigned and never read |
| 1 | `RedundantAssignment` | An assigned value is never used |

By project: `EtAlii.Adp.Specification.Fbl` 39, `c4` 8, `databricks` 5, `EtAlii.Adp.Diagram` 4, `rdf` 4, `sankey` 4, and one or two each in nineteen further projects.

Three things about this list decide how the work is done:

1. **69 of the 89 are two inspections about data shapes** - records and properties that are written and never read by C# code. Many of these are read by something the inspection cannot see: a serializer, a protobuf mapping, a test that compares whole records. Each one is either dead and deleted, or alive and told so. Which is which is the real work, and it is not mechanical.
2. **Three are a possible concurrency defect.** `DiagramDocumentReloadBridge` reads a field outside the lock it writes it under (lines 58, 61 and 69 at the measured commit). This is a finding about behaviour, not style.
3. **Two are a possible arithmetic defect.** `SkosLayout` divides integers where a fraction may be intended, and its test repeats the division.

### The 799 suggestions

Rider draws these too, as green squiggles, and lists them in *Problems* beside the warnings. The ten largest, 653 of the 799:

| Count | Inspection | What it says |
|---|---|---|
| 224 | `MemberCanBePrivate.Global` | A member can be made private |
| 132 | `MergeIntoPattern` | Null and pattern checks can be merged into one pattern |
| 120 | `UnusedMember.Global` | A non-private member is never used |
| 50 | `UnusedType.Global` | A non-private type is never used |
| 27 | `UnusedMethodReturnValue.Global` | A method's return value is never used |
| 26 | `ConvertToPrimaryConstructor` | A constructor can become a primary constructor |
| 21 | `ArrangeObjectCreationWhenTypeEvident` | `new` can omit an evident type |
| 20 | `UseWithExpressionToCopyRecord` | A record copy can use `with` |
| 17 | `RedundantExplicitParamsArrayCreation` | An explicit array for a `params` argument is redundant |
| 16 | `RedundantVerbatimStringPrefix` | An `@` prefix is redundant |

The other 146 are spread over 31 inspections of 15 findings or fewer. 546 of the 799 are in the diagram modules; `rdf` (80) and `EtAlii.Adp.Specification.Fbl` (91) carry the most.

**More than half, 468, are "never used" or "can be private" judgements made across the whole solution** (`*.Global`). In this codebase those have a known class of false positive: a type found by reflection or registered by name. 48 diagram modules report exactly two each, `UnusedType.Global` and `UnusedMember.Global` in `Diagram.cs`, for the `Diagram` class and its `Definitions`, which the catalog discovers by scanning assemblies. The decision log in tech.md (entry 10) already records a type-level scan that found zero orphaned types, and warns that a cleanup which must produce deletions will produce bad ones. That warning applies here in full.

### What this measurement does not establish

- **Whether Rider on the user's machine shows exactly this list.** `jb inspectcode` honours the committed `.DotSettings` files and `src/.editorconfig`, and not settings kept in a personal Rider profile. A severity changed only in someone's IDE is invisible to it.
- **What is on branches other than `develop`.** The user corrected Rider findings by hand on 2026-10-04 and again on 2026-10-07; the latter is on a branch that has not reached `develop`. The counts move as that work lands.
- **Anything about the client.** The TypeScript under `src/client/` and the modules' `client/` folders is outside the rule, which names C# projects.

## Alignment with Product Vision

ADP's tool types are plugins, and [product.md](../../steering/product.md) expects more of them to keep arriving. Each new module copies an existing one, so whatever an existing module tolerates is reproduced in the next: 48 modules already carry the same two findings each. A codebase that reports nothing is the only state in which a new finding is noticed, by a developer in Rider or by an agent reading a report.

[tech.md](../../steering/tech.md) names Rider as the development tool, and [processes.md](../../steering/processes.md#checking-that-the-conventions-are-actually-followed) already says of an InspectCode finding that leaving it reported is the one option not available. This specification makes the code agree with that sentence.

## Requirements

### Requirement 1 - No warning remains

**User Story:** As a developer working in Rider, I want the solution to report no warning, so that a warning I see is one I just caused.

#### Acceptance Criteria

1. WHEN the inspection is run over `src/backend/EtAlii.Adp.slnx` at the completion of this specification THEN it SHALL report zero findings at severity Warning.
2. WHEN a warning is corrected THEN the correction SHALL be a change to the code at the place the warning occurs, unless criterion 3 or Requirement 3 applies.
3. IF a warning says a member, accessor, parameter or type is unused AND something the inspection cannot see does use it THEN the code SHALL say so at that member in a way the inspection honours, and SHALL NOT be deleted.
4. IF a warning says a member, accessor, variable or type is unused AND nothing uses it THEN it SHALL be removed.
5. WHEN the three `InconsistentlySynchronizedField` warnings in `DiagramDocumentReloadBridge` are handled THEN the handling SHALL state whether the unsynchronized access is a defect, and IF it is a defect THEN it SHALL be fixed with a test seen to fail before the fix.
6. WHEN the two `PossibleLossOfFraction` warnings in `SkosLayout` and its test are handled THEN the handling SHALL state whether integer division is intended, and IF it is not THEN it SHALL be fixed with a test seen to fail before the fix.

### Requirement 2 - No suggestion remains

**User Story:** As a developer working in Rider, I want the solution to show no green squiggle either, so that the *Problems* list is empty and stays readable.

#### Acceptance Criteria

1. WHEN the inspection is run at the completion of this specification THEN it SHALL report zero findings at severity Suggestion.
2. WHEN a suggestion-level inspection is handled THEN each of its findings SHALL be corrected in the code, unless Requirement 3 applies to the inspection as a whole.
3. IF a suggestion says a non-private type or member is never used THEN it SHALL be handled as Requirement 1 criteria 3 and 4 handle a warning, and a type that is discovered by reflection or registered by name SHALL NOT be deleted.
4. WHEN a correction changes what a member is visible to, or removes a member THEN the four gates SHALL exit zero afterwards.
5. WHEN the two findings that 48 diagram modules share in `Diagram.cs` are handled THEN one decision SHALL cover all of them, and the stub shape a tool engineer copies for the next catalog entry SHALL report nothing.

### Requirement 3 - An inspection that does not fit is switched off where the rule lives

**User Story:** As a developer, I want every deviation from Rider's defaults written down in one place with its reason, so that I can tell a decision from an oversight.

#### Acceptance Criteria

1. IF an inspection is judged not to fit this codebase THEN its severity SHALL be changed in a committed `.DotSettings` file or in `src/.editorconfig`, and nowhere else.
2. WHEN a severity is changed THEN a note beside the change SHALL state why, in the manner the existing downgraded rules in `src/.editorconfig` do.
3. WHEN a single finding is suppressed in place, by a `// ReSharper disable` comment or an attribute THEN the suppression SHALL carry its reason on the same line.
4. The deliberately malformed `Fixtures/cpm/Broken.csproj` SHALL be excluded from inspection by a committed setting, with a note naming the test it serves, and its content SHALL NOT change.
5. WHEN this specification completes THEN the number of inspections switched off or downgraded by it SHALL be stated in the closing record with each one's reason, so the user can see what was decided rather than corrected.
6. An inspection SHALL NOT be switched off because its findings are numerous.

### Requirement 4 - The measurement is repeatable and proves it could see

**User Story:** As a developer or an agent, I want one committed command that runs the inspection correctly, so that nobody reports a clean result from a run that inspected nothing.

#### Acceptance Criteria

1. A committed script SHALL run the inspection over the whole solution with the SDK and toolset named explicitly, and SHALL be the one way this repository's documents tell a reader to run it.
2. WHEN the script's run reports a C# compiler error or an unresolved symbol THEN the script SHALL exit non-zero and say the run could not see the code.
3. WHEN the script's run inspected fewer C# files than the solution tracks THEN the script SHALL exit non-zero.
4. WHEN the run reports any finding at Warning or Suggestion severity THEN the script SHALL exit non-zero and list the findings by file and line.
5. WHEN the run is healthy and reports no such finding THEN the script SHALL exit zero.
6. The script SHALL be seen to fail on a planted finding and pass again once the finding is removed, and that control SHALL be recorded.

### Requirement 5 - A new finding fails a pull request

**User Story:** As the user, I want a pull request that adds a Rider warning to fail, so that the rule holds without my reading every diff in Rider.

#### Acceptance Criteria

1. WHEN a pull request into `develop` is built THEN the Build workflow SHALL run the script of Requirement 4 and SHALL fail when it exits non-zero.
2. The check SHALL be added only after Requirements 1 and 2 are met on `develop`, so that it never fails a pull request for a finding the pull request did not add.
3. The check SHALL run in the Build workflow and SHALL NOT become a fifth local gate: the four gates named in CLAUDE.md stay four.
4. WHEN the check is added THEN [processes.md, *Checking that the conventions are actually followed*](../../steering/processes.md#checking-that-the-conventions-are-actually-followed) SHALL be brought in line with it: the bullet that tells a reader to expect a backlog, and the statement elsewhere that `jb inspectcode` is not a gate, each describe a state this specification ends.

### Requirement 6 - The work lands in pieces that can each be reviewed

**User Story:** As the user reviewing pull requests, I want each one to do a single kind of thing, so that a deleted member is not hidden among four hundred restyled lines.

#### Acceptance Criteria

1. WHEN corrections are delivered THEN a pull request SHALL contain either corrections that cannot change behaviour or corrections that can, and not both.
2. WHEN a pull request removes or narrows a type or member THEN its description SHALL list what was removed and what showed each was unused.
3. WHEN implementation starts THEN the inventory SHALL be measured again on the then-current `develop`, and the tasks SHALL be reconciled with what it shows.
4. WHEN each pull request is opened THEN the four gates SHALL have exited zero on its branch.

## Out of scope

- **Hint-level inspections.** Rider does not list them as problems. A second run counted 2455 of them, 1088 being one inspection about trailing commas, and they are named here so the number is known rather than discovered later. Whether any of them should be raised to a suggestion and so brought under this rule is a separate decision.
- **The client.** TypeScript is not covered by the rule.
- **Personal Rider settings.** Only committed settings are in scope; Requirement 3 is what moves a personal preference into the repository.
- **The other repositories.** The constitution carries the rule for them; none has a C# project today.

## Non-Functional Requirements

### Code Architecture and Modularity

- No correction introduces a nested type, moves a type out of its `_Model` folder, or changes a namespace: the rules of tech.md's *Backend* section and CLAUDE.md's *Folders and namespaces* outrank a Rider suggestion that points the other way.
- A correction that needs a new package reference, for example annotations that mark a member as used implicitly, is a design decision and is made once for the solution in `src/Directory.Packages.props`, not per project.

### Performance

- The check of Requirement 5 adds minutes to the Build workflow, not to a developer's local gates. The measured run took about four minutes after a three-minute build on the user's machine.

### Security

- None beyond the existing workflow's: the script reads source and writes a report.

### Reliability

- A red check names its findings by file and line. A check that cannot see the code says that, and is red for that reason rather than green.
- No correction changes a file whose bytes are a test subject: the fixtures marked `-text` in `.gitattributes` are not touched.

### Usability

- After this specification a developer opening the solution in Rider with the committed settings sees an empty *Problems* list for C#.
