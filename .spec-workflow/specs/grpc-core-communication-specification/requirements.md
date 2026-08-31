# Requirements Document

## Introduction

This spec covers **creation of the core gRPC `.proto` contract file(s)** for EtAlii.Adp: the concrete message and service definitions that make up the wire contract between viewing applications (browser or desktop) and the backend. It is scoped purely to authoring the contract itself — the `.proto` file(s), their messages, and the service definition. It does not cover implementing the behavior that produces or consumes these messages (connection handling logic, view-state tracking, auth enforcement, reconnection/reconciliation) — that is covered by the sibling [`grpc-core-communication`](../../archive/specs/grpc-core-communication/requirements.md) spec, which depends on this contract.

## Alignment with Product Vision

This implements the "Bi-directional gRPC for frontend-backend communication" decision in [tech.md](../../steering/tech.md), and produces the shared `api/` folder contract described in [structure.md](../../steering/structure.md) that diagram modules build their own `diagrams/<diagram>/api/` extensions against.

## Requirements

### Requirement 1 — Connection initialization message

**User Story:** As a backend/client developer, I want a proto-defined initialization message carrying a path, so that a viewing application can request a connection scoped to a specific file (and optional sub-scope) with compile-time type safety.

#### Acceptance Criteria

1. WHEN the core contract is authored THEN it SHALL define a connection-initialization message that carries a path value identifying the target file.
2. WHEN the path is defined THEN it SHALL be able to represent additional path segments beyond the file itself, not just a single flat filename.
3. WHEN the initialization message is defined THEN it SHALL be what opens the connection - the request of the server-streaming `Open` call (Requirement 5.1).

### Requirement 2 — View update message

**User Story:** As a backend/client developer, I want a proto-defined view-update message, so that a viewing application can report its current xy center position and bounding box in a type-safe way.

#### Acceptance Criteria

1. WHEN the core contract is authored THEN it SHALL define a view-update message with an xy center position field.
2. WHEN the core contract is authored THEN it SHALL define a bounding box field alongside (or paired with) the center position.
3. WHEN the view-update message is defined THEN it SHALL be sendable by the client at any point after connection initialization, through the unary `UpdateView` call correlated to the open stream by connection id (Requirement 5.1).

### Requirement 3 — Delta envelope and action messages

**User Story:** As a backend/client developer, I want proto-defined delta messages for add/remove/group/ungroup, so that the backend can stream typed, unambiguous change notifications to viewing applications.

#### Acceptance Criteria

1. WHEN the core contract is authored THEN it SHALL define a delta envelope message capable of representing exactly one of: `add`, `remove`, `group`, `ungroup`.
2. WHEN the `add` action is defined THEN it SHALL carry one or more elements, and a receiver SHALL treat each as an **upsert** keyed on the element id: an id it does not hold is inserted, an id it already holds is replaced in place. An edit to an existing element therefore travels as an `add` of its new state, never as a `remove` followed by an `add` - which would make the element momentarily absent, indistinguishable from a real deletion, and drop any selection or focus on it. *(Amended for `mindmap-diagram` Requirement 11.3; the message itself is unchanged.)*
3. WHEN the `remove` action is defined THEN it SHALL carry one or more element identifiers to remove.
4. WHEN the `group` action is defined THEN it SHALL carry the source element identifiers being grouped and the grouping element. The grouping element MAY be one the receiver already holds - a folded mindmap node stands for its hidden branch - or a new one; a receiver SHALL upsert it by id exactly as it does for `add`. *(Amended for `mindmap-diagram` Requirement 11.4.)*
5. WHEN the `ungroup` action is defined THEN it SHALL carry the group element's identifier and the resulting elements.

### Requirement 4 — Extensible, mime-typed element message

**User Story:** As a diagram module developer, I want a core element message with a type-safe extension point, so that I can define new visual element types in their own proto files without modifying the core contract.

#### Acceptance Criteria

1. WHEN the core contract is authored THEN it SHALL define an element message that includes an xy coordinate field.
2. WHEN the element message is defined THEN it SHALL include a mime-style type identifier field (e.g. `mindmap/node`, `mindmap/relation`).
3. WHEN the element message is defined THEN it SHALL provide a type-safe extension mechanism (e.g. a `oneof`/`Any`-style payload) so each concrete element type is backed by its own gRPC message rather than an untyped blob.
4. WHEN a new diagram-specific element type is introduced THEN it SHALL be definable in its own `.proto` file under `diagrams/<diagram>/api/` without modifying the core element message.

### Requirement 5 — Core service definition

**User Story:** As a backend/client developer, I want a single core gRPC service exposing the bidirectional stream, so that there is one authoritative entry point for connection, view updates, and delta delivery.

#### Acceptance Criteria

1. WHEN the core contract is authored THEN it SHALL define a gRPC service carrying the client-to-backend messages (Requirements 1–2) one way and the backend-to-client delta messages (Requirement 3) the other, as **two correlated one-way legs** per `tech.md`'s *gRPC call shapes*: a server-streaming `Open` that takes the initialization message and returns the delta stream, and a unary `UpdateView` that takes a view update, both correlated by the connection id the client already carries. *(Amended: this originally asked for one bidirectional-streaming RPC. A browser on grpc-web cannot stream a request body, so that RPC could never be called from the client this product has; `mindmap-diagram` was the first spec to need it and found it unusable. The messages are unchanged - only how they travel.)*
2. WHEN the core service and its messages are authored THEN they SHALL be placed in the shared `api/` folder described in `structure.md`, separate from any diagram-specific `.proto` files.
3. WHEN the core `.proto` file(s) are authored THEN they SHALL use package/namespace naming consistent with the `EtAlii.Adp` / `com.etalii.adp` conventions from `tech.md`/`structure.md`.

## Non-Functional Requirements

### Code Architecture and Modularity
- The deliverable of this spec is contract-only: `.proto` file(s) (and their generated stubs), with no server-side or client-side business logic.
- The core contract SHALL NOT import or reference any diagram-specific `.proto` file.

### Performance
- Not applicable to this spec directly; runtime performance of the streaming implementation is covered by the `grpc-core-communication` spec.

### Security
- The contract SHALL NOT embed credentials or secrets in any message; authentication/authorization is applied by decoration at the service-implementation layer, outside this spec's scope.

### Reliability
- The contract SHALL be defined so it can evolve in a backward-compatible way (proto3 field-numbering/reserved discipline), so clients and backends built against adjacent contract revisions can still interoperate where possible.

### Usability
- Message and field names SHALL be self-explanatory, since `.proto` files are the primary API documentation per `structure.md`'s documentation standards.
