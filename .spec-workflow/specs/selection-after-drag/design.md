# Design Document

## Overview

One small gesture arbiter in the central canvas library replaces two defective mechanisms:
the one-shot suppression flags of the C4 and mindmap canvases, and the raw `click` handlers
through which relations are selected everywhere. The arbiter owns exactly one question -
*was this pointer gesture a click or a drag?* - and answers it when the gesture ends, from
what the gesture itself recorded. It never subscribes to the browser's `click` event, which
is what makes the latch unrepresentable rather than merely fixed: there is no trailing event
left to guard against, so there is nothing to guard with.

The design subtracts. The flag model's four refs, its arming sites and its guarded-click
plumbing are deleted; the relation click handlers become one-line wirings of the same press
path elements use; the mouseleave-as-pointerup hack and the leave-handler state flush retire
because pointer capture makes them unnecessary. What each module keeps is exactly what
`selection.ts`'s header already prescribes for shared canvas code: the meaning. What a
commit does (move to a coordinate, reparent onto a node), what a selection names, which
targets exist - all module business, passed in as callbacks.

## The arbiter

### Where it lives

`src/client/src/canvas/gesture/` - beside `selection.ts` and inside the folder plan
`drag-and-drop-centralization` lays out. That specification's derivatives (reposition, pan,
connect, placement, reparent) each need a click-versus-drag verdict; this module is the one
place they get it. Nothing here merges the derivatives - the arbiter is upstream of all of
them, deciding only *whether* a drag happened, never *what kind*.

### The state machine

One gesture at a time, held in a ref, owned entirely by pointer events:

```
idle
  --pointerdown on a target--> pressed { target, pointerId, startX, startY, moved: false }
pressed
  --pointermove beyond threshold--> dragging (moved = true; onDragMove fires per move)
  --pointerup, !moved--> idle       (verdict: click  -> onPress(target))
  --pointerup,  moved--> idle       (verdict: drag   -> onDragEnd(target, dx, dy))
  --Escape / lostpointercapture--> idle (verdict: none -> onDragAbandon(target))
```

Three properties carry the whole correction:

1. **The verdict is computed at `pointerup` from `moved`** - the record the gesture itself
   wrote - so it cannot be stolen by whatever the DOM happens to place under the release
   point, and cannot be swallowed by a flag nobody consumed. This generalizes the rule
   eleven canvases already apply to elements (`RdfCanvas.tsx:322` and kin) into the one rule
   for everything pressable.

2. **`setPointerCapture` at press.** A captured pointer delivers its `pointermove` and
   `pointerup` to the capturing element wherever the pointer goes, so a release outside the
   surface or outside the window ends the gesture through the ordinary path. This is what
   retires `onMouseLeave={onSurfacePointerUp}` (`C4Canvas.tsx:439`) - the line that today
   arms a suppression flag at a moment no click can follow, which is the deterministic latch
   behind the report - and the mindmap's leave-handler state flush (`MindmapCanvas.tsx:292`).
   `lostpointercapture` doubles as the abandonment signal, so a gesture interrupted by
   anything (alt-tab, element removal mid-drag) resolves to a verdict instead of leaking
   armed state. `AnsibleCanvas` demonstrates capture already works in this codebase and in
   its jsdom tests.

3. **One threshold, one distance.** A single exported constant with the hypotenuse rule
   replaces the 4px-hypot (`C4Canvas.tsx:207`) / 3px-Manhattan (`DatabricksCanvas.tsx:363`)
   divergence. The constant lives with the arbiter because the threshold *is* the
   click/drag boundary - the thing this module owns.

### The API a canvas sees

```ts
const gesture = usePointerGesture({
  // The verdicts. Every callback receives the pressed target, typed by the module.
  onPress:      (target) => ...,  // an unmoved release: select, focus, whatever pressing means here
  onDragMove:   (target, dx, dy) => ...,  // live feedback while dragging; omit for unmovable targets
  onDragEnd:    (target, dx, dy) => ...,  // the commit: moveElementTo, reparent - module semantics
  onDragAbandon:(target) => ...,  // restore whatever onDragMove showed
});

<circle {...gesture.press({ kind: "relation", id })} />
<g      {...gesture.press({ kind: "element", id, x, y })} />
<svg    {...gesture.surface()} />
```

`gesture.press(target)` yields the pointer-down wiring for anything pressable; `surface()`
yields the move/up/cancel wiring plus capture bookkeeping. A target is whatever the module
says it is - the arbiter threads it through without reading it, exactly as
`elementSelectionOf` threads element ids today. A background press is a target like any
other (`kind: "background"`), so pan and click-to-deselect ride the same verdicts: an
unmoved background press is `onPress` (deselect), a moved one is the pan the module wires.

Relations are the same call with a different `kind` and no `onDragMove` wiring - which is
the whole of Requirement 3.4's symmetry, and the seam `multi-select` later widens by adding
modifier flags to the `onPress` payload without touching the machine.

### What the arbiter deliberately does not do

- It does not render, position, clamp, zoom or scroll: the derivatives and the modules do.
- It does not know what selection is: `onPress` receives the target and the module calls
  `select(elementSelectionOf(...))` as it always has.
- It does not handle the right button: the context-menu path
  (`useElementContextMenu`) is untouched, and a `button !== 0` press is ignored exactly as
  every canvas ignores it today.
- It does not debounce, defer or animate: one pointer event in, at most one callback out.

## The conversions

**C4Canvas** - delete `dragJustEndedRef`, `panJustEndedRef`, both arming sites, both guarded
early-returns and the `onMouseLeave` wiring; `onNodeClick`/`onRelationshipClick`/
`onBackgroundClick` collapse into `onPress` arms of one `usePointerGesture`; the existing
drag body becomes `onDragMove`/`onDragEnd` unchanged in substance. Net deletion, and the
canvas serving seven diagram types loses its latch.

**MindmapCanvas** - the same, with `onDragEnd` doing the reparent it does today. The
node-drag-ends-over-background case stops deselecting because a completed drag simply is
not a click (Requirement 2.1); nothing needs to remember anything for that to hold.

**PipelineCanvas (azure-pipeline)** - pan and background press onto the arbiter;
`panJustEndedRef` deleted.

**Relation handlers in the pointer-up canvases** (`DependencyGraphCanvas.tsx:540` and every
selectable connection) - the raw `onClick` becomes `gesture.press({ kind: "relation", id })`.
The trailing click a drag fires over a relation then reaches no handler at all, which ends
the selection theft without adding a guard anywhere.

**The remaining element paths** in the pointer-up canvases already behave correctly and are
left for `drag-and-drop-centralization` to migrate onto the same arbiter as part of its
structural pass - per its own Requirement 4, now reading this document as the definition of
the boundary it preserves.

## Testing strategy

The three reproductions of Requirement 5, written against today's code and seen to fail
before any fix lands, then kept as the guard:

1. C4: press node A, move beyond threshold, fire the surface's mouse-leave, release, click
   node B - assert B is selected. Fails today because the latch swallows the click.
2. Mindmap: drag node A onto node P (the reparent moves A from under the pointer), click
   node B - assert B is selected. Fails today for the same latch.
3. Dependency-graph: drag node A to a point over relation R, release - assert the selection
   still names A's entry and not R. Fails today because R's raw click handler fires.

Each is then verified against its own defect by sabotage - reintroducing the old model in
the smallest way that compiles - and must redden exactly its own test, because this
repository has already produced three client tests that passed against the code they were
written to catch.

The arbiter itself gets unit tests in jsdom (press/click, press/drag, capture-loss
abandonment, threshold boundary at exactly the constant), using `AnsibleCanvas.test.tsx`'s
existing `setPointerCapture` stubs as the precedent for capture under jsdom.

## What gets deleted

| Site | Today | After |
| --- | --- | --- |
| `C4Canvas.tsx:92,97` | two suppression refs | gone |
| `C4Canvas.tsx:241,256` | two arming sites | gone |
| `C4Canvas.tsx:264-268,274-278` | two guarded early-returns | gone |
| `C4Canvas.tsx:439` | `onMouseLeave={onSurfacePointerUp}` | gone (capture) |
| `MindmapCanvas.tsx:66,271,288` | ref and two arming sites | gone |
| `MindmapCanvas.tsx:159-163,180-185` | two guarded early-returns | gone |
| `MindmapCanvas.tsx:292-299` | leave-handler state flush | gone (capture) |
| `PipelineCanvas.tsx:84,231,239-243` | pan flag, arming, guard | gone |
| relation `onClick` handlers | raw click selection | one-line `gesture.press` wiring |
| per-canvas thresholds | 4px hypot / 3px Manhattan | one constant |

The arbiter is one small module plus one hook adapter. Every conversion above removes more
lines than it adds, and the model that produced the defect stops existing as something a new
canvas could copy.
