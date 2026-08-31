# Requirements Document

## Introduction

This spec covers a **timeline diagram**: elements laid out left to right along a time axis, each occupying a **period** — a begin and an end it can be dragged and resized by — placed on a **row**, with connections drawn as bezier curves between the mid-points of their left and right sides. It is catalogued as `generic/timeline`, following the `generic/swimlane` precedent for a notation no standards body owns.

**What is genuinely new here, and what is not.** Two things are new. First, **an element occupies a period rather than a point**: every type so far positions a box, while here the box's *width is data* — begin and end are properties a user edits, and resizing the box edits them. Second, **the canvas needs chrome fixed to the viewport rather than to the diagram**: a time ruler along the bottom that stays put while the diagram scrolls beneath it, growing and shedding labels as the visible window moves. Everything else is deliberately not new — the connector geometry, the positional drag, the command/undo path, the toolbox, the property grid and the context actions are all mechanisms that already exist and that this type consumes unchanged.

**Where it sits among the types already built.** [`mindmap-diagram`](../mindmap-diagram/requirements.md) proved positions that are *computed* and never written back. [`wardley-map`](../wardley-map/requirements.md) proved the mirror image, where coordinates are *authored* and moving one is an edit. This type is wardley's shape with **both axes authored, but neither of them a raw coordinate**: x is authored *as time*, through the begin and end a user types in the property grid or drags an element horizontally to change; y is authored *as a row*, an index a user changes by dragging an element vertically onto another row. That pairing — one axis carrying domain data, one axis carrying a placement the author chooses — is the thing worth proving, because it is the shape every scheduling, roadmap and process-over-time diagram takes.

**Dependencies.** This spec redefines none of them:

* [`adp-diagram-ide`](../adp-diagram-ide/requirements.md) — the workspace shell, the pannable/zoomable canvas, read-only mode.
* [`grpc-core-communication-specification`](../grpc-core-communication-specification/requirements.md) — `Element`, `Delta`, `Point2D`, and the `Any` payload extension point.
* [`diagram-workspace-tabs`](../diagram-workspace-tabs/requirements.md) — the tab host and its unavailable state.
* [`add-diagram-action`](../add-diagram-action/requirements.md) and [`create-diagram-file`](../create-diagram-file/requirements.md) — the Add action, the `.adp` file and its MIME first line.
* [`context-service`](../context-service/requirements.md) — the selection chain, `IContextActionProvider`, and the pushing of a selection's actions.
* [`property-grid`](../property-grid/requirements.md) — `IContextPropertyProvider`, `ContextProperty`, `DescribeProperties`/`SetProperty`.
* [`diagram-undo-redo`](../diagram-undo-redo/requirements.md) and `tech.md`'s **Commands** rule.

**Reused code is generic code, and generic code lives in the shared projects.** The mechanisms this type consumes are already there, and this module depends on the **projects** that hold them rather than on the diagram modules that happened to introduce them:

| What is reused | The shared project it lives in |
| --- | --- |
| Routing a body file on a distinctive extension | `src/backend/EtAlii.Adp.Backend/Hierarchy/DiagramFileRouter.cs` |
| The diagram-type declaration (`DiagramDefinition`, `DiagramOrigin`) | `src/backend/EtAlii.Adp.Diagram/_Model/` |
| The positional move a drag dispatches | `MoveElementRequest.position` in `src/api/diagrams.proto`, `IDiagramSession.MoveElementToAsync` in `src/backend/EtAlii.Adp.Backend/Diagrams/` |
| Connector anchors and curves (`sideAnchorOf`, `horizontalBezierPath`) | `src/client/src/canvas/connectors.ts` |

The specs that introduced each of these — [`wardley-map`](../wardley-map/requirements.md) for the positional move, [`azure-pipeline-diagram`](../../archive/specs/azure-pipeline-diagram/requirements.md) for the routing work — are **provenance, not dependencies**; nothing here reaches into another diagram module. WHERE implementation finds that something it needs still sits inside a diagram module rather than in the shared project it belongs to, **lifting it into that project is part of this work**, not a reason to copy it or to depend on the module.

**What this spec changes in core: nothing.** Stated positively because it is unusual and worth checking rather than assuming. The two new things above are both satisfied without a core change: the time ruler is drawn by this module's own canvas component, which knows its own scroll offset natively in the client and needs nothing from the backend to render view-fixed chrome; and the resize adorners are this module's own pointer handling, dispatching ordinary commands. If implementation finds a core change is genuinely required, that is a **finding to report** per the established practice, not a licence to make one quietly.

**Deliberately out of scope.**

* **Scheduling.** No critical path, no resource levelling, no automatic date arithmetic, no "move this and everything downstream shifts". A connection here means what the author says it means; it does not propagate.
* **Calendars.** No working days, holidays, or timezone conversion. Requirement 3.7 fixes an interpretation and stops there.
* **Being a Gantt chart.** `mermaid/gantt` is a separate catalog entry. See Requirement 1.4 for why this is not that, and why the Gantt formats were considered and rejected as the body.

## Alignment with Product Vision

* [product.md](../../steering/product.md)'s **"Files are the source of truth"** — a timeline is a plain text document in the project folder, diffable and reviewable, and a document ADP did not change comes back byte-identical.
* product.md's **"A familiar surface"** — dragging an element's edge to change when it ends is the interaction every scheduling tool has taught people to expect; this spec asks for that rather than a dialog.
* [tech.md](../../steering/tech.md)'s **Diagram storage** — an established text format wherever possible, with ADP's own data never smuggled into a file another tool owns. Requirement 1.4 is where that rule is applied *and* where its limit is stated honestly.
* tech.md's **Specifying a diagram type** — its four mandatory aspects are Requirements 1–2 (file format), 4–8 (visualization), 9 (toolbox) and 11 (context actions and commands).
* tech.md's **Commands** — every edit, including a drag and a resize, is a command with an inverse.
* [structure.md](../../steering/structure.md)'s **dependency direction** — the module depends on core; core never depends on it. The time ruler is the test: it is timeline-specific chrome, so it lives in the module, not in the shell.

## Requirements

### Requirement 1 — Registration, and the document on disk

**User Story:** As an author, I want my timeline stored as readable text I can diff and review, so that it lives in version control like everything else in the project.

#### Acceptance Criteria

1. WHEN a timeline exists on disk THEN it SHALL be `<name>.adp`, whose first line is the MIME line `generic/timeline`, plus a sibling document holding the timeline itself.
2. WHEN the module declares itself THEN it SHALL set `DiagramDefinition.Extension` to `".tml"` — **Timeline Markup Language** — and SHALL NOT declare it shared. `.tml` is this type's own extension, owned outright the way `.mm`, `.owm` and `.dsl` are owned by theirs, so a bare `.tml` body SHALL route to this type on sight through the existing `DiagramFileRouter`, with no registration step and no Add-on-a-file path.
3. WHEN a new timeline is created through the Add dialog THEN its `IDiagramDocumentFactory` SHALL produce a valid empty document — a schema marker and an empty element list — and nothing else.
4. **BECAUSE no existing notation carries what this type needs**, the body SHALL be YAML against a schema this spec defines (Requirement 2), written to a `.tml` file, and this SHALL be recorded as a decision rather than a default. The Gantt family was considered and rejected: `mermaid/gantt` and PlantUML's gantt both **compute** their layout, express links only as scheduling dependencies, and have nowhere to put an authored row — so adopting one would mean either discarding the two features that define this type or smuggling them into comments, which tech.md forbids outright. YAML is chosen as an established, diffable, human-editable *serialization* while the *schema* and the *extension* are ADP's own — which is what makes the extension distinctive rather than shared: a `.tml` file is a timeline, and no other tool writes one. **If a reviewer knows a text notation that carries begin, end, row placement and arbitrary connections together, that is a better answer than this one and this requirement should be revisited before implementation starts.**
5. IF the sibling document named by an `.adp` file is missing THEN the diagram SHALL open in the unavailable state `diagram-workspace-tabs` Requirement 5 defines, naming the path it looked for — never a crash and never a silently empty canvas.

### Requirement 2 — Reading and writing the document without disturbing it

**User Story:** As an author, I want ADP to leave my file alone, so that opening a timeline does not produce a diff nobody can review.

#### Acceptance Criteria

1. WHEN a document is opened and saved with no edit THEN the file SHALL be byte-identical, including line endings, indentation, key order, comments and blank lines — the guarantee `mindmap-diagram` Requirement 3, `wardley-map` Requirement 3 and `c4-diagrams` Requirement 3 each established for their own formats.
2. WHEN an edit is made THEN only the lines that edit affects SHALL change; the writer SHALL NOT reformat, reorder or re-indent the rest of the document, and SHALL NOT rewrite a key it did not change.
3. WHEN the document contains keys this spec does not model THEN they SHALL be preserved verbatim across a round trip and SHALL NOT be silently dropped, so a later version of this type — or a human — can put data there without ADP eating it.
4. IF the document is not well-formed YAML, or does not satisfy the schema THEN the diagram SHALL open in the unavailable state with the parser's message and, where it has one, the line number — and the file SHALL NOT be rewritten while it is unparseable, so a broken file is never made worse.
5. WHEN an element is deleted THEN the connections that referenced it SHALL be deleted with it, and the action SHALL say how many connections that is before it runs.

### Requirement 3 — The model: an element occupies a period, on a row

**User Story:** As an author, I want each element to have a beginning and an end, so that the diagram says when something happens and for how long.

#### Acceptance Criteria

1. WHEN an element is modelled THEN it SHALL carry: a stable **id**, a **label**, a **begin**, an **end**, and a **row**.
2. WHEN begin and end are stored THEN they SHALL be ISO 8601 values, and the same element SHALL NOT mix a date-only begin with a date-time end.
3. WHEN an element is created THEN begin SHALL be required and end SHALL be optional; an element with a begin and an end occupies a **period**, while an element with no end is a **moment** — a single point in time, drawn as such (Requirement 4.6) and not resizable at its right edge (Requirement 7.5).
4. IF end is earlier than begin THEN the document SHALL be reported as invalid for that element (Requirement 12.2) and the element SHALL still be drawn — but **no path through the application SHALL be able to create that state**. Neither a gesture on the canvas (Requirement 7.4) nor an edit in the property grid (Requirement 10.3) SHALL produce it, so **the only way a file can contain it is that somebody edited the text by hand**, and that is the case this requirement exists to survive.
5. WHEN an element's vertical placement is stored THEN it SHALL be a **row**: an integer index, where several elements MAY share one row because they occupy different periods on it, and where dragging an element vertically **snaps it to the nearest row** rather than leaving it between two (Requirement 6.2).
6. WHERE rows are used THEN they SHALL be a **placement grid and nothing more** — a row has no identity, no label, no membership and no meaning this type assigns to it. That is what keeps this a timeline rather than a swimlane: `generic/swimlane` is a separate catalog entry precisely because a lane there *means* something (an actor, a system, a stage), and a timeline that silently grew lane semantics would be the wrong diagram.
7. WHEN times are interpreted THEN they SHALL be taken at face value, with no timezone conversion, no working-day arithmetic and no calendar; a value written is the value drawn.
8. WHEN a connection is modelled THEN it SHALL carry a stable id, a source element id, a target element id, and an optional label — and SHALL NOT carry a meaning this type assigns to it (see *Deliberately out of scope*).

### Requirement 4 — Left-to-right layout: x is authored as time, y as a row

**User Story:** As an author, I want position to mean something — horizontal is when, vertical is which row I put it on.

#### Acceptance Criteria

1. WHEN a timeline is drawn THEN the horizontal axis SHALL be time, increasing left to right, and an element's **left edge SHALL be its begin** and its **right edge its end**.
2. WHEN an element's horizontal extent is determined THEN it SHALL be derived from begin and end through the current scale — the module SHALL NOT store an x coordinate. **x is authored, but it is authored as time**: a user changes it by typing a begin or an end in the property grid, or by dragging the element horizontally (Requirement 6.1), and both edit the same two values. Storing a coordinate as well would give the diagram two answers to when something happens.
3. WHEN an element's vertical placement is determined THEN it SHALL be the authored row from Requirement 3.5, converted to a y through the row geometry and otherwise unchanged — the module SHALL NOT compute, pack or auto-arrange elements onto rows of its own choosing.
4. **BECAUSE this is the authored-coordinate fork** `tech.md` requires every diagram-type spec to declare: placement here is **authored, not computed**, on both axes — exactly as `wardley-map` and unlike `mindmap-diagram`. A drag is an edit to the document and survives reopening.
5. WHEN two elements overlap — in time on the same row, or by any other arrangement the author chose — THEN they SHALL be drawn overlapping. The author put them there, and a diagram that silently moved things to avoid overlap would be lying about its own data.
6. WHEN an element is a moment (Requirement 3.3) THEN it SHALL be drawn as a marker at its begin, on its row, rather than as a zero-width box.
7. WHEN the same document is opened twice THEN the drawing SHALL be identical, because a diagram that shifts between openings cannot be talked about.

### Requirement 5 — The time ruler: chrome fixed to the view, not to the diagram

**User Story:** As a reader, I want to see what time I am looking at without hunting, and to keep seeing it as I scroll.

#### Acceptance Criteria

1. WHEN a timeline is displayed THEN a **time ruler** SHALL be drawn along the bottom of the view, carrying labels for the times currently visible.
2. WHEN the ruler is positioned THEN it SHALL be fixed to the **view** bounds and SHALL NOT scroll vertically out of sight with the diagram — it stays at the bottom of what the user is looking at, whatever the diagram's own extent.
3. WHEN the user scrolls or pans horizontally THEN the ruler's labels SHALL scroll with the content, so a label stays over the time it names; labels scrolled out of view SHALL be removed and labels scrolled into view SHALL appear.
4. WHEN the visible time range changes by zooming THEN the ruler SHALL choose a **label interval appropriate to that range** — years, months, days, hours — so the ruler stays legible rather than either crowded or empty at every zoom level.
5. WHEN labels are placed THEN they SHALL be positioned on round time boundaries appropriate to the chosen interval, not at arbitrary offsets from the viewport edge.
6. WHEN the ruler renders THEN it SHALL be visually subordinate to the diagram — a watermark the reader can consult, not chrome that competes with the content.
7. WHEN the ruler is implemented THEN it SHALL live in this module's own canvas component, drawn from the viewport that component already knows, and SHALL NOT require a core change (see *What this spec changes in core*).

### Requirement 6 — Dragging: horizontal moves time, vertical moves row

**User Story:** As an author, I want to drag an element to reschedule it or to move it to another row, and have the diagram understand which I meant.

#### Acceptance Criteria

1. WHEN an element is dragged horizontally THEN both its begin and its end SHALL shift by the same amount — the element moves in time, and its **duration is preserved**.
2. WHEN an element is dragged vertically THEN it SHALL **snap to the nearest row** on release, its authored row SHALL become that row, and its begin and end SHALL NOT change. An element SHALL NOT come to rest between two rows.
3. WHEN a drag has both components THEN both SHALL apply, as one edit rather than two.
4. WHEN a drag completes THEN it SHALL be a single `ICommand` with an inverse restoring the previous begin, end and row, per `tech.md`'s Commands rule — and a drag SHALL produce **one** history entry, not one per pointer move.
5. WHEN a drag is in progress THEN the element SHALL follow the pointer; the **times** it would land on and the **row** it would snap to SHALL both be visible before the user commits; and the element's **connections SHALL be redrawn as it moves**, so the author can see what the move does to the curves rather than discovering it on release (Requirement 8.6).
6. WHEN a drag is cancelled — `Escape`, or released outside a valid area — THEN the element SHALL return to where it started and no command SHALL be recorded.
7. WHEN the positional move is dispatched THEN it SHALL use `MoveElementRequest.position`, the field `wardley-map` contributed, and SHALL NOT reintroduce any encoding of coordinates into another field.

### Requirement 7 — Adorners: resizing an element edits its begin and end

**User Story:** As an author, I want to drag an element's left or right edge to change when it starts or finishes, because that is faster than typing a date.

#### Acceptance Criteria

1. WHEN an element is selected THEN **adorners** SHALL appear on its left and right edges, indicating that the element can be resized there.
2. WHEN the **left** adorner is dragged THEN it SHALL change the element's **begin** and SHALL leave its end unchanged.
3. WHEN the **right** adorner is dragged THEN it SHALL change the element's **end** and SHALL leave its begin unchanged.
4. WHEN a resize would put end before begin THEN it SHALL be refused at the boundary — the edge SHALL stop rather than the element inverting — so the invalid state in Requirement 3.4 can be read from a hand-edited file but never created by a gesture.
5. WHEN an element is a moment (Requirement 3.3) THEN it SHALL offer no right adorner, and the action that gives it an end SHALL come from the context menu (Requirement 11.3) rather than from a gesture on something that is not there.
6. WHEN a resize completes THEN it SHALL be one `ICommand` with an inverse restoring the previous value, on the same terms as Requirement 6.4.
7. WHEN a resize is in progress THEN the time the edge would land on SHALL be visible before the user commits, and the element's connections SHALL be redrawn as the edge moves (Requirement 8.6).
8. WHERE the diagram is read-only THEN adorners SHALL NOT be offered, per `adp-diagram-ide`'s read-only mode.

### Requirement 8 — Connections: side anchors and bezier curves

**User Story:** As an author, I want to connect one element to another and have the line read clearly across a left-to-right diagram.

#### Acceptance Criteria

1. WHEN a connection is drawn THEN it SHALL leave and arrive at the **mid-point of an element's left or right side** — never its top, bottom or corner — because a left-to-right diagram reads along that axis.
2. WHEN a connection is rendered THEN it SHALL be a **cubic bezier** that leaves and arrives horizontally, so the curve stays legible against a timeline's strong horizontal grain.
3. WHEN the geometry is implemented THEN it SHALL reuse `src/client/src/canvas/connectors.ts` — `sideAnchorOf` for the anchors and `horizontalBezierPath` for the curve — and SHALL NOT introduce a second implementation of either. This module contributes no new connector geometry.
4. WHEN a connection is created THEN the user SHALL drag from one element's side anchor to another element's side anchor.
5. WHEN a connection gesture ends anywhere that cannot accept it THEN it SHALL cancel with no change to the document and SHALL say why, rather than creating a connection to nothing.
6. WHEN an element is dragged or resized THEN its connections SHALL follow, recomputed from the anchors rather than stored as paths — **continuously, while the gesture is in progress** and not only once it ends (Requirements 6.5, 7.7).
7. WHEN a connection would join an element to itself THEN it SHALL be refused with a reason.
8. WHEN two elements are already connected THEN a further connection between them SHALL be permitted, because the notation carries a label per connection and two relationships between the same pair are meaningful.

### Requirement 9 — Toolbox

**User Story:** As an author, I want to drag a new element onto the timeline rather than hunting for a menu.

#### Acceptance Criteria

1. WHEN a timeline is active THEN the Toolbox SHALL be described **by the backend as data**, per `tech.md`'s Toolbox rule and the existing `DescribeToolbox` RPC, and the client SHALL render a palette it does not interpret.
2. WHEN the Toolbox is populated THEN it SHALL offer a **Period** element (begin and end) and a **Moment** element (begin only, Requirement 3.3).
3. WHEN an item is dropped on the canvas THEN the element SHALL be created with its begin taken from the **drop position's time** and its row from the **row nearest the drop position's y** — so where the user dropped it is where it lands, in both senses, and it lands on a row rather than between two.
4. WHEN a newly dropped element needs a label THEN it SHALL be created with a placeholder and immediately offered for rename in place, rather than opening a dialog before the user has seen the thing they made.
5. WHEN a drop creates an element THEN it SHALL be one undoable command.

### Requirement 10 — Properties

**User Story:** As an author, I want to type an exact date when dragging is not precise enough.

#### Acceptance Criteria

1. WHEN an element is selected THEN its properties SHALL be contributed through the `IContextPropertyProvider` seam [`property-grid`](../property-grid/requirements.md) defines, as `ContextProperty` rows the panel renders without understanding — this module SHALL introduce no panel change.
2. WHEN an element is selected THEN the contributed properties SHALL include: **Label**, **Begin**, **End**, and **Row**.
3. WHEN Begin or End is edited THEN the value SHALL be validated on commit and an invalid or malformed value SHALL be **rejected with a reason** rather than written — including a value that would put end before begin, which the grid SHALL refuse on exactly the terms the adorner does (Requirements 3.4, 7.4). The grid is the second of the two paths that must be closed for Requirement 3.4 to hold, and closing only one of them would leave the state reachable.
4. WHEN a connection is selected THEN its **Label** SHALL be contributed, and its source and target SHALL be shown **read-only** with a reason saying that reconnecting is done on the canvas.
5. WHEN a property is edited THEN the change SHALL travel as a command, land on the project's history, and reach every other open view through the existing delta stream — never written directly to the file by the panel.
6. **Editor gap, stated rather than worked around:** `ContextPropertyEditor` now offers `Line`, `Text`, `Toggle` and `Choice` — the `Choice` editor having been added by [`azure-pipeline-diagram`](../../archive/specs/azure-pipeline-diagram/requirements.md), the type that first needed it. None of the four is date-aware, so Begin and End SHALL use `Line` with the validation in 10.3 until one exists. `property-grid` Requirement 9 owns that gap; this spec records a **date editor** as the next want against the same seam and SHALL NOT add a private one. WHERE implementation does add it, it SHALL be added to the shared seam for every type to use, exactly as `Choice` was.

### Requirement 11 — Context actions and commands

**User Story:** As an author, I want every timeline edit to be undoable and reachable the same way as every other edit in ADP.

#### Acceptance Criteria

1. WHEN any edit is made THEN it SHALL be an `ICommand` with an inverse, per `tech.md`'s Commands rule and `diagram-undo-redo`: add element, remove element, rename, set begin, set end, move (Requirement 6), resize (Requirement 7), connect, disconnect, and relabel a connection.
2. WHEN actions are offered THEN they SHALL come from an `IContextActionProvider` and reach the ribbon, the right-click menu and the keyboard through the one path `context-service` defines, with any shortcut described as data.
3. WHEN an **element** is selected THEN the actions offered SHALL include *Remove*, *Connect*, and — for a Moment — *Give it an end* (Requirement 7.5); for a Period, *Remove its end*.
4. WHEN a **connection** is selected THEN the actions offered SHALL include *Disconnect* and *Relabel*.
5. WHEN the **canvas background** is selected THEN the actions offered SHALL include *Add element here*, taking its begin and its row from the click, on the same terms as a toolbox drop (Requirement 9.3).
6. WHEN *Connect* is chosen from a menu rather than drawn THEN the system SHALL put the canvas into a state where the next element clicked completes the connection, and `Escape` SHALL abandon it without change.
7. WHERE the diagram is read-only THEN no mutating action SHALL be offered — not greyed out and silently inert, but absent or explained.
8. WHEN an action cannot apply to the current selection THEN it SHALL be reported unavailable with a reason, through the mechanism `context-service` already provides.

### Requirement 12 — Diagnostics

**User Story:** As an author, I want to be told what is wrong with my timeline rather than seeing it drawn wrongly.

#### Acceptance Criteria

1. WHEN a document is read THEN any diagnostic SHALL be reported through the [`errors-and-warnings-panel`](../errors-and-warnings-panel/requirements.md), not through a canvas overlay of this module's own invention.
2. WHEN an element has end before begin (Requirement 3.4), an unparseable time, or a connection naming an element that does not exist THEN it SHALL be reported as a **warning** naming the element, and the rest of the diagram SHALL still draw.
3. WHEN the document is unparseable (Requirement 2.4) THEN the diagnostic SHALL be an **error** and SHALL be the same message the unavailable state shows, so the panel and the canvas never disagree.
4. WHEN a diagnostic is selected in the panel THEN it SHALL select the offending element through the existing context chain, per the errors panel's own contract.

## Non-Functional Requirements

### Code Architecture and Modularity

* **Single Responsibility Principle**: parsing/writing the document, the time-to-pixel scale, the element model, and the mapping to `Element`/`Delta` are four separate concerns in four separate units — the split `mindmap-diagram` arrived at and every type since has adopted.
* **One entity per file, no nested types**: per `tech.md`, data-carrying records live in the module's `_Model/` folder at namespace level. A nested `record struct` is not acceptable in new code, whatever nearby files still do.
* **Clear Interfaces**: the time-to-pixel scale SHALL be a pure function of (time range, viewport width), and the row-to-pixel geometry a pure function of (row index, row height) with an inverse that answers *which row is nearest this y* — so every guarantee in Requirements 4, 5 and 6.2 is testable without a canvas, a file or a connection.
* **No core coupling**: core SHALL learn nothing about time or rows. The ruler, the adorners, the period model and the row grid are all this module's; what crosses to core is an `Element`, a `Delta` and a `Point2D`, exactly as for every other type.
* **Reuse over reinvention**: connector geometry from `src/client/src/canvas/connectors.ts`, positional moves from `MoveElementRequest.position`, routing and declaration from `EtAlii.Adp.Backend/Hierarchy/` and `EtAlii.Adp.Diagram/_Model/`. This module SHALL NOT reimplement any of them, SHALL NOT copy one out of another diagram module, and WHERE one is found living in a diagram module rather than in its shared project it SHALL be lifted there first.

### Performance

* A timeline of 500 elements and 500 connections SHALL open and render within the budget `adp-diagram-ide` sets for a diagram of that size.
* Scrolling and zooming SHALL re-render the ruler and the visible elements without a round trip to the backend — the scale is a client-side transform, and panning a diagram is not an edit.
* A drag or resize SHALL update the canvas at pointer rate while producing exactly one command on release (Requirements 6.4, 7.6).

### Reliability

* A document ADP did not change SHALL be byte-identical after a save (Requirement 2.1), guarded by a corpus test covering CRLF and LF line endings, comments, and documents containing keys the module does not model.
* An unparseable or partially written document SHALL never be overwritten (Requirement 2.4).
* Requirement 4.7's determinism SHALL be tested by rendering the same document twice and comparing output.
* Every refusal in Requirements 7.4, 8.5, 8.7 and 10.3 SHALL have a test proving the refusal, because a guard that is not tested is a guard that quietly stops guarding. Requirement 3.4's claim — that end-before-begin is reachable only by hand-editing the file — SHALL be tested from **both** paths it closes, the adorner and the property grid, since a claim about every path is only as good as the least-tested one.
* Requirement 6.2's snapping SHALL be tested at the midpoint between two rows, where an off-by-one in the nearest-row calculation is the mistake to expect.

### Usability

* The ruler SHALL be readable at every zoom level the canvas allows (Requirement 5.4) — this is the requirement most likely to be got wrong, and the one a reviewer should exercise first.
* An adorner SHALL be large enough to hit comfortably without obscuring a short element, and SHALL NOT be the only way to change begin or end — the property grid is the precise path (Requirement 10.2).
* Dragging SHALL feel like the scheduling tools people already use: horizontal movement reschedules, the duration does not change unless an edge is dragged, and a vertical move lands on a row rather than wherever the pointer happened to stop.
