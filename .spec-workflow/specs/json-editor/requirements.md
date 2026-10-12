# Requirements Document

## Introduction

The request, as the project's coordinator relayed it on 2026-10-12:

> Write three separate spec-workflow (MCP) specs, one each for an XML, a JSON and a YAML editor tool. Each must cover syntax colouring, schema validation where a schema is available, and completion based on the schema where available. Third-party editor libraries are allowed only if they are Apache-2.0 compatible and the de-facto standard; name the choice and why in each spec.

**This is the JSON editor**, the first of the three. It is an **editor** in ADP's terms: typing text is the core interaction. Its origin is `editor/json`, its module is `src/editors/json/`, and its specification language is EDSL (`.eds`), which is a placeholder today (Requirement 10).

**What the tool is.** A `.json` file opens in the plain editor today: black text, no colour, no word about a missing comma until something else fails to read the file. The JSON editor is the same text surface with three things added: the text is coloured by what it is, a mistake is marked where it is made, and where the document names a schema the editor says what the schema allows and offers it while typing.

**The series.** The three editors are three specifications, each worked by one Developer. They share four pieces that no editor has today (Requirement 9), and one specification has to own them or three will each write their own. **This one does**, because the YAML editor validates against the same JSON Schema sources and reuses them directly. The order is therefore: `json-editor` first; `yaml-editor` and `xml-editor` after its Requirement 9 has merged, in either order or side by side.

## What was measured

Read on 2026-10-12 at `develop` `eae4c2221`, and etalii-adp/etalii.adp at `develop` `73ec4a7`.

| Thing | State | Known |
| --- | --- | --- |
| The editor family | Two modules, `markdown` and `plain`. Both wrap the shared `TextEditorPanel`, which takes a module's CodeMirror extensions. | Read |
| The engine | CodeMirror 6: `codemirror`, `@codemirror/state`, `@codemirror/view`, `@codemirror/language`, `@codemirror/commands` and `@codemirror/lang-markdown` are in `src/client/package.json`. | Read |
| Colour today | No file under `src/client/src/editors/` or `src/editors/` calls `syntaxHighlighting`, and none loads `basicSetup`. So the markdown editor parses markdown and paints none of it. | Searched; the consequence is inferred, not seen in a running app |
| Findings and completion today | No file there references `@codemirror/lint` or `@codemirror/autocomplete`. Neither package is a dependency. | Searched |
| An editor module's own dependencies | `src/package.json` lists the workspaces `client`, `diagrams/*/client` and `designers/*/client`. `editors/*/client` is not among them, and `docs/creating-an-editor-module.md` says to add it "the day an editor panel needs a dependency of its own". | Read |
| JSON files in this repository | 1,184 tracked `.json` files. | Counted with `git ls-files` |
| Schemas in this repository | Five `values.schema.json` files, all of the helm-chart module. No other JSON Schema is tracked. | Searched |
| ADP's own JSON formats | The seven tracked `.dis` definitions and the `.des` definition (two tracked copies) are JSON, and each names its schema by address: `https://etalii.net/adp/disl/schema/0.2/disl.schema.json` and `https://etalii.net/adp/desl/schema/0.1/desl.schema.json`. The schemas themselves live in etalii.adp (`specifications/disl/disl.schema.json`, `specifications/desl/desl.schema.json`) and are not in this repository. | Read |
| Diagrams whose body is JSON | The Databricks module declares one diagram type with the body extension `.json`. | Searched `Diagram.cs` files |
| EDSL | A placeholder in etalii.adp: no construct, no schema, no example. `definitions/editors/` holds a readme only. | Read |
| A size limit on opening text | None found in `EtAlii.Adp.Editor`. | Searched for the words; an absence, so a floor and not a proof |

## Library choice

The rule set for this specification: a third-party library is allowed only if its licence is compatible with Apache-2.0 and it is the de-facto standard for what it does. Licences and weekly downloads were read from the npm registry's own metadata on 2026-10-12; the dependency inventory re-checks the licence when the row is written.

| Need | Choice | Licence | Why it is the standard | What was passed over |
| --- | --- | --- | --- | --- |
| The text surface | **CodeMirror 6**, already here | MIT | One of the two engines the web uses for code, and the one this family was built on: `BaseTextEditor` records the choice over Monaco. A second engine would mean two editing surfaces with two sets of keys. | Monaco (MIT): the other standard, and VS Code's own. Not chosen because it is not the family's engine. |
| Grammar and colour | **`@codemirror/lang-json`** with `@lezer/json` | MIT | CodeMirror's own JSON package, 8.1 million downloads a week. | Nothing else is maintained for CodeMirror 6. |
| Showing findings and completions | **`@codemirror/lint`** and **`@codemirror/autocomplete`** | MIT | CodeMirror's own, 12.8 and 13.6 million a week. | None. |
| Reading a schema: validation, completion, hover text | **`vscode-json-languageservice`** | MIT | Microsoft's JSON service: the one inside VS Code and inside Monaco's JSON support, 5.7 million a week. It answers validation, completion and hover from one reading of the schema, so the three cannot disagree. | `ajv` (MIT, 394 million a week): the standard *validator*, and it validates only. Completion would be written here by hand, against a second reading of the schema. · `codemirror-json-schema` (MIT, 120 thousand a week): made for CodeMirror, and not a standard by any count. |

**One thing is not known.** `vscode-json-languageservice` runs in a browser inside Monaco, which is why it is expected to bundle here. That is an inference from another product: nobody has built it with this repository's Vite configuration or run it under this repository's test runner. Requirement 1 settles it before anything is built on it, and names what happens if it fails.

## Open questions

Each is a selection with a default. The requirements below are written for the defaults; an answer other than the default changes the criteria named in the last column.

| # | Question | Options | Changes |
| --- | --- | --- | --- |
| Q1 | Which files does the editor claim? | **(default) `.json` only.** Every other extension keeps opening as it does today. · **`.json`, `.dis`, `.did`, `.des` and `.ded` too**, with ADP's own schemas bundled: a tool engineer writing a specification gets validation and completion from the first keystroke. Costs vendoring the schemas from etalii.adp and keeping them in step. · **`.json` and `.jsonc`**, where `.jsonc` allows comments and trailing commas. · Other. | 2.1, 5.3 |
| Q2 | A document names a schema by web address. Is it fetched? | **(default) No.** A schema is read from the project or from what ADP bundles; a web address that matches neither is reported once, as information, and nothing is validated. A file in a repository does not get to decide which address the host calls. · **Yes, by the backend, over https only**, cached and bounded in size and time. Most public JSON files name their schema this way, so this is where the feature reaches furthest; it costs an outbound call that a file's content chooses. · Other. | 5.2, 5.5 |
| Q3 | Where does schema validation run? | **(default) In the client**, on the text as typed. No round trip per keystroke, and the library chosen above is a client library. · **In the backend**, on a unary call carrying the text. Costs a second library on the .NET side (the candidates, NJsonSchema and JsonSchema.Net, give validation and no completion) and a call per pause in typing. · Other. | 6, 7, Library choice |
| Q4 | Do findings also appear in the Errors and Warnings panel? | **(default) No, in the editor only.** The panel shows what the backend found in files as saved; these findings are found in the client, in text as typed. · **Yes, for the file as saved.** Costs validating in the backend as well, so it implies the second option of Q3 beside the first. · Other. | 4, 6 |
| Q5 | Does the markdown editor get colour in this specification? | **(default) Yes.** The colour scheme of Requirement 9.2 is shared, and markdown already parses; turning it on is one line and one test. · **No**, JSON only; markdown follows in a change of its own. · Other. | 9.2 |
| Q6 | Is an `.eds` file written for this editor? | **(default) No.** EDSL has no construct to write it in. What the language would have to state for this editor is listed and reported to etalii.adp (Requirement 10), and the module is code until the language has content. · **Yes, as a draft** beside the module, in a syntax this specification proposes. Costs designing a language inside an editor's specification. · Other. | 10 |

## Alignment with Product Vision

- **Files are the source of truth.** A JSON file that is wrong should say so where it is edited, not later in whatever reads it.
- **Bringing clarity to textual data.** Colour, structure and the schema's own descriptions are what turn a wall of braces into something that can be read and corrected.
- **Don't reinvent the wheel.** JSON Schema is the convention for saying what a JSON file may hold, and the services chosen above are the ones the format's users already rely on. ADP writes neither a parser nor a validator.
- **Specified, not coded.** The editor is an editor type, to be specified in EDSL. The language has no content yet; this tool is one of the first that says what it needs (Requirement 10).

## Requirements

### Requirement 1: The library is proven here before anything is built on it

**User Story:** As the Developer of this specification, I want to know the schema service runs in this client before I design around it, so that a bundling surprise costs an afternoon and not the design.

#### Acceptance Criteria

1. WHEN work starts THEN the first task SHALL be a spike that loads `vscode-json-languageservice` in this client and asks it, for one fixed document and one fixed schema, for its findings, its completions at one position and its hover text at one position.
2. WHEN the spike runs THEN it SHALL run twice: under `npm test`, and in a production build of the client opened in a browser, because the two resolve packages differently and a pass in one says nothing of the other.
3. WHEN the spike has run THEN its result SHALL be recorded in the design with the size the library adds to the client's bundle, measured.
4. IF the spike fails in either run THEN the Developer SHALL stop and put the choice to the user as a selection: `ajv` for validation with completion written in the module, or validation in the backend (Q3). Nothing SHALL be worked around silently.

### Requirement 2: The module and what it claims

**User Story:** As a user, I want a `.json` file to open in an editor that knows JSON, with nothing to register.

#### Acceptance Criteria

1. WHEN the module is organised THEN it SHALL be `src/editors/json/` in the family's four folders, with the editor id `json`, the client mime `editor/json`, the title "JSON editor", and the claim `.json` (Q1).
2. WHEN a `.json` file that no diagram claims is opened THEN it SHALL open in this editor, with no registration written to the project.
3. WHEN a `.json` file is the body of a diagram THEN opening it SHALL open the diagram, as today, and "Open as text" SHALL open it in this editor.
4. WHEN the module is added THEN no core file and no shell file SHALL change for it, apart from the shared pieces of Requirement 9.
5. WHEN the backend starts THEN no claim conflict SHALL be reported: no other editor claims `.json`.

### Requirement 3: Colour

**User Story:** As a reader of a JSON file, I want its parts told apart at a glance, so that I can see structure before I read values.

#### Acceptance Criteria

1. WHEN a document is shown THEN these SHALL each have a colour of their own: a property name, a string value, a number, the words `true` and `false`, the word `null`, and punctuation.
2. WHEN a property name and a string value stand side by side THEN they SHALL differ, since both are quoted and telling them apart is most of what colour is for in JSON.
3. WHEN the shell is in light mode or in dark mode THEN every colour SHALL meet the contrast the shell's own theme guard requires of text, and SHALL follow a switch between the two without reopening the file.
4. WHEN the text is not well-formed THEN everything the parser can still read SHALL keep its colour; one missing brace SHALL NOT turn the file one colour.
5. WHEN the caret is beside a brace or a bracket THEN its partner SHALL be marked.
6. WHEN an object or an array spans more than one line THEN it SHALL be foldable from the gutter, and folding SHALL change nothing in the text.

### Requirement 4: Mistakes in the JSON itself

**User Story:** As someone editing JSON by hand, I want a missing comma marked where it is missing, whether or not there is a schema.

#### Acceptance Criteria

1. WHEN the text is not well-formed JSON THEN each error SHALL be marked at its place in the text with a sentence saying what is wrong.
2. WHEN an object holds the same property name twice THEN the second SHALL be marked as a warning.
3. WHEN a `.json` file holds a comment or a trailing comma THEN it SHALL be marked as an error: neither is JSON.
4. WHEN a finding is marked THEN it SHALL show in three ways at once: an underline on the text, a mark in the gutter of its line, and its sentence on hover and on keyboard focus. Colour SHALL NOT be the only carrier of severity.
5. WHEN the document has findings THEN the status line of the editor SHALL say how many errors and how many warnings, and SHALL say nothing of them when there are none.
6. WHEN the text changes THEN the findings SHALL be those of the text as it now is, within the bound of the Performance section; a finding for text that is gone SHALL NOT stay on screen.

### Requirement 5: Which schema applies

**User Story:** As an author, I want the editor to use the schema my file names, and to tell me plainly when it cannot.

#### Acceptance Criteria

1. WHEN the root object has a string property `$schema` THEN that is the document's schema reference.
2. WHEN the reference is a relative path THEN it SHALL be resolved against the document's own folder and read from the project. A path that leaves the project root SHALL be refused, with a sentence saying so.
3. WHEN the reference is an address ADP bundles a schema for THEN the bundled schema SHALL be used. With the default of Q1 the bundle is empty, and this criterion is the seam the second option fills.
4. WHEN the reference is the address of a JSON Schema dialect itself THEN the document is a schema, and it SHALL be validated as one against that dialect, which the library carries.
5. WHEN the reference cannot be resolved (a file that is not there, a web address under the default of Q2, a schema that is not valid JSON) THEN the editor SHALL say so once, as one information finding on the `$schema` line naming the reference and the reason, and SHALL offer no schema findings and no schema completions.
6. WHEN there is no `$schema` THEN the editor SHALL work as Requirements 3 and 4 describe, and its status line SHALL say "No schema".
7. WHEN a schema applies THEN the status line SHALL name it, by its title where it has one and by its reference otherwise.
8. WHEN a schema read from the project changes on disk THEN an open document that uses it SHALL be validated again against the new text, with no reopening.
9. WHEN a schema refers to another schema with `$ref` THEN the same rules SHALL apply to that reference: project and bundle, never beyond the project root.

### Requirement 6: Validation against the schema

**User Story:** As an author, I want to see where my document breaks its schema while I am still typing it.

#### Acceptance Criteria

1. WHEN a schema applies and the document is well-formed THEN every place the document breaks the schema SHALL be marked on the value or the property concerned, as an error, with a sentence that names what the schema asks.
2. WHEN a required property is missing THEN the finding SHALL sit on the object that lacks it and SHALL name the property.
3. WHEN the schema marks a property as deprecated THEN its use SHALL be marked as a warning.
4. WHEN the document is not well-formed THEN the findings of Requirement 4 SHALL be shown and schema findings SHALL be those the library can still give for the parts it could read; none SHALL be invented for a part it could not.
5. WHEN the schema is written in a dialect the library does not support THEN the editor SHALL say which dialect and that it is not supported, once, as Requirement 5.5 says a failure. The design SHALL list the dialects supported, read from the library and not from memory.
6. WHEN a finding of the schema and a finding of Requirement 4 fall on the same text THEN both SHALL be readable; neither SHALL hide the other.

### Requirement 7: Completion from the schema

**User Story:** As an author, I want the editor to offer what the schema allows at the place I am typing, so that I do not need the schema open beside me.

#### Acceptance Criteria

1. WHEN the caret is where a property name may be written THEN the editor SHALL offer the properties the schema allows on that object that are not yet present, required ones first.
2. WHEN the caret is where a value may be written and the schema limits the value (an enumeration, a constant, a boolean, null) THEN the editor SHALL offer those values.
3. WHEN an offered item has a description in the schema THEN the description SHALL be shown beside it.
4. WHEN a property is accepted THEN what is inserted SHALL be well-formed where it lands: quoted, followed by a colon, with the value's opening shape where the schema fixes its type, and with a comma where the surrounding object needs one.
5. WHEN the user types a quote where a name or a value may begin, or presses Ctrl+Space, THEN the list SHALL open; it SHALL be reachable, walked and accepted or dismissed from the keyboard alone.
6. WHEN the pointer rests on, or keyboard focus is at, a property that the schema describes THEN the schema's title and description for it SHALL be shown.
7. WHEN no schema applies THEN no schema completion SHALL be offered and none SHALL be guessed from other files.

### Requirement 8: The family's contract holds

**User Story:** As a user, I want this editor to give my file back as it found it, apart from what I typed.

#### Acceptance Criteria

1. WHEN a file is opened and saved without an edit THEN it SHALL be byte-identical: encoding, byte-order mark, each line's own terminator and the presence or absence of a final newline.
2. WHEN the user saves THEN the save SHALL be the family's one `SaveText` call, on the project's history, and a document with findings SHALL save like any other: a finding never blocks a save.
3. WHEN the file changes on disk while open THEN the shared panel's conflict presentation SHALL apply unchanged.
4. WHEN the editor opens, validates, completes or saves THEN it SHALL NOT reformat, reorder or re-indent anything the user did not type or accept.
5. WHEN the module's examples are tested THEN its `Examples.Tests` SHALL hold the family's rule: every example opens, round-trips byte-identically, and has a byte-identical replica under `src/examples/editors/json/`.

### Requirement 9: The four pieces the three editors share

**User Story:** As the Developers of the YAML and XML editors, we want colour, findings, completion and schema lookup to exist once, so that three editors look and behave as one family.

#### Acceptance Criteria

1. WHEN an editor module needs a dependency of its own THEN it SHALL be able to declare one: `editors/*/client` SHALL be a workspace in `src/package.json`, and the dependency inventory SHALL count it.
2. WHEN any editor colours text THEN it SHALL do so through one shared colour scheme, bound to the shell's theme variables, that names the kinds of token once (name, string, number, keyword, comment, punctuation, tag, attribute, invalid) and holds the contrast of Requirement 3.3 for all of them in a test. With the default of Q5 the markdown editor uses it too.
3. WHEN any editor shows findings THEN it SHALL hand them to the shared panel in one shape (range, severity, sentence, source) and the panel SHALL show them as Requirement 4.4 and 4.5 say. No module SHALL draw its own underline.
4. WHEN any editor offers completions or hover text THEN it SHALL do so through one shared presentation with the keys of Requirement 7.5.
5. WHEN any editor resolves a schema reference THEN it SHALL ask one shared resolver, which holds the rules of Requirement 5.2, 5.3, 5.8 and 5.9 and knows nothing of JSON, YAML or XML: it is given a reference and the document's path, and answers with the schema's text or the reason there is none.
6. WHEN the client needs a schema that is a file of the project THEN it SHALL get the text from the backend, by project-relative path, over the connection's existing stream, as a text tab reads its file, and SHALL learn of a change to it the same way. No new stream and no new call SHALL be added. *(Amended 2026-10-12 by the user's chat ruling A1 on the design.)*
7. WHEN these pieces merge THEN `docs/creating-an-editor-module.md` SHALL describe them in the same change, since each is a touch point a module author now has.

### Requirement 10: What EDSL would have to state

**User Story:** As a tool engineer, I want this editor to become a specification one day, so I want what it needs from the language written down while it is fresh.

#### Acceptance Criteria

1. WHEN the module is finished THEN a list SHALL exist of what an EDSL specification of this editor would state, each item traced to code of the module: the claims; the grammar that colours; the kinds of token and their colour roles; where a schema reference is read from the document; what findings exist and their severities; what triggers completion.
2. WHEN that list exists THEN it SHALL be reported to etalii-adp/etalii.adp as one issue, as input for the language, and the issue SHALL be linked from the module's readme.
3. WHEN the module is written THEN no `.eds` file SHALL be invented for it (Q6), and no code SHALL read one.

### Requirement 11: Documentation, catalog and gates

#### Acceptance Criteria

1. WHEN the editor is implemented THEN `docs/tools.md` SHALL carry its row under Editors: implemented, `editor/json`, "JSON editor", kind Editor.
2. WHEN a dependency is added THEN `docs/dependencies.md` SHALL carry its row with the reason and the licence read from the package's own metadata at that moment.
3. WHEN the examples are chosen THEN they SHALL include a document with a schema beside it that it satisfies, a document that breaks its schema in ways Requirement 6 names, and a document with no schema. Data taken from elsewhere SHALL follow the vendored example data rule, licence file included.
4. WHEN the branch is pushed THEN the four gates SHALL exit zero, and every defect found on the way SHALL have left a guard that was seen to fail.

## Non-Functional Requirements

### Code Architecture and Modularity

- What JSON is lives in `src/editors/json/`. The shared pieces of Requirement 9 know no format.
- The module has no backend logic beyond the family's session: with the default of Q3 nothing about JSON is decided in C#.

### Performance

- Typing SHALL never wait for validation. Findings SHALL follow a pause in typing, not each keystroke.
- For a document of 5,000 lines against a schema of 2,000 lines, findings SHALL be on screen within one second of the last keystroke and a completion list within 300 milliseconds of being asked for, measured on the development machine and recorded in the design.
- The design SHALL name a size above which the editor colours and no longer validates, and the editor SHALL say so in its status line when that size is passed.

### Security

- A schema is data. Nothing in a schema or a document SHALL cause code to run.
- No schema SHALL be read from outside the project root or from a network address, unless Q2 is answered otherwise.
- The schema's descriptions are shown as text; markup in them SHALL NOT be rendered as HTML.

### Reliability

- IF the schema service throws THEN the editor SHALL keep colour and the findings of Requirement 4, and SHALL say once that schema support stopped and why.
- A schema that refers to itself, directly or through others, SHALL NOT hang the editor.

### Usability

- Every finding and every completion SHALL be reachable without a pointer.
- The sentences are the library's own where they are clear, in English, and name the property concerned.

## Out of scope

- Formatting or sorting a document, and any command that rewrites text the user did not type.
- JSON5, and JSON Lines. `.jsonc` only if Q1 says so.
- Finding a schema by file name from a public catalogue such as SchemaStore: it needs the network (Q2) and a ruling of its own.
- A tree or form view of a document. That would be a designer, and a different tool.
- The Errors and Warnings panel, unless Q4 says otherwise.

## Sources

- npm registry metadata, read 2026-10-12: `@codemirror/lang-json` 6.0.2 MIT; `@codemirror/lint` 6.9.7 MIT; `@codemirror/autocomplete` 6.20.3 MIT; `@lezer/json` 1.0.3 MIT; `vscode-json-languageservice` MIT (its `latest` tag pointed at `6.0.0-next.3` that day, a pre-release, so the design picks the version); `ajv` 8.20.0 MIT; `codemirror-json-schema` 0.8.1 MIT; `monaco-editor` 0.57.0 MIT. Download counts are npm's last-week figures of the same day.
- NuGet metadata, same day: `NJsonSchema` 11.6.1 MIT. `JsonSchema.Net` 9.4.1 gives a licence page and no expression; its licence was not read.
- `docs/creating-an-editor-module.md`, `src/client/src/editors/BaseTextEditor.tsx`, `src/client/src/editors/TextEditorPanel.tsx`, `src/package.json`, `src/client/package.json` at `eae4c2221`.
- The family's own requirements: `git show d573ad87^:.spec-workflow/archive/specs/modular-text-editors/requirements.md`.
- [ADP terminology](https://github.com/etalii-adp/etalii.adp/blob/develop/docs/terminology.md) and the EDSL placeholder, etalii.adp at `73ec4a7`.
