# Technology stacks

Moved to [docs/architecture.md, *The three parts, and the single boundary*](../../docs/architecture.md#the-three-parts-and-the-single-boundary): .NET on the SDK `src/global.json` pins, React with TypeScript built by Vite, gRPC over grpc-web, one ASP.NET Core process serving both, and a canvas drawn as SVG.

# Development tools

* The development will mostly be done using Jetbrains Rider.
* The development team prefers an F5 experience, which means that development, testing and debugging should be doable in one go. I.e. locally and without any complex dependencies. To achieve this it should be possible to ramp up all relevant services using local-only persistency and hosting.
* How Vite and ASP.NET Core share the browser's port during F5, and why production runs no Vite at all: [docs/architecture.md, *The three parts, and the single boundary*](../../docs/architecture.md#the-three-parts-and-the-single-boundary).

# Naming & Identity

* The organization is called 'EtAlii', mind the uppercase E and A.
* The product name is an abbreviation 'ADP', which stands for 'A Different Perspective'.
* Namespaces (`EtAlii.Adp.<Area>` in .NET, `com.etalii.adp` elsewhere) and the identity type `ShortGuid`: [structure.md, *Naming*](structure.md#naming) keeps the rules applied while writing, and [docs/solution-structure.md](../../docs/solution-structure.md#namespaces-and-the-folders-that-do-not-contribute) the fuller treatment.
* **An agent commits under its own per-task identity, never the repository owner's.** Where that identity comes from and why is [CLAUDE.md, *Git identity*](../../CLAUDE.md#git-identity), and it is deliberately not repeated here: a second normative copy is how this file came to contradict it.
* **Committing in a checkout shared with other sessions** follows the pathspec discipline CLAUDE.md requires, and that discipline has a trap worth stating, because it has already cost this repository one mis-attributed commit:
  * **`git add <narrow paths>`, then `git commit -- <the same narrow paths>`.** Both halves are needed, and each closes a hole the other leaves open.
  * A bare `git commit` commits whatever is *staged*, anywhere in the tree, so it sweeps up another session's staged work.
  * `git commit -- <pathspec>` commits the **working-tree** state of everything matching the pathspec, *bypassing the index for those paths*. So it sweeps another session's **unstaged** edits under that path — and checking `git diff --cached` first does not warn you, because the index is not what it commits.
  * **The pathspec must be no broader than the change itself** — a single file when the change is a single file. Not merely "narrower than `.spec-workflow`": a directory is safe only in the sense that nobody happened to be editing it that second, which is timing rather than scoping. Commit `0aa7f666` changed 7 files under a pathspec covering 213, and came out clean by luck; `3e3ed952` under a similarly broad one carried six approval files belonging to two other sessions.
  * **The reason the trap is invisible is a plausible wrong model of what `-- <pathspec>` does.** It is natural to read `git add` as choosing the content and the pathspec as a filter over the index — under which a broad pathspec is harmless, because the index holds only what you added. That model is wrong, and the failure it produces is invisible in precisely the place you would look to check it.
  * The failure is silent: nothing errors, and the commit simply contains files you never touched. Verify with `git show --stat` after committing rather than assuming.
  * `git commit -- <path>` also fails outright on an *untracked* new file with "did not match any file(s) known to git", which is why the `git add` comes first.

# Runtime

Moved to [docs/architecture.md, *The three parts, and the single boundary*](../../docs/architecture.md#the-three-parts-and-the-single-boundary).

# Diagram storage

Moved to [docs/architecture.md, *Where a diagram lives*](../../docs/architecture.md#where-a-diagram-lives).

# Frontend-backend synchronization

Moved to [docs/architecture.md, *The two call legs*](../../docs/architecture.md#the-two-call-legs).

# gRPC call shapes

Moved to [docs/architecture.md, *The two call legs*](../../docs/architecture.md#the-two-call-legs).

# Implementation order

Moved to [processes.md, *Implementation order*](processes.md#implementation-order): data model, persistence, wire protocol, client UI, then remaining logic — each finished with its tests before the next, bottom-up because the dependencies are.

# Specifying a diagram type

A diagram type is a plugin, and what makes one complete is the same every time. A specification for a diagram type SHALL cover at least these four aspects; the *Implementation order* above is how they get built, this is what has to be decided before that starts.

* **File format type.** Which existing text format the diagram is stored in - an established one wherever possible, per *Diagram storage* - and how it is registered: the `.adp` file carrying the type's MIME line, the body in a sibling named by the type's `DiagramDefinition.Extension`, and the `IDiagramDocumentFactory` that produces an empty one. The spec says what a round trip guarantees (a document ADP did not change should come back unchanged), what happens to constructs the module does not model, and where ADP's own data goes when the format has no place for it - a sidecar, never an annotation smuggled into a file another tool owns.
* **Visualization.** What is drawn, and what decides where. Whether positions are **computed by the module** or **authored in the document** is the single biggest fork in a diagram type's design, and the spec must state which: a mindmap's layout is derived and never written back, a Wardley map's coordinates are the author's claim and moving one is an edit. The spec also covers what chrome the canvas needs beyond the elements themselves - axes, bands, swimlanes, a grid - how the type's element kinds are told apart, and how all of it maps onto the core `Element` and `Delta` vocabulary without extending it.
* **Toolbox.** What the type contributes to the Toolbox panel: which elements a user can pick or drag onto the canvas, how they are grouped and labelled, and what dropping one actually does. Like context actions, this is **described by the backend as data** - the client renders a palette it does not understand. The Toolbox is a placeholder today, so a spec states what its type contributes even though the host that shows it arrives separately; a type whose elements cannot be created that way says so, and says how they are created instead.
* **Context actions and commands.** What a user can do to an element and to the diagram as a whole. Every one of them is an `ICommand` with an inverse (*Commands*), offered through an `IContextActionProvider` and reaching the ribbon, the right-click menu and the keyboard through one path (*Context*), with any shortcut described as data. The spec names the commands, says what each one's inverse restores, and says what is **not** offered - in read-only mode, or when an action does not apply to the thing selected.

An aspect that genuinely does not apply is stated as not applying, with the reason, rather than left out - the same rule the implementation order uses for a step with nothing in it. These four are a floor, not the whole spec: pluggable registration, the selection chain, multi-client behaviour and the rest still belong in it where they are not already settled by an earlier type.

# Declaring a diagram definition

A module's `Diagram.cs` takes exactly one of two shapes, and which one is not a matter of taste - it tracks whether the module is implemented.

* **Stub**: `Definitions` is an inline `DiagramDefinition[]` array literal - no `Build` delegate, no named property. This is the shape a cataloged-but-unimplemented type keeps, and the one to copy when adding the next entry to the catalog.
* **Implemented**: each definition is a `public static DiagramDefinition` property named for what it identifies (`Mindmap`, `Pipeline`), each carrying a `Build` delegate, with `Definitions` referencing the properties (`[Pipeline]`) rather than repeating them. The named property exists because a module's own registrations and tests need to say which definition they serve without indexing into an array - `Diagram.Pipeline.Origin`, never `Diagram.Definitions[0].Origin`.
* **Family**: several definitions in one module, where **one** carries the `Build` delegate and the registration it names loops over the family's origins, registering a session factory, document factory and toolbox provider for each. `c4` is the worked example - one engine serves all its views, which share a model, a parser, a writer and a layout and differ only in which view they draw - and `databricks` follows it over bundle, job and pipeline. The siblings carry no `Build` of their own and are fully implemented notwithstanding.
* **So `Build is null` does not mean "a stub".** It means "registers nothing itself", which is true of a stub and equally true of a family sibling. Anything deciding whether a type is *served* must look at the registration rather than at the definition: `AddC4()` and `AddDatabricks()` both loop, and reading their doc comments rather than their code gives the same wrong answer twice. This has already produced one confident, wrong report that seven shipped types were unimplemented.
* **The trigger is gaining a `Build` delegate.** A module moves from stub to implemented exactly when its implementation spec starts wiring services - which is when `Build` appears somewhere in the module - so the transition needs no separate tracking. The family shape is not consistently applied and that is worth knowing rather than tidying: `rdf` puts `Build:` on all four of its definitions while `c4` and `databricks` put it on one, and both work because the registration is idempotent.
* **Every shape carries `[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]` on the class**, and so does an editor's `Editor` class. Discovery finds both by reflection and nothing calls them by name, so without it Rider reports the class and its members as unused - 48 modules' worth of findings that say nothing. `JetBrains.Annotations` is referenced once, for every project, in `src/Directory.Build.props`; no module references the package itself.
* **A folder-subject module declares no `DocumentExtension` constant, and that is not a gap.** `ansible-structure` has none because its subject is `DiagramSubject.Folder` - a folder has no sibling body to name an extension for. Do not "fix" that omission.

# Registering a diagram module's services

A module's service registration lives in **one file** - `ServiceCollection.AddX.cs` - by default.

* **Split into `ServiceCollection.AddX.cs` + `ServiceCollection.AddXCommands.cs` only once a concrete consumer - a test or another feature - needs the module's command handlers registered without its other seams** (session factory, toolbox provider, validator). The trigger is that need, not a line count or file size: no size threshold governs this, and none should be recorded.
* The evidence behind the rule, measured rather than assumed: `wardley-map` and `mindmap` are split because real call sites register the commands alone (`AddWardleyMap.Tests`' commands-only fact; mindmap's `Commands.Tests`, `MindmapSession.Tests` and `MindmapTestProject`). `c4`, `azure-devops-pipeline` and `ansible-structure` have no such consumer anywhere in their test suites, so they keep one file.
* The rule is additive: a module that later gains such a consumer splits at that point - nothing prevents it, and nothing but that consumer justifies it.

# Testing & quality

* Tests should be runnable as part of the same local "F5 experience" - no separate environment or manual setup required to run the test suite.
* Prefer fast, local unit/integration tests over end-to-end tests that depend on hosted infrastructure, given the local-first runtime model.
* To test the modular diagram implementation, use the diagram types that are actually implemented, not a fixed list here: `docs/tools.md` is the catalog of record and marks each type's state, and `src/examples/` holds a document of each in one explorer tree. This bullet named Mindmap alone until 2026-09-04, which was true when Mindmap was the only module and quietly false for every module added after it - an enumeration in a document that is not the catalog goes stale the moment the catalog moves.
* Test classes in C# should follow the filename `<Classname>.Tests` and the class name `<Classname>Tests`. Mind the dot.
* Tests should follow the tripple a pattern: arrange, act, assert.

# Checking that the conventions are actually followed

Moved to [processes.md, *Checking that the conventions are actually followed*](processes.md#checking-that-the-conventions-are-actually-followed): `dotnet format style` is the fast check and JetBrains InspectCode the thorough, authoritative one — passing either says nothing about the other.

# JetBrains Rider warnings

**In a C# project every JetBrains Rider warning is corrected.** Ruled by the user on 2026-10-07, and held for the whole organization as principle VI of the [etalii.adp constitution](https://github.com/etalii-adp/etalii.adp/blob/develop/.specify/memory/constitution.md); this is the standalone repository's copy of the rule and how it is applied here.

* **What counts**: every finding Rider's code inspection reports at severity Warning or higher, in any C# project of the solution - `src/backend/` and every module's `backend/` and tests alike. Rider shows them in the editor and in *Problems*; JetBrains InspectCode runs the same engine without the IDE, `bash .github/tools/inspect/inspect.sh` is how it is run here, and its report is the one that settles a disagreement.
* **Correct the code.** A warning is fixed where it occurs. Code being written now adds none, and a piece of work is not finished while a file it touched still reports one.
* **Where an inspection does not fit, change the rule and say why** - in the `.DotSettings` beside the project or solution, or in `src/.editorconfig`, with a note, exactly as *Backend code style* in CLAUDE.md does for an `.editorconfig` rule. A `// ReSharper disable` comment or a `[SuppressMessage]` placed only to make a warning disappear is not a correction; one that stays carries its reason on the same line.
* **A warning left reported is the one outcome that is not available.** A list that always shows something is a list nobody reads, and the next real defect arrives in it.
* **A clean report only counts from a run that could see the code.** The script builds first, names the SDK and toolset to the inspector explicitly, and judges the run's health before its findings: it exits 2, neither clean nor dirty, when the report is missing, the results carry compiler errors, or fewer files were inspected than git tracks. [processes.md, *An instrument that reports absence must first prove it is present*](processes.md#an-instrument-that-reports-absence-must-first-prove-it-is-present) names the three ways this tool reports nothing while seeing nothing.
* **What `src/backend/EtAlii.Adp.sln.DotSettings` excludes from inspection, and why**, here because a `.DotSettings` file keeps no comment Rider will not rewrite away:
  * `Broken.csproj` (file mask) - `Fixtures/cpm/Broken.csproj` is malformed on purpose: it is the input of the test in `EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests` for a project file that cannot be parsed, so its four XML errors are the fixture working. The mask names the full file name, never `*.csproj` or the `Fixtures` folder, so every real project file and every `.cs` file under a `Fixtures` folder stays inspected. `.github/tools/inspect/inspect.sh` also passes the inspector this one file's path, because on the Linux runner the mask did not take while a severity entry in the same settings file did (measured 2026-10-07); the script's comment records it.
* **What `src/backend/EtAlii.Adp.sln.DotSettings` switches off, and why**, for the same reason:
  * `ConvertToPrimaryConstructor` (`DO_NOT_SHOW`, was Suggestion) - a primary constructor's parameter is a mutable capture, not a readonly field, so converting a constructor that assigns readonly fields either loses `readonly` or leaves a field that `ReplaceWithPrimaryConstructorParameter` then reports. Ordinary constructors with readonly fields stay. Ruled by Peter on 2026-10-07 (`rider-warnings-cleanup`, task 5); it is the one inspection that specification switched off.
* **What it ranks up, and why**: the command-line inspector reads only the settings in the repository, while Rider also layers settings from outside it, and on Peter's machine that outside layer showed two inspections at Warning which the inspector, taking their defaults, did not report. Both are ranked `WARNING` in the same file so the command-line inspection and Rider agree:
  * `SuggestVarOrType_DeconstructionDeclarations` ("Use explicit type for each variable" in a deconstruction such as `var (a, b) = ...`) - each element is written with its type, as Rider's quick-fix does.
  * `ParameterOnlyUsedForPreconditionCheck.Local` and `.Global` ("Parameter is only used for precondition check(s)"). The inspector already ranks `.Local` Warning by default, and the entry was measured binding (set to `ERROR` on a scratch solution, it changed the reported severity), yet it does not report the one site Rider showed - a lambda parameter in a test used only by `ArgumentNullException.ThrowIfNull` and xUnit `Assert` calls - so its analysis of that shape differs from Rider's and the entry records the ruling rather than making the two agree. That site was rewritten so neither has anything to report.
  * Ruled by Peter on 2026-10-08. A key is written escaped - `_` as `_005F`, `.` as `_002E` - and a wrong one parses, matches nothing and changes nothing, so the run that first reported the deconstruction findings is what showed that key took.
* **How it is run and where it is enforced**: `bash .github/tools/inspect/inspect.sh`, after `dotnet tool restore` once for the version `.config/dotnet-tools.json` pins, exits 0 on a clean run and 1 on any finding at Suggestion severity or above, listing each by file and line. The solution has none, and the `inspection` job of the Build workflow runs the script on every pull request into `develop` and every push to it and fails on any finding. It is a check on the runner, not a local gate: the four gates in CLAUDE.md stay four. [processes.md, *Checking that the conventions are actually followed*](processes.md#checking-that-the-conventions-are-actually-followed) says the rest.

# Frontend

* All styling for the application should be done in a centralized manner, avoiding inline styles and promoting consistency and maintainability as much as possible.

# Backend

* For a specific subsystem or component, place simple POCO objects and objects that represent data rather than functionalities in a dedicated subfolder called '\_Model'.
* Follow the one entity per file principle. One exception to this is a CommandHandler and the Command it handles.
* **Never nest a class, record, enum or interface inside another type.** A nested type is a type that could not be found, cannot be tested on its own, and takes its meaning from where it happens to sit rather than from its own name. Every type stands on its own, in its own file, at namespace level.
  * **Lift it out and give it a name that stands alone.** A nested `Entry` for example becomes `ContextSelectionEntry`; a nested `Options` becomes `MindmapLayoutOptions`. If a name only makes sense while reading the parent, it is not specific enough yet - the reader who meets it in a stack trace or a DI registration has no parent in view.
  * **Where it goes**: a POCO or a record that carries data lands in the area's `_Model` folder, per the rule above. Anything with behaviour sits beside its parent class, in the same folder and namespace.
  * The one-entity-per-file exception above (a Command and its handler) is about two *sibling* types in one file, not about nesting - neither is inside the other.

# Context

The context service owns what is selected and what can be done with it. Every feature that has a notion of "the thing the user is working on" goes through it rather than growing a selection of its own.

* **One selection, held in one place.** `ContextService` is the single owner of a connection's selection. No surface keeps its own copy, and nothing calls the backend to ask what is selected - the answer is pushed over the context stream, with the resolved chain and the innermost level's actions in the same message.
* **Make something selectable by registering an `IContextSourceResolver`**, never by adding a bespoke RPC. A diagram element, a search hit, an entry in an error list: each is one resolver, registered once, and the service is not touched.
* **Offer what a user can do through an `IContextActionProvider`** for the relevant `ContextScope`. Actions discovered this way reach the ribbon, the right-click menu and the keyboard shortcut through the same path, so a feature never wires up three of them.
* **Consumers are pure subscribers.** The ribbon, menus and panels read the pushed selection and its actions and call nothing but `ExecuteAction`. A consumer that needs more information asks for it to be carried on the selection rather than resolving it again itself.
* **Extend the chain, don't add a parallel message.** A selection carries a source, a path, an id, and a nested `none | action | child` choice. A feature that needs to say more should nest a level or add detail to one, so every existing consumer keeps working unchanged.
* Registering a resolver or a provider is one line in `Program.cs`. If a new feature needs a change inside the context service itself, that is a sign the abstraction is wrong - fix the abstraction rather than special-casing the feature.

# Commands

Anything that changes state is a command. This is what makes undo/redo a property of the system rather than something each feature has to remember to support.

* **Every functional action is an `ICommand` with a matching `ICommandHandler<TCommand>`**, dispatched through `IHistoryStack`. A service, a gRPC method or a context action provider never performs the change inline - it builds the command and dispatches it.
* **A handler reports the command that reverses it** as `CommandResult.Inverse`, which is what puts the change on the undo stack. A change that is deliberately not undoable returns plain `CommandResult.Success()`, and the reason it is not undoable belongs in the handler's own documentation.
* **Handlers validate their own preconditions.** Undo and redo dispatch a handler again long after the original call, so it can trust nothing that was checked earlier - not the caller, and not the state of the disk.
* **A rejected command is a `CommandResult`, not an exception.** A name already taken or a file that has since vanished is an expected outcome carrying a message meant for the user. Exceptions stay reserved for programming errors, such as dispatching a command with no registered handler.
* A context action's `CommitAsync` is the point where the two rules above meet: it resolves what the user chose and dispatches a command, and does no filesystem work itself. The explorer's rename, delete and add are the worked examples.
* Handlers live in a `Commands` subfolder of the area they belong to, keeping the namespace of that area, and are registered in `AddCommands` rather than one by one in the host - so a new command has one place to be added and a test can wire up the same pipeline the host does.

# Decision log

1. **File-based storage over a database**: keeps diagrams reviewable and mergeable through normal repository tooling; revisit only if a hosted/multi-user scenario proves this insufficient.
2. **Bi-directional gRPC for frontend-backend communication**: chosen for strongly-typed contracts and native streaming support, which fits the "push changes to open clients" requirement better than plain REST/JSON.
3. **.NET/ASP.NET Core backend**: aligns with the team's Rider-based, F5-first development workflow and existing tooling familiarity.
4. **React + TypeScript client over Blazor WebAssembly**: the canvas/diagramming ecosystem available to React/TypeScript is significantly more mature for a virtualized, WebGL-rendered diagram surface than Blazor's, outweighing the appeal of an all-C# stack; revisit only if a specific chunk of C# logic clearly needs to run client-side.
5. **ASP.NET Core hosts the React build directly**: kept to a single process/runtime in production (static files + gRPC-Web endpoint together) rather than combining Blazor and React, avoiding a JS-interop serialization boundary on the path that streams diagram deltas to the canvas.
6. **One context service rather than per-surface selection**: selection and the actions that apply to it are cross-cutting - the explorer, the ribbon, the canvas and search all need the same answer - so they are resolved once and pushed, instead of each surface keeping its own state and asking its own questions. The cost is one indirection when adding a feature; the return is that a new selectable thing or a new action reaches every surface without any of them changing.
7. **Commands for every state change**: undo/redo is a property users expect of a design tool everywhere, not per feature. Routing changes through `ICommand`/`ICommandHandler` and the history stack makes reversibility the default and forces each change to name its own inverse, rather than leaving each feature to reimplement it or quietly skip it.
8. **Unary `Action` plus streaming `Watch` instead of bidirectional streaming**: grpc-web offers a browser neither bidirectional nor client streaming, so decision 2's "bi-directional gRPC" is realised as two correlated one-way legs rather than one two-way call. Splitting them is not only a workaround: it makes each client-to-backend call individually authorizable and retryable, and it forces per-connection state to have an explicit owner and an explicit lifetime - the connection id it is keyed by, and the stream whose end discards it.
9. **Bottom-up implementation order**: data model, persistence, wire protocol, client UI, then the remaining logic - each finished and tested before the next begins. Building in dependency order means every layer is written against one that already exists and already passes its tests, rather than against an assumption about it. The cost is that a feature shows nothing on screen until fairly late, which is accepted deliberately: discovering at the UI that the model is wrong is cheaper than discovering at the model that the UI already depends on it being wrong.
10. **No orphaned types exist (measured at technical-debt-cleanup R8, against `5844814d`)**: a type-level scan of all 710 declared backend/module types found 155 mentioned in only one file; 137 of those are xUnit test classes (reflection-discovered, expected single-file), 14 are `ServiceCollection*Extension` static classes referenced by method name rather than type name (false positives of a name-based scan), and the final 4 (`C4CommandHandler`, `C4ViewBlock`, `MindmapSize`, `WardleyEdit`) were hand-checked and are used within their declaring file. Zero orphans. The method, so it can be re-run: grep every declared type name, count the files it appears in, then hand-check whatever the two false-positive rules above do not explain. The scan was **type-level only** - unused public members, unreferenced generated protobuf messages and unused client exports were not scanned and would each be their own effort. Two previously-circulating claims were checked and withdrawn as stale: committed `.bld` build artifacts (0 tracked files) and an unused `WardleyTestDiagramDefinitionCatalog` (referenced and wired up since `d125bfb6`). Do not go hunting for dead code here without re-measuring first: a cleanup that must produce deletions in order to feel complete will produce bad deletions.
11. **Concurrent saves to one document take turns in `AdpFileWriter.Save`; they are not retried, and no sharing mode changes to avoid a failure** (landed at `eb019687`; approved on the dashboard 2026-09-15): two in-process publishes to one destination race inside `File.Replace`, which fails with `0x80070497` ("Unable to remove the file to be replaced") and can briefly leave the destination missing or under a backup name - measured at 1009 of 3000 saves for two writers on one path, while no reader in any sharing mode produced it. The flake that looked like a sharing violation was this race. So `Save` holds a per-destination turn across the whole publish, keyed by the normalised full path and ignoring case on Windows, and **a save that had to wait logs a warning naming the path** - because a silent lock would hide the second writer, which is still unidentified and is a finding of its own. A sharing violation (`0x80070020`) from a reader sharing only `Read` still fails at once: that reader has asked for the file not to change under it, and a retry would turn a regression in ADP's own file access into a slow save. The turn is in-process only and does not cover `CreateAll`; both limits are stated beside the code. An earlier ruling proposed a bounded retry on the theory that the holder was an external scanner; it was withdrawn when measurement showed that theory - already recorded as wrong once in `AdpFileWriter.SharingContract.Tests` - did not fit. Revisit only if saves are found racing across processes.
