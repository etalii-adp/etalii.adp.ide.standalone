# Design Document

## Overview

**The library is a single canvas component driven by a declarative diagram definition, raising typed events a module answers by updating its model.** It is not new rendering technology: the shapes, connector geometry, inline editor, scrollbars, selection helper, context-menu discipline and pointer gesture already exist under `src/client/src/canvas/`, built piecewise by earlier specifications. This design adds the three things that bind them — a **definition schema**, a **canvas component that reads it**, and an **event/configuration API over the canvas** — and demonstrates, module by module against the real tree, that the schema expresses every diagram we draw.

**The survey that this design rests on was run before it was written**, because Requirement 9.1 asks for exactly that and this repository has been burned by asserted coverage. Every element shape, connector route, anchor strategy and layout mode named below was read out of the twelve modules' canvases, not recalled. The one genuinely bespoke piece it found — causal-loop's self-loop arc — is called out as a custom-shape/custom-route case rather than hidden.

## Steering Document Alignment

### Technical Standards (tech.md)

- **The backend owns meaning; the client renders and reports.** The library holds no model of its own and performs no mutation: every gesture becomes an event, and the module answers it by changing the model the backend gave it. This is the same division the canvases already honour by hand.
- **The contract is the boundary, and here the boundary does not move.** Client-only: no `.proto`, no backend, no wire change. The single known protocol gap — selection sets — belongs to `multi-select` and is consumed, not forked.
- **Adding a type means adding a module.** `DiagramClientModule.registrations` stays the seam; a library canvas is still a `Canvas` component behind a MIME `matches` predicate.

### Project Structure (structure.md)

New code lives under `src/client/src/canvas/library/` beside the parts it composes — `definition/` (the schema and its validators), `DiagramCanvas.tsx` (the component), `api/` (the event and configuration types), `layout/` (the pluggable algorithms), and guards beside them. Nothing moves out of the existing `canvas/` folders; the library imports them.

## Code Reuse Analysis — what already exists

| Concern | Existing piece | Role in the library |
| --- | --- | --- |
| Element shapes | `elements/`: box, centered-box, ellipse, frame, span, styled-box, symbol (7) | The built-in shape set (Requirement 2.1); custom shapes join them through the same contract |
| Connector geometry | `connectors.ts`: `edgePointOf`, `anchorsBetween`, `sideAnchorOf`, `branchAnchorsBetween`, `facingAnchorsBetween`, `straightPath`, `horizontalBezierPath`, `forwardBezierPath`, `midpointOf` | The route and anchor primitives every route type is built from |
| Connector components | `connections/`: straight, bezier, fixed-bezier, interactive-bezier (4) | Absorbed as route choices of one system (Requirement 3.1) |
| Inline editing | `label/InlineLabelEditor`, `labelPlacement` (aside/centred/midpoint/inset), `noPrivateLabelEditors` guard | The only label editor (Requirements 2.3, 3.3, 6) |
| Scrollbars | `scroll/CanvasScrollbars`, `scrollGeometry`, `noPrivateScrollbars` guard | The canvas's scroll surface |
| Selection | `selection.ts`: `elementSelectionOf`, `elementSourceOf`, `selectedElementIdOf` | The single-selection seam the library renders and, later, the set (Requirement 7.4) |
| Context menu | `useElementContextMenu` | The right-click discipline (Requirement 7.2) |
| Keyboard | `interaction.ts`: `isTextTarget`, `structuralShortcutFor` | Delete and structural shortcuts (Requirement 5.3) |
| Gestures | `gesture/usePointerGesture` (from `drag-and-drop-centralization`) | The layer drag/pan/connect/drop are built on (Requirement 5.1) |
| Shell seams | `registrations`, `useRegisterDiagramView`, `useRegisterDiagramToolbox`, `useViewReport`, `TOOLBOX_DRAG_TYPE` | Unchanged; a library canvas registers view controls and toolbox as a pair |

**The gap is orchestration, not parts.** Each canvas today is 460–1,006 lines wiring these together with private drag/pan/connect/zoom/drop state. The library is that wiring, written once and configured by a definition.

## Architecture

### The diagram definition

A definition is plain data — no React, no closures the shell must serialize — describing what a diagram type allows:

```
interface DiagramDefinition {
  elementTypes: ElementTypeDefinition[];
  relationTypes: RelationTypeDefinition[];
  toolbox?: ToolboxDefinition;          // defaults derived from elementTypes (R4.4)
  layout: LayoutDefinition;             // allowed modes; first is the default (R8)
  dragging: DraggingPolicy;             // canvas-wide default, per-type override (R5.2)
}

interface ElementTypeDefinition {
  id: string;
  shape: BuiltInShape | CustomShapeRef;         // R2.1
  style: ElementStyle;                          // theme-token colours (R2.2)
  label?: LabelRule;                            // placement, wrap, editable (R2.3)
  anchors: AnchorSet;                           // compass | per-side fractions | points | edge (R2.4)
  sizing: "model" | "content" | "user";         // R2.5
  draggable?: boolean;                          // overrides DraggingPolicy
  deletable?: boolean;                          // R5.3
}

interface RelationTypeDefinition {
  id: string;
  route: RouteKind;                             // R3.1
  style: ConnectionStyle;                       // colour, width, dash, markers (R3.2)
  label?: RouteLabelRule;                        // along-route placement (R3.3)
  endpoints: {                                   // R4.2 — the enforced part
    source: EndpointConstraint;                 // allowed element types + anchors
    target: EndpointConstraint;
    allowSelf: boolean;
    cardinality?: Cardinality;
  };
  adjustable?: boolean;                          // waypoints/control points (R3.5)
}

type RouteKind =
  | "straight" | "polyline" | "orthogonal"       // + cornerRadius
  | "arc" | "quadratic-bezier" | "cubic-bezier"
  | "spline"                                     // through waypoints
  | CustomRouteRef;                              // module-supplied path builder (R3.1)
```

**The definition is the whole statement of what the canvas offers** (Requirement 4.1), and it is read every render, so changing it at runtime reconfigures the canvas without a remount (Requirement 1.3): disabling dragging, swapping endpoint rules, or narrowing the allowed layout modes are all edits to this object.

### The canvas component

```
<DiagramCanvas
  definition={definition}
  model={model}                 // elements + connections in the definition's terms, with positions
  events={handlers}             // one handler per DiagramEvent kind (R1.2)
  config={runtimeConfig}        // dynamic overrides: dragging on/off, active layout, active tool
/>
```

It renders elements through the shape components, connections through the route builders, resolves anchors once for rendering **and** the connect preview **and** hit-testing (Requirement 3.4), owns pan/zoom/selection state, and drives every gesture through `usePointerGesture`. It **mutates nothing** — it calls the matching handler and waits for `model` to change, exactly as the backend-fed canvases already behave.

### The API — methods and events over the canvas

Configuration and event handling are done over the canvas (the user's explicit requirement). Two surfaces:

**Events raised** (`DiagramEvent`, one handler each — Requirement 1.2): `elementDropped(type, position)`, `elementDeleted(id)`, `elementMoved(id, position)`, `connectionDrawn(relationType, source, target, anchors)`, `connectionDeleted(id)`, `connectionAdjusted(id, waypoints)`, `selectionChanged(selection)`, `labelCommitRequested(target, value)`, `viewChanged(viewport)`, `layoutModeChanged(mode)`. Every one is a **request**; the module is free to answer, transform, or ignore it.

**Configuration accepted** (`runtimeConfig`, applied without remount — Requirement 1.3): `dragging`, `activeLayoutMode`, `activeTool` (which relation type a connect gesture draws — Requirement 5.5), and per-runtime definition overrides. A behaviour reachable only by forking the canvas is a defect in this surface (Requirement 1.5).

### Enforcement at gesture time, not after

Requirement 4.3 is the load-bearing interaction decision. The definition's endpoint constraints are consulted **during** the gesture: as a connect drag moves, candidate targets and anchors that no relation rule admits render as non-connectable; a toolbox drop over a position the definition forbids refuses rather than dropping. The event is raised **only** for a gesture the definition already allows, so a module never has to veto a completed action with a dialog — the rule is taught where the user's hand is.

### Layout behind one seam

```
interface LayoutAlgorithm {
  place(model: LayoutInput, mode: LayoutMode): Positions;
}
```

Built-ins: **manual/external** (returns the model's own positions untouched — the mode every backend-laid-out diagram uses today), **horizontal-flow**, **vertical-flow**, **tree** (directional hierarchy), **layered-graph** (dependency ranks). A definition lists its allowed modes; one mode means no switching surface (Requirement 8.2). **No layout computation crosses the wire** (Requirement 8.3): where the backend places elements today, the library uses manual/external and leaves that untouched. Where automatic layout is active and dragging is still permitted, the definition says whether a drag repins to manual or is a displacement the next pass reclaims (Requirement 8.4).

## The coverage check — schema against the twelve, measured

Read out of each module's canvas. **Shapes** and **route** name what the module draws today; **anchors** names its connector-attachment strategy; **layout** is manual/external unless noted.

| Module | Element shape(s) | Route today | Anchors | Layout | Expressible? |
| --- | --- | --- | --- | --- | --- |
| ansible-structure | box | own `<path>` via `forwardBezierPath`/`straightPath` | edge | external | Yes — route `cubic-bezier`, edge anchors |
| azure-pipeline | box | fixed-bezier component | facing sides | external (`setView(null)`) | Yes — route `cubic-bezier` |
| c4 | styled-box, **frame** | straight | `anchorsBetween` (centre→edge) | external | Yes — **nesting**, see below |
| causal-loop | box | **arc** via `causalLoopArc` (self-loops, polarity) | own | backend self-organizing → external | Yes — route `arc` + **custom route** for self-loop |
| databricks | box, frame, straight+fixed-bezier | mixed | facing/edge | external | Yes — three readings, one inner canvas |
| dependency-graph | span | interactive-bezier | `sideAnchorOf`+`facingAnchorsBetween` | external | Yes — route `cubic-bezier`, adjustable |
| helm-charts | box | own `<path>` via `forwardBezierPath`/`straightPath` | edge | external | Yes — as ansible |
| mindmap | centered-box | bezier | own (branch) | external (`setView(null)`) + side-balance | Yes — **tree** layout, `branchAnchorsBetween` |
| rdf | box | straight | edge | external (budgeted ≤1000) | Yes — budget is model-side, not schema |
| sparql | box, **frame** | straight | edge | external | Yes — **region grouping** via frame |
| timeline | span | interactive-bezier | `sideAnchorOf`+`facingAnchorsBetween` | external, **time-scaled axis** | Yes — axis is a definition-declared background |
| wardley-map | **symbol** | straight | edge/centre | **intrinsic 0..1 space + evolution axis** | Yes — intrinsic extent, see below |

### The hard cases, per Requirement 9.2

- **wardley-map — intrinsic 0..1 space and evolution axis.** The space is definitional, not derived (this is why it is `fit-to-view`'s one immune canvas: `setView(fullView)`). The definition declares an **intrinsic extent** and a **background axis** the library draws behind the elements; positions are model coordinates in 0..1. No layout computation, no derived extent.
- **timeline — time-scaled axis.** Same mechanism as wardley's axis: a declared background the library renders, with the horizontal position a scale of the model's time value. The axis is definition data, not canvas code.
- **causal-loop — arc connectors with polarity and self-connections.** The `arc` route covers the chord-bowed link; the **self-loop is a custom route** (`causalLoopArc.selfArc` — an ellipse pair, no chord), registered through `CustomRouteRef` rather than forced into a built-in. Polarity marks are along-route labels. `allowSelf: true`. This is the one place the survey found genuinely bespoke geometry, and the schema admits it as a first-class custom route rather than pretending a built-in covers it (Requirement 9.4).
- **c4 — nested containers.** `FrameElement` is a container element; nesting is expressed by an element declaring a parent, and the library renders children within the frame's bounds and hit-tests inner elements first. `sparql`'s region grouping is the same frame mechanism with a different style.
- **rdf — budgeted graphs at 1,000 drawn elements.** The budget is a backend/model concern (`RdfProjection.DefaultBudget`), not a schema one: the library draws whatever model it is handed. Named here only to record that it is **not** a gap — nothing in the schema needs a budget.
- **databricks — three readings, one inner canvas.** Preserved as-is: three registered wrappers, one shared component built on the library. The library does not disturb this; `diagram-library-adoption` migrates the four files as a unit.

### The one gap, stated with its consequence (Requirement 9.4)

**No built-in shape or route was found that the schema cannot express**, given the custom-shape and custom-route escape hatches. The custom-route hatch is load-bearing: without it, causal-loop's self-loop would force either a bespoke canvas or a dishonest "arc" that cannot draw a chordless loop. The escape hatches are therefore part of the coverage claim, not an admission against it — a custom shape/route is a first-class citizen of hit-testing, anchoring and connecting (Requirement 2.1), so a module needing one is still on the library, not beside it.

## The two reference canvases (Requirement 9.3)

Built end to end within this specification, chosen to be of different character so the schema is proven on both axes:

- **rdf — graph-shaped.** Box elements, straight routes, edge anchors, external layout, and the 1,000-element budget: proves the library at the sizes that started `drag-and-drop-centralization`, and proves a plain graph is a short definition.
- **timeline — axis-shaped.** Span elements on a time-scaled axis, cubic-bezier routes with facing anchors, three label placements: proves the intrinsic-axis/background mechanism and along-route labels that wardley and causal-loop also need.

Migrating the remaining ten is `diagram-library-adoption`'s work.

## Data Models

No persisted change and no wire change. The definition is client-side configuration data; the model is what the module already folds from the delta stream. The library adds runtime state (pan, zoom, selection, in-flight gesture) owned by the component and never sent anywhere.

## Error Handling

1. **A model element references an undefined element type** — the library renders a visible fallback box and raises no event; a module that produces such a model has a mapping bug, and a silent omission would hide it.
2. **A connect gesture released over empty space or an invalid target** — no event; the preview is discarded. Enforcement at gesture time (4.3) means an invalid release cannot raise a `connectionDrawn`.
3. **A custom shape/route renderer throws** — caught at the element/connection boundary so one bad element cannot blank the canvas; the offender renders as the fallback and the error surfaces in the console.
4. **A definition with zero layout modes** — rejected by the definition validator at construction; a canvas that can lay out no way is a definition bug, not a runtime state.

## Testing Strategy

### Unit
The definition validators; each built-in route's path and hit-test through the shared geometry; the anchor resolver's single answer serving render, preview and hit-test alike; the layout algorithms' placements.

### Behaviour, mounted (Requirement 10.2)
The reference canvases mounted with a harness: a forbidden connect cannot complete; a permitted one raises `connectionDrawn` with the right anchors; a toolbox drop on a forbidden position refuses; dragging disabled by config does not start a move.

### Guards (Requirement 10)
- **Connection rules are enforced at gesture time** — a definition with a forbidden pair, mounted, cannot produce a `connectionDrawn` for it. Seen to fail against a canvas that raises first and vetoes after.
- **Every built-in route renders and hit-tests through the shared geometry** — no route builder constructs a path outside `connectors.ts`.
- **A library canvas registers view controls and toolbox as a pair** — mounted, both registries non-null. This closes the half-registration class by construction (Requirement 1.4).
- Each guard carries a named-member canary and **mounts rather than greps**, because a grep over canvas sources has already reported three correct databricks canvases as offenders here.

## Sequencing

1. **The definition schema and its validators** — data and its checks, nothing rendered yet.
2. **The API types** — events and configuration, so both reference canvases are written against a fixed surface.
3. **The canvas component over the existing parts** — shapes, routes, anchors, selection, context menu, gestures wired once.
4. **The layout seam** with manual/external and one automatic mode (tree, which mindmap needs).
5. **rdf as the graph reference**, end to end, with its guards seen to fail then pass.
6. **timeline as the axis reference**, proving the intrinsic-axis and along-route-label mechanisms.
7. **The guards**, mounted, over both references.

`diagram-library-adoption` takes the remaining ten from here.
