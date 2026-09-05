# Design Document

## Overview

Nothing is built here and nothing is fixed here. This specification's own Requirement 5.3 says the scan reports and each finding is scheduled as its own change with its own guard, so this document's job is to decide **where each check lives, what shape it takes, and in what order the findings are repaired** — and to re-run the scan, because the tree moved under it.

**The one genuinely new idea is that a conformance check must discover by artifact, not by folder.** It is not a style preference; the two answers differ today. Walking `src/diagrams/` reports that `c4` carries four backend projects and two test projects, which would be the only layout deviation in sixty modules. Asking git reports two, correctly: `EtAlii.Adp.C4/` and `EtAlii.Adp.C4.Tests/` are untracked `bin`/`obj` husks left by the rename to `EtAlii.Adp.Diagram.C4`, holding no source and no `.csproj`. A check that walks the filesystem would have opened this specification with a false finding against the largest module in the tree.

The rest is ordering, six components, and one invariant that has quietly stopped being true since the requirements were written.

## The scan, re-run at `e1db2213`

The requirements scanned the tree at `469b1fbd`. Read again from `git ls-files` rather than from the filesystem:

| | At `469b1fbd` | At `e1db2213` |
| --- | --- | --- |
| Implemented modules | 11 | **12** — `causal-loop` crossed over |
| Stubs | 49 | **48**, still exactly the four-file shape, all of them |
| Layout deviations | none | **none**, across all sixty, read from git |

So Requirement 1's two properties both still hold, and the check written for them starts green. That is the useful state for a guard: it pins something true rather than reporting a backlog.

### What stopped being true: the appearance split is no longer internally consistent

The requirements recorded the split as harmless *because* it was consistent — "every module that uses the shared classes imports the sheet, and no module uses them without importing it". That sentence is now false, and it stopped being true without anything breaking.

| Module | Shared `canvas-*` classes named | Imports `canvas.css` |
| --- | --- | --- |
| `dependency-graph` | 19 | yes |
| `rdf` | 18 | yes |
| `timeline` | 18 | yes |
| `databricks` | 17 | yes |
| `sparql` | 17 | yes |
| `causal-loop` | 11 | yes — born conforming |
| `ansible-structure` | 6 | yes |
| **`c4`** | **2** | **no** |
| **`helm-charts`** | **1** | **no** |
| `azure-pipeline` | 0 | no |
| `mindmap` | 0 | no |
| `wardley-map` | 0 | no |

Two modules now compose a shared class without loading the sheet that defines it. Nothing in the shell imports `canvas.css` — only those seven `register.ts` files do — so on paper those class names resolve to no rule at all.

**In the running app they resolve anyway, and that is the point.** `diagramCanvases.ts` globs every module's `register.ts` with `eager: true`, so all twelve register at startup and the sheet is always present, loaded on another module's behalf. In a unit test the sheet is absent, but jsdom applies no CSS, so nothing there notices either. The result is markup whose appearance depends on a module it does not name, held up by a build-time glob — correct today, silently wrong the moment the last importing module is removed or lazily loaded.

That is this specification's shape a fourth time: a correct mechanism, a satisfied specification, a passing suite, and a defect living in the gap. It also makes Requirement 3's guard cheap and exact, which Component 3 takes up.

## Alignment with Steering Documents

### Technical Standards (tech.md)

- **Specifying a diagram type** — the five build steps assume a house shape. Every check here asserts a step that document already asks for; none introduces a new obligation.
- **Discovery over lists** — the backend seeds an assembly walk, the client globs `register.ts`. A conformance check that keeps a list of modules contradicts both, and goes stale on the thirteenth. Every check below discovers.

### Project Structure (structure.md)

- **Core vs module** — a check about the house shape belongs to the house. The repository-wide checks go in `EtAlii.Adp.Backend.Tests/Integration Tests/`, beside `DependencyInventory`, `DocumentationLinks`, `WorkspaceLock` and `ShapeOfFileAccess`, which already walk the tree and fail once naming every offender. The client-side check goes beside `noPrivateScrollbars` and `noPrivateLabelEditors`, for the same reason.
- **What stays module-side** — only the document-factory assertion, because Requirement 2.2 requires it to run against the module's own registration rather than against the host's catalog.

## Code Reuse Analysis

### Existing components to leverage

| Piece | Where | Used how |
| --- | --- | --- |
| `RepositoryRoot`/`Locate()` | four `Integration Tests` classes | The established walk-up from `AppContext.BaseDirectory`. Copied a fifth time rather than lifted — see *Considered and declined*. |
| Collect-then-fail-once | `DependencyInventory.Tests` | Every new check reports all offenders in one message, per the requirements' usability rule. |
| `DiagramDocumentFactories.Verify` | `EtAlii.Adp.Diagram` | Unchanged. The per-module assertion calls it; the startup check keeps calling it (Requirement 2.3). |
| `CatalogTests` | `causal-loop` test project | The finding behind Component 5, and the test that component replaces. |
| `ShapeOfSourcesTests` | `causal-loop` test project | The template for Component 4, and the guard whose gap defines it. |
| `noPrivateScrollbars.test.ts` | `client/canvas/scroll/` | The client-side module walk, and the prose shape a guard's doc-comment takes here. |

### What is not being built

No new project, no new tool, no scanner run by hand, no new mechanism in the product. Every check is a test in a project that already exists.

## Architecture

### Discovery: by artifact, not by folder

Every check below enumerates by the artifact that defines the thing:

| Thing | Found by | Not by |
| --- | --- | --- |
| A module | a folder under `src/diagrams/` holding tracked files | `Directory.GetDirectories` |
| A backend project | exactly one `*.csproj` in the folder | the folder existing |
| A module client | a `client/register.ts` | a `client/` folder |
| A stub | its tracked file set being the four-file shape | a file count |

The `.csproj` rule is what makes this robust without shelling out to git: c4's two husks contain `bin/` and `obj/` and no project file, so a check keyed on `*.csproj` cannot see them. That is strictly better than the denylist `ShapeOfSourcesTests` and `noPrivateScrollbars` both use — `bin`, `obj`, `node_modules`, `dist` — which has to be maintained and which would not have excluded these husks, since it is the *parent* folder that is stale rather than a subfolder.

### The six components, and which requirement each answers

| # | Component | Where | Requirements |
| --- | --- | --- | --- |
| 1 | Module layout and stub shape | `Backend.Tests/Integration Tests/ModuleLayout.Tests.cs` | 1.1, 1.2 |
| 2 | The document-factory assertion, made ordinary | each module's own test project | 2.1, 2.2, 2.3 |
| 3 | The appearance guard, and five migrations | `client/canvas/`, then per module | 3.1–3.4 |
| 4 | The byte guard, and the `.gitattributes` narrowing | `Backend.Tests`, `.gitattributes` | 4.1, 4.2, 4.3 |
| 5 | The catalog row, for the modules the shared check cannot see | one shared check | 1.3, 5.2 |
| 6 | The house shape written down, and the examples convention | `docs/creating-a-diagram-module.md` | 5.1, 5.2 |

Components 1, 4 and 5 are repository-wide and land once. Components 2, 3 and 6 land per module, and Requirement 3.3 forbids sweeping them.

## Components and Interfaces

### Component 1 — `ModuleLayoutTests` (new, one file, repository-wide)

Three facts, each collecting every offender before failing:

1. **Folder to project to namespace.** For each module, `backend/` holds exactly one non-test project named `EtAlii.Adp.Diagram.<PascalCase(folder)>` and exactly one `.Tests` project beside it, and every `namespace` declared inside the main project is that name or a descendant of it. Measured as starting green across all sixty modules.
2. **The stub shape.** A module whose tracked file set is not the four-file shape is implemented; a module that is neither the four-file shape nor implemented is the failure. Stated that way round because Requirement 1.2's property is about strays: forty-eight stubs, no half-started project, no stale readme.
3. **`api/` and `client/` exist and carry content** for an implemented module.

**What it deliberately does not check:** whether a module registers a toolbox, a context action provider or a validator. Three read-only modules legitimately register none, and each says so where it registers (Requirement 1.3). A guard that demanded them would be wrong three times over and would push those modules into inventing registrations to satisfy it.

### Component 2 — the document-factory assertion, per module

One `[Fact]` in each implemented module's own test project, built on the module's own `ServiceCollection`, asserting `Verify` returns nothing.

**The template is sparql's, and not "the shape sparql and wardley-map already carry".** The requirements name both; only one of them asserts. `sparql/.../Diagram.Tests.cs:129` reads

`Assert.Empty(new DiagramDocumentFactories([factory]).Verify(Diagram.Definitions));`

while `wardley-map/.../AddWardleyMap.Tests.cs:67`, under a comment that says `// Assert.`, reads

`factories.Verify(Diagram.Definitions);`

`Verify` returns the offending definitions and throws nothing, so that call discards its own answer: the test named for the rule passes whether or not the rule holds.

**The module is not unguarded, and saying so matters.** Two sibling assertions in the same class — line 37, that a `WardleyDocumentFactory` is registered, and line 55, that its origin is the module's — hold a floor between them, so deleting the factory still fails that class. What is missing is not coverage but the named check. Repairing it is one line, and it is a task of this specification rather than a fix made while writing it (Requirement 5.3).

The remaining ten modules gain the fact they lack. Where a module carries several origins — rdf's four readings, c4's seven notations, databricks' three — one fact covers all of them, because `Verify` takes the whole `Definitions` array.

### Component 3 — the appearance guard, then five migrations

**The guard, first and on its own** (`src/client/src/canvas/sharedAppearance.test.ts`): a module client that names any class defined in `canvas.css` must import `canvas.css` from its own `register.ts`. Two offenders today, `c4` and `helm-charts`; the guard is written, seen to fail on those two, and lands with their one-line imports.

It is deliberately the *weak* direction — names a class, so must load the sheet — and not the strong one. The strong rule, that a canvas must compose the shared classes, is Requirement 3.1 and cannot be a guard yet: it would fail four modules that have not migrated, and Requirement 3.3 forbids migrating them in one sweep. The weak guard is enforceable today and it is exactly the rule both new offences broke.

**Then the migrations, one module per task, in this order:**

| Order | Module | Private sheet | Why here |
| --- | --- | --- | --- |
| 1 | `mindmap` | 105 lines | Smallest, one canvas, no notation-specific chrome. The migration whose diff teaches the pattern. |
| 2 | `azure-pipeline` | 152 lines | Next smallest, and its canvas draws with the same shared box and connection components the migrated modules use. |
| 3 | `c4` | 172 lines | Already partly there. Its `StyledBoxElement` is the composite-box shape, so this migration is the one that says what a module may keep. |
| 4 | `helm-charts` | 177 lines | Read-only, so no selection or anchor rules to reconcile — but the largest set of module-own status rules. |
| 5 | `wardley-map` | 262 lines | Last, and the one most likely to end in Requirement 3.2 rather than 3.1: an evolution axis and a value chain are not a node and a connection. |

Each migration carries its canvas test asserting the composed classes, as `causal-loop`'s already does. Where a module keeps a private rule it says which shared rule it overrides and why (Requirement 3.2), in the shape `timeline.css:3` uses for the opposite direction. Where the central library lacks a shape, it is added centrally (Requirement 3.4).

### Component 4 — the byte guard, and the narrowing

This is the component the findings of 2026-09-04 reshaped, and the one whose scope the requirements set too narrowly.

**The finding, verified here rather than taken on report.** `CausalLoopSession.cs` reports `i/-text w/-text` — git classifies it as binary — and it holds no NUL byte and no control character at all. The index blob is 10393 bytes, 264 CRLF, zero bare LF, and **one lone carriage return at byte 7332, line 183**, inside `/// <inheritdoc />` followed by CR CR LF. A doubled CR: one edit's stray byte.

**What one stray byte cost.** Git's content classifier calls a buffer binary when it holds a NUL *or a lone CR*. The root `.gitattributes` says `* text=auto eol=crlf`, and `text=auto` means *normalise only what I judge to be text* — so the classification suppressed the CRLF-to-LF clean filter and the file was committed to the index with CRLF throughout. The blob at `d7c5955a~1` is 8849 bytes with 234 bare LF and no CRLF; the blob at `d7c5955a` is the 10393-byte CRLF one. The diff of that commit is a single hunk, `@@ -1,234 +1,264 @@` — the whole file rewritten, in a commit that changed part of it. Nothing behaved wrongly at any point.

**Why the existing guard cannot see it.** `ShapeOfSourcesTests.NoSourceFile_CarriesARawControlCharacter` excludes tab, CR and LF from its control-character set — correctly, since a CRLF file is full of CRs. A lone CR is invisible to it by construction. Two byte-level defects in one file, and the guard written for the first is structurally unable to catch the second. That is the argument for **one repository-wide check rather than a module-local one**, and it is a stronger argument than "twelve copies is a lot": the module-local guard was not merely un-generalised, it was incomplete, and a second copy of an incomplete check is two incomplete checks.

**The check** (`Backend.Tests/Integration Tests/ShapeOfSources.Tests.cs`, repository-wide) asserts of every tracked text-bearing source that it holds no control character outside tab, CR and LF, **and no CR that is not followed by LF**. The failure names the file, the byte offset and the line, and says what it means: git will store this file as binary, its diffs will be opaque and grep will not read it.

**Scope, decided by measurement.** Reading the whole tree's `git ls-files --eol`: 4274 tracked files, 19 classified `-text` in the index, 241 carrying an explicit `-text` attribute. Of the 18 that git calls binary while the house attribute claims them as text, **17 are genuine binaries** — seven `docs/screenshots/*.png`, six vendored `mongodb/images/*.png`, a `.gif` and three `.war` archives — and **one is a C# source file**. So a check phrased as "no tracked file may be classified `-text`" starts with seventeen findings that are not defects, while the same check phrased as a byte rule over source extensions starts with exactly one. The byte rule is the check; marking the seventeen binaries in `.gitattributes` is a separate, optional tidy-up scheduled after it.

**The `crlf-line-endings` / `lf-line-endings` pair (Requirement 4.3).** One shared check discovers every such pair by name across the tree and asserts the two files still differ on disk. Discovered, not listed, so a module that adds a pair is covered without editing the check.

**The narrowing of the two bare `* -text` files (Requirement 4.2), and a deviation from 4.1.** They freeze 65 files between them — 39 under ansible-structure's fixtures, 26 under helm-charts' — including the five ordinary markdown files the root `.gitattributes` predicted: two `readme.md`, a `README.md`, `well-formed/notes-for-humans.md` and `infrastructure/docs/runbook.md`. None of the 65 currently holds CRLF in the index, so the narrowing changes no index bytes and produces no whole-file diffs.

Requirement 4.1 offers two mechanisms: by extension, or by a glob naming those extensions. **For these two modules neither works, and the design uses a third.** Both parse a *directory tree* rather than a document. Ansible-structure's byte-snapshotted fixtures are `.yml`, `.cfg`, `.j2`, `.sh` and extensionless files named `hosts`, `dbservers`, `webservers`; helm-charts' are `.yaml`, `.tpl`, `.lock`, `.txt`, a `.tgz` and `Chart.yaml`. There is no extension whose bytes are the subject, because the subject is the tree — and an extensionless file cannot be named by extension at all.

The narrowing is therefore **subtractive**: keep `* -text`, and re-exempt the ordinary text after it, since `.gitattributes` resolves by last match.

    * -text
    *.md text=auto eol=crlf

Verified in a throwaway repository rather than reasoned about: with those two lines, `readme.md` and `well-formed/notes-for-humans.md` come back to `attr/text=auto eol=crlf` at any depth while `values.yml` and `hosts` stay `-text`. This satisfies what Requirement 4.2 is actually protecting — readmes and recorded verdicts are ordinary text — by the only mechanism available to a tree-subject module, and it is recorded here as a deviation from 4.1's two rather than presented as one of them. The nested `.gitattributes` files match their own `* -text`; exempting them too is part of the same edit.

### Component 5 — the catalog row, for the modules the shared check cannot see

`DiagramDiscoveryStartupTests` selects definitions with `Build is null`, and its own comment explains why that scoping is right: a definition that registers services may legitimately annotate its catalog row beyond the plain title. The consequence is that **sixteen definitions across the twelve implemented modules sit outside the repository's catalog check** — one each for eleven modules and four for rdf — and only `causal-loop` guards its own row, in a test written after its author deleted the row and watched the shared check stay green at 8 passed.

That is the right finding reached the right way, and copying `CatalogTests` eleven more times is the wrong answer to it: the shared check's own comment records that forty-eight per-module origin assertions were collapsed into one for exactly this reason. So Component 5 is **one additional fact in the shared check**, over the complementary population — every definition that *does* carry a `Build` has a row in `docs/diagrams.md` carrying its origin tag, a state from the legend, and a link to its specification. The title is deliberately not asserted for this population, because annotating it is the reason they were excluded in the first place.

`causal-loop`'s own `CatalogTests` is then redundant and is removed by the same task, with its reasoning moved into the shared check's doc-comment so the deletion experiment that justified it is not lost.

### Component 6 — the house shape, written down, and the examples convention

`docs/creating-a-diagram-module.md` gains the shape as an author meets it: the folder layout, the one-project-plus-one-test-project rule, the four-file stub, the read-only variant three modules use, and the appearance rule with its two directions. Each addition points at the check that enforces it, so a reader who breaks a rule meets the failure and the prose together.

**The examples convention** is one folder per corpus, with the reading level only in the showcase. Two findings, both scheduled rather than fixed:

- **The two-folder move.** `src/diagrams/rdf/examples/shacl/` is a reading-level folder holding two corpora, `fair-data-point` and `w3c-shacl`, while every other corpus in that module — `nobel`, `owl-time`, `prov-o`, `stw`, `w3c-turtle`, `wikidata` — sits directly under `examples/`. Nothing in `src/`, `docs/` or the module's own readme references the `examples/shacl/` path; the only mentions are in historical `.spec-workflow` snapshots, which are records and are not edited. So the move is two `git mv`s and one readme edit.
- **Six placeholder folders.** `example 1`, `example 2`, `example-1` and `example-2` in ansible-structure, azure-pipeline, dependency-graph, mindmap, timeline and wardley-map — four of them with a space in the folder name. These are not corpora: `wardley-map/examples/example 2` holds that module's entire edge-case suite, including the `crlf-line-endings.owm` / `lf-line-endings.owm` pair Component 4 discovers. Renaming them to what they are is one task per module, scheduled last, after the checks that would otherwise be reading paths that moved.

## Data Models

None. Every check reads files and definitions that already exist, and adds no type to the product.

## Error Handling

| Scenario | Handling |
| --- | --- |
| A check cannot locate the repository root from the test output | Skip with a message naming what it looked for, as `CatalogTests` already does — not a silent pass, and not a failure on a machine with an unusual output path. |
| A module folder holds no tracked files | Not a module; skipped. Keeps a half-deleted folder from failing the layout check. |
| A byte offence is found | Fail naming file, byte offset, line and consequence. Collect every offender first. |
| A module legitimately deviates | Stated at the point of decision in the module (Requirement 1.3), never by adding the module to a check's exclusion list. |

## Testing Strategy

### Verification by sabotage

Every check here is a guard, so each is perturbed and seen to fail for the right reason before being accepted.

Two are already proven and their evidence is reused rather than re-derived: the NUL-byte guard was sabotage-verified when it was written, and the catalog finding came from deleting the row and watching the shared check stay green. The lone-CR half of Component 4 is proven against `CausalLoopSession.cs` itself, which is a live offender today — the strongest form available, since it is a real defect rather than an injected one. It must be seen to fail on the tree as it stands and to pass once the doubled CR is removed.

**The trap this specification is most likely to fall into** is a check that starts green and was never seen red. Components 1 and 5 both start green by construction, which is the point of them, so neither may be accepted on a passing run: Component 1 is proven by renaming a project folder in a scratch copy, Component 5 by deleting a catalog row.

### Coverage

The mechanical requirement-coverage diff runs against the tasks document before implementing and against the finished code afterwards. Applied at this phase, against the design rather than the tasks, every acceptance criterion of Requirements 1 through 5 is addressed by a component above, with three worth naming because their treatment is not the obvious one:

- **4.1** is deviated from for two modules, with the reason and the substitute mechanism recorded in Component 4.
- **3.1** is not made a guard — only prose plus the five migrations; the guard is the weaker rule. Component 3 says why, and the tasks document says the same rather than leaving the diff to report an unclaimed criterion.
- **5.3** is a property of how the tasks are shaped rather than something a task can claim. It is a *no task can claim it* rather than a *no task claimed it*, and the tasks document states that in those words.

## Considered and declined

**Lifting `RepositoryRoot` into a shared test helper.** Four classes already carry the same walk-up from `AppContext.BaseDirectory`, and more arrive here. Declined for now: the copies are six lines each, they are the reason these classes have no shared base to go stale, and a `.Tests` project in a module folder cannot reference a helper in `Backend.Tests` without a project reference that would drag the host's test dependencies into every module. If it is lifted it belongs in `EtAlii.Adp.Diagram` as production code that tests happen to use, which is a larger decision than this specification should make on the way past.

**A single "module conformance" test class holding all of it.** Declined. The four checks fail for unrelated reasons, and one class means one failing test name for a layout break, a byte break and a catalog break alike. The existing `Integration Tests` folder already answers this by keeping one class per convention.

**Making the appearance rule a strong guard now, with the four un-migrated modules on an exclusion list.** Declined, and this is the one that will be reopened. An exclusion list is a second copy of the finding, kept in the guard rather than in this document, and it goes stale in the direction that matters: a module removed from the list is a migration, but a module *added* to it is a regression that looks like maintenance. The weak guard has no list.

**Fixing the doubled CR, the wardley assertion or the two `.gitattributes` files while writing this.** Declined by Requirement 5.3, which is the requirement most likely to be quietly broken by whoever finds a one-character fix while drafting. Each is a task with a guard that fails first.

## Deviations and notes

**Twelve implemented modules, not eleven.** `causal-loop` landed between the requirements' scan and this design, and it is the first module built after the shared appearance layer existed: it composes eleven shared classes and imports the sheet without anyone telling it to. That is evidence for what these checks are for — the conventions are learnable by imitation when the module being imitated conforms, and the checks exist for when it does not.

**The measurement behind the appearance table** counts distinct shared classes named anywhere in a module's client, markup and stylesheet alike, intersected with the nineteen `canvas-*` selectors defined in `canvas.css`. A module scoring zero is not necessarily drawing nothing shared; it is naming nothing shared.

**Working-tree line-ending drift, measured because the question was asked.** 933 tracked files sit LF in the working tree under an `eol=crlf` attribute. They are not spread across file types: 633 are dashboard-written files under `.spec-workflow/`, 298 are vendored example corpora, and exactly **two** are ours — `src/client/src/generated/deltas_pb.ts`, generated by buf, and `src/diagrams/mindmap/examples/example 1/mindmap.adp`. The index holds LF for all of them, which is the house policy, so none of this is index damage. The answer to *which file types silently drift because no gate reads them* is that our own authored source barely drifts at all; the drift is concentrated in files no human writes. A repository-wide working-tree gate would therefore fail on 931 files nobody authored, which is why no such gate is proposed here.

**One file reports `i/none`** — `src/backend/.idea/.idea.EtAlii.Adp/.idea/.name`, which holds no line ending at all. It is IDE-written, it is harmless, and it is recorded only so the next person reading `git ls-files --eol` does not spend an hour on it.
