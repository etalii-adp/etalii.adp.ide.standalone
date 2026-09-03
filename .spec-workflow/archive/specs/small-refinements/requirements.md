# Requirements Document

## Introduction

This spec bundles three **small refinements** that each polish an existing surface rather than add a new one: scrollbars for the mindmap viewer, a sweep replacing placeholder text in the shipped examples with believable content, and explorer icons that show at a glance which files are diagrams — or could be.

They share a spec because none of them is big enough to carry one alone, and because all three are finishing work: the mechanisms exist (the timeline already has scrollbars, the examples already have tests that open them, the explorer already draws per-type icons), and what remains is applying an existing idea to one more place.

**Dependencies.** This spec redefines none of them:

* [`mindmap-diagram`](../mindmap-diagram/requirements.md) — the canvas gaining scrollbars.
* [`timeline-diagram`](../timeline-diagram/requirements.md) — where the scrollbar behaviour, and its reasoning, already live.
* [`project-root-folder-explorer`](../project-root-folder-explorer/requirements.md) — the tree whose icons gain state colors.
* [`modular-text-editors`](../modular-text-editors/requirements.md) — the editor family whose claims contribute to the "could open as" knowledge.
* structure.md's example-replication rule — every example touched here exists twice, in its module and in `src/examples/`, and the two must not drift.

## Alignment with Product Vision

All three serve the same product idea: the tool should *show* what it knows instead of making the user discover it. A mindmap larger than the view should say where the view sits; an example project should demonstrate the tool the way a real project would; the explorer should reveal which files ADP can already draw and which are one registration away.

## Requirements

### Requirement 1 — Scrollbars in the mindmap viewer

**User Story:** As a user panning around a large mindmap, I want scrollbars like the timeline's, so that I can see where the view sits inside the map and drag straight to another part of it.

#### Acceptance Criteria

1. WHEN a mindmap is open and its content extends beyond the view THEN the canvas SHALL show the same style of thin edge scrollbars the timeline shows: a horizontal bar along the bottom and a vertical bar along the right, each drawing a thumb whose position and size describe where the view sits inside the content's extent (plus a margin), exactly as `TimelineScrollbars` does for the timeline.
2. WHEN a thumb is dragged THEN the view SHALL pan accordingly — dragging a thumb only pans; zoom stays on the wheel and the ribbon, per the timeline's own rule.
3. WHEN the view is panned or zoomed by any other means THEN the thumbs SHALL follow, staying an honest description of the view.
4. WHEN the whole map fits in the view THEN the scrollbars SHALL claim the whole track (or hide, matching whatever the timeline does in that state) rather than inviting a pan that cannot happen.
5. WHEN this lands THEN the scroll-view code and its styling SHALL be **centralized as one reusable piece** — a shared component (and its CSS) living in the client's shared code, not in any one diagram module — with the timeline **migrated onto it** and the mindmap consuming the same one. Copying `TimelineScrollbars` into the mindmap module is not an acceptable implementation; after this spec there SHALL be exactly one scroll-view implementation, positioned so the next canvas (the dependency graph among them) picks it up by importing it.
6. WHEN the shared component is extracted THEN the timeline's behaviour SHALL be unchanged — its existing tests and manual checks still pass, and the extraction carries the timeline's own reasoning (a windowed bar over an unbounded plane, margin included) with it.

### Requirement 2 — Real data in the shipped examples

**User Story:** As someone evaluating ADP through its examples, I want every example to show believable content, so that the diagrams read as demonstrations rather than leftover scratch files.

The current state, measured: the mindmap examples are keyboard-mash (`sdfsdf`, `dfsdfsdf`, `AAA`/`BBB`, `Tesdfsdf` — both `mindmap.mm` and `design.mm`, in the module and in `src/examples/`), and the timeline's `example-2/roadmap.tml` carries `Test` labels and several untouched `New element` entries. The other families (c4, wardley-map, ansible-structure, azure-pipeline, and the editor examples) are authored content already — the sweep covers all of them, but is expected to change only where placeholder text is actually found.

#### Acceptance Criteria

1. WHEN the sweep is done THEN no shipped example — under any `src/diagrams/*/examples/`, `src/editors/*/examples/`, or `src/examples/` — SHALL contain placeholder or keyboard-mash text (`sdf…`, `AAA`, `Test 33`, `New element`, and their kin). Every label, node text, and description SHALL be believable content on a coherent theme per example (a real-looking product roadmap for the timeline, a real-looking subject for the mindmap).
2. WHEN an example is rewritten THEN it SHALL remain valid for its diagram type: the existing example-opening tests still pass, the validators report no problems for it, and any type-specific invariants (the timeline's rows and date ordering, the mindmap's Freeplane structure and node IDs) are preserved in form.
3. WHEN an example is rewritten THEN it SHALL still exercise what it exercised before — an example that existed to show connections keeps connections, one that showed nesting keeps nesting — and SHALL look presentable on the canvas: no overlapping elements, no degenerate one-node trees where a structure was being demonstrated. "Looks nice" is checked by opening each changed example in the running app, recorded as a manual pass.
4. WHEN a module's example changes THEN its `src/examples/` replica SHALL change identically, per structure.md's must-never-drift rule — using each family's own replication convention (byte-identical where nothing is project-relative).

### Requirement 3 — Explorer icons that show diagram state

**User Story:** As a user scanning the explorer, I want a file's icon color to tell me whether it is already a diagram, or could be one, so that I don't have to right-click everything to find out.

#### Acceptance Criteria

1. WHEN a file or folder has an accompanying `.adp` registration (the sibling registration a diagram type reads — `courier.dsl` beside `courier.adp`, or a folder-subject registration like `infrastructure.adp` beside `infrastructure/`) THEN its explorer icon SHALL be drawn in the theme's highlight green — the same `--color-primary` accent the rest of the shell uses.
2. WHEN a file has no `.adp` yet, but ADP could open it meaningfully — a diagram type claims its extension (it is registrable, or opens as a bare body the way an unregistered `.dsl` does), or a non-fallback editor claims it (its extension or exact name) — THEN its icon SHALL be drawn in a dark green highlight, visibly distinct from both the registered green and the neutral icon color.
3. WHEN a file is claimed by nothing but the plain-text fallback THEN its icon SHALL keep the neutral color — the fallback claims every file by construction, so counting it would turn the whole tree dark green and say nothing.
4. WHEN the two greens are defined THEN they SHALL live in the centralized theme styles (`src/client/src/index.css`'s variables) — the registered state reusing `--color-primary`, the potential state introducing one new variable there with light- and dark-mode values — and no component SHALL carry a literal color of its own for this.
5. WHEN the explorer decides which state a file is in THEN that knowledge SHALL come from the backend's own routing (the router and resolver that already answer these questions), never from a file-extension table kept in the client — the same rule every other type decision in the client already follows. What an entry already shows (its `.adp` sibling is itself an entry in the tree) MAY be used where it answers the question exactly.
6. WHEN the registration state of a file changes — an `.adp` appears through **Add as diagram…**, or is deleted — THEN the icon SHALL follow without a reload, through the same pushed hierarchy updates the tree already lives on.

## Non-Functional Requirements

### Code Architecture and Modularity
- **Reuse over duplication**: Requirement 1 mandates one centralized scroll-view component and stylesheet, shared by the timeline and the mindmap and importable by every later canvas; Requirement 3 reuses the existing routing/resolution seams rather than adding a parallel client-side notion of "diagram file".
- **Centralized theming**: every color this spec introduces lives in `index.css`'s variable block, with both light and dark values, per the shell's existing convention.

### Performance
- Icon-state knowledge must not add a per-entry backend round trip while the tree renders; it rides whatever the hierarchy push already carries, extended if the design finds it carries too little.

### Reliability
- The example sweep must leave every existing example-opening and validation test passing, and the replication drift guards intact.

### Usability
- The three icon states (registered green, potential dark green, neutral) must be distinguishable in both light and dark mode; the visual check is recorded as a manual pass in `tests.md`, since no unit test can see rendered colors.
