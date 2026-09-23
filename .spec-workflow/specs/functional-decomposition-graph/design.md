# Design Document

## Overview

The functional decomposition graph is a new diagram module at `src/diagrams/functional-decomposition-graph/`, built declaratively on the shared canvas library, over a line-oriented `.fdg` document the backend owns. It is built in two independent halves:

- **The library half** adds six capabilities, each named for geometry or behaviour and usable by any diagram: three shapes with their outlines, height resize, wrapped labels, a multiline inline editor, a declared cycle rule, and outline attachment for polygon shapes. The sixth was found by this design rather than by the requirements (see *The one thing the requirements did not list*).
- **The module half** is a definition, event handlers and a backend: parser, writer, store, session, validator, providers and commands, each following the shape `dependency-graph` already has.

The library half depends on nothing on the board and is built now. **The module half is built after the shared backend and client pieces it would otherwise copy have landed**, by the user's ruling: see *Sequencing with the two centralization specifications*.

Every name in this document that does not exist yet is a proposal, marked as new where it is introduced.

## Steering Document Alignment

### Technical Standards (tech.md)

- **Specifying a diagram type** asks for four aspects - the document, the model, the view and the editing. They are *The `.fdg` document*, *Backend*, *Client* and *Commands* below.
- **Decision 7**, commands for every state change: every edit is a command with an inverse, and the inverse restores exactly the lines the edit rewrote (Requirement 8.2).
- **Decision 11**, concurrent saves take turns: the store publishes through `AdpFileWriter.Save`, and inherits the per-destination turn without doing anything itself.
- **Diagram storage**: positions are authored, so they live in the `.fdg` body, which ADP owns. Nothing goes into the `.adp` registration except the origin line and `body:`.

### Project Structure (structure.md)

- A module adds no core code (Requirement 1). Everything under `src/client/src/canvas/library/` is shared library work, named for geometry and behaviour (Requirement 9.1), and would be written the same way for any other diagram that needed it.
- Backend types take the prefix **`Fdg`** (new) - `FdgParser`, `FdgDocumentStore` - under the namespace `EtAlii.Adp.Diagram.FunctionalDecompositionGraph`. The full name is too long for every type in a folder where each file already carries the module's name, and `Fdg` is the file extension the user chose, so it is already the notation's short name.

## Code Reuse Analysis

### Existing Components to Leverage

- **The library's constraint check** (`connectVerdict` in `DiagramCanvas.tsx`) already decides the valid-target highlight from the target's type, `allowSelf` and `cardinality`. The cycle rule is added as its third independent check (Requirement 5.4), and nothing else about connecting changes.
- **Relation cardinality** expresses every one-parent ruling as a declaration: one relation type per child kind with `maxIntoTarget: 1`, and Shows with `maxFromSource: 1`. No cross-relation counting is needed, because each child kind has exactly one relation type arriving at it (requirements, *What was measured*).
- **`sizing: "user"`**, the left and right resize handles and `element-resized` exist; they are extended to height (L2), not replaced.
- **`labels` (`LabelDeclaration`) and `layoutLabels`** lay out lines purely, which makes wrapping testable without a canvas (L3).
- **The shared inline editor** (`src/client/src/canvas/label/InlineLabelEditor.tsx`) and `label-commit-requested` carry the multiline editor's commit unchanged (L4).
- **Core document primitives**: `LineDocument`, `LineSplice`, `AdpFileWriter`, `SharedDocumentReader`, exactly as `dependency-graph` uses them after `file-io-centralization`.
- **`dependency-graph`'s backend** is the closest existing module: a YAML-shaped, line-oriented body read into a model and edited by splicing, commands with a restore-lines inverse (`RestoreDependencyGraphLinesCommand`), a rule set feeding the validator, and `new:` / `rel:` gesture ids. The module half follows its layout file for file, so a reader who knows one knows the other.
- **`ExampleRegistrationTests`** already checks that every seeded example opens against the deployed catalog (Requirement 11.3).

### The one thing the requirements did not list

The requirements measured the library and found five missing capabilities. **Reading the shape code for this design found a sixth.** Every polygon built-in (`diamond`, `hexagon`, `parallelogram`) is drawn as a true polygon, but a connector attaches where a ray from the centre meets the **bounding box** (`edgePointOf`). On a parallelogram or a trapezoid the box's corners lie outside the shape, so an arrowhead lands in empty space beside the element instead of on it. That breaks Requirement 6.1 (*"following the curve's direction where it meets the element"*).

Declared anchors cannot fix it: `points` anchors are absolute offsets, so they stop matching the outline the moment a width changes. Under Requirement 1.2 a behaviour that cannot be declared is library work, so **it is added as L6 without amending the requirements**. It changes no existing diagram: measured on `develop` `32406026`, no module declares `diamond`, `hexagon` or `parallelogram`, and `causal-loop`'s `pill` is not a polygon and is untouched.

### Sequencing with the two centralization specifications

The module half would otherwise write fresh copies of exactly what `backend-centralization` (requirements pending) and `client-centralization` (in progress) are centralizing:

- **Backend:** the store lifecycle (R2), the save result (R3), the change-detecting diff (R4), the change handler (R5), the restore-lines edit (R6), the YAML node range (R7), and the `new:` / `rel:` grammar (R11).
- **Client:** the stream hook and delta fold.

Each copy written now is a fourteenth instance that those specifications then have to convert.

**The user's ruling, given as a selection via the Scrum master on 2026-09-15: build the library half now, and the module half after those shared pieces have landed.** It was the recommended option. The alternative offered was building both halves now as copies of `dependency-graph` and converting them later.

- **So nothing below writes a fourteenth copy.** The module's store, reloader, session, diff, change handler, restore edit, YAML range, gesture grammar, stream hook and delta fold are the shared ones.
- **The tasks document states the ordering**, naming the `backend-centralization` and `client-centralization` work each module task waits on. Those specifications have no tasks yet, so the waits are named by requirement until they do.
- **Everything the module half needs that neither specification provides** is designed here in full: the `.fdg` format, parser, writer, rules, commands, providers, mapper, definition and examples.

## Architecture

- **Client:** `register.ts` mounts `FdgCanvas`, which is a `DiagramDefinition` plus event handlers on the shared canvas library, fed by the stream hook.
- **Backend reading path:** `FdgSession` asks `FdgDocumentStore` for the document (read by `FdgParser`) and turns the model into library elements through `FdgElementMapper`. `FdgValidator` reports what `FdgRuleSet` finds.
- **Backend editing path:** `FdgContextActionProvider` and `FdgContextPropertyProvider` produce commands; each command splices the document through `FdgWriter` and saves through the store. `FdgToolboxProvider` describes the five element types as data.
- **Client → backend:** every edit is a context action with a target id, as every module does: a placement `new:x,y`, a relation gesture `rel:a->b`, or an element or connection id.
- **Backend → client:** deltas only. The client folds them as an upsert keyed on id, and removes only on a remove delta.

## Library work (Requirement 9)

Each item below lands as its own task, with a library test seen to fail before the change (Requirement 9.6), and names no module.

### L1 - Superellipse, trapezoid and diode, with outlines and text regions (9.1)

- **New built-in shapes** `"superellipse"`, `"trapezoid"` and `"diode"` are added to `BuiltInShape` and `BUILT_IN_SHAPES`, so the `declarativeModules` guard admits them.
- **One geometry function per shape, new: `outlineOf(shape, bounds): readonly ShapePoint[]`.** It returns the closed outline in canvas units, and all three consumers read that one outline: the drawing, the text region (below) and the edge point (L6). They cannot disagree, because there is nothing else for them to read.
  - **Superellipse:** |x/a|^4 + |y/b|^4 = 1 (exponent 4, the squircle), sampled at 64 points.
  - **Trapezoid:** corners at fractions (0,0), (1,0), (0.85,1), (0.15,1), so it is narrower at the bottom than the top.
  - **Diode:** a rectangle from the left edge to `width - height/2`, closed on the right by a semicircle of radius `height/2`, the arc sampled at 24 points.
  - **Existing polygons:** `diamond`, `hexagon` and `parallelogram` return their existing corner lists, so their drawing is unchanged.
- **Text region, new: `textRegionOf(shape, bounds, bandHeight): ShapeBounds`.** It returns the widest rectangle of the given height, centred vertically, whose four corners all lie inside the outline, less a 6-unit inner padding on each side. Computed from the outline, it is the same function for every shape. For `box` it is the bounds less the padding.
- **The guard for L1:** for each of the three shapes, at the shared height and at widths 80, 160 and 400, every corner of the text region lies inside the outline (point-in-polygon). It is seen to fail against a planted version that returns the bounding box - **for the trapezoid and the diode at every tested width, and for the superellipse at 160 and 400 but NOT at 80.** At 80x48 the padded box's corner gives `|x/a|^4 + |y/b|^4` of 0.8384, inside the outline, against 1.0485 at 160 and 1.2017 at 400 - so bounds-less-padding is a CORRECT text region there and the plant is not a defect. **Stated because the alternative is a later reader meeting eight failures out of nine and treating the ninth as a hole in the guard.** The consequence to keep: at its narrowest tested width this guard cannot distinguish `textRegionOf` from bounds-less-padding at all, and a discriminating case would need a taller or narrower ELEMENT rather than another width at the shared height, because the plant ignores `bandHeight` entirely.

### L2 - Height resize (9.2)

- `ElementTypeDefinition` gains **`resize?: "width" | "both"`** (new). It is read only when `sizing` is `"user"`. **Omitted means `"width"`**, which is today's behaviour, so `timeline` declares nothing and is unaffected.
- `"both"` adds top and bottom handles. `ElementResized.side` widens to `"left" | "right" | "top" | "bottom"`, and `resizedBounds` carries the vertical edge the same way it carries the horizontal one: the far edge stays put and is never crossed.
- **Guards:** a `"both"` type raises `element-resized` with `side: "bottom"` and the new height. A type that omits `resize` renders no top or bottom handle. `timeline`'s own suite passes unchanged.

### L3 - Wrapped labels (9.3)

- `LabelDeclaration` gains **`wrap?: boolean`** (new). A wrapped label is laid out inside `textRegionOf(shape, bounds, …)` rather than the bounding box. Its text is broken at spaces, and at explicit newlines, into lines no wider than the region, at the library's existing character-width estimate. Lines stack at the typography's line height.
- **Text that overflows the region's height** ends its last visible line with an ellipsis, so the text stays inside the shape (Requirement 4.4). The full text stays available as the label's tooltip.
- **`LabelRule.wrap` is removed** (Requirement 9.3's "or removed"). It has no reader - measured, no module declares it - and `LabelRule` is itself the deprecated predecessor of `labels`. Keeping a second, dead way to ask for the same thing is what the requirement forbids.
- **Width estimate.** Wrapping uses `labels.ts`'s `CHAR_WIDTH`, the one the library already truncates with. `backend-centralization` R10 makes the text metric a shared cross-tier fixture. When that lands, this reads the fixture's value, and L3's guard is written against the metric function rather than the literal 7, so the change is one line.
- **Guards:** a 60-character label in a 160-wide box yields more than one line, and no line's estimated width exceeds the region. A label that cannot fit ends in "…". An explicit newline starts a new line.

### L4 - Multiline inline editor (9.4)

- When the label being edited declares `wrap: true`, `InlineLabelEditor` renders a **textarea** (new mode) placed over the label's text region, instead of the single-line input.
- **Keys:** Enter inserts a newline, Ctrl+Enter or Cmd+Enter commits, Escape cancels, and losing focus commits, as the single-line editor's blur does.
- **The commit is today's `label-commit-requested`, with the newlines in `value`.** No new event, so the module's handler is the same for a Name and a Comment's text.
- **Guards:** a wrapped label opens a textarea and an unwrapped one an input. Enter does not commit and Ctrl+Enter does. The committed value carries the newline.

### L5 - A declared cycle rule (9.5)

- `DiagramDefinition` gains **`acyclic?: readonly { relationTypes: readonly string[] }[]`** (new): each entry names a set of relation types among which no directed cycle may be drawn.
- **`connectVerdict` gains a third check, independent of the other two.** When the relation being drawn is in an entry's set, the target is refused if a directed path already runs from the target to the source through connections whose types are in that set. The search is a breadth-first walk over `model.connections`, bounded by the connection count. **The type check, the cardinality check and the cycle check each return a refusal on their own, and none is consulted to skip another** (Requirement 5.4).
- **`validateDiagramDefinition` rejects an entry naming a relation type the definition does not declare.** A misspelt id would otherwise silently guard nothing, which is the failure this repository's guards keep being written against.
- **Guards:**
  - A → B → C under a covered type refuses C → A.
  - The same chain under an exempt type allows it.
  - A chain mixing a covered and an exempt type is not a cycle.
  - Each is seen to fail against a planted verdict that skips the walk.
  - A definition naming an unknown type fails validation.

### L6 - Outline attachment for polygon shapes (Requirement 6.1, via 1.2)

- For every shape `outlineOf` covers (L1), the edge point is where the ray from the centre towards the other end **meets the outline**, not the bounding box. `box` and `pill` keep their existing geometry.
- **Guard:** a connector approaching a trapezoid from its lower-left lands on the slanted side, inside the bounding box and on the outline to within half a unit. It is seen to fail against today's bounding-box point.

## The `.fdg` document (Requirement 2)

**A YAML subset, read and edited line by line, in the style of `.dgr` and `.tml`:**

```yaml
functional-decomposition-graph: 1
# Comments and blank lines survive every edit.
elements:
  - id: task-list
    type: ui-element
    name: Task list
    description: The day's tasks, in planned order.
    x: 120
    y: 80
    width: 160
  - id: open-task
    type: action
    name: Open task
    x: 120
    y: 200
    width: 140
  - id: note-offline
    type: comment
    text: |-
      Everything here must work offline.
      Sync happens when the device reconnects.
    x: 420
    y: 80
    width: 240
    height: 96
connections:
  - id: c-open
    type: owns-action
    from: task-list
    to: open-task
    name: tap
    description: A tap anywhere on a task row.
```

- **The header line** is `functional-decomposition-graph: 1`. A missing or different version opens the document and reports it, as `.dgr` does.
- **Element `type`** is one of `ui-element`, `data-element`, `action`, `function` and `comment`.
- **Connection `type`** is one of `ui-child`, `owns-action`, `owns-data`, `owns-function` and `shows`. It names the relation, because the rules are per relation (Requirement 5.1), and is what the client's relation ids are.
- **Ids** are Base36 short ids for anything ADP creates (tech.md). Hand-authored ids, as in the example, are any non-empty token.
- **Positions are the element's top-left corner** in canvas units, with `width` stored per element. `height` is stored only for a `comment`. The other four share one height, the constant **`FdgGeometry.SharedHeight` = 48** (new), which the mapper sends and the validator does not read. Top-left rather than centre because a reader editing the file thinks in where a box starts, and it is what the library's snap rests on (its leading edge). The mapper converts to the library's centre coordinates.
- **Minimums:** width 80, and a Comment's height 48. A smaller value in the file opens and is reported. A resize below the minimum is clamped by the command.
- **A Comment's `text`** is a block scalar (`|-`), so newlines round-trip. It has no `name`. A `name` written on a Comment is passed over and survives, and is reported.
- **What the parser does not understand** - an unknown key, an unknown `type`, a malformed entry - it passes over. The lines survive, because nothing rewrites them, and the validator reports them (Requirement 2.2). **The parser never throws**: a YAML error produces an empty model, the document text is kept, and the problem is reported with its line.
- **Byte-identical round trip:** an unchanged document is never rewritten. An edit rewrites only the lines of the entry it changes, through `LineSplice`, using the node range of the entry. `.gitattributes` gains `*.fdg -text` (Requirement 2.3).
- **The empty document** from `FdgDocumentFactory` (new) is the header line plus `elements: []` and `connections: []` (Requirement 2.5).

## Backend

**Components**, each mirroring its `dependency-graph` counterpart unless noted:

- **`FdgParser`** - text to `FdgModel` (elements, connections, parse problems, and each entry's line range).
- **`FdgWriter`** - one pure function per edit: add, remove, move, resize, rename, set text, set description, connect, disconnect, and set a connection's name. Each returns the splice or a refusal sentence.
- **`FdgDocumentStore`**, **`FdgDocumentReloader`**, **`FdgSession`**, **`FdgSessionFactory`** - lifecycle and view, as thin uses of `backend-centralization`'s shared store lifecycle (R2), save result (R3), diff (R4) and change handler (R5), with its restore edit (R6), YAML node range (R7) and gesture grammar (R11).
- **`FdgElementMapper`** - model to library elements. It sends the type, centre, width and height, the payload `FdgElementPayload { name, text }`, and for connections `FdgConnectionPayload { name }` (new, `api/functional-decomposition-graph.proto`). **Descriptions are never sent**, so none can be drawn (Requirement 7.1).
- **`FdgRuleSet`** and **`FdgValidator`** - Requirement 5.5, below.
- **`FdgContextActionProvider`**, **`FdgContextPropertyProvider`**, **`FdgToolboxProvider`** and **`FdgContextSourceResolver`** - editing, properties, toolbox and selection resolution (Requirements 7 and 8).
- **`FdgRelations`** (new) - the rules table as data: for each relation id, its allowed source and target element types and its limits. **The one backend statement of Requirement 5's table**, read by the rule set and by the connect command's refusal.
  - The client's definition states the same table as declarations. `backend-centralization` R12's type-string fixture is what proves the two agree once it exists; until then a module test asserts the backend table against a checked-in copy of the client's relation ids.

### Rules and validation (Requirement 5.5)

`FdgRuleSet` reports, each naming the elements involved, to the Errors and Warnings panel:

| Rule id | Breach |
| --- | --- |
| `fdg.forbidden-link` | A connection whose relation does not admit its source or target type, including any link to or from a Comment, or a connection whose `type` is unknown. |
| `fdg.self-link` | A connection from an element to itself. |
| `fdg.second-parent` | A second ownership link into a UI Element, Action, Data Element or Function, by child kind. |
| `fdg.second-shows` | An Action with more than one Shows. |
| `fdg.ownership-cycle` | A directed cycle among `ui-child`, `owns-action`, `owns-data` and `owns-function`, reported once per cycle with its members in order. |
| `fdg.dangling-reference` | A `from` or `to` naming no element. |
| `fdg.duplicate-id` | Two entries with one id. |
| `fdg.unreadable-entry` | An entry the parser passed over, an unknown key, a Comment with a `name`, or a size below its minimum. |

**The cycle rule is one function**, `FdgOwnership.CyclesIn(model)` (new), used by the rule set. **The connect command refuses on the same three checks the canvas makes** - type, cardinality, cycle - so a stale or scripted request cannot write what the canvas would not offer. The refusal sentence names which check failed.

## Commands (Requirement 8)

Every edit is a context action producing a command whose inverse is a **restore-lines** command. That command puts back exactly the line range the edit replaced, which is how `dependency-graph` and `timeline` undo, and it satisfies "undo SHALL restore exactly the lines the change rewrote".

| Action | Target | Command | Notes |
| --- | --- | --- | --- |
| Add element (toolbox drop) | `new:x,y` plus the element type | `AddFdgElementCommand` | Default width 160, or 240 × 96 for a Comment. The name is "New UI element", and so on, made unique. The drop hold applies (Requirement 8.1). |
| Delete element | element id | `RemoveFdgElementCommand` | Also removes every connection to or from it, in the same edit, so the undo restores both. |
| Move | element id | `SetFdgPlacementCommand` | Top-left from the library's centre. Snap is whatever the library applies (Requirement 3.3). |
| Resize | element id | `SetFdgSizeCommand` | Width for the four; width and height for a Comment. Clamped to minimums. |
| Connect | `rel:a->b` plus the relation type | `ConnectFdgElementsCommand` | Refused on type, cardinality or cycle (above). |
| Disconnect | connection id | `DisconnectFdgConnectionCommand` | |
| Rename / edit text | element id | `RenameFdgElementCommand` | A Name for the four, a Comment's `text` for a Comment. Both come from the inline editor and the property grid. |
| Set description | element or connection id | `SetFdgDescriptionCommand` | From the property grid only. An empty value removes the key. |
| Set connection name | connection id | `RenameFdgConnectionCommand` | From the property grid, or the route label's editor where declared editable. |

**A refusal returns the writer's sentence as the command result**, which the canvas shows through `action-refused`, and the document is unchanged (Requirement 8.3).

## Client

**Files**, following `dependency-graph`'s: `register.ts`, `FdgCanvas.tsx`, `fdg.css` and `readme.md`. The stream hook and delta fold are `client-centralization`'s shared ones, so the module writes neither.

**The definition** (in `FdgCanvas.tsx`):

- **Element types.**
  - `ui-element`: `superellipse`
  - `data-element`: `parallelogram`
  - `action`: `trapezoid`
  - `function`: `diode`
  - `comment`: `box`
  - Every type is `sizing: "user"`; `comment` also has `resize: "both"`.
  - Each has one `labels` entry, editable, bound to `payload.name`; the Comment's is bound to `payload.text` with `wrap: true`.
  - Each carries a class `fdg-{type}` for its fill.
  - `comment` has anchors `{ kind: "edge", enabled: false, visible: false }`, so it has no anchors (Requirement 5.3).
- **Relation types.**
  - The five, each with `route: "cubic-bezier"`, an `arrow` end marker only, and `label: { placement: "midpoint", editable: true }` bound to `payload.name`, which draws nothing when empty.
  - Endpoints and cardinality are exactly the rules table; `allowSelf: false` on all.
- **`acyclic`:** `[{ relationTypes: ["ui-child", "owns-action", "owns-data", "owns-function"] }]`. `shows` is deliberately absent (Requirement 5.4).
- **Layout** `manual` only; dragging enabled; the toolbox derived from the element types.

**Handlers** answer the library's events through the module's own transport, and nothing else. `element-dropped`, `element-moved`, `element-resized`, `connection-drawn`, `element-deleted`, `connection-deleted` and `label-commit-requested` each become one context action. The module has **no rendering, gesture, selection or label code** (Requirement 1.1). Selection is the library's (Requirement 10.1).

### Colours (Requirement 4.5, 4.6)

Five tokens in `src/client/src/index.css`, beside `--color-diagram-potential`, with the dark values fixed here:

| Token | Light (the user's hex) | Light vs `--color-text` `#0f172a` | Dark | Dark vs `--color-text` `#f1f5f9` |
| --- | --- | --- | --- | --- |
| `--color-diagram-fdg-ui-element` | `#aaed92` | 12.95:1 | `#2e7814` | 5.03:1 |
| `--color-diagram-fdg-data-element` | `#ededed` | 15.25:1 | `#696969` | 5.01:1 |
| `--color-diagram-fdg-action` | `#9edcfa` | 11.96:1 | `#086fa1` | 5.05:1 |
| `--color-diagram-fdg-function` | `#86e6d9` | 12.19:1 | `#187569` | 5.06:1 |
| `--color-diagram-fdg-comment` | `#fcf281` | 15.44:1 | `#736a03` | 5.06:1 |

- **How the dark values were chosen:** each keeps the light colour's hue and saturation, with the lightness lowered to the lightest value that reaches **5.0:1**. That leaves a margin of about half a point above the 4.5:1 line, where the requirements' candidates sat at 4.50 to 4.52.
- **How they are checked:** by computation (WCAG 2.x relative luminance), both in this design and in a client test that parses `index.css`, computes each ratio in both modes, and fails below 4.5. That test is seen to fail against a planted `#7c7203`-style value lowered below the line.
- **Text** on every fill is `--color-text` (Requirement 4.6).
- **Borders** use `--color-border`, the stroke the library's shapes already draw with (`canvas.css`). In light mode it is `#e2e8f0`, almost the Data Element's `#ededed`. So the Data Element's outline is carried by its silhouette against the canvas background rather than by its border, and the browser pass checks it in both themes.

## Examples (Requirement 11)

`src/diagrams/functional-decomposition-graph/examples/field-service/`, seeded into `src/examples/` like every module's:

- **`field-service.fdg`** - a mobile app for field service employees:
  - **UI:** Planning (a UI Element) owning a Task list, which owns a Task row; Task detail, owning a Step list and a Step row.
  - **Actions:** Open task, Back to list, Tick step, Complete task.
  - **Data:** Task, which has a nested Step and a nested Location; Planning day.
  - **Functions:** Sync queue, which calls Upload photos; Offline cache.
  - **Shows** Open task → Task detail, and **Back to list → Task list**: the navigation round trip the cycle ruling allows.
  - Named connections, Descriptions on elements and connections, and two Comments.
- **`field-service.adp`** - the origin line `etalii/functional-decomposition-graph` and `body: field-service.fdg`.
- **`readme.md`** - **why the example is hand-authored** (a new notation, so no published corpus can exist; the user's ruling), and **what it does not demonstrate**: a document with breaches, Base36 ids, an empty Name, and more than one Shows per Action (which the rules forbid).
- **A test opens the example and asserts the validator reports nothing** (Requirement 11.4), beside `ExampleRegistrationTests`.

## Error Handling

### Error Scenarios

1. **A document that breaks the rules**, edited outside ADP.
   - **Handling:** it opens, everything readable is drawn, and each breach is a validator problem naming the elements.
   - **User Impact:** the panel lists what is wrong; nothing is lost or rewritten.
2. **A malformed or truncated file.**
   - **Handling:** the parser never throws; an unreadable entry is passed over and reported with its line.
   - **User Impact:** the rest of the graph draws, and the panel names the line.
3. **A connect the rules forbid** reaching the backend anyway (a stale client, a script).
   - **Handling:** the command refuses with the failing check's sentence.
   - **User Impact:** the refusal is shown and the document is unchanged.
4. **A save that fails** (disk, permissions).
   - **Handling:** the store's save result carries the error (`backend-centralization` R3) and the edit stays in memory.
   - **User Impact:** the refusal sentence is shown, and retrying after fixing the cause works.
5. **The body changes or disappears on disk** while open.
   - **Handling:** the shared store's reload rules, `backend-centralization` R2.3 to R2.6: its own saves are ignored, an unreadable file keeps the last good document, and a deleted body becomes empty.
   - **User Impact:** the canvas follows the file, without blanking during another program's save.

## Testing Strategy

**Every guard below is seen to fail against the defect it rules out before it is trusted** (CLAUDE.md, *Bugs found during implementation or verification*).

### Unit Testing

- **Library:** the guards named under L1 to L6, in the library's own test files, naming no module.
- **Parser and writer:**
  - A byte-identical round trip over every fixture, including CRLF, LF, a missing final newline, comments and blank lines. The fixtures are byte-compared, so `*.fdg -text` is added first and checked with `git ls-files --eol`.
  - Each edit rewrites only its entry's lines.
  - A malformed document never throws.
- **Rule set:** one fixture per rule id with exactly that breach, plus the example with none. The cycle rule is also tested with a Shows loop, which must NOT be reported.
- **Commands:** each edit, then its undo, gives the original bytes. Each refusal leaves the bytes unchanged.
- **Colours:** the contrast test above.

### Integration Testing

- **Module client:** the definition passes `validateDiagramDefinition`, and `declarativeModules` passes against the module unchanged (Requirement 1.3).
- **Handlers:** each library event produces its one context action.
- **Relations:** a test drives the connect gesture over the example's elements and asserts, for each of the five relations, one allowed target highlighted, one forbidden pair not highlighted, a second parent not highlighted, and a cycle-closing target not highlighted, while the Shows round trip IS highlighted.
- **Backend:** the example opens against the deployed catalog (`ExampleRegistrationTests`), and a session over it delivers every element with the shared height.

### End-to-End Testing

- **A `tests.md` entry run in a real browser**, as Requirement 13.1 lists it:
  - All five shapes in both themes.
  - Text inside every shape at the shared height, at a minimum and a generous width, and a Comment with more text than fits.
  - Width resize on the four, and width and height on a Comment.
  - Each allowed link drawn, each forbidden one refused, a second parent and a cycle refused, none of the refusals highlighted.
  - A connection name appearing and disappearing.
  - A Description never appearing.
- **jsdom is not evidence for any of these** (Requirement 13.2).

## Requirement Coverage

| Requirement | Where |
| --- | --- |
| 1 Declarative module | Client; Code Reuse; L1 to L6 |
| 2 The `.fdg` file | The `.fdg` document |
| 3 Positions are authored | The `.fdg` document; Commands (move, resize) |
| 4 Element types, text inside, colours | L1, L3, L6; Client (definition, colours) |
| 5 Which links may be drawn | Client (relation types, `acyclic`); L5; Rules and validation |
| 6 Connections | Client (relation types); L6; Commands |
| 7 Properties | Backend (mapper sends no description); Commands |
| 8 Toolbox and commands | Commands; Backend (toolbox provider) |
| 9 Library work | L1 to L5, plus L6 under 1.2 |
| 10 Inherited behaviour | Client (selection is the library's; no module gesture code) |
| 11 Examples | Examples |
| 12 Catalog and documentation | Tasks: the catalog row moves to 📝 on approval; `creating-a-diagram-module.md` gains the six capabilities in the definition section |
| 13 Browser confirmation | Testing Strategy, End-to-End |
