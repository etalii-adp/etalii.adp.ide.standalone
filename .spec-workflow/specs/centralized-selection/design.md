# Design Document

## Overview

The highlight is already drawn once. `DiagramCanvas`'s `isSelected(kind, id)` applies `canvas-selected` to elements and connections alike. What this design moves into the library is **deciding what the selection is**: the inbound mapping from the backend's pushed selection, the outbound push from a gesture, and the context-menu integration, which today are written by hand in all sixteen canvases.

**The one idea that makes this generic is that kind resolution needs no module knowledge.** The four working modules agree on one inbound rule: *the selected id is a connection if it names one of the model's relations, and an element otherwise*. The library already holds both lists, as `DiagramModel.connections` and `DiagramModel.elements`. So the rule every module wrote by hand is a lookup the library can do alone.

Beside it, two looks are defined once each (*selected* and *would accept a connection*), and today they are not even distinct.

## What this design decides

| Question | Answer | Requirements |
| --- | --- | --- |
| Where the selection model lives | In `DiagramCanvas`, given the canvas's identity as a `source` prop | 1.1–1.4 |
| How a type opts out | `selectable: false` on the element or relation type, **default selectable** | 2.3, 2.4 |
| What a click means | Stated once in the library; a drag does not select | 3.1–3.4 |
| What "selected" looks like | One library-drawn outline, independent of fill | 5.1–5.3 |
| What "would accept" looks like | One distinct library-drawn look, one class | 6.1, 6.2 |
| How states combine | Three independent flags, additive | 5.3, 6.2 |
| How it stays ready for sets | `DiagramSelection` arrays end to end | 7.1, 7.2 |
| How compliance is asserted | A text guard plus a mounted assertion in every module's harness | 9.1–9.4 |

## Alignment with Steering Documents

- **structure.md** — the selection model is canvas infrastructure and moves to the library; *which* types are selectable is a fact about the diagram and stays in the module's definition. No selection property names a diagram type.
- **tech.md, consumers are pure subscribers** — the library reads the pushed selection and pushes gestures back; no module interprets either.

## Code Reuse Analysis

| Piece | Where | Used how |
| --- | --- | --- |
| `isSelected(kind, id)` and `canvas-selected` | `DiagramCanvas` | Unchanged as the drawing path; now fed by the library's own selection |
| `select(item)` and `onPress` | `DiagramCanvas` | Already the only place a press selects; becomes the only place a selection is pushed |
| `useContextSelection`, `useContextConnection` | `shell/context` | Called by the library instead of by sixteen modules |
| `selectedElementIdOf`, `elementSelectionOf`, `innermostKey` | `canvas/selection`, `shell/context` | The inbound and outbound conversions every module already uses, called once |
| `groupState = { selected, dragging, connectTarget }` | `LibraryElement` (`c077af58`) | The composition point for the looks; already three independent flags |
| Declared actions (`declarative-diagram-modules`) | the definition | Replace the last private focus state (ansible's Enter/Space) |

## Architecture

### A. The selection model, inside `DiagramCanvas`

`DiagramCanvas` gains one prop, **`source: { entryId: Uint8Array; path: readonly string[] }`**, the identity the shell already gives every canvas in `DiagramCanvasProps`. It is a value, not glue, and it is all the library lacks to talk to the backend itself.

- **Inbound.** The library reads the connection's selection through `useContextSelection`, takes the id through `selectedElementIdOf`, and resolves its kind against the model it already holds: a connection if `model.connections` has it, an element if `model.elements` has it, and nothing otherwise. An item of an unselectable type resolves to nothing. The result is the `DiagramSelection` the highlight already reads.
- **Outbound.** The library's existing `select(...)`, which `onPress` already calls for elements, connections and the background, additionally pushes `elementSelectionOf(source.entryId, source.path, id)`, or `null` for an empty selection.
- **The menu.** The library builds the `DiagramContextIntegration` itself: `selectionKey` from `innermostKey`, `actions` from the pushed selection, `selectForMenu` as the context-menu push, and `executeAction` against the current selection.
- **Refusals.** A backend action run from the shared menu can be refused. The library has no refusal surface of its own, and each module's rejection display is outside this specification, so the library raises the refusal to the module as an event (`action-refused`, carrying the backend's message). Showing a backend refusal of an action is event handling for an action, which the user's rule permits.

The module-facing `selection` prop, `context` prop and `events.onSelectionChanged` handler are **removed from the contract**. A module cannot hand-wire selection, because there is nothing left to wire.

### B. Selectability — declared, never inferred

`ElementTypeDefinition` and `RelationTypeDefinition` each gain **`selectable?: boolean`**. **The default is selectable, and the default is written in the type's doc-comment in words.** So "not selectable" is only ever a value someone declared: a type nobody configured selects like every other, and an unselectable type is always a recorded decision. That rules out the ambiguity found with rename in the previous specification, where *nobody wired it* and *it must not happen* looked the same.

A press on an unselectable type behaves exactly as a press on the background. That is c4's boundary behaviour, which moves from a hand-written check in `onSelectionChanged` to `selectable: false` on the boundary type. `mindmap`'s and `wardley-map`'s relation types declare `selectable: false`, each with a readme line recording that it is a decision about the notation (Requirement 2.4).

### C. What a click means, stated once

- A press on an element or connection makes it the one selected item, replacing any previous selection of either kind.
- A press on the background clears the selection. This is the branch `helm-charts` and `ansible-structure` lack today.
- When the model changes and the selected item is no longer in it, the library pushes `null`. This settles in one round: the backend clears, pushes an empty selection, and the inbound mapping resolves it to nothing.
- **A drag does not select what it moves.** This is settled by the code rather than chosen here: `select` is called only from `onPress`, which fires only on an unmoved release, never from `onDragEnd`, and no module selects from its own drop handler. It is one library decision, identical on all sixteen canvases, so the four working modules cannot disagree with each other on it. It is recorded as a check in the browser pass rather than left as an assumption.

### D. One look for "selected"

`canvas-selected` becomes **the only selected class**, and its look is an **outline the library draws outside the shape's own boundary**. It does not recolour the shape's stroke, as the shared rule does today, so it is independent of the shape's fill and stroke. That is what makes it legible on colours the backend chooses, such as c4's element styles (Requirement 5.2). A selected connection keeps its centrally defined line treatment. Anchors keep `1df91874`'s behaviour of changing colour with their element when selected.

The thirteen module-private selected declarations are deleted, and `causal-loop`'s second declaration of the shared class goes with them.

### E. One distinct look for "would accept a connection"

Today one CSS rule styles both states, so they are identical. The rule is split:

- *Would accept* becomes **`library-connect-target` only**. The twelve private `<module>-connect-target canvas-connect-target` declarations are deleted, as the amended Requirement 6.1 requires.
- Its look is **deliberately unlike selection**: a dashed outline, at a larger offset than the selected outline, in its own colour token.

An element that is both selected and a valid drop target shows both at once, one outline inside the other (Requirement 6.2). Anchors under a connect target take the accept colour, not the selected colour.

### F. How the states combine

`LibraryElement` already computes `{ selected, dragging, connectTarget }` as three independent flags, and since `c077af58` the dragging look ends at release while the position is held at the landing. **The rule is that the three are independent and additive, and none reads another.**

- **Dropped, held and selected** shows the selected outline at the held position, with no dragging look.
- **Selected and a valid drop target** shows both outlines (E).
- **Dragging** shows its own look and does not change what is selected (C).

### G. One source of truth

- **`azure-pipeline`** deletes its `focusedId` state and the `payload.focused` field computed from it. Its highlight becomes `canvas-selected`, driven by the selection, like everyone else's.
- **`ansible-structure`**'s `focusedId` drives one thing: Enter or Space on the focused node reveals its file. That becomes a **declared action** (`invokedBy` Enter and Space, applying to elements) whose handler runs against the library's selection, which is the carve-out `declarative-diagram-modules` already provides. The private state is deleted.

### H. Ready for sets without implementing them

The selection is a `DiagramSelection` array throughout. Inbound, the one pushed id becomes a one-item array; outbound, the single member is pushed, because the context wire carries one. When `multi-select` lands, the inbound mapping reads all members and the outbound push sends all of them. Nothing here is rewritten, and none of Ctrl+click, the marquee or the wire change is built (Requirement 7).

## Components and Interfaces

| # | Component | Where | Requirements |
| --- | --- | --- | --- |
| 1 | `source` prop, inbound and outbound model, menu integration, `action-refused` | `DiagramCanvas` | 1.1–1.4, 3.3 |
| 2 | `selectable` on element and relation types, default stated | `diagramDefinition.ts` | 2.1–2.4 |
| 3 | The click rules and the vanished-item clear | `DiagramCanvas` | 3.1–3.4 |
| 4 | The selected outline and the accept outline, split from one rule | `canvas.css`, `LibraryElement` | 5.1–5.3, 6.1, 6.2 |
| 5 | The composition rule | `LibraryElement` | 5.3, 6.2 |
| 6 | The two guards | `canvas/library` | 9.1–9.4 |
| 7 | Module migrations: delete glue, private classes, private state | the sixteen canvases | 1.4, 4.1, 4.2, 5.1, 6.1 |
| 8 | The `declarative-diagram-modules` 1.2 annotation, on its own card | that spec's requirements | 8.1, 8.2 |
| 9 | The browser pass | `tests.md` | 10.1–10.3 |

## Compliance: the two guards

**The text guard** (`noModuleSelection.test.ts`) walks every module client and fails, naming each offender, on any of: a `DiagramSelection` derivation, an `onSelectionChanged` handler, a `selection=` or `context=` prop, a declared class bound to `state.selected` or `state.connectTarget`, or module-held focus state. It is keyed per canvas file, so `databricks`' wrappers cannot misreport, and it carries both canary shapes: a floor on files walked and a named member present. Its doc-comment states its limit: glue under an unrecognisable name goes unseen.

**The mounted assertion** closes that limit. Requirement 9.2 asks that *every registered canvas* be mounted and checked. Each module already mounts its canvas in its own test harness with a mocked stream and a real model. So the design provides one shared assertion, `expectLibrarySelection(mount)`, and every module's canvas test calls it. It checks that a pushed element selection highlights that element, a pushed connection selection highlights that connection wherever its type is selectable, and a background press pushes `null`. **The text guard also asserts that every registered module's canvas test calls it**, so a module cannot join the tree unchecked.

**Seen red first against today's `helm-charts`**: its background press pushes nothing and its connections are never highlighted, so it fails two of the three checks before migration and passes all three after.

## Migration order

1. **The library** — components 1 to 5, gated and landed before any module changes.
2. **Reference: `timeline`.** It already uses only the shared look and already selects connections, so migrating it proves the central model reproduces the user's working behaviour with the smallest visible change. Its tests passing unchanged is the evidence.
3. **The four the user named as broken**: `helm-charts`, `ansible-structure`, `azure-pipeline`, `dotnet-dependency-graph`. This is the acceptance set, and each must end up behaving like `timeline`.
4. **The remaining eleven**, each as its own landing: `dependency-graph`, `causal-loop`, `c4`, `databricks`, the four `rdf` readings, `sparql`, `mindmap`, `wardley-map`.
5. **The annotation** on `declarative-diagram-modules` 1.2, raised on its own card (Requirement 8).
6. **The browser pass**, all sixteen.

## Error Handling

| Scenario | Handling |
| --- | --- |
| Pushed selection names an id not in this model | Resolves to nothing, and nothing is highlighted |
| Selected item removed by an edit | The library pushes `null` once; settles in one round |
| Press on an unselectable type | Treated as a background press |
| Menu action refused by the backend | `action-refused` is raised to the module, whose existing rejection display shows it |
| Module still passes `selection`, `context` or `onSelectionChanged` | Removed from the contract, so typecheck fails, and the text guard names it |

## Testing Strategy

- **Library unit tests** for kind resolution, selectability, each click rule, the vanished-item clear, and the composition rule. jsdom can see classes, so the flags and classes are asserted there.
- **Every module's canvas test** calls `expectLibrarySelection`, and the text guard enforces that it does.
- **Requirement 11.1's bar**: tests pass unchanged, except those asserting a private selected class (citing Requirement 5) or a private connect-target class (citing Requirement 6.1). Any other test change is a behaviour change.
- **The browser pass** (`tests.md`, all sixteen) is the only evidence for the look: the outline is legible on each diagram's fills, including backend-chosen colours; a connection's highlight runs its whole route; selected and accept are told apart when both hold; the background clears; and a drag does not select. jsdom applies no CSS, so it is not taken as evidence for any of these (Requirement 10.3).

## Considered and declined

**A React context for the canvas identity instead of a `source` prop.** It would be implicit where a prop is explicit and testable, and the shell already hands every canvas these two values.

**A module callback to resolve kind.** Declined because the library already holds both lists. A callback would put the glue back under a new name.

**Keeping module overrides of the selected look behind a recorded reason.** The user ruled for one look, and every escape hatch in this tree has been taken universally.

**One guard mounting all sixteen canvases directly.** It would have to fake thirteen different stream protocols. That would be the test re-implementing each module, a second copy of the data rather than a check on it. The shared assertion in each module's own harness mounts the real canvas with the real model, and the text guard makes its presence complete.

**Recolouring the stroke as today's rule does.** A stroke recolour is only legible where it contrasts with the fill beside it, which fails on backend-chosen colours. An outline outside the shape does not depend on the fill.

## Deviations and notes

- **Requirement 6.1 was amended on 2026-09-11 by user ruling**, during this design: the drop-target highlight joins *one way*, and the twelve private connect-target declarations go. The amendment is marked in place in the requirements and raised on its own card beside this one.
- **Requirement 9.2's "mount every registered canvas"** is met through each module's own harness plus a completeness check, not by one test faking every stream. The reason is under *Considered and declined*.
