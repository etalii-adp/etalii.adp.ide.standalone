# Dependency graph — client

The web client's dependency graph view: the canvas, its model and its stream hook, its
stylesheet, and the `register.ts` the shell discovers it through.

This is a workspace package (`@adp/diagram-dependency-graph-client`). It lives outside
`src/client/`, so it declares its own dependencies and is installed by the workspace root at
`src/package.json`.

- **`register.ts`** is the entry point as far as the shell is concerned, and it imports this
  module's stylesheet so the CSS arrives with the module.
- **Imports back into the shell** go through the `@client` alias, defined in `vite.config.ts`,
  `vitest.config.ts` and `tsconfig.json`.

## What this canvas declares

**It draws nothing itself.** This was the reference migration for `declarative-diagram-modules`:
the module had a `CustomShapeRef` that wrapped the shared `SpanElement` to add three classes and
an edge rule, and both of those are declarations now. What is left in `DependencyGraphCanvas.tsx`
is a definition, a fold from the stream into the library's model, and event handlers — which is
the shape every other module is migrating toward.

The element type `node` declares:

- **`shape: "span"`** — the same shared component the custom renderer wrapped.
- **`classNames`** — `dependency-graph-element canvas-element` and `dependency-graph-node
  canvas-node` always; `dependency-graph-selected` and `dependency-graph-connect-target
  canvas-connect-target` conditioned on the canvas's own state. Those two conditions are why the
  library can see `state.selected` at all: twenty-three of the twenty-eight element types in the
  tree style on interaction state, and before this specification a declaration could not.
- **`labels`** — one line, truncated to the box, editable through the shared inline editor.
- **`anchors`** — the two named side anchors a dependency gesture lifts from, plus
  `edgeSides: "horizontal"`, which is what the custom shape's `edgePoint` did: a target end
  attaches on the side facing its source rather than by true edge intersection.

**What is still code, and why.** The `depends` relation keeps a `CustomRouteRef`: the forward
loop it draws when a dependency sits *behind* its dependant is decided from the endpoint bounds,
and custom routes are admitted first-class by the design rather than treated as an escape hatch.
The event handlers stay imperative, which is the carve-out the specification permits — a
declaration says what exists, not what happens when it fires.

**One thing the module's own tests do not cover**, found by sabotage rather than by reading:
removing `edgeSides` leaves all forty-five of them green. The custom route takes the endpoint
*bounds* and picks its own anchors, so the attachment point never reaches the drawn line here.
The guard for that behaviour is therefore in the library
(`DiagramCanvas.declared.test.tsx`), where it stands for the two modules that have no such
shelter — `dotnet-dependency-graph` and `timeline`, which migrate next.

See [../../readme.md](../../readme.md) for what this folder is for, and
[`declarative-diagram-modules`](../../../../.spec-workflow/specs/declarative-diagram-modules/) for
the specification this canvas is the reference for.
