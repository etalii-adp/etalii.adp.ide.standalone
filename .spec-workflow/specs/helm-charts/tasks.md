# Tasks Document

One worktree for the whole spec: `.claude/worktrees/helm` — deliberately short, because the Windows MAX_PATH failure mode (apphost never produced, `dotnet test` reporting zero tests) was traced to long worktree names, and this module's deepest project paths are long. Each group ends in a gate-and-merge task judged by exit code. Tasks marked **(new work)** build the design's genuinely new pieces; everything else replicates established Ansible/Databricks patterns. Example ordering is fact-based: `ExampleReplication.Tests` demands central copies land in the same change as module copies, and `ExampleRegistration.Tests` opens every example `.adp` against the registered definitions — so the charts arrive in group 1 (the external-risk-first task) while their `.adp` registrations wait for group 7, after the definition exists.

- [x] 1. Examples acquired and attributed **(new work: the acquisition plan executed)**
  - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5_

- [x] 1.1 Acquire the three charts, licenses re-verified at acquisition
  - Files: `src/diagrams/helm-charts/examples/hello-world/`, `…/prometheus/`, `…/nginx/` (with bitnami's `common` vendored unpacked under `nginx/charts/common/`), each carrying the upstream `LICENSE` (and `NOTICE` where provided)
  - Download from the three sources the design names. **Acceptance criterion, not a note: re-verify each source's license text reads permissive (Apache-2.0/MIT/BSD) at the moment of acquisition, before copying a byte** — the research verification of 2026-09-03 is a candidate list, not a verification. A source that has changed, or whose license reads differently than the research said, is a finding to report and a stop for that source, never a step to push through; substitution needs the same verification. Review every acquired file for secrets or credentials before committing; record chart name, version and retrieval date for the readmes of task 1.2. No `.adp` files in this task.
  - _Requirements: 11.2, 11.3, 11.5_
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Release engineer with open-source licensing diligence | Task: Acquire hello-world (helm/examples), prometheus (prometheus-community/helm-charts) and nginx (bitnami/charts) into the module examples folder, vendor bitnami's common library chart unpacked under nginx/charts/, place each upstream LICENSE/NOTICE beside its chart, re-verifying every license at acquisition time as the acceptance criterion and reviewing for secrets before commit | Restrictions: nothing is copied before its license re-verification passes; a changed source or divergent license stops that source and is reported as a finding; unmodified upstream content stays byte-unmodified; no .adp files yet — the registration sweep would open them against a definition that does not exist | _Leverage: the design's Example Acquisition Plan table; requirements Requirement 11 | Success: three charts plus the vendored common sit in the module examples tree with their license texts, provenance recorded, no secrets, no .adp files. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 1.2 Authored additions and central replication
  - Files: `nginx/values-dev.yaml` + `nginx/values-prod.yaml` (ADP-authored), a readme per example (source URL, chart version, retrieval date, verified license, exact local-additions list), the whole tree replicated byte-identically to `src/examples/diagrams/helm-charts/`; a `*.tgz -text` line in the root `.gitattributes` (with the one-line reason, per the file's established commentary style) if any archive fixture ships and the line is absent
  - The override files demonstrate the values stack and are listed in nginx's readme as local additions — layering shown by honest authorship, never misattributed to upstream. Replication must land in this same task: `ExampleReplication.Tests` compares every module example file against its central replica.
  - _Requirements: 11.1, 11.3, 11.4, 11.6_
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Technical writer and repository curator | Task: Author the nginx values-dev/values-prod overrides and the per-example readmes with full provenance, then replicate the examples tree to src/examples/diagrams/helm-charts/ byte-identically, adding the .gitattributes archive line if needed | Restrictions: every local addition is named in its example's readme; upstream files stay untouched; module and central copies must not differ by a byte — helm .adp files do not exist yet so the body: tolerance is irrelevant | _Leverage: ExampleReplication.Tests' path mapping; the ansible-structure example readmes as the format precedent | Success: readmes complete and honest, overrides in place, ExampleReplication.Tests green over the new trees. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 1.3 Gate and merge group 1
  - Backend tests + format from `src/backend` (format introduces no findings beyond develop's baseline), npm test + typecheck from `src/client`, exit codes checked; merge `.claude/worktrees/helm` into `develop`
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Release engineer | Task: Run the gates with exit codes checked and merge the helm worktree's group-1 work into develop via the main checkout | Restrictions: do not merge on a failing gate; a zero-test run is a broken build, never an empty suite | Success: gates green, merged. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 2. Reading: tolerant YAML and the template scanner
  - _Requirements: 1.1, 1.2, 1.3, 3.1, 3.2, 3.3, 3.4, 3.5, 4.1, 4.2, 4.3_

- [x] 2.1 HelmYaml, the tolerant reader (replication of AnsibleYaml)
  - Files: `src/diagrams/helm-charts/backend/EtAlii.Adp.Diagram.HelmCharts/HelmYaml.cs` + `_Model/` result records; `EtAlii.Adp.Diagram.HelmCharts.Tests` project (Exe, xUnit v3) with its first tests; both projects added to `EtAlii.Adp.slnx`
  - YamlDotNet 18.1.0 representation model, line marks kept, the `(Line: n, Col: m): ` prefix stripped from error messages, never throws
  - _Requirements: 3.1, 3.5_
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior C# developer | Task: Create the module and test projects and implement HelmYaml as a tolerant never-throwing YAML reader with line marks, replicating AnsibleYaml's shape module-locally per the design's pattern-reuse note | Restrictions: module code depends on core only, never on the ansible module; test project is Exe per xUnit v3; use IoPath alias where EtAlii.Adp.Path shadows System.IO.Path | _Leverage: AnsibleYaml.cs and AnsibleYaml.Tests as the pattern to replicate | Success: parse, line-mark and unreadable-result tests pass; malformed YAML yields an error result, never an exception. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 2.2 TemplateScan **(new work)**
  - Files: `TemplateScan.cs` + tests
  - The literal-fact line scanner: `kind:`/`apiVersion:` count only when the value carries no `{{`; `define`/`include`/`template` names count only as literal double-quoted strings; everything else is unknown by design; results sorted ordinally
  - _Requirements: 3.2, 4.2_
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior C# developer | Task: Implement TemplateScan as a pure static line scanner per the design's literal/templated boundary, with the boundary pinned case by case in tests — literal kind, templated kind, quoted and unquoted names, multi-document files, NOTES.txt yielding nothing | Restrictions: no YAML parsing of templates, no Go-template evaluation, no guessing at templated values; one class, fixture-pinned | _Leverage: HelmYaml only for nothing — this is line-based on purpose | Success: the boundary tests pass and read as the specification of what counts as literal. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 2.3 The model and HelmChartReader
  - Files: `_Model/` records (`HelmChart`, `ChartMetadata`, `ValuesFile`, `TemplateFile`, `DependencyDeclaration`, `VendoredEntry`, `LockFile`), `HelmChartReader.cs`; test fixtures under `…Tests/Fixtures/{well-formed,broken,unconventional}/` with a directory-local `.gitattributes` (`* -text`) and a fixtures readme
  - Reads the full Requirement 1 inventory: v2 fields, v1 legacy fallback to `requirements.yaml`/`.lock`, values stack, schema presence, template roles, `crds/`, `charts/` one level deep (`.tgz` sealed), lock; skips reparse points; sorts ordinally; never writes; foreign files ignored without complaint; record equality over lists is not asserted in tests — rendered descriptions are
  - _Requirements: 1.1, 1.2, 1.3, 3.3, 3.4, 4.1, 4.2, 4.3_
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior C# developer familiar with the ansible module's reader | Task: Implement the _Model records and HelmChartReader over the three fixture charts per design — well-formed, broken (bad YAML, missing fields), unconventional (v1 legacy, alias, tgz + unpacked mix, library chart) — with the directory-local .gitattributes protecting fixture bytes | Restrictions: templates go through TemplateScan only; one recursion level into charts/; deeper content summarized by count; no file writes anywhere in the read path | _Leverage: HelmYaml and TemplateScan from 2.1/2.2; AnsibleProjectReader's fixture discipline and Describe()-style test comparisons | Success: reader tests cover all three fixtures; a broken file degrades to an unreadable node while the rest reads; v1 marks legacy. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 2.4 Gate and merge group 2
  - Same gates as 1.3; merge into `develop`
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Release engineer | Task: Run the gates with exit codes checked and merge group-2 work into develop | Restrictions: do not merge on a failing gate | Success: gates green, merged. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 3. Graph and store
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 1.4_

- [x] 3.1 The dependency resolution matcher **(new work)**
  - Files: `DependencyResolution.cs` (or the design's equivalent home in `HelmGraph`) + tests
  - `EffectiveName` (alias-over-name) matched ordinally against `charts/` entries (directory name; archive name stripped of `-<version>.tgz`); dependencies get Resolved/Unvendored, unmatched vendored content gets Undeclared; no semver-range evaluation, deliberately
  - _Requirements: 5.2_
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior C# developer | Task: Implement the alias-aware three-state resolution matcher as a pure function with tests for alias matching, archive-name stripping, Unvendored open ends and Undeclared leftovers | Restrictions: ordinal matching only; no version-constraint evaluation — the lock's pin is display data, never validated against the range | _Leverage: the _Model records from 2.3; AnsibleEdge's three-state TargetResolution as the shape precedent | Success: matcher tests pin every state transition including the alias and tgz cases. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 3.2 HelmGraph: nodes and the five edge families
  - Files: `HelmGraph.cs` + `_Model` node/edge records + tests
  - declares (constraint label), resolves (via 3.1), overrides (stack), configures (effective-name key; `global:` marks the node), includes (literal names to local partials; unmatched become open ends, not findings); condition badges resolved against parsed default values where the path exists; lock pins carried onto dependency nodes
  - _Requirements: 5.1, 5.3, 5.4, 5.5, 5.6, 5.7_
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior C# developer | Task: Build HelmGraph from the model per design, every edge family and badge tested on the fixtures, unmatched includes drawn as open ends | Restrictions: pure function over the model; no filesystem access; open ends are model states, never exceptions | _Leverage: the matcher from 3.1; AnsibleGraph's node/edge shape | Success: graph tests cover each edge family's fire and non-fire cases on the three fixtures. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 3.3 HelmChartStore and the watched folder
  - Files: `IHelmChartStore.cs`, `HelmChartStore.cs`, `HelmWatchedFolder.cs` + tests
  - `GetOrLoad`/`Get`/`Acquire`/`Release`; one guarded coalescing watcher per acquired chart root; a settled burst is one whole-folder re-read; burst tests assert a range, never an exact count; no save method exists
  - _Requirements: 1.4_
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior C# developer | Task: Replicate the ansible watched-folder store shape for chart roots with coalescing burst re-reads and acquire/release lifetimes, tested with range assertions | Restrictions: no save path anywhere; the watcher watches the chart root itself, not a parent; burst assertions use Assert.InRange, not exact counts | _Leverage: AnsibleProjectStore/AnsibleWatchedFolder and their tests, including the range-assertion lesson | Success: store tests pass reliably including the burst case; release stops the watcher. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 3.4 Gate and merge group 3
  - Same gates; merge into `develop`
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Release engineer | Task: Run the gates with exit codes checked and merge group-3 work into develop | Restrictions: do not merge on a failing gate | Success: gates green, merged. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [-] 4. Wire, layout, session and registration
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 4.4, 6.1, 6.2, 6.3, 6.4, 12.1, 12.2_

- [-] 4.1 The proto and the element mapper
  - Files: `src/diagrams/helm-charts/api/helm-charts.proto` (`option csharp_namespace = "EtAlii.Adp.Diagram.HelmCharts.Wire";`), buf generation wired like the sibling modules, `HelmElementMapper.cs` + tests
  - The design's payload set; element types `helm/chart+{chart,values,schema,template,partial,crds,dependency,subchart,archive,lock,edge}`; stable content-derived ids (`dep:`, `tpl:`, `values:`, `edge:` prefixes); `Diff()` emits Remove-then-Add, never group/ungroup
  - _Requirements: 4.4_
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior C# developer with protobuf experience | Task: Define helm-charts.proto with the Wire namespace, wire generation, and implement HelmElementMapper projecting graph plus layout to Elements with Any payloads and a Remove-then-Add Diff | Restrictions: the Wire sub-namespace is mandatory — generated names collide with _Model records without it (the CS0101 lesson); ids must be stable across sessions because the layout block stores them | _Leverage: ansible-structure.proto and AnsibleElementMapper as templates | Success: generation builds clean; mapper tests cover projection, ids and Diff. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 4.2 HelmLayout
  - Files: `HelmLayout.cs` + tests
  - Banded computed layout per design: metadata band, values column, templates column, dependencies/vendored column; pure and deterministic on fixtures
  - _Requirements: 6.1_
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior C# developer | Task: Implement the banded layout as a pure deterministic function with fixture tests | Restrictions: no randomness, no filesystem; determinism is a test | _Leverage: AnsibleLayout's band approach | Success: layout tests pin the banding on the fixtures. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 4.3 The definition and service registration
  - Files: `Diagram.cs` (implemented shape: named `Chart` property with `Build`, `Subject: DiagramSubject.Folder`, no `Extension`, fitting mdi icon), `ServiceCollection.AddHelmCharts.cs`, registration/discovery tests
  - No document factory, no toolbox provider, no action provider, no commands — each absence is the requirements' stated position; the Add flow suggests `helm/chart` on a folder containing `Chart.yaml`
  - _Requirements: 2.1, 2.3, 2.4, 12.1, 12.2_
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior C# developer | Task: Declare the helm/chart folder-subject definition and the single-file service registration with discovery tests, wiring the Add-flow folder suggestion | Restrictions: no DocumentExtension constant — tech.md says that omission is correct for a folder subject, do not fix it; core files are not edited | _Leverage: the ansible module's Diagram.cs and ServiceCollection.AddAnsibleStructure.cs | Success: discovery tests see the definition; Add suggests on a Chart.yaml folder; the module registers without core changes. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 4.4 HelmSession and factory, with the verified seam as precondition
  - Files: `HelmSession.cs`, `HelmSessionFactory.cs` + tests
  - **Stated preconditions, verified in code at design time — an implementer finding any of them false stops and reports rather than working around:** (a) `RegistrationLayout.Read` tolerates a registration whose only header is the MIME line; (b) the router hands a folder-subject factory the `.adp` as both `bodyPath` and `registrationPath` (pinned in `AnsibleSessionFactory`); (c) `IDiagramSession.MoveElementToAsync` is a default-refused, overridable member. The session delivers the whole diagram at `Baseline()`, `UpdateView` answers nothing new, `MoveElementAsync` (reparent) refuses with a sentence, `MoveElementToAsync` refuses edge ids then dispatches `SetRegistrationLayoutCommand` through the history stack; store changes diff against the last delivery; positions overlay via `RegistrationLayout.Apply`
  - _Requirements: 6.2, 6.3, 6.4, 2.2_
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior C# developer familiar with DatabricksSession's move path | Task: Implement the session and factory per design — folder resolution as AnsibleSessionFactory does it, reposition dispatching the core layout command as DatabricksSession does it — with execute/undo tests proving the .adp gains and loses the entry while chart bytes never change | Restrictions: the three stated preconditions are stop-and-report if found false, never worked around; no module writer for chart content exists | _Leverage: AnsibleSessionFactory.cs, DatabricksSession.cs MoveElementToAsync, RegistrationLayout + SetRegistrationLayoutCommand from core | Success: session tests cover baseline, diffing, refusals and the undoable reposition; chart files stay byte-identical throughout. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 4.5 Gate and merge group 4
  - Same gates; merge into `develop`
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Release engineer | Task: Run the gates with exit codes checked and merge group-4 work into develop | Restrictions: do not merge on a failing gate | Success: gates green, merged. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 5. Validation and context
  - _Requirements: 10.1–10.9, 8.1, 8.2, 8.3, 9.2, 9.3, 9.4_

- [ ] 5.1 HelmRuleSet
  - Files: `HelmRuleSet.cs` + `_Model` finding records + tests
  - The Requirement 10 rules as a pure function with chart-root-relative paths and lines; tested fire **and** non-fire per rule — including that Unvendored never fires, library charts escape the templates rule, and lock rules stay silent without a lock
  - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7_
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior C# developer | Task: Implement the rule set as a pure function over the model with both-direction tests per rule | Restrictions: no filesystem access in rules; noise guards are part of the specification — Unvendored and missing-lock are silence, not findings | _Leverage: AnsibleRuleSet's pure-function shape and its noise-guard precedent | Success: every rule has a fire and a non-fire test; the shipped example fixtures produce zero findings. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 5.2 HelmValidator
  - Files: `HelmValidator.cs` + tests
  - Rebases folder-relative findings to project-relative (the `Rebased()` discipline — the bug class 177 unit tests missed once), attributes the not-a-chart finding to the folder, registers into the problems pipeline
  - _Requirements: 10.1, 10.8, 10.9_
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior C# developer | Task: Implement the validator wrapping the rule set with path rebasing and folder attribution, tested for a chart registered in a subfolder | Restrictions: rebasing is tested against a nested chart, not just the root — that is where the ansible bug hid; folder findings rely on core ProblemStamp, which is not modified | _Leverage: AnsibleValidator's Rebased() and the DiagramProblemFileLocation seam | Success: validator tests place findings at real project-relative paths; the folder finding renders fresh. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 5.3 HelmContextSourceResolver
  - Files: `HelmContextSourceResolver.cs`, `HelmNodeSubscription.cs` + tests
  - Folder target (`ResolvedFullPath` is the chart root); node subscriptions clear the selection when the backing artifact disappears, watching the chart root itself — the one-folder-too-high bug is a named test
  - _Requirements: 8.1, 8.2, 8.3_
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior C# developer | Task: Implement the resolver and subscription per the ansible shape, with the deleted-artifact selection-clearing test watching the correct folder | Restrictions: do not GetDirectoryName a path that is already the folder — the exact ansible Track bug, pinned by test | _Leverage: AnsibleContextSourceResolver and its ADeletedRole_ClearsTheSelection test | Success: resolver tests pass including the deletion case at speed (no timeout-driven pass). Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 5.4 HelmContextPropertyProvider
  - Files: `HelmContextPropertyProvider.cs` + tests
  - One private `Row(id, label, value, source, group)` helper always setting `ReadOnlyReason = $"Defined in {source}; edit it in a text editor."`; `SetAsync` refuses unconditionally; the nothing-selected grid summarizes the chart
  - _Requirements: 9.3, 9.4_
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior C# developer | Task: Implement the read-only property grids per element kind with the house reason on every row and an unconditional SetAsync refusal, tested per kind | Restrictions: every row read-only with a true reason naming its defining file; no editable path exists | _Leverage: AnsibleContextPropertyProvider's Row helper shape | Success: grid tests cover each element kind and the summary grid; every row carries the reason. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 5.5 Gate and merge group 5
  - Same gates; merge into `develop`
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Release engineer | Task: Run the gates with exit codes checked and merge group-5 work into develop | Restrictions: do not merge on a failing gate | Success: gates green, merged. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 6. Client
  - _Requirements: 6.2, 7.1, 8.1, 8.2, 8.3_

- [ ] 6.1 Model and stream hook
  - Files: `src/diagrams/helm-charts/client/helmModel.ts`, `useHelmStream.ts` + tests
  - Payload decoding, two delta cases, anchor/palette helpers, and `moveElementTo` — the member the ansible hook deliberately lacked
  - _Requirements: 6.2_
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior TypeScript developer | Task: Implement the client model and stream hook with moveElementTo, tested over mocked streams | Restrictions: follow the client editorconfig sections; two delta cases only — the wire emits Remove-then-Add | _Leverage: ansibleModel.ts and useAnsibleStream.ts, plus the databricks client's move dispatch | Success: model and hook tests pass; typecheck clean. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 6.2 Canvas, styles, registration
  - Files: `HelmCanvas.tsx`, `helm-charts.css`, `register.ts`, client `readme.md` + tests
  - Central canvas composition, band chrome, drag-to-reposition wired to `moveElementTo`, activation per node kind (open in text editor; subchart diagram offer; sealed/open-end no-op), palette as CSS classes per kind
  - _Requirements: 7.1, 8.1, 8.2, 8.3_
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior TypeScript/React developer | Task: Implement HelmCanvas from the central canvas library with band chrome, reposition dispatch and per-kind activation, plus module CSS and registration, tested over mocked streams | Restrictions: reuse src/client/src/canvas primitives — no bespoke drawing; every tab's content keyed per tab (the React instance-reuse lesson) | _Leverage: AnsibleCanvas.tsx and its css slot pattern; canvas elements/connections | Success: canvas tests cover structure, activation and move dispatch; npm test and typecheck green. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 6.3 Gate and merge group 6
  - Same gates; merge into `develop`
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Release engineer | Task: Run the gates with exit codes checked and merge group-6 work into develop | Restrictions: do not merge on a failing gate | Success: gates green, merged. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 7. Proof and finish
  - _Requirements: 7.1, 7.2, 2.3, 11.6, 12.3, 10.9_

- [ ] 7.1 Zero-writes proof
  - Files: `ZeroWrites.Tests.cs` (module tests)
  - Open, browse, lay out, validate and close a fixture chart; every file byte-identical via `SequenceEqual` naming the offending file (never tuple/record equality over byte arrays); no file created or deleted
  - _Requirements: 7.1, 7.2_
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior C# developer | Task: Implement the zero-writes proof over a fixture chart with byte-level assertions naming any offending file | Restrictions: SequenceEqual per file — Assert.Equal over dictionaries of byte arrays compares references (the recorded lesson); assert file-set equality too | _Leverage: the ansible ZeroWrites.Tests and its AssertUnchanged helper shape | Success: the proof passes and fails loudly (naming the file) when sabotaged. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 7.2 Integration tests against the real host
  - Files: `EtAlii.Adp.Backend.Tests/Integration Tests/HelmChartsFlow.Tests.cs`, `HelmValidationFlow.Tests.cs` (+ fixture plumbing)
  - The gRPC flow: open a chart end-to-end, baseline observed, a disk change producing a delta; validation findings at project-relative paths; layout persistence — move through the session, `.adp` gains the entry, reopen overlays, undo removes, chart bytes untouched
  - _Requirements: 2.2, 6.2, 6.3, 6.4, 10.8_
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior C# developer | Task: Implement the integration flows per design — open/delta, validation placement, and the full reposition-undo cycle with byte checks | Restrictions: run via dotnet test --solution per the repo's xUnit v3 conventions; zero tests ran means broken build | _Leverage: AnsibleStructureFlow.Tests.cs and AnsibleValidationFlow.Tests.cs as templates | Success: all flows green against the real host. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 7.3 Example registrations, now that the definition exists
  - Files: one `helm-chart.adp` (MIME line `helm/chart`) inside each of the three module example chart roots, replicated to the central copies
  - `ExampleRegistration.Tests` now opens them against the registered definition; `ExampleReplication.Tests` stays green (identical copies — no `body:` header exists to differ); validation over all shipped examples reports zero findings
  - _Requirements: 11.1, 11.6, 10.9_
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior developer | Task: Add the .adp registrations to the three examples in both trees and prove the registration sweep, replication sweep and validation are all green | Restrictions: one MIME line per .adp, nothing else; both trees in the same change | _Leverage: the ansible example's structure.adp precedent | Success: both sweeps green; every example opens by double-click and validates clean. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 7.4 Catalog, manual pass, final gates, retire
  - Files: `docs/diagrams.md` (the `helm/chart` row to its earned state, catalog vocabulary), `tests.md` (manual checks for what only a running app shows: Add-flow suggestion on a Chart.yaml folder, per-kind double-click navigation, the drag-reposition-reopen cycle through the real client)
  - Run the manual verification pass against a live app from the worktree — changing the client dev port and `Client:DevServerUrl` to a free pair first and reverting both before the final merge; run the full gates; merge; the worktree is then retirable (removal is the user's call to confirm)
  - _Requirements: 12.3_
  - _Prompt: Implement the task for spec helm-charts, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Release engineer and manual tester | Task: Update the catalog row, add the tests.md manual checks, execute the manual pass against a live app from the worktree with the port-pair rule honored both directions, run all gates and merge | Restrictions: kill dev processes by PID never by image name; revert the port files before merging; findings from the manual pass leave guards behind per CLAUDE.md | _Leverage: the tests.md format precedent; CLAUDE.md's worktree and port rules | Success: catalog states the truth, manual checks recorded, gates green, merged, no stray port change in develop. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._
