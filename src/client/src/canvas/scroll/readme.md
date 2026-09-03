# Scroll view

Two thin scrollbars over a canvas's edges, each drawing a thumb that says where the view sits
inside the content, and each draggable to pan there. Extracted from the timeline, where it
grew, and renamed generically; the mindmap consumes the same one, and the next canvas picks it
up by importing `@client/canvas/scroll/CanvasScrollbars`.

- `CanvasScrollbars.tsx` - the component. It takes two `ScrollAxis` values of plain numbers and
  reports two new starts through `onPan`; it knows nothing about seconds, rows, nodes or view
  boxes, and each canvas converts at its own call site. Dragging a thumb only pans - zoom stays
  on the wheel and the ribbon.
- `scrollGeometry.ts` - the two pure calculations, free of React: `thumbOf`, where a view sits
  inside its extent as track fractions, and `scrollExtentOf`, the content's bounds plus a
  margin. The margin is the reasoning: a bar over an unbounded plane has to let the user drag a
  little past the content, or the plane stops feeling unbounded. Its shape differs per canvas
  (the timeline's horizontal margin is proportional, its vertical a fixed two rows), so the
  helper is parameterized and the units stay the caller's.
- `scrollView.css` - the look, on the theme variables. A canvas that needs its bars placed
  differently overrides only the offsets, through the `className` prop, never these rules.

Why not a native scrollbar: the canvas is an unbounded plane whose size is a transform, and a
native bar cannot describe that. These are windows onto an extent, not bars onto a sized
element. Both are `aria-hidden` for the same reason the timeline's were - they duplicate a pan
the canvas drag already offers, and announcing them adds noise rather than access.
