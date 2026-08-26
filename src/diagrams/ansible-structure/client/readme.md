# Ansible structure — client

The read-only canvas for `ansible/structure`, and the registration the shell discovers.

| File | What it is |
|---|---|
| `register.ts` | The shell's glob finds this and nothing else needs editing to add the type. |
| `AnsibleCanvas.tsx` | Draws the nodes and the five edge kinds; selects, and reveals a node's file. |
| `ansible-structure.css` | **All** of the appearance, including the per-play palette. |
| `ansibleModel.ts` | Decodes the payload and folds the delta stream into a plain model. |
| `useAnsibleStream.ts` | Opens the diagram, re-baselines on reconnect, reports the viewport. |

## Three absences that are the design

This is a read-only diagram type, and the client half says so by what it does not have. Each is
covered by a test, so none of them can quietly come back:

- **No `moveElement` on the hook.** The backend refuses a move with a sentence, so exposing the
  call would put a gesture in the client's reach whose only possible outcome is a refusal.
- **No group or ungroup handling in `applyDelta`.** Nothing here folds, so the backend never
  emits those deltas and the model has no fold state to hold.
- **No editing affordance on the canvas** — nothing draggable, no drop target, no editable
  field, no toolbox. `AnsibleCanvas.test.tsx` asserts all four.

## The palette rule

The backend sends a play **index**; `ansible-structure.css` decides what an index looks like.
Keep it that way: a colour on the wire would put half the design in the backend and turn a theme
change into a protocol change. If you add a palette slot to the stylesheet, bump `PALETTE_SLOTS`
in `AnsibleCanvas.tsx` to match — that constant is the only thing the component knows about the
palette, and it knows only how many there are.
