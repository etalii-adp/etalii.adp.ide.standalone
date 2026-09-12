# Requirements Document

## Introduction

The user's instruction, verbatim:

> *"Create a specification wrt to centralize the selection + highlighting of elements and connections. Implement it in one way and then apply it consistently across the diagrams. Diagrams where it works as expected are: timeline, dependency-graph, causal-loop-diagrams and the c4 diagrams. Diagrams that need it applied: helm-charts, dotnet-dependency-graph, azure-pipeline-diagram and the ansible-structure-diagram."*

The user supplied the **acceptance oracle** as well as the scope: four modules whose selection behaves as intended, and four whose selection does not. Two further decisions were taken by the user on 2026-09-10, both on the recommended option:

- **All sixteen canvases move to one mechanism**, not only the eight named. The named eight remain the acceptance set.
- **Selection has one shared look.** Every module-private "selected" style goes, and the library's shared highlight is the only one.

## What was measured

All sixteen module canvases that render through `DiagramCanvas` were read at `c72b33f2`: `AnsibleCanvas`, `PipelineCanvas` (azure-pipeline), `C4Canvas`, `CausalLoopCanvas`, `DatabricksCanvas`, `DependencyGraphCanvas`, `DotNetDependencyGraphCanvas`, `HelmCanvas`, `MindmapCanvas`, `OwlCanvas`, `RdfCanvas`, `ShaclCanvas`, `SkosCanvas`, `SparqlCanvas`, `TimelineCanvas` and `WardleyCanvas`.

### The highlight is already central; the selection model is not

`DiagramCanvas` already draws the highlight in one place: `isSelected(kind, id)` checks the `selection` it is given and applies `canvas-selected` to elements and to connections alike. **What is not central is deciding what that selection is.** Every one of the sixteen canvases writes the same three pieces of glue by hand:

1. **Inbound** — a `useMemo<DiagramSelection>` turning the backend's pushed selection (`selectedElementIdOf`) into the library's `selection` prop.
2. **Outbound** — an `onSelectionChanged` handler turning a canvas gesture back into a backend selection (`elementSelectionOf`).
3. **The menu** — a `context={{ … }}` prop carrying `selectionKey`, the actions, `selectForMenu` and `executeAction`, without which the shared context menu does not open on a selection.

### The four that work agree with each other

`timeline`, `dependency-graph`, `causal-loop` and `c4` write the same glue. **Inbound**: the selected id becomes a *connection* if it names one of the model's relations, and an *element* otherwise. **Outbound**: a gesture on either kind is pushed to the backend, and an empty selection pushes `null`, which deselects. **All four pass `context`.** Their only differences are *which* element types they refuse — c4's inert boundaries deselect rather than select, causal-loop accepts variables and loop badges — which is a fact about an element type, not about selection.

### The four that do not work are four different variants

| | Selected connection highlighted | Connection press reaches the backend | Background click deselects | Private selection state | Shared menu on selection |
| --- | --- | --- | --- | --- | --- |
| `helm-charts` | no | no — filtered out | **no — no else branch** | — | no |
| `ansible-structure` | no | no — filtered out | **no — no else branch** | **yes, `focusedId`** | no |
| `azure-pipeline` | no | no — filtered out | yes | **yes, `focusedId`** | no |
| `dotnet-dependency-graph` | no | **yes, mislabelled as an element** | yes | — | no |

**None is the same defect twice.** Four authors wrote the same glue four ways, which is what hand-written glue does once it exists in more than one place.

`azure-pipeline` is the sharpest case: **its highlight does not follow the selection at all.** It declares its highlight on `payload.focused`, a field it computes from a private `focusedId` React state set in its own `onSelectionChanged`. The backend's selection and the drawn highlight are two sources of truth, and the drawing follows the private one.

### The look of "selected" is per-module everywhere

Fourteen of the sixteen canvases declare a selected class of their own beside the library's shared `canvas-selected`. **Thirteen are private classes**: `c4-node-focused`, `helm-node-selected`, `ansible-node-selected`, `pipeline-focused`, `dependency-graph-selected`, `dotnet-dependency-selected`, `databricks-selected`, `mindmap-node-focused`, `owl-selected`, `shacl-selected`, `skos-selected`, `sparql-selected` and `wardley-selected`. The fourteenth is `causal-loop` declaring the shared class a second time. **Only `TimelineCanvas` and `RdfCanvas` rely on the shared highlight alone** — the other three `rdf` readings declare private classes. So even the four that work do not look alike when selected.

### Of the unnamed eight

The four `rdf` readings follow the working pattern, each pushing whatever was pressed. **`databricks` does not, and this sentence said it did — an error in this census, corrected on 2026-09-12.** Its `onSelectionChanged` drops any press that includes a connection, so an edge press pushes nothing at all, not even a clear, and `JobCanvas.test.tsx` pins that. The intent was never that edges are exempt: `src/client/src/canvas/label/readme.md` records them as *pending, not exempt*, and the backend already resolves edge ids. So `databricks` edges are selectable under 2.1, with no declaration, and it is a sixth canvas where a connection press changes behaviour. **`mindmap` and `wardley-map` select no connections at all**, and nothing records whether that is a decision or an omission.

### Why this survived the declarative migration

`declarative-diagram-modules` completed on 2026-09-10 and removed every shape renderer from every module. **The selection glue survived it intact in all sixteen**, and the reason is that specification's own words: its Requirement 1.2 defined the permitted event handling as dispatching *"a backend action, command **or selection**"*. **Per-module selection wiring was licensed in writing**, so it stayed per module, and four copies drifted. This specification withdraws that clause.

### Two meanings of "highlight" already exist

Besides *selected*, the library draws *would accept this connection* on the element a connection drag is over — `library-connect-target`, decided by the declared relation constraints (`397ac141`). The two answer different questions and can hold at the same time.

*Added on 2026-09-11, during design, and ruled by the user the same day.* Two things were found once the look itself was read. **First, the two meanings currently look identical**: `canvas.css` styles `.canvas-selected .canvas-node` and `.canvas-connect-target .canvas-node` in one shared rule — the same stroke colour, the same width — and their anchors share one colour too. So an element that is both selected and a valid drop target cannot show both today. **Second, the drop-target highlight has drifted the way selection did**: the library applies `library-connect-target`, and four modules also declare **twelve private** `<module>-connect-target canvas-connect-target` classes of their own — seven in the OWL reading, two each in `databricks` and the SKOS reading, one in `dependency-graph`. The user ruled that the drop-target highlight joins *one way* as well.

## Alignment with Product Vision

`structure.md` requires canvas infrastructure to carry no diagram-type knowledge, and a new diagram type to be addable without touching core code. Selection is canvas infrastructure written sixteen times by hand. `product.md`'s **"Don't reinvent, integrate"**: every one of those copies re-implements the same three pieces of glue next to a library that already draws the result.

## Requirements

### Requirement 1 — One selection model, owned by the library

**User Story:** As a module author, I want selection to work without writing any selection code, so that my diagram cannot select differently from the others.

#### Acceptance Criteria

1. WHEN the backend pushes a selection THEN the library SHALL derive the highlighted items itself; **no module client SHALL turn the pushed selection into a `DiagramSelection`.**
2. WHEN the user selects on the canvas THEN the library SHALL push the selection to the backend itself; **no module client SHALL handle `onSelectionChanged` to push a selection.**
3. WHEN a canvas mounts THEN the shared context menu SHALL work on the current selection **without the module passing `selectionKey`, actions, `selectForMenu` or `executeAction`**.
4. WHEN this is complete THEN **all sixteen canvases** SHALL select through this one mechanism, and no module client SHALL retain a hand-written copy of any of the three pieces.

### Requirement 2 — Elements and connections alike

**User Story:** As a reader, I want a connection to be selectable and to show that it is selected, exactly as an element does.

#### Acceptance Criteria

1. WHEN a connection is pressed THEN it SHALL become the selection, SHALL be highlighted, and SHALL reach the backend **as a connection**, never labelled as an element.
2. WHEN the backend pushes a selection naming a connection THEN that connection SHALL be highlighted.
3. WHEN a diagram type should not select some of its element or relation types THEN it SHALL **declare** each such type unselectable in its definition, and a press on an unselectable type SHALL behave as a press on the background. This replaces c4's hand-written boundary check.
4. WHEN `mindmap` and `wardley-map` are migrated THEN their connections SHALL keep today's behaviour — not selectable — **by declaration**, and each module's readme SHALL record that this is a decision about the notation. Making them selectable is a separate question for the user, not a side effect of this work.

### Requirement 3 — Selection behaves the same on every diagram

**User Story:** As someone moving between diagrams, I want a click to mean the same thing everywhere.

#### Acceptance Criteria

1. WHEN an element or connection is pressed THEN it SHALL become the one selected item, replacing any previous selection of either kind.
2. WHEN the background is pressed THEN the selection SHALL clear. This is the behaviour `helm-charts` and `ansible-structure` lack today.
3. WHEN a selected item stops existing — an edit, a reload — THEN the selection SHALL clear rather than point at nothing.
4. WHEN these rules are stated THEN they SHALL be stated **once**, in the library, and apply to every canvas without a module opting in.

### Requirement 4 — One source of truth

**User Story:** As a maintainer, I want exactly one answer to "what is selected", so that the drawing cannot disagree with it.

#### Acceptance Criteria

1. WHEN anything on a canvas depends on what is selected — the highlight, a detail panel, a focus ring — THEN it SHALL read the library's selection, and **no module client SHALL hold selection or focus state of its own**. This removes the `focusedId` state in `azure-pipeline` and `ansible-structure`.
2. WHEN `azure-pipeline` is migrated THEN its highlight SHALL follow the selection rather than a model field computed from private state.

### Requirement 5 — One look

**User Story:** As a reader, I want "selected" to look the same on every diagram.

#### Acceptance Criteria

1. WHEN an element or connection is selected THEN it SHALL be drawn with the shared highlight **only**, and every module-private selected class SHALL be removed — the thirteen listed above — together with `causal-loop`'s redundant second declaration of the shared class.
2. WHEN the shared highlight is drawn THEN it SHALL be legible on any fill a diagram uses, **including colours the backend chooses**, such as c4's element styles.
3. WHEN the shared look is defined THEN the look of a selected element and of a selected connection SHALL both be defined centrally. The anchors coloured with their element when selected (`1df91874`) are part of that look and SHALL be kept.

### Requirement 6 — The two meanings of highlight stay two

**User Story:** As a reader dragging a connection, I want to tell *selected* from *would accept this* at a glance.

#### Acceptance Criteria

1. WHEN the look is centralised THEN *selected* and *would accept a connection* SHALL remain **two mechanisms, each with exactly one class and one look, both owned by the library**. One answers what the reader chose; the other answers what a gesture would do. Neither SHALL be drawn with the other's class or share its look, and **the twelve module-private connect-target declarations SHALL be removed**, with `library-connect-target` as the only drop-target class. *Amended on 2026-09-11 by user ruling; see "Two meanings of highlight already exist".*
2. WHEN an element is both selected and a valid drop target THEN both SHALL be visible.

### Requirement 7 — Multi-selection is not specified here, and not ruled out

**User Story:** As the eventual author of multi-select, I want this work to leave room for a set.

#### Acceptance Criteria

1. WHEN this specification is implemented THEN it SHALL be **single-selection**, and SHALL NOT implement Ctrl+click set-building, a marquee, or any change to the context wire's single selection. `multi-select`'s parked requirements and design stay authoritative for those.
2. WHEN the central model is built THEN it SHALL take and return the library's existing `DiagramSelection` array rather than a single id, so that a set can later flow through it without rewriting it.

### Requirement 8 — The permission that let this drift is withdrawn

**User Story:** As a future reader of the declarative rule, I want it to say what it means.

#### Acceptance Criteria

1. WHEN this specification lands THEN `declarative-diagram-modules` Requirement 1.2's inclusion of *selection* among the permitted per-module handlers SHALL be withdrawn, and a pointer to this specification SHALL be recorded there.
2. THEN the permitted per-module event handling SHALL be **actions and validation**, as the user's original instruction said. Selection is not a module's to handle.

### Requirement 9 — Compliance is asserted, never reviewed

**User Story:** As a maintainer, I want the four broken modules' failure to be impossible to reintroduce.

#### Acceptance Criteria

1. WHEN the text guard is written THEN it SHALL walk every module client and fail, naming each offender, on a hand-written `DiagramSelection` derivation, an `onSelectionChanged` handler, a declared class bound to the selected state, or module-held selection state. It SHALL be keyed per canvas file so `databricks`' wrappers cannot misreport, and SHALL carry both canary shapes: a floor on files walked and a named member present.
2. WHEN the behavioural guard is written THEN it SHALL **mount every registered canvas** and assert, for each: a pushed element selection highlights that element; a pushed connection selection highlights that connection wherever the relation type is selectable; a background press clears the selection. *Mount, don't grep*: selection is reached through composition, and a text check alone reports wrappers wrongly.
3. WHEN either guard is accepted THEN it SHALL first have been **seen to fail** — against today's `helm-charts`, whose background press does not deselect and whose connections cannot be selected, which makes it the natural red.
4. WHERE the text guard can be evaded by glue under an unrecognisable name THEN that limit SHALL be stated in its doc-comment. The behavioural guard is what closes it.

### Requirement 10 — The browser is the acceptance instrument for the look

**User Story:** As the user, I want highlighting confirmed where it is seen, not only where it is computed.

#### Acceptance Criteria

1. WHEN the work completes THEN a `tests.md` entry SHALL cover **all sixteen canvases** in a real browser: select an element, select a connection where selectable, press the background, and confirm the shared highlight appears on the right item, reads clearly on that diagram's fills, runs the length of a connection's route, and clears.
2. WHEN the named eight are checked THEN the four that did not work SHALL behave exactly as the four that did. That comparison is the user's own oracle and SHALL be recorded as such.
3. THEN jsdom SHALL NOT be taken as evidence of the look. It applies no CSS, so Requirement 5's legibility and the visible difference between Requirement 6's two meanings are browser-only criteria.

### Requirement 11 — What changes, and what does not

**User Story:** As a user of these diagrams, I want the only visible change to be the one I asked for.

#### Acceptance Criteria

1. WHEN a canvas is migrated THEN its existing tests SHALL pass unchanged, **except a test that pinned behaviour which a named acceptance criterion of this specification changes**. Such a change SHALL **name that criterion**, and SHALL change **only what that criterion changes**, leaving the rest of the test as it was. **Any other test that must change is evidence of a behaviour change, not a test to update.** The criteria are therefore the complete list of visible changes this work is permitted to make, which is what this requirement is for: a test cannot be reconciled with a criterion that does not speak to it, and a criterion that changes nothing visible excuses no test.
   - *This replaced a case-by-case list on 2026-09-12, by the user's ruling, after three tests in one day changed for three different reasons and each needed its own amendment. **The instances so far, each a worked example of the rule rather than the rule itself:*** a test asserting a module-private selected class (5.1 removes the class); a module-private connect-target class (6.1 removes it); a test whose Arrange established a selection through module-held selection or focus state (4.1 removes that state), where only the Arrange changes and it then establishes the selection the way the backend does, because the four working canvases already select only after the backend's round trip; and `databricks`' edge press, which pinned that nothing at all is pushed (2.1 makes the connection selectable). *The first two were named on 2026-09-11 with 6.1's amendment, the third on 2026-09-11 for `ansible-structure` and `azure-pipeline`.*
2. WHEN a canvas is migrated THEN its gestures, actions, drag and connect behaviour SHALL be unchanged. This work moves where selection is decided and what it looks like, and nothing else.

## Non-Functional Requirements

### Code Architecture and Modularity

- Selection belongs to the library; which types are selectable belongs to each module's definition. No selection property names a diagram type.
- A module adopts by **deleting**: the three pieces of glue and its private selected class go, and nothing replaces them in the module.

### Reliability

- One source of truth for what is selected (Requirement 4); a selection naming nothing clears (Requirement 3.3).

## Sources

- The sixteen module canvases and `DiagramCanvas.tsx`, read at `c72b33f2`, per-module findings counted from the files rather than from a pattern.
- `declarative-diagram-modules` Requirement 1.2, whose *"or selection"* is the licence withdrawn here.
- `1df91874` (anchors coloured with their element when selected) and `397ac141` (the drop-target highlight asking the declared relation constraints).
- The parked `multi-select` requirements and design, whose boundary Requirement 7 keeps.
