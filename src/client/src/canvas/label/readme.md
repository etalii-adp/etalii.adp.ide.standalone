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
- `labelPlacement.ts` — three pure functions answering *where the editor goes*, for the three
  shapes a canvas draws: `centredLabelPlacement` for a label filling its element's box,
  `insetLabelPlacement` for one named line inside a composite box, and `midpointLabelPlacement`
  for a bare text on a connection. No React, no DOM, no module vocabulary. They were C4's
  private helpers first and were extracted rather than written, so the fourth canvas to adopt
  does not copy them a fourth time.

  **A connection label's width is the caller's measured width where it has one**, because such a
  label has no box — it is a text node whose width is whatever the font made it. Measuring needs
  the DOM, which does not belong in a pure function, so the caller measures and passes it.
  **jsdom implements no `getBBox`, so every unit test takes the estimate branch by
  construction**; the measured branch cannot be unit-tested and is covered by the manual check
  in `tests.md`. A green suite is not evidence that measuring works.

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
- **timeline** - element labels and connection labels. A span's editor covers its box; an
  instant's sits beside its diamond, which is why `asideLabelPlacement` exists.
- **dependency-graph** - node labels and relation labels. Every node is a span, so centred
  placement is the whole answer here.
- **databricks** - task keys and the bundle name, across all three readings, since `JobCanvas`,
  `BundleCanvas` and `PipelineCanvas` are wrappers around one canvas. A task's key **is** its
  drawn label, and the bundle element is packed with `Key = bundle.Name`, so what looks like a
  two-value fallback (`payload.key || payload.kind`) renders the very value being renamed - the
  `|| kind` is an empty-state placeholder, not a second value. Deployment targets are frames and
  are not renamed. **Its edges are pending, not exempt**: they select since
  [centralized-selection](../../../../../.spec-workflow/specs/centralized-selection/requirements.md)
  (Requirement 2.1), so a relabelled edge can now be reached by a gesture, and the relabel prompt
  qualifies and should be marked.
- **azure-pipeline** - display names, on stages, jobs, steps and templates. A stage's editor
  covers its name line above the job count; a single-line box's covers it whole. The drawn
  label falls back from the display name to the element's own identifying name, which is a
  placeholder for an empty authored value rather than a second value - and clearing the
  display name inline is a real instruction, per the provider's own remark. Adoption also gave
  this canvas its first keyboard path: F2 is forwarded, and the provider's other declared
  shortcuts (Space, Delete, Alt+Up, Alt+Down) remain unreachable - pending, not exempt.
- **wardley-map** - component names. Adoption came with the canvas's whole context channel:
  it had no selection, menu or keyboard at all, so every wardley action was unreachable until
  the selectability task landed. The editor honours the document's own label offset, left of
  the mark included, through `asideLabelPlacement`'s signed gap. Evolve is deliberately not
  marked: it asks for a maturity number, which is emphatically not a label.
- **c4** - element names and relationship labels. The element's editor covers its name line
  rather than its box, and a relationship's opens on the description alone, without the
  technology drawn beside it. Relationships became selectable to make that reachable: they had
  no click or context-menu handler at all, so the backend offered relabel on them and no gesture
  could invoke it.

Every implemented module now has either an entry above or a recorded reason in its own
client readme. The reasons divide in two, and the difference matters: **exempt** means the
module has nothing this feature could attach to - the four RDF readings, whose drawn labels
are prefixed names computed from IRIs, and ansible-structure, helm-charts and sparql, which
offer no rename at all - while **pending** means a gesture is missing, not a reason:
databricks' edges await edge selection. A module that later gains a rename adopts as ever:
a module adopts by setting the marker on the prompts whose value is the label, and its canvas by
registering a placement resolver. Nothing else is needed and no other canvas has to change.
