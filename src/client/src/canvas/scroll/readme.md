# Scroll view

Two thin scrollbars over a canvas's edges, each drawing a thumb that says where the view sits
inside the content, and each draggable to pan there. Extracted from the timeline, where it
grew, and renamed generically; every canvas in the repository now consumes this one, and the
next picks it up by importing `@client/canvas/scroll/CanvasScrollbars`.

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

## Who consumes it

Every diagram canvas in the repository, without exception: ansible-structure, azure-devops-pipeline,
c4, databricks, dependency-graph, helm-chart, mindmap, rdf (with owl and skos), sparql,
timeline and wardley-map. The remaining modules under `src/diagrams/` have no client canvas at
all, so there is nothing to give bars to.

Two shapes of caller, and the difference is worth knowing before writing a third:

- **A canvas driving an SVG `viewBox`** - c4, azure-devops-pipeline, helm-chart, wardley-map - needs
  no measurement. Its view's span in content units *is* `w` and `h`, so `scrollAxesOf` is four
  numbers straight out of the view box and the extent, with no ref and no fallback for the
  frame before layout settles.
- **A canvas driving pixels-per-unit** - rdf, sparql, databricks, timeline - has to measure its
  own element and divide, so it passes a `DOMRect` (or a width) in and tolerates a null while
  the first layout is pending.

The `viewBox` shape is strictly the easier of the two here, which is worth stating because the
same model is the awkward one for pointer-delta gestures. A model can be inconvenient for drags
and convenient for reporting a viewport; these are different questions.

## The rule, and what enforces it

A module never builds its own bars. It imports this folder, converts its view at its own call
site, and if it needs them placed differently it passes a `className` and overrides only the
offsets. `noPrivateScrollbars.test.ts` fails the build, naming the file, when a module
stylesheet defines scrollbar styling of its own or a module component builds a thumb or track
itself.

That guard exists because the alternative was measured. The drag-and-drop survey found the same
pan and drag state hand-rolled across ten canvases, each copied from whichever came before, and
these bars were five private copies away from the same fate when the work to centralize them
started. Each individual copy looks reasonable in review; only the tenth one looks like a
problem, and by then it is one.
