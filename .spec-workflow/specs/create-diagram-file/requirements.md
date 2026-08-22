# Requirements Document

## Introduction

[`add-diagram-action`](../add-diagram-action/requirements.md) puts the whole **Add** flow in place — the action on a folder or on the project root, the dialog listing every discovered diagram type grouped by vendor, and the delivery of the chosen type to the backend — and then stops, by design, at a single commit step that answers "creating this diagram type is not supported yet" (its Requirement 6). This spec fills that step in: when the user presses **Add** in the Add dialog, the backend **creates a real file on disk** in the target folder.

The file has the extension **`.adp`**, and its content identifies the diagram type that was chosen: the **MIME type** of the selected `DiagramDefinition`, derived from its `DiagramOrigin` exactly the way [docs/diagrams.md](../../../docs/diagrams.md) already writes origins (`vendor/type`). Nothing else is written: this spec makes a diagram *exist* and *say what it is*; what a diagram of a given type contains is each module's business and comes with the module's own format later. An `.adp` file whose only content is its MIME type is therefore a valid, empty diagram of that type.

The user also **names the file in the Add dialog**: alongside the type list the dialog carries a name field, pre-filled with a collision-free suggestion derived from the chosen type, validated as it is typed by the backend — the same live-validation mechanism the rename dialog already uses ([`rename-files-and-folders`](../rename-files-and-folders/requirements.md) Requirement 6) — so **Add** only becomes available for a name that is non-empty, valid for the host filesystem, and not already taken in the target folder. This widens `add-diagram-action`'s choice prompt with one field; everything else in that flow is untouched.

Once created, the new file is selected in the explorer, so it can be opened, renamed or acted on without navigating to it.

**Dependencies:** this spec builds on `add-diagram-action` (the action, the choice dialog, the `AddDiagramContextActionProvider.CommitAsync` seam, `DiagramDefinition.All`), on [`rename-files-and-folders`](../rename-files-and-folders/requirements.md) (the name rules and the live propose-and-verdict validation this spec's name field reuses), on [`context-service`](../context-service/requirements.md) (the selection that lets the new file be selected after creation, and the pushed actions that make its own actions immediately available), and on [`project-root-folder-explorer`](../project-root-folder-explorer/requirements.md) (the watcher that turns the new file into an `EntryCreated` for every connected client). It should be implemented after `add-diagram-action`.

## Alignment with Product Vision

- [product.md](../../steering/product.md)'s **"Files are the source of truth"**: Add produces a real file in the project folder, which every client — including the one that created it — learns about through the same `RootFolderWatcher` → `EntryCreated` path as a file created outside ADP. There is no client-side insert, exactly as `add-diagram-action` Requirement 6.5 pinned in advance.
- product.md's **"Don't reinvent, integrate"**: the file says what it is with a MIME type, the vocabulary the diagram catalog and `DiagramOrigin` already use, rather than a new type identifier; the file is plain UTF-8 text so it stays diffable and version-control-friendly ([tech.md](../../steering/tech.md) *Diagram storage*).
- tech.md's **"Wherever possible already existing text-based file formats will be used"**: a diagram type's *own* document format (FreeMind `.mm`, PlantUML, …) is untouched by this spec; `.adp` is ADP's own envelope for a diagram that ADP created and that declares its type, and a later spec may decide per type whether the diagram body lives inside the `.adp` file or beside it.
- tech.md's **"The backend is the sole owner of reading/writing diagram files"** and **"File-system access from the backend is scoped to explicitly opened workspace folders"**: the client never names a path; the backend composes the file name inside the resolved, containment-checked target folder.
- [`rename-files-and-folders`](../rename-files-and-folders/requirements.md)'s Reliability rule that a rename is atomic: the same standard applies to creation — the file either exists with its full content, or does not exist at all.

## Requirements

### Requirement 1 — Pressing Add creates a file on disk

**User Story:** As an architect, I want pressing **Add** in the Add dialog to create the diagram file in the folder I chose, so that adding a diagram in ADP is as real as creating a file any other way.

#### Acceptance Criteria

1. WHEN the user confirms a diagram type in the Add dialog (the dialog's **Add** button, `add-diagram-action` Requirement 5.6) THEN the system SHALL create a new file inside the target folder that the Add action was triggered on — the folder itself, or the project root when Add was triggered on empty space — before the dialog closes.
2. WHEN the file is created THEN this SHALL happen at the commit step `add-diagram-action` Requirement 6 defines (`AddDiagramContextActionProvider.CommitAsync`), replacing its "not supported yet" answer and nothing else in the Add flow: the action, the dialog, the option ids and the delivery of the chosen type SHALL NOT change (that spec's Requirement 6.3).
3. WHEN the commit step runs THEN the system SHALL keep the checks `add-diagram-action` already places before creation, in the same order: a target folder that no longer exists is reported as such (its Requirement 6.4), and an option id that names no discovered diagram type is rejected; the entered name is then re-validated server-side (Requirement 2.6) — creation is attempted only after all three pass.
4. WHEN the file has been created THEN the system SHALL complete the interaction so the dialog closes, exactly as a successful rename or delete completes today; WHEN creation fails THEN the system SHALL leave the dialog open with a clear error (Requirement 5) so the user can retry or cancel.
5. WHEN the file is created THEN the system SHALL write it through the backend's file-access layer inside the resolved target folder only; the client SHALL NOT supply, and SHALL NOT receive, an absolute filesystem path at any point.

### Requirement 2 — The user names the file in the Add dialog

**User Story:** As an architect, I want to name the diagram while I am adding it, so that it arrives in my project called what I mean it to be called instead of something I have to rename afterwards.

#### Acceptance Criteria

1. WHEN the Add dialog opens THEN it SHALL present, alongside the diagram-type list (`add-diagram-action` Requirement 5), a **name field** for the file being created. The choice prompt that carries the type list SHALL therefore also describe this field as data — its label and its current value — so the client still learns nothing about diagram types or file naming.
2. WHEN the user selects a diagram type and has not edited the name THEN the system SHALL pre-fill the field with a suggestion derived from that type: the origin's `Type` segment, plus `-<subtype>` where the origin carries one (e.g. `mindmap`, `context`, `class-uml`), made collision-free against the target folder by appending an increasing number when needed (`mindmap-2`, `mindmap-3`, …). Selecting a different type before the user edits the name SHALL update the suggestion; once the user has edited the name, the system SHALL NOT overwrite what they typed.
3. WHEN the name field is presented THEN the extension SHALL NOT be part of what the user types: the field holds the base name, and the system appends **`.adp`** (Requirement 3.1). A name the user types *with* a trailing `.adp` SHALL be accepted as meaning the same file rather than producing `name.adp.adp`.
4. WHILE the dialog is open THEN the system SHALL keep its **Add** button disabled unless the entered name is non-empty, valid for the host filesystem, and free in the target folder — the same "only a usable value enables the confirm button" rule the rename dialog already applies (`rename-files-and-folders` Requirement 5.2).
5. WHEN the user edits the name THEN the system SHALL validate it on the backend as it is typed, through the existing debounced propose-and-verdict mechanism the rename dialog uses (`rename-files-and-folders` Requirement 6, `context-service`'s `ProposeInput`), and SHALL surface the reason when a name is rejected — empty, containing a character the filesystem forbids, or already taken in this folder — rather than only greying the button out.
6. WHEN a name is validated or submitted THEN the backend SHALL enforce every rule again server-side, so a client that skips validation cannot create a file with an invalid name, outside the target folder, or over an existing entry; a name containing a path separator or a `..` segment SHALL be rejected outright rather than sanitised, since the user can see and fix it.
7. WHEN the user confirms THEN the system SHALL create the file under exactly the name shown, plus the `.adp` extension — the name is the user's, and the system SHALL NOT silently alter it (Requirement 2.3's extension handling aside).
8. IF the name becomes taken between validation and creation (another client, or an external tool, created it in that moment) THEN creation SHALL fail rather than overwrite (create-new semantics) and the dialog SHALL stay open reporting that the name is now taken, so the user chooses another — the system SHALL NOT silently pick a different name on their behalf.

### Requirement 3 — The file content is the diagram type's MIME type

**User Story:** As an architect (and as any tool that later opens the file), I want the `.adp` file to state which kind of diagram it is, so that ADP, a colleague, or a diff can tell what the file is for without ADP's help.

#### Acceptance Criteria

1. WHEN the file is written THEN its content SHALL be the **MIME type of the chosen `DiagramDefinition`**, UTF-8 encoded without a byte-order mark, on the first line, terminated by a single line feed — and nothing else in this spec.
2. WHEN a diagram type's MIME type is derived THEN it SHALL be built from its `DiagramOrigin` as `<vendor>/<type>` — the same string docs/diagrams.md's Origin column uses (e.g. `freeplane/mindmap`, `c4/context`, `archimate/business`) — and, when the origin carries a `Subtype`, as `<vendor>/<type>+<subtype>`, following the structured-suffix convention MIME already uses (`svg+xml`) rather than a second slash, which a MIME type cannot contain. This derivation SHALL live in one place on `DiagramOrigin` (a `MimeType` property), so the file writer, the catalog, and any later reader agree by construction.
3. WHEN the content is written THEN it SHALL be written atomically from the reader's point of view: the file SHALL never be observable with partial content — write to a temporary name in the same folder and move into place, or an equivalent guarantee — so the watcher, another client, or version control never sees a half-written diagram.
4. WHEN `.adp` files are later read (out of scope here, but shaped now) THEN the first line SHALL be the authoritative statement of the diagram's type; this spec therefore SHALL NOT write any content before it, such as a comment or a header, that a later reader would have to skip.
5. The body of a diagram — its elements, layout, or a reference to a sibling file in the type's native format — is NOT defined by this spec; a file containing only its MIME type line SHALL be treated as a valid, empty diagram of that type by everything this spec touches.

### Requirement 4 — The new file shows up everywhere through the watcher, and is selected where it was added

**User Story:** As an architect, I want to see the diagram I just added appear in the explorer, already selected, so that I can open or act on it immediately — and as a colleague with the same project open, I want to see it appear too.

#### Acceptance Criteria

1. WHEN the file has been created THEN it SHALL appear in the hierarchy of every connected client through the existing filesystem watcher and `EntryCreated` change (`project-root-folder-explorer` Requirement 4), exactly as a file created outside ADP would; the system SHALL NOT insert it client-side and SHALL NOT push a dedicated "file created" message (`add-diagram-action` Requirement 6.5).
2. WHEN the interaction completes on the connection that performed the Add THEN the backend's completion SHALL carry the created file's **project-relative path** (the same `Path` form `context-service` already exposes for selections), so that connection's client can recognise the file once its `EntryCreated` arrives — without ever being told an absolute path.
3. WHEN the client that performed the Add receives the `EntryCreated` for that path THEN the explorer SHALL focus the new entry, expanding its parent folder if the folder was collapsed, and report it as the current selection (`context-service` Requirement 3.1) — so the Property Grid shows it and the ribbon offers its actions with no further navigation.
4. IF the `EntryCreated` for the new file never arrives within a short, bounded time (the watcher is stopped, or the folder is not expanded on this connection and `project-root-folder-explorer` Requirement 4.7 therefore suppresses the message) THEN the explorer SHALL expand the target folder so the file is listed on demand and SHALL focus it from that listing — a created file SHALL never be silently out of view on the connection that created it.
5. WHEN a client other than the one that performed the Add receives the `EntryCreated` THEN that client's explorer SHALL apply it exactly as any other created entry (Requirement 3.10 of `project-root-folder-explorer`) and SHALL NOT change that client's focus or selection.

### Requirement 5 — Failure leaves no trace and is reported where the user is

**User Story:** As an architect, I want a failed Add to tell me why, in the dialog I was using, and to leave my project exactly as it was, so that I can fix the cause and try again.

#### Acceptance Criteria

1. IF the target folder cannot be written to (permissions, read-only media, disk full) THEN the system SHALL report a clear error on the interaction, SHALL leave the dialog open with the chosen type still selected, and SHALL leave no partial or temporary file behind in the target folder.
2. IF creation fails after a temporary file was written but before it was moved into place THEN the system SHALL delete the temporary file as part of reporting the failure.
3. IF the chosen diagram type is no longer available at commit time (cannot happen today, since `DiagramDefinition.All` is immutable, but the check exists per `add-diagram-action`) THEN the system SHALL report that and create nothing.
4. WHEN an error is reported THEN it SHALL name the problem the user can act on (the folder, the permission, the disk), never an internal path outside the project or a stack trace.

### Requirement 6 — `.adp` files are recognisable in the explorer

**User Story:** As an architect, I want ADP's own diagram files to look like diagrams in the tree, so that I can tell them from ordinary files at a glance.

#### Acceptance Criteria

1. WHEN the explorer renders a file whose extension is `.adp` THEN it SHALL use a diagram-specific Material Design Icon (e.g. `mdi-graph-outline` or `mdi-sitemap-outline`) from the existing `@mdi/font` integration, through the same extension-to-icon mapping `project-root-folder-explorer` Requirement 3.8 already defines, rather than the generic file icon.
2. This spec SHALL NOT open, parse, or render an `.adp` file's content anywhere in the client; reading the MIME type back to choose a per-type icon or to open the right diagram module is a later spec's concern, and the first-line rule of Requirement 3.4 is what makes that cheap.

## Non-Functional Requirements

### Code Architecture and Modularity

- **Single Responsibility**: suggesting and validating the file name (Requirement 2), deriving the MIME type (Requirement 3.2, on `DiagramOrigin`), and writing the file atomically (Requirement 3.3) SHALL be three separately testable pieces; `AddDiagramContextActionProvider.CommitAsync` orchestrates them and stays the only place in the Add flow that changes.
- **Modular Design**: nothing in this spec depends on any specific diagram-type module — the content is derived from the shared `DiagramDefinition`/`DiagramOrigin` abstraction alone, so core stays compilable with zero modules present.
- **Reuse over reinvention**: the name rules SHALL be the ones `rename-files-and-folders` already enforces for a rename (empty, invalid character, collision) rather than a second, parallel set, and live validation SHALL ride the existing `ProposeInput` verdict path; the existence and containment checks SHALL be the ones `add-diagram-action`/`rename-files-and-folders` already use (`HierarchyTargets`, the resolved `ContextTarget`); the change feed SHALL be the existing watcher path, and the post-creation selection SHALL go through `context-service`'s existing `Select`. No new RPC is added; the contract changes are the name field on the choice prompt and the created path on the existing submit response.
- **Dependency direction**: `EtAlii.Adp.Diagram` (where `DiagramOrigin.MimeType` lives) SHALL NOT gain a dependency on the backend; the backend's writer depends on it, never the reverse.

### Performance

- Creating a diagram SHALL be one file write plus one rename; finding the suggested name (Requirement 2.2) SHALL stop at the first free candidate rather than enumerating the folder, and validating a typed name SHALL be a single existence check, not a listing.
- The new file SHALL appear in the creating client's explorer within the same latency budget any watcher-detected change already has; Requirement 4.4's fallback SHALL wait no longer than a second before listing the folder itself.

### Security

- The target folder SHALL be the resolved, containment-checked location from the `ContextTarget`; a name containing a path separator or a `..` segment SHALL be rejected server-side (Requirement 2.6), so no entered name — nor a suggestion derived from a diagram type's own origin segments — can produce a path outside that folder.
- The created path returned to the client SHALL be project-relative only (Requirement 4.2); absolute paths never leave the backend.
- Creation SHALL use create-new semantics so an existing file is never truncated or replaced, whatever name is chosen.

### Reliability

- Creation SHALL be atomic from the reader's point of view (Requirement 3.3) and leave nothing behind on failure (Requirement 5.1–5.2).
- A name that becomes taken between validation and creation SHALL fail the creation and be reported to the user (Requirement 2.8), never overwrite, and never be silently replaced by a different name.
- Every bug found while implementing or verifying this spec SHALL be covered by a unit or integration test, or recorded in `tests.md`, per CLAUDE.md.

### Usability

- The dialog SHALL be usable by keyboard alone end to end: choosing a type, reaching the name field, and confirming — inheriting `add-diagram-action`'s own keyboard-navigability requirement rather than relaxing it.
- The suggested name (Requirement 2.2) SHALL be a usable answer on its own, so a user who only wants "a mindmap here" can press Add without typing anything.
- After creation the new file SHALL be selected on the connection that added it (Requirement 4.3), so opening or acting on it takes no further navigation.
