# Dependency graph

[`dependency-graph.dis`](dependency-graph.dis) specifies ADP's dependency graph in DISL 0.3: its node and relation types, its two authored axes, its notation, toolbox, context menus, forms, findings, operations and persistence settings, and, in its `x-dependencies` block, the ids the standalone tool gives each palette entry, menu entry and property row. This document holds everything about the diagram type that DISL cannot express, and records where the `.dis` had to approximate. Each statement is tied to the code or document that shows it.

| | |
|---|---|
| Kind | Diagram |
| Origin | `generic/dependencies` |
| Display name | Dependency graph |
| File | `.dgr`, ADP's own YAML schema, owned outright by this type |
| State | ✅ Implemented in standalone; no IntelliJ, VS Code or Eclipse implementation |
| Source read | [etalii.adp.ide.standalone `src/diagrams/dependency-graph/`](https://github.com/etalii-adp/etalii.adp.ide.standalone/tree/develop/src/diagrams/dependency-graph) at `c15705f0` |

The type is a disciplined fork of the timeline diagram with every time-related aspect removed (no dates, no ruler, no time units on zoom, no moments) and one addition: its edges mean something. A relation `from: a, to: b` reads "a depends on b", and the arrowhead is drawn at `b` ([`DependencyGraphRelation.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/_Model/DependencyGraphRelation.cs); the original requirements in standalone's history before `d573ad87`, `.spec-workflow/archive/specs/dependency-graph/requirements.md`).

Its catalogue entries agree: standalone [`docs/tools.md`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/docs/tools.md) lists it as implemented, and the Notion "Tools" row gives the purpose "Draw named things and what depends on what, freely placed and connected", the reason for specialising "Dependencies are about direction and order; a dedicated tool keeps every edge directed and readable, in ADP's own .dgr format", the focus areas "Planning and roadmapping" and "Systems and strategy", and the family "Business process & workflow notation".

## 1. The `.dgr` file format

DISL's persistence layer (section 11) configures a DID definition. A `.dgr` file is not one, so the `.dis` names a required persistence plugin, `net.etalii.adp.generic.dgr`, and this section is that plugin's specification.

### 1.1 Grammar

A `.dgr` file is a YAML mapping with three keys the type reads, in this order by convention:

```yaml
dependencies: 1
elements:
  - id: api-gateway
    label: API gateway
    x: 270
    row: 4
  - id: identity-service
    label: Identity service
    x: 797.5
    row: 4
relations:
  - id: r-gateway-identity
    from: api-gateway
    to: identity-service
    label: verifies tokens with
```

| Key | Content | Read by |
|---|---|---|
| `dependencies` | The version marker, `1`. Written into every new file; **never checked when reading** (see section 9). | [`DependencyGraphDocumentFactory.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/DependencyGraphDocumentFactory.cs) |
| `elements` | A sequence of nodes. Each is a mapping with `id`, `label`, `x` and `row`. | [`DependencyGraphParser.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/DependencyGraphParser.cs) |
| `relations` | A sequence of dependencies. Each is a mapping with `id`, `from` (the dependent), `to` (the dependency) and an optional `label`. May be absent. | same |

- `x` is read as a floating-point number with the invariant culture, so a file says the same thing on every machine. `row` is read as an integer.
- Every value is read as a YAML scalar string first. Anchors, aliases, block scalars and every quoting style are accepted because a full YAML parser (YamlDotNet) does the reading; nothing is ever serialised back through it.
- An entry of `elements` or `relations` that is not a mapping is skipped. A missing key reads as empty (`id`, `label`, `from`, `to`) or zero (`x`, `row`).
- An empty file, or a file whose root is not a mapping, is an empty graph rather than an error.
- The file holds no date, duration or instant anywhere. A `.dgr` that did would be a defect ([fixtures readme](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph.Tests/Fixtures/readme.md)).

### 1.2 Mapping to DID

A runtime that works on DID definitions sees a `.dgr` file through this mapping. DISL expresses the DID side; the mapping itself is the plugin's.

| `.dgr` | DID |
|---|---|
| `elements[i]` | `elements[]` record with `type: "Node"`, `id` from `id`, `attributes.label`, `attributes.x`, `attributes.row` |
| `relations[i]` | `relations[]` record with `type: "DependsOn"`, `id`, `source` from `from`, `target` from `to`, `attributes.label` when present |
| `dependencies: 1` | `language.version` 1.x of `net.etalii.adp.generic.dependencies` |
| any other key, at any level | preserved verbatim, never modelled (section 1.4) |
| comments, blank lines, indentation, quoting style, line endings | preserved verbatim; DID has no place for them |

There is no view record: `x` and `row` are bound placement attributes, so the position lives only in the model, exactly as DID requires of bound values. Nothing else is stored about the view (`persistence.view.store` is empty): no viewport, no sizes, no waypoints, no label offsets.

### 1.3 Writing: splices, never serialisation

Every write is a splice of the lines an edit affects, located by the line range the parser recorded for each node and relation ([`DependencyGraphWriter.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/DependencyGraphWriter.cs)). DISL's persistence settings assume a canonical writer that regenerates the file; this type does the opposite, which is why `ordering`, `keys` and `omitDefaults` in the `.dis` describe only what new content looks like.

- **Round trip.** Opening and saving an unchanged file returns it byte for byte, and editing one node changes only that node's lines.
- **Setting a value** (rename, move, relabel) replaces the value of the one key inside the node's or relation's range and leaves the rest of the line's layout alone.
- **Clearing a relation's label** removes its `label` key. Clearing a node's label writes `label: ""`; a node always keeps its `label` key.
- **Adding** appends after the last existing entry of the section, copying the indentation of the entries already there (a file written with four spaces stays so). Only an empty section falls back to the default two-space `- ` form. A new node is written as `id`, `label`, `x`, `row`; a new relation as `id`, `from`, `to`, then `label` only when it has one.
- **The `relations:` key is created on demand** at the end of the file with its first entry, and the undo of that insert removes the key again, so an undo is byte-identical (section 6).
- **Removing a node** removes its lines and those of every relation that names it, bottom-up so earlier ranges stay valid.
- **Numbers** are written in their shortest round-trippable invariant form, so a whole `320` stays `320` rather than becoming `320.0`.
- **Quoting.** A label is written plain unless it contains `:` or `#`, starts with `-`, a space or a quote, or ends with a space; then it is double-quoted with `\` and `"` escaped. An empty label is `""` ([`LineSplice.Quote`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/backend/EtAlii.Adp.Documents/LineSplice.cs)). Existing quoting is never changed on keys the edit does not touch.
- **Line endings.** A file keeps whatever it already uses, including a missing final newline. A new file is written as `dependencies: 1\r\nelements: []\r\n`: CRLF because that is standalone's house style, which is why the `.dis` says `newline: "crlf"`.
- **A file that does not parse is never written.** Every command refuses with a sentence naming the edit ("This graph does not parse, so nothing can be added until the file is fixed."), so a broken file is never made worse.

### 1.4 What the file may carry beyond the model

Keys the type does not model survive every edit verbatim, at the document, node and relation level, including nested sequences, so a later version of the type or a human can put data there without ADP eating it. The same holds for comments in every position YAML allows and for blank lines. The round-trip corpus proves each case: `simple`, `shared-rows`, `coordinates`, `relations`, `comments`, `lf-line-endings`/`crlf-line-endings`, `no-trailing-newline`, `indentation`, `scalars` and `unmodelled-keys` ([fixtures readme](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph.Tests/Fixtures/readme.md)). The fixtures are byte-compared, so a repository that stores `.dgr` files must exempt them from line-ending normalisation (`*.dgr -text` in standalone's `.gitattributes`).

### 1.5 Files and registration

A graph is one `.dgr` file. In standalone it may also have an `.adp` registration beside it whose first line is the MIME type `generic/dependencies`; the registration carries nothing else the type reads, and a bare `.dgr` opens as a dependency graph with no registration at all, because no other tool writes `.dgr` ([`Diagram.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/Diagram.cs), [`DependencyGraphSessionFactory.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/DependencyGraphSessionFactory.cs)). The base name of a new diagram is not written into the file: the schema has no title line. A new graph has no sample content.

## 2. Identity and loading

- **Ids come from the file.** A hand-written file may use readable ids (`api-gateway`); ids ADP generates are ShortGuids, a version 4 GUID written as 25 characters of base 36 (`ShortGuid.cs` in standalone's `src/backend/EtAlii.Adp/`). The `.dis` says `uuid-v4` with `encoding: "base36"`, as the hype cycle graph does; an earlier version of this document said 22 URL-safe base64 characters, which the code never wrote.
- **Missing and duplicate ids are tolerated on load and reported** (section 3). Wherever an id is looked up, the first declaration with it wins ([`DependencyGraphEdits.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/Commands/DependencyGraphEdits.cs)). DID requires unique, present ids, so these states exist only in a `.dgr` file.
- **A file that is not YAML still opens.** The canvas shows the diagram as unavailable, naming the file and line, the Errors and Warnings panel carries the same message, and every edit is withheld ([`DependencyGraphDocumentEntry.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/_Model/DependencyGraphDocumentEntry.cs)).
- **An unreadable `x` or `row` reads as 0**, silently (section 9).
- **One document per path.** Two diagrams on the same file share one loaded document, so an edit through one appears in the other. An edit made outside ADP is picked up and sent to every open view; a reload that cannot read the file keeps the last good document; deleting the file clears it ([`IDependencyGraphDocumentStore.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/IDependencyGraphDocumentStore.cs), [`DependencyGraphDocumentReloader.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/DependencyGraphDocumentReloader.cs)). A save writes exactly the document the command edited, so a reload that lands between an edit and its save cannot discard the edit while reporting success.

## 3. Validation

Every rule reports a **warning** naming its element, because the rest of the graph still draws. The one **error** is a file that is not YAML, reported as a single problem at the parser's line instead of running the rules over an empty model ([`DependencyGraphRuleSet.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/DependencyGraphRuleSet.cs), [`DependencyGraphValidator.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/DependencyGraphValidator.cs)).

| ADP rule id | Severity | Message | Located at | In the `.dis` |
|---|---|---|---|---|
| `dependencies.unparseable` | error | "This is not YAML that can be read: {parser message}" | the parser's line (at least 1) | `std.unparseable`, with this code and message |
| `dependencies.missing-id` | warning | "'{label}' has no id, so nothing can select, connect or edit it." (a relation: "A dependency has no id, …") | the declaration's first line | `std.missingId`, with this code and message |
| `dependencies.duplicate-id` | warning | "The id '{id}' is declared more than once, which makes every reference to it ambiguous." | the second and later declarations' first line | `std.duplicateId`, with this code and message. Nodes and relations share one id space |
| `dependencies.dangling-relation` | warning | "A dependency names '{id}', and no node with that id is in this graph." | the relation, by id; once per missing end | `std.references`, with this code and message |
| `dependencies.self-dependency` | warning | "'{id}' is declared to depend on itself, which says nothing about what needs what." | the relation, by id | `allowSelfLoops: false` produces `std.endpoints`, with this code and message |

The `.dis` states every code, severity and message, but four things about the findings are the code's and DISL cannot say them, so the standalone tool keeps its own rules ([`DependencyGraphRuleSet.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/DependencyGraphRuleSet.cs)) rather than evaluating the `.dis`:

- **Order.** The findings come in two passes: first every missing and duplicate id, interleaved in declaration order, nodes before dependencies whatever the file's order; then, per dependency in declaration order, its dangling `from`, its dangling `to` and its self-dependency. DISL's default order (8.6) puts the self-dependencies (`std.endpoints`) before every dangling end (`std.references`), and `constraints.order` sorts by code, which cannot interleave two codes.
- **An empty end is not a reference.** A dependency whose `from` or `to` is absent or empty is reported by no rule; `std.references` reports any end that resolves to no node.
- **A self-dependency is judged on the ids as written.** `from: ghost, to: ghost` is reported as dangling twice and as a self-dependency; `std.endpoints` judges only a relation whose two ends both resolve.
- **The missing-id message names a node by its label**, which is empty when it has none ("'' has no id, …"), because the code falls back to the id and the id is the thing missing. The `.dis` says the same with `self.label`; a runtime that mints an ephemeral id for the element still names it by its label.

Rule ids are prefixed with the origin's type name. A self-dependency can only come from a hand-edited file: connecting a node to itself is refused at the command with "A node cannot depend on itself." There is no cycle rule: the design names cycle detection as something the type will want one day, and the code deliberately does not invent it ([`DependencyGraphRules.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/DependencyGraphRules.cs)). The `.dis` therefore leaves `acyclic` unset.

A dangling relation is still sent to the canvas with the ids as written, so the canvas can show the loose end and the validator can report it ([`DependencyGraphElementMapper.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/DependencyGraphElementMapper.cs)). Labels are never validated: every value asked for is a label, and a label has no wrong answer.

## 4. Placement arithmetic

The `.dis` binds `x` and `row` to a linear x axis and a linear row axis with a scale of 60 and whole-row snapping. These numbers and rules sit beside it:

- **Row height 60, node box 160 × 36.** A node's top-left corner sits at (`x`, `row × 60`), leaving a gutter of 24 between rows. All three constants are rendering constants mirrored in the backend and the client ([`DependencyGraphRows.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/DependencyGraphRows.cs), [`DependencyGraphCanvas.tsx`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx)); if they disagree, every vertical drag lands on the wrong row. Changing the row height re-spaces every graph without touching a file.
- **Row rounding.** A y becomes a row by rounding `y / 60` to the nearest integer with halves away from zero, never producing negative zero. The same rule is shared by every row canvas and pinned by a golden fixture both sides read. DISL's snap `direction: "nearest"` does not say how ties break.
- **Why a linear row axis, not an ordinal one.** A DISL ordinal axis has declared bands. Rows here have none: any integer is a row, negative rows are valid, several nodes share one and gaps are allowed ([`shared-rows.dgr`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph.Tests/Fixtures/shared-rows.dgr)). A row is a placement grid, never a lane, so no band is drawn and nothing is stacked.
- **Moving.** A drag moves freely on x and snaps to the nearest row; the drop sends the top-left corner, and the move is one command. Nothing on x is snapped, and the canvas offers no way to switch row snapping off, which the `.dis` states with `userToggle: false` and `bypassModifier: "none"`.
- **Dropping from the toolbox** places the node's top-left corner at the pointer's x, on the row nearest the pointer's y. The id the canvas sends is a placement id `new:{x},{row}` that lives only for that one call ([`DependencyGraphNewPlacement.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/DependencyGraphNewPlacement.cs)).
- **Tab and Enter** add a node 240 to the right on the same row, or at the same x one row down. The `.dis` expresses both as operations; the 240 step is chosen so the new node does not land under its source at the zoom levels the canvas opens at ([`DependencyGraphContextActionProvider.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/DependencyGraphContextActionProvider.cs)).
- **Adding from a menu** with no placement asks for a label ("Add node", field "Label", prefilled "New node") and places the node 240 right of the node the action was invoked on, or at (0, 0) with no node.
- **Property grid.** X accepts any invariant number; Row accepts only an integer, and a fractional row is refused ("'{value}' is not a number this graph can place a node at.") rather than truncated. The `.dis` states both as `validate` patterns; the code parses with .NET's invariant rules, which also accept `NaN` and `Infinity` for X and refuse a row outside the 32-bit range. The X row shows the number as the file writes it, .NET's shortest invariant text (`1000000`, `1E-05`), where CEL's `string(double)` would write `1e+06` and `1e-05` ([`DependencyGraphContextPropertyProvider.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/DependencyGraphContextPropertyProvider.cs)).

## 5. Gestures and interactions

- **Drawing a dependency.** A node has two anchors, one at the middle of each side. Dragging from the **right** anchor to another node makes the dragged node depend on the one it lands on. Dragging from the **left** anchor reverses it: the node landed on depends on the dragged node, because what depends on a node arrives at its left side. DISL's `connect` context tool and `create-edge` tool have no notion of a direction chosen by the anchor, so the `.dis` can only name the anchors.
- **Releasing on empty canvas** creates a node labelled "New node" at the release point and the dependency, as one command and one undo step. From the right anchor the new node is the dependency; from the left anchor it is the dependent. The `.dis` says this with `createSource` and `createTarget` on a library tool, `dependsOn`, that no palette group shows: the gesture starts from a node's anchor, never from the palette.
- **A whole gesture is one call.** The canvas sends source and landing together as `rel:{from}->{to}`, where either end may be a placement id. An earlier two-call protocol kept an armed source between calls, and a stale arm related the wrong pair in the running app ([`DependencyGraphRelationGesture.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/DependencyGraphRelationGesture.cs)). The split takes the first `->`, so a node id containing `->` would be misread.
- **Refusals** come back as sentences shown on the canvas's refusal line: "A node cannot depend on itself.", "Both ends of a dependency must be nodes of this graph.", "The node this dependency starts from is no longer in this graph.", "That node is no longer in this graph.".
- **Removing a node** removes every dependency that names it. With none, it goes at once; with some, the user is asked first ("Removing this node also removes the 1 dependency attached to it." or "… the {n} dependencies attached to it.", button "Remove", marked dangerous). The `.dis` says so with DISL 0.3's `deletion.confirm` `count` and `threshold`.
- **Keys.** On a node: F2 renames, Delete removes, Tab adds to the right, Enter adds below. On a dependency: F2 relabels, Delete removes it. Labels of nodes and dependencies also edit inline on the canvas.
- **No reparenting.** A node has no parent; the host's move-to-parent request is refused with "A node has no parent to move it under; dragging changes where it sits and which row it is on."
- **Not supported:** copy and paste (the `.dis` sets `copyable: false`), resizing, reconnecting a dependency's end (`reconnectable: false`), selecting several elements at once (standalone's multi-select specification is not built yet), and automatic layout. The canvas pans and zooms; zoom and pan never reach the file.
- **No ruler, no dates, no moments.** A ruler, a date in a drag hint or a moment marker appearing anywhere in this type is a defect (original requirement 3.3).

## 6. Undo and redo

Every edit is one command on the project's history. What DISL's single "one transaction, one undo step" rule does not say is how the inverse restores the file:

- **Rename, relabel and move** are undone by the same command carrying the previous value.
- **Removing a node** captures the exact lines of the node and its dependencies before the splice, and its undo re-inserts those lines, so comments and formatting inside the removed block come back byte for byte. If the file has since been cut down so far that there is no place to put them back, the undo refuses: "This graph has changed too much since then to put that back."
- **Adding** is undone by removing what was added; if the add created the `relations:` key, the undo removes that key too.
- **Redo reuses the ids** the first execution generated, because the ids are generated once, where the gesture happens, and carried by the command the history holds. A redo that would duplicate an id is refused.
- Every handler re-locates its target in the current document, because undo and redo dispatch the same command instance again later.

## 7. The dependency curve

The `.dis` names a routing plugin, `net.etalii.adp.generic.dependencyCurve`, with `bezier` as its fallback. Its geometry ([`DependencyGraphCanvas.tsx`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx)):

- When the dependency's left edge lies to the right of the dependent's right edge, the curve is a horizontal bezier between the two facing side anchors.
- Otherwise, when the dependency sits behind or overlaps the dependent, the curve is a forward loop: out of the dependent's right side and back into the dependency's left side.
- While a dependency is being drawn, the preview is a plain horizontal bezier to the pointer.
- The target end attaches on the side facing its source rather than by true outline intersection, because the notation reads left to right and an edge through the top of a box would read as a different relation.
- The arrowhead is a filled triangle at the dependency end, coloured with its line. Two dependencies between the same pair are drawn on the same curve; nothing separates them.

## 8. Rendering and the wire

- **The look is the host's shared canvas theme**: surface fill, border stroke 1.5, corner radius 6, a 12 px centred label truncated to the box, the label falling back to the node's id when empty. The `.dis` records those values as theme tokens with the standalone light values; the host supplies dark mode. Selection and connect-target highlighting are the canvas library's, not the type's.
- **Viewport culling.** The canvas reports its viewport, and the backend sends only what it admits: every node whose box touches the viewport (touching edges count), and every dependency whose span, the hull of its two end boxes, touches it, together with both of its end nodes so a line always has two boxes to draw between. Until a viewport is reported the whole graph is sent. A view change sends removals first, then additions ([`DependencyGraphSession.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/DependencyGraphSession.cs)).
- **Wire payloads.** A node travels with its authored `x`, `row` and `label`; a dependency with `from_element_id`, `to_element_id` and `label`, in that order, because the order is the only thing carrying the type's meaning across the wire and nothing may normalise it ([`dependency-graph.proto`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/api/dependency-graph.proto)). Element types are `generic/dependencies+node` and `generic/dependencies+relation`; an update arrives as an add of the element in its new state, never as a remove and add, so the selection survives it.

## 9. Known gaps and discrepancies

Found while tracing the code for this document. None of them is fixed here; each is a candidate for its own change.

- **The version marker is never read.** `dependencies: 1` is written into every new file, but the parser ignores it, so a file with another value or none opens the same way. The `.dis` maps it to language version 1.x; nothing enforces that.
- **An unreadable `x` or `row` becomes 0 without a problem.** The parser's comments say "the rules say so", but no rule reports it ([`DependencyGraphParser.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/DependencyGraphParser.cs), [`DependencyGraphRules.cs`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/backend/EtAlii.Adp.Diagram.DependencyGraph/DependencyGraphRules.cs)).
- **Labels that look like other YAML types are written plain.** A label such as `yes`, `true` or `123` is written unquoted. ADP reads it back as the same text, but DISL section 11.2 requires YAML output to quote such strings, and another YAML reader would see a boolean or a number.
- **The client declares an Insert key the backend does not answer.** The canvas sends `Insert` for a selected node, but no action on a diagram element carries that shortcut, so the backend refuses it with "That action is not available for this item." ([`DependencyGraphCanvas.tsx`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx), `ContextService.Actions.cs` in `src/backend/EtAlii.Adp.Context/`).
- **The property grid says reconnecting is done on the canvas**, by dragging a dependency's end to another node, but no reconnect gesture or command exists. The `.dis` follows the code: it sets `reconnectable: false` and keeps the sentence as the ends' read-only reason.
- **Adding a node to a graph that has no `elements` key fails.** Dropping a node into a file whose root is not a mapping, or which declares no `elements`, raises an out-of-range error in the writer instead of adding the key or refusing.
- **Undoing every edit does not always return the file it started from.** Over a scripted run of adds, renames, moves and removals, undoing them all leaves eleven of the twenty-six recorded documents different from what they were (standalone's parity transcript, `dependency-graph.transcript.json`).
- **There is no cycle rule**, by decision rather than by accident (section 3).

## 10. Choices made in the `.dis`

Where DISL had more than one reasonable reading, these were chosen:

- **Language id `net.etalii.adp.generic.dependencies`**, version `1.1.0` since the DISL 0.3 rewrite: the reverse-DNS form of etalii.net followed by the origin tag, which `language.origin` now carries.
- **Node label shown with a fallback.** The label is a CEL text (`label`, or the id when empty) with a `parse` write-back, so it stays editable inline even though its text is computed.
- **`DependsOn` label defaults to `""`**, which lets DISL's `omitDefaults` explain why an empty relation label has no key; the `.dis` still sets `omitDefaults: false` because node values are always written, even when zero or empty.
- **The palette tool uses `mode: "drop"`** and `after: "none"`: the node is dropped where it belongs and appears labelled "New node" without starting a label edit. Its initial values, and the `addNodeHere` operation that "Add node here" and a drop both run, put its left edge at the point's x and round the point's row to the nearest.
- **Menus.** A node's menu has two groups, `edit` (Rename…, Remove) and `add` (Add node to the right, Add node below), a dependency's one (Relabel…, Remove dependency); empty canvas offers "Add node here" and a finished drag "Depends on". No entry is hidden or disabled on a graph that could not be read, because the code offers them there too and its edits refuse instead.
- **Forms.** Every item states its label; the two read-only ends of a dependency are computed items with the ids `from` and `to` and the read-only reason as `readOnlyReasons`. A dependency end the file names but no node holds has no id CEL can read (4.9), so a runtime shows it as the file wrote it, as the code does.
- **Operations.** `addRight` and `addBelow` create a node and `connect` the selected node to it in one transaction.
- **Icons** are named as the standalone tool names them (`mdi-graph-outline`, `mdi-rectangle-outline`, `mdi-ray-start-arrow` and the menu icons).
- **`x-dependencies`** maps the specification's names to the standalone tool's wire ids: the palette tool `node` to `dependencies.toolbox.node`, dropping `dependencies.add-element`; each menu entry by its operation or kind (`DependsOn/editLabel` to `dependencies.relabel`, `DependsOn/delete` to `dependencies.disconnect`); each form row by its id or attribute.
- **`x-adp`** at the top level records the kind, catalogue state and implementations, which DISL has no property for.

## Sources

- Code: [etalii.adp.ide.standalone `src/diagrams/dependency-graph/`](https://github.com/etalii-adp/etalii.adp.ide.standalone/tree/develop/src/diagrams/dependency-graph), backend, client, api, tests, fixtures and example, at `b2a26925`; shared helpers `LineSplice`, `GestureIds` and `RowRounding` in `src/backend/EtAlii.Adp.Documents/`.
- Example: [`examples/example-1/services.dgr`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/src/diagrams/dependency-graph/examples/example-1/services.dgr), a storefront's service dependencies.
- The original specification, removed from standalone's tree and readable at the commit before `d573ad87`: `.spec-workflow/archive/specs/dependency-graph/requirements.md` and `design.md`.
- Catalogue: standalone [`docs/tools.md`](https://github.com/etalii-adp/etalii.adp.ide.standalone/blob/develop/docs/tools.md), row `generic/dependencies`; the Notion "Tools" database, row "Dependency graph".
- Specifications: [DISL](../../specifications/disl/DISL-specification.md) and [DID](../../specifications/did/DID-specification.md) in this repository.
- Project conversations: no recorded decision about this type's design beyond the implementation; the catalogue seeding and the backend-centralization work that moved its document store onto the shared lifecycle are the only mentions.
