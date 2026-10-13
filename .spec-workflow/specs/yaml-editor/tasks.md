# Tasks Document

One Developer owns this specification, per CLAUDE.md's one-owner-per-specification rule, and works it in one worktree (`.claude/worktrees/yamled`, branch `features/yaml-editor`). It lands as one pull request into `develop`, with the four gates green, judged by exit codes captured before any other command.

**What this waits on.** The first landing of `json-editor` (its tasks 1 to 6) merged on `develop`. Nothing here starts before that, and nothing here writes a second copy of a shared piece meanwhile. It does not wait on `xml-editor`.

**The order is load-bearing in one place.** Task 1 is first and can stop everything: if the service does not run in a browser build of this client, the Developer stops and the user chooses (Requirement 1.4).

**Rulings this document is written for.** Every open question of the requirements at its default (approved 2026-10-12), and ruling A1 of the design at its first option: one alias, `path` to `path-browserify`, in the client's shared Vite configuration. The user ruled so in chat on 2026-10-12 and Requirement 2.5 was amended the same day.

**Every guard is seen to fail.** A test written for a behaviour is run once against that behaviour removed, the red run is recorded in the task's implementation log, and the file is touched after the sabotage is restored.

---

## The coverage diff, run before this document was raised

**55 acceptance criteria in the requirements; 55 claimed by the tasks below; none unclaimed, nothing claimed that is not a criterion, and none claimed twice.**

| Requirement | Criteria | Claimed by |
| --- | --- | --- |
| 1 | 4 | 1 (1.1 to 1.4) |
| 2 | 6 | 2 (2.1 to 2.6) |
| 3 | 7 | 3 (3.1 to 3.7) |
| 4 | 6 | 4 (4.1 to 4.6) |
| 5 | 5 | 5 (5.1 to 5.5) |
| 6 | 6 | 6 (6.1 to 6.6) |
| 7 | 7 | 7 (7.1 to 7.7) |
| 8 | 5 | 2 (8.1, 8.3, 8.4, 8.5), 6 (8.2) |
| 9 | 2 | 8 (9.1, 9.2) |
| 10 | 3 | 8 (10.1 to 10.3) |
| 11 | 4 | 2 (11.3), 8 (11.1, 11.2, 11.4) |

**The non-functional requirements are claimed here by name:** Performance by tasks 6 and 7, which measure and record the two bounds; Security (tags construct nothing, alias expansion is bounded, nothing read beyond the project) by tasks 4 and 5; Reliability (a throwing service) by task 6; Usability (keyboard reach, a sentence about indentation names the column) by tasks 3 and 4.

**One criterion is satisfied by a rule and not by an artefact.** Requirement 9.1 (use the shared pieces, add none beside them) is claimed by task 8 as a check over the finished module: its client imports the shared pieces and defines no underline, no completion list and no reference rule of its own. Every earlier task is bound by it.

---

## The tasks

- [x] 1. Prove the YAML service in this client, with the alias
  - Files: `src/client/vite.config.ts`, `src/client/package.json` (`path-browserify`, and `yaml-language-server` until task 2 moves it), `docs/dependencies.md`, `src/client/src/editors/spike/yamlLanguageService.spike.test.ts` (new), `src/client/src/viteBrowserExternals.test.ts` (new)
  - Add `yaml-language-server` at an exact version and `path-browserify`, each with its inventory row and the licence read from the installed package.
  - Add the alias with a comment naming this module and why. Run the whole client suite: three test-support files import `node:path`, and the suite staying green is what shows the alias does not touch them.
  - The spike as tests that stay: for one fixed two-document file and one fixed schema, the findings per document, key and value completions, and hover text. One further test per schema dialect the service names (draft-04, draft-07, 2019-09, 2020-12).
  - Read from a run which fields of a diagnostic (`source`, `code`) tell a schema violation, a deprecation and an unresolved alias apart, and where the finding for a value reached through an alias sits. Record both in the design by a small amendment: tasks 4 and 6 are written against the answer.
  - Build the client for production and open it in a browser with a temporary route that runs the same calls, **including a hover**, the call that failed without the alias. Record what the console shows. Remove the route before the pull request.
  - The build guard: a test that builds the editor's chunk and fails when Vite's placeholder for a module left out for the browser is in it. See it fail with the alias removed.
  - Measure the size of the YAML chunk and that the shell's own bundle did not grow by it. Record, by the same amendment, every Node-only module replaced: `path`, and any other the browser run finds.
  - If the browser run fails with the alias in place, or needs a further replacement the Developer cannot justify: stop, and put the choice of Requirement 1.4 to the user as a selection through the Scrum master.
  - _Leverage: the scratch run recorded in the design; `src/client/src/viteFsAllow.test.ts` as the shape of a test about the build configuration_
  - _Requirements: 1.1, 1.2, 1.3, 1.4_
  - _Prompt: Implement the task for spec yaml-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Front-end developer who verifies a third-party library and a build change before building on them | Task: Add yaml-language-server and path-browserify, add the one alias to the shared Vite configuration, write the spike as tests that stay, read which diagnostic fields tell the finding kinds apart, run a production build in a browser including a hover, add the build guard, and record everything in the design by a small amendment | Restrictions: One alias and nothing else in the shared configuration; do not work around a failure, stop and raise the selection of Requirement 1.4; the temporary route does not reach the pull request; the build guard must be seen to fail with the alias removed; read licences from the packages' own metadata | _Leverage: the design's scratch run, viteFsAllow.test.ts | _Requirements: 1.1, 1.2, 1.3, 1.4 | Success: spike tests green, the browser run recorded including the hover, the guard seen red then green, sizes and the diagnostic fields in the design, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [x] 2. The module, its session and its examples
  - Files: `src/editors/yaml/backend/EtAlii.Adp.Editor.Yaml/` (`Editor.cs`, `YamlEditorSession.cs`, `YamlEditorSessionFactory.cs`, the project file and its `.DotSettings`), `src/editors/yaml/backend/EtAlii.Adp.Editor.Yaml.Tests/`, `src/backend/EtAlii.Adp.slnx`, `src/editors/yaml/api/readme.md`, `src/editors/yaml/client/package.json`, `src/editors/yaml/client/register.ts`, `src/editors/yaml/client/YamlEditorPanel.tsx` (the shared panel with the grammar only), `src/editors/yaml/examples/`, `src/examples/editors/yaml/`
  - The definition: id `yaml`, title "YAML editor", the claims `.yaml` and `.yml`. The session and its tests in the shape of the plain module's. `YamlDotNet` is not referenced.
  - Examples: a document that names a schema of the project and satisfies it; one that breaks its schema in the ways Requirement 6 names; one with anchors and aliases; one with no schema; a CRLF file; a file of more than one document.
  - `Examples.Tests`: every example opens, round-trips byte-identically, and has a byte-identical replica.
  - Tests: a YAML file no diagram claims resolves to this editor; the Azure DevOps pipeline example's `.yml` still opens its diagram and "Open as text" resolves to this editor; a save in this editor reaches an open diagram of the same file as a change on disk; startup reports no claim conflict.
  - Move `yaml-language-server` to the module's own manifest.
  - _Leverage: `src/editors/plain/` and `src/editors/json/` as worked examples; `docs/creating-an-editor-module.md`_
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 8.1, 8.3, 8.4, 8.5, 11.3_
  - _Prompt: Implement the task for spec yaml-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Full-stack developer adding an editor module to EtAlii.Adp | Task: Create the yaml editor module in the family's four folders with its definition, session, client registration and examples, and the tests that hold its claims, the diagram-body case and the family's contract | Restrictions: No core or shell file changes beyond task 1's alias; the session holds nothing YAML-specific and YamlDotNet is not referenced; a diagram's body still opens its diagram; no blocking call in an async method and tests pass TestContext.Current.CancellationToken; run the JetBrains inspection for the new projects | _Leverage: the plain and json modules, creating-an-editor-module.md | _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 8.1, 8.3, 8.4, 8.5, 11.3 | Success: a .yml file opens in the YAML editor, every example round-trips byte-identically with its replica, the diagram case is held both ways, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [x] 3. Colour, and a keyboard that keeps indentation right
  - Files: `src/editors/yaml/client/yamlKeys.ts` (new), `src/editors/yaml/client/yamlScalarColour.ts` (new), their tests, `src/editors/yaml/client/YamlEditorPanel.tsx`, `src/editors/yaml/client/YamlEditorPanel.test.tsx`
  - A test over a mounted editor: each kind of token of Requirement 3.1 carries a class of the shared colour scheme; a document with a mistake keeps classes on what can still be read.
  - `yamlScalarColour`: a plain scalar in the visible range carries the class of the type YAML 1.2 reads it as. Cases: `yes`, `1.10`, `0x1f`, `~`, `null`, `true`, a date.
  - `yamlKeys`: the indentation width read from the document, two when it cannot be; Tab inserts spaces to the next stop and never a tab character; Shift+Tab removes a level; Enter continues at the indentation of the line left, one level deeper after a key or a dash that opens a block. Cases on documents indented by two and by four.
  - Folding of a mapping, a sequence and a block scalar leaves the text unchanged.
  - The contrast of Requirement 3.3 is the shared tokens' and is held by the JSON editor's guard; this task adds no colour of its own, and a test over the module's folder holds that it declares none.
  - _Leverage: `editorHighlight` and `BaseTextEditor` from `json-editor`; `@codemirror/lang-yaml`'s own indentation; `@codemirror/language` (`indentUnit`)_
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_
  - _Prompt: Implement the task for spec yaml-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Front-end developer working on CodeMirror 6 language support | Task: Give the YAML editor its colour through the shared scheme, colour plain scalars by the type YAML 1.2 reads them as, and write the Tab, Shift+Tab and Enter behaviour that keeps indentation in spaces at the document's own width | Restrictions: No colour declared in the module; a tab character is never inserted; the scalar colouring reads only the visible range; do not assert colour through jsdom, assert classes; every test is seen to fail against the behaviour removed | _Leverage: editorHighlight, lang-yaml, indentUnit | _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7 | Success: one passing test per criterion, each seen to fail, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [x] 4. Several documents in a file, and the mistakes of YAML itself
  - Files: `src/editors/yaml/client/splitDocuments.ts` (new), `src/editors/yaml/client/splitDocuments.test.ts` (new), `src/editors/yaml/client/yamlAnalysis.ts` (new), `src/editors/yaml/client/yamlAnalysis.test.ts` (new)
  - `splitDocuments`: each document's start and end offset and the schema line in the comments before its first content. Cases: one, two and three documents; a `---` inside a block scalar and inside a quoted string, which are not markers.
  - `yamlAnalysis` loads the service with `import()`, analyses each part on its own, and shifts every range by the part's start.
  - Findings without a schema: each parser error at its place; a tab used for indentation as an error on the tab; a repeated key as an error on the second; an alias with no anchor as an error, by the field task 1 found or by the module's own check of the parser's document.
  - A mistake in one document leaves the findings of the others in place.
  - A sentence about indentation names the column that was expected; where the parser's sentence does not, the module's finding adds it.
  - Tags construct nothing: a test with a tag that would construct an object in an unsafe loader shows a plain value. A document whose aliases expand past the parser's bound gets the parser's error as a finding and returns in under a second.
  - _Leverage: task 1's record of the diagnostic fields; `analysisExtensions` from `json-editor`; the `yaml` parser's `LineCounter` and document ranges_
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6_
  - _Prompt: Implement the task for spec yaml-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Front-end developer integrating a YAML language service | Task: Split a file into its YAML documents with the schema line of each, write yamlAnalysis as the only file that knows the service, analyse each document on its own with ranges shifted to the whole text, and report YAML's own mistakes with the severities the requirements name | Restrictions: Nothing but yamlAnalysis.ts imports the service and it is loaded with import(); a marker inside a scalar is not a marker; assert ranges and severities, not sentences; no tag constructs anything; every test is seen to fail against the behaviour removed | _Leverage: analysisExtensions, the yaml parser, task 1's findings | _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6 | Success: one passing test per criterion, the two security cases held, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [x] 5. Which schema applies
  - Files: `src/editors/yaml/client/yamlAnalysis.ts`, `src/editors/yaml/client/yamlAnalysis.test.ts`, `src/editors/yaml/client/YamlEditorPanel.tsx`, `src/editors/yaml/client/YamlEditorPanel.test.tsx`
  - Each part's schema line goes through the shared `resolveSchemaReference`; the answer is handed to the service through its custom schema provider and its schema request callback, served from `SchemaFiles`. The service's own lookup of the comment line is not relied on.
  - A line does not reach past the next document marker; a document without a line has no schema.
  - A reference that cannot be resolved becomes one information finding on the comment line, with the reason.
  - A schema written in YAML is parsed and handed over as JSON text.
  - The status names the schema or says "No schema".
  - A panel test: a schema arriving after the document; a second document with a different schema; a new revision of a schema re-validating without reopening.
  - _Leverage: `resolveSchemaReference`, `SchemaFiles` and the shared case file from `json-editor`_
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_
  - _Prompt: Implement the task for spec yaml-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Front-end developer integrating JSON Schema for YAML | Task: Read the schema comment line per YAML document, resolve it with the shared rules, serve the schema to the service from SchemaFiles, accept a schema written in YAML, and say in the status line which schema applies | Restrictions: Resolve with the shared resolver only and add no rule of the module's own; nothing is read from outside the project or from the network; a schema line never applies beyond its own document; every test is seen to fail against the behaviour removed | _Leverage: resolveSchemaReference, SchemaFiles | _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5 | Success: one passing test per criterion, the panel test shows per-document schemas and re-validation, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [x] 6. Validation against the schema, and nothing written back
  - Files: `src/editors/yaml/client/yamlAnalysis.ts`, `src/editors/yaml/client/yamlAnalysis.test.ts`, `src/editors/yaml/backend/EtAlii.Adp.Editor.Yaml.Tests/Examples.Tests.cs` or a client test over the examples
  - Map the service's diagnostics: a schema violation becomes an error, a deprecated key stays a warning, by the fields task 1 found.
  - A missing required key sits on the mapping and names the key. A value reached through an alias or a merge key carries its finding at the alias; where the service places it at the anchor only, the module adds it at each alias that reaches the value.
  - No schema finding for a part that could not be read. An unsupported dialect is said once.
  - Above 1,000,000 characters nothing is validated and the status says so. A throw inside the service is caught once: the parser's findings stay and the status says schema support stopped.
  - **Nothing the module parses is written back**: a test runs a full analysis over every example, saves, and compares bytes; a second test holds that no function of the module returns a text for the editor other than a completion's insert, by the module's exports.
  - Measure one second for findings on a 5,000-line document against a 2,000-line schema and record it.
  - _Leverage: tasks 4 and 5; the design's mapping table_
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 8.2_
  - _Prompt: Implement the task for spec yaml-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Front-end developer integrating JSON Schema validation for YAML | Task: Map the service's diagnostics to findings with the severities the requirements name, place findings for aliased values where the document uses them, keep the editor working when the service throws, and hold by tests that nothing the editor understood is ever written back | Restrictions: Tell finding kinds apart by the service's fields, not by its sentences; never register or call the service's formatter; findings are never invented for text that could not be read; every test is seen to fail against the behaviour removed | _Leverage: yamlAnalysis, splitDocuments | _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 8.2 | Success: one passing test per criterion, every example byte-identical after analysis and save, the measured time recorded, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [x] 7. Completion and hover from the schema
  - Files: `src/editors/yaml/client/yamlAnalysis.ts`, `src/editors/yaml/client/yamlAnalysis.test.ts`
  - Map the service's completions to the shared shape: keys of the mapping the caret's indentation is in, required ones first; the values a schema limits; a new item on an empty line of a described sequence.
  - An accepted key leaves well-formed YAML at that depth, in spaces: tests insert each kind and parse the result.
  - Two items that would insert the same text are shown once. Ctrl+Space on an empty line asks explicitly.
  - Hover: the service's markdown reduced to text, the "Source:" line dropped.
  - Without a schema nothing is offered.
  - Measure 300 milliseconds for a completion list on the Performance section's document and record it.
  - _Leverage: the shared completion and hover presentation from `json-editor`_
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7_
  - _Prompt: Implement the task for spec yaml-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Front-end developer integrating schema-driven completion for YAML | Task: Map the service's completions and hover to the shared shapes so that keys, limited values and sequence items are offered at the caret's depth and inserted as well-formed YAML in spaces | Restrictions: Hover and descriptions are text, never markup; every accepted completion leaves YAML that parses, held by tests; nothing is offered without a schema; every test is seen to fail against the behaviour removed | _Leverage: analysisExtensions, yamlAnalysis | _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7 | Success: one passing test per criterion, the measured time recorded, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [x] 8. Documentation, the catalog, the EDSL list and the closing pass
  - Files: `docs/tools.md`, `docs/dependencies.md`, `docs/creating-an-editor-module.md` (the alias, as a touch point), `src/editors/yaml/readme.md`, `src/editors/yaml/tests.md`, `src/editors/yaml/client/readme.md`
  - The catalog row: implemented, `editor/yaml`, "YAML editor", kind Editor. The dependency rows read back against the manifests.
  - The shared-piece check of Requirement 9.1 over the finished module: it imports the shared pieces and defines no underline, completion list or reference rule of its own. If a shared piece was widened, the change is named in the design by a small amendment and the JSON editor's tests are shown green over it (9.2).
  - `src/editors/yaml/readme.md`: the EDSL list with what YAML adds. Draft the addition to the issue the JSON editor opened and **ask the user before posting**. No `.eds` file; a test holds that none exists.
  - `tests.md`: the browser checks of the design's End-to-End section, in both themes, recorded with what was seen.
  - The coverage diff again, traced to files and strings; two traces read back to their artefacts.
  - Four gates, each judged by a captured exit code; a newly created worktree is built first.
  - _Leverage: `docs/tools.md`'s Editors section; the JSON editor's task 11 as the shape of this one_
  - _Requirements: 9.1, 9.2, 10.1, 10.2, 10.3, 11.1, 11.2, 11.4_
  - _Prompt: Implement the task for spec yaml-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Developer closing a specification | Task: Add the catalog row, bring the dependency inventory and the editor-module guide up to date, check that the module adds nothing beside the shared pieces, write the EDSL list and draft its addition to the issue, run and record the browser checks, and run the after-implementation coverage diff | Restrictions: Ask the user before posting anything to another repository; write no .eds file; record a browser check that was not run as not run; read two traces back; judge every gate by a captured exit code | _Leverage: docs/tools.md, the design's testing strategy | _Requirements: 9.1, 9.2, 10.1, 10.2, 10.3, 11.1, 11.2, 11.4 | Success: catalog, inventory and guide are true, the shared-piece check passes, the EDSL list exists, every browser check is recorded, the coverage diff balances with two traces read back, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._
