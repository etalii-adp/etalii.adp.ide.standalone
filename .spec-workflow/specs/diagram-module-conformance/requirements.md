# Requirements Document

## Introduction

This spec covers **the house shape of a diagram module** — what `src/diagrams/<module>/` must contain, register and reuse to be a conforming member of the family — and turns the conventions that eleven implemented modules already agree on into rules a build can check. It adds no diagram type and changes no behaviour a user sees.

It exists because the conventions are currently held by prose and by imitation. Where a rule has a guard, the guard has held: every one of the twenty document-sibling definitions in the tree has its factory today. Where a rule has no guard, the tree has drifted quietly and in ways nobody chose — two stylesheet regimes, and a `.gitattributes` pattern living on inside two modules after the root file recorded withdrawing it.

**The scan behind this document.** All sixty folders under `src/diagrams/` were read at `469b1fbd`: eleven are implemented, forty-nine are stubs. Findings are cited as `path:line` and split into defects and deviations-with-reason, because most of what looks irregular here was decided deliberately and said so at the point of decision.

## What the scan found

### The population

| State | Count | What it is |
| --- | --- | --- |
| Implemented | 11 | `ansible-structure`, `azure-pipeline`, `c4`, `databricks`, `dependency-graph`, `helm-charts`, `mindmap`, `rdf`, `sparql`, `timeline`, `wardley-map` |
| Stub | 49 | Exactly four files each: `api/readme.md`, `client/readme.md`, `backend/<Project>/Diagram.cs`, `backend/<Project>/<Project>.csproj` |

The stubs are **uniform**: every one of the forty-nine has exactly those four files, no strays, no stale readme, no half-started project. Nothing in the empty half of the tree will mislead the next person.

Naming is uniform too, across all eleven implemented modules: folder `kebab-case` maps to project and namespace `EtAlii.Adp.Diagram.<PascalCase>`, each with exactly one `.Tests` project beside it. No module deviates.

### Defects

**D1 — the withdrawn directory rule lives on inside two modules.** `.gitattributes:47-52` records that `Fixtures/** -text` was tried first and withdrawn, because it also catches readmes and recorded verdicts, "marking those `-text` would freeze CRLF into the index for files where the house style should simply apply". Two modules nevertheless carry exactly that pattern, one step further:

- `src/diagrams/ansible-structure/backend/EtAlii.Adp.Diagram.AnsibleStructure.Tests/Fixtures/.gitattributes:1` — `* -text`
- `src/diagrams/helm-charts/backend/EtAlii.Adp.Diagram.HelmCharts.Tests/Fixtures/.gitattributes:1` — `* -text`

They catch what the root file predicted: `helm-charts/.../Fixtures/readme.md`, `ansible-structure/.../Fixtures/readme.md`, `.../well-formed/notes-for-humans.md`, `.../infrastructure/docs/runbook.md`. The root file knows they are there and says "Left where their authors put them" (`.gitattributes:97-100`) because both specs were in flight. They are no longer in flight.

Note the difference from the third nested file, `src/diagrams/wardley-map/backend/EtAlii.Adp.Diagram.WardleyMap.Tests/Fixtures/.gitattributes:1`, which says `*.owm -text` — scoped to the extension whose bytes are the subject, and conforming to the root reasoning exactly.

**D2 — two appearance regimes, and the split is invisible from inside either.** Six modules adopted the shared appearance layer: they compose the nineteen `canvas-*` classes defined in `src/client/src/canvas/canvas.css` and import that sheet from their own `register.ts`. Five have not, use none of the shared classes, and carry a full private appearance instead:

| Module | Shared classes used | Imports `canvas.css` | Own stylesheet |
| --- | --- | --- | --- |
| `dependency-graph` | 19 | yes | 45 lines |
| `rdf` | 18 | yes | 4 sheets, module-own rules only |
| `timeline` | 18 | yes | 78 lines |
| `databricks` | 17 | yes | module-own rules only |
| `sparql` | 14 | yes | module-own rules only |
| `ansible-structure` | 6 | yes | module-own rules only |
| `wardley-map` | 0 | no | `wardley.css`, 261 lines, 56 appearance rules |
| `helm-charts` | 0 | no | `helm-charts.css`, 176 lines, 38 appearance rules |
| `azure-pipeline` | 0 | no | `azure-pipeline.css`, 151 lines, 32 appearance rules |
| `c4` | 0 | no | `c4.css`, 150 lines, 19 appearance rules |
| `mindmap` | 0 | no | `mindmap.css`, 104 lines, 19 appearance rules |

The split is internally consistent — every module that uses the shared classes imports the sheet, and no module uses them without importing it — so nothing is broken today. What it costs is the thing the shared layer was built for: a change to how a selected node or a connection line looks reaches six modules and stops. `timeline.css:3` and `dependency-graph.css:3` describe the migration in the past tense ("appearance moved to `@client/canvas/canvas.css`"), which is what makes the remaining five a tail rather than a choice.

**D3 — the document-factory rule is guarded late.** A definition that declares an extension needs an `IDiagramDocumentFactory`, or creating one answers "This diagram type cannot be created: its module is incomplete." (`src/backend/EtAlii.Adp.Backend/Hierarchy/AddDiagramContextActionProvider.cs:260`). The rule **is** guarded: `src/backend/EtAlii.Adp.Backend.Service/Program.cs:86` calls `DiagramDocumentFactories.Verify` and throws at startup, and every document-sibling definition in the tree satisfies it today — including the four readings of the `rdf` family, whose skos and shacl factories are registered from their own nested files (`.../Skos/ServiceCollection.AddSkos.cs:34`, `.../Shacl/ServiceCollection.AddShacl.cs:38`).

The gap is *when* it fires. Only two modules assert it in their own test project — `sparql/backend/EtAlii.Adp.Diagram.Sparql.Tests/Diagram.Tests.cs:128` and `wardley-map/backend/EtAlii.Adp.Diagram.WardleyMap.Tests/AddWardleyMap.Tests.cs:67` — and sparql's was added by `f00edf27` after the omission shipped. A module under construction is not in the host's catalog, so the startup throw cannot see it: its author learns of the omission only by booting the whole application. That is precisely the position `plantuml-uml` is in.

### Deviations with reason, all stated at the point of decision

- **No context action provider and no toolbox provider** in `helm-charts` and `ansible-structure`, and no toolbox in `sparql` — each is read-only and says so where it registers: `ServiceCollection.AddSparql.cs:14` ("no toolbox provider, because a read-only diagram…"), `ServiceCollection.AddAnsibleStructure.cs:17`, `ServiceCollection.AddHelmCharts.cs:54`.
- **Three canvases that are one canvas.** `databricks/client/BundleCanvas.tsx:11`, `JobCanvas.tsx:10` and `PipelineCanvas.tsx:10` are eleven-line wrappers delegating to `DatabricksCanvas` with different props — the multi-registration arrangement, not three implementations.
- **A second registration file** in `mindmap` and `wardley-map` (`ServiceCollection.AddMindmapCommands.cs`, `ServiceCollection.AddWardleyMapCommands.cs`), splitting command registration out. Two of eleven do this; the other nine keep one file.
- **`.gitattributes` reached from four directions** — by extension at the root (`*.mm`, `*.dsl`, `*.tml`, `*.dgr`, `*.ttl`, `*.nt`, `*.rq`), by scoped glob for `azure-pipeline` and `databricks`, and by nested file for `wardley-map`, `ansible-structure` and `helm-charts`. The root file reasons each one out; only the two `* -text` files (D1) contradict it.

### The one module that is structurally different

`rdf` is not like its siblings and could not be. It carries four diagram types in one module — `w3c/rdf`, `w3c/owl`, `w3c/skos`, `w3c/shacl` — over one parser and one document store, which is the C4 arrangement taken further: three protos, four stylesheets, four canvases, seven files naming `IDiagramValidator`, and registration split across three `ServiceCollection.Add*.cs` files at two directory levels. It is the intended shape for a family of readings and every other module is a single type, so "different" here is not "wrong" — but it is the module whose conventions a reader is most likely to copy by accident.

It also carries one internal inconsistency worth naming, because it was mine: `skos.proto` and `shacl.proto` are separate files, while the OWL reading's three payload messages sit inside `rdf.proto`. Same family, same kind of thing, two placements.

## Alignment with Product Vision

- [structure.md](../../steering/structure.md)'s **dependency direction and module layout** — this spec writes down the layout that eleven modules already share, so the twelfth does not have to be inferred from the eleven.
- [product.md](../../steering/product.md)'s **"Don't reinvent, integrate"** — read inward: a module that hand-rolls what the central canvas library already offers is reinventing inside our own walls. D2 measures how much of that exists.
- [tech.md](../../steering/tech.md)'s **Specifying a diagram type** — the five build steps assume a house shape; this spec states it.

## Requirements

### Requirement 1 — The module layout is checkable, not remembered

**User Story:** As someone adding the twelfth diagram module, I want the house shape stated and enforced, so that I learn what is required from a failing check rather than from reading eleven modules and guessing which of their differences matter.

#### Acceptance Criteria

1. WHEN a module is implemented THEN it SHALL have `backend/EtAlii.Adp.Diagram.<PascalCase>/` with a matching namespace, exactly one `.Tests` project beside it, an `api/` folder, and a `client/` folder — and a test SHALL assert the folder-to-project-to-namespace correspondence rather than leaving it to review.
2. WHEN a module is a stub THEN it SHALL hold exactly the four-file shape and nothing else, and a test SHALL fail on a stray file — the property all forty-nine stubs have today and that nothing currently protects.
3. WHERE a module deviates from the layout THEN the deviation SHALL be stated at the point of decision, in the file that makes it, as the read-only modules already do.

### Requirement 2 — The document-factory rule is caught in the module, not at boot

**User Story:** As a module author, I want the missing-factory mistake to fail in my own test run, so that I do not ship it and discover it from a user-facing refusal.

#### Acceptance Criteria

1. WHEN a module's test project runs THEN it SHALL assert that every definition in its own `Diagram.Definitions` declaring an extension has a registered `IDiagramDocumentFactory` — the assertion `sparql` and `wardley-map` already carry, made ordinary rather than exceptional.
2. WHEN that assertion is written THEN it SHALL run against the module's own registration, so it holds for a module not yet in the host's catalog.
3. THEN the startup check at `Program.cs:86` SHALL remain exactly as it is — it catches the deployment-level case the per-module test cannot, and nothing here replaces it.

### Requirement 3 — One appearance layer, or a stated reason for two

**User Story:** As someone changing how a selected node looks, I want the change to reach every canvas, so that the shared layer means what it says.

#### Acceptance Criteria

1. WHEN a module's canvas draws nodes, connections, selection, anchors, status or rejection THEN it SHALL compose the shared `canvas-*` classes and import `src/client/src/canvas/canvas.css` from its `register.ts`, as the six migrated modules do.
2. WHERE a module keeps a private appearance for those concerns THEN its stylesheet SHALL say which shared rule it is overriding and why — the shape `timeline.css:3` and `dependency-graph.css:3` already use for the opposite direction.
3. WHEN the five un-migrated modules are migrated THEN each SHALL be migrated on its own, with its canvas tests asserting the composed classes, rather than in one sweep across five modules at once.
4. IF a module needs a shape the central library lacks THEN it SHALL be added to the central library, never forked module-side — the rule the ellipse element followed.

### Requirement 4 — The line-ending guarantee follows the format

**User Story:** As someone whose round-trip test compares bytes, I want the `-text` marking to be narrow and reasoned, so that ordinary documents beside my fixtures are not frozen with them.

#### Acceptance Criteria

1. WHEN a module's documents are byte-compared THEN their extension SHALL be marked, by extension where the extension means one format everywhere, or by a glob naming those extensions where it does not — the two mechanisms `.gitattributes` already reasons out.
2. WHEN a marking is written THEN it SHALL NOT be a bare `* -text` over a fixture tree, because that catches readmes and recorded verdicts, which are ordinary text — and the two modules that do this today (D1) SHALL be narrowed to the extensions whose bytes are the subject.
3. WHEN a `crlf-line-endings.*` / `lf-line-endings.*` pair exists THEN a test SHALL assert the two files still differ on disk, so a collapse is caught by a failing test rather than by a round-trip test that quietly starts measuring git.

### Requirement 5 — Findings are recorded where the next reader will meet them

**User Story:** As a reviewer, I want a deviation to be distinguishable from a defect without re-deriving the reasoning, so that the irregular-looking things that were chosen deliberately stop being re-litigated.

#### Acceptance Criteria

1. WHEN this spec is implemented THEN `docs/creating-a-diagram-module.md` SHALL carry the house shape as the module author meets it, including the read-only variant that three modules use.
2. WHEN a module deviates deliberately THEN the reason SHALL live in the module, not only in this document, so it survives this document.
3. THEN nothing in this spec SHALL be fixed as part of writing it — the scan reports, and each finding is scheduled as its own change with its own guard.

## Non-Functional Requirements

### Code Architecture and Modularity

- Every check this spec adds is a test in an existing test project. No new project, no new tool, no scanner run by hand.
- A check that discovers modules by walking `src/diagrams/` is preferred over one that lists them, so it cannot go stale — the shape `src/client/src/canvas/scroll/noPrivateScrollbars.test.ts` already uses, and the reason its coverage claim can be trusted.

### Reliability

- Each finding above is reproducible from the tree at `469b1fbd` by the citation given; a fix for any of them is expected to carry a test that fails before it.

### Usability

- A conformance failure SHALL name the module, the rule and the file, in one sentence a module author can act on without opening this spec.

## Sources

- The scan: all sixty folders under `src/diagrams/` at `469b1fbd`, both halves of each implemented module.
- `.gitattributes:45-100` — the by-extension reasoning, the withdrawn directory rule, and the nested-file exceptions.
- `src/backend/EtAlii.Adp.Backend.Service/Program.cs:86` and `src/backend/EtAlii.Adp.Diagram/DiagramDocumentFactories.cs:25` — the startup guard.
- `src/backend/EtAlii.Adp.Backend/Hierarchy/AddDiagramContextActionProvider.cs:252-261` — the user-facing refusal the guard exists to prevent.
- `src/client/src/canvas/canvas.css` and `src/client/src/canvas/scroll/noPrivateScrollbars.test.ts` — the shared appearance layer and the guard that keeps its scrollbars honest.
- `f00edf27` — the sparql correction that first recorded the missing-factory failure mode.
