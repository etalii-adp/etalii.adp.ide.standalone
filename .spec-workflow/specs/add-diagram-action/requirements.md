# Requirements Document

## Introduction

ADP can list and rename what is already in a project, but it cannot yet create anything. This spec adds the missing verb: an **Add** context action, offered on a folder and on the hierarchy's empty space (the project root, with nothing selected), which opens a dialog listing every diagram type the running installation supports and creates the chosen one.

For that list to exist, the backend has to know which diagram types it is carrying. The 58 diagram-type modules scaffolded under `src/diagrams/` each already publish their identity as a static `Diagram.Definition` of type [`DiagramDefinition`](../../../src/backend/EtAlii.Adp.Diagram/_Model/DiagramDefinition.cs) (`DiagramOrigin` + `Title`). This spec makes the backend **discover** those definitions at startup by reflection rather than by a hand-maintained registry, and exposes them through a single cached `DiagramDefinition.All`.

The dialog presents them the way [docs/diagrams.md](../../../docs/diagrams.md) already catalogs them: grouped hierarchically by their MIME-type-style origin (`vendor/type`), labelled by their human title.

This spec covers discovery, the action, and the dialog. It does **not** define what a created diagram's file contents are beyond an empty, well-formed document — each diagram type's own schema is that module's concern.

### Relationship to `context-service`

[`context-service`](../context-service/requirements.md) moves the context-action RPCs and the prompt channel off `HierarchyService` onto a dedicated `ContextService`, drops the `scope` field from those requests, and pushes a selected target's available actions down its own `Watch` stream. It explicitly keeps the provider abstractions — `IContextActionProvider`, `IContextActionResolver`, `ContextTarget` — unchanged.

This spec therefore contributes Add through those same unchanged abstractions, and expects the action to be discovered and its dialog delivered over whichever channel is current when it is implemented. Two consequences follow, and are stated as requirements rather than left implicit: the "nothing selected" case (Requirement 4.2) is the context service's own notion of a selection that names no entry, and the new dialog kind this spec needs (Requirement 5) is a new prompt on that channel, not a second, parallel prompt mechanism.

## Alignment with Product Vision

- [product.md](../steering/product.md)'s **"Don't reinvent, integrate"**: the grouping vocabulary is the origin tag the diagram catalog already uses, not a new taxonomy invented for a dialog.
- product.md's **"Files are the source of truth"**: Add creates a real file in the project folder; it becomes visible through the same `RootFolderWatcher` → `EntryCreated` path as a file created outside ADP, not through a special-cased client-side insert.
- product.md's **"Minimal footprint, incremental value"**: a diagram-type module contributes itself by existing and being deployed. Nothing in core code enumerates diagram types, so adding the 59th is a new project rather than an edit to a registry.
- [structure.md](../steering/structure.md)'s **"Core vs diagram-type plugins"**: core must not depend on any single diagram type's schema. Discovery therefore reads only the shared `DiagramDefinition` abstraction, never a module's own types.
- [tech.md](../steering/tech.md)'s **"the backend is the sole owner of reading/writing files"**: the client never composes a path or writes the new file; it names a chosen diagram type and the backend does the rest.

## Requirements

### Requirement 1 — Diagram types discovered at startup by reflection

**User Story:** As a developer adding a diagram-type module, I want the backend to find my module on its own, so that contributing a diagram type never means editing a central list.

#### Acceptance Criteria

1. WHEN the backend starts THEN the system SHALL search the application's referenced assemblies for a **static class named `Diagram`** exposing a **public static `Definition` property** of type `DiagramDefinition`, and collect each one it finds.
2. WHEN discovery completes THEN the system SHALL expose the collected definitions through a **static readonly `DiagramDefinition.All`**, computed once per process and reused for every later read.
3. WHEN `DiagramDefinition.All` is read more than once THEN the system SHALL return the same cached collection without repeating the reflection scan.
4. IF an assembly cannot be inspected (it fails to load, or its types cannot be enumerated) THEN the system SHALL skip that assembly, record why, and continue discovering the remaining ones rather than failing startup.
5. IF a candidate `Diagram` class is found whose `Definition` property is missing, non-public, non-static, or not a `DiagramDefinition` THEN the system SHALL skip it and record it as malformed rather than throwing.
6. IF two discovered definitions carry the same origin THEN the system SHALL keep one, record the collision naming both assemblies, and continue - a duplicate must not produce two indistinguishable entries in the dialog.
7. WHEN discovery runs THEN the system SHALL NOT require core code to name, reference by type, or otherwise know about any specific diagram type.

### Requirement 2 — Discovery is observable

**User Story:** As someone running or debugging ADP, I want the log to tell me which diagram types were found, so that a missing entry in the Add dialog is diagnosable without a debugger.

#### Acceptance Criteria

1. WHEN discovery completes THEN the system SHALL log, at information level, how many diagram types were discovered and how many assemblies were scanned.
2. WHEN a diagram type is discovered THEN the system SHALL log its origin and title, so the log shows *what* was found and not merely how many.
3. WHEN an assembly is skipped, a candidate is malformed, or an origin collides (Requirement 1.4-1.6) THEN the system SHALL log that at warning level, naming the assembly involved.
4. IF no diagram types are discovered at all THEN the system SHALL log a warning, since an empty Add dialog is far more likely a deployment or reference problem than a genuine state.

### Requirement 3 — Diagram-type modules reach the running application

**User Story:** As someone deploying ADP, I want the diagram types that ship with it to actually be discoverable, so that the Add dialog reflects what was built.

#### Acceptance Criteria

1. WHEN the backend runs THEN every diagram-type module intended to ship SHALL be present among the assemblies discovery searches. The host already references them as a wildcard - `..\..\diagrams\*\backend\*\*.csproj`, excluding test projects, in [`EtAlii.Adp.Backend.Service.csproj`](../../../src/backend/EtAlii.Adp.Backend.Service/EtAlii.Adp.Backend.Service.csproj) - so this holds without a per-module reference.
2. IF the runtime has not yet loaded a referenced assembly THEN discovery SHALL still find it - .NET loads referenced assemblies lazily, so scanning only `AppDomain.CurrentDomain.GetAssemblies()` is **not** sufficient and will under-report on a cold start.
3. WHEN a diagram-type module is added to the solution THEN making it discoverable SHALL require no change to discovery code **and no change to the host's project references**, since the wildcard already covers a newly added module.
4. IF a diagram-type module is referenced but contributes no `Diagram.Definition` THEN the system SHALL treat that as the module simply having nothing to contribute, not as an error - the 58 scaffolded modules are expected to gain definitions over time rather than all at once.

### Requirement 4 — An Add action on a folder and on the root

**User Story:** As an architect working in a project, I want to add a new diagram where I am in the tree, so that I don't have to leave ADP or create the file by hand.

#### Acceptance Criteria

1. WHEN the user opens the context menu on a **folder** in the hierarchy THEN the system SHALL offer an **Add** action among that folder's actions.
2. WHEN the user opens the context menu on the hierarchy's **empty space**, with no entry selected THEN the system SHALL offer the same Add action, targeting the **project root folder**. This is the one case the current `ContextSource` cannot express - it carries an `entry_id` and nothing else - so the contract SHALL gain a way to name the root without naming an entry.
3. WHEN the user opens the context menu on a **file** THEN the system SHALL NOT offer Add - a file cannot contain a new entry.
4. WHEN Add is triggered THEN the system SHALL open a dialog for choosing a diagram type, following the same backend-initiated prompt flow the existing rename and delete actions use.
5. IF the target folder no longer exists when Add is triggered THEN the system SHALL report that rather than creating anything.
6. WHEN the action is offered THEN its availability and any keyboard shortcut SHALL be described by the backend as data, exactly as existing actions are, so the client gains no knowledge of what Add means.

### Requirement 5 — The dialog lists diagram types by title, grouped by origin

**User Story:** As an architect choosing a diagram type, I want the choices organised the way the notations themselves are, so that I can find the one I want among dozens.

#### Acceptance Criteria

1. WHEN the dialog opens THEN the system SHALL list every diagram type from `DiagramDefinition.All`, each shown by its **`Title`**. The existing prompt kinds - `InputDialogPrompt` and `ConfirmDialogPrompt` - cannot express a grouped list of choices, so the contract SHALL gain a prompt describing a hierarchy of selectable options as data, keeping the client free of any knowledge of diagram types.
2. WHEN the dialog lists diagram types THEN the system SHALL **group them hierarchically by their origin**: by `Vendor` first, then by `Type`, and by `Subtype` where a diagram type carries one.
3. WHEN a group is displayed THEN the system SHALL label it by its origin segment, so the grouping is legible rather than implied by indentation alone.
4. WHEN the dialog is open THEN the system SHALL keep its confirm action disabled until a diagram type - not merely a group - is selected.
5. WHEN the user confirms a chosen diagram type THEN the system SHALL create a new diagram of that type in the target folder and close the dialog.
6. IF no diagram types were discovered THEN the dialog SHALL say so plainly instead of presenting an empty list with an enabled confirm button.
7. WHEN the dialog is dismissed without confirming THEN the system SHALL create nothing.

### Requirement 6 — Creating the diagram

**User Story:** As an architect, I want the new diagram to appear in the tree straight away and be a real file on disk, so that it behaves like everything else in my repository.

#### Acceptance Criteria

1. WHEN a diagram type is confirmed THEN the system SHALL create a file in the target folder whose name does not collide with an existing entry.
2. WHEN the new file is created THEN it SHALL appear in every connected client's hierarchy through the existing `EntryCreated` change flow, not through a client-side insertion.
3. IF the file cannot be created (permissions, a name collision that cannot be resolved, a vanished folder) THEN the system SHALL report the reason to the user and leave the folder untouched.
4. WHEN the new file is created THEN its content SHALL be a well-formed empty document for the chosen diagram type, carrying enough to identify which type it is.

## Non-Functional Requirements

### Code Architecture and Modularity

- **Single Responsibility Principle**: discovery (finding definitions), the action provider (offering and performing Add), and the dialog contract (describing a choice) are separate concerns in separate files.
- **Modular Design**: discovery depends only on the shared `DiagramDefinition`/`DiagramOrigin` abstraction. Core code must remain compilable and testable with zero diagram-type modules present.
- **Dependency Management**: the dependency direction stays one-way - diagram modules may depend on core abstractions, never the reverse (structure.md, *Module boundaries*).
- **Clear Interfaces**: Add is contributed through the existing `IContextActionProvider` seam; it must not require a new special case in the resolver, the context menu, or the prompt host beyond a genuinely new *kind* of prompt.
- **Testability**: discovery must be exercisable against assemblies supplied by a test, without spinning up the host or depending on which diagram modules happen to be built.

### Performance

- Discovery runs **once** at startup, not per request; a context-menu open or dialog display SHALL NOT trigger a reflection scan.
- Startup cost SHALL stay proportional to the number of assemblies scanned, and SHALL NOT force every referenced assembly to be fully JIT-loaded when only its type metadata is needed.

### Security

- Discovery SHALL load only assemblies already deployed with the application; it SHALL NOT scan or load assemblies from user-supplied or project-supplied paths, which would make opening a project a code-execution vector.
- The client SHALL NOT receive or supply filesystem paths at any point in the Add flow, consistent with the existing context-action contract.

### Reliability

- A malformed or unloadable diagram-type module SHALL degrade to that one type being absent, never to a failed startup (Requirement 1.4-1.5).
- The Add flow SHALL tolerate the target folder changing between the menu opening and the confirmation, per Requirement 4.5.

### Usability

- The dialog SHALL be usable by keyboard alone: moving through groups and types, confirming, and dismissing.
- With dozens of diagram types the dialog SHALL remain navigable - grouping is what makes the list tractable, so groups SHALL be visually distinct from the types inside them.
