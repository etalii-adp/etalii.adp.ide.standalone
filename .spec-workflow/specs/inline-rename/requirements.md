# Requirements Document

## Introduction

Renaming a diagram element today opens a modal dialog: the backend answers an action with an input prompt, and `ContextPromptHost` puts a `Dialog` on screen with a field, a validation round trip and a confirm button. This specification adds an **inline editor** — a textbox drawn in place of the label, on the canvas, at the label's own position — and makes it the way renaming happens wherever a rename is really "change the text you are looking at".

The phrase to get right is the user's own: *wherever possible*. It is load-bearing, and this document draws the line rather than promising "always inline" and then meeting a case it cannot serve.

## What exists today

**The prompt contract is already the right shape, and the inline editor should satisfy it rather than replace it.** `ContextPromptHost` takes three callbacks: `onPropose(revision, value)` returning a `{ revision, valid, reason }` verdict, `onSubmit(value, text?)` returning `{ completed, error }`, and `onCancel()`. Validation is a debounced backend round trip (200 ms), and verdicts carry a revision so a reply for stale text is recognisable as stale. That is exactly what an inline editor needs — live refusal while typing, a commit that can fail, and an abandon — so the inline editor is a second **presentation** of an existing interaction, not a second path to the backend.

**An input prompt is not the same thing as a rename.** There are 53 sites answering with `ContextExecutionRequiresInput` across the modules; only eight modules have a rename-shaped action at all, and those same providers also ask for run-if values, task keys, cluster keys, prefix declarations, library paths and predicate IRIs through the identical prompt. Nothing in `InputDialogPrompt` — title, icon, field label, initial value, confirm label — distinguishes "the text you can see on this element" from "some other value about it".

**So the client cannot tell which prompts are inline-able, and must not guess.** Sniffing an action id for the word "rename" would put module vocabulary in shared code, which is exactly what the family rulings of 2026-09-03 and 2026-09-04 forbid: element ids, action ids and payload field names are the module's, and shared code never interprets them. Only the module knows whether the value it is asking for *is* the label being displayed. That makes a small contract addition unavoidable, and this document names it plainly rather than working around it.

**There is no groundwork.** The only inline-editing reference anywhere in the client tree is a single ansible canvas test. Every canvas already draws labels through the central library and already forwards `F2` as a structural shortcut — c4, databricks, dependency-graph, mindmap, rdf and timeline all do — so the reach exists; the editor does not.

## Requirements

### Requirement 1 — One reusable inline editor, in the central canvas library

**User Story:** As a developer, I want the inline editor written once, so that every canvas gets the same editing behaviour instead of six near-copies.

#### Acceptance Criteria

1. WHEN the inline editor is implemented THEN it SHALL live in the central canvas library beside the existing shared pieces, and no canvas SHALL carry its own copy.
2. WHEN a canvas adopts it THEN the canvas SHALL supply only what it alone knows — where the label is on screen, what it currently reads, and how big it is — and SHALL NOT reimplement the editing, validation or commit behaviour.
3. WHEN the editor is named THEN the name SHALL describe the mechanism and SHALL NOT contain a diagram type or a module name, per the naming standard the central library already keeps.
4. WHEN the editor needs to talk to the backend THEN it SHALL use the same propose/submit/cancel contract the dialog host uses, so that an interaction behaves identically whichever presentation renders it.
5. WHEN this specification is compared to the other centralization work in flight THEN it SHALL NOT invent variants for symmetry: an inline text editor has one real shape, and a folder of derivatives would be a costume rather than a design.

### Requirement 2 — The backend says which prompts may be inline, and the canvas says whether it can be

**User Story:** As a module author, I want to declare that a prompt edits the label a user is looking at, so that the client can render it in place without guessing from my action ids.

#### Acceptance Criteria

1. WHEN a module answers an action with an input prompt that edits the visible label of the element the action targets THEN it SHALL be able to say so as data on the prompt, and the contract SHALL carry that marker — a small addition, named here rather than avoided.
2. WHEN a prompt carries no such marker THEN it SHALL render as the dialog does today, unchanged, which is what the other 45 input sites keep doing.
3. WHEN a marked prompt arrives THEN the client SHALL render it inline **only if** the canvas can place it: the target is on the canvas, it is visible in the viewport, and its label has a known position and size.
4. IF a marked prompt arrives and the canvas cannot place it THEN the dialog SHALL be shown instead, and the interaction SHALL behave exactly as it does today — the fallback is silent and always available, never an error.
5. WHEN the marker is absent because a module has not adopted it THEN that module's renames SHALL keep using the dialog, so adoption is per module and incremental.

### Requirement 3 — Which labels accept an inline edit, and which do not

**User Story:** As a user, I want inline renaming where the label is mine to change, and a clear reason where it is not, so that the feature is predictable rather than surprising.

#### Acceptance Criteria

1. WHEN an element's label is an **authored** value — a name, a title, a key, a description the document stores as text — THEN it SHALL be inline-editable, subject to Requirement 2.
2. WHEN a **relation's** label is authored THEN it SHALL be inline-editable on the same terms as an element's: C4 requires a relationship to carry a description and already offers a relabel action bound to `F2`, so relations are in scope and are not a lesser case.
3. WHEN a label is **derived** rather than authored THEN it SHALL NOT be inline-editable, and the action that changes the underlying value SHALL keep its dialog. The RDF family is the worked example: an edge's label is a predicate's prefixed name computed from an IRI, and changing it is a rename of that term across the whole document — a different act from retyping a caption, with its own collision and prefix rules that the writer refuses on.
4. WHEN a label is a **projection of several values** — a card's type badges, a row rendered from a predicate and its object, a computed summary — THEN it SHALL NOT be inline-editable, because there is no single stored value for the typed text to become.
5. WHEN a label is not inline-editable THEN the reason SHALL be visible where the user asks for it: the existing action keeps its dialog, and nothing silently does nothing.
6. WHEN this specification is implemented THEN the set of inline-editable labels SHALL follow from these rules per module rather than from a list frozen here, since modules are being added weekly.

### Requirement 4 — Editing in place behaves like an editor, not like a dialog in a small box

**User Story:** As a user, I want the inline editor to behave the way in-place editing behaves everywhere else, so that I do not have to learn it.

#### Acceptance Criteria

1. WHEN the editor opens THEN it SHALL replace the label in place, carry the label's current text, and select all of it, so that typing replaces and a click places the caret.
2. WHEN the editor opens THEN it SHALL take keyboard focus, and SHALL return focus to the canvas when it closes.
3. WHEN the user presses Enter THEN the edit SHALL be committed; WHEN the user presses Escape THEN it SHALL be abandoned with the label unchanged and nothing dispatched.
4. WHEN the editor loses focus by a click elsewhere THEN the edit SHALL be committed rather than lost, because losing typed text to a stray click is the failure users resent most.
5. WHEN the value fails validation THEN the refusal SHALL be shown against the editor while it stays open, in the module's own words, exactly as the dialog shows it today — a rejected inline edit SHALL NOT close and discard the user's text.
6. WHEN the committed value is unchanged from the original THEN nothing SHALL be dispatched and no history entry SHALL be recorded.
7. WHEN the text is longer than the label it replaces THEN the editor SHALL remain usable and SHALL NOT resize the element behind it.

### Requirement 5 — Surviving the canvas moving underneath

**User Story:** As a user, I want an edit in progress to survive whatever else the canvas does, so that a stray scroll does not cost me my typing.

#### Acceptance Criteria

1. WHEN the canvas is panned or zoomed while the editor is open THEN the editor SHALL stay on its element, at the element's position and at the current zoom, and the text being typed SHALL be preserved.
2. WHEN the element being edited scrolls out of view THEN the edit SHALL NOT be silently discarded; the editor SHALL either follow the element or commit, and whichever is chosen SHALL be the same on every canvas.
3. WHEN a delta from the backend changes the element being edited THEN the user's typing SHALL NOT be overwritten mid-edit.
4. IF the element being edited disappears — removed by another change, or gone after a reload — THEN the edit SHALL be abandoned without dispatching, and the user SHALL be told rather than left typing into nothing.
5. WHEN a gesture that moves things begins — a drag, a marquee, a pan — THEN the open editor SHALL commit first, so that a gesture never lands on a canvas in an ambiguous editing state.

### Requirement 6 — One editor at a time, and how it meets a multi-selection

**User Story:** As a user with several things selected, I want renaming to do something sensible, so that the two features do not contradict each other.

#### Acceptance Criteria

1. WHEN the inline editor is open THEN exactly one SHALL be open on a canvas, and opening another SHALL commit the first.
2. WHEN a selection holds several members and a rename is invoked THEN the inline editor SHALL NOT open: there is no single label to replace, and typing one name into many elements is a different feature — a merged property edit through the grid, which the `multi-select` specification already covers.
3. WHEN a selection holds several members THEN the rename action SHALL either be withheld or keep its dialog, and whichever is chosen SHALL be stated once rather than per canvas.
4. WHEN an inline edit commits THEN the selection SHALL be unchanged by the edit itself.

### Requirement 7 — The dialog stays where it is still the right answer

**User Story:** As a user, I want the dialog when the dialog is genuinely better, so that "inline wherever possible" does not mean "inline even where it hurts".

#### Acceptance Criteria

1. WHEN a prompt asks for a value that is not the visible label THEN it SHALL keep its dialog — a run-if, a cluster key, a prefix declaration, a library path, a predicate IRI. These are the majority of the 53 input sites and are unaffected by this feature.
2. WHEN a prompt needs to show more than a field — a title explaining what is being asked, a candidate list, a confirmation of consequences — THEN it SHALL keep its dialog, because an inline box has room for text and a refusal and nothing else.
3. WHEN the target has no visible label to replace — an element off screen, a target selected from the explorer rather than the canvas, a scope with no canvas at all — THEN it SHALL keep its dialog (Requirement 2.4).
4. WHEN the dialog is used THEN it SHALL behave exactly as it does today: this specification adds a presentation and removes none.

### Requirement 8 — Reachable the ways renaming is already reachable

**User Story:** As a user, I want to start an inline rename the ways I already start a rename, so that nothing I know stops working.

#### Acceptance Criteria

1. WHEN a rename is invoked from the context menu THEN it SHALL open the inline editor where Requirement 2 allows it, and the dialog otherwise.
2. WHEN `F2` is pressed on a selected element THEN it SHALL do the same — six canvases already forward `F2` as a structural shortcut, and the backend already owns the key-to-action table, so no canvas gains its own rename key.
3. WHEN a rename is invoked from anywhere else that exists — the explorer, a ribbon — THEN it SHALL behave as it does today unless that surface can place an editor.
4. WHEN the editor is open THEN the keys that structurally act on a selection — Delete and its kin — SHALL be typed into the editor rather than acted on, since the user is typing text.

### Requirement 9 — Guarded

**User Story:** As a maintainer, I want the behaviours that are easy to get wrong pinned by tests, so that adoption by later canvases cannot erode them.

#### Acceptance Criteria

1. WHEN the editor is implemented THEN tests SHALL cover: commit on Enter, abandon on Escape, commit on blur, a refusal keeping the editor open with the text intact, an unchanged value dispatching nothing, and focus returning to the canvas.
2. WHEN the fallback is implemented THEN a test SHALL prove that an unmarked prompt still renders the dialog and that a marked prompt the canvas cannot place falls back to it.
3. WHEN a canvas adopts the editor THEN a test SHALL fail if it implements in-place editing privately instead of composing the shared one.
4. WHEN the feature ships THEN `tests.md` SHALL gain a manual check covering a rename inline, a refusal shown inline, and an edit surviving a pan — confirmed in a real browser, since focus, caret and blur are exactly what jsdom models least faithfully.

## Non-Functional Requirements

### Code Architecture and Modularity

- **Single Responsibility Principle**: the editor renders a textbox and reports intent; it neither decides what may be renamed nor performs the rename.
- **Modular Design**: the editor depends on the canvas library and React, never on a diagram module; the decision about what is renameable stays with the module that owns the label.
- **Dependency Management**: a canvas adopts the editor without any other canvas changing, and a module adopts the prompt marker without any other module changing.
- **Clear Interfaces**: the editor speaks the propose/submit/cancel contract the dialog host already speaks, so both presentations are interchangeable from the backend's point of view.

### Performance

- An open editor SHALL not cause the canvas to re-render its elements on every keystroke; typing is local until it is proposed or committed.
- Validation SHALL stay debounced as it is today, so typing does not become one round trip per character.

### Security

- No new surface: the inline editor proposes and commits through the same context interaction, with the same backend validation and the same refusals. A value refused by a module is refused identically whichever presentation asked for it.

### Reliability

- An abandoned edit dispatches nothing and leaves the document untouched.
- A committed edit is one command with an inverse, like every other edit here — one rename, one undo.
- An edit whose element vanished mid-flight fails safely and visibly rather than writing to something that no longer exists.

### Usability

- The editor looks like the label it replaces — same position, same approximate size — so that editing feels like changing the thing rather than filling in a form about it.
- Everything the dialog tells the user, the inline editor tells them too: the current value to start from, the refusal when there is one, and the fact that Escape abandons.
