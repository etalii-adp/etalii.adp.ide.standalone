# Requirements Document

## Introduction

The Gartner Hype Cycle Graph places socio-technological **Trends** on a shared time axis, shows how far each has travelled through the phases of the Gartner hype cycle, and draws the **Influences** trends have had on one another. Its purpose is insight into how trends of any kind relate: which earlier trend made a later one possible, which one pulled another out of its trough, and where a cluster of mutually reinforcing trends sits in time.

**Why this is worth a diagram of its own.** The classic hype cycle chart shows one curve and plots each technology as a dot on it, which says where a technology stands today and nothing about how it got there or what moved it. Plotting trends against real time, each spanning the years its phases took, and connecting them with influences turns the chart into a causal history. A reader can follow electricity into telegraphy, telegraphy into telephony, telephony into mobile phones, and mobile phones into the smartphone economy, and see that each influence lands on a specific phase: the steam engine influenced the railways during its plateau, when it had become dependable, not during its peak. That is the value-add: the diagram makes visible *when in its life* a trend influenced another, which neither a timeline nor a plain dependency graph can say. It applies equally to trends that are not technological at all, such as social movements, regulation, or market shifts.

The description this specification follows is the *Gartner HypeCycle Graph* entry in the Notion **Diagrams** database (origin `gartner/hypecycle-graph`, rarity *Unique to ADP*, state 💡 Identified), read on 2026-09-26. Theory: [Gartner hype cycle (Wikipedia)](https://en.wikipedia.org/wiki/Gartner_hype_cycle). The description's points are carried below as requirements; where it left a choice open, the choice and the default taken are listed under *Open questions*.

## Terms

| Term | Meaning |
| --- | --- |
| **Trend** | The one element type. It has a Name, a start date, a stop date, a number of visible phases, Tags, and a Description. |
| **Phase** | One of the four segments of a Trend, named for the eras of the Gartner hype cycle: **Peak** (Peak of Inflated Expectations), **Trough** (Trough of Disillusionment), **Slope** (Slope of Enlightenment) and **Plateau** (Plateau of Productivity). Gartner's first era, the Innovation Trigger, is not a phase here: the description names four, and the trend's start date is its trigger. |
| **Influence** | A directed relation from one Trend to another, attached at each end to one phase of that trend. |
| **Tag** | A free-text label on a Trend. A Trend can carry any number of tags. |

## Open questions

The Notion description leaves these open. Each is put to the user as a selection; the default marked **(default)** is what this document is written against until the user rules otherwise, and a ruling that differs is a small amendment to the criteria that name it.

| # | Question | Options | Criteria affected |
| --- | --- | --- | --- |
| Q1 | The colour list names Trough twice (Light Gray and Orange) and Plateau not at all. Which colour does each phase take? | **(default) By position:** Peak yellow, Trough light gray, Slope orange, Plateau lime green. · **By name, Plateau added:** Peak yellow, Trough orange, Slope lime green, Plateau light gray. · Other. | 4.5 |
| Q2 | How is a trend's time span divided between its visible phases? | **(default) Equal shares:** the visible phases split the span from start to stop date equally, so one date pair is all a trend stores. · **Dated phases:** each phase has its own end date, dragged separately. · Other. | 3.3, 4.3 |
| Q3 | "Each trend can only influence each other trend once": does that allow both A→B and B→A? | **(default) One influence per pair, either direction:** once A influences B, B cannot influence A. · **One per direction:** A→B and B→A may both exist. · Other. | 6.4 |
| Q4 | What does a tag filter do to trends that do not match? | **(default) Hidden,** with every influence touching them. · **Dimmed,** still visible but faded. · Other. | 8.3 |
| Q5 | File extension. | **(default) `.hcg`** (hype cycle graph). · `.hype`. · Other. | 2.1 |

## What was measured

The shared canvas library and the `generic/timeline` module, read at `develop` `78b9cd7` on 2026-09-26, against each capability this diagram needs. The timeline is the nearest existing diagram: elements spanning a begin and an end date on snapped rows, bezier connections, a time ruler at the bottom of the view.

| Capability | State | Where |
| --- | --- | --- |
| Width-only resize by dragging the left or right border, no vertical resize | **Exists** | `sizing: "user"` with the default `resize: "width"` (`diagramDefinition.ts`, `DiagramCanvas.tsx`); the timeline's `period` type uses it. |
| Snap per axis to a fixed step | **Exists** | `SnapDeclaration`, leading edge on `origin + k * step`, applied during the drag and on the drop. The timeline snaps rows to `ROW_HEIGHT` 60 (36 high plus a 24 gutter) and dates to whole days. |
| Snap to calendar months | **Missing** | A snap step is a fixed number; months are not all the same length. Resize does not snap at all. |
| A time axis pinned to the bottom of the view, following horizontal pan and zoom | **Exists in the timeline module only** | `TimelineRuler.tsx` and `timelineTicks.ts`. The library declares `chrome.rulers` (`definition/chrome.ts`) with month, quarter and year rungs, **and nothing renders it**. |
| A right-pointing arrow banner with chevrons inside | **Missing** | Not in `BUILT_IN_SHAPES`; the nearest is `diode`. |
| An element split into segments, each with its own fill and its own tooltip | **Missing** | Labels have slots and columns, text only. Tooltips exist per element, per label and per decoration, not per region of a shape. |
| A label outside the element, to its left, right-aligned and vertically centred | **Missing** | `beside` places a label on the right only; the `offset` path forces `anchor: "middle"`. |
| Invisible anchors | **Declared, unread** | `AnchorEnablement.visible` exists and nothing reads it. |
| Anchoring anywhere along an edge, stored as a fraction per connection | **Missing** | Side anchors are a fixed declared set (`kind: "sides"`, `at: 0..1`); a connection stores named anchor strings only. |
| Anchors belonging to a region of an element | **Missing** | |
| At most one connection between a pair of elements | **Missing** | `maxFromSource` and `maxIntoTarget` count per relation type and end, not per pair. |
| A connection hidden while kept in the document | **Missing** | `RelationTypeDefinition` has no `when`; a connection has no visibility. |
| Tags and filtering by and/or tag expressions | **Missing** | Nothing in the library. C4 has tags in its own backend model only. |
| A slider in the property grid | **Missing** | `ContextPropertyEditor` offers LINE, TEXT, TOGGLE and CHOICE. CHOICE, with four values, is an enum editor. |
| Diagram colour tokens in both themes, contrast-tested | **Exists** | `--color-diagram-*` in `client/src/index.css`, checked by `theme.contrast.test.ts`. |
| Cubic bezier connection with an arrowhead at the target | **Exists** | `route: "cubic-bezier"`, `endMarker: "arrow"`. |

So this diagram needs more new library capability than any diagram before it. Requirement 10 lists it, and holds it to the rule that the library gains it for every module, never this one alone.

## Alignment with Product Vision

The diagram is the first in the catalog aimed squarely at **(constructive) technology assessment**, one of the purposes ADP is being built for: it makes the relationships between trends explicit, authored and diffable rather than implicit in a slide. It follows `product.md`'s *Don't reinvent, integrate* and `structure.md`'s rule that a new diagram type adds declarations on the shared library and no core code, which is why the library gaps above are library work and not module code.

## Requirements

### Requirement 1 — A declarative module

**User Story:** As a maintainer, I want the hype cycle graph built from declarations on the shared library, so that it selects, drags, connects and undoes exactly like every other diagram.

#### Acceptance Criteria

1. WHEN the module's client is written THEN it SHALL consist of a registration, a stream hook, a `DiagramDefinition`, and event handlers that answer the library's events through the module's own transport. It SHALL contain no rendering, gesture, selection or label code.
2. IF a behaviour this document asks for cannot be declared THEN it SHALL be added to the shared library under Requirement 10, and SHALL NOT be written in the module.
3. WHEN the module is complete THEN every guard that walks module clients SHALL pass against it unchanged.

### Requirement 2 — The document

**User Story:** As an author, I want my graph stored in a readable text file that diffs well and survives being edited outside ADP.

#### Acceptance Criteria

1. WHEN a graph is saved THEN it SHALL be written to a body file with the extension `.hcg` (Q5), registered by an `.adp` file whose origin line is `gartner/hypecycle-graph`. The body SHALL be ADP's own YAML schema in the style of the timeline's `.tml`: a version header, then a `trends` list and an `influences` list. No established format carries trends, phases and phase-anchored influences, so ADP owns the schema, as it does for `.tml`, `.dgr` and `.fdg`.
2. WHEN a trend is stored THEN it SHALL carry an id, its Name, a start and a stop date as ISO dates at month precision (`2007-06`), its vertical position, the number of visible phases (1 to 4), its Tags, and its Description.
3. WHEN an influence is stored THEN it SHALL carry an id, its source and target trend, and at each end the phase it is attached to, the edge (top or bottom) and the position along that phase as a fraction from 0 to 1 (Requirement 6.3).
4. WHEN a document is read THEN the parser SHALL never throw. What it does not understand SHALL be passed over and survive, and a document breaking a rule of this specification (a second influence between one pair, a stop date before a start date, a phase count outside 1 to 4) SHALL still open, with each breach reported by the validator to the Errors and Warnings panel.
5. WHEN ADP did not change a document THEN it SHALL be written back byte-identical, and WHEN a trend or influence changes THEN only the lines that change SHALL be rewritten.
6. WHEN the type is registered THEN its `IDiagramDocumentFactory` SHALL produce an empty, valid document.

### Requirement 3 — Time runs left to right

**User Story:** As an author, I want the horizontal position and width of a trend to mean its dates, so that the picture is a true history.

#### Acceptance Criteria

1. WHEN a trend is drawn THEN its left edge SHALL be at its start date and its right edge at its stop date on the diagram's time scale. Positions SHALL be the author's, never computed.
2. WHEN a trend is dragged horizontally or its left or right border is dragged THEN its start and stop dates SHALL change through a command, and both SHALL snap to the start of a calendar month, during the gesture and on release. A trend SHALL be at least one month long.
3. WHEN a trend's span changes THEN its visible phases SHALL keep dividing the span as Q2 rules (default: in equal shares).
4. WHEN a trend is resized THEN it SHALL NOT be resizable vertically: every trend has one shared height.

### Requirement 4 — The trend's shape

**User Story:** As a reader, I want each trend to read at a glance as something moving forward through the hype cycle, with the phase it has reached visible.

#### Acceptance Criteria

1. WHEN a trend is drawn THEN it SHALL be a right-pointing arrow banner (a pentagon arrow, or "home plate"): a rectangle whose right end is a point.
2. WHEN a trend with all four phases is drawn THEN three right-pointing chevrons inside the banner SHALL divide it into four segments, one per phase, left to right Peak, Trough, Slope and Plateau.
3. WHEN a trend shows fewer than four phases THEN only those phases SHALL be drawn, always the earliest ones, and the banner SHALL end in its point after the last visible phase: a trend is a Peak; a Peak and Trough; a Peak, Trough and Slope; or all four. A trend with N visible phases has N - 1 chevrons.
4. WHEN the pointer rests on a segment THEN a tooltip SHALL name the phase in full as Gartner does: *Peak of Inflated Expectations*, *Trough of Disillusionment*, *Slope of Enlightenment*, *Plateau of Productivity*.
5. WHEN a segment is filled THEN its colour SHALL be a bland (muted) variant of the phase's colour as Q1 rules (default: Peak yellow, Trough light gray, Slope orange, Plateau lime green), each a theme token defined in both modes, `--color-diagram-hype-peak`, `--color-diagram-hype-trough`, `--color-diagram-hype-slope` and `--color-diagram-hype-plateau`, never a literal in the module's stylesheet. Where text or a chevron is drawn on a fill, the contrast SHALL be checked by `theme.contrast.test.ts` in both modes.

### Requirement 5 — The trend's name and vertical placement

**User Story:** As an author, I want names to line up beside their trends, and trends to settle into tidy rows.

#### Acceptance Criteria

1. WHEN a trend is drawn THEN its Name SHALL be the only label, drawn **outside the banner on its left**, vertically centred on the banner and right-aligned, so that every name ends the same distance from its trend's left edge.
2. WHEN a trend's Name is edited THEN it SHALL be editable in place through the shared inline editor and in the property grid, both through one command.
3. WHEN a trend is dragged vertically THEN its vertical middle SHALL snap to rows spaced by the trend's height plus a margin, so that trends on adjacent rows never touch and their influences have room to curve between them.

### Requirement 6 — Influences

**User Story:** As an author, I want to draw how one trend influenced another, and say during which phase of each it happened.

#### Acceptance Criteria

1. WHEN an influence is drawn THEN it SHALL be a cubic bezier from the source trend to the target trend, with an arrowhead at the target end only.
2. WHEN an influence is started or ended THEN it SHALL attach only to the **top or bottom edge** of a trend, never its left end or its point, and SHALL attach to one visible phase of that trend: the phase whose part of the edge the pointer is over.
3. WHEN an influence is attached THEN it MAY attach **anywhere along that phase's top or bottom edge**, not at a fixed set of anchor points, and its position SHALL be stored as a fraction of that phase's span. WHEN the trend's span changes THEN the attachment point SHALL keep its fraction, so it moves proportionally with the phase.
4. WHEN a trend already influences another THEN a second influence between the two SHALL NOT be offered or highlighted while drawing (Q3; default: in either direction). A trend SHALL NOT influence itself.
5. WHEN anchors are drawn THEN they SHALL be invisible. The edge under the pointer SHALL still show where an influence would attach while one is being drawn, through the library's existing valid-target highlight, so the gesture is not blind.
6. WHEN an influence is created, moved to another attachment point or removed THEN each SHALL be a command with an inverse.

### Requirement 7 — Phases are toggled incrementally

**User Story:** As an author, I want to say how far a trend has got through the hype cycle with a single control.

#### Acceptance Criteria

1. WHEN a trend is selected THEN the property grid SHALL offer **Phases** as a slider with four stops, Peak; Peak and Trough; Peak, Trough and Slope; and all four. A value outside that sequence, such as a Trough without a Peak, SHALL NOT be expressible.
2. WHEN the number of visible phases changes THEN influences attached to a phase that becomes hidden SHALL be hidden, and SHALL reappear when the phase is shown again. **Hiding is visual only:** a hidden influence SHALL be kept in the document unchanged and persisted exactly as when it was visible.
3. WHEN a hidden influence exists between two trends THEN it SHALL still count towards Requirement 6.4, so a phase being hidden never allows a second influence between the same pair.
4. WHEN the phase count changes THEN it SHALL be one command with an inverse.

### Requirement 8 — Tags and filtering

**User Story:** As a reader of a large graph, I want to narrow it to the trends that matter to my question.

#### Acceptance Criteria

1. WHEN a trend is selected THEN the property grid SHALL let the user add and remove Tags. A trend MAY carry any number of tags; changing them SHALL be a command with an inverse.
2. WHEN a diagram is open THEN the user SHALL be able to filter it by a tag expression composing tags with **and** and **or**, with parentheses for grouping, such as `energy and (transport or industry)`. An empty filter shows everything.
3. WHEN a filter is applied THEN trends not matching it, and every influence touching them, SHALL be hidden (Q4; default hidden rather than dimmed). The filter SHALL be a view setting and SHALL NOT change the document.
4. WHEN a filter expression cannot be parsed THEN the diagram SHALL stay as it was and the user SHALL be told what is wrong with the expression.

### Requirement 9 — The time axis

**User Story:** As a reader, I want to read the dates off the diagram wherever I have scrolled.

#### Acceptance Criteria

1. WHEN the diagram is shown THEN a horizontal time axis SHALL be drawn along the **bottom of the view**, anchored in screen coordinates, so it stays visible while scrolling vertically.
2. WHEN the diagram is scrolled or zoomed horizontally THEN the axis SHALL follow, so a tick's date always lines up with the same date on the canvas.
3. WHEN the axis is labelled THEN its ticks SHALL step between months, quarters, years and decades by zoom level, so a 150-year graph and a two-year one both read cleanly.

### Requirement 10 — The library work this diagram needs

**User Story:** As a maintainer of the shared library, I want each capability this diagram needs added once, for every module, rather than hand-rolled in one.

#### Acceptance Criteria

1. WHEN the library gains the shape THEN `BUILT_IN_SHAPES` SHALL include a **pentagon arrow** that can be divided by internal chevrons into declared **segments**, each with its own fill and its own tooltip, and each exposing its own stretch of the top and bottom edges as an anchor region. Shape and segments SHALL be named for their geometry, never for this diagram.
2. WHEN a label declares a placement **left of the element**, right-aligned and vertically centred THEN the library SHALL draw it there, beside the existing right-hand `beside`.
3. WHEN an element type declares **continuous edge anchoring** THEN a connection SHALL be attachable anywhere along the declared edge or region and SHALL store its position as a fraction, recomputed from the element's bounds as they change.
4. WHEN a relation type declares **one connection per pair** (ordered or unordered) THEN the connect gesture SHALL refuse and not highlight a target that already has one with the source, composing with the existing element-type, cardinality and cycle checks as an independent check.
5. WHEN a relation type declares a **visibility condition** THEN a connection whose condition is false SHALL NOT be drawn, and SHALL be kept in the model and the document untouched.
6. WHEN a snap declaration names a **calendar step** (month) against a declared time scale THEN dragging and resizing SHALL snap to it; resizing SHALL snap as dragging does.
7. WHEN a definition declares `chrome.rulers` THEN the library SHALL **render** the ruler pinned to the bottom of the view, following pan and zoom, so the declaration that exists today stops being declared and unread. WHEN it does THEN the timeline module SHALL move onto it, or the design SHALL say why not.
8. WHEN the canvas is given a **filter** over element properties THEN it SHALL hide the elements that do not match and every connection touching them, without changing the model.
9. WHEN the property grid is given a **slider** editor over an ordered set of values THEN it SHALL render one, added to `ContextPropertyEditor` in the contract, with unknown editors still shown read-only as today.
10. WHEN `AnchorEnablement.visible` is set to false THEN anchors SHALL NOT be drawn, so the field stops being declared and unread.
11. WHEN any of these is added THEN it SHALL be proven by a library test that was seen to fail before the change, and no module name SHALL appear in its declaration.

### Requirement 11 — Toolbox and commands

**User Story:** As an author, I want to build a graph from the toolbox and undo anything I do.

#### Acceptance Criteria

1. WHEN the toolbox is shown for this diagram THEN it SHALL offer a Trend, described by the backend as data, and dropping it SHALL create a trend at the drop position, snapped as in Requirements 3.2 and 5.3, one year long with all four phases.
2. WHEN a user edits a graph THEN every change SHALL be a command with an inverse, offered through the context-action provider: add and delete a trend (deleting its influences with it, restored by undo), move it, change its span, rename it, change its phases, change its tags and Description, and add, move and delete an influence.
3. WHEN the backend refuses a command THEN the user SHALL see the refusal's sentence, and the document SHALL be unchanged.
4. WHEN the diagram is read-only THEN no command that edits it SHALL be offered; filtering SHALL still be available.

### Requirement 12 — Properties

**User Story:** As an author, I want to record what a trend or influence means without cluttering the picture.

#### Acceptance Criteria

1. WHEN a trend is selected THEN the property grid SHALL offer its Name, start and stop dates, Phases (Requirement 7.1), Tags (Requirement 8.1) and a Description.
2. WHEN an influence is selected THEN the property grid SHALL offer a Description, and SHALL show the source and target trend and phase read-only.
3. WHEN a Description is set THEN it SHALL NEVER be drawn on the canvas.

### Requirement 13 — Inherited behaviour stays inherited

**User Story:** As a user moving between diagrams, I want this one to behave as the others do.

#### Acceptance Criteria

1. WHEN trends and influences are selected THEN selection, multi-select, the drop hold, inline rename and tangent-following arrowheads SHALL be the library's, unchanged. This specification SHALL NOT define what "selected" looks like.

### Requirement 14 — The example

**User Story:** As a reader, I want an example large enough to show what the diagram is for.

#### Acceptance Criteria

1. WHEN the example is provided THEN it SHALL be a hand-authored graph of large technological and societal trends of roughly the past 150 years (among them the Industrial Revolution, steam engines, electricity, railways, the telephone, radio, the automobile, aviation, computing, the Internet, mobile phones and AI), expansive, **up to about 200 trends**, most of them related to one another by influences.
2. WHEN the trends are placed THEN their dates SHALL be historically plausible, and their vertical placement SHALL keep closely related trends visually close, so clusters read as clusters.
3. WHEN the example is written THEN it SHALL exercise every phase count from 1 to 4, influences attached to each phase on both top and bottom edges, at least one influence hidden by a trend's phase count, tags that make at least three meaningful filters, and Descriptions on trends and influences.
4. WHEN the example ships THEN its readme SHALL say that it is hand-authored because the notation is ADP's own and no published corpus exists, as the reason for exempting it from the published-data rule; that its dates and influences are illustrative, not a historical claim; and what it does not demonstrate. It SHALL be seeded into `src/examples/` like every other module's and open against the deployed catalog, as `ExampleRegistrationTests` checks, and the validator SHALL report nothing for it.
5. WHEN the example is opened THEN a reader SHALL be able to see from it how trends influence each other across time; the readme SHALL carry the purpose statement from this document's *Introduction*.

### Requirement 15 — Catalog and documentation

**User Story:** As a reader of the diagram catalog, I want to find this type and its state.

#### Acceptance Criteria

1. WHEN this specification is approved THEN `docs/diagrams.md`'s row for `gartner/hypecycle-graph` SHALL move from 💡 Identified to 📝 Specified, naming this specification, and SHALL move with the type's state thereafter; the Notion Diagrams entry SHALL be kept in step.
2. WHEN the module is implemented THEN any touch point it moves in `docs/creating-a-diagram-module.md` SHALL be updated in the same change, and each library capability of Requirement 10 SHALL be documented where module authors are pointed for the definition.
3. WHEN the tasks document is written THEN every acceptance criterion here SHALL be claimed by a task, established by diffing the two sets before the tasks card is raised.

### Requirement 16 — The browser confirms what jsdom cannot

**User Story:** As the user, I want the shape, colours and gestures checked where they are seen.

#### Acceptance Criteria

1. WHEN the work completes THEN a `tests.md` entry SHALL cover, in a real browser: the banner and chevrons at each phase count in both themes; the phase tooltips; the name to the left, right-aligned; month snapping on drag and resize and row snapping vertically; drawing an influence on each phase's top and bottom edge and seeing it keep its place when the trend is resized; refusal of a second influence between one pair; influences hiding and reappearing as phases are toggled, and still present in the saved file while hidden; tag filtering with and, or and parentheses; and the time axis staying at the bottom of the view while scrolling and zooming.
2. THEN jsdom SHALL NOT be taken as evidence of any of these, since it applies no CSS and lays out no text.

## Non-Functional Requirements

### Code Architecture and Modularity

- The module adds declarations, event handlers and validation, and nothing else (Requirement 1). Library additions are named for geometry and behaviour, never for this diagram (Requirement 10).

### Performance

- The example of about 200 trends and their influences SHALL pan, zoom and drag without visible lag on the machine the application is developed on, and applying a filter SHALL take effect without a perceptible pause.

### Reliability

- A document that breaks a rule still opens (Requirement 2.4); a document ADP did not change comes back byte-identical (Requirement 2.5); a hidden influence is never lost (Requirement 7.2).

### Usability

- The canvas refuses a forbidden influence before it is released, not after (Requirement 6.4). Phase names are available on hover, so the colours need not be memorised.

## Sources

- The *Gartner HypeCycle Graph* entry in the Notion Diagrams database, read on 2026-09-26: its properties and its *Implementation Details*, *Example to work with* and *Explain the purpose and the value-add* sections.
- [Gartner hype cycle (Wikipedia)](https://en.wikipedia.org/wiki/Gartner_hype_cycle), for the phase names.
- The shared canvas library and the timeline module at `develop` `78b9cd7`: `diagramDefinition.ts`, `DiagramCanvas.tsx`, `definition/chrome.ts`, `definition/labels.ts`, `api/diagramModel.ts`, `api/context-contract.proto`, `shell/panels/PropertyRow.tsx`, `index.css`, `TimelineCanvas.tsx`, `TimelineRuler.tsx`, `timelineTicks.ts`.
- `tech.md`, *Specifying a diagram type*; the `functional-decomposition-graph` specification for the shape of a declarative module's requirements.
