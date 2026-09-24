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

**Every figure quoted below says what it counts and when it was read**, because a count with neither is a number a reader cannot check. Figures in this document were read at `d1cc0da3` unless their own sentence says otherwise, and the test recomputes them rather than trusting them.

## The shape of a module client

## Registration and discovery

**Declarations:** `DiagramCanvasRegistration`, `DiagramClientModule`

**What it is for.** A module tells the client which diagram types it can draw, and with what. This is the only wiring between a module and the shell: there is no list of diagram types anywhere in the client, exactly as there is none in the backend.

**Whether a module needs it.** Always, and it is the one file that cannot be omitted. The shell discovers modules by globbing `src/diagrams/*/client/register.ts` and `src/editors/*/client/register.ts` at build time. **A module without `register.ts` is not loaded and fails silently** — nothing reports a missing module, because nothing knew to expect it.

**Its shape.** `register.ts` exports `registrations`, an array of `DiagramCanvasRegistration`. Each entry answers two questions: which MIME types it speaks for (`matches`), and what to draw (`Canvas`). A module that knows a type this build cannot draw supplies `unsupported` instead — a `description` and the `futureSpec` that would add it — so the explanation comes from the module that understands the notation rather than from a fallback in the shell. `DiagramClientModule` is the shape of the module as a whole, which the shell reads; a module never imports it.

Source: [`src/diagrams/dependency-graph/client/register.ts`](../src/diagrams/dependency-graph/client/register.ts)

```ts
export const registrations: DiagramCanvasRegistration[] = [
  {
    matches: (mimeType) => mimeType === "generic/dependencies",
    Canvas: DependencyGraphCanvas,
  },
];
```

**The shared stylesheet is imported here, not in the canvas.** A module's own stylesheet and the canvas library's come in as side-effect imports at the top of the same file, so a module's styles load exactly when its registration does:

Source: [`src/diagrams/dependency-graph/client/register.ts`](../src/diagrams/dependency-graph/client/register.ts)

```ts
import type { DiagramCanvasRegistration } from "@client/shell/panels/diagramCanvas";
import { DependencyGraphCanvas } from "./DependencyGraphCanvas";
import "@client/canvas/canvas.css";
import "./dependency-graph.css";
```

**The workspace `package.json`.** Each module client is a private npm workspace package, named `@adp/diagram-<type>-client`. It declares the runtime dependencies the module's own code uses and nothing the library already provides.

**The guards.** `register.test.ts` in each module asserts the registration matches its own MIME types and no others. The shell's own registry test asserts the glob finds every module.

**Related.** [The shape of a module client](#the-shape-of-a-module-client) for where this sits in the whole.

## The stream and the model

**Declarations:** `useDiagramStream`, `DiagramModel`, `DiagramModelElement`, `DiagramModelConnection`

**What it is for.** A diagram's contents arrive as a stream of deltas from the backend, and the canvas draws a model. `useDiagramStream` owns the transport, the lifecycle, the retry and the state; a module supplies an empty model and a function that folds one delta into it.

**Whether a module needs it.** Always, and **only through this hook**. `diagramStreamOpensOnlyInHook.test.ts` forbids opening a diagram's delta stream anywhere else and names any offender. A module wraps the hook rather than calling the transport, so reconnection, teardown on identity change, and the loading and failed states are the same everywhere.

**Its shape.** The hook takes the project, the path, an empty model and a delta-folding function, and returns the model with `loading`, `failed` and the client. A module's wrapper adds whatever its canvas needs on top — a move call, a view report — built on the client it returns.

Source: [`src/diagrams/dependency-graph/client/useDependencyGraphStream.ts`](../src/diagrams/dependency-graph/client/useDependencyGraphStream.ts)

```ts
export function useDependencyGraphStream(projectId: Uint8Array, path: readonly string[]): DependencyGraphStream {
  const { watchId } = useContextConnection();
  const { model, loading, failed, client } = useDiagramStream(projectId, path, emptyModel, applyDelta);

```

**What the library reads.** The canvas is handed a `DiagramModel`, whose members are `elements`, `connections` and `background`. An element (`DiagramModelElement`) carries `id`, `type`, `x`, `y`, `width`, `height`, `label`, `labelAt`, `parentId`, `style` and `payload`. A connection (`DiagramModelConnection`) carries `id`, `type`, `sourceId`, `targetId`, `sourceAnchor`, `targetAnchor`, `label`, `className`, `title`, `editValue`, `waypoints`, `style`.

**`payload` and `background` are opaque on purpose.** The library walks them only by a path a module's own declaration names, so nothing in the library learns a diagram type's schema. A module unpacks its own generated payload types itself; those types are the module's, not the surface's, and are excluded from the API this document covers.

**An element naming no declared type renders as a visible fallback box** rather than disappearing — a mapping bug shown, not hidden.

**Authentication is not part of this surface.** `useAuth` is reached by several modules' *test* files to build an authenticated client, and by no module's production code. It is therefore outside the computed surface and carries no entry here; a module that finds itself needing it in production code is doing something this document does not cover, and that is the signal rather than an omission.

**Related.** [The view report](#the-view-report) for telling the backend what is visible, and [`src/client/src/diagrams/readme.md`](../src/client/src/diagrams/readme.md) for the stream's own mechanism.

## The definition

**Declarations:** `DiagramDefinition`

**What it is for.** One object says what a diagram type IS — what its elements are, how they connect, what may be dragged, what a user may do to them. The library reads it and draws; a module writes no rendering code.

**Whether a module needs it.** Always, and it is the largest thing a module writes by hand. **Everything below is a member of this one object.**

**Its shape.** `elementTypes`, `relationTypes`, `toolbox`, `layout`, `dragging`, `extent`, `snap`, `dropTarget`, `background`, `chrome`, `actions`, `backgroundMenu`, `connectOnRightDrag`, `dragBounds` and `acyclic`. Only `elementTypes` and `relationTypes` are structural; the rest declare behaviour and may be omitted.

**Which of these are actually used, measured rather than assumed.** Parsing all 13 modules that declare a definition — in both shapes it is written in, a typed constant and a function returning one — `elementTypes`, `relationTypes`, `layout` and `dragging` are set by all 13; `actions` by 7; `snap` by 2; `background`, `backgroundMenu`, `connectOnRightDrag`, `dragBounds`, `dropTarget` and `extent` by one each. **`toolbox`, `chrome` and `acyclic` are set by none**, and their entries below say so and excerpt a type-checked example instead of an invented one.

**It is validated at declaration, not at draw time.** A module wraps its definition in `assertValidDiagramDefinition`, so a contradictory declaration fails where it is written rather than as a blank canvas later.

Source: [`src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx`](../src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx)

```ts
const DEPENDENCY_GRAPH_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
```

### Element types and shapes

**Declarations:** `ElementTypeDefinition`, `CustomShapeRef`, `ShapeBounds`

**What it is for.** What kinds of thing the diagram has, and how each is drawn.

**Whether a module needs it.** Always — all 13 modules declare it, and it is the only member with no useful default.

**Its shape.** An `ElementTypeDefinition` carries `id`, `shape`, `style`, `boundStyle`, `classNames`, `data`, `tooltip`, `label`, `labels`, `decorations`, `actions`, `anchors`, `sizing`, `resize`, `draggable`, `deletable`, `selectable` and `beneathConnections`. A type whose shape is not one of the built-ins names a `CustomShapeRef` instead, which carries `customShape`, `render`, `edgePoint` and `anchors`; a renderer is handed `ShapeBounds` — `x`, `y`, `width`, `height`.

Source: [`src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx`](../src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx)

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

**The guards.** `assertValidDiagramDefinition` rejects a type whose shape and anchors disagree. An element whose `type` matches no declared id draws a visible fallback box rather than vanishing.

### Relation types, routes and constraints

**Declarations:** `RelationTypeDefinition`, `CustomRouteRef`, `AcyclicRule`

**What it is for.** What connects to what, how the line is routed, and what connections are forbidden.

**Whether a module needs it.** `relationTypes` is declared by all 13 modules. `acyclic` is declared by none.

**Its shape.** A `RelationTypeDefinition` carries `id`, `route`, `style`, `label`, `adjustable`, `selectable`, `className`, `lineClassName`, `hitClassName` and `emptyRelease`. A route the built-ins do not cover is a `CustomRouteRef` — `customRoute` and `path`. An `AcyclicRule` carries `relationTypes`: the relation ids a cycle may not be formed from, so a "depends on" edge can refuse a cycle while other relation types stay free to form one.

Source: [`src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx`](../src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx)

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

**No shipped module declares `acyclic`**, so its example is a type-checked file rather than an excerpt passed off as shipped code:

Source: [`src/client/src/canvas/library/examples/acyclic.example.ts`](../src/client/src/canvas/library/examples/acyclic.example.ts)

```ts
 */
```

### Layout, dragging, snap and extent

**Declarations:** `SnapDeclaration`, `DropTargetDeclaration`

**What it is for.** Where elements may go and how they move.

**Whether a module needs it.** `layout` and `dragging` are declared by all 13 modules; `snap` by 2, `dropTarget`, `extent` and `dragBounds` by one each. Omitted, a diagram lays out by its default mode and drags freely.

**Its shape.** A `SnapDeclaration` carries `x` and `y`. A `DropTargetDeclaration` carries `parentPath`, `ring`, `preview` and `group`. `dragging` is a policy rather than an object, and the runtime config can override it per render without a remount.

Source: [`src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx`](../src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx)

```ts
  snap: { y: { step: ROW_HEIGHT } },
  layout: { modes: ["manual"] },
  dragging: "enabled",
```

### Actions, shortcuts and enablement

**Declarations:** `ActionDeclaration`

**What it is for.** What a user may do to an element, a connection or the diagram, and how each is invoked.

**Whether a module needs it.** Seven of the 13 declare `actions` on the definition; others declare them per element type, which is where the reference module puts them.

**Its shape.** An `ActionDeclaration` carries `id`, `invokedBy`, `appliesTo`, `enabled`, `label` and `when`. **A shortcut is described as data, never wired by hand**: the module says which key invokes which action id, and the library derives the key set and dispatches.

Source: [`src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx`](../src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx)

```ts
      actions: [
        { id: "rename", invokedBy: [{ kind: "shortcut", key: "F2" }], appliesTo: [{ kind: "element" }] },
        { id: "insert", invokedBy: [{ kind: "shortcut", key: "Insert" }], appliesTo: [{ kind: "element" }] },
        { id: "add-right", invokedBy: [{ kind: "shortcut", key: "Tab" }], appliesTo: [{ kind: "element" }] },
        { id: "add-below", invokedBy: [{ kind: "shortcut", key: "Enter" }], appliesTo: [{ kind: "element" }] },
        { id: "delete", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }, { kind: "connection" }] },
      ],
```

**`{ kind: "menu" }` is an invocation a module must read carefully.** `centralized-selection` gave an existing name a new meaning: an action invoked from the shared menu reaches the module as `action-invoked` and **never reaches the backend**. The declaration is unchanged and the member names are unchanged, so no check keyed on declarations or members can demand an entry for it — which is exactly why it is written out here.

### The toolbox

**Declarations:** `ToolboxDefinition`, `ToolboxItemDefinition`

**What it is for.** What a module contributes to the toolbox panel, beyond what its element types already imply.

**Whether a module needs it.** **No shipped module declares `toolbox`.** A module that declares nothing gets a toolbox derived from its element types, which is why none has needed to override it yet.

**Its shape.** `ToolboxDefinition` carries `suppress` and `add`. A `ToolboxItemDefinition` carries `id`, `title`, `icon` and `payload`.

Source: [`src/client/src/canvas/library/examples/toolbox.example.ts`](../src/client/src/canvas/library/examples/toolbox.example.ts)

```ts
export const TOOLBOX_EXAMPLE: ToolboxDefinition = {
  suppress: ["note"],
  add: [{ id: "milestone", title: "Milestone", icon: "flag", payload: "milestone" }],
};
```

### Chrome

**Declarations:** `ChromeDeclaration`

**What it is for.** The text around the diagram rather than the diagram — loading and unavailable states, a title, a legend, rulers.

**Whether a module needs it.** **No shipped module declares `chrome`.** The three modules with loading and unavailable states render them in their own JSX, which is what `client-centralization` task 3 moves into the library; when that lands, the first module to adopt it replaces this example.

**Its shape.** `ChromeDeclaration` carries `loading`, `unavailable`, `title`, `legend` and `rulers`.

Source: [`src/client/src/canvas/library/examples/chrome.example.ts`](../src/client/src/canvas/library/examples/chrome.example.ts)

```ts
  loading: { text: { template: "Loading\u2026" } },
  unavailable: { text: { template: "This build cannot draw this diagram." } },
  title: { text: { template: "Dependency graph" } },
};

```

**Related.** [Styling](#styling) for the classes chrome uses, and [Events, and how a module answers them](#events-and-how-a-module-answers-them) for what a declared action raises.

## The canvas

## Events, and how a module answers them

## Selection

## The context channel

## The toolbox

## The view report

## Geometry for custom shapes and routes

## Styling

## Tests a module writes, and what a module must not do

## A minimal module client, end to end

## Library-internal exports
