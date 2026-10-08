# Tasks Document

Sixteen tasks against the approved design, delivered as its six pull requests. **The order is the design's and it is deliberate**: the instrument (tasks 1 to 4) before any correction, what cannot change behaviour (5 to 7) before what can (8 to 13), and enforcement (14 to 16) only once the tree is clean.

**One Developer owns this specification until every task is done**, per CLAUDE.md. The tasks inside one pull request may be taken in any order; the pull requests may not.

**Delivery.** Each pull request is its own worktree and branch, `features/rider-warnings-<n>` in `.claude/worktrees/rw<n>` - short, because a long worktree path is what turns `dotnet test` into a run with zero tests. Set `git config --worktree user.name "agent-<N>-rider-warnings"` when the worktree is created and read it back with plain `git config user.name` before the first commit. All four gates exit zero on the branch before it is pushed, each judged by an exit code captured before any later command (Requirement 6.4, which every task below carries without repeating it). The pull request goes into `develop` and is merged with a merge commit. Nothing is merged locally.

**Counts are re-measured, never copied.** Every number in the requirements and design was measured at `724f7837`. `develop` has moved since and the user corrects Rider findings by hand. Task 4 measures again, and each later task takes its list from the script's output on its own branch, not from this document.

**Switching an inspection off is not a task, and no task may do it by itself.** Where a Developer concludes an inspection does not fit, the proposal goes to the user through the Scrum master as a selection, with the inspection, its count, two example findings and the reason. Only on the user's answer is the severity changed, in `src/backend/EtAlii.Adp.sln.DotSettings` or `src/.editorconfig` with a note (Requirements 3.1 and 3.2), and the decision added to the list task 16 publishes. *Its findings are numerous* is not a reason (Requirement 3.6).

**Every bug found leaves a guard behind**, and a test written for one is seen to fail against it first. Tasks 11 to 13 are where this is most likely to apply.

## Pull request 1 - the instrument

- [x] 1. Pin the inspector and write the script that runs it
  - Files: `.config/dotnet-tools.json`, `.github/tools/inspect/inspect.sh`
  - Add a local tool manifest naming `jetbrains.resharper.globaltools` at 2026.2.2
  - Write `inspect.sh`: resolve the SDK version from `src/global.json` and the toolset path from `dotnet --list-sdks`, build unless `--no-build` is given, run `dotnet jb inspectcode` on `src/backend/EtAlii.Adp.slnx` with `--no-build`, `--dotnetcoresdk`, `--toolset-path` and `--severity=SUGGESTION`, keep the console log beside the report, then hand both to the evaluator of task 2 and exit with its code
  - Accept `--report` and pass it through
  - Purpose: one committed way to run the inspection, the same on every machine
  - _Leverage: `.github/tools/gate/gate.sh` for the house style of a tool script; `.gitattributes` already keeps `.sh` LF_
  - _Requirements: 4.1_
  - _Prompt: Implement the task for spec rider-warnings-cleanup, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Build engineer fluent in bash and the .NET CLI | Task: Create the local tool manifest and `.github/tools/inspect/inspect.sh` exactly as the design's section The instrument describes, so that one command runs JetBrains InspectCode over the whole solution with the SDK and toolset named explicitly | Restrictions: Do not hard-code an SDK version or a Windows path; do not add a second solution file; capture the inspector's exit code before any later command; the script must run under Git Bash on Windows and bash on ubuntu-latest | _Leverage: `.github/tools/gate/gate.sh`, `src/global.json` | _Requirements: 4.1 | Success: on a clean checkout `dotnet tool restore` then `inspect.sh --report` produces a SARIF report and a console log, and the run inspects at least as many `.cs` files as git tracks under `src/` | Instructions: mark this task in progress in tasks.md before starting, log the implementation with the log-implementation tool when done, then mark it complete_

- [x] 2. Write the evaluator and its tests
  - Files: `.github/tools/inspect/evaluate.mjs`, `.github/tools/inspect/fixtures/*.sarif` and `*.log`, `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/InspectScript.Tests.cs`
  - Implement the five ordered checks of the design: report parses; no compiler error or unresolved symbol; inspected count not below tracked count; any Error, Warning or Suggestion result listed as `path:line  Inspection  message` grouped by inspection; otherwise clean
  - Exit 0 clean, 1 findings, 2 blind; under `--report` findings exit 0 and blind still exits 2
  - Cut one fixture per outcome down from a real report of this repository: healthy and clean, one warning, one suggestion, compiler errors present, valid but nothing inspected, report missing
  - One xUnit fact per fixture plus one for `--report`, each seen to fail against the evaluator with that rule removed
  - Purpose: a clean result can only come from a run that could see the code
  - _Leverage: `GateScript.Tests.cs` for running a tool script from xUnit; `TestContext.Current.CancellationToken` on every call that takes a token_
  - _Requirements: 4.2, 4.3, 4.4, 4.5_
  - _Prompt: Implement the task for spec rider-warnings-cleanup, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Tooling developer in Node.js and xUnit | Task: Write `evaluate.mjs` and its fixtures and facts so that health is judged before findings, per the design's five numbered checks and its Testing Strategy | Restrictions: Node standard library only; the report is SARIF JSON whatever its extension; do not invent a SARIF shape, cut fixtures from a real report; do not use a blocking call inside an async test method; a fact that passes against the evaluator with its rule removed is deleted, not kept | _Leverage: `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/GateScript.Tests.cs` | _Requirements: 4.2, 4.3, 4.4, 4.5 | Success: every fact passes, and each was observed failing with its rule removed, the observation recorded in the implementation log | Instructions: mark this task in progress in tasks.md before starting, log the implementation with the log-implementation tool when done, then mark it complete_

- [x] 3. Exclude the deliberately broken fixture
  - File: `src/backend/EtAlii.Adp.sln.DotSettings`
  - Add one file-mask entry for `Broken.csproj`, keeping the file's shape: BOM, tab indentation, the closing tag on the last entry's line
  - Put the note where the repository's convention for a `.DotSettings` note allows, naming `EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests` as the test the file serves; if the format admits no comment, the note goes in the *JetBrains Rider warnings* section of tech.md as a list of what the settings file excludes and why
  - Do not change `Fixtures/cpm/Broken.csproj`
  - Purpose: the four XML errors stop being reported without destroying the test that needs the file malformed
  - _Leverage: the control run recorded in the design's Code Reuse Analysis, which showed this exact entry working_
  - _Requirements: 3.1, 3.2, 3.4_
  - _Prompt: Implement the task for spec rider-warnings-cleanup, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer familiar with ReSharper settings layers | Task: Exclude `Broken.csproj` from inspection by a file-mask entry in the solution settings file, with its reason recorded | Restrictions: mask the full file name only, never `*.csproj` or the `Fixtures` folder; the path escape for a dot is `_002E`; do not touch the fixture; check line endings with `git ls-files --eol` | _Leverage: `src/backend/EtAlii.Adp.sln.DotSettings`, CLAUDE.md Folders and namespaces for the file's shape | _Requirements: 3.1, 3.2, 3.4 | Success: a run reports zero XML errors and every other count unchanged from the run before the entry, both runs' totals stated in the implementation log | Instructions: mark this task in progress in tasks.md before starting, log the implementation with the log-implementation tool when done, then mark it complete_

- [x] 4. Add the inspection job in report mode, measure again, and run the planted control
  - File: `.github/workflows/build.yml`
  - Add a job `inspection` beside `gates`, on pull requests into `develop` and pushes to it, on `ubuntu-latest`, repeating the checkout, .NET, Node and NuGet-cache steps, then `dotnet tool restore` and `inspect.sh --report`; leave `release` needing `gates` alone
  - Run the planted control by hand: plant one warning and one suggestion in a real file, see `inspect.sh` exit 1 naming both, remove them, see the previous result return
  - Measure the inventory on the then-current `develop` and record counts per inspection in the implementation log; where an inspection named in tasks 5 to 13 has gone, or a new one has appeared, say so there
  - Compare the runner's counts with the local ones and record any difference
  - Purpose: the runner inspects every change from now on without failing any, and the later tasks start from current numbers
  - _Leverage: the `gates` job's setup steps; the design's Order of delivery on why report mode is not `continue-on-error`_
  - _Requirements: 4.6, 5.3, 6.3_
  - _Prompt: Implement the task for spec rider-warnings-cleanup, first run spec-workflow-guide to get the workflow guide then implement the task: Role: CI engineer for GitHub Actions and .NET | Task: Add the `inspection` job in report mode, run and record the planted control, and re-measure the inventory locally and on the runner | Restrictions: a separate job, not a step in `gates`; no `continue-on-error`; do not make `release` depend on it; confirm the planted findings were actually applied before reading the result, this repository is CRLF and a line-anchored edit can silently miss; remove the planted findings before committing | _Leverage: `.github/workflows/build.yml` | _Requirements: 4.6, 5.3, 6.3 | Success: the pull request's `inspection` job is green and its log shows counts per inspection; the control's two exits and the re-measured inventory are in the implementation log | Instructions: mark this task in progress in tasks.md before starting, log the implementation with the log-implementation tool when done, then mark it complete_

## Pull request 2 - what cannot change behaviour

- [x] 5. Correct the restatement suggestions (Kind A)
  - Files: every C# file the script lists for the inspections the design names under Kind A
  - One commit per inspection, each corrected as Rider's quick-fix corrects it
  - Keep a `readonly` field where a constructor being converted to a primary constructor assigned one that is read more than once
  - Read the five `InconsistentNaming` findings against `src/.editorconfig` before renaming anything; the `_logger` convention stands
  - Purpose: about 280 findings gone with no change in what the code does
  - _Leverage: the script's per-inspection listing; `jb cleanupcode` may be used for a single inspection where its result is identical to the quick-fix_
  - _Requirements: 2.2, 6.1_
  - _Prompt: Implement the task for spec rider-warnings-cleanup, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior C# developer | Task: Correct every finding of the Kind A inspections named in the design, one inspection per commit | Restrictions: no behavioural change; never nest a type, move a type out of `_Model` or change a namespace; do not touch a file marked `-text` in `.gitattributes`; do not switch any inspection off; anything that turns out able to change behaviour is left for pull request 4 or 5 and listed in the implementation log | _Leverage: `inspect.sh --report` output, `src/.editorconfig` | _Requirements: 2.2, 6.1 | Success: each Kind A inspection reports zero; the pull request description gives the count per inspection before and after from the script's own output; the four gates exit zero | Instructions: mark this task in progress in tasks.md before starting, log the implementation with the log-implementation tool when done, then mark it complete_

- [x] 6. Give every existing in-place suppression its reason
  - Files: the C# files carrying `// ReSharper disable` comments, 41 at the measured commit
  - For each comment without one, add `- Reason: <why>` on the same line, or remove the comment when the inspection no longer fires without it
  - Purpose: a suppression can be told from an oversight
  - _Leverage: the 16 comments that already carry a reason, for wording_
  - _Requirements: 3.3_
  - _Prompt: Implement the task for spec rider-warnings-cleanup, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Make every in-place ReSharper suppression in the solution carry its reason on the same line, removing those that are no longer needed | Restrictions: the reason states why the code is right, not that the warning is unwanted; test a suppression is unneeded by removing it and running the script, not by reading; do not add new suppressions here | _Leverage: existing `- Reason:` comments | _Requirements: 3.3 | Success: a search for `ReSharper disable` finds no line without `Reason:` apart from the regular expression in the repository tests that matches the phrase; the script's counts are unchanged or lower | Instructions: mark this task in progress in tasks.md before starting, log the implementation with the log-implementation tool when done, then mark it complete_

## Pull request 3 - the discovered classes

- [x] 7. Mark the classes the catalog discovers by reflection
  - Files: `src/Directory.Build.props`, the 66 `Diagram.cs` and 2 `Editor.cs` files under `src/diagrams/` and `src/editors/`, `.spec-workflow/steering/tech.md` (*Declaring a diagram definition*), `docs/creating-a-diagram-module.md`, `docs/creating-an-editor-module.md`
  - Reference `JetBrains.Annotations` once in `src/Directory.Build.props` with `PrivateAssets="all"`, and remove the now redundant reference from `EtAlii.Adp.Backend.Service.csproj`
  - Add `[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]` to every discovered class, the ones that report nothing today included
  - Show the attribute in the stub shape both documents teach
  - Purpose: one decision for the two findings 48 modules share, and one shape for a tool engineer to copy
  - _Leverage: `[UsedImplicitly]` in `Program.cs`; `DiagramDefinitionDiscovery` and `ToolDefinitionScan` for what is discovered and how_
  - _Requirements: 1.3, 2.3, 2.5_
  - _Prompt: Implement the task for spec rider-warnings-cleanup, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer who knows this repository's module discovery | Task: Make the reflection-discovered `Diagram` and `Editor` classes report nothing by marking them as implicitly used, with the annotations package referenced once for the solution | Restrictions: no per-module package reference; the attribute goes on every discovered class, not only those reporting; delete no type; update the documents in the same pull request; build a newly created worktree before trusting a green gate, since `Directory.Build.props` is upstream of everything | _Leverage: `src/Directory.Packages.props`, `src/backend/EtAlii.Adp.Diagram/DiagramDefinitionDiscovery.cs` | _Requirements: 1.3, 2.3, 2.5 | Success: `UnusedType.Global` and `UnusedMember.Global` report nothing in any `Diagram.cs` or `Editor.cs`; a stub copied from the document reports nothing; the four gates exit zero in a fresh worktree | Instructions: mark this task in progress in tasks.md before starting, log the implementation with the log-implementation tool when done, then mark it complete_

## Pull request 4 - narrowing and unused code

- [x] 8. Narrow what can be narrowed (Kind B)
  - Files: every C# file the script lists for `MemberCanBePrivate.*`, `MemberCanBeProtected.Global`, `AutoPropertyCanBeMadeGetOnly.*`, `PropertyCanBeMadeInitOnly.*`, `ClassNeverInstantiated.*` and `VirtualMemberNeverOverridden.Global`
  - For each finding, first ask what the compiler cannot see: a serializer, configuration binding, the dependency-injection container, reflection
  - Narrow where nothing unseen uses it; mark `[UsedImplicitly]` naming the user where something does
  - Purpose: about 250 findings gone, each by a change the compiler and the gates check
  - _Leverage: the service registrations (`ServiceCollection.Add*.cs`) to see what the container constructs_
  - _Requirements: 2.2, 2.4_
  - _Prompt: Implement the task for spec rider-warnings-cleanup, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior C# developer | Task: Correct every Kind B finding by narrowing, or by marking an implicit use where narrowing would break something the compiler cannot see | Restrictions: `ClassNeverInstantiated` on a class the container constructs is marked, never deleted or made static; a `[UsedImplicitly]` names what uses the member in a comment on the same line; no inspection is switched off; run all four gates, not a scoped subset, before calling the task done | _Leverage: `inspect.sh --report` output, the modules' `ServiceCollection.Add*.cs` | _Requirements: 2.2, 2.4 | Success: each Kind B inspection reports zero; the four gates exit zero; the pull request description lists what was narrowed and what was marked | Instructions: mark this task in progress in tasks.md before starting, log the implementation with the log-implementation tool when done, then mark it complete_

- [x] 9. Decide every remaining unused type, member and parameter (Kind D)
  - Files: every C# file the script lists for `UnusedType.Global`, `UnusedMember.*`, `UnusedMemberInSuper.Global` and `UnusedParameter.Global` after task 7
  - For each, search the solution for the name as a string as well as a symbol: reflection, `nameof`, registration by convention, the `.proto` files, the client
  - Found: mark `[UsedImplicitly]` and name the user. Not found: remove it, and whatever existed only to serve it
  - `[PublicAPI]` only where a document already names that surface as an API
  - Purpose: no "never used" finding left, and no live code deleted to get there
  - _Leverage: tech.md decision log entry 10 for the method and for its two classes of false positive_
  - _Requirements: 1.4, 2.3, 6.2_
  - _Prompt: Implement the task for spec rider-warnings-cleanup, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior C# developer doing a dead-code review | Task: Give every remaining unused-type, unused-member and unused-parameter finding one of two outcomes, marked as implicitly used with its user named, or removed | Restrictions: the default is prove it, not delete it; a search that found nothing must name what it swept; a type discovered by reflection or registered by name is never deleted; test classes discovered by xUnit are not dead; do not switch the inspection off | _Leverage: `.spec-workflow/steering/tech.md` decision log entry 10 | _Requirements: 1.4, 2.3, 6.2 | Success: these inspections report zero; the four gates exit zero; the pull request description lists every removed type and member with what showed it was unused | Instructions: mark this task in progress in tasks.md before starting, log the implementation with the log-implementation tool when done, then mark it complete_

## Pull request 5 - unread data and possible defects

- [x] 10. Decide everything written and never read (Kind C)
  - Files: every C# file the script lists for `NotAccessedPositionalProperty.Global`, `UnusedAutoPropertyAccessor.Global`, `CollectionNeverQueried.Local`, `CollectionNeverUpdated.Local`, `NotAccessedVariable`, `RedundantAssignment`, `UnusedVariable`, `UnusedMethodReturnValue.Global` and `OutParameterValueIsAlwaysDiscarded.Global`, and the parser tests beside them
  - Ask the design's three questions in order: read by something that is not C# in this solution (mark, name the reader); populated by a parser and asserted nowhere (add the assertion to the parser's existing test, or remove the field and its parsing when no fixture contains the construct); otherwise dead (remove)
  - `EtAlii.Adp.Specification.Fbl` holds the most; take it first and alone
  - Purpose: 69 of the 89 warnings gone, and every parsed field either checked by a test or no longer parsed
  - _Leverage: the existing parser tests and their fixtures; the vendored example data rule in CLAUDE.md_
  - _Requirements: 1.2, 1.3, 1.4_
  - _Prompt: Implement the task for spec rider-warnings-cleanup, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer who writes parser tests | Task: Resolve every written-but-never-read finding by the design's three ordered questions, preferring a real test assertion over an annotation | Restrictions: an added assertion must be seen to fail when the parser's handling of that field is broken; do not hand-write a toy fixture to justify keeping a field; do not touch a fixture marked `-text`; no inspection switched off; a removed field's parsing is removed with it | _Leverage: `src/backend/EtAlii.Adp.Specification.Fbl.Tests`, the modules' `Fixtures` folders | _Requirements: 1.2, 1.3, 1.4 | Success: these inspections report zero; each new assertion was observed failing first; the pull request description lists what was asserted, what was marked and what was removed | Instructions: mark this task in progress in tasks.md before starting, log the implementation with the log-implementation tool when done, then mark it complete_

- [x] 11. Settle the inconsistently synchronized field
  - Files: `src/backend/EtAlii.Adp.Diagram/DiagramDocumentReloadBridge.cs`, its test file
  - Establish which reads outside `_watcherLock` can race a write
  - A race: fix it, with a test seen to fail before the fix. No race: state the reason at the field and suppress the reads with it
  - Purpose: three warnings resolved as what they are, a defect or a documented non-defect
  - _Leverage: the comment at the first lock, which records why a lock was chosen over `GetOrAdd`_
  - _Requirements: 1.5_
  - _Prompt: Implement the task for spec rider-warnings-cleanup, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer experienced in concurrency | Task: Decide whether the unsynchronized reads in `DiagramDocumentReloadBridge` are a defect, and fix or document accordingly | Restrictions: state the verdict and its reasoning in the implementation log either way; a concurrency test that passes against the unfixed code is deleted, not kept; do not widen the lock without saying what it now serializes; pass `TestContext.Current.CancellationToken` in tests | _Leverage: `src/backend/EtAlii.Adp.Diagram/DiagramDocumentReloadBridge.cs` | _Requirements: 1.5 | Success: `InconsistentlySynchronizedField` reports zero; if a defect was fixed, its test was observed failing first; the four gates exit zero | Instructions: mark this task in progress in tasks.md before starting, log the implementation with the log-implementation tool when done, then mark it complete_

- [x] 12. Settle the possible loss of fraction
  - Files: `src/diagrams/rdf/backend/EtAlii.Adp.Diagram.Rdf/Skos/SkosLayout.cs`, `src/diagrams/rdf/backend/EtAlii.Adp.Diagram.Rdf.Tests/SkosLayout.Tests.cs`
  - Decide from what the layout draws whether integer division is intended
  - Intended: suppress with the reason. Not intended: fix, with a test seen to fail first
  - Purpose: two warnings resolved, and the layout's arithmetic stated rather than accidental
  - _Leverage: the existing suppression that reads `PossibleLossOfFraction - Reason: On purpose`_
  - _Requirements: 1.6_
  - _Prompt: Implement the task for spec rider-warnings-cleanup, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer working on diagram layout | Task: Decide whether the integer divisions in `SkosLayout` and its test are intended, and suppress with a reason or fix accordingly | Restrictions: state the verdict in the implementation log; the test must not simply repeat the production expression; if positions change, check the SKOS examples still draw as the screenshots show and retake any that became misleading | _Leverage: `src/diagrams/rdf/backend/EtAlii.Adp.Diagram.Rdf/Skos/SkosLayout.cs` | _Requirements: 1.6 | Success: `PossibleLossOfFraction` reports zero without an unexplained suppression; the four gates exit zero | Instructions: mark this task in progress in tasks.md before starting, log the implementation with the log-implementation tool when done, then mark it complete_

- [x] 13. Trace the six null-coalescing warnings and close out the warnings
  - Files: `FdgWriter.cs`, `SankeyWriter.cs`, `SetSankeyPropertyCommandHandler.cs`, `SupplyChainWriter.cs`, `SetSupplyChainPropertyCommandHandler.cs`, and the types whose annotations they rely on
  - Trace each left operand to where its value comes from
  - Annotation right: remove the dead `??`. A null can arrive: correct the annotation at its source, with a test that passes a null
  - Then run the script without `--report` and correct whatever warning or suggestion remains from any kind
  - Purpose: the last warnings gone, and the first run that exits zero
  - _Leverage: the nine existing suppressions of this inspection, which show where this codebase met a real null before_
  - _Requirements: 1.1, 1.2, 2.1_
  - _Prompt: Implement the task for spec rider-warnings-cleanup, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer fluent in nullable reference types | Task: Resolve the six null-coalescing warnings by tracing each value to its source, then bring the whole solution to zero warnings and zero suggestions | Restrictions: do not delete a `??` on the annotation's word alone, deserialized and reflection-set values ignore annotations; do not silence with `!`; no inspection switched off; a remaining finding of another kind is corrected by that kind's rules from the design | _Leverage: the functional-decomposition-graph, sankey and supply-chain modules | _Requirements: 1.1, 1.2, 2.1 | Success: `inspect.sh` without `--report` exits zero on the branch, with the run's inspected-file count and zero compiler errors quoted in the implementation log; the four gates exit zero | Instructions: mark this task in progress in tasks.md before starting, log the implementation with the log-implementation tool when done, then mark it complete_

## Pull request 6 - the rule enforced

- [x] 14. Make the inspection job fail on a finding
  - File: `.github/workflows/build.yml`
  - First confirm `inspect.sh` exits zero on the current tip of `develop`, on the runner as well as locally; a finding that arrived since task 13 is corrected first
  - Drop `--report` from the `inspection` job
  - Open a throwaway pull request that plants one suggestion, see the job fail and name the line, and close it unmerged
  - Purpose: a pull request that adds a Rider finding is refused
  - _Leverage: the job added in task 4_
  - _Requirements: 5.1, 5.2_
  - _Prompt: Implement the task for spec rider-warnings-cleanup, first run spec-workflow-guide to get the workflow guide then implement the task: Role: CI engineer | Task: Switch the `inspection` job from report mode to enforcing, after proving `develop` is clean, and prove on GitHub that it refuses a planted finding | Restrictions: do not enforce while `develop` reports anything; the throwaway pull request is never merged and its branch is deleted; do not add the check to the local gates | _Leverage: `.github/workflows/build.yml` | _Requirements: 5.1, 5.2 | Success: the job is green on this pull request and red on the throwaway one, both run links in the implementation log | Instructions: mark this task in progress in tasks.md before starting, log the implementation with the log-implementation tool when done, then mark it complete_

- [x] 15. Bring the documents in line
  - Files: `.spec-workflow/steering/processes.md`, `.spec-workflow/steering/tech.md`, `CLAUDE.md`, `docs/guards.md`
  - processes.md, *Checking that the conventions are actually followed*: replace the bullet that says to expect a backlog, and the install-by-hand instruction, with the script and the job; and where it says `jb inspectcode` is not a missing fifth gate, say what is now true, a check on the runner and still not a local gate
  - tech.md, *JetBrains Rider warnings*: remove the sentence about warnings that existed when the rule was made, and point at the script
  - CLAUDE.md, *Folders and namespaces*, and `docs/guards.md`: the same two facts
  - Purpose: no document describes a state this specification ended
  - _Leverage: CLAUDE.md on retiring a reason and not only a rule_
  - _Requirements: 5.3, 5.4_
  - _Prompt: Implement the task for spec rider-warnings-cleanup, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Technical writer who knows this repository's steering documents | Task: Update the four documents so each says how the inspection is run and that the runner enforces it, and none tells a reader to expect a backlog or to install the tool by hand | Restrictions: the four local gates stay four and the text must say so; keep worked incidents as history, change only what is stated as current; search each document for every mention rather than editing the ones remembered; `ArchitecturePages.Tests` and the documentation-link tests must stay green | _Leverage: `docs/guards.md`, `.spec-workflow/steering/processes.md` | _Requirements: 5.3, 5.4 | Success: a search of the four documents for `inspectcode` and `InspectCode` finds no instruction that contradicts the script or the job; the four gates exit zero | Instructions: mark this task in progress in tasks.md before starting, log the implementation with the log-implementation tool when done, then mark it complete_

- [ ] 16. Write the closing record and trace the requirements
  - File: the implementation log of this task
  - List every inspection this specification switched off or downgraded, with the change, where it lives, the reason and the date of the user's answer; an empty list is stated as none
  - Open the solution in Rider with the committed settings and solution-wide analysis on, and record what the *Problems* list shows for C#
  - Trace every acceptance criterion to a file and a string, then read two of the traces back to their artefacts and ask whether the evidence is that criterion's
  - Purpose: the user can see what was decided rather than corrected, and that the code shows what the requirements ask
  - _Leverage: CLAUDE.md, Checking that a specification's tasks cover its requirements_
  - _Requirements: 1.1, 2.1, 3.5, 3.6_
  - _Prompt: Implement the task for spec rider-warnings-cleanup, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Reviewer closing a specification | Task: Publish the list of switched-off inspections, record the Rider check by eye, and trace all 31 acceptance criteria to evidence in the tree | Restrictions: trace to files and strings, not to a task's promise; a count that balances is not a trace, read two back; say plainly if Rider shows something the script does not, that is a finding about settings outside the repository; tick this task only after the last pull request is an ancestor of `develop` | _Leverage: `.spec-workflow/specs/rider-warnings-cleanup/requirements.md` | _Requirements: 1.1, 2.1, 3.5, 3.6 | Success: the closing record holds the list, the Rider observation and the trace, and the user has been told where to read it | Instructions: mark this task in progress in tasks.md before starting, log the implementation with the log-implementation tool when done, then mark it complete_

## Coverage of the requirements

The requirements hold 31 acceptance criteria. Extracting every reference from the task lines above and diffing against that list leaves 30 claimed and one unclaimed:

- **6.4** (the four gates exit zero on each branch) is claimed by no task because no single task can claim it. It is stated once under *Delivery* and binds every task.

**6.1** (a pull request holds corrections that cannot change behaviour or corrections that can, not both) is claimed by task 5, where a correction most easily strays across the line, but what satisfies it is the division into six pull requests rather than anything task 5 does.

Requirements 3.1 and 3.2 are claimed by task 3 for the one exclusion, and 3.6 by task 16 for the record; for any inspection switched off in between they are carried by the standing rule at the top of this document, since which task that would happen in cannot be known in advance.
