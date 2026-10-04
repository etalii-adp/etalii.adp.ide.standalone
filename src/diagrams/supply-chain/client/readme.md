# Supply chain diagram — client

The web client's supply chain view: the ids it shares with its backend, the delta fold, the canvas with its definition and handlers, its stylesheet, and the `register.ts` the shell discovers it through.

This is a workspace package (`@adp/diagram-supply-chain-client`). It lives outside `src/client/`, so it declares its own dependencies and is installed by the workspace root at `src/package.json`. Its payload types are generated from `../api/` by the client's `generate` script.

| File | What it is |
| --- | --- |
| `register.ts` | The entry point the shell finds; it imports `supply-chain.css` so the look arrives with the module. |
| `supplyChainIds.ts` | Every id the client and the backend agree on - stages, types, actions, properties - stated once. |
| `supplyChainModel.ts` | Folds the delta stream into groups, nodes and flows, and formats an amount for a card or a band. |
| `SupplyChainCanvas.tsx` | The definition, the fold into the library's model, the stepper elements, and the handlers. |
| `supply-chain.css` | **All** of the appearance: the stage palette in both themes, the bands, the goods animation and the trace. |

## What the canvas draws

- **A card per node**, a rounded rectangle whose header carries the stage's colour and title, with the name (editable in place), the quantity and its unit, and a bar for the node's share of what it ships.
- **A frame per group**, drawn beneath the flows so a band crosses it rather than hiding behind it. A group with no members yet, such as one just dropped from the toolbox, is drawn one card's frame in size where the document places it.
- **A band per flow**, routed as a horizontal S between the facing sides of its two cards. Its width follows the flow's weight - the backend's volume relative to the heaviest flow in the same unit - and dashes move along it in the direction the goods go, unless the user prefers reduced motion. A pill in the middle shows the volume.

## The trace and the steppers

**Selecting is the library's, and the trace is the backend's.** A module may not read the selection, so the backend learns of it through its resolver, works out everything upstream and downstream, and sends each element's trace word on its payload. The canvas only turns that word into a class: orange upstream, blue downstream, everything unrelated dimmed.

**The + and - are elements the canvas derives**, not ones the backend sends: beside the one node or flow whose trace is `selected`. Pressing one fires the `press` gesture (see `docs/diagram-module-client-api.md`), which the definition maps to `supply-chain.increase` or `supply-chain.decrease` on the owner, so a press steps the value without moving the selection. The keyboard does the same with `+` and `-`. A decrease never goes below zero; at zero the - is drawn disabled.

## How each gesture reaches the backend

| Library event | Route | Id |
| --- | --- | --- |
| `element-moved` | the stream's `moveElementTo`, as the top-left; a group moves its members, and a node dropped inside another group's frame joins it | — |
| `element-dropped` | context action on `new:x,y` | `supply-chain.add.<stage>`, or `supply-chain.add-group` for the toolbox's Group |
| `connection-drawn` (right-button drag) | context action on `rel:from->to` | `supply-chain.connect` |
| press on + or -, or `+` / `-` | declared action on the owner | `supply-chain.increase`, `supply-chain.decrease` |
| F2, or a double-click | declared action, answered with an inline prompt | `supply-chain.rename` |
| delete | declared action | `supply-chain.remove` |

## Inline renaming

A node's or group's name and a flow's product are renamed in place: `supply-chain.rename` answers with a prompt whose value is the drawn label, and the library opens the shared editor on it.
