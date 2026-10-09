# Requirements Document

## Introduction

The user's request, verbatim (2026-10-09):

> Create spec workflow MCP specifications. One specification for converting one of the standalone diagram tools to FBL and DISL. i.e. analyse the current implementation, define the DISL and FBL and then write the specification on the refactoring., also focus on finding issues and aspects where the DISL and FBL languages do not yet cover everything.

This is the specification for the **Helm chart diagram** (`helm/chart`, module folder `helm-chart`), one of the fifteen that reading gives (the series and the survey behind it are in `causal-loop-disl-fbl`, *Introduction*). The Helm chart diagram is on neither language today.

**It is the series' folder subject, and it never writes its subject.** The diagram has no body file: a registration (`.adp`) inside a chart folder makes that folder a diagram, and `HelmChartReader` reads `Chart.yaml`, the values files, `templates/`, `crds/`, `charts/` and the lock into a model. Nothing in the chart folder is ever created, changed or deleted; the one edit is dragging a box, which core writes into the registration's `layout:` block. So this conversion has no writer to replace, no template, no insert and no remove. It is **reading plus derivation**: the reader becomes a persistence plugin under the contract of FBL section 11 over a folder subject of FBL section 10, and the property rows, the findings and the refusals are derived from a bundled DISL definition.

**What "converted" means here** is therefore narrower than for the causal loop diagram and has one more part: the module's property rows, findings and refusals are derived from a bundled DISL 0.3 definition by `EtAlii.Adp.Specification.Disl`; the chart folder is recognised, selected, read and watched as the module's FBL binding declares, by `EtAlii.Adp.Specification.Fbl`; the module keeps the plugin that reads the selected files and the code neither language can state, each piece with its reason. Nothing a user sees changes unless this document names the change and the user has ruled on it.

## What was measured

Read on 2026-10-09: the module at `develop` `9a646009`; `definitions/diagrams/helm-chart.dis` and `.md`, `specifications/fbl/helm-chart.fbl`, the DISL specification (0.3 draft) and the FBL specification (0.2 draft) in etalii-adp/etalii.adp at `develop` `da64ff0`; and the two libraries in this repository.

| Thing | State |
| --- | --- |
| The module's backend | 3,399 lines of C# in 45 files. `HelmChartReader` (464 lines), `HelmYaml` (94) and `TemplateScan` (103) read the folder; `HelmGraph` (150) and `DependencyResolution` (58) make the nodes and relations; `HelmRuleSet` (232) holds the eleven findings and `HelmContextPropertyProvider` (387) the rows, as code. 17 test files, three fixture charts. |
| Does it write? | Never the chart. There is no writer, no command, no document factory, no toolbox provider and no context action provider in the module, `HelmContextPropertyProvider.SetAsync` refuses unconditionally, and `ZeroWrites.Tests.cs` compares every fixture file byte for byte after opening, browsing, validating and closing. The one write is a position, by core's `SetRegistrationLayoutCommand`. |
| The module's client | 567 lines of TypeScript beside its tests. `HelmCanvas.tsx` holds a hand-written canvas definition and draws open ends as stub elements. |
| The DISL definition | Exists in etalii.adp, 726 lines, **written against DISL 0.1**. Not bundled here (`src/diagrams/helm-chart/definition/` does not exist). Its `persistence.format` names a plugin directly. |
| The FBL binding | Exists in etalii.adp as an example, 36 lines, marked `"fbl": "0.1"` (which a 0.2 host reads unchanged), a folder body with a plugin reader and `readOnly`. Vendored byte-identical in `EtAlii.Adp.Specification.Fbl.Tests/Conformance/`. |
| The real-file check of the binding | `Registrations.Tests` recognises every chart folder of this repository through the binding and finds its `Chart.yaml` selected. Nothing reads a chart through it: `ModuleCrossCheck.Tests` has no Helm case and `RealFiles/divergences.json` has no Helm entry, because the library has nothing that reads a folder (finding B2). |
| A parity transcript | None. |

**How each finding below is known:** *recorded* is a test, a fixture or a sentence in the code that already holds it; *measured* is a search of this repository whose command is in *Sources*; *read* is a reading of the code or the definition against the specification text, not run. Requirement 1 turns every *read* finding into a fixture before anything is changed.

## Findings

The three kinds are those of `causal-loop-disl-fbl`: a **definition defect** is repaired in the definition or the binding, a **runtime gap** in this repository's libraries, a **language gap** in etalii-adp/etalii.adp.

### A. The DISL definition against the module

| # | Finding | Kind | Known |
| --- | --- | --- | --- |
| A1 | The definition is DISL 0.1 and carries its origin as `x-adp-origin`. The client's `parseDisl` refuses anything but 0.3, every bundled definition here is 0.3, and since 0.2 the origin is `language.origin`. | Definition defect | Read |
| A2 | Its companion lists as inexpressible eight things DISL 0.2 and 0.3 have since answered: the fixed ids `chart` and `crds` (`persistence.ids.types` with a `derived` expression, which DISL 11.5.2 names for "fixed singleton ids"); an open end (an `optional` target end, drawn as the edge notation's `stub`, whose own example is 48 units out of the right side, exactly the module's); a reason on every read-only row (`readOnlyReasons`); one finding per item (`forEach`); the tool's rule ids (`code`); where a finding points (`location`); values the panel phrases and rows that appear only when there is something to say (a form item's `display` and `visible`); and the sentence every write is refused with (`behavior.messages`, `std.readOnly`). The definition still works around each, for example by drawing an open end as a badge on its source. | Definition defect | Read |
| A3 | `persistence` names the plugin as its format, with `files`, `ordering`, `precision` and `newline`. With the binding it becomes `format: "fbl"` with `binding`, DISL 11.2 forbids those four keys beside it, and the plugin stays declared in `plugins` with `provides: ["persistenceFormat"]`. | Definition defect | Read |
| A4 | **Includes.** The definition draws one relation per pair of a template and a partial, labelled with every name. The module draws one per included name, to the first partial that defines it, labelled with that name, and the name is in the relation's id. | Definition defect | Read |
| A5 | **An open end cannot be derived.** An unvendored dependency and an include no local partial defines are relations without a target; the module gives each an id (`edge:` then source, kind, an empty target and the label), rows and a stub. DISL 4.9 lets a relation be **stored** without a target. A derived relation's `target` is an Expression that yields an `Element` (4.11.3), and an item whose ends do not satisfy the declaration is dropped. So `Resolves` and `Includes` cannot be derived by DISL while some of them are open, although their matching is two lines of CEL. `DislModelBuilder` holds the same: its `Allows` accepts only an element. | Language gap | Read |
| A6 | **The summary rows.** The definition puts the template, dependency and values-layer counts on the diagram, "with nothing selected". The module answers no rows without an element and shows the three counts on the Chart node, in a group `Summary`. | Definition defect | Read |
| A7 | **Findings the definition shapes differently.** It has three rules for an unparseable file where the module has one code, `helm.unreadable-yaml`, once per file; DISL's `std.unparseable` is that rule. It marks every dependency of a name collision where the module reports once, at the second declaration; `std.duplicateId` with `oncePerGroup` and a `code` is that rule. It reports a stale lock once with all names; the module reports once per entry. | Definition defect | Read |
| A8 | **The order of findings.** The module reports unreadable files in the order `Chart.yaml`, `requirements.yaml`, the values files, the lock, the CRDs. DISL orders reader findings in reading order, which for a folder is the ordinal order of relative paths (`Chart.lock` first). Under `constraints.order` both `helm.lock-drift` shapes share one code, and findings without a line come after those with one, where the module reports the declared-but-not-locked ones (no line) first. | Language gap, to settle by transcript | Read |
| A9 | **Legacy charts.** The definition says `info`; the module reports `helm.legacy-chart` as a warning worded as information, and the companion explains that the panel knows two severities. `DiagramProblemSeverity` has `Info` today. | Behaviour change to rule on (Q3) | Read |
| A10 | **Two dependencies with one effective name** get the same node id `dep:` plus that name today, and both are drawn under it. DISL 11.5.4 gives the id to the first; the second is drawn and treated as ephemeral, so it cannot keep a position. | Behaviour change to rule on (Q4) | Read; the companion records that no test covers the drawing |
| A11 | **Ids need a pattern.** The default id pattern admits neither `/` nor the pipe and at most 128 characters; `tpl:templates/_helpers.tpl` and every relation id fail it. The definition declares none. | Definition defect | Read |
| A12 | **No wire ids.** The module's eleven wire types (`helm/chart+chart` to `helm/chart+edge`, a partial travelling apart from a template) and 33 row ids (`helm.name` to `helm.open-end`) are what the client and the tests use. There are no toolbox and no action ids. | Language gap, already ruled: wire ids stay an `x-` block. This definition gets `x-helm`. Whether one type can map to two wire types by an attribute is the design's to confirm. | Read |
| A13 | **What the standalone DISL runtime lacks for this tool.** `ConstraintEvaluator` reads a rule's `code`, `forEach` and `constraints.order` and reads neither `location` nor `subject`; `DislCelLibrary` declares twelve methods and `location()` is not among them; an `FblElement` carries a line and no file, so a finding about an element of a folder subject cannot say which file. The client's `compileNotation` reads no `stub`. | Runtime gap | Measured |
| A14 | The companion says the whole diagram is delivered at open and "a viewport change brings nothing new". `HelmElementMapper.Visible` filters by the viewport with one hop and `HelmSession.UpdateView` sends the difference. | Definition defect (the companion) | Read |
| A15 | The banded layout, the element mapper and its `.proto` payload, the viewport filter, the store with its watcher, and reveal on activation stay code. This is not a gap: DISL names a layout as a plugin (D.2, open question 9) and has no streaming model on purpose. | None | Read |

**What is derivable from DISL once the plugin delivers the nodes:** every property row with its group, phrasing, visibility and reason; the read-only refusals; nine of the eleven findings as declared rules or built-ins with the tool's codes (the other two, `helm.unreadable-yaml` and `helm.not-a-chart`, are the reader's: findings A7 and B10); the derived attributes (`legacy`, a template's role from its path, undetermined kinds, the pinned version, resolved and declared); and the three relations that always have both ends, `Declares`, `Overrides` and `Configures`.

### B. The FBL binding against the module's reader

| # | Finding | Kind | Known |
| --- | --- | --- | --- |
| B1 | **The tool never writes its subject**, so the binding's `readOnly` is right, and it needs no rule, `insert`, `remove` or `template`. A read-only binding's plugin is never asked to `plan`. | None | Recorded (`ZeroWrites.Tests.cs`; `PluginBody.Tests`, *AReadOnlyBindingsPluginIsNeverAskedToPlan*) |
| B2 | **The library has no folder body.** `PluginBody` is "a file body read by a persistence plugin" and hands the plugin one `PluginFile` with an empty path; `FolderSubject` answers `Recognise` and `Files` and nothing reads, settles or watches; no host calls `IPersistencePlugin.Watch`. The one test that opens the Helm binding opens it over the bytes `label a`. The .NET dependency graph says the same of itself: its model is built "not through FBL", "where an FBL binding reads one file". | Runtime gap | Recorded (`DotNetDefinition.cs`, `PluginBody.cs`, `IPersistencePlugin.cs`) |
| B3 | **The file rules select less than the reader reads.** `values*.yaml` leaves out `values*.yml`, which the reader takes; `requirements.lock`, the lock of a legacy chart, is not listed although `requirements.yaml` is; nothing selects the files under `charts/*/templates/` that the reader counts. The reader also matches `values.yaml`, `NOTES.txt` and the YAML extensions ignoring case on every platform, where FBL's globs follow the file system. | Definition defect | Read |
| B4 | **A directory is not an entry.** A `crds/` folder with no file in it is a CRDs node with a count of zero; a directory under `charts/` with no `Chart.yaml` is a Subchart; and the deeper count is the number of entries, directories included, in a subchart's own `charts/`. File rules select files, and the plugin is handed files. The fixture `unconventional/charts/deep/charts/deeper/marker.txt` holds it: the reader counts one deeper entry, and no glob over files one level down sees a directory. | Language gap | Recorded (`HelmChartReader.Tests`, the deeper count of `deep` is 1) |
| B5 | **The plugin is handed bytes it must not need.** The contract's `read` receives "the relative path and bytes of every file the file rules select". The reader wants only the name of `values.schema.json` and of each `*.tgz` ("sealed", never unpacked) and only a count of a subchart's templates. A file rule cannot say that presence is all that is read, and FBL section 16 makes a body over the size limit unreadable, so a large vendored archive could cost the diagram. | Language gap | Read |
| B6 | **A reader is all or nothing.** "A binding with a plugin reader has no rules; its model is what the plugin reads." `Chart.yaml`, its `dependencies`, the lock's entries and a values file's top-level keys are plain YAML that element rules could bind; only `templates/` is not YAML. FBL section 17 names "a declared binding, with a plugin for part of its reading" as an outcome and gives no construct for it. The `family` on a file rule of a plugin-read folder has no stated effect either, since the plugin receives bytes. | Language gap | Read |
| B7 | **If the YAML files were declared, the timeline's gap returns.** The reader keeps every scalar as written text: `version: 1.0` is `1.0` and fails the SemVer rule with that spelling, `appVersion: 1.16` stays `1.16`. `deprecated` and a condition's state are read with .NET's `bool.TryParse`, so a quoted `"true"` and `True` count and `yes` does not. FBL's YAML reading types plain scalars by the core schema (`timeline-disl-fbl`, finding B1). A file with a repeated key is unreadable to YamlDotNet and `fbl.duplicate-key` to FBL. | Language gap | Read |
| B8 | **It is not the Go templating that forces a plugin.** `TemplateScan` never interprets a template: it is three regular expressions and a column-zero test over lines, which the `lines` family can state. What FBL cannot state is around it: a file that matches nothing must still be a node, and a file is not an entry; a line with two `include` actions yields two references, and a statement is one entry; a template's role comes from its file's name and place, and a rule has no access to the path of the file it reads. FBL 11.5 gives "includes resolved" as the reason, and resolving them is a CEL filter (finding A5 is what stops that). The module also splits on LF only, where FBL 2.6 ends a line at a lone CR as well. | Language gap | Read |
| B9 | **The contract asks for a determinism this plugin cannot prove.** `read` "MUST be deterministic ... in every host's implementation of the plugin". The message of `helm.unreadable-yaml` is YamlDotNet's own sentence and line, by design ("the parser's own words"), and a host with another YAML library words it differently. No fixture can pin the plugin either: a round-trip fixture's `input` is one file beside it, so a folder cannot be an input, and the plugin conformance class says only "meets section 11.4". | Language gap | Read |
| B10 | **A registered folder that is not a chart.** The module reports exactly one warning, `helm.not-a-chart`, at the folder, and the canvas shows an empty state. FBL 10.3 makes a folder that fails `recognise` an unreadable body, reported as `std.unparseable`, an error by default. A built-in's `code` may be computed in 0.3 and its `severity` may not, so one built-in cannot be the error `helm.unreadable-yaml` for a file and the warning `helm.not-a-chart` for the folder. | Language gap, or a behaviour change to rule on (Q3) | Read |
| B11 | **Where a finding points.** `helm.version-not-semver` points at the line of `version`, not at the chart's entry. The plugin delivers the span "of the entry and of every writable value", a read-only binding has no writable value, and DISL's `location()` takes no attribute. `helm.lock-drift` for a declared dependency points at the lock file with no line. | Language gap for the first; the second is a rule's `location` (A13) | Read |
| B12 | **Watching.** The module watches every file under the chart root and settles for 400 ms; the binding's `settle` says the same and FBL watches only what the file rules read. The module also relies on that watcher to redraw after a position is written: the registration lies in the watched folder, and "there is no second watcher". Under FBL the registration is no file rule's file. | Behaviour to hold in Requirement 4 | Read |
| B13 | **The registration.** The module's `layout:` block is what FBL 8.3 describes: one entry per dragged element, ids in ordinal order, three decimals. A stored id the folder no longer has is ignored and dropped at the next write; FBL also reports it, as `fbl.stale-view-data`. No module here writes a registration through the library's `OpenRegistration`. | Behaviour change to rule on (Q4) | Read; measured for the last sentence |
| B14 | **Routing.** The binding claims the name `Chart.yaml`, is `registrationOnly`, and suggests itself when a body contains `apiVersion:`. `suggest.contains` is defined over "a body's first 64 KiB", which a folder has not; the tool suggests itself on a folder that holds `Chart.yaml`. `createOnFirstPlacement` says a registration is created on the first placement, and a folder diagram does not exist without one. Its `readOnly` sentence is not the one the module refuses with. | Definition defect; the first is a language gap | Read |

### C. Language gaps, as they would be reported to etalii-adp/etalii.adp

1. **FBL: a mixed reader for a folder subject** (B6). Rules for the file rules that are a declared family, a plugin for the others, one model. Section 17 already names the outcome.
2. **FBL: a file as an entry, and the file's path in a rule** (B8). Candidate: a file rule with `element`, producing one element per selected file with its relative path as an attribute, and `file` in the CEL context of every rule that reads it.
3. **FBL: folder entries that are directories, and presence without bytes** (B4, B5). Candidate: `entries: "files"` or `"all"` on a file rule, and `read: "path"` beside the default of bytes.
4. **FBL: a folder as a fixture input, and a plugin's conformance** (B9). What may a plugin's finding say that another host's implementation must repeat?
5. **FBL: several entries from one statement** (B8), and **a YAML scalar as written** (B7, the timeline's first gap).
6. **FBL: `suggest` for a folder body** (B14).
7. **DISL: a derived relation without a target** (A5). Candidate: `target` as an Expression that may yield null where the type's target end is `optional`.
8. **DISL: the source location of one attribute** (B11), and **a computed severity on a built-in** (B10).
9. **DISL: the order of reader findings across the files of a folder, and of two shapes of one code** (A8), if the transcript shows the tool's order cannot be declared.

These are candidates. Requirement 8 says they are recorded with their evidence and reported, and never worked around silently.

## Open questions

Each is put to the user as a selection. The option marked **(default)** is what this document is written against.

| # | Question | Options | Criteria affected |
| --- | --- | --- | --- |
| Q1 | How much of the reading leaves the plugin now? | **(default) None of it**: the whole folder stays one plugin read, as FBL 11.5 expects, and gaps 1 to 3 and 5 are reported; nothing is built on a construct FBL does not have. · **The YAML files are declared in a second binding** read beside the plugin by module code that merges the two models: more is declared, and the merge is new code no language describes. · Other. | 3.2, 6.1 |
| Q2 | Who makes `Resolves` and `Includes`, which may have an open end (finding A5)? | **(default) The plugin delivers both whole**, as stored relations with an `optional` target; the three relations that always have both ends are derived by DISL; the matching stays plugin code, named as standing in for gap 7. · **DISL derives all five and the open ends are a node badge**, as the 0.1 draft has it: an open end then has no id, no rows and cannot be selected, which is a change users see. · Other. | 2.3, 3.4 |
| Q3 | Severities the languages would change: the legacy-chart finding as `info`, the not-a-chart finding as the reader's error. | **(default) Keep today's**: both stay warnings, with today's sentences and codes; the legacy rule is declared `warning`, and `helm.not-a-chart` stays a module finding until gap 8 is answered. · **Adopt the languages'**: legacy becomes information and a non-chart an error, each named in the transcript's diff. · Other. | 5.2, 5.4 |
| Q4 | Findings and treatment the languages would add: `fbl.stale-view-data` for a position whose file was renamed, and the second of two same-named dependencies becoming an element that cannot keep a position. | **(default) Switched off in this conversion**: no finding the tool does not give today appears, and both dependencies stay drawn as today; each is raised afterwards as a small change of its own. · **Adopted now**, each named in the transcript's diff. · Other. | 5.3, 2.5 |
| Q5 | Is the client's canvas definition in scope? | **(default) Yes**: it is compiled from the bundled definition with `compileNotation`, as the .NET dependency graph's read-only notation is, with today's hand-written definition kept in a test as the oracle and the `stub` added to the shared library. · **No**: backend only. · Other. | 7 |

## Alignment with Product Vision

The product direction (2026-09-26) is that every tool is a markup definition interpreted by a core in each IDE, with code only where a definition cannot reach. Three tools here are on both languages and each has one body file. This is the first whose subject is a folder and whose reader FBL itself expects to stay a plugin, so it tests the half of FBL no tool has used: recognition, file rules, the settle delay and the plugin contract over many files. It follows `structure.md`'s rule that a diagram type adds declarations and no core code: the folder body the library lacks (finding B2) is added to the library, for the Ansible structure and .NET dependency graph tools as much as for this one.

## Requirements

### Requirement 1: An oracle before any change

**User Story:** As the owner of the tool, I want what it answers today frozen before anything is converted, so that every later step is held to it.

#### Acceptance Criteria

1. WHEN the work starts THEN a parity transcript `Parity/helm-chart.transcript.json` SHALL be generated from today's hand-written code and committed before the reader, the rule set or the property provider is changed: per chart folder, the nodes and relations with their ids and wire types in delivery order, the payload of every element, the property rows of every node and every relation and of an unknown id, the findings with code, severity, sentence, file and line in order, the computed boxes, and the answers to a move of a node, of a relation and of an unknown id.
2. The corpus SHALL be the three fixture charts, the three examples and their copies under `src/examples/diagrams/helm-chart/`, and **one new fixture chart for each finding of tables A and B marked Read that concerns a folder's content** (A4, A5, A7, A8, A10, B3, B4, B7, B8, B10, B11), written so that the finding either shows in the transcript or is struck from this document with the reason.
3. A test SHALL require today's code to reproduce the checked-in transcript byte for byte, and SHALL have been seen to fail against a deliberately changed sentence before it is trusted.
4. WHEN a later step changes the transcript THEN the change SHALL be one this document names (Q3, Q4), regenerated on purpose, and the diff of the checked-in file SHALL be the review of it. Any other difference is a regression.
5. The transcript SHALL contain no edit sequence over chart files, and `ZeroWrites.Tests.cs` SHALL stay and pass unchanged through every step.

### Requirement 2: The definition, at DISL 0.3, bundled

**User Story:** As a tool engineer, I want one definition of the Helm chart diagram that every host runs.

#### Acceptance Criteria

1. `definitions/diagrams/helm-chart.dis` in etalii-adp/etalii.adp SHALL be rewritten at DISL 0.3 with `language.origin`, using the constructs finding A2 names in place of each workaround, and SHALL validate against the DISL schema in that repository's checks.
2. It SHALL declare `persistence.format` `fbl` with `binding` naming the module's binding (Requirement 3.1), an id strategy that yields today's ids with a `pattern` that admits them (findings A2, A11), and an `x-helm` block mapping its types and form items to the wire types and row ids the module uses today (finding A12).
3. `Declares`, `Overrides` and `Configures` SHALL be derived relations with today's ids and labels; `Resolves` and `Includes` SHALL have an `optional` target end and a `stub` of 48 units out of the right side (Q2), and `Includes` SHALL be one relation per included name (finding A4).
4. Its companion `helm-chart.md` SHALL be rewritten to say what DISL 0.3 and FBL 0.2 cannot express, which is section C and whatever Requirement 8 adds, and nothing that they can; the sentence finding A14 corrects SHALL be corrected.
5. The definition SHALL be bundled into `src/diagrams/helm-chart/definition/` by `src/diagrams/tools/bundle-disl.sh`, never edited here, embedded in the backend project, held by a `BundledDefinition.Tests` like the other bundled modules', and SHALL load through `EtAlii.Adp.Specification.Disl` with no diagnostic of severity error.

### Requirement 3: The folder is read under the binding, by a plugin

**User Story:** As a user, I want every chart that opens today to open the same, read through a contract every host can implement.

#### Acceptance Criteria

1. The module SHALL own its binding, `backend/EtAlii.Adp.Diagram.HelmChart/helm-chart.fbl`, embedded in the backend project, with the file rules of finding B3 repaired. The example in etalii.adp SHALL be brought in line with it or the difference recorded (Requirement 8).
2. The module SHALL implement `IPersistencePlugin` with the id `net.etalii.adp.helm.chartFolder`. Its `Read` SHALL work from the files the host hands it and SHALL NOT touch the file system; `Plan` SHALL never be reached; `Template` SHALL not be asked for, because a chart is never created here.
3. WHEN a folder is read THEN the nodes, their attributes and their ids SHALL be those `HelmChartReader` and `HelmGraph` give today, for every chart of the corpus of Requirement 1.2.
4. `Resolves` and `Includes` SHALL be delivered by the plugin with today's ids, and an open end SHALL be a relation without a target (Q2).
5. WHERE the contract cannot hand the plugin what the reader reads today (a directory with no file in it, a count of entries that includes directories: finding B4) THEN the module SHALL keep today's answer by a stated stand-in, named in the companion document as standing in for gap 3, and SHALL NOT change the answer to fit the contract.
6. Reading SHALL never throw on content. A file that does not parse SHALL cost that file's detail and nothing else, as today.

### Requirement 4: The library's folder body

**User Story:** As a maintainer of the shared libraries, I want a folder subject opened once in the library, for every tool that has one.

#### Acceptance Criteria

1. `EtAlii.Adp.Specification.Fbl` SHALL gain a folder body: it recognises a folder by the binding's `recognise`, selects files by its file rules and `ignore`, in ordinal order of relative paths, never through a reparse point, and hands them to the binding's plugin in one `read` (FBL 10.2, 10.3, 11.2).
2. An element and a finding the plugin delivers SHALL carry the file they are in, relative to the folder, so that a finding of a folder subject has a file and a line (finding A13).
3. A folder body with a `readOnly` binding SHALL refuse every model change with the binding's reason and SHALL never ask the plugin to plan.
4. The library SHALL say which paths a reading depends on (the files read, the folders the globs could add files to, and what the plugin's `watch` adds) and SHALL read again once after no change has arrived for `settle` milliseconds, however many files changed. The watching itself stays the host's.
5. WHEN a position is written to the registration THEN every open reading of that folder SHALL show it without a reload, although the registration is no file rule's file (finding B12).
6. Each of these SHALL be proven by a library test seen to fail first, over a folder on disk, and no module name SHALL appear in the library change.

### Requirement 5: Rows, findings and refusals are derived

**User Story:** As a maintainer, I want the rows, the findings and the refusals to come from the definition, so that changing the tool is changing one file.

#### Acceptance Criteria

1. The property rows of every node and every relation SHALL be derived from the bundled definition through `FormDerivation`, mapped to the row ids of `x-helm`: the labels, the groups Identity, Contents, Wiring and Summary, the phrased values ("override layer, stacked on values.yaml", "undetermined - the kind is templated, and templates are never rendered here"), the rows that appear only when there is something to say, and on every row the reason that names its file, `Defined in {file}; edit it in a text editor.`
2. The findings SHALL be derived by `ConstraintEvaluator` with the tool's eleven codes, severities, sentences, files, lines and order (Q3). WHERE the order of finding A8 cannot be declared THEN the module SHALL order the derived findings by stated code, and that code SHALL be named in the companion as standing in for gap 9.
3. No finding the tool does not give today SHALL appear (Q4), and a file reported unreadable SHALL have no other finding located in it, as today.
4. `helm.not-a-chart` SHALL stay exactly one warning at the folder with today's sentence, and the canvas SHALL show today's empty state (Q3, finding B10).
5. A write to any row SHALL be refused with today's sentence, read from the definition's `std.readOnly`; a move of a relation and a move under a parent SHALL be refused with today's sentences.
6. WHEN the derived answers are compared with the transcript of Requirement 1 THEN the rows, findings, elements and payloads SHALL be byte-identical.
7. `EtAlii.Adp.Specification.Disl` SHALL read a rule's `location` and `subject` and implement `location()` (finding A13), each proven by a library test seen to fail first.
8. The hand-written logic SHALL be kept in the test project as the oracle (`Parity/HandWrittenHelm.cs`) and removed from the product code.

### Requirement 6: What stays code, and why

**User Story:** As a maintainer, I want the code that remains in the module to be exactly what the languages cannot state, each piece with its reason.

#### Acceptance Criteria

1. The module SHALL keep: the plugin, with the reading of chart-owned YAML, the template scan, the condition walk and the matching of the two relations of Q2 (Q1); the banded layout; the element mapper with its payload and the viewport filter; the session; and the store, reduced to holding one reading per folder for its viewers.
2. `HelmRuleSet`, the logic of `HelmContextPropertyProvider`, and the parts of `HelmGraph` that make the three derived relations SHALL leave the product code. `HelmWatchedFolder` SHALL leave it when Requirement 4.4 covers what it does.
3. WHEN the conversion is complete THEN the companion document SHALL list every remaining module class beside the gap or the host concern that keeps it, and a class with neither SHALL be removed.

### Requirement 7: The client

**User Story:** As a user, I want the canvas to look and behave as it does, drawn from the same definition the backend runs.

#### Acceptance Criteria

1. The client's canvas definition SHALL be compiled from the bundled definition with `parseDisl` and `compileNotation` (Q5), with today's hand-written definition kept in a test as the oracle and the compiled one drawing the same markup for the corpus.
2. IF `compileNotation` cannot read something the definition states (the stub of a relation without a target, the forward Bézier from a source's right side into a target's left, a dashed outline that overrides a kind's outline) THEN it SHALL be added to the shared library for every module, named for its geometry.
3. A `tests.md` entry SHALL cover in a real browser, in both themes: each of the nine node kinds, an unreadable node, both open ends with their captions, the five relation styles, the not-a-chart empty state, the toolbox saying it contributes nothing, double-click revealing a file, dragging a node with its undo, and a relation refusing to move.

### Requirement 8: Gaps are recorded and reported, never tuned away

**User Story:** As the owner of DISL and FBL, I want every place the languages fall short reported back with its evidence.

#### Acceptance Criteria

1. Every language gap found, the nine of section C and any the implementation adds, SHALL be recorded in the companion document with the construct concerned, the fixture or test that shows it, and what would resolve it.
2. A gap SHALL NOT be closed by weakening a check, skipping a fixture, or stating in the definition or the binding something the tool does not do.
3. The delivery report SHALL list every gap as a candidate change for etalii-adp/etalii.adp, and every definition defect that was repaired.
4. WHEN a *read* finding of this document turns out not to occur THEN it SHALL be struck here with the fixture that shows it, as a small amendment.
5. A cross-check SHALL be added to `ModuleCrossCheck.Tests` that reads every chart folder of this repository through the binding and the plugin and compares ids per type with today's reader, and any disagreement SHALL be an entry of `RealFiles/divergences.json` and never a silenced test.

### Requirement 9: Documentation, catalog and gates

**User Story:** As a reader of the repository, I want its pages to stay true through the conversion.

#### Acceptance Criteria

1. `src/diagrams/readme.md`'s list of modules with a `definition/` folder, `docs/creating-a-diagram-module.md` where it describes a folder subject, `docs/diagram-module-client-api.md`'s list of modules that compile their notation, and `docs/architecture.md` and `docs/solution-structure.md` where a sentence becomes false SHALL be updated in the change that makes them so.
2. `docs/tools.md`'s row for `helm/chart` SHALL name this specification.
3. All four gates SHALL exit zero on every pull request of this specification, judged by captured exit codes, and a newly created worktree SHALL be built before a green gate is trusted about the bundled definition.
4. WHEN the tasks document is written THEN every acceptance criterion here SHALL be claimed by a task, established by diffing the two sets, and two traces SHALL be read back to their artefacts.

## Non-Functional Requirements

### Code Architecture and Modularity

- The module ends as its definition, its binding, its plugin and the code Requirement 6.1 names. Library additions are named for what they do, never for this tool.
- The house rules apply: `src/.editorconfig`, no blocking call in an `async` method, `TestContext.Current.CancellationToken` in tests, no unneeded `using`, CRLF in the working tree. The fixture charts stay `-text` by their local `.gitattributes`, and every new fixture chart goes under it.

### Performance

- Opening and validating the largest chart of the corpus SHALL not be perceptibly slower than today. A burst of changes SHALL still become one read. Validation SHALL stay a one-shot read that leaves no watcher behind.

### Reliability

- No file of a chart is ever written, created or deleted (Requirement 1.5). No chart that opens today opens differently (Requirement 3.3). No stored position is lost. A symbolic link is never followed.
- Every test written for a finding is seen to fail against that finding before it is trusted.

### Usability

- Every sentence a user reads today is read unchanged (Requirement 5), unless this document names the change.

## Out of scope

- Any change to what the tool does beyond the five questions above.
- Changing DISL or FBL. Gaps are reported (Requirement 8); a language change is etalii.adp's own specification.
- Writing chart content, rendering templates, evaluating a version range or a values schema: the tool does none and this conversion adds none.
- How a position is written: it stays core's `SetRegistrationLayoutCommand`, as for every module.
- The other tools of the series. What this one adds to the libraries (Requirements 4 and 5.7) is theirs to use.

## Appendix: the binding, as drafted

A draft against FBL 0.2, to be settled in the design. It differs from the example in etalii.adp where findings B3 and B14 say so. Three things in it are **not valid FBL 0.2** and mark where a gap is: `"entries": "all"` and `"read": "path"` stand for gap 3 (findings B4 and B5), and `suggest.entries` stands for gap 6 (finding B14). Until they are answered the binding ships without those three keys and Requirement 3.5 covers the difference.

```json
{
  "fbl": "0.2",
  "bindings": {
    "chart": {
      "title": "Helm chart",
      "claims": {
        "names": ["Chart.yaml"],
        "origins": ["helm/chart"],
        "registrationOnly": true,
        "suggest": { "entries": ["Chart.yaml"] }
      },
      "body": {
        "kind": "folder",
        "recognise": { "all": ["Chart.yaml"] },
        "files": [
          { "name": "chart", "glob": "Chart.yaml", "family": "yaml" },
          { "name": "values", "glob": "values*.yaml", "family": "yaml" },
          { "name": "valuesYml", "glob": "values*.yml", "family": "yaml" },
          { "name": "schema", "glob": "values.schema.json", "read": "path" },
          { "name": "templates", "glob": "templates/**", "family": "lines" },
          { "name": "crds", "glob": "crds/**", "family": "yaml", "entries": "all" },
          { "name": "subcharts", "glob": "charts/*/Chart.yaml", "family": "yaml" },
          { "name": "subchartTemplates", "glob": "charts/*/templates/**", "read": "path" },
          { "name": "deeper", "glob": "charts/*/charts/*", "read": "path", "entries": "all" },
          { "name": "vendored", "glob": "charts/*", "read": "path", "entries": "all" },
          { "name": "lock", "glob": "Chart.lock", "family": "yaml" },
          { "name": "requirements", "glob": "requirements.yaml", "family": "yaml" },
          { "name": "legacyLock", "glob": "requirements.lock", "family": "yaml" }
        ],
        "ignore": [".git/**"],
        "settle": 400
      },
      "reader": { "plugin": "net.etalii.adp.helm.chartFolder", "version": "^0.1.0" },
      "readOnly": "A helm chart diagram shows the folder as it is and changes nothing in it. Edit the file this value comes from in a text editor."
    }
  }
}
```

The first matching file rule takes a file, so `vendored` stands after the three rules that begin with `charts/`. The example's `**/.helmignore` is left out of `ignore` because no file rule selects such a file except under `templates/`, where the reader draws it today. There is no `template`, no `elements` and no `registration` block: nothing is created, a plugin reader has no rules, and a folder diagram exists only through its registration.

## Appendix: the definition's persistence and wire ids, as drafted

The rest of the definition is the existing file brought to 0.3 (Requirement 2.1). These blocks are new; the row ids shown are a sample of the 33.

```json
{
  "persistence": {
    "format": "fbl",
    "binding": "https://raw.githubusercontent.com/etalii-adp/etalii.adp.ide.standalone/develop/src/diagrams/helm-chart/backend/EtAlii.Adp.Diagram.HelmChart/helm-chart.fbl#chart",
    "ids": {
      "strategy": "derived", "expression": "self.type + ':' + self.path", "pattern": "^.+$",
      "types": {
        "Chart": { "strategy": "derived", "expression": "'chart'" },
        "Crds": { "strategy": "derived", "expression": "'crds'" },
        "Values": { "strategy": "derived", "expression": "'values:' + self.path" },
        "Schema": { "strategy": "derived", "expression": "'schema:' + self.path" },
        "Template": { "strategy": "derived", "expression": "'tpl:' + self.path" },
        "Dependency": { "strategy": "derived", "expression": "'dep:' + self.name" },
        "Subchart": { "strategy": "derived", "expression": "'sub:' + self.path" },
        "Archive": { "strategy": "derived", "expression": "'tgz:' + self.path" },
        "Lock": { "strategy": "derived", "expression": "'lock:' + self.path" },
        "Resolves": { "strategy": "derived", "expression": "'edge:' + self.source.id + '|Resolves|' + (self.target == null ? '' : self.target.id) + '|' + self.label" },
        "Includes": { "strategy": "derived", "expression": "'edge:' + self.source.id + '|Includes|' + (self.target == null ? '' : self.target.id) + '|' + self.label" }
      }
    },
    "view": { "store": ["bounds"], "styleOverrides": "none" }
  },
  "behavior": {
    "messages": {
      "std.readOnly": "A helm chart diagram shows the folder as it is and changes nothing in it. Edit the file this value comes from in a text editor."
    }
  },
  "plugins": {
    "net.etalii.adp.helm.chartFolder": { "version": "^0.1.0", "provides": ["persistenceFormat"], "required": true, "plans": [] }
  },
  "x-helm": {
    "typePrefix": "helm/chart+",
    "types": {
      "Chart": "chart", "Values": "values", "Schema": "schema", "Template": "template", "Crds": "crds",
      "Dependency": "dependency", "Subchart": "subchart", "Archive": "archive", "Lock": "lock",
      "Declares": "edge", "Resolves": "edge", "Overrides": "edge", "Configures": "edge", "Includes": "edge"
    },
    "typeWhen": { "Template": { "partial": "self.role == 'partial'" } },
    "properties": {
      "name": "helm.name", "version": "helm.version", "appVersion": "helm.app-version", "apiVersion": "helm.api-version",
      "path": "helm.path", "role": "helm.role", "renders": "helm.kinds", "references": "helm.includes",
      "constraint": "helm.constraint", "condition": "helm.condition", "pinnedVersion": "helm.pinned",
      "resolution": "helm.resolution", "openEnd": "helm.open-end"
    }
  }
}
```

Two things in it are the design's to close. `typeWhen` is not a key `WireIdMap` reads today: it marks that a partial travels as `helm/chart+partial` while it is a `Template` in the model (finding A12). And the three derived relations take their ids from their `derived.id`, written in the same shape as the two above, so that every relation id stays `edge:`, the source, the kind, the target and the label, joined by a pipe.

## Sources

- The module `src/diagrams/helm-chart/` at `develop` `9a646009`: `HelmChartReader.cs`, `HelmYaml.cs`, `TemplateScan.cs`, `HelmGraph.cs`, `DependencyResolution.cs`, `HelmRuleSet.cs`, `HelmRules.cs`, `HelmValidator.cs`, `HelmContextPropertyProvider.cs`, `HelmContextSourceResolver.cs`, `HelmElementMapper.cs`, `HelmSession.cs`, `HelmChartStore.cs`, `HelmWatchedFolder.cs`, `Diagram.cs`, `client/HelmCanvas.tsx`, `Fixtures/readme.md`, `ZeroWrites.Tests.cs`, and the deeper-count assertions of `HelmChartReader.Tests.cs`. Line and file counts are from `git ls-files src/diagrams/helm-chart`; the absence of a writer, a command, a document factory and a toolbox or action provider is that listing.
- etalii-adp/etalii.adp at `develop` `da64ff0`: `definitions/diagrams/helm-chart.dis` and `.md`; `specifications/fbl/helm-chart.fbl` and `registrations/chart.adp`; `specifications/disl/DISL-specification.md` (0.3 draft: sections 3.2, 4.9, 4.11.3, 4.11.5, 6.10 *Stubs*, 7.5, 8.1, 8.2, 8.6, 8.7, 11.2, 11.5, 12.2, 13.1, D.2, *Changes from 0.1*, *Changes from 0.2*); `specifications/fbl/FBL-specification.md` (0.2 draft: sections 2.6, 3.1 to 3.4, 4.3, 4.6, 5.1, 7.4, 7.5, 8.1 to 8.5, 10, 11, 12.1, 15.3, 16, 17).
- `src/backend/EtAlii.Adp.Specification.Fbl/`: `Plugins/PluginBody.cs`, `Plugins/IPersistencePlugin.cs`, `Plugins/_Model/`, `Routing/FolderSubject.cs`, `FblModel.cs` and `Finding.cs` for findings B2 and A13. Searched for `settle` and `Watch(` across the project: one result, the interface's declaration. Searched `src/` for `FolderSubject.`, `PluginBody.Open` and `OpenRegistration` outside the library: tests, and the mind map and agent behavior modelling modules for `PluginBody.Open` only.
- `src/backend/EtAlii.Adp.Specification.Disl/`: `Expressions/DislCelLibrary.cs` (the twelve methods), `Model/DislModelBuilder.cs` (`DerivedRelations`, `Allows`), `Derivation/ConstraintEvaluator.cs` and `FormDerivation.cs`. Searched the project for `location`: one result, in `DislExpressionWalker.cs`. Searched `client/src/canvas/library/disl/compileNotation.ts` for `stub`: no result.
- `src/backend/EtAlii.Adp.Specification.Fbl.Tests/`: `Conformance/helm-chart.fbl` (the same blob as the example in etalii.adp, by `git hash-object`), `RealFiles/Registrations.Tests.cs`, `Routing/Routing.Tests.cs`, `Plugins/PluginBody.Tests.cs`, and `RealFiles/divergences.json`, searched for `helm` and `folder` with no result.
- The .NET dependency graph module as the nearest pattern, a read-only tool on DISL with its own reader: `DotNetDefinition.cs`, and `definition/dotnet-dependency-graph.dis` for `x-dotnet` and `std.readOnly`. The hype cycle graph module for the parity transcript and `BundledDefinition.Tests`.
- `causal-loop-disl-fbl` for the series, the survey and the shape of this document; `timeline-disl-fbl`, finding B1, for the typed YAML scalar.
