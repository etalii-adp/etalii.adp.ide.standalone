# Requirements Document

## Introduction

**The user's words:** *"take the larger examples and apply better layouting to the elements — i.e. where the diagram meta layout information can be stored in the .adp file."*

That is two requests joined by an "i.e.", and they are genuinely two. The first is about **quality**: the large vendored documents lay out badly today, and this document has to say what "badly" means in numbers before it can ask for better. The second is about **persistence**: where a position lives once it exists — computed or authored by hand.

They are joined because they fail together. A layout good enough to keep is one worth storing; a layout nobody can read is one every reader will rearrange, and rearranging is worthless if it does not survive reopening.

**Eleven requirements follow.** Requirements 1 to 3 are quality, 4 to 6 persistence, 7 and 10 stability, and 8, 9 and 11 the surrounding obligations the two halves turned out to share.

### What the large examples actually do today

Not an impression — measured, by running each module's own layout over the vendored corpus and reporting the extent of the positions it returned. An overlapping pair is two elements whose centres are closer than one card in both axes.

| Example | Lines | Placed | Extent | Aspect | Overlapping pairs |
| --- | --- | --- | --- | --- | --- |
| `skos/stw/business-economics.ttl` | 11,022 | 1,000 (budget-capped) | 117,600 × 830 | **142 : 1** | 1 |
| `rdf/nobel/laureates.ttl` | 8,260 | 1,000 (budget-capped) | 840 × 40,320 | **1 : 48** | 0 |
| `skos/stw/geographic-names.ttl` | 3,656 | 436 (complete) | 39,600 × 720 | **55 : 1** | 1 |
| `owl/owl-time.ttl` | 1,785 | 114 | 2,120 × 6,382 | 1 : 3 | 0 |
| `owl/prov-o.ttl` | 1,321 | 42 | 1,590 × 1,920 | 1 : 1.2 | 3 |

**The dominant defect is not overlap. It is shape.** Every one of these layouts grows without bound along one axis. At the 1,200-unit surface width the canvases assume, `business-economics` is **98 screens wide and two-thirds of one screen tall**; `laureates` is a thread **67 screens tall**. A reader who opens either sees a handful of cards and an ocean, with no indication that the rest exists sideways. Panning across 98 screens to read a thesaurus is not a layout — it is a filing cabinet with the drawers removed.

This is a layout defect that the recently landed [view-delta-adoption](../view-delta-adoption/requirements.md) turns from ugly into expensive: now that viewports genuinely cull, an extent 98 screens wide means almost every element is culled almost always, and the loop spends its deltas shuttling a ribbon past a window.

**The small examples are where overlap lives instead.** `prov-o` — the smallest measured, 42 elements — has three overlapping pairs, while the 1,000-element documents have at most one. Density is not the cause of overlap; the packing rule is. A requirement that only addressed large documents would leave the defect that is easiest to see.

**These are measurements of computed positions, not of appearance.** No one has ever opened either STW document in the running app — the three skos entries in `tests.md` are written and unexecuted, confirmed by the agent who authored and implemented skos-diagram. So this document says what the layouts *compute* and deliberately claims nothing about how they *look*. Somebody rendering them may find worse; they will not find better.

### What the corpus does not cover

Vendored examples test input shapes their author never had in mind, which is the point of them — but a requirement written against a corpus inherits that corpus's blind spots, and these are stated so a later reader meets them before assuming otherwise. Counted from the documents by the agent who vendored them.

| | geographic-names | business-economics |
| --- | --- | --- |
| Concepts | 435 | 1,150 |
| `skos:broader` objects | 712 | 1,905 |
| Concepts with more than one broader | 272 | 588 |
| `skos:related` | 144 | 725 |
| Top concepts | 1 | 1 |

* **Both extracts are single-rooted by construction.** Neither demonstrates a forest, several top concepts, or disconnected components — and those are exactly the shapes a balancing rule has to handle. Requirement 2 is therefore evidenced on single-rooted documents and rests on fixtures for the rest.
* **Polyhierarchy is the rule, not the exception**: 272 of 435 and 588 of 1,150 concepts have more than one broader. skos places such a concept once, below its deepest broader, so long edges spanning many layers are the expected pressure on any layering change.
* **725 `skos:related` links on `business-economics` are non-hierarchical and no layering rule places them at all.** That is Requirement 1's case with real data rather than a fixture: cross-links a layout has nowhere to put.
* **No `skos:Collection` anywhere in STW, and labels are completely bilingual** — so collection grouping and incomplete translation have fixture evidence only.

### Where layout is stored today: two mechanisms, and a rule that already picks one

[tech.md](../../steering/tech.md) settles the question in one line: *"When a diagram's file format carries no layout information but authored layout makes sense for the type, the layout is stored in the `.adp` registration file — as metadata enriching the body, never written into the body itself."*

There are nevertheless two implementations.

| Mechanism | Where | Used by | Shape |
| --- | --- | --- | --- |
| The `.adp` `layout:` block | `RegistrationLayout.cs` in core, with `SetRegistrationLayoutCommandHandler` and a byte-restoring inverse | ansible-structure, databricks, helm-charts, rdf (4 definitions), sparql | `layout:` then indented `<element-id>: <x> <y>` |
| A `.layout.json` sidecar | `C4LayoutSidecar.cs`, module-private | c4 only | JSON beside the body, keyed by view then element |

**The sidecar's stated reason no longer holds, and this is checkable rather than arguable.** `C4LayoutSidecar` explains itself as *"per model document, keyed by view: positions belong to a view, and a view belongs to a document, so one file beside the `.dsl` holds them all"* — the `.adp` block being a flat id-to-position map with no view dimension. But **c4 already registers one `.adp` per view**: `bottling-mes.adp` carries `view: SystemContext`, `bottling-mes.mes-containers.adp` carries `view: Containers`, and so on, four registrations over one `.dsl`. The view dimension the sidecar was built to supply is already carried by the file the block would live in. One `.adp` per view means one flat block per view, which is exactly what the block does.

So the answer to "what happens to the sidecar" is **it converges onto the `.adp` block**, and Requirement 4 says so explicitly rather than leaving it implied. No third mechanism is invented.

### The finding that ties both halves together

Four modules were caught this week emitting elements at coordinates no layout ever assigned — wardley's links and axis, databricks' edges across all three readings, sparql's edges and unplaced nodes and regions, rdf's registration header. Each was found while adopting view deltas and each was fixed as a filtering defect. **They are not filtering defects. They are layout defects that filtering made visible.**

The mechanism is one line, repeated at seven sites across databricks, the four rdf definitions and sparql:

`positions.TryGetValue(id, out var position) ? position : default`

`default` is `RegistrationPosition(0, 0)` — the origin. An element the layout never placed is therefore handed to the mapper wearing a coordinate that means "top-left corner", and nothing downstream can tell it from an element genuinely placed there. Before viewports culled, this was invisible: everything was sent anyway and a misplaced card in the corner looked like a cosmetic bug. Now it decides whether the element is ever sent at all.

**And a genuine origin element always exists.** In every one of the five measured documents, exactly one element sits at exactly `(0, 0)` — by design, because layouts start at the origin. So the ambiguity is not theoretical: the collision is present in every diagram in the corpus. A layout that cannot place something must be able to say so.

## Alignment with Product Vision

* [product.md](../../steering/product.md)'s **"Value from day one"** — a reader who opens an 11,000-line thesaurus should meet a diagram, not a 98-screen ribbon.
* product.md's **"Live, pushed updates"** — layout stability is now load-bearing for correctness, not just for comfort: an unstable layout makes every edit re-send the document.
* [tech.md](../../steering/tech.md)'s **layout-in-`.adp` rule** — this specification applies the existing rule to the one module that predates it, rather than renegotiating it.
* [structure.md](../../steering/structure.md)'s **core-vs-module boundary** — what is genuinely identical across layouts moves to shared machinery; what encodes a notation's meaning stays in its module.

## Requirements

### Requirement 1 — A layout that cannot place an element says so

**User Story:** As a reader, I want an element the layout could not place to be reported rather than silently parked in the top-left corner, so that a defect shows up as a defect instead of as an element that disappears when I pan.

#### Acceptance Criteria

1. WHEN a module's element mapper needs a position for an element the layout did not place THEN it SHALL be able to distinguish that absence from a position of `(0, 0)`, rather than receiving the origin as a fallback value.
2. WHEN an element has no computed position THEN the module SHALL either place it deliberately at a stated fallback that is documented as a fallback, or exclude it from the drawn set — and in both cases the choice SHALL be visible in code rather than emergent from `default`.
3. WHERE a layout returns positions for a set of elements THEN a test SHALL assert that every drawn element has a position, so a newly added element kind cannot silently acquire the origin.
4. WHEN this requirement is verified THEN the seven `? position : default` sites in databricks, the four rdf definitions and sparql SHALL each be addressed, and any site keeping the origin SHALL state why in a comment at the site.
5. IF an element is genuinely placed at `(0, 0)` by the layout THEN it SHALL remain distinguishable from an unplaced one, because at least one such element exists in every document measured.

### Requirement 2 — A large document lays out into a readable shape

**User Story:** As a reader opening a large document, I want its diagram to occupy a shape I can navigate, so that the content is discoverable by looking rather than by panning blindly along one axis.

#### Acceptance Criteria

1. WHEN a module whose layout is freely computed lays out a document THEN neither dimension of the resulting extent SHALL exceed **four times** the other.
2. WHERE a diagram type's notation fixes the meaning of an axis — timeline's time axis, wardley-map's evolution and value axes — THEN this requirement SHALL NOT apply, because a long timeline is legitimately a ribbon and compressing it would destroy the meaning the axis carries.
3. WHEN the vendored corpus is laid out THEN each of the five measured examples SHALL satisfy criterion 1, replacing the measured 142:1, 1:48 and 55:1 with ratios inside the bound.
4. WHEN a layout is changed to satisfy this requirement THEN the change SHALL preserve the ordering the notation asserts — subclass depth still reads left-to-right in owl, broader-to-narrower still reads outward in skos — so that balancing the shape does not cost the meaning of a position.
5. IF a module cannot satisfy criterion 1 without losing meaning AND is not covered by criterion 2 THEN it SHALL record the reason in its layout class, and that reason SHALL name the property that would be lost.

### Requirement 3 — Drawn elements do not overlap

**User Story:** As a reader, I want to be able to read every card, so that no element is hidden behind another.

#### Acceptance Criteria

1. WHEN a module lays out any document THEN no two drawn elements' bounding boxes SHALL intersect.
2. WHEN an element's size depends on its content — a card with a dozen annotation rows, a long label — THEN the layout SHALL reserve that element's own measured size rather than a fixed row height.
3. WHEN the vendored corpus is laid out THEN the overlapping-pair count SHALL be zero for every example, replacing the measured 3 for `prov-o` and 1 each for the two STW documents.
4. WHERE overlap is verified in a test THEN the test SHALL measure against real vendored documents and not only against hand-written fixtures, because the three-pair case appears in the smallest real document and in none of the synthetic ones.

### Requirement 4 — One place where layout is stored

**User Story:** As someone who arranges a diagram by hand, I want my arrangement stored in one predictable place regardless of which diagram type I am looking at, so that I can find it, diff it and move it with the project.

#### Acceptance Criteria

1. WHEN a module persists authored positions THEN it SHALL use the `.adp` registration file's `layout:` block, per tech.md's rule.
2. WHEN c4 persists authored positions THEN it SHALL use the same block, and `C4LayoutSidecar` SHALL be retired rather than kept in parallel — the per-view keying it exists to provide is already supplied by c4's one-`.adp`-per-view registration.
3. WHEN an existing `.layout.json` sidecar is found beside a body THEN its positions SHALL be migrated into the corresponding registrations' blocks, and the reader SHALL NOT lose an arrangement to the change.
4. WHEN a layout block is written THEN everything above it in the `.adp` SHALL be preserved byte for byte, because the MIME line and the headers belong to the file-pair contract.
5. IF a diagram type's body format carries layout natively — wardley-map's coordinates are the author's claim, stated in the document — THEN nothing SHALL be stored in the `.adp`, and moving an element SHALL remain an edit of the body.
6. WHEN this specification is complete THEN exactly one layout persistence mechanism SHALL exist in the tree, and no third mechanism SHALL be introduced.

### Requirement 5 — A module that computes positions can persist an authored one, end to end

**User Story:** As a reader who has dragged an element to where it belongs, I want the position to be saved, so that reopening the diagram does not undo my work.

#### Acceptance Criteria

1. WHEN a module supports dragging an element THEN the drag SHALL reach the backend's layout write, and the wiring SHALL be complete on both sides rather than present on one.
2. WHEN a backend session implements the layout read and the layout write THEN its client hook SHALL send the command — `OwlSession` implements both and `useOwlStream.ts` sends neither, so owl's drags are discarded today.
3. WHEN a module deliberately does not persist drags THEN it SHALL state the reason at the seam, in the way modules already state why they decline other mechanisms.
4. WHEN a drag is persisted and the diagram is reopened THEN the element SHALL be where the reader put it.
5. WHERE a module has a canvas but no persistence at all — azure-pipeline, dependency-graph, mindmap, timeline — THEN this specification SHALL record for each whether persistence is wanted or deliberately absent, so that the gap is a decision rather than an omission.

### Requirement 6 — Stored positions survive re-layout, and stale ones do no harm

**User Story:** As someone whose document changes over time, I want my arrangement to keep working as the document grows, so that adding one element does not discard the positions of the others.

#### Acceptance Criteria

1. WHEN stored positions are applied to computed ones THEN they SHALL be overlaid element by element, so an element with no stored position keeps its computed place.
2. WHEN a stored position names an element that no longer exists THEN it SHALL be ignored rather than being an error.
3. WHEN a stored position names an element whose identity is not stable across edits — an rdf structural expression id, for example — THEN it SHALL NOT be applied, and the boundary SHALL be stated where the identity is minted.
4. WHEN a `layout:` block is malformed by hand-editing THEN the diagram SHALL still open, because the block is metadata and never a precondition.

### Requirement 7 — A layout is deterministic for a given document

**User Story:** As a reader of a live diagram, I want an edit to move only what changed, so that editing one label does not reshuffle the diagram under me.

#### Acceptance Criteria

1. WHEN the same document is laid out twice THEN every element SHALL receive the same position both times.
2. WHEN a document changes in a way that does not affect an element's place in the notation THEN that element's position SHALL NOT change, so that the view-delta mechanism sends deltas for what changed and not for everything.
3. WHEN layout depends on iteration over a set or dictionary THEN the ordering SHALL be made explicit, because insertion-order dependence is the usual source of a layout that is stable within a run and not across them.

### Requirement 8 — Truncation is visible to the reader

**User Story:** As a reader of a document larger than the drawn-element budget, I want to know that I am looking at part of it, so that I do not mistake a truncated diagram for a complete one.

#### Acceptance Criteria

1. WHEN a projection truncates a document to its drawn-element budget THEN the reader SHALL be told, with the shown count and the total.
2. WHERE a module has a budget THEN it SHALL surface truncation in the way the rdf family already does through its truncation payload, rather than each module inventing a presentation.
3. WHEN the budget is reported THEN the report SHALL name the budget's owner and value — `RdfProjection.DefaultBudget`, 1,000 — rather than a number repeated at the call site, and the **second copy of that number** in `ShaclProjection` SHALL be reconciled with it, because there are two constants for one budget today.
4. IF a module has no budget and no truncation THEN it SHALL state that the whole document is always drawn, so a reader can tell the two situations apart.
5. WHEN truncation is guarded by a test THEN the guard SHALL cover the skos reading — the only budget assertion in the tree, `TheLaureatesFile_ReallyExceedsTheBudget`, is pinned to `laureates.ttl` in the plain RDF reading, so **nothing currently asserts that `business-economics.ttl` exceeds the budget** even though it was extracted breadth-first to 1,150 concepts specifically to do so.

### Requirement 9 — Shared layering machinery, if the bar is met

**User Story:** As someone maintaining these modules, I want the arrangement logic that is genuinely the same to exist once, so that a fix to it is a fix everywhere.

#### Acceptance Criteria

1. WHEN this specification evaluates whether a shared layout library is warranted THEN it SHALL apply the [view-delta-adoption](../view-delta-adoption/requirements.md) bar — three or more consumers wanting *the same behaviour*, not three that merely resemble each other.
2. WHEN that bar is applied to **layering a hierarchy by depth with cycles collapsed** THEN the evidence SHALL be weighed as follows: ansible-structure, azure-pipeline, owl and skos each implement it separately, and owl and skos do so **inside the same module** — `OwlClassHierarchy.Depths` and `SkosLayout.BreakCycles` are two implementations of one idea, one folder apart.
3. WHERE the bar is met THEN the shared piece SHALL take a graph and return layers, and SHALL NOT know what the nodes mean; where it is not met, the resemblance SHALL be recorded and left alone.
4. WHEN a shared piece is introduced THEN it SHALL not force a module whose notation fixes an axis to adopt it.
5. WHEN the balancing rule of Requirement 2 is implemented THEN it SHALL be evaluated against this same bar, because it is a candidate with more consumers than the layering walk has.

### Requirement 10 — Lay out the whole document, then filter

**User Story:** As a reader panning across a large diagram, I want the arrangement to stay still while I move, so that the content does not repack under me as I look at it.

#### Acceptance Criteria

1. WHEN a module computes layout THEN it SHALL position every projected element regardless of the reported viewport, and culling SHALL happen after positioning and never before it.
2. WHEN a reader pans THEN no element's position SHALL change as a result, because a layout computed over the visible set repacks as the reader moves and is the reason an element panned to could otherwise have no position at all.
3. WHERE a projection and its layout are expensive THEN they SHALL be cached per session and dropped when the document changes, because a pan is not a document change.
4. WHEN this obligation is stated THEN it SHALL be the one already written in [creating-a-diagram-module.md](../../../docs/creating-a-diagram-module.md) under *"The view-delta loop"*, not a second wording of it — that document is the working rule, and this requirement makes it a layout obligation rather than a filter obligation.

### Requirement 11 — A layout exposes each element's box, and downstream uses it

**User Story:** As someone writing the code that decides what is on screen, I want the real size of each element, so that I do not cull something whose lower half the reader is still looking at.

#### Acceptance Criteria

1. WHERE a layout knows an element's measured size THEN it SHALL expose it, rather than leaving downstream code to assume a nominal cell.
2. WHEN downstream code judges an element against a viewport THEN it SHALL use the element's box and not its corner — owl judged nodes against a nominal 240x320 cell while `OwlLayout.SizeOf` already knew each real box, which would have culled the ontology header while the reader was still looking at its lower half.
3. WHEN an element spans a region — a frame, a band, a swimlane — THEN it SHALL be judged on the rectangle it spans.
4. IF a module has no shared notion of element size THEN that SHALL be recorded rather than silently re-derived per canvas; `ViewBox` is currently declared identically in six module canvases, which is evidence for a canvas-level concern this specification does not itself own.

## Non-goals

* **No new layout notation.** This specification improves the arrangement of what modules already draw; it does not add element kinds, chrome or visual vocabulary.
* **No force-directed physics.** The rdf family rejected it explicitly and that ruling stands; balancing an extent is not a licence to reintroduce it.
* **No change to the view-delta mechanism.** Layout stability serves it; the loop itself is settled by its own specification.
* **No re-litigation of the layout-in-`.adp` rule.** tech.md already decided it. This specification applies it to the one module that predates it.
* **Stub modules are out of scope.** Only the modules with implemented layouts are addressed: ansible-structure, azure-pipeline, c4, databricks, dependency-graph, helm-charts, mindmap, rdf, sparql, timeline, wardley-map.
