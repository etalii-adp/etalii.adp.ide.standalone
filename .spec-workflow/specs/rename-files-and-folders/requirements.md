# Requirements Document

## Introduction

This spec adds the ability to rename and delete a file or folder directly from the workspace explorer introduced by [`project-root-folder-explorer`](../project-root-folder-explorer/requirements.md), triggered consistently through two paths: a right-click (or keyboard-equivalent) context menu built on [`reusable-context-menu`](../reusable-context-menu/requirements.md), and dedicated keyboard shortcuts — `F2` for rename, `Delete` for delete — that act on whichever entry currently has keyboard focus in the tree. Both paths resolve to the same underlying actions, and both are populated and gated by a single source of truth: the backend, which the client asks which actions currently apply to a given entry — including, for each, its id, label, icon, and keyboard shortcut (if it has one) — rather than deciding any of this itself from hard-coded client-side rules. This is also what lets a later spec add a diagram-type-specific action, complete with its own shortcut, for particular file types without this spec's client-side menu- or key-handling code changing. Renaming is confirmed through a small, purpose-built dialog (built on the existing `Dialog` primitive) whose Rename button only enables once the new name is non-empty, valid, and actually different from the current name; deleting is confirmed through the existing `ConfirmDialog`, mirroring the pattern already established for removing a project from the grid. This spec also formalizes the explorer tree itself as keyboard-navigable, with the currently-focused entry always visibly indicated, since that focused entry is exactly what `F2` and `Delete` act on.

**Dependencies:** this spec builds on three now-implemented pieces: [`project-root-folder-explorer`](../project-root-folder-explorer/requirements.md) (the tree itself, hierarchy listing and live-sync, stable entry ids), [`reusable-context-menu`](../reusable-context-menu/requirements.md) (the `ContextMenu` primitive this spec's Requirement 3 renders), and the `Dialog`/`ConfirmDialog` primitives (this spec's Requirement 5 rename dialog and Requirement 11 delete confirmation are both built on top of them). All three are implemented, so this spec is ready to move directly into design.

## Alignment with Product Vision

- **"Files are the source of truth"** ([product.md](../../steering/product.md)): rename and delete are both real filesystem operations performed by the backend, not metadata-only relabeling or app-managed undoable state that could drift from disk.
- [tech.md](../../steering/tech.md)'s **"The backend is the sole owner of reading/writing diagram files; the web client never touches the filesystem directly, it only talks gRPC to the backend"**: both rename and delete SHALL be performed by the backend on request, not simulated client-side.
- tech.md's **"File-system access from the backend is scoped to explicitly opened workspace folders, not the whole machine"**: rename and delete targets SHALL both be bound by the same containment guarantee already established for hierarchy listing.
- **"A familiar surface"** (product.md) / `adp-diagram-ide`'s usability goal: a right-click menu, `F2` rename, and `Delete` are baseline VS Code-like conventions. The original draft of this spec only listed these as a Usability "SHOULD"; this revision makes them this spec's actual, required mechanism.
- [`reusable-context-menu`](../reusable-context-menu/requirements.md): this spec is the first real consumer of its composable `ContextMenu` — Rename and Delete become one group of items rendered by that component, which remains entirely unaware of files, folders, or what either action does (that spec's own Requirement 5/NFR Single Responsibility).
- The `Dialog` primitive and its recent `ConfirmDialog` extension (already used to confirm removing a project from the grid, naming the project and using a danger-colored confirm button): this spec reuses both directly — `ConfirmDialog` as-is for delete, and a small custom dialog built on `Dialog` for rename — rather than inventing a second confirmation or dialog mechanism, per `Dialog`'s own documented "a custom dialog is just `Dialog` rendered with developer-supplied content, whose footer buttons a consumer wires to that content's own callbacks" pattern.
- [structure.md](../../steering/structure.md)'s **core-vs-diagram-type-plugin dependency direction**: the backend capability this spec adds for discovering an entry's available actions (Requirement 2) is what lets a future diagram-type module contribute path-specific actions later without core `Hierarchy` code ever depending on that diagram type — mirroring `reusable-context-menu`'s own composability alignment point.

## Requirements

### Requirement 1 — Keyboard-navigable tree with a visibly indicated focused entry

**User Story:** As a keyboard user, I want to move focus through the explorer tree with the keyboard and always clearly see which entry is focused, so that I know what the shortcuts in Requirement 4, or a context menu I open, will act on.

#### Acceptance Criteria

1. WHEN the explorer tree has keyboard focus THEN the system SHALL visibly indicate exactly one entry as focused at all times, using a treatment distinct from hover, matching the same focus-visible theming standard already used elsewhere in the client (e.g. `Dialog`'s and `reusable-context-menu`'s focus ring).
2. WHEN the user presses `ArrowDown` or `ArrowUp` while an entry is focused THEN the system SHALL move focus to the next or previous entry as currently rendered — i.e. respecting which folders are expanded (`project-root-folder-explorer` Requirement 3.2/3.3) — skipping nothing.
3. WHEN the user presses `ArrowRight` on a focused, collapsed folder THEN the system SHALL expand it (`project-root-folder-explorer` Requirement 3.2); WHEN pressed on a focused folder that is already expanded and has children THEN the system SHALL move focus to its first child instead.
4. WHEN the user presses `ArrowLeft` on a focused, expanded folder THEN the system SHALL collapse it without moving focus; WHEN pressed on a focused file, or a focused folder that is already collapsed (or has no children) THEN the system SHALL move focus to its parent folder, if any.
5. WHEN the explorer tree receives focus from outside it (e.g. via `Tab`) THEN the system SHALL focus a single, predictable entry — the previously-focused entry if it still exists, otherwise the first root-level entry — rather than requiring the user to tab through every rendered node individually to reach the tree's content, mirroring the roving-tabindex approach `reusable-context-menu`'s own `ContextMenuLevel` already established in this client.

### Requirement 2 — Discovering an entry's available actions from the backend

**User Story:** As a developer who will extend the explorer with more actions later, and as a user who should only see actions that actually apply, I want the set of actions available for an entry — including each one's keyboard shortcut, if it has one — to come from the backend rather than being hard-coded in the client.

#### Acceptance Criteria

1. WHEN the client needs an entry's currently-available actions — to populate its context menu (Requirement 3) or to decide whether a keyboard shortcut applies (Requirement 4) — THEN the system SHALL query the backend for that entry's available actions rather than the client deciding this itself from static, client-side rules.
2. WHEN the backend determines an entry's available actions THEN the system SHALL base that decision on the entry itself, resolved to its actual location on disk (i.e. its path and/or kind), not on anything the client supplies beyond identifying which entry it means.
3. WHEN the backend returns an entry's available actions THEN each SHALL carry enough information for the client to render it directly as a `reusable-context-menu` item and, without the client needing a hard-coded label, icon, or shortcut per action id: a stable action identifier, a display label, an icon, and — when the action has one — a keyboard shortcut.
4. WHEN an action's keyboard shortcut is included THEN the system SHALL express it in a form the client can match directly against a key event (the key, plus any modifiers), so the client only needs to compare an incoming keypress against whichever shortcuts the currently-focused entry's actions carry — it SHALL NOT need a separate, client-maintained table of "which key means which action."
5. This spec's own backend logic SHALL report exactly two possible actions for any file or folder entry — Rename (Requirement 5, subject to Requirement 6's validity rules), carrying the `F2` shortcut, and Delete (Requirement 9), carrying the `Delete` shortcut — each reported as unavailable, rather than omitted, when it currently cannot be performed, consistent with `reusable-context-menu` Requirement 4's "visible but disabled, not omitted" treatment, so the reason can be surfaced the same way. WHEN an action is reported as unavailable THEN its shortcut SHALL be reported as unavailable along with it (Requirement 4.2's "no effect" behavior applies uniformly to both triggers).
6. A future spec MAY extend the set of actions this capability returns for a given entry (e.g. a diagram-type-specific action for a particular file extension, complete with its own shortcut) without requiring this spec's context-menu- or keyboard-shortcut-triggering code to change, per structure.md's core-vs-diagram-type-plugin dependency direction.
7. IF the entry no longer exists by the time its available actions are requested (e.g. deleted by another process between being listed and being right-clicked) THEN the system SHALL report no available actions for it rather than actions that would fail if invoked.

### Requirement 3 — Context menu for an explorer entry

**User Story:** As a user, I want to right-click a file or folder in the explorer and see the actions I can currently take on it, so that rename, delete, and future actions are discoverable without memorizing a shortcut for each.

#### Acceptance Criteria

1. WHEN the user right-clicks an entry in the explorer THEN the system SHALL open `reusable-context-menu`'s `ContextMenu` at the pointer position, populated with that entry's currently-available actions (Requirement 2), and SHALL give that entry keyboard focus (Requirement 1) at the same time, so the menu and the keyboard shortcuts (Requirement 4) always act on the same entry.
2. WHEN the user invokes the keyboard-accessible equivalent of a right-click on the focused entry (e.g. the keyboard "context menu" key, or `Shift+F10`) THEN the system SHALL open the same context menu, positioned near the focused entry, without requiring a mouse.
3. WHEN the context menu's Rename or Delete item is selected THEN the system SHALL trigger exactly the same action Requirement 4's corresponding keyboard shortcut triggers for that entry — one action implementation behind both triggers, not two.
4. This spec relies on `reusable-context-menu`'s own requirements for the menu's rendering, positioning, disabled-item treatment (e.g. an action Requirement 2 reports as currently unavailable renders disabled per that spec's Requirement 4), and focus-return-on-close behavior (that spec's Requirement 6.5); it does not redefine any of that here.

### Requirement 4 — Keyboard shortcuts on the focused entry

**User Story:** As a keyboard user, I want to press `F2` or `Delete` on the file or folder I've focused in the explorer, so that I can rename or delete without reaching for the mouse — and as a developer adding a future action, I want a shortcut I attach to it on the backend to just work, without touching the explorer's key-handling code.

#### Acceptance Criteria

1. WHEN an entry in the explorer has keyboard focus (Requirement 1) and the user presses a key (with modifiers, if any) matching the shortcut of one of that entry's currently-available actions (Requirement 2) THEN the system SHALL trigger that action, exactly as selecting it from the context menu would (Requirement 3.3).
2. WHEN the pressed key matches no shortcut among the focused entry's currently-available actions — including a key that would match an action reported as currently unavailable (Requirement 2.5) — THEN the system SHALL NOT trigger anything; a shortcut that doesn't currently apply has no fallback behavior.
3. The client SHALL NOT hard-code which key triggers Rename or Delete (or any future action); it SHALL match pressed keys only against the shortcuts Requirement 2 reports for the focused entry, so this spec's own `F2`/`Delete` bindings, and any a future spec adds, are entirely backend-supplied data.
4. These shortcuts SHALL act on exactly the single currently-focused entry; this spec does not support multi-selecting several entries to rename or delete at once.
5. WHEN focus is not within the explorer tree (e.g. an editor tab or another panel has focus) THEN none of an entry's action shortcuts SHALL trigger, so these shortcuts don't unexpectedly fire while the user is typing or working elsewhere.

### Requirement 5 — Renaming a file or folder via a dialog

**User Story:** As a diagram author, I want to rename a file or folder directly from the workspace explorer, so that I can reorganize my project without leaving the IDE.

#### Acceptance Criteria

1. WHEN the user triggers Rename for a file or folder entry (its context menu action, Requirement 3, or the `F2` shortcut, Requirement 4) THEN the system SHALL open a rename dialog, pre-filled with that entry's current name, rather than editing the name in place within the tree.
2. WHILE the rename dialog is open THEN the system SHALL disable its confirming (Rename) button whenever the entered name is empty, contains a character invalid for the host filesystem (Requirement 6.1), or is unchanged from the entry's original name — only a non-empty, valid, and actually-different name enables it.
3. WHEN the rename dialog's Rename button is enabled and the user activates it THEN the system SHALL rename the corresponding file or folder on disk within the project's root folder.
4. WHEN a rename completes THEN the system SHALL close the dialog and update the explorer to reflect the new name without requiring a manual refresh, consistent with `project-root-folder-explorer` Requirement 4.1's push-based hierarchy updates.
5. IF the user cancels the dialog (its Cancel button, or `Escape`) THEN the system SHALL close it without renaming anything, per `Dialog`'s existing close behavior.

### Requirement 6 — Validating a rename before it happens

**User Story:** As a user, I want invalid or unsafe renames rejected with a clear reason, so that I don't accidentally corrupt my project or lose files.

#### Acceptance Criteria

1. WHEN a new name is submitted (i.e. the dialog's Rename button is activated, Requirement 5.3) THEN the system SHALL, on the backend, reject an empty name or one containing characters not valid for the host filesystem, with a clear error, before attempting the rename — the same rule Requirement 5.2 already prevents submitting from the dialog, enforced again server-side so the check cannot be bypassed.
2. IF a file or folder with the target name already exists in the same parent folder THEN the system SHALL reject the rename rather than silently overwriting the existing entry.
3. WHEN validating a rename request THEN the system SHALL only allow the item to keep its current parent folder — a rename changes the name, not the location; moving an item to a different folder is out of scope for this spec.
4. WHEN resolving the rename target path THEN the system SHALL enforce the same root-folder containment guarantee already required for hierarchy listing (`project-root-folder-explorer` Requirement 1.4), rejecting any target that would resolve outside the project's root folder.
5. IF the rename fails at the filesystem level (e.g. permissions, file in use) THEN the system SHALL surface a clear error in the dialog, SHALL leave the original file or folder intact, and SHALL leave the dialog open so the user can adjust the name or cancel rather than losing their input.

### Requirement 7 — Renaming folders with contents

**User Story:** As a diagram author, I want to rename a folder that has files and subfolders inside it, so that reorganizing doesn't require renaming everything one by one.

#### Acceptance Criteria

1. WHEN a folder containing files or subfolders is renamed THEN the system SHALL rename only that folder, preserving its entire contents and nested structure unchanged beneath the new name.
2. WHEN a renamed folder was already expanded in the explorer (per `project-root-folder-explorer` Requirement 3.3) THEN the system SHALL continue to show its previously-fetched children under the new name without an unnecessary re-fetch.

### Requirement 8 — Effect on open diagrams and links when renaming

**User Story:** As a diagram author, I want to understand what happens to open diagrams and links when I rename a file, so that I'm not surprised by lost work or broken references.

#### Acceptance Criteria

1. IF the file being renamed is currently open in an editor tab THEN the system SHALL keep the tab open against the renamed file rather than closing it or leaving it pointing at the old, now-nonexistent path.
2. IF the file being renamed has unsaved changes THEN the system SHALL complete the rename without losing those changes, or SHALL block the rename until the changes are saved or discarded — the system SHALL NOT silently discard unsaved edits as a side effect of a rename.
3. This spec does NOT require rewriting path references held by diagram-type-specific data (e.g. a mindmap node's code-artifact link, per `mindmap-diagram` Requirement 12). Such links already degrade to a clearly-indicated "broken" state when their target no longer exists (`mindmap-diagram` Requirement 12.4); that remains the accepted behavior after a rename, rather than this spec introducing reference-rewriting.

### Requirement 9 — Deleting a file or folder from the explorer

**User Story:** As a diagram author, I want to delete a file or folder directly from the workspace explorer, so that I can clean up my project without leaving the IDE.

#### Acceptance Criteria

1. WHEN the user triggers Delete for a file or folder entry (its context menu action, Requirement 3, or the `Delete` shortcut, Requirement 4) THEN the system SHALL first show the delete confirmation dialog (Requirement 11) before deleting anything — no entry SHALL be deleted without this confirmation step, regardless of trigger.
2. WHEN the user confirms the delete THEN the system SHALL delete the corresponding file or folder from disk within the project's root folder.
3. WHEN a delete completes THEN the system SHALL update the explorer to reflect the removal without requiring a manual refresh, consistent with `project-root-folder-explorer` Requirement 4.1's push-based hierarchy updates.
4. IF the user cancels the confirmation dialog THEN the system SHALL leave the file or folder unchanged.
5. IF the delete fails at the filesystem level (e.g. permissions, file in use) THEN the system SHALL surface a clear error and SHALL leave the explorer reflecting the entry's actual, unchanged state on disk.
6. WHEN resolving the delete target THEN the system SHALL enforce the same root-folder containment guarantee already required for hierarchy listing and rename (`project-root-folder-explorer` Requirement 1.4, Requirement 6.4), rejecting any target that would resolve outside the project's root folder.
7. Deleting is a direct, immediate filesystem operation with no in-app undo; Requirement 11's confirmation step is the only safeguard against an accidental delete, consistent with product.md's "files are the source of truth" — a delete is real, not app-managed undoable state.

### Requirement 10 — Deleting a folder with contents, and effect on open diagrams

**User Story:** As a diagram author, I want deleting a folder to remove everything inside it, and to understand what happens to any of those files I currently have open, so that cleanup is one action and I'm not surprised by what happens to open work.

#### Acceptance Criteria

1. WHEN a folder containing files or subfolders is deleted THEN the system SHALL delete that folder and everything nested beneath it, recursively.
2. IF a recursive folder delete partially fails partway through (e.g. a locked file deep inside) THEN the system SHALL surface a clear error identifying that the delete did not fully complete, and the explorer SHALL reflect whatever the actual, resulting on-disk state is afterward, rather than claiming the whole folder was removed.
3. IF a file being deleted (directly, or as part of a folder being deleted) is currently open in an editor tab THEN the system SHALL close that tab, since keeping it open against a file that no longer exists on disk is meaningless — this differs from rename's Requirement 8.1, where the same file still exists under its new name.
4. IF one or more files being deleted (directly, or as part of a folder being deleted) are currently open in an editor tab with unsaved changes THEN the confirmation dialog (Requirement 11) SHALL make that clear before the user confirms, so cancelling remains possible while those changes still exist — the system SHALL NOT silently discard unsaved edits as a side effect of a delete.

### Requirement 11 — The delete confirmation dialog

**User Story:** As a user, I want a clear confirmation step before an irreversible delete, so that I don't lose files to an accidental click or keypress.

#### Acceptance Criteria

1. WHEN delete is triggered for an entry (Requirement 9.1) THEN the system SHALL show a confirmation dialog naming that specific file or folder by name and stating plainly that the action deletes it from disk and cannot be undone, reusing the same confirmation-dialog pattern and danger-colored confirm-button treatment already established for removing a project from the grid, rather than inventing a second confirmation pattern for a structurally identical irreversible-action decision.
2. WHEN the entry being confirmed for delete is a folder THEN the dialog SHALL make clear that this deletes the folder and everything inside it (Requirement 10.1), not just the folder itself.
3. WHEN one or more of the files that would be deleted are currently open with unsaved changes (Requirement 10.4) THEN the dialog SHALL surface that fact as part of the same confirmation, rather than a separate warning step.
4. The dialog's confirming button SHALL be the one requiring an explicit affirmative action (e.g. "Delete"), with a clearly separate, easy cancel path, matching the Yes/No, danger-colored-Yes convention already established for project removal.

## Non-Functional Requirements

### Code Architecture and Modularity

- **Single Responsibility**: rename and delete are both mutations on the same root-folder-scoped file-access layer `project-root-folder-explorer` already introduces for hierarchy listing; delete SHALL reuse that layer rather than opening a second, parallel filesystem access path.
- **Modular Design**: the rename dialog and delete-confirmation wiring live alongside the explorer component from `project-root-folder-explorer`; neither SHALL be duplicated per diagram type. The context menu itself (`reusable-context-menu`'s `ContextMenu`) SHALL remain unaware of files, folders, rename, or delete specifically — this spec only supplies the item groups and callbacks it renders, per that spec's own Requirement 5 / NFR Single Responsibility.
- **Reuse over reinvention**: the rename dialog and delete confirmation dialog SHALL both be built on the existing `Dialog` primitive (`ConfirmDialog` for delete; a small custom dialog for rename, per `Dialog`'s own documented "custom dialog whose embedded content enables/disables a footer button via its own callback" pattern) rather than introducing a second dialog mechanism; the context menu SHALL be `reusable-context-menu`'s `ContextMenu`, not a second menu implementation.
- **Extensibility**: action discovery (Requirement 2) SHALL be implemented so a future spec can add actions — and their shortcuts — for a given entry without changing this spec's client-side trigger code (Requirement 2.6) — core `Hierarchy` code SHALL NOT depend on any specific diagram type to do so, per structure.md's dependency direction.

### Performance

- A rename SHALL remain a single, direct filesystem operation — no recursive copy-then-delete of a folder's contents.
- A folder delete SHALL be a single recursive filesystem delete operation, not an enumerate-then-delete-each-entry round trip over gRPC per file.
- Querying an entry's available actions (Requirement 2) SHALL be fast enough to feel instantaneous when opening a context menu, mirroring `reusable-context-menu`'s own Performance NFR for the menu's own opening — it SHALL NOT, for example, require walking the entry's entire subtree.

### Security

- Rename and delete SHALL both be scoped to the project's root folder exactly as hierarchy listing already is (`project-root-folder-explorer` Requirement 1.4), never able to reach outside it.
- A user SHALL only be able to rename, delete, or query available actions within a project they are authorized to access, per `login-project-selection` Non-Functional: Security.
- The available-actions capability (Requirement 2) SHALL apply the same authorization and containment checks before returning anything, so it cannot be used to probe the existence or nature of entries outside a project a user is authorized to access.

### Reliability

- A rename SHALL be atomic from the caller's perspective: it either fully succeeds (old name gone, new name present, contents intact) or fully fails (original untouched) — no partially-renamed state SHALL be left behind or exposed to the client.
- A folder delete is not guaranteed atomic the way a rename is, since it may touch many files individually; IF it fails partway through THEN the system SHALL report this clearly (Requirement 10.2) rather than claiming success, and the explorer's view SHALL always reflect actual on-disk state afterward, live-synced the same way any other out-of-band disk change already is (`project-root-folder-explorer` Requirement 4).

### Usability

- Rename and delete SHALL both be reachable the same two ways: the entry's context menu (Requirement 3) and a dedicated keyboard shortcut on the focused entry (`F2` / `Delete`, Requirement 4) — the "SHOULD" familiar-IDE-convention guidance this spec previously only suggested is now this spec's actual, required mechanism.
- The explorer tree's currently-focused entry SHALL always be visibly indicated (Requirement 1.1), so a keyboard user always knows what `F2`/`Delete`, or an opened context menu, will act on.
