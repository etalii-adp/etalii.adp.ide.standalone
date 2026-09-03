# Requirements Document

## Introduction

Every diagram canvas in the client hand-rolls its own pointer gestures. Ten canvases carry their own drag state, their own pan state, their own connect state and their own toolbox-drop handling, copied from whichever canvas came before and adjusted in place. This specification extracts that machinery into the central canvas library as **separate, deliberately unmerged implementations** — one folder per kind of gesture — leaving each module's own semantics where they belong: in the module.

The work was prompted by a report that dragging feels slow on the RDF and Helm chart diagrams, with the hypothesis that those two use a different implementation while the timeline, mindmap and dependency-graph diagrams use a shared, reusable one. That hypothesis was tested before this document was written. It is half right, and the half that is wrong changes what this specification has to ask for.

## What the survey and the measurement found

**There is no shared drag-and-drop implementation to be reusing.** The central canvas library holds `connections/`, `elements/`, `scroll/`, `connectors.ts`, `selection.ts`, `useElementContextMenu.ts` and `interaction.ts` — and `interaction.ts`, the only file whose name suggests gestures, is 37 lines of *keyboard* handling. Every canvas that drags does so on its own. The premise that RDF and Helm are failing to use an existing shared capability is therefore not the case: nothing shared exists yet, which is precisely what this specification creates.

**The gesture machinery is duplicated across ten canvases.** Counting the canvases that draw and interact (`grep` over `src/diagrams/*/client/*Canvas.tsx`): eight carry a private `dragRef` reposition gesture, ten a private `panRef`, four a private `connectRef` relation gesture, seven a private `zoomBy`, five a private toolbox-drop handler, and six their own anchor rendering. The RDF canvas is not an outlier here — it imports *more* of the central library (`scroll/`, `interaction`, `useElementContextMenu`, `connectors`, `selection`) than the Azure pipeline, Ansible, Wardley or Helm canvases do. Helm is the genuine outlier in the opposite direction: it uses none of `scroll/`, `interaction` or `useElementContextMenu`, and drives its viewport through an SVG `viewBox` rather than the pixels-per-unit view the others share.

**The slowness is real, but its cause is not the implementation split.** A drag was measured in jsdom over 30 pointer frames on realistic models, counting rendered DOM nodes and milliseconds per frame:

| Canvas and model | DOM nodes | ms per frame | ms per DOM node |
|---|---|---|---|
| RDF, Wikidata shape (101 cards, 763 literal rows on one) | 1,577 | 32.0 | 0.020 |
| RDF, laureates shape (1,000 cards) | 5,011 | 65.5 | 0.013 |
| Timeline, 100 spans | 308 | 5.3 | 0.017 |
| Timeline, 1,000 spans | 3,008 | 34.6 | 0.012 |

Cost tracks the number of drawn elements, not which canvas is drawing them: per DOM node every canvas measured lands between 0.012 and 0.020 ms, and the timeline at 1,000 spans (34.6 ms per frame) is as slow as the RDF canvas the report calls slow. The mechanism is the same in all of them, because it was copied: a pointer move calls `setDrag`, React re-renders the whole canvas, and every element — not just the one under the pointer — is rebuilt for that frame. RDF and Helm feel slower only because their documents routinely produce far more elements than a timeline's do; the timeline's own examples are tens of elements, so its users never reach the same wall. Left alone, the timeline will feel exactly this slow the first time somebody opens a thousand-span roadmap.

So this specification asks for two things that the report treated as one: the **structural** consolidation the user asked for, and a **per-frame render discipline** that belongs in the shared implementation so it is fixed once rather than ten times.

## Alignment with Product Vision

`structure.md` requires that canvas rendering infrastructure not depend on any single diagram type's schema, so new diagram types can be added without touching core code. Gesture handling is canvas rendering infrastructure that has never been held to that rule: it lives in the modules, is copied per module, and is therefore rewritten by every new diagram type. Moving it into the central library under diagram-neutral names, while keeping each module's semantics in the module, is that rule applied to the last part of the canvas that has not had it.

## Requirements

### Requirement 1 — One folder per gesture, deliberately unmerged

**User Story:** As a developer adding a diagram type, I want each kind of canvas gesture available as its own implementation in the central library, so that I compose the gestures my diagram needs instead of copying another module's canvas.

#### Acceptance Criteria

1. WHEN the central canvas library is read THEN it SHALL hold a `drag/` folder beside the existing `connections/`, `elements/` and `scroll/` folders, containing one subfolder per gesture derivative — the shape `connections/` already demonstrates, where `straight/`, `bezier/`, `fixed-bezier/` and `interactive-bezier/` sit side by side as separate implementations.
2. WHEN the derivatives are enumerated THEN they SHALL be the five the survey found in the canvases: **reposition** (a pointer drag that moves an element to a new position), **pan** (a pointer drag on empty canvas that moves the viewport), **connect** (a pointer drag from an element's anchor to another element, yielding a relation gesture), **placement** (an HTML5 drag from the toolbox dropped onto the canvas, yielding a drop position), and **reparent** (a pointer drag of an element onto another element, yielding a structural move rather than a position).
3. WHEN the derivatives are implemented THEN they SHALL NOT be merged into a single generalized gesture, and no derivative SHALL be implemented in terms of another; the folder's readme SHALL say so explicitly, as `connections/readme.md` does, and SHALL record that unifying them is a later and separate decision.
4. IF a gesture is already centralized elsewhere THEN it SHALL stay where it is and be named in the readme rather than moved: scrollbar-thumb dragging already lives in `scroll/`, and duplicating it under `drag/` would create the second implementation this specification exists to prevent.

### Requirement 2 — Names say what the gesture is, never who uses it

**User Story:** As a developer reading the central library, I want every name there to describe a mechanism, so that nothing in shared code reads as though it belongs to one diagram type.

#### Acceptance Criteria

1. WHEN any folder, file, exported function, hook, type or option under `drag/` is named THEN the name SHALL describe the gesture or its mechanics, and SHALL NOT contain a diagram type, vendor or module name — the standard `connections/` and `elements/` already keep.
2. WHEN a derivative's name is chosen THEN it SHALL name the gesture rather than the module that grew it, even where the implementation is lifted wholesale from one module.
3. WHEN vocabulary appears in shared code that a module invented — element-id prefixes such as `rel:` and `new:`, action ids, or payload field names — THEN it SHALL be passed in by the module or expressed as a callback result, never hard-coded in the central library.
4. WHEN the migration is complete THEN a search of `src/client/src/canvas/` for the names of the diagram types SHALL return nothing outside comments that cite provenance.

### Requirement 3 — Diagram-specific behaviour stays in the module

**User Story:** As a module author, I want my diagram's own rules to remain mine, so that sharing the mechanics does not quietly standardize semantics that differ on purpose.

#### Acceptance Criteria

1. WHEN a derivative is extracted THEN it SHALL carry only the mechanics — pointer bookkeeping, movement thresholds, coordinate conversion, frame scheduling, gesture lifecycle — and SHALL take every decision that depends on the diagram as a parameter or callback.
2. WHEN the semantics the survey found are considered THEN each SHALL remain in its module: the timeline's horizontal axis being **seconds** rather than canvas units, the Wardley map **clamping** both coordinates into its 0..1 map space, the mindmap's drop onto another node meaning **re-parenting**, the RDF canvas refusing drags rooted in **blank nodes**, and the Helm canvas driving its viewport through an SVG **viewBox**.
3. WHEN a module refuses a gesture THEN the refusal and its sentence SHALL come from the module or its backend, and the central implementation SHALL surface the refusal without composing or interpreting it.
4. IF a derivative cannot be extracted without absorbing a diagram-specific rule THEN that derivative SHALL be left in its module and the readme SHALL record why, rather than the rule being generalized into shared code.
5. WHEN a module keeps a gesture of its own THEN it SHALL still be able to use the other derivatives, so that partial adoption is possible and no canvas is forced to migrate all its gestures at once.

### Requirement 4 — Migration preserves behaviour exactly

**User Story:** As a user of any existing diagram, I want the canvases to behave exactly as they do today after the consolidation, so that a refactor costs me nothing.

#### Acceptance Criteria

1. WHEN a canvas is migrated onto a derivative THEN its existing client tests SHALL pass unchanged, and any test that must change SHALL be treated as evidence of a behaviour change rather than as a test to update.
2. WHEN the migration touches a canvas THEN the movement threshold, the still-click-selects rule, the Escape-abandons-gesture rule and the authored-position-is-stored-unrounded rule SHALL all be preserved as they are today.
3. WHEN a gesture completes THEN it SHALL dispatch exactly what it dispatches today — the same command, the same action id, the same one-undo-per-gesture contract.
4. WHEN every canvas has been migrated THEN no canvas SHALL retain a private implementation of a derivative the central library provides.

### Requirement 5 — A drag costs the same on a large diagram as on a small one

**User Story:** As a user dragging on a big diagram, I want the canvas to keep up with my pointer, so that arranging a large graph is not slower than arranging a small one.

#### Acceptance Criteria

1. WHEN an element is dragged THEN the elements that are not part of the gesture SHALL NOT be re-rendered for each pointer frame; the per-frame cost SHALL be a function of the gesture, not of how many elements the diagram draws.
2. WHEN the per-frame cost is measured on a diagram at the drawn-element budget and on a ten-element diagram of the same type THEN the two SHALL be within a stated tolerance of each other, and the tolerance SHALL be recorded in the test that measures it.
3. WHEN pointer events arrive faster than the display refreshes THEN the shared implementation SHALL coalesce them, so that one frame is rendered per displayed frame rather than one per event.
4. WHEN a canvas reads layout from the DOM during a gesture THEN the shared implementation SHALL NOT perform a synchronous layout read per pointer frame.
5. WHEN the discipline is implemented THEN it SHALL live in the shared derivative rather than in any module, so that every canvas gets it once — including the timeline, mindmap and dependency-graph canvases, which the measurement shows are only fast today because their documents are small.

### Requirement 6 — The improvement is guarded, not just achieved

**User Story:** As a maintainer, I want the performance property pinned by a test, so that the next canvas cannot quietly reintroduce the per-element cost.

#### Acceptance Criteria

1. WHEN the shared derivatives are in place THEN a client test SHALL drag on a large model and a small model of the same diagram type and SHALL fail if the per-frame cost regains its scaling with element count.
2. WHEN that test is written THEN it SHALL state its own tolerance and the machine-independent property it asserts — a ratio between two measurements taken in the same run — rather than an absolute millisecond budget, so it does not become a flaky test on a slower machine.
3. WHEN a canvas is added or migrated THEN a test SHALL fail if it implements a derivative privately instead of composing the shared one.

### Requirement 7 — The library documents itself the way it already does

**User Story:** As a developer meeting the canvas library for the first time, I want the new folder to explain its own shape, so that the next person extends it correctly instead of guessing.

#### Acceptance Criteria

1. WHEN `drag/` is created THEN it SHALL carry a `readme.md` in the register `connections/readme.md` uses: one entry per derivative saying what it is, which module it grew in, and what stays with the module.
2. WHEN a derivative's provenance is recorded THEN the readme SHALL name the canvas it was extracted from, so that a reader tracing behaviour knows where its history is.
3. WHEN the consolidation moves a touch point named in `docs/creating-a-diagram-module.md` THEN that document SHALL be updated in the same change, per the documentation-refresh rule in CLAUDE.md.
4. WHEN the readme is written THEN it SHALL record the measurement in this document's survey as the reason the render discipline lives in the shared code, so the constraint is not later mistaken for an accident.

### Requirement 8 — The report's own diagrams are verified, not assumed

**User Story:** As the person who reported that RDF and Helm feel slow, I want the fix demonstrated on the diagrams I complained about, so that the work is judged against the symptom that prompted it.

#### Acceptance Criteria

1. WHEN the migration is complete THEN the RDF canvas and the Helm chart canvas SHALL both be measured again on the same models used in this document's survey, and the before-and-after figures SHALL be recorded.
2. WHEN the Helm canvas is migrated THEN its `viewBox`-driven viewport SHALL be reconciled with the shared derivatives without changing what the user sees, or the readme SHALL record why it keeps its own viewport handling.
3. WHEN the measurement is repeated THEN a manual check SHALL be added to `tests.md` for dragging on the Wikidata and laureates examples, so the improvement is confirmed in a real browser and not only in jsdom.

## Non-Functional Requirements

### Code Architecture and Modularity

- **Single Responsibility Principle**: each derivative folder holds one gesture and nothing else; a file that handles two gestures is a merge this specification forbids.
- **Modular Design**: the derivatives depend on the central canvas library and on React, never on a diagram module; the dependency runs module to library and never back.
- **Dependency Management**: a module adopts derivatives one at a time; no derivative requires another.
- **Clear Interfaces**: every diagram-specific decision crosses the boundary as a typed parameter or callback, so the contract can be read without reading a module.

### Performance

- The per-frame cost of a gesture is independent of the number of drawn elements (Requirement 5), and that independence is asserted by a test that compares two measurements from one run (Requirement 6).
- The consolidation adds no per-frame synchronous layout reads, and removes the ones the canvases perform today where it can do so without changing behaviour.

### Security

- No new surface: gestures dispatch the same commands and actions through the same context and history paths they use today, and the central library gains no ability to write a file or bypass a backend refusal.

### Reliability

- A gesture abandoned mid-flight — Escape, a lost pointer, an unmount — leaves no state behind and dispatches nothing, exactly as the canvases behave today.
- A refused gesture leaves the document untouched, with the refusal surfaced verbatim from the module or backend that issued it.

### Usability

- Nothing the user does changes: the same gestures produce the same results, and the only difference is that large diagrams keep up with the pointer.
- Each module keeps its own cursor, styling and affordances, since styling stays with the module the way it does for `connections/` today.
