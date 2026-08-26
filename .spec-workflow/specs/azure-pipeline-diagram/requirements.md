# Requirements Document

## Introduction

This spec covers the **Azure DevOps Pipeline diagram module** — a diagram of a CI/CD pipeline, drawn from the `azure-pipelines.yml` a repository already has. It defines how such a file is recognised, parsed into a stage/job/step model, laid out as the dependency graph it describes, rendered on the shared canvas, and edited through a deliberately narrow set of commands.

**Why this type is different from the two before it.** The pluggable diagram-type model has been proven twice, and both times the document belonged to ADP:

|                   | [`mindmap-diagram`](../mindmap-diagram/requirements.md) | [`wardley-map`](../wardley-map/requirements.md) | This spec                        |
| ----------------- | ------------------------------------------------------- | ----------------------------------------------- | -------------------------------- |
| Positions         | computed by the module                                  | authored in the document                        | **computed** (the file has none) |
| Document          | created by ADP's Add flow                               | created by ADP's Add flow                       | **already in the repository**    |
| If ADP damages it | a diagram is wrong                                      | a diagram is wrong                              | **the build breaks**             |

That third row is what makes this type worth specifying. An `azure-pipelines.yml` is executable configuration owned by the build system, hand-authored, often split across template files, and frequently containing expressions whose value is not knowable until the pipeline runs. A diagram module that rewrites it carelessly does not produce a wrong picture; it stops the team shipping. Requirements 3 and 9 are shaped by that, and the scope decision in Requirement 9.1 follows from it.

**Dependencies.** This spec redefines none of them:

* [`adp-diagram-ide`](../adp-diagram-ide/requirements.md) — the workspace shell, the pannable/zoomable canvas, read-only mode.
* [`grpc-core-communication-specification`](../grpc-core-communication-specification/requirements.md) — `Element`, `Delta`, `Point2D`, and the `Any` payload extension point.
* [`mindmap-diagram`](../mindmap-diagram/requirements.md) — which introduced, and this spec reuses unchanged: `DiagramDefinition.Extension`, `IDiagramDocumentFactory`, `ContextSource.element_id`, `ContextScope.DIAGRAM_ELEMENT`, and `DiagramService`'s `Open` + `UpdateView` legs.
* [`wardley-map`](../wardley-map/requirements.md) — which established that a module may read a document it did not invent, and that round-tripping a hand-authored text format needs a concrete syntax tree rather than a regenerator.
* `IDiagramToolboxProvider` and `DescribeToolbox` — the toolbox seam the mindmap module already uses.
* [`context-service`](../context-service/requirements.md), [`diagram-undo-redo`](../diagram-undo-redo/requirements.md), and `tech.md`'s **Commands** rule.
* [`errors-and-warnings-panel`](../errors-and-warnings-panel/requirements.md) — `IDiagramValidator`, which this type has unusually much to say through (Requirement 10).

**What this spec changes in core.** Two things, both type-agnostic and both earned rather than convenient. First, a diagram type must be able to declare an extension **shared** — too common to claim on sight — so a bare `.yml` is not routed to whichever type happens to declare it (Requirement 2.2). Second, **Add must be offered on a file**, listing the types that declare that file's extension, so a user registers an existing file as a diagram by naming its type (Requirement 2.3). The second amends [`add-diagram-action`](../add-diagram-action/requirements.md) Requirement 4.3, which forbids Add on a file; Requirement 2.10 says why that reasoning does not cover this case. Everything else is a module registration.

## Alignment with Product Vision

* [product.md](../../steering/product.md)'s **"Don't reinvent, integrate"** — the format is the Azure Pipelines YAML schema exactly as Microsoft publishes it. ADP invents no pipeline format, and adds no file to a repository that has one.
* product.md's **"Files are the source of truth"** — the pipeline that runs in Azure DevOps and the diagram on the canvas are the same file. There is no export step and nothing to keep in sync.
* product.md's **"Linkage over illustration"** — a pipeline diagram is only worth drawing because it stays true as the pipeline changes. A picture pasted into a wiki is stale the day after it is drawn; this one cannot be.
* [tech.md](../../steering/tech.md)'s **"Wherever possible already existing text-based file formats will be used"** — taken to its conclusion: the file already exists, is already authored, and is already the source of truth for something other than ADP.
* tech.md's **Specifying a diagram type** — the four aspects it requires are Requirements 2–3 (file format), 6–8 (visualization), 9.6 (toolbox) and 9 (context actions and commands).
* [structure.md](../../steering/structure.md)'s **dependency direction** — the module depends on core; core never depends on the module.

## Requirements

### Requirement 1 — A pipeline the repository already has

**User Story:** As an architect opening a repository that already builds, I want its existing pipeline to become a diagram, without that pipeline being rewritten, moved or replaced to make it one.

#### Acceptance Criteria

1. WHEN a repository already contains a pipeline file THEN making it a diagram SHALL cost exactly one new file — the `.adp` registration of Requirement 2.4 — and no change whatsoever to the pipeline itself.
2. WHEN a registered pipeline is opened THEN the system SHALL NOT write to the repository at all. Opening a diagram is a read.
3. WHEN Add is used on a **folder** THEN this diagram type SHALL appear among the choices, since it is a discovered `Diagram.Definition` like any other; confirming it SHALL create a minimal but **valid and runnable** pipeline — a `trigger`, a `pool` and one job with one script step — rather than an empty file, because an Azure Pipelines file that does not run is not a pipeline.
4. IF the chosen name is already taken in the target folder THEN Add SHALL report the collision through the existing name validation rather than overwriting anything, exactly as `create-diagram-file` Requirement 2.8 already requires.
5. WHERE a repository holds several pipeline files (`azure-pipelines.yml`, `.azure/release.yml`) THEN each SHALL be registerable and openable as its own diagram; the module SHALL NOT assume one pipeline per repository.
6. WHERE a repository holds pipeline files the user has not registered THEN those SHALL remain ordinary files in the explorer. **Not every pipeline in a repository has to become a diagram**, and one the user never asked about is not ADP's to claim.

### Requirement 2 — Making an existing file a diagram, by saying so

**User Story:** As a developer, I want to point at the YAML file in my repository and say "this is a pipeline diagram", rather than having ADP guess which of my YAML files are pipelines.

#### Acceptance Criteria

1. WHEN the module declares itself THEN it SHALL declare `.yml` and `.yaml` as the extensions its documents use, and SHALL declare them **shared** — an extension too common for any one type to claim on sight. A `docker-compose.yml`, a Kubernetes manifest and an OpenAPI document all end in `.yml`, and none of them is a pipeline.
2. WHEN a body file carries a **shared** extension and has no `.adp` registration beside it THEN the system SHALL NOT route it to any diagram type. Today's bare-body routing stays exactly as it is for a **distinctive** extension — `.mm`, `.owm`, `.dsl` — which one type or one vendor's family owns outright; only the shared case is withheld.
3. WHEN the user opens the context menu on a **file** whose extension at least one diagram type declares THEN the system SHALL offer **Add**, and the dialog SHALL list **only the diagram types that declare that file's extension** — for a `.yml`, this type; for a `.dsl`, the C4 family. The user names the type; ADP does not infer it. *(This is what replaces the content sniffing an earlier draft of this requirement proposed: a heuristic that reads a file and guesses is both slower and less honest than an option the user picks once.)*
4. WHEN the user confirms a type for a file THEN the system SHALL create `<name>.adp` beside it, naming that type's MIME type, and SHALL NOT modify, move or rewrite the body file in any way. Registering a file is a create of one new file, never a change to an existing one.
5. WHEN the registration exists THEN the file SHALL open as a diagram through the ordinary `.adp`-first routing every type already uses, and no further recognition SHALL be needed.
6. WHEN Add is offered on a file THEN it SHALL read as **registering an existing file**, distinct from Add on a folder, which creates a new diagram and its body. The two share the dialog and the commit seam but not their meaning, and the action's label SHALL say which one the user is about to do.
7. IF a file already has an `.adp` registration THEN Add SHALL NOT be offered on it — it is already a diagram — and the existing open action SHALL be offered instead.
8. IF no diagram type declares a file's extension THEN Add SHALL NOT be offered on that file, rather than opening a dialog with nothing selectable in it.
9. WHEN the created `.adp` is written THEN it SHALL reach every connected client through the existing watcher and `EntryCreated` path, and be selected on the connection that added it, exactly as `create-diagram-file` Requirement 4 already defines.
10. **This amends** [`add-diagram-action`](../add-diagram-action/requirements.md) **Requirement 4.3**, which says Add SHALL NOT be offered on a file because "a file cannot contain a new entry". That reasoning holds for creating a diagram *inside* something, and this is a different act: the new entry is the `.adp` created *beside* the file, in its parent folder. That requirement needs revising alongside this one; it is named here so the contradiction is tracked rather than discovered.

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
9. ~~WHEN the user wants an edit this spec excludes THEN the diagram SHALL offer to **open the file at that element's line**, so the answer to "I can't do that here" is the text editor, positioned where the user was looking.~~ **Descoped.** ADP has no text editor to open: a non-diagram file renders `PanelPlaceholder`, and `ContextExecutionResult` has no case that asks for a navigation — a provider can request input, confirmation or a choice, or report that it is done, and nothing more. The locator half is already built and is not wasted: every element carries `first_line` and `last_line` in `azure-pipeline.proto`, so whatever adds a text editor need only add the destination. Restoring this requirement needs two things that are outside this spec — a text-editor panel, and a way for a context action to publish a selection (the file → location chain `context-service` Requirement 2.3 describes as a *contained* child). Offering an action that completed without going anywhere would have been worse than offering none, so Requirement 9.7's action set ships without it.

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

### Requirement 13 — What a pipeline element shows in the Property Grid

**User Story:** As an architect who has selected a stage, I want the panel to tell me the things that decide whether and how it runs, without my having to read the YAML to find them.

> **The mechanism belongs to the Property Grid work, in progress on `claude/property-grid`.** This requirement does not design it. It fixes the half that is this module's to decide — **which properties a pipeline element exposes, and which of them may be edited** — and binds that to the contract that work has settled:
>
> * **`IContextPropertyProvider`**, a per-**scope** provider mirroring `IContextActionProvider` rather than a per-origin one. This module registers for `ContextScope.DiagramElement` and receives a `ContextTarget` already resolved and containment-checked. It is deliberately *not* carried on `ContextLevelDetail`, whose `{ text, has_children, folded, linked }` is mindmap-shaped — C4 already loses its description and technology squeezing into it, and this type would lose far more.
> * **`ContextPropertyDefinition(Id, Label, Value, Editor, ReadOnlyReason, Group)`** — described as data the panel renders without understanding, as `ToolboxItemDefinition` is. `Value` is always text. `ReadOnlyReason` empty means editable; otherwise it is a sentence shown beside the value, mirroring how an unavailable action carries a reason rather than being omitted.
> * **`SetAsync` dispatches an `ICommand` through `IHistoryStack`**, so a property edit is one undo away like every other edit, and **commits on Enter or blur, never per keystroke** — one call per committed edit.
>
> Requirements 13.2–13.5 (what is shown) and 13.6–13.7 (what may be edited) are this module's own and stand whatever the mechanism becomes. Where the two must agree, this spec names the contract above rather than restating it.

#### Acceptance Criteria

1. WHEN a pipeline element is selected THEN the module SHALL contribute that element's properties by registering an `IContextPropertyProvider` for `ContextScope.DiagramElement`, resolving the element from the `ContextTarget` it is given. Registering it SHALL be one line, like every other seam this module uses, and core SHALL learn nothing about pipelines.
2. WHEN a **stage** is selected THEN the panel SHALL show at least: its `stage` name, its `displayName`, its `dependsOn` (with the implicit sequential default shown as what it resolves to, not as absent), its `condition`, its `pool`, whether it is `trigger: manual`, whether it is `isSkippable: false`, and its job count.
3. WHEN a **job** is selected THEN the panel SHALL show at least: its name and `displayName`, whether it is a plain job or a `deployment` job, its `dependsOn`, its `condition`, its `pool`, its `timeoutInMinutes`, its `continueOnError`, and — where it has one — its `strategy` with the multiplicity a `matrix` or `parallel` produces. For a deployment job it SHALL also show its `environment` and its strategy kind (`runOnce`, `rolling`, `canary`).
4. WHEN a **step** is selected THEN the panel SHALL show at least: its kind (`task`, `script`, `bash`, `pwsh`, `powershell`, `checkout`, `download`, `publish`, `template`), its `displayName`, its `condition`, its `continueOnError`, its `enabled`, and the value that identifies it — a task's name and version, or a script's first line.
5. WHEN a **dependency edge** is selected THEN the panel SHALL show which element declares it and which it points at, and the condition governing it, so the answer to "why does this wait" is in one place.
6. WHEN a property is contributed THEN its `ReadOnlyReason` SHALL be empty for exactly the set Requirement 9.1 permits — `displayName`, `dependsOn` and `enabled` — and non-empty for everything else in Requirements 13.2–13.5. A task's inputs, a pool, a strategy, an environment and a condition are all **shown but not editable**: a text editor edits them better, and a bad write breaks the build. The reason SHALL say that, so the panel can explain rather than merely grey the value out. A reason is a **contract, not a hint**: `ContextPropertyResolver` refuses a write to any property its provider marked read-only, so a stale grid cannot write a value this module said was unwritable.
7. WHEN the selected element came from a **template** (Requirement 5) THEN every one of its properties SHALL carry a `ReadOnlyReason` naming the template file the value lives in, whatever Requirement 13.6 would otherwise permit. This is the case that makes a reason worth more than a flag: the value is real, worth showing, and unwritable *here* — and the reader's next question is where it is written, which the reason answers.
8. WHEN properties are grouped THEN the module SHALL use `Group` to separate what a reader is asking about — identity, ordering (`dependsOn`, `condition`), and execution (`pool`, `strategy`, `environment`, `timeoutInMinutes`) — because a stage carries more than reads comfortably as one flat list.
9. WHEN a property carries an **expression** (`$[ ]`, `${{ }}`) THEN the panel SHALL show the expression itself rather than a resolved value, and mark it as unresolved — its value is not knowable until the pipeline runs (Requirement 3.4), and showing a guess would be a lie the user acts on.
10. WHEN a property is **absent** from the document THEN the module SHALL NOT contribute it at all, rather than contributing it with an empty `Value`. A stage with no `condition` has no condition row; a stage with `condition: ''` has a row whose value is empty. The two are different states of the file and the panel SHALL show them differently, which is why no sentinel value is used for either.
11. WHEN an editable property is changed THEN `SetAsync` SHALL dispatch the same `ICommand` the canvas and the context menu use (Requirement 9.2), so a rename from the panel and a rename from the diagram are one implementation, land on the project's history, and are one undo away. WHERE a value is rejected — a `dependsOn` naming a stage that does not exist, an edit that would create a cycle (Requirement 9.4) — the result SHALL carry the reason for the panel to show, and the document SHALL be unchanged.
12. WHEN a value is committed THEN the module SHALL rely on the contract's cadence — **on Enter or blur, once per committed edit, never per keystroke**. A pipeline edit rewrites lines of an executable file, and per-keystroke writes would put the repository through a state per character typed.
13. WHEN a property change is applied THEN the resulting document change SHALL reach every connection viewing that pipeline as ordinary deltas, and the panel SHALL follow the pushed selection rather than holding its own copy of the element.
14. WHERE the contract's editors are `Line`, `Text` and `Toggle` THEN `displayName` and `enabled` are served by `Line` and `Toggle`, and every read-only property of Requirements 13.2–13.5 is served by `Line` or `Text`. **`dependsOn` is the one that is not.** It is a list of names drawn from a known set — the other stages, or the other jobs in this stage — and a free-text box gives the user no idea what those names are.
    * This is an **ergonomics** gap, not a safety one, and the distinction matters for sequencing. Requirement 13.10 already refuses an invalid value and leaves the document unchanged, so a mistyped name is rejected with a reason rather than written and then reported. The pipeline file is safe either way; what a text box costs is the user typing a name they could have picked.
    * **A `Choice` editor carrying provider-supplied candidates is therefore a planned addition rather than a condition of this requirement.** It does not exist, because no diagram type has needed one yet; adding it is a backward-compatible proto enum value plus candidates on the message plus a select in the panel, and **this module's implementation is what adds it**. Specifying it as owed rather than as a fallback keeps the sequencing honest: nothing in the contract pretends to support it in the meantime.
    * `dependsOn` is genuinely a **list**, which a single `Value` string cannot carry without a delimiter convention. This spec deliberately does **not** invent one. Whether the answer is a repeated value on the message or an encoding is a decision worth making against working code and a second consumer, not against this paragraph.

### Requirement 14 — Pluggable registration

**User Story:** As a developer of EtAlii.Adp, I want this to plug in where the two before it did, so a third type demonstrates a path rather than a third special case.

#### Acceptance Criteria

1. WHEN the backend starts THEN the module SHALL be discovered through the existing `Diagram.Definition` reflection scan, declaring its origin, title, description and extensions.
2. WHEN the module organises its code THEN it SHALL be `diagrams/azure-pipeline/` with `backend/` (`EtAlii.Adp.Diagram.AzurePipeline` and its `.Tests`), `api/` and `client/`, per `structure.md`.
3. WHEN the module registers its document factory, source resolver, action providers, toolbox provider, validator and command handlers THEN each SHALL be one registration through an existing extension point, gathered into one `AddAzurePipeline` extension method per tech.md's registration rule.
4. IF core canvas, storage, sync, context or command code is inspected THEN it SHALL contain no Azure-Pipelines-specific type check, import or branch. Both core changes (Requirements 2.2 and 2.3) SHALL be type-agnostic, and the next type with a shared extension — `.json`, `.xml` — SHALL need neither of them changed again.
5. WHEN this module is complete THEN the three shipped diagram types SHALL differ in layout ownership, in who owns the document, and in how a file becomes a diagram — and core SHALL be unchanged by those differences apart from the two seams Requirement 2 names. **That is the acceptance test for the pluggable model at this stage**, and any further core change needed is a finding to report rather than a step to take quietly.

> **Outcome of the Requirement 14.5 review (task 30).** The acceptance test passes, and two further
> core changes are reported here rather than taken quietly.
>
> **The three types do differ in all three ways.** *Layout ownership*: a mindmap's tree is its
> layout, C4 computes an arrangement a user may override through a sidecar, and a pipeline computes
> a layered one that nobody may override — it has no coordinates to store and refuses a drag.
> *Document ownership*: the mindmap owns one `.mm` per diagram, C4 shares one workspace across many
> views, and a pipeline owns one file per diagram but reads others it may not write (its templates).
> *How a file becomes a diagram*: a `.mm` routes on sight, a `.dsl` routes through an `.adp` naming
> a view, and a `.yml` routes only where somebody registered it.
>
> **Core carries no Azure-Pipelines-specific branch, import or type check.** Every mention of the
> words in `EtAlii.Adp.Backend`, `EtAlii.Adp.Diagram` and the client shell is an explanatory comment
> citing the motivating case, or an unrelated use of "pipeline". Both Requirement 2 seams are
> type-agnostic: `SharedExtension` is a flag any definition may set, and the router's guard reads it
> off whichever definitions claim the extension. A `.json` or `.xml` type would need neither changed.
>
> **Two core changes beyond those seams, both reported:**
>
> 1. *The `Choice` property editor* (`context.proto`, `ContextPropertyDefinition`,
>    `ContextServiceImpl.Properties`, `PropertyRow.tsx`). Predicted and assigned by Requirement
>    13.14 — "this module's implementation is what adds it" — so it is a planned widening rather
>    than a surprise, and it is type-agnostic: a new enum value plus provider-supplied candidates
>    that the panel renders without understanding.
> 2. *`ContextSelectionResolver` taking the first resolver that claims an id shape rather than the
>    first that accepts the id.* This is not a widening for this type at all. Every diagram
>    module's resolver claims every element id, because `CanResolve` asks about the shape of an id
>    and ownership depends on the enclosing file. Taking the first claimant gave whichever module
>    registered earliest silent ownership of every element selection in the application — a C4
>    element could not be selected at all. The fix makes core *more* type-agnostic, and was found
>    only because a third type finally collided with the second.
>
> The second finding is the one worth carrying forward: the pluggable model held, but a defect in
> it stayed invisible for two diagram types and surfaced on the third.
6. WHEN the module reaches each state THEN `docs/diagrams.md`'s `azure-devops/pipeline` row SHALL be updated per CLAUDE.md.

## Non-Functional Requirements

### Code Architecture and Modularity

* **Single Responsibility**: the YAML CST parser/writer, the template resolver, the pipeline model, the dependency-graph derivation, the layout, the validator, the element/delta mapper, the context resolver and the canvas rendering are separate, independently testable concerns.
* **Modular Design**: `EtAlii.Adp.Diagram.AzurePipeline`, `diagrams/azure-pipeline/api` and `diagrams/azure-pipeline/client` depend only on core abstractions; nothing in core depends on the module.
* **Dependency Management**: the parser, the graph derivation and the validator SHALL each be usable without gRPC, the filesystem or a browser — given a YAML string, each returns its result.
* **Clear Interfaces**: the integration surface is the discovered `Diagram.Definition` — its shared-extension declaration included — plus `IDiagramDocumentFactory`, `IContextSourceResolver`, `IContextActionProvider`(s), `IDiagramToolboxProvider`, `IDiagramValidator` and the `ICommandHandler`s. No other coupling.
* **No nested types**, per tech.md.

### Performance

* Offering Add on a file SHALL cost one extension lookup against the discovered definitions. No file is read to decide what a file is, which is the point of Requirement 2.3 over the content test an earlier draft proposed: a repository full of large YAML files costs nothing to browse.
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
* Where the diagram cannot do something, it SHALL say so rather than silently omitting the action. ~~and offer the file instead (Requirement 9.9)~~ — offering the file is descoped with Requirement 9.9, since there is no text editor to offer; until one exists, an action that cannot succeed is withheld and the reason is reported when the equivalent edit is attempted through the property grid.
