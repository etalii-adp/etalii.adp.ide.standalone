# Requirements Document

## Introduction

This spec moves diagram undo/redo history from a client-local concept to a **server-side, per-diagram** capability. [`adp-diagram-ide`](../adp-diagram-ide/requirements.md) Requirement 6 describes a "local undo stack," but the backend is already the sole, durable owner of diagram element state ([tech.md](../../steering/tech.md); [`grpc-core-communication`](../grpc-core-communication/requirements.md)), and more than one client can be simultaneously connected to the same diagram (`adp-diagram-ide` Requirement 3.2 / `grpc-core-communication` Requirement 3). A history that lives only in one browser tab can't reflect that reality: it fragments across tabs/clients and is lost on reconnect.

This spec defines a single, server-maintained undo/redo stack scoped to one diagram (identified by its file path), replacing the local-history framing of `adp-diagram-ide` Requirement 6 for the multi-client, backend-authoritative model this product actually has. It does not redefine what counts as an editable change — that remains diagram-module-specific (`grpc-core-communication-specification` Requirement 3's `add`/`remove`/`group`/`ungroup` deltas) — only how the resulting history is tracked, requested, and applied.

## Alignment with Product Vision

- [product.md](../../steering/product.md)'s **"Live, pushed updates"**: undo/redo is itself a change, and SHALL propagate to every connected viewer the same way any other change does.
- tech.md's **"the backend is the sole owner of reading/writing diagram files"**: history that determines what gets written back to disk belongs with that same owner, not scattered across clients.
- tech.md's **frontend-backend synchronization** delta model: this spec reuses the existing `add`/`remove`/`group`/`ungroup` delta vocabulary rather than inventing a parallel change-representation just for undo.

## Requirements

### Requirement 1 — Server-owned, per-diagram undo/redo stack

**User Story:** As a diagram author, I want undo/redo history to live on the backend per diagram file, so that my ability to undo doesn't depend on which browser tab or client I'm using, and survives reconnects.

#### Acceptance Criteria

1. WHEN an editable change (a delta, per `grpc-core-communication-specification` Requirement 3) is applied to a diagram THEN the system SHALL record it on a server-side undo history scoped to that diagram's file path, not to the connection/client that produced it.
2. WHEN a viewing application requests undo for an open diagram THEN the system SHALL revert the diagram to its state before the most recently recorded change on that diagram's server-side stack, regardless of which connection originally made that change.
3. WHEN a viewing application requests redo after an undo THEN the system SHALL reapply the most recently reverted change from that diagram's server-side stack.
4. IF a new editable change is applied to a diagram after an undo (from any connected client) THEN the system SHALL discard the stale redo entries for that diagram, consistent with standard undo/redo semantics.

### Requirement 2 — Multi-client visibility of undo/redo

**User Story:** As a diagram author collaborating with others (or across my own multiple open tabs) on the same diagram, I want undo/redo to behave consistently for everyone viewing it, so that the diagram's history isn't fragmented per viewer.

#### Acceptance Criteria

1. WHEN a connected client triggers undo or redo for a diagram THEN the system SHALL apply the resulting change and deliver it to all connections currently viewing that diagram via the existing delta streaming mechanism (`grpc-core-communication` Requirement 3), not only to the client that triggered it.
2. WHEN multiple clients are connected to the same diagram THEN the system SHALL expose a single, shared undo/redo stack for that diagram rather than maintaining independent per-client histories.
3. IF two clients trigger undo (or redo) for the same diagram at effectively the same time THEN the system SHALL serialize the requests and apply them one at a time against the shared stack, so the stack's state remains consistent and no entry is double-applied or lost.

### Requirement 3 — Scope of what is undoable

**User Story:** As a diagram author, I want undo to only affect genuine editable changes, not incoming external updates or view state, so that undo behaves predictably.

#### Acceptance Criteria

1. WHEN a change to a diagram originates from an editable action (add/remove/group/ungroup requested through the gRPC contract) THEN the system SHALL record it on the server-side undo stack.
2. IF a change to a diagram originates from an external file modification detected by the backend (per `adp-diagram-ide` Requirement 2.3) rather than from an editable action THEN the system SHALL NOT record it as an undoable entry on the server-side stack, mirroring the exclusion already defined for local history in `adp-diagram-ide` Requirement 6.5.
3. WHEN view-state updates (pan/zoom, per `grpc-core-communication-specification` Requirement 2) occur THEN the system SHALL NOT record them on the undo stack, consistent with `mindmap-diagram` Requirement 2.5's treatment of view-only state.

### Requirement 4 — Stack lifetime and persistence

**User Story:** As a diagram author, I want a predictable lifetime for undo history, so I know when I can still undo something and when I can't.

#### Acceptance Criteria

1. WHEN a diagram is opened for the first time in a backend process's lifetime THEN the system SHALL start it with an empty undo/redo stack.
2. WHILE at least one client remains connected to a diagram THEN the system SHALL retain its undo/redo stack in memory, so undo/redo continues to work across an individual client's disconnect/reconnect (per `grpc-core-communication` Requirement 6).
3. WHEN the backend process restarts THEN the system is NOT required to retain undo/redo history from before the restart — the on-disk diagram file (last saved/applied state) remains the durable source of truth, not the undo stack itself.
4. WHEN a diagram's undo/redo stack grows THEN the system SHALL bound its size (e.g. a maximum entry count), so a long editing session cannot cause unbounded server-side memory growth.

### Requirement 5 — Client interaction with server-side undo/redo

**User Story:** As a viewing application developer, I want a clear way to trigger undo/redo and know whether it's currently possible, so that I can wire up the IDE's undo/redo UI correctly.

#### Acceptance Criteria

1. WHEN a viewing application wants to trigger undo or redo THEN the system SHALL provide a gRPC-level way to request it for the diagram it is currently connected to.
2. WHEN a diagram's undo stack is empty THEN the system SHALL let the client determine that undo is not currently possible, rather than the client needing to guess or attempt-and-fail.
3. WHEN a diagram's redo stack is empty THEN the system SHALL let the client determine that redo is not currently possible, by the same means.
4. IF an undo or redo is requested when not possible (empty stack) THEN the system SHALL reject the request with a clear response rather than applying an undefined change.

## Non-Functional Requirements

### Code Architecture and Modularity

- **Single Responsibility**: the undo/redo stack is a core, diagram-type-agnostic capability — it operates on the generic delta vocabulary (`grpc-core-communication-specification` Requirement 3), so it SHALL live in the core communication layer, not be duplicated per diagram module.
- **Modular Design**: diagram modules SHALL NOT need to implement their own undo/redo logic; they get it for free by producing changes through the existing delta-producing mechanism this spec observes.

### Performance

- Undo/redo application SHALL complete within the same bounded, sub-second latency budget as ordinary delta propagation (`grpc-core-communication` Non-Functional: Performance).
- Recording a change on the undo stack SHALL NOT add perceptible latency to the change being applied and streamed.

### Security

- Undo/redo requests SHALL be subject to the same authentication/authorization enforcement as any other gRPC call against a diagram connection (`grpc-core-communication` Requirement 5).

### Reliability

- The shared per-diagram stack (Requirement 2.2–2.3) SHALL be safe under concurrent requests from multiple clients; no undo/redo request SHALL be able to corrupt the stack's ordering or the diagram's on-disk state.
- Loss of a single client's connection SHALL NOT affect the undo/redo stack's integrity for the diagram's other connected clients.

### Usability

- Viewing applications SHOULD be able to reflect undo/redo availability (Requirement 5.2–5.3) in their UI (e.g. disabling an undo button) without needing to attempt an operation just to discover it isn't possible.
