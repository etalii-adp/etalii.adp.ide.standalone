# Connection drawing and interaction

One folder per way of drawing a connection between two diagram elements. The implementations
are deliberately **not merged**: each was extracted from the module that grew it, renamed
generically, and parameterized just enough to move - unifying them into fewer shapes is a
later, separate decision, and easier to take with them sitting side by side here.

- `straight/` - a straight line between two boxes' facing edge points, optional arrowhead and
  midpoint label. Grew in the C4 canvas; also what the Wardley map and the Ansible structure
  draw.
- `bezier/` - a horizontal cubic between a parent's side and a child's facing side, for tree
  layouts. Grew in the mindmap.
- `fixed-bezier/` - a horizontal cubic between two precomputed points with a fixed control
  reach, for column layouts whose rows connect left edge to right edge. Grew in the Azure
  pipeline.
- `interactive-bezier/` - the timeline's selectable relation: a facing-sides bezier that loops
  forward-and-back when the target starts before the source ends, with an invisible fat hit
  path, selection, and a context menu.

Styling stays with each module: every component takes its CSS class names as props, so the
stylesheets (and the tests that query those classes) did not have to move with the code.
The pure geometry these build on lives one level up, in `../connectors.ts`.
