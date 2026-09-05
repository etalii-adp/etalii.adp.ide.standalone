# Requirements Document

## Introduction

Tester 1 pressed **Fit to View** on a viewport-filtered ontology and it showed **less** than the reader already had. On `owl-time`: zooming out five times climbed the drawn set from **55 elements to 114** as the backend delivered what came into view; pressing Fit to View dropped it to **85**, and it stayed at 85 across four further presses. A stable fixed point, not a transient.

**Nothing is broken, and that is the whole difficulty.** The view-delta loop culled those elements because the fitted view is smaller — which is the loop doing exactly what its specification says. The view-delta tests assert that a view change produces deltas, and it does. Nothing ever asserted that Fit to View shows the whole document, because until the loop landed, it always did. A correct mechanism, a satisfied specification, a passing suite, and a defect that exists only because two true things became true at once.

### The defect is not in Fit to View

It is that **the client treats the set it has been delivered as if it were the document**. `OwlCanvas.fitToView` computes bounds from `[...model.nodes.values()]` — precisely what the last viewport admitted. It fits to the subset, reports a smaller window, the backend culls the difference, and pressing again re-fits the now-smaller set. Fit to View is where a Tester happened to see it.

**It has a second consumer already, and nobody has reported that one.** The same content-derived bounds feed `scrollExtentOf` on ansible-structure, azure-pipeline, c4, causal-loop, databricks and mindmap among others — so on a filtered diagram the scrollbar thumbs describe the delivered set and claim nearly the whole track while content exists outside it. That is `canvas-scrollbars`' Requirement 1.4 quietly becoming false for the same reason. **Any answer that fixes only the fit will leave the second instance in place**, which is why this specification is about where a canvas gets the document's extent, not about one button.

### What was measured

Twelve canvases call `fitToView`, and that number is not the blast radius.

| | Canvases | Why |
| --- | --- | --- |
| **Affected** | rdf, owl, shacl, skos, timeline, dependency-graph, databricks, c4, azure-pipeline, sparql | The fit derives its bounds from the delivered model. |
| **Affected, differently spelled** | mindmap | `fitToView` sets the view to `null`, and `null` resolves to `fitBoxOf(elements)` — the same content-derived box, reached by a different route. Its scrollbars read the same box. |
| **Immune by construction** | wardley-map | `fitToView` is `setView(fullView)`: the map's extent is the notation's own fixed 0..1 space, a property of the diagram type rather than of its content. **A canvas whose extent is intrinsic needs nothing from this specification**, and the requirements SHALL NOT force one on it. |

### What the backend already knows

`OwlSession.LaidOut()` projects and positions **the whole ontology, unbudgeted, cached**, and its comment says why: *"a class the reader can pan to needs a position, and it can only have a stable one in a layout of everything."* Every filtering module works this way, because `creating-a-diagram-module.md` requires it — lay out the whole document, then cull. **So the extent already exists server-side and costs nothing to compute.** What is missing is only that nothing carries it to the client: `ViewUpdate` runs client-to-backend, `UpdateViewResponse` carries an error string and nothing else, and no message in `diagrams.proto` mentions bounds or extent.

## Alignment with Product Vision

* [product.md](../../steering/product.md)'s **"Value from day one"** — a reader who presses Fit to View is asking to see the whole thing, and on the largest documents, where the loop matters most, is currently shown least.
* [tech.md](../../steering/tech.md)'s **gRPC call shapes** — the paired-leg design is settled; this asks what the backward leg should carry, not whether to change the shape.
* [view-delta-adoption](../../archive/specs/view-delta-adoption/requirements.md) — this is the first defect the loop has produced in the product, and it will reach every module that spec touches.
* [canvas-scrollbars](../../archive/specs/canvas-scrollbars/requirements.md) — its Requirement 1.4 depends on the extent being the document's, so the second consumer is already specified and already silently wrong.

## Requirements

### Requirement 1 — A canvas can obtain the document's extent, not the delivered set's

**User Story:** As a reader, I want Fit to View to show me the whole diagram, so that the command means what its name says however much of the document has been delivered.

#### Acceptance Criteria

1. WHEN a canvas needs the document's extent THEN it SHALL be able to obtain the extent of the **whole document**, independent of what the viewport has admitted.
2. WHEN a diagram is viewport-filtered THEN the extent SHALL NOT change as elements arrive or are culled — it is a property of the document, and a value that moves with delivery is the defect wearing a new name.
3. WHEN a document changes THEN its extent SHALL be re-reported, since an added or removed element genuinely changes it.
4. WHEN a module does not filter by viewport THEN the extent SHALL still be correct, so that a canvas has one code path rather than two.
5. WHEN a diagram type's extent is **intrinsic** — wardley-map's fixed coordinate space is the worked case — THEN that canvas SHALL keep using it and SHALL NOT be made to ask for a content extent it does not want.

### Requirement 2 — Where the extent comes from, decided rather than assumed

**User Story:** As an implementer, I want the transport settled with its alternatives written down, so that the decision is not silently remade in the design.

#### Acceptance Criteria

1. WHEN the extent is delivered THEN it SHALL travel on the **`Open` stream**, arriving with the baseline and again whenever the document changes.
2. WHEN this is compared to putting it on `UpdateViewResponse` THEN the stream SHALL be preferred, and the reason SHALL be recorded: the extent is a property of the *document*, and answering it to a *view report* ties a document fact to a view event, re-sends it on every pan, and leaves it undefined for a client that has not yet reported a viewport — including at open, which is exactly when Fit to View is most likely to be pressed.
3. WHEN this is compared to a separate RPC THEN the stream SHALL be preferred, because a second call must be correlated, retried and ordered against the deltas it describes, and the stream already does all three.
4. WHEN this is compared to computing it client-side from what has been delivered THEN that SHALL be rejected outright: it is the present behaviour and the present defect.
5. WHEN the extent is added to the contract THEN it SHALL reuse the existing `BoundingBox` message rather than introducing a second shape for the same idea.
6. WHEN a module has no meaningful extent to report THEN it SHALL be able to send none, and a canvas receiving none SHALL fall back to what it does today rather than fit to nothing.

### Requirement 3 — The guard, which is the hard half

**User Story:** As a maintainer, I want a test that fails against today's behaviour, because today's behaviour passes every test we have.

#### Acceptance Criteria

1. WHEN the guard is written THEN it SHALL fail against the current implementation and pass after the fix, and SHALL be **seen to do both** before it is accepted. This is the requirement most likely to be satisfied in appearance only: a test that asserts the extent field is populated proves the transport and says nothing about the defect.
2. WHEN the defect is expressed as a test THEN it SHALL be the **fixed point** the Tester found, not a single fit: open filtered, fit, and assert the fitted view covers the document's extent — then fit **again** and assert the view is unchanged. Today the second fit shrinks it further, and idempotence is the property a reader actually relies on.
3. WHEN the backend half is tested THEN it SHALL assert that the reported extent covers elements the viewport **excluded**, since an extent that happens to equal the delivered set's proves nothing on a document small enough not to be filtered.
4. WHEN the client half is tested THEN it SHALL assert the fit derives from the reported extent and not from the model, by fitting a canvas whose delivered model is deliberately smaller than the reported extent.
5. WHEN a canvas is fixed THEN a manual check SHALL be added to `tests.md` reproducing the Tester's sequence — zoom out repeatedly, note the count climbing, press Fit to View, and confirm the count does not fall — because the fixed point was found by a human doing exactly that and no unit test would have looked.
6. WHEN the guard is designed THEN it SHALL be **one shared guard over the registered canvas modules**, not a twelfth per-canvas test, and it SHALL also catch a canvas that supplies **no** view controls at all. A per-canvas test catches a canvas that fits *wrongly*; **a canvas that registers nothing has nothing to test, so its absence cannot fail anything.** That is this specification's own shape appearing a third time — a correct guard, a satisfied specification, and the defect living in the gap between them.
7. WHEN that shared guard is written THEN `causal-loop` SHALL be the case it is proven against: `CausalLoopCanvas` never calls `useRegisterDiagramView`, so with a `.cld` open the shell registry holds `null` and the ribbon disables Zoom In, Zoom Out and Fit to View under the title *"Open a diagram to use this."* — wrong twice over, since a diagram **is** open and opening one would not help. Twelve canvases register; that one does not, and no test in the tree notices.

### Requirement 4 — One answer for twelve canvases, or a stated reason why not

**User Story:** As the developer who will do this, I want to know whether I am making one change or eleven.

#### Acceptance Criteria

1. WHEN the fix is designed THEN it SHALL be established whether one shared mechanism serves every affected canvas, and the finding SHALL be recorded either way rather than assumed in either direction.
2. WHEN a shared mechanism is possible THEN the extent SHALL be exposed to canvases through one shared seam, and an affected canvas's change SHALL be to read it instead of computing bounds from its model.
3. WHEN a canvas genuinely cannot use the shared mechanism THEN its reason SHALL be recorded in that canvas's own module, and it SHALL NOT be worked around locally.
4. WHEN the fix lands THEN **both** consumers SHALL be corrected — the fit and the scroll extent — because they read the same wrong bounds and fixing one leaves the other quietly false.
5. WHEN this specification is implemented THEN it SHALL be by one developer through every canvas in series, per the ownership rule; the canvases are independently landable for ordering and clean merges, not for extra hands.
6. WHEN `causal-loop` is wired to the view controls it lacks THEN it SHALL happen **after** this specification, not before. Developer 4 declined to fix it for the right reason: the only `fitToView` writable today is `setView(null)`, which **is** this defect, so wiring one now buys an enabled button by importing a known bug. Its own SOM layout computes over the whole model and can supply the extent when the contract exists.

### Requirement 5 — Nothing that works today stops working

**User Story:** As a reader of a diagram that is not filtered, I want nothing to change at all.

#### Acceptance Criteria

1. WHEN a diagram is small enough that the viewport admits everything THEN Fit to View SHALL behave exactly as it does now.
2. WHEN a canvas is fixed THEN pan, zoom, the scrollbars and the view-delta loop SHALL be unaffected, and the loop SHALL keep culling — **this specification does not weaken the filtering**, it stops the client asking for the wrong window.
3. WHEN the extent is unavailable — an older backend, a module that reports none — THEN the canvas SHALL degrade to today's behaviour rather than fail.
4. WHEN this specification is complete THEN no diagram SHALL have lost a view control it had before.

## Non-Functional Requirements

### Code Architecture and Modularity

- The extent is one value with two consumers today and more later; it belongs in one place that both read, not copied into each.
- Nothing in the shared canvas library learns what a class, a task or a span is: the extent is four numbers.

### Performance

- Reporting the extent SHALL NOT cause a re-layout. Every filtering module already lays out the whole document and caches it, so the extent is read from work already done.
- The extent SHALL NOT be re-sent on every view report; it changes when the document changes.

### Reliability

- Fit to View SHALL be **idempotent**: pressing it twice leaves the view where pressing it once did. The absence of that property is how this defect was noticed.
