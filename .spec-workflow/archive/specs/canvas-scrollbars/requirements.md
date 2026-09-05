# Requirements Document

## Introduction

Five diagram canvases draw on an unbounded plane with no scrollbars: **c4**, **wardley-map**, **azure-pipeline**, **ansible-structure** and **helm-charts**. Five others already have them — timeline, mindmap, dependency-graph, databricks and rdf — through the shared `src/client/src/canvas/scroll/` component. This specification closes that gap by making the remaining canvases consume the component that already exists.

The user's constraint is the whole point and is treated here as a requirement rather than a preference: **use the centralized, reusable version; do not invent new ones.** The failure mode is not hypothetical. The [`drag-and-drop-centralization`](../../../specs/drag-and-drop-centralization/requirements.md) survey counted the same gesture machinery hand-rolled across ten canvases — ten private `panRef`s, eight private `dragRef`s, seven private `zoomBy`s — each copied from whichever canvas came before. A sixth private scrollbar would be that pattern repeating while somebody was watching it happen.

**Two facts in the brief turned out to be wrong, and both change the work.**

* **`sparql` has no canvas to add scrollbars to.** `src/diagrams/sparql/` contains `api/` and `backend/` only — there is no `client/` folder and no canvas component anywhere. It is mid-implementation. It therefore cannot be in scope: there is nothing to wire. Requirement 7 records what should happen instead, which is that its canvas is built with scrollbars rather than retrofitted with them.
* **Helm is not the outlier for scrolling.** The drag survey correctly identifies helm as the odd one out *for gestures*, because it drives its viewport through an SVG `viewBox` where the others use a pixels-per-unit view. For scrolling that framing inverts: of the canvases in scope, **c4, wardley-map, azure-pipeline and helm-charts all use the same `ViewBox {x, y, w, h}` model**, and it is `rdf` — an existing, working scrollbar consumer — that uses pixels-per-unit. Helm is in the majority here, and the shared component's own documentation anticipates it in as many words: *"This component knows nothing about seconds, rows, nodes or view boxes. It takes two axes of plain numbers and reports two new starts; each canvas converts at the call site."*

**Can all of them take the shared component unchanged? Yes — verified against the contract, not assumed.** `CanvasScrollbars` takes two `ScrollAxis` values of four plain numbers each (`viewStart`, `viewSpan`, `extentStart`, `extentEnd`) and reports two new starts. A `ViewBox` maps onto that directly — `viewStart: view.x, viewSpan: view.w` horizontally and `view.y`/`view.h` vertically — with no conversion, no new props and no widening. The component is already unit-agnostic; that is what it was built to be. This specification therefore expects **no change to `CanvasScrollbars.tsx` or `scrollGeometry.ts` at all**, and Requirement 6 makes that expectation falsifiable rather than merely stated.

**Ansible is somebody else's, and this spec does not touch it.** Agent 7's in-flight `ansible-refinements` task 3.2 adds scrollbars to the ansible canvas and explicitly names the others. Ansible is therefore listed above as one of the five without scrollbars — which is the true count — but is **out of scope here**, so the two pieces of work meet rather than collide. That leaves this specification four canvases: **c4, wardley-map, azure-pipeline and helm-charts**.

**Why a canvas ends up without scrollbars**, from having built one of them: nothing forces the question. A canvas is written to draw its elements, and pan-and-zoom on the wheel and a drag of empty space is enough to make it feel finished — the author never meets the moment where a reader is lost off-screen with no way to tell where. The shared component is one import and about fifteen lines at the call site, so the omission is not cost; it is that the canvas works well enough for the person who just built it and has the whole layout in their head. That is precisely the kind of gap a specification is for.

## Alignment with Product Vision

* [product.md](../../../steering/product.md)'s **"A familiar surface"** — every other editing surface a reader knows shows where the view sits in the content. A canvas that pans silently is the one place ADP asks a user to keep the map in their head.
* product.md's **"Don't reinvent, integrate"** — read here as an instruction not to reinvent *ourselves*: the component exists, five canvases use it, and the fourth-through-eighth private copy is the thing this spec forbids.
* [tech.md](../../../steering/tech.md)'s **centralised-styling and shared-canvas rules** — chrome lives in the shared library and the module supplies its own semantics at the call site.
* [structure.md](../../../steering/structure.md)'s **core-vs-module boundary** — a canvas converts its own view model to plain numbers; the shared component learns nothing about any module.

## Requirements

### Requirement 1 — The four canvases gain scrollbars, from the shared component

**User Story:** As a reader of a large diagram, I want to see where my view sits inside the whole drawing, so that I can tell at a glance that there is more to the right and how much.

#### Acceptance Criteria

1. WHEN the c4, wardley-map, azure-pipeline and helm-charts canvases are rendered THEN each SHALL display a horizontal and a vertical scrollbar drawn by `CanvasScrollbars` from `src/client/src/canvas/scroll/`.
2. WHEN a canvas wires the component THEN it SHALL import it rather than reimplement it, and no module SHALL contain its own scrollbar, thumb, track or scroll-geometry code.
3. WHEN a canvas computes its axes THEN it SHALL convert its own view model at the call site, so the shared component continues to know nothing about view boxes, map space, stages or charts.
4. WHEN this specification is complete THEN nine of the ten canvases that draw SHALL use the shared component, and the tenth (sparql) SHALL be covered by Requirement 7.

### Requirement 2 — Scrollbars that work, not scrollbars that are present

**User Story:** As a reader, I want the scrollbar to actually move my view and to actually describe it, so that it is a control rather than a decoration.

#### Acceptance Criteria

1. WHEN a thumb is dragged THEN the canvas's view SHALL pan to the position the thumb describes, in the same frame and on the axis dragged.
2. WHEN the view is panned by any other means — a drag of empty canvas, a keyboard pan, a reveal that scrolls an element into view — THEN both thumbs SHALL move to describe the new view, so the bars and the canvas never disagree.
3. WHEN the view is zoomed THEN each thumb's SIZE SHALL change to describe the fraction of the extent now visible, because a thumb that only moves reports position while hiding magnification.
4. WHEN the content changes such that its bounds change — elements added, removed or repositioned — THEN the extent SHALL be recomputed and the thumbs SHALL describe the new extent.
5. IF the view spans at least its whole extent THEN the thumb SHALL fill its track and dragging it SHALL do nothing, rather than inviting a pan that cannot happen.
6. **An acceptance test SHALL assert the behaviour of 2.1 through 2.3 and not merely the presence of the component.** A test that asserts only that `CanvasScrollbars` is imported or rendered SHALL NOT satisfy this requirement, because it passes on a canvas whose thumb is inert — which is the exact defect this requirement exists to prevent.

### Requirement 3 — Each canvas's extent, in its own terms

**User Story:** As a reader, I want the scrollable area to make sense for the diagram I am looking at, so that the thumb is not describing a space that has nothing in it.

#### Acceptance Criteria

1. WHEN a canvas computes its extent THEN it SHALL use the shared `scrollExtentOf` helper rather than its own padding arithmetic.
2. WHEN a content-laid-out canvas computes its extent — c4, azure-pipeline, helm-charts — THEN the extent SHALL be the drawn content's bounds plus a margin, so a reader can pan slightly past the outermost element rather than having it flush to the edge.
3. WHEN the wardley-map canvas computes its extent THEN it SHALL derive it from the map's own fixed coordinate space rather than from content bounds, because a Wardley map's plane is the 0..1 space itself and a map with two components on it still has the whole space to show.
4. WHEN a canvas is empty, or holds a single element, or holds elements all at one point THEN the extent SHALL remain valid and the thumbs SHALL remain draggable, per the degenerate-extent floors the shared geometry already applies.

### Requirement 4 — The bars sit where the canvas's own chrome allows

**User Story:** As a reader, I want the scrollbars at the edges of the drawing surface without covering the things I am reading.

#### Acceptance Criteria

1. WHEN a canvas renders the bars THEN they SHALL sit at the canvas's bottom and right edges, matching the placement the five existing consumers already establish.
2. IF a canvas carries chrome of its own along an edge — the wardley map's axes and their labels most obviously — THEN the bars SHALL be inset via the component's existing `className` prop, which exists for exactly this and is how a canvas with a ruler already handles it.
3. WHEN the bars are styled THEN their appearance SHALL come from the shared `scrollView.css`, and no module stylesheet SHALL restyle the thumb, the track or their dimensions.

### Requirement 5 — Nothing the reader already relies on changes

**User Story:** As an existing user of these four diagrams, I want everything else about them to behave exactly as it did, so that gaining scrollbars costs me no relearning.

#### Acceptance Criteria

1. WHEN scrollbars are added THEN wheel zoom, empty-canvas pan drag, element selection, activation and every context gesture SHALL behave as before.
2. WHEN a canvas has an element drag of its own — helm-charts repositioning a node into the `.adp` `layout:` block — THEN that gesture SHALL be unaffected, and a drag begun on a scrollbar thumb SHALL NOT be interpreted as an element or canvas gesture.
3. WHEN the bars are added THEN each canvas's existing tests SHALL continue to pass unchanged, and any test that needed adjusting SHALL have its adjustment explained rather than merely made.

### Requirement 6 — The shared component grows once, centrally, or not at all

**User Story:** As a maintainer, I want the outcome of this work to be one scrollbar implementation, so that the next canvas has one obvious thing to use.

#### Acceptance Criteria

1. WHEN the work is complete THEN there SHALL be exactly one scrollbar implementation in the client, under `src/client/src/canvas/scroll/`.
2. **The expectation is that `CanvasScrollbars.tsx` and `scrollGeometry.ts` need no change at all**, because the contract is already unit-agnostic and a `ViewBox` maps onto it directly.
3. IF a canvas genuinely cannot consume the component as it stands THEN exactly two outcomes SHALL be permitted: the shared component grows **once, centrally**, in a change that serves every consumer and keeps the existing five working; or that canvas is **documented in `src/client/src/canvas/scroll/readme.md` as unable to use it, with the specific reason**. Building a private scrollbar for that canvas SHALL NOT be permitted, and neither SHALL leaving it silently without one.
4. IF the shared component is changed THEN the change SHALL be additive and the five existing consumers SHALL be verified unaffected, by their own tests.
5. WHEN the work is complete THEN a guard SHALL exist that fails if a module client folder gains its own scrollbar implementation — the drag-and-drop survey's evidence being that copied gesture code spreads silently, and a rule nothing enforces is a rule that decays.

### Requirement 7 — sparql, which has no canvas yet

**User Story:** As the agent building the sparql canvas, I want to know that scrollbars are expected of it, so that it ships with them instead of joining a later catch-up spec.

#### Acceptance Criteria

1. `src/diagrams/sparql/` currently holds `api/` and `backend/` only, with no `client/` folder — so sparql SHALL be out of scope for this specification, because there is no canvas to wire.
2. WHEN the sparql canvas is built THEN it SHALL consume the shared component at that point, and its own specification's canvas task SHALL say so rather than leaving it to be discovered.
3. WHEN this specification's work completes THEN sparql SHALL be the only drawing canvas without scrollbars, and that SHALL be recorded in the scroll readme as pending rather than as an exemption.

### Requirement 8 — Where this meets `drag-and-drop-centralization`

**User Story:** As a maintainer of two overlapping specs, I want the seam named in advance, so that neither spec quietly undoes the other.

#### Acceptance Criteria

1. **The seam is already agreed in the other direction.** `drag-and-drop-centralization` Requirement 3.4 states that a gesture already centralized elsewhere stays where it is, naming scrollbar-thumb dragging in `scroll/` explicitly and warning that duplicating it under `drag/` would create the second implementation that spec exists to prevent. This specification SHALL keep to the same line from its side: scrollbar dragging stays in `scroll/` and is not reimplemented as a `drag/` derivative.
2. **Both specs write the same viewport state.** A pan gesture and a scrollbar thumb both move the view, so whichever lands second SHALL leave the other's writes working; neither SHALL take private ownership of the view state.
3. **Helm's `viewBox` is that spec's question to answer, not this one's.** Its Requirement 10.2 requires helm's viewBox viewport to be reconciled with the shared drag derivatives or documented. This specification SHALL NOT pre-empt that decision, and its own finding is narrower and independent: for *scrolling*, a `viewBox` needs no reconciliation at all, because the scroll contract consumes view position and span in content units, which is exactly what a `viewBox` holds.
4. WHEN both specs are complete THEN pan-by-drag and pan-by-thumb SHALL move the same view through the same state, on every canvas that has both.

## Non-Functional Requirements

### Code Architecture and Modularity

- One scrollbar implementation, in the shared library; each canvas contributes only the conversion from its own view model to plain numbers.
- No module gains scroll geometry, thumb maths or scrollbar styling of its own.

### Performance

- Adding scrollbars SHALL NOT add a re-render per pointer move beyond what each canvas already does while panning; the thumb follows the view state the canvas already holds.
- The extent SHALL be derived from bounds the canvas already computes for its own layout, recomputed when content changes rather than per frame.

### Usability

- The bars SHALL be visible enough to find and quiet enough to ignore, taking their appearance from the shared stylesheet so all nine canvases look and behave alike.
- A thumb SHALL remain large enough to grab however large the extent grows, per the shared geometry's existing size floor.

### Reliability

- A canvas with no content, one element, or every element at one point SHALL render valid, draggable bars rather than dividing by zero or producing an invisible thumb.
