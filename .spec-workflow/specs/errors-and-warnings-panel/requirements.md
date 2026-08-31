# Requirements Document

## Introduction

The Errors & Warnings panel is the last of the workspace shell's panels still standing empty — [`ErrorsWarningsPanel.tsx`](../../../src/client/src/shell/panels/ErrorsWarningsPanel.tsx) is a `PanelPlaceholder` saying "Problems detected in the current project." This spec makes it real: **one list of everything wrong with the project**, kept current, and the means to ask for it to be rechecked.

Two things feed that list, and they are different in kind:

* **What is open right now.** A diagram the user is editing produces problems as they edit. These arrive the way everything else about the current state arrives — pushed down the connection's own **context `Watch` stream**, which already carries selections, prompts and project actions.
* **What is not open.** A project's other diagrams are files on disk that nothing has looked at this session. The panel is only honest if it covers those too, so the backend **traverses the project's diagram files** and aggregates what it finds.

The second is expensive, so its results are **cached** — keyed per file, persisted so that reopening a project does not mean rechecking every diagram before the panel can say anything. The cache is kept current two ways: **by events**, since the backend already watches the project folder and knows when a diagram changed, and **by explicit request**, because a user who has just fixed something wants to ask rather than wait.

Those requests come at three scopes, and each is an ordinary context action rather than an RPC of its own: **a diagram** (its context menu and the ribbon), **a folder** (its context menu, covering everything beneath it), and **everything** — "Validate all", offered on the panel's own right-click menu and, when the panel has focus, in the ribbon.

What counts as a problem is deliberately not core's business. Core contributes the problems it can already see — [`DiagramFileRouter`](../../../src/backend/EtAlii.Adp.Backend/Hierarchy/DiagramFileRouter.cs) already decides a file is `UnknownType`, `Ambiguous` or `Unreadable`, and each of those *is* a problem — and each diagram type contributes its own through a seam alongside the one it already uses to create an empty document.

**Dependencies:** [`context-service`](../../archive/specs/context-service/requirements.md) (the `Watch` stream, the selection that a problem's `PROBLEMS` source already has a place in, the provider seam every action reaches the ribbon and the menu through), [`project-root-folder-explorer`](../../archive/specs/project-root-folder-explorer/requirements.md) (the watcher that reports a changed file), [`create-diagram-file`](../../archive/specs/create-diagram-file/requirements.md) and [`mindmap-diagram`](../../archive/specs/mindmap-diagram/requirements.md) (`.adp` registration, the router, and the first diagram type with a body worth validating).

## Alignment with Product Vision

* [product.md](../../steering/product.md)'s **"A familiar surface"**: every IDE has this panel, and the expectations that come with it — a list you can click to get to the thing, a count, and a way to rebuild it on demand. This spec meets those rather than inventing a new idea of what "problems" means.
* product.md's **"Files are the source of truth"**: the panel reports on what is *on disk*, not on what happens to be open. A diagram nobody opened this session still appears if it is broken, which is the whole reason for the traversal.
* [tech.md](../../steering/tech.md)'s **frontend-backend synchronization**: problems are state the backend owns and pushes; the client renders what arrives and never computes a problem itself.
* tech.md's **Context service** rules: the Validate actions are `IContextActionProvider` contributions with their shortcuts declared as data, so they reach the ribbon, the right-click menu and the keyboard through the one path — no new RPC, no key-to-action table on the client.
* [structure.md](../../steering/structure.md)'s **Core vs diagram-type plugins**: core knows how to *collect* problems and nothing about what makes a particular diagram wrong. A type's rules live in that type's module, behind a seam shaped like the `IDiagramDocumentFactory` one it already implements.

## Requirements

### Requirement 1 — One list of the project's problems, pushed to the panel

**User Story:** As an architect, I want a single place that tells me what is wrong across my whole project, so that I don't have to open each diagram to find out.

#### Acceptance Criteria

1. WHEN a connection is watching a project THEN the system SHALL deliver that project's problems over the existing context **`Watch`** stream, as a new member of `ContextMessage` alongside `selection`, `prompt` and `project_actions` - one channel per connection, not a second stream to open and keep alive.
2. WHEN the panel first renders THEN the system SHALL deliver the current problem set as a baseline, so a client that connects late is immediately consistent - the same discipline `ContextSelectionStore.Register` already follows for selections.
3. WHEN a problem is reported THEN it SHALL carry at least: a **severity** (error or warning), a **message** meant for the user, the **project-relative path** of the file it concerns, an optional **location within that file** (an element id or a line), and the **id of the rule or the module** that raised it, so a reader can tell an unreadable file apart from a diagram type's own complaint.
4. WHEN problems are delivered THEN the system SHALL also deliver the counts, so the panel can show "3 errors, 12 warnings" without counting a truncated list.
5. IF the problem set is large THEN the system SHALL bound what it sends, and say that it did, rather than pushing an unbounded list down a stream.
6. WHEN the panel is rendered THEN the system SHALL show, per problem, its severity, its message and where it is - and when there are none, say so plainly rather than showing an empty box.
7. WHEN nothing has been validated yet in a fresh project THEN the panel SHALL distinguish **"no problems found"** from **"not checked yet"**, because they mean opposite things to someone deciding whether to trust the list.
8. WHEN the user wants to filter for either errors, warnings or both THEN the panel SHALL have small icon buttons to do so.

### Requirement 2 — The list covers the whole project, not just what is open

**User Story:** As an architect, I want the panel to account for diagrams I haven't opened, so that "no problems" means the project is clean rather than that I haven't looked.

#### Acceptance Criteria

1. WHEN the project is validated THEN the system SHALL traverse the project folder and consider every file the router recognises as a diagram - a `.adp` registration file, or a body file whose extension a discovered type claims.
2. WHEN traversal meets a file the router reports as `UnknownType`, `Ambiguous` or `Unreadable` THEN the system SHALL record that outcome as a problem in its own right, attributed to core rather than to any diagram type - these are already-computed diagnoses that today are simply discarded.
3. WHEN traversal meets a file the router reports as `NotADiagram` THEN the system SHALL pass over it silently: a project holds source code, images and notes, and none of them are ADP's to complain about.
4. WHEN a recognised diagram has a body THEN the system SHALL ask that diagram type for its own problems (Requirement 3), and record what it returns.
5. WHEN traversal runs THEN the system SHALL stay within the project root, refusing to follow a link or a path that escapes it, exactly as every other filesystem path in the backend does.
6. IF a folder or file cannot be read during traversal THEN the system SHALL record that as a problem and continue, so one unreadable corner does not cost the report of everything else.

### Requirement 3 — A diagram type declares its own rules

**User Story:** As the author of a diagram-type module, I want to say what makes a diagram of my type wrong, so that its rules live with the type rather than in core.

#### Acceptance Criteria

1. WHEN a diagram type wants to contribute problems THEN it SHALL do so by registering a **validator**, resolved by its `DiagramOrigin`, registered exactly the way `IDiagramDocumentFactory` and `IContextActionProvider` already are - one line in the host, no change inside the collector.
2. WHEN a validator is asked about a diagram THEN it SHALL be given that diagram's document and SHALL return the problems it finds, each with a severity, a message and an optional location inside the document.
3. IF a diagram type registers no validator THEN the system SHALL treat its diagrams as having no type-specific problems, rather than as an error - a type is allowed to have nothing to say.
4. IF a validator throws THEN the system SHALL record one problem naming that module, skip it, and continue with the remaining files - a faulty module costs its own diagrams' results and nothing more.
5. IF a validator does not finish within a bounded time THEN the system SHALL abandon it, record that, and continue, so one pathological document cannot hang a "Validate all".
6. WHEN core collects problems THEN it SHALL NOT interpret, rank or rewrite what a validator returned beyond ordering the combined list for display.

### Requirement 4 — Results are cached, and the cache survives a restart

**User Story:** As an architect reopening a project, I want the panel populated straight away, so that I am not made to wait for a full recheck before I can see anything.

#### Acceptance Criteria

1. WHEN a file has been validated THEN the system SHALL cache its problems keyed by that file, so a later validation of something else does not discard them.
2. WHEN the backend restarts and a project is opened THEN the system SHALL serve the panel from the cache, without first revalidating the project.
3. WHEN a cached entry is stored THEN it SHALL carry enough about the file's state at the time - its last-write time and size, at least - for the system to tell later whether the entry still describes the file on disk.
4. WHEN the cache is served THEN an entry whose file has since changed SHALL be reported as **stale** rather than as fact, so the panel can show it while making clear it may be out of date.
5. WHERE the cache is stored THEN it SHALL be per project and SHALL NOT be written inside the project folder itself, so ADP never adds files to a repository the user did not ask for; the application-data location `FileProjectStore` already uses is the precedent.
6. IF the cache is missing, unreadable or written by an incompatible version THEN the system SHALL start from empty and say the project has not been checked (Requirement 1.7), rather than failing to open the project.
7. WHEN a diagram type's validator changes what it reports THEN the system SHALL have a way to invalidate entries produced by an older version of that validator, so a rule fixed in a release does not leave stale verdicts behind forever.
8. WHEN the backend restarts THEN the system SHALL start an asynchronous re-validation of the known projects, so the cache converges on the truth without anyone having to ask - while a project opened meanwhile is still served from cache at once (Requirement 4.2).

### Requirement 5 — Events keep the list current

**User Story:** As an architect, I want the panel to react when I change something, so that a problem I have just fixed stops being reported without my asking.

#### Acceptance Criteria

1. WHEN the watcher reports that a diagram file changed THEN the system SHALL revalidate that file alone and push the updated problems, leaving every other file's cached results untouched.
2. WHEN a diagram file is deleted THEN the system SHALL drop its problems from the list and from the cache.
3. WHEN a diagram file is renamed or moved THEN the system SHALL carry its problems to the new path rather than reporting the file as new and unchecked.
4. WHEN a diagram is edited through ADP itself THEN the system SHALL revalidate it on the same terms as an edit made outside ADP - one path, so an external edit is never treated as second-class.
5. WHEN several changes arrive in quick succession THEN the system SHALL coalesce them rather than revalidating the same file repeatedly, the way the client already coalesces selections before sending them.
6. WHILE a validation is in progress THEN the panel SHALL show that it is working, and SHALL remain usable rather than blocking on the result.

### Requirement 6 — Validate on demand, at three scopes

**User Story:** As an architect, I want to ask for a recheck of exactly as much as I care about, so that fixing one diagram doesn't mean waiting for the whole project.

#### Acceptance Criteria

1. WHEN the user invokes **Validate** on a diagram file THEN the system SHALL revalidate that diagram and update its problems.
2. WHEN the user invokes **Validate** on a folder THEN the system SHALL revalidate every recognised diagram beneath it, recursively, and update their problems.
3. WHEN the user invokes **Validate all** THEN the system SHALL revalidate every recognised diagram in the project (Requirement 2) and replace the list wholesale, so a problem whose file no longer produces it disappears.
4. WHEN any Validate completes THEN the system SHALL push the updated problems to **every** connection watching that project, not only to the one that asked - the panel is about the project, and two people looking at it should not disagree.
5. IF a Validate is requested while one is already running for the same project THEN the system SHALL NOT start a second concurrent traversal; it SHALL either coalesce with the running one or refuse with a reason, and SHALL say which.
6. WHEN a Validate is requested on something that is not a diagram and not a folder THEN the action SHALL NOT be offered at all, rather than being offered and failing.
7. WHEN a Validate runs THEN it SHALL NOT modify any diagram file - validation reports, it never repairs.

### Requirement 7 — The actions appear where the user expects them

**User Story:** As an architect, I want Validate where I already right-click and where I already look for buttons, so that I don't have to learn a new place for it.

#### Acceptance Criteria

1. WHEN the user right-clicks a diagram file in the explorer THEN **Validate** SHALL be among its actions; and it SHALL likewise appear in the ribbon's contextual group for that selection, since both surfaces render the same pushed actions.
2. WHEN the user right-clicks a folder in the explorer THEN **Validate** SHALL be among its actions, described as covering what is inside it.
3. WHEN the user right-clicks the Errors & Warnings panel THEN **Validate all** SHALL be among its actions.
4. WHEN the Errors & Warnings panel has focus THEN **Validate all** SHALL appear in the ribbon - which follows from the panel reporting itself as the current selection, exactly as the explorer and the canvas already do, rather than from the ribbon being taught about this panel.
5. WHEN an action is offered THEN its label, icon, availability, unavailable reason and shortcut SHALL all be data supplied by the backend, so the client gains no knowledge of what Validate means.
6. IF there are no diagram types with validators deployed THEN Validate SHALL still be offered, since core's own router problems (Requirement 2.2) are worth finding on their own.
7. WHEN a problem in the panel is activated THEN the system SHALL reveal and select the file it concerns - the flow [`context-service`](../../archive/specs/context-service/requirements.md) already catalogs, where a problem *references* the file and, within it, the element or line.

### Requirement 8 — Keyboard shortcuts that match the conventions already in use

**User Story:** As a keyboard-driven user, I want Validate on keys I can guess, so that the panel is usable without the mouse.

#### Acceptance Criteria

1. WHEN the Validate actions are offered THEN their shortcuts SHALL be **`F6`** for validating the current selection (a diagram or a folder) and **`Ctrl+Shift+B`** for **Validate all**.
2. WHEN those shortcuts are chosen THEN they SHALL follow the split this codebase already uses: bare keys for actions on the selected thing (`F2` rename, `Delete`, `Insert` add) and `Ctrl` chords for actions on the project as a whole (`Ctrl+Z` undo, `Ctrl+Y` redo). `F6` acts on a selection; `Ctrl+Shift+B` acts on the project.
3. WHEN those shortcuts are chosen THEN they SHALL match what mainstream IDEs already bind: `Ctrl+Shift+B` is Build Solution in Visual Studio and Run Build Task in VS Code; `F6` is the Build family in Visual Studio - so the pair reads as "check this" and "check everything" to someone arriving from either.
4. WHEN a shortcut is declared THEN it SHALL be carried as data on the action by its provider, and matched by the existing client-side matcher, so no key-to-action table is introduced on the client.
5. WHEN a shortcut fires THEN the system SHALL suppress the browser's own handling of that chord, as `useProjectShortcuts` already does for undo and redo.
6. IF the focus is in a text field or a modal prompt is open THEN neither shortcut SHALL fire, on the same terms as every other shortcut in the shell.
7. WHEN the shortcuts are assigned THEN they SHALL NOT collide with any shortcut already declared by a provider - today `F2`, `Delete`, `Insert`, `Enter`, `Space`, `Ctrl+Z`, `Ctrl+Y` - nor with a browser chord that cannot be suppressed (`Ctrl+N`, `Ctrl+T`, `Ctrl+W` and their shifted forms).

## Non-Functional Requirements

### Code Architecture and Modularity

* **Single Responsibility Principle**: traversal (finding diagram files), validation (asking a type what is wrong), the cache (remembering answers), the push (telling connections), and the actions (asking for a recheck) are five concerns in five files.
* **Modular Design**: core depends only on the validator seam and the router. A diagram type's rules never enter core, and core must remain compilable and testable with zero validators registered.
* **Dependency Management**: the collector depends on the router and the validator seam, not on `ContextService`; the push is a subscriber to the collector rather than a caller inside it, so a future consumer - a status-bar count, a CI report - attaches without touching either.
* **Clear Interfaces**: Validate is contributed through the existing `IContextActionProvider`; the panel consumes the existing `Watch` stream. This spec adds one seam, one message and one action provider, and changes no existing abstraction.
* **Testability**: validation must be exercisable against a temporary folder and a test-supplied validator, without a running host and without depending on which diagram modules happen to be built.

### Performance

* Validating one file SHALL cost one file's work: a change to one diagram must not trigger a project-wide traversal.
* A "Validate all" on a project of a few hundred diagrams SHALL remain responsive - progress visible, the UI usable - rather than delivering nothing until it is finished.
* Serving the panel from the cache SHALL not require reading the diagram files themselves.

### Security

* Traversal SHALL stay within the resolved project root, and SHALL NOT follow links out of it.
* The client SHALL NOT receive or supply an absolute filesystem path at any point; problems name files by project-relative path, as every other message already does.
* A validator is module code deployed with the application; nothing user-supplied or project-supplied is ever loaded or executed as a validator.

### Reliability

* A validator that throws, hangs, or returns nonsense SHALL cost its own diagrams' results and nothing else (Requirement 3.4-3.5).
* A corrupt or unreadable cache SHALL never prevent a project from opening (Requirement 4.6).
* Two connections watching the same project SHALL see the same list (Requirement 6.4).

### Usability

* The panel SHALL be usable by keyboard alone: reaching it, moving through the problems, and activating one to get to the file.
* A problem SHALL say enough to act on without opening anything: what is wrong, and where.
* The distinction between "checked, and clean" and "not checked yet" SHALL be visible at a glance (Requirement 1.7).
