# Mind map — client

The web client's mind map view: the canvas, its model and its stream hook, its stylesheet, and
the `register.ts` the shell discovers it through.

This is a workspace package (`@adp/diagram-mindmap-client`). It lives outside `src/client/`, so
it declares its own dependencies and is installed by the workspace root at `src/package.json` —
without that, nothing here could resolve `react`.

- **`register.ts`** is the entry point as far as the shell is concerned. It exports the
  registrations the shell's `toolPanels.ts` picks up by globbing every
  `src/diagrams/<type>/client/register.ts`, and it imports this module's stylesheet so the CSS
  arrives with the module rather than living in the shell's `index.css`.
- **Imports back into the shell** go through the `@client` alias, defined in `vite.config.ts`,
  `vitest.config.ts` and `tsconfig.json`. Relative paths out of this folder would break on every
  move; the alias is the module's side of a deliberate boundary.
- **`mindmap.css`** is namespaced under `.mindmap-*` throughout, so nothing here can collide
  with a shell rule whatever order the bundler emits. Rule order *within* the file is
  load-bearing: `.mindmap-node-drop-target rect` must stay after `.mindmap-node rect`, which is
  the bug recorded in `tests.md` — equal specificity, so source order decides, and jsdom cannot
  catch it. A selected node has no rule here at all: selection and its look are the canvas
  library's, the same on every diagram.
- **The canvas definition is compiled from the bundled DISL specification**,
  `../definition/mindmap.dis`, imported with Vite's `?raw` and compiled by the library's
  `compileNotation` at module load. What the library cannot read from it is in
  **`mindmapBindings.ts`**, each entry with its reason: the payload path of the corner glyphs
  (`indicators(self)`, computed in `MindmapCanvas.tsx`), the class names `mindmap.css` paints, the
  topic box drawn as the library's `centered-box`, the drag preview, and the key each action sends
  the backend, with Tab as a second key for add-child. `mindmapCompiledDefinition.test.ts` holds the
  compiled definition to the one this canvas stated by hand before, and
  `mindmapShapeGeometry.test.ts` holds the specification's `topicBox` to the rect the canvas draws.
- **Branches are not selectable, by decision.** A branch is a node's link to its parent, not a
  thing a reader picks, so the specification's branch edge says `selectable: false` and a press on
  one is a press on the background. That is a decision about the notation, not an omission; making
  branches selectable is a separate question for the user
  ([centralized-selection](../../../../.spec-workflow/specs/centralized-selection/requirements.md),
  Requirement 2.4).

See [../../readme.md](../../readme.md) for what this folder is for, and
`mindmap-diagram` (removed from the tree; in history before `ece03c36`) for this diagram type's
spec.
