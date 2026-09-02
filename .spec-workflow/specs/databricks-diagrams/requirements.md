# Requirements Document

## Introduction

This spec covers a **family of three Databricks diagram types** over the configuration files a Databricks project already carries — the way the C4 module covers seven types over one Structurizr engine. The files are not invented for ADP: they are the artifacts Databricks' own tooling reads and deploys, which makes them exactly the kind of "established text format another tool owns" that [tech.md](../../steering/tech.md) asks a diagram type to sit on.

| Origin | File visualized | What it shows |
| --- | --- | --- |
| `databricks/bundle` | `databricks.yml` (a Databricks Asset Bundle) | The bundle's topology: its deployment targets, the resources it declares, and which target overrides what |
| `databricks/job` | A job resource file (bundle `resources/*.yml`, or a Jobs API 2.x JSON) | The task DAG: tasks as nodes, `depends_on` as edges, clusters as the compute each task runs on |
| `databricks/pipeline` | A Lakeflow Declarative Pipelines settings file (JSON, or a bundle pipeline resource) | The dataflow frame: source code libraries flowing through the pipeline into its target catalog and schema |

**The configuration formats, as researched.** A bundle is defined by exactly one `databricks.yml` at the project root, with top-level mappings `bundle`, `include`, `variables`, `workspace`, `artifacts`, `permissions`, `resources` (jobs, pipelines, clusters, dashboards, model serving endpoints and more) and `targets` — where a target such as `dev` or `prod` overrides top-level settings and resource fields, and exactly one target may be `default: true`. The JSON schema used to validate this YAML lives in the Databricks CLI repository and is emitted by `databricks bundle schema`. A job resource declares `tasks[]`, each with a `task_key`, a task type (`notebook_task`, `spark_python_task`, `python_wheel_task`, `sql_task`, `dbt_task`, `pipeline_task`, `run_job_task`, `condition_task`, `for_each_task`, JAR and dashboard tasks among others), dependencies via `depends_on: [{task_key, outcome?}]` — `outcome` only off a condition task — and `run_if` semantics (`ALL_SUCCESS`, `AT_LEAST_ONE_SUCCESS`, `NONE_FAILED`, `ALL_DONE`, `AT_LEAST_ONE_FAILED`, `ALL_FAILED`), plus `job_clusters[]`, `schedule`/`trigger`/`continuous`, and `parameters`. A pipeline settings file declares `name`, `edition` (CORE/PRO/ADVANCED), `channel` (current/preview), `catalog`, `schema`/`target`, `serverless`, `continuous`, `development`, `photon`, `libraries[]` (notebook, file or glob entries), `clusters[]`, `configuration{}`, `notifications[]` and `root_path`. Sources are listed at the end of this document.

**What is genuinely new here, and what is not.** Three things are new. First, **layout lives in the `.adp` file**: these config files belong to Databricks' deploy tooling, so ADP may never write positions into them — instead the registration file that already names the type gains a metadata section that enriches the config with authored positions (Requirement 7). Every prior type either computed layout (mindmap, azure-pipeline) or owned its body outright (timeline, wardley); this is the first type whose *authored* layout must live beside a body ADP does not own. Second, **domain actions arrive mocked**: Run job, Deploy target and Start update are the actions that make these files worth diagramming, but they need a workspace connection ADP does not have — so this spec defines them as visible, clickable placeholders that simulate their outcome locally and mark themselves as simulations (Requirement 8), establishing the UX seams a later connectivity spec fills in. Third, a **family shares one vendor engine**: three MIME types, one module, exactly as C4 already proved. Everything else — routing, CST-splicing writers, the command/undo path, toolbox, property grid, context actions, validation — is consumed unchanged.

**Where it sits among the types already built.** [`azure-pipeline-diagram`](../../archive/specs/azure-pipeline-diagram/requirements.md) proved the shared-`.yml` registration path and the read-only DAG over a YAML another tool owns; `databricks/job` is that DAG *made editable*, because a bundle's job YAML is the source of truth the user is supposed to edit. The timeline proved byte-identical round-trips of an edited YAML body; the writers here inherit that discipline. C4 proved one engine serving several MIME types and layout kept out of the body (`courier.layout.json`) — this spec moves that idea into the `.adp` itself, where the registration already lives.

**Dependencies** — all consumed, none redefined: [`adp-diagram-ide`](../../archive/specs/adp-diagram-ide/requirements.md) (shell, canvas), [`grpc-core-communication-specification`](../../archive/specs/grpc-core-communication-specification/requirements.md) (`Element`, `Delta`, payloads), [`diagram-workspace-tabs`](../../archive/specs/diagram-workspace-tabs/requirements.md), [`add-diagram-action`](../../archive/specs/add-diagram-action/requirements.md) and [`create-diagram-file`](../../archive/specs/create-diagram-file/requirements.md) (the Add flow, `.adp` MIME line, shared-extension registration), [`context-service`](../../archive/specs/context-service/requirements.md), [`property-grid`](../../archive/specs/property-grid/requirements.md), [`diagram-undo-redo`](../../archive/specs/diagram-undo-redo/requirements.md) and tech.md's **Commands** rule.

**Reused code is generic code, and generic code lives in the shared projects:**

| What is reused | Where it lives |
| --- | --- |
| Shared-extension registration (`.yml`/`.json` never claimed on sight) | `DiagramDefinition.SharedExtension`, `src/backend/EtAlii.Adp.Backend/Hierarchy/` |
| `body:` headers, resolved against the `.adp`'s own folder | `src/backend/EtAlii.Adp.Backend/Hierarchy/DiagramFilePair.cs` |
| Connection drawing (straight, fixed-reach bezier, interactive bezier) | `src/client/src/canvas/connections/` |
| Element drawing (box, frame) and the selection/menu/keyboard plumbing | `src/client/src/canvas/elements/`, `src/client/src/canvas/` |
| Definition icons in the Add dialog | `DiagramDefinition.Icon` |

**What this spec changes in core: one bounded thing.** The `.adp` metadata section of Requirement 7 — a `layout:` block after the existing headers — is read and written by code beside `DiagramFilePair`, because the `.adp` format is core's. That is the whole core change; it is flagged here so it is a decision, not a discovery. Anything further found necessary during implementation is a **finding to report**, per established practice.

**Deliberately out of scope.**

* **Real Databricks connectivity.** No REST calls, no authentication, no live run state, no deploy. Requirement 8's actions are simulations and say so on their face.
* **The DLT dataset graph.** The tables and views of a declarative pipeline are declared in *source code* (Python/SQL), not in the settings file; deriving that graph means parsing user code and is a spec of its own. This type visualizes the *configuration*.
* **Editing arbitrary YAML.** The writers touch only the constructs the requirements name; everything else in the file survives byte-identically.
* **Cluster policy files, Unity Catalog grants, `for_each_task` inner DAGs** — cataloged interest, later specs.

## Alignment with Product Vision

* [product.md](../../steering/product.md)'s **"Files are the source of truth"** — every diagram here renders a file Databricks' own tools deploy; an untouched file round-trips byte-identically, and layout goes to the `.adp` precisely so the truth-holding file is never disturbed.
* product.md's **"A familiar surface"** — the job DAG looks like the workflow graph Databricks' own UI shows; a user who knows that UI reads this diagram unaided.
* tech.md's **Diagram storage** — established formats, ADP data never smuggled into them; Requirement 7 is that rule enforced structurally.
* tech.md's **Specifying a diagram type** — file format (R1–2), visualization (R3–6), toolbox (R9), context actions and commands (R11).
* tech.md's **Commands** — every edit, including a reposition, is a command with an inverse.
* [structure.md](../../steering/structure.md)'s **dependency direction** — one `databricks` module under `src/diagrams/`, depending on core only.

## Requirements

### Requirement 1 — Registration, and the files on disk

**User Story:** As a data engineer, I want ADP to open the Databricks files my repo already has, so that diagramming them costs no conversion and breaks no deploy.

#### Acceptance Criteria

1. WHEN the module declares itself THEN it SHALL expose three `DiagramDefinition`s — `databricks/bundle`, `databricks/job`, `databricks/pipeline` — each with a description, a fitting icon, and `SharedExtension: true`: `.yml`, `.yaml` and `.json` are everybody's extensions, so no bare file is ever claimed on sight and every diagram is registered explicitly through an `.adp`, exactly as `azure-devops/pipeline` established.
2. WHEN a user invokes Add on a file named `databricks.yml` THEN the choice tree SHALL suggest `databricks/bundle`; on a YAML file whose top level carries `resources.jobs` or a `tasks:` list, `databricks/job`; on a JSON or YAML file carrying `libraries` beside `catalog`/`target`, `databricks/pipeline`. These are suggestions in the Add flow, never automatic claims.
3. WHEN a registration exists THEN it SHALL be the standard `.adp`: MIME line first, a `body:` header naming the config file relative to the `.adp`'s own folder, and (for `databricks/job` and `databricks/pipeline` inside a bundle) an optional `resource:` header naming which resource key within the file the diagram shows — one file may declare several jobs.
4. WHEN a new diagram is created through Add on a folder THEN the factory SHALL write a minimal valid file for the chosen type: a bundle with a `bundle.name` and one `dev` target; a job with one notebook task; a pipeline with a name and an empty `libraries` list.

### Requirement 2 — Reading and writing without disturbing the file

**User Story:** As a reviewer, I want ADP's edits to show up as the smallest possible diff, so that a pull request stays readable and the deploy tooling stays trusted.

#### Acceptance Criteria

1. WHEN a document is opened and closed without edits THEN the bytes on disk SHALL be identical — order, comments, quoting style, blank lines and line endings all preserved.
2. WHEN an edit is made THEN the writer SHALL splice only the lines the edit concerns, following the CST-and-splice discipline the timeline and pipeline writers established.
3. WHEN a file does not parse THEN the diagram SHALL open in its unavailable state naming the parse error, and every edit SHALL be refused with the reason until the file is fixed.
4. WHEN a construct is unknown to ADP (a task type it does not model, a resource kind it does not draw) THEN it SHALL still be represented — as a generic node labeled by its key — rather than dropped, and the writer SHALL never touch it.

### Requirement 3 — The bundle diagram (`databricks/bundle`)

**User Story:** As a platform engineer, I want to see at a glance what a bundle deploys and where, so that "what does prod actually get?" stops requiring a mental YAML merge.

#### Acceptance Criteria

1. WHEN a bundle opens THEN the canvas SHALL show three bands: the **bundle** node (its `name`, CLI version requirement, and count of `include`d files), the **resources** it declares (one node per resource, labeled by key and kind — job, pipeline, cluster, dashboard, endpoint), and the **targets** (one frame per target, `default: true` marked, `mode: production` visually distinct from `mode: development`).
2. WHEN a target overrides a resource's fields THEN an **override edge** SHALL run from the target frame to that resource node, labeled with the count of overridden fields, and selecting the edge SHALL show the overridden values in the property grid — the answer to "what changes in prod".
3. WHEN a job resource carries a `pipeline_task` or `run_job_task` referring to another resource in the same bundle THEN a **reference edge** SHALL run between the two resource nodes, so the internal wiring of the bundle is visible.
4. WHEN a resource is declared in an `include`d file rather than `databricks.yml` itself THEN its node SHALL carry the file name as a badge, and activating the node SHALL offer to open that file's own diagram where one is registered.
5. WHEN a variable (`${var.*}`) is used in a shown value THEN the value SHALL be displayed unresolved with the variable marked, and the property grid SHALL list the variable's declared default beside it.

### Requirement 4 — The job diagram (`databricks/job`)

**User Story:** As a data engineer, I want the job's task graph drawn from its `depends_on` edges, so that ordering mistakes are visible before a deploy fails at 3am.

#### Acceptance Criteria

1. WHEN a job opens THEN each task SHALL be a node labeled by `task_key`, carrying its task type as an icon and caption (notebook, Python, wheel, SQL, dbt, pipeline, run-job, condition, for-each, JAR), and each `depends_on` entry SHALL be an edge from dependency to dependent, drawn with the shared fixed-reach bezier and an arrowhead.
2. WHEN an edge leaves a `condition_task` with an `outcome` THEN the edge SHALL be labeled `true` or `false`; WHEN a task declares a non-default `run_if` THEN the node SHALL carry it as a badge (`ALL_DONE`, `AT_LEAST_ONE_FAILED`, …).
3. WHEN tasks share a `job_cluster_key` THEN the cluster SHALL be visible — a color/badge per cluster with a key beside the diagram — and a task with no cluster reference SHALL read as serverless.
4. WHEN the job declares a `schedule`, `trigger` or `continuous` mode THEN a **trigger node** SHALL sit at the diagram's edge, showing the cron or trigger kind and its paused/unpaused state.
5. WHEN a `depends_on` names a `task_key` that does not exist THEN the edge SHALL be drawn as a stub to a marked missing node and reported by validation — mirroring the Ansible module's unresolved-edge treatment.
6. WHEN the graph has a cycle THEN the diagram SHALL still render (the cycle drawn, not crashed on) and validation SHALL name the tasks in the cycle.

### Requirement 5 — The pipeline diagram (`databricks/pipeline`)

**User Story:** As a data engineer, I want the pipeline's settings framed as a flow — code in, tables out — so that the file's fifteen scattered keys read as one picture.

#### Acceptance Criteria

1. WHEN a pipeline opens THEN the canvas SHALL show, left to right: one node per `libraries` entry (notebook, file or glob, labeled by path), flowing into the central **pipeline** node (name, `edition`, `channel`, `serverless`/`photon` badges, `continuous` vs triggered, `development` vs production), flowing into the **target** node (`catalog`.`schema`).
2. WHEN `clusters` are declared THEN a compute node SHALL sit beneath the pipeline node summarizing them (label, autoscale range); WHEN `serverless: true` THEN the compute node SHALL say so instead.
3. WHEN `notifications` are declared THEN a notification node SHALL hang off the pipeline node listing recipients and the events they hear about.
4. WHEN `configuration` carries Spark settings THEN they SHALL NOT be drawn as nodes — they belong to the property grid (Requirement 10) — keeping the picture a flow rather than a key dump.

### Requirement 6 — Editability: each type earns its own verdict

**User Story:** As an author, I want to edit where editing is safe and read where it is not, so that the diagram never writes something the deploy tooling would reject.

#### Acceptance Criteria

1. `databricks/job` SHALL be **editable as a diagram**: the task DAG is plain data in a file the user owns and is expected to edit, so add-task, connect, disconnect, rename and remove are diagram gestures (Requirement 11). This is the family's editable flagship.
2. `databricks/bundle` SHALL be **read-mostly**: the picture is a derived merge view, and most of its truth (target overrides) is not safely writable through a picture. Renaming a resource key and adding a resource skeleton SHALL be the only structural edits; everything else edits through the property grid or the file's own text editor.
3. `databricks/pipeline` SHALL be **read-only in shape, editable in values**: nodes are fixed by what the settings declare; adding/removing a `libraries` entry and every scalar setting edit go through context actions and the property grid rather than free-form diagram gestures.
4. WHEN any edit would produce a file the published schema rejects THEN the edit SHALL be refused with the reason, never written and repaired later.

### Requirement 7 — Layout: authored positions live in the `.adp`, never in the config

**User Story:** As a user, I want to drag the boxes into an arrangement that makes sense to me and find it again tomorrow, without my `databricks.yml` diff showing coordinates the deploy tooling never asked for.

#### Acceptance Criteria

1. WHEN any of the three diagrams opens with no stored layout THEN the module SHALL compute one (layered left-to-right for the job DAG, banded for bundle and pipeline), exactly as the computed-layout types already do.
2. WHEN the user drags an element THEN the element SHALL move — in every one of the three types, read-only ones included: repositioning is a view arrangement, not a config edit.
3. WHEN a reposition lands THEN it SHALL be stored in the diagram's own `.adp` file as metadata — a `layout:` block after the existing headers, one `<element-id>: <x> <y>` entry per moved element — and the config file SHALL NOT change by a single byte.
4. WHEN a diagram reopens THEN stored positions SHALL win over computed ones, element by element; an element with no stored position SHALL take its computed place, so a config change adding new elements degrades gracefully.
5. WHEN an element id stored in the layout no longer exists in the config THEN its entry SHALL be ignored on read and dropped on the next layout write — stale layout never blocks opening.
6. A reposition SHALL be a command with an inverse like any other edit: undo puts the element back and rewrites the `.adp` accordingly.
7. BECAUSE the `.adp` format is core's, the `layout:` block SHALL be defined once in core (beside the `body:`/`view:` headers of `DiagramFilePair`) and offered to every module — this family is its first consumer, not its owner.

### Requirement 8 — Domain actions, arriving as honest mocks

**User Story:** As a user, I want the actions that make these files meaningful — run, deploy, start — visible on the diagram today, so that the workflow is designed now and only the wiring waits for a connection.

#### Acceptance Criteria

1. Every mocked action SHALL be visibly a simulation: its result banner and any state badges it sets SHALL carry a "simulated" marker, and a `Workspace connection` placeholder property (value: `Not configured`, read-only, with the reason "Connecting ADP to a Databricks workspace is a later spec") SHALL sit in the property grid of every diagram in the family.
2. `databricks/job` SHALL offer **Run job (simulated)** on the file and **Run task (simulated)** on a task: invoking it SHALL play a mock run — tasks lighting pending → running → succeeded in dependency order over a few seconds, `run_if`/`outcome` edges honored, one configurable mock failure path — and end with a simulated result banner. No file content changes.
3. `databricks/job` SHALL offer **Pause/Resume schedule** on the trigger node: this one is a real edit (it writes `pause_status`), and the two SHALL be visually distinct — a real edit is undoable, a simulation is not and never touches the history.
4. `databricks/bundle` SHALL offer **Validate bundle (simulated)** — replaying ADP's own Requirement 12 validation and presenting it as the familiar `bundle validate` moment — and **Deploy to target… (simulated)** on a target frame: a mock progress sequence over the resources the target would deploy, ending in a simulated summary.
5. `databricks/pipeline` SHALL offer **Start update (simulated)** and **Full refresh (simulated)**: sources → pipeline → target lighting in order, plus the real, undoable toggles **Continuous/Triggered** and **Development/Production** which edit their booleans in the file.
6. WHEN a later spec supplies real connectivity THEN these actions SHALL be the seam it fills: same ids, same placement, the "simulated" marker removed — recorded here so the mock is architecture, not decoration.

### Requirement 9 — Toolbox

#### Acceptance Criteria

1. WHEN a job diagram is active THEN the toolbox SHALL offer the task types of Requirement 4.1 as draggable entries; dropping one SHALL create a task of that type at the drop point (its position going to the `.adp` layout, its declaration to the file) — the timeline's placement-id mechanism reused.
2. WHEN a pipeline diagram is active THEN the toolbox SHALL offer **Notebook library** and **File library**; dropping one SHALL add the `libraries` entry and prompt for its path.
3. WHEN a bundle diagram is active THEN the toolbox SHALL offer **Job resource** and **Pipeline resource** skeletons per Requirement 6.2.

### Requirement 10 — Properties

#### Acceptance Criteria

1. WHEN a task is selected THEN the grid SHALL show: `task_key` (editable, rename-with-references per R11.4), task type (read-only), the type's principal source field (notebook path, python file, SQL file/query, dbt commands, referenced pipeline/job — editable), cluster binding (editable choice of declared `job_cluster_key`s or serverless), `run_if` (editable choice), retries and timeout (editable), and the simulated last-run state (read-only, marked simulated).
2. WHEN a job file level is selected THEN: `name`, schedule cron and pause state, `continuous`, `max_concurrent_runs`, parameter list, tags — editable where the schema allows; plus the `Workspace connection` placeholder of R8.1.
3. WHEN a pipeline node is selected THEN: `name`, `edition`, `channel`, `catalog`, `schema`, `serverless`, `photon`, `continuous`, `development` — all editable scalars; a library node SHALL show its kind and path; the compute node its cluster summary (read-only, "edit in text" hint); the notification node its recipients (editable list).
4. WHEN a bundle element is selected THEN: the bundle node shows `name` and CLI version; a target frame shows `mode`, `default`, workspace host (unresolved variables shown per R3.5); a resource node shows its kind, key and defining file; an override edge shows the overridden field/value pairs (read-only).
5. Every editable property SHALL go through the standard `SetProperty` path and land as one undoable command.

### Requirement 11 — Context actions and commands

**User Story:** As a user, I want every change to be one undo away, so that experimenting on a deploy-critical file is safe.

#### Acceptance Criteria

1. Every edit named in this spec — add/remove/rename task, connect/disconnect `depends_on`, set `run_if`/cluster/outcome, add/remove library, add resource skeleton, every property write, every reposition, pause/resume, the R8.5 toggles — SHALL be an `ICommand` with a working inverse, and redo SHALL re-apply under the same identity.
2. WHEN a task is right-clicked THEN the menu SHALL offer: Run task (simulated), Rename…, Set run-if…, Assign cluster…, Disconnect from… (submenu of its edges), Remove task (removing its edges with it, one undo). WHEN empty canvas is right-clicked on a job THEN: Add task… (typed submenu), Run job (simulated), Pause/Resume schedule.
3. WHEN a relation gesture is dragged between two tasks THEN a `depends_on` entry SHALL be written — the timeline's stateless `rel:` gesture reused — including its begin-anchor reversal; onto empty canvas it SHALL create a new task of a prompted type, related in one undo.
4. WHEN a task is renamed THEN every `depends_on` and `condition_task` reference to the old key in the same job SHALL be rewritten in the same command, so a rename never strands an edge.
5. WHEN a bundle resource or target is right-clicked THEN the menu SHALL offer its R6.2/R8.4 actions; WHEN a pipeline library node is right-clicked THEN Remove library and Change path…; the pipeline node itself the R8.5 actions.
6. Simulated actions SHALL appear in menus with their marker and SHALL NOT enter the undo history (R8.3's distinction, enforced structurally).

### Requirement 12 — Diagnostics

#### Acceptance Criteria

1. WHEN a file is validated THEN ADP SHALL report, at minimum: parse failures; `depends_on` naming a missing `task_key` (R4.5); dependency cycles (R4.6); a bundle with zero or multiple `default: true` targets; a target overriding a resource that is not declared; a job task referencing an undeclared `job_cluster_key`; a pipeline with an empty `libraries` list or a `schema` without a `catalog`; duplicate task keys or resource keys.
2. Every finding SHALL carry the file, the line where determinable, and a sentence a person can act on, surfaced through the standard problems panel with go-to navigation.
3. Validation SHALL NOT attempt what needs a workspace (does the notebook exist? is the catalog real?) — those are connectivity-spec territory, and their absence here is deliberate.

### Requirement 13 — Examples, in the module and in the central showcase

**User Story:** As a user meeting these diagram types for the first time, I want ready-made examples I can open and poke at, so that the family explains itself the way every shipped type already does.

#### Acceptance Criteria

1. WHEN the module ships THEN it SHALL carry its own example sets under `src/diagrams/databricks/examples/`, one subfolder per set, following the shape the sibling modules established (`src/diagrams/c4/examples/reference/`, `src/diagrams/timeline/examples/example-1/`).
2. WHEN the central showcase is browsed THEN `src/examples/diagrams/databricks/` SHALL hold the same family with per-example subfolders, exactly as `src/examples/diagrams/` already holds ansible-structure, azure-pipeline, c4, dependency-graph, mindmap, timeline and wardley-map.
3. WHEN the examples are authored THEN every one of the three types SHALL be covered: at least one bundle example whose `databricks.yml` declares a dev and a prod target with a visible override; at least one job example whose task DAG exercises several task types, a `condition_task` with `outcome` edges and a non-default `run_if`; and at least one pipeline example with multiple libraries, a target catalog and schema, and notifications. One coherent scenario MAY provide all three (a bundle containing the job and the pipeline), and SHOULD, so the examples also demonstrate the reference edges of Requirement 3.3.
4. WHEN an example is opened THEN it SHALL be complete: each config file beside the `.adp` registration that routes it, so double-clicking in the explorer opens the diagram with no setup step, and each example folder SHALL carry a short readme naming what the example demonstrates.
5. WHEN validation (Requirement 12) runs over the shipped examples THEN it SHALL report no findings — the examples are the family's first test fixtures, and a red example is a broken example.
6. WHEN the byte-identity discipline of Requirement 2 needs fixtures THEN the example files SHALL serve as some of them, and any extension that must not be line-ending-normalized SHALL gain its `.gitattributes` line per the repository's established rule.

## Sources

* [Databricks Asset Bundles configuration](https://docs.databricks.com/aws/en/dev-tools/bundles/settings) — `databricks.yml` structure, targets and overrides
* [Bundle configuration reference](https://docs.databricks.com/aws/en/dev-tools/bundles/reference) — full key reference; JSON schema via the Databricks CLI (`databricks bundle schema`)
* [Bundle resources](https://docs.databricks.com/aws/en/dev-tools/bundles/resources) — the resource kinds a bundle declares
* [Job task types in bundles](https://docs.databricks.com/aws/en/dev-tools/bundles/job-task-types) — task types, `depends_on`, `outcome`, `run_if`
* [Lakeflow Declarative Pipelines properties reference](https://docs.databricks.com/aws/en/ldp/properties) — the pipeline settings fields
* [Configure pipelines](https://docs.databricks.com/aws/en/ldp/configure-pipeline) — the JSON settings surface and UI parity
