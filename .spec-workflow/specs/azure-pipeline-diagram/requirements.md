# Requirements Document

## Introduction

This spec covers the **Azure DevOps Pipeline diagram module** — a diagram of a CI/CD pipeline, drawn from the `azure-pipelines.yml` a repository already has. It defines how such a file is recognised, parsed into a stage/job/step model, laid out as the dependency graph it describes, rendered on the shared canvas, and edited through a deliberately narrow set of commands.

**Why this type is different from the two before it.** The pluggable diagram-type model has been proven twice, and both times the document belonged to ADP:

| | [`mindmap-diagram`](../mindmap-diagram/requirements.md) | [`wardley-map`](../wardley-map/requirements.md) | This spec |
|---|---|---|---|
| Positions | computed by the module | authored in the document | **computed** (the file has none) |
| Document | created by ADP's Add flow | created by ADP's Add flow | **already in the repository** |
| If ADP damages it | a diagram is wrong | a diagram is wrong | **the build breaks** |

That third row is what makes this type worth specifying. An `azure-pipelines.yml` is executable configuration owned by the build system, hand-authored, often split across template files, and frequently containing expressions whose value is not knowable until the pipeline runs. A diagram module that rewrites it carelessly does not produce a wrong picture; it stops the team shipping. Requirements 3 and 9 are shaped by that, and the scope decision in Requirement 9.1 follows from it.

**Dependencies.** This spec redefines none of them:

* [`adp-diagram-ide`](../adp-diagram-ide/requirements.md) — the workspace shell, the pannable/zoomable canvas, read-only mode.
* [`grpc-core-communication-specification`](../grpc-core-communication-specification/requirements.md) — `Element`, `Delta`, `Point2D`, and the `Any` payload extension point.
* [`mindmap-diagram`](../mindmap-diagram/requirements.md) — which introduced, and this spec reuses unchanged: `DiagramDefinition.Extension`, `IDiagramDocumentFactory`, `ContextSource.element_id`, `ContextScope.DIAGRAM_ELEMENT`, and `DiagramService`'s `Open` + `UpdateView` legs.
* [`wardley-map`](../wardley-map/requirements.md) — which established that a module may read a document it did not invent, and that round-tripping a hand-authored text format needs a concrete syntax tree rather than a regenerator.
* `IDiagramToolboxProvider` and `DescribeToolbox` — the toolbox seam the mindmap module already uses.
* [`context-service`](../context-service/requirements.md), [`diagram-undo-redo`](../diagram-undo-redo/requirements.md), and `tech.md`'s **Commands** rule.
* [`errors-and-warnings-panel`](../errors-and-warnings-panel/requirements.md) — `IDiagramValidator`, which this type has unusually much to say through (Requirement 10).

**What this spec changes in core.** One thing, and it is a real gap rather than a convenience: **`DiagramFileRouter` cannot route this type by extension alone** (Requirement 2.3). Everything else is a module registration.

## Alignment with Product Vision

* [product.md](../../steering/product.md)'s **"Don't reinvent, integrate"** — the format is the Azure Pipelines YAML schema exactly as Microsoft publishes it. ADP invents no pipeline format, and adds no file to a repository that has one.
* product.md's **"Files are the source of truth"** — the pipeline that runs in Azure DevOps and the diagram on the canvas are the same file. There is no export step and nothing to keep in sync.
* product.md's **"Linkage over illustration"** — a pipeline diagram is only worth drawing because it stays true as the pipeline changes. A picture pasted into a wiki is stale the day after it is drawn; this one cannot be.
* [tech.md](../../steering/tech.md)'s **"Wherever possible already existing text-based file formats will be used"** — taken to its conclusion: the file already exists, is already authored, and is already the source of truth for something other than ADP.
* tech.md's **Specifying a diagram type** — the four aspects it requires are Requirements 2–3 (file format), 6–8 (visualization), 9.6 (toolbox) and 9 (context actions and commands).
* [structure.md](../../steering/structure.md)'s **dependency direction** — the module depends on core; core never depends on the module.

## Requirements

### Requirement 1 — Recognising a pipeline, and not creating one

**User Story:** As an architect opening a repository that already builds, I want its pipeline to be a diagram I can open, without having to add anything to the repository first.

#### Acceptance Criteria

1. WHEN the explorer shows a file the module recognises as an Azure Pipelines definition (Requirement 2) THEN the system SHALL offer to open it as a pipeline diagram, with no `.adp` registration file present and none created.
2. WHEN a pipeline diagram is opened this way THEN the system SHALL NOT write to the repository at all. Opening a diagram is a read.
3. WHEN the Add dialog is shown THEN this diagram type SHALL appear in it, since it is a discovered `Diagram.Definition` like any other; confirming it SHALL create a minimal but **valid and runnable** `azure-pipelines.yml` — a `trigger`, a `pool` and one job with one script step — rather than an empty file, because an Azure Pipelines file that does not run is not a pipeline.
4. IF a file named `azure-pipelines.yml` already exists in the target folder THEN Add SHALL report the collision through the existing name validation rather than overwriting it, exactly as `create-diagram-file` Requirement 2.8 already requires.
5. WHERE a repository holds several pipeline files (`azure-pipelines.yml`, `ci/build.yml`, `.azure/release.yml`) THEN each SHALL be openable as its own diagram; the module SHALL NOT assume one pipeline per repository.

### Requirement 2 — Deciding what is a pipeline file

**User Story:** As a developer, I want ADP to recognise my pipeline file without claiming every other YAML file in my repository.

#### Acceptance Criteria

1. WHEN the module declares itself THEN it SHALL declare `.yml` and `.yaml` as the extensions its documents use.
2. WHEN a `.yml` or `.yaml` file is considered THEN the system SHALL treat it as an Azure Pipelines definition only if its **content** says so: a mapping at the root carrying at least one of `stages`, `jobs`, `steps`, `extends`, `trigger`, `pr` or `schedules`. A `docker-compose.yml`, a Kubernetes manifest and an OpenAPI document all end in `.yml` and none of them is a pipeline.
3. WHEN routing is decided THEN this SHALL require a new capability in core: `DiagramFileRouter` today routes a body file by extension alone, and an extension this common cannot be claimed that way. **A diagram type SHALL be able to contribute a content test that the router consults** before accepting an extension match. This is the one core change this spec anticipates, it SHALL be type-agnostic, and it SHALL be usable by any later type whose extension is shared (`.json`, `.xml`).
4. WHEN the content test is applied THEN it SHALL read no more of the file than it needs to decide, so opening a folder of large YAML files does not read all of them in full.
5. IF a file passes the content test but then fails to parse THEN the system SHALL report that as a problem on the file (Requirement 10), not silently fall back to treating it as an ordinary file — a pipeline that ADP half-recognises is a bug the user needs to see.
6. WHEN an `.adp` registration file naming this type is present THEN it SHALL take precedence over the content test, as it does for every type.

### Requirement 3 — Reading and writing the file without damaging it

**User Story:** As a developer, I want ADP to leave my pipeline exactly as I wrote it apart from what I changed, so that opening it in a diagram is never the reason a build breaks or a diff is noisy.

#### Acceptance Criteria

1. WHEN a pipeline ADP has not changed is saved THEN the resulting file SHALL be **byte-identical** to what was read — key order, indentation, comments, blank lines, quoting style, anchors and line endings included.
2. WHEN ADP changes one thing THEN it SHALL rewrite only the lines that thing occupies. A model-to-YAML regeneration SHALL NOT be used: YAML has many spellings for the same value, and a regenerator would reformat an entire hand-authored pipeline on the first edit.
3. WHEN a construct is encountered that this module does not model — a task's `inputs`, a `resources` block, a service connection, a key added by a newer schema version — THEN it SHALL be preserved verbatim through a round trip. **Preserving what it does not understand is more important than understanding everything**, because the file has to keep running.
4. WHEN a file contains a runtime or compile-time **expression** (`$[ ]`, `${{ }}`) in a position the diagram reads — a `condition`, a `dependsOn`, a `displayName` — THEN the system SHALL preserve it exactly, render the element as **conditional or indeterminate** rather than guessing its value, and never rewrite the expression while editing something else.
5. WHEN the file is written THEN it SHALL be written atomically through the backend's file-access layer, to the standard `create-diagram-file` Requirement 3.3 sets.
6. IF the file cannot be parsed as YAML THEN the system SHALL surface a clear error naming the file and line, and SHALL NOT offer any edit that would rewrite it.

### Requirement 4 — The pipeline model

**User Story:** As an architect, I want the diagram to show what my pipeline actually contains, in the vocabulary Azure DevOps itself uses.

#### Acceptance Criteria

1. WHEN a pipeline is read THEN the system SHALL produce a model in Azure Pipelines' own terms — **pipeline, stage, job, step** — and SHALL use those names throughout, so what a user reads on the canvas matches the documentation they will search.
2. WHEN a pipeline declares no `stages` THEN the system SHALL model the implicit single stage the schema defines, so a `jobs`-only or `steps`-only file is a valid pipeline and not an empty diagram.
3. WHEN a job is read THEN the system SHALL distinguish a plain `job` from a **`deployment` job**, and for a deployment job SHALL carry its `environment` and its `strategy` (`runOnce`, `rolling`, `canary`), since the strategy is what determines the lifecycle hooks it runs.
4. WHEN a step is read THEN the system SHALL carry its kind — `task`, `script`, `bash`, `pwsh`, `powershell`, `checkout`, `download`, `publish`, `template` — and its `displayName` where it has one, falling back to the kind's own identifying value (a task's name, a script's first line) where it does not.
5. WHEN an element carries a `condition` THEN the system SHALL carry it, and WHEN it carries `continueOnError`, `enabled: false`, `timeoutInMinutes` or (for a stage) `trigger: manual` or `isSkippable: false` THEN the system SHALL carry those too — they change whether and how an element runs, which is exactly what a reader of the diagram is asking.
6. WHEN a job declares a `strategy` with a `matrix` or `parallel` THEN the system SHALL carry the multiplicity, so a job that becomes several at run time does not look like one.
7. WHEN a `pool` is declared at pipeline, stage or job level THEN the system SHALL carry it and SHALL apply the schema's inheritance — a job's own pool wins over its stage's, which wins over the pipeline's.

### Requirement 5 — Templates, which the file may be spread across

**User Story:** As a developer whose pipeline is built from shared templates, I want the diagram to tell me the truth about what it can and cannot see.

#### Acceptance Criteria

1. WHEN a `template` reference is encountered — as `extends`, or under `stages`, `jobs`, `steps` or `variables` — THEN the system SHALL show it on the canvas as a distinct element rather than omitting it, so the diagram never silently hides part of the pipeline.
2. WHEN a template lives in the same repository at a resolvable path THEN the system SHALL follow it and show what it contributes, marked as **coming from that template**, so a reader can tell authored-here from included-from-there.
3. IF a template cannot be followed — it lives in another repository resource, or its path depends on a parameter — THEN the system SHALL show it as an **unexpanded** element naming the template, and SHALL say why it could not be followed. A diagram that quietly drops a template is worse than one that admits the gap.
4. WHEN a template is expanded THEN the elements it contributed SHALL NOT be editable through the diagram, because their text lives in another file and the edit would land in the wrong place. They SHALL be visibly read-only and SHALL offer to open the template's own file instead.
5. WHEN `extends` is used THEN the system SHALL make clear that the pipeline's real shape is the template's, and that what this file contributes is parameters — the one case where the diagram of a file is mostly not about that file.
6. WHEN template parameters are supplied THEN the system SHALL carry them, since they frequently decide which stages exist at all.

### Requirement 6 — The dependency graph, which is the diagram

**User Story:** As an architect, I want to see which stages wait for which, because that ordering is the thing a pipeline file makes hardest to read and a diagram makes easiest.

#### Acceptance Criteria

1. WHEN stages are read THEN the system SHALL derive their dependency graph from `dependsOn`, applying the schema's rules exactly: **with no `dependsOn`, a stage depends on the one declared before it**; with `dependsOn: []` it depends on nothing and runs in parallel; with one or more names it depends on those.
2. WHEN jobs within a stage are read THEN the system SHALL apply the same rules to their own `dependsOn`, noting that jobs — unlike stages — do **not** default to sequential.
3. WHEN the graph is derived THEN fan-out and fan-in SHALL both be represented, since one stage feeding several and several converging on one is the shape the ordering exists to express.
4. WHEN steps are read THEN they SHALL be shown in their declared order within their job, because steps are a sequence and not a graph.
5. IF a `dependsOn` names a stage or job that does not exist THEN the system SHALL report it as a problem (Requirement 10) and render the edge as **broken**, rather than dropping it — a dangling dependency is precisely the mistake this diagram should catch.
6. IF the declared dependencies contain a **cycle** THEN the system SHALL report it, naming the elements in the cycle, and SHALL still render the rest of the graph.
7. WHEN a `condition` is present on a stage or job THEN the edge into it SHALL be marked conditional, and where the condition is one of the well-known forms (`succeeded()`, `failed()`, `always()`, `succeededOrFailed()`, or one naming another stage) the system SHALL indicate **which outcome** it waits for — an edge that only fires on failure is a different arrow from one that fires on success.

### Requirement 7 — Layout, computed by the module

**User Story:** As an architect, I want the pipeline to arrange itself into something readable, because a pipeline file has no coordinates for me to have chosen.

#### Acceptance Criteria

1. WHEN a pipeline is rendered THEN the system SHALL compute the layout: a **left-to-right layered arrangement** of the stage graph, where a stage's layer is its longest dependency depth, so every arrow points forward and stages that can run at the same time sit in the same column.
2. WHEN the layout is computed THEN it SHALL be **deterministic** — the same file always produces the same picture — and stable under edits that do not affect the graph, so renaming a step does not rearrange the diagram.
3. WHEN the layout is computed THEN it SHALL happen in the module on the backend, filling each `Element.position`, for the same reason `mindmap-diagram` Requirement 5.3 gives: the backend answers the viewport query, and a layout only the client knows makes that impossible.
4. WHEN positions are computed THEN they SHALL NOT be written to the pipeline file. The file has no place for them and inventing one would be exactly the kind of annotation Requirement 3.3 forbids.
5. WHEN a stage is expanded to show its jobs THEN the jobs SHALL be laid out within the stage by their own dependency graph, so the same reading applies at both levels.
6. WHEN the layout is defined THEN it SHALL be a separately testable component taking a model and returning positions, with no gRPC, filesystem or canvas dependency.
7. WHERE a pipeline is large — the schema permits 256 jobs in a stage — the layout SHALL remain readable and SHALL NOT require the user to untangle crossing edges by hand.

### Requirement 8 — Rendering

**User Story:** As an architect, I want to read the pipeline at the level I care about, from the shape of the whole thing down to an individual step.

#### Acceptance Criteria

1. WHEN a pipeline is opened THEN the system SHALL render it on the shared canvas (`adp-diagram-ide` Requirement 4.1) as stages, the jobs within them, and the dependency edges between them.
2. WHEN the diagram is shown THEN stages SHALL be **expandable**: collapsed to a single element showing its name and job count, or expanded to show its jobs, and a job in turn expandable to its steps. A pipeline read at three levels at once is unreadable; which level is shown is the reader's choice.
3. WHEN an element's kind differs THEN it SHALL be visually distinguishable — a deployment job from a plain job, a template reference from an authored element, a manual-trigger stage from an automatic one.
4. WHEN an element carries something that changes whether it runs — a condition, `continueOnError`, `enabled: false`, a matrix multiplicity, a manual trigger — THEN the element SHALL indicate it without the user opening an edit mode.
5. WHEN an element comes from a template THEN it SHALL be marked as such (Requirement 5.2) and visibly distinguished from what this file authored.
6. WHEN a collapsed stage is rendered THEN it SHALL use the existing `Group`/`Ungroup` delta actions, as folding does in `mindmap-diagram` Requirement 11.4 — the third independent use of those two actions.
7. WHEN a problem was reported on an element THEN the element SHALL be marked on the canvas, so a dangling `dependsOn` is visible where it is rather than only in a list.
8. This spec does **not** show pipeline **run** status — which stage passed, which failed, how long it took. That needs a live Azure DevOps connection, credentials and an API client, none of which ADP has; it is a substantial spec of its own and is deliberately excluded rather than half-built. What is drawn here is the pipeline as written, not any particular run of it.

### Requirement 9 — Editing: a narrow, deliberate set

**User Story:** As an architect, I want to make the structural changes a diagram is good at, and be stopped from making the ones a text editor is better at.

#### Acceptance Criteria

1. WHEN the editable set is decided THEN it SHALL be **deliberately narrow**, and this is the spec's central scope judgement. A pipeline file is executable configuration; the edits worth doing on a canvas are the ones about *structure and ordering*, which is what the diagram shows. Editing a task's inputs, a variable's value or a script's body on a canvas offers nothing a text editor does not do better, and every such edit is another way to break a build. **In scope**: rename (`displayName`), add and remove a dependency edge, add and remove a stage, add and remove a job, add and remove a step, reorder steps within a job, and toggle `enabled`. **Out of scope**: task inputs, variables, resources, triggers, pool selection, and anything inside a template.
2. WHEN any of those changes is made THEN it SHALL be an `ICommand` with a matching `ICommandHandler<TCommand>` dispatched through `IHistoryStack`, reporting the command that reverses it, per `tech.md`'s Commands rule.
3. WHEN a dependency edge is added or removed THEN the system SHALL edit the `dependsOn` of the element that declares it, and SHALL make the implicit explicit when it has to: a stage relying on the default sequential order gains an explicit `dependsOn` the moment the user draws an edge that contradicts it.
4. IF an edit would create a cycle, or would remove the last stage that has no dependencies THEN the command SHALL be rejected with a message meant for the user — a pipeline "must contain at least one stage with no dependencies", and the diagram should refuse rather than let the user write a file that cannot run.
5. WHEN an edit targets an element that came from a template THEN the command SHALL be rejected, naming the template file, and the action SHALL not have been offered (Requirement 5.4).
6. WHEN the **Toolbox** is described for this type THEN it SHALL contribute one entry per thing that can be added — **Stage**, **Job**, **Deployment job**, **Script step** — each described as data through `IDiagramToolboxProvider`, and each dropping onto a valid parent: a stage onto the canvas, a job onto a stage, a step onto a job. Dropping SHALL execute the same add command the context menu and keyboard use, so there is one implementation behind all three triggers.
7. WHEN context actions are offered THEN they SHALL be contributed through an `IContextActionProvider` for the diagram-element scope, cover the edits of 9.1, carry their shortcuts as data, and be withheld where they do not apply — in read-only mode, on a template-sourced element, or where the schema forbids the result.
8. WHEN an edit is applied THEN it SHALL reach every connection viewing that pipeline as ordinary deltas, and be one undo away.
9. WHEN the user wants an edit this spec excludes THEN the diagram SHALL offer to **open the file at that element's line**, so the answer to "I can't do that here" is the text editor, positioned where the user was looking.

### Requirement 10 — Telling the user what is wrong with their pipeline

**User Story:** As a developer, I want the diagram to point out the mistakes that a YAML file hides, because that is most of what I would draw it for.

#### Acceptance Criteria

1. WHEN a pipeline is validated THEN the module SHALL contribute its rules through the existing `IDiagramValidator` seam, so its problems reach the Errors & Warnings panel exactly as any other type's do.
2. WHEN a `dependsOn` names something that does not exist THEN it SHALL be reported, naming the missing element and the one that referenced it.
3. WHEN the dependency graph contains a cycle THEN it SHALL be reported, naming the elements in it.
4. WHEN a stage or job is **unreachable** — nothing depends on it and it depends on something that can never complete — THEN it SHALL be reported, since an orphaned stage silently never runs.
5. WHEN no stage has an empty dependency set THEN it SHALL be reported: the pipeline cannot start.
6. WHEN a stage or job has no name and no `displayName` THEN it SHOULD be reported at a low severity, since an unnamed element cannot be referred to by a `dependsOn` or read in a log.
7. WHEN a template cannot be followed THEN it SHALL be reported as information rather than as an error — an unfollowable template is a limit of the reader, not a defect in the pipeline (Requirement 5.3).
8. WHEN a problem is reported THEN it SHALL carry a **line location** in the pipeline file where the rule can name one, so the panel can take the user to it.
9. WHEN validation runs THEN it SHALL be a pure function from a parsed model to problems, testable from a plain YAML string with no file, canvas or connection — the shape `c4-diagrams` established for `C4RuleSet`.

### Requirement 11 — The core contract

**User Story:** As a developer of EtAlii.Adp, I want this type expressed in the existing element and delta vocabulary, so a third diagram type tests that vocabulary rather than extending it.

#### Acceptance Criteria

1. WHEN a client opens a pipeline THEN it SHALL do so over `DiagramService.Open` with the file's project-relative path, and report its viewport through the unary `UpdateView`.
2. WHEN an element is sent THEN it SHALL be one core `Element`: an id stable across edits, a `position` from the layout, a mime-style `type` under `azure-devops/pipeline` naming the element kind, and a module-owned payload packed into the core `Any`, defined in `diagrams/azure-pipeline/api/` without modifying the core contract files.
3. WHEN element ids are assigned THEN they SHALL be derived from the element's **path within the document** (stage name, then job name, then step index), so an id survives an edit elsewhere in the file and a selection is not lost on every keystroke. The pipeline schema gives elements names but no ids, and ADP SHALL NOT add any to the file.
4. WHEN dependency edges are sent THEN they SHALL be elements naming their two endpoint ids, as `mindmap-diagram` Requirement 11.2 and `wardley-map` established.
5. WHEN elements appear, change or disappear THEN the existing `Add` and `Remove` actions SHALL express it, an edit being an `Add` carrying the element in its new state. No new `Delta` action SHALL be added.
6. WHEN a stage is collapsed THEN `Group`/`Ungroup` SHALL express it (Requirement 8.6).
7. WHEN the pipeline file changes on disk — a `git pull`, an edit in another editor — THEN the system SHALL reload and push the difference as deltas, and that reload SHALL NOT be undoable.
8. WHEN a viewport is reported THEN the module MAY answer with the whole pipeline; these are small graphs and filtering has little to do, per the same reasoning as `wardley-map` Requirement 10.5.

### Requirement 12 — Selection, focus and navigation

**User Story:** As an architect, I want the element I select on the canvas to be the selection everywhere, and to be one click from the line that declares it.

#### Acceptance Criteria

1. WHEN the user selects an element THEN the client SHALL report it through `ContextService.Select` as a nested selection — the pipeline file as the outer level, the element as its `child` with source `DIAGRAM_CANVAS` and its `element_id` — reusing the members that already exist.
2. WHEN an element is made selectable THEN it SHALL be by registering an `IContextSourceResolver`, with no change inside the context service.
3. WHEN an element selection is pushed THEN it SHALL carry backend-resolved detail — its kind, its name, its condition, its pool, whether it came from a template — so the property grid shows what is selected without a lookup of its own.
4. WHEN the user activates an element THEN the system SHALL navigate to the **line in the pipeline file** that declares it, through the referenced-child selection `context-service` Requirement 2.3 defines. This is the type's most valuable single interaction: the diagram answers *where is this*, and one gesture takes the user there.
5. WHEN the selection changes from elsewhere THEN the canvas SHALL bring the element into view, expanding its collapsed stage if needed, and SHALL NOT re-react to a selection it produced itself.
6. Multi-selection is out of scope, per `context-service` Requirement 2.8.

### Requirement 13 — Pluggable registration

**User Story:** As a developer of EtAlii.Adp, I want this to plug in where the two before it did, so a third type demonstrates a path rather than a third special case.

#### Acceptance Criteria

1. WHEN the backend starts THEN the module SHALL be discovered through the existing `Diagram.Definition` reflection scan, declaring its origin, title, description and extensions.
2. WHEN the module organises its code THEN it SHALL be `diagrams/azure-pipeline/` with `backend/` (`EtAlii.Adp.Diagram.AzurePipeline` and its `.Tests`), `api/` and `client/`, per `structure.md`.
3. WHEN the module registers its document factory, content test, source resolver, action providers, toolbox provider, validator and command handlers THEN each SHALL be one registration through an existing extension point, gathered into one `AddAzurePipeline` extension method per tech.md's registration rule.
4. IF core canvas, storage, sync, context or command code is inspected THEN it SHALL contain no Azure-Pipelines-specific type check, import or branch. The one core change (Requirement 2.3's content test) SHALL be type-agnostic.
5. WHEN this module is complete THEN the three shipped diagram types SHALL differ in layout ownership, in who owns the document, and in how a file is recognised — and core SHALL be unchanged by those differences apart from the content-test seam. **That is the acceptance test for the pluggable model at this stage**, and any further core change needed is a finding to report rather than a step to take quietly.
6. WHEN the module reaches each state THEN `docs/diagrams.md`'s `azure-devops/pipeline` row SHALL be updated per CLAUDE.md.

## Non-Functional Requirements

### Code Architecture and Modularity

* **Single Responsibility**: the YAML CST parser/writer, the template resolver, the pipeline model, the dependency-graph derivation, the layout, the validator, the element/delta mapper, the context resolver and the canvas rendering are separate, independently testable concerns.
* **Modular Design**: `EtAlii.Adp.Diagram.AzurePipeline`, `diagrams/azure-pipeline/api` and `diagrams/azure-pipeline/client` depend only on core abstractions; nothing in core depends on the module.
* **Dependency Management**: the parser, the graph derivation and the validator SHALL each be usable without gRPC, the filesystem or a browser — given a YAML string, each returns its result.
* **Clear Interfaces**: the integration surface is the discovered `Diagram.Definition`, the content test, `IDiagramDocumentFactory`, `IContextSourceResolver`, `IContextActionProvider`(s), `IDiagramToolboxProvider`, `IDiagramValidator` and the `ICommandHandler`s. No other coupling.
* **No nested types**, per tech.md.

### Performance

* Deciding whether a `.yml` file is a pipeline SHALL be cheap enough to run over every YAML file in a large repository without a perceptible pause (Requirement 2.4).
* Opening a pipeline SHALL be one file read plus one read per followed template; templates SHALL be read once and shared between diagrams that include the same one.
* Layout SHALL stay comfortable at the schema's own limits — 256 jobs in a stage — and SHALL recompute only what a change can affect.

### Security

* The module SHALL read and write only through the backend's file-access layer, within the opened workspace folder; an absolute path SHALL never reach a client.
* A template path SHALL be resolved **inside the workspace only**; a path escaping it, whether by `..` or by absolute path, SHALL be refused and reported. A pipeline file is untrusted input that arrives with any cloned repository.
* Pipeline files routinely reference secrets by name (`$(mySecret)`, variable groups). The module SHALL render such references as the names they are and SHALL NOT attempt to resolve, fetch or display any value.
* The module SHALL make no network call. It reads a file; it does not talk to Azure DevOps.

### Reliability

* A pipeline ADP did not change SHALL round-trip byte-identically (Requirement 3.1). This is the module's headline correctness property and SHALL be tested against a corpus of real pipelines, including ones using constructs this spec does not model.
* An unparseable or unmodelled construct SHALL degrade to that construct being preserved-but-not-drawn, never to a failed open or a corrupted file.
* Every bug found while implementing or verifying this spec SHALL be covered by a test, or recorded in `tests.md`, per CLAUDE.md.

### Usability

* A pipeline authored in Azure DevOps and opened in ADP SHALL be recognisable as the same pipeline, and SHALL still run unchanged afterwards.
* The reader SHALL be able to answer "what runs before what, and what stops this from running" without leaving the canvas — that is the question the diagram exists for.
* Where the diagram cannot do something, it SHALL say so and offer the file instead (Requirement 9.9), rather than silently omitting the action.
