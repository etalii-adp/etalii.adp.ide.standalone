# Requirements Document

## Introduction

The user's request, verbatim (2026-10-09):

> Create spec workflow MCP specifications. One specification for converting one of the standalone diagram tools to FBL and DISL. i.e. analyse the current implementation, define the DISL and FBL and then write the specification on the refactoring., also focus on finding issues and aspects where the DISL and FBL languages do not yet cover everything.

This is the specification, of the fifteen that reading gives, for the **Azure DevOps pipeline** diagram (`azure-devops/pipeline`, `.yml`, module folder `azure-devops-pipeline`). The series and the survey behind it are in `causal-loop-disl-fbl`, *Introduction*, and are not repeated here.

**What "converted" means here** is what it means for the hype cycle graph: the toolbox, context menus, property rows, findings and refusals are derived from a bundled DISL definition by `EtAlii.Adp.Specification.Disl`, the body is read and written through an FBL binding by `EtAlii.Adp.Specification.Fbl`, and the module keeps code only for what neither language can state. Nothing a user sees or a file holds changes unless this document names the change and the user has ruled on it.

**What sets this tool apart** is that it has no format of its own. The body is a pipeline file that Azure DevOps owns, runs, and defines the schema of: ADP reads a part of it, writes three keys of it, and must leave every other byte alone. It is also the one tool that FBL names twice in its own text as a mixed case ("A declared binding, with a plugin for part of its reading", FBL 17) while FBL 0.2 has no construct for a mixed reader. That is finding B2, and with B1, B3 and B5 it decides how far this conversion can go today.

## What was measured

Read on 2026-10-09: the module at `develop` `9a646009`; `definitions/diagrams/azure-devops-pipeline.dis` and `.md`, the DISL specification (0.3 draft), the FBL specification (0.2 draft) and `fbl.schema.json` in etalii-adp/etalii.adp at `develop` `da64ff0`; and the two libraries in this repository.

| Thing | State |
| --- | --- |
| The module's backend | 6,322 lines of C# in 64 files. `PipelineParser` (747 lines) reads with YamlDotNet and follows templates through `PipelineTemplates` (154); `PipelineWriter` (419) replaces, inserts and removes whole lines; `PipelineBlocks` (125) holds the text of a new stage, job and step; `PipelineRuleSet` (234) and `PipelineValidator` (56) hold the findings; `PipelineContextActionProvider` (392), `PipelineContextPropertyProvider` (471) and `PipelineToolboxProvider` (55) hold the menus, rows and palette as code. 23 test files, 15 fixture documents. |
| The module's client | 733 lines of TypeScript outside tests. `PipelineCanvas.tsx` holds a hand-written canvas definition. |
| The DISL definition | Exists in etalii.adp, 912 lines, **written against DISL 0.1**. It is not bundled here (no `definition/` folder). It declares `persistence.format` as the plugin `net.etalii.adp.azure-devops.pipelineYaml`. |
| The FBL binding | None, in either repository. The appendix drafts one in the `yaml` family. |
| The real-file check | None. `RealFiles/RealFileCorpus.cs` has no entry for this tool and `divergences.json` records nothing about it; its only `pipeline` entries are the Databricks pipeline's. |
| A parity transcript | None. |
| Registrations | Eleven `.adp` files in `examples/`, each one line, `azure-devops/pipeline`. No `layout:` block exists anywhere, because nothing about the drawing is stored and dragging is switched off. |

**How each finding below is known** is marked, because none of them was run: *read* is a reading of the code or the definition against the specification text; *recorded* is a sentence in the code or in the definition's companion that a reviewer already holds; *measured* is a search whose command is in *Sources*. Requirement 1 turns every *read* finding into a fixture before anything is changed.

## Findings

The three kinds are those of `causal-loop-disl-fbl`: a **definition defect** is repaired in the definition or the binding, a **runtime gap** in this repository's libraries, a **language gap** in etalii-adp/etalii.adp. A fourth kind appears here twice: a **module defect**, which the conversion would repair by accident and which is therefore repaired on purpose first.

### A. The DISL definition against the module

| # | Finding | Kind | Known |
| --- | --- | --- | --- |
| A1 | The definition is DISL 0.1. The client's `parseDisl` refuses anything but 0.3 and every bundled definition here is 0.3. It must be rewritten at 0.3 before it can be bundled. | Definition defect | Read |
| A2 | Its companion lists twelve things DISL "cannot say" that DISL 0.2, DISL 0.3 or FBL have since answered: a label that follows state, "Show jobs" and "Hide jobs" (a CEL Message); Move up and Move down (`moveUp`, `moveDown`, the `reorder` action, `std.atStart`); a reason on each read-only row (`readOnlyReasons`); a refusal sentence from an operation (Reason lists); what a toolbox entry is dropped on (`drop.targets`); one finding per cycle naming its members in order (`diagram.cycles` with `forEach`); open and closed state that is never stored and never undone (viewer state, DISL 11.6, and the `view` action); badges that take the next free slot (`badgeLayout`); the content of a new document (FBL 13); a shared extension and an opt-in registration (FBL 12.1 `shared` and `registrationOnly`, `language.origin`); a change made outside the editor (FBL 7.3); and a body that does not parse (`std.unparseable`, FBL 7.5). The definition still works around each. | Definition defect | Read |
| A3 | `persistence` names a plugin and carries `encoding`, `mediaType` and `omitDefaults`. With a binding it becomes `format: "fbl"` with `binding` and a `typeMap`, and DISL 11.2 forbids those three keys beside it. | Definition defect | Read |
| A4 | The definition gives no wire ids. The module's 12 action ids, 17 property ids, 4 toolbox ids and 7 finding codes are what the client and the tests use. | Language gap, already ruled: wire ids stay an `x-` block (Peter, 2026-10-06, "Spec all but wire ids"). This definition gets an `x-azure-pipeline` block. | Read |
| A5 | The draft calls `inCycle` three times and `reachable` once, and the rewrite needs `diagram.cycles`. `DislCelLibrary` declares twelve element and diagram methods and refuses a specification that calls any other; none of the three is among them. | Runtime gap | Measured |
| A6 | **A dependency that names nothing is drawn.** The module draws it as a broken edge with the id `edge:?<name>-><to>`, in the danger colour, and marks the waiting stage indeterminate. DISL 11.2 keeps such a relation under `typeMap` and says "it is not drawn (6.1)". | Language gap, and a behaviour change to rule on (Q4) | Read |
| A7 | The companion says a deployment job's steps are gathered per lifecycle hook, `on.failure` and `on.success` included. `ReadJobSteps` reads a hook only when the hook's own mapping holds `steps`, so the steps under `on:` are passed over. No fixture and no test holds an `on:` hook. | Definition defect (the companion says more than the tool does) | Read; the absence is measured |
| A8 | Ids are paths that are never stored: a stage is its name, or `stage-<n>` when it has none; a job `<stage>/<name>`; a step `<job>/step-<n>`. Two stages of one name therefore share an id, and `PipelineEdits.Locate` answers every gesture with the first. The module reports nothing; DISL's `std.duplicateId` would. | Behaviour change to rule on (Q3) | Read |
| A9 | **Template references can share an id.** A reference is numbered `template-<n>` by the count of references its own parser has seen, and a followed template is read by a parser of its own that starts at zero, after which its references are appended to the caller's. A template that itself names a template gives two `template-0`, and the finding `azure-pipeline.template-not-followed` and the unresolved node are both located by `template:<id>`. No test names a template id. | Module defect, to fix on purpose with its own guard | Read; the absence is measured |
| A10 | The arrangement is DISL's `layered` by name only. The column rule (the longest chain before a stage, ignoring broken edges and the edge that closes a cycle), the open stage that grows to its jobs, and unresolved templates placed below at the x of what names them are in `PipelineLayout`. DISL 0.3 defines `rowPacked`, `rows` and `tidyTree` and still only names `layered`. | Language gap (DISL D.2, open question 9) | Read |
| A11 | The session pushes add, remove and change deltas when a stage opens or the file changes on disk, and the element mapper builds the payload of `azure-pipeline.proto`. These are the host's and stay code. This is not a gap. | None | Read |

### B. The FBL binding against the module's parser and writer

| # | Finding | Kind | Known |
| --- | --- | --- | --- |
| B1 | **A value is the text it was written with.** `ReadExecution` says every value is read as text "because every one of them may be an expression this module will not evaluate", and names are read the same way. `YamlScalars` in the library types a plain scalar by the YAML 1.2 core schema, so a stage named `1.0`, a display name `yes` or a job named `null` reaches the model as a number, a boolean or nothing. | Language gap; the one `timeline-disl-fbl` records as its B1 | Read; recorded for the timeline |
| B2 | **Templates and `extends` need a reader FBL does not have.** FBL 11.5 and 17 expect this tool to be "a declared binding, with a plugin for part of its reading". The schema's `reader` is `"declared"` or one PluginReader, and nothing states a plugin for a part. The whole-plugin form does not reach either: `read` receives "the body's bytes (a file body)", and here `PluginReadRequest` is a list of files with a relative path that is empty for the body, so a plugin is told neither where the body is nor where the workspace ends, and cannot open `templates/build-jobs.yml`. `watch` is for folder subjects and the interface notes "no host calls it yet". | Language gap and runtime gap; this one decides Q2 | Measured (the schema and the request type) |
| B3 | **The implicit stage and job.** A file with only `jobs` gets a stage nobody wrote; a file with only `steps` gets that stage and a job. FBL 5.1 says "an entry becomes at most one element or relation", and `parent` needs "the nearest enclosing entry matched by one of `rules`". The root mapping would have to be the diagram, a stage and a job at once. | Language gap | Read |
| B4 | **Conditional insertion.** A sequence item that is one key starting with `${{` over a list is unwrapped at any depth, and the expressions are kept as the element's gate, joined with " and ". FBL 4.2 says a key containing `{` or `}` "cannot be selected"; a `{gate}` capture with a `when` reaches one level; `**` reaches every level and binds nothing, so the joined gate cannot be read. | Language gap | Read |
| B5 | **`dependsOn` is a string or a list, and absent is not empty.** A relation rule at `dependsOn/*` finds list items only and nothing in `dependsOn: Build`; its items are scalars, and no slot means "the entry's own value". Read as a list attribute instead, three things are unstated: that a scalar reads as a list of one; how a list is written other than `flow` (the module writes a scalar for one name, a block list for several, `[]` for none); and removing the key as distinct from writing `[]`. The property row offers both, "(the default order)" and "(nothing)", and they mean different pipelines. `empty` chooses one of them for the binding, and FBL 11.2 has no model change that unsets. | Language gap | Read |
| B6 | **A dependency names by name, in a scope.** `dependsOn` is matched without regard to case, the first element of that name wins, and a job's names are those of its own stage. FBL's `reference: {to, by}` is exact and file-wide. | Language gap | Read |
| B7 | **What a changed value writes.** The writer replaces the property's lines with `key: value` and the trailing comment it found. The author's quotes, the spacing after the colon and a folded value's line breaks are lost. `Scalarise` leaves `true`, `null` and `1.0` bare; FBL's plain-safe rule quotes them. FBL replaces the value's span in its own style. | Behaviour change to rule on (Q1) | Read |
| B8 | **A new key can land inside a script.** A key the element does not have is inserted on the line after the element's first line. For a step that starts with a block scalar (`script:`, `bash:`, `pwsh:` or `powershell:` followed by a bar and the script's lines), Rename and Disable write `displayName:` or `enabled:` between the bar and the script. Every block scalar in the tests sits in a step that already has a `displayName`, or is only parsed. FBL writes a new key where `insert.keys` puts it, after the whole value. | Module defect, to fix on purpose with its own guard | Read; the absence is measured |
| B9 | **What a removal takes, and what undo gives back.** `RemoveElement` removes the element's lines and the blank lines after it; the inverse holds the element's lines only, so the blank lines do not come back and undo is not byte for byte. The redo is found by the value on the restored first line, which is a stage's id and never a job's (`<stage>/<name>`) or a step's, so the redo of a removed job or step is lost. FBL never removes a blank line, removes the leading comments with the entry, and undoes by exact inverse. | Module defect, and a behaviour change to rule on (Q1) | Read |
| B10 | **Where a new stage goes.** It goes after the last stage written in this file, at that stage's indentation. When that stage sits inside a `${{ if }}` block the new stage becomes conditional too. `after-last` in the container `/stages` puts it after the block. No command test adds beside a gated stage. | Behaviour change to rule on (Q1) | Read; the absence is measured |
| B11 | **A new stage is three elements in one block**: the stage, a job named `<stage>Job` and the placeholder step, the inner lists always indented by two. A `skeleton` with a placeholder states the same text; whether the bytes agree in a file that writes its lists flush is not known. | To settle by fixture | Read |
| B12 | **Moving a step.** FBL 0.2 section 6.4 has a declared binding plan a `move`. `EditPlanner` answers "This file's binding cannot move an element to another place; only a persistence plugin can." | Runtime gap | Measured |
| B13 | **Merge keys and aliases.** FBL: "A slot reached through an alias or a merge is read-only." The module writes the element's own key and leaves the anchor alone, which is what `edge-anchors.yml` holds. For the three written keys the two agree unless the key itself arrives through the merge, where the module overrides it locally and FBL refuses. | Behaviour change to rule on (Q1) | Read |
| B14 | **Findings FBL would add.** A sequence item that is not a mapping is passed over in silence; FBL reports an unreadable entry. A key written twice makes YamlDotNet refuse the file, so the tool reports `azure-pipeline.unparseable`; FBL reports `fbl.duplicate-key` and reads on. | Behaviour change to rule on (Q3) | Read |
| B15 | **The sentence for a file that does not parse** is "This is not YAML that can be read: " followed by YamlDotNet's own message. The library's YAML parser is its own, so the second half changes with the reader. | Behaviour change to rule on (Q3) | Read |
| B16 | **Step kind is the first recognised key in written order.** An entry reaches CEL as a map, which has no order. | Language gap, minor | Read |
| B17 | **The template.** `PipelineDocumentFactory` writes a jobs-only pipeline with the base name as a comment and no final newline. FBL's `{base}` placeholder states it; the binding's template must be those bytes. | Definition defect, avoided by writing it down | Read |

### C. Language gaps, as they would be reported to etalii-adp/etalii.adp

1. **FBL: a reader that is declared for the file and a plugin for part of it** (B2), and for any plugin: the body's path, the workspace's edge, the other files it read, and the watching of them. FBL's own section 17 promises the first.
2. **FBL: a slot that reads a YAML scalar as written** (B1). Already reported by `timeline-disl-fbl`; this tool adds that the scalar may be an expression.
3. **FBL: one entry, several elements, and a parent that is not an entry** (B3).
4. **FBL: selecting through a key that contains braces, at any depth, with the keys on the way** (B4).
5. **FBL: a value that is one scalar or a sequence of scalars** (B5): reading it as a list, a relation per item whose end is the item, and the three written forms.
6. **FBL: unsetting a slot as distinct from emptying it** (B5).
7. **FBL: a reference by a name that is compared without case and resolved in a scope** (B6).
8. **FBL: the order of an entry's keys in CEL** (B16).
9. **DISL: a relation with an unset end that is drawn** (A6), with the name it was written with.
10. **DISL: the standard `layered` algorithm** (A10).

These are candidates. Requirement 9 says they are recorded with their evidence and reported, and never worked around silently.

## Open questions

Each is put to the user as a selection. The option marked **(default)** is what this document is written against until the user rules otherwise.

| # | Question | Options | Criteria affected |
| --- | --- | --- | --- |
| Q1 | When an edit changes a value, adds a key, adds or removes an element, what is written? | **(default) What FBL writes**: the value's own span in its own style, a new key in the binding's order, a new stage after the last stage of the list, a removal that leaves blank lines and takes leading comments; the transcript's edit hunks are regenerated once and reviewed as a diff. · **What the writer writes today**: the transcript stays byte-identical, and FBL needs a whole-line rewrite, a key's place after the first line and a removal that takes trailing blank lines, each a language change first. · Other. | 5.2 to 5.5, 1.4 |
| Q2 | Finding B2 means no FBL reader can follow a template today. How is the body read meanwhile? | **(default) A plugin reader now**: the binding names a persistence plugin, the module's parser and writer become its `read` and `plan`, the history, drift and saving become the library's, and the module hands the plugin the path and the workspace root beside the contract, named as standing in for the gap. The declared binding of the appendix is kept as the measure of the gaps. · **Declared now, templates not followed**: every `template:` and `extends` shows as a reference only, which loses what the tool is for. · **Report the gaps and wait**: only Requirements 1 to 3 are built. · Other. | 4.1, 4.4, 6 |
| Q3 | Findings the languages would add or reword (two stages of one name, an entry that is not a mapping, a key written twice, the parse sentence). | **(default) Switched off or kept as today in this conversion**, each raised afterwards as a small change of its own. · **Adopted now**, each named in the transcript's diff. · Other. | 3.3, 4.5 |
| Q4 | A dependency that names nothing (finding A6). | **(default) Still drawn**, by module code named as standing in for the gap. · **Not drawn**, as DISL says, and reported only. · Other. | 3.4 |
| Q5 | The two module defects that write a wrong file or lose bytes (B8, B9) and the shared template id (A9). | **(default) Repaired first**, each with a test seen to fail against today's code, before the transcript is frozen. · **Frozen as they are** and repaired by the conversion, named in the transcript's diff. · Other. | 1.5 |
| Q6 | Is the client's canvas definition in scope? | **(default) Yes**: compiled from the bundled definition with `compileNotation`, with today's hand-written definition kept in a test as the oracle. · **No**: backend only. · Other. | 7 |

## Alignment with Product Vision

The product direction (2026-09-26) is that every tool is a markup definition interpreted by a core in each IDE, with code only where a definition cannot reach. Every converted tool so far owns its format. This is the first whose file belongs to another product, so it tests the claim FBL makes in its security section, that byte preservation, exact undo and drift refusal "are what make it safe to point ADP at files other tools own". It follows `product.md`'s *Don't reinvent, integrate*: the pipeline stays Azure DevOps' file, and ADP is a second reader of it.

## Requirements

### Requirement 1: An oracle before any change

**User Story:** As the owner of the tool, I want what it answers today frozen before anything is converted, so that every later step is held to it.

#### Acceptance Criteria

1. WHEN the work starts THEN a parity transcript `Parity/azure-pipeline.transcript.json` SHALL be generated from today's hand-written code and committed before any provider, parser or writer is changed: per document, the context menu and the property rows of every element, the findings, the payload of every mapped element with a stage and a job opened, and a scripted edit sequence with each step's answer, its undo, its redo and the hunks each made.
2. The corpus SHALL be the fifteen fixture documents, the copies under `examples/` and `src/examples/diagrams/azure-devops-pipeline/`, and **one new fixture for each finding of tables A and B marked Read**, written so that the finding either shows in the transcript or is struck from this document with the reason.
3. A test SHALL require today's code to reproduce the checked-in transcript byte for byte, and SHALL have been seen to fail against a deliberately changed sentence before it is trusted.
4. WHEN a later step changes the transcript THEN the change SHALL be one this document names (Q1, Q3, Q4, Q5), regenerated on purpose, and the diff of the checked-in file SHALL be the review of it. Any other difference is a regression.
5. Findings B8, B9 and A9 SHALL each be shown by a test seen to fail against today's code and repaired in the module before the transcript is generated (Q5).

### Requirement 2: The definition, at DISL 0.3, bundled

**User Story:** As a tool engineer, I want one definition of the pipeline diagram that every host runs.

#### Acceptance Criteria

1. `definitions/diagrams/azure-devops-pipeline.dis` in etalii-adp/etalii.adp SHALL be rewritten at DISL 0.3, using the constructs finding A2 names in place of each workaround, and SHALL validate against the DISL schema in that repository's checks.
2. It SHALL declare `persistence.format` `fbl` with `binding` naming the module's binding, a `typeMap`, an id rule per type that yields today's ids with positional ids marked ephemeral (finding A8), and an `x-azure-pipeline` block mapping its tools, actions, properties and findings to today's wire ids (finding A4).
3. Its companion SHALL be rewritten to say what DISL 0.3 and FBL 0.2 cannot express, which is section C and whatever Requirement 9 adds, and SHALL be corrected where it says more than the tool does (finding A7).
4. The definition SHALL be bundled into `src/diagrams/azure-devops-pipeline/definition/` by `src/diagrams/tools/bundle-disl.sh`, never edited here, embedded in the backend project, and held by a `BundledDefinition.Tests`.
5. WHEN the bundled definition is loaded by `EtAlii.Adp.Specification.Disl` THEN it SHALL load with no diagnostic of severity error.

### Requirement 3: Toolbox, menus, rows and findings are derived

**User Story:** As a maintainer, I want the palette, the menus, the rows and the findings to come from the definition.

#### Acceptance Criteria

1. The toolbox, the context menu and the property rows of every element SHALL be derived from the bundled definition through `ToolboxDerivation`, `ContextMenuDerivation` and `FormDerivation`, mapped to the wire ids of `x-azure-pipeline`, and the providers SHALL compute none themselves.
2. Every sentence a user reads today SHALL be read unchanged: "Show jobs" and "Hide jobs", "Enable" and "Disable", "Use the default order", "Depends on (by default)", the two entries "(nothing)" and "(the default order)", and every read-only reason, "This comes from " followed by the template's path and ", so it has to be edited there." among them.
3. The six findings of `PipelineRules` SHALL be derived by `ConstraintEvaluator` with today's codes, severities, sentences, locations and order, one finding per cycle naming its members in order, none of them when the pipeline has no stage; and no finding the tool does not give today SHALL appear (Q3).
4. A dependency that names nothing SHALL be drawn as today (Q4), and the stage that waits for it marked indeterminate.
5. An element that is implicit or comes from a template SHALL offer no action and SHALL show every row read-only with today's reason.
6. WHEN the derived answers are compared with the transcript THEN the menus, rows, findings and payloads SHALL be byte-identical.
7. The hand-written providers' logic SHALL be kept in the test project as the oracle and removed from the product code.

### Requirement 4: The body is read through the binding

**User Story:** As a user, I want every pipeline that opens today to open the same.

#### Acceptance Criteria

1. The module SHALL own its binding, `backend/EtAlii.Adp.Diagram.AzureDevOpsPipeline/azure-devops-pipeline.fbl`, embedded in the backend project, with the reader Q2 rules. WHERE the reader is a plugin THEN it SHALL meet FBL 11.4, and what the module hands it beside the contract (finding B2) SHALL be one named type.
2. WHEN a body is read THEN the stages, jobs, steps, template references and unresolved templates SHALL be those `PipelineParser` reads today, with today's ids, for every document of the corpus: the three shapes of a pipeline (finding B3), every gate (B4), both forms of `dependsOn` and its absence (B5), names matched as today (B6), and merge keys expanded with the mapping's own keys winning.
3. Every value SHALL be read as the text it was written with (finding B1), and no expression SHALL be evaluated.
4. A template SHALL be followed only inside the workspace, never over the network, with today's six reasons for one that is not, and a template's parameters SHALL be read by name and never by value.
5. A body that is not YAML SHALL open empty and read-only with the one finding `azure-pipeline.unparseable` on the line of the problem (Q3 for its sentence).
6. Reading SHALL never throw on content.

### Requirement 5: Every edit is written by splices

**User Story:** As a user whose build depends on this file, I want an edit to change what I changed and nothing beside it.

#### Acceptance Criteria

1. Every command of the module SHALL write through `EtAlii.Adp.Specification.Fbl` as a model change planned into splices of FBL 6.1, each naming its operation truthfully. Only `displayName`, `dependsOn` and `enabled` SHALL ever be written, with whole stages, jobs and steps added, removed and moved.
2. WHEN a value changes THEN only its span SHALL change, in its own style (Q1); an expression SHALL be written back as written and never quoted.
3. WHEN a key is added THEN it SHALL be written after the value of the key before it and never inside a block scalar (finding B8).
4. `dependsOn` SHALL be written as one name, a block list, `[]`, or removed, as the gesture asks (finding B5); WHERE FBL cannot state one of the four THEN that splice SHALL be planned by module code named as standing in for the gap.
5. A removal and its undo SHALL restore the file byte for byte, and its redo SHALL remove the same element, for a stage, a job and a step (finding B9).
6. Moving a step SHALL be one `move` that keeps the step's bytes (finding B12).
7. Every refusal SHALL keep today's sentence: the five of adding, the four of removing, the four of dependencies, the one of moving, and "That element is no longer in this pipeline."
8. An edit that would close a cycle or leave no stage that waits for nothing SHALL be refused before anything is written, as today.
9. Undo SHALL be refused when the file has changed since the edit (FBL 7.2), which replaces "This pipeline has changed too much since then to put that back." where the library's check fires first; the change SHALL be in the transcript's diff.
10. A document ADP did not change SHALL be written back byte-identical, for every document of the corpus, `edge-crlf.yml` and `edge-tied-endings.yml` included.

### Requirement 6: What stays code, and why

**User Story:** As a maintainer, I want the code that remains to be exactly what the languages cannot state.

#### Acceptance Criteria

1. The module SHALL keep: template resolution (`PipelineTemplates`), the layout (finding A10), the element mapper and the session's deltas (A11), the view state, the document store, the `.proto` payloads, and each stand-in a requirement above names.
2. `PipelineValidator`, `PipelineRuleSet`, `PipelineEdits`, the three providers' logic and `PipelineBlocks` SHALL leave the product code. `PipelineParser` and `PipelineWriter` SHALL leave it or become the plugin's `read` and `plan`, as Q2 rules.
3. WHEN the conversion is complete THEN the companion document SHALL list every remaining module class beside the gap or the host concern that keeps it, and a class with neither SHALL be removed.

### Requirement 7: The client

**User Story:** As a user, I want the canvas to look and behave as it does, drawn from the definition the backend runs.

#### Acceptance Criteria

1. The client's canvas definition SHALL be compiled from the bundled definition with `parseDisl` and `compileNotation` (Q6), with today's hand-written definition kept in a test as the oracle.
2. IF `compileNotation` cannot read something the definition states THEN it SHALL be added to the shared library for every module, named for its geometry, and SHALL NOT be written in this module.
3. A `tests.md` entry SHALL cover in a real browser, in both themes: opening and closing a stage and a job, the seven indicators and their order, the problem mark, an implicit edge, a broken edge, an element from a template, an unresolved template, dropping each of the four toolbox entries, F2, Alt+Up and Alt+Down, and that dragging is refused.

### Requirement 8: The library work this tool needs

**User Story:** As a maintainer of the shared libraries, I want each thing this tool needs added once, for every tool.

#### Acceptance Criteria

1. `EtAlii.Adp.Specification.Disl` SHALL implement `diagram.cycles`, `reachable` and `inCycle` as DISL 12.4 defines them, where the rewritten definition calls them (finding A5), each proven by a library test seen to fail first. What `causal-loop-disl-fbl` Requirement 9.1 adds is used and not added twice.
2. `EtAlii.Adp.Specification.Fbl` SHALL plan a `move` for a declared `yaml` binding as FBL 6.4 says (finding B12), with a conformance fixture, if Q2 rules a declared reader.
3. A defect of a library that a fixture of Requirement 1 shows SHALL be repaired in the library; a language gap SHALL NOT be repaired here.
4. No module name SHALL appear in a library change.

### Requirement 9: Gaps are recorded and reported, never tuned away

**User Story:** As the owner of DISL and FBL, I want every place the languages fall short reported with its evidence.

#### Acceptance Criteria

1. Every language gap, the ten of section C and any the implementation adds, SHALL be recorded in the companion document with the construct concerned, the fixture that shows it, and what would resolve it.
2. The draft binding of the appendix SHALL be offered to etalii-adp/etalii.adp as `specifications/fbl/azure-devops-pipeline.fbl`, with every construct it needs and FBL lacks marked.
3. A gap SHALL NOT be closed by weakening a check, skipping a fixture, or stating in the definition something the tool does not do.
4. WHEN a *read* finding of this document turns out not to occur THEN it SHALL be struck here with the fixture that shows it, as a small amendment.

### Requirement 10: Documentation, catalog and gates

**User Story:** As a reader of the repository, I want its pages to stay true through the conversion.

#### Acceptance Criteria

1. `src/diagrams/readme.md`, `docs/creating-a-diagram-module.md`, `docs/architecture.md` and `docs/solution-structure.md` SHALL be updated in the change that makes a sentence of theirs false, and `docs/tools.md`'s row for `azure-devops/pipeline` SHALL name this specification.
2. All four gates SHALL exit zero on every pull request of this specification, judged by captured exit codes, with a newly created worktree built before a green gate is trusted about the bundled definition.
3. WHEN the tasks document is written THEN every acceptance criterion here SHALL be claimed by a task, established by diffing the two sets, and two traces SHALL be read back to their artefacts.

## Non-Functional Requirements

### Code Architecture and Modularity

- The module ends as its definition, its binding and the code Requirement 6.1 names. The house rules of `src/.editorconfig` and the three rules the gates refuse apply. The fixtures stay `-text`, as `.gitattributes` has them.

### Performance

- Opening, validating and editing the largest pipeline of the corpus, and a pipeline that follows twenty templates, SHALL not be perceptibly slower than today. A template SHALL be read once per reading, as the resolver's cache does today.

### Security

- A template outside the workspace SHALL never be read, a repository resource never fetched, and a parameter's value never held in the model. These are today's rules and FBL 16's.

### Reliability

- No file that opens today opens differently. An unchanged document is written back byte-identical. A file ADP could not read is never written. Every test written for a finding is seen to fail against it first.

### Usability

- Every sentence a user reads today is read unchanged, unless this document names the change.

## Out of scope

- Writing any key beyond `displayName`, `dependsOn` and `enabled`, registering `.yaml`, and showing run status.
- Changing DISL or FBL. Gaps are reported (Requirement 9).
- The layout's algorithm, the streaming of deltas, and the other tools of the series.

## Appendix: the binding, as drafted

A draft in the `yaml` family, the declared half that FBL 17 expects. It is not valid FBL 0.2 in six places, each marked by a key FBL does not have: `expand` (finding B2), `"as": "text"` (B1), `also` (B3), `through` (B4), `"list": "scalar-or-sequence"` with `unset` (B5), and `reference.scope` with `reference.compare` (B6). Rules for a deployment job, for the steps of a lifecycle hook and for the `/jobs/*` and `/steps/*` shapes repeat the ones shown and are left out.

```json
{
  "fbl": "0.2",
  "bindings": {
    "pipeline": {
      "title": "Azure DevOps pipeline",
      "claims": { "extensions": [".yml"], "shared": true, "registrationOnly": true, "origins": ["azure-devops/pipeline"], "suggest": { "contains": ["stages:", "jobs:", "steps:"] } },
      "body": { "kind": "file", "family": "yaml" },
      "reader": "declared",
      "expand": { "plugin": "net.etalii.adp.azure-devops.templates", "rules": ["stageTemplate", "jobTemplate", "stepTemplate", "extends"] },
      "text": { "newline": "crlf", "indent": 2, "sequenceIndent": "indented", "finalNewline": false },
      "elements": [
        {
          "name": "pipeline", "type": "Pipeline", "at": "/",
          "also": [ { "type": "ImplicitStage", "when": "!has(entry.stages) && (has(entry.jobs) || has(entry.steps))" }, { "type": "ImplicitJob", "when": "!has(entry.stages) && !has(entry.jobs) && has(entry.steps)" } ],
          "attributes": { "hasStagesBlock": { "value": "has(entry.stages)" } },
          "readOnly": true
        },
        {
          "name": "extends", "type": "TemplateReference", "at": "/extends",
          "attributes": { "reference": { "key": "template", "as": "text" } },
          "readOnly": true
        },
        {
          "name": "stageTemplate", "type": "TemplateReference", "at": "/stages/*", "through": "^\\$\\{\\{", "when": "has(entry.template)",
          "attributes": { "reference": { "key": "template", "as": "text" } },
          "readOnly": true
        },
        {
          "name": "stage", "type": "Stage", "at": "/stages/*", "through": "^\\$\\{\\{", "when": "!has(entry.template)",
          "attributes": {
            "name": { "key": "stage", "as": "text", "readOnly": "This is edited in the pipeline file. A wrong value here would break the build." },
            "displayName": { "key": "displayName", "as": "text", "empty": "remove" },
            "dependsOn": { "key": "dependsOn", "as": "text", "list": "scalar-or-sequence", "empty": "keep", "unset": "remove", "style": "flow", "reference": { "to": ["stage"], "by": "name", "compare": "ignore-case" } },
            "condition": { "key": "condition", "as": "text", "readOnly": true },
            "enabled": { "key": "enabled", "as": "text", "empty": "remove" },
            "trigger": { "key": "trigger", "as": "text", "readOnly": true },
            "isSkippable": { "key": "isSkippable", "as": "text", "readOnly": true }
          },
          "insert": { "place": "after-last", "container": "/stages", "keys": ["stage", "displayName", "dependsOn", "condition", "enabled"], "skeleton": "jobs:\n  - job: {name}Job\n    steps:\n      - script: echo Add your build steps here\n" },
          "remove": {}
        },
        {
          "name": "job", "type": "Job", "at": "/stages/*/jobs/*", "through": "^\\$\\{\\{", "when": "!has(entry.template) && !has(entry.deployment)",
          "parent": { "rules": ["stage"], "slot": "jobs" },
          "attributes": {
            "name": { "key": "job", "as": "text", "readOnly": true },
            "displayName": { "key": "displayName", "as": "text", "empty": "remove" },
            "dependsOn": { "key": "dependsOn", "as": "text", "list": "scalar-or-sequence", "empty": "keep", "unset": "remove", "style": "flow", "reference": { "to": ["job"], "by": "name", "compare": "ignore-case", "scope": "parent" } },
            "enabled": { "key": "enabled", "as": "text", "empty": "remove" }
          },
          "insert": { "place": "after-last", "container": "jobs", "keys": ["job", "displayName", "dependsOn", "enabled"], "skeleton": "steps:\n  - script: echo Add your build steps here\n" },
          "remove": {}
        },
        {
          "name": "step", "type": "Step", "at": "/stages/*/jobs/*/steps/*", "when": "true",
          "parent": { "rules": ["job"], "slot": "steps" },
          "attributes": {
            "displayName": { "key": "displayName", "as": "text", "empty": "remove" },
            "enabled": { "key": "enabled", "as": "text", "empty": "remove" },
            "script": { "key": "script", "as": "text", "readOnly": true }
          },
          "insert": { "place": "after-last", "container": "steps", "keys": ["script", "displayName", "enabled"] },
          "remove": {}
        }
      ],
      "registration": { "createOnFirstPlacement": false },
      "template": { "text": "# {base}\r\ntrigger:\r\n  - main\r\npool:\r\n  vmImage: ubuntu-latest\r\njobs:\r\n  - job: Build\r\n    displayName: Build\r\n    steps:\r\n      - script: echo \"Replace this with your build\"\r\n        displayName: Build" }
    }
  }
}
```

Four things in it are unsettled and are the design's to close with fixtures: `style: "flow"` writes several names as `[a, b]` where the module writes a block list; the step rule binds one kind key where the tool reads nine in written order (B16); `keys` cannot say "after whatever the entry's last key is", which is where a hand-edited element wants a new key; and the template's line endings are written as CRLF here on the reading that the factory's raw string takes them from a source file checked out with CRLF.

## Appendix: the definition's persistence and wire ids, as drafted

The rest of the definition is the existing file brought to 0.3 (Requirement 2.1). These two blocks are new. Under the plugin reader of Q2 the `binding` is the same and its `reader` names the plugin.

```json
{
  "persistence": {
    "format": "fbl",
    "binding": "https://raw.githubusercontent.com/etalii-adp/etalii.adp.ide.standalone/develop/src/diagrams/azure-devops-pipeline/backend/EtAlii.Adp.Diagram.AzureDevOpsPipeline/azure-devops-pipeline.fbl#pipeline",
    "ids": {
      "strategy": "derived", "stable": false,
      "types": {
        "Stage": { "strategy": "derived", "expression": "self.name != '' ? self.name : 'stage-' + string(self.positionIn(diagram.nodesOfType('Stage')))", "ephemeral": "self.name == ''" },
        "Job": { "strategy": "derived", "expression": "self.parent.id + '/' + (self.name != '' ? self.name : 'job-' + string(self.positionIn(self.parent.childrenOfType('Job'))))", "ephemeral": "self.name == ''" },
        "Step": { "strategy": "derived", "expression": "self.parent.id + '/step-' + string(self.positionIn(self.parent.childrenOfType('Step')))", "ephemeral": true }
      }
    },
    "view": { "store": [] },
    "typeMap": {
      "Pipeline": { "as": "diagram" },
      "ImplicitStage": { "as": "Stage" },
      "ImplicitJob": { "as": "Job" }
    }
  },
  "x-azure-pipeline": {
    "tools": {
      "stage": { "id": "azure-pipeline.toolbox.stage", "drop": "azure-pipeline.add-stage" },
      "job": { "id": "azure-pipeline.toolbox.job", "drop": "azure-pipeline.add-job" },
      "deploymentJob": { "id": "azure-pipeline.toolbox.deployment-job", "drop": "azure-pipeline.add-deployment-job" },
      "step": { "id": "azure-pipeline.toolbox.step", "drop": "azure-pipeline.add-step" }
    },
    "actions": {
      "editLabel": "azure-pipeline.rename", "toggleEnabled": "azure-pipeline.toggle-enabled",
      "addStage": "azure-pipeline.add-stage", "addJob": "azure-pipeline.add-job", "addDeploymentJob": "azure-pipeline.add-deployment-job", "addStep": "azure-pipeline.add-step",
      "delete": "azure-pipeline.remove", "moveUp": "azure-pipeline.move-step-up", "moveDown": "azure-pipeline.move-step-down",
      "clearDependencies": "azure-pipeline.clear-dependencies", "Stage/toggle": "azure-pipeline.toggle-stage", "Job/toggle": "azure-pipeline.toggle-job"
    },
    "properties": {
      "name": "azure-pipeline.name", "displayName": "azure-pipeline.display-name", "jobCount": "azure-pipeline.job-count", "kind": "azure-pipeline.kind",
      "dependsOn": "azure-pipeline.depends-on", "condition": "azure-pipeline.condition", "trigger": "azure-pipeline.trigger", "isSkippable": "azure-pipeline.is-skippable",
      "pool": "azure-pipeline.pool", "environment": "azure-pipeline.environment", "strategy": "azure-pipeline.strategy", "multiplicity": "azure-pipeline.multiplicity",
      "timeout": "azure-pipeline.timeout", "continueOnError": "azure-pipeline.continue-on-error", "enabled": "azure-pipeline.enabled",
      "identifier": "azure-pipeline.identifier", "hook": "azure-pipeline.hook"
    },
    "findings": {
      "std.unparseable": "azure-pipeline.unparseable", "danglingDependency": "azure-pipeline.dangling-dependency", "cycle": "azure-pipeline.cycle",
      "unreachable": "azure-pipeline.unreachable", "noStartingStage": "azure-pipeline.no-starting-stage", "unnamed": "azure-pipeline.unnamed", "templateNotFollowed": "azure-pipeline.template-not-followed"
    }
  }
}
```

The positional counters follow the parser: a stage's number is the count of stages read before it, those a followed template contributed included, which is reading order as DISL 8.6 defines it only if the expanded elements are in the model in place. That is the design's to show with the `templates.yml` fixture.

## Sources

- The module `src/diagrams/azure-devops-pipeline/` at `develop` `9a646009`: `PipelineParser.cs`, `PipelineWriter.cs`, `PipelineBlocks.cs`, `PipelineEdits.cs`, `PipelineTemplates.cs`, `PipelineRuleSet.cs`, `PipelineRules.cs`, `PipelineValidator.cs`, `PipelineGraphBuilder.cs`, the three providers, `Commands/`, `PipelineDocumentFactory.cs`, `Diagram.cs`, `_Model/PipelineEditTarget.cs`, `_Model/PipelineExecution.cs`, the fixtures `edge-comments.yml`, `edge-anchors.yml`, `edge-expressions.yml`, `templates.yml`, and `examples/example 1/templates.adp`.
- `src/backend/EtAlii.Adp.Documents/YamlNodeRange.cs` for what an element's lines are (findings B8, B9).
- Counts: `git ls-files src/diagrams/azure-devops-pipeline`, with `wc -l` over the backend's `.cs` files (6,322 in 64), the test project's (23 files) and the client's `.ts` and `.tsx` outside tests (733).
- Absences, each a `grep` over the test project and its fixtures: a block scalar after `script:`, `bash:` or `pwsh:` (three places, none edited without a `displayName`); `on:`, `failure:`, `preDeploy` (none); `template-0`, `template-1` (none); "blank" in `PipelineCommands.Tests.cs` (none); "if " and "gate" in `PipelineCommands.Tests.cs` (none).
- etalii-adp/etalii.adp at `develop` `da64ff0`: `definitions/diagrams/azure-devops-pipeline.dis` (searched for `inCycle`, `reachable`, `diagram.`) and `.md`; `specifications/fbl/FBL-specification.md` sections 3.1, 4.1 to 4.3, 5.1 to 5.7, 6.1 to 6.4, 7.2 to 7.5, 11.2, 11.5, 12.1, 13, 16 and 17; `specifications/fbl/fbl.schema.json` (`Binding.reader`, `PluginReader`, `Slot`); `specifications/fbl/databricks-job.fbl` for a dependency read as a relation; `specifications/disl/DISL-specification.md` (0.3 draft: 2.3, 4.11, 7.2, 7.3, 7.5, 8.6, 11.2, 11.5, 11.6, 12.4, *Changes from 0.1*, *Changes from 0.2*, D.2).
- `src/backend/EtAlii.Adp.Specification.Disl/Expressions/DislCelLibrary.cs` for finding A5; a search of the project for `cycles`, `reachable` and `inCycle` found no file.
- `src/backend/EtAlii.Adp.Specification.Fbl/`: `Planning/EditPlanner.cs` (the refusal of a move, B12), `Files/Yaml/YamlScalars.cs` (B1), `Plugins/IPersistencePlugin.cs` and `Plugins/_Model/PluginReadRequest.cs` (B2).
- `src/backend/EtAlii.Adp.Specification.Fbl.Tests/RealFiles/divergences.json` and `RealFileCorpus.cs`, searched for `azure` and `pipeline`: nothing for this tool.
- `causal-loop-disl-fbl` and `timeline-disl-fbl` for the series, the survey and the shape of this document.
