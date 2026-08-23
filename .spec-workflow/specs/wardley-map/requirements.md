# Requirements Document

## Introduction

This spec covers the **Wardley Map diagram module** — the second concrete diagram type, and the first that does *not* fit the shape the first one established. It defines how a Wardley map is created, stored as an OnlineWardleyMaps `.owm` document beside its `.adp` registration file, parsed and written back without disturbing what a human wrote, rendered on the shared canvas against its two semantic axes, and edited through commands and the context service.

`src/diagrams/wardley-map/` is already scaffolded and its `Diagram.Definition` already publishes `new DiagramOrigin("wardley", "map")`, so the type is already discovered and already offered in the Add dialog. What is missing is everything behind that entry.

**Why this type, second.** [`mindmap-diagram`](../mindmap-diagram/requirements.md) proved the pluggable model with a type whose positions are *computed* by the module and never written to the file. A Wardley map is the mirror image: its coordinates are *authored*, they carry the meaning, and moving a component is a strategic claim rather than a cosmetic one. If core can serve both without knowing which is which, the pluggable diagram-type model is proven rather than asserted. Three places where mindmap's answer must not be mistaken for a core rule are called out explicitly: layout (Requirement 7), viewport filtering (Requirement 10.5) and canvas chrome (Requirement 8).

**Dependencies.** This spec redefines none of them:

- [`adp-diagram-ide`](../adp-diagram-ide/requirements.md) — the workspace shell, the pannable/zoomable canvas, read-only mode.
- [`grpc-core-communication-specification`](../grpc-core-communication-specification/requirements.md) — `Element`, `Delta` (`add`/`remove`/`group`/`ungroup`), `Point2D`, and the `Any` payload extension point.
- [`add-diagram-action`](../add-diagram-action/requirements.md) and [`create-diagram-file`](../create-diagram-file/requirements.md) — the Add action, the `.adp` file and its MIME first line, the name field.
- [`mindmap-diagram`](../mindmap-diagram/requirements.md) — which introduced, and this spec reuses unchanged: `DiagramDefinition.Extension`, `IDiagramDocumentFactory`, `ContextSource.element_id`, `ContextScope.DIAGRAM_ELEMENT`, and `DiagramService`'s `Open` + `UpdateView` legs.
- [`context-service`](../context-service/requirements.md) — the selection chain and the pushing of a selection's actions.
- [`diagram-undo-redo`](../diagram-undo-redo/requirements.md) and `tech.md`'s **Commands** rule.

**What this spec changes in core.** Ideally nothing. The one candidate is Requirement 8.4's canvas background layer, which is a general seam and must be usable by any diagram type. If anything else in core needs to change, that is a finding worth reporting rather than a licence to change it.

**Deliberately out of scope.** Editing the `.owm` as text with a live preview — the way onlinewardleymaps.com, the VS Code extension and the Obsidian plugin all work. It is a reasonable thing for ADP to want, it is a substantial feature in its own right, and nothing here precludes it: Requirement 3's round-trip is what a text editor would need anyway. Stated so its absence is a decision rather than an oversight.

## Alignment with Product Vision

- [product.md](../../steering/product.md)'s **"Don't reinvent, integrate"** — `.owm` is the de-facto maps-as-code format, shared by onlinewardleymaps.com, the VS Code extension, the Obsidian plugin and `cli-owm`. ADP reads and writes what that ecosystem already reads and writes, rather than a format of its own.
- product.md's **"Files are the source of truth"** — a map is two plain text files in the project folder, and a map ADP did not change is byte-identical after a save.
- product.md's **"Linkage over illustration"** — `submap` and `url` tie a map to other maps and to external material; Requirement 6.6 keeps those live rather than decorative.
- [tech.md](../../steering/tech.md)'s **Commands** — every edit, including a drag, is a command with an inverse.
- tech.md's **Context** — a component becomes selectable by registering a resolver, exactly as a mindmap node did.
- tech.md's **Implementation order** — the requirement order below follows model, persistence, wire, UI, logic, so the tasks can too.
- [structure.md](../../steering/structure.md)'s **dependency direction** — the module depends on core; core never depends on the module.

## Requirements

### Requirement 1 — A Wardley map is created through the Add action

**User Story:** As an architect, I want to create a Wardley map from the Add dialog like any other diagram, so that starting one costs nothing new to learn.

#### Acceptance Criteria

1. WHEN the user opens the Add dialog THEN **Wardley Map** SHALL appear under the `wardley` vendor group by the `Title` its existing `Diagram.Definition` already carries, through discovery alone.
2. WHEN the user selects it and has not edited the name THEN the suggested file name SHALL be `map`, made collision-free per `create-diagram-file` Requirement 2.2 (`map-2`, `map-3`, …) — derived from the origin's `Type` segment, with no per-type special case.
3. WHEN the user confirms THEN the system SHALL create both `<name>.adp`, whose first line is `wardley/map`, and `<name>.owm`, through the shared create-file command and the module's `IDiagramDocumentFactory`, with no change to the Add flow.
4. WHEN the empty `.owm` body is written THEN it SHALL be a valid, minimal document — a `title` line carrying the file's base name — which opens as an empty map showing its axes and nothing else. A map with no components SHALL be a valid map, not an error.
5. IF writing either file fails THEN neither SHALL be left behind, per `create-diagram-file` Requirement 5.

### Requirement 2 — Registration in `.adp`, body in the `.owm` sibling

**User Story:** As an architect, I want my map to live in a real `.owm` file, so that I can open it in onlinewardleymaps.com, VS Code or Obsidian without converting anything.

#### Acceptance Criteria

1. WHEN a Wardley map exists on disk THEN it SHALL be `<name>.adp` (first line `wardley/map`) plus `<name>.owm` holding the OnlineWardleyMaps DSL, sharing one base name in one folder.
2. WHEN the module declares itself THEN it SHALL set `DiagramDefinition.Extension` to `".owm"`, reusing the member `mindmap-diagram` Requirement 2.2 added; no new core mechanism SHALL be introduced for this.
3. WHEN an `.adp` file naming `wardley/map` is opened THEN the system SHALL read its `.owm` sibling, and WHEN a map is saved THEN it SHALL write that same sibling; the `.adp` file SHALL NOT be rewritten by any edit.
4. IF the `.owm` sibling is missing THEN the map SHALL open as an empty map (Requirement 1.4) and the sibling SHALL be created on the first save, rather than failing to open.
5. WHEN an `.owm` file is present without an `.adp` sibling — a map authored elsewhere and dropped into the project — THEN it SHALL still be openable as a Wardley map through extension routing, and the `.adp` file SHALL NOT be created implicitly.
6. WHEN `.wm` files are encountered THEN the system SHALL treat them as the same format, since the ecosystem's own tooling accepts both extensions for this DSL. The **created** extension SHALL always be `.owm`.

### Requirement 3 — Reading and writing `.owm` without disturbing it

**User Story:** As a map author, I want ADP to leave alone every part of my file it did not change, so that opening a map in ADP never shows up as a diff I have to review.

#### Acceptance Criteria

1. WHEN a map ADP has not changed is saved THEN the resulting file SHALL be **byte-identical** to what was read — statement order, blank lines, comments, indentation and line endings included.
2. WHEN ADP changes one thing THEN it SHALL rewrite **only the statements that thing occupies**, leaving every other line exactly as the author wrote it. A model-to-text regeneration of the whole file SHALL NOT be used, because it would reformat a hand-authored document on the first save.
3. WHEN a statement is encountered that this module does not recognise — a keyword from a newer DSL version, or one this spec does not cover — THEN it SHALL be preserved verbatim through a round trip and SHALL NOT be dropped, reordered or reported as an error. Forward compatibility is a property of the writer, not a promise to keep chasing the DSL.
4. WHEN a file cannot be parsed at all THEN the system SHALL surface a clear error naming the file and the line, rather than partially loading a map or silently discarding statements.
5. WHEN a line is syntactically valid but semantically broken — a link naming a component that does not exist, a coordinate outside `0..1` — THEN the map SHALL still open, the problem SHALL be reported against that line, and the rest of the map SHALL render. A map is a thinking tool; refusing to show it because one link is dangling is worse than showing it with the link marked.
6. WHEN the module writes a file THEN it SHALL do so through the backend's existing file-access layer and atomically (temporary file in the same folder, then move into place), to the standard `create-diagram-file` Requirement 3.3 sets.

### Requirement 4 — Every element has a stable identity, and the file does not provide one

**User Story:** As an architect, I want my selection, my undo history and my links to survive renaming a component, so that renaming is an edit rather than a small catastrophe.

#### Acceptance Criteria

1. WHEN the module needs to identify an element THEN it SHALL use an ADP-assigned `ShortGuid`, not the component's name. The `.owm` format has **no identifier of any kind**: components are named, and `evolve`, links and pipelines all reference them by name. Name-as-identity would make every rename a delete-plus-add, breaking the current selection, every undo entry that mentions the element, and the pushed element ids on the delta stream.
2. WHEN identities are stored THEN they SHALL live in an adjacent metadata file, per `tech.md`'s "additional files will be used to store meta-data" and the same mechanism `mindmap-diagram` Requirement 3.5 uses — so the `.owm` file stays a standard document that ADP has not annotated.
3. WHEN a map is opened whose elements have no recorded identity — authored elsewhere, or the metadata file is missing — THEN the system SHALL assign identities on load and persist them on the first save, and the map SHALL be fully usable in the meantime.
4. WHEN a component is renamed THEN its identity SHALL be unchanged, and every statement referring to it by name — links, `evolve`, pipeline membership — SHALL be rewritten in the same command, so the file stays internally consistent.
5. IF the metadata file is lost or is inconsistent with the `.owm` (an id naming a component that no longer exists) THEN the system SHALL re-derive what it can by name, discard what it cannot, and continue. The metadata file SHALL never be the reason a map fails to open — the `.owm` is the document, the sidecar is a convenience.
6. WHEN two components share a name — which the DSL permits and which makes references ambiguous — THEN the system SHALL report it against those lines and SHALL keep both elements distinguishable by identity.

### Requirement 5 — The map model: components, links and pipelines

**User Story:** As an architect, I want the things I drew to come back as the things I drew, so that a round trip through ADP is lossless.

#### Acceptance Criteria

1. WHEN a `component`, `anchor`, `market`, `ecosystem` or `submap` statement is read THEN the system SHALL produce a positioned element carrying its name, its kind and its coordinates.
2. WHEN a coordinate pair `[a, b]` is read THEN the system SHALL interpret **`a` as visibility (the value-chain axis) and `b` as maturity (the evolution axis)**, both in `0..1`. This is the reverse of `Point2D(x, y)`, and the conversion SHALL live in exactly one place so no other code has to remember it.
3. WHEN a link `A->B`, a flow link `A+>B`, or a link with context `A->B; text` is read THEN the system SHALL produce an element naming both endpoints by identity, carrying the link's kind and its context text.
4. WHEN a `pipeline` is read THEN the system SHALL produce a container holding its child components, where a child carries only an evolution position and takes its visibility from the parent — the current nested form `pipeline Parent{ … }`. The **legacy** two-coordinate form SHALL be readable, and SHALL be written back in whichever form it was read (Requirement 3.2).
5. WHEN an element carries a `label [dx, dy]` offset THEN the system SHALL preserve it through a round trip and SHALL honour it when rendering. The offset is in pixels rather than map coordinates — a property of the format, which ADP reproduces rather than corrects.
6. WHEN map-level statements are read — `title`, `size [w, h]`, `style` — THEN they SHALL be preserved and applied where they affect rendering. They are properties of the map, not elements on it.

### Requirement 6 — The strategy vocabulary

**User Story:** As an architect, I want the annotations that make a map a strategy tool rather than a picture, so that what I record about movement and intent survives.

#### Acceptance Criteria

1. WHEN an `evolve` statement is read THEN the component SHALL be shown at **both** its current and its target maturity, joined by a movement indicator, since the pair is the point of the statement. `evolve Name->NewName x` SHALL additionally carry the name the component takes when it arrives.
2. WHEN `inertia` is present on a component THEN it SHALL be preserved and indicated on the map.
3. WHEN a `(build)`, `(buy)` or `(outsource)` decorator is present THEN it SHALL be preserved and indicated.
4. WHEN `pioneers`, `settlers` or `townplanners` areas are declared THEN they SHALL be preserved and rendered as areas behind the elements they cover.
5. WHEN an `accelerator` or `deaccelerator` is declared THEN it SHALL be preserved and rendered at its position.
6. WHEN a `submap` or a `url` is read THEN it SHALL be preserved, and activating a `submap` SHALL open the map it names through the same navigation path any other diagram is opened by — not through a Wardley-specific mechanism.
7. WHEN a `note` is read THEN it SHALL be preserved and rendered as free text at its position.
8. WHEN an `annotation` is read THEN it SHALL be preserved with **all** of its coordinates: the DSL permits one numbered annotation pinned at several places, which a one-element-one-position model does not express directly. How that is represented is a design decision; that none of the positions may be lost is not.

### Requirement 7 — Coordinates are authored, and moving something is an edit

**User Story:** As an architect, I want dragging a component to change my map, so that I can think by moving things rather than by editing numbers.

#### Acceptance Criteria

1. WHEN the module positions an element THEN the position SHALL come **from the document**. This module SHALL NOT compute a layout, and SHALL NOT contain one.
2. WHEN the user drags an element on the canvas THEN the system SHALL treat it as a **document edit**: a command whose inverse restores the previous coordinates, written back to the `.owm`, pushed to every other client, and recorded on the project's history. Position is meaning here — moving a component right asserts that it is more evolved — so a drag is never a view-only change.
3. WHEN an element is dragged THEN its coordinates SHALL be clamped to `0..1` on both axes; a component cannot be more evolved than commodity.
4. WHEN a component inside a pipeline is dragged THEN only its evolution position SHALL change, since its visibility is the parent's (Requirement 5.4).
5. WHEN the map is open in read-only mode THEN dragging SHALL be rejected like any other document edit, and SHALL not be offered.
6. WHEN this requirement is read against `mindmap-diagram` Requirement 5 THEN the contradiction is intended and is the point: one module computes positions and never writes them, the other reads them from the file and writes them back. Core SHALL be able to serve both without knowing which it is serving. Anything in core that has to ask is a defect in core.

### Requirement 8 — Rendering the map

**User Story:** As an architect, I want to see the axes, the evolution bands and my components in the places I put them, so that the picture means what a Wardley map means.

#### Acceptance Criteria

1. WHEN a map is opened THEN the system SHALL render a bounded `0..1` × `0..1` coordinate space on the shared canvas, with the value-chain axis vertical (the user need at the top, invisible components at the bottom) and the evolution axis horizontal (genesis at the left, commodity at the right).
2. WHEN the evolution axis is drawn THEN it SHALL show the four stages with boundaries at **0.175**, **0.400** and **0.700**, labelled **Genesis**, **Custom Built**, **Product `(+rental)`** and **Commodity `(+utility)`**. These constants are not published in the DSL documentation; they are derived from the reference renderer's own `EvoOffsets` (`custom: 3.5`, `product: 8`, `commodity: 14`, each divided by 20) and SHALL be pinned in the module with a test and a comment citing that source, so nobody re-derives them from a search result.
3. WHEN an element is rendered THEN its kind SHALL be visually distinguishable — an anchor from a component from a market from a submap — and the annotations of Requirement 6 SHALL be visible on it without entering an edit mode.
4. WHEN the axes, bands and their labels are drawn THEN they SHALL be contributed **by the module**, not by core, per `structure.md`'s dependency rule — core's canvas must not learn what an evolution axis is. This is the first diagram type needing a background layer beneath the elements and fixed to the coordinate space rather than to the viewport; the seam that allows it SHALL be general enough for the next type to use unchanged.
5. WHEN the canvas is panned or zoomed THEN the axis chrome SHALL scale with the map rather than float over it, since a component's position is only meaningful against its own axes.
6. WHEN a semantic problem was reported on load (Requirement 3.5) THEN the elements involved SHALL be marked on the canvas, so a dangling link is visible where it is rather than only in a list.

### Requirement 9 — Every change is a command

**User Story:** As a developer of EtAlii.Adp, I want every map edit to be an `ICommand` with an inverse, so that undo, validation and multi-client delivery come from the system rather than from this module.

#### Acceptance Criteria

1. WHEN anything changes a map's document state THEN it SHALL be an `ICommand` with a matching `ICommandHandler<TCommand>` dispatched through `IHistoryStack`, per `tech.md`'s Commands rule; nothing SHALL edit the `.owm` inline.
2. WHEN a handler succeeds THEN it SHALL report the command that reverses it as `CommandResult.Inverse`, so the change becomes undoable on the project's history without this module implementing undo.
3. WHEN the module defines its commands THEN they SHALL cover at least: add an element, remove an element, move an element, rename an element, set or clear a link, set or clear `evolve`, set or clear `inertia` and the build/buy/outsource decorators, and add or remove a component from a pipeline.
4. WHEN a command is rejected — the element is gone, the name is already taken, the map is read-only — THEN the handler SHALL return a `CommandResult` carrying a message for the user, not throw.
5. WHEN a handler runs THEN it SHALL validate its own preconditions against current state, since undo and redo dispatch it again later.
6. WHEN a rename is undone THEN every statement rewritten under Requirement 4.4 SHALL be restored with it, so an undo leaves the file exactly as it was rather than partially renamed.
7. WHEN handlers are placed THEN they SHALL live in a `Commands` subfolder of the module and be registered through the module's own `AddCommands`-style extension, as the mindmap module's are.

### Requirement 10 — The core delta and viewport contract

**User Story:** As a developer of EtAlii.Adp, I want a Wardley map expressed entirely in the existing element and delta vocabulary, so that this module tests the contract rather than extending it.

#### Acceptance Criteria

1. WHEN a client opens a map THEN it SHALL do so over `DiagramService.Open` with the `.adp` file's project-relative path, and report its viewport through the unary `UpdateView` — the two correlated legs `tech.md`'s *gRPC call shapes* requires. The client SHALL NOT name the `.owm` sibling.
2. WHEN an element is sent THEN it SHALL be one core `Element`: `id` the identity of Requirement 4, `position` converted from the document's `[visibility, maturity]`, `type` a mime-style string under `wardley/map` naming the element kind, and `payload` a module-owned message defined in `diagrams/wardley-map/api/` and packed into the core `Any`, without modifying the core contract files.
3. WHEN elements appear, change or disappear THEN the system SHALL express it with the existing `Add` and `Remove` actions, an edit being an `Add` carrying the element in its new state. No new `Delta` action SHALL be added for this module.
4. WHEN a pipeline is sent THEN it SHALL use the existing `Group` and `Ungroup` actions, the parent as the group element and its children as the sources — the second independent use of those two actions after mindmap folding.
5. WHEN a viewport is reported THEN the module MAY answer with the entire map. A Wardley map is a bounded space holding tens of elements, so viewport filtering has nothing to do here; `adp-diagram-ide` Requirement 4.3's virtualization is a capability core offers, not an obligation every module owes. No filter SHALL be implemented merely to satisfy a rule.
6. WHEN a connection reconnects, or several clients edit the same map THEN re-baselining and reconciliation SHALL be the core behaviour, with no module-specific mechanism.
7. WHEN the `.owm` or its sidecar changes on disk outside ADP THEN the system SHALL reload and push the difference as deltas, and that reload SHALL NOT be undoable.

### Requirement 11 — Selection, focus and actions

**User Story:** As an architect, I want the component I click to become the selection everywhere in the IDE, so that the property grid and the ribbon follow what I am looking at.

#### Acceptance Criteria

1. WHEN the user selects an element on the canvas THEN the client SHALL report it through `ContextService.Select` as a nested selection: the `.adp` file as the outer level, the element as its `child` with source `DIAGRAM_CANVAS` and its `element_id`. Both the `ContextSource.element_id` member and the `DIAGRAM_ELEMENT` scope already exist; this module SHALL reuse them and SHALL NOT add a variant.
2. WHEN an element is made selectable THEN it SHALL be by registering an `IContextSourceResolver` for this module, with no change inside the context service.
3. WHEN an element selection is pushed THEN it SHALL carry backend-resolved detail — the element's name, its kind, its coordinates and its evolution stage — so the property grid can show what is selected without a lookup of its own.
4. WHEN an element is selected THEN its actions SHALL be pushed alongside it, contributed through an `IContextActionProvider` for the diagram-element scope, and SHALL reach the ribbon, the right-click menu and the keyboard through the one path every other action uses.
5. WHEN actions are offered THEN they SHALL cover the edits of Requirement 9.3, and each SHALL be described by the backend as data, including any shortcut. The client SHALL hold no key-to-action table.
6. WHEN an action does not apply — an unlink on an element with no link, any edit in read-only mode — THEN it SHALL NOT be offered, so the menu agrees with what would succeed.
7. WHEN the selection changes from elsewhere THEN the canvas SHALL bring the selected element into view and SHALL NOT re-react to a selection it produced itself.
8. Multi-selection is out of scope, per `context-service` Requirement 2.8.

### Requirement 12 — Pluggable registration as a diagram type

**User Story:** As a developer of EtAlii.Adp, I want this module to plug in exactly where the mindmap did, so that the second diagram type demonstrates a repeatable path rather than a second special case.

#### Acceptance Criteria

1. WHEN the backend starts THEN the module SHALL be discovered through the existing `Diagram.Definition` reflection scan, as it already is, with `Extension` the only addition to what it publishes.
2. WHEN the module organises its code THEN it SHALL be `diagrams/wardley-map/` with `backend/` (`EtAlii.Adp.Diagram.WardleyMap` and its `.Tests`), `api/` and `client/`, per `structure.md` — the layout the mindmap module already follows.
3. WHEN the module registers its document factory, its source resolver, its action providers and its command handlers THEN each SHALL be one registration through an existing extension point.
4. IF core canvas, storage, sync, context or command code is inspected THEN it SHALL contain no Wardley-specific type check, import or branch. The only core change this spec anticipates is Requirement 8.4's background-layer seam, which SHALL be type-agnostic.
5. WHEN this module is complete THEN the two shipped diagram types SHALL differ in layout ownership, viewport behaviour and canvas chrome, and core SHALL be unchanged by that difference. **This is the acceptance test for the pluggable diagram-type model**, and a core change needed to accommodate this module is a finding to report, not a step to take quietly.
6. WHEN the module reaches each state THEN `docs/diagrams.md`'s `wardley/map` row SHALL be updated per CLAUDE.md — `💡 Identified` to `📝 Specified` when this document is approved, and onward as implementation proceeds. Its Notes column SHALL gain the `.owm` DSL reference, as the mindmap row cites `.mm`.

## Non-Functional Requirements

### Code Architecture and Modularity

* **Single Responsibility Principle**: the `.owm` parser/writer, the identity sidecar, the map model, the element/delta mapping, the context resolver, the action provider and the canvas rendering are separate, independently testable concerns.
* **Modular Design**: `EtAlii.Adp.Diagram.WardleyMap`, `diagrams/wardley-map/api` and `diagrams/wardley-map/client` depend only on core abstractions; nothing in core depends on the module, and core stays compilable with zero diagram modules present.
* **Dependency Management**: the parser/writer SHALL be usable without gRPC, without the filesystem and without a browser — given text, it returns a model, and given a model and the original text, it returns new text.
* **Clear Interfaces**: the integration surface is the discovered `Diagram.Definition`, the registered `IDiagramDocumentFactory`, `IContextSourceResolver`, `IContextActionProvider`(s) and `ICommandHandler`s. No other coupling.

### Performance

* Opening a map SHALL be one file read plus one sidecar read, and SHALL be fast enough to feel immediate for the map sizes this notation is used at (tens of elements, occasionally low hundreds).
* An edit SHALL rewrite the statements it touches, not re-serialize the document, so save cost tracks the size of the change rather than the size of the map.
* Because the whole map is delivered (Requirement 10.5), the baseline `Add` SHALL be one message rather than a stream of per-element ones.

### Security

* The module SHALL read and write only through the backend's file-access layer, within the opened workspace folder; an absolute path SHALL never reach a client.
* A `url`, a `submap` target, a component name or a note read from a file authored elsewhere is untrusted input: it SHALL be rendered as text, never interpreted as markup or script, and a `submap` or `url` SHALL only resolve within the authorized workspace or as an external link the user explicitly activates.
* A malformed `.owm` SHALL not be able to exhaust memory or time — parsing SHALL be linear in the file's length, with no construct that can expand.

### Reliability

* A save SHALL be atomic and SHALL never leave an `.owm` observable in a partial state; creation SHALL leave both files or neither.
* A map ADP did not change SHALL round-trip byte-identically (Requirement 3.1). This is the module's headline correctness property and SHALL be tested against a corpus of real maps, including ones using constructs this spec does not model.
* A lost or inconsistent identity sidecar SHALL degrade to re-derivation, never to a map that will not open (Requirement 4.5).
* Every bug found while implementing or verifying this spec SHALL be covered by a test, or recorded in `tests.md`, per CLAUDE.md.

### Usability

* The four evolution stages SHALL be legible on the canvas at default zoom, since a component's position means nothing without them.
* A map authored in onlinewardleymaps.com, VS Code or Obsidian SHALL open in ADP looking recognisably like the same map, and SHALL open in those tools again afterwards.
* The user SHALL be able to tell an element they positioned from one ADP positioned, which — since ADP never positions anything here (Requirement 7.1) — means every element on the canvas is the author's own claim.
