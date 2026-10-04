# Agent Behavior Modelling — client

The web client's Agent Behavior Modelling view: the ids it shares with its backend, the delta fold, the canvas with its definition and handlers, its stylesheet, and the `register.ts` the shell discovers it through.

This is a workspace package (`@adp/diagram-agent-behavior-modelling-client`). It lives outside `src/client/`, so it declares its own dependencies and is installed by the workspace root at `src/package.json`. Its payload types are generated from `../api/` by the client's `generate` script.

- **`register.ts`** is the entry point as far as the shell is concerned, and it imports this module's stylesheet so the CSS arrives with the module.
- **`abmIds.ts`** states every id the client and the backend agree on - the eleven node kinds, the parent relation, the context actions, the properties and the shortcuts - once. The backend's providers answer exactly these strings.
- **`abmModel.ts`** folds the delta stream: an add is an upsert keyed on id, and only a remove removes. The stream itself is the shared `useDiagramStream`.
- **`abmDrag.ts`** says what a drag means before the backend answers: the node's new place among its siblings, how far its row moves down, and where every node in that row and beneath it is drawn. It mirrors the backend's `AbmArrangement` and `AbmLayout.Arrange`.

## What this canvas declares

**It draws nothing itself.** `AbmCanvas.tsx` is a definition, a fold from the stream into the library's model, and event handlers.

- **Eleven node kinds on the library's built-in shapes.** The three composites are superellipses and the four wrappers hexagons, as behavior tree editors in games set the two families apart; a Check is a pill, a Do a box, an Ask the user a parallelogram and a Delegate a diode. Each node shows its keyword above its label, and only the label is edited in place: the kind changes through the property grid, where the backend refuses a kind that cannot hold the node's children.
- **One relation, parent to child**, orthogonal with an arrow, declared `acyclic`, and starting only from a kind that holds children.
- **The layout is the backend's.** The tree is laid out top-down from the Markdown. Across, a node's place is its order among its siblings; down, all children of one parent share one row, whose height a drag keeps in the `.adp` `layout:` block.
- **A drag carries the subtree.** Everything beneath a dragged node follows the pointer, its siblings follow it up and down, and the siblings it passes step aside to show where it will land, as the Sankey diagram does in a column.
- **Connecting is a right-button drag** from the parent's body to the child's (`connectOnRightDrag`), because edge anchors draw no handle.

## How each gesture reaches the backend

- A drag goes through the stream's `moveElementTo`, with the centre the library reports turned into the top-left the backend takes. The backend's `ArrangeAbmNodeCommand` moves the node before the first sibling whose middle is right of the drop's, rewriting the order in the Markdown, and stores the row's new height for every node in it and beneath it, renaming the stored ids that follow the reordered nodes. Both files come back with one undo. A row never goes closer to its parent than 16 units.
- A drawn parent line runs `abm.connect.child` on a `rel:parent->child` gesture id: the child, with everything under it, moves under the parent.
- A toolbox drop runs `abm.add.<kind>` on a `new:x,y` gesture id: the node goes under the nearest node above the drop that can take another child, or becomes the tree's root.
- Rename, remove, and moving a node earlier or later among its siblings (Alt+Up, Alt+Down) are declared actions forwarded on the node's id.
