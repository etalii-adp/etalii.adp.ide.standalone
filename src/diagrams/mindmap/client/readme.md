# Mind map — client

The web client's mind map view: the canvas, its model and its stream hook, its stylesheet, and
the `register.ts` the shell discovers it through.

This is a workspace package (`@adp/diagram-mindmap-client`). It lives outside `src/client/`, so
it declares its own dependencies and is installed by the workspace root at `src/package.json` —
without that, nothing here could resolve `react`.

- **`register.ts`** is the entry point as far as the shell is concerned. It exports the
  registrations the shell's `diagramCanvases.ts` picks up by globbing every
  `src/diagrams/<type>/client/register.ts`, and it imports this module's stylesheet so the CSS
  arrives with the module rather than living in the shell's `index.css`.
- **Imports back into the shell** go through the `@client` alias, defined in `vite.config.ts`,
  `vitest.config.ts` and `tsconfig.json`. Relative paths out of this folder would break on every
  move; the alias is the module's side of a deliberate boundary.
- **`mindmap.css`** is namespaced under `.mindmap-*` throughout, so nothing here can collide
  with a shell rule whatever order the bundler emits. Rule order *within* the file is
  load-bearing: `.mindmap-node-focused rect` and `.mindmap-node-drop-target rect` must stay
  after `.mindmap-node rect`, which is the bug recorded in `tests.md` — equal specificity, so
  source order decides, and jsdom cannot catch it.

See [../../readme.md](../../readme.md) for what this folder is for, and
[`mindmap-diagram`](../../../../.spec-workflow/specs/mindmap-diagram/) for this diagram type's
spec.
