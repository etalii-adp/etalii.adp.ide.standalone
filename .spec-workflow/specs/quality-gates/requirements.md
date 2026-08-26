# Requirements Document

## Introduction

This spec collects the loose ends left behind by `c4-diagrams`, and it turns out they are all
the same kind of thing. Each item below is a **guard that does not guard**: a check that exists,
is documented, and is believed to protect something — but which currently protects nothing,
because it fails, skips, under-reports, or was never run.

| Guard                                                               | Why it does not guard today                                                                                                         |
| ------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| `dotnet format style --verify-no-changes --severity info`           | Exits non-zero on 109 `IMPORTS` errors before it reaches anything else. A check that always fails is a check nobody runs.           |
| `C4InteropTests` (18 tests)                                         | Skips unless `ADP_STRUCTURIZR_CLI` is set. In a normal `dotnet test` run it guarantees nothing.                                     |
| `C4RuleSet`'s C4 conformance rules                                  | Two of them were narrowed on a mistaken inference, so they now under-report exactly the problems Structurizr's own inspector flags. |
| The Structurizr Lite rendering check in `tests.md`                  | Has never been run. It was recorded as impossible, on a reason that was false.                                                      |
| "Create a worktree per piece of work and merge it back" (CLAUDE.md) | Nothing retires them. Twelve exist; most are merged, three are on detached HEADs.                                                   |

The unifying requirement is therefore not "fix these five things" but: **a check that is not
currently protecting something must either be made to protect it, or be honestly retired.** A
check kept in a permanently-failing or permanently-skipped state is worse than no check, because
it advertises a guarantee the repository does not actually have.

### How these were found, and why one of them is a correction rather than a gap

Four of the five were found by finishing `c4-diagrams` task 39, which installed a JDK and ran the
real Structurizr CLI for the first time. The fifth — the rule narrowing — is different in kind,
and it is worth being precise about, because this spec exists partly to correct it.

While building the `big-bank-plc.dsl` fixture, three of ADP's C4 rules fired on C4's own worked
example. The argument used at the time was: *the worked example is what C4 holds up as done
properly, so a rule that fires on it is ADP's rule being wrong.* On that basis
`c4.missing-description` was narrowed to exclude deployment nodes, `c4.missing-protocol` was
narrowed to container-to-container only, and `c4.mixed-abstraction-levels` was deleted outright.

**That argument was wrong.** The Structurizr CLI has a second command besides `validate` —
`inspect` — which runs Structurizr's own model-quality rules. Run against the same worked
example, it reports **26 findings**, including `model.deploymentnode.description` on precisely
the deployment nodes that prompted the first narrowing, and `model.relationship.technology` on
precisely the relationships that prompted the second:

```
ERROR | model.deploymentnode.description | The deployment node "Live/Big Bank plc/bigbank-web***" is missing a description.
ERROR | model.relationship.technology    | The relationship between the component "...Sign In Controller" and the component "...Security Component" is missing a technology.
```

So the worked example is authoritative about **syntax** — `validate` accepts it, which is what
made it a good round-trip fixture — but it is not a model Structurizr itself considers clean.
"The example does not do X" was treated as "X is not required", and those are different claims.
Two of the three narrowings were therefore regressions in rule coverage, made on bad evidence.

The third — removing `c4.mixed-abstraction-levels` — stands: Structurizr's inspector has no such
rule either, so on that one the reasoning happened to reach the right answer by a bad route.

The lesson generalises past C4, and Requirement 2 exists to record it: when ADP takes a position
on what "correct" means for a format someone else owns, the owner's own tooling is the authority,
not our inference from one document they published.

## Alignment with Product Vision

* **"Don't reinvent, integrate"** (product.md, Product principles). ADP embraces the Structurizr
  DSL rather than inventing a format. Requirement 1 extends that from the *file format* to the
  *rules about what a good model is* — currently ADP has its own opinion on model quality that
  silently disagrees with the ecosystem it claims to join. Integration that stops at the file
  extension is not integration.
* **"Files are the source of truth"** (product.md). Requirement 3's design constraint — that the
  everyday test run must not need a JDK — is met by committing Structurizr's verdicts as
  reviewable fixture files rather than by requiring a live tool, which is the same principle
  applied to test data.
* **"Tests should be runnable as part of the same local F5 experience — no separate environment
  or manual setup required"** (tech.md, Testing & quality). This is a hard constraint on
  Requirement 3, not a preference. It rules out making the JDK mandatory, and it is why the
  requirement is written as "the guarantee holds in a plain `dotnet test`" rather than "install a
  JDK in CI".
* **"Prefer fast, local unit/integration tests over end-to-end tests that depend on hosted
  infrastructure"** (tech.md). Requirement 4 closes a *manual* check by automating it locally,
  rather than by standing up Structurizr Lite in a pipeline.
* **"Architectural artifacts are intended to be part of the solution repositories, and adhere to
  the applied version control principles"** (product.md, mantra). Requirement 6 is the
  housekeeping half of that: the repository's own working state should be as tidy as the
  artifacts it asks users to keep.

## Requirements

### Requirement 1 — ADP's C4 rules agree with Structurizr's inspector wherever both have an opinion

**User Story:** As an architect editing a C4 model in ADP, I want ADP's warnings to match what
the C4 ecosystem's own tooling would tell me, so that a model ADP calls clean does not turn up a
page of findings the first time a colleague runs Structurizr over it.

#### Context: the current gap

Structurizr's `inspect` command exposes at least 14 model-quality rules (observed across this
repository's fixture corpus plus a targeted probe). ADP's `C4RuleSet` has 10. They overlap
partially, and the mismatch runs in both directions:

| Structurizr rule                       | ADP's equivalent                                 | State                                              |
| -------------------------------------- | ------------------------------------------------ | -------------------------------------------------- |
| `model.person.description`             | `c4.missing-description` (Person)                | Covered                                            |
| `model.softwaresystem.description`     | `c4.missing-description` (SoftwareSystem)        | Covered                                            |
| `model.container.description`          | `c4.missing-description` (Container)             | Covered                                            |
| `model.container.technology`           | `c4.missing-technology`                          | Covered                                            |
| `model.deploymentnode.description`     | —                                                | **Regression** — was covered, narrowed away        |
| `model.relationship.technology`        | `c4.missing-protocol` (container→container only) | **Regression** — narrowed from a much broader rule |
| `model.deploymentnode.technology`      | —                                                | Gap                                                |
| `model.infrastructurenode.description` | —                                                | Gap                                                |
| `model.infrastructurenode.technology`  | —                                                | Gap                                                |
| `model.element.disconnected`           | —                                                | Gap                                                |
| `model.element.noview`                 | —                                                | Gap (ADP has the inverse, `c4.empty-view`)         |
| `workspace.scope`                      | —                                                | Gap                                                |
| `model.softwaresystem.documentation`   | —                                                | Gap; ADP has no documentation concept              |
| `model.softwaresystem.decisions`       | —                                                | Gap; ADP has no decision-record concept            |

ADP also has rules Structurizr's inspector has no equivalent for, and these are **not** to be
removed — they guard ADP's editing surface rather than the model's quality:
`c4.kind-not-permitted-on-view`, `c4.misplaced-element`, `c4.include-not-followed`,
`c4.unknown-view-scope`, `c4.dangling-relationship`, `c4.empty-view`,
`c4.unlabelled-relationship`.

#### Acceptance Criteria

1. WHEN the reconciliation is complete THEN the system SHALL, for every rule in the table above,
   either implement an ADP equivalent or record in the design an explicit, reasoned decision not
   to — and "not yet" SHALL NOT be recorded as a decision.
2. WHEN a deployment node has no description THEN the system SHALL report a problem, restoring the
   coverage removed during `c4-diagrams` task 1.
3. WHEN a relationship between any two elements has no technology THEN the system SHALL report a
   problem, widening `c4.missing-protocol` from its current container-to-container scope to match
   `model.relationship.technology`.
4. IF widening a rule per criteria 2 or 3 makes an existing fixture or test report problems THEN
   the system SHALL treat that as the rule working, and the fixture expectation SHALL be updated
   to the new, correct verdict rather than the rule being re-narrowed to keep the test quiet.
5. WHEN an element participates in no relationship at all THEN the system SHALL report a problem
   equivalent to `model.element.disconnected`.
6. WHEN an element appears on no view THEN the system SHALL report a problem equivalent to
   `model.element.noview`.
7. WHEN a rule is implemented to match a Structurizr rule THEN its `RuleId` SHALL record the
   correspondence, so a reader can trace an ADP problem back to the Structurizr rule it mirrors.
8. IF a Structurizr rule depends on a concept ADP does not model — `model.softwaresystem.
   documentation` and `model.softwaresystem.decisions` depend on `!docs`/`!adrs`, which ADP
   round-trips but does not read — THEN the system SHALL record that dependency as the reason for
   not implementing it, naming what would have to exist first.
9. WHEN ADP's severity for a mirrored rule differs from Structurizr's THEN the design SHALL state
   why. ADP reports these as warnings; Structurizr's CLI prints them all as `ERROR`. These are
   recommendations rather than syntax errors, so ADP's warning severity is expected to stand —
   but the divergence SHALL be a decision on the record rather than an accident.
10. WHEN the rules have been reconciled THEN a test SHALL assert the correspondence itself — for
    each fixture, that ADP's reported problem set and Structurizr's reported finding set agree on
    the rules that are claimed to be mirrored — so the two cannot drift apart silently.

### Requirement 2 — The record of why each rule changed is corrected, not quietly rewritten

**User Story:** As a developer reading why a rule was removed, I want the record to show the
reasoning that was actually used and where it failed, so that I do not repeat it.

#### Acceptance Criteria

1. WHEN the reconciliation lands THEN `design.md`'s deviations section for `c4-diagrams` SHALL be
   corrected: the entries asserting that `c4.missing-protocol` and `c4.missing-description` were
   over-reaching SHALL be replaced with the finding that the narrowing was itself the error.
2. WHEN a previously-recorded reason is found to be wrong THEN it SHALL be corrected in place with
   the correction visible, and SHALL NOT be silently deleted — matching how the "the sandbox
   filters `structurizr`" error was handled.
3. WHEN `Fixtures/readme.md`'s defect table is updated THEN the two entries attributing
   over-reach to `c4.missing-description` and `c4.missing-protocol` SHALL be restated as what they
   were: correct rules narrowed on bad evidence.
4. IF the `c4.mixed-abstraction-levels` removal survives review THEN the record SHALL note that it
   stands on Structurizr's inspector having no equivalent rule, rather than on the worked-example
   argument that was used at the time and has since been shown unsound.
5. WHEN the design records the general lesson THEN it SHALL be stated in a form that applies to
   future diagram types, not only to C4: for a format ADP does not own, the owner's tooling is the
   authority on what "correct" means, and a published example demonstrates what is *permitted*,
   never what is *sufficient*.

### Requirement 3 — The interoperability guarantee holds in a plain `dotnet test`, with no JDK

**User Story:** As a developer running the test suite normally, I want ADP's interoperability
claim to be checked, so that "another tool can read what ADP wrote" is a guarantee rather than
something that was true on one machine, once.

#### Context

`C4InteropTests` contributes 18 tests. Without `ADP_STRUCTURIZR_CLI` set, all 18 skip. The full
backend run reports `succeeded: 1116, skipped: 18` — the guarantee is advertised in the count and
delivered by none of them. tech.md forbids the obvious fix: a mandatory JDK would be exactly the
"separate environment or manual setup" the steering rules out.

#### Acceptance Criteria

1. WHEN `dotnet test --solution EtAlii.Adp.slnx` runs on a machine with no JDK THEN the system
   SHALL still verify that ADP's output matches Structurizr's recorded verdict, without invoking
   Java.
2. WHEN Structurizr's verdict on a document is needed by the everyday suite THEN it SHALL come
   from a committed, human-readable baseline file that is reviewable as a diff, rather than from a
   live tool invocation.
3. WHEN a baseline file is committed THEN it SHALL record which CLI version produced it and when,
   so a stale baseline is identifiable.
4. WHEN a JDK and CLI *are* available THEN a test SHALL regenerate the verdicts and assert the
   committed baselines are still current, so a baseline cannot rot into a fiction.
5. IF ADP's output changes such that Structurizr's verdict would change THEN the everyday suite
   SHALL fail, naming the document and the divergence.
6. WHEN a check genuinely cannot run without the CLI THEN it SHALL skip with a message naming the
   exact environment variable and command needed, and the count of such skips SHALL be visible in
   the run's summary rather than buried.
7. IF the number of environment-gated skips grows THEN the design SHALL state a ceiling and the
   reasoning for it, so "skipped" does not quietly become the normal state of this suite again.
8. WHEN the CLI is obtained THEN the documented route SHALL be recorded (Maven Central
   coordinates or the GitHub release), so the next person does not repeat the failed search that
   produced the false "unreachable" conclusion.

### Requirement 4 — Structurizr renders what ADP writes, checked automatically

**User Story:** As an architect, I want to know that the views ADP declares actually draw in
Structurizr's tooling, not merely that the file parses, so that interoperability means a usable
diagram rather than an accepted file.

#### Context

`tests.md` carries a manual check — open an ADP-authored `.dsl` in Structurizr Lite and confirm
every view renders. It has never been executed. Parsing and rendering are genuinely different
guarantees: a document can parse cleanly and still declare a view that draws nothing, which is
exactly the class of defect the `c4/deployment` template had (an empty `deploymentEnvironment`
parsed, and the view bound to nothing).

The CLI's `export` command renders a workspace to `plantuml`, `mermaid`, `dot`, `ilograph` or
`json` without Lite, Docker, or a browser.

#### Acceptance Criteria

1. WHEN the CLI is available THEN the system SHALL export every fixture and every ADP-authored
   test document to at least one diagram format, and SHALL assert the export succeeds.
2. WHEN a view is exported THEN the system SHALL assert the output actually contains that view's
   elements, so a view that renders empty is caught rather than counted as a success.
3. WHEN every view ADP can declare is covered by an export assertion THEN the manual Structurizr
   Lite entry in `tests.md` SHALL be retired, and the entry SHALL say what replaced it.
4. IF some aspect of rendering genuinely cannot be asserted from an export — visual styling, theme
   resolution, layout quality — THEN that residue SHALL remain in `tests.md` as a manual check,
   narrowed to only what is genuinely visual, rather than the whole check being kept.
5. WHEN the export check runs THEN it SHALL be subject to the same no-JDK constraint as
   Requirement 3: the everyday suite SHALL check against committed baselines, and the live export
   SHALL verify those baselines are current.

### Requirement 5 — The backend style gate passes, so it can start gating

**User Story:** As a developer, I want the style command CLAUDE.md tells me to run to exit clean
on an unmodified checkout, so that any output I see is about my own change.

#### Context

`dotnet format style --verify-no-changes --severity info` (from `src/backend/`, against
`EtAlii.Adp.slnx`) currently reports, across 192 files:

| Diagnostic                                                                  | Count   | Nature                                                                                                                                                                            |
| --------------------------------------------------------------------------- | ------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `IMPORTS`                                                                   | 109     | Using directives not sorted/grouped. Mechanically fixable.                                                                                                                        |
| `IDE0130`                                                                   | 84      | Namespace does not match folder. Conflicts with a deliberate repo convention. \=> Check with rider style check (InspectCode) as rider has metadata on folder namespace providers. |
| `IDE0046`                                                                   | 17      | `if` convertible to a conditional expression. Judgement per site.                                                                                                                 |
| `IDE0330`, `IDE0059`, `IDE0270`, `IDE0066`, `IDE0053`, `IDE0032`, `IDE0017` | 9 total | Singletons.                                                                                                                                                                       |

Two of these are not ordinary debt but structural disagreements:

**`IMPORTS` is caused by a self-contradicting config.** `src/.editorconfig` carries this, on
consecutive lines:

```ini
# Organize usings
# Commented out: dotnet_separate_import_directive_groups (blank line between
# using-directive groups by root namespace) currently fires on ~14 of ~20 .cs
# files, since usings here aren't grouped that way today.
dotnet_separate_import_directive_groups = true
```

The note says the rule is commented out and explains why; the very next line enables it. The
comment's own reasoning — that it fires on most files — is the reason the gate fails today. This
is exactly the failure mode CLAUDE.md's "comment it out with a note explaining why" convention was
meant to prevent, and it shows the convention needs a check of its own.

**`IDE0130` is a rule fighting a convention.** `dotnet_style_namespace_match_folder = true`
requires `_Model/MindmapNode.cs` to live in `...Mindmap._Model`, but the repository deliberately
keeps `_Model/` and `Commands/` subfolders in the parent namespace. Structure.md prescribes the
project layout and says nothing requiring namespaces to follow subfolders. Folder 2 namespace mappings need to be validated using Jetbrains Rider 'InspectCode', as the Rider metadata files have namespace providers assigned to some folders but not all.&#x20;

#### Acceptance Criteria

1. WHEN `dotnet format style --verify-no-changes --severity info` is run against an unmodified
   checkout THEN it SHALL exit zero.
2. WHEN a diagnostic is resolved THEN it SHALL be resolved by exactly one of two routes, chosen
   per diagnostic and recorded: the code is brought in line, or the rule is downgraded/disabled in
   `src/.editorconfig` with a note giving the reason — CLAUDE.md's existing convention.
3. WHEN a rule is downgraded THEN the note SHALL state what the rule wanted, what the codebase
   does instead, and why the codebase's way was kept — sufficient for a reader to disagree with
   the decision on its merits.
4. WHEN the `dotnet_separate_import_directive_groups` contradiction is resolved THEN the note and
   the setting SHALL agree, whichever way the decision goes.
5. IF `IMPORTS` is resolved by fixing the code THEN the reformatting SHALL be committed separately
   from any behavioural change, so it does not obscure review of anything else.
6. IF `IDE0130` is resolved by downgrading THEN the note SHALL record the `_Model/`- and
   `Commands/`-in-parent-namespace convention as the reason, and structure.md SHALL be updated to
   state that convention explicitly, since it is currently only implicit in the code.
7. WHEN the gate is green THEN a check SHALL exist that keeps it green, and the design SHALL name
   what that check is. This repository has no `.github/workflows`, so the design SHALL NOT assume
   a CI pipeline exists; if the answer is a documented pre-merge step, that SHALL be stated as
   such rather than dressed up as automation.
8. WHEN the client's conventions are considered THEN this requirement SHALL apply to backend C#
   only. CLAUDE.md records that the client has no ESLint/Prettier config and was checked by hand;
   establishing a client gate is explicitly out of scope here (see Out of scope).

### Requirement 6 — A worktree is retired when its work merges

**User Story:** As a developer opening this repository, I want the worktrees present to be the
ones with live work in them, so that `git worktree list` tells me what is in flight.

#### Context

CLAUDE.md requires a dedicated worktree per piece of work and says to merge it back when done. It
says nothing about removing it afterwards, and nothing does. Twelve exist:

```
actions-as-commands, adp-logo-icon-proposals-a0e0f3, c4-diagrams,
centralized-package-versions-29f938 (detached), csharp-settings-centralize-79e450,
diagram-undo-redo, diagram-workspace-tabs, mindmap-diagram,
mindmap-diagram-spec-db3688 (detached), post-c4-cleanup,
rename-files-folders-6fdadd (detached)
```

Most correspond to specs that are complete and merged. Three are on detached HEADs, so it is not
even apparent from the listing what branch they represent. One — `post-c4-cleanup` — has an
unmerged commit and is genuinely live.

#### Acceptance Criteria

1. WHEN a worktree's branch has been merged into `develop` and its working tree is clean THEN it
   SHALL be removed, along with its branch if the branch has no other purpose.
2. WHEN a worktree has uncommitted changes or unmerged commits THEN it SHALL NOT be removed, and
   the triage SHALL record what it is holding and who to ask.
3. WHEN the triage runs THEN each of the twelve existing worktrees SHALL be classified as
   *removed*, *kept with a stated reason*, or *needs an owner's decision*, and the classification
   SHALL be recorded rather than performed silently.
4. IF a worktree is on a detached HEAD THEN it SHALL be resolved to a named branch or removed,
   because a detached worktree cannot be reasoned about from the listing alone.
5. WHEN the convention is settled THEN CLAUDE.md SHALL state the retirement step alongside the
   existing creation rule, so the lifecycle is documented end to end rather than half of it.
6. IF removal of any worktree would destroy work THEN it SHALL be raised for a decision rather
   than removed — this requirement is housekeeping, and housekeeping must not be destructive.

## Non-Functional Requirements

### Code Architecture and Modularity

* **Single Responsibility Principle**: the rule set stays a pure function from workspace to
  problems, as `C4RuleSet` is today. New rules are added inside that shape; none may acquire an
  I/O dependency, and in particular no rule may shell out to the CLI.
* **Modular Design**: baseline files and their comparison live with the C4 test project, not in
  shared test infrastructure. Nothing outside `src/diagrams/c4/` needs to know Structurizr exists.
* **Dependency Management**: no managed or unmanaged dependency on Structurizr's Java libraries is
  introduced. The CLI is an external tool invoked by an opt-in test, never a build or runtime
  dependency — consistent with the existing decision to hand-write the DSL parser.
* **Clear Interfaces**: rules mirroring Structurizr rules are identifiable as such through the
  contract (`RuleId`), not through a comment alone.

### Performance

* The everyday suite must not regress noticeably. Reading committed baselines is file I/O
  comparable to the existing fixture reads; the current C4 project runs 324 tests in \~0.23s
  without the CLI, and that order of magnitude should hold.
* The CLI-backed tests may be slow — they are \~11s today, dominated by JVM startup — and this is
  acceptable precisely because they are opt-in.

### Reliability

* Widening a rule must not change how a document is *parsed or written*. Rules are advisory;
  Requirement 3's byte-identical round-trip guarantee from `c4-diagrams` is untouched by this spec
  and must remain so.
* A missing, unreadable or stale baseline must degrade the same way the layout sidecar does: it
  costs the check, never the test run's ability to execute. A corrupt baseline must produce a
  clear failure naming the file, not an unhandled exception.

### Usability

* A problem ADP reports must be actionable at the point it appears: an element id or line, as the
  existing `DiagramProblemLocation` already provides, so the errors-and-warnings panel can select
  the offending element.
* Widening rules will increase the number of warnings on existing models — `model.element.noview`
  and `model.element.disconnected` in particular are noisy on partial models. The design must
  address whether a newly-created empty diagram trips its own warnings the moment it is created,
  because a tool that greets a new file with warnings teaches users to ignore warnings.

### Security

* No new network access. The CLI is obtained by a developer deliberately, not fetched by the build
  or by a test.

## Out of scope

Named explicitly so that "not mentioned" is not mistaken for "forgotten":

* **`c4/code`.** It stays 📝 Specified. C4 itself discourages the Code level as IDE-generated, and
  the `c4-diagrams` tasks record the deferral with its reasoning. Nothing here changes that.
* **A client-side style gate.** CLAUDE.md records that the client has no ESLint/Prettier config
  and that its conventions were verified by hand. Establishing an automated client gate is a
  separate piece of work with its own tooling decision; Requirement 5 is backend C# only.
* **Setting up CI.** The repository has no `.github/workflows` today. Requirement 5.7 asks the
  design to name what keeps the gate green *given that*, which may well be a documented local
  step. Introducing a pipeline is a larger decision that should not ride in on a cleanup spec.
* **The surviving `c4.missing-protocol` warning on `big-bank-plc.dsl`.** Requirement 1.3 widens
  the rule, so this warning will be joined by others on that fixture; the fixture's expectation
  becomes "these specific findings, which Structurizr also reports" rather than "clean". No
  separate decision is needed.
* **Re-approving `c4-diagrams`' requirements.md.** Requirement 7.7 of that spec states a rule this
  work has shown to be wrong. Amending an approved requirements document is a process question for
  its own moment; Requirement 2 records the correction in the design and code, which is where a
  reader looking at the rule will actually land.

## Open questions for review

1. **Should `model.element.noview` and `model.element.disconnected` be warnings on a
   work-in-progress model?** Both fire constantly on a model being built up incrementally. Options
   are to report them always, to report them only on a model that already has views, or to give
   them a lower severity than the rest. This affects whether the warnings panel is useful or
   ignored, and it is a product judgement rather than a technical one.
2. **Is `workspace.scope` worth mirroring?** It is a Structurizr concept about workspace metadata
   rather than a C4 concept. ADP creates one workspace per document and may have no meaningful way
   to set a scope, in which case mirroring the rule would produce a warning users cannot act on.
3. **Should this be one spec or two?** Requirements 1–4 are C4/Structurizr alignment; Requirements
   5–6 are repository hygiene that merely surfaced during that work. They share the "guard that
   does not guard" theme but little else, and they could be split if that makes review or
   scheduling easier. Kept together here because they were found together and are individually too
   small to justify their own workflow.