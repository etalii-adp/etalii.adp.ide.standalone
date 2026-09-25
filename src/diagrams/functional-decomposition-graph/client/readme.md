# Functional decomposition graph — client

The web client's functional decomposition graph view: the ids it shares with its backend, the
delta fold, the canvas with its definition and handlers, its stylesheet, and the `register.ts`
the shell discovers it through.

This is a workspace package (`@adp/diagram-functional-decomposition-graph-client`). It lives
outside `src/client/`, so it declares its own dependencies and is installed by the workspace root
at `src/package.json`. Its payload types are generated from `../api/` by the client's `generate`
script.

- **`register.ts`** is the entry point as far as the shell is concerned, and it imports this
  module's stylesheet so the CSS arrives with the module.
- **`fdgIds.ts`** states every id the client and the backend agree on — element and relation
  types, the context actions, the properties and the shortcut — once. The backend's providers
  answer exactly these strings; a second spelling anywhere in this module is a defect.
- **`fdgModel.ts`** folds the delta stream: an add is an upsert keyed on id, and only a remove
  removes. The stream itself is the shared `useDiagramStream`, so there is no hook of this
  module's own.
- **Imports back into the shell** go through the `@client` alias.

## What this canvas declares

**It draws nothing itself.** `FdgCanvas.tsx` is a definition, a fold from the stream into the
library's model, and event handlers.

- **Five element types on the library's built-in shapes** — a UI Element is a superellipse, a
  Data Element a parallelogram, an Action a trapezoid, a Function a diode and a Comment a box.
  Each is sized by the reader; the Comment alone resizes in height too, wraps its text, and has
  no anchors. The fills are theme tokens, set in `fdg.css`.
- **Five relation types, exactly the rules table** — which sources each admits, its one target
  type, its limit (one parent for everything owned; one Shows out of an Action), and no
  self-links. The four ownership relations are declared `acyclic` together; Shows is not, so a
  navigation loop stays drawable.
- **Connecting is a right-button drag** from one element's body to another's
  (`connectOnRightDrag`), because edge anchors draw no handle to start from.

## How each gesture reaches the backend

Every library event takes the one route that can carry it (the user's chat ruling of
2026-09-25):

| Library event | Route | Id |
| --- | --- | --- |
| `element-moved` | the stream's `moveElementTo`, as the top-left | — |
| `element-resized` | `setProperty` on the element | `fdg.width`, `fdg.height` |
| `element-dropped` | context action on `new:x,y` (the drop's centre) | `fdg.add.<type>` |
| `connection-drawn` | context action on `rel:from->to` | `fdg.connect.<relation>` |
| delete, on an element or a connection | declared action | `fdg.remove`, `fdg.disconnect` |
| F2, or a double-click on an element | declared action, answered with an inline prompt | `fdg.rename`, `fdg.rename-connection` |

A label commit sends nothing of the module's own: the rename action opened the shared editor from
the backend's prompt, and the library hands the commit to that prompt.

**One edge is two edits.** Dragging a left or top edge moves the top-left as well as the size,
because the far edge stays put, and a property carries one value — so it is a size and then a
move, and undoes in two steps.

## What is not settled yet

The library picks a connect gesture's relation when the gesture starts, from the source element
alone. Every relation here is identified by its target type, so from an Action only one of Shows,
owns-data and owns-function can be drawn, and from a UI Element one of four.
`fdgConnect.test.tsx` fails its two Shows cases against that until it is resolved.

See [../../readme.md](../../readme.md) for what this folder is for, and
[`functional-decomposition-graph`](../../../../.spec-workflow/specs/functional-decomposition-graph/)
for the specification.
