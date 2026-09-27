# Gartner hype cycle graph — client

The web client's hype cycle graph view: the ids and the time scale it shares with its backend, the delta fold, the canvas with its definition and handlers, its stylesheet, and the `register.ts` the shell discovers it through.

This is a workspace package (`@adp/diagram-gartner-hypecycle-graph-client`). It lives outside `src/client/`, so it declares its own dependencies and is installed by the workspace root at `src/package.json`. Its payload types are generated from `../api/` by the client's `generate` script.

- **`register.ts`** claims the mime type `gartner/hypecycle-graph` and imports this module's stylesheet.
- **`ghgIds.ts`** states every id the client and the backend agree on — the element and relation types, the actions, the properties the canvas sets and the shortcut — and the time scale: four canvas units a month from 1900-01, rows 56 apart, a trend 32 tall. A document may name a coarser time unit in its `unit:` key - `year`, `decade` or `century` - and every trend's payload carries it; a step of that unit is then four units wide, and the dates stay months. `../scale-fixture.json` states the same numbers, the units included, and both tiers are tested against it.
- **`ghgModel.ts`** folds the delta stream: an add is an upsert keyed on id, and only a remove removes.
- **`ghgExample.ts`** reads an example (technology-trends unless another is named) for the tests; nothing in the canvas imports it.

## What this canvas declares

**It draws nothing itself.** `GhgCanvas.tsx` is a definition, a fold from the stream into the library's model, and event handlers. Every capability below is the shared canvas library's.

- **One element type, `trend`**, on the library's `arrow-banner` shape, sized by the reader in width. Its `segments` are bound to the payload: the number of phases it has reached and the fractions its inner boundaries sit at. Each phase has its own class (`ghg-peak` to `ghg-plateau`) and its full Gartner name as a tooltip; chevrons divide them, and their boundaries can be dragged.
- **The name sits before the banner**, right-aligned, and is renamed in the shared inline editor.
- **Influences attach anywhere along a phase's top or bottom edge** (`along` anchors, no dots drawn). One influence is allowed each way between two trends, never to itself, and an influence attached to a phase its trend does not show is hidden but still counts.
- **Snapping** is to whole steps of the diagram's unit horizontally - months unless the document names another - and to rows vertically. A **bottom ruler** is declared in months, stepping to quarters, years, decades, centuries and millennia as the view widens, and never to a rung finer than one step. `ghgDefinitionFor(unit)` gives the definition for each unit; they differ only in the ruler. A **filter box** takes tags as chips, looked up among the diagram's own as they are typed, and hides the trends having none of them - or, switched from Any to All, not every one of them.

## How each gesture reaches the backend

Every library event takes the one route that can carry it:

- **A move** goes through the stream's `moveElementTo`, as the top-left: the backend snaps it to the start of a step of the diagram's unit and to a row.
- **A resize** is `setProperty` on `ghg.start` or `ghg.stop`, the dragged edge's month as `YYYY-MM`.
- **A dragged boundary** is `setProperty` on `ghg.peak-end`, `ghg.trough-end` or `ghg.slope-end`, the month that phase now ends.
- **A drawn influence** is the action `ghg.connect.influence` on `rel:{from}@{phase}/{edge}/{at}->{to}@{phase}/{edge}/{at}`, so both ends and where each attaches travel in one undoable step.
- **A drop** is `ghg.add.trend` on `new:x,y`; rename, delete and disconnect are declared actions the library dispatches.

## Tests

- `ghgDefinition.test.ts` — the definition in every unit, and the client's month-to-x against the scale fixture, through the module and through the ruler.
- `ghgErasExample.test.tsx` — the eras-of-innovation example, drawn in decades: every trend and influence reaches the canvas.
- `ghgHandlers.test.tsx` — each library event produces its one route and nothing else.
- `ghgConnect.test.tsx` — over the example: A to B is offered, a second A to B is not, B to A is, a self-influence is not, a hidden influence still blocks its duplicate, and a vertical drag lands on a row.
- `register.test.ts` — the registration claims this mime type and nothing else.
