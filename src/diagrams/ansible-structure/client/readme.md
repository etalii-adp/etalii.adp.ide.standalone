# Ansible structure — client

The canvas for `ansible/structure`, and the registration the shell discovers. It edits no
Ansible file; the one thing a user authors here is where a node sits.

| File | What it is |
|---|---|
| `register.ts` | The shell's glob finds this and nothing else needs editing to add the type. |
| `AnsibleCanvas.tsx` | Compiles the canvas definition from the bundled `definition/ansible-structure.dis`, builds the model it draws, and reveals a node's file - the handler that stands in for gap 9 of `definition/ansible-structure.md`. |
| `ansibleBindings.ts` | What the library cannot read from the specification: the CEL the backend composes into the payload, the classes, the forward Bezier route, the play colour's class prefix, Space beside Enter, the role and the tab order. |
| `ansible-structure.css` | The appearance the specification's notation does not paint itself: the per-kind outlines, the per-play palette, the edges' colours and weights. The edges' dashes are the specification's, painted on each line by the library. |
| `handWrittenDefinition.ts` | Test only: the definition the canvas stated by hand until it was compiled, kept as the oracle `ansibleCompiledDefinition.test.tsx` holds the compiled drawing to. |
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
change into a protocol change. The number of slots is the specification's (`self.playIndex % 6` in
the `Play` style), which the library reads into the class `ansible-play-<slot>`; the stylesheet
paints each slot, and `ansibleCompiledDefinition.test.tsx` holds its hues and its mix to the
specification's tokens. A slot is added in the specification first, then in the stylesheet.

## Inline renaming

**Exempt, not pending: there is no rename to mark.** This module's context actions offer no
rename at all - the structure it draws comes from the inventory's own files, and changing a
host's or group's name is an edit to those files, not to a label. A module with no prompt whose
value is the drawn text has nothing this feature could attach to. If a rename action is ever
added, it adopts the ordinary way: mark the prompt with the element id, register a placement
resolver over the shared helpers.
