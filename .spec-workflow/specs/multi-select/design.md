# Design Document

## Overview

Four pieces, none reaching into another's concern, each landing where the week's consolidations put its ground:

| Piece | Lives | Because |
| --- | --- | --- |
| Set-building gestures (Ctrl+click, marquee) | `DiagramCanvas`, on the one arbiter | Every canvas renders through it; `DiagramSelection` is already `readonly SelectedItem[]`, left single-membered for exactly this specification |
| The set on the wire | `context.proto`: `ContextSelection` gains `repeated ContextSource members` | Empty for a single selection, so today's bytes are unchanged (Requirement 4.3) |
| The property merge and action intersection | Backend, above the providers, as pure functions | `ContextPropertyResolver` describes one `ContextTarget`; providers never learn about sets — the set is resolved member-by-member and merged where the answer is assembled |
| The composite command | `History/` core | The one place every module's commands already flow through |

**The single-target seams never see a set.** That is this design's organizing rule and how Requirement 4.5 is satisfied structurally rather than per provider: resolvers, providers and command handlers keep their one-target signatures, and a set is fanned out and merged by a coordinator that owns set semantics — so a provider cannot "silently answer for the first member" because no provider is ever handed more than one.

## Verified premises (2026-09-06)

- `ContextSelection` carries one `ContextSource`; `SetPropertyRequest` one source with *unset = the connection's current selection* — which is the hook the fan-out uses unchanged.
- `History/` holds dispatcher, stack, `ICommand` — no composite.
- `DescribeProperties` is a pull RPC returning `repeated ContextProperty`; the grid renders what it is handed.
- `DiagramSelection = readonly SelectedItem[]` (kind: element | connection), raised by every press through `selectionChanged`.
- Thirteen canvas files across ten modules dispatch `moveElementTo`; mindmap and azure-pipeline do not and are untouched (Requirement 7.2).

**Written after `backend-project-decomposition` task 3**: generated wire types (`ShortGuid`, `Path`, `ElementId`) were relinked into `Common.Wire` while this design was drafted; the design names proto messages and fields, which that rename does not touch, and the implementer takes the generated namespaces from the tree at implementation time.

## Steering alignment

- **structure.md** — selection stays diagram-neutral: the set is ids the modules already mint, the merge is over `ContextPropertyDefinition` records the panel already renders without understanding. No module learns about another; a non-qualifying module is not touched.
- **tech.md, consumers are pure subscribers** — the grid renders merged rows exactly as it renders single rows; the one new bit it reads is a "values differ" marker.

## Code reuse

| Piece | Used how |
| --- | --- |
| `usePointerGesture` | Unchanged. Ctrl+click is a press verdict it already delivers; the marquee is one more press target decided by it. |
| `gestureFrame` cells | The marquee's rubber band is a live-write; the group move rides the reposition cell with N participants. |
| `selection.ts` | `elementSelectionOf`/`selectedElementIdOf` unchanged for singles; gains `selectionMembersOf` beside them. |
| `ContextPropertyResolver` | Unchanged signature; called once per member by the set coordinator. |
| `noPrivateGestures.test.ts` | Already fails any module that builds a private pointer stack — a privately implemented marquee cannot avoid `setPointerCapture`/gesture state, so Requirement 8.4's guard exists; the cost guard is what gains a marquee case. |
| `dragCost.test.tsx` | Gains the marquee kind, per drag-and-drop-centralization's amended 3.1: every kind the discipline covers. |

## Architecture

### Set-building, in one place

**Ctrl+click** — the arbiter's press verdict, with the modifier read at press: unmodified press replaces the selection (today's behaviour, 1.3/1.4); Ctrl toggles membership (1.1/1.2), elements and connections alike (1.6). All inside `DiagramCanvas`'s existing `select` path; `ownSelection`/controlled selection become genuinely multi-membered.

**The marquee** — a new press-target kind on empty canvas, qualified by **Shift**: plain background drag pans exactly as today (3.4); Shift+drag draws the rubber band; Ctrl held as well makes the sweep additive (2.3). One rule, stated once in `DiagramCanvas`, no per-canvas choice (3.3). The rubber-band rectangle is a live-write through a marquee gesture cell — no canvas-wide state per frame, no layout read per move (amended 3.2) — and **coverage is computed once, at gesture end**: the rectangle itself is the live feedback 2.1 asks for, and end-only hit-testing keeps the per-frame cost gesture-shaped. Escape or lost capture reverts to the pre-drag selection recorded at press (2.4). Connections are never swept (2.5): the coverage pass collects element kind only.

**The one coverage rule (2.2): containment.** An element joins the sweep when the rectangle fully contains its box. Chosen over intersection because a rectangle dragged across a dense graph inevitably *grazes* neighbours at its edge; full containment makes the swept set predictable from what the user drew, and matches the requirement's own verb — the rectangle "covers" it. Stated once in the library beside the hit test, with this reasoning.

### Qualification: derived from the definition, never declared beside it

Requirement 7.2 needs mindmap and azure-pipeline to keep exactly today's behaviour — no rubber band, no Ctrl+click set-building — without either module being touched. The tree now holds a worked precedent for where per-module gesture variation lives: `connectOnRightDrag?: boolean` on the shared definition (`diagramDefinition.ts`), added for causal-loop's right-drag connect. This design follows that precedent's *placement* — the definition is where a canvas's gesture surface is described — but **derives rather than declares**: a canvas qualifies for multi-select where its definition already exhibits a qualifying capability — it wires `element-moved` (authored positions) or declares selectable connections — which is the requirements' own criterion read off the artifact that proves it. A boolean flag was considered and declined: it can drift from the capabilities that justify it, and thirteen definitions would carry a value derivable from what they already say. If a canvas ever qualifies wrongly under derivation, an explicit override is the recorded fallback — a requirements-level event, not a quiet flag.

The `gestureFrame` template this design builds on is now demonstrated twice by different sessions — the drag-and-drop discipline and the right-drag connect — which is better evidence that a marquee cell generalises than the original implementation alone was.

### The set on the wire

`ContextSelection` gains `repeated ContextSource members` — the selection's full membership, present only when there is more than one. A single selection serializes byte-identically to today (4.3), every existing resolver and provider path untouched. The ids are exactly the ones modules mint today (4.2); nothing new is invented, and `selection.ts` keeps minting them.

Pruning (4.4): the connection layer already re-pushes on document change; the set coordinator drops members whose source no longer resolves and pushes the surviving set. A selection never becomes invalid; it shrinks.

### Merged properties and intersected actions — pure, above the providers

`SelectionDescription` (backend, new, pure): given per-member `IReadOnlyList<ContextPropertyDefinition>` lists —

- intersection by property id (5.1);
- equal values shown; unequal marked *differing*, never one member's value standing for all (5.2/5.3) — carried to the grid as one new field on `ContextProperty` (`bool values_differ`), which the grid renders as its mixed-value presentation;
- any member read-only makes the row read-only, reason: at least one selected item refuses it (5.5);
- mixed kinds fall out of the same rule, and an empty intersection is an unremarkable empty grid (5.6);
- one member degenerates to that member's list unchanged (5.7).

Actions: the context menu for a set offers the intersection of per-member action ids, and nothing when empty (7.3). Both functions are testable with records alone — no canvas, no backend host, no diagram (NFR).

`SetProperty` with source unset already means "the current selection": when that selection is a set, the service fans out one `SetAsync` per member **inside one composite command** (5.4, 6.1). A member's refusal — module-worded, as today — aborts and rolls back the whole edit (6.3, 7.4).

### The composite command

`CompositeCommand` in `History/` core: an ordered list of member commands, executed sequentially; on any failure the applied prefix is inverted in reverse order and **nothing is recorded** (6.3). Its inverse is the members' inverses, reversed, so undo restores every document byte-for-byte in one step (6.2). Usable by any module (6.5): it is a core `ICommand` like any other, and the dispatcher records it as one entry.

**The group move** rides it: dragging a member of a multi-selection moves every selected *element* by the same delta — the reposition cell with N participants, live-writes per member, one composite of the modules' existing move commands at gesture end. The three position-storage shapes in use (6.4) never meet the composite; each member's own move command already knows its storage, and fan-out is what keeps that knowledge where it lives.

## Error handling

| Scenario | Handling |
| --- | --- |
| Marquee abandoned | Selection restored to the pre-drag set; nothing dispatched. |
| Member refused mid-composite | Applied prefix inverted, nothing recorded, module's refusal surfaced verbatim. |
| Member vanishes (edit, reload) | Pruned from the set; survivors stay selected. |
| Set reaches a single-target seam | It cannot — fan-out happens above; asserted by the coordinator's tests. |
| Provider answers for only some members | Its property simply leaves the intersection; no special case. |

## Testing strategy

- **The merge is the most test-dense piece and the cheapest to test** (8.1): records in, records out — identical, differing, partial, read-only, mixed-kind, single-member — every case from Requirement 5, no mounting.
- **Composite** (8.2/8.3): one history entry per multi-edit; undo byte-identical against fixture documents; mid-member failure applies nothing and records nothing. Each seen to fail first — the failure case by un-suppressing the rollback, the entry count by recording per member.
- **Marquee guards** (8.4): `noPrivateGestures` already fails a module-side pointer stack; the cost guard gains the marquee kind and is re-perturbed. Canaries in both shapes: population floor and named member.
- **Existing tests pass unchanged** (7.1) — the free master gate, as it was for drag-and-drop-centralization.
- `tests.md` gains the manual check (8.5): box-select, merged edit, one undo, in a real browser.

## Considered and declined

- **Intersection (grazing) coverage for the marquee** — declined for containment, reasoning above; revisit only with user feedback, as a one-line rule change in one place.
- **Teaching providers about sets** — declined; it multiplies every provider by two cases and makes 4.5 a per-provider promise instead of a structural fact.
- **A `values_differ` sentinel value instead of a field** — declined; a sentinel is a lie waiting for a property whose real value equals it.
- **Sweeping connections when fully contained** — declined by 2.5's own reasoning; revisiting it is a requirements change, not a design choice.
- **A click-event path for Ctrl+click** — forbidden; `gesture/readme.md` and the arbiter's press verdict already carry the modifier.

## Deviations and notes

None against the amended requirements at the time of writing. The census and Requirement 3's machinery were re-verified against develop the day this was written; the generated-type relink (`backend-project-decomposition` task 3) landed during drafting and touches namespaces this design deliberately does not name.
