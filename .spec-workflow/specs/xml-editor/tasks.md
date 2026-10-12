# Tasks Document

One Developer owns this specification, per CLAUDE.md's one-owner-per-specification rule, and works it in one worktree (`.claude/worktrees/xmled`, branch `features/xml-editor`). It lands as one pull request into `develop`, with the four gates green, judged by exit codes captured before any other command.

**What this waits on.** The first landing of `json-editor` (its tasks 1 to 6) merged on `develop`. It does not wait on `yaml-editor`, and the two may be worked side by side by two Developers: they share no file except `docs/tools.md`, `docs/dependencies.md` and `docs/creating-an-editor-module.md`, each of which gains a row or a paragraph per editor.

**The order is the steering's: seam and wire first, then the backend, then the client.** Task 1 is first and can stop everything (Requirement 1.4). Task 2 is the only task that changes a core file.

**Rulings this document is written for.** Every open question of the requirements at its default (approved 2026-10-12), and both rulings of the design at their first option: A1, two calls on the shared `EditorService` that carry no format; A2, a DTD gives validation and no completion. The user ruled so in chat on 2026-10-12 and Requirements 2.4, 9.2 and 7.8 were amended the same day.

**Every guard is seen to fail.** A test written for a behaviour is run once against that behaviour removed, the red run is recorded in the task's implementation log, and the file is touched after the sabotage is restored so that the build does not keep the sabotaged binary.

---

## The coverage diff, run before this document was raised

**63 acceptance criteria in the requirements; 63 claimed by the tasks below; none unclaimed, nothing claimed that is not a criterion, and none claimed twice.**

| Requirement | Criteria | Claimed by |
| --- | --- | --- |
| 1 | 4 | 1 (1.1 to 1.4) |
| 2 | 5 | 3 (2.1 to 2.5) |
| 3 | 7 | 9 (3.1 to 3.7) |
| 4 | 7 | 5 (4.1 to 4.6), 9 (4.7) |
| 5 | 8 | 6 (5.1 to 5.8) |
| 6 | 6 | 7 (6.1 to 6.6) |
| 7 | 8 | 8 (7.1 to 7.8) |
| 8 | 5 | 3 (8.1, 8.3, 8.4, 8.5), 7 (8.2) |
| 9 | 6 | 2 (9.1, 9.2), 4 (9.6), 9 (9.3, 9.4, 9.5) |
| 10 | 3 | 10 (10.1 to 10.3) |
| 11 | 4 | 3 (11.3), 10 (11.1, 11.2, 11.4) |

**The non-functional requirements are claimed here by name:** Security (the resolver, the entity bound, both held by tests seen to fail; the request bound; no XSLT run) by tasks 2 and 4; Performance (the two bounds, a compiled schema kept) by tasks 1 and 6; Reliability (a throwing analyser answers with a sentence and is logged once) by task 2; Usability by task 9.

**One criterion is claimed by one task and finished by another, and says so.** Requirement 5.8 (a changed XSD re-validates an open document) is claimed by task 6, which makes the backend notice the change and report the files an answer depended on. The client half, watching those files and asking again, is task 9's, and task 9 names it.

---

## The tasks

- [x] 1. Settle what the platform's validator can answer
  - Files: `src/editors/xml/backend/EtAlii.Adp.Editor.Xml.Tests/Spike/ExpectedAtTheCaret.Tests.cs` (new), the test project file, `src/backend/EtAlii.Adp.slnx`
  - Create the test project and write the spike as tests that stay: `XmlSchemaValidator` fed a document that stops in the middle of an element, asked which elements and which attributes are expected, for a sequence, a choice and an all-group. The nine caret positions and the two attribute cases of the design's table are the cases.
  - Add what the scratch run did not try: a wildcard (`xs:any`), a substitution group, an element whose type is derived by extension, a document with two namespaces.
  - Measure, for a 5,000-line document and an XSD of 2,000 lines: validating the whole document, one completion in the middle, and compiling the schema. Write the figures to the test output.
  - Record answers and timings in the design by a small amendment, each marked as measured.
  - If the validator cannot answer for a document that stops at the caret, or the timings miss the Performance section by more than the design could close: stop, and put Q2 to the user again with the measurement, as a selection through the Scrum master.
  - _Leverage: the scratch run recorded in the design, section "What was run before this design"_
  - _Requirements: 1.1, 1.2, 1.3, 1.4_
  - _Prompt: Implement the task for spec xml-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer who verifies a platform API before building on it | Task: Write the spike as xUnit tests that stay: ask XmlSchemaValidator what it expects at a caret for sequence, choice, all-group, wildcard, substitution group, derived type and two namespaces, measure validation, completion and compilation on the sizes the requirement names, and record the results in the design by a small amendment | Restrictions: Build nothing else on the answers in this task; do not work around a failure, stop and raise Q2 again; tests pass TestContext.Current.CancellationToken; no blocking call in an async method | _Leverage: the design's scratch run | _Requirements: 1.1, 1.2, 1.3, 1.4 | Success: every case has a test with its observed answer, timings are measured and in the design, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [x] 2. The family's seam: an editor can be asked about its text
  - Files: `src/api/editors.proto`, `src/client/src/generated/editors_pb.ts` (generated, committed), `src/backend/EtAlii.Adp.Editor/IEditorAnalyzer.cs` (new), `src/backend/EtAlii.Adp.Editor/_Model/TextAnalysis.cs` and `TextCompletion.cs` (new), `src/backend/EtAlii.Adp.Diagram/EditorAnalyzers.cs` (new), `src/backend/EtAlii.Adp.Diagram/EditorService.cs`, the tests of `EditorService`, `docs/architecture.md` (the call legs), `docs/creating-an-editor-module.md`
  - Add `Analyze` and `Complete` to `EditorService` with the messages of the design. Offsets are UTF-16 units into the content as sent.
  - `IEditorAnalyzer` keyed by editor id; `EditorAnalyzers` resolves one, in the shape of `EditorSessionFactories`.
  - `EditorService`: resolve the path with `ProjectTextFile.TryResolve`; refuse content over 1,000,000 characters with a sentence; answer "This editor has no analysis." for an editor without an analyser; catch an analyser's exception, log it once with the project-relative path and never the text, and return it as `error`.
  - Tests with a fake analyser: each of those four, and that no type in the seam names a format (a test over the proto and the interface for the letters of the three formats).
  - `docs/architecture.md` gains the two calls where it lists the call legs; build a newly created worktree before trusting a green gate, since this task changes generated code.
  - _Leverage: `EditorService.SaveText`, `ProjectTextFile`, `EditorSessionFactories`; `ProblemSeverity` in `src/api/problems.proto`_
  - _Requirements: 9.1, 9.2_
  - _Prompt: Implement the task for spec xml-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Backend developer working on gRPC contracts in ASP.NET Core | Task: Add two unary calls to the shared EditorService, Analyze and Complete, that carry text, offsets and sentences and no format, with the IEditorAnalyzer seam a module answers them through, and the service-side bounds and error handling | Restrictions: No stream is added; no type in the seam names XML, JSON or YAML; the text of a document is never logged; no blocking call in an async method; the generated client code is committed and a fresh worktree is built before the gates are trusted; update docs/architecture.md in the same change | _Leverage: EditorService, ProjectTextFile, EditorSessionFactories | _Requirements: 9.1, 9.2 | Success: the two calls answer through a fake analyser, the four refusals are held by tests, the architecture page is true, four gates exit zero from a fresh tree | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [ ] 3. The module, its session and its examples
  - Files: `src/editors/xml/backend/EtAlii.Adp.Editor.Xml/` (`Editor.cs`, `XmlEditorSession.cs`, `XmlEditorSessionFactory.cs`, the project file and its `.DotSettings`), `src/editors/xml/backend/EtAlii.Adp.Editor.Xml.Tests/` (`EditorTests.cs`, session tests, `Examples.Tests.cs`), `src/backend/EtAlii.Adp.slnx`, `src/editors/xml/api/readme.md`, `src/editors/xml/client/package.json`, `src/editors/xml/client/register.ts`, `src/editors/xml/client/XmlEditorPanel.tsx` (the shared panel with the grammar only), `src/editors/xml/examples/`, `src/examples/editors/xml/`, `.gitattributes` (only if an example's bytes need it), `docs/dependencies.md`
  - The definition: id `xml`, title "XML editor", the claims `.xml`, `.xsd`, `.xsl` and `.xslt`. The session and its tests in the shape of the plain module's.
  - Examples: a document with an XSD beside it that it satisfies; one that breaks its XSD in the ways Requirement 6 names; one with a DTD; one that uses two namespaces with a schema each; one with no schema; one in ISO-8859-1 whose declaration says so. Check with `git ls-files --eol` how each is stored, and mark the ISO-8859-1 example so that git does not rewrite it.
  - `Examples.Tests`: every example opens, round-trips byte-identically, and has a byte-identical replica.
  - Tests: a claimed file no diagram claims resolves to this editor; the knowledge designer's `cities.xml` still opens as it does today and "Open as text" resolves to this editor; startup reports no claim conflict.
  - Add `@codemirror/lang-xml` to the module's manifest with its inventory row.
  - _Leverage: `src/editors/plain/` and `src/editors/json/`; `docs/creating-an-editor-module.md`; CLAUDE.md, *Line endings*, for byte-compared documents_
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 8.1, 8.3, 8.4, 8.5, 11.3_
  - _Prompt: Implement the task for spec xml-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Full-stack developer adding an editor module to EtAlii.Adp | Task: Create the xml editor module in the family's four folders with its definition, session, client registration and examples, including one in an encoding other than UTF-8, and the tests that hold its claims and the family's contract | Restrictions: No core or shell file changes in this task; the session holds nothing XML-specific; check stored line endings with git ls-files --eol and never with a CR grep; no blocking call in an async method and tests pass TestContext.Current.CancellationToken; run the JetBrains inspection for the new projects | _Leverage: the plain and json modules, creating-an-editor-module.md | _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 8.1, 8.3, 8.4, 8.5, 11.3 | Success: an .xml file opens in the XML editor with colour, every example round-trips byte-identically with its replica, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [ ] 4. One way to read XML, confined to the project
  - Files: `src/editors/xml/backend/EtAlii.Adp.Editor.Xml/XmlReaders.cs`, `ProjectXmlResolver.cs`, `SchemaReferenceRules.cs` (new), and in the test project `SchemaReferenceRules.Tests.cs`, `ProjectXmlResolver.Tests.cs`, `Security.Tests.cs`, `ReaderFactoryOnly.Tests.cs` (new)
  - `SchemaReferenceRules`: the C# twin of the client's resolver, held to `src/fixtures/schema-reference-cases.json` by a test that reads the same file the client's test reads.
  - `ProjectXmlResolver`: resolves against the including file's folder with those rules, reads through `TextFileBuffer`, and records every file read and every reference refused with its reason.
  - `XmlReaders`: the one factory for readers and schema sets, with the resolver, DTD parsing, the entity bound and line information.
  - **The two security guards, each seen to fail with its protection removed**: a document whose `DOCTYPE` names a file outside the project root, with a canary file there whose content must appear in no answer; a document of nested entities that must be refused in under a second.
  - A test over the module's source: no reader and no schema set is created outside `XmlReaders`, and the platform's default resolver is named nowhere.
  - _Leverage: `TextFileBuffer`; the shared case file from `json-editor` task 6_
  - _Requirements: 9.6_
  - _Prompt: Implement the task for spec xml-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer with a security focus on XML processing | Task: Write the single factory through which the module reads XML and compiles schemas, the resolver that confines every reference to the project, and the C# twin of the shared schema reference rules, with the two security guards and the source-level test that nothing bypasses the factory | Restrictions: No network address and no file outside the project root is ever read; entity expansion is bounded; both security tests must be seen to fail with the protection removed and the red runs recorded; the rules are held to the same case file as the client's, read from the same path | _Leverage: TextFileBuffer, schema-reference-cases.json | _Requirements: 9.6 | Success: the shared cases pass in C#, both guards were red without their protection and are green with it, the factory-only test holds, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [ ] 5. Mistakes in the XML itself
  - Files: `src/editors/xml/backend/EtAlii.Adp.Editor.Xml/WellFormedness.cs`, `XmlAnalyzer.cs` (new), `Editor.cs` (registers the analyser), `WellFormedness.Tests.cs` (new)
  - Read the text to its end or its first error; turn the error into one finding at its line and column, converted to an offset into the text as sent, CRLF and LF alike.
  - Tests assert offsets and severity, and that the platform's sentence names both tags of a mismatch, a repeated attribute and an undeclared prefix; where a sentence does not, the finding adds what is missing.
  - The declared encoding against the one `TextFileBuffer` detected for the file on disk: a difference is one warning on the declaration.
  - When the reader stopped, the status says the rest was not checked.
  - `XmlAnalyzer` implements `IEditorAnalyzer` and, in this task, answers well-formedness only.
  - _Leverage: task 2's seam, task 4's `XmlReaders`_
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6_
  - _Prompt: Implement the task for spec xml-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer working with System.Xml | Task: Report whether a text is well-formed XML as one finding at the right offset with a sentence that names what is wrong, warn when the declared encoding differs from the stored one, say in the status that the rest was not checked, and register the module's analyser | Restrictions: Every reader comes from XmlReaders; one error is reported, not a guessed list; offsets are into the text as sent and are tested with CRLF and LF; logging as CLAUDE.md prescribes; every test is seen to fail against the behaviour removed | _Leverage: XmlReaders, IEditorAnalyzer | _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6 | Success: one passing test per criterion, each seen to fail, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [ ] 6. Which schema applies, compiled once
  - Files: `src/editors/xml/backend/EtAlii.Adp.Editor.Xml/SchemaAssociation.cs`, `SchemaCache.cs` (new), `XmlAnalyzer.cs`, `SchemaAssociation.Tests.cs`, `SchemaCache.Tests.cs` (new)
  - `SchemaAssociation`: from the prolog and the root's start tag only, the references of `xsi:noNamespaceSchemaLocation`, the pairs of `xsi:schemaLocation` and the `DOCTYPE`'s system identifier with its internal subset; an `xml-model` instruction and an XSD that declares version 1.1 answered as named and not supported; a root that is `xs:schema` marks the document as a schema, and what compiling it reports becomes findings in it.
  - `SchemaCache`: a compiled set per set of root schemas, kept with the last-write time of every file the resolver read for it, compared before each use and compiled again on a difference; dropped when unused for ten minutes. Includes, imports and redefines go through the resolver.
  - A reference that cannot be resolved becomes one information finding where it is named, with the reason. The status names the schema by target namespace or file name, or says "No schema".
  - The answer lists the project files it depended on (`depends_on`), which is the backend half of Requirement 5.8; a test changes an included file and sees the next answer differ with no restart.
  - A test holds that a second request with nothing changed does not compile again.
  - _Leverage: task 4's resolver and rules; task 1's measured compile time_
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8_
  - _Prompt: Implement the task for spec xml-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer working with System.Xml.Schema | Task: Read which schemas a document names, resolve and compile them once through the confined resolver, notice when any file of a schema changes, name the schema in the status, report what is named but not supported, and list the files an answer depended on | Restrictions: Read only the prolog and the root's start tag to find references; every schema set comes from XmlReaders; nothing is read beyond the project; a compiled schema is not compiled again while nothing changed, held by a test; every test is seen to fail against the behaviour removed | _Leverage: ProjectXmlResolver, SchemaReferenceRules | _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8 | Success: one passing test per criterion, a changed include changes the next answer, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [ ] 7. Validation against the schema
  - Files: `src/editors/xml/backend/EtAlii.Adp.Editor.Xml/SchemaValidation.cs` (new), `XmlAnalyzer.cs`, `SchemaValidation.Tests.cs` (new)
  - A validating reader over the text with the cached set, or DTD validation when the document has a `DOCTYPE` and no XSD. Each event becomes a finding with the platform's severity and sentence.
  - Ranges: an element that is not allowed, on its name in the start tag, with what was expected; a missing required attribute or child, on the element that lacks it, naming it; a value that does not fit its type, on the value.
  - Not well-formed: validation is not run, schema findings are withheld, and the status says the schema was not checked.
  - A schema that does not compile: said once, naming the file and its first error, and no schema findings.
  - **Nothing read is written back**: a test validates every example against a schema that supplies attribute defaults, saves, and compares bytes.
  - _Leverage: tasks 5 and 6_
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 8.2_
  - _Prompt: Implement the task for spec xml-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer working with System.Xml.Schema | Task: Validate a well-formed document against its XSD or DTD and turn each validation event into a finding on the element name, the lacking element or the value it concerns, withhold schema findings when the document is not well-formed, and say once when the schema itself does not compile | Restrictions: Every reader comes from XmlReaders; assert offsets and severities; schema defaults never reach the text; a schema finding is never shown for a document that is not well-formed; every test is seen to fail against the behaviour removed | _Leverage: SchemaCache, WellFormedness | _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 8.2 | Success: one passing test per criterion, every example byte-identical after validation and save, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [ ] 8. Completion from the schema
  - Files: `src/editors/xml/backend/EtAlii.Adp.Editor.Xml/CaretContext.cs`, `XmlCompletion.cs` (new), `XmlAnalyzer.cs`, `CaretContext.Tests.cs`, `XmlCompletion.Tests.cs` (new)
  - `CaretContext`: from the text and the offset, whether the caret is in content, in a start tag after the name, inside an attribute value, or somewhere that cannot be told (a comment, a CDATA section, after an error that hides the position). The last answers nothing.
  - In content: the text before the caret is fed to the validator and the expected elements are offered, wildcards left out. Each insert is well-formed: required attributes with empty values and a tab stop in the first, then a self-closing tag where the type allows no content, or a start and end tag with a tab stop between.
  - In a start tag: the attributes not yet present, required ones first. In a value: the values of an enumeration, and `true` and `false` for a boolean.
  - Namespaces: an element is offered with the prefix in scope for its namespace; one whose namespace has no declaration in scope is offered with a note and adds no declaration.
  - Documentation from `xs:documentation` beside the item, and for a name already in the document when asked at that name.
  - With a DTD and no XSD nothing is offered and the status says "DTD: checked, no suggestions". With no schema nothing is offered from here: the end tag is the client's.
  - Tests insert each kind of completion and assert the result is well-formed.
  - _Leverage: task 1's spike tests as the proven feeding loop; task 6's cache_
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 7.8_
  - _Prompt: Implement the task for spec xml-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer working with XmlSchemaValidator | Task: Decide where the caret is, and offer from the XSD the elements allowed there, the attributes not yet present and the values a type limits, each inserted well-formed with the document's own namespace prefixes and with the schema's documentation | Restrictions: Offer nothing when the position cannot be told and nothing from a DTD; never add a namespace declaration silently; every accepted completion leaves well-formed XML, held by tests; every reader comes from XmlReaders; every test is seen to fail against the behaviour removed | _Leverage: the spike tests, SchemaCache | _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 7.7, 7.8 | Success: one passing test per criterion, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [ ] 9. The client: colour, tags, and asking the backend
  - Files: `src/editors/xml/client/XmlEditorPanel.tsx`, `xmlAnalysis.ts`, `xmlPrefixColour.ts` (new), `xmlAnalysis.test.ts`, `XmlEditorPanel.test.tsx` (new)
  - A test over a mounted editor: each kind of token of Requirement 3.1 carries a class of the shared scheme; the prefix of a name carries one of its own; a document with a mistake keeps classes on what can be read; the tag matching the one at the caret is marked; folding an element, a comment and a CDATA section leaves the text unchanged. The module declares no colour.
  - Typing the `>` of a start tag inserts the end tag and typing `</` offers the open element's name, each one undo away.
  - `xmlAnalysis` over the two calls: at most one request for findings waiting and an older one in flight aborted; an answer shown only against the text it was asked about; on every edit the findings of this source are cleared at once and the new ones shown when they arrive.
  - `depends_on` handed to `SchemaFiles`; a new revision of any of those files asks again. This is the client half of Requirement 5.8.
  - A failed call: colour and tag matching stay, and the status says once that checking is unavailable.
  - _Leverage: the shared pieces of `json-editor`; `@codemirror/lang-xml`'s own tag closing; the generated client of task 2_
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 4.7, 9.3, 9.4, 9.5_
  - _Prompt: Implement the task for spec xml-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Front-end developer working on CodeMirror 6 and gRPC-web calls | Task: Give the XML editor its colour and tag behaviour through the shared scheme and CodeMirror's XML package, and write xmlAnalysis, which asks the backend for findings and completions, never shows an answer for a text that has changed, clears stale findings on edit, re-asks when a schema file changes and says when the backend cannot be reached | Restrictions: No colour declared in the module; no stream is added; assert classes, not colours; a stale answer never reaches the screen and a test holds it; every test is seen to fail against the behaviour removed | _Leverage: analysisExtensions, SchemaFiles, lang-xml | _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 4.7, 9.3, 9.4, 9.5 | Success: one passing test per criterion, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._

- [ ] 10. Documentation, the catalog, the EDSL list and the closing pass
  - Files: `docs/tools.md`, `docs/dependencies.md`, `docs/creating-an-editor-module.md` (the analyser seam, as a touch point), `src/editors/xml/readme.md`, `src/editors/xml/tests.md`, `src/editors/xml/client/readme.md`
  - The catalog row: implemented, `editor/xml`, "XML editor", kind Editor. The dependency rows read back against the manifests; nothing is added on the backend.
  - `src/editors/xml/readme.md`: the EDSL list with what XML adds. Draft the addition to the issue the JSON editor opened and **ask the user before posting**. No `.eds` file; a test holds that none exists.
  - `tests.md`: the browser checks of the design's End-to-End section, in both themes, recorded with what was seen, including the backend stopped.
  - The coverage diff again, traced to files and strings; two traces read back to their artefacts.
  - Four gates, each judged by a captured exit code; a newly created worktree is built first.
  - _Leverage: `docs/tools.md`'s Editors section; the JSON editor's task 11 as the shape of this one_
  - _Requirements: 10.1, 10.2, 10.3, 11.1, 11.2, 11.4_
  - _Prompt: Implement the task for spec xml-editor, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Developer closing a specification | Task: Add the catalog row, bring the dependency inventory and the editor-module guide up to date, write the EDSL list and draft its addition to the issue, run and record the browser checks, and run the after-implementation coverage diff | Restrictions: Ask the user before posting anything to another repository; write no .eds file; record a browser check that was not run as not run; read two traces back; judge every gate by a captured exit code | _Leverage: docs/tools.md, the design's testing strategy | _Requirements: 10.1, 10.2, 10.3, 11.1, 11.2, 11.4 | Success: catalog, inventory and guide are true, the EDSL list exists, every browser check is recorded, the coverage diff balances with two traces read back, four gates exit zero | Instructions: set this task to [-] in tasks.md before starting, log the work with log-implementation when done, then set it to [x]._
