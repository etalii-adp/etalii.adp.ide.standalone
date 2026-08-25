# Requirements Document

## Introduction

ADP can list and rename what is already in a project, but it cannot yet create anything. This spec adds the missing verb: an **Add** context action, offered on a folder and on the hierarchy's empty space (the project root, with nothing selected), which opens a dialog listing every diagram type the running installation supports and carries the chosen one to the point where it would be created.

For that list to exist, the backend has to know which diagram types it is carrying. The 57 diagram-type modules scaffolded under `src/diagrams/` each already publish their identity as a static `Diagram.Definition` of type [`DiagramDefinition`](../../../src/backend/EtAlii.Adp.Diagram/_Model/DiagramDefinition.cs) (`DiagramOrigin` + `Title`). This spec makes the backend **discover** those definitions at startup by reflection rather than by a hand-maintained registry, and exposes them through a single cached `DiagramDefinition.All`.

The dialog presents them the way [docs/diagrams.md](../../../docs/diagrams.md) already catalogs them: grouped hierarchically by their MIME-type-style origin (`vendor/type`), labelled by their human title.

This spec covers discovery, the action, and the dialog, end to end - up to and including the point where a chosen diagram type is confirmed. It deliberately does **not** create the diagram file yet: `DiagramDefinition` carries no file format, extension or empty-document template, so there is nothing to write. Confirming a type reaches a well-defined commit seam that a later spec fills in once modules describe their documents (Requirement 6). Everything around that seam is put in place now.

Undo is out of scope: nothing in the Add flow touches the history stack, consistent with delete today.

### Relationship to `context-service`

[`context-service`](../context-service/requirements.md) moves the context-action RPCs and the prompt channel off `HierarchyService` onto a dedicated `ContextService`, drops the `scope` field from those requests, and pushes a selected target's available actions down its own `Watch` stream. It explicitly keeps the provider abstractions — `IContextActionProvider`, `IContextActionResolver`, `ContextTarget` — unchanged.

This spec therefore contributes Add through those same unchanged abstractions, and expects the action to be discovered and its dialog delivered over whichever channel is current when it is implemented. Two consequences follow, and are stated as requirements rather than left implicit: the "nothing selected" case (Requirement 4.2) is the context service's own notion of a selection that names no entry, and the new dialog kind this spec needs (Requirement 5) is a new prompt on that channel, not a second, parallel prompt mechanism.

## Alignment with Product Vision

- [product.md](../../steering/product.md)'s **"Don't reinvent, integrate"**: the grouping vocabulary is the origin tag the diagram catalog already uses, not a new taxonomy invented for a dialog.
- product.md's **"Files are the source of truth"**: when creation is filled in at the commit seam, Add will produce a real file in the project folder that becomes visible through the same `RootFolderWatcher` → `EntryCreated` path as a file created outside ADP, not through a special-cased client-side insert. Requirement 6.5 pins that now so the seam cannot be designed to preclude it.
- product.md's **"Minimal footprint, incremental value"**: a diagram-type module contributes itself by existing and being deployed. Nothing in core code enumerates diagram types, so adding the 58th is a new project rather than an edit to a registry.
- [structure.md](../../steering/structure.md)'s **"Core vs diagram-type plugins"**: core must not depend on any single diagram type's schema. Discovery therefore reads only the shared `DiagramDefinition` abstraction, never a module's own types.
- [tech.md](../../steering/tech.md)'s **"the backend is the sole owner of reading/writing files"**: the client never composes a path and will never write the file; it names a chosen diagram type and the backend does the rest.

## Requirements

### Requirement 1 — Diagram types discovered at startup by reflection

**User Story:** As a developer adding a diagram-type module, I want the backend to find my module on its own, so that contributing a diagram type never means editing a central list.

#### Acceptance Criteria

1. WHEN the backend starts THEN the system SHALL search the application's assemblies **whose simple name starts with `EtAlii.Adp`** for a **static class named `Diagram`** exposing a **public static `Definition` property** of type `DiagramDefinition`, and collect each one it finds. Assemblies outside that prefix (the framework, gRPC, third-party packages) are never inspected.
2. WHEN discovery completes THEN the system SHALL expose the collected definitions through a **static `DiagramDefinition.All` property with a getter only** - no setter, no public way to replace it - holding a collection computed once per process and reused for every later read.
3. WHEN `DiagramDefinition.All` is read more than once THEN the system SHALL return the same cached collection without repeating the reflection scan.
4. WHEN discovery is implemented THEN the **scan itself SHALL be a separate, instantiable component** that takes the assemblies to inspect and a logger as inputs, and `DiagramDefinition.All` SHALL be the cached result of running it once at startup against the application's assemblies. This is what lets a test drive the scan against assemblies it supplies, and lets the scan log, while `All` stays a plain cached property.
5. IF an assembly cannot be inspected (it fails to load, or its types cannot be enumerated) THEN the system SHALL skip that assembly, record why, and continue discovering the remaining ones rather than failing startup.
6. IF a candidate `Diagram` class is found whose `Definition` property is missing, non-public, non-static, or not a `DiagramDefinition` THEN the system SHALL skip it and record it as malformed rather than throwing.
7. IF two discovered definitions carry the same origin THEN the system SHALL keep the one from the assembly whose name sorts first (ordinal), record the collision naming both assemblies, and continue - so the outcome is the same on every run, and a duplicate never produces two indistinguishable entries in the dialog.
8. WHEN discovery runs THEN the system SHALL NOT require core code to name, reference by type, or otherwise know about any specific diagram type.

### Requirement 2 — Discovery is observable

**User Story:** As someone running or debugging ADP, I want the log to tell me which diagram types were found, so that a missing entry in the Add dialog is diagnosable without a debugger.

#### Acceptance Criteria

1. WHEN discovery completes THEN the system SHALL log, at information level, how many diagram types were discovered and how many assemblies were scanned.
2. WHEN a diagram type is discovered THEN the system SHALL log its origin and title, so the log shows *what* was found and not merely how many.
3. WHEN an assembly is skipped, a candidate is malformed, or an origin collides (Requirement 1.5-1.7) THEN the system SHALL log that at warning level, naming the assembly involved.
4. IF no diagram types are discovered at all THEN the system SHALL log a warning, since an empty Add dialog is far more likely a deployment or reference problem than a genuine state.

### Requirement 3 — Diagram-type modules reach the running application

**User Story:** As someone deploying ADP, I want the diagram types that ship with it to actually be discoverable, so that the Add dialog reflects what was built.

#### Acceptance Criteria

1. WHEN the backend runs THEN every diagram-type module intended to ship SHALL be present among the assemblies discovery searches. The host already references them as a wildcard - `..\..\diagrams\*\backend\*\*.csproj`, excluding test projects, in [`EtAlii.Adp.Backend.Service.csproj`](../../../src/backend/EtAlii.Adp.Backend.Service/EtAlii.Adp.Backend.Service.csproj) - so this holds without a per-module reference.
2. WHEN discovery decides which assemblies to search THEN it SHALL use the **breadth-first walk described in [How to find all application assemblies](https://www.davidguida.net/how-to-find-all-application-assemblies)**: start from `Assembly.GetEntryAssembly()`, take each visited assembly's `GetReferencedAssemblies()`, `Assembly.Load` each reference not yet visited, and track visited assemblies by `FullName` in a set - filtered to the `EtAlii.Adp` prefix (Requirement 1.1) so the walk never descends into the framework or third-party packages. It SHALL NOT rely on `AppDomain.CurrentDomain.GetAssemblies()`, which lists only what the runtime has already loaded and under-reports on a cold start - the lazy-loading problem the article's walk exists to avoid.
3. WHEN the walk is seeded THEN the queue SHALL start with **the entry assembly and every `EtAlii.Adp`-prefixed assembly listed in the entry assembly's deployment manifest (`.deps.json`)**, not the entry assembly alone. This addresses a gap the article does not cover, because it does not arise in the article's scenario: the C# compiler records a metadata reference (`AssemblyRef`) only for assemblies whose types the code actually uses, and the host uses no type from any diagram module. Measured on the current build output, `GetReferencedAssemblies()` on the built host returns **12** references and **0** diagram modules, while its `.deps.json` lists all **57** deployed. Run verbatim, the article's walk would therefore visit the host, `EtAlii.Adp.Backend`, `EtAlii.Adp` and stop, finding nothing - a walk cannot reach an assembly no visited assembly references. Seeding from the manifest puts the modules in the queue; from there the walk proceeds exactly as the article describes, and also catches anything a module references in turn.
4. WHEN a diagram-type module is added to the solution THEN making it discoverable SHALL require no change to discovery code **and no change to the host's project references**, since the wildcard already covers a newly added module and the manifest the walk is seeded from is regenerated on build.
5. IF a diagram-type module is referenced but contributes no `Diagram.Definition` THEN the system SHALL treat that as the module simply having nothing to contribute, not as an error - the scaffolded modules are expected to gain definitions over time rather than all at once (as it happens, all 57 already declare one).

### Requirement 4 — An Add action on a folder and on the root

**User Story:** As an architect working in a project, I want to add a new diagram where I am in the tree, so that I don't have to leave ADP or create the file by hand.

#### Acceptance Criteria

1. WHEN the user opens the context menu on a **folder** in the hierarchy THEN the system SHALL offer an **Add** action among that folder's actions.
2. WHEN the user opens the context menu on the hierarchy's **empty space**, with no entry selected THEN the system SHALL offer the same Add action, targeting the **project root folder**. This is the one case the current `ContextSource` cannot express - it carries an `entry_id` and nothing else - so the contract SHALL gain a way to name the root without naming an entry.
3. WHEN the user opens the context menu on a **file** THEN the system SHALL offer Add only as **registration**: the file becomes a diagram of a type the user names, by writing an `.adp` beside it. The dialog SHALL list only the diagram types that declare that file's extension, and the action SHALL NOT be offered on a file that already has an `.adp`, or whose extension no type declares. Nothing in the file itself is created, modified or moved.
4. WHEN Add is triggered THEN the system SHALL open a dialog for choosing a diagram type, following the same backend-initiated prompt flow the existing rename and delete actions use.
5. IF the target folder no longer exists when Add is triggered THEN the system SHALL report that rather than creating anything.
6. WHEN the action is offered THEN its availability and any keyboard shortcut SHALL be described by the backend as data, exactly as existing actions are, so the client gains no knowledge of what Add means.
7. WHEN Add is offered on a folder or the root, and when it is offered on a file, THEN the two SHALL read as different acts and the action's label SHALL say which: on a folder it **creates** a diagram inside it; on a file it **registers** that file. They share the dialog, the option tree and the commit seam, but not their meaning.

> **Correction to Requirement 4.3.** The first version said Add SHALL NOT be offered on a file, because "a file cannot contain a new entry". That reasoning is sound and it is also narrower than it looked: it rules out creating a diagram *inside* a file, which nothing has ever wanted to do. It does not cover creating an `.adp` *beside* a file, in the folder that already contains it — which is an ordinary create in an ordinary folder, and the only way a repository's existing document becomes a diagram without ADP guessing at it.
>
> The case that forced this was [`azure-pipeline-diagram`](../azure-pipeline-diagram/requirements.md) Requirement 2.3. Its documents are `azure-pipelines.yml` files a repository already has, and `.yml` is far too common an extension to route on sight — a repository is full of workflows, compose files and manifests that are not pipelines. The alternative considered was sniffing each file's content to guess which ones were pipelines; that was rejected as both slower and less honest than an option the user picks once. Registration needs Add on a file, so Requirement 4.3 was revised rather than worked around.

**User Story:** As an architect choosing a diagram type, I want the choices organised the way the notations themselves are, so that I can find the one I want among dozens.

#### Acceptance Criteria

1. WHEN the dialog opens THEN the system SHALL list every diagram type from `DiagramDefinition.All`, each shown by its **`Title`**. The existing prompt kinds - `InputDialogPrompt` and `ConfirmDialogPrompt` - cannot express a grouped list of choices, so the contract SHALL gain a prompt describing a hierarchy of selectable options as data, keeping the client free of any knowledge of diagram types.
2. WHEN the dialog lists diagram types THEN the system SHALL **group them by their origin's `Vendor`**, with each diagram type listed under its vendor by `Title`. `Type` is not a grouping level: the origin's `Type` *is* the diagram type, so grouping by it would put every title in a group of one (`c4` → `context` → "System Context", 57 times over).
3. IF a diagram type's origin carries a `Subtype` THEN the system SHALL nest it under the diagram type sharing its `Vendor`/`Type`, so a family with subtypes reads as one entry with children rather than as unrelated siblings.
4. WHEN a group is displayed THEN the system SHALL label it by its vendor segment, so the grouping is legible rather than implied by indentation alone.
5. WHEN the dialog is open THEN the system SHALL keep its confirm action disabled until a diagram type - not merely a group - is selected.
6. WHEN the user confirms a chosen diagram type THEN the system SHALL hand the chosen type and the target folder to the commit seam (Requirement 6) and close the dialog.
7. IF no diagram types were discovered THEN the dialog SHALL say so plainly instead of presenting an empty list with an enabled confirm button.
8. WHEN the dialog is dismissed without confirming THEN the system SHALL do nothing further.

### Requirement 6 — The commit seam: everything in place, creation deferred

**User Story:** As a developer filling in diagram creation later, I want the whole Add flow to already reach one well-defined point with the chosen type and the target folder in hand, so that creating the file is a local change, not a rework of the flow.

#### Acceptance Criteria

1. WHEN a diagram type is confirmed THEN the system SHALL reach a single commit step that receives the chosen `DiagramDefinition` and the resolved target folder - the same step that will later create the file - and SHALL NOT create, modify or delete anything on disk in this spec.
2. WHEN that commit step runs in this spec THEN the system SHALL tell the user, through the existing interaction-closed message, that creating this diagram type is not supported yet, naming the type by its title - so the flow ends visibly rather than silently.
3. WHEN the commit step is implemented THEN it SHALL be the only place a later spec needs to change to create the file: the action, the dialog, the selection, and the delivery of the chosen type to the backend SHALL NOT need to change.
4. IF the target folder no longer exists when the commit step runs THEN the system SHALL report that rather than reporting "not supported yet", so the later implementation inherits the check instead of rediscovering it.
5. WHEN file creation is later added at this seam THEN the new file SHALL appear in every connected client's hierarchy through the existing `EntryCreated` change flow, not through a client-side insertion - this is stated now so the seam is not designed in a way that precludes it.

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

- A malformed or unloadable diagram-type module SHALL degrade to that one type being absent, never to a failed startup (Requirement 1.5-1.6).
- The Add flow SHALL tolerate the target folder changing between the menu opening and the confirmation, per Requirements 4.5 and 6.4.

### Usability

- The dialog SHALL be usable by keyboard alone: moving through groups and types, confirming, and dismissing.
- With dozens of diagram types the dialog SHALL remain navigable - grouping is what makes the list tractable, so groups SHALL be visually distinct from the types inside them.
