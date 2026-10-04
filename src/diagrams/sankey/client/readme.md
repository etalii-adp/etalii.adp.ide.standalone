# Sankey diagram — client

The web client's Sankey view: the ids it shares with its backend, the delta fold, the canvas with its definition and handlers, its stylesheet, and the `register.ts` the shell discovers it through.

This is a workspace package (`@adp/diagram-sankey-client`). It lives outside `src/client/`, so it declares its own dependencies and is installed by the workspace root at `src/package.json`. Its payload types are generated from `../api/` by the client's `generate` script.

| File | What it is |
| --- | --- |
| `register.ts` | The entry point the shell finds; it imports `sankey.css` so the look arrives with the module. |
| `sankeyIds.ts` | Every id the client and the backend agree on - types, palette words, actions, properties - stated once. |
| `sankeyModel.ts` | Folds the delta stream into nodes and flows, and works out where a dragged node lands in its column. |
| `SankeyCanvas.tsx` | The definition, the band geometry, the fold into the library's model, and the handlers. |
| `sankey.css` | The band's translucency, the hidden centre line and the label halo. The colours are theme tokens, painted inline. |

## What the canvas draws

- **A bar per node**, as tall as the larger of what flows into it and out of it, in its palette colour or its own `#rrggbb`. Its name, its value in the document's format and an optional note sit before it in the first column and after it everywhere else, the name and value in the bar's colour.
- **A band per flow**, filled between two parallel horizontal S-curves, as thick as its value at every x - not a stroked line, which would narrow on the slope. Bands leave a bar's right edge and reach the next bar's left edge where the layout stacked them, so they meet the bar edge to edge. The library's own line is the band's centre, kept for hit-testing and hidden.

**Everything about where things are is the backend's.** The layout - columns from the flows, heights from the values, the order of a column from the document, the relaxation that levels the bands - runs in `SankeyLayout.cs`; the canvas draws what arrives.

## Reordering a column

A node moves up and down its column and never out of it: the definition snaps x to the node's own left edge. While it is dragged, `columnPlaceOf` works out which neighbours it has passed, and those step aside by its height and the gap, so the reader sees where it will land. On release the backend moves the node's entry before or after the neighbour it landed beside (`SankeySession.PlaceOf`, the same rule), and until the new layout arrives the node is drawn in its slot.

## How each gesture reaches the backend

| Library event | Route | Id |
| --- | --- | --- |
| `element-moved` | the stream's `moveElementTo`, as the top-left; only the order in the column is kept | — |
| `element-dropped` | context action on `new:x,y` | `sankey.add` |
| `connection-drawn` (right-button drag) | context action on `rel:from->to` | `sankey.connect` |
| `+` / `-` on a selected flow | declared action | `sankey.increase`, `sankey.decrease` |
| F2, or a double-click on a node | declared action, answered with a prompt | `sankey.rename` |
| delete | declared action | `sankey.remove` |
| the context menu | backend-discovered | `sankey.thicker`, `sankey.thinner` |

Colours, notes, value formats, columns, values and steps are set in the property grid.
