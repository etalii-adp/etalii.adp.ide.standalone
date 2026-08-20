# Requirements Document

## Introduction

This spec covers the **Mindmap diagram module** — the first concrete diagram type built on top of EtAlii.Adp's pluggable diagram-type model (per [`adp-diagram-ide`](../adp-diagram-ide/requirements.md) Requirement 4.4). It defines how mindmaps are stored on disk, structured as a node tree, rendered and edited on the shared canvas, and registered with the core IDE without the core needing any mindmap-specific knowledge, per `structure.md`'s `diagrams/<diagram>` module convention (`backend/`, `api/`, `client/`).

Per [`tech.md`](../../steering/tech.md)'s "Testing & quality" section, Mindmap (`.mm`) is explicitly named as the diagram type used to validate the modular diagram architecture — this spec is that validation, not just a standalone feature. It depends on, but does not redefine, the core gRPC contract ([`grpc-core-communication-specification`](../grpc-core-communication-specification/requirements.md)) and its runtime behavior ([`grpc-core-communication`](../grpc-core-communication/requirements.md)), and it builds on the workspace shell, canvas, read-only mode, and undo/redo already defined by [`adp-diagram-ide`](../adp-diagram-ide/requirements.md).

## Alignment with Product Vision

This implements [product.md](../../steering/product.md)'s "Diagramming on files" key capability using Mindmap as the concrete, first diagram type, and its "Don't reinvent, integrate" principle by adopting the established FreeMind/FreePlane-compatible `.mm` XML format rather than inventing a new one. Requirement 6 (linking nodes to code artifacts) directly implements product.md's "Linkage over illustration" principle: mindmap nodes are not just boxes on a canvas, they can stay tied to the code they describe as that code evolves.

## Requirements

### Requirement 1 — Mindmap file format and storage

**User Story:** As a diagram author, I want my mindmaps stored as plain, diffable `.mm` files, so that I can version, review, and merge them like any other file in my repository.

#### Acceptance Criteria

1. WHEN a mindmap is created THEN the system SHALL persist it as a single `.mm` file using the established FreeMind/FreePlane-compatible XML mindmap format, per `tech.md`.
2. WHEN a `.mm` file is opened THEN the system SHALL parse it per that format, so mindmaps created or edited outside ADP (e.g., in FreeMind/FreePlane) remain interoperable.
3. WHEN a mindmap is saved THEN the system SHALL write it back with stable, minimal-diff serialization (consistent element ordering and attribute formatting) so incidental saves don't produce noisy diffs.
4. IF ADP-specific data has no place in the standard `.mm` schema (e.g., the code-artifact links in Requirement 6) THEN the system SHALL store it in an adjacent metadata file rather than extend or mutate the standard format, consistent with `structure.md`'s guidance on metadata files.
5. WHEN the backend loads or saves a `.mm` file THEN this SHALL happen entirely within the backend's existing file-access layer (per `adp-diagram-ide` Requirement 2); the mindmap module SHALL NOT introduce its own filesystem access path.

### Requirement 2 — Mindmap tree structure and canvas rendering

**User Story:** As a diagram author, I want to see my mindmap as a connected tree of nodes on the canvas, so that I can visually understand its structure at a glance.

#### Acceptance Criteria

1. WHEN a mindmap is opened THEN the system SHALL render exactly one root node plus its descendants, with each parent visually connected to its children.
2. WHEN the tree is rendered THEN the system SHALL apply automatic tree layout (no manually maintained x/y placement) so the structure stays legible as nodes are added or removed.
3. WHEN a mindmap is opened THEN the system SHALL render it on the shared pannable/zoomable canvas defined by `adp-diagram-ide` Requirement 4.1, rather than a mindmap-specific rendering surface.
4. IF a mindmap contains a large number of nodes THEN the system SHALL only render nodes currently in view, consistent with `adp-diagram-ide` Requirement 4.3's virtualization.

### Requirement 3 — Structuring nodes (add, move, delete)

**User Story:** As a diagram author, I want to add, move, and delete nodes, so that I can freely shape my mindmap as my thinking evolves.

#### Acceptance Criteria

1. WHEN the user adds a child node to an existing node THEN the system SHALL insert it as the last child of that node and persist the change.
2. WHEN the user adds a sibling node to an existing non-root node THEN the system SHALL insert it immediately after that node under the same parent and persist the change.
3. WHEN the user moves a node to a new parent THEN the system SHALL move that node's entire subtree with it and persist the change.
4. WHEN the user deletes a node THEN the system SHALL delete that node and its entire subtree, after the user confirms if the node has children.
5. IF the user attempts to delete or move the root node such that no root would remain THEN the system SHALL reject the action rather than leave the mindmap without a root.
6. WHEN a structural change (add, move, delete) is made THEN the system SHALL reflect it immediately on the canvas and push it to other connected clients, per `adp-diagram-ide` Requirement 3.2.

### Requirement 4 — Editing node content

**User Story:** As a diagram author, I want to edit a node's text and add optional notes, so that I can capture and refine ideas directly on the mindmap.

#### Acceptance Criteria

1. WHEN the user activates text editing on a node THEN the system SHALL let them edit that node's text inline on the canvas.
2. WHEN the user commits an edited node text (including an empty string) THEN the system SHALL persist the new text; an empty node text SHALL remain valid (a node is not required to have non-empty text).
3. WHEN the user adds or edits a node's notes THEN the system SHALL persist free-form, longer-form text separate from the node's primary (title) text.
4. WHEN the user cancels an in-progress text edit (e.g., via Escape) THEN the system SHALL discard the pending edit and restore the node's previous text.

### Requirement 5 — Collapsing and expanding branches

**User Story:** As a diagram author, I want to collapse and expand branches, so that I can focus on relevant parts of a large mindmap without losing the rest of my work.

#### Acceptance Criteria

1. WHEN the user collapses a node with children THEN the system SHALL hide that node's descendants from the canvas while keeping the node itself visible.
2. WHEN the user expands a previously collapsed node THEN the system SHALL show its direct children again (respecting their own collapsed/expanded state).
3. WHEN a node's collapsed/expanded state changes THEN the system SHALL persist that state as part of the mindmap file so it survives reopening.
4. WHEN a mindmap is open in read-only mode (per `adp-diagram-ide` Requirement 5) THEN the system SHALL still allow collapsing/expanding, since it changes only local view state, not the document.
5. WHEN a node's collapsed/expanded state changes THEN the system SHALL NOT record it in the undo/redo history (per `adp-diagram-ide` Requirement 6), since it is a view-state change, not a document edit.

### Requirement 6 — Linking nodes to code artifacts

**User Story:** As a diagram author, I want to link a mindmap node to a file (and optionally a specific location within it) in my repository, so that the mindmap stays meaningful as the code it describes evolves.

#### Acceptance Criteria

1. WHEN the user links a node to a code artifact THEN the system SHALL let them specify a path within the current workspace and, optionally, a location within that file (e.g., a line number).
2. WHEN a node has a linked code artifact THEN the system SHALL visually indicate this on the node so the link is discoverable without opening an edit mode.
3. WHEN the user activates a node's code-artifact link THEN the system SHALL navigate to that file (and location, if specified) within the IDE.
4. IF a node's linked code artifact no longer exists at its recorded path THEN the system SHALL indicate the link is broken rather than silently navigating nowhere or failing without explanation.
5. WHEN the user removes a link from a node THEN the system SHALL delete the stored link without affecting the node's text, notes, or structural position.

### Requirement 7 — Pluggable registration as a diagram type

**User Story:** As a developer of EtAlii.Adp, I want the mindmap module to register itself as a diagram type without any core code depending on it, so that the pluggable diagram-type model is proven out, not just asserted.

#### Acceptance Criteria

1. WHEN the backend encounters a file with the `.mm` extension THEN the system SHALL recognize it as a mindmap and route it to the mindmap module, via the same diagram-type registration mechanism any future diagram module would use.
2. WHEN the mindmap module defines its element payload (node data) THEN the system SHALL do so as its own `.proto` message packed into the core `Element.payload` (per `grpc-core-communication-specification`'s extension point), without modifying the core contract files.
3. WHEN organizing the mindmap module's code THEN the system SHALL place it under `diagrams/mindmap/` with `backend/`, `api/`, and `client/` subfolders, per `structure.md`.
4. IF core canvas, storage, or sync code is inspected THEN it SHALL contain no mindmap-specific type checks, imports, or branching logic — the dependency SHALL only run from the mindmap module toward core abstractions, per `structure.md`'s dependency-direction rule.

## Non-Functional Requirements

### Code Architecture and Modularity

* **Single Responsibility Principle**: Parsing/serialization of `.mm` files, tree-structure editing logic, canvas rendering of nodes/connections, and code-artifact linking are separate, independently testable concerns within the module.
* **Modular Design**: The mindmap module (`EtAlii.Adp.Diagram.Mindmap` backend project, `diagrams/mindmap/api` protos, `diagrams/mindmap/client` components) depends only on core abstractions from `adp-diagram-ide` and the core gRPC contract; nothing in core depends on the mindmap module.
* **Dependency Management**: The `.mm` parser/serializer SHALL be isolated from the gRPC-facing element-mapping code, so the file format can be tested without a running backend or client.
* **Clear Interfaces**: The mindmap module's registration with the core diagram-type mechanism SHALL be the only integration surface between this module and core; no other back-channel coupling.

### Performance

* Tree-layout recomputation after a structural edit SHALL remain fast enough to feel immediate for mindmaps with hundreds of nodes, consistent with `adp-diagram-ide`'s canvas responsiveness target.
* Collapsing a branch SHALL immediately stop rendering its (now hidden) descendants, contributing to the virtualization goals of `adp-diagram-ide` Requirement 4.3.

### Security

* Code-artifact links (Requirement 6) SHALL only resolve to paths within the currently authorized workspace, consistent with `tech.md`'s filesystem-access scoping; a link SHALL NOT be usable to reference or navigate to files outside it.

### Reliability

* WHEN a `.mm` file cannot be parsed (malformed or non-conformant XML) THEN the system SHALL surface a clear error rather than partially loading a corrupted tree or crashing the workspace.
* Concurrent structural edits to the same mindmap from multiple clients SHALL be reconciled using the core delta/reconciliation behavior from `grpc-core-communication`, without the mindmap module needing its own conflict-resolution logic.

### Usability

* Node creation/navigation keyboard shortcuts SHOULD follow conventions familiar from established mindmap tools (e.g., Tab to add a child, Enter to add a sibling), consistent with `adp-diagram-ide`'s general "familiar surface" usability goal.
