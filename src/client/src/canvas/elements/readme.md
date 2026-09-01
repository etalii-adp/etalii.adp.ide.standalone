# Element drawing and interaction

One folder per way of drawing a diagram element. As with `../connections/`, the
implementations are deliberately **not merged**: each was extracted from the module that grew
it, renamed generically, and parameterized just enough to move - unifying them into fewer
shapes is a later, separate decision, and easier to take with them sitting side by side here.

- `box/` - a top-left-origin rectangle with a left-aligned label and room for badges. Grew in
  the Ansible structure; also what the Azure pipeline's stages, jobs and steps draw.
- `centered-box/` - a centre-origin rounded rectangle with centred text and a corner
  indicator line. Grew in the mindmap.
- `styled-box/` - a centre-origin box whose shape and colours come from the document's own
  styling: person, cylinder, plain or rounded box, with name / type / description lines. Grew
  in the C4 canvas.
- `frame/` - the dashed enclosure around a group of elements, labelled in a corner. Grew as
  the C4 boundary.
- `symbol/` - a small mark (circle, square, double ring) with a label beside it, badges and
  an optional inertia bar. Grew in the Wardley map.
- `span/` - a horizontal extent on a row (or a diamond for a single point in time) with
  selection adorners: resize strips on the edges and connection anchors painted over them.
  Grew in the timeline.

Every component takes its CSS class names as props and spreads the rest of its props onto the
wrapping `<g>`, so interaction handlers, aria roles and `data-*` attributes stay each
module's own - and the stylesheets (and the tests that query those classes) did not have to
move with the code.
