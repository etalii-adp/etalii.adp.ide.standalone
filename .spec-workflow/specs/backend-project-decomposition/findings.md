# Findings — the implementer's measurement snapshot

An implementer's measurement of `EtAlii.Adp.Backend`'s internal structure, taken **2026-09-06** by Developer 3 for this specification's design work. The folder-level graph was measured on the tree at `9f57d769`; the per-edge type map below was measured a few hours later against the main checkout at `0706beea` (a working tree other sessions write to — treat single-type differences against a clean checkout as possible drift, not as errors). This file is the provenance trail behind the "verified 2026-09-06" figures the requirements carry.

## Method, and the blind spots it is known to have

Two instruments, deliberately different so their disagreement is an alarm:

- **A per-file parse** of `namespace` and `using` declarations (every `.cs`, `obj/` excluded) produced the folder-level graph. Trustworthy for import edges; blind **by construction** to partial qualification (`Backend.Diagrams.IDiagramSessionFactory` — two real instances existed the same day) and to types reachable through a parent namespace with no `using` at all.
- **A type-level word-match**: harvest every `public`/`internal` type declaration per folder, then search every other folder's sources for those names **with comments and string literals stripped**. This sees what the import parse cannot.

Two blind-spot classes were caught during the measurement itself, and both matter to anyone re-deriving these numbers:

1. **Doc-comments fabricate edges when not stripped.** The first type-level pass reported `Sessions → History` and `Problems → History` edges on `ServiceCollectionAddCommandsExtension`. Both were `<see cref>` doc-comment mentions only; the comment-stripped pass shows **no such code edges**. Two independent confirmations (Developer 3's first pass and a re-derivation by type-name grep) agreed on the phantom because they shared the blind spot — agreement between instruments with the same blind spot is not confirmation.
2. **Extension methods are consumed by method name, not type name.** `services.AddCommands()` never mentions `ServiceCollectionAddCommandsExtension`, so a type-name search misses every registration call site. Measured directly: History's `AddCommands()`/`AddHistoryActions()` are called by `EtAlii.Adp.Backend.Service/Program.cs`, by `Backend.Tests`' support code, and by diagram-module test composition roots — **not** by any Backend folder.

## Corrections to figures relayed earlier in session traffic

- History is **19 of 19** files in the bare root namespace `EtAlii.Adp.Backend` (Architect 1's re-read; an earlier relay said 15).
- `Sessions → History` and `Problems → History` are **doc-comment references, not code dependencies** (see above). The real in-Backend History consumers are **Context** and **Hierarchy** only.

## The per-edge type map

For each folder, the concrete types it consumes from each other folder, from `EtAlii.Adp.Diagram`, `EtAlii.Adp.Editor`, and from the **generated proto types** — the last being silent edges: the generated namespace is an ancestor of every folder's namespace, so no `using` betrays them.

```text
Authentication -> (generated protos) (11): DescribeProductRequest, DescribeProductResponse, DeveloperSessionRequest, DeveloperSessionResponse, LoginError, LoginRequest, LoginResponse, LogoutRequest, LogoutResponse, SessionToken, ShortGuid
Authentication -> Sessions (2): ISessionStore, SessionContext

Client -> (generated protos) (2): Path, Remove

Context -> (generated protos) (50): Add, CancelInteractionRequest, CancelInteractionResponse, ChoiceDialogPrompt, ConfirmDialogPrompt, ContextAction, ContextActionGroup, ContextLevelDetail, ContextMessage, ContextNotice, ContextOption, ContextProjectActions, ContextPrompt, ContextProperty, ContextPropertyEditor, ContextScope, ContextSelection, ContextSelectionAction, ContextSelectionChanged, ContextSelectionSource, ContextShortcut, ContextSource, ContextTextField, DescribePropertiesRequest, DescribePropertiesResponse, DetailOneofCase, DiscoverActionsRequest, DiscoverActionsResponse, ElementId, ExecuteActionRequest, ExecuteActionResponse, Group, InlineLabelEdit, InputDialogPrompt, Path, Project, ProjectProblems, ProposeInputRequest, ProposeInputResponse, Remove, SelectRequest, SelectResponse, SetPropertyRequest, SetPropertyResponse, ShortGuid, SourceOneofCase, SubmitInteractionRequest, SubmitInteractionResponse, TriggerOneofCase, WatchContextRequest
Context -> EtAlii.Adp.Diagram (1): DiagramOrigin
Context -> Hierarchy (1): Line
Context -> History (3): HistoryChangedEventArgs, IContextNoticeSink, IHistoryStackStore
Context -> Problems (3): ProblemBroadcaster, ProblemMaintenance, StartupRevalidation
Context -> Projects (2): IProjectStore, ProjectRootResolver
Context -> Sessions (1): SessionContext

Hierarchy -> (generated protos) (26): Add, ContextLevelDetail, ContextScope, ContextSelectionSource, ContextSource, ElementId, Entries, Entry, EntryCreated, EntryDetail, EntryKind, EntryRemoved, EntryRenamed, EntryUpdated, Group, HierarchyChange, HierarchyMessage, ListEntriesError, ListEntriesRequest, ListEntriesResponse, Path, Remove, RootUnavailable, ShortGuid, SourceOneofCase, WatchHierarchyRequest
Hierarchy -> Context (28): ContextActionDefinition, ContextActionGroupDefinition, ContextChoiceRequest, ContextCommitResult, ContextConfirmationRequest, ContextExecutionCompleted, ContextExecutionFailed, ContextExecutionRequiresChoice, ContextExecutionRequiresConfirmation, ContextExecutionRequiresInput, ContextExecutionResult, ContextInputRequest, ContextLevelResolution, ContextNesting, ContextOptionAnnotations, ContextOptionNode, ContextPropertyDefinition, ContextPropertyResult, ContextResolvedLevel, ContextShortcutDefinition, ContextTarget, ContextTextFieldRequest, ContextValidationResult, IContextActionProvider, IContextPropertyProvider, IContextSourceResolver, RejectedContextLevel, ResolvedContextLevel
Hierarchy -> EtAlii.Adp.Diagram (4): DiagramDefinition, DiagramDocumentFactories, DiagramOrigin, IDiagramDefinitionCatalog
Hierarchy -> EtAlii.Adp.Editor (3): EditorDefinition, IEditorDefinitionCatalog, TextFileBuffer
Hierarchy -> History (4): CommandResult, ICommand, ICommandHandler, IHistoryStackStore
Hierarchy -> Projects (2): IProjectStore, ProjectRootResolver
Hierarchy -> Sessions (1): SessionContext

History -> (generated protos) (3): ContextScope, Path, Project
History -> Context (11): ContextActionDefinition, ContextActionGroupDefinition, ContextCommitResult, ContextExecutionCompleted, ContextExecutionFailed, ContextExecutionResult, ContextShortcutDefinition, ContextTarget, ContextValidationResult, HistoryActionsBroadcaster, IContextActionProvider
History -> EtAlii.Adp.Diagram (3): DiagramDefinition, DiagramDefinitionCatalog, IDiagramDefinitionCatalog
History -> Hierarchy (16): CreateDiagramFileCommand, CreateDiagramFileCommandHandler, CreateFolderCommand, CreateFolderCommandHandler, DeleteEntryCommand, DeleteEntryCommandHandler, RemoveCreatedFolderCommand, RemoveCreatedFolderCommandHandler, RemoveRegistrationLayoutCommand, RemoveRegistrationLayoutCommandHandler, RenameEntryCommand, RenameEntryCommandHandler, SaveTextFileCommand, SaveTextFileCommandHandler, SetRegistrationLayoutCommand, SetRegistrationLayoutCommandHandler

Problems -> (generated protos) (16): Add, ContextLevelDetail, ContextScope, ContextSelectionSource, ContextSource, ElementId, Path, Problem, ProblemFileLocation, ProblemLocation, ProblemSetState, ProblemSeverity, ProjectProblems, Remove, ShortGuid, SourceOneofCase
Problems -> Context (17): ContextActionDefinition, ContextActionGroupDefinition, ContextCommitResult, ContextExecutionCompleted, ContextExecutionFailed, ContextExecutionResult, ContextLevelResolution, ContextNesting, ContextResolvedLevel, ContextShortcutDefinition, ContextTarget, ContextValidationResult, IContextActionProvider, IContextSelectionStore, IContextSourceResolver, RejectedContextLevel, ResolvedContextLevel
Problems -> EtAlii.Adp.Diagram (8): DiagramProblem, DiagramProblemElementLocation, DiagramProblemFileLocation, DiagramProblemLineLocation, DiagramProblemLocation, DiagramProblemSeverity, DiagramValidationRequest, DiagramValidators
Problems -> Hierarchy (11): AdpFileWriter, DiagramAmbiguousExtension, DiagramFileName, DiagramFileRouter, DiagramRouted, DiagramUnknownType, DiagramUnreadable, HierarchyTargets, Line, NotADiagram, SharedDocumentReader

Projects -> (generated protos) (12): Add, AddProjectError, AddProjectRequest, AddProjectResponse, ListProjectsRequest, ListProjectsResponse, Path, Project, Remove, RemoveProjectRequest, RemoveProjectResponse, ShortGuid
Projects -> Hierarchy (1): AdpFileWriter
Projects -> Sessions (1): SessionContext

Sessions -> (generated protos) (1): ShortGuid
Sessions -> Authentication (3): IAuthenticator, LocalAuthenticator, LocalAuthenticatorOptions
```

## What the map says that the folder graph could not

- **The Context↔Hierarchy cycle is radically asymmetric**: Hierarchy consumes 28 Context types (the whole context-provider contract), while Context consumes exactly **one** Hierarchy type — `Line`. Re-homing or duplicating `Line` breaks the fattest cycle in the graph at a single point.
- **The History↔Hierarchy cycle is the dispatcher-versus-implementations shape**: Hierarchy consumes History's *contract* (`ICommand`, `ICommandHandler`, `CommandResult`, `IHistoryStackStore`); History consumes Hierarchy's sixteen *command implementations* — all eight command/handler pairs. The classic resolution (contract low, implementations high, dispatcher discovers via DI rather than naming types) fits exactly; where History names the handler types, it is presumably wiring, movable to the composition root.
- **The Authentication↔Sessions cycle is five types in total** (`ISessionStore` + `SessionContext` one way, `IAuthenticator` + `LocalAuthenticator` + `LocalAuthenticatorOptions` the other) — small enough to break with one relocation decision.
- **The silent proto edges are large and universal**: Context 50 generated types, Hierarchy 26, Problems 16, Projects 12, Authentication 11. Every functional project must sit above whichever single project owns generation (the requirements' single-owner constraint), and `ShortGuid` — a proto message with cast operators added by `ShortGuid.Cast.cs`, itself already in namespace `EtAlii.Adp.Contracts` — is consumed by six of the eight folders.
- **`Client` consumes only two proto types** and nothing from any folder — confirming it as the one freely movable piece.
- **`SessionContext` is the one type every layer touches** (Authentication, Context, Hierarchy, Projects all consume it): wherever Sessions lands, that type is effectively part of the bottom contract.

## The Backend.Diagrams split, per file (for the Diagram-merge half of the mandate)

Measured before this spec existed and re-stated here for the design: of `EtAlii.Adp.Backend.Diagrams`' twenty files, the session contract is Backend-free — `IDiagramSession`, `IDiagramSessionFactory`, `DiagramSessionFactories`, the delta `_Model` (all eight files), `DiagramElement`, `DiagramViewport`, `IDiagramViewportRegistry`, `DiagramViewportRegistry`, `IDiagramDocumentReloader`. Backend-bound: the three `DiagramService` partials and `ServiceCollection.AddDiagrams` (Hierarchy, Projects, Sessions, History, generated gRPC base), `DiagramDocumentReloadBridge` (Hierarchy), and `EditorSessionAdapter`/`EditorSessionFactories` (`EtAlii.Adp.Editor`).
