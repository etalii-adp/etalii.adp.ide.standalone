# C4 — client

The web client's C4 view: one canvas for six of C4's seven notations, its model and its stream
hook, its stylesheet, and the `register.ts` the shell discovers it through.

This is a workspace package (`@adp/diagram-c4-client`). It lives outside `src/client/`, so it
declares its own dependencies and is installed by the workspace root at `src/package.json` —
without that, nothing here could resolve `react`.

- **One canvas, six types.** `c4/context`, `c4/container`, `c4/component`,
  `c4/system-landscape`, `c4/dynamic` and `c4/deployment` are views of one model, so they share
  a canvas rather than having six. The backend declares all seven in one `Diagram.Definitions`
  array for the same reason.
- **`c4/code` has no canvas, and says so itself.** Its registration carries an `unsupported`
  explanation instead, so the reason lives with the module that understands the notation rather
  than in a fallback in the shell. C4 specifies UML class or entity-relationship notation for
  that level and advises generating it from an IDE (c4-diagrams Requirement 11).
- **Imports back into the shell** go through the `@client` alias, defined in `vite.config.ts`,
  `vitest.config.ts` and `tsconfig.json`.
- **`c4.css`** is namespaced under `.c4-*` throughout, so nothing here can collide with a shell
  rule whatever order the bundler emits.

See [../../readme.md](../../readme.md) for what this folder is for, and
`c4-diagrams` (removed from the tree; in history before `ece03c36`) for this diagram type's spec.
