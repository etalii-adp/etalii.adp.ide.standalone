# Requirements Document

## Introduction

The core gRPC service defined in `src/api/service.proto` is currently named `AdpService`. This spec covers renaming it to `DiagramService` — a name that describes what the service actually does (streams diagram connection/view/delta traffic) rather than reusing the product acronym, which is redundant given the C# namespace/package (`EtAlii.Adp` / `etalii.adp`) already carries the product identity. The rename covers the `.proto` service identifier itself and every planning document that references it by name, so that no stale name survives into the not-yet-written backend/client implementation.

The repository is still at an early, planning-heavy stage: no backend or client code exists yet (confirmed — no `.cs`/`.csproj`/`.sln` files anywhere in the repo). The only concrete artifact carrying the name today is `src/api/service.proto`; every other reference lives in `.spec-workflow/` planning documents.

## Alignment with Product Vision

This aligns with `structure.md`'s documentation standard that "public gRPC contracts (`.proto` files) are the primary API documentation and must stay self-explanatory" — `DiagramService` states its purpose directly, where `AdpService` only restated the product name already present in the namespace. It also keeps `tech.md`'s naming convention intact: namespaces/packages carry the `EtAlii.Adp` / `etalii.adp` identity, while the service name itself remains free to describe its function.

## Requirements

### Requirement 1 — Rename the service in the proto contract

**User Story:** As a backend/client developer, I want the core gRPC service to be named `DiagramService` in the contract, so that the generated stubs and any future implementation are built against the correct, descriptive name from the start.

#### Acceptance Criteria

1. WHEN `src/api/service.proto` is updated THEN the `service AdpService { ... }` definition SHALL be renamed to `service DiagramService { ... }` with no other change to its body.
2. WHEN the service is renamed THEN the `Connect` RPC signature (`rpc Connect(stream ClientMessage) returns (stream Delta)`) SHALL remain unchanged.
3. WHEN the service is renamed THEN the `package etalii.adp;` declaration and `option csharp_namespace = "EtAlii.Adp";` SHALL remain unchanged, since the rename applies only to the service identifier, not the namespace/package.
4. WHEN the service is renamed THEN no message type (`ClientMessage`, `Path`, `ViewUpdate`, `Delta`, etc.) or any other `.proto` file (`connection.proto`, `elements.proto`, `deltas.proto`) SHALL be modified.

### Requirement 2 — Update forward-looking planning documents

**User Story:** As a developer picking up a not-yet-implemented task, I want every spec-workflow planning document to already reference `DiagramService`, so that I don't generate new code against the old name.

#### Acceptance Criteria

1. WHEN the rename is applied THEN every occurrence of `AdpService` in `.spec-workflow/specs/login-project-selection/design.md` and `.spec-workflow/specs/login-project-selection/tasks.md` (architecture diagram, interceptor coverage text, integration test descriptions) SHALL be updated to `DiagramService`, since that spec's tasks are not yet implemented (`tasks.md` has no completed tasks).
2. WHEN the rename is applied THEN every occurrence of `AdpService` in `.spec-workflow/specs/grpc-core-communication-specification/design.md` SHALL be updated to `DiagramService`, including the prose, the component/data-model sections, and the mermaid diagram label, so this document continues to accurately describe the current `service.proto` contract it documents.
3. WHEN `grpc-core-communication-specification/tasks.md` is examined THEN its completed task descriptions (task 4's reference to `AdpService`) SHALL also be updated to `DiagramService`, since this file describes the deliverable's current shape, not a point-in-time narrative.

### Requirement 3 — Preserve historical records as-is

**User Story:** As someone auditing what was actually built at a point in time, I want implementation logs and approval snapshots left untouched, so that the historical record isn't silently rewritten.

#### Acceptance Criteria

1. WHEN the rename is applied THEN files under any `.spec-workflow/specs/*/Implementation Logs/` directory SHALL NOT be modified, since they are timestamped records of what was true at the time they were written.
2. WHEN the rename is applied THEN files under any `.spec-workflow/approvals/**/.snapshots/` directory SHALL NOT be modified, since they are frozen snapshots tied to a specific historical approval.

## Non-Functional Requirements

### Code Architecture and Modularity
- The rename SHALL be a pure identifier change: no message shape, RPC signature, package, or namespace changes.
- The change SHALL be scoped to `src/api/service.proto` plus the two forward-looking planning documents identified in Requirement 2 — no other file in the repository references `AdpService` (verified by a repo-wide search).

### Performance
- Not applicable — no runtime code exists yet to measure.

### Security
- Not applicable — the rename carries no security-relevant behavior change.

### Reliability
- Since no generated stubs, server implementation, or client code exist yet in the repository, this rename carries no wire-compatibility risk today; it must land before any implementation work references `AdpService`.

### Usability
- The new name SHALL be self-explanatory on its own, consistent with `structure.md`'s documentation standard for `.proto` files.
