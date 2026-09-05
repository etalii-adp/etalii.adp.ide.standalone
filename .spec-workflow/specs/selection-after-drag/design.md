# Design Document

## Overview

One small gesture arbiter in the central canvas library replaces two defective mechanisms:
the one-shot suppression flags of the C4 and mindmap canvases, and the raw `click` handlers
through which relations are selected everywhere. The arbiter owns exactly one question —
*was this pointer gesture a click or a drag?* — and answers it when the gesture **ends**,
from what the gesture itself recorded. It never subscribes to the browser's `click` event.

That refusal is the whole fix. The reported latch exists because a completed drag arms a
one-shot flag (`dragJustEndedRef`) that a *later* `click` is meant to consume, and that click
is not guaranteed to fire, not guaranteed to land where the flag expects, and — on C4 — is
armed from `onMouseLeave` at a moment no click can follow. Remove the dependence on the
trailing click and there is no flag to arm, nothing to consume it, and nothing to latch.

The design subtracts. The flag model's four refs, their arming sites and their guarded-click
plumbing are deleted; the relation click handlers become one-line wirings of the same press
path elements use; the mouseleave-as-pointerup hack and the mindmap leave-handler flush
retire because pointer capture makes them unnecessary. What each module keeps is exactly what
`selection.ts`'s own header already prescribes for shared canvas code: the meaning. What a
commit does (move to a coordinate, reparent onto a node), what a selection names, which
targets exist — all module business, passed in as callbacks.

## Steering Document Alignment

### Technical Standards (tech.md)

- **State changes flow from the backend as deltas; the client reconciles.** This design does
  not touch that flow. Selection is a client-local decision about which element the context
  channel names, and the defect is entirely in *who decides a gesture was a click* — no wire
  message, resolver or delta changes.
- **The client renders and interacts; the backend owns the files.** The arbiter is pure
  interaction: it converts pointer events into click/drag verdicts and hands them back to the
  module, which does with them exactly what it does today. Nothing here reads or writes a
  document.
- **jsdom is the client test substrate.** `AnsibleCanvas.test.tsx` already exercises
  `setPointerCapture` under jsdom with a stub; the arbiter's capture-based design is testable
  the same way, so the correctness this design leans on (capture ends a gesture off-surface)
  is verifiable in the suite rather than only in a browser.

### Project Structure (structure.md)

- **Core canvas infrastructure must not depend on any single diagram type's schema.** Gesture
  handling is canvas infrastructure that has never been held to that rule — it lives copied
  in the modules. The arbiter puts the click/drag decision in the core library under a
  diagram-neutral name, with the target it threads left opaque, so no diagram type's
  vocabulary enters core. This is the same rule `selection.ts` already applies to selection
  shapes.
- **Diagram-type code may depend on core; core must never depend on a diagram type.** The
  arbiter depends on nothing module-specific; each canvas depends on the arbiter. The
  dependency direction is the one structure.md mandates.
- Touched: `src/client/src/canvas/` (new `gesture/` folder) and the canvases under
  `src/diagrams/*/client/` that carry the defect. No new project, no backend change, no proto
  change.

## Code Reuse Analysis

### Existing Components to Leverage

- **`src/client/src/canvas/selection.ts`** — the precedent this follows. Its header states the
  house division exactly: *"What an element is stays each module's business; that a selection
  names one is not."* The arbiter sits beside it and applies the same division to gestures.
- **`src/client/src/canvas/interaction.ts`** — the keyboard half of the same idea already
  centralized: the backend holds the key-to-action table, the shared helper turns a keystroke
  into data, the module passes in which keys matter. The arbiter is the pointer half of that
  design, and belongs beside it.
- **`AnsibleCanvas` pointer capture** (`AnsibleCanvas.tsx`, and its `setPointerCapture` stubs
  at `AnsibleCanvas.test.tsx:111,247`) — the one canvas that already captures the pointer.
  Its approach is the model the arbiter generalizes; its test stubs are the jsdom precedent
  for testing capture.
- **The eleven pointer-up canvases' element rule** (`RdfCanvas.tsx:322`,
  `DatabricksCanvas.tsx:415`, `DependencyGraphCanvas.tsx:469` and kin) — *"a press with no
  movement is a click"*, decided at pointer-up. The arbiter is this exact rule, lifted out of
  eleven copies and made the one rule for everything pressable, elements and relations alike.
- **`elementSelectionOf` / `select`** (`selection.ts:16`, the context channel) — unchanged.
  The arbiter's `onPress` callback calls `select(elementSelectionOf(...))` exactly as every
  click handler does today; the arbiter never learns what selection is.
- **`useElementContextMenu`** — the right-click path, untouched. The arbiter handles the
  primary button only; a `button !== 0` press is ignored exactly as every canvas ignores it
  now.

### What is deliberately NOT reused

- **The `dragJustEndedRef` / `panJustEndedRef` model** (`C4Canvas.tsx:92,97`,
  `MindmapCanvas.tsx:66`, `PipelineCanvas.tsx:84`) — deleted, not generalized. It is the
  defect; centralizing it would be the outcome this specification exists to prevent, and is
  the outcome `drag-and-drop-centralization` would reach by faithfully preserving it.
- **`onMouseLeave={onSurfacePointerUp}`** (`C4Canvas.tsx:439`) and the mindmap leave-handler
  flush (`MindmapCanvas.tsx:292`) — both exist only to paper over the pointer leaving the
  surface mid-drag. Pointer capture removes the underlying problem, so both retire rather than
  move.

### Integration Points

- Each converted canvas's element, relation and background handlers, which become arms of one
  `usePointerGesture` call.
- `drag-and-drop-centralization`'s planned `drag/` folder: its gesture derivatives obtain
  their click-versus-drag verdict from this arbiter. This design places the arbiter so that
  interlock is a dependency, not a duplication (Requirement 6).
- `multi-select`'s future modifier handling: the `onPress` payload is where a Ctrl/Shift flag
  will later be read, so this design keeps that seam a single call site per canvas.

## Architecture

### Where the arbiter lives

`src/client/src/canvas/gesture/` — beside `selection.ts` and `interaction.ts`, inside the
folder plan `drag-and-drop-centralization` lays out. It is upstream of that specification's
five gesture derivatives (reposition, pan, connect, placement, reparent): it decides only
*whether* a drag happened, never *what kind*, so it neither merges those derivatives nor is
merged into any of them.

### The state machine

One gesture at a time, held in a ref, driven entirely by pointer events:

```
idle
  --pointerdown, button 0, on a target--> pressed { target, pointerId, startX, startY, moved:false }
  (non-primary button: ignored, stays idle — the menu's gesture, not ours)
pressed
  --pointermove past threshold--> dragging   (moved := true; onDragMove(target, dx, dy) per move)
  --pointerup, !moved-----------> idle        VERDICT click : onPress(target)
  --pointerup,  moved-----------> idle        VERDICT drag  : onDragEnd(target, dx, dy)
  --Escape / lostpointercapture-> idle        VERDICT none  : onDragAbandon(target)
```

Three properties carry the entire correction:

1. **The verdict is computed at `pointerup` from `moved`** — the record the gesture itself
   wrote — so it cannot be stolen by whatever the DOM places under the release point, and
   cannot be swallowed by a flag nobody consumed. This is the eleven-canvas element rule made
   universal.

2. **`setPointerCapture(pointerId)` at press.** A captured pointer delivers its `pointermove`
   and `pointerup` to the capturing element wherever the pointer travels, so a release outside
   the surface or outside the window still ends the gesture through the ordinary path. This is
   what retires `onMouseLeave={onSurfacePointerUp}` (the deterministic C4 latch: a mid-drag
   leave arms a flag no click can follow) and the mindmap leave flush.
   `lostpointercapture` doubles as the abandonment signal, so a gesture interrupted by
   anything — alt-tab, the element being removed mid-drag by an incoming delta — resolves to
   `onDragAbandon` instead of leaking armed state.

3. **One movement threshold, one distance rule.** A single exported constant using the
   hypotenuse rule replaces the 4px-hypotenuse (`C4Canvas.tsx:207`) versus 3px-Manhattan
   (`DatabricksCanvas.tsx:363`) divergence — the same intent hand-copied apart. The constant
   lives with the arbiter because the threshold *is* the click/drag boundary this module owns.

### The API a canvas sees

```ts
const gesture = usePointerGesture<Target>({
  onPress:       (target) => ...,          // unmoved release: what pressing means here (select, focus)
  onDragMove:    (target, dx, dy) => ...,  // live feedback; omit for targets that cannot move
  onDragEnd:     (target, dx, dy) => ...,  // the commit: moveElementTo, reparent — module semantics
  onDragAbandon: (target) => ...,          // restore whatever onDragMove showed
});

<g      {...gesture.press({ kind: "element",    id, x, y })} />
<circle {...gesture.press({ kind: "relation",   id })} />
<svg    {...gesture.surface()} />           // move / up / cancel wiring + capture bookkeeping
```

- `gesture.press(target)` yields the `onPointerDown` wiring for anything pressable.
  `gesture.surface()` yields the surface's move/up/lostpointercapture wiring.
- **`Target` is opaque to the arbiter** — `{ kind, id, ... }` typed by the module, threaded
  through untouched, exactly as `elementSelectionOf` threads ids. The arbiter never reads a
  target's fields; only the module's callbacks do.
- **Background is a target like any other** (`kind: "background"`): an unmoved background
  press is `onPress` (deselect), a moved one is the pan the module wires in `onDragMove` /
  `onDragEnd`. Pan and click-to-deselect thus ride the same verdicts, which is why the pan
  flag can go too.
- **Relations are the same call with a different `kind` and no `onDragMove`** — the whole of
  Requirement 3.4's symmetry, and the seam `multi-select` later widens by adding modifier
  flags to the `onPress` payload without touching the machine.

### What the arbiter deliberately does not do

- It does not render, position, clamp, zoom or scroll — the derivatives and the modules do.
- It does not know what selection is — `onPress` hands back the target; the module selects.
- It does not handle the right button, double-click activation
  (`onDoubleClick` on the causal-loop, helm and ansible canvases), or keyboard shortcuts —
  those stay exactly where they are.
- It does not debounce, defer or animate — one pointer event in, at most one callback out.

### The conversions

**C4Canvas** (serving all seven C4 types) — delete `dragJustEndedRef`, `panJustEndedRef`,
both arming sites, both guarded early-returns and the `onMouseLeave` wiring;
`onNodeClick` / `onRelationshipClick` / `onBackgroundClick` collapse into `onPress` arms of
one `usePointerGesture`; the existing drag body becomes `onDragMove` / `onDragEnd` unchanged
in substance; `endInlineEditBeforeGesture` stays at press. Net deletion, and the canvas
serving seven diagram types loses its latch.

**MindmapCanvas** — the same, with `onDragEnd` performing the reparent it does today
(`moveElement(drag.id, target.id)`) and the drop-target highlight riding `onDragMove`. The
node-drag-ends-over-background case stops deselecting because a completed drag simply *is not*
a click (Requirement 2.1) — nothing needs to remember anything for that to hold. The leave
flush (`MindmapCanvas.tsx:292`) is deleted; capture covers it.

**PipelineCanvas (azure-pipeline)** — pan and background press onto the arbiter;
`panJustEndedRef` deleted. Small on purpose: this canvas has no element drag.

**Relation handlers in the pointer-up canvases** (`DependencyGraphCanvas.tsx:540` and every
selectable connection, enumerated by grep during implementation, not from memory) — the raw
`onClick` becomes `gesture.press({ kind: "relation", id })`. The trailing click a drag fires
over a relation then reaches no selection handler at all, which ends the theft without adding
a guard anywhere.

**The remaining element paths** in the pointer-up canvases already behave correctly and are
left for `drag-and-drop-centralization` to migrate onto the same arbiter as part of its
structural pass — per its Requirement 4, now reading this document as the definition of the
boundary it preserves.

### Deletion table — every site the fix removes

| Site | Today | After |
| --- | --- | --- |
| `C4Canvas.tsx:92,97` | two suppression refs | gone |
| `C4Canvas.tsx:241,256` | two arming sites | gone |
| `C4Canvas.tsx:264-268,274-278` | two guarded early-returns | gone |
| `C4Canvas.tsx:439` | `onMouseLeave={onSurfacePointerUp}` | gone (capture) |
| `C4Canvas.tsx:207` | 4px-hypotenuse threshold | shared constant |
| `MindmapCanvas.tsx:66,271,288` | ref and two arming sites | gone |
| `MindmapCanvas.tsx:159-163,180-185` | two guarded early-returns | gone |
| `MindmapCanvas.tsx:292-299` | leave-handler state flush | gone (capture) |
| `PipelineCanvas.tsx:84,231,239-243` | pan flag, arming, guard | gone |
| relation `onClick` handlers | raw click selection | one-line `gesture.press` |
| per-canvas thresholds | 4px hypot / 3px Manhattan | one constant |

## Data Models

No persisted or wire data changes. One transient client-side type, internal to the arbiter:

```ts
/** The one gesture in flight, or null. Held in a ref, never in React state — a pointer move
    and its pointerup can batch into one render, and reading state there is a frame behind. */
type ActiveGesture<Target> = {
  target: Target;      // opaque; the module's, threaded through
  pointerId: number;   // the captured pointer
  startX: number;      // client coordinates at press
  startY: number;
  moved: boolean;      // has the hypotenuse from (startX,startY) exceeded the threshold
};
```

The `dx`/`dy` handed to `onDragMove` / `onDragEnd` are client-pixel deltas; converting them to
diagram units stays each canvas's job, because the unit system differs per canvas (pixels-
per-unit view versus the helm SVG `viewBox`) and is the module's business, not the arbiter's.
`Target` is a module-supplied generic; the arbiter constrains it to nothing.

## Error Handling

### Error Scenarios

1. **Pointer released outside the surface or window.** Handled by design: capture routes the
   `pointerup` back, so the gesture ends normally. Without capture this is the C4 latch; with
   it there is no special case.
2. **Gesture interrupted (focus lost, element removed mid-drag by a delta).** `lostpointer­
   capture` fires → `onDragAbandon(target)` → the module restores whatever `onDragMove`
   previewed. No armed state survives, so the next gesture is clean.
3. **A second pointer down while a gesture is active** (a stray touch, a chorded button). The
   arbiter ignores presses while non-idle: one gesture at a time, and the active one owns the
   captured pointer. The second press starts nothing.
4. **`onDragEnd` commit rejected by the backend** (e.g. a move the resolver refuses). Nothing
   changes here: the arbiter has already returned to idle; the module handles the rejection as
   it does today (`DatabricksCanvas.tsx:408` sets a rejection message). The arbiter does not
   wait on, or care about, the commit's outcome.

## Testing Strategy

### Unit Testing

The arbiter in jsdom, using `AnsibleCanvas.test.tsx`'s `setPointerCapture` stubs as the
precedent: press-then-release yields `onPress` and never `onDragEnd`; press-move-release
yields `onDragEnd` and never `onPress`; a release dispatched off the surface still ends the
gesture (capture); `lostpointercapture` yields `onDragAbandon`; the threshold boundary at
exactly the constant, both sides. At least one of these is written to fail first and seen to
fail, per the house rule.

### Integration / Canvas Testing

The three deterministic reproductions of the reported defect, each **written against today's
code and seen to fail before any fix**, then kept as the guard:

1. **C4 latch.** Press node A, move past threshold, fire the surface's mouse-leave, release,
   click node B → assert B is selected. Fails today because the leave-armed flag swallows the
   click.
2. **Mindmap latch.** Drag node A onto parent P (the reparent's layout moves A from under the
   pointer), then click node B → assert B is selected. Fails today for the same latch. A
   second reproduction: drag a node and release over empty canvas → assert the node stays
   selected; fails today because the trailing background click deselects it.
3. **Relation theft.** On dependency-graph, drag node A to a point over relation R, release →
   assert the selection still names A's entry, not R. Fails today because R's raw click
   handler fires on the trailing click.

Each is then verified against its own defect by sabotage — reintroducing the old model in the
smallest way that compiles — and must redden **exactly** its own reproduction, because this
repository has already produced three client tests that passed against the code they were
written to catch (`processes.md`, *Verifying that a test actually tests something*).

### Guard Testing

A one-time completion check, not a standing test: `grep -rn JustEndedRef src/` returns nothing
once the conversions land (Requirement 4.1). It is deliberately not encoded as a test, because
a guard over an identifier name is a guard over prose — it would need editing whenever the
name legitimately changed, which is a copy of the data, not a check on it.

## Sequencing

**This specification lands before `drag-and-drop-centralization`'s migration begins**
(Requirement 6.1). That migration pledges to preserve "the still-click-selects rule … exactly
as … today", and today's rule at the boundary is the defect; executed first, it would
faithfully centralize the latch. Landing this first means the rule it preserves is the
corrected one, and its gesture derivatives take their verdict from an arbiter that already
exists.

If that specification's owner begins first regardless, the correct action is to stop and
raise it, not to race — the two touch the same canvases, and a merge between them is cheapest
when this one is already in.

Within this specification the order is: the arbiter first (it is the dependency of every
conversion), then the four conversions in any order (they touch disjoint files), then the
gate-and-merge. Nothing here needs the backend, so the backend suite is unaffected and the
client gates are the ones that matter.
