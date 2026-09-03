# Requirements Document

## Introduction

This spec adds the **Dependency Graph** diagram type: named nodes and directed *depends-on* edges, freely placed and freely connected. It is deliberately specified as a **duplicate of the timeline diagram with every time-related aspect removed** — the timeline already solved authored placement, snap-to rows, bezier connections, drag with live curves, toolbox drops, context-menu removal, keyboard adds, pan/zoom and edge scrollbars, and this type wants all of that with one difference: the horizontal axis means nothing.

The catalog row for this type already exists (`docs/diagrams.md`, `generic/dependencies`, currently 💡 identified) and states the same intent: it follows the `generic/timeline` precedent — no standards body owns "a box depends on a box", so ADP defines the schema and owns the extension outright, keeping the document diffable text.

**What "remove all time-related aspects" means, concretely**, measured against the timeline module as it stands:

* Elements lose `begin`/`end` dates. A node is a labelled box at an **authored position**, not a span between two moments.
* The **ruler goes** — its whole job is naming times, and there are none. Nothing replaces it; the canvas edge is just the canvas edge.
* Zoom loses its **time units**: no seconds-per-pixel, no month/day/year tick logic. Zoom and pan remain, as plain geometry.
* **Moments go** — a point-in-time marker has no meaning here. The toolbox offers the node, and whatever relation affordances the timeline's anchors already offer.
* Drag hints lose their dates: the hint that read `2026-01-31 · row 1` shows the position in this type's own terms.

**What stays**, because it was never about time: snap-to rows as vertical structure, bezier connections, the whole interaction set (drag with live curve updates and Escape-to-abandon, anchor-drag creating a connected node on empty canvas, toolbox drops landing where dropped, context-menu removal with the relation-count confirmation, Tab/Enter keyboard adds, right-drag pan, the edge scrollbars), one command per edit on the project history, the property grid, validation through the problems panel, and the round-trip discipline that leaves untouched lines untouched.

**One semantic difference beyond deletion**: the timeline's connections are deliberately arbitrary ("connections are arbitrary rather than scheduling dependencies", per its catalog row). This type's edges *mean something*: **A depends on B**, directed, drawn with an arrowhead. That meaning is the type's reason to exist.

**Dependencies.** This spec redefines none of them:

* [`timeline-diagram`](../timeline-diagram/requirements.md) — the module being duplicated; its requirements apply here wholesale except where this document deletes or overrides them.
* structure.md — the `diagrams/<diagram>/` module layout and the example-replication rule.
* tech.md's **Commands** rule and [`diagram-undo-redo`](../diagram-undo-redo/requirements.md) — every edit is one undo away.
* [`errors-and-warnings-panel`](../errors-and-warnings-panel/requirements.md) — where this type's validation findings land.

## Alignment with Product Vision

Dependency graphs are the most requested "just boxes and arrows" notation in architecture work — build orders, service dependencies, team topologies. Shipping it as a disciplined duplicate of an existing, tested module demonstrates the product's own plugin claim: a new diagram type is a new module, not a change to core.

## Requirements

### Requirement 1 — A module of its own, shaped like the timeline's

**User Story:** As a maintainer, I want the Dependency Graph to be a full module beside the others, so that it costs core nothing and inherits the family conventions.

#### Acceptance Criteria

1. WHEN the module is organised THEN it SHALL live at `src/diagrams/dependency-graph/` with the standard `backend/` (`EtAlii.Adp.Diagram.DependencyGraph` + `.Tests`), `client/`, and `examples/` folders, registered and discovered exactly as every other diagram module is.
2. WHEN the type introduces itself THEN its origin SHALL be `generic/dependencies` with the display name **Dependency Graph**, and it SHALL declare its own document extension (proposed: `.dgr`; the design decides and the catalog row records it).
3. WHEN this spec is implemented THEN the `docs/diagrams.md` row for `generic/dependencies` SHALL move through its states (📝 specified now, then ⏸️/🛠️/✅ as work proceeds) with the spec linked, per the repository's catalog rule.
4. WHEN code is duplicated from the timeline module THEN the duplication SHALL be acknowledged as the chosen approach — a fork, not a shared abstraction invented prematurely — but anything the two modules can share through an existing seam (shell components, the family's base classes) SHALL be shared rather than copied. Copying the timeline's *files* and deleting the time out of them is the expected implementation shape.

### Requirement 2 — The document: ADP's own YAML, no dates in it

**User Story:** As an architect, I want the graph stored as small diffable YAML, so that reviews and merges treat it like any other source file.

#### Acceptance Criteria

1. WHEN a document is written THEN it SHALL be YAML in the timeline's mould — a version marker (`dependencies: 1`), an `elements` list and a `relations` list — where an element has an `id`, a `label`, and an **authored position** (its horizontal coordinate and its row), and carries no date, duration, or time field of any kind.
2. WHEN a relation is written THEN it SHALL name the dependent and the dependency (`from` depends on `to`, or equivalently named fields the design settles), and the direction SHALL be part of the document, not a drawing convention.
3. WHEN a document is opened and saved without changes THEN it SHALL round-trip byte-identically, and WHEN one element is edited THEN only that element's lines SHALL change — the timeline's own discipline, kept.
4. WHEN a document names a relation endpoint that does not exist THEN validation SHALL report it through the problems panel, at the offending line.

### Requirement 3 — The canvas: the timeline minus time

**User Story:** As a user, I want the graph canvas to feel exactly like the timeline canvas I already know, so that nothing has to be relearned.

#### Acceptance Criteria

1. WHEN a graph is open THEN nodes SHALL render as the timeline's elements do (labelled boxes on snap-to rows) at their authored positions, and edges SHALL render as the timeline's bezier connections do, **plus a directional arrowhead on the dependency end**.
2. WHEN the canvas is used THEN the timeline's whole interaction set SHALL work unchanged: dragging a node moves it with its curves following live and Escape abandoning the drag byte-cleanly; dragging from an anchor onto empty canvas creates a connected node at the release point as one history entry; toolbox drops land where dropped with no dialog; the context menu removes nodes and relations with the relation-count confirmation; Tab and Enter add siblings; right-drag pans; the edge scrollbars pan.
3. WHEN the canvas shows no ruler, no time units on zoom, and no Moment anywhere THEN that is correct — a ruler, a date in a drag hint, or a moment marker appearing anywhere in this type is a defect.
4. WHEN a drag hint or a property row would have shown a date THEN it SHALL show the node's position in this type's own terms instead.
5. WHEN the property grid describes a node THEN it SHALL offer the label for editing and the position values per the timeline's property conventions, with time-derived rows (begin, end, duration) absent rather than blanked.

### Requirement 4 — Examples and replication

**User Story:** As someone evaluating the type, I want a shipped example that shows a believable dependency graph.

#### Acceptance Criteria

1. WHEN the module ships THEN `src/diagrams/dependency-graph/examples/` SHALL hold at least one example on a believable theme (a service or build dependency graph), with real names — consistent with the `small-refinements` spec's no-placeholder rule — replicated into `src/examples/` per structure.md, and opened by the module's own tests so it cannot drift from what the code supports.

## Non-Functional Requirements

### Code Architecture and Modularity
- The module touches no shared proto and opens no stream of its own; it rides the same wire every diagram type rides.
- Core learns nothing new: discovery, routing, validation, context actions and the property grid all reach this type through the existing seams.

### Performance
- No new obligations beyond the timeline's own: the canvas it is copied from already handles its content sizes, and this type removes work (ticks, unit math) rather than adding it.

### Reliability
- The timeline module SHALL be left completely untouched by the duplication — its tests, examples and behaviour identical before and after. A shared seam extracted under Requirement 1.4 counts as touching it and must keep its tests green.

### Usability
- The visual checks that guarded the timeline's interactions (its `tests.md` entries) SHALL exist for this type too, rewritten in its own terms, so the manual pass covers the graph the way it covered the timeline.
