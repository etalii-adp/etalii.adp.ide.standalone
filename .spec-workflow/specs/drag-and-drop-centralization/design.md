# Design Document

## Overview

The remaining scope is one discipline in one file. `DiagramCanvas` is the single gesture layer for all fifteen canvases, and it schedules gesture frames the way the ten hand-rolled canvases it replaced did — one React state write per pointer frame (`setDragOffset` for a reposition, `setView` for a pan, `setConnect` for a connect preview), each re-rendering the whole canvas, plus one synchronous `getBoundingClientRect` per move inside `unitsPerPixel`. This design replaces that scheduling without changing what any gesture means.

The mechanism: **per-frame values stop being React state.** During a gesture they flow through a small gesture-frame scheduler — a cached surface rectangle, a `requestAnimationFrame` coalescer, and live writes reaching only the gesture's own participants — and canvas-wide React state is written **once**, at gesture end. Per-frame cost becomes a function of the gesture, not of the number of drawn elements, which is Requirement 1.1 stated as a mechanism.

## Alignment with Steering Documents

### Technical Standards (tech.md)

- **Consumers are pure subscribers** — the definition contract, event handlers and module boundary are untouched (Requirement 2). Modules cannot tell the scheduling changed except by keeping up with the pointer.

### Project Structure (structure.md)

- **Canvas rendering infrastructure carries no diagram knowledge.** The scheduler moves rectangles and numbers; what an element is stays in the definition. Nothing new crosses the module boundary in either direction, and `noPrivateGestures.test.ts` continues to hold module gesture code at zero.

## Code Reuse Analysis

### Existing components to leverage

| Piece | Where | Used how |
| --- | --- | --- |
| `usePointerGesture` | `canvas/gesture/` | Unchanged. Still decides click-or-drag, capture and abandonment; the scheduler sits between its callbacks and React. |
| `DiagramCanvas` | `canvas/library/` | The one file rewired. Its `onDragMove` cases become scheduler writes; its `onDragEnd`/`onDragAbandon` become the single commit or revert. |
| `libraryGuards.test.tsx` family | `canvas/library/` | The Requirement 3 guard lands beside them, in the same walk-collect-fail-once idiom. |
| Custom shapes via the definition | `canvas/library/definition/` | The guard's lever: a test definition whose shape component counts its own renders, so "non-gesture elements did not re-render" is asserted deterministically. |
| `CanvasScrollbars` | `canvas/scroll/` | Unchanged component; during a pan its thumbs are fed live values by the scheduler rather than by per-frame React state. |

### What is not being built

No derivative folders, no migrations, no new guard for private gestures — all delivered by `diagram-library-adoption` and recorded as such in the requirements. No virtualization or culling: reducing what is *drawn* is a different lever from reducing what is *re-rendered per frame*, and it belongs to the view-delta loop, not here.

## Architecture

### The gesture-frame scheduler (`library/gestureFrame.ts`)

A small module with no React dependency beyond types:

- **The cached rect.** Captured once when a gesture begins, from the surface; every conversion during the gesture uses it. `unitsPerPixel`'s per-move `getBoundingClientRect` is retired by construction — during a gesture there is no code path left that reads layout (Requirement 1.3). A window resize mid-gesture makes the cached rect stale for the remainder of that gesture; that trade is recorded in the scheduler's doc-comment and is the same one every drag implementation in the tree already made implicitly.
- **The coalescer.** Pointer moves record the latest deltas and schedule one `requestAnimationFrame` callback if none is pending; the callback applies the newest values and clears. One applied frame per displayed frame, last position winning (Requirement 1.2).
- **The live writes.** The applied frame publishes through `LiveWrite` handles that each know how to undo themselves:
  - a **reposition** publishes the dragged element's live position to a store **only the gesture's own participants subscribe to**, so the dragged element re-renders per frame in place — *scoped re-render*, the user's ruling recorded under Deviations, because three module suites render mid-drag feedback from that live position and a pure transform write would freeze them;
  - a **connect preview** writes the preview node's geometry;
  - a **pan** writes the `<svg>`'s `viewBox` attribute and the scrollbar thumb positions — named explicitly, because committing the view only at gesture end would freeze the thumbs mid-pan, and Requirement 2's "nothing a gesture means changes" covers what the user watches, not only what is dispatched.

Requirement 1.5 is why all three kinds route through the one scheduler: fixing the reposition alone leaves the report's symptom reproducible by panning.

### The commit, and the abandon

`onDragEnd` reverts the live writes and performs today's single state write and dispatch — same command, same action id, same one-undo contract (Requirement 2.2). React then renders the final state exactly as it does now, so the post-gesture picture is produced by the same code path as before this change. `onDragAbandon` reverts the live writes and dispatches nothing (Requirement 2.3); the revert is the scheduler's, so an abandonment cannot leak a transform any more than it can leak state.

### Why the per-frame values cannot simply be memoized away

The idiomatic alternative — keep `setDragOffset` and memoize every element so React skips the unchanged ones — depends on every element component receiving stable prop identities on every frame, a property nothing checks and any later edit to the render path can break silently. It also leaves the per-frame reconciliation walk itself O(elements). The scheduler makes the cheap path structural: during a gesture, React renders only the gesture's own subscribers — the dragged element and whatever renders mid-drag feedback from it — never the canvas, so the per-frame cost is a handful of components regardless of how many the diagram draws. Declined formally below.

## Components and Interfaces

### Component 1 — `library/gestureFrame.ts`

The scheduler above: `beginFrame(surfaceRect)`, per-kind live-write handles, `applyLatest`, `commit`, `revert`. Tested on its own for coalescing (N moves, one applied frame), staleness (last value wins), and revert (all writes undone).

### Component 2 — `DiagramCanvas` rewiring

`onDragMove`'s three cases write to the scheduler instead of `setDragOffset`/`setView`/`setConnect`; gesture start captures the rect; end and abandon route through commit and revert. The canvas-wide `dragOffset` state is retired — the gesture store replaces it, and only the gesture's participants subscribe. `elementAt` hit-testing during a connect stays: it is per-frame *computation*, not per-frame *rendering*, and the requirement governs the latter.

### Component 3 — the guards

Beside `libraryGuards`, one test file with two assertions, both mounted through a test definition:

1. **The deterministic one:** a definition whose shape component counts renders; drag thirty frames on a hundred-element model; assert the non-dragged shapes rendered **zero** times during the gesture. This is the sharper property and cannot flake.
2. **The required ratio (Requirements 3.1, 3.2):** the same drag on a ten-element and a budget-sized model in one run, per-frame cost compared as a ratio with its tolerance stated in the test.

**Acceptance is by failure first (Requirement 3.3):** restore the per-frame `setDragOffset` path and both assertions must fail — the count goes non-zero, the ratio blows its tolerance. A ratio test that has never failed is asserting arithmetic.

### Component 4 — the measurements and the manual check

Fresh before-and-after figures on the Wikidata, laureates, `owl-time` and Helm models (Requirement 4.1) — both sides newly measured, never compared against the 2026-09-03 quote — recorded in the implementation log and in `DiagramCanvas`'s doc-comment beside the scheduler's reasoning. In the same run, a timeline drag at a comparable element count is measured and recorded beside the after figures (Requirement 4.2): the user's acceptance bar is *"prefer the timeline/dependency graph/causal loop drag over the owl/rdf/shacl one"*, and a number beside a number is how that preference is answered rather than asserted. A `tests.md` entry for dragging the Wikidata and laureates examples in a real browser (Requirement 4.3), because jsdom measures work done, not smoothness perceived.

## Data Models

None. The scheduler holds a rect, the latest deltas, and a pending-frame flag, all for the life of one gesture.

## Error Handling

| Scenario | Handling |
| --- | --- |
| Gesture abandoned (Escape, lost capture, unmount) | Scheduler `revert`; no dispatch, no leaked transform (Requirement 2.3). Unmount also cancels any pending animation frame. |
| Window resized mid-gesture | The cached rect serves out the gesture; the next gesture reads afresh. Recorded in the doc-comment. |
| `requestAnimationFrame` unavailable or inert (jsdom) | The scheduler applies synchronously when no frame provider exists, so unit tests exercise the same write path the browser does. |
| A commit is refused by the backend | Unchanged: the refusal surfaces exactly as today; the scheduler has already reverted its writes and the document was never touched. |

## Testing Strategy

- **Requirement 2.1 is the master gate and it is free:** every existing module and library test passes unchanged. A test that must change is evidence of a behaviour change, treated as a finding.
- The scheduler is tested alone (Component 1), the discipline through the mounted guard (Component 3), and both guards are perturbed and seen to fail before acceptance.
- A seam note carried from this specification's earlier revision: the abandon path's test — nothing dispatched — is a negative-case assertion and passes whether or not the positive path works, so it is always paired with the commit path's test — dispatched exactly once.
- The coverage diff runs against the tasks before implementing and against the finished code after, each unclaimed criterion labelled *no task claimed it* or *no task can claim it*.

## Considered and declined

**Memoizing every element component instead of a scheduler.** Distributes the discipline across every element type, depends on prop-identity stability nothing guards, and keeps the O(elements) reconciliation walk per frame. The scheduler removes React from the per-frame path instead of making React's involvement carefully cheap.

**Coalescing to `requestAnimationFrame` and stopping there.** Satisfies Requirement 1.2, improves the measurement enough to look finished, and leaves each frame O(elements) — the requirements now record this trap themselves (1.2's second clause); it is repeated here because it is the change an implementer under time pressure reaches for first.

**Committing the pan view only at gesture end without live thumb writes.** Cheapest pan fix, and visibly wrong: the scrollbar thumbs would freeze mid-pan. Requirement 2 covers what the user watches, so the thumbs are in the scheduler's write set.

**Virtualizing or culling drawn elements.** A different lever — it changes what is drawn, this specification changes what is re-rendered — and it belongs to the view-delta loop's budget machinery, not to gesture scheduling.

## Deviations and notes

**The reposition mechanism is scoped re-render, not a pure transform write — ruled by the user on 2026-09-06 during task 1.** As first approved, this design said a gesture bypasses React entirely and the dragged element's `transform` is written directly. Task 1's implementation found three module suites that depend on React rendering the dragged element mid-drag: mindmap computes its drop ring and edge preview inside the shape render from the shifted position, wardley-map's clamp test reads the circle's `cx` mid-drag, and c4's tests read the inner `.c4-node` transform and the `c4-node-dragging` state class. A pure write would freeze all three and fail their existing tests, which Requirement 2.1 forbids changing. The user chose the resolution by selection: per-frame values publish through a live-write handle to a store **only the gesture's own participants subscribe to** — the dragged element re-renders per frame in place, every non-gesture element renders zero times, and task 4's guard asserts exactly what it was written to assert. The scheduler — cached rect, coalescer, live viewBox and thumb writes, commit and revert — survives unchanged. Record: `Implementation Logs/task-1_2026-09-06T1544_7aefafa1.md`.

**jsdom's `requestAnimationFrame` exists and is a 16 ms timer — a measurement correcting this design's assumption that it is absent.** The scheduler's synchronous fallback therefore keys on *the environment does not paint* (rAF missing, or the UA naming jsdom), not on rAF's absence — the design had encoded one form of "no animation frames", and the environment satisfied the name without satisfying the property. A test pins the measured fact so a future environment change surfaces as a failure rather than as silently deferred writes. Same log.

**Dissolved earlier:** the per-canvas threshold convergence and the injected coordinate conversion, both retired when `diagram-library-adoption` delivered the structural half — one boundary exists, and conversion happens inside the one gesture layer that owns the cached rect.

**The 2026-09-03 measurement stays quoted, not reproduced.** This design relies on it only for the shape of the finding — cost tracks drawn elements — which is separately confirmed by the per-frame state writes visible in `DiagramCanvas` at HEAD. Requirement 4.1's figures are fresh on both sides.
