# Requirements Document

## Introduction

This spec covers the entry flow into EtAlii.Adp, ahead of everything covered by [`adp-diagram-ide`](../adp-diagram-ide/requirements.md): a mandatory login gate, followed by a grid-based home page from which the user selects which **project** folder to open. Only after a project is selected does the workspace shell (activity bar, explorer, tabbed editor area, per `adp-diagram-ide` Requirement 1) become reachable.

This generalizes `adp-diagram-ide` Requirement 2's single "workspace folder chosen by the user" into a model where a logged-in user has one or more available project folders and explicitly picks one per session; `adp-diagram-ide` continues to own everything that happens once a project is open.

## Alignment with Product Vision

This implements the "Minimal footprint, incremental value" and "Value from day one" principles from [product.md](../../steering/product.md): even in the local, standalone scenario, gating access behind login and letting a user pick from multiple project folders keeps the door open for the hosted, multi-team scenario described in product.md's Future Vision, without requiring it up front. It also follows [tech.md](../../steering/tech.md)'s local-only auth mode (no external identity provider required for the F5 scenario) and reuses the authentication mechanism defined by the [`grpc-core-communication`](../grpc-core-communication/requirements.md) spec (Requirement 5).

## Requirements

### Requirement 1 — Mandatory login gate

**User Story:** As a user, I want to be required to log in before accessing any part of the application, so that my projects and diagrams are protected from unauthorized access.

#### Acceptance Criteria

1. WHEN an unauthenticated user opens the web client THEN the system SHALL present a login screen and SHALL NOT render the project grid, the workspace shell, or any diagram content.
2. WHEN a user submits valid credentials THEN the system SHALL establish an authenticated session and proceed to the project-selection home page.
3. IF a user submits invalid credentials THEN the system SHALL reject the attempt and remain on the login screen with a clear error, without granting any access.
4. WHEN running in the local, standalone "F5" scenario THEN the system SHALL support a local-only auth mode (per `grpc-core-communication` Requirement 5.4) so login still functions without an external identity provider.
5. IF an authenticated session expires or becomes invalid THEN the system SHALL return the user to the login screen rather than continuing to display protected content.
6. WHEN enforcing the login gate THEN the system SHALL enforce it at the backend/gRPC connection level (per `grpc-core-communication` Requirement 5.1–5.2), not only by hiding UI on the client.

### Requirement 2 — Grid-based project home page

**User Story:** As a logged-in user, I want to see my available project folders as a grid on a home page, so that I can quickly pick which one to open.

#### Acceptance Criteria

1. WHEN a user successfully logs in THEN the system SHALL display a home page listing the user's available project folders in a grid layout.
2. WHEN the project grid is displayed THEN each grid item SHALL distinctly represent one project folder (at minimum its name).
3. WHEN the user selects a project folder from the grid THEN the system SHALL open that folder as the active project and transition into the workspace shell for it (per `adp-diagram-ide` Requirement 1).
4. IF the user has no available project folders THEN the system SHALL present a way to add one (e.g. browse for / register a folder) rather than showing an empty, dead-end grid.
5. WHILE a user remains within the same authenticated session THEN the system SHALL NOT require re-entering credentials to view the project grid or switch between projects.

### Requirement 3 — Returning to project selection

**User Story:** As a user working inside a project, I want to get back to the project grid without logging out, so that I can switch between projects easily.

#### Acceptance Criteria

1. WHEN a user is inside an opened project's workspace THEN the system SHALL provide a way to return to the project grid without ending their authenticated session.
2. WHEN a user navigates away from a project back to the grid THEN the system SHALL stop actively syncing changes for that project's open diagrams (consistent with `adp-diagram-ide` Requirement 1.3) until it is reopened.

### Requirement 4 — Session and logout

**User Story:** As a user, I want to log out explicitly, so that I can end my session on a shared or public machine.

#### Acceptance Criteria

1. WHEN a user chooses to log out THEN the system SHALL end the authenticated session and return to the login screen.
2. WHEN a session ends (logout or expiry) THEN the system SHALL discard any in-memory project/diagram state held on the client.

### Requirement 5 — Persisted, user-managed project list

**User Story:** As a user, I want my list of project folders to persist across sessions and be able to add or remove entries myself, so that I don't have to reconfigure my workspace every time I log in.

#### Acceptance Criteria

1. WHEN a user's project list changes (an add or a remove) THEN the system SHALL persist that list associated with the user, so it survives logout/login and application restarts.
2. WHEN a user wants to add a project THEN the system SHALL let them do so by entering a folder path as plain text (first iteration); a richer folder-browse picker MAY replace or augment this later without changing the persisted data model. This is how Requirement 2.4's "way to add one" is satisfied.
3. WHEN a user submits a folder path to add THEN the system SHALL validate that it resolves to an accessible folder before adding it to their project list, and SHALL reject an invalid path with a clear error without corrupting the existing list.
4. WHEN a user removes a project from their list THEN the system SHALL remove it from their persisted project list and from the grid, but SHALL NOT delete or otherwise modify the underlying folder or its contents on disk.
5. WHEN a project is added or removed THEN the system SHALL reflect the change in the grid without requiring a full page reload.

## Non-Functional Requirements

### Code Architecture and Modularity
- **Single Responsibility Principle**: Login, project-grid, and workspace-shell concerns are separate, independently reachable states in the client, not intertwined.
- **Modular Design**: The login gate and project-grid components SHALL NOT depend on `adp-diagram-ide`'s workspace/canvas components; the dependency only runs the other way (workspace shell is reached only after project selection).

### Performance
- The project grid SHALL load and render without waiting on any individual project's diagram data being fetched — only the list of available projects is needed up front.

### Security
- Login SHALL be enforced server-side; a client-side-only route guard is not sufficient (Requirement 1.6).
- A user's project grid SHALL only ever list projects that user is authorized to access.

### Reliability
- IF the backend is unreachable at login time THEN the system SHALL surface a clear connection error rather than an unexplained hang or silent failure.
- Per-user project list persistence SHOULD follow the same file-based, no-database philosophy as diagram storage (per `tech.md`'s "File-based storage over a database" decision) rather than introducing a new persistence mechanism.

### Usability
- The project grid SHOULD follow a recent-projects-grid convention familiar from IDEs like Rider (per `tech.md`'s development-tools context), reinforcing the "familiar surface" product principle even before a project is opened.
