# The diagram module client API

**Everything a diagram module's client may reach for, and nothing it may not.** One document, so a module author does not have to read the canvas library to find out what is offered — and so a name leaving module use is a change to a page rather than a discovery made by a broken build.

## How to read this document

**Each entry answers the same six questions, in the same order:** what the thing is for; whether a module needs it and what happens without it; its shape; a worked example taken from a real module; the guards that constrain it; and what to read next.

**An entry begins with its declarations.** The line `**Declarations:**` names, in backticks, every declaration the entry covers. Those lines together are what this document claims to cover, and the test reads them:

```
### Element types
**Declarations:** `ElementTypeDefinition`, `BuiltInShape`, `CustomShapeRef`
```

**An excerpt names its source.** A fenced code block is preceded by a `Source:` line linking the file it was taken from, and the block appears in that file verbatim, line endings aside:

```
Source: [`src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx`](../src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx)
```

**`dependency-graph` is the reference module.** Examples come from it wherever it exercises the aspect, so a reader following one module's code can find every excerpt in one place rather than assembling a picture from eight.

### What is authoritative here, and what is linked

**This document is authoritative for the module-facing surface**: which declarations a module may use, what each is for, and which are library-internal. When it disagrees with a module that reaches past the surface, this document is right and the module is the thing to change.

**It is not authoritative for mechanism.** How the canvas draws, how scrolling works, how a label is measured and how the stream is consumed are described where that code lives, and this document links them rather than restating them:

- [`src/client/src/diagrams/readme.md`](../src/client/src/diagrams/readme.md) — the stream, the view report and the module-side hooks
- [`src/client/src/canvas/elements/readme.md`](../src/client/src/canvas/elements/readme.md) — element geometry and shapes
- [`src/client/src/canvas/library/shapes/readme.md`](../src/client/src/canvas/library/shapes/readme.md) — the built-in shapes' outlines: which shapes have one, the text region and edge point each derives from it, and how to add a shape
- [`src/client/src/canvas/label/readme.md`](../src/client/src/canvas/label/readme.md) — label measurement and placement
- [`src/client/src/canvas/gesture/readme.md`](../src/client/src/canvas/gesture/readme.md) — the shared gesture layer
- [`src/client/src/canvas/scroll/readme.md`](../src/client/src/canvas/scroll/readme.md) — scrolling and the viewport

**And it is not a tutorial.** [`creating-a-diagram-module.md`](creating-a-diagram-module.md) walks a whole module from nothing to a drawn diagram; this document is what that walkthrough reaches into, entry by entry.

### How this document stays true

**It is checked against the code on every client test run, not reviewed.** The command is:

```bash
npm test -- src/diagramModuleClientApi.test.ts
```

from `src/client`. The test computes the module-facing surface by parsing the tree — what modules import, and what they supply by value — and fails when this document covers a name that no longer exists, omits one a module uses, or quotes an excerpt that has changed in its source.

**What a parse cannot tell you about the diagrams.** The test parses every diagram and checks it is the kind the document uses it as - a flowchart where a flowchart belongs, a sequence where a sequence does. That catches a diagram that has stopped rendering, which is otherwise silent, since a block that fails to parse shows as its source text rather than as an error. It cannot catch a diagram that parses and draws the WRONG FLOW: an arrow pointing the wrong way, a step in the wrong order, a call that never happens. Whether each picture tells the truth about the code is a reader's to judge, by eye.

**What the test does not yet hold.** It covers names a module imports, types a module supplies by value, and types a module file satisfies by existing. It does not yet cover the RETURN TYPES of the hooks a module calls - `DiagramStreamResult` from `useDiagramStream` among them - because a module reads their members without ever writing the type's name, so no set sees them. A member added to one of those types would leave every check green. Seven such return shapes exist today - six named types, and the unnamed pair `useCanvasRefusal` returns, which `client-centralization` task 3 added; an amendment to this specification is being raised to cover them, so this is a known gap rather than an oversight, and until it lands a change to one of them is caught by a reader rather than by this test.

**Every figure quoted below says what it counts and when it was read**, because a count with neither is a number a reader cannot check. Figures in this document were read at `267ee8f2`, where the test last ran green against them, unless their own sentence names another commit. **The test recomputes the sets, not the prose counts** - a sentence saying how many modules declare something, or how many guards walk module clients, is a timestamp, true at that commit and not re-checked afterwards.

## The shape of a module client

**Two pictures before the entries.** The first is how a module is found and mounted; the second is what happens while one diagram is open.

```mermaid
flowchart LR
    reg["client/register.ts<br/>exports registrations"]
    glob["the shell's glob<br/>src/diagrams/*/client/register.ts"]
    match["matches(mimeType)"]
    canvas["DiagramCanvas<br/>mounted with the module's Canvas"]

    reg --> glob
    glob --> match
    match -->|"the type is claimed"| canvas
    match -->|"nothing claims it"| none["PanelPlaceholder<br/>from the module's unsupported"]
```

**Nothing lists the diagram types.** The shell globs for `register.ts`, so adding a type means adding a module, and deleting the folder removes the type.

```mermaid
sequenceDiagram
    participant Backend
    participant Hook as useDiagramStream
    participant Model as DiagramModel
    participant Canvas as DiagramCanvas
    participant Module as the module's handler

    Backend-->>Hook: delta
    Hook->>Model: fold the delta in
    Model->>Canvas: the model to draw
    Canvas->>Module: a DiagramEvent - a request, not a report
    Module->>Backend: a command, through the module's own transport
    Backend-->>Hook: the resulting delta
```

**The loop is the whole architecture.** A gesture is a request the module answers by telling the backend; the change comes back as a delta like any other, so an edit by this client and an edit by another take the same path.

## Registration and discovery

**Declarations:** `ToolPanelRegistration`, `ToolContentProps`, `ToolClientModule`, `TextEditorPanel`

**What it is for.** A module tells the client which diagram types it can draw, and with what. This is the only wiring between a module and the shell: there is no list of diagram types anywhere in the client, exactly as there is none in the backend.

**Whether a module needs it.** Always, and it is the one file that cannot be omitted. The shell discovers modules by globbing `src/{diagrams,designers,editors}/*/client/register.ts` at build time. **A module without `register.ts` is not loaded and fails silently** — nothing reports a missing module, because nothing knew to expect it.

**Its shape.** `register.ts` exports `registrations`, an array of `ToolPanelRegistration`. Each entry answers two questions: which MIME types it speaks for (`matches`), and what to show (`Panel`, which for a diagram module is its canvas). A module that knows a type this build cannot draw supplies `unsupported` instead — a `description` and the `futureSpec` that would add it — so the explanation comes from the module that understands the notation rather than from a fallback in the shell. `ToolClientModule` is the shape of the module as a whole, which the shell reads; a module never imports it. `ToolContentProps` is what the shell hands the registered component: `projectId`, `entryId` and `path` name the document, `editorId` forces the editor family on an "Open as text" tab, and `initialLine` is the line to scroll to on a go-to-line.

Source: [`src/diagrams/dependency-graph/client/register.ts`](../src/diagrams/dependency-graph/client/register.ts)

```ts
export const registrations: ToolPanelRegistration[] = [
  {
    matches: (mimeType) => mimeType === "generic/dependencies",
    Panel: DependencyGraphCanvas,
  },
];
```

**The shared stylesheet is imported here, not in the canvas.** A module's own stylesheet and the canvas library's come in as side-effect imports at the top of the same file, so a module's styles load exactly when its registration does:

Source: [`src/diagrams/dependency-graph/client/register.ts`](../src/diagrams/dependency-graph/client/register.ts)

```ts
import type { ToolPanelRegistration } from "@client/shell/panels/toolPanelRegistration";
import { DependencyGraphCanvas } from "./DependencyGraphCanvas";
import "@client/canvas/canvas.css";
import "./dependency-graph.css";
```

**The workspace `package.json`.** Each module client is a private npm workspace package, named `@adp/diagram-<type>-client`. It declares the runtime dependencies the module's own code uses and nothing the library already provides.

**The guards.** `register.test.ts` in each module asserts the registration matches its own MIME types and no others. The shell's own registry test asserts the glob finds every module.

**Related.** [The shape of a module client](#the-shape-of-a-module-client) for where this sits in the whole.

## Editor analysis and schemas

**Declarations:** `EditorAnalysis`, `EditorFinding`, `EditorFindingSeverity`, `EditorCompletion`, `EditorCompletionList`, `EditorHover`, `editorHighlight`, `SchemaFiles`, `SchemaFile`, `schemaFileKey`, `resolveSchemaReference`, `useAuth`

**What it is for.** An editor module tells `TextEditorPanel` what it knows about its format through one asynchronous interface, and the panel draws everything: the underlines, the gutter marks, the counts on the status line, the completion list and the hover text. A module never draws any of them itself, so JSON, YAML and XML look and behave alike.

**Whether a module needs it.** Only an editor module that checks or completes its text. A diagram module never does; an editor that only colours its text passes `extensions` and no `analysis`.

**Its shape.** `EditorAnalysis` has `findings` (required), and `completions`, `hover` and `status` (each optional). Offsets are into the text as CodeMirror holds it, which is LF-only. An `EditorFinding` is `from`, `to`, `severity`, `message` (plain text, never markup) and `source` (who found it, such as `json-schema`). `EditorFindingSeverity` is `error`, `warning` or `info`. An `EditorCompletion` is `label`, `filterText`, `detail`, `info`, `from`, `to`, `insert` (with `${1}` tab stops applied as a snippet) and `boost`, which sorts an item up the list. `completions` answers an `EditorCompletionList` of `items`; `hover` answers an `EditorHover`, a `from`, `to` and `text`. `editorHighlight` is the one colour scheme, whose classes a module may put on text but never restyle.

`SchemaFiles` is a component that renders nothing: given the paths a document's schema references lead to, it reads each file over the tab's existing stream and reports every arrival, change and failure as a new map of `SchemaFile` (`loading`, `loaded` with its text and revision, or `failed` with its reason), keyed by `schemaFileKey` of the path. `resolveSchemaReference` decides where a reference points, before any URL arithmetic: a file of the project, a bundled schema, or no file with the reason, refusing a `..` that steps above the project root and anything with a scheme it does not bundle.

A module that marks a token of its own reads a class from `editorHighlight` (`editorHighlight.style([tags.namespace])`, as the XML editor does for a prefix) rather than writing a colour. A module whose checking is in the backend implements `EditorAnalysis` over the family's `Analyze` and `Complete` calls of `EditorService`, made on the `transport` that `useAuth` gives; see *Asking the backend about the text* in [creating-an-editor-module.md](creating-an-editor-module.md).

Source: [`src/editors/json/client/JsonEditorPanel.tsx`](../src/editors/json/client/JsonEditorPanel.tsx), [`src/editors/xml/client/XmlEditorPanel.tsx`](../src/editors/xml/client/XmlEditorPanel.tsx)

**The guards.** `resolveSchemaReference` is held to `src/fixtures/schema-reference-cases.json`, the cases the XML editor's C# twin is held to as well. `src/client/src/editors/analysisExtensions.test.tsx` checks that each severity carries a line style and a gutter mark as well as a colour.

**Related.** [Registration and discovery](#registration-and-discovery) for `TextEditorPanel`, and [creating-an-editor-module.md](creating-an-editor-module.md) for the JSON editor as the worked example.

## The stream and the model

**Declarations:** `useDiagramStream`, `DiagramModel`, `DiagramModelElement`, `DiagramModelConnection`, `Delta`, `Element`

**What it is for.** A diagram's contents arrive as a stream of deltas from the backend, and the canvas draws a model. `useDiagramStream` owns the transport, the lifecycle, the retry and the state; a module supplies an empty model and a function that folds one delta into it.

**Whether a module needs it.** Always, and **only through this hook**. `diagramStreamOpensOnlyInHook.test.ts` forbids opening a diagram's delta stream anywhere else and names any offender. A module wraps the hook rather than calling the transport, so reconnection, teardown on identity change, and the loading and failed states are the same everywhere.

**Its shape.** The hook takes the project, the path, an empty model and a delta-folding function, and returns the model with `loading`, `failed`, the client, and `moveElementTo` — the one call that arranges an element where it was dropped, which resolves to the backend's refusal as a sentence or to `""` when the move was accepted, and shows that refusal on the canvas's one refusal line itself. A module's wrapper adds whatever else its canvas needs on top — a view report — built on the client it returns. **A module does not build its own arrangement move:** until client-centralization task 8 each of fourteen modules did, one had drifted to report a fixed sentence in place of the backend's reason, and `diagramStreamOpensOnlyInHook.test.ts` now names any module that builds one again. **Re-parenting is a different operation on the same request** — a `newParentId` in place of a position — and a module that re-parents, as `mindmap` does, still builds that call itself.

**`loading` and `failed` are for a module's own decisions, never for drawing.** The hook reports them to the frame the shell puts around every canvas, which says opening, reconnecting and unavailable in one appearance, in the backend's own words where it gave some (see [Refusals and status](#refusals-and-status)). A module reads them to hold back what would be false before the diagram is read — an empty-diagram explanation, a view report — and draws no status of its own.

Source: [`src/diagrams/dependency-graph/client/useDependencyGraphStream.ts`](../src/diagrams/dependency-graph/client/useDependencyGraphStream.ts)

```ts
export function useDependencyGraphStream(projectId: Uint8Array, path: readonly string[]): DependencyGraphStream {
  const { watchId } = useContextConnection();
  const { model, loading, failed, client, moveElementTo } = useDiagramStream(projectId, path, emptyModel, applyDelta);

```

**What the library reads.** The canvas is handed a `DiagramModel`, whose members are `elements`, `connections` and `background`. An element (`DiagramModelElement`) carries `id`, `type`, `x`, `y`, `width`, `height`, `label`, `labelAt`, `parentId`, `style` and `payload`. A connection (`DiagramModelConnection`) carries `id`, `type`, `sourceId`, `targetId`, `sourceAnchor`, `targetAnchor`, `sourceAttachment`, `targetAttachment`, `label`, `className`, `title`, `editValue`, `waypoints`, `style` and `payload`, which a relation type's conditions, stroke ladder and bowed arc read as an element's declarations read its own.

**`payload` and `background` are opaque on purpose.** The library walks them only by a path a module's own declaration names, so nothing in the library learns a diagram type's schema. A module unpacks its own generated payload types itself; those types are the module's, not the surface's, and are excluded from the API this document covers.

**An element naming no declared type renders as a visible fallback box** rather than disappearing — a mapping bug shown, not hidden.

**Authentication is not part of this surface.** `useAuth` is reached by several modules' *test* files to build an authenticated client, and in production code only by the XML editor, for the `transport` its analysis calls are made on; that one use is in [Editor analysis and schemas](#editor-analysis-and-schemas). Any other module that finds itself needing it in production code is doing something this document does not cover, and that is the signal rather than an omission.

**Related.** [The view report](#the-view-report) for telling the backend what is visible, and [`src/client/src/diagrams/readme.md`](../src/client/src/diagrams/readme.md) for the stream's own mechanism.

## The definition

**Declarations:** `DiagramDefinition`

**What it is for.** One object says what a diagram type IS — what its elements are, how they connect, what may be dragged, what a user may do to them. The library reads it and draws; a module writes no rendering code.

**Whether a module needs it.** Always, and it is the largest thing a module writes by hand. **Everything below is a member of this one object.**

**Its shape.** `elementTypes`, `relationTypes`, `toolbox`, `layout`, `dragging`, `extent`, `snap`, `dropTarget`, `background`, `chrome`, `actions`, `backgroundMenu`, `backgroundPlacement`, `connectOnRightDrag`, `dragBounds`, `acyclic` and `filter`. Only `elementTypes` and `relationTypes` are structural; the rest declare behaviour and may be omitted. `backgroundPlacement` goes with `backgroundMenu`: it maps the canvas point of a background right-click into the module's own coordinates before the placement is selected, for a canvas whose units are not the backend's (the timeline's are seconds and rows).

**Which of these are actually used, measured rather than assumed.** Parsing all 14 modules that declare a definition — in both shapes it is written in, a typed constant and a function returning one — `elementTypes`, `relationTypes`, `layout` and `dragging` are set by all 14; `actions` by 8; `snap` by 2; `connectOnRightDrag` by 2; `acyclic`, `background`, `backgroundMenu`, `dragBounds`, `dropTarget` and `extent` by one each (read at `5b44878d`, when the functional decomposition graph became the fourteenth, and the first to declare `acyclic`). **`toolbox` and `chrome` are set by none**, and their entries below say so and excerpt a type-checked example instead of an invented one.

**It is validated at declaration, not at draw time.** A module wraps its definition in `assertValidDiagramDefinition`, so a contradictory declaration fails where it is written rather than as a blank canvas later.

**The reference module compiles its definition** from its bundled specification ([below](#compiling-it-from-the-bundled-specification)), so the declarations the entries below excerpt from it come from its former hand-written definition, kept in a test as the oracle the compiled one is held to: they are what `compileNotation` produces for it, written out, but for the differences that test names.

Source: [`src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx`](../src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx)

```ts
export const DEPENDENCY_GRAPH_DEFINITION: DiagramDefinition = assertValidDiagramDefinition(compileNotation(parseDisl(disText), DEPENDENCY_GRAPH_BINDINGS));
```

```mermaid
classDiagram
    class DiagramDefinition
    DiagramDefinition --> ElementTypeDefinition : elementTypes
    DiagramDefinition --> RelationTypeDefinition : relationTypes
    DiagramDefinition --> ToolboxDefinition : toolbox
    DiagramDefinition --> ChromeDeclaration : chrome
    DiagramDefinition --> ActionDeclaration : actions
    DiagramDefinition --> SnapDeclaration : snap
    DiagramDefinition --> DropTargetDeclaration : dropTarget
    DiagramDefinition --> AcyclicRule : acyclic
    ElementTypeDefinition --> CustomShapeRef : shape
    RelationTypeDefinition --> CustomRouteRef : route
```

### Element types and shapes

**Declarations:** `ElementTypeDefinition`, `CustomShapeRef`, `ShapeBounds`, `AnchorEnablement`, `AnchorPositions`, `AnchorSet`, `NamedAnchorPoint`, `CompassPosition`, `SideFraction`, `BuiltInShape`, `ShapeKind`, `ShapeSelection`, `CustomShapeState`, `SizingRule`, `ElementStyle`, `BoundElementStyle`, `DataAttributes`, `ClassDeclaration`, `DecorationDeclaration`, `DecorationGlyph`, `SegmentDeclaration`, `AlongAnchors`, `EdgeName`, `EdgeAttachment`

**What it is for.** What kinds of thing the diagram has, and how each is drawn.

**Whether a module needs it.** Always — all 14 modules declare it (read at `5b44878d`), and it is the only member with no useful default.

**Its shape.** An `ElementTypeDefinition` carries `id`, `shape`, `style`, `boundStyle`, `classNames`, `data`, `tooltip`, `label`, `labels`, `decorations`, `compartments` (see *Lists inside an element*), `links` (see *Links*), `actions`, `anchors`, `sizing`, `resize`, `segments`, `draggable`, `editOnDrop`, `deletable`, `selectable`, `beneathConnections` and `centreOf`, the payload path of a list of element ids: an element of the type is drawn at the mean of those elements' centres, as a causal loop's badge is among the variables it runs through, and keeps its own position while the list is empty or names an element the model lacks. A type whose shape is not one of the built-ins names a `CustomShapeRef` instead, which carries `customShape`, `render`, `edgePoint` and `anchors`; a renderer is handed `ShapeBounds` — `x`, `y`, `width`, `height`.

Source: [`src/diagrams/dependency-graph/client/dependencyGraphCompiledDefinition.test.tsx`](../src/diagrams/dependency-graph/client/dependencyGraphCompiledDefinition.test.tsx)

```ts
  elementTypes: [
    {
      id: "node",
      // THE SHAPE THIS MODULE USED TO DRAW ITSELF. `span` is the same shared component the
      // custom renderer wrapped - the renderer existed to add three classes and an edge rule,
      // both of which are declarations now.
      shape: "span",
      classNames: [
        { className: "dependency-graph-element canvas-element" },
        { className: "dependency-graph-node canvas-node" },
      ],
```

**Three shapes whose text stays inside them.** `BuiltInShape` includes `"superellipse"`, a squircle that reads as a box with its corners taken off; `"trapezoid"`, narrower at the bottom than the top; and `"diode"`, a rectangle closed on the right by a semicircle, so the shape points the way its relations run. Each has an outline, and its text region and edge point come from that outline rather than from the bounding box - which is what keeps a wrapped label off a slanted or curved edge and makes a connection meet the drawn line. How the outline works, and how to add a shape, is in [`src/client/src/canvas/library/shapes/readme.md`](../src/client/src/canvas/library/shapes/readme.md).

**A person.** `BuiltInShape` includes `"person"`, DISL 0.5's `person` (§6.7): a box with rounded corners of radius 8 over the bounds, never more than half its smaller side, and a round head of radius 10 centred 8 above its top edge, the body drawn over the head's bottom. Its outline is the body's rectangle, so a connection meets the box and a label sits in it; the head stands above the bounds and is still the element: a drop on it lands on the element and the fitted view includes it. A `styled-box` whose silhouette is `"Person"` draws the same figure from the same geometry (`shapes/person.ts`) and counts its head the same way. A DISL specification reaches the library's `"person"` through the shape catalog (`disl/shapeCatalog.ts`) as `"person"` or with `"radius": 8`, the one corner the library draws, and the library's `"cylinder"` as `{ "type": "cylinder", "params": { "cap": 12 } }`, the one cap the library draws.

**A banner in phases, and connections that attach anywhere along an edge.** `BuiltInShape` includes `"arrow-banner"`, a horizontal band pointed at its right end. A type on it may declare `segments` - a `SegmentDeclaration` - which divides the band into up to `max` phases, the count and the inner boundaries bound to the model, each phase with its own class name and tooltip, divided by chevrons or lines; with `draggableBoundaries` an inner boundary is a handle whose drag arrives as `SegmentBoundaryMoved`. Anchors of kind `"along"` - `AlongAnchors` - name the edges (`EdgeName`) a connection may attach to anywhere, optionally counted per segment; the drawn connection reports each end as an `EdgeAttachment`, a fraction along an edge or a segment's stretch of it, which the model hands back so the end follows a resize. The first declarer is `gartner-hype-cycle-graph`.

**A source drawn by edge, and the editor opened on a drop.** An `AnchorEnablement` may say `attachDrawnBy: "edge"`: the type's named anchors are still handles a connection starts from, but every end on it is drawn by edge intersection towards the other end - on the round outline itself for an `ellipse` - and `ConnectionDrawn` carries no anchor name for it, so a small circle starts a line from a handle and the line leaves it facing its target. It is refused on `along` anchors. An `ElementTypeDefinition` may say `editOnDrop: true`: after a toolbox drop the canvas remembers the drop point for five seconds, and the first new element of that type whose model bounds contain it is selected and its `activate` gesture dispatched - which opens the label editor through the module's own rename - while any other gesture forgets the drop. The hype cycle's trigger and note are the first declarers.

**Height resize.** `resize` is read only when `sizing` is `"user"`. **Omitted means `"width"`**: left and right handles, which is what every user-sizable type had before. `"both"` adds top and bottom handles, for an element whose height is its own content rather than a shared constant. The drag arrives as `ElementResized`, whose `side` - a `ResizedSide` - is then `"top"` or `"bottom"` as well as `"left"` or `"right"`, and whose `bounds` are the whole resized rectangle. A handler that assumed a horizontal edge has to read `side` once a type declares `"both"`.

**The functional decomposition graph is the first module to declare them** (read at `04cc58df`). Three of its four named types are drawn as the three shapes, the fourth as a `"parallelogram"`:

Source: [`src/diagrams/functional-decomposition-graph/client/FdgCanvas.tsx`](../src/diagrams/functional-decomposition-graph/client/FdgCanvas.tsx)

```ts
const SHAPES: Readonly<Record<FdgElementType, ElementTypeDefinition["shape"]>> = {
  [FdgElementTypes.uiElement]: "superellipse",
  [FdgElementTypes.dataElement]: "parallelogram",
  [FdgElementTypes.action]: "trapezoid",
  [FdgElementTypes.function]: "diode",
  [FdgElementTypes.comment]: "box",
};
```

Its Comment is the only type sized in both directions, and its one label wraps inside it - so it is also the first declarer of the multiline editor [below](#labels-and-bindings):

Source: [`src/diagrams/functional-decomposition-graph/client/FdgCanvas.tsx`](../src/diagrams/functional-decomposition-graph/client/FdgCanvas.tsx)

```ts
const COMMENT_TYPE: ElementTypeDefinition = {
  id: FdgElementTypes.comment,
  shape: SHAPES[FdgElementTypes.comment],
  classNames: [
    { className: "canvas-element fdg-element", on: "element" },
    { className: `canvas-node fdg-${FdgElementTypes.comment}`, on: "shape" },
  ],
  labels: [{ text: { path: "payload.text" }, editable: true, wrap: true, className: "fdg-comment-text" }],
  anchors: { kind: "edge", enabled: false, visible: false },
  sizing: "user",
  resize: "both",
};
```

**The guards.** `assertValidDiagramDefinition` rejects a type whose shape and anchors disagree. An element whose `type` matches no declared id draws a visible fallback box rather than vanishing.

**Their members.** `AnchorEnablement` carries `visible`, `enabled`, `edgeSides`, `attachDrawnBy`; `NamedAnchorPoint` carries `x`, `y`, `name`; `SideFraction` carries `side`, `at`, `name`; `ShapeSelection` carries `path`, `cases`, `fallback`; `CustomShapeState` carries `selected`, `dragging`, `connectTarget`; `SegmentDeclaration` carries `count`, `max`, `boundaries`, `classNames`, `tooltips`, `divider`, `draggableBoundaries`; `AlongAnchors` carries `kind`, `edges`, `regions`; `EdgeAttachment` carries `edge`, `region`, `at`; `ElementStyle` carries `fill`, `stroke`, `strokeWidth`, `dash`, `cornerRadius`, `labelTypography`; `BoundElementStyle` carries `fill`, `stroke`, `labelColor`, `silhouette`; `ClassDeclaration` carries `className`, `when`, `on`; `DecorationDeclaration` carries `glyph`, `anchor`, `each`, `step`, `from`, `to`, `radius`, `width`, `height`, `d`, `marker`, `text`, `textAt`, `textAnchor`, `typography`, `className`, `markerEnd`, `tooltip`, `data`, `accessibility`, `when`, `mirrored` - the condition under which a `circular-arrow`, the near-complete circle centred on `from` with a gap for its head, turns anticlockwise rather than clockwise - and `pack`, whose `step` makes it a slot in a packed row: it is drawn at `from` moved by `step` once for each packed decoration before it on the element that is drawn, so a row of badges closes up around the ones an element does not wear.

### Relation types, routes and constraints

**Declarations:** `RelationTypeDefinition`, `CustomRouteRef`, `AcyclicRule`, `BuiltInRoute`, `RouteKind`, `RouteEnds`, `RouteLabelRule`, `ConnectionStyle`, `CustomMarkerRef`, `MarkerKind`, `EndpointConstraint`, `Cardinality`, `StrokeLadder`, `StrokeRung`, `StubDeclaration`, `StubLabel`, `StubSide`, `DashChoice`

**What it is for.** What connects to what, how the line is routed, and what connections are forbidden.

**Whether a module needs it.** `relationTypes` is declared by all 14 modules. `acyclic` is declared by one, the functional decomposition graph (both read at `5b44878d`).

**Its shape.** A `RelationTypeDefinition` carries `id`, `route`, `style`, `label`, `adjustable`, `movableEnds`, `selectable`, `className`, `lineClassName`, `hitClassName`, `emptyRelease`, `hideWhenAttachmentHidden`, which leaves a connection undrawn while the segment one of its ends attaches to is not shown, `strokeLadder`, which chooses a connection's stroke width from one payload number by thresholds and paints each width with a class of the module's stylesheet, `dashWhen`, a `DashChoice` that dashes a connection while a condition on its payload holds, in place of `style.dash`, and `stub`, which draws a connection whose `targetId` is empty - a relation without a target (DISL 4.9) - as a straight line out of the middle of one side of its source with a label beside it, where without a stub such a connection is not drawn at all. A route the built-ins do not cover is a `CustomRouteRef` — `customRoute` and `path`, which is handed the two end boxes and the connection itself, so a route can bow by the connection's payload. An `AcyclicRule` carries `relationTypes`: the relation ids a cycle may not be formed from, so a "depends on" edge can refuse a cycle while other relation types stay free to form one.

Source: [`src/diagrams/dependency-graph/client/dependencyGraphCompiledDefinition.test.tsx`](../src/diagrams/dependency-graph/client/dependencyGraphCompiledDefinition.test.tsx)

```ts
  relationTypes: [
    {
      id: "depends",
      route: dependencyRoute,
      style: { endMarker: "arrow" },
      label: { placement: "midpoint", offset: -6, editable: true },
      className: "dependency-graph-relation",
      lineClassName: "dependency-graph-relation-line",
      hitClassName: "dependency-graph-relation-hit",
```

**The functional decomposition graph is the first module to declare `acyclic`** (read at `5b44878d`): its four ownership relations may form no cycle among themselves, while `shows`, which is navigation, is deliberately left out so a navigation loop stays drawable:

Source: [`src/diagrams/functional-decomposition-graph/client/FdgCanvas.tsx`](../src/diagrams/functional-decomposition-graph/client/FdgCanvas.tsx)

```ts
  // The four ownership relations form a forest; Shows is navigation and may loop (Requirement 5.4).
  acyclic: [{ relationTypes: FDG_OWNERSHIP_RELATIONS }],
```

**Which relation a connect gesture draws.** The press sees only the source, so it starts with the `activeTool` relation if the runtime names one, and otherwise with the first relation whose source admits the element. Over a target, the canvas keeps that relation if it admits the target's type. If it does not, and no `activeTool` is named, it draws the first relation that admits BOTH ends - the source's anchor included - and the connect checks then run on the relation actually drawn. So a notation whose relations are told apart by what they point at can draw each of them from the same element: the functional decomposition graph's screen owns a child screen, an action, data or a function through four different relations. **A named `activeTool` still wins**: a target it does not admit is refused, not drawn as some other relation. A definition whose starting relation already admits every target it meets never reaches the search, so it draws exactly what it did before this behaviour arrived (`5ce04fbc`).

**Their members.** `RouteEnds` carries `source`, `target`, `sourceEdge`, `targetEdge` (the edge an end is attached along, which a `cubic-bezier` route leaves and meets square on); `RouteLabelRule` carries `placement`, `offset`, `editable`; `ConnectionStyle` carries `stroke`, `strokeWidth`, `dash`, `startMarker`, `endMarker`, `cornerRadius`; `CustomMarkerRef` carries `customMarker`, `path`; `EndpointConstraint` carries `elementTypes`, `anchors`; `Cardinality` carries `maxFromSource`, `maxIntoTarget`, `perPair` - `"ordered"` allows one connection of the type per direction between two elements, `"unordered"` one per pair; `StrokeLadder` carries `path`, `rungs`, `otherwise`, `classNames` - the first rung that holds gives the width, `otherwise` the width where none does, and `classNames` the class each width is painted by; `StrokeRung` carries `test` (`absent`, `atLeast`, `atMost`, `above` or `below`), `value`, `width`; `StubDeclaration` carries `length`, `side` (a `StubSide`: `right`, `left`, `top` or `bottom`), `className`, `dash`, which dashes the stub's line in place of the relation's, `label` - the stub is the connection, pressed, selected and followed through a drag as one, and draws no arrowhead; `StubLabel` carries `text`, resolved against the connection's payload, `side` (`above` or `below`), `distance` (6 unless stated), `inset` (8 unless stated, past the source), `className`; `DashChoice` carries `when`, `dash`. A dash is painted on the line itself, inline, as a connection's own `style.dash` is, and that comes first, then `dashWhen` where it holds, then the relation's `style.dash`.

**Their members.** `RelationTypeDefinition` carries `endpoints`, `adorn`, `strokeLadder`, `dashWhen`, `stub`.

### Layout, dragging, snap and extent

**Declarations:** `SnapDeclaration`, `DropTargetDeclaration`, `LayoutDefinition`, `LayoutMode`, `RowPackedDeclaration`, `LayoutToggleDeclaration`, `DraggingPolicy`, `SnapAxis`, `DeclaredNumber`, `snapToStep`

**What it is for.** Where elements may go and how they move.

**Whether a module needs it.** `layout` and `dragging` are declared by all 14 modules (read at `5b44878d`); `snap` by 2, `dropTarget`, `extent` and `dragBounds` by one each. Omitted, a diagram lays out by its default mode and drags freely.

**Its shape.** A `SnapDeclaration` carries `x` and `y`. A `DropTargetDeclaration` carries `parentPath`, `ring`, `preview` and `group`. `dragging` is a policy rather than an object, and the runtime config can override it per render without a remount.

Source: [`src/diagrams/dependency-graph/client/dependencyGraphCompiledDefinition.test.tsx`](../src/diagrams/dependency-graph/client/dependencyGraphCompiledDefinition.test.tsx)

```ts
  snap: { y: { step: ROW_HEIGHT } },
  layout: { modes: ["manual"] },
  dragging: "enabled",
```

**Their members.** `LayoutDefinition` carries `modes`, `dragUnderAutomaticLayout`, `treeDirection`, `rowPacked`, `modeOverrides`, `toggle`, `pinned`, `tiers`; `RowPackedDeclaration` carries `width`, `gap`, `types`, `rowStep`, `followConnections`; `LayoutToggleDeclaration` carries `caption`, `on`; `SnapAxis` carries `step`, `origin`.

**A second layout mode, switched by a toggle.** A definition allowing two modes may declare `toggle: { caption, on }`: the library draws a toggle button with that caption directly below the filter box's legend, which is why the validator requires a `filter` beside it, and switches between the first mode and `on`. The active mode is the canvas's own view state, so a module need hold nothing; `layout-mode-changed` is still raised on every switch for a module that wants to know, and a host that sets `activeLayoutMode` in the runtime config keeps the last word. Without a toggle, a definition allowing several modes gets the switcher row, whose clicks now switch too.

**`row-packed`** draws every element at `rowPacked.width` and packs them along the rows they already sit on, in the order of their manual x, each at least `rowPacked.gap` after the one before it on its row, plus the width of any `before` label, so a name drawn to an element's left never overprints its neighbour; elements starting at one x on different rows share it. It is for diagrams whose x means time, read for their order rather than their durations. With `rowPacked.types` only elements of the listed types take the width, and every other keeps its own size and is still placed in order; with `rowPacked.rowStep` an element occupies every row line its span crosses, from `floor(top / rowStep)` to `floor((bottom - 1) / rowStep)`, so a box two rows tall keeps both clear. `rowPacked.width` may be bound, and is then read per element - the hype cycle draws a trend at its share of the width by the phases it shows - and with `rowPacked.followConnections` an element starts no earlier than halfway along every element already placed that connects into it, so causes stand to the left of their effects. A drop under it is raised in the model's own space: the layout's inverse interpolates between the two elements the pointer fell between, so a module places a dropped element exactly as it does in its manual mode.

**`modeOverrides`** replace parts of the definition while a mode is active, merged shallowly as the runtime's `definitionOverrides` are and beneath them. It is how a mode switches off what does not make sense in it: the hype cycle's compact mode redeclares its trend type with `sizing: "model"`, `draggable: false` and no draggable boundaries, sets `dragging: "disabled"`, and declares `chrome: { rulers: [] }`. `layout` itself cannot be overridden, and the validator checks each mode's merged definition as it checks the base.

**A mode that places by the whole document needs the whole document.** A module whose backend culls to the viewport reports an unbounded view while such a mode is on, as the [hype cycle graph](../src/diagrams/gartner-hype-cycle-graph/client/GhgCanvas.tsx) does on `layout-mode-changed`; otherwise the packing is computed over whatever is on screen and repacks as the reader pans.

**`snapToStep(value, step)` is the one rounding rule for a lattice**, halves away from zero and never negative zero - the rule a declared `snap` drags by, and the one the backend persists a row with. A module that needs a row index for an id divides it back out, `snapToStep(y, ROW_HEIGHT) / ROW_HEIGHT`, rather than rounding again; the dependency graph and the timeline each had their own copy until `client-centralization` task 9.


**Locked elements.** `pinned` on the layout is a path that says whether the reader put an element where it is. Such an element keeps the model's position under every automatic mode, and a layout places the others around it. A drag under an automatic mode raises `ElementMoved` for the dragged element, as it always has; a module that stores the position and marks the element pinned has locked it, and the mode stays automatic for the rest.

**The radiating layout.** The mode `tiered-force` places element types on rings from the centre outwards, as `tiers` lists them, draws connected elements together and keeps every pair clear of each other, whatever their sizes. The same model gives the same picture every time. When the model changes while the diagram is open the layout starts from where the picture is, so one change moves the rest a little; and a change that places nothing differently, such as a label, moves nothing at all. A type no tier names goes on a ring beyond the last. A move this layout makes is drawn as a short motion from the old place to the new, and as a jump where the reader's system asks for reduced motion.
### Background

**Declarations:** `DiagramBackground`, `AxisDeclaration`, `BackgroundDeclaration`, `BandDeclaration`, `GridlineDeclaration`, `MarkDeclaration`, `RegionDeclaration`

**What it is for.** What sits behind the elements rather than among them — the axes, bands, gridlines, regions and marks of a diagram whose space means something, such as a Wardley map's evolution axis.

**Whether a module needs it.** One of the 14 modules declares a background (read at `5b44878d`). A diagram whose position carries no meaning declares none, and the canvas draws plain ground.

**Its shape.** A background binds against the model's own `background` value by a path the module declares, so the library draws it without learning the diagram's schema.

**Their members.** `AxisDeclaration` carries `orientation`, `title`, `startLabel`, `endLabel`, `className`, `typography`; `BackgroundDeclaration` carries `className`, `bands`, `axes`, `gridlines`, `regions`, `marks`; `BandDeclaration` carries `each`, `orientation`, `start`, `end`, `label`, `edge`, `className`, `typography`, `when`; `GridlineDeclaration` carries `orientation`, `at`, `className`; `MarkDeclaration` carries `each`, `x`, `y`, `glyph`, `radius`, `width`, `label`, `labelOffset`, `labelAnchor`, `tooltip`, `className`, `typography`, `when`; `RegionDeclaration` carries `each`, `x`, `y`, `width`, `height`, `label`, `className`.

### Labels and bindings

**Declarations:** `LabelColumn`, `LabelDeclaration`, `LabelFit`, `LabelPlacement`, `LabelRule`, `LabelSlot`, `LabelStack`, `LabelTypography`, `LabelUnderline`, `Binding`, `BindingPath`, `CollectionBinding`, `Condition`, `FieldBinding`, `NumberFormat`, `PartsBinding`, `TemplateBinding`, `TemporalFormat`

**What it is for.** What text an element or connection shows, where, and where that text comes from.

**Whether a module needs it.** Any module whose elements carry text. A binding reads a value from the model instead of repeating a constant, which is what keeps a label tracking the document.

**Its shape.** A label is declared with a placement and a typography; its text is a `Binding` — a literal template, a field read by path, a collection, or parts assembled together — optionally gated by a `Condition` and formatted by a number or temporal format.

**A line fitted at the size its stylesheet draws it.** A label's `fit` (`LabelFit`: a `fontSize` and an `inset`) says the size a stylesheet draws it at, which the library does not see: a `truncate`d label is cut to the characters of that size that fit the box's width less the inset on either side, and a `wrap`ped label placed by an `offset` is broken at spaces into lines of that capacity, a word longer than a line cut, laid out from the offset `stack.lineHeight` apart (the font size times 1.4 when no stack is stated), its inline editor offered only while it is one line. An empty text draws nothing. The C4 card's type line and description are the first to declare it, compiled from their specification's styles (`labelFit` below).

**A wrapped label is edited in a multiline editor.** A label declaring `wrap: true` is laid out as wrapped text inside the shape's text region, and when it is editable its inline editor opens over that same region as a text area rather than a one-line field. There, **Enter types a newline**; Ctrl+Enter or Cmd+Enter commits; Escape cancels; and blur commits, as it does in the one-line field. The commit is the same `LabelCommitRequested`, so a module's handler does not change - but its `value` can now contain newlines, and a module whose document keeps a label on one line has to decide what a newline means there. The functional decomposition graph's Comment is the first to declare it (read at `04cc58df`); its declaration is excerpted under [Element types and shapes](#element-types-and-shapes). The mechanism is [`src/client/src/canvas/label/readme.md`](../src/client/src/canvas/label/readme.md)'s.

**Their members.** `LabelColumn` carries `text`, `insetX`, `align`, `className`, `truncate`; `LabelDeclaration` carries `wrap`, `text`, `placement`, `slot`, `offset`, `anchorTo`, `stack`, `typography`, `editable`, `truncate`, `fit`, `when`, `underline`, `tooltip`, `className`, `align`, `insetX`, `editorBox`, `columns`; `LabelFit` carries `fontSize`, `inset`; `LabelRule` carries `placement`, `insetTop`, `insetHeight`, `insetX`, `truncate`, `editable`; `LabelStack` carries `lineHeight`, `start`; `LabelTypography` carries `fontSize`, `fontWeight`, `fontStyle`, `color`, `scaleWithView`; `LabelUnderline` carries `style`, `when`, and is a wavy line the library draws under the label in the label's own classes and fill, as wide as the text the browser measures, because Chromium draws a stylesheet's `underline wavy` straight on SVG text; `CollectionBinding` carries `path`, `each`, `when`, `join`; `FieldBinding` carries `path`, `when`, `plural`, `number`; `NumberFormat` carries `times`, `plus`, `modulo`, `round`, `format`; `PartsBinding` carries `parts`, `join`, `when`; `TemplateBinding` carries `template`, `when`.

### Lists inside an element

**Declarations:** `CompartmentDeclaration`, `CompartmentGroup`

**What it is for.** Rows inside an element that are things in their own right - a specification's tasks, a location's pull requests - grouped under headings that fold.

**Whether a module needs it.** Only a module whose elements hold child entries the reader should see without their becoming elements. A label bound to a collection is enough where the lines are only text; a compartment is for rows that have an id.

**Its shape.** An element type declares `compartments`, each naming the path to its rows, how a row reads its id and its text, and the line heights it is laid out with. `groupBy` gives one heading per value of a field, in the declared order, each showing its title and its count; a group with no row is not drawn, and a row whose value is none of the declared ones goes under `otherTitle` rather than disappearing. `orderBy` orders rows within a group, with rows lacking the value last.

**Which headings are folded is the model's to say.** `collapsed` is a path to the keys of the folded headings: a group's `value`, or the compartment's `id` when it is not grouped. The library holds no collapse state. Pressing a heading, by pointer or by Enter or Space, raises `CompartmentToggled`, and the heading stays as it is until the model changes.

**A row can be the selection.** A row that has an id takes a press, or Enter or Space when focused, and becomes the selection as `{ kind: "row", id }`; the press does not reach the element it is in. The property grid and the shared menu then serve the row through the same selection an element uses, so a module writes nothing for it beyond answering for that id on its backend. A row the model gave no id is drawn and takes no press. Delete and the declared shortcuts pass a selected row by.

**The element grows with its rows.** A row is one line, shortened with an ellipsis and carrying its full text as a tooltip, so the height is the declared `top`, one `headingHeight` per heading, one `rowHeight` per visible row and the `bottom` inset. An element with rows is at least that tall, whatever height the model gives it, and its connections follow.

**Their members.** `CompartmentDeclaration` carries `id`, `rows`, `rowId`, `text`, `link`, `orderBy`, `groupBy`, `title`, `heading`, `collapsed`, `top`, `headingHeight`, `rowHeight`, `bottom`, `insetX`, `rowIndent`; `CompartmentGroup` carries `value`, `title`.

**A list that is one fixed line.** `heading: "none"` draws a compartment's rows alone: no heading line, no count, and nothing to fold it by, so it is never folded whatever the model's `collapsed` says. It is for a line of an element that is written as a list of one, such as a location's folder with its link at the row's end.

### Links

**Declarations:** `LinkDeclaration`

**What it is for.** A link an element or a row carries - a web page, a file - drawn as a small symbol the reader can follow.

**Whether a module needs it.** Only a module whose document holds links.

**Its shape.** An element type declares `links`, each with the path to the link and where its symbol sits, measured from the element's top-right corner; a compartment names a row's link with its own `link` path, and the symbol is drawn at the row's end. Nothing is drawn where the model has no link. A press, or Enter or Space when focused, raises `LinkActivated`; it selects nothing and drags nothing, and the symbol says where it leads as its tooltip and its accessible name.

**A module does not open a link.** Where the canvas owns its selection the library opens it, by the page's one rule: an `http` or `https` address in a new tab that gets no handle on the page, a path inside the project in the workspace tree, relative to the document the canvas draws, and anything else shown in a notice with its location offered for copying and never followed. The module hears `LinkActivated` afterwards and usually ignores it.

**Their members.** `LinkDeclaration` carries `id`, `link`, `at`, `label`.

### Actions, shortcuts and enablement

**Declarations:** `ActionDeclaration`, `ActionInvocation`, `ActionTarget`, `DeclaredFlag`, `ElementGesture`

**What it is for.** What a user may do to an element, a connection or the diagram, and how each is invoked.

**Whether a module needs it.** Eight of the 14 declare `actions` on the definition (read at `5b44878d`); others declare them per element type, which is where the reference module's hand-written definition put them. Its compiled definition declares them on the definition, from its specification's context menus.

**Its shape.** An `ActionDeclaration` carries `id`, `invokedBy`, `appliesTo`, `enabled`, `label`, `when`, `backendKey` and `keepsWhereDrawn`. **A shortcut is described as data, never wired by hand**: the module says which key invokes which action id, and the library derives the key set and dispatches.

**`backendKey` is the key the backend knows the action by, and the library sends it.** The backend's context table is keyed by keystroke, so an action the backend carries out names its key here; when the action is invoked — by its key, by a gesture or from the shared menu — the library sends that key against the target, shows nothing itself, and raises `action-refused` if the backend says no. **It is the declared key that travels, not the pressed one**: `mindmap` fires `add-child` on both Insert and Tab and declares `backendKey: "Insert"`, because Insert is the key the backend knows. A module does not build or send a keystroke itself, and a guard forbids it.

Source: [`src/diagrams/dependency-graph/client/dependencyGraphCompiledDefinition.test.tsx`](../src/diagrams/dependency-graph/client/dependencyGraphCompiledDefinition.test.tsx)

```ts
      actions: [
        { id: "rename", backendKey: "F2", invokedBy: [{ kind: "shortcut", key: "F2" }], appliesTo: [{ kind: "element" }] },
        { id: "insert", backendKey: "Insert", invokedBy: [{ kind: "shortcut", key: "Insert" }], appliesTo: [{ kind: "element" }] },
        { id: "add-right", backendKey: "Tab", invokedBy: [{ kind: "shortcut", key: "Tab" }], appliesTo: [{ kind: "element" }] },
        { id: "add-below", backendKey: "Enter", invokedBy: [{ kind: "shortcut", key: "Enter" }], appliesTo: [{ kind: "element" }] },
        { id: "delete", backendKey: "Delete", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }, { kind: "connection" }] },
      ],
```

**`{ kind: "menu" }` is an invocation a module must read carefully.** `centralized-selection` gave an existing name a new meaning: an action invoked from the shared menu reaches the module as `action-invoked`, and **reaches the backend only if its declaration names a `backendKey`**. Without one, the module's own handler is the whole of what happens. The declaration is unchanged and the member names are unchanged, so no check keyed on declarations or members can demand an entry for it — which is exactly why it is written out here.

**`{ kind: "gesture", gesture: "press" }` makes an element a button.** An unmoved click on an element whose declarations match a `press` gesture dispatches that action as `action-invoked` instead of selecting the element, and the selection stays as it was. It exists for small controls drawn as elements of their own - the supply chain's + and - steppers beside a node's quantity - so they need neither a custom shape nor a handler of the module's own. Declare it on a type that is also `selectable: false` and `draggable: false`; a press that matches no action is then a press on the background, as before. `ElementGesture` names the three gestures made on one element: `activate`, `context-menu` and `press`.

### The toolbox declaration

**Declarations:** `ToolboxDefinition`, `ToolboxItemDefinition`, `ToolboxDropDeclaration`, `ToolboxDropTarget`

**What it is for.** What a module contributes to the toolbox panel, beyond what its element types already imply.

**Whether a module needs it.** **No shipped module declares `toolbox`.** A module that declares nothing gets a toolbox derived from its element types, which is why none has needed to override it yet.

**Its shape.** `ToolboxDefinition` carries `suppress`, `add` and `drops`. A `ToolboxItemDefinition` carries `id`, `title`, `icon` and `payload`.

**`drops` are the entries that mean something only on an element** (DISL 7.2 `drop.targets`). A `ToolboxDropDeclaration` carries `payload`, what the entry's drag carries, `targets` and `refusal`; a `ToolboxDropTarget` carries `elementTypes` and `actionId`. A drop carrying such a payload is resolved by the library: the topmost element under the release point that a target names, and the first target naming its type, reach the module as `element-dropped` with `onto` set to that element and the target's action; a drop on anything else is refused on the canvas's refusal line with `refusal`, or ignored in silence without one, and is never raised. `compileNotation` writes them from a specification's `drop.targets`; the causal loop and Azure DevOps pipeline definitions state them.

Source: [`src/client/src/canvas/library/examples/toolbox.example.ts`](../src/client/src/canvas/library/examples/toolbox.example.ts)

```ts
export const TOOLBOX_EXAMPLE: ToolboxDefinition = {
  suppress: ["note"],
  add: [{ id: "milestone", title: "Milestone", icon: "flag", payload: "milestone" }],
};
```

### Chrome

**Declarations:** `ChromeDeclaration`, `ChromeLegendDeclaration`, `ChromeTextDeclaration`, `RulerDeclaration`, `RulerRung`, `MonthScale`, `CalendarStep`, `FilterDeclaration`, `FilterLegendEntry`, `SwitchDeclaration`, `EmptyCanvas`

**What it is for.** The text around the diagram rather than the diagram — loading and unavailable states, a title, a legend, rulers, and the sentence a canvas with nothing to draw says.

**Whether a module needs it.** **No shipped module writes `chrome` by hand.** Loading and unavailable are not a module's to declare any more: since `client-centralization` task 3 the frame the shell puts around every canvas says both, for every canvas alike (see [Refusals and status](#refusals-and-status)). A title, a legend and rulers remain a module's to declare here, and the first module to do so by hand replaces this example. The `chrome` shipped modules have is compiled: the Ansible structure diagram's `empty`, from its specification's `canvas.empty`, and the C4 views' `title` and `legend`, from their specifications' `canvas.title` and computed `canvas.legend`.

**A title above the canvas and a key below it.** The canvas draws a `title` as a `div` before its root, with the declaration's `className` and `data-*` attributes, once its text resolves against the model's `background`; and a `legend` as a `div` after its root while it has an entry: its `title` in a span of `titleClassName`, then per entry a span of `entryClassName` holding a swatch span of `swatchClassName` (and the entry's `swatchClass`), coloured inline by `swatchColour` where the entry has one, and the caption. Both sit beside the canvas root in the module's own container, so the module's stylesheet lays the three out.

**Its shape.** `ChromeDeclaration` carries `loading`, `unavailable`, `title`, `legend`, `rulers` and `empty`. `empty` is the sentence of a canvas with nothing to draw, which the module shows once it knows there is nothing - only the module knows when its source has been read - by rendering `EmptyCanvas` in place of the canvas: `<EmptyCanvas definition={...} className="..." />` draws the sentence in the shared `canvas-host-message` frame, with the module's class beside it, and draws nothing for a definition without one.

Source: [`src/client/src/canvas/library/examples/chrome.example.ts`](../src/client/src/canvas/library/examples/chrome.example.ts)

```ts
  loading: { text: { template: "Loading\u2026" } },
  unavailable: { text: { template: "This build cannot draw this diagram." } },
  title: { text: { template: "Dependency graph" } },
};

```

**Related.** [Styling](#styling) for the classes chrome uses, and [Events, and how a module answers them](#events-and-how-a-module-answers-them) for what a declared action raises.

**Their members.** `ChromeLegendDeclaration` carries `entries`, `swatchClass`, `swatchColour`, `when`, `className`, `title`, `titleClassName`, `entryClassName`, `swatchClassName`, `data`; `ChromeTextDeclaration` carries `text`, `when`, `typography`, `className`, `data`; `RulerDeclaration` carries `orientation`, `edge`, `unitsPerCanvasUnit`, `scale`, `origin`, `ladder`, `minSpacingPx`, `className`, `when`; `RulerRung` carries `every`, `label`; `MonthScale` carries `unit`, `unitsPerStep`, `origin`; `FilterDeclaration` carries `field`, `label`, `legend`, `elementTypes`; `FilterLegendEntry` carries `caption`, `swatchClass`.

**A ruler on the bottom edge, and a filter box.** A ruler declaring `edge: "bottom"` is drawn by the canvas as a strip under the diagram that follows the view. Its `scale` may be a `MonthScale` - a fixed number of canvas units per calendar month from a `YYYY-MM` origin - instead of `unitsPerCanvasUnit`, and a rung's `every` may be any `CalendarStep`, `"decade"` included. A definition's `filter` - a `FilterDeclaration` on the DiagramDefinition rather than in chrome, declared here because it is the other piece of view furniture - shows a box that takes tags as removable chips, looking typed text up among the tags the elements carry at `field`, and hides every element having none of the chosen tags - or, with its Any/All switch on All, not every one of them - with the connections touching them. The filter is view state: nothing is sent to the backend. Its optional `legend` draws a small key under the box, one swatch and caption per `FilterLegendEntry`; the swatch carries the entry's `swatchClass`, so the module colours it with the rule that colours what it stands for, as the hype cycle graph does for its four phases. Its optional `elementTypes` scopes it: an element of any other type is never hidden by it, keeps its connections, and offers none of its tags as suggestions - the hype cycle's notes stay put under every filter.


**A switch whose value is the document's.** `switches` declares two-state switches drawn over the canvas, top right, whether or not a filter is declared. Each reads its value from the model's background at its `on` path and raises `SwitchToggled` when pressed; the canvas holds no state for it, so the switch changes when the model does. It is for a setting the reader expects to find as they left it - the filter box and the layout toggle are for what belongs to one viewing.

**Its members.** `SwitchDeclaration` carries `id`, `caption`, `on`.
### Compiling it from the bundled specification

**Declarations:** `compileNotation`, `NotationBindings`, `parseDisl`

**What it is for.** Writing the definition once, in the diagram type's DISL specification, rather than twice. A module that bundles its `.dis` under `definition/` imports the same bytes the backend runs with Vite's `?raw`, reads them with `parseDisl`, and `compileNotation` maps the notation onto the library's vocabulary: element types and their shapes, labels, anchors, sizes and placement (a label `outside-top` or `outside-bottom` above or below its node, at the library's own gap), relations with their routes, markers and end rules, the shortcuts and gestures of the context menus, the connect gesture's pointer, snap, ruler, filter and legend, the viewpoints' layout modes, a node's `deletable: false`, a label's CEL `visible`, a `doubleClick` that names an operation, which becomes an action on the activate gesture, fixed anchors named for what they are (a timeline's `begin` and `end`), which become named fractions of the sides they lie on, an edge's label, the anchors its `connect.from` starts a connection from, and a release on empty canvas that its tool's `createTarget` or `createSource` answers, which becomes `emptyRelease: "complete"`. It reads curved lines and what rides along them too: a `curved` routing with its `curvature`, the `variants` that bow it the other way, its `selfLoop` and its `midMarkers` of type `bar` become a bowed arc, and an edge label placed at a fraction along the line becomes a mark beside it, on the side and at the distance it names; a stroke width chosen by thresholds on one attribute becomes a `strokeLadder`; `outline` anchors and outline anchoring become edge anchors; a `none` shape is a node with no body, whose `mdi-sync` icon is the `circular-arrow` decoration above its point, mirrored while its `flip` condition holds, and whose CEL placement at the mean of its members' centres is `centreOf`; and a `circular` layout is left to the host, as the backend places such a diagram. An edge's `stub` (DISL 6.10 *Stubs*) becomes the relation type's `stub`, its style the class `stubClassName` names and its label the class `edgeLabelClassName` names; an edge label bound as the CEL `self.<attribute>` is the connection's own label, as the attribute is; and a node that states no `accessibility` is named as DISL 6.15's default names it, its type's label and its label attribute, where the module asks for `accessibleRole` or `focusable`, as is a node that states its role and no name. Notation is inherited along the supertype chain (DISL 4.7), a node or edge resolving each visual property key by key, so a specification whose notation sits on an abstract type compiles each concrete type with it. A line's `dash` is the relation's `style.dash` when it is a list; when it is the CEL `<condition> ? [..] : [..]`, with a condition that is `self.<attribute>` or one `celConditions` names, it is `dashWhen` and the other list the `style.dash`; and a stub's style's dash is the stub's `dash`. A fill or stroke colour chosen as `token('<prefix>' + string(self.<attribute> % N))` is a class on the shape built from the prefix `indexedColourClassName` gives that token and the attribute's value modulo N, so the stylesheet paints each slot. An operation's `shortcut` is a key on the action its `doubleClick` makes, and `canvas.empty` is `chrome.empty`. `canvas.title` is `chrome.title`, its text bound against the model's `background` (an attribute `heading` is `payload.heading` there), and an editable title is refused; a `canvas.legend` that is `computed` and drawn `outside-bottom` is `chrome.legend`, its entries the list `canvasChrome.legend.entries` names on the background and each entry's caption and swatch colour the paths within it that `rowPaths` gives the key's `label` and `swatch.fill` CEL. A CEL expression the module names whole in `celPaths` is that path, whatever its terms, and a term it names is that path ahead of the `self.<attribute>` reading. There is no generated file, so nothing goes stale between the two tiers.

**Whether a module needs it.** No; a hand-written definition is as valid. The hype cycle graph, the behavior model, the .NET dependency graph, the causal loop diagram, the timeline, the dependency graph and the helm chart diagram compile theirs, and each keeps its former hand-written definition in a test as the oracle: the first two require the compiled definition to equal it; the .NET dependency graph, whose specification has a node type per kind where its hand-written definition had one, and the causal loop diagram, whose one link type replaces six, require the two to draw the same markup, the causal loop diagram on every document of its backend's parity transcript but for the accessible names its specification adds and an undecidable loop's turn; the timeline and the dependency graph require both - the same markup, and an equal definition once the differences their tests name are taken out (the relation's name, a label's path written as a template, and actions taken from the specification's menus with its wire ids); and the helm chart diagram, whose specification has a node type per kind and a relation per relation where its hand-written definition had one of each and built a stub element for every open end, requires the same markup on a corpus of every node type, relation and open end, with each stub compared by what it draws and two differences its test names: a `data-kind` nothing reads, and a partial's accessible name. The Ansible structure diagram, whose notation sits on two abstract types, is held to its oracle the same way, with the dashes its stylesheet painted given to the oracle's drawing and the differences its test names: class order, a class nothing reads, `data-kind`, the accessible names DISL's default gives, and the empty sentence's plain text. The Azure DevOps pipeline compiles its own from its definition as bundled; it requires the same markup as its oracle on a corpus of every kind, indicator, problem mark and arrow, with the differences its test names: the definition's tooltip on every box, a stage announced as a group, the accessible names DISL's default gives, and the unknown count shown on a job alone. The mind map compiles its own as well, and requires it to equal the hand-written definition it replaced to the last key but for the action ids, which are the specification's wire ids (`mindmap.add-child`) where the hand-written ones were local names, and the canvas draws that definition. The agent activity diagram compiles its own too, but never had a hand-written one, so it has no oracle: its test holds the compiled definition to the bundled specification in the specification's own terms - its element types and no other, each in the shape it names or in a stand-in the library still owes, the rings and their order, one line per key an element holds, the pairs its relations join, a task's statuses, the order of every list, a link symbol for every link badge, and a switch for every filter it keeps. The C4 module bundles six specifications, one per view kind, and compiles the one of the view it draws; it requires the same markup as its oracle for every view kind, with the two differences its test names: a line's dash drawn inline, and a card without a name announced by its id. The Databricks module bundles three, the bundle, the job and the pipeline, and each of its three canvases compiles its own; the job and pipeline drawings equal the oracle's markup on a corpus per reading, and the bundle's does but for the three differences its test names: a target is a box on the background layer with corners of 4, an override's dash comes from the definition's line style, and a target with nothing to summarise draws its summary line empty.

**Its shape.** `parseDisl` takes the text and refuses anything but DISL 0.3, 0.4 and 0.5; a 0.5 specification is read as a 0.4 one, since 0.5 adds only the `person` shape. `compileNotation(spec, bindings, options)` returns a `DiagramDefinition`, which the module still wraps in `assertValidDiagramDefinition`. `NotationBindings` is what the library cannot read from the specification, so the module states it in code: `wireIds` names the `x-` block holding the wire ids, and is left out by a module whose wire ids are the specification's own names (a type in lower case, an action its operation or kind); `celPaths` maps each CEL term, or a whole expression, to the payload path the backend computes it into, because the library has no CEL; `classNames`, `labelClassName`, `relationClassName`, `relationLineClassName` and `relationHitClassName` tie the drawing to the module's stylesheet; `labelUnderline` draws a wavy line under a label where DISL's text decoration cannot say its style; `labelEditorBox` places the inline editor; `endpointAnchors` overrides an end's anchor rule; `customShapes` says what a custom shape is drawn as, and `builtInShapes` what a DISL built-in is drawn as, ahead of the library's own drawing of it: one the library has none of (`roundedRect` as the `span`), or one the module draws in its own family (the timeline's `diamond` as its `moment` marker); `rowPaths` maps each CEL term a list's row binds to its path within the row (`item.<attribute>` needs none); `badgeClassName` is the class of a badge drawn as a mark, and `badgeData` the `data-*` attributes a badge carries; `elementData` gives the `data-*` attributes of an element type's group, and `elementDecorations` adds ornaments after the ones the specification states, for what is the host's to say rather than the specification's, such as where a finding of the problems service is marked; `celConditions` maps each CEL `visible` of a label or a badge to the condition the payload answers it with, and a `visible` of the form `<term> != ''` without one is drawn when that payload text is non-empty; `focusable` puts every element in the tab order, and `accessibleRole` gives every element its role, which DISL leaves to the runtime (6.15); `stubClassName` is the class a relation's stub is painted by, standing for the stub's style; `customRoutes` draws an edge with the module's own route; `indexedColourClassName` gives the class prefix of a colour chosen by index from a token family; `snapOrigins` names each element's snap origin; `snapAxes` states an axis's resting lines where they are the module's arithmetic, as the timeline's day is, whose width follows the scale it froze; `dragHint` is the label a drag shows where the snapping asks for the value (`feedback.showValue`), added to every type that moves; `ownRulers` leaves the rulers to a module that draws its own beside the canvas; `legendSwatchClass` colours a filter legend's swatch; `iconClassName`, `midMarkerClassNames`, `edgeLabelClassName` and `strokeWidthClassName` give the classes of a node's icon, of a line's bars and each bar, of a mark beside a line, and of each width a computed stroke width gives; `action` hands each compiled action back to the module with the shortcut its menu entry writes, where the backend resolves an action by its key (`backendKey`) or a second key invokes it; `toolboxDrops`, set to `"module"`, states no `toolbox.drops`, so every drop reaches the module as `element-dropped`, for a specification whose drops land on the canvas or are refused by a computed sentence, which the backend evaluates; `boundStyle` gives an element type the `boundStyle` its colours and silhouette are read from per element, where they are the document's rather than the type's, as a C4 author's are; `labelFit`, set to `"style"`, fits each cut or wrapped label at the size its style draws it - its style's font size, inside its node style's horizontal padding, a wrapped label's lines its font size times its line height apart - and refuses one whose style states no font size; `canvasChrome` gives the canvas title's and key's classes and `data-*` attributes, and the list a computed key's entries are; and `extras` adds what DISL has no construct for, which the mind map uses for its drag preview alone. A CEL term, shape, marker or label position it cannot map is refused with an error naming it.

**What it reads of DISL 0.4.** A node's `compartments` become the library's lists: its children in a slot are `payload.<slot>`, a CEL list is the payload path `celPaths` gives it, a list grouped by an enum attribute of its items takes the enum's values in order under their labels, and a list with no title, no groups and nothing to fold it by is `heading: "none"`. Which headings are folded is always `payload.collapsed`. A badge whose press opens one attribute is the link symbol for that attribute; a badge that runs nothing is a mark, shown while its `celConditions` entry holds; any other badge is refused. A `force` layout with `tiers` is the radiating layout on those rings, with `pinned` read from `payload.pinned` where the layout respects it. A filter drawn as a switch and kept with the diagram is a canvas switch whose value is the payload path the specification's `persistence.view.bind.filters` names, and is left out of the filter box. A drawable derived relation whose key sits on one end allows one line per element at that end. A menu's `pin` entry is an action the menu invokes with `keepsWhereDrawn`: chosen, the library raises `element-moved` for the selected element with the position it is drawn at, since where a layout drew an element is the canvas's to say, and a module that keeps a dragged element where it was dropped already answers that event. A badge with `pack: true` takes the next free slot of its node's `badgeLayout`: the layout's corner moved by its `offset`, then `spacing` further in its `direction` for each packed badge before it that is drawn, its text anchored on the side the row grows from and shown while its `visible` holds, through `celConditions` or the conditions the compiler reads itself. Of DISL 0.3 it also reads a node on the `background` layer, which is painted beneath the connections; a `roundedRect` drawn as the `rounded-rectangle` with the corner its `radius` asks for; a label `string(self.n) + (self.n == 1 ? ' one' : ' other')` as a count that declines its noun; a `layered` layout as one the host computes; a relation's ends, direction and loops taken from its supertype where it states none; and a toolbox entry's `drop.targets` of operations, with `elsewhere` `refuse` and a stated sentence or `ignore`, as `toolbox.drops`, its `on` read as the concrete types it stands for and its payload and actions under the wire block's `tools` and `actions`.

Source: [`src/diagrams/agent-behavior-modelling/client/AbmCanvas.tsx`](../src/diagrams/agent-behavior-modelling/client/AbmCanvas.tsx)

```ts
export const ABM_DEFINITION: DiagramDefinition = assertValidDiagramDefinition(compileNotation(SPEC, ABM_BINDINGS));
```

**Related.** [Styling](#styling), since colours stay in CSS: a module that compiles its definition proves in a test that the specification's theme tokens equal the custom properties its stylesheet paints with.

## The canvas

**Declarations:** `DiagramCanvas`, `DiagramRuntimeConfig`, `assertValidDiagramDefinition`, `DiagramCanvasProps`

**What it is for.** The one component that draws a diagram. A module renders it, hands it a definition, a model and a set of handlers, and writes no rendering code of its own.

**Whether a module needs it.** Always. There is no second way to draw.

**Its shape.** `DiagramCanvas` takes `definition`, `model`, `events`, `config`, `source`, `toolboxItems`, `className`, `scrollbarsClassName` and `ariaLabel`. **Two props types meet here** — the shell's `ToolContentProps`, which is what a canvas COMPONENT receives (`projectId`, `entryId`, `path`, `editorId`, `initialLine`), and the library's `DiagramCanvasProps`, which is the props above. Both are module-facing; a module uses the first to receive its arguments and the second to hand them on.

**Inline rename is the library's, as selection is.** Given `source`, the canvas reads the shell's inline-edit prompt itself and opens the shared editor over the label the backend named, so a label marked `editable` in the definition needs nothing else. Until `client-centralization` task 7 nine modules each built an `editing` prop from the same three lines to pass in; the prop is gone from `DiagramCanvasProps`, so **typecheck refuses a module that still passes it**.

**`config` is the runtime half.** `DiagramRuntimeConfig` carries `dragging`, `activeLayoutMode`, `activeTool` and `definitionOverrides`, and is applied on the next render — so a mode that forbids editing, or a phase that disables a relation type, is an edit to this object rather than a remount.

Source: [`src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx`](../src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx)

```tsx
      <DiagramCanvas
        definition={DEPENDENCY_GRAPH_DEFINITION}
        model={diagramModel}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        ariaLabel="Dependency graph"
        className="dependency-graph-surface"
        scrollbarsClassName="dependency-graph-scrollbars"
      />
```

**Their members.** `DiagramCanvasProps` carries `definition`, `model`, `events`, `config`, `source`, `toolboxItems`, `className`, `scrollbarsClassName`, `ariaLabel`.

## Events, and how a module answers them

**Declarations:** `DiagramEventHandlers`, `ElementDropped`, `ElementDeleted`, `ElementMoved`, `ElementResized`, `ConnectionDrawn`, `ConnectionReleasedOnEmpty`, `ConnectionRefused`, `ConnectionDeleted`, `ConnectionAdjusted`, `LabelCommitRequested`, `ViewChanged`, `LayoutModeChanged`, `ActionInvoked`, `ActionRefused`, `DiagramEvent`, `DiagramViewport`, `ResizedSide`, `SelectionChanged`, `SegmentBoundaryMoved`, `ElementPreviewed`, `ConnectionEndMoved`, `CompartmentToggled`, `LinkActivated`, `SwitchToggled`

**What it is for.** **Every event is a request, never a report.** The canvas raises what a user did; the module decides what happens and sends it to the backend. Nothing is applied to the model by the library on its own. `connection-refused` is the one that asks for nothing: a line was released on an element the definition does not admit, nothing was drawn, and the module is told so that it can say which rule refused it.

**Whether a module needs it.** Every handler is optional, and that is deliberate: a read-only diagram legitimately answers nothing, and an unhandled request is a gesture the module chose to ignore rather than an error.

**Its shape.** One optional handler per event kind, named `on` plus the kind — `onElementMoved`, `onConnectionDrawn`, and so on. **`onSelectionChanged` is not among them**: a handler map that names it is a type error, which is what keeps selection glue from coming back.

Source: [`src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx`](../src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx)

```tsx
  // Selection is the library's (centralized-selection), and so are sending a declared action and
  // showing a refusal: every call below reports its own to the library's one refusal line.
  const events: DiagramEventHandlers = {
    onElementMoved: ({ elementId, position }) => {
```

**`SelectionChanged` is declared here because it is a member of the `DiagramEvent` union, and it is NOT a module's to handle.** It is the library's own event, raised to the library's selection wrapper and to nobody else; there is no handler a module can supply for it, and a handler map that names one is a type error. It appears in this document only so that a reader who meets it in the union knows it is the library's.

**A refusal needs no handler.** Every call that reaches the backend from a canvas — the library's declared keystrokes and menu actions, and a module's own `executeAction`, `setProperty` and `moveElementTo` — shows its refusal on the one line the library draws around the canvas, and clears it when the next is sent (see [Refusals and status](#refusals-and-status)). `ActionRefused` is still raised, as `action-refused` with the `actionId` and a `message`, for a module that wants to do something of its own besides; **nothing is required to listen**, and a module that draws the message itself draws it twice.

**A refused move snaps back only if the handler hands back the answer.** The canvas holds a dropped element at the drop until the model places it, so an accepted move does not flash back to where it began. A refused move changes nothing in the model, so nothing releases that hold: the element stays at a position nothing recorded. `onElementMoved` may therefore return the `Promise<string>` that `moveElementTo` resolves to - `""` when the move was recorded, its refusal otherwise - and a refusal lets the element go back to where the model has it. A handler that returns nothing is held until the model answers. **Every positional module returns it** (ruled 2026-10-19): `return moveElementTo(...)`, never `void moveElementTo(...)`, as [`C4Canvas.tsx`](../src/diagrams/c4/client/C4Canvas.tsx) does for a view that declares `autoLayout`. Two modules release the hold another way and need not: `agent-behavior-modelling` and `sankey` draw the drop through a rearrangement of their own, which moves the element off its old place and is dropped again on a refusal. The mindmap's drop is a re-parenting proposal (`dropTarget`), which the canvas never holds. A module's canvas test checks it with `dragAndSettle` (see [Tests a module writes](#tests-a-module-writes-and-what-a-module-must-not-do)).

```mermaid
sequenceDiagram
    participant User
    participant Canvas as DiagramCanvas
    participant Module as the module's handler
    participant Channel as the context channel
    participant Frame as the library's frame
    participant Backend

    User->>Canvas: draws a connection
    Canvas->>Module: ConnectionDrawn
    Module->>Channel: executeAction
    Channel->>Frame: sent - the refusal line clears
    Channel->>Backend: the command
    alt accepted
        Backend-->>Module: a delta, through the stream
        Module-->>Canvas: the new model
    else refused
        Backend-->>Channel: a message
        Channel->>Frame: refused - the line shows it
    end
```

**`ElementPreviewed` is the other member that is not a request.** It is raised on every frame of a move, a resize or a boundary drag with the element's drawn rectangle and its inner boundaries, and once more with `bounds: null` after the gesture's final event. Nothing is written for it: it is there so a module can show what the release will write while the gesture is still going, which is what `showPropertyPreview` is for (see [The context channel](#the-context-channel)).

**Their members.** `DiagramViewport` carries `x`, `y`, `width`, `height`; `SelectionChanged` carries `kind`, `selection`.

**Their members.** `CompartmentToggled` carries `elementId`, `compartmentId`, `key`, `collapsed`; `LinkActivated` carries `elementId`, `rowId`, `link`; `SwitchToggled` carries `id`, `on`; `ElementDropped` carries `elementType`, `position`, `elementIds` - every element under the drop, topmost first, so a module whose items are dropped onto an element finds it there rather than measuring boxes of its own - and `onto`, the element and action a declared `toolbox.drops` entry resolved to; `ElementDeleted` carries `elementId`; `ElementMoved` carries `elementId`, `position`; `ElementResized` carries `elementId`, `side`, `bounds`; `ConnectionDrawn` carries `relationType`, `sourceElementId`, `targetElementId`, `sourceAnchor`, `targetAnchor`, `sourceAttachment`, `targetAttachment`; `SegmentBoundaryMoved` carries `elementId`, `index`, `x`; `ConnectionReleasedOnEmpty` carries `relationType`, `sourceElementId`, `sourceAnchor`, `position`; `ConnectionDeleted` carries `connectionId`; `ConnectionAdjusted` carries `connectionId`, `waypoints`; `ConnectionEndMoved` carries `connectionId`, `end`, `attachment`; `ElementPreviewed` carries `elementId`, `bounds`, `boundaries`; `LabelCommitRequested` carries `target`, `value`; `ViewChanged` carries `viewport`; `LayoutModeChanged` carries `mode`; `ActionInvoked` carries `targetKind`, `targetId`.

## Selection

**Declarations:** `SelectedItem`, `DiagramSelection`

**What it is for.** What the user has selected — **owned by the library, not by the module**.

**Whether a module needs it.** A module writes no selection code at all. It declares which types may be selected and passes `source`; the library reads the backend's selection, pushes a press's, and wires the shared context menu itself.

**Its shape.** `source` is a `CanvasSource` — which diagram this canvas draws. **`CanvasSource` has no Declarations line yet, and that is a known gap rather than an omission:** it is declared in `librarySelection.ts`, a library file the surface's folder rule does not include, so the computation reaches the `source` prop and stops there. It is plainly module-facing - it is what a module hands the canvas - and an amendment is being raised so the walk resolves a referenced type through the import that declares it. `selectable` is a flag a module declares on an element type or a relation type. A handler that needs to know what was selected reads it from the event it is given.

Source: [`src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx`](../src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx)

```tsx
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
```

**Given no `source`, the canvas highlights its own last press and tells nobody** — which is what a library test or a picture with no backend wants.

**What a module no longer writes.** Deriving a selection, pushing one, or mapping keys to element ids are all the library's now. If you are reading an older module for a pattern, that part of it is gone rather than optional — and the names that did it have no entry here on purpose, because documenting them would teach a way of working the library no longer accepts.

**Their members.** `SelectedItem` carries `kind`, `id`.

## The context channel

**Declarations:** `useContextConnection`, `elementSourceOf`, `ActionOutcome`, `ActionOptions`, `useContextProblems`, `Problem`, `ProblemSeverity`, `showPropertyPreview`, `settlePropertyPreview`, `endPropertyPreview`

**What it is for.** Running an action against the backend. A prompt the backend asks in return — an inline rename among them — is answered by the shell and the library, not by the module.

**Whether a module needs it.** Any module whose actions do something. `useContextConnection` gives `executeAction`, which is how a module runs an action it answers itself — a drawn connection, a drop.

**A toolbox drop may say it is one.** `executeAction(actionId, source, { drop: true })` (`ActionOptions` carries `drop`) sends the id as a toolbox entry's drop action, which the backend resolves by the entry rather than by the menu of the element it landed on; a module whose backend does not resolve drops itself (`IContextDropProvider`) is answered from that menu exactly as without it, and a drop it refuses comes back with the provider's sentence. C4 sends its drops so, because no C4 element's menu has an add. A drop the library resolves from `toolbox.drops` runs the target's own action and needs no option.

**Its shape.** `elementSourceOf(elementId, entryId)` builds the source an action is run against: the element, and the `entryId` the canvas was given for its diagram. The diagram is required because the backend otherwise finds the element under whatever file the connection's selection names — after a switch of diagram tabs that is the other tab's file, and an edit made on one canvas was written into another diagram.

**A keystroke is not a module's to send.** The same connection also offers `executeShortcut`, and it is the library's: a declared action names its key in `backendKey` (see [Actions, shortcuts and enablement](#actions-shortcuts-and-enablement)) and the library sends it. `contextShortcutOf` and `ContextShortcut`, which built that request, have no entry here for that reason, and `noModuleSendsAKeystroke.test.ts` fails on a module that names either, or `executeShortcut`.

**The channel resolves and never rejects.** A refusal comes back as a value, not as a thrown error, **so a module that wraps these in a try/catch is writing a branch that cannot be reached.** Inside a canvas the channel's gesture calls — `executeAction`, `executeShortcut`, `setProperty` — also show that refusal on the library's line themselves, so a module reads the returned value only when it has something of its own to do with it; ignoring it drops nothing.

Source: [`src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx`](../src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx)

```tsx
  const { executeAction } = useContextConnection();
  const toolboxItems = useToolboxItems(projectId, path);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);
```

**Their members.** `ActionOutcome` carries `accepted`, `error`.

**A value shown before it is written.** `showPropertyPreview(elementId, values)` puts values in the property grid in place of what the backend last said, for that element while it is selected, and writes nothing; a module calls it from `ElementPreviewed` so Start, Stop or a phase's end follow a drag. `settlePropertyPreview` says the release wrote them, so they stay until the grid has read the answer and the old value never flashes back; `endPropertyPreview` ends the gesture, dropping a preview nothing wrote at once. The hype cycle graph is the first user.

## Refusals and status

**Declarations:** `useCanvasRefusal`

**What it is for.** Telling the user that something they did was refused, and what the canvas is doing while it opens, reconnects or cannot be opened — **in one place, drawn by the library, on every canvas.** The shell puts every module canvas inside the library's frame, and the frame draws both: the refusal line in the corner and the status in the middle.

**Whether a module needs it.** **Almost never.** Every call that reaches the backend already reports to the frame: the library's declared keystrokes and menu actions, and a module's own `executeAction`, `setProperty` and `moveElementTo`. Each clears the line when it is sent and fills it when it is refused, so the line always says *your last attempt failed*, and a click dismisses it. The stream reports opening, reconnecting and unavailable. `useCanvasRefusal` is for the two gestures nothing shared carries: a refusal a module decides on the client before anything is sent, and a call to the backend a module builds itself. Outside a canvas it does nothing.

**Its shape.** `useCanvasRefusal()` returns two functions: `attempted()` clears the line, as sending anything does, and `refuse(message)` shows the message. The return has no type name of its own, so a module reads its two members without naming one.

Source: [`src/diagrams/causal-loop-diagram/client/CausalLoopCanvas.tsx`](../src/diagrams/causal-loop-diagram/client/CausalLoopCanvas.tsx)

```tsx
  // The refusals this canvas decides on the client, before anything is sent; every other refusal
  // is the backend's, and the call that got it reports it to the library's line itself.
  const { refuse } = useCanvasRefusal();
```

**What a module no longer writes.** A `rejection` state and the banner that draws it, an `onActionRefused` handler that copies the message into it, a status block for loading or unavailable, and an early return that draws that block instead of the canvas. Until `client-centralization` task 3 seventeen canvases each drew their own, with three different rules for when the message cleared, and four showed a refused menu action nowhere at all.

**The guards.** `everyCanvasHasOneRefusalSurface.test.tsx` mounts every registered canvas inside the frame, pushes a refusal, and fails if no message appears, if more than one surface shows it, if the one that shows it is not the library's, or if a module declares a rejection or status element; it asserts the same of opening and unavailable, and reads every module source for a surface class, because a banner drawn only from a module's own state is absent from the page at rest.

## The toolbox

**Declarations:** `useToolboxItems`, `useRegisterDiagramToolbox`

**What it is for.** The palette a user drags from.

**Whether a module needs it.** `useToolboxItems` is called by a module that wants the backend's entries for its diagram; omitted, the toolbox derives from the definition's element types. `useRegisterDiagramToolbox` registers the panel itself and is the shell's, not a module's.

**Its shape.** `useToolboxItems(projectId, path)` returns the items to hand `DiagramCanvas` as `toolboxItems`. The canvas registers the toolbox and the view controls as a pair, by construction.

## The view report

**Declarations:** `useViewReport`, `viewReportOf`, `Viewport`

**What it is for.** Telling the backend which rectangle the canvas can see, so a diagram larger than the view is not delivered in full.

**Whether a module needs it.** Any module whose diagrams can outgrow the viewport. Omitted, the backend sends everything.

**Its shape.** `useViewReport` takes the current `view`, the `report` function from the module's stream wrapper, a `convert` that puts the rectangle in the module's OWN units, and a `ready` flag. **The library converts nothing**: a module whose y axis is in row-height units reports row-height units, because only the module knows what its coordinates mean.

Source: [`src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx`](../src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx)

```tsx
  useViewReport({
    view: { x: viewport?.x ?? 0, y: viewport?.y ?? 0, w: viewport?.width ?? 0, h: viewport?.height ?? 0 },
    report: reportView,
    convert: () => ({
```

**Related.** [`src/client/src/diagrams/readme.md`](../src/client/src/diagrams/readme.md) describes the mechanism — the debounce, the delta the backend computes from it, and why the report is per connection.

```mermaid
sequenceDiagram
    participant User
    participant Canvas as DiagramCanvas
    participant Report as useViewReport
    participant Backend

    User->>Canvas: pans or zooms
    Canvas->>Report: ViewChanged
    Report->>Report: debounce, and convert to the module's own units
    Report->>Backend: UpdateView
    Backend-->>Canvas: only the deltas for what is visible
```

**Their members.** `Viewport` carries `minX`, `minY`, `maxX`, `maxY`.

## Gesture ids

**Declarations:** `placementId`, `relationId`, `withoutPrefix`

**What it is for.** Naming what a gesture lands on when it is not an existing element: a placement on empty canvas, `new:x,y`, or a relation between two ends, `rel:from->to`. The backend parses both, so the grammar is its own, and its golden fixture pins the client's builders to it.

**Whether a module needs it.** Any module whose drops, connections or menu gestures send an action to a target it builds: ten do today.

**Its shape.** `placementId(x, y)` and `relationId(from, to)` build the two forms. `withoutPrefix(id, prefix)` takes a prefix off an element id and answers null when the id does not carry it or nothing follows it, so an end is never sent truncated or empty; a module refuses the gesture instead.

**The guard.** `gestureIds.test.ts` fails on either form written by hand, or a `.slice("prefix:".length)` with no `startsWith` check of that prefix on the line or just above it, anywhere outside `gestureIds.ts`.

## Geometry for custom shapes and routes

**Declarations:** `forwardBezierPath`, `ShapePoint`, `ConnectorBox`, `facingAnchorsBetween`, `horizontalBezierPath`, `sideAnchorOf`

**What it is for.** Building a path or an anchor point when a built-in shape or route does not fit.

**Whether a module needs it.** Only a module declaring a `customShape` or a `customRoute`. **These are shared primitives, and using them is legitimate** — the gesture guard deliberately does not police them, because a custom shape must build geometry from something.

**Its shape.** The connectors module exports the path builders a custom route composes; a custom shape's `render` is handed `ShapeBounds` and returns an element, and its `edgePoint` answers where a connection meets it.

**Related.** [`src/client/src/canvas/elements/readme.md`](../src/client/src/canvas/elements/readme.md) for element geometry and [`src/client/src/canvas/label/readme.md`](../src/client/src/canvas/label/readme.md) for label placement. Neither is restated here.

**Their members.** `ConnectorBox` carries `x`, `y`, `width`, `height`.

## Text width and fit

**Declarations:** `capacityOf`, `fitToCapacity`, `widthOf`, `LABEL_FONT_SIZE`

**What it is for.** Estimating how much text fits a width, and cutting it with an ellipsis when it does not, by the one metric the whole client uses.

**Whether a module needs it.** Only a module that draws text of its own outside a declared label - the OWL canvas's rows, the supply chain's volume pill. A declared label is fitted by the library already, at the size its stylesheet draws it where it declares a `fit`, as the C4 card's type line and description do.

**Its shape.** `capacityOf(width, fontSize)` is how many characters fit, by the backend's shared metric: characters × font size × 0.55. `fitToCapacity(text, capacity)` cuts to that many with the ellipsis inside it. `widthOf(text, fontSize)` is the same metric the other way round, for a module that sizes a shape to its text, as the supply chain's volume pill does. `LABEL_FONT_SIZE` is the size the library draws a label at when none is declared. A module's padding stays its own: it takes it off the width before asking.

**The guard.** `textMetrics.test.ts` fails on a width from a character count, a per-character advance or an ellipsis cut by slice anywhere outside `textMetrics.ts`, and holds that a text sized by the metric is never then trimmed by the fit.

## Styling

**What it is for.** Making a module's diagram look like part of the application rather than like itself.

**Whether a module needs it.** Every module has a stylesheet, imported from `register.ts` beside the library's own.

**Its shape.** **Classes compose**: a module's class sits beside the library's rather than replacing it — `dependency-graph-node canvas-node`, `dependency-graph-label canvas-node-label`. The library's class carries the behaviour and the theme; the module's carries what is specific to it.

**A custom property must be defined, and must say both modes.** A `var(--x)` that resolves to nothing falls through to its fallback in BOTH themes, which reads as working until somebody switches theme. A module's own palette is declared on its canvas class and redeclared under the dark scheme; type stacks are exempt, because a font is the same in both.

**A module styles its shapes by the classes it declares on them, never by element type.** A class declared `on: "shape"` lands on the drawn body itself - the box's `rect`, the frame's outline, the centred box's `rect` - and a mark's circle or rule carries its class suffixed `-glyph`, as its caption carries `-label`. `.mindmap-node rect` once matched the library's own selection ring in that group and painted an opaque box over the label; a selector naming a type reaches whatever the library draws there, today and later.

**The guards.** `themeTokens.test.ts` walks every custom property in the tree and requires each to resolve to a theme token or to one the same stylesheet declares, and a local palette that declares one mode fails. `noModuleReachesLibraryShapes.test.ts` fails on any module stylesheet selector naming an SVG element type.

## Tests a module writes, and what a module must not do

**Declarations:** `expectLibrarySelection`, `fakeContextConnection`, `idsPushed`, `pointer`, `dragAndSettle`

**What it is for.** The library's shared test helpers, and the guards that walk every module client and fail when one reinvents something shared.

**Whether a module needs it.** Every module has a canvas test, and every module is walked by the guards below whether it knows it or not. **This is the half of the API that is enforced rather than offered.**

**The shared helpers.** `expectLibrarySelection`, in `src/client/src/canvas/library/testing/`, asserts that a press produced the library's own selection rather than a module's idea of one. It is the one library folder a module's TEST files may import from, and imports from it are part of the module-facing surface for that reason.

**The canvas test harness** beside it, `canvasHarness.ts`, holds what every canvas test used to write for itself (client-centralization Requirement 10): `pointer` builds a pointer event jsdom can carry, `idsPushed` reads the ids a canvas pushed for a `LibrarySelectionHarness`, `dragAndSettle` drags an element, lets go and waits for the move's answer, handing back its markup before and after - a refused move must leave the two equal - and `fakeContextConnection` is the one fake of the context connection, every member a `vi.fn()` - pass only the members a test drives, and mount it as `useContextConnection: () => connection` with `connection` built once. jsdom's missing pointer capture is stubbed in `test-setup.ts` for every test. A module's `renderCanvas` stays its own: its props differ per module.

**The guards that walk module clients — twenty-one of them, measured rather than recalled:**

| Guard | What it forbids | The shared mechanism instead |
| --- | --- | --- |
| `noPrivateGestures.test.ts` | a module-owned drag, pan or connect layer | the library's gesture handling |
| `noModuleSelection.test.ts` | deriving or pushing a selection | the library owns selection |
| `noPrivateScrollbars.test.ts` | a module's own scrollbars | the shared scroll surface |
| `noPrivateLabelEditors.test.ts` | a module's own inline label editor | the declared label, which the library edits from the backend's prompt |
| `noPrivateViewReports.test.ts` | a hand-rolled viewport report | `useViewReport` |
| `diagramStreamOpensOnlyInHook.test.ts` | opening a delta stream anywhere else | `useDiagramStream`, which every module wraps |
| `tableStreamOpensOnlyInHook.test.ts` | opening a designer's table stream anywhere else; it walks the diagram modules' clients too, where no table stream belongs | `useTableStream`, for a designer module; nothing, for a diagram module |
| `declarativeModules.test.ts` | drawing a diagram outside the declaration | `DiagramDefinition` |
| `libraryGuards.test.tsx` | built-in routes leaving the shared geometry | the connectors module |
| `noUnstyledLibraryClasses.test.ts` | a library class with no style behind it | the library's own stylesheet |
| `sharedStylesheetImports.test.ts` | a module that does not import the shared canvas stylesheet | the import in `register.ts` |
| `highlightSurvivesModuleStyles.test.tsx` | module styles that defeat the shared highlight | class composition |
| `themeTokens.test.ts` | a custom property defined nowhere, or a palette declaring one mode | a theme token, or a local palette declaring both |
| `fileUrlPaths.test.ts` | a hand-built file URL | the shared path helpers |
| `noModuleSendsAKeystroke.test.ts` | a module sending the backend a keystroke, or building one to send | `backendKey` on the action's declaration |
| `noModuleReachesLibraryShapes.test.ts` | a module stylesheet selecting an SVG element type, which reaches the shapes the library draws | a class the module declares on its own shape |
| `noCanvasTestCopiesAHelper.test.ts` | a canvas test declaring its own pointer-capture stub, pointer-event factory, `pushedIds` adapter or context-connection fake | `test-setup.ts` and `canvasHarness.ts` |
| `noStylesheetRuleWithoutAnEmitter.test.tsx` | a stylesheet rule for a class no shipped example draws, or a drawn class no stylesheet rules, unless either is listed with why | every canvas mounted on the shipped examples the backend exports to `src/fixtures/cross-tier/example-models/` |
| `gestureIds.test.ts` | a placement or relation id written by hand, or a prefix removed without a check | `placementId`, `relationId` and `withoutPrefix` |
| `textMetrics.test.ts` | a width estimated from a character count, a per-character advance, or an ellipsis cut by slice, outside the shared metric | `capacityOf` and `fitToCapacity` |
| `everyCanvasHasOneRefusalSurface.test.tsx` | a module's own refusal line or status, or one that replaces the library's | the library's frame around every canvas |
| `diagramModuleClientApi.test.ts` | this document drifting from the module-facing surface — an undocumented name, a stale entry, a changed excerpt, a diagram naming nothing real | an entry here |

**How that list was found, and what it misses.** A client test counts as walking module clients when it names the diagrams folder in a path literal AND reads the filesystem — `readdirSync`, `statSync` or `import.meta.glob`. The requirements named eight of these; twenty-one is what the tree holds now, with `client-centralization` task 6 adding `noModuleSendsAKeystroke.test.ts`, task 3 `everyCanvasHasOneRefusalSurface.test.tsx`, task 4 `noModuleReachesLibraryShapes.test.ts`, task 11 `noCanvasTestCopiesAHelper.test.ts` task 12 `noStylesheetRuleWithoutAnEmitter.test.tsx`, task 10 `gestureIds.test.ts` and task 5 `textMetrics.test.ts`, so that figure is a timestamp rather than a count.

**The rule has two blind spots, and both have already mattered.** It does not find a test that walks ENTIRELY through a helper, because the rule reads the test file's own text: a test that delegates every filesystem read to an imported module names no folder and calls nothing the rule looks for. And it does not find a guard that walks something OTHER than module clients — `channelResolvesRatherThanRejects.test.tsx` drives every value-returning context-channel method over a rejecting transport and fails naming any it cannot classify, which constrains a module exactly as the twenty above do, and appears in no table here. **So this table is the guards that walk MODULE CLIENTS, not every guard a module is subject to** — read it as the first list rather than the complete one.

## A minimal module client, end to end

**From an empty `client/` folder to a mounted canvas.** Each step links the entry that explains it; none of them repeats it.

1. **Create the workspace package.** `client/package.json`, private, named `@adp/diagram-<type>-client`, declaring only what the module's own code uses. See [Registration and discovery](#registration-and-discovery).
2. **Declare what the diagram IS.** A `DiagramDefinition` with at least `elementTypes` and `relationTypes`, wrapped in `assertValidDiagramDefinition` so a contradiction fails where it is written. See [The definition](#the-definition).
3. **Wrap the stream.** A hook of your own that calls `useDiagramStream` with an empty model and a function folding one delta into it, returning whatever your canvas needs on top. See [The stream and the model](#the-stream-and-the-model). **Do not open the stream any other way** — a guard forbids it and will name your file.
4. **Write the canvas component.** It receives the shell's `ToolContentProps`, calls your hook, and renders `DiagramCanvas` with the definition, the model, your handlers and `source`. See [The canvas](#the-canvas).
5. **Answer the events you care about.** Each handler is optional; an unanswered one is a gesture you chose to ignore. See [Events, and how a module answers them](#events-and-how-a-module-answers-them).
6. **Run actions through the context channel** rather than calling the backend directly, and remember it resolves rather than rejects. See [The context channel](#the-context-channel).
7. **Report the view** if your diagrams can outgrow the viewport, converting to your own units. See [The view report](#the-view-report).
8. **Register.** `client/register.ts` exporting `registrations`, importing the shared stylesheet and your own beside it. See [Registration and discovery](#registration-and-discovery).
9. **Write the canvas test**, calling the shared helper rather than asserting your own selection. See [Tests a module writes, and what a module must not do](#tests-a-module-writes-and-what-a-module-must-not-do).

**What you never write:** selection, scrollbars, gestures, an inline label editor, a viewport report of your own, a second stream, a keystroke sent to the backend, or a refusal line or status of your own. Each has a guard, and each guard names the shared mechanism instead.

## Library-internal exports

**These are the library's own exports that no module uses and none may.** A name here is not a smaller API than the one above — it is the other side of the same boundary, and a module importing one is the signal that either the module is reaching too far or this document is out of date.

**The list is computed, not maintained.** It is exactly the library's exports that appear in neither set, so it cannot drift from the code: a name that leaves the library fails the test, and a name a module starts importing leaves this list and must gain an entry above.

**69 names** since `client-centralization` task 5 moved `estimatedTextWidth` out of the library into `textMetrics.ts`; 70 since task 9 made `snapToStep` module-facing; 71 since `gartner-hype-cycle-graph` added `BEFORE_GAP`, `canvasPositionOf` and `monthIndexOf`; 68 read on `client-centralization` task 7's branch over `develop` at `a062888d`: `client-centralization` task 6 added `ActionDeclaring` and `backendKeyOf`, and task 7 `DiagramEditingIntegration`; 70 since `labelKey` gave a declaration's columns keys of their own; 72 since `ghg-compact-mode` added `LayoutPlacement` and `rowPackedLayout`, for the library's own use; 109 since `hype-cycle-fbl-client` compiled two modules' definitions from DISL, leaving `CompileOptions`, `typeList` and the DISL document types `parseDisl` returns to the library's own use; 129 since `causal-loop-disl-fbl` compiled the causal loop diagram's, leaving the stroke ladder's `strokeClassOf` and `strokeWidthOf`, the `circularArrowPath` a decoration draws, `placedAtCentreOfMembers` and the DISL types `DislIcon` and `DislMidMarker` to the library's own use; 132 since `wavy-underline` added the wavy underline's geometry, `WavyUnderlineAt`, `wavyUnderlineMetrics` and `wavyUnderlinePath`, which only the canvas draws with; 135 since `helm-chart-disl-fbl` added the stub's `DislStub`, `StubGeometry` and `stubGeometry`, which only the compiler and the canvas read:

`ActionDeclaring`, `ActionLookup`, `BEFORE_GAP`, `BUILT_IN_ROUTES`, `BUILT_IN_SHAPES`, `BackgroundLine`, `BackgroundMark`, `BackgroundRect`, `BackgroundText`, `Bindable`, `BindingSource`, `CompileOptions`, `DiagramCanvasCore`, `DiagramCanvasCoreProps`, `DiagramEditingIntegration`, `DislAnchors`, `DislAttribute`, `DislAxis`, `DislBadge`, `DislBadgeLayout`, `DislBox`, `DislCanvas`, `DislCompartment`, `DislContextMenu`, `DislContextTool`, `DislCoordinateSystem`, `DislCoordinates`, `DislCustomShape`, `DislDocument`, `DislDrop`, `DislEdgeLabel`, `DislEdgeNotation`, `DislEndAnchoring`, `DislEnum`, `DislFilter`, `DislIcon`, `DislLabel`, `DislLayout`, `DislMetamodel`, `DislMidMarker`, `DislNodeNotation`, `DislNotation`, `DislPathSegment`, `DislPlacement`, `DislRelation`, `DislRelationEnd`, `DislRuler`, `DislShapePart`, `DislShapeRef`, `DislSize`, `DislStub`, `DislTheme`, `DislTool`, `DislToolbox`, `DislType`, `DislViewpoint`, `DispatchedAction`, `ElementBounds`, `Extensions`, `GeomExpr`, `InteractionState`, `LAYOUT_ALGORITHMS`, `LaidOutCompartments`, `LaidOutHeading`, `LaidOutLabel`, `LaidOutRow`, `LayoutAlgorithm`, `LayoutElement`, `LayoutInput`, `LayoutPlacement`, `LayoutPositions`, `LibraryEventHandlers`, `LibrarySelectionHarness`, `PLACEMENT_MOTION_MS`, `ROW_LINK_WIDTH`, `ResolvedBackground`, `ResolvedChromeText`, `ResolvedDecoration`, `ResolvedEntry`, `ResolvedLegendEntry`, `ResolvedTick`, `StubGeometry`, `WavyUnderlineAt`, `actionForGesture`, `actionForKey`, `actionForMenuEntry`, `anchorPoints`, `backendKeyOf`, `canvasPositionOf`, `circularArrowPath`, `compartmentsHeight`, `connectionOf`, `dispatchDiagramEvent`, `effectiveDefinition`, `elementOf`, `flagOf`, `formatEpochSeconds`, `hasRow`, `holds`, `isConnectionElement`, `isCustomShape`, `labelKey`, `layoutAlgorithmFor`, `layoutCompartments`, `layoutLabels`, `manualLayout`, `monthIndexOf`, `placedAtCentreOfMembers`, `resolveBackground`, `resolveChromeText`, `resolveDecorations`, `resolveEntries`, `resolveLegend`, `resolveMany`, `resolveNumber`, `resolveNumberAt`, `resolveOne`, `resolveOneAt`, `resolveTicks`, `routePath`, `rowPackedLayout`, `rulerRangeOf`, `scaledTypography`, `shortcutKeysOf`, `strokeClassOf`, `strokeWidthOf`, `structuralModelOf`, `stubGeometry`, `tieredForceLayout`, `treeLayout`, `typeList`, `useAnimatedPlacements`, `validateDiagramDefinition`, `valueAtPath`, `wavyUnderlineMetrics`, `wavyUnderlinePath`, `wrappedLabelRegion`

