# Tasks Document

**One developer owns this specification end to end**, in a worktree. The tasks are ordered so each lands on a green tree, and the reading stages (solution, project, package cache) are separable from the graph so the parsing can be tested without a canvas.

**Every task is `[normal]`, and that is a finding rather than an oversight.** The previous backend specification needed `[HOLD]` landings because it renamed types consumed across the module tree. **This one adds a module and renames nothing**: no shared type moves, no proto is re-namespaced, no existing consumer changes. The blast radius of every task below is the module's own folder, **plus the two registries a new module must enumerate itself in** - the backend solution (`EtAlii.Adp.slnx`, already listed in task 1) and the client's `generate` script in `src/client/package.json`, a chain of `buf generate` calls with one entry per module that has a proto - **plus, in two tasks, a documentation file.**

**The tripwire, stated correctly: if a task needs to teach anything outside the module folder about .NET, MSBuild, NuGet or solution files, the design was wrong - and that is not a reason to arrange a hold, it is a reason to stop and route it.** It guards against the module's *concepts* leaking outward. **Enumerating the module in a registry is not a concept leak**, and an earlier draft of this document said "any change outside the module folder", which was wrong in a way its own task 1 contradicted. **A tripwire that fires falsely is worse than none**: the developer who met this one argued instead of stopping, which is a property of that developer rather than of the rule.

**Every landing:** the four gates green on the merged tree (exit codes captured **before any pipe**, `Zero tests ran` read as a broken build), a **fresh-tree build**, and `jb inspectcode` clean for the affected projects. Merge with the fast-forward **withheld** and land it in the foreground after a fresh read of `develop` - a chained fast-forward moved `develop` under another session's open window once already.

**Worktree.** A short name - a long one pushes the deepest project paths past `MAX_PATH`, and the symptom is `dotnet test` reporting `Zero tests ran` rather than failing. `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_USE_MSBUILD_SERVER=0` before gating. Identity at creation: `git config --worktree user.name "developer-N-dotnet-dependency-graph"`, read back with plain `git config user.name` before the first commit.

## Coverage

**The diff was run, not promised: every requirement reference in the tasks below extracted, every acceptance criterion in the requirements listed, and the two sets compared.** All **36** acceptance criteria across Requirements 1-8 are claimed by exactly one task. **Nothing is unclaimed.**

**Three non-functional requirements are cross-cutting and deliberately claimed by no single task** - the *no task can claim it* kind rather than the *nobody claimed it* kind, named here so the diff's silence is not read as a gap:

* **Correctness - never invent an edge, never omit one silently.** Binds tasks 2, 3 and 4 together; each carries it in its own success criteria.
* **Correctness - never write to a `.csproj`, `.sln` or `.slnx`.** Binds every task. The only file this module writes is its own `.adp`, and only that file's `layout:` block.
* **Consistency with the authored `generic/dependencies` canvas.** Binds task 10, and is a review criterion rather than an assertable test.

**Scale is the exception and is claimed**, by task 13, because it needs a subject that exercises it rather than a promise that it holds.

## Tasks

- [x] 1. Create the module skeleton and register the type **[normal]**
  - Files: `src/diagrams/dotnet-dependency-graph/` with `api/`, `backend/EtAlii.Adp.Diagram.DotNetDependencyGraph/`, `client/`, `examples/`; `Diagram.cs` declaring the origin; `ServiceCollection.AddDotNetDependencyGraph.cs`; solution entry.
  - **Land it empty and green before anything reads a file.** The module registers, resolves, and draws nothing - so every later task is a change to a working module rather than a step in a module that has never built.
  - **Core gains nothing.** If a task in this list needs a change under `src/backend/`, stop and route it: the design says core learns no .NET, MSBuild, NuGet or solution vocabulary.
  - _Requirements: 1.1, 1.2_

- [x] 2. `SolutionReader` - read `.sln` and `.slnx` **[normal]**
  - Files: `SolutionReader.cs`, tests.
  - Both formats: the classic `Project(...)` lines and the newer XML. Project paths resolved relative to the solution file.
  - **An unreadable solution and a missing project file are reported, never swallowed and never fatal** - the diagram opens with what resolved.
  - _Requirements: 2.1, 2.2, 2.4_

- [x] 3. `ProjectReader` - references, frameworks, central package management **[normal]**
  - Files: `ProjectReader.cs`, tests.
  - `ProjectReference`, `PackageReference`, `TargetFramework`/`TargetFrameworks`. **A `PackageReference` with no `Version` resolves from `Directory.Packages.props`, walking upward as MSBuild does - this repository uses central package management, so it is the normal case here, not an edge case.**
  - **What the reader does not resolve is written in the module readme**, not left for a reader to discover from a missing edge.
  - _Requirements: 3.2, 3.3, 3.6_

- [x] 4. `DependencyGraph` and the element id scheme **[normal]**
  - Files: `DependencyGraph.cs`, `_Model/` node and edge records, tests.
  - Ids: `project:<path relative to the solution>`, `package:<package id>` - **the package id carries no version, deliberately.**
  - One element per package id; a package referenced at several versions is **one element marked as a version conflict, with the versions carried as properties**. Collapsing loudly, not silently.
  - **The guarding test: a package whose version changes keeps its element id.** It **must be seen to fail** against an id that includes the version, and only then be trusted. A test that has never been watched failing against the defect it guards is not a guard - and this one guards the design's load-bearing decision, which review left unchallenged rather than endorsed on its merits.
  - _Requirements: 3.1, 3.4, 3.5, 6.7_

- [x] 5. `PackageDescriptionReader` - the local NuGet cache, and nothing else **[normal]**
  - Files: `PackageDescriptionReader.cs`, tests.
  - Reads the `.nuspec` from the global packages folder and takes its description. **No feed. No network. No exception.** This is the user's decision, taken over a more capable recommendation, to keep the workspace-files-only principle absolute.
  - **A package that is not cached yields absence, not an error** - and absence must be distinguishable from an empty description.
  - _Requirements: 5.2, 5.3, 5.4_

- [x] 6. `DotNetContextPropertyProvider` - every row read-only **[normal]**
  - Files: `DotNetContextPropertyProvider.cs`, tests.
  - Project rows: name, target framework, .NET version where discoverable. Package rows: id, version(s), description.
  - **Every row carries a non-empty `ReadOnlyReason`**, in the house shape - cause first, then remedy, naming the file the value lives in. `SetAsync` refuses even though the resolver already refuses server-side.
  - **A property with no value shows its absence explicitly**, never an empty field indistinguishable from an empty value.
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 5.1_

- [x] 7. Session and factory - bind through the `body:` header **[normal]**
  - Files: `DotNetDependencyGraphSession.cs`, `DotNetDependencyGraphSessionFactory.cs`, `DotNetContextSourceResolver.cs`, tests.
  - The module declares `.sln`/`.slnx` as document extensions and binds through `body:` - **the `c4` pattern, not the ansible one**, because a bare registration cannot say which solution when a folder holds two.
  - _Requirements: 2.3_

- [x] 8. Layout - adopt `RegistrationLayout`, add nothing **[normal]**
  - Files: `DotNetDependencyGraphLayout.cs` (computation only), tests.
  - Computed layering for a directed graph; `RegistrationLayout.Apply` overlays stored positions element by element; reposition dispatched as core's `SetRegistrationLayoutCommand`, undoable.
  - **If this task finds itself writing layout parsing, persistence or pruning, it has gone wrong** - core has all three and they are offered to every module.
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6_

- [x] 9. Refresh - automatic and explicit, for different reasons **[normal]**
  - Files: watcher over the solution, its project files and `Directory.Packages.props`; an explicit refresh action; tests.
  - Automatic because the product capability is live pushed updates. **Explicit as well because a description can become available after a `restore` that touches no watched file** - a consequence of the cache-only decision reaching past the property it was about.
  - A refresh **must not cost the user their arrangement**; a vanished element's stored id is dropped on the next write.
  - _Requirements: 7.1, 7.2, 7.3, 7.4_

- [x] 10. Client canvas **[normal]**
  - Files: `client/` canvas, stream hook, tests.
  - Follows `generic/dependencies`' conventions so two dependency graphs do not feel like two products. Projects and packages visually distinct; edges directed; **elements drag although the subject is read-only.**
  - _Requirements: 3.1 (visual), and the consistency non-functional requirement_

- [x] 11. Examples **[normal]**
  - Files: `examples/`, replicated per structure.md; a readme recording what the corpus does **not** demonstrate.
  - **Any vendored data: permissive licence only, share-alike refused, `LICENSE.md` verbatim beside the data, read from the dataset's own statement rather than the page around it.**
  - _Requirements: 8.1, 8.2, 8.3_

- [x] 12. Catalog row and documentation **[normal]**
  - Files: `docs/diagrams.md` row with state icon and the `dotnet/dependency-graph` origin tag; `docs/creating-a-diagram-module.md` where this module moves a touch point it names.
  - **Move the row as the state changes** rather than adding it once and leaving it stale.
  - _Requirements: 1.3, 1.4_

- [x] 13. Scale: run it against this repository's own solution **[normal]**
  - Files: whatever the answer requires - grouping, filtering, or a stated limit - plus the measurement recorded in the implementation log.
  - **`EtAlii.Adp.slnx` is the subject: roughly thirty backend projects plus sixty-one diagram modules.** This is the one task that cannot be satisfied by assertion, because the requirement is that the type stays *usable* at that size.
  - **Rendering an unreadable hairball and calling it correct is the failure mode this task exists to prevent.** If the answer is a limit, the diagram must say it is limited rather than silently showing part of the graph.
  - _Requirements: the performance and scale non-functional requirements_
