# Requirements Document

## Introduction

This specification began as two things the prompting report treated as one: a **structural** consolidation — every canvas hand-rolled its own pointer gestures — and a **per-frame render discipline**, because the measured slowness tracked drawn-element count, not which canvas was drawing.

**The structural half has been delivered by another specification, and this document now asks only for the performance half.** On 2026-09-06, `diagram-library-adoption` landed `src/client/src/canvas/library/DiagramCanvas.tsx` (`9769cfd4`) and migrated **all fifteen canvas files** onto it. Its requirements cite this specification's survey by name — "after full adoption those counts are zero, and that is checkable" — and `library/noPrivateGestures.test.ts` is that check. What this document previously asked for structurally is recorded below as delivered, so it is not re-done; what remains is the render discipline, which survived the consolidation intact and now lives in exactly one file.

## The survey, in three readings

**2026-09-03 (this document's first version).** 13 canvas files; eight private `dragRef`, ten `panRef`, four `connectRef`, seven `zoomBy`; nothing shared existed. The per-frame measurement (below) showed cost tracking drawn elements at 0.012–0.020 ms per DOM node in every canvas measured, because each pointer move wrote React state and re-rendered the whole canvas.

**2026-09-05.** 18 canvas files and 12/12/6/12 — five canvases created on 2026-09-04 had each hand-rolled drag and pan. `selection-after-drag` had landed the click-or-drag arbiter (`gesture/usePointerGesture.ts`, `b9295b0a`), adopted by six canvases, three of them for relations only, leaving two click-or-drag boundaries live at once.

**2026-09-06 (current).** All fifteen canvas files render through `DiagramCanvas`. Private `dragRef`, `panRef`, `connectRef` and `zoomBy` in module sources: **zero, zero, zero, zero** — the only remaining matches in `src/diagrams/` are module *test* files exercising the shared layer. `DiagramCanvas` composes `usePointerGesture`, so one click-or-drag boundary remains (4 client pixels by hypotenuse). `noPrivateGestures.test.ts` guards the zeros, walking the tree and failing once naming every offender, with its text-reading limit stated in its own doc-comment.

**What survived the consolidation, confirmed in `DiagramCanvas` at HEAD.** The gesture-frame mechanism this specification measured in ten canvases now exists once, unchanged in kind:

- `onDragMove` writes React state **per pointer frame** — `setDragOffset` for a reposition, `setView` for a pan, `setConnect` for a connect preview — so every pointer frame re-renders the whole canvas, rebuilding every element rather than the one under the pointer.
- `unitsPerPixel`, called on every drag move to turn pointer deltas into canvas units, performs a synchronous `getBoundingClientRect` — **one layout read per pointer frame**.

Centralization did not fix the cost; it made the fix a change to one file instead of ten. That is the whole remaining scope.

### The measurement (2026-09-03, quoted)

A drag was measured in jsdom over 30 pointer frames on realistic models, counting rendered DOM nodes and milliseconds per frame:

| Canvas and model | DOM nodes | ms per frame | ms per DOM node |
|---|---|---|---|
| RDF, Wikidata shape (101 cards, 763 literal rows on one) | 1,577 | 32.0 | 0.020 |
| RDF, laureates shape (1,000 cards) | 5,011 | 65.5 | 0.013 |
| Timeline, 100 spans | 308 | 5.3 | 0.017 |
| Timeline, 1,000 spans | 3,008 | 34.6 | 0.012 |

Cost tracks the number of drawn elements, not which canvas draws them. These figures predate the library; Requirement 4 re-measures on the same models rather than comparing against this quote.

## Delivered elsewhere, recorded so it is not re-done

The first version of this document carried requirements for a `drag/` folder of five gesture derivatives, diagram-neutral naming, module-owned semantics, behaviour-preserving per-canvas migrations, a no-private-implementation guard, and a self-documenting readme. **`diagram-library-adoption` delivered the substance of all of them by a different route** — a declarative diagram definition consumed by one `DiagramCanvas`, rather than composable derivative hooks — and the five-folder shape is withdrawn as overtaken:

- One gesture layer exists, inside `DiagramCanvas`, built on `gesture/usePointerGesture`; module sources hold none.
- Diagram-specific semantics cross as the definition's typed fields and callbacks; shared code names no diagram type.
- `noPrivateGestures.test.ts` keeps the private-gesture count at zero, per canvas file, with the databricks wrappers correctly out of its population.
- The Helm canvas — whose `viewBox` viewport this document once treated as its hardest case — is migrated with the rest.

Nothing in this specification SHALL re-create, duplicate or re-migrate any of that. Where this document's remaining requirements touch `DiagramCanvas`, they change how it schedules gesture frames, never what a gesture means.

## Alignment with Product Vision

`structure.md` requires that canvas rendering infrastructure not depend on any single diagram type's schema. The gesture layer now satisfies that structurally; this specification makes its **cost** satisfy it too — a drag on a big diagram is the report that prompted all of this, and the reader who dragged it is still waiting.

## Requirements

### Requirement 1 — A drag costs the same on a large diagram as on a small one

**User Story:** As a user dragging on a big diagram, I want the canvas to keep up with my pointer, so that arranging a large graph is not slower than arranging a small one.

#### Acceptance Criteria

1. WHEN an element is dragged THEN the elements that are not part of the gesture SHALL NOT be re-rendered for each pointer frame; the per-frame cost SHALL be a function of the gesture, not of how many elements the diagram draws.
2. WHEN pointer events arrive faster than the display refreshes THEN they SHALL be coalesced, one rendered frame per displayed frame — and it is recorded here that coalescing alone does NOT satisfy criterion 1: it caps how often the whole canvas is rebuilt without stopping each rebuild being proportional to element count.
3. WHEN a gesture is in flight THEN no synchronous layout read SHALL be performed per pointer frame; the surface rectangle SHALL be read once at gesture start and cached for the gesture's life. `unitsPerPixel`'s per-move `getBoundingClientRect` is the named offender.
4. WHEN the discipline is implemented THEN it SHALL live in `DiagramCanvas` — never pushed into diagram definitions or modules — so every canvas, present and future, gets it from the one gesture layer.
5. WHEN a pan or a connect preview is in flight THEN the same discipline SHALL apply to them as to a reposition, because all three write React state per pointer frame today and fixing one leaves the report's symptom reproducible through the others.

### Requirement 2 — Nothing a gesture means changes

**User Story:** As a user of any existing diagram, I want every gesture to behave exactly as it does today, so that a scheduling change costs me nothing.

#### Acceptance Criteria

1. WHEN the discipline lands THEN the existing module and library tests SHALL pass unchanged, and any test that must change SHALL be treated as evidence of a behaviour change rather than as a test to update.
2. WHEN a gesture completes THEN it SHALL dispatch exactly what it dispatches today — the same command, the same action id, the same one-undo-per-gesture contract — and the still-click-selects rule, the Escape-abandons rule and the authored-position-is-stored-unrounded rule SHALL be untouched.
3. WHEN a gesture is abandoned mid-flight — Escape, lost capture, unmount — THEN it SHALL leave no state behind and dispatch nothing, including whatever transient visual the discipline was showing.

### Requirement 3 — The improvement is guarded, not just achieved

**User Story:** As a maintainer, I want the performance property pinned by a test, so that a later change to the one gesture layer cannot quietly reintroduce the per-element cost.

#### Acceptance Criteria

1. WHEN the discipline is in place THEN a test SHALL drag on a large model and a small model of the same diagram type in one run and SHALL fail if the per-frame cost regains its scaling with element count.
2. WHEN that test is written THEN it SHALL assert a machine-independent property — a ratio between the two measurements from the same run, with its tolerance stated in the test — never an absolute millisecond budget, which becomes a flaky test on a slower machine.
3. WHEN the guard is accepted THEN it SHALL first have been seen to fail for the right reason, by restoring the per-frame state write and watching the ratio blow its tolerance; a ratio test that has never failed is asserting arithmetic, not a property.

### Requirement 4 — The report's own diagrams are verified, not assumed

**User Story:** As the person who reported that RDF and Helm feel slow, I want the fix demonstrated on the diagrams I complained about, so that the work is judged against the symptom that prompted it.

#### Acceptance Criteria

1. WHEN the discipline lands THEN the RDF and Helm canvases SHALL be measured again on the same models as the 2026-09-03 survey, with fresh before-and-after figures recorded — new measurements on both sides, not comparisons against the quote above.
2. WHEN the measurement is repeated THEN a manual check SHALL be added to `tests.md` for dragging on the Wikidata and laureates examples, so the improvement is confirmed in a real browser and not only in jsdom.

## Non-Functional Requirements

### Code Architecture and Modularity

- The change is to how `DiagramCanvas` schedules gesture frames; its definition contract, its event handlers and the module boundary are untouched.
- No module gains gesture code, and `noPrivateGestures.test.ts` continues to hold that at zero.

### Performance

- Per-frame gesture cost independent of drawn-element count (Requirement 1), asserted by a same-run ratio (Requirement 3).
- No per-frame synchronous layout reads (Requirement 1.3).

### Reliability

- An abandoned gesture reverts its transient visual and dispatches nothing (Requirement 2.3).

### Usability

- Nothing the user does changes; the only difference is that large diagrams keep up with the pointer.
