# Timeline diagram

[timeline.dis](timeline.dis) specifies ADP's **Timeline diagram** (`generic/timeline`) in DISL 0.3. The specification holds everything DISL 0.3 can state: the metamodel, the time axis and the row axis, the notation, the toolbox, the context menus, the forms with their checks, the constraints with the tool's own codes and sentences, the operations, the removal's confirmation and the persistence. Reading and comparing times is done by the CEL functions of the plugin `net.etalii.adp.generic.timelineTimes`, because DISL's CEL has no timestamps, and the row arrangement is the layout plugin `net.etalii.adp.generic.timelineRowArrange`. The wire ids are declared under `x-timeline`. This page holds the rest, so that an IDE implementing the tool from the specification ends up with the tool that exists today, and says, in [What DISL 0.3 cannot express](#what-disl-03-cannot-express), where the specification stops. Each aspect names the code or document that shows it.

The reference implementation is the standalone module [`src/diagrams/timeline/`](https://github.com/etalii-adp/etalii.adp.ide.standalone/tree/develop/src/diagrams/timeline) in etalii.adp.ide.standalone, which bundles this specification and derives its toolbox, context menus, property rows, findings, placement additions and removal confirmation from it (`backend/EtAlii.Adp.Diagram.Timeline/TimelineDefinition.cs`). Its original requirements live in that repository's history (`.spec-workflow/archive/specs/timeline-diagram/`, removed in `ece03c36`); requirement numbers below (R3.4 and so on) refer to that document. The Notion "Tools" row for the tool says: kind Diagram, focus areas *Planning and roadmapping* and *Technology assessment*, "Time is the axis that matters; a dedicated timeline tool snaps elements to rows and dates, which a generic canvas leaves to the eye." The project's conversations contain no further decisions about this tool, apart from the owner's ruling for this definition that the code wins: where the specification and the code disagreed, the specification was changed to say what the code does.

Paths below are relative to `src/diagrams/timeline/` in the standalone repository unless they start with `src/`.

## Contents

1. [How the specification maps the tool](#how-the-specification-maps-the-tool)
2. [The document on disk](#the-document-on-disk)
3. [Times and the time plugin](#times-and-the-time-plugin)
4. [Rows](#rows)
5. [The time axis](#the-time-axis)
6. [The time ruler](#the-time-ruler)
7. [Appearance](#appearance)
8. [Gestures](#gestures)
9. [Actions and keys](#actions-and-keys)
10. [Arrange diagram](#arrange-diagram)
11. [Properties](#properties)
12. [Diagnostics](#diagnostics)
13. [Wire ids](#wire-ids)
14. [What DISL 0.3 cannot express](#what-disl-03-cannot-express)
15. [Scale and performance](#scale-and-performance)
16. [Known gaps](#known-gaps)

## How the specification maps the tool

| Tool concept | In timeline.dis | Notes |
| --- | --- | --- |
| Element with an end (a period) | node type `Period` | Wire type `generic/timeline+period`. Labelled "Element" in the toolbox. |
| Element without an end (a moment) | node type `Moment` | Wire type `generic/timeline+moment`. |
| Both | abstract `Element` | Holds `label`, `begin`, `row`. |
| Connection | relation `Connection` | Wire type `generic/timeline+connection`; the UI calls it a "relation" and its type label is "Relation". |
| x axis | time axis `time` | Seconds since the Unix epoch in the implementation. |
| y axis | linear axis `rows`, 60 canvas units per row | See *Rows* below. |

The split into `Period` and `Moment` is a modelling choice of this specification. In the file there is one kind of element, and whether it is a period or a moment is decided only by whether an `end` key is present, even an empty one (`backend/EtAlii.Adp.Diagram.Timeline/TimelineParser.cs`, `ReadElements`). Giving a moment an end, or removing a period's end, is a retype in DISL (`giveEnd`, `removeEnd`) and a single-key edit in the file.

**The model is built from the tool's own reader, not through FBL.** The standalone module builds the DISL model from what its YAML reader read (`backend/EtAlii.Adp.Diagram.Timeline/TimelineDisl.cs`), because the file is not a DID definition and a generic YAML reading types plain scalars (`1.0`, `null`, `yes`) while the timeline keeps every value as the text it was written with. Each element and relation is given a unique model id: the first declaration written with an id keeps it, and a later one, or one written without an id, gets an ephemeral id; the written id is kept beside the model, and findings name an element by it (DISL 11.5.4). A relation end resolves to the first **element** written with that id, never to a relation.

## The document on disk

DISL's `persistence` layer describes a DID definition. The timeline does not store DID: it stores its own YAML format, so the whole file format is documented here.

**Two files.** A timeline is `<name>.adp`, whose first line is the MIME-style origin `generic/timeline`, plus a sibling body `<name>.tml` (Timeline Markup Language) holding the timeline itself (R1.1). `.tml` is owned outright by this type: a bare `.tml` routes to it with no registration step, and the extension is not shared with any other tool (`backend/EtAlii.Adp.Diagram.Timeline/Diagram.cs`, R1.2).

**Why its own format.** The Gantt family (`mermaid/gantt`, PlantUML gantt) was considered and rejected: they compute their layout, express links only as scheduling dependencies and have nowhere to put an authored row. YAML is the serialization; schema and extension are ADP's own (R1.4).

**Layout of the file.**

```yaml
timeline: 1
elements:
  - id: discovery
    label: Discovery
    begin: 2025-04-27
    end: 2025-06-05
    row: 11
  - id: launch
    label: Launch
    begin: 2025-12-04
    row: 8
connections:
  - id: c1
    from: discovery
    to: launch
    label: informs
```

- `timeline: 1` is the schema marker. `elements` is a sequence of mappings with `id`, `label`, `begin`, optional `end` and `row`. `connections` is a sequence of mappings with `id`, `from`, `to` and optional `label`. Keys are read with those exact names; a missing `connections` key means no connections, and a missing key in an entry reads as the empty text.
- A new document is exactly `timeline: 1\r\nelements: []\r\n`: CRLF, a trailing newline, no title and no sample content (`backend/EtAlii.Adp.Diagram.Timeline/TimelineDocumentFactory.cs`, R1.3).
- An empty file, or one whose root is not a mapping, is an empty timeline, not an error (`TimelineParser.Parse`).

**Only the affected lines change (R2.1, R2.2).** Nothing ever serializes a model back to the file. Every write is a splice of the line range the parser recorded for that element or connection (`backend/EtAlii.Adp.Diagram.Timeline/TimelineWriter.cs`). Consequences an implementation must reproduce:

- A document opened and saved with no edit is byte-identical: line endings (LF or CRLF), indentation, key order, comments, blank lines, quoting style and a missing final newline all survive.
- Keys the tool does not model survive verbatim, at document, element and connection level, including nested sequences (R2.3; fixture `unmodelled-keys.tml`). DISL's `x-*` rule covers the specification, not user files.
- A new element is appended after the last existing one, with indentation copied from the file (only an empty document falls back to two spaces). Keys are written in the order `id`, `label`, `begin`, `end` (only for a period), `row`. A label is quoted only when it needs to be.
- A new connection is appended after the last existing one; when the file has no `connections:` key, the key is created at the end of the document together with the first entry, and undoing that insert removes the key again so the file returns byte for byte (`TimelineWriter.HasConnectionsSection`, `RemoveConnectionsSectionIfEmpty`). The `label` key is omitted when the label is empty, and clearing a connection's label removes the key.
- Removing a period's end removes the `end` line (R11.3).
- The fixture corpus in `backend/EtAlii.Adp.Diagram.Timeline.Tests/Fixtures/` (comments, CRLF/LF pair, no trailing newline, odd indentation, quoting, shared rows, unmodelled keys) is the test of all this; `*.tml -text` in `.gitattributes` keeps git from rewriting it.

**Ids.** Ids live in the file because the schema is ADP's own, and they are unique across elements and relations together. New ids are ShortGuids: a version 4 GUID written as 25 lowercase characters of base 36 (`src/backend/EtAlii.Adp/ShortGuid.cs`), for example `467lu0vgaiudevamnkpl6k052`, which `persistence.ids` states as `uuid-v4` with `encoding: "base36"`. Hand-written ids (`discovery`, `c1`) are equally valid. A command that creates an element generates its ids once, when the gesture happens, so a redo re-creates the element under the same ids.

**An unreadable file** (not YAML) opens in the unavailable state naming the parser's message and line, and no edit is accepted until it is fixed, so a broken file is never made worse (R2.4, `_Model/TimelineDocumentEntry.cs`). The store keeps the last good document across a reload that cannot read, and a deleted body closes rather than lingering (`TimelineDocumentReloader.cs`). Changes made to the file outside ADP are picked up and pushed to every open view.

## Times and the time plugin

`begin` and `end` are normally ISO 8601, either date-only (`2026-01-05`) or date-time without offset (`2026-01-05T14:30:00`). The implementation keeps the **text** as written and its **precision** (date or date-time), decided by whether the text contains a `T` (`_Model/TimelineInstant.cs`, `_Model/TimelinePrecision.cs`). timeline.dis declares both as `datetime` with `timezone: "preserve"`, and every value in the model is the text as written, also one that is not a time at all.

DISL's CEL has no timestamps, so the specification reads times through the CEL functions of its plugin `net.etalii.adp.generic.timelineTimes` (DISL 13.1.1). The standalone module implements them with the very calls its reader and writer use (`backend/EtAlii.Adp.Diagram.Timeline/TimelineTimes.cs`); a host without the plugin falls back to the declared fallbacks, which read ISO 8601 only.

| Function | What it does | Fallback |
| --- | --- | --- |
| `readableTime(text)` | Whether `text` is a time the tool can read: .NET's `DateTimeOffset.TryParse` under the invariant culture, surrounding whitespace ignored, a value without an offset taken as UTC. That reads more than ISO 8601: `2026/07/01` and `July 5, 2026` are readable, `2026-02-30` and the empty text are not. | An ISO 8601 date or date-time pattern. |
| `compareTimes(a, b)` | -1, 0 or 1 as the instant `a` names is before, at or after `b`'s, offsets applied. | Lexical comparison. |
| `dateAfter(time, days)` | The date `days` days after the instant `time` names, written `yyyy-MM-dd`, in UTC. | None. |
| `today()` | Today's date in UTC, `yyyy-MM-dd`; not deterministic. | None. |
| `writtenEnd(relation, end)` | The id a relation's `source` or `target` end is written with (`from`, `to`), also when it names no element and the end is null in the model. | The resolved end's id, or the empty text. |

- A value is read at face value and pinned to offset zero; it is never interpreted in the machine's local time zone (`TimelineInstants.cs`). A value written is the value drawn (R3.7).
- One element never mixes a date-only value with a date-time value (R3.2). The check is the tool's own: whether each text contains a `T`, so `Tuesday 7 July 2026` counts as a date-time. DISL's `std.mixedPrecision` compares written precisions, which a runtime does not know for a text kept as written, so the specification states the tool's test as the rule `mixedPrecision` and the form checks.
- A value the tool writes back after a drag or resize takes the precision of the value it replaces: `2026-01-05` stays `2026-01-05`, never `2026-01-05T00:00:00` (`TimelineScale.ToText`). Date-times are written as `yyyy-MM-ddTHH:mm:ss`, rounded to the whole second.
- Values created by the tool (toolbox drops, create-and-relate, add after or below, the menu's additions) are always date-only.

## Rows

A row is an integer the author owns: negative rows are valid, gaps are allowed, and several elements share a row (R3.5). It is a placement grid and **nothing more**: no identity, label, membership or meaning (R3.6). That is what keeps this a timeline rather than a swimlane, so an implementation must not add lane headers, lane labels or lane packing. DISL's ordinal axis would give rows identity, so timeline.dis uses a linear axis in row units with grid snapping of one row instead.

- A row is 60 canvas units high (`TimelineRows.Height`, mirrored as `ROW_HEIGHT` in `client/TimelineCanvas.tsx`). The height is a rendering constant, not data.
- A vertical drag snaps to the nearest row; halves round **away from zero**, so the midpoint between two rows resolves the same way on both sides of the origin (`src/backend/EtAlii.Adp.Documents/RowRounding.cs`, shared by the client's `snapToStep`). DISL's `nearest` does not say how halves round.
- A row that does not read as an integer is read as row 0.
- Overlapping elements are drawn overlapping; the tool never moves anything to avoid overlap (R4.5), except when the reader asks for [Arrange diagram](#arrange-diagram).

## The time axis

- The implementation's x is seconds since the Unix epoch, as a double (`TimelineScale.cs`); the time-to-coordinate conversion lives in exactly one function pair.
- The client freezes a seconds-to-canvas-units scale when the first non-empty model arrives: the span of all elements plus 10 % on each side, fitted to about 1200 units; for an empty timeline, one day is 20 units (`timelineScaleOf` in `client/TimelineCanvas.tsx`). It is frozen so an edit never rescales the drawing under the user. DISL's fixed `scale` of 20 units per day in timeline.dis is the empty-timeline value; the fitted initial zoom is this implementation's.
- The same document always draws identically (R4.7).

**x snapping depends on the element.** A date-only element's begin snaps to the start of a day when dragged, and its edges snap to days when resized. An element whose times carry a time of day does not snap in x at all and keeps whole seconds (`snap` in `TIMELINE_DEFINITION`, `TimelineScale.ToTime`). DISL cannot make snapping depend on a value's written precision, so timeline.dis declares day snapping, which is the case for every element the tool itself creates.

## The time ruler

The ruler is chrome fixed to the view, not to the diagram (R5). DISL's `ruler` declares a position and a size; the behaviour below is the tool's own (`client/TimelineRuler.tsx`, `client/timelineTicks.ts`, `client/timeline.css`):

- It sits along the **bottom** of the view, 24 px high, stays in sight whatever is scrolled vertically, and is visually subordinate: muted 10 px labels on a mostly transparent band, no pointer events.
- Its labels slide with the content, because their x comes from the same view transform as the elements.
- The label interval adapts to the visible span so labels neither crowd nor vanish: at least 80 px per label, choosing from 1 s, 15 s, 1 min, 5 min, 15 min, 1 h, 6 h, 1 day and 1 week, then calendar months, quarters and years (any whole number of years). Labels fall on round UTC boundaries, never on offsets from the viewport edge.
- Label text: `HH:mm:ss` below a minute, `HH:mm` below a day, `Mon d` for days and weeks (`Mon yyyy` on the first of a month), `Mon yyyy` for months and quarters, and the bare year on 1 January and for year ticks.
- It is measured against the surface's real width, so a label sits over the elements it dates.

## Appearance

- A period is a box 36 units high, its left edge at begin and its right edge at end, at least 2 units wide. It sits at the top of its row, leaving a 24-unit gutter to the next row. Its label is inside, on one line, truncated.
- A moment is a diamond with a radius of 9 units, drawn with its left tip at begin and its centre 18 units below the top of its row, so it lines up with the middle of the periods on the same row. Its label is beside it, to the right. It is filled with the accent colour and has no outline (`client/timeline.css`, the library's `moment` shape). timeline.dis expresses the vertical alignment as a placement offset of 0.3 row.
- An element with an empty label shows its id instead (the specification's function `shownLabel`).
- A connection leaves the **end** anchor (right-side midpoint) of its source and arrives at the **begin** anchor (left-side midpoint) of its target, always; anchors never move to the top, bottom or a corner (R8.1). It is a cubic bezier that leaves and arrives horizontally. When the target begins left of where the source ends, the curve loops forward out of the source and back into the target (`forwardBezierPath` versus `horizontalBezierPath` in `src/client/src/canvas/connectors.ts`); DISL's `bezier` routing has no such rule. It has no arrowhead. Its label sits at the midpoint, 6 units above the line.
- Connections are redrawn continuously while an element is dragged or resized, not only on release (R6.5, R7.7, R8.6).
- The shared canvas styling (`src/client/src/canvas/canvas.css`) supplies colours and selection states; the timeline adds only the moment's fill and the ruler.

## Gestures

- **Move.** A horizontal drag shifts begin and end by the same amount, keeping the duration; a vertical drag changes only the row; a drag with both does both, as **one** edit with one undo entry (R6.1 to R6.4). A moment never gains an end from a drag. While dragging, a hint above the element shows the time under its left edge and the row it would land on, as `yyyy-MM-ddTHH:mm:ss · row N`. `Escape` returns the element and records nothing (R6.6).
- **Resize.** A selected period shows adorners on both edges; the left one changes only begin, the right one only end. The moving edge stops at the other instead of crossing it, on the client and again on the server (R7.4, `SetTimelinePlacementCommandHandler`; the placement rule `noInversionByGesture`). A moment offers no right adorner (R7.5). A resize is sent as a property edit of begin or end, formatted in the element's own precision.
- **Relate.** Dragging from an element's **end** anchor to another element relates source to target. Dragging from its **begin** anchor runs the relation the other way: the element dropped on becomes the source, because what precedes an element points into it. The whole gesture travels as one stateless call (`TimelineRelationGesture.cs`), deliberately replacing a two-call protocol whose stale state once related the wrong pair. The context menu `for: ["connection"]` with `runSingle` is that call's one action, "Relate".
- **Create and relate.** Releasing a relation on empty canvas creates a new element there (begin at the start of the dropped day, row nearest the drop, 14 days long, labelled "New element") and relates it, in one command and one undo. From the begin anchor, the new element is the source and the existing one the target.
- **Self relations are refused** ("An element cannot be connected to itself.", the connect rule `noSelfRelation`); two relations between the same pair are allowed, because each carries its own label (R8.7, R8.8). A self relation written by hand is drawn and not reported.
- **Toolbox drop.** The palette has two items, *Element* and *Moment* (R9). A drop creates the element with its begin at the start of the day under the drop and its row nearest the drop; an element is 14 days long. It is labelled "New element" or "New moment". The drop and the menu run the same operation, `addElementHere` or `addMomentHere`, and the toolbox's `initial` values say the same.
- There is no connection tool in the palette; relations are made only by dragging from an anchor (`contextTools`).

## Actions and keys

Actions reach the ribbon, the right-click menu and the keyboard through one path (R11.2). The specification's `contextMenus` give them, in three groups for an element (the edits, the additions, Arrange) and two for a relation; the standalone module derives its menus from them (`TimelineContextActionProvider.cs`).

| On | Action | Key | Behaviour |
| --- | --- | --- | --- |
| Element | Rename… | F2 | Asks for the label. |
| Moment | Give it an end… | | Asks for the end, prefilled with the begin as written (titled "Give it an end", field "End", button "Set"); refuses an unreadable time and an end before the begin as typed. |
| Period | Remove its end | | Removes the `end` key; the period becomes a moment. |
| Element | Remove | Delete | Removes the element and every relation attached to it. With no relations it happens at once; otherwise it asks first, naming the count ("Removing this element also removes the 1 relation attached to it." / "…the N relations…"), as a danger confirmation titled "Remove" with the button "Remove" (R2.5). The specification's `deletion.Element.confirm` counts the relations with a threshold of 1, a relation from the element to itself counted once. |
| Element | Add element after | Tab | A new element 6 days after this one's end (or begin, for a moment; or today when neither reads), 14 days long, same row, related from this one. |
| Element | Add element below | Enter | A new element with this one's begin as a date (or today), one row down, 14 days long, related from this one. |
| Relation | Relabel… | F2 | Asks for the label; an empty label removes the key. |
| Relation | Remove relation | Delete | |
| Empty canvas | Add element here, Add moment here | | Placed at the clicked time and row. From a menu without a position, asks for the begin (prefilled with today, dialog titled "Add element" or "Add moment", field "Begin", button "Add") and uses the row of the element the gesture anchored on, or row 0. |
| Anywhere | Arrange diagram | | See [Arrange diagram](#arrange-diagram). |

An action that does not apply to the selection is reported as not applicable rather than failing silently (R11.8). Every edit is one command with an inverse on the project's history (R11.1).

## Arrange diagram

"Arrange diagram" puts every element on the row that keeps the diagram least cluttered, as one undoable step; only rows change, never a time (`TimelineArrangement.cs`, `ArrangeTimelineCommandHandler.cs`). It is offered on an element, a relation and empty canvas alike, which keeps it in the ribbon. It is unavailable while the timeline has no element, with the reason "There is nothing to arrange until this timeline has an element."; the menu shows that sentence beside the entry also while it is available, as the hand-written menu always did.

It is not DISL 0.3's standard `rows` algorithm (10.2), so the specification names the layout plugin `net.etalii.adp.generic.timelineRowArrange`:

- Extents are taken at the client's first fit: the timeline's span, at least one day, over 1000 of its 1200 units. A period covers its begin to its end, at least 2 units; a moment covers 9 units before its time to 15 units after it plus its shown label's width at 12 units.
- Elements without an id, without a readable begin, or with an id an earlier element has, take no part and keep their row.
- The shared row packing (`src/backend/EtAlii.Adp.Documents/RowPacking.cs`) puts elements onto as few rows as their extents allow, 12 units apart, drawing related elements to nearby rows.
- A moment and an element later than it that it is related to are kept on different rows, so their relation does not run through the moment's label. `rows` has no such rule.

## Properties

The property grid shows, for an element: **Label** (group Identity), **Begin** and **End** (group Timing; End only for a period) and **Row** (group Placement). For a relation: **Label**, and **From** and **To** read-only with the reason "Reconnecting is done on the canvas, by dragging the relation's end to another element.", showing the ids the relation is written with, also one that names no element (R10). The specification's `forms` give these rows; the standalone module derives them (`TimelineContextPropertyProvider.cs`).

- Begin and End are plain line editors validated on commit; there is no date picker yet (R10.6). A value that does not read as a time is refused ("'…' is not a time this timeline can read."), a value in the other precision than the element's other time is refused ("This element uses the other time form; begin and end must both be dates, or both carry a time."), and an end before the begin is refused ("An element cannot end before it begins."). A moment shows no empty End row: giving it an end is the menu's job.
- Row is a line editor too; a value that is not an integer is refused.
- Editing Begin moves only the begin; unlike a drag, it does not keep the duration.
- Every edit travels as a command onto the history and out to other views, never written by the panel directly (R10.5).

## Diagnostics

Problems reach the Errors and Warnings panel, not a canvas overlay (R12). All are **warnings**, and the rest of the diagram still draws; only an unparseable file is an **error**, reported as one problem with the same message the unavailable state shows. The specification's `constraints` state every one of them with the tool's own code and sentence, and the standalone module evaluates them (`TimelineRuleSet.cs`, `TimelineValidator.cs`, `TimelineDefinition.cs`).

| Code | When | In timeline.dis | Located at |
| --- | --- | --- | --- |
| `timeline.unparseable` | The file is not YAML (error): "This is not YAML that can be read: " and the parser's message. Nothing else is reported. | `std.unparseable`, its reason the parser's message. | The parser's line. |
| `timeline.missing-id` | An element or relation has no id, or an empty one: "'Label' has no id, so nothing can select, connect or edit it." (or "A relation has no id, …"). | Rule `missingId`. | Its line. |
| `timeline.duplicate-id` | A declaration reuses an id an earlier one holds: "The id 'x' is declared more than once, which makes every reference to it ambiguous." The first keeps it. | `std.duplicateId`. | Its line. |
| `timeline.unreadable-time` | A begin or an end does not read as a time, the empty text included: "'Label' begins at '…', which is not a time this timeline can read." (or "ends at"). | Rules `unreadableBegin`, `unreadableEnd`. | The element. |
| `timeline.end-before-begin` | End lies before begin; only a hand edit can cause it. | Rule `endBeforeBegin`. | The element. |
| `timeline.mixed-precision` | One element mixes a date and a date-time. | Rule `mixedPrecision`. | The element. |
| `timeline.dangling-connection` | A relation end names no element: "A relation names 'x', and no element with that id is on this timeline." An end written empty, or not at all, is not reported. | Rules `danglingSource`, `danglingTarget`. | The relation. |

An element is named in a finding by its label in quotes, or by its id in quotes when the label is empty (the specification's function `displayName`). `std.references`, `std.endpoints` and `std.required` are turned off, because the rules above report what the tool reports and nothing more.

**The order of the findings** is the tool's: first the ids (missing and duplicate, in document order, elements before relations), then the times per element (unreadable begin, unreadable end, end before begin, mixed precision), then the relations' ends per relation (source, then target). `constraints.order` cannot state a grouping, so the standalone module sorts what the evaluator reports. Its duplicate-id finding orders the holders by their line, where the tool orders elements before relations; the two differ only for a file whose `connections:` come before its `elements:` and that gives a relation an element's id.

Elements with problems stay on the canvas: an element whose begin cannot be read is drawn at the epoch as a moment, so it stays selectable, and a relation to a missing element is still sent so the canvas can show it (`TimelineElementMapper.cs`).

## Wire ids

The standalone client and backend share their own ids for the toolbox entries, the actions and the property rows. DISL 0.3 has no place for them, so the specification declares them under `x-timeline`, as `x-ghg` does for the hype cycle graph: a tool is keyed by its id, an action by its entry's operation or kind (with `"<Type>/<kind>"` overriding that for entries on that type, so `Connection/editLabel` is `timeline.relabel` and `Connection/delete` is `timeline.disconnect`), and a property by its form item's row id.

## What DISL 0.3 cannot express

- **The file format and its writing discipline.** `persistence.format: "yaml"` describes a DID definition; the `.tml` layout, the splice-only writes, the indentation copied from the file and the `connections:` key created with its first entry are described in [The document on disk](#the-document-on-disk).
- **Reading times.** CEL has no timestamps; the plugin functions above carry the tool's reading, and their fallbacks are an approximation.
- **Precision-dependent snapping.** The x snapping depends on whether an element's times carry a time of day; the specification declares the day snapping of every element the tool creates.
- **The connection route.** A forward loop when the target begins before the source ends is the tool's own route, and the named begin and end anchors at the side midpoints are fixed points of a rectangle and a diamond that the client draws itself.
- **The drag hint and the ruler's tick choice.** Both are client chrome described above.
- **Create and relate from the begin anchor.** The new element is the source there; DISL's `createTarget` covers only the end-anchor direction.
- **The row arrangement.** A layout plugin, described in [Arrange diagram](#arrange-diagram).
- **A menu addition without a position** asks for the begin and takes its row from the element the menu was opened on; DISL's operation reads `position` and has no fallback for a menu opened without one.
- **The grouping of findings.** Described in [Diagnostics](#diagnostics).
- **The wire ids**, under `x-timeline`.

The standalone module runs Add element after, Add element below, Give it an end, Remove its end, Relate and Remove in its own commands rather than through a DISL runtime's operation interpreter, because they create and connect in one step, retype, or splice lines the interpreter does not write; the operations in the specification say what those commands do.

## Scale and performance

- The client asks the backend only for what is in view; a period is culled on its whole span, not its begin, and a relation is sent only when both its ends are (`TimelineElementMapper.Visible`).
- Scrolling and zooming never round-trip to the backend (NFR performance).

## Known gaps

- Several requirements stated in the original specification are not visible in the code: choosing *Relate* from a menu and then clicking a target (R11.6) and adorners withheld in read-only mode (R7.8) were not verified here.
- There is no date editor in the property grid (R10.6).
- The only real `.tml` documents are the examples under `examples/`; the fixtures were written by the module's author, which the fixtures' own readme names as a weakness.
- Two defects of the standalone module were found while freezing its behaviour for this specification and are left as they are: a document that YamlDotNet rejects with an `InvalidOperationException` rather than a `YamlException` (for example `label: [unclosed` at the end of the file) is not reported as `timeline.unparseable` but fails the validation, and undoing a run of edits does not always return the file byte for byte: the `end:` line taken away by Remove its end, and in one fixture a removed relation, come back at a different place in the element or the list than they were.
