# Tasks Document

One Developer owns this specification, per CLAUDE.md's one-owner-per-specification rule, and works it in one worktree (`.claude/worktrees/jsoned`, branch `features/json-editor`).

**Two landings.** Tasks 1 to 6 are the pieces the three editors share and land as one pull request into `develop`: the `yaml-editor` and `xml-editor` specifications wait on exactly that merge and on nothing after it. Tasks 7 to 11 are the JSON editor itself and land as a second pull request. Each landing leaves the four gates green, judged by exit codes captured before any other command.

**The order is load-bearing in one place.** Task 1 is first and can stop everything: if the library does not run in this client, the Developer stops and the user chooses (Requirement 1.4). Nothing else is started on a workaround meanwhile.

**Rulings this document is written for.** Every open question of the requirements at its default (approved 2026-10-12), and ruling A1 of the design at its first option: a schema file of the project is read over the connection's existing stream. The user ruled so in chat on 2026-10-12 and Requirement 9.6 was amended the same day.

**Every guard is seen to fail.** A test written for a behaviour is run once against that behaviour removed, the red run is recorded in the task's implementation log, and the file is touched after the sabotage is restored so that the build does not keep the sabotaged output.

---

## The coverage diff, run before this document was raised

**62 acceptance criteria in the requirements; 62 claimed by the tasks below; none unclaimed, nothing claimed that is not a criterion, and none claimed twice.**

| Requirement | Criteria | Claimed by |
| --- | --- | --- |
| 1 | 4 | 1 (1.1 to 1.4) |
| 2 | 5 | 7 (2.1 to 2.5) |
| 3 | 6 | 3 (3.3, 3.5, 3.6), 8 (3.1, 3.2, 3.4) |
| 4 | 6 | 4 (4.4, 4.5, 4.6), 8 (4.1, 4.2, 4.3) |
| 5 | 9 | 6 (5.2, 5.3, 5.8, 5.9), 9 (5.1, 5.4, 5.5, 5.6, 5.7) |
| 6 | 6 | 9 (6.1 to 6.6) |
| 7 | 7 | 5 (7.3, 7.5, 7.6), 10 (7.1, 7.2, 7.4, 7.7) |
| 8 | 5 | 7 (8.1 to 8.5) |
| 9 | 7 | 2 (9.1), 3 (9.2), 4 (9.3), 5 (9.4), 6 (9.5, 9.6), 11 (9.7) |
| 10 | 3 | 11 (10.1 to 10.3) |
| 11 | 4 | 7 (11.3), 11 (11.1, 11.2, 11.4) |

**The non-functional requirements are not numbered and are claimed here by name:** Performance (typing never waits, the one-second and 300-millisecond bounds, the size above which nothing is validated) by tasks 4, 9 and 10; Security (no code from a schema, nothing read beyond the project or from the network, descriptions as text) by tasks 5 and 6; Reliability (a throwing service, a schema that refers to itself) by task 9; Usability (keyboard reach) by tasks 4 and 5.

**Two criteria are split and say so.** Requirement 3.3 (contrast in both themes) is claimed by task 3, which adds the tokens and their guard; task 8 only uses them. Requirement 6.5 (the list of supported dialects) is claimed by task 9; the list itself is produced by task 1's per-dialect tests and written into the design by a small amendment.

---

## The tasks

- [x] 1. Prove the schema service in this client
  - Files: `src/client/src/editors/spike/jsonLanguageService.spike.test.ts` (new), `src/client/package.json` (the dependency, moved to the module's own manifest in task 7), `docs/dependencies.md`
  - Add `vscode-json-languageservice` at an exact version and its row in the dependency inventory, with the licence read from the installed package's own metadata.
  - Write the spike as tests that stay: for one fixed document and one fixed schema, the findings, the completions at one position and the hover text at one position. One further test per JSON Schema dialect (draft-04, draft-06, draft-07, 2019-09, 2020-12), each asserting one finding that only that dialect's keyword gives.
  - Run it under `npm test`. Then build the client for production and open it in a browser with a temporary route that runs the same calls; record what the browser's console shows. Remove the route before the pull request.
  - Measure the size the library adds to the production bundle (the build's own report, before and after) and the time of one validation and one completion for a 5,000-line document against a 2,000-line schema.
  - Record all of it in the design by a small amendment: the answers, the two sizes, the timings, the dialects that passed.
  - If either run fails: stop, and put the choice of Requirement 1.4 to the user as a selection through the Scrum master.
  - _Leverage: the scratch run recorded in the design, section "What was run before this design"; `src/client/vite.config.ts`_
  - _Requirements: 1.1, 1.2, 1.3, 1.4_
  - _Prompt: Implement the task for spec json-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Front-end developer who verifies a third-party library before building on it | Task: Add vscode-json-languageservice, write the spike as Vitest tests that stay (findings, completion, hover, one test per schema dialect), run it under npm test and in a production build opened in a browser, measure bundle size and timings, and record the results in the design by a small amendment | Restrictions: Do not design or build anything else on the library in this task; do not work around a failure, stop and raise the selection of Requirement 1.4; read the licence from the package's own metadata; the temporary browser route does not reach the pull request | _Leverage: the design's scratch run, vite.config.ts | _Requirements: 1.1, 1.2, 1.3, 1.4 | Success: the spike tests are green under npm test, the browser run is recorded with what the console showed, sizes and timings are measured and in the design, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [x] 2. Let an editor module declare its own dependencies
  - Files: `src/package.json`, `src/editors/markdown/client/package.json` and `src/editors/plain/client/package.json` (only if the workspace glob requires every match to have one), `src/package-lock.json`
  - Add `editors/*/client` to the workspaces. Find out whether npm accepts a matched folder without a `package.json`; if not, give the two existing modules a manifest with no dependencies.
  - Show that `DependencyInventoryTests` counts the new workspace: add a throwaway dependency to one editor manifest, see the guard fail for the missing row, and remove it.
  - _Leverage: `src/backend/EtAlii.Adp.Repository.Tests/` (`DependencyInventoryTests`, its `Expand`), the `diagrams/*/client` workspace as the shape to copy_
  - _Requirements: 9.1_
  - _Prompt: Implement the task for spec json-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Build engineer for an npm workspace | Task: Add editors/*/client to the workspaces of src/package.json so an editor module can declare dependencies, and prove the dependency inventory guard counts it | Restrictions: Change no dependency version; do not edit the guard unless it cannot expand the new glob, and then extend Expand deliberately with a test; build a newly created worktree before trusting a green gate | _Leverage: DependencyInventoryTests, the diagrams workspace | _Requirements: 9.1 | Success: npm install resolves from a clean tree, the guard was seen to fail on an uninventoried editor dependency and is green without it, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [x] 3. One colour scheme for every editor
  - Files: `src/client/src/editors/editorHighlight.ts` (new), `src/client/src/editors/BaseTextEditor.tsx`, `src/client/src/index.css`, `src/client/src/theme.contrast.test.ts`, `src/client/src/themeTokens.test.ts`, `src/client/src/editors/editorHighlight.test.tsx` (new), `src/editors/markdown/client/MarkdownEditorPanel.test.tsx`
  - Declare the nine `--color-syntax-*` tokens for the light and the dark theme as literal hex.
  - Write the `HighlightStyle` that maps Lezer's tags to them, and install it in `BaseTextEditor` with `bracketMatching()` and `foldGutter()`.
  - Extend the contrast guard to the nine tokens against the editor's surface in both themes, at the ratio it already requires of text. See it fail with one token set to a colour that does not pass.
  - A test over a mounted markdown editor asserts that a heading carries a highlight class: the markdown editor gets colour by this task (Q5), and its panel file does not change.
  - A test asserts that folding changes nothing in the document text.
  - _Leverage: `@codemirror/language` (already a dependency), `src/client/src/themeContrast.ts`, `src/client/src/moduleThemeContrast.test.ts` for why the guard is token-level_
  - _Requirements: 9.2, 3.3, 3.5, 3.6_
  - _Prompt: Implement the task for spec json-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Front-end developer working on CodeMirror 6 themes | Task: Add nine syntax colour tokens for both themes, one shared HighlightStyle bound to them, install it with bracket matching and fold gutter in BaseTextEditor, and extend the token-level contrast guard to them | Restrictions: Literal hex in the theme so the guard can compute; no colour written in a module; do not assert colour through jsdom, which applies no CSS; the guard must be seen to fail; follow src/.editorconfig | _Leverage: themeContrast.ts, theme.contrast.test.ts, @codemirror/language | _Requirements: 9.2, 3.3, 3.5, 3.6 | Success: every token passes in both themes, the markdown editor shows highlight classes, folding leaves the text unchanged, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [x] 4. One presentation of findings
  - Files: `src/client/src/editors/editorAnalysis.ts` (new: `EditorFinding`, `EditorAnalysis`), `src/client/src/editors/analysisExtensions.ts` (new), `src/client/src/editors/TextEditorPanel.tsx`, `src/client/src/editors/editors.css`, `src/client/src/editors/analysisExtensions.test.tsx` (new), `src/client/package.json`, `docs/dependencies.md`
  - Add `@codemirror/lint`. Give `TextEditorPanel` an optional `analysis` and turn it into a linter with a 400 ms delay, a gutter mark, an underline whose line style differs per severity, and the keyboard path to the next finding.
  - The status line shows the counts of errors and warnings when there are any, and the analysis's own status after them.
  - An answer for a text that is no longer the view's text is dropped.
  - Tests with a fake analysis: the three carriers of a finding, the counts, the stale answer, two findings on one range both readable, and that a document with findings still saves.
  - _Leverage: `TextEditorPanel`'s status line and `data-testid="dirty-indicator"`; the theme's `--color-danger` and `--color-warning`_
  - _Requirements: 9.3, 4.4, 4.5, 4.6_
  - _Prompt: Implement the task for spec json-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Front-end developer working on CodeMirror 6 extensions | Task: Define EditorFinding and EditorAnalysis, and present findings from any analysis in the shared text panel: underline per severity, gutter mark, sentence on hover and from the keyboard, counts in the status line, stale answers dropped | Restrictions: The shared code names no format; extensions are built once at mount, so read what changes through references; severity is never carried by colour alone; a finding never blocks a save; every test is seen to fail against the behaviour removed | _Leverage: TextEditorPanel, BaseTextEditor, @codemirror/lint | _Requirements: 9.3, 4.4, 4.5, 4.6 | Success: a fake analysis's findings show in all three ways, counts are right and absent when zero, a stale answer never reaches the screen, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [x] 5. One presentation of completions and hover text
  - Files: `src/client/src/editors/editorAnalysis.ts`, `src/client/src/editors/analysisExtensions.ts`, `src/client/src/editors/analysisExtensions.test.tsx`, `src/client/package.json`, `docs/dependencies.md`
  - Add `@codemirror/autocomplete`. Define `EditorCompletion`, `EditorCompletionList` and `EditorHover`; install completion with the analysis as its only source, and a hover tooltip.
  - The list opens on typing and on Ctrl+Space and is walked, accepted and dismissed from the keyboard. An item's description is shown beside it.
  - Descriptions and hover text are set as text. A test hands over a description holding markup and asserts no element was created from it.
  - An insert with tab stops is applied as a snippet.
  - _Leverage: task 4's `analysisExtensions.ts`_
  - _Requirements: 9.4, 7.3, 7.5, 7.6_
  - _Prompt: Implement the task for spec json-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Front-end developer working on CodeMirror 6 extensions | Task: Define the completion and hover shapes and present them from any analysis: a list with descriptions that opens on typing and on Ctrl+Space and works from the keyboard alone, hover text for a described name, snippet inserts | Restrictions: Nothing that comes from a schema is assigned as HTML, ever, and a test holds it; the shared code names no format; every test is seen to fail against the behaviour removed | _Leverage: analysisExtensions.ts, @codemirror/autocomplete | _Requirements: 9.4, 7.3, 7.5, 7.6 | Success: a fake analysis's completions and hover show and work from the keyboard, markup in a description stays text, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [x] 6. Where a schema comes from, once
  - Files: `src/client/src/editors/schemaReference.ts` (new), `src/client/src/editors/schemaReference.test.ts` (new), `src/fixtures/schema-reference-cases.json` (new), `src/client/src/editors/SchemaFiles.tsx` (new), `src/client/src/editors/SchemaFiles.test.tsx` (new), `docs/creating-an-editor-module.md`
  - Write the case file first: relative references in the same folder, a subfolder and a parent folder; a reference that steps above the project root; `http`, `https` and an unknown scheme; a bundled address; an empty reference. Each case names its expected target or its reason.
  - Write `resolveSchemaReference` to the cases. A reference that leaves the root is refused and never clamped; one test holds exactly that against URL arithmetic.
  - Write `SchemaFiles`: one child per project path, each reading through `useEditorText`, reporting text, revision and failure upward. Tests with a stubbed stream: a file arriving, a new revision, a failed open, a path added later, a path removed.
  - Describe the five shared pieces in `docs/creating-an-editor-module.md` as touch points a module author now has. This is the part of Requirement 9.7 that must merge with the first landing; task 11 completes it.
  - _Leverage: `src/client/src/editors/useEditorText.ts`; the design's section "Shared piece 5"_
  - _Requirements: 9.5, 9.6, 5.2, 5.3, 5.8, 5.9_
  - _Prompt: Implement the task for spec json-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Front-end developer who writes rules as pure functions with shared test cases | Task: Write the shared case file of schema references, the pure resolver held to it, and SchemaFiles, the render-nothing component that reads a growing set of project files over the connection's existing stream and reports changes | Restrictions: The resolver does no I/O and knows no format; a reference above the project root is refused and never clamped; no network address is ever fetched; add no backend call and no stream; the case file is the contract the XML editor's C# twin is also held to, so its shape is data only | _Leverage: useEditorText, the design | _Requirements: 9.5, 9.6, 5.2, 5.3, 5.8, 5.9 | Success: every case passes, the clamp test was seen to fail with URL arithmetic, SchemaFiles reports arrival, change and failure, the documentation names the shared pieces, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [ ] 7. The module, its session and its examples
  - Files: `src/editors/json/backend/EtAlii.Adp.Editor.Json/` (`Editor.cs`, `JsonEditorSession.cs`, `JsonEditorSessionFactory.cs`, the project file and its `.DotSettings`), `src/editors/json/backend/EtAlii.Adp.Editor.Json.Tests/` (`EditorTests.cs`, session tests, `Examples.Tests.cs`), `src/backend/EtAlii.Adp.slnx`, `src/editors/json/api/readme.md`, `src/editors/json/client/package.json`, `src/editors/json/client/register.ts`, `src/editors/json/client/JsonEditorPanel.tsx` (the shared panel with the grammar only), `src/editors/json/examples/`, `src/examples/editors/json/`
  - The definition: id `json`, title "JSON editor", the claim `.json`. The session and its tests in the shape of the plain module's.
  - Examples: a document with a schema beside it that it satisfies; a document that breaks its schema in the ways Requirement 6 names; a document with no schema; a CRLF document and one with a byte-order mark. Data taken from elsewhere follows the vendored example data rule, licence file included.
  - `Examples.Tests`: every example opens, round-trips byte-identically, and has a byte-identical replica under `src/examples/editors/json/`.
  - Tests: a `.json` file no diagram claims resolves to this editor; the Databricks diagram's `.json` body still opens its diagram and "Open as text" resolves to this editor; startup reports no claim conflict; a save goes through `SaveText` and is one undo away.
  - Move the two JSON dependencies from the client's manifest to the module's.
  - _Leverage: `src/editors/plain/` and `src/editors/markdown/` as worked examples; `docs/creating-an-editor-module.md`'s checklist; CLAUDE.md, *Folders and namespaces* for the `.DotSettings`_
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 8.1, 8.2, 8.3, 8.4, 8.5, 11.3_
  - _Prompt: Implement the task for spec json-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Full-stack developer adding an editor module to EtAlii.Adp | Task: Create the json editor module in the family's four folders with its definition, session, client registration and examples, and the tests that hold its claims and the family's contract | Restrictions: No core file and no shell file changes; the session holds nothing JSON-specific; nothing reformats, reorders or re-indents; no blocking call in an async method and tests pass TestContext.Current.CancellationToken; logging as CLAUDE.md prescribes; run the JetBrains inspection for the new projects | _Leverage: the plain and markdown modules, creating-an-editor-module.md | _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 8.1, 8.2, 8.3, 8.4, 8.5, 11.3 | Success: a .json file opens in the JSON editor with colour, every example round-trips byte-identically with its replica, the diagram case is held, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [ ] 8. Colour and the mistakes of JSON itself
  - Files: `src/editors/json/client/JsonEditorPanel.tsx`, `src/editors/json/client/jsonAnalysis.ts` (new), `src/editors/json/client/jsonAnalysis.test.ts` (new), `src/editors/json/client/JsonEditorPanel.test.tsx` (new)
  - `jsonAnalysis` loads the service with `import()` and answers findings for a document without a schema: syntax errors at their place, a duplicate property name as a warning, a comment or a trailing comma as an error.
  - Tests over a mounted editor: a property name and a string value carry different highlight classes; a document with a missing brace keeps highlight classes on what precedes it; each kind of token of Requirement 3.1 carries a class.
  - Tests of the analysis assert ranges and severities, not sentences.
  - _Leverage: tasks 3 and 4; the design's section "What was run before this design" for the settings that give each severity_
  - _Requirements: 3.1, 3.2, 3.4, 4.1, 4.2, 4.3_
  - _Prompt: Implement the task for spec json-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Front-end developer integrating a language service into a CodeMirror editor | Task: Write jsonAnalysis as the only file that knows vscode-json-languageservice, loaded on first use, answering JSON's own findings (syntax errors, duplicate names, comments and trailing commas), and hold the colour criteria with tests over a mounted editor | Restrictions: Nothing but jsonAnalysis.ts imports the service; assert ranges and severities, not the library's sentences; the service is loaded with import() so it is not in the shell's bundle; every test is seen to fail against the behaviour removed | _Leverage: analysisExtensions, editorHighlight, the design | _Requirements: 3.1, 3.2, 3.4, 4.1, 4.2, 4.3 | Success: the six criteria each have a passing test that was seen to fail, the shell's bundle does not contain the service, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [ ] 9. Which schema applies, and validation against it
  - Files: `src/editors/json/client/jsonAnalysis.ts`, `src/editors/json/client/jsonAnalysis.test.ts`, `src/editors/json/client/JsonEditorPanel.tsx`, `src/editors/json/client/JsonEditorPanel.test.tsx`
  - The document gets a URI of the module's scheme; the service's schema requests go through `resolveSchemaReference` and are answered from `SchemaFiles` or refused with the reason. The raw reference is checked before any URI arithmetic.
  - Schema findings are errors, a deprecated property a warning. The "not allowed" finding on the document's own `$schema` property is dropped, recognised by the property it sits on.
  - A reference that cannot be resolved, a schema that is not valid JSON and an unsupported dialect each become one information finding on the `$schema` line, with the reason. A document that is itself a schema is validated against its dialect.
  - The status names the schema, or says "No schema", or says why nothing is checked. Above 1,000,000 characters nothing is validated and the status says so.
  - A throw inside the service is caught once: syntax findings from the grammar stay and the status says schema support stopped. A schema that refers to itself is held by a test with a time limit.
  - A panel test: a schema arriving after the document makes findings appear; a new revision of the schema changes them without reopening.
  - Measure one second for findings and record it: the 5,000-line document and 2,000-line schema of the Performance section, in the implementation log.
  - _Leverage: task 6's resolver and `SchemaFiles`; task 1's dialect tests_
  - _Requirements: 5.1, 5.4, 5.5, 5.6, 5.7, 6.1, 6.2, 6.3, 6.4, 6.5, 6.6_
  - _Prompt: Implement the task for spec json-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Front-end developer integrating JSON Schema validation | Task: Connect the document's $schema to the shared resolver and SchemaFiles, validate against the schema with the severities the requirements name, turn every way a schema can be unavailable into one information finding, name the schema in the status line, and keep the editor working when the service throws | Restrictions: Check the raw reference before any URL arithmetic; no schema is read from outside the project or from the network; findings are never invented for text the parser could not read; recognise the dropped $schema finding by its property, not by its sentence; every test is seen to fail against the behaviour removed | _Leverage: resolveSchemaReference, SchemaFiles, the dialect tests | _Requirements: 5.1, 5.4, 5.5, 5.6, 5.7, 6.1, 6.2, 6.3, 6.4, 6.5, 6.6 | Success: one passing test per criterion, re-validation on a changed schema shown in a panel test, the measured time recorded, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [ ] 10. Completion from the schema
  - Files: `src/editors/json/client/jsonAnalysis.ts`, `src/editors/json/client/jsonAnalysis.test.ts`
  - Map the service's completions to `EditorCompletion`: properties not yet present with required ones first, the values a schema limits, and inserts that are well-formed where they land (quotes, colon, the value's opening shape, a comma where the object needs one).
  - The service's tab stops are converted to the snippet form the shared presentation applies.
  - Without a schema the service is not asked and nothing is offered.
  - Tests insert each kind of completion into a document and assert the result parses as JSON.
  - Measure 300 milliseconds for a completion list on the Performance section's document and record it.
  - _Leverage: task 5's presentation; task 9's schema wiring_
  - _Requirements: 7.1, 7.2, 7.4, 7.7_
  - _Prompt: Implement the task for spec json-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Front-end developer integrating schema-driven completion | Task: Map the language service's completions to the shared completion shape so that properties, limited values and well-formed inserts are offered where the schema allows them, and nothing is offered without a schema | Restrictions: Never guess a completion from other files; every accepted completion leaves well-formed JSON, held by tests that parse the result; every test is seen to fail against the behaviour removed | _Leverage: analysisExtensions, jsonAnalysis | _Requirements: 7.1, 7.2, 7.4, 7.7 | Success: one passing test per criterion, the measured time recorded, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [ ] 11. Documentation, the catalog, the EDSL list and the closing pass
  - Files: `docs/tools.md`, `docs/dependencies.md`, `docs/creating-an-editor-module.md`, `src/editors/json/readme.md`, `src/editors/json/tests.md`, `src/editors/json/client/readme.md`
  - The catalog row: implemented, `editor/json`, "JSON editor", kind Editor. The dependency rows read back against the manifests, licences re-read from the installed packages.
  - `docs/creating-an-editor-module.md`: the workspace sentence, the checklist, and the shared pieces as they finally are.
  - `src/editors/json/readme.md`: the list of what an EDSL specification of this editor would state, each item beside the file that implements it. Draft the issue for etalii-adp/etalii.adp from it and **ask the user before posting**, because it leaves this repository; link it from the readme once posted. No `.eds` file is written and no code reads one; a test over the module's folder holds that none exists.
  - `tests.md`: the browser checks of the design's End-to-End section, run against a local build in both themes and recorded with what was seen. A check that cannot be automated and was not run is recorded as not run, never as pending by default.
  - Run the coverage diff again, traced to files and strings; read two traces back to their artefacts and confirm each evidences its own criterion.
  - Four gates, each judged by an exit code captured before any other command; a newly created worktree is built first.
  - _Leverage: `docs/tools.md`'s Editors section; CLAUDE.md, *Checking that a specification's tasks cover its requirements*_
  - _Requirements: 9.7, 10.1, 10.2, 10.3, 11.1, 11.2, 11.4_
  - _Prompt: Implement the task for spec json-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Developer closing a specification | Task: Add the catalog row, bring the dependency inventory and the editor-module guide up to date, write the EDSL list and draft its issue, run and record the browser checks, and run the after-implementation coverage diff | Restrictions: Ask the user before posting anything to another repository; write no .eds file; record a browser check that was not run as not run; a balancing count is not a trace, so read two traces back; judge every gate by a captured exit code | _Leverage: docs/tools.md, the design's testing strategy | _Requirements: 9.7, 10.1, 10.2, 10.3, 11.1, 11.2, 11.4 | Success: catalog, inventory and guide are true, the EDSL list exists and its issue is posted or awaits the user, every browser check is recorded, the coverage diff balances and two traces were read back, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._
