# Requirements — layout-modes

## Introduction

A reader can move a diagram's elements, and a module can compute where they go. What nobody can do is **choose between those two**, or choose the shape the computation aims at. This specification makes the layout of an individual diagram a setting the user selects, from four named modes:

* **Manual layout**
* **Automatic layout / Horizontal**
* **Automatic layout / Vertical**
* **Automatic layout / Cloud**

Those four names are the user's own, and they are the user-facing vocabulary throughout — menus, the property grid, and whatever the mode is stored as. This document does not rename them to something more technical.

**The direction is not a new idea; it is a hard-coded private decision in every module today.** `HelmLayout` places five columns left to right. `SkosLayout` puts top concepts on the first row and every concept one layer below its deepest broader concept — the same layered idea rotated a quarter turn. `MindmapLayout` splits first-level branches into two sides, each laid out as its own column, which is neither. Ten layout implementations exist across eight modules and each settled its direction once, in code, for reasons that were local and are now invisible. The user is asking for that decision to move out of the module and into the reader's hands.

**Manual is already half-built.** A `layout:` block in the `.adp` registration stores an element key and its `x y`, read by `RegistrationLayout`, written by `SetRegistrationLayoutCommandHandler` with a byte-restoring inverse. It appears in 7 of the 78 showcase registrations. So Manual is largely a matter of naming an existing mechanism and making it universal — but only five modules reference that mechanism at all (ansible-structure, databricks, helm-charts, rdf, sparql). Four modules have a canvas and no persistence whatsoever: azure-pipeline, dependency-graph, mindmap and timeline. c4 persists through a sidecar of its own rather than the block.

**The automatic modes answer a measured problem.** `diagram-layout` measured the vendored corpus and found extents of 142:1, 55:1 and 1:48 — the STW thesaurus lays out 98 screens wide and two-thirds of one screen tall. Since the view-delta loop landed and viewports genuinely cull, a ribbon that shape means nearly every element is culled nearly always: the reader pans blindly along one axis through a diagram that is almost entirely absent. A user who can say "lay this out vertically instead" has a remedy that no amount of panning provides.

**Not every diagram can offer all four, and saying so is part of the requirement.** The steering documents name the fork this turns on: whether positions are *computed by the module* or *authored in the document* is "the single biggest fork in a diagram type's design". A Wardley map's coordinates are the author's claim about evolution and value — rearranging them automatically would not improve the picture, it would destroy the assertion. A timeline's horizontal axis *is* time. A mindmap's two-sided arrangement is a notation, not a default. A requirement that every diagram offers four modes would be wrong, so this specification requires each module to *declare* what it supports and says what happens to the modes it does not.

### Relationship to `diagram-layout`

The two specifications share a subject and divide cleanly:

> **`diagram-layout` is about how well a layout places things, and where positions are stored. This specification is about which layout is in force, and how a reader changes it.**

This specification **claims**: the four mode names as user vocabulary; how a module declares the modes it supports; where the chosen mode is persisted; what a mode switch does to positions authored under Manual; and the fallback when a stored mode names something a module cannot do.

This specification **claims none of, and inherits all of**: the unplaced-element contract (`diagram-layout` R1), the extent ratio bound (R2), non-overlap (R3), the `.adp` `layout:` block as the one persistence mechanism (R4), end-to-end persistence of an authored position (R5), stored positions surviving re-layout (R6), determinism (R7), and lay-out-then-filter (R10). A mode is not a licence to break any of them; where a mode makes one harder to satisfy, that is a defect in the mode.

Requirement 7 below deliberately re-opens one recorded decision, and says so in the open rather than quietly.

## Alignment with Product Vision

* **Files are the source of truth.** The chosen mode is a property of the diagram, not of the session or the browser, so it is stored in the file pair and travels with the repository — reviewable as a diff like every other artifact.
* **Don't reinvent, integrate.** Horizontal and Vertical are the rank direction that established graph-drawing tools have exposed for decades; this adopts the convention rather than inventing a vocabulary.
* **Value from day one.** The mode is a per-diagram choice with a working default, so a repository that never touches it behaves exactly as it does today.

## Requirements

### Requirement 1 — Four named modes, in the user's words

**User Story:** As a reader, I want to choose how a diagram is laid out from a small set of clearly named options, so that I can make a diagram readable without editing it.

#### Acceptance Criteria

1. WHEN a diagram offers a layout choice THEN the options SHALL be presented as **Manual layout**, **Automatic layout / Horizontal**, **Automatic layout / Vertical** and **Automatic layout / Cloud**, in that vocabulary.
2. WHEN the modes are represented in the file, the wire or the code THEN the four SHALL be a closed set, so a fifth cannot be introduced without amending this specification.
3. WHEN a mode is offered THEN the grouping SHALL read as the user stated it: Manual on one side, three variants of Automatic on the other, rather than four flat peers.
4. WHERE a module supports fewer than four modes THEN the unsupported ones SHALL be visibly unavailable with the reason rather than silently absent, per the repository's standing rule that a gesture that cannot apply says why.

### Requirement 2 — A module declares which modes it can honour

**User Story:** As a diagram type author, I want to state which layout modes my notation can support, so that a mode that would destroy my diagram's meaning is never offered for it.

#### Acceptance Criteria

1. WHEN a module is registered THEN it SHALL declare which of the four modes it supports and which single mode is its default.
2. WHEN a module's positions are **authored in the document** rather than computed — wardley-map's coordinates being the author's claim about evolution and value — THEN it SHALL declare Manual only, and the three automatic modes SHALL be unavailable with the reason that rearranging the elements would rewrite the author's assertion rather than re-draw it.
3. WHEN a module's notation fixes the meaning of an axis — timeline's time axis — THEN it SHALL NOT declare the modes that would rotate or dissolve that axis, and the reason SHALL name the meaning that would be lost.
4. WHERE a module's layout is a notation in its own right rather than a direction — mindmap's two-sided branch arrangement — THEN it MAY declare a single automatic mode that its own layout satisfies, rather than being forced to implement three.
5. WHEN a module declares a mode THEN it SHALL be answerable for that mode against the inherited obligations, so declaring Horizontal is a claim that the horizontal layout places every element, does not overlap and is deterministic.
6. IF a module declares no automatic mode at all THEN Manual SHALL still be offered, because every diagram a user can see is a diagram a user may want to arrange.

### Requirement 3 — Manual layout

**User Story:** As someone who has arranged a diagram by hand, I want the diagram to stay exactly as I left it, so that my arrangement is not undone by the tool deciding it knows better.

#### Acceptance Criteria

1. WHEN a diagram is in Manual layout THEN the module's computed positions SHALL serve as the starting arrangement, and authored positions SHALL override them element by element, per the overlay semantics `diagram-layout` R6 already governs.
2. WHEN a diagram is in Manual layout THEN nothing SHALL re-arrange it on its own — not opening it, not a view report, not an unrelated change elsewhere in the document.
3. WHEN an element is added to the document while a diagram is in Manual layout THEN it SHALL be placed by the module's computed layout and SHALL NOT displace an authored position.
4. WHEN a diagram is in Manual layout and its module has no persistence THEN the arrangement SHALL be refused with the reason rather than accepted and lost — the four modules with a canvas and no persistence (azure-pipeline, dependency-graph, mindmap, timeline) are the case this covers, and `diagram-layout` R5 owns closing it.
5. WHEN a file is open without a registration THEN Manual SHALL be unavailable with the existing reason that there is nowhere to store a position, and registering the file SHALL lift that.

### Requirement 4 — Automatic layout, and what it does to an authored arrangement

**User Story:** As a reader switching to an automatic mode, I want to know what happens to the positions I authored, so that I am not surprised by losing work I spent time on.

#### Acceptance Criteria

1. WHEN a diagram is in an automatic mode THEN the module's computed positions for that mode SHALL be what is drawn, and authored positions SHALL NOT override them.
2. WHEN a diagram is switched from Manual to an automatic mode THEN authored positions SHALL be **retained, not discarded**, so that switching back to Manual restores the arrangement exactly.
3. WHEN a diagram is switched to an automatic mode THEN the switch SHALL be undoable as one action, and undoing it SHALL return the registration byte for byte.
4. WHEN a diagram is in an automatic mode THEN repositioning an element by hand SHALL either be refused with a reason naming the active mode, or switch the diagram to Manual as an explicit, undoable act — and whichever is chosen SHALL be the same across every module.
5. WHEN a document changes while an automatic mode is in force THEN the diagram SHALL be re-laid out, and the result SHALL be the same as opening the changed document fresh in that mode.

### Requirement 5 — Horizontal and Vertical are one layout under two directions

**User Story:** As a reader of a diagram that is unreadably wide, I want to lay it out the other way round, so that its shape fits the screen I am looking at.

#### Acceptance Criteria

1. WHEN a module supports both Horizontal and Vertical THEN the two SHALL differ in the direction the layout's primary axis runs — left-to-right against top-to-bottom — and SHALL NOT differ in which elements are placed or how they are grouped.
2. WHEN a document is laid out in Horizontal and then in Vertical THEN the ordering the notation asserts SHALL be preserved in both, so that a subclass depth or a broader-to-narrower relation still reads in the direction the notation means, along whichever axis is active.
3. WHEN a module whose current layout already runs in one of the two directions adopts this requirement THEN that layout SHALL become the corresponding mode rather than being rewritten — helm-charts' five left-to-right columns are Horizontal, and skos' descending layers are Vertical.
4. WHEN either direction is applied to the measured corpus THEN at least one of the two SHALL bring each measured document inside `diagram-layout` R2's ratio bound, because a mode that leaves a 142:1 ribbon a ribbon has not answered the problem it exists for.
5. WHERE a module supports only one of the two THEN the other SHALL be unavailable with the reason, rather than offered and silently identical.

### Requirement 6 — The chosen mode is stored with the diagram

**User Story:** As a user, I want the layout mode I chose to still be in force tomorrow and on my colleague's machine, so that the choice is part of the diagram rather than of my session.

#### Acceptance Criteria

1. WHEN a mode is chosen THEN it SHALL be persisted in the `.adp` registration file, beside the `layout:` block rather than inside it, because a mode is one value for the diagram and the block is a list of per-element positions.
2. WHEN a mode is written THEN everything else in the `.adp` SHALL be preserved byte for byte, per the file-pair contract `diagram-layout` R4.4 states.
3. WHEN a diagram is opened THEN the stored mode SHALL be in force, and a diagram with no stored mode SHALL open in its module's declared default.
4. WHEN a stored mode names one the module does not support — a mode removed, or a registration copied between diagram types — THEN the module's default SHALL be used, the diagram SHALL open rather than fail, and the mismatch SHALL be reported as a finding on the file rather than silently corrected.
5. WHEN a mode is stored THEN it SHALL be readable and editable as text by a human, since the `.adp` is a reviewable artifact like every other file in the repository.
6. WHEN two clients have the same diagram open and one changes the mode THEN the other SHALL follow, through the same delta stream every other change travels on.

### Requirement 7 — Cloud, and the force-directed ruling it must not break

**User Story:** As a reader of a graph with no meaningful ranking, I want an arrangement that spreads it out organically rather than forcing it into rows, so that its structure is visible as structure.

#### Acceptance Criteria

1. WHEN Cloud is applied THEN it SHALL produce an **organic arrangement with no dominant axis** — related elements drawn near one another and unrelated ones apart, as an organic or force-directed arrangement does — for documents whose relations imply no rank that Horizontal or Vertical could express.
2. WHEN Cloud lays out a document THEN it SHALL be a **pure, deterministic function of that document**: a fixed iteration count, any seed derived from a stable ordering the document itself supplies, no wall-clock, no random source, and no settling animation whose stopping point depends on timing.
3. WHEN the same document is laid out in Cloud twice, in different processes THEN the positions SHALL be identical.
4. WHEN Cloud is specified THEN it SHALL be understood to **re-open a recorded decision**, and the reasoning SHALL stand or the mode SHALL not ship: `rdf-diagram` R4.4 requires layout to be "a pure, deterministic function — no physics, no randomness", and `diagram-layout` records "no force-directed physics" as a non-goal. Both give the same reason — that non-deterministic layouts break the same-file-opens-the-same-way rule and the `.adp` position overlay. Criteria 2 and 3 are written to satisfy that reason rather than to argue with it: a deterministic Cloud opens the same way every time and overlays authored positions no differently than any other mode.
5. IF a module's family maintains the rejection on grounds beyond determinism THEN that module SHALL simply not declare Cloud, per Requirement 2, rather than the mode being weakened for everyone.
6. WHEN Cloud is applied THEN it SHALL satisfy the inherited obligations without exception — every element placed and distinguishable from unplaced (`diagram-layout` R1), no overlapping boxes (R3), and the extent ratio (R2) — because an organic arrangement is the mode most likely to violate all three and least likely to be noticed doing it.

### Requirement 8 — A mode change is a layout change and nothing else

**User Story:** As a user, I want changing how a diagram is arranged to leave what it says untouched, so that choosing a view is never an edit to my content.

#### Acceptance Criteria

1. WHEN a mode is changed THEN the diagram's body file SHALL NOT be written, for any module, in any mode.
2. WHEN a mode is changed THEN the set of elements and relations drawn SHALL be unchanged; only where they sit SHALL differ.
3. WHEN a mode is changed THEN the whole document SHALL be laid out and the viewport SHALL filter the result, per `diagram-layout` R10 — a mode switch SHALL NOT become an occasion to lay out only what is currently visible.
4. WHEN a mode is changed THEN what the reader is looking at SHALL be preserved as far as the new arrangement allows, so that the diagram does not silently jump to a corner and leave the reader lost.
5. WHEN a mode is changed on a truncated diagram THEN the drawn-element budget SHALL apply to the new arrangement exactly as it applied to the old, and the banner SHALL continue to state the real totals.

## Non-goals

* **No new layout algorithms for their own sake.** This specification makes direction and arrangement selectable; the quality of what each module computes is `diagram-layout`'s subject.
* **No per-element mode.** The mode is a property of the diagram, not of a selection within it.
* **No user-defined modes.** The set is the four the user named, closed by Requirement 1.2.
* **No re-litigation of where positions are stored.** The `.adp` `layout:` block is settled by tech.md and `diagram-layout` R4; this adds one sibling value to the same file and nothing else.
* **No animation between modes.** How a switch is presented is a client concern, and a settling animation is explicitly ruled out for Cloud by Requirement 7.2 regardless.
* **No change to the view-delta loop.** Modes are laid out whole and filtered afterwards, exactly as today.
* **Stub modules are out of scope.** Only modules with an implemented canvas are addressed.
