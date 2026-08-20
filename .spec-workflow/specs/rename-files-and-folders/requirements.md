# Requirements Document

## Introduction

This spec adds the ability to rename a file or folder directly from the workspace explorer introduced by [`project-root-folder-explorer`](../project-root-folder-explorer/requirements.md). It operates on the same tree that spec populates and validates, so it depends on that spec's hierarchy-listing capability and explorer UI already existing.

**Sequencing note:** design work on this spec SHALL NOT begin until `project-root-folder-explorer` has been *implemented* (not merely designed or approved). This requirements document is written now so it is ready to move into design as soon as that dependency is satisfied — do not proceed to this spec's design phase before confirming `project-root-folder-explorer`'s tasks are complete.

## Alignment with Product Vision

- **"Files are the source of truth"** ([product.md](../../steering/product.md)): a rename is a real filesystem operation on the user's actual files, not a metadata-only relabeling that could drift from disk.
- [tech.md](../../steering/tech.md)'s **"The backend is the sole owner of reading/writing diagram files; the web client never touches the filesystem directly, it only talks gRPC to the backend"**: renaming SHALL be performed by the backend on request, not simulated client-side.
- tech.md's **"File-system access from the backend is scoped to explicitly opened workspace folders, not the whole machine"**: a rename target SHALL be bound by the same containment guarantee already established for hierarchy listing.
- **"A familiar surface"** (product.md): rename-in-place from the explorer is a baseline VS Code-like interaction (`adp-diagram-ide`'s usability goal).

## Requirements

### Requirement 1 — Renaming a file or folder from the explorer

**User Story:** As a diagram author, I want to rename a file or folder directly from the workspace explorer, so that I can reorganize my project without leaving the IDE.

#### Acceptance Criteria

1. WHEN the user chooses to rename a file or folder node in the explorer (per `project-root-folder-explorer` Requirement 3) THEN the system SHALL let them edit its name in place before committing the change.
2. WHEN the user commits a rename THEN the system SHALL rename the corresponding file or folder on disk within the project's root folder.
3. WHEN a rename completes THEN the system SHALL update the explorer to reflect the new name without requiring a manual refresh, consistent with `project-root-folder-explorer` Requirement 4.1's push-based hierarchy updates.
4. IF the user cancels the rename (e.g. presses Escape, or clicks away without committing) THEN the system SHALL leave the file or folder unchanged.

### Requirement 2 — Validating a rename before it happens

**User Story:** As a user, I want invalid or unsafe renames rejected with a clear reason, so that I don't accidentally corrupt my project or lose files.

#### Acceptance Criteria

1. WHEN a new name is submitted THEN the system SHALL reject an empty name or one containing characters not valid for the host filesystem, with a clear error, before attempting the rename.
2. IF a file or folder with the target name already exists in the same parent folder THEN the system SHALL reject the rename rather than silently overwriting the existing entry.
3. WHEN validating a rename request THEN the system SHALL only allow the item to keep its current parent folder — a rename changes the name, not the location; moving an item to a different folder is out of scope for this spec.
4. WHEN resolving the rename target path THEN the system SHALL enforce the same root-folder containment guarantee already required for hierarchy listing (`project-root-folder-explorer` Requirement 1.4), rejecting any target that would resolve outside the project's root folder.
5. IF the rename fails at the filesystem level (e.g. permissions, file in use) THEN the system SHALL surface a clear error and SHALL leave the original file or folder intact.

### Requirement 3 — Renaming folders with contents

**User Story:** As a diagram author, I want to rename a folder that has files and subfolders inside it, so that reorganizing doesn't require renaming everything one by one.

#### Acceptance Criteria

1. WHEN a folder containing files or subfolders is renamed THEN the system SHALL rename only that folder, preserving its entire contents and nested structure unchanged beneath the new name.
2. WHEN a renamed folder was already expanded in the explorer (per `project-root-folder-explorer` Requirement 3.3) THEN the system SHALL continue to show its previously-fetched children under the new name without an unnecessary re-fetch.

### Requirement 4 — Effect on open diagrams and links

**User Story:** As a diagram author, I want to understand what happens to open diagrams and links when I rename a file, so that I'm not surprised by lost work or broken references.

#### Acceptance Criteria

1. IF the file being renamed is currently open in an editor tab THEN the system SHALL keep the tab open against the renamed file rather than closing it or leaving it pointing at the old, now-nonexistent path.
2. IF the file being renamed has unsaved changes THEN the system SHALL complete the rename without losing those changes, or SHALL block the rename until the changes are saved or discarded — the system SHALL NOT silently discard unsaved edits as a side effect of a rename.
3. This spec does NOT require rewriting path references held by diagram-type-specific data (e.g. a mindmap node's code-artifact link, per `mindmap-diagram` Requirement 6). Such links already degrade to a clearly-indicated "broken" state when their target no longer exists (`mindmap-diagram` Requirement 6.4); that remains the accepted behavior after a rename, rather than this spec introducing reference-rewriting.

## Non-Functional Requirements

### Code Architecture and Modularity

- **Single Responsibility**: rename is a mutation on the same root-folder-scoped file-access layer `project-root-folder-explorer` introduces for hierarchy listing — it SHALL reuse that layer rather than opening a second, parallel filesystem access path.
- **Modular Design**: rename-in-place UI logic lives in the explorer component from `project-root-folder-explorer`; it SHALL NOT be duplicated per diagram type.

### Performance

- A rename SHALL be a single, direct filesystem operation (no recursive copy-then-delete of a folder's contents), so renaming a large folder is not noticeably slower than renaming an empty one.

### Security

- Rename SHALL be scoped to the project's root folder exactly as hierarchy listing already is (`project-root-folder-explorer` Requirement 1.4, Requirement 2.3), never able to reach outside it.
- A user SHALL only be able to rename within a project they are authorized to access (their own project list, per `login-project-selection` Requirement 5.1).

### Reliability

- A rename SHALL be atomic from the caller's perspective: it either fully succeeds (old name gone, new name present, contents intact) or fully fails (original untouched) — no partially-renamed state SHALL be left behind or exposed to the client.

### Usability

- Triggering rename SHOULD follow familiar IDE conventions (e.g. a context-menu "Rename" action, and/or the F2 keyboard shortcut), per tech.md's "familiar surface" development-tools guidance.
