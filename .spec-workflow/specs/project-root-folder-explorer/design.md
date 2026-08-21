# Design Document

## Overview

This design adds a new backend gRPC service, `HierarchyService`, that resolves a project's root folder (Requirement 1), lists its file/folder hierarchy on demand (Requirement 2), and streams live changes to it (Requirement 4) — backed by a per-project, in-memory hierarchy model fed by a single `FileSystemWatcher` instance per project, configured with `IncludeSubdirectories = true` so that one watcher covers the entire folder hierarchy rather than one watcher per folder. On the client, a new explorer tree component (living inside `adp-diagram-ide`'s workspace-shell side panel) consumes both calls to populate and keep the tree current (Requirement 3), rendering Material Design Icons for every node.

## Steering Document Alignment

### Technical Standards (tech.md)

* gRPC is the sole frontend-backend transport, per "the communication between the frontend and backend services will be gRPC based" — `HierarchyService` follows the same unary + server-streaming split already used by `ProjectService` (unary) and `AdpService` (streaming), rather than inventing a third transport.
* "Use the Base36 based ShortId everywhere where an ID or identity is needed" — every hierarchy entry id is `EtAlii.Adp.ShortGuid`, carried on the wire as `shared.proto`'s `ShortGuid` message (the same 16-byte contract `projects.proto` already uses for `Project.id`), not a bespoke string id.
* "File-system access from the backend is scoped to explicitly opened workspace folders, not the whole machine" — `HierarchyService` never resolves a path outside the project's root folder (Requirement 1.4/2.3), enforced the same way for both listing and watching.
* "State changes flow from backend to client as a gRPC stream... so multiple clients... stay in sync without manual refresh" — `WatchHierarchy` is that stream for the file/folder hierarchy, mirroring `AdpService.Connect`'s role for diagram deltas.
* No database: the hierarchy model is derived from disk and held in memory only for the lifetime of an open project session (Requirement 2.6), never persisted, consistent with the "File-based storage over a database" decision log entry.

### Project Structure (structure.md)

* `HierarchyService`'s backend implementation lives in `EtAlii.Adp.Backend` (core, not diagram-specific), in a new `Hierarchy/` folder alongside the existing `Projects/`, `Sessions/`, `Authentication/`, `Client/` folders — mirroring `Projects/`'s `ProjectServiceImpl` + `IProjectStore` split.
* Its `.proto` contract, `hierarchy.proto`, lives in the shared `src/api/` folder alongside `projects.proto`/`connection.proto`/`shared.proto`, importing `shared.proto` for `ShortGuid` exactly as `projects.proto` does.
* The client's explorer tree component lives in `client/` alongside `adp-diagram-ide`'s workspace shell (the side panel it plugs into), not inside any `diagrams/<diagram>/client/` folder — it has no diagram-type-specific knowledge.

## Code Reuse Analysis

### Existing Components to Leverage

* **`SessionContext`** (`EtAlii.Adp.Backend.Sessions`): `HierarchyService`'s calls read the authenticated user id from `ServerCallContext.UserState` the same way `ProjectServiceImpl` does, so authorization (Non-Functional: Security) reuses the existing session interceptor rather than a new mechanism.
* **`IProjectStore`** (`EtAlii.Adp.Backend.Projects`): resolving a `project_id` to its root folder path (and re-validating it resolves to an accessible folder, Requirement 1.2) reuses the existing per-user project store rather than duplicating project lookup.
* **`ShortGuid` / `EtAlii.Adp.Contracts.ShortGuid`** (`EtAlii.Adp` library, `ShortGuid.Cast.cs`): entry ids reuse the exact same type and implicit wire conversion `ProjectRecord.Id` already uses — no new id type.
* **`@mdi/font` client integration** (`main.tsx`, `ProjectGridPage.tsx`): the explorer's node icons (Requirement 3.6–3.9) reuse this existing import rather than adding a package.
* **`adp-diagram-ide` workspace shell side panel**: the explorer tree is a new component rendered inside that existing panel, not a new panel.

### Integration Points

* **`login-project-selection`'s `Project.path`**: `HierarchyService` resolves a project's root folder through `IProjectStore`, the same store `ProjectService` already owns — no second source of truth for "where is this project's folder."
* **`adp-diagram-ide` Requirement 1 (workspace shell) / Requirement 3 (`AdpService` change feed)**: the explorer tree is a new leaf under the existing side panel, and `WatchHierarchy` is a second, independent change-feed stream opened alongside `AdpService.Connect` when the workspace shell opens (Requirement 3.11) — the two streams don't share a connection, since a project's hierarchy and an individual open diagram's elements have different lifecycles (one per project vs. one per open diagram tab).
* **`rename-files-and-folders`** (future spec, blocked on this one being implemented): will add a `RenameEntry` RPC to `hierarchy.proto` and reuse `HierarchyModel`'s id-preserving rename handling (Requirement 4.5) directly — this design's id stability is what makes that spec's "keep the same node identity across a rename" requirement possible.

## Architecture

```mermaid
graph TD
    FSW[FileSystemWatcher<br/>one instance per project<br/>IncludeSubdirectories = true] -->|create/delete/rename events| Model[HierarchyModel<br/>in-memory, per project]
    Registry[HierarchyModelRegistry<br/>ref-counted by open streams] -->|owns| Model
    Registry -->|owns| FSW
    Store[IProjectStore] -->|resolves + re-validates root path| Service[HierarchyServiceImpl]
    Service -->|ListEntries unary| Model
    Service -->|WatchHierarchy stream| Registry
    Model -->|HierarchyChange messages| Service
    Service -->|gRPC-web| Client[ExplorerTreePanel<br/>client]
    Client -->|renders| Icons[MDI icon mapping]
```

* `HierarchyModelRegistry` is the connection-lifecycle owner Requirement 4.8 describes: it creates a project's `HierarchyModel` + `FileSystemWatcher` pair on the first `WatchHierarchy` connection for that project, hands the same instances to every subsequent connection for that project, and disposes both once the last connection closes.
* `HierarchyModel` is the "in-memory model" of Requirement 4.8: a dictionary of entries keyed by `ShortGuid`, populated lazily as `ListEntries` lists folders and as the watcher observes changes, never eagerly walking the whole tree (Performance NFR).

### Modular Design Principles

* **Single file responsibility**: `HierarchyServiceImpl` (gRPC surface), `HierarchyModel` (tree + id state), `HierarchyModelRegistry` (connection-scoped lifecycle), and the watcher wrapper are four separate classes, mirroring how `Projects/` already splits `ProjectServiceImpl` from `IProjectStore`/`FileProjectStore`.
* **No diagram coupling**: `hierarchy.proto` and `Hierarchy/` depend on nothing from `elements.proto`/`deltas.proto` — a hierarchy entry is a name + kind + id, not a diagram element.
* **Testability**: `HierarchyModel`'s id assignment, containment checks, and event-to-message translation are plain, watcher-independent logic, unit-testable without a running `FileSystemWatcher` or gRPC server (tests can feed it synthetic events).

## Components and Interfaces

### `HierarchyService` (backend, `EtAlii.Adp.Backend`, gRPC contract)

* **Purpose:** List a folder's direct children (Requirement 2) and stream hierarchy changes (Requirement 4) for one project.
* **Interfaces:** `ListEntries(ListEntriesRequest) returns (ListEntriesResponse)` (unary); `WatchHierarchy(WatchHierarchyRequest) returns (stream HierarchyChange)` (server-streaming).
* **Dependencies:** `IProjectStore` (root path resolution/re-validation), `IHierarchyModelRegistry`.
* **Reuses:** `SessionContext` for the authenticated user id on every call.

### `IHierarchyModelRegistry` / `HierarchyModelRegistry` (backend, `EtAlii.Adp.Backend.Hierarchy`)

* **Purpose:** Own the connection-scoped lifecycle from Requirement 4.4/4.8 — one shared `HierarchyModel` + watcher per project, created on first `WatchHierarchy` connection, disposed on last disconnect.
* **Interfaces:** `HierarchyModel GetOrCreate(ShortGuid projectId, string rootPath)`, `void Release(ShortGuid projectId)` (ref-counted; calls `Dispose` on the model/watcher pair when the count reaches zero).
* **Dependencies:** None beyond what it's handed (root path comes from `IProjectStore` via `HierarchyServiceImpl`).
* **Reuses:** N/A — this is the new shared-state owner other pieces reuse.
* **Design note:** if `ListEntries` is called for a project before any `WatchHierarchy` connection exists (Requirement 3.11's normal order is list-then-watch), the registry lazily creates the model on that first `ListEntries` call too, but the *ref count* only tracks `WatchHierarchy` connections per Requirement 4.8. As a safety net against a client that lists but never opens the stream (e.g., navigates away first), the registry evicts an unreferenced, watcher-less model after a short idle timeout rather than holding it forever.

### `HierarchyModel` (backend, `EtAlii.Adp.Backend.Hierarchy`)

* **Purpose:** The in-memory tree for one project: entries keyed by `ShortGuid` (Requirement 2.5/2.6), containment-checked path resolution (Requirement 1.4/2.3), and watcher-event-to-`HierarchyChange` translation (Requirement 4.5–4.7).
* **Interfaces:** `IReadOnlyList<Entry> ListChildren(ShortGuid? folderId)` (null = root); `void OnWatcherEvent(WatcherChangeTypes, string oldPath, string newPath)`; `void Reconcile()` (full re-scan, used for Requirement 4.6's buffer-overflow recovery); event `EntryChanged` that `HierarchyServiceImpl` subscribes each connected `WatchHierarchy` call to.
* **Dependencies:** The project's resolved root folder path.
* **Reuses:** `ShortGuid.NewShortGuid()` for id assignment (Requirement 2.6).

### `RootFolderWatcher` (backend, `EtAlii.Adp.Backend.Hierarchy`)

* **Purpose:** Thin wrapper around a single .NET `FileSystemWatcher` instance, scoped to one project's root folder with `IncludeSubdirectories = true` — one watcher per project covers that project's entire folder hierarchy, never one watcher per folder — forwarding create/delete/rename/error events to its owning `HierarchyModel` (Requirement 4.4).
* **Interfaces:** Constructor takes the root path and a callback; `Dispose()` stops watching.
* **Dependencies:** `System.IO.FileSystemWatcher`.
* **Reuses:** N/A — first use of `FileSystemWatcher` in the codebase.

### `ExplorerTreePanel` (client, `client/`)

* **Purpose:** Renders the hierarchy inside the workspace shell's side panel (Requirement 3): fetches via `ListEntries` on expand, applies `WatchHierarchy` messages by id (Requirement 3.10), and renders MDI icons per node (Requirement 3.6–3.9).
* **Interfaces:** React component; local state is a `Map<ShortGuid, TreeNode>` mirroring the backend's id-keyed model, so an incoming change message is a direct map lookup, not a tree walk.
* **Dependencies:** A generated `HierarchyServiceClient` (grpc-web).
* **Reuses:** `@mdi/font` (already imported in `main.tsx`), the workspace shell's existing side-panel slot from `adp-diagram-ide`.

## Data Models

### `hierarchy.proto`

```protobuf
syntax = "proto3";

package etalii.adp;

import "shared.proto"; // ShortGuid

option csharp_namespace = "EtAlii.Adp";

enum EntryKind {
  ENTRY_KIND_UNSPECIFIED = 0;
  FILE = 1;
  FOLDER = 2;
}

message Entry {
  ShortGuid id = 1;
  ShortGuid parent_id = 2; // unset for a root-level entry
  string name = 3;
  EntryKind kind = 4;
  bool available = 5; // false when Requirement 2.4 applies (permissions, deletion)
}

message ListEntriesRequest {
  ShortGuid project_id = 1;
  ShortGuid folder_id = 2; // unset = list the root folder's direct children
}

message ListEntriesError {
  string message = 1;
}

message Entries {
  repeated Entry entries = 1;
}

message ListEntriesResponse {
  oneof result {
    Entries entries = 1;
    ListEntriesError error = 2;
  }
}

message WatchHierarchyRequest {
  ShortGuid project_id = 1;
}

message EntryCreated {
  Entry entry = 1; // parent_id/name/kind resolved from the model, per Requirement 4.5
}

message EntryRemoved {
  ShortGuid entry_id = 1;
}

message EntryRenamed {
  ShortGuid entry_id = 1;
  string new_name = 2;
}

message RootUnavailable {
  string message = 1; // Requirement 4.3
}

message HierarchyChange {
  oneof change {
    EntryCreated created = 1;
    EntryRemoved removed = 2;
    EntryRenamed renamed = 3;
    RootUnavailable root_unavailable = 4;
  }
}

service HierarchyService {
  rpc ListEntries(ListEntriesRequest) returns (ListEntriesResponse);
  rpc WatchHierarchy(WatchHierarchyRequest) returns (stream HierarchyChange);
}
```

### `HierarchyModel` in-memory entry (backend, not a proto)

```
EntryNode:
  Id: ShortGuid
  ParentId: ShortGuid?     // null for a root-level entry
  Name: string
  IsFolder: bool
  Available: bool
```

Held only in memory per `HierarchyModelRegistry`'s lifecycle (Requirement 2.6) — never written to disk, since the folder tree itself is the source of truth (Alignment with Product Vision).

## Error Handling

### Error Scenarios

1. **Root folder inaccessible at project-open time (Requirement 1.2/1.3)**
   * **Handling:** The first `ListEntries` call for a project (root, `folder_id` unset) re-validates the path via `IProjectStore`; if it no longer resolves, `ListEntriesResponse.error` is returned and `HierarchyModelRegistry` never creates a model for that project.
   * **User Impact:** Workspace-shell entry is aborted with a clear error (per `adp-diagram-ide`'s shell-entry flow); the project stays in the user's list untouched.

2. **A listed folder becomes inaccessible later (Requirement 2.4)**
   * **Handling:** `HierarchyModel` marks that entry `available = false` rather than removing it or failing the containing `ListEntries`/`WatchHierarchy` call.
   * **User Impact:** The folder renders with the distinct unavailable icon/treatment (Requirement 3.9) instead of disappearing or erroring the whole tree.

3. **The root folder itself becomes inaccessible while open (Requirement 4.3)**
   * **Handling:** `RootFolderWatcher` reports the failure to `HierarchyModel`, which pushes `RootUnavailable` on every open `WatchHierarchy` stream for that project and attempts to restart the watcher once the root is reachable again (Reliability NFR).
   * **User Impact:** The explorer shows a clear, scoped error banner rather than a stale or silently empty tree.

4. **Watcher buffer overflow (Requirement 4.6)**
   * **Handling:** `HierarchyModel.Reconcile()` re-scans everything currently in the model against disk, preserving existing ids and only assigning new ones for genuinely new entries.
   * **User Impact:** Invisible under normal use — the tree simply stays correct through a burst of missed low-level events.

5. **Path escape attempt (`..`, symlink) (Requirement 1.4/2.3)**
   * **Handling:** `HierarchyModel` resolves every path against the project's root using `Path.GetFullPath` + a strict prefix check (and resolves symlink targets before that check), rejecting anything that would resolve outside it; such a request is treated as an invalid/not-found entry, never as a folder to list.
   * **User Impact:** No content from outside the project is ever observable through the explorer, regardless of how the request was shaped.

## Testing Strategy

### Unit Testing

* `HierarchyModel`: id assignment and stability across relist/rename (Requirement 2.5/2.6), root-containment rejection for `..`/symlink paths (Requirement 1.4/2.3), watcher-event → `HierarchyChange` translation (create carries parent id/name/kind; delete/rename carry existing id), and `Reconcile()`'s id-preserving behavior on synthetic "buffer overflow" input.
* `HierarchyModelRegistry`: create-on-first-connect / dispose-on-last-disconnect ref-counting, and the idle-timeout eviction path for a model with no `WatchHierarchy` connection.
* Client `ExplorerTreePanel` state reducer: applying created/removed/renamed messages by id against a synthetic tree, independent of any real gRPC call.

### Integration Testing

* `HierarchyServiceImpl.ListEntries`/`WatchHierarchy` against a real temporary folder: expand-on-demand returns only direct children; two simultaneous `WatchHierarchy` connections for the same project observe the same ids for the same entries (Code Architecture: single shared model).
* A real `FileSystemWatcher` against that temp folder: create/delete/rename on disk produce the expected `HierarchyChange` messages, including a rename preserving the entry's id (the guarantee `rename-files-and-folders` will depend on).
* Root folder deleted out from under an open project: `RootUnavailable` is pushed, and recreating the folder allows the watcher to recover per the Reliability NFR.

### End-to-End Testing

* Local F5 scenario: open a project → explorer shows its real files/folders with correct icons → create/rename/delete a file with the OS file explorer, outside the IDE → the tree updates without a manual refresh, without collapsing an already-expanded sibling folder or losing the current selection.
