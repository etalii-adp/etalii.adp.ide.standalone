# Requirements Document

## Introduction

EtAlii.Adp ("A Different Perspective") is a web-based IDE for creating, viewing, and editing a wide range of visual diagrams. It presents a VS Code-like interface (activity bar, side panels, tabbed editor area, command palette) inside the browser, but is backed by a native process on the user's machine. Diagrams and their supporting assets are persisted as files on disk, and the backend streams live state and changes to the web client over gRPC, keeping the UI in sync with the underlying file-backed model in near real time.

This spec covers the foundational rebuild of the product following a full reset of the codebase, re-establishing the core architecture and the first slice of end-to-end functionality (open a diagram, view it, edit it, see changes pushed live, undo/redo, read-only mode).

## Alignment with Product Vision

No `product.md` steering document exists yet for this project. This requirements document establishes the initial product direction for EtAlii.Adp: a fast, file-native, VS Code-familiar diagramming IDE that treats diagrams as first-class, version-controllable files rather than opaque blobs in a proprietary database.

## Requirements

### Requirement 1 — Workspace shell

**User Story:** As a diagram author, I want a VS Code-like workspace shell (activity bar, (file) explorer, tabbed editor area, status bar) in my browser, so that the tool feels immediately familiar and I can navigate my files without a learning curve.

#### Acceptance Criteria

1. WHEN the web client loads THEN the system SHALL render an activity bar, a collapsible side panel, a tabbed main editor area, and a status bar.
2. WHEN the user opens a file from the explorer THEN the system SHALL open it in a new editor tab, or focus the existing tab if already open.
3. WHEN the user closes the last tab for a file THEN the system SHALL stop actively syncing changes for that file until it is reopened.
4. IF the browser window is resized THEN the system SHALL reflow the layout responsively without loss of state.

### Requirement 2 — File-based diagram storage

**User Story:** As a diagram author, I want my diagrams stored as plain files on disk, so that I can use my own folder structure, back them up, and manage them with any version control system.

#### Acceptance Criteria

1. WHEN a diagram is created THEN the system SHALL persist it as one or more files under a workspace folder chosen by the user.
2. WHEN a diagram file is saved THEN the system SHALL write it in a documented, text-based format suitable for diffing in version control.
3. IF a diagram file is modified outside the IDE (e.g., by another editor or `git checkout`) THEN the system SHALL detect the external change and reload or prompt the user, without corrupting unsaved in-app edits.
4. WHEN the backend starts THEN the system SHALL NOT require any database or external service to read or write diagrams — the filesystem is the source of truth.

### Requirement 3 — Backend-to-client live sync over gRPC

**User Story:** As a diagram author, I want changes to a diagram to appear in my view immediately, so that I trust the editor is always showing current state, including when changes originate outside my own edits.

#### Acceptance Criteria

1. WHEN the web client opens a diagram THEN the system SHALL establish a gRPC streaming connection to the backend for that diagram's change feed, using grpc-web as the browser-side transport (see Non-Functional Requirements: Client Technology Stack).
2. WHEN the backend applies a change to a diagram (from this client, another client, or an external file change) THEN the system SHALL push the change to all connected clients viewing that diagram within a bounded latency.
3. IF the gRPC stream is interrupted THEN the system SHALL attempt to reconnect and reconcile client state with backend state without requiring a full page reload.
4. WHEN a change is pushed to the client THEN the system SHALL apply it without discarding the user's current selection, viewport (pan/zoom), or in-progress uncommitted edit.

### Requirement 4 — Diagram viewing and editing

**User Story:** As a diagram author, I want to view and edit a wide range of diagram types on an interactive canvas, so that I can model different kinds of systems and structures in one tool.

#### Acceptance Criteria

1. WHEN a diagram is opened THEN the system SHALL render its nodes and connections on a pannable, zoomable canvas.
2. WHEN the user adds, moves, resizes, connects, or deletes a diagram element THEN the system SHALL persist the change and reflect it on the canvas.
3. IF the diagram contains a large number of elements THEN the system SHALL use virtualization so that only visible elements are rendered, keeping interaction responsive, rendered via a canvas/WebGL-based renderer rather than raw SVG/DOM (see Non-Functional Requirements: Client Technology Stack).
4. WHEN the diagram or element schema differs by diagram type THEN the system SHALL support that variability through a pluggable diagram-type model rather than hardcoding a single diagram shape.

### Requirement 5 — Read-only mode

**User Story:** As a reviewer, I want to open a diagram in read-only mode, so that I can view it without risk of accidentally modifying it.

#### Acceptance Criteria

1. WHEN a diagram is opened in read-only mode THEN the system SHALL disable all editing interactions (add, move, delete, connect) on the canvas.
2. WHEN a diagram is read-only THEN the system SHALL still apply incoming pushed changes from the backend so the view stays current.
3. WHEN a diagram is read-only THEN the system SHALL NOT record entries in the undo/redo history for that diagram.
4. IF a user with edit permission switches a read-only diagram to editable THEN the system SHALL re-enable editing interactions and resume history tracking.

### Requirement 6 — History (undo/redo)

**User Story:** As a diagram author, I want to undo and redo my edits, so that I can experiment and recover from mistakes.

#### Acceptance Criteria

1. WHEN the user performs an editable change THEN the system SHALL record it in a per-diagram undo history.
2. WHEN the user triggers undo THEN the system SHALL revert the diagram to its state before the most recent recorded change.
3. WHEN the user triggers redo after an undo THEN the system SHALL reapply the reverted change.
4. IF a new edit is made after an undo THEN the system SHALL discard the stale redo stack from that point forward.
5. WHEN a change originates from another client or an external file change (not the local user) THEN the system SHALL NOT add it to the local undo stack as an undoable local action.

## Non-Functional Requirements

### Code Architecture and Modularity

* **Single Responsibility Principle**: Each file should have a single, well-defined purpose
* **Modular Design**: Components, utilities, and services should be isolated and reusable
* **Dependency Management**: Minimize interdependencies between modules
* **Clear Interfaces**: Define clean contracts between components and layers
* **Diagram-type extensibility**: The core canvas, storage, and sync layers SHALL NOT depend on any single diagram type's schema; new diagram types SHALL be addable without modifying core layers.

### Client Technology Stack

* **Framework**: React + TypeScript.
* **Diagram rendering**: A canvas/WebGL-based rendering library (e.g. Konva or PixiJS) rather than raw SVG/DOM, chosen for its ability to sustain smooth pan/zoom/drag interaction on large, virtualized diagrams (Requirement 4.3, Performance below).
* **gRPC transport**: grpc-web (or Connect-Web), backed by ASP.NET Core's gRPC-Web middleware on the backend, since browsers cannot speak native HTTP/2 gRPC directly (Requirement 3.1).
* **Decision rationale**: chosen over a Blazor WebAssembly (all-C#) client because the canvas/diagramming ecosystem available to React/TypeScript is significantly more mature for this use case, outweighing the benefit of reusing generated gRPC stubs and Rider tooling end-to-end in C#.

### Performance

* Canvas interactions (pan, zoom, select, drag) SHALL remain responsive (target: no perceptible input lag) on diagrams with thousands of elements via virtualization.
* Backend-to-client change propagation SHALL complete within a bounded latency suitable for a "live" feel (target: sub-second on a local/LAN connection).

### Security

* The gRPC channel between backend and web client SHALL be authenticated/authorized appropriately for the deployment context before exposing any workspace beyond localhost.
* File system access from the backend SHALL be scoped to explicitly opened workspace folders, not the whole filesystem.

### Reliability

* Loss of the gRPC stream SHALL NOT corrupt on-disk diagram state; the backend remains the durable source of truth.
* Concurrent edits from multiple clients to the same diagram SHALL be reconciled without silent data loss.

### Usability

* The web client's look and interaction model SHALL closely follow familiar VS Code conventions (layout, keyboard shortcuts, command palette, undo/redo capabilities) to minimize onboarding friction.