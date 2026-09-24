# Ansible structure — client

The canvas for `ansible/structure`, and the registration the shell discovers. It edits no
Ansible file; the one thing a user authors here is where a node sits.

| File | What it is |
|---|---|
| `register.ts` | The shell's glob finds this and nothing else needs editing to add the type. |
| `AnsibleCanvas.tsx` | Draws the nodes and the five edge kinds; selects, and reveals a node's file. |
| `ansible-structure.css` | **All** of the appearance, including the per-play palette. |
| `ansibleModel.ts` | Decodes the payload and folds the delta stream into a plain model. |
| `useAnsibleStream.ts` | Opens the diagram, re-baselines on reconnect, reports the viewport, and moves a node. |

## What the client deliberately does not have

The diagram edits no Ansible file, and the client half says so by what it does not have. Each
absence is covered by a test, so none of them can quietly come back:

- **One write on the hook, `moveElementTo`, and no other.** The layout is the only thing a user
  authors here; ansible-refinements gave the backend a position to store, so a drag now has an
  answer other than a refusal. `useAnsibleStream.test.ts` pins that there is no second write.
- **No group or ungroup handling in `applyDelta`.** Nothing here folds, so the backend never
  emits those deltas and the model has no fold state to hold.
- **No content editing on the canvas** — nodes reposition, but there is no HTML `draggable`
  element, no editable field and no toolbox. `AnsibleCanvas.test.tsx` asserts those three.

## The palette rule

The backend sends a play **index**; `ansible-structure.css` decides what an index looks like.
Keep it that way: a colour on the wire would put half the design in the backend and turn a theme
change into a protocol change. If you add a palette slot to the stylesheet, bump `PALETTE_SLOTS`
in `AnsibleCanvas.tsx` to match — that constant is the only thing the component knows about the
palette, and it knows only how many there are.

## Inline renaming

**Exempt, not pending: there is no rename to mark.** This module's context actions offer no
rename at all - the structure it draws comes from the inventory's own files, and changing a
host's or group's name is an edit to those files, not to a label. A module with no prompt whose
value is the drawn text has nothing this feature could attach to. If a rename action is ever
added, it adopts the ordinary way: mark the prompt with the element id, register a placement
resolver over the shared helpers.
