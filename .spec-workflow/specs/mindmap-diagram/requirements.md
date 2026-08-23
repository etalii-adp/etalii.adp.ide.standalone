# Requirements Document

## Introduction

This spec covers the **Mindmap diagram module** — the first concrete diagram type built on top of EtAlii.Adp's pluggable diagram-type model (per [`adp-diagram-ide`](../adp-diagram-ide/requirements.md) Requirement 4.4). It defines how a mindmap is **created** from the Add dialog, how it **declares itself** through an `.adp` file while its body lives in a `.mm` sibling, how it is **laid out**, **rendered** and **edited** on the shared canvas, how every edit travels as a **command** and reaches other clients as a **delta**, and how a node becomes **selectable and focusable** through the context service — all without the core needing any mindmap-specific knowledge, per `structure.md`'s `diagrams/<diagram>` module convention (`backend/`, `api/`, `client/`).

Per [`tech.md`](../../steering/tech.md)'s "Testing & quality" section, Mindmap (`.mm`) is explicitly named as the diagram type used to validate the modular diagram architecture — this spec is that validation, not just a standalone feature.

**Dependencies.** This spec does not redefine what it builds on:

* [`grpc-core-communication-specification`](../grpc-core-communication-specification/requirements.md) — the `Path`, `ViewUpdate`, `Element` and `Delta` (`add`/`remove`/`group`/`ungroup`) contract this module speaks, and its `Any` payload extension point.
* [`grpc-core-communication`](../grpc-core-communication/requirements.md) — the runtime behaviour of that contract (streaming, reconciliation, reconnect).
* [`adp-diagram-ide`](../adp-diagram-ide/requirements.md) — the workspace shell, the pannable/zoomable canvas, virtualization, read-only mode.
* [`add-diagram-action`](../add-diagram-action/requirements.md) — the **Add** action, reflection-based discovery of `Diagram.Definition`, and the grouped choice dialog. `EtAlii.Adp.Diagram.Mindmap` already publishes `new DiagramOrigin("freeplane", "mindmap")` and is already discovered.
* [`create-diagram-file`](../create-diagram-file/requirements.md) — the `.adp` file, its MIME-type first line, the name field, and the watcher-driven appearance of the new file. Its Requirement 3.5 deliberately left "a reference to a sibling file in the type's native format" to a later spec; **this is that spec, for mindmaps**.
* [`context-service`](../context-service/requirements.md) — the single per-connection selection, its recursive `none | action | child` chain, and the pushing of a selection's available actions.
* [`diagram-undo-redo`](../diagram-undo-redo/requirements.md) and `tech.md`'s **Commands** section — every state change is an `ICommand` with an `ICommandHandler<TCommand>` that names its own inverse.

**What this spec changes in core.** Four things, each type-agnostic and usable unchanged by the next diagram type: `DiagramDefinition` gains a document extension (Requirement 2.2), `ContextSource` gains an `element_id` member with its own resolver (Requirement 10.2), `ContextScope` gains a value routing to a diagram type's own providers (Requirement 10.3), and the core contract's `add` delta is defined as an **upsert** rather than an insert (Requirement 11.3). Nothing in core learns what a mindmap is.

The fourth is a change to a *meaning* rather than to a message, so it is called out here rather than left implicit: `grpc-core-communication-specification` Requirement 3.2 currently reads "carry one or more elements to add", and every diagram type that can edit an element needs it to also mean "replace the element with this id". Amending that spec is a prerequisite of this one — redefining the member here alone would be extending the contract silently, which Requirement 11 exists to prevent.

## Alignment with Product Vision

* [product.md](../../steering/product.md)'s **"Diagramming on files"** — implemented with Mindmap as the concrete, first diagram type.
* product.md's **"Don't reinvent, integrate"** — the body is the established FreeMind/Freeplane `.mm` XML format, not a new one; the keyboard and context vocabulary is the one mindmap users already have in their fingers (Requirement 8); the type is declared with the MIME vocabulary `docs/diagrams.md` and `DiagramOrigin` already use.
* product.md's **"Files are the source of truth"** — a mindmap is two plain text files in the project folder, created through the same watcher path as a file created outside ADP; nothing about it lives only in memory or only in a database.
* product.md's **"Linkage over illustration"** — Requirement 12 keeps nodes tied to the code they describe as that code evolves.
* `tech.md`'s **Commands** — every mindmap edit is a command with an inverse, so undo/redo is inherited rather than reimplemented (Requirement 6).
* `tech.md`'s **Context** — a node becomes selectable by registering a resolver, not by growing a mindmap-specific selection mechanism (Requirement 10).
* [structure.md](../../steering/structure.md)'s **dependency direction** — the module depends on core abstractions; core never depends on the module (Requirement 13).

## Requirements

### Requirement 1 — A mindmap is created through the Add action

**User Story:** As an architect, I want to create a mindmap by choosing "Mind map" in the Add dialog, so that starting one takes no more than the flow ADP already has for adding a diagram.

#### Acceptance Criteria

1. WHEN the user opens the Add dialog on a folder or on the project root (`add-diagram-action` Requirement 4) THEN **Mind map** SHALL appear in it under the `freeplane` vendor group, by the `Title` its already-published `Diagram.Definition` carries — through discovery alone, with no mindmap-specific entry anywhere in the dialog or its provider.
2. WHEN the user selects Mind map and has not edited the name THEN the suggested file name SHALL be `mindmap`, made collision-free as `create-diagram-file` Requirement 2.2 defines (`mindmap-2`, `mindmap-3`, …).
3. WHEN the user confirms THEN the system SHALL create **both** files of Requirement 2 — the `.adp` registration file and its `.mm` body — in the target folder, at `AddDiagramContextActionProvider.CommitAsync`, through the command discipline of Requirement 6; no step of the Add flow before that seam SHALL change (`add-diagram-action` Requirement 6.3).
4. WHEN the `.mm` body is written at creation THEN it SHALL contain exactly **one root node**, whose text is the file's base name, and no other nodes — a new mindmap opens as a named, empty map rather than as an error or a blank canvas with nothing to type into.
5. WHEN creation completes THEN the new `.adp` file SHALL appear in every connected client's hierarchy and SHALL be selected on the connection that added it, exactly as `create-diagram-file` Requirement 4 already defines; this spec SHALL NOT add a second notification path for the `.mm` sibling.
6. IF writing either file fails THEN the system SHALL leave **neither** behind, report the failure in the open dialog and change nothing on disk, per `create-diagram-file` Requirement 5 — a half-created mindmap SHALL never be observable.
7. WHEN creation completes on the connection that requested it THEN the workspace SHALL open the new mindmap in a diagram view and give it focus, without a second round trip asking for it. The client SHALL reach that conclusion from what it already receives: the `Select` of the created `.adp` file that Requirement 1.5 pushes names a diagram file this connection has no view open for, which is the whole trigger. The client SHALL NOT infer it from having been the caller of the Add, so that opening a diagram stays one behaviour rather than one for files this client created and another for files it did not.

### Requirement 2 — Registration in the `.adp` file, storage in the `.mm` sibling

**User Story:** As an architect, I want the file I see in the explorer to say what kind of diagram it is, while the mindmap itself stays in a real Freeplane file, so that ADP knows how to open it and Freeplane can still read it.

#### Acceptance Criteria

1. WHEN a mindmap exists on disk THEN it SHALL be a **pair of files sharing one base name** in one folder: `<name>.adp`, whose first line is the MIME type `freeplane/mindmap` (`create-diagram-file` Requirement 3), and `<name>.mm`, holding the mindmap body in the FreeMind/Freeplane XML format (Requirement 3). The `.adp` file is the **registration**; the `.mm` file is the **document**.
2. WHEN the backend needs to know which file holds a diagram type's body THEN the diagram type SHALL declare its **document extension** on its own `Diagram.Definition` — a new, type-agnostic member on the shared `DiagramDefinition` abstraction (e.g. `Extension`, `".mm"` here) — so the creating and opening code derives the sibling's name by construction and core never carries a mapping from MIME type to extension. A definition that declares **no** extension keeps today's behaviour: the `.adp` file is the whole diagram.
3. WHEN an `.adp` file is opened THEN the system SHALL read its first line, resolve it to the discovered `DiagramDefinition` whose `MimeType` matches, and route the diagram to that module — where an `.adp` file is present it, and not the extension of the body, is what decides which module opens it. Requirement 2.7 is the single, deliberate exception, for a body that has no `.adp` file at all.
4. WHEN the module opens a mindmap THEN it SHALL read the `.mm` sibling named by Requirement 2.2, and WHEN it saves one THEN it SHALL write that same sibling; the `.adp` file SHALL NOT be rewritten by any edit in this spec.
5. IF the `.adp` file names `freeplane/mindmap` but its `.mm` sibling is **missing** THEN the system SHALL open the diagram as an empty map with a single root node named after the file (Requirement 1.4) and create the sibling on the first save, rather than failing to open — a deleted or not-yet-committed body is a recoverable state, not a broken diagram.
6. IF the `.adp` file's first line names a MIME type no discovered definition matches THEN the system SHALL report that the diagram type is not available, naming the MIME type it read, rather than opening an empty canvas.
7. WHEN a `.mm` file is present in a project **without** an `.adp` sibling (a map created in Freeplane and dropped into the folder) THEN the system SHALL still let it be opened as a mindmap, deriving the type from the `.mm` extension through the same declared-extension member of Requirement 2.2; the missing `.adp` file SHALL NOT be created implicitly — interoperability is not conditional on ADP having created the file.
8. IF more than one discovered `DiagramDefinition` declares the same document extension THEN the system SHALL NOT route by extension at all for that extension, SHALL report the ambiguity naming every definition that claimed it, and SHALL still open any such file that does have an `.adp` sibling by Requirement 2.3 — guessing which module owns a body is worse than saying the deployment is ambiguous, and mirrors how discovery already refuses to guess on a duplicate `DiagramOrigin`.
9. WHEN the explorer renders the pair THEN the `.adp` file SHALL carry the diagram icon `create-diagram-file` Requirement 6.1 defines; whether the `.mm` sibling is shown as its own row, nested under it, or hidden is a presentation decision this spec leaves open, but the two SHALL NOT be presented as two unrelated diagrams.
10. WHEN the `.adp` file of a pair is **renamed or moved** THEN the system SHALL rename or move the `.mm` sibling with it, keeping the shared base name of Requirement 2.1 intact, as **one** command whose inverse restores both (Requirement 6.2) — so a rename cannot leave a registration pointing at a body that is no longer its sibling, and undoing it cannot put one of the two back on its own.
11. WHEN the `.adp` file of a pair is **deleted** THEN the system SHALL delete the `.mm` sibling with it, as one command, so no orphaned body is left behind. Since deleting is deliberately not undoable (`diagram-undo-redo` Requirement 3.5), the confirmation SHALL name **both** files, so what is about to be lost is what the user was shown.
12. IF the `.mm` sibling is renamed, moved or deleted on its own — from outside ADP, or through a future action that reaches it directly — THEN the `.adp` file SHALL be treated as a registration whose body is missing and SHALL open by Requirement 2.5 rather than reporting a broken diagram; the pair SHALL be repairable by putting a body back, not only by recreating the diagram.

### Requirement 3 — The `.mm` file format

**User Story:** As a diagram author, I want my mindmaps stored as plain, diffable Freeplane files, so that I can version, review and merge them like any other file in my repository, and open them in Freeplane.

#### Acceptance Criteria

1. WHEN a mindmap is written THEN the system SHALL use the established FreeMind/Freeplane-compatible XML mindmap format, per `tech.md`.
2. WHEN a `.mm` file is read THEN the system SHALL parse it per that format, so mindmaps created or edited outside ADP remain interoperable, and SHALL preserve attributes and elements it does not itself understand, so a round trip through ADP never silently strips another tool's data.
3. WHEN a mindmap is saved THEN the system SHALL write it back with stable, minimal-diff serialization (consistent element ordering, attribute formatting and line endings) so that saving a map ADP did not change produces **no** diff. The one-time id normalisation of Requirement 3.4 is the sole exception, and only on the first save of a map that arrived without ids.
4. WHEN a node needs an identity THEN the system SHALL use the `.mm` node's own `ID` attribute as the element identity on the wire (Requirement 11.2), and WHEN a node carries none THEN the system SHALL assign it a `ShortGuid`-based id (per `tech.md`'s identity rule) on load. Those assigned ids SHALL be **persisted on the next save of that file**, not written back on open: a map opened and not edited SHALL produce no write at all, while a map that is edited carries its ids from then on. Identity has to outlive the connection because a link (Requirement 12) and a selection (Requirement 10) both name a node by id, and an id reinvented on every open would silently break both.
5. IF ADP-specific data has no place in the standard `.mm` schema THEN the system SHALL store it in the diagram's own `.adp` file, after the MIME-type first line, rather than extend or mutate the standard format or introduce a third file. `create-diagram-file` Requirement 3.4 already shaped the `.adp` file for this — nothing may be written *before* the MIME line, precisely so a later reader can find more after it — and Requirement 3.5 of that spec leaves what follows undefined for a spec like this one to define. A mindmap therefore stays **two** files, never three.
6. Code-artifact links are **not** such data: the `.mm` schema has a native home for them, and Requirement 12 uses it. This is stated because an earlier revision of this spec assumed the opposite and specified a sidecar file for them.
7. WHEN the backend loads or saves a `.mm` file THEN this SHALL happen entirely within the backend's existing file-access layer (per `adp-diagram-ide` Requirement 2); the mindmap module SHALL NOT introduce its own filesystem access path, and SHALL write atomically (temporary file in the same folder, then move into place) to the same standard `create-diagram-file` Requirement 3.3 sets.
8. IF a `.mm` file cannot be parsed THEN the system SHALL surface a clear error naming the file rather than partially loading a corrupted tree, silently discarding content, or crashing the workspace.

### Requirement 4 — Tree structure and canvas rendering

**User Story:** As a diagram author, I want to see my mindmap as a connected tree of nodes on the canvas, so that I can understand its structure at a glance.

#### Acceptance Criteria

1. WHEN a mindmap is opened THEN the system SHALL render exactly one root node plus its descendants, with each parent visually connected to its children.
2. WHEN a mindmap is opened THEN the system SHALL render it on the shared pannable/zoomable canvas defined by `adp-diagram-ide` Requirement 4.1, rather than a mindmap-specific rendering surface.
3. IF a mindmap contains a large number of nodes THEN the system SHALL only render nodes currently in view, consistent with `adp-diagram-ide` Requirement 4.3's virtualization, driven by the viewport contract of Requirement 11.5.
4. WHEN a node carries notes (Requirement 7.7), a code-artifact link (Requirement 12.2) or collapsed children (Requirement 9.1) THEN the node SHALL indicate each on the canvas, so what a node holds is visible without entering an edit mode.

### Requirement 5 — A default layout, computed by the module

**User Story:** As a diagram author, I want my mindmap to arrange itself, so that I add ideas instead of dragging boxes, and the map stays legible as it grows.

#### Acceptance Criteria

1. WHEN a mindmap is rendered THEN the system SHALL apply an **automatic layout** — the conventional mindmap arrangement: the root centred, its branches distributed to either side, each node's children stacked along the cross axis with depth expressed as distance from the root — and SHALL NOT require or maintain manual x/y placement for any node.
2. WHEN the layout is computed THEN it SHALL be **deterministic**: the same tree SHALL always produce the same positions, so two clients viewing one map see the same map and a reconnect does not reshuffle it.
3. WHEN the layout is computed THEN it SHALL happen in the **mindmap module on the backend**, and its result SHALL be what fills each `Element.position` sent over the contract (Requirement 11.2). This is what lets the backend answer "which nodes fall inside this client's bounding box" for Requirement 11.5's virtualization; a layout known only to the client would make viewport-driven delivery impossible.
4. WHEN a structural change is applied (Requirement 7) THEN the system SHALL recompute the layout and push the resulting position changes as part of the same delta exchange, so the client never has to run its own layout to stay correct.
5. WHEN the layout is defined THEN it SHALL be a **separately testable component** taking a node tree and returning positions, with no gRPC, filesystem or canvas dependency, and it SHALL be the module's **default** — replaceable within the module by another layout later without any change to the contract, the canvas or core.
6. WHEN a node's size depends on its text THEN the layout SHALL account for it well enough that siblings do not overlap. Since the layout runs on the backend (Requirement 5.3) and real font metrics exist only in the browser, the module SHALL size nodes from a **declared font metric** — the canvas's node font, its size, and a per-character advance table or average advance shipped with the module — and SHALL NOT ask the client to measure text before it can lay out, which would make the first paint wait on a round trip. The client SHALL render node boxes at the size the backend computed rather than shrinking them to its own measurement, so both ends agree on where a node is even when the estimate is imperfect.
7. WHERE the declared metric and the browser's actual rendering disagree enough for text to overflow its box, the module SHALL allow the box to clip or wrap by its own rule rather than move the node — a node that sits where the backend said it sits is worth more than a perfectly fitted box, because position is what the viewport query in Requirement 11.5 is answered against.
8. WHEN layout positions are computed THEN they SHALL NOT be written to the `.mm` file — the file holds the tree, the layout is derived from it (Requirement 3.3's no-diff rule depends on this).

### Requirement 6 — Every mindmap change is a command

**User Story:** As a developer of EtAlii.Adp, I want every mindmap edit to be an `ICommand` with a handler that names its inverse, so that undo/redo, validation and multi-client delivery are inherited from the system rather than rebuilt inside this module.

#### Acceptance Criteria

1. WHEN anything changes a mindmap's document state THEN it SHALL be expressed as an `ICommand` with a matching `ICommandHandler<TCommand>`, dispatched through `IHistoryStack`, per `tech.md`'s Commands section. No gRPC method, context action or canvas interaction SHALL modify a mindmap inline.
2. WHEN a mindmap command handler succeeds THEN it SHALL report the command that reverses it as `CommandResult.Inverse`, so the change becomes undoable on the project's history (`diagram-undo-redo` Requirements 1.1 and 3.1) without this module implementing undo itself.
3. WHEN a mindmap command is rejected — the node is gone, the value is invalid, the map is read-only — THEN the handler SHALL return a `CommandResult` carrying a message meant for the user, **not** throw; exceptions stay reserved for programming errors.
4. WHEN a handler runs THEN it SHALL validate its own preconditions against current state, because undo and redo dispatch it again long after the original call and it can trust nothing checked earlier.
5. WHEN mindmap handlers are placed THEN they SHALL live in a `Commands` subfolder of the mindmap module, keeping that module's namespace, following the placement rule `tech.md`'s Commands section states. They SHALL be registered by an `AddMindmapCommands` extension **the module itself owns**, which the host calls alongside core's `AddCommands`; a mindmap command SHALL NOT be registered one-by-one in the host, and SHALL NOT be added to core's own `AddCommands`. The latter lives in `EtAlii.Adp.Backend` and would have to reference `EtAlii.Adp.Diagram.Mindmap` to name a mindmap handler, which is exactly the dependency direction Requirement 13.4 and `structure.md` forbid. Every diagram module registers its handlers the same way.
6. WHEN a command is applied THEN the resulting change SHALL reach every connection viewing that diagram as a delta (Requirement 11), including the connection that issued it, so the backend's answer is the single source of what the document now is and no client diverges from it by applying its own edit independently.
7. WHERE Requirement 6.6 forbids a client applying an edit to its own document state, it SHALL NOT be read as forbidding local feedback while a command is in flight: an inline editor MAY keep showing what the user typed, and a node MAY show that a change is pending, until the delta lands and replaces it. What a client SHALL NOT do is treat its own optimistic guess as the document, or keep it if the delta says otherwise. Requirement 7.10's "immediately" is therefore measured on the delta round trip, within the latency budget the Performance section sets, not on the keystroke.
8. WHEN the diagram is open in read-only mode (`adp-diagram-ide` Requirement 5) THEN every document-changing command SHALL be rejected per Requirement 6.3, and the actions that would dispatch them SHALL not be offered (Requirement 8.6).
9. WHEN the same command is dispatched twice with the same arguments against the same state THEN the outcome SHALL be well defined and the inverse SHALL still reverse exactly one application — an undo SHALL never leave a subtree partially restored.

### Requirement 7 — Adding, updating and removing elements

**User Story:** As a diagram author, I want to add, rename, move and delete nodes freely, so that I can shape my mindmap as my thinking evolves.

#### Acceptance Criteria

1. WHEN the user adds a **child** node to an existing node THEN the system SHALL insert it as the last child of that node; its inverse SHALL remove exactly that node.
2. WHEN the user adds a **sibling** node to an existing non-root node THEN the system SHALL insert it immediately after that node under the same parent; its inverse SHALL remove exactly that node.
3. WHEN the user **moves** a node THEN the system SHALL move its entire subtree with it, to a new parent and/or a new position among its siblings; its inverse SHALL restore both the previous parent and the previous position among siblings, not merely the previous parent.
4. WHEN the user **removes** a node THEN the system SHALL remove that node and its entire subtree, after confirming with the user if the node has children; its inverse SHALL restore the whole subtree, with its previous parent, previous position among siblings and every node's own id, text, notes, links and fold state intact.
5. WHEN the user **updates** a node THEN the system SHALL apply the change to that node alone — its text (Requirement 7.6), its notes (Requirement 7.7) or its link (Requirement 12) — leaving its structural position untouched; its inverse SHALL restore the previous value.
6. WHEN the user commits an edited node text, **including an empty string** THEN the system SHALL persist it; an empty node text SHALL remain valid — a node is not required to have non-empty text.
7. WHEN the user adds or edits a node's **notes** THEN the system SHALL persist free-form, longer-form text separate from the node's primary (title) text.
8. WHEN the user cancels an in-progress text edit (Escape) THEN the system SHALL discard the pending edit, restore the node's previous text, and dispatch **no** command — a cancelled edit SHALL leave nothing on the undo stack.
9. IF the user attempts to remove the root, to move the root, or to move a node into its own subtree THEN the system SHALL reject the action with a message rather than leave the mindmap rootless or cyclic.
10. WHEN a structural or content change is applied THEN the system SHALL reflect it immediately on the canvas and push it to other connected clients, per `adp-diagram-ide` Requirement 3.2 and Requirement 11 here.

### Requirement 8 — Mainstream mindmap shortcuts and context actions

**User Story:** As someone who already uses a mindmap tool, I want the keys I know to do what they always do, so that I can use ADP's mindmaps without learning a new set of gestures.

#### Acceptance Criteria

1. WHEN a node is focused and the user presses a structural key THEN the system SHALL follow the conventions established by Freeplane/FreeMind and the wider mindmap tradition:
   * **Enter** — add a sibling after the focused node (Requirement 7.2);
   * **Tab** and **Insert** — add a child of the focused node (Requirement 7.1), Tab being the convention of XMind/MindManager and Insert that of FreeMind/Freeplane; both SHALL be offered rather than one chosen against half the audience;
   * **F2** — begin editing the focused node's text (Requirement 7.6);
   * **Delete** — remove the focused node and its subtree (Requirement 7.4);
   * **Escape** — cancel an in-progress edit (Requirement 7.8);
   * **Space** — toggle the focused node's fold (Requirement 9);
   * **Arrow keys** — move focus through the tree (Requirement 10.5);
   * **Ctrl+Up / Ctrl+Down** — move the focused node among its siblings (Requirement 7.3).
2. WHEN a shortcut fires while a node's text is being edited THEN text editing SHALL win for keys that mean something in text (Escape, arrows within the text), and the structural shortcut SHALL apply only outside an edit — a mindmap's shortcuts are modal, and SHALL be specified as such rather than left to the canvas and the inline editor to fight over.
3. WHEN the user opens the context menu on a node THEN the system SHALL offer that node's actions — add child, add sibling, rename, delete, fold/unfold, edit notes, link/unlink a code artifact — as **the same action implementations** the shortcuts trigger, so there is exactly one implementation behind both triggers.
4. WHEN these actions are offered THEN they SHALL be contributed through an `IContextActionProvider` for the diagram-element scope (Requirement 10.3) and reach the ribbon, the context menu and the keyboard through the one path `tech.md`'s Context section describes; the client SHALL NOT hold a key→action table of its own, and each action's shortcut SHALL be described by the backend **as data**, exactly as the explorer's actions are.
5. WHEN an action needs input — a new node's text, a rename, a link target — THEN it SHALL use the existing prompt channel on the context stream (`context-service`), not a mindmap-specific dialog mechanism.
6. WHEN the diagram is read-only, or when an action does not apply to the focused node (a sibling for the root, an unlink on a node with no link) THEN that action SHALL NOT be offered, so the menu and the shortcuts agree with what will actually succeed.
7. WHERE a shortcut in Requirement 8.1 is one the explorer already claims — **F2** is Rename, **Delete** is Delete and **Insert** is Add in the hierarchy scope today — the two SHALL coexist without either being renamed, and the system SHALL resolve which one fires from the **innermost level of the current selection**, never from a global key table. A shortcut is resolved against a target, so a node-innermost selection reaches the mindmap module's providers and an entry-innermost selection reaches the explorer's. This is the property that lets a diagram type reuse the keys its own users expect, and it SHALL be stated in the design rather than left as an accident of registration order.
8. IF two providers in the **same** scope offer the same shortcut for one target THEN that is a conflict the deployment cannot resolve, and the system SHALL report it rather than let registration order decide which action a key performs.

### Requirement 9 — Collapsing and expanding branches

**User Story:** As a diagram author, I want to collapse and expand branches, so that I can focus on part of a large mindmap without losing the rest of my work.

#### Acceptance Criteria

1. WHEN the user collapses a node with children THEN the system SHALL hide that node's descendants from the canvas while keeping the node itself visible, and SHALL indicate on the node that it has hidden children.
2. WHEN the user expands a previously collapsed node THEN the system SHALL show its direct children again, respecting their own collapsed/expanded state.
3. WHEN a mindmap is opened THEN the `.mm` file's `FOLDED` attributes SHALL **seed** the connection's fold state, so a map folded in Freeplane opens folded in ADP.
4. WHEN the fold state changes in ADP THEN it SHALL be held as **per-connection view state** and SHALL NOT modify the `.mm` file; on save, the fold attributes SHALL be round-tripped exactly as they were read, per Requirement 3.3's no-diff rule. *(This resolves an inconsistency in the previous revision of this spec, which required fold state to be persisted to the file and simultaneously to be permitted in read-only mode and excluded from undo. Treating it as view state makes all three hold at once, at the stated cost that a fold made in ADP does not outlive the connection.)*
5. WHEN a mindmap is open in read-only mode (`adp-diagram-ide` Requirement 5) THEN the system SHALL still allow collapsing and expanding, since it changes no document state.
6. WHEN a node's fold state changes THEN the system SHALL NOT dispatch a document command at all, and SHALL therefore record nothing on the project's history (`diagram-undo-redo` Requirement 3.1: only a command reporting an inverse is recorded) — it is view state, not an edit.
7. WHEN a node's fold state changes THEN the descendants that became hidden SHALL immediately stop being rendered and stop being delivered to that connection, and those that became visible SHALL be delivered, through the group/ungroup deltas of Requirement 11.4.
8. WHEN a collapsed node's subtree is the target of a change from another client THEN the collapse SHALL persist through it — the change SHALL be applied to the model without silently expanding the branch under the user.

### Requirement 10 — Discoverability and focus awareness through the context selection

**User Story:** As a diagram author, I want the node I'm on to be *the* selection everywhere in the IDE, so that the property grid, the ribbon and the context menu all follow what I'm looking at without me telling them separately.

#### Acceptance Criteria

1. WHEN the user focuses or selects a node on the canvas THEN the client SHALL report it through `ContextService.Select` as a **nested selection**: the outermost level is the `.adp` file (its entry id and project-relative path) and its `child` is the node (source `DIAGRAM_CANVAS`, the node's element id, and its **parent-relative** path — the node's chain of names from the root). Per `context-service` Requirement 2.9's containment rule, concatenating the chain yields one real location — the `.adp` file's path followed by the node's chain, e.g. `["docs", "design.adp", "root", "Architecture", "Backend"]`. That concatenation is **not** what `DiagramService.Connect` is opened with: Connect takes the outermost level alone, the `.adp` file's own path (Requirement 11.1). The canvas SHALL NOT keep a selection of its own alongside this.
2. WHEN a node id is expressed THEN it SHALL be a new `element_id` member of `context.proto`'s `ContextSource` — the extension point `context-service` Requirement 2.4 already anticipated for exactly this — served by an `IContextSourceResolver` the mindmap module registers. Registering that resolver SHALL be the whole of making a node selectable; the context service itself SHALL NOT change.
3. WHEN the backend derives the routing scope for a node selection THEN it SHALL derive it from the `element_id` member (a new `ContextScope` value for diagram elements), never from anything the client states, and route to the `IContextActionProvider`s of Requirement 8.4.
4. WHEN a node selection is recorded THEN the resolver SHALL verify the child against its parent — the node SHALL actually belong to the diagram named by the outer level, on this connection — and reject the whole chain otherwise, leaving the previous selection untouched (`context-service` Requirement 2.6).
5. WHEN the selection changes — by click, by arrow-key navigation (Requirement 8.1), or by a `Select` originating anywhere else — THEN the canvas SHALL move focus to the selected node, scrolling it into view if it is off-screen and expanding its collapsed ancestors so it is actually visible; the canvas SHALL react to the pushed selection, not to its own event handlers, and SHALL NOT re-react to a selection whose source is itself.
6. WHEN a node selection is pushed on the `Watch` stream THEN it SHALL carry the node's backend-resolved detail as a new `ContextLevelDetail` member (its text, whether it has children, whether it is folded, whether it carries a link), so the property grid can show *what* is selected without a lookup of its own, and the innermost level's available actions SHALL be pushed alongside it (`context-service` Requirement 5.1) so the context menu and shortcuts are ready the moment the node is focused.
7. WHEN the selected node is removed, or its diagram is closed, renamed or deleted THEN the backend SHALL update or clear the selection and notify watchers without the client calling `Select` again (`context-service` Requirement 3.3).
8. WHEN keyboard navigation moves focus rapidly THEN the client SHOULD coalesce the resulting plain (`none`) selects, while a `CONTEXT_MENU` or `ACTIVATE` select SHALL bypass the debounce, per `context-service` Requirement 3.4.
9. Multi-selection of nodes is **out of scope**, matching `context-service` Requirement 2.8's single-id-per-level rule; Requirement 11.4's group/ungroup deltas therefore describe folding, not a user selecting several nodes.

### Requirement 11 — The core delta and viewport contract

**User Story:** As a developer of EtAlii.Adp, I want the mindmap to be expressed entirely in the core contract's existing add/remove/group/ungroup/viewport vocabulary, so that this module proves the contract is sufficient rather than quietly extending it.

#### Acceptance Criteria

1. WHEN a client opens a mindmap THEN it SHALL do so over `DiagramService.Connect`, whose first message is the `Path` of the `.adp` file (`grpc-core-communication-specification` Requirement 1); the client SHALL NOT name the `.mm` sibling, which is the backend's business (Requirement 2.4).
2. WHEN a node is sent to a client THEN it SHALL be one core `Element`: its `id` the node's `.mm` `ID` (Requirement 3.4), its `position` the layout's result (Requirement 5.3), its `type` the mime-style string for its element kind (e.g. `freeplane/mindmap+node`, `freeplane/mindmap+edge`), and its `payload` a mindmap-owned message — text, notes, fold indicator, link indicator — defined in the module's own `.proto` under `diagrams/mindmap/api/` and packed into the core `Any`, **without modifying the core contract files** (that spec's Requirement 4).
3. WHEN nodes appear or disappear for a connection THEN the system SHALL express it with the existing `Add` and `Remove` delta actions; an edit to a node's text, notes, link or position SHALL be expressed as an `Add` carrying the element in its new state, replacing the element with that id. The contract's `add`/`remove`/`group`/`ungroup` set SHALL NOT gain an `update` member for this module's sake — but neither SHALL `add` be quietly read as an upsert while `grpc-core-communication-specification` Requirement 3.2 still says "elements to add". That spec SHALL be amended to define `add` as an upsert keyed on element id, as the fourth core change this spec's Introduction declares, **before** this requirement is implemented. Expressing an edit as `Remove` followed by `Add` is explicitly rejected as the alternative: it makes an element momentarily absent, which a client cannot distinguish from a real deletion and which would drop focus and selection on the node being edited.
4. WHEN a branch is folded THEN the system SHALL express it as a `Group` delta — the subtree's element ids as `source_element_ids`, the folded parent as the `group_element` — and WHEN it is unfolded, as an `Ungroup` delta carrying the branch's elements again. This gives folding a natural home in the existing contract and keeps a folded branch cheap to hold on the client. `grpc-core-communication-specification` Requirement 3.4 describes the `group_element` as "the **new** grouping element"; folding reuses an element that already exists — the parent node — so that spec SHALL be read as, or amended to say, that the group element MAY be an element the client already holds. If it is instead settled that a group element must always be new, this requirement changes rather than the contract, and folding is expressed some other way.
5. WHEN the client's viewport changes (pan/zoom) THEN it SHALL report it with the existing `ViewUpdate` message — the centre and the bounding box (`grpc-core-communication-specification` Requirement 2) — and the backend SHALL use it, against the layout it computed (Requirement 5.3), to decide which nodes to deliver to that connection, so a large map does not stream in full. A `ViewUpdate` SHALL never be recorded on the history (`diagram-undo-redo` Requirement 3.4) and SHALL never modify a file.
6. WHEN a connection reconnects THEN the backend SHALL re-baseline it against the current document state per `grpc-core-communication` Requirement 6.2; the module SHALL NOT keep its own reconnect or replay mechanism.
7. WHEN concurrent edits from several clients reach the same mindmap THEN they SHALL be reconciled by the core delta/reconciliation behaviour, with the module contributing no conflict-resolution logic of its own.
8. WHEN a mindmap is changed on disk outside ADP THEN the system SHALL reload it and push the difference as deltas (`adp-diagram-ide` Requirement 2.3), and SHALL NOT make that reload undoable, since it arrives through the watcher and never becomes a command (`diagram-undo-redo` Requirement 3.3).

### Requirement 12 — Linking a node to a code artifact

**User Story:** As an architect, I want to point a node at the file or folder it describes, so that my mindmap stays connected to the code as the code changes, instead of becoming a picture that quietly goes out of date.

#### Acceptance Criteria

1. WHEN a node is linked THEN the link SHALL name **one** artifact in the current project — a file or a folder — and SHALL be **stored in the node's own Freeplane `LINK` attribute**, in the form Freeplane itself uses: a path relative to the `.mm` file. No ADP-specific storage is introduced for it. This is what makes a link ADP created clickable in Freeplane, and a link a user made in Freeplane meaningful in ADP, which is the whole of product.md's *Don't reinvent, integrate* applied to this feature.
2. WHEN a link is **sent to a client** THEN it SHALL travel as a **project-relative** `Path` together with the hierarchy **entry id** the receiving connection knows the artifact by, so the client can select it through the context service (Requirement 12.6). An absolute filesystem path SHALL never be stored, sent to a client, or accepted from one, per this spec's Security section and `tech.md`'s scoping rule.
3. WHERE Requirement 12.1 stores a map-relative path and Requirement 12.2 sends a project-relative one plus an id, the conversion SHALL happen in the module and the three forms SHALL NOT be collapsed into one. Each is the only correct form where it is used: Freeplane defines `LINK` as map-relative and would misread anything else; the client is never given a location outside the project; and an entry id is stable only *within the connection that observed it* (`project-root-folder-explorer` Requirement 2.5), so it cannot be written to a file that outlives the connection.
4. WHERE this spec supports only Freeplane's own link — one artifact, no further attributes — richer link kinds are deliberately **out of scope** and are expected later. WHEN they arrive THEN they SHALL be stored in the diagram's `.adp` file per Requirement 3.5, never in a third file and never by extending the `.mm` schema, so a mindmap stays two files however much ADP later knows about a link.
5. WHEN a node carries a link THEN the canvas SHALL indicate it on the node (Requirement 4.4), and the node's pushed detail SHALL say that it is linked (Requirement 10.6), so a reader sees which parts of the map are anchored to code without opening anything.
6. WHEN the user links or unlinks a node THEN it SHALL be an update to that node alone (Requirement 7.5), dispatched as a command whose inverse restores the previous link — including restoring "no link" — per Requirement 6.
7. WHEN the user chooses a link target THEN the action SHALL use the existing prompt channel (Requirement 8.5) and SHALL let the target be picked from the project rather than typed as a path, so an unresolvable link cannot be created by mistyping.
8. WHEN the user activates a link THEN the system SHALL reveal the target in the explorer and select it, by reporting that selection through `ContextService.Select` like any other selection (Requirement 10) — following a link SHALL NOT be a private canvas behaviour that other surfaces cannot see.
9. WHEN the linked artifact is **renamed or moved through ADP** THEN the link SHALL be updated to the artifact's new path, so it keeps resolving. A rename is a command (`tech.md`'s Commands section), which is what makes this possible: the same change that moves the file updates the links naming it, and the rename's inverse restores them. This is what "keeps nodes tied to the code they describe **as that code evolves**" means in practice.
10. IF the linked artifact is renamed or moved **outside ADP**, or while the backend is not running THEN the link SHALL be reported as broken by Requirement 12.9 rather than silently repaired by guesswork. Following a file across an unobserved rename would mean matching on content or name similarity, and a link that quietly re-points at the wrong artifact is worse than one that visibly needs fixing.
11. WHEN a link is resolved THEN the system SHALL verify the target still lies within the currently authorized project, and SHALL refuse to resolve one that does not — a link SHALL never become a way to reach outside the workspace, including after the file it names has been replaced by a symbolic link (`HierarchyModel`'s containment re-check on resolution).
12. IF the linked artifact has been **deleted**, or no longer resolves for any other reason THEN the node SHALL show the link as **broken**, keeping what it pointed at for the user to see, and SHALL NOT silently drop it — a link that disappears takes the knowledge of what was intended with it. Unlinking SHALL stay an explicit action.
13. WHEN a node holding a link is deleted, moved or restored by an undo THEN its link SHALL travel with it without any separate bookkeeping, because the link is an attribute of the node rather than a record pointing at one. A link can therefore never be orphaned, and no rule is needed for reconciling one against the tree.
14. WHEN the `.mm` file itself is renamed or moved — including as part of the pair move of Requirement 2.10 — THEN every `LINK` in it SHALL be rewritten so it still resolves to the same artifact, since a map-relative path means something different from a new location. This is the one bookkeeping cost of storing links the Freeplane way, and it is paid in the command that moves the file.

### Requirement 13 — Pluggable registration as a diagram type

**User Story:** As a developer of EtAlii.Adp, I want the mindmap module to register itself without any core code depending on it, so that the pluggable diagram-type model is proven out, not just asserted.

#### Acceptance Criteria

1. WHEN the backend starts THEN the mindmap module SHALL be discovered through the existing reflection-based `Diagram.Definition` scan (`add-diagram-action` Requirement 1) — as it already is — with the extension of Requirement 2.2 as the only addition to what it publishes.
2. WHEN the backend encounters an `.adp` file naming `freeplane/mindmap`, or a `.mm` file without an `.adp` sibling (Requirement 2.7), THEN it SHALL route it to the mindmap module through that same registration, via a mechanism any future diagram module uses identically.
3. WHEN the module organises its code THEN it SHALL place it under `diagrams/mindmap/` with `backend/` (`EtAlii.Adp.Diagram.Mindmap` and `EtAlii.Adp.Diagram.Mindmap.Tests`), `api/` (its own `.proto` files) and `client/` subfolders, per `structure.md`.
4. IF core canvas, storage, sync, context or command code is inspected THEN it SHALL contain no mindmap-specific type checks, imports or branching logic — the dependency SHALL only run from the mindmap module toward core abstractions, per `structure.md`'s dependency-direction rule. The three core changes this spec makes (Requirement 2.2's extension, Requirement 10.2's `element_id`, Requirement 10.3's scope value) SHALL each be type-agnostic and usable unchanged by the next diagram type.
5. WHEN the module registers its resolver, its action providers and its command handlers THEN each SHALL be one registration line through the existing extension points, per `tech.md`'s Context and Commands sections; a change inside the context service, the command dispatcher or the canvas host to accommodate mindmaps SHALL be treated as a sign the abstraction is wrong, and the abstraction fixed instead.
6. WHEN this spec reaches implementation, and again when it is complete THEN `docs/diagrams.md`'s `freeplane/mindmap` row SHALL be moved to the matching state — 🛠️ work-in-progress, then ✅ implemented — per CLAUDE.md's diagram type catalog rule. The catalog is how "which diagram types does ADP actually have" is answered, and a row left at 📝 specified while the module works answers it wrongly.

## Non-Functional Requirements

### Code Architecture and Modularity

* **Single Responsibility Principle**: `.mm` parsing/serialization, the tree model and its commands, the layout, the element/delta mapping, the context resolver, and the canvas rendering are separate, independently testable concerns within the module.
* **Modular Design**: `EtAlii.Adp.Diagram.Mindmap`, `diagrams/mindmap/api` and `diagrams/mindmap/client` depend only on core abstractions; nothing in core depends on the mindmap module, and core stays compilable and testable with zero diagram modules present.
* **Dependency Management**: the `.mm` parser/serializer and the layout SHALL be isolated from the gRPC-facing element-mapping code, so both can be tested without a running backend or client.
* **Clear Interfaces**: the module's integration surface with core is exactly four seams — the discovered `Diagram.Definition`, the registered `IContextSourceResolver`, the registered `IContextActionProvider`(s), and the registered `ICommandHandler`s. No other back-channel coupling.
* **Testability**: opening, editing and folding a mindmap SHALL each be exercisable against a temporary folder without a browser; the layout SHALL be assertable on positions alone.

### Performance

* Layout recomputation after a structural edit SHALL remain fast enough to feel immediate for mindmaps of hundreds of nodes, consistent with `adp-diagram-ide`'s canvas responsiveness target, and SHALL recompute only what the change can affect rather than the whole map where that is avoidable.
* Folding a branch SHALL immediately stop rendering **and** stop delivering its hidden descendants (Requirement 9.7), contributing to the virtualization goals of `adp-diagram-ide` Requirement 4.3.
* Delivery SHALL be bounded by the client's reported viewport (Requirement 11.5), so opening a very large map does not stream every node before the first paint.

### Security

* Code-artifact links SHALL only resolve to artifacts within the currently authorized project (Requirement 12.7), consistent with `tech.md`'s filesystem-access scoping; an absolute filesystem path SHALL never be sent to, or accepted from, a client at any point in this module — a link travels as an entry id and a project-relative path (Requirement 12.1).
* A node text, a note or a link read from a `.mm` file authored elsewhere is untrusted input: it SHALL be rendered as text and SHALL never be interpreted as markup, script, or a path the backend acts on without the containment check above.

### Reliability

* A malformed `.mm` file SHALL degrade to a clear error on that one diagram, never to a failed workspace (Requirement 3.7).
* A save SHALL be atomic and SHALL never leave a `.mm` file observable in a partial state (Requirement 3.6); creation SHALL leave both files or neither (Requirement 1.6).
* Concurrent structural edits to the same mindmap from multiple clients SHALL be reconciled using the core delta/reconciliation behaviour from `grpc-core-communication`, without the mindmap module needing its own conflict-resolution logic (Requirement 11.7).
* Every bug found while implementing or verifying this spec SHALL be covered by a unit or integration test, or recorded in `tests.md`, per CLAUDE.md.

### Usability

* The keyboard alone SHALL be enough to build a map: create, name, navigate, move, fold and delete nodes without reaching for the mouse (Requirement 8.1).
* A user arriving from Freeplane, FreeMind, XMind or MindManager SHOULD find their structural keys working as they expect, and SHOULD NOT have to discover an ADP-specific gesture to do something as basic as adding a child.
* The map SHALL always show where focus is, and the focused node SHALL always be brought into view when the selection changes from elsewhere (Requirement 10.5).