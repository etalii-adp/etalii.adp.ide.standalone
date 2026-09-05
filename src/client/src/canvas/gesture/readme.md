# gesture

The click-or-drag arbiter every canvas presses through: one gesture at a time, its verdict
decided **when it ends**, from what the gesture itself recorded - where it started and
whether it ever exceeded the one movement threshold. `press(target)` wires anything
pressable (an element, a relation), `background(target)` wires the surface itself, and the
module's callbacks say what a press, a drag-move, a drag-end and an abandonment mean there.
Targets are opaque: what an element *is* stays each module's business, exactly as
`selection.ts` keeps for selection shapes and `interaction.ts` for keyboard.

## Why there is no `click` handling here, and why none may be added

This module exists because selection used to be decided by the browser's trailing `click`
event, and that event is the wrong witness. Whether it fires at all and where it lands
depend on DOM geometry after the drop: it targets the common ancestor when the press and
release land on different elements, and it never fires when the pointer is released outside
the window. Canvases that relied on it needed a one-shot "suppress the next click" flag
armed when a drag ended - and a flag whose consumer is not guaranteed is a latch by
construction. One canvas armed it from `onMouseLeave`, a moment when no click can follow;
the latch then silently swallowed the next legitimate click, which is the user-reported bug
the `selection-after-drag` specification traces in full.

The fix is not a better flag; it is that nothing here listens to `click`, so there is
nothing to suppress and nothing to latch. **Adding a click subscription to this module would
reintroduce the defect it exists to delete.** A canvas that wants double-click activation
keeps its own `onDoubleClick` - that event is independent of this arbiter and unaffected by
it.

## Why the pointer is captured on the pressed element, not the surface

Capture retargets every subsequent pointer event - and the browser's own `click` - to the
capturing element. Capturing to the surface therefore moves clicks off the pressed element,
which broke click-to-select on the ansible canvas while every unit test stayed green (jsdom
implements no capture; the comment on its `onNodePointerDown` tells the story). Capturing on
the element keeps events where the wiring is, and makes a release outside the surface or the
window end the gesture through the ordinary `pointerup` - which is what retires the
mouseleave-as-pointerup hacks. `lostpointercapture` is the abandonment signal: a gesture
interrupted by anything resolves to `onDragAbandon` instead of leaking armed state.

## What this module deliberately does not do

Render, position, clamp, zoom, scroll, or know what selection is - the modules and the
other `canvas/` folders do. It does not handle the right button (the context menu's
gesture), double-click, or keyboard; a module's Escape handler calls `abandon()`. It never
merges the gesture *kinds* (reposition, pan, connect, placement, reparent - see the
`drag-and-drop-centralization` specification): it only tells their owner whether a drag
happened at all.
