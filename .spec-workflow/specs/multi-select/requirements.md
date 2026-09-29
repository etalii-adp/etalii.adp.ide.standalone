# Requirements Document

## Introduction

Today a diagram canvas holds exactly one selected thing. This specification adds selecting several: Ctrl+click to add and remove members one at a time for both elements and connections, and a dragged box over the canvas to sweep up elements. The property grid then describes the selection as a whole rather than refusing to describe it, and an edit made there applies to every member as a single undoable change.

Two things were checked before this document was written, because both change what it has to ask for.

**The selection protocol cannot express a set.** `ContextSelection` is a nested chain — an entry, then its child — carrying exactly one `element_id`, and `selectedElementIdOf` walks that chain returning one id. `SetPropertyRequest` likewise carries one `ContextSource`. Nothing in the wire or in the shared client helpers has a place to put a second element. Multi-select is therefore not a canvas-only feature: it needs the context protocol to carry a set, and this document states that as a requirement rather than leaving an implementer to discover it.

**There is no composite command.** `src/backend/EtAlii.Adp.Backend/History/` holds the dispatcher, the stack and the command interfaces, and nothing that groups several commands into one undoable unit. This repository's rule is that every edit is one command with an inverse; an edit across five elements that lands as five commands would be five undos, which is not what a user who made one gesture expects. A composite command with an all-or-nothing inverse is a core addition this feature requires.

**A third fact arrived after approval and was verified on 2026-09-06, when this document was re-read against develop before designing:** both premises above still hold at HEAD — the wire still carries exactly one `ContextSource` and `History/` is unchanged — and the client seam this feature needs **already exists, left deliberately**. `diagram-library-adoption` typed the library's selection as `DiagramSelection = readonly SelectedItem[]`, single-membered on purpose, with a doc-comment naming this specification as the reason: adopting the set changes the canvas layer once, and no module reading the seam changes at all. Every canvas now renders through that one layer, so Ctrl+click, the marquee and the selected styling land in one place rather than per canvas — the per-canvas adoption this document's first version had to assume is no longer the shape of the work.

## Which canvases get it, and why the others do not

The criterion is not "which canvas has the gesture wired" but **what a multi-element selection could do that a single one cannot**. Two capabilities qualify a canvas: its elements have **authored positions** the user arranges (so moving several at once means something), or it draws **selectable connections** (so Ctrl+click across edges means something).

Measured by which canvases dispatch `moveElementTo` when this document was written (2026-09-04), seven qualified: **c4, databricks, dependency-graph, helm-charts, rdf, timeline, wardley-map**. *Re-measured 2026-09-06: thirteen canvas files across ten modules — `ansible-structure` joined when `ansible-refinements` landed exactly as predicted below, and `causal-loop` and `sparql`, absent from the first census, both exist and dispatch it. The criterion held; only the list moved, which is why the requirements state the criterion.* Two modules still do not qualify, for two different reasons:

- **mindmap** — the user's own example, and the reason is exact: it has no authored positions at all. Its layout is computed from the tree, and its drag gesture is a *re-parent* (`MoveElementAsync` onto another node), not a move to a coordinate. A box drag there would sweep up nodes whose position nothing can store, and dragging several nodes onto a new parent is a different feature with different semantics — worth considering on its own merits, not as a side effect of this one.
- **azure-pipeline** — a computed column layout with no `moveElementTo`, so the same objection applies without the re-parent complication.
- **ansible-structure** — computed today, but the in-flight `ansible-refinements` specification gives it drag-to-reposition with positions in the `.adp` `layout:` block. It qualifies the moment that lands.

Because the third case shows the list is a moving target, the requirements below state the **criterion** and let the membership follow it, rather than freezing seven names that would be wrong within the week.

## Alignment with Product Vision

`structure.md` requires canvas infrastructure not to depend on any single diagram type's schema. Selection is already held to that rule — `selection.ts` exists precisely because eleven canvases had eleven copies of the same twenty lines — and this feature extends the same shared vocabulary rather than adding a parallel one. The property grid's contract (`ContextPropertyDefinition`: id, label, value, editor, read-only reason, group, candidates) is likewise diagram-neutral data the panel renders without understanding, and merged properties must remain exactly that.

## Requirements

### Requirement 1 — Ctrl+click builds a selection, for elements and connections alike

**User Story:** As a user arranging a diagram, I want to add and remove things from my selection one click at a time, so that I can act on exactly the set I mean.

#### Acceptance Criteria

1. WHEN a user Ctrl+clicks an unselected element or connection THEN it SHALL join the selection, and the previously selected members SHALL remain selected.
2. WHEN a user Ctrl+clicks a member of the current selection THEN it SHALL leave the selection, and the rest SHALL remain — so the same gesture both adds and removes.
3. WHEN a user clicks without Ctrl THEN the selection SHALL collapse to the single thing clicked, exactly as it behaves today.
4. WHEN a user clicks empty canvas without Ctrl THEN the selection SHALL clear, exactly as it behaves today.
5. WHEN a selection holds several members THEN every member SHALL carry the same visible selected styling a single selected member carries today, drawn through the shared canvas classes.
6. WHEN elements and connections are both selected THEN the selection SHALL hold them together without either kind displacing the other.

### Requirement 2 — A dragged box sweeps up elements

**User Story:** As a user, I want to drag a box over part of the diagram and select what it covers, so that selecting many elements does not mean many clicks.

#### Acceptance Criteria

1. WHEN a user drags a box across the canvas THEN a rubber-band rectangle SHALL be drawn during the drag and SHALL disappear when it ends.
2. WHEN the box drag ends THEN every element the rectangle covers SHALL become the selection; whether a partially covered element counts SHALL be one rule stated once and applied by every canvas, never a per-module choice.
3. WHEN a box drag is made with Ctrl held THEN what it covers SHALL be added to the existing selection rather than replacing it.
4. WHEN the box drag is abandoned — Escape, or a release outside the canvas — THEN the selection SHALL be exactly what it was before the drag began.
5. IF connections are covered by the box THEN they SHALL NOT be swept up: a box selects elements, and connections are selected by Ctrl+click, since a rectangle over a graph inevitably crosses edges the user did not mean.

### Requirement 3 — The box drag is one more gesture in the shared family, not a second pointer stack

**User Story:** As a developer, I want the rubber band to live where the other canvas gestures live, so that this feature does not start a second pointer implementation beside the one being consolidated.

#### Acceptance Criteria

1. WHEN the rubber band is implemented THEN it SHALL be one more gesture kind in the **one shared gesture layer that now exists** — a **marquee** press target inside `DiagramCanvas`, decided by `gesture/usePointerGesture` and scheduling its frames through a `gestureFrame` cell like reposition, pan and connect — and SHALL NOT be implemented privately in any module. *Amended 2026-09-06: as approved, this criterion asked for a derivative in `drag-and-drop-centralization`'s planned folder family; that family was never built, because `diagram-library-adoption` consolidated every gesture into `DiagramCanvas` and `drag-and-drop-centralization` shipped as the frame discipline inside it. The intent — no second pointer stack — is unchanged; the letter now names the machinery that actually landed.*
2. WHEN the marquee previews per pointer frame THEN it SHALL be inside the per-frame discipline and its guard — no canvas-wide state write and no layout read per move, the rubber band drawn through a live-write, and the cost guard exercising the marquee like every other kind it covers — per `drag-and-drop-centralization`'s Requirements 1.5 and 3.1 as amended. *Amended 2026-09-06: replaces the original not-yet-landed contingency, which is moot — that specification is complete on develop.*
3. WHEN the marquee and the pan gesture are both available THEN their trigger SHALL be arbitrated by a single rule shared by every canvas — both begin with a pointer press on empty canvas, so one of them must be qualified by a modifier or a button, and each canvas choosing for itself would make the same gesture mean different things in different diagrams.
4. WHEN the arbitration rule is chosen THEN the gesture that pans today SHALL keep panning by the same input it uses today, so that no user has to relearn navigation to gain selection.

### Requirement 4 — The selection travels as a set

**User Story:** As a developer, I want the backend to know what is selected when several things are, so that actions and properties can answer for the whole selection.

#### Acceptance Criteria

1. WHEN several things are selected THEN the context channel SHALL carry all of them; the current single-element chain cannot express this, so extending it is in scope for this feature.
2. WHEN the set is expressed THEN its members SHALL be the element ids the canvases already use — the shapes each module already parses in one place, such as the RDF family's `res:`, `blank:` and `edge:` — and no new id vocabulary SHALL be invented.
3. WHEN exactly one thing is selected THEN what the backend receives SHALL be indistinguishable from what it receives today, so that every existing resolver, action provider and property provider keeps working unchanged.
4. WHEN a selection member disappears — removed by an edit, or gone after a reload — THEN it SHALL drop out of the selection and the remaining members SHALL stay selected.
5. WHEN a set reaches a provider that can only answer for one thing THEN it SHALL answer for none rather than silently answering for the first member.

### Requirement 5 — The property grid describes the whole selection

**User Story:** As a user with several things selected, I want the grid to show what they have in common, so that I can see and change shared values without clicking through them one at a time.

#### Acceptance Criteria

1. WHEN several things are selected THEN the grid SHALL show the properties that **every** member defines — the intersection by property id — because a property only some members have would offer an edit that fails on the rest.
2. WHEN every member reports the same value for a shared property THEN the grid SHALL show that value.
3. WHEN members report different values for a shared property THEN the grid SHALL show that the values differ rather than showing any one member's value, and SHALL NOT present a differing value as though it were shared.
4. WHEN a shared property is edited THEN the new value SHALL be applied to every member, including those that already had it.
5. IF any member reports a property read-only THEN the merged row SHALL be read-only, and the reason shown SHALL say that at least one selected item refuses it.
6. WHEN the selection mixes kinds — two different element types, or an element and a connection — THEN the same intersection rule SHALL apply, and an empty grid SHALL be an acceptable and unremarkable answer, because two unlike things having nothing in common is the truth.
7. WHEN the selection holds exactly one member THEN the grid SHALL show precisely what it shows today.

### Requirement 6 — One gesture is one undo

**User Story:** As a user, I want a change I made in one action to come back in one undo, so that the history reflects what I did rather than how it was implemented.

#### Acceptance Criteria

1. WHEN an edit is applied across several selected members THEN it SHALL be recorded as a single undoable change, not one per member.
2. WHEN that change is undone THEN every member SHALL return to its prior value in one step, and the documents involved SHALL be byte-identical to their state before the edit.
3. IF applying the edit fails on any member THEN the whole change SHALL be rolled back and nothing SHALL be recorded in the history, so that a partly applied edit is never left behind for the user to find.
4. WHEN several elements are moved together THEN the whole move SHALL likewise be one undoable change, whichever way each canvas stores positions — the `.adp` `layout:` block, a layout sidecar, or coordinates inside the document itself, all three of which are in use today.
5. WHEN the composite change is implemented THEN it SHALL be a core capability usable by any module, since no such command exists today and building one per module would be the same code several times.

### Requirement 7 — Nothing that works today works differently

**User Story:** As a user of the diagrams I already use, I want single selection to behave exactly as it does now, so that a new capability costs me nothing.

#### Acceptance Criteria

1. WHEN a canvas gains multi-select THEN its existing client tests SHALL pass unchanged, and a test that must change SHALL be treated as evidence of a behaviour change rather than as a test to update.
2. WHEN a canvas that does not qualify is used THEN it SHALL behave exactly as it does today, with no rubber band and no Ctrl+click set-building.
3. WHEN the context menu is opened on a selection of several THEN it SHALL offer only actions that apply to all of them, and offering nothing SHALL be preferred to offering an action that would fail on some.
4. WHEN a module's own refusal applies to a member — a blank-node-rooted element, a truncated view, a read-only diagram — THEN that refusal SHALL still be issued by the module, in its own words, and SHALL prevent the whole composite edit per Requirement 6.3.

### Requirement 8 — The behaviour is guarded

**User Story:** As a maintainer, I want the merged-value and single-undo rules pinned by tests, so that they cannot erode as canvases adopt the feature.

#### Acceptance Criteria

1. WHEN merged properties are implemented THEN tests SHALL cover each case named in Requirement 5: identical values, differing values, a property only some members define, a read-only member, and a mixed-kind selection.
2. WHEN the composite command is implemented THEN a test SHALL prove that a multi-member edit is one history entry and that undoing it restores every document byte-for-byte.
3. WHEN a member fails mid-edit THEN a test SHALL prove nothing was applied and nothing was recorded.
4. WHEN a canvas adopts the marquee THEN a test SHALL fail if it implements the gesture privately rather than composing the shared derivative.
5. WHEN the feature ships THEN `tests.md` SHALL gain a manual check for a box selection followed by a merged edit and one undo, confirmed in a real browser.

## Non-Functional Requirements

### Code Architecture and Modularity

- **Single Responsibility Principle**: the marquee gesture, the selection set, the property merge and the composite command are four separate pieces; none reaches into another's concern.
- **Modular Design**: merging is a pure function over what providers already return (`ContextPropertyDefinition` records), so it can be tested without a canvas, a backend or a diagram.
- **Dependency Management**: no module learns about another; a canvas gains multi-select by composing shared pieces, and a module that does not qualify is not touched at all.
- **Clear Interfaces**: what a member of a selection *is* stays each module's business, exactly as `selection.ts` already states; that a selection names several is not.

### Performance

- Building and drawing a selection of many members SHALL not re-render elements outside the gesture on every pointer frame — the same per-frame discipline `drag-and-drop-centralization` requires, since a rubber band across a large diagram is precisely the case that exposes it.
- Describing a merged selection SHALL be proportional to the number of selected members, not to the number of elements the diagram draws.

### Security

- No new surface: selection carries ids the client already sends, and every edit still travels the existing context and history paths, subject to the same backend refusals.

### Reliability

- An abandoned marquee leaves no state and dispatches nothing.
- A composite edit is all-or-nothing (Requirement 6.3); a document is never left in a state no single command produced.
- A selection that outlives the thing it names loses that member rather than becoming invalid (Requirement 4.4).

### Usability

- Ctrl+click and rubber-band selection are what users expect from a diagram; this feature adds no vocabulary a user has to learn.
- The grid never lies about a shared value: a differing value is shown as differing, never as one member's value standing in for all.
