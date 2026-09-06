# Design Document

## Overview

The requirements now carry the survey — both readings of it, the three adoption tiers, the two-threshold finding, and the layering rule that the five derivatives sit **on** `gesture/usePointerGesture` (Requirement 1.5). This design does not repeat any of that; it decides what the requirements left open:

- **where the boundary between a derivative and a module runs**, and the one place this design departs from Requirement 3.1's letter to keep Requirement 8.2 answerable;
- **what the render discipline actually is**, because coalescing — the fix an implementer reaches for first — satisfies Requirement 5.3 while leaving 5.1 and 5.2 failing;
- **the migration order**, which the tiers in the requirements' survey decide almost by themselves;
- **the shape of the two guards**, which are the specification's spine.

Nothing here invents gesture machinery. The arbiter answers *whether* a drag happened; each derivative answers *what kind*; the module answers *what it means*. A derivative that re-implements a threshold, a capture or an abandonment path has broken Requirement 1.5, and the guard treats it as a private implementation.

## Alignment with Steering Documents

### Technical Standards (tech.md)

- **Consumers are pure subscribers** — a derivative reports a gesture and its geometry. It does not decide what an element is, does not interpret a refusal, and does not name an action.
- **Discovery over lists** — the guards discover canvases rather than listing them, so the thirteenth module is covered without an edit.

### Project Structure (structure.md)

- **Canvas rendering infrastructure must not depend on any diagram type's schema.** Gesture handling is the last part of the canvas never held to that rule. Moving the mechanics into `canvas/drag/` under diagram-neutral names, with every diagram-dependent decision crossing as a typed callback, is that rule applied.
- **Dependency direction** — module to library, never back. A derivative imports React and the canvas library and nothing else.

## Code Reuse Analysis

### Existing components to leverage

| Piece | Where | Used how |
| --- | --- | --- |
| `usePointerGesture` | `canvas/gesture/` | **Unchanged, and the foundation.** Every derivative composes it for the click-or-drag verdict, the capture discipline and the abandonment signal (Requirement 1.5). |
| `gesture/readme.md` | `canvas/gesture/` | Its prohibition on click subscriptions binds every derivative, and Requirement 1.5 restates it. |
| `connections/readme.md` | `canvas/connections/` | The register Requirement 7.1 names: one entry per derivative, what it is, which module it grew in, what stays with the module. |
| `scroll/` | `canvas/scroll/` | Requirement 1.4: scrollbar-thumb dragging stays there and is *named* in the new readme, never copied under `drag/`. |
| `noPrivateScrollbars.test.ts` | `canvas/scroll/` | The shape of the Requirement 6.3 guard, including how it states its own limit. |
| `selection.ts`, `interaction.ts` | `canvas/` | The precedent for threading module-owned meaning through shared code untouched. |

### What is not being built

No new gesture arbiter, no click handling anywhere, no second scrollbar drag, and no merged general-purpose gesture (Requirement 1.3). The readme says all four out loud.

## Architecture

### The layering, stated once

```
module canvas
  └── canvas/drag/<derivative>/     the kind of gesture, and its frame discipline
        └── canvas/gesture/         was this a click or a drag, and when did it end
```

### The five derivatives, and what each takes as a parameter

Requirement 3.1 lists "coordinate conversion" among the mechanics a derivative carries. **This design departs from that in one narrow way, and it is the departure that makes Requirement 8.2 answerable.** Conversion is injected as a single function the derivative calls, not implemented inside it:

```ts
toUnits(clientX: number, clientY: number): { x: number; y: number }
```

The derivative owns *when* conversion happens — once per frame, against a rect captured at gesture start — and the module owns *what it computes*. That split is what lets `helm-charts` keep its `viewBox`-driven viewport while using the same reposition derivative as a pixels-per-unit canvas, so Requirement 8.2 has both of its branches genuinely available rather than only the keep-your-own-viewport one. Recorded in *Deviations*.

| Derivative | Mechanics it owns | Crosses the boundary as |
| --- | --- | --- |
| `reposition/` | press-to-move bookkeeping, cached rect, per-frame transform, commit at end | `toUnits`, the live node ref, `onCommit(target, x, y)` |
| `pan/` | background drag, view delta accumulation, per-frame view write | the view type, `onViewChange` |
| `connect/` | anchor press, live endpoint tracking, drop-target resolution | `toUnits`, `resolveTarget(clientX, clientY)`, `onConnect(from, to)` |
| `placement/` | HTML5 `dragover`/`drop`, drop position | `toUnits`, `onPlace(payload, x, y)` — the payload's shape is the module's |
| `reparent/` | element-onto-element pointer drag, hover target tracking | `resolveTarget`, `onReparent(child, parent)` |

Requirement 2.3 is what the third column exists for: `rel:` and `new:` prefixes, action ids and payload field names are all module vocabulary and cross as values, never as literals in shared code.

### The render discipline, and why coalescing alone does not satisfy Requirement 5

Requirement 5.3 asks for coalescing and 5.1 asks that non-gesture elements not re-render. **These are different properties and coalescing is the weaker one.** Capping at one frame per display refresh reduces how *often* the whole canvas is rebuilt; it does not stop each rebuild being O(elements). A design that implements only 5.3 will measure better and still fail 5.2's ratio on a large model.

So the discipline has three parts, all in the derivative:

1. **The transient position never enters React state.** The derivative writes the dragged node's `transform` directly, through a ref the module supplies, and React state is written **once**, at gesture end. Per-frame cost becomes a function of the gesture, not of element count — Requirement 5.1 stated as a mechanism.
2. **The rect is read once, at gesture start**, and cached for the gesture's life. Requirement 5.4 is then satisfied structurally rather than by care: there is no per-frame layout read left to forget to remove. This is what retires the `toUnitsX`/`toUnitsY`-per-frame pattern the un-migrated canvases share, where a connect drag performs two synchronous `getBoundingClientRect` calls per pointer frame.
3. **Pointer moves are coalesced to `requestAnimationFrame`**, one applied frame per displayed frame, the last position winning. Requirement 5.3.

Part 1 is the one that will be argued with, because writing DOM outside React is unidiomatic. The alternative — memoizing every element component in every canvas so React can skip them — is rejected under *Considered and declined*: it puts the discipline in twelve modules, which Requirement 5.5 forbids in as many words.

### Ordering

The requirements' three tiers decide it:

**Tier A first, because extraction is provably faithful there.** `c4`, `mindmap` and `azure-pipeline` already press everything through the arbiter with a rich target type, so lifting their reposition and placement handling into `drag/` is a move rather than a rewrite, and Requirement 4.1's criterion applies at its sharpest: the module's existing tests pass **unchanged**, or the extraction changed behaviour.

**Tier B second**, where the arbiter already governs relations and the remaining private drag, pan and inline threshold are removed onto the derivatives. These canvases end the tier with one click-or-drag boundary instead of two, which is Requirement 4.2's convergence applied.

**Tier C last, and `rdf` as one task rather than four.** `OwlCanvas`, `RdfCanvas`, `ShaclCanvas` and `SkosCanvas` are near-identical same-day copies; migrating them separately would mean reviewing the same diff four times and letting three drift while the first lands. These canvases are also wired to `React.MouseEvent`, so each Tier C migration is mouse-to-pointer as well — the capture-semantics hazard the requirements' survey names, tested per canvas rather than assumed.

**`helm-charts` last of all** (Requirement 8.2), because its `viewBox` viewport is the case that proves the injected `toUnits` boundary was drawn in the right place. If it needs a change to a derivative, that is a finding about the derivative, reported rather than worked around locally (Requirement 3.4).

## Components and Interfaces

### Component 1 — `canvas/drag/` and its readme

The folder, the five subfolders, and `readme.md` in the `connections/` register: one entry per derivative naming what it is, the canvas it grew in, and what stays with the module (Requirements 7.1, 7.2). It records four things the code cannot:

- that the derivatives are **deliberately unmerged**, none implemented in terms of another, and that unifying them is a later and separate decision (Requirement 1.3);
- that **scrollbar-thumb dragging lives in `scroll/`** and the **click-or-drag verdict lives in `gesture/`**, and neither is duplicated here (Requirements 1.4, 1.5);
- that **no click subscription may be added**, pointing at `gesture/readme.md` for the defect history;
- **the measurement from the requirements' survey**, as the reason the render discipline lives in shared code, so a later reader does not mistake the constraint for an accident (Requirement 7.4).

### Component 2 — the five derivatives

Each is one folder, one hook, one test file, composing `usePointerGesture`. Extracted from its Tier A or Tier B home, renamed to the mechanism (Requirement 2.2), and parameterized exactly as far as it took to move — the standard `connections/` set.

### Component 3 — the per-canvas migrations

One task per canvas file, not per module — the counting rule the requirements' survey states: `BundleCanvas`, `JobCanvas` and `PipelineCanvas` are twelve-line wrappers rendering the shared inner `DatabricksCanvas`, which is where the gesture code actually lives. A migration keyed on modules would either triple the work or miss the file that holds it.

### Component 4 — the guards, which are the spine

**4a — no private derivative** (Requirement 6.3). A text guard in the `noPrivateScrollbars` shape: walk every module canvas file, fail on any that declares its own drag, pan, connect, placement or reparent bookkeeping instead of composing the shared one, naming every offender at once. Phrased **per canvas file**, so the three databricks wrappers are neither offenders nor false compliance. Its limit is stated in its own doc-comment, as `noPrivateScrollbars` states its: it reads text, so a canvas that reinvents a gesture under an unrecognizable private name goes unseen. It catches the copy, which is how this actually happens — the requirements record five canvases hand-rolling drag and pan in one day, each starting from the canvas beside it.

**4b — the cost does not scale** (Requirements 6.1, 6.2). One test, one run, two models of the *same* diagram type — ten elements and one at the drawn-element budget — dragging the same number of pointer frames, asserting a **ratio** between the two per-frame costs against a tolerance stated in the test itself. Never an absolute millisecond budget, which becomes a flaky test on a slower machine.

**Both guards must be seen to fail before they are accepted, and 4b is the one that will be accepted green by mistake.** The perturbation is precise and available: restore the `setDrag`-per-frame path in the canvas under test and the ratio must blow the tolerance. A ratio test that has never been watched to fail is asserting arithmetic, not a property.

### Component 5 — the measurements and the manual check

Requirement 8.1's before-and-after on the RDF and Helm models from the survey, recorded in the readme beside the reason. Requirement 8.3's `tests.md` entry for dragging the Wikidata and laureates examples in a real browser, because jsdom measures work done, not smoothness perceived.

## Data Models

No new persisted or wire model. A derivative's state is a ref living for one gesture; what crosses to the module is a target of the module's own type and two numbers.

## Error Handling

| Scenario | Handling |
| --- | --- |
| Gesture abandoned (Escape, lost capture, unmount) | `usePointerGesture`'s `onDragAbandon`; the derivative reverts the live transform and dispatches nothing. Nothing new is invented here. |
| Module refuses the gesture | Surfaced verbatim from module or backend; the derivative neither composes nor interprets it (Requirement 3.3). |
| Drop target cannot be resolved | `resolveTarget` returns nothing and the gesture ends as a no-op, exactly as the canvases behave today. |
| A derivative cannot be extracted without a diagram-specific rule | Left in its module, with the reason in the readme (Requirement 3.4) — not generalized into shared code. |

## Testing Strategy

### Per migration

Requirement 4.1 is the strongest tool this specification has and it is nearly free: **the canvas's existing tests must pass unchanged.** A test that must change is evidence of a behaviour change and is treated as a finding, not as a test to update. The one systematic exception is the click-or-drag boundary, which Requirement 4.2 now resolves in the open: convergence on the arbiter's threshold is deliberate, stated in each migration's log, and a threshold-sensitive test that must change for that reason cites 4.2 rather than being quietly updated.

### Verification by sabotage

Every guard is perturbed and seen to fail for the right reason before acceptance. Two specific traps, both of which this repository has walked into:

- **A ratio test that never failed** asserts nothing. Named under Component 4b, with its perturbation.
- **A seam whose only test asserts the negative case** passes whether or not the positive path works. The relevant seam here is the derivative-to-module callback: a test proving that an abandoned gesture dispatches nothing must be paired with one proving that a completed gesture dispatches exactly once.

### Coverage

The mechanical requirement-coverage diff runs against the tasks document before implementing and against the finished code afterwards, and each unclaimed criterion is labelled *no task claimed it* or *no task can claim it* in the tasks document.

## Considered and declined

**Memoizing every element component instead of writing the transform directly.** The idiomatic React answer, and rejected because it distributes the discipline across twelve modules and depends on every element receiving stable prop identities per frame — a property nothing checks and every future canvas can break silently. Requirement 5.5 requires the discipline to live in the shared derivative; this puts it everywhere else.

**Coalescing to `requestAnimationFrame` and stopping there.** It satisfies Requirement 5.3 and improves the measurement enough to look finished, while leaving each frame O(elements) and Requirement 5.2's ratio failing on a large model. Recorded because it is the change an implementer under time pressure will reach for first.

**Merging `reposition` and `reparent`, which differ only in what the drop means.** Requirement 1.3 forbids it, and the requirements are right: the two have different refusal shapes and different undo contracts. Named here because they are the pair most likely to be merged by someone who has not read the readme.

**Adding a `click` derivative for symmetry.** Forbidden by Requirement 1.5 and `gesture/readme.md` in as many words. Recorded so the symmetry argument is met once and does not have to be met again.

**Migrating `rdf`'s four readings as four tasks.** Declined; they are same-day copies of one another and would be reviewed four times and drift three ways.

## Deviations and notes

**Requirement 3.1 lists coordinate conversion among the mechanics; this design injects it.** The derivative owns when conversion happens and against which cached rect; the module owns what it computes. Without that split, `helm-charts`' `viewBox` viewport could not use the shared derivatives at all, and Requirement 8.2 would have only its second branch available. This is the design's one departure from the requirements' letter, and it is in their spirit: every diagram-dependent decision crosses the boundary as a typed callback.

**The threshold question is no longer a design deviation.** An earlier revision of this design had to depart from Requirement 4.2 to resolve the two-boundary state; the updated requirements resolve it themselves, and this design simply implements the convergence they specify.

**One measurement in this design is quoted, not reproduced.** The per-frame millisecond figures in the requirements' table were measured on 2026-09-03 and are relied on here only for the *shape* of the finding — cost tracks drawn elements, not which canvas draws — which is separately confirmed by the identical `setDrag`-per-frame mechanism in every un-migrated canvas. The before-and-after numbers Requirement 8.1 asks for are new measurements taken during implementation, not comparisons against this quote.
