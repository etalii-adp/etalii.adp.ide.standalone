# Requirements Document

## Introduction

The user's request, 2026-09-27, verbatim:

> in spec workflow mcp, start a specification to improve the ghg and add 'triggers' and 'notes'.
>
> * A note should be straightforward. but include wordwrapping and inline text editing.
> * A trigger is a singular point in time (depicted by a small circle, half size of height of trend), and from it influences can be added to trends.
> * A influence cannot flow from a trend to a trigger.
> * The text of a influence is positioned to the left, similar as the trends.
> * Triggers could be inventions, political moments, natural disasters or any other 'moment in time'.
> * Hovering over the trigger should show that it is a trigger.
> * Triggers also snap to vertical lines, and are expressed next to a label also as a moment in time.
> * Triggers can also be dragged and dropped from the toolbox.
> * propertygrid behaves as usual.
> * triggers can also have tags added. and are impacted by the filtering. And of course are also included in the currently being developed algorithm that facilitates the 'compact view'.

And its follow-up, the same day:

> Also add 'real' example triggers to the existing ghg diagrams. and consider rewriting trends that might be logically actually kind of a trigger (like a scientific breakthrough). If doing so make sure to layout the file also accordingly so that it still looks nice.
>
> When finished with the implementation also update the ghg implementation details, examples and purpose in notion.

**What it builds on.** The Gartner hype cycle graph is specified in [gartner-hype-cycle-graph](../gartner-hype-cycle-graph/requirements.md) (implemented), and its compact view in [ghg-compact-mode](../ghg-compact-mode/requirements.md) (approved, being implemented). This document amends neither: it adds two element types to the graph and says how each existing behaviour treats them. **It depends on ghg-compact-mode being on `develop` first**, because Requirement 6 extends the compact placement that specification introduces.

**Why triggers.** A trend has a span; many of the things that move trends do not. The transistor's invention, the Chernobyl disaster and the 1973 oil embargo are moments, and drawing them as one-month trends with a single Peak phase misstates them: it claims a hype cycle they never had. A trigger says what they are, a moment in time that influenced trends, and lets a graph show *what set a trend off* beside *which trend enabled which*.

**Why notes.** A graph used for technology assessment needs room for the assessor's own remarks, placed where they apply, without inventing a trend to carry them.

## Terms

| Term | Meaning |
| --- | --- |
| **Trigger** | A new element type: one moment in time, drawn as a small circle. It has a Name, a date, a row, Tags and a Description. Examples are inventions, political moments and natural disasters. |
| **Note** | A new element type: free text in a box on the canvas, word-wrapped, with no relations. |
| **Trend**, **Phase**, **Influence**, **Tag** | As in the gartner-hype-cycle-graph specification. An influence may now start at a trigger as well as at a trend. |
| **Compact placement** | As in the ghg-compact-mode specification. |

## What was measured

The shared canvas library and the hype cycle module, read on `develop` at `91d8de03` on 2026-09-27.

| Capability | State | Where |
| --- | --- | --- |
| A circle shape | **Exists** | `ellipse` in `BUILT_IN_SHAPES`, drawn round when width equals height. The timeline's `moment` shape is a diamond, not a circle. |
| A label left of the element, right-aligned, vertically centred | **Exists** | `placement: "before"`, which the trend's name uses (`GhgCanvas.tsx`). |
| A label built from two fields (a name and a date) | **Exists** | `Binding` templates, such as the timeline's `{element.label}`. |
| A tooltip on the whole element | **Exists** | `ElementTypeDefinition.tooltip`. |
| Word-wrapped text in a box, edited in place across several lines | **Exists** | `LabelDeclaration.wrap` with `editable: true` and `resize: "both"`; FDG's Comment is exactly this (`FdgCanvas.tsx`, `COMMENT_TYPE`), with anchors disabled. |
| Restricting a relation's source and target element types | **Exists** | `endpoints.source.elementTypes` and `endpoints.target.elementTypes`. |
| One influence per ordered pair, and hiding an influence whose phase is hidden | **Exists** | `cardinality: { perPair: "ordered" }`, `hideWhenAttachmentHidden`. Whether the second works when only the target end has a phase attachment is **not measured**; the design says. |
| A connection between an element with edge anchors and one with anchors along an edge | **Not measured** | Every influence today runs between two `along`-anchored trends. The design measures it. |
| Snapping an element's centre, rather than its leading edge, to a line | **Expressible, unused** | A snap rests the leading edge on `origin + k * step`, and `SnapAxis.origin` is a `DeclaredNumber`, bindable per element, so an origin offset by half the element's size puts its centre on the line. Nothing does this today. |
| A filter that leaves elements of some types alone | **Missing** | The canvas hides every element whose tags at `filter.field` do not match (`DiagramCanvas.tsx`, `tagsByElement`), so an element with no tags vanishes under any filter. |
| A compact placement that gives one type the compact width and leaves another type its own size | **Missing** | ghg-compact-mode's `row-packed` layout gives every element one declared width. |
| A toolbox offering more than one item for this diagram | **Exists** | `GhgToolboxProvider`, as data from the backend. |
| Unknown top-level keys in a `.ghg` document | **Pass over and survive** | gartner-hype-cycle-graph Requirement 2.4, so a document with triggers and notes opens in an older build without losing them. |

## Open questions

Each is put to the user as a selection. The option marked **(default)** is what this document is written against until the user rules otherwise; a ruling that differs is a small amendment to the criteria it names.

| # | Question | Options | Criteria affected |
| --- | --- | --- | --- |
| Q1 | "The text of a influence is positioned to the left, similar as the trends": which text is meant? | **(default) The trigger's name,** drawn left of its circle like a trend's; influences stay unlabelled, as today. · **An influence label,** a new text on each influence drawn left of its target end. · Other. | 2.3 |
| Q2 | How is a trigger's date written next to its name? | **(default) In the diagram's time unit:** `Transistor · Dec 1947` in a month diagram, `Transistor · 1947` in a year, decade or century diagram. · **Always the stored month:** `Transistor · 1947-12`. · Other. | 2.3 |
| Q3 | Can an influence run from a trigger to another trigger? | **(default) No:** an influence always ends at a trend, so a trigger is only ever a source. · **Yes:** one moment may cause another, such as an invention prompting a law. · Other. | 3.2 |
| Q4 | Do notes take part in the tag filter? | **(default) No:** notes carry no tags and stay visible under every filter. · **Yes:** notes carry tags and are hidden like trends. · Other. | 5.3 |
| Q5 | Where does a note go in compact mode? | **(default) Placed by the compact placement,** in time order by its left edge, keeping its own size and rows. · **Hidden** while compact mode is on. · Other. | 6.3 |
| Q6 | Does a trigger carry a kind (invention, political, disaster, other)? | **(default) No kind:** Tags already say it, and filter by it. · **A fixed kind with its own icon** inside the circle. · Other. | 2.1 |

## Alignment with Product Vision

The hype cycle graph is ADP's first diagram for (constructive) technology assessment. Triggers make it say *what set a trend off*, which a technology assessment asks as often as *what enabled it*, and notes let an assessor annotate a graph in place. It follows `structure.md`'s rule that a diagram type adds declarations on the shared library and no core code, so what the two element types need from the canvas is library work (Requirement 8).

## Requirements

### Requirement 1 - The document

**User Story:** As an author, I want triggers and notes stored in the same readable `.ghg` file as my trends, so that the whole graph diffs and survives outside ADP.

#### Acceptance Criteria

1. WHEN a graph with triggers is saved THEN they SHALL be written as a top-level `triggers` list, each entry carrying an id, its Name, its date as an ISO date at month precision (`1947-12`), its row, its Tags and its Description.
2. WHEN a graph with notes is saved THEN they SHALL be written as a top-level `notes` list, each entry carrying an id, its text (multi-line text preserved), its position as a month-precision date and a row, and its width and height.
3. WHEN an influence starts at a trigger THEN its `from` SHALL name the trigger and SHALL carry no phase, edge or fraction; its `to` end SHALL be written as today.
4. WHEN a document is read THEN the parser SHALL never throw, and a document breaking a rule of this specification (an influence ending at a trigger, a trigger or note without a readable date, an id shared between a trend, a trigger and a note) SHALL still open, with each breach reported by the validator to the Errors and Warnings panel.
5. WHEN ADP did not change a document THEN it SHALL be written back byte-identical, and WHEN a trigger or note changes THEN only the lines that change SHALL be rewritten. A document without triggers or notes SHALL be written exactly as it is today.
6. WHEN the document version is written THEN it SHALL stay at the current version: both lists are additions an older build passes over and keeps.

### Requirement 2 - The trigger

**User Story:** As an author, I want to mark a moment in time that set trends off, drawn so that it is not mistaken for a trend.

#### Acceptance Criteria

1. WHEN a trigger is drawn THEN it SHALL be a circle whose diameter is half a trend's height, its centre at the start of its date's month on the time scale and on the middle of its row. It SHALL carry no phases (Q6; default no kind).
2. WHEN the pointer rests on a trigger THEN a tooltip SHALL say that it is a trigger, with its name and date, such as *Trigger: Transistor, December 1947*.
3. WHEN a trigger is drawn THEN its label SHALL be drawn **left of the circle**, right-aligned and vertically centred, as a trend's name is, and SHALL show its Name followed by its date (Q1, Q2; default name and the date in the diagram's time unit).
4. WHEN a trigger is dragged THEN its centre SHALL snap horizontally to the time scale's step lines (the ruler's vertical lines) and vertically to the middle of a row, during the drag and on release, and its date and row SHALL change through one command with an inverse.
5. WHEN a trigger is resized THEN it SHALL NOT be: every trigger has one size.
6. WHEN a trigger's Name is edited THEN it SHALL be editable in place through the shared inline editor and in the property grid, both through one command; the date SHALL NOT be part of the edited text.

### Requirement 3 - Influences from a trigger

**User Story:** As an author, I want to draw what a trigger set off, landing on the phase of the trend it affected.

#### Acceptance Criteria

1. WHEN an influence is drawn from a trigger THEN it SHALL be the same cubic bezier with an arrowhead at the target as between trends, leaving the trigger's circle at its edge and attaching to the target trend along a phase's top or bottom edge exactly as gartner-hype-cycle-graph Requirement 6 describes.
2. WHEN an influence is being drawn THEN a trigger SHALL NOT be offered or highlighted as a target, whether the source is a trend or a trigger (Q3; default a trigger is only a source). The backend SHALL refuse such an influence however it arrives.
3. WHEN a trigger already influences a trend THEN a second influence from that trigger to that trend SHALL NOT be offered, as between trends.
4. WHEN the target phase of an influence from a trigger becomes hidden THEN the influence SHALL be hidden and kept, as gartner-hype-cycle-graph Requirement 7.2 describes, and SHALL still count towards criterion 3.
5. WHEN a trigger is deleted THEN its influences SHALL be deleted with it in one command, and undo SHALL restore them.
6. WHEN an influence from a trigger is selected THEN its target end SHALL be movable along the trend's edge as any influence's is; its source end SHALL stay on the trigger.

### Requirement 4 - The note

**User Story:** As an author, I want to write a remark on the graph where it applies, and have it read as a remark, not as a trend.

#### Acceptance Criteria

1. WHEN a note is drawn THEN it SHALL be a box with its text **word-wrapped** inside it, breaking at spaces and at the author's own line breaks; text that does not fit SHALL end in an ellipsis with the whole text as the note's tooltip.
2. WHEN a note is edited THEN its text SHALL be edited **in place** in the shared multi-line inline editor, opened by the rename shortcut or by activating the note, committing as one command; the property grid SHALL offer the same text.
3. WHEN a note is dragged THEN it SHALL move through a command with an inverse, snapping as a trend does, and WHEN either border is dragged THEN it SHALL resize in both directions through a command with an inverse.
4. WHEN a note is drawn THEN it SHALL have no anchors and take part in no relation: no influence starts or ends at a note.
5. WHEN a note is drawn THEN its fill and text colours SHALL be theme tokens defined in both modes, checked by `theme.contrast.test.ts`, never literals in the module's stylesheet.

### Requirement 5 - Tags and filtering

**User Story:** As a reader of a large graph, I want the tag filter to narrow triggers as it narrows trends.

#### Acceptance Criteria

1. WHEN a trigger is selected THEN the property grid SHALL let the user add and remove Tags, as for a trend, through a command with an inverse.
2. WHEN a tag filter is applied THEN triggers not matching it, and every influence touching them, SHALL be hidden exactly as trends are, and their tags SHALL be offered among the filter's suggestions.
3. WHEN a tag filter is applied THEN notes SHALL stay visible (Q4; default notes do not take part).

### Requirement 6 - Compact mode

**User Story:** As a reader, I want triggers and notes in the compact view, in time order with the trends around them.

#### Acceptance Criteria

1. WHEN compact mode is on THEN each trigger SHALL be placed by the compact placement in time order among the trends by its date, on its own row, keeping its own size, and never overlapping another element on its row, with the properties ghg-compact-mode Requirement 3 states: time order kept, no overlap on a row, leftmost position, determinism.
2. WHEN compact mode is on THEN a trigger SHALL NOT be draggable, and influences SHALL still be drawable from it, as ghg-compact-mode Requirements 4 and 5 say for trends.
3. WHEN compact mode is on THEN each note SHALL be placed by the same placement in time order by its left edge's date, keeping its own size and the rows it covers, and SHALL NOT be draggable or resizable (Q5; default placed).
4. WHEN a trigger or note is dropped from the toolbox in compact mode THEN its date SHALL be approximated by running the compact placement backwards, as ghg-compact-mode Requirement 6 says for a trend, using the same function.
5. WHEN compact mode is switched on or off THEN no trigger or note SHALL change in the document.

### Requirement 7 - Toolbox, commands and properties

**User Story:** As an author, I want to add triggers and notes from the toolbox, edit them in the property grid, and undo anything I do.

#### Acceptance Criteria

1. WHEN the toolbox is shown for this diagram THEN it SHALL offer a Trigger and a Note beside the Trend, described by the backend as data. Dropping a Trigger SHALL create one at the drop position, snapped as in Requirement 2.4; dropping a Note SHALL create an empty note of a default size there, with its inline editor open.
2. WHEN a trigger is selected THEN the property grid SHALL offer its Name, Date, Tags and Description, and WHEN a note is selected THEN its Text; each edit SHALL be one command with an inverse, and the Description SHALL never be drawn on the canvas.
3. WHEN a user edits triggers or notes THEN every change SHALL be a command with an inverse offered through the context-action provider: add, delete, move, rename or edit text, resize a note, change a trigger's date, tags and Description, and add and delete an influence from a trigger.
4. WHEN the backend refuses a command THEN the user SHALL see the refusal's sentence, and the document SHALL be unchanged.
5. WHEN the diagram is read-only THEN no command that edits a trigger or note SHALL be offered; tooltips, filtering and compact mode SHALL still work.
6. WHEN triggers and notes are selected THEN selection, multi-select, the drop hold and delete SHALL be the library's, unchanged, as for trends.

### Requirement 8 - The library work this needs

**User Story:** As a maintainer of the shared library, I want each capability added once, for every module.

#### Acceptance Criteria

1. WHEN a filter declaration names the **element types it applies to** THEN elements of other types SHALL never be hidden by it, and a declaration naming none SHALL behave as today.
2. WHEN the row-packed layout's width is declared **per element type** THEN elements of a type without one SHALL keep their own size and SHALL still be placed in time order and without overlap on every row they cover.
3. WHEN the design finds that a connection between an edge-anchored source and an along-anchored target, or the per-pair and hidden-attachment checks with one end unattached, do not work THEN the library SHALL be made to support them, named for their geometry.
4. WHEN any of these is added THEN it SHALL be proven by a library test seen to fail before the change, and no module name SHALL appear in its declaration.

### Requirement 9 - The module declares it, and nothing else

**User Story:** As a maintainer, I want triggers and notes in the hype cycle module to be declarations, as the rest of it is.

#### Acceptance Criteria

1. WHEN triggers and notes are added to the module THEN its client SHALL change only its `DiagramDefinition`, its model mapping and its event handlers, and SHALL contain no rendering, gesture, selection, placement or label code.
2. WHEN the module is complete THEN every guard that walks module clients SHALL pass against it unchanged.

### Requirement 10 - The examples

**User Story:** As a reader, I want the shipped examples to show real triggers, so that the value of the element is plain.

#### Acceptance Criteria

1. WHEN the work completes THEN each of the nine shipped examples under `src/examples/diagrams/gartner-hypecycle-graph/`, the six at `91d8de03` and the three PR #92 added (electric vehicles, internet evolution and warfare in Ukraine), SHALL contain real, historically dated triggers (inventions, political moments, natural disasters and other moments) with influences to the trends they set off.
2. WHEN an existing trend is logically a moment rather than a span, such as a scientific breakthrough or a single event, THEN it SHALL be rewritten as a trigger, keeping its influences, with an outgoing influence turned into one from the trigger and an incoming one either dropped or noted in the readme, since a trigger cannot be influenced.
3. WHEN an example is changed THEN its layout SHALL be revised so that it still reads well: related elements near one another, no trigger label overlapping a trend or another label, and no row left needlessly empty; checked in a real browser in true-time and in compact mode.
4. WHEN an example is changed THEN the validator SHALL report nothing for it, its tests SHALL be updated to the new counts, and its readme SHALL say which trends became triggers and that the dates are illustrative, not a historical claim.
5. WHEN the examples are written THEN at least one SHALL contain a note.

### Requirement 11 - Documentation and the browser pass

**User Story:** As the user, I want triggers and notes seen working where they are seen, and documented where authors and readers look.

#### Acceptance Criteria

1. WHEN the work completes THEN each library capability of Requirement 8 SHALL be documented where module authors are pointed for the definition, and `docs/diagrams.md`'s row for `gartner/hypecycle-graph` SHALL mention triggers and notes and name this specification.
2. WHEN the implementation is merged THEN the *Gartner HypeCycle Graph* entry in the Notion Diagrams database SHALL have its implementation details, examples and purpose updated to describe triggers and notes.
3. WHEN the work completes THEN a `tests.md` entry SHALL cover, in a real browser and in both themes: the trigger's circle at half a trend's height, its label left of it with the date, and its tooltip; snapping a dragged trigger to the step lines and rows; drawing an influence from a trigger to each phase and being refused one into a trigger; the influence hiding with its phase; a note's wrapping, in-place multi-line editing, moving and resizing; triggers hidden by the filter and notes kept; triggers and notes in compact mode, and a trigger dropped in compact mode between two trends; and dropping each from the toolbox.
4. THEN jsdom SHALL NOT be taken as evidence of any of these, since it applies no CSS and lays out no text.
5. WHEN the tasks document is written THEN every acceptance criterion here SHALL be claimed by a task, established by diffing the two sets before the tasks card is raised and again after implementing.

## Non-Functional Requirements

### Code Architecture and Modularity

- The filter scope and per-type compact width live in the shared library (Requirement 8); the module declares triggers and notes (Requirement 9).

### Performance

- The largest shipped example, with its triggers, SHALL pan, zoom, filter and switch to compact mode without a perceptible pause.

### Reliability

- A document with triggers and notes opens in the build before this one without losing them (Requirement 1.6), and a document without them is written exactly as today (Requirement 1.5).

### Usability

- A trigger is recognisable without a legend: a circle, the word *Trigger* on hover, and a date beside its name. A forbidden influence into a trigger is refused before release, not after.

## Sources

- The user's two requests in the project chat, 2026-09-27.
- [gartner-hype-cycle-graph requirements](../gartner-hype-cycle-graph/requirements.md), Requirements 2, 5, 6, 7, 8 and 11; [ghg-compact-mode requirements](../ghg-compact-mode/requirements.md) and design, Requirements 3 to 7 and *The row-packed layout*.
- At `develop` `91d8de03`: `src/client/src/canvas/library/definition/diagramDefinition.ts` (`BUILT_IN_SHAPES`, `LabelDeclaration`, `AnchorSet`, `SnapAxis`, `FilterDeclaration`), `DiagramCanvas.tsx` (`tagsByElement`), `src/diagrams/gartner-hypecycle-graph/client/GhgCanvas.tsx`, `src/diagrams/functional-decomposition-graph/client/FdgCanvas.tsx` (`COMMENT_TYPE`), `src/diagrams/timeline/client/TimelineCanvas.tsx`, and the six examples under `src/examples/diagrams/gartner-hypecycle-graph/`.
