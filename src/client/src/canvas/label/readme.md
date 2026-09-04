# Inline label editor

A textbox drawn in place of a label, on the canvas, at the label's own position — so renaming
something means changing the text you are looking at rather than filling in a form about it.

There is **one** implementation, here, and importing it from `@client/canvas/label` is the only
permitted way for a canvas to have in-place editing. `noPrivateLabelEditors.test.ts` enforces
that, for the same reason `noPrivateScrollbars.test.ts` enforces its rule: the drag-and-drop
survey measured what a convention without a guard costs, and it was the same pan and drag state
hand-rolled across ten canvases, each copied from whichever came before.

- `InlineLabelEditor.tsx` — the component. It takes a `LabelPlacement` (a rectangle in canvas
  units plus the text it replaces) and the propose/submit/cancel callbacks the dialog host
  already speaks, and reports intent. It does not know what a rename is and cannot decide what
  is renameable.
- `inlineLabelEditor.css` — the look, on the theme variables.

## Why it is mounted in canvas units

It renders as a `foreignObject` inside the canvas's own `svg`, at the label's rectangle in the
`viewBox` coordinate system. Panning and zooming therefore carry it with its element the way
they carry everything else drawn there, with no code that follows anything. An editor positioned
in screen pixels would have to track every view change and would drift on the ones it missed.

## The rules that are easy to reverse

Four behaviours look like preferences and are not:

- **Blur commits**, it does not discard. Losing typed text to a stray click is the failure users
  resent most. This is the one most likely to be "fixed" backwards by a later reader.
- **A refusal keeps the editor open with the text intact**, so a value can be adjusted rather
  than retyped from a label that is no longer visible.
- **An unchanged value dispatches nothing.** Opening an editor and pressing Enter is not an edit,
  and a command there would put an inverse on the undo stack that undoes nothing.
- **Enter commits whatever is typed**, unlike the dialog's confirm button, which stays disabled
  until a verdict has judged the exact text in the box. An inline editor has no button to
  disable, and a keystroke that silently does nothing — which is what waiting looks like inside
  the 200 ms validation debounce — is worse than a refusal the user can read. Nothing is
  bypassed: the handler validates its own preconditions, and the refusal comes back through the
  same submission result the dialog reads.

## Which labels get one

Not this component's decision, and deliberately not the client's at all. The backend marks a
prompt whose value *is* the visible label of a named element (`InlineLabelEdit` on
`InputDialogPrompt`), the shell asks the placement registry whether any mounted canvas can place
that element, and only then is an editor drawn. An unmarked prompt, or one no canvas can place,
renders the dialog exactly as it always did.

Two kinds of label are deliberately **not** markable, and a module author deciding what to mark
should recognise them:

- **A derived label.** An RDF edge shows a predicate's prefixed name computed from an IRI.
  Changing it renames that term across the whole document, with collision and prefix rules whose
  refusals need more room than a textbox has. That is a different act from retyping a caption,
  and it keeps its dialog.
- **A projection of several values.** Type badges, a row rendered from a predicate and its
  object, a computed summary: there is no single stored value for the typed text to become.

A label that *decorates* one authored value is a different case and is markable - C4 draws a
relationship as `description [technology]`, and the editor replaces that whole string on screen
while editing the description alone.

## Adoption

- **mindmap** - node labels. Its provider marks `Rename` and leaves add-child, add-sibling and
  edit-notes on the dialog, which is the sharpest example in the tree of where the line falls.
- **c4** - element names and relationship labels. The element's editor covers its name line
  rather than its box, and a relationship's opens on the description alone, without the
  technology drawn beside it. Relationships became selectable to make that reachable: they had
  no click or context-menu handler at all, so the backend offered relabel on them and no gesture
  could invoke it.

Every canvas without an entry above keeps its dialog **pending adoption**, which is not the same
as being exempt:
a module adopts by setting the marker on the prompts whose value is the label, and its canvas by
registering a placement resolver. Nothing else is needed and no other canvas has to change.
