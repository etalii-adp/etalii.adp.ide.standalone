# Requirements Document

## Introduction

The ansible-structure diagram (`ansible/structure`, ⚗️ prototype) draws a project's playbooks, plays, roles, task files, inventories and variable folders from the registered folder's own tree. Three refinements bring it level with its sibling canvases:

- **Positions**: elements cannot be moved today — the module has no reposition support at all. Authored positions must become changeable and be stored in the diagram's `.adp` registration file, through the `layout:` mechanism core already provides (built by databricks-diagrams, consumed by helm-charts, in use by rdf-diagram).
- **Styling**: the canvas must adopt the shared appearance the timeline, dependency graph and mindmap canvases share. Today the module does not import `@client/canvas/canvas.css`, and its own stylesheet leans on two theme tokens that are defined nowhere (`--color-surface-raised`, `--color-warning`) — so those rules silently fall back, themed right in one mode by accident — plus six literal per-play hex colours against the repository's no-literal-colours practice.
- **Scrollbars**: the canvas lacks the `CanvasScrollbars` overlay that timeline, dependency-graph, mindmap and databricks all wire up.

This spec covers exactly these refinements. The ansible module stays read-only with respect to the Ansible files themselves: the only thing it ever writes is the `layout:` block of its own `.adp`.

## Alignment with Product Vision

- **Files are the source of truth**: authored positions land in the `.adp` — a plain, diffable file reviewed with the code — never in a sidecar database, and never inside Ansible's own files, which belong to Ansible's tooling (product.md: don't reinvent, integrate).
- **A familiar surface**: one shared canvas appearance and one shared scrollbar overlay across diagram types is what makes the web client read as a single tool rather than a collection of five.
- **Live, pushed updates**: a layout write travels the same watcher route every other change does, so open views converge without manual refreshing.

## Requirements

### Requirement 1 — Elements can be repositioned

**User Story:** As a developer reading an Ansible project's structure, I want to drag elements to positions of my choosing, so that I can arrange the graph to match how I think about the project.

#### Acceptance Criteria

1. WHEN a user drags a node (playbook, play, role, task file, inventory, variable folder) to a new position and drops it THEN the system SHALL record that position as the element's authored position and redraw the element there.
2. WHEN a user drags an edge THEN the system SHALL refuse the move with a reason, as edges have no position of their own (the databricks/helm behaviour).
3. WHEN a reposition has been applied THEN the system SHALL offer exactly one undo step that restores the element's previous authored position — or its absence — byte-restoring the registration's prior layout entry (core `SetRegistrationLayoutCommand` semantics).
4. WHEN an element has an authored position THEN the system SHALL draw it there in preference to the computed layout position, leaving all unauthored elements at their computed positions (the `RegistrationLayout.Apply` overlay).
5. WHEN a reparenting move arrives (the tree-shaped `MoveElement` form) THEN the system SHALL keep refusing it: nothing in this diagram nests by dragging.

### Requirement 2 — Positions are stored in the .adp registration

**User Story:** As a team member reviewing changes, I want authored diagram positions stored in the `.adp` registration file, so that layout changes are diffable, revertible and travel with the repository.

#### Acceptance Criteria

1. WHEN a reposition is applied THEN the system SHALL write it to the `layout:` block of the diagram's `.adp` registration file through the core `RegistrationLayout`/`SetRegistrationLayoutCommand` mechanism, keyed by the element's id.
2. WHEN the layout block is written THEN the system SHALL preserve every byte above it (the MIME line and any headers), per core's existing contract.
3. WHEN a reposition is applied THEN the system SHALL NOT write to any file inside the registered Ansible folder: playbooks, roles, inventories and their kin are another ecosystem's files.
4. WHEN a diagram is closed and reopened THEN the system SHALL read the stored positions back from the `.adp` and overlay them again.
5. WHEN the `.adp` gains a layout write THEN the change SHALL reach every open session of the diagram as ordinary pushed deltas, through the same watcher path any external `.adp` edit takes.
6. IF the registered `.adp` is a bare one-line registration THEN the layout reader SHALL treat it as having no stored positions, and the first reposition SHALL create the block (verified against core by the helm-charts design; anything contradicting that verification is a finding to report, not to work around).

### Requirement 3 — Stored positions survive a changing folder gracefully

**User Story:** As a user whose Ansible project keeps evolving, I want stored positions to degrade gracefully when plays, roles or files change underneath the diagram, so that a stale layout entry never breaks or distorts the diagram.

#### Acceptance Criteria

1. WHEN the folder no longer yields an element for a stored id (a play removed, a role deleted, a file renamed) THEN the system SHALL ignore that stored position on read, and the next layout write SHALL drop it (core's stale-id behaviour, already tested there).
2. WHEN a file is renamed THEN its elements SHALL intentionally forfeit their stored positions: element ids are stable and content-derived (`playbook:<relative-path>`, `role:<name>`, `play:<relative-path>#<index>`, `taskfile:<relative-path>`, `inventory:<relative-path>`, `vars:<relative-path>`), and a new id is a new element (the helm-charts precedent).
3. IF plays are reordered within a playbook THEN their stored positions SHALL follow the play index, since a play's id is positional (`#<index>`); this is an accepted consequence of the id scheme and SHALL be documented in the design rather than compensated for.
4. WHEN a stored position exists for an element the current folder does produce THEN a change elsewhere in the folder SHALL NOT disturb it.

### Requirement 4 — The canvas adopts the shared appearance

**User Story:** As a user moving between diagram types, I want the ansible canvas to look and behave like the timeline, dependency graph and mindmap canvases, so that the client reads as one tool.

#### Acceptance Criteria

1. WHEN the ansible module registers its client THEN it SHALL import `@client/canvas/canvas.css` from its `register.ts`, the same way timeline, dependency-graph and databricks do.
2. WHEN the canvas renders chrome, elements and connections THEN it SHALL compose the shared `canvas-*` classes alongside its own class names, keeping module-specific styling (per-play tinting, kind-specific accents) in `ansible-structure.css` and removing every rule that duplicates what the shared stylesheet states.
3. WHEN this alignment lands THEN existing module tests that assert ansible-specific class names SHALL keep passing; classes may be composed, not renamed away.

### Requirement 5 — Colours come from the theme, not from thin air

**User Story:** As a user switching between light and dark mode, I want the ansible canvas themed correctly in both, so that it never renders on accidental fallback colours.

#### Acceptance Criteria

1. WHEN module styling needs a raised-surface tint THEN it SHALL use a `--color-surface-raised` token that is actually defined: this spec adds the definition centrally to `src/client/src/index.css` for both themes, since the central stylesheet itself already references the token and defines it nowhere — completing a half-built token, not inventing one.
2. WHEN module styling needs a warning colour THEN it SHALL use a `--color-warning` token defined centrally for both themes, beside `--color-danger` and `--color-success`: warning is a theme-level semantic, not an ansible concept.
3. WHEN plays are tinted apart THEN the six per-play hues SHALL be declared once as module-owned custom properties in `ansible-structure.css` (with light and dark values), applied via `color-mix` against the real themed surface — the databricks derivation shape — and SHALL NOT appear as literal hex values inside individual rules.
4. The per-play categorical palette SHALL remain module-owned rather than moving into the central theme: the theme names meanings (primary, danger, success, warning), and no second module consumes a categorical ramp today; by the rule of three, promotion happens when a third categorical consumer appears, and is not smuggled into this spec.
5. WHEN more plays exist than palette entries THEN tinting SHALL wrap around the palette, preserving today's behaviour.
6. WHEN both themes are inspected after this change THEN no rule in `ansible-structure.css` SHALL reference an undefined custom property, and no colour literal SHALL appear outside a custom-property definition.

### Requirement 6 — Scrollbars, as on the other canvases

**User Story:** As a user viewing a large Ansible project, I want scrollbars on the diagram, so that I can see where I am in the arranged content and reach the rest of it.

#### Acceptance Criteria

1. WHEN the diagram's content extends beyond the viewport THEN the canvas SHALL show the shared `CanvasScrollbars` overlay, wired with the shared scroll geometry exactly as the timeline, mindmap, dependency-graph and databricks canvases wire it.
2. WHEN the user drags a scrollbar thumb THEN the viewport SHALL pan accordingly, and WHEN the viewport pans or content changes THEN the thumbs SHALL track position and extent.
3. WHEN the content fits the viewport THEN the scrollbars SHALL not be shown, matching the shared component's behaviour elsewhere.

### Requirement 7 — Examples stay honest, and nothing more

The combined showcase tree currently holds fourteen ansible folders under `src/examples/diagrams/ansible-structure/` against a single module example (`example 1`) — a live imbalance the user is sorting out separately. This spec deliberately does not design around it.

#### Acceptance Criteria

1. WHEN this spec's changes land THEN the module's own `example 1` and its combined replica SHALL remain in sync per structure.md's replication rule; no new examples are added by this spec.
2. IF an implementation task repositions elements in a shipped example to demonstrate the layout block THEN it SHALL do so in the module's `example 1` and its replica only, leaving the other showcase folders untouched.

## Non-Functional Requirements

### Code Architecture and Modularity

- **No core changes expected**: `RegistrationLayout`, `SetRegistrationLayoutCommand`, `CanvasScrollbars` and `canvas.css` are consumed as they are. The folder-subject-meets-layout-block intersection was already verified in code by the helm-charts design; the two central token definitions (R5.1, R5.2) are the only intended edits outside the module, and anything further discovered is a finding to report before proceeding.
- **Module-local otherwise**: every other change stays inside `src/diagrams/ansible-structure/`.
- **Pattern reuse over invention**: the reposition path mirrors `DatabricksSession`/`HelmSession` (override the default-refused positional move, dispatch the core command, let the watcher bring the write back); the scrollbar wiring mirrors the timeline's.

### Performance

- A reposition writes one small `.adp`; the folder watcher's existing coalescing applies. No new watchers are introduced.

### Security

- No new inputs cross a trust boundary; the layout block's malformed-entry tolerance is core behaviour and stays as is.

### Reliability

- A hand-mangled layout line must never stop the diagram from opening (core guarantee, kept).
- Undo of a reposition restores the registration's previous bytes for that entry, including its absence.

### Usability

- Dragging, undo, theming and scrolling behave exactly as they do on the sibling canvases, so nothing new has to be learned per diagram type.
