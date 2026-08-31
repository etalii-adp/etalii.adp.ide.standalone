# Tasks Document

> **No task below writes a file.** This module has no document factory, no command handler, no toolbox and no editable property. If a task tempts you to open a file for writing, to add an `ICommand`, or to give a property an empty `ReadOnlyReason`, the task is wrong, not the design. Task 24 proves it byte-for-byte; the other twenty-five have to earn that proof.
>
> **Task 1 first, or Requirements 3 to 5 are untested.** Everything this module claims — which folders it recognises, which edges it draws, which mistakes it reports — is only as good as the tree it reads. Build a real, best-practice-layout Ansible project as a fixture before writing the reader, not after. The C4 corpus found six defects on the day it landed, and it found them because it was real.
>
> **Three core seams land before the module can validate (Phase B).** The design's Deviation 1 explains why: the validator seam passes text with no path, a problem can only be attributed to the file the validator was handed, and revalidation never fires for a file that is not itself a diagram. All three changes are defaulted or additive, none names a diagram type, and **they touch other modules' code** — `MindmapValidator`, `C4Validator` and seven test stubs move to the new signature in task 3. Do that mechanically and in one commit, so a bisect through it is one step rather than nine.
>
> **Worktree.** Per CLAUDE.md, implement in one dedicated worktree for this spec (`.claude/worktrees/ansible-structure-diagram/`). For the manual pass use a free port pair and revert `vite.config.ts` and `appsettings.developer.json` before merging.
>
> **Regenerate the client stubs.** Task 14 adds `ansible-structure.proto` and task 5 adds a case to `context.proto`. `src/client/src/generated/*_pb.ts` is gitignored for new protos, so a stale copy fails silently. Run `npm run generate` in `src/client/` as part of both.
>
> **Recent steering rules apply.** No nested types (lift anything you are tempted to nest); every test carries `// Arrange.` / `// Act.` / `// Assert.`; the module registers through one `AddAnsibleStructure` extension method rather than lines in `Program.cs`; every class that logs holds `private static readonly ILogger _logger = Log.ForContext<TheClass>();`.
>
> **Determinism is a test, not an intention.** `Directory.EnumerateFiles` order is the filesystem's. Every enumeration sorts with `StringComparer.Ordinal` before use, and tasks 10 and 13 each carry a test that builds the same model twice from two orderings and asserts one answer.
>
> **Bugs.** Per CLAUDE.md, any bug found while implementing or verifying a task is covered by a unit or integration test, or recorded in `tests.md`.

## Phase A — the fixture tree

- [x] 1. Build the Ansible fixture project
  - File: `src/diagrams/ansible-structure/backend/EtAlii.Adp.Diagram.AnsibleStructure.Tests/Fixtures/infrastructure/**` (new), `Fixtures/readme.md` (new)
  - Commit the worked example from the requirements as a real tree: `ansible.cfg`, `site.yml` importing `webservers.yml` and `dbservers.yml`, `inventories/production` and `inventories/staging` with `group_vars`, and `roles/common`, `roles/nginx` (with `handlers`, `templates`, an `include_tasks` to `tls.yml`, and a `meta` dependency) and `roles/postgres`
  - Beside it, a second fixture `broken/` carrying one instance of each Requirement 9.2 mistake, and a third `unconventional/` that is valid Ansible in a layout the model does not recognise (playbooks under `plays/`) — the fixture that must produce **no problems at all**
  - Also: a `Makefile`, a `docs/` folder and a shell script in `infrastructure/`, so Requirement 1.3's "ignored without complaint" has something to ignore; a deliberately unparsable YAML file in `broken/`; and a `roles:` entry written as `{{ role_name }}` so unresolvable-is-not-missing has a fixture
  - The readme records what each file exists to prove. Validate the YAML against Ansible's own expectations where you can, so the fixture is not ADP's opinion of Ansible
  - Purpose: every claim in Requirements 3, 4, 5 and 9 rests on these trees
  - _Leverage: src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4.Tests/Fixtures/readme.md (the provenance convention this follows)_
  - _Requirements: 1.3, 3.1, 4.1, 5.1, 9.2, 9.4_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer familiar with Ansible project layout | Task: Build three fixture trees - a best-practice project, a broken one carrying each validation mistake, and a valid-but-unrecognised layout - and document what each file proves | Restrictions: write real Ansible, not an approximation; do not add an .adp to any fixture (the tests register them); keep the unconventional fixture genuinely valid so the silence rule has a real subject | Success: the three trees exist, the readme states what each file guards, and the unconventional tree is one a real team could deploy from_

## Phase B — the three core seams

- [x] 2. Declare a diagram's subject on `DiagramDefinition`
  - File: `src/backend/EtAlii.Adp.Diagram/_Model/DiagramSubject.cs` (new), `_Model/DiagramDefinition.cs` (edited), `src/backend/EtAlii.Adp.Diagram/DiagramDefinitionDiscovery.cs` (edited), `src/backend/EtAlii.Adp.Diagram.Tests/DiagramDefinition.Tests.cs` (edited)
  - Add `DiagramSubject { Document, Folder }` and `DiagramSubject Subject = DiagramSubject.Document` after `Extension`. Defaulted, so every existing definition compiles and behaves unchanged. Document `Folder` as "the diagram is the folder the `.adp` sits in, and the files beneath it"
  - In discovery, refuse `Folder` with a non-empty `Extension` — a contradiction — beside the duplicate-origin check already there
  - Purpose: lets core do the right thing for a folder-subject type without learning what Ansible is
  - _Leverage: the duplicate-origin refusal in DiagramDefinitionDiscovery_
  - _Requirements: 2.1, 11.2_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Add a defaulted DiagramSubject to DiagramDefinition and refuse the Folder-plus-extension contradiction at discovery | Restrictions: it must default to Document so no existing definition changes behaviour; do not reorder existing parameters; the enum is its own file per the no-nested-types rule | Success: the solution builds, every existing definition is untouched, and tests assert the default and the refusal_

- [x] 3. Widen the validator seam to carry a location
  - File: `src/backend/EtAlii.Adp.Diagram/_Model/DiagramValidationRequest.cs` (new), `IDiagramValidator.cs` (edited), `src/backend/EtAlii.Adp.Backend/Problems/ProjectValidator.cs` (edited), and every implementer: `MindmapValidator`, `C4Validator`, and the seven test stubs (`ProblemMaintenanceCountingValidator`, `ProblemStoreReportingValidator`, `ProblemStoreStubValidator`, `ProjectValidatorTestValidator`, `StartupRevalidationGatedValidator`, `DiagramValidatorsStubValidator`, `DiagramValidatorsOtherStubValidator`)
  - `ValidateAsync(DiagramValidationRequest request, CancellationToken)` where the request carries `Document`, `BaseName`, `RootPath`, `BodyPath`, `RegistrationPath` and `SubjectFolder` (null for a `Document`-subject type). `ProjectValidator` fills it, setting `SubjectFolder` from the routed definition's `Subject`
  - A parameter object rather than more loose strings, so the next fact a validator needs is an added property rather than another signature change
  - Do the migration mechanically and in **one commit**: nine call sites that change shape and not behaviour
  - Purpose: a validator whose rules live on disk can find the disk
  - _Leverage: MindmapValidator's own remarks, which already name this seam as the reason its broken-link rule cannot be written_
  - _Requirements: 9.1, 9.3_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer performing a seam migration across modules | Task: Replace IDiagramValidator's two loose string parameters with a DiagramValidationRequest record carrying the diagram's location, and migrate both real implementers and all seven test stubs | Restrictions: no behaviour change for any existing validator; do not add a compatibility overload or a default interface method - one door only; keep the migration to one commit | Success: the solution builds, every existing validator test passes unchanged in meaning, and ProjectValidator fills SubjectFolder for a Folder-subject definition_

- [x] 4. Let a problem name a file other than the diagram's own
  - File: `src/backend/EtAlii.Adp.Diagram/_Model/DiagramProblemFileLocation.cs` (new), `_Model/DiagramProblemLocation.cs` (edited: the remarks naming the closed set), `src/backend/EtAlii.Adp.Backend/Problems/CachedProblem.cs` (edited), `ProblemBroadcaster.cs` (edited), `src/api/context.proto` (edited), `src/client/src/shell/panels/ErrorsWarningsPanel.tsx` (edited)
  - `DiagramProblemFileLocation(string RelativePath, uint Line = 0)` as a third case. Add a new field number to `ProblemLocation`'s oneof — backward compatible. `CachedProblem` round-trips it; the panel reveals the named file rather than the diagram
  - Run `npm run generate` in `src/client/`
  - Purpose: `ansible.role-missing` points at `roles/nginx/meta/main.yml`, not at `infrastructure.adp`
  - _Leverage: the two existing location records and their handling in CachedProblem and ProblemBroadcaster_
  - _Requirements: 9.3_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# and TypeScript developer working across the problem wire | Task: Add a third DiagramProblemLocation case naming a project-relative file and an optional line, carry it through the cache, the broadcaster, the proto and the panel | Restrictions: use a new proto field number, never reuse one; the path crossing the wire must be project-relative, never absolute; do not change how the two existing cases behave | Success: a problem carrying a file location round-trips through the cache, reaches the panel, and reveals the named file_

- [x] 5. Attribute and pin a file-located problem to that file
  - File: `src/backend/EtAlii.Adp.Backend/Problems/ProjectValidator.cs` (edited), `ProblemCollector.cs` (edited), `src/backend/EtAlii.Adp.Backend.Tests/Unit Tests/Problems/ProjectValidator.Tests.cs` (edited)
  - When a returned `DiagramProblem` carries a `DiagramProblemFileLocation`, use that file for **both** the attribution path and the `statsPath` staleness pin. Pinning a folder diagram's problems to an `.adp` that never changes would leave every verdict looking fresh forever
  - Purpose: the panel reveals the right file, and the verdict goes stale when that file is edited
  - _Leverage: ProblemCollector.Add already takes attribution and stats paths separately - this is why_
  - _Requirements: 9.3_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Make ProjectValidator attribute and staleness-pin a file-located problem to the file it names | Restrictions: a problem with no location or an element/line location must behave exactly as today; the named file must be containment-checked against the root before it is used | Success: a test proves a file-located problem is stored against that file and marked stale when that file changes, and every existing attribution test still passes_

- [x] 6. Revalidate a folder diagram when a file beneath it changes
  - File: `src/backend/EtAlii.Adp.Backend/Problems/ProblemMaintenance.cs` (edited), `src/backend/EtAlii.Adp.Backend.Tests/Unit Tests/Problems/ProblemMaintenance.Tests.cs` (edited)
  - In `RevalidateAsync`, when a changed path routes to `NotADiagram`, walk up to the nearest ancestor folder holding an `.adp` whose definition is `DiagramSubject.Folder`, and validate that diagram's scope. Stop at the project root; read only `.adp` files
  - Without this, editing a role file **clears** the folder diagram's problems without re-finding them — the panel loses a problem rather than refreshing it, which is worse than not watching at all
  - Purpose: Requirement 3.5's "the diagram a reader leaves open stays true" holds for the problems panel too
  - _Leverage: the existing NotADiagram branch and its HasProblemsFor guard_
  - _Requirements: 3.5, 9.1_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer working in the problem maintenance loop | Task: Walk up from a changed non-diagram file to the nearest folder-subject registration and revalidate it | Restrictions: a project with no folder-subject diagram must pay at most one directory probe per changed file; never walk above the project root; do not follow reparse points out of the root | Success: a test proves that editing a file inside a registered folder refreshes that diagram's problems rather than clearing them, and that a project without such a diagram is unaffected_

## Phase C — reading the tree

- [x] 7. Scaffold the module
  - File: `src/diagrams/ansible-structure/**` (new), `src/backend/EtAlii.Adp.slnx` (edited)
  - `backend/EtAlii.Adp.Diagram.AnsibleStructure` and `.Tests` (the test project an executable, for xUnit v3), `api/`, `client/`, following the mindmap module's layout. `Diagram.cs` declares one `Definitions` entry: origin `ansible/structure`, title, description, **no `Extension`**, `Subject = DiagramSubject.Folder`
  - Purpose: the module exists and is discovered
  - _Leverage: src/diagrams/mindmap/backend/EtAlii.Adp.Diagram.Mindmap/Diagram.cs_
  - _Requirements: 2.1, 11.1_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Scaffold the module projects and declare its Diagram.Definitions entry as a folder-subject type with no extension | Restrictions: follow the per-diagram module layout structure.md defines; the test project must be an executable; do not declare an extension - the .adp is the whole registration | Success: the solution builds, the type appears in DiagramDefinition.All, and the definition-description test still passes_

- [x] 8. Add YamlDotNet centrally
  - File: `src/Directory.Packages.props` (edited), `src/diagrams/ansible-structure/backend/EtAlii.Adp.Diagram.AnsibleStructure/EtAlii.Adp.Diagram.AnsibleStructure.csproj` (edited)
  - Pin the current release as a `PackageVersion`, and reference it from the module only. This is the tree's first YAML dependency; a read-only module has no reason to hand-roll a parser, and YamlDotNet's node marks are what let a problem point at a line
  - Purpose: `AnsibleYaml` has something to parse with
  - _Requirements: 1.2, 9.2_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer maintaining central package management | Task: Add YamlDotNet to Directory.Packages.props at its current release and reference it from the Ansible module only | Restrictions: version goes in Directory.Packages.props, never in the csproj; do not add the reference to any other project | Success: the module builds against YamlDotNet and no other project gains a YAML dependency_

- [x] 9. `AnsibleYaml`: one tolerant read
  - File: `.../AnsibleYaml.cs` (new), `_Model/AnsibleYamlFailure.cs` (new), `.../AnsibleYaml.Tests.cs` (new)
  - `Read(string path)` returns either a parsed root node **with line marks kept**, or an `AnsibleYamlFailure(Message, Line)` carrying the parser's own message. Never throws for a malformed document. Opened read-only and shared, so validation never contends with an editor
  - Purpose: Requirement 1.2's "degrades that file's detail, never the whole diagram", at its narrowest point
  - _Requirements: 1.2, 9.2_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement a tolerant single-file YAML read that keeps node line marks and returns a failure record rather than throwing | Restrictions: must not throw for any malformed input; must open the file read-only and shared; do not normalise or reformat anything | Success: tests prove a valid file parses with usable line marks, a malformed file returns a failure carrying the parser's message and line, and neither case throws_

- [x] 10. `AnsibleProjectReader`: the folder becomes a model
  - File: `.../AnsibleProjectReader.cs` (new), `_Model/AnsibleProject.cs`, `AnsiblePlaybook.cs`, `AnsiblePlay.cs`, `AnsibleRole.cs`, `AnsibleRoleContents.cs`, `AnsibleTaskFile.cs`, `AnsibleInventory.cs`, `AnsibleInventoryGroup.cs`, `AnsibleVariableFolder.cs` (new), `.../AnsibleProjectReader.Tests.cs` (new)
  - Recognise by Ansible's own conventions: **playbooks** (root-level and `playbooks/` YAML whose top level is a *list of mappings* — which is how a data file excludes itself without a name list), **roles** (`roles/<name>/` with any canonical subfolder, and a hollow one still a role), **inventories** (`inventories/<env>/`, or a root `inventory`/`hosts`/`hosts.yml`, with `group_vars`/`host_vars`), and **annotations** (`ansible.cfg`, `collections/requirements.yml`, `requirements.yml`) recorded on the project rather than drawn as boxes
  - Sort every enumeration with `StringComparer.Ordinal` before use. Canonicalise reparse points and refuse a folder already visited, so a symlink loop ends the walk rather than the process
  - Purpose: Requirements 3.1 and 4 — the model's vocabulary is Ansible's
  - _Leverage: ProblemCollector.MarkFolderVisited (the reparse-point canonicalisation this copies)_
  - _Requirements: 1.3, 3.1, 4.1, 4.2_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer familiar with Ansible layout conventions | Task: Walk a registered folder into an AnsibleProject, recognising playbooks, roles, inventories and annotations by convention and ignoring everything else | Restrictions: never open a file for writing; sort every directory enumeration ordinally before use; terminate on a symlink loop; unrecognised content is ignored silently, never reported | Success: the fixture tree reads into the expected model, a Makefile and a docs folder are ignored, a hollow role is present and visibly hollow, an unparsable file degrades only itself, and a test builds the same model twice from two enumeration orderings and asserts one answer_

- [x] 11. `AnsibleGraph`: five edge kinds, three resolution states
  - File: `.../AnsibleGraph.cs` (new), `_Model/AnsibleEdge.cs`, `AnsibleEdgeKind.cs`, `AnsibleTargetResolution.cs` (new), `.../AnsibleGraph.Tests.cs` (new)
  - Derive `UsesRole` (`roles:`, `import_role`, `include_role`), `ImportsPlaybook`, `IncludesTasks`, `DependsOn` (`meta/main.yml`) and `Targets` (a play's `hosts:` against a group an inventory defines). Each edge carries the declaring file and line, the target **as written**, the `when:` **as written and never evaluated**, and whether the mechanism is static or dynamic
  - Resolution is three states, not two: `Resolved`, `Missing` (a problem), `Unresolvable` (a `{{ expression }}` — **not** a problem, or every parameterised role becomes a false error)
  - No per-variable usage edges. Requirement 5.8 refuses them and so does this task
  - Purpose: Requirement 5 — the relationships are the diagram
  - _Requirements: 3.2, 3.3, 3.4, 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Derive the five edge kinds from the model, each carrying its declaring file and line, its target as written, its condition as written, and a three-state resolution | Restrictions: never evaluate a when: or a Jinja expression; never draw a per-variable usage edge; an expression target is Unresolvable and must not be reported as Missing | Success: one test per edge kind passes against the fixture, static and dynamic are distinguishable, a when: survives verbatim, and the {{ role_name }} fixture yields an unresolvable edge rather than a missing role_

## Phase D — following the tree

- [x] 12. `AnsibleProjectStore`: one project per folder, watched
  - File: `.../AnsibleProjectStore.cs`, `IAnsibleProjectStore.cs` (new), `_Model/AnsibleProjectChangedEventArgs.cs` (new), `.../AnsibleProjectStore.Tests.cs` (new)
  - `GetOrLoad`, `Get`, `Release`, and a `Changed` event. One `FileSystemWatcher` per registered folder with `IncludeSubdirectories` and content filters, a settle timer coalescing a burst, and **guarded handlers** — an unhandled exception on the watcher thread is the death of the process, which this repository has already paid for once
  - A settled burst re-reads the files that changed; a created, deleted or renamed **directory** re-walks the tree, since that is when the walk itself changes. `Release` disposes the watcher when the last viewer leaves
  - There is deliberately no `Save`
  - Purpose: Requirement 3.5 — the diagram a reader leaves open stays true
  - _Leverage: TrackedProblemRoot (the watcher shape, the settle timer and the guarded dispatch this copies)_
  - _Requirements: 3.5, 4.3_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer working with file system watchers | Task: Hold one AnsibleProject per registered folder, keep it true through a guarded, coalescing watcher, and announce what changed | Restrictions: no save method and no write path of any kind; every watcher handler is guarded so a throw cannot kill the process; dispose the watcher when the last viewer leaves | Success: tests prove a file edit re-reads only that file, a directory change re-walks, a burst coalesces, and a throwing handler is logged rather than fatal_

## Phase E — layout and the wire

- [x] 13. `AnsibleLayout`: ranked, banded, deterministic
  - File: `.../AnsibleLayout.cs` (new), `_Model/AnsibleMetrics.cs` (new), `.../AnsibleLayout.Tests.cs` (new)
  - Ranks left to right by longest path — entry playbooks (nothing imports them), then playbooks and plays, then roles, then task files. Within a rank, declaration order then name, both ordinal. Inventories and variable folders occupy their own band rather than being ranked, so a `Targets` edge drops out of the execution story instead of lengthening it
  - Each play gets an index in declaration order; a role carries the index of its play. A role used by two plays is drawn **once**, carrying the lower index — seeing that `common` is shared is the point of drawing it
  - Pure: `AnsibleGraph` in, `IReadOnlyDictionary<string, Point>` out. No I/O
  - Purpose: Requirement 6 — the same project always produces the same picture
  - _Requirements: 6.1, 6.2, 6.3_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer with graph layout experience | Task: Compute deterministic positions for the graph, ranked in execution order with inventories and variable folders banded apart, assigning each play an index for colour continuity | Restrictions: pure function, no file or clock access; a shared role is one node, not one per play; ties break ordinally so the answer never depends on enumeration order | Success: tests prove the same graph yields the same positions twice, ranks follow Requirement 6.2, the shared role appears once with the lower play index, and nothing overlaps on the fixture_

- [x] 14. `ansible-structure.proto`
  - File: `src/diagrams/ansible-structure/api/ansible-structure.proto` (new), `api/readme.md` (new)
  - `AnsibleElementPayload` with `name`, `kind`, `project_relative_path`, `play_index`, `hosts`, `contents`, `edge`, `unresolvable` and `annotations`; plus `AnsibleRoleContents` and `AnsibleEdge` as the design defines them
  - **`play_index` is an index, never a colour** — styling stays in the module's stylesheet per tech.md's Frontend rule
  - Run `npm run generate` in `src/client/`
  - Purpose: what a node is, on the wire, without extending any core message
  - _Leverage: src/diagrams/mindmap/api/mindmap.proto (including MindmapLink.project_relative_path, the reveal precedent this follows)_
  - _Requirements: 7.1, 8.1_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer defining a gRPC contract | Task: Define the module's element payload messages and regenerate the client stubs | Restrictions: no core proto is edited; paths are project-relative, never absolute; carry a play index rather than a colour; the proto is the API documentation, so comment every non-obvious field | Success: the proto compiles on both sides, the client stubs regenerate, and no core message changed_

- [x] 15. `AnsibleElementMapper`
  - File: `.../AnsibleElementMapper.cs` (new), `.../AnsibleElementMapper.Tests.cs` (new)
  - Element types `ansible/structure+playbook|play|role|taskfile|inventory|vars|edge`. Ids are folder-relative paths with a discriminator (`role:nginx`, `play:webservers.yml#0`, `edge:<sourceId>|<directive>|<targetAsWritten>`), so an id survives an unrelated edit elsewhere in the tree
  - Viewport: what intersects, plus one hop of graph partners so an edge leaving the screen keeps both ends — the mindmap's rule and its reason
  - Never emits a group delta (nothing folds) and never an edit delta (nothing edits); a watcher push is `Remove` + `Add` for what changed
  - Purpose: Requirement 7.1 — the shared element and delta contract, unextended
  - _Leverage: MindmapElementMapper.Visible (the viewport-plus-one-hop rule)_
  - _Requirements: 4.3, 7.1, 8.1_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Map the model and layout onto core elements and deltas, with stable ids and viewport culling that keeps both ends of an edge | Restrictions: never emit a group or an edit delta; ids must survive an unrelated edit; carry the project-relative path on every node so the client can reveal it | Success: tests prove id stability across an unrelated edit, that a culled edge keeps both endpoints, and that no group or edit delta is ever produced_

## Phase F — the session and the registration

- [x] 16. `AnsibleSession` and `AnsibleSessionFactory`
  - File: `.../AnsibleSession.cs`, `AnsibleSessionFactory.cs` (new), `.../AnsibleSession.Tests.cs` (new)
  - `Open` resolves the subject as **the folder the `.adp` sits in** — for a bodyless type `bodyPath` and `registrationPath` are the same file, which is what `DiagramFileRouter` already returns. Baseline and `UpdateView` from the mapper; the store's `Changed` becomes deltas
  - `MoveElementAsync` **refuses**: *"An Ansible structure diagram is drawn from the folder's own files; move a role by moving its folder."* The seam returns a reason rather than throwing, so a read-only type answers honestly
  - Takes **no** `IHistoryStackStore`. A module with no commands has no reason to hold the project's history, and the missing parameter is the clearest statement this design makes
  - Purpose: Requirements 2.4 and 7.1
  - _Leverage: MindmapSession and MindmapSessionFactory_
  - _Requirements: 2.2, 2.4, 7.1_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer implementing a diagram session | Task: Open a read-only session over the folder the .adp sits in, stream baseline and viewport deltas, and push the store's changes | Restrictions: do not take a history stack dependency; MoveElementAsync refuses with a sentence rather than throwing or silently succeeding; no write path anywhere | Success: tests prove the folder is resolved from the .adp, the baseline matches the mapper, a store change reaches the session as deltas, and a move is refused with a reason_

- [x] 17. `AddAnsibleStructure`, and the four registrations that are missing
  - File: `.../ServiceCollection.AddAnsibleStructure.cs` (new), `src/backend/EtAlii.Adp.Backend.Service/Program.cs` (edited), `.../AddAnsibleStructure.Tests.cs` (new)
  - Registers the store, the session factory, the source resolver, the validator and the property provider. Registers **no document factory, no toolbox provider, no action provider and no commands** — their absence is this type's statement, and the test asserts the absence rather than leaving it to inspection
  - Purpose: Requirement 11.2, including the half of it that is about what is *not* there
  - _Leverage: ServiceCollection.AddMindmap.cs_
  - _Requirements: 11.1, 11.2_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Register the whole module through one extension method and add one line to Program.cs | Restrictions: register no command handler, no document factory, no toolbox provider and no action provider; core must resolve everything by origin or scope | Success: the host starts with the module registered, and a test asserts that no IDiagramDocumentFactory, IDiagramToolboxProvider or IContextActionProvider is registered for this origin_

## Phase G — telling the user what is broken

- [x] 18. `AnsibleRuleSet` and `AnsibleValidator`
  - File: `.../AnsibleRuleSet.cs`, `AnsibleValidator.cs` (new), `.../AnsibleRuleSet.Tests.cs` (new)
  - A pure function from `AnsibleProject` to `DiagramProblem`s — it reads no file, because the reader already did. The five rules: `ansible.role-missing` (error), `ansible.dangling-import` (error), `ansible.unmatched-hosts` (warning), `ansible.unreadable-yaml` (error, carrying the parser's own message), `ansible.empty-role` (warning). Each location is a `DiagramProblemFileLocation` naming the **declaring** file and its line
  - `AnsibleValidator` resolves its folder from the request's `SubjectFolder` (task 3) and delegates
  - **Silence is a valid answer**: the `unconventional/` fixture must produce nothing at all
  - Purpose: Requirement 9 — read-only does not mean silent
  - _Leverage: C4RuleSet (the pure-function-per-rule shape)_
  - _Requirements: 9.1, 9.2, 9.3, 9.4_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer writing diagram validation rules | Task: Implement the five rules as a pure function over the model, each locating its problem at the declaring file and line, and wire it behind IDiagramValidator | Restrictions: read no file in the rule set; an unresolvable expression target is never reported as missing; an unrecognised-but-valid layout produces nothing | Success: one test per rule fires against the broken fixture, the unconventional fixture produces zero problems, and every problem names the file that declared the mistake_

## Phase H — selection and properties

- [x] 19. `AnsibleContextSourceResolver`
  - File: `.../AnsibleContextSourceResolver.cs` (new), `_Model/AnsibleNodeSubscription.cs` (new), `.../AnsibleContextSourceResolver.Tests.cs` (new)
  - Resolve an `element_id` against the project named by the enclosing hierarchy level, verify the node is really in that project, fill the detail a consumer shows, and carry the node's **project-relative path segments** for the reveal. `NestingOf` → `NotNestable`. `Track` re-resolves on every store change: a deleted role clears the selection, a renamed file changes the path
  - Purpose: Requirement 8.2 — a node answers like any other element
  - _Leverage: MindmapContextSourceResolver, including its Track subscription_
  - _Requirements: 8.1, 8.2, 8.3_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer working in the context seam | Task: Make an Ansible node selectable, verified against its project, carrying its project-relative path, and re-resolved when the folder changes | Restrictions: never resolve a location yourself - the target arrives resolved; reject an unverifiable selection rather than recording it; an edge selection must name its declaring side | Success: tests prove a node resolves, a stranger's id is rejected, a deleted role clears the selection, and a rename updates the path_

- [x] 20. `AnsibleContextPropertyProvider` — every row, none editable, every reason true
  - File: `.../AnsibleContextPropertyProvider.cs` (new), `.../AnsibleContextPropertyProvider.Tests.cs` (new)
  - The rows of Requirements 10.3–10.6, grouped: **Identity/Runs/Targets** for a playbook or play, **Identity/Contents/Relationships** for a role, **Identity/Groups/Variables** for an inventory, **Declaration** for an edge. A property absent from the files is **not contributed** — a play with no `when:` has no condition row
  - **Every row carries a non-empty `ReadOnlyReason` naming the file the value lives in** and how it is edited. `SetAsync` refuses unconditionally with the property's own reason — unreachable in practice, because `ContextPropertyResolver` refuses the write server-side first, and written anyway, because a provider that would silently accept a write if the resolver ever changed is a trap
  - Purpose: property-grid Requirement 4 exercised at 100%
  - _Leverage: MindmapContextPropertyProvider (the two read-only rows it already carries, and their reasons)_
  - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7, 10.8_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer working in the property-grid seam | Task: Describe every node and edge kind's properties, grouped, with every row read-only and every reason naming the file the value lives in, and refuse every write | Restrictions: no property may have an empty ReadOnlyReason; an absent property is omitted, not contributed empty; SetAsync refuses as a result, never an exception | Success: a test walks every node kind of the fixture and asserts every property carries a reason naming a file that exists, absent properties are absent, groups match the requirements, and SetAsync refuses whatever it is given_

## Phase I — the canvas

- [x] 21. `ansibleModel.ts` and `useAnsibleStream.ts`
  - File: `src/diagrams/ansible-structure/client/ansibleModel.ts`, `useAnsibleStream.ts`, and their `.test.ts` (new)
  - Decode the payload into the client's own model; subscribe to the diagram stream and apply deltas. No knowledge of Ansible in the shell
  - Purpose: the client half of Requirement 7
  - _Leverage: src/diagrams/mindmap/client/mindmapModel.ts and useMindmapStream.ts_
  - _Requirements: 7.1, 7.2_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: TypeScript developer | Task: Decode the element payload into a client model and apply the delta stream | Restrictions: 2-space indent, double quotes, semicolons per src/.editorconfig; no Ansible knowledge leaks into the shell; do not re-measure what the backend computed | Success: unit tests cover decoding each element kind and applying add and remove deltas_

- [x] 22. `AnsibleCanvas.tsx` and `ansible-structure.css`
  - File: `src/diagrams/ansible-structure/client/AnsibleCanvas.tsx`, `ansible-structure.css`, `AnsibleCanvas.test.tsx` (new)
  - Draw the node kinds distinguishably, the five edge kinds in their own styles (solid for static, dashed for dynamic, a third for `DependsOn`), edge labels for the mechanism and any `when:`, and an unresolvable target shown as such. **The play-index-to-colour mapping lives here**, in the stylesheet, per tech.md's centralised-styling rule
  - Activation (double-click, Enter) calls `revealPath(segments)` with the node's project-relative path; selection sets the diagram-element context
  - Purpose: Requirements 5.4, 6.3, 7.1, 8.1
  - _Leverage: MindmapCanvas.tsx; ContextConnectionProvider's revealPath_
  - _Requirements: 5.4, 6.3, 7.1, 8.1, 8.2_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React developer | Task: Render the diagram's node and edge kinds distinguishably, map play index to colour in the stylesheet, and reveal a node's file on activation | Restrictions: no inline styles - all styling in the module stylesheet; no colour crosses the wire; the canvas offers no edit affordance of any kind | Success: component tests prove each node and edge kind renders distinguishably, static and dynamic differ, activation calls revealPath with the node's path, and no editing control exists_

- [x] 23. `register.ts`
  - File: `src/diagrams/ansible-structure/client/register.ts`, `register.test.ts`, `package.json`, `readme.md` (new)
  - One registration matching `ansible/structure`. The shell's glob discovers it; nothing in the shell is edited
  - Purpose: Requirement 7.2 — the shell learns nothing about Ansible
  - _Leverage: src/diagrams/mindmap/client/register.ts_
  - _Requirements: 7.2_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: TypeScript developer | Task: Export the module's canvas registration so the shell's glob discovers it | Restrictions: do not edit any file in the shell; match only this module's MIME type | Success: a test proves canvasFor("ansible/structure") resolves to this canvas and the shell is untouched_

## Phase J — proof

- [x] 24. Prove the module writes nothing
  - File: `.../ZeroWrites.Tests.cs` (new)
  - Snapshot the fixture tree byte-for-byte (contents **and** mtimes), then: open the diagram, take the baseline, change the viewport twice, select every node and edge kind, describe every property, attempt a `SetAsync`, attempt a `MoveElementAsync`, run the validator, close the session — and assert the tree is identical, `.adp` included
  - Purpose: the Non-Functional Requirement this whole spec turns on, as a guard rather than a claim
  - _Leverage: ProjectValidator.Tests' ValidateAsync_LeavesTheProjectByteForByteUnchanged (the snapshot technique)_
  - _Requirements: 1.1_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: QA-minded C# developer | Task: Exercise every read path of the module against the fixture and assert the tree is byte-identical afterwards, mtimes included | Restrictions: exercise the real components, not mocks of them; include the refused write attempts, since a refusal that still touched the disk is the bug this guards | Success: the test fails if any component is later given a write path, and passes today_

- [x] 25. Integration flows
  - File: `.../AnsibleStructureFlow.Tests.cs`, `AnsibleValidationFlow.Tests.cs` (new)
  - **Flow**: Add on a folder writes one `.adp` and nothing else; open it; receive the baseline; select a role; describe its properties; touch `roles/nginx/meta/main.yml` and see the deltas arrive
  - **Validation**: the broken fixture produces each problem attributed to the declaring file; fixing a file on disk clears it **through the watcher**, not through a manual revalidate — which is what tasks 5 and 6 exist for
  - Purpose: the layers meet
  - _Leverage: the existing diagram flow integration tests_
  - _Requirements: 2.2, 2.3, 3.5, 8.1, 9.1, 9.3_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer writing integration tests against the real host | Task: Cover the Add-open-select-follow flow and the validate-fix-clear flow end to end | Restrictions: use the real host and the real module registrations; assert that Add wrote exactly one file; the clear must come from the watcher, not from an explicit revalidate call | Success: both flows pass, and the validation flow fails if task 6's walk-up is removed_

- [x] 26. Catalog, manual pass and implementation log
  - File: `docs/diagrams.md` (edited), `tests.md` (edited), `.spec-workflow/specs/ansible-structure-diagram/Implementation Logs/` (new)
  - Move the `ansible/structure` row from 📝 Specified to ✅ Implemented per CLAUDE.md's catalog rule
  - Run the app from the worktree on a free port pair and verify by hand: right-click a folder → **Add… → Ansible project structure** creates one file; the diagram opens; double-clicking the `nginx` role reveals `roles/nginx/tasks/main.yml` in the explorer; selecting an edge shows which file and directive declared it; deleting `roles/common/` in a text editor moves both the diagram and the problems panel; the toolbox says the type has no entries and the ribbon offers no edit; `git status` is clean apart from the deletion made by hand. Revert both port files before merging
  - Anything the harness cannot exercise goes into `tests.md` as a step-by-step check rather than being claimed
  - Purpose: the catalog stays true, and the claims that only a running app can test get tested
  - _Requirements: 1.1, 7.3, 8.1, 9.1, 11.3_
  - _Prompt: Implement the task for spec ansible-structure-diagram, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer performing manual verification | Task: Update the diagram catalog, run the app and verify the read-only behaviours by hand, and log the implementation | Restrictions: use a free port pair and revert both port files before merging; kill processes by PID, never by image name - an image-name kill has already taken the main checkout's backend once; record anything unverified in tests.md rather than claiming it | Success: the catalog row is ✅ Implemented, every check above is either verified live or recorded in tests.md as a manual check, the fixture folder is left byte-identical, and the implementation log records what was and was not exercised_
