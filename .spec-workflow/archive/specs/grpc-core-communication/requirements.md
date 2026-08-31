# Requirements Document

## Introduction

This spec covers the **behavior** of the core gRPC communication layer of EtAlii.Adp: what the backend and viewing applications (a browser-based web client, or a locally hosted/desktop client) actually do with the core gRPC contract — connection handling, view-state tracking, delta streaming logic, authentication enforcement, and reconnection/reconciliation. The wire contract itself (the `.proto` message and service definitions this behavior is built on) is covered separately by the [`grpc-core-communication-specification`](../../../specs/grpc-core-communication-specification/requirements.md) spec, which this spec depends on.

It deliberately excludes diagram-type-specific business logic and the canvas/editing UI itself. Diagram-type-specific behavior is owned by their diagram modules but must conform to the core contract and behavior defined here.

## Alignment with Product Vision

This capability directly implements the "Live, pushed updates" and "Files are the source of truth" principles from [product.md](../../../steering/product.md): the backend remains the sole owner of file state, and this layer is what lets any viewing application stay live without ever touching the filesystem itself. It also implements the "Frontend-backend synchronization" section in [tech.md](../../../steering/tech.md).

## Requirements

### Requirement 1 — Connection initialization and path resolution

**User Story:** As a viewing application, I want the backend to resolve the path I connect with to the right file (and sub-scope, if any), so that I'm always viewing/editing the thing I asked for.

#### Acceptance Criteria

1. WHEN a viewing application initializes a connection with a path (per the core contract) THEN the system SHALL resolve that path to the target file before treating the connection as active.
2. IF the path includes additional segments beyond the file itself THEN the system SHALL resolve the connection to that specific location/sub-scope within the file, not the file as a whole.
3. IF the supplied path cannot be resolved to an accessible file (missing, out of workspace scope, or unauthorized) THEN the system SHALL reject the connection initialization with a clear error rather than opening a connection to undefined state.

### Requirement 2 — View-state tracking

**User Story:** As a viewing application, I want the backend to remember my current view, so that what it sends me stays relevant as I pan and zoom.

#### Acceptance Criteria

1. WHEN the backend receives a view update (xy center + bounding box, per the core contract) for a connection THEN the system SHALL treat it as that connection's current view state until the next update.
2. IF no view update has yet been received for a newly initialized connection THEN the system SHALL apply an explicit default view state rather than leaving behavior for that connection undefined.
3. WHEN a connection closes THEN the system SHALL discard the view state held for it.

### Requirement 3 — Delta streaming logic

**User Story:** As a viewing application, I want to receive only the deltas relevant to what I can see, in the right order, so that I can keep my visual representation in sync efficiently.

#### Acceptance Criteria

1. WHEN the backend has element changes relevant to a connection's current view state THEN the system SHALL stream them to that connection as `add`/`remove`/`group`/`ungroup` deltas (per the core contract) rather than a full-state snapshot.
2. WHEN a viewing application first initializes a connection THEN the system SHALL deliver an initial baseline (via `add` deltas for everything currently in view) before/as part of streaming subsequent deltas, so the client can construct current state without a separate fetch.
3. WHEN multiple deltas affecting the same element are produced in sequence THEN the system SHALL deliver them in the order they were applied on the backend.
4. WHEN a connection's view state changes such that previously out-of-view elements become relevant THEN the system SHALL deliver the deltas needed to bring that connection's view up to date.

### Requirement 4 — Element type extensibility at runtime

**User Story:** As a viewing application, I want to handle element types safely even when new types are introduced, so that the platform can evolve without breaking existing clients.

#### Acceptance Criteria

1. WHEN a diagram module registers a new mime-typed element type (per the core contract's extension mechanism) THEN the system SHALL make elements of that type deliverable through the existing delta streaming logic without changes to that logic.
2. IF a viewing application receives an element of a type it does not recognize THEN the system SHALL allow the application to detect this condition (rather than fail unpredictably), so it can decide how to degrade gracefully.

### Requirement 5 — Connection authentication and lifecycle

**User Story:** As a user of a viewing application, I want my connection to the backend to be authenticated and cleanly established/torn down, so that only authorized clients can read or change my workspace's files.

#### Acceptance Criteria

1. WHEN a viewing application establishes a gRPC connection THEN the system SHALL apply authentication and authorization as decorations on the corresponding gRPC initialization call, per `tech.md`.
2. IF a connection attempt fails authentication/authorization THEN the system SHALL reject it without exposing any workspace file state.
3. WHEN a connection closes (gracefully or otherwise) THEN the system SHALL release any per-connection state (view state, subscriptions) held for it on the backend.
4. WHEN running in the local, standalone "F5" scenario THEN the system SHALL support a local-only auth mode that requires no external identity provider.

### Requirement 6 — Reconnection and reconciliation

**User Story:** As a user of a viewing application, I want my client to recover automatically if the connection drops, so that a transient network issue doesn't force me to reload or lose my place.

#### Acceptance Criteria

1. IF the gRPC stream is interrupted THEN the system SHALL attempt to reestablish the connection (re-initializing with the same path) without requiring a full page reload.
2. WHEN a connection is reestablished THEN the system SHALL re-baseline the client (per Requirement 3.2) using the last known view state before resuming live delta delivery.
3. WHEN reconciling after a reconnect THEN the system SHALL NOT discard the user's local unsaved edits, selection, or viewport without giving the client the information needed to preserve them.
4. WHEN repeated reconnect attempts fail THEN the system SHALL surface a clear connected/disconnected state to the client rather than failing silently.

## Non-Functional Requirements

### Code Architecture and Modularity
- **Single Responsibility Principle**: Connection/path resolution, view-state tracking, delta streaming logic, and reconnection handling are separate concerns.
- **Modular Design**: This behavior layer SHALL depend on the contract defined in `grpc-core-communication-specification`, and SHALL NOT reference any specific diagram module (per `structure.md`'s dependency direction rule).
- **Dependency Management**: Diagram-specific behavior lives in diagram modules, which depend on this layer, never the reverse.
- **Clear Interfaces**: The behaviors defined here are what diagram modules and viewing applications can rely on when built against the core contract.

### Performance
- Delta propagation from backend apply to client delivery SHALL complete within a bounded, sub-second latency on a local/LAN connection.
- View-driven filtering (Requirement 2) SHALL avoid sending deltas for elements outside a client's current bounding box.

### Security
- All gRPC calls SHALL pass through the authentication/authorization decoration described in Requirement 5 before reaching business logic.
- Per-connection state (view state, subscriptions) SHALL be isolated per authenticated session; one client SHALL NOT be able to read another's view state or subscriptions.
- Path resolution (Requirement 1) SHALL stay scoped to the authorized workspace; a path SHALL NOT be usable to escape it.

### Reliability
- Loss of a gRPC stream SHALL NOT corrupt backend-held file state; the backend remains the durable source of truth per `tech.md`.
- The re-baseline path (Requirement 3.2 / 6.2) SHALL be exercised by tests so reconnection behavior does not silently regress.

### Usability
- Connection state (connected / reconnecting / disconnected) SHALL be observable by the viewing application so its UI can reflect it to the user.
