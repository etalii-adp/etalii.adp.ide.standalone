# Backend consistency — requirements

A survey of `src/backend/**` (14 projects, 228 non-test source files) for defects, pattern
deviation, naming inconsistency and dead code. Nothing was fixed; every line below cites a
file and line verified against the tree at `8dfb7f64`.

The lane excludes the client, `src/diagrams/**`, protos and build files, and the test
projects, each of which is another agent's survey.

## Introduction

The backend is in better shape than a survey usually finds. There are no swallowed
exceptions, no `async void`, no dead types, and dependency injection is perfectly uniform.
What the scan did find is one genuine defect class with three instances, a handful of
smaller defects, and — worth as much as the findings — five categories of apparent
deviation that are correct as they stand and should never be "fixed".

## Requirement 1: user-editable files are read through the shared reader

**User story:** As a developer, I want every read of a file the user can edit to go through
`SharedDocumentReader`, so that a save landing during a read does not fail with a sharing
violation the user cannot explain.

`SharedDocumentReader` exists precisely for this. Its own documentation states the hazard:
`File.ReadAllText` and friends open with `FileShare.Read`, Windows sharing is mutual, and so
a save landing while such a read is in flight fails and the user is told the file "could not
be written". Three reads of user-editable documents bypass it.

### Acceptance criteria

1. WHEN `AddDiagramContextActionProvider` tests whether a file suggests a body
   (`src/backend/EtAlii.Adp.Backend/Hierarchy/AddDiagramContextActionProvider.cs:131`)
   THEN it SHALL read through `SharedDocumentReader`. Today the `IOException` is caught and
   the file is reported as not suggesting a body, so a save in flight silently removes an
   offer from the user's menu.
2. WHEN `RegistrationLayout.ReadLines` reads an `.adp` registration
   (`src/backend/EtAlii.Adp.Backend/Hierarchy/RegistrationLayout.cs:293`)
   THEN it SHALL read through `SharedDocumentReader`. This is the sharpest of the three: the
   same class writes layout blocks, so a read losing to a concurrent write means a dragged
   element's position silently fails to persist.
3. WHEN `TextFileBuffer` opens a file for the text editor
   (`src/backend/EtAlii.Adp.Editor/TextFileBuffer.cs:106`, `File.ReadAllBytes`)
   THEN it SHALL read with the same sharing. This is the exact user-visible symptom the
   shared reader's documentation describes.
4. WHERE a read targets a file ADP itself owns rather than one the user edits — the problem
   cache at `src/backend/EtAlii.Adp.Backend/Problems/ProblemStore.cs:145` and `:274`, and the
   projects file at `src/backend/EtAlii.Adp.Backend/Projects/FileProjectStore.cs:62` — the
   change is optional and lower priority, because no user-driven writer contends for them.
5. THE codebase SHALL carry a guard that fails when a new direct read of a user document
   appears, because nothing prevents the next one. This is the unguarded class named in
   Requirement 5.

## Requirement 2: a fire-and-forget task never faults unobserved

**User story:** As a developer, I want every discarded task to carry a terminal `catch`, so
that a failure inside one leaves a log line rather than vanishing.

Five sites start work with `_ = SomethingAsync()`. One of them is exemplary and the others
are partial.

### Acceptance criteria

1. WHERE `TrackedProblemRoot` discards a task
   (`src/backend/EtAlii.Adp.Backend/Problems/TrackedProblemRoot.cs:80`)
   THE whole body is already inside `try`/`catch` with an `Error` line. THIS is the shape the
   others SHALL follow, and it needs no change.
2. WHEN `ContextSelectionStore` re-derives a selection off-thread
   (`src/backend/EtAlii.Adp.Backend/Context/ContextSelectionStore.cs:157` and `:202`, running
   `RediscoverAndPushAsync` at `:213`)
   THEN the terminal `catch` SHALL cover the whole method. Today it covers only the
   `rediscover` call; the `lock` block after it — which maps the record to the wire and writes
   it — is outside, so a mapping failure there faults the task with no trace at all.
3. WHEN `HierarchyService` starts root recovery and root-presence watching
   (`src/backend/EtAlii.Adp.Backend/Hierarchy/HierarchyService.cs:86` and `:97`)
   THEN each SHALL carry a terminal `catch`. The inner handling covers cancellation only.

## Requirement 3: blocking on async work is confined and explained

**User story:** As a developer, I want a synchronous wait on asynchronous work to be rare and
justified in a comment, so a reader can tell a considered choice from an oversight.

### Acceptance criteria

1. WHEN `HistoryActionsBroadcaster.Broadcast` resolves actions
   (`src/backend/EtAlii.Adp.Backend/Context/HistoryActionsBroadcaster.cs:64`,
   `DiscoverAsync(...).AsTask().GetAwaiter().GetResult()`)
   THEN the block SHALL either become asynchronous or carry a comment saying why blocking a
   timer callback is safe here. It is the only sync-over-async in the lane. The risk is low —
   ASP.NET Core installs no synchronisation context — but the reasoning is not written down.
2. WHERE `HistoryStack.Clear` calls `_gate.Wait()`
   (`src/backend/EtAlii.Adp.Backend/History/HistoryStack.cs:254`)
   THE call SHALL be left alone: `_gate` is a `SemaphoreSlim` (`:24`) and `Wait()` is its
   correct synchronous API, not a blocking wait on a task.

## Requirement 4: smaller defects

### Acceptance criteria

1. WHEN `StartupRevalidation` is disposed of by the host
   (`src/backend/EtAlii.Adp.Backend/Problems/StartupRevalidation.cs:23`)
   THEN its `CancellationTokenSource` SHALL be disposed. The class is `sealed`, implements
   `IHostedService`, holds `private readonly CancellationTokenSource _stopping = new()`, and
   implements neither `IDisposable` nor `IAsyncDisposable`. Severity is low — one instance,
   process lifetime — but it is the only undisposed disposable in the lane.
2. WHEN `DiagramService.TryResolveBody` fails
   (`src/backend/EtAlii.Adp.Backend/Diagrams/DiagramService.Open.cs:168`, `origin = null!`)
   THEN the signature SHOULD say so with `[NotNullWhen(true)]` rather than a null-forgiving
   assignment. This is idiomatic for a `Try` method and is the lane's only nullability
   suppression; it is listed for completeness, not urgency.

## Requirement 5: the unguarded class

**User story:** As a developer, I want the sharing-mode discipline enforced by a test rather
than by memory, so that the next direct read is caught before it ships.

Every other discipline in this repository has a guard: line endings have `.gitattributes` and
a test, dependencies have `DependencyInventoryTests`, the npm lock has `WorkspaceLockTests`,
style has `dotnet format`. The sharing-mode rule has only `SharedDocumentReader`'s own
documentation, and three call sites already drifted past it. The failures are invisible by
construction — each is caught as `IOException` and turned into a silent degradation or a
vague message — so nothing gets louder as the problem spreads.

### Acceptance criteria

1. THE test suite SHALL fail when a source file under `src/backend/**` reads a
   user-editable document through `File.ReadAllText`, `File.ReadAllLines`, `File.ReadAllBytes`,
   `File.OpenRead` or a bare `new FileStream`, naming the file and the replacement.
2. WHERE a read targets a file ADP owns exclusively, the guard SHALL allow it through a
   stated exemption rather than by not noticing.

## What was checked and found correct

Recorded so that a later reader does not "fix" any of it. Each was verified, not assumed.

1. **Three loggers built with `typeof(...)` rather than the generic form** —
   `ClientDevServerProxy.cs:18`, `AdpFileWriter.cs:41`, `ProjectRootResolver.cs:11`. All three
   are `static` classes, and C# forbids a static type as a type argument, so
   `Log.ForContext<T>()` cannot compile there. `Log.ForContext(typeof(X))` is the only option
   and is correct.
2. **`EditorResolver` takes an `ILogger` through its constructor** (`EditorResolver.cs:40`).
   The code states the reason: the once-at-`Error` contract must stay assertable, and the
   shared static pipeline is replaced whenever an integration test builds a host.
3. **Twelve async methods without an `Async` suffix** — all in `ContextService`,
   `DiagramService` and `HierarchyService`. Every one is a `public override` of a generated
   gRPC base method whose name comes from the proto. They cannot be renamed here.
4. **Ten types that appear only at their own declaration** — every one is a static container
   for extension methods (`ServiceCollectionAdd...Extension` and kin). Callers use the
   methods; the type name is never written. Not dead code.
5. **No empty or comment-only `catch` block exists anywhere in the lane**, and there is no
   `async void`. Both were searched for exhaustively.
6. **Dependency injection is uniform**: 47 registrations, all `AddSingleton`, no `AddScoped`
   or `AddTransient` anywhere.

## Non-functional requirements

### Scope

Findings are `src/backend/**` only. Where the lane touches another survey — `TextFileBuffer`
sits in `EtAlii.Adp.Editor`, and the sharing-mode discipline reaches diagram modules that read
their own documents — this document notes it and stops, rather than following the thread.

### Method and its limits

Every finding cites a verified `path:line`. Dead-code detection was type-level only: a
member-level sweep needs a real analyzer, so unreferenced methods and properties are not
covered and should not be assumed absent. Reflection- and DI-resolved types were treated as
referenced.

### Relationship to existing work

The `file-io-centralization` spec already exists and appears to cover the same discipline
Requirement 1 describes. If its scope includes these three call sites, Requirements 1 and 5
should be folded into it rather than implemented twice.
