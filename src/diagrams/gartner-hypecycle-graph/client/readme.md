# Gartner hype cycle graph — client

The web client's hype cycle graph view: the ids and the time scale it shares with its backend, the delta fold, the canvas with its definition and handlers, its stylesheet, and the `register.ts` the shell discovers it through.

This is a workspace package (`@adp/diagram-gartner-hypecycle-graph-client`). It lives outside `src/client/`, so it declares its own dependencies and is installed by the workspace root at `src/package.json`. Its payload types are generated from `../api/` by the client's `generate` script.

- **`register.ts`** claims the mime type `gartner/hypecycle-graph` and imports this module's stylesheet.
- **`ghgIds.ts`** states every id the client and the backend agree on — the element and relation types, the actions, the properties the canvas sets and the shortcut — and the time scale: four canvas units a month from 1900-01, rows 56 apart, a trend 32 tall. A document may name a coarser time unit in its `unit:` key - `year`, `decade` or `century` - and every trend's payload carries it; a step of that unit is then four units wide, and the dates stay months. `../scale-fixture.json` states the same numbers, the units included, and both tiers are tested against it.
- **`ghgModel.ts`** folds the delta stream into trends, triggers, notes and influences: an add is an upsert keyed on id, and only a remove removes.
- **`ghgExample.ts`** reads an example (technology-trends unless another is named) for the tests; nothing in the canvas imports it.

## What this canvas declares

**It draws nothing itself.** `GhgCanvas.tsx` is a definition, a fold from the stream into the library's model, and event handlers. Every capability below is the shared canvas library's.

- **One element type, `trend`**, on the library's `arrow-banner` shape, sized by the reader in width. Its `segments` are bound to the payload: the number of phases it has reached and the fractions its inner boundaries sit at. Each phase has its own class (`ghg-peak` to `ghg-plateau`) and its full Gartner name as a tooltip; chevrons divide them, and their boundaries can be dragged.
- **The name sits before the banner**, right-aligned, and is renamed in the shared inline editor.
- **Influences attach anywhere along a phase's top or bottom edge** (`along` anchors, no dots drawn). One influence is allowed each way between two trends, never to itself, and an influence attached to a phase its trend does not show is hidden but still counts.
- **A `trigger`** is a moment that set trends off - an invention, a political moment, a disaster: a circle half a trend's height across, its centre at the start of its month and on its row's middle. Its name and its date, in the diagram's unit, are drawn before it as a trend's name is, and only the name is edited in place; hovering says *Trigger: name, date*. It is never resized. It offers three handles to start an influence from, and the line leaves its outline facing the target (`attachDrawnBy: "edge"`), so the document stores nothing for that end. An influence may start at a trigger and never end at one.
- **A `note`** is the author's own remark: a box of word-wrapped text, edited in place across several lines, moved and resized both ways, with no anchors, so it takes part in no relation. A note dropped from the toolbox opens its editor (`editOnDrop`).
- **Snapping** is to whole steps of the diagram's unit horizontally - months unless the document names another - and to rows vertically. Each element binds its own snap origins from its payload: a trend's and a note's are 0, and a trigger's put its centre, not its edge, on a step line and a row's middle. A **bottom ruler** is declared in months, stepping to quarters, years, decades, centuries and millennia as the view widens, and never to a rung finer than one step. `ghgDefinitionFor(unit)` gives the definition for each unit; they differ only in the ruler. A **filter box** takes tags as chips, looked up among the diagram's own as they are typed, and hides the trends and triggers having none of them - or, switched from Any to All, not every one of them. Notes carry no tags and stay visible under every filter (the filter's `elementTypes`).
- **Compact** packs trends at one width, and triggers and notes at their own size, in time order along the rows each covers (`rowPacked.types` and `rowStep`); nothing that would change a date is offered while it is on, and influences can still be drawn from a trigger.

## How each gesture reaches the backend

Every library event takes the one route that can carry it:

- **A move** goes through the stream's `moveElementTo`, as the top-left - each element's own half-size taken off the centre the library reports: the backend snaps it to the start of a step of the diagram's unit and to a row, a trigger by its centre.
- **A resize** is `setProperty` on `ghg.start` or `ghg.stop`, the dragged edge's month as `YYYY-MM`; a note's is `ghg.size`, as `width x height at YYYY-MM row N`.
- **A dragged boundary** is `setProperty` on `ghg.peak-end`, `ghg.trough-end` or `ghg.slope-end`, the month that phase now ends.
- **A drawn influence** is the action `ghg.connect.influence` on `rel:{from}@{phase}/{edge}/{at}->{to}@{phase}/{edge}/{at}`, so both ends and where each attaches travel in one undoable step.
- **A drop** is `ghg.add.trend`, `ghg.add.trigger` or `ghg.add.note` on `new:x,y`; rename (a note's text included), delete and disconnect are declared actions the library dispatches.

## Tests

- `ghgDefinition.test.ts` — the definition in every unit, and the client's month-to-x against the scale fixture, through the module and through the ruler.
- `ghgErasExample.test.tsx` — the eras-of-innovation example, drawn in decades: every trend and influence reaches the canvas.
- `ghgHandlers.test.tsx` — each library event produces its one route and nothing else.
- `ghgTriggersAndNotes.test.tsx` — a trigger and a note as the real library draws them from this definition: the circle, its label and tooltip, a centre snapped to the lines, no resize, an influence from a handle and never into a trigger, the filter's scope, and the fold of both from deltas.
- `ghgCompact.test.tsx` — compact mode over the example, and triggers and notes placed among the trends at their own size.
- `ghgConnect.test.tsx` — over the example: A to B is offered, a second A to B is not, B to A is, a self-influence is not, a hidden influence still blocks its duplicate, and a vertical drag lands on a row.
- `register.test.ts` — the registration claims this mime type and nothing else.
