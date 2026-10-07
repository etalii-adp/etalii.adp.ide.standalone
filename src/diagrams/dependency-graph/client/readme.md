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

**It draws nothing itself, and it no longer states its definition by hand.** This was the reference
migration for `declarative-diagram-modules`: the module had a `CustomShapeRef` that wrapped the
shared `SpanElement` to add three classes and an edge rule, and both of those became declarations.
Those declarations are now compiled from the bundled DISL specification,
`../definition/dependency-graph.dis`, by the library's `compileNotation`, as the .NET dependency
graph's are. What is left in `DependencyGraphCanvas.tsx` is that one compile, a fold from the stream
into the library's model, and event handlers.

The element type `node` it compiles to declares:

- **`shape: "span"`** — DISL's `roundedRect`, drawn as the shared span (`builtInShapes`).
- **`classNames`** — `dependency-graph-element canvas-element` and `dependency-graph-node
  canvas-node`, both unconditional. The selected and connect-target looks are the library's own
  (since 528f1434).
- **`labels`** — one line, the label or else the id, truncated to the box, editable through the
  shared inline editor.
- **`anchors`** — the specification's two fixed anchors, `left` and `right`, as named side fractions,
  plus `edgeSides: "horizontal"`, which is what the custom shape's `edgePoint` did: a target end
  attaches on the side facing its source rather than by true edge intersection.

The `dependson` relation starts from either named anchor (the edge's `connect.from`), draws its label
6 above the middle of the line, and raises a release on empty canvas, because its tool creates the
missing end there (`createTarget` and `createSource`). The actions are the specification's context
menus: F2, Delete, Tab and Enter on a node, F2 and Delete on a dependency, and a double-click renames.
Empty canvas has a menu too, "Add node here", whose click lands on the nearest row.

**What is still code, and why** — `dependencyGraphBindings.ts`, each entry with its reason: the CEL
label's payload path, the class names, the span, the dependency curve, a target end's facing-side
rule, where a background right-click lands, and the key each action is sent to the backend as. The
curve keeps a `CustomRouteRef`: the forward loop it draws when a dependency sits *behind* its
dependant is decided from the endpoint bounds. The event handlers stay imperative, which is the
carve-out the specification permits — a declaration says what exists, not what happens when it fires.

**Held to what it replaced.** `dependencyGraphCompiledDefinition.test.tsx` keeps the hand-written
definition as the oracle: the compiled definition must draw the same markup and, but for the
differences it names, declare the same thing.

**One thing the module's own tests do not cover**, found by sabotage rather than by reading:
removing `edgeSides` leaves the canvas tests green. The custom route takes the endpoint *bounds* and
picks its own anchors, so the attachment point never reaches the drawn line here. The guard for that
behaviour is therefore in the library (`DiagramCanvas.declared.test.tsx`), and the oracle test holds
the compiled declaration to the hand-written one.

See [../../readme.md](../../readme.md) for what this folder is for, and
[`declarative-diagram-modules`](../../../../.spec-workflow/archive/specs/declarative-diagram-modules/) for
the specification this canvas is the reference for.
