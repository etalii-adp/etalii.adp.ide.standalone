# Requirements Document

## Introduction

This spec formalizes a project's **root folder** as an explicit, backend-validated setting, and specifies how that root folder's contents (files and folders) populate the workspace shell's file explorer once a project is opened, including keeping that explorer live as the folder changes on disk.

[`login-project-selection`](../login-project-selection/requirements.md) already established that a project record carries a `Path` (Requirement 5.2) which is validated to resolve to an accessible folder before being added (Requirement 5.3). This spec builds directly on that: it treats that same `Path` as the project's root folder, adds re-validation at *open* time (a folder can be moved, renamed, or deleted after being added, which adding alone can't catch), and defines the previously unspecified capability of listing that root folder's file/folder hierarchy over gRPC so [`adp-diagram-ide`](../adp-diagram-ide/requirements.md) Requirement 1's side-panel explorer has real content to show instead of being an empty stub. It also defines how that hierarchy is kept current: a filesystem watcher scoped to the root folder detects changes as they happen, the backend tracks what it has told clients about in an in-memory model that lives for as long as a client is connected to that project's hierarchy, and changes are pushed to connected clients from that model, with each entry identified by a stable [`ShortGuid`](../short-guid/requirements.md) id so an entry keeps its identity across a rename instead of being re-keyed by path.

## Alignment with Product Vision

- **"Files are the source of truth"** ([product.md](../../steering/product.md)): the explorer must reflect what's actually on disk at read time, not a separately-maintained index that can drift from reality.
- [tech.md](../../steering/tech.md)'s **"File-system access from the backend is scoped to explicitly opened workspace folders, not the whole machine"**: hierarchy listing must never let a client read outside a project's root folder.
- tech.md's **frontend-backend synchronization** principle ("state changes flow from backend to client... so multiple clients... stay in sync without manual refresh") extends naturally from diagram deltas to the file/folder hierarchy itself.
- [short-guid](../short-guid/requirements.md)'s reusable identifier type: hierarchy entries reuse `ShortGuid` for the same reason diagram elements already carry a stable `ElementId` (`elements.proto`) — so an entry maintains identity across create/rename/delete notifications rather than being re-keyed by its (mutable) path.
- **"A familiar surface"** (product.md): a file explorer that mirrors real disk contents, with expand/collapse, file/folder distinction, and file-type icons, is core to the VS Code-like experience `adp-diagram-ide` promises. This spec reuses the Material Design Icons integration already established in the client (`@mdi/font`, imported once in `main.tsx` and consumed as `mdi mdi-*` classes, per `ProjectGridPage.tsx`'s trash icon) rather than introducing a second icon system.

## Requirements

### Requirement 1 — Project root folder as a validated setting

**User Story:** As a user, I want each of my projects to have one clearly-defined root folder that the system verifies is a real, accessible folder whenever I open it, so that I can trust the project actually points at real content on disk.

#### Acceptance Criteria

1. WHEN a project is added or opened THEN the system SHALL treat its root folder path (`login-project-selection` Requirement 5.2's `Path`) as the single, authoritative folder from which all of that project's files and folders are read.
2. WHEN a user opens a project (not only when it is first added) THEN the system SHALL re-validate that the root folder still resolves to an accessible folder on disk.
3. IF a project's root folder no longer resolves to an accessible folder at open time THEN the system SHALL reject opening it with a clear error and SHALL NOT transition into the workspace shell for it, while leaving the project entry in the user's list untouched (removal remains the separate, explicit user action described in `login-project-selection` Requirement 5.4).
4. WHEN resolving a project's root folder THEN the system SHALL treat it as a fixed, absolute location — no request (including later hierarchy requests under Requirement 2) SHALL be able to resolve a path that escapes it, e.g. via `..` segments or symbolic links.

### Requirement 2 — Reading the root folder's hierarchy

**User Story:** As a diagram author, I want the backend to read my project's actual folder and file structure, so that what I see in the IDE matches what's really on disk.

#### Acceptance Criteria

1. WHEN a project is opened THEN the system SHALL provide a gRPC capability to retrieve the direct children (files and subfolders) of the root folder, with each entry identifying whether it is a file or a folder.
2. WHEN a folder within the hierarchy is expanded THEN the system SHALL retrieve that folder's direct children the same way, so the full tree is never required to be loaded up front.
3. WHEN listing a folder's children THEN the system SHALL NOT read or expose anything outside the project's root folder (Requirement 1.4), even where a symbolic link or similar filesystem construct would otherwise allow it.
4. IF a folder within the hierarchy becomes inaccessible (permissions, deletion) after the project was opened THEN the system SHALL report that specific folder as unavailable rather than failing the entire hierarchy request.
5. WHEN an entry (file or folder) is included in a hierarchy response THEN the system SHALL identify it with a `ShortGuid` id that is stable for that entry's lifetime within the open project session, so the same entry can be referenced across later requests and pushed updates (Requirement 4) without relying on its path, which can change on rename.
6. WHEN an entry is first observed by the backend (at open-time listing or via the watcher in Requirement 4) THEN the system SHALL assign its id at that point; the system SHALL NOT be required to preserve ids across a project being closed and reopened, since the hierarchy is derived from disk at open time rather than from a persisted index (per this spec's "files are the source of truth" alignment).

### Requirement 3 — Explorer populated from the real hierarchy

**User Story:** As a diagram author, I want the workspace shell's file explorer to show my project's actual files and folders, so that I can navigate and open them the way `adp-diagram-ide` Requirement 1 already promises.

#### Acceptance Criteria

1. WHEN a project's workspace shell is opened THEN the system SHALL populate the explorer (side panel, per `adp-diagram-ide` Requirement 1.1) with the root folder's direct children, both files and folders.
2. WHEN the user expands a folder node in the explorer THEN the system SHALL fetch and display that folder's children per Requirement 2.2, showing a loading indication while the fetch is in progress.
3. WHEN the user collapses a folder node THEN the system SHALL hide its children without discarding already-fetched data, so re-expanding the same node does not require another fetch.
4. WHEN the user selects a file node in the explorer THEN the system SHALL open it per `adp-diagram-ide` Requirement 1.2 — this spec only supplies the tree the selection is made from, it does not change what happens once a file opens.
5. WHEN the explorer displays folders and files together at the same level THEN the system SHALL visually distinguish folders from files and SHALL list them in a stable, predictable order (folders before files, then alphabetical within each group).
6. WHEN the explorer renders a folder or file node THEN the system SHALL give it a Material Design Icon (via the existing `@mdi/font` integration, Alignment with Product Vision) rather than a generic bullet or no icon at all, satisfying the folder/file distinction required by 3.5 visually as well as by position.
7. WHEN a folder node's expand state changes THEN the system SHALL switch its icon accordingly (e.g. `mdi-folder`/`mdi-folder-outline` when collapsed, `mdi-folder-open`/`mdi-folder-open-outline` when expanded), so the icon itself reflects whether the folder's contents are currently shown.
8. WHEN a file node is rendered THEN the system SHOULD select its icon by file extension (e.g. distinct icons for common source, image, or config file types) and SHALL fall back to a generic file icon (e.g. `mdi-file-outline`) for any extension without a specific mapping, so every file always has an icon.
9. WHEN a folder is reported as unavailable (Requirement 2.4) THEN the system SHALL render it with a distinct icon/visual treatment (e.g. a warning overlay or muted style) rather than showing it identically to an accessible folder.
10. WHEN the client receives a hierarchy-change message (Requirement 4.5) THEN the system SHALL locate the affected node in the explorer's current tree by its id and apply the change in place — inserting a new node (with its own icon per Requirement 3.6–3.8) under the parent identified in the message for a create, removing the node together with any descendants for a delete, and updating the displayed name for a rename — without requiring a separate re-fetch of the parent folder's children (Requirement 4.2).

### Requirement 4 — Hierarchy stays current with disk changes

**User Story:** As a diagram author, I want the explorer to reflect files and folders added, removed, or renamed outside the IDE, so that I don't have to manually refresh to trust what I see.

#### Acceptance Criteria

1. WHEN a file or folder is added, removed, or renamed within an open project's root folder (from any source: the IDE itself, another editor, or a version-control operation) THEN the system SHALL update the explorer to reflect the change without requiring a manual refresh.
2. WHEN a hierarchy change is pushed to the client THEN the system SHALL apply it without collapsing already-expanded folders or losing the user's current selection, mirroring `adp-diagram-ide` Requirement 3.4's guarantee for diagram changes.
3. IF the root folder itself becomes inaccessible while a project is open THEN the system SHALL surface a clear error in the workspace shell rather than silently showing a stale or empty explorer.
4. WHEN the first client connects to a project's hierarchy stream THEN the system SHALL start a filesystem watcher (e.g. .NET's `FileSystemWatcher`) scoped recursively to that project's root folder to detect Requirement 4.1's add/remove/rename changes as they happen, and SHALL stop that watcher once the last client connected to that project's hierarchy stream disconnects.
5. WHEN the watcher detects a create, delete, or rename for an entry already represented in the in-memory hierarchy model (Requirement 4.8) THEN the system SHALL translate it into a hierarchy-change message that identifies the affected entry by its `ShortGuid` id (Requirement 2.5) — a create carries the newly assigned id together with its parent folder's id, its name, and whether it is a file or a folder (all resolved from the model); a delete carries the existing id; a rename carries the existing id together with its new name — and SHALL push that message over a gRPC stream to every connected client viewing that project's explorer, consistent with the change-delivery mechanism `adp-diagram-ide` Requirement 3 already establishes for diagram deltas.
6. IF the watcher's event buffer overflows under a high rate of filesystem changes (e.g. `FileSystemWatcher`'s `InternalBufferOverflowException`) THEN the system SHALL recover by re-listing the affected folder's children rather than silently missing changes, reconciling the in-memory model (Requirement 4.8) and ids for entries already known to a client (Requirement 2.5) instead of reassigning them.
7. IF the watcher detects a change under a folder whose children have never been listed to any client (Requirement 2.2) THEN the system SHALL update the in-memory model as needed to stay consistent but SHALL NOT push a hierarchy-change message for it, since no client has anything rendered for that folder yet to update — a later expand of that folder SHALL simply return current disk state (Requirement 2.2).
8. WHEN a client connects to a project's hierarchy stream THEN the system SHALL maintain, in the backend's gRPC service, a per-project in-memory model of every entry listed to a client so far (id, name, file/folder type, and parent/child relationship) — created on the first such connection for that project, shared by every client simultaneously connected to that same project's hierarchy stream (never one model per connection), and kept alive for as long as at least one such connection remains open; the system SHALL discard the model, together with the watcher (Requirement 4.4), once the last connection for that project closes.

## Non-Functional Requirements

### Code Architecture and Modularity

- **Single Responsibility**: hierarchy-listing logic is a distinct backend capability from diagram element storage (`elements.proto`/`deltas.proto`) and from project list management (`projects.proto`); it SHALL NOT be folded into either.
- **Modular Design**: the explorer's tree UI component SHALL be implemented independent of any specific diagram type, consistent with `adp-diagram-ide`'s diagram-type-plugin boundary (core must not depend on a diagram type's schema).
- **Reuse over reinvention**: entry ids SHALL use the `ShortGuid` type from `EtAlii.Adp` (`short-guid` spec) rather than introducing a second, competing identifier representation, and node icons SHALL use the client's existing `@mdi/font` dependency rather than adding a second icon package.
- **Single shared hierarchy model per project**: the in-memory model backing Requirement 4.5's push messages (Requirement 4.8) SHALL be owned by the backend's gRPC service and scoped per project, not per connection — simultaneously connected clients for the same project SHALL observe the same ids for the same entries because they share one model, not independent copies.

### Performance

- Listing a folder's direct children SHALL NOT require reading file contents or recursing into subfolders — depth is client-driven via expand-on-demand (Requirement 2.2), so opening a project with a very large tree remains fast.
- A burst of filesystem changes (e.g. many files touched by a version-control operation) SHALL be coalesced by the watcher before pushing to clients where possible, so the explorer does not thrash with one message per individual file event.

### Security

- File-system access for hierarchy listing and watching SHALL be scoped to the project's root folder exactly as diagram file access already is (tech.md; `login-project-selection` Requirement 5.3), never to the whole machine.
- A user SHALL only be able to list or watch the hierarchy of a project they are authorized to access, per `login-project-selection` Non-Functional: Security.
- Hierarchy-change messages for a project SHALL only be pushed to clients currently connected to that project's explorer, never broadcast to clients viewing other projects.

### Reliability

- Hierarchy listing failures (missing/inaccessible folder) SHALL degrade to a clear, scoped error rather than crashing the workspace shell or leaving it in an inconsistent state.
- IF the filesystem watcher itself fails to start or stops unexpectedly (e.g. the root folder becomes inaccessible) THEN the system SHALL surface this the same way as Requirement 4.3, and SHALL attempt to restart the watcher once the root folder is accessible again rather than leaving the explorer permanently stale.

### Usability

- The explorer's folder/file presentation SHOULD follow familiar IDE conventions (indentation, expand/collapse chevrons), per tech.md's "familiar surface" development-tools guidance and `adp-diagram-ide`'s VS Code-like usability goal.
- Folder and file icons (Requirement 3.6–3.9) SHALL be drawn from Material Design Icons, matching the icon system already in use elsewhere in the client, rather than a one-off icon set introduced just for the explorer.
