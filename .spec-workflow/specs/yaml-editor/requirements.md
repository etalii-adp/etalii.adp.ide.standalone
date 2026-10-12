# Requirements Document

## Introduction

The request, as the project's coordinator relayed it on 2026-10-12:

> Write three separate spec-workflow (MCP) specs, one each for an XML, a JSON and a YAML editor tool. Each must cover syntax colouring, schema validation where a schema is available, and completion based on the schema where available. Third-party editor libraries are allowed only if they are Apache-2.0 compatible and the de-facto standard; name the choice and why in each spec.

**This is the YAML editor.** It is an **editor** in ADP's terms: typing text is the core interaction. Its origin is `editor/yaml`, its module is `src/editors/yaml/`, and its specification language is EDSL (`.eds`), which is a placeholder today (Requirement 10).

**What the tool is.** YAML is the format in which indentation is meaning, and a `.yml` file opens in the plain editor today: one colour, a Tab key that inserts a character YAML forbids, and no word about a key indented one space too far until a pipeline fails. The YAML editor is the same text surface with colour, with mistakes marked where they are made, and, where the document names a schema, with what the schema allows offered while typing.

**The series.** This is one of three specifications: [`json-editor`](../json-editor/requirements.md), this one, and [`xml-editor`](../xml-editor/requirements.md), each worked by one Developer. **This one starts after `json-editor` Requirement 9 has merged**: the shared colour scheme, the shared presentation of findings and completions, the shared schema resolver and the `editors/*/client` workspace are that specification's, and this one uses them and writes none of them again. A YAML document is validated against a JSON Schema, so everything `json-editor` says about where a schema comes from holds here by reference.

## What was measured

Read on 2026-10-12 at `develop` `eae4c2221`, and etalii-adp/etalii.adp at `develop` `73ec4a7`. The state of the editor family, the engine, and the absence of colour, findings and completion today are measured in `json-editor` and not repeated.

| Thing | State | Known |
| --- | --- | --- |
| YAML files in this repository | 439 tracked `.yml` and `.yaml` files. | Counted with `git ls-files` |
| Files that name a schema | Two of them contain the words `yaml-language-server`, the comment line that names a schema. | Searched |
| Diagrams whose body is YAML | The Azure DevOps pipeline module and the Databricks module declare the body extension `.yml` in their `Diagram.cs`. Other modules read YAML files through a folder registration (ansible structure, Helm chart). | Searched `Diagram.cs` files for the first; read `docs/dependencies.md` for the second |
| YAML on the backend | `YamlDotNet` 18.1.0 (MIT) is a dependency, used for reading only by six diagram modules. | Read `docs/dependencies.md` |
| A YAML library in the client | None. | Read `src/client/package.json` |
| A schema beside a YAML file | The helm-chart examples carry `values.schema.json` beside `values.yaml`: a schema found by a convention of Helm's, not by a line in the file. | Read |
| EDSL | A placeholder: no construct, no schema, no example. | Read |

## Library choice

The rule set for this specification: a third-party library is allowed only if its licence is compatible with Apache-2.0 and it is the de-facto standard for what it does. Licences and weekly downloads were read from the npm registry's own metadata on 2026-10-12.

| Need | Choice | Licence | Why it is the standard | What was passed over |
| --- | --- | --- | --- | --- |
| The text surface | **CodeMirror 6**, already here | MIT | The family's engine; see `json-editor`. | Monaco (MIT), for the reason given there. |
| Grammar and colour | **`@codemirror/lang-yaml`** with `@lezer/yaml` | MIT | CodeMirror's own YAML package, 5.5 million downloads a week. | Nothing else is maintained for CodeMirror 6. |
| Reading YAML and reading a schema: findings, completion, hover text | **`yaml-language-server`**, its language service | MIT | Red Hat's YAML service: the one inside the VS Code YAML extension and inside `monaco-yaml`, 4.2 million a week. It validates a YAML document against a JSON Schema and answers completion and hover from the same reading. It is also where the convention this editor follows for naming a schema comes from (Requirement 5.1). | `yaml` plus `ajv`, wired here by hand: both are standards for their half, and completion would be written in the module. Kept as the fallback of Requirement 1. · `js-yaml` (MIT, 303 million a week): the most-downloaded YAML reader, and it keeps no positions for values, so a finding could not be placed. · `codemirror-json-schema` (MIT, 120 thousand a week): not a standard. |
| The parser underneath | **`yaml`** (eemeli) | ISC | The parser `yaml-language-server` itself is built on, 200 million a week, YAML 1.2, and it keeps the position of every node. It arrives with the service and is not chosen separately. ISC is a permissive licence compatible with Apache-2.0. | — |

**Two things are not known.** First, whether the language service of `yaml-language-server` bundles in this client. `monaco-yaml` ships it to browsers, which is why it is expected to; that is an inference from another product. The package is published as a server for Node, and its service is one part of it. Second, what it adds to the bundle. Requirement 1 settles both before anything is built on them.

## Open questions

Each is a selection with a default. The requirements below are written for the defaults. Questions Q2, Q3 and Q4 of `json-editor` (web addresses, where validation runs, the Errors and Warnings panel) are **not asked again**: their answers there hold here, and a different answer for YAML alone would be asked as a new question.

| # | Question | Options | Changes |
| --- | --- | --- | --- |
| Q1 | Which files does the editor claim? | **(default) `.yaml` and `.yml`.** A file that is a diagram's body still opens its diagram; "Open as text" lands here. · **Those, and exact names without an extension that are YAML by convention** (`.clang-format`, `.yamllint`). Costs a list somebody keeps. · Other. | 2.1 |
| Q2 | How does a YAML document name its schema? | **(default) The comment line `# yaml-language-server: $schema=`** followed by the reference, anywhere before the first content. It is the convention the chosen library established and editors outside ADP already honour, so a file prepared for them works here unchanged. · **That, and a top-level `$schema` key** as JSON has. Costs treating a key of the user's data as an instruction. · Other. | 5.1 |
| Q3 | A schema that sits beside the file by a tool's convention, as Helm's `values.schema.json` beside `values.yaml`: is it used? | **(default) No.** Only what the file names. Conventions of one tool belong to the module that knows that tool. · **Yes, for Helm**, as the one case this repository has examples of. Costs a Helm rule inside a general editor. · Other. | 5 |
| Q4 | Which YAML? | **(default) YAML 1.2**, the library's default: `yes`, `no`, `on` and `off` are strings. · **YAML 1.1**, in which they are booleans, as some older tools read them. · Other. | 4.5 |
| Q5 | Is an `.eds` file written for this editor? | **(default) No**, as `json-editor` Q6: the needs are listed and reported. · **Yes, as a draft.** · Other. | 10 |

## Alignment with Product Vision

- **Files are the source of truth.** Most of what this repository's diagram modules draw is YAML. When the picture cannot express a change, the text is where it is made, and the text should say when it is wrong.
- **Bringing clarity to textual data.** In YAML the structure is whitespace. Colour, folding and indentation that cannot go wrong by accident are what make it readable.
- **Don't reinvent the wheel.** JSON Schema is how YAML files are described in practice, and the service chosen is the one their authors already use. The file stays openable, unchanged, by every other tool that owns its format.
- **Specified, not coded.** An editor type, to be specified in EDSL once the language has content (Requirement 10).

## Requirements

### Requirement 1: The library is proven here before anything is built on it

**User Story:** As the Developer of this specification, I want to know the YAML service runs in this client before I design around it.

#### Acceptance Criteria

1. WHEN work starts THEN the first task SHALL be a spike that loads the language service of `yaml-language-server` in this client and asks it, for one fixed document and one fixed schema, for its findings, its completions at one position and its hover text at one position.
2. WHEN the spike runs THEN it SHALL run twice: under `npm test`, and in a production build of the client opened in a browser.
3. WHEN the spike has run THEN its result SHALL be recorded in the design with the size the library adds to the client's bundle, measured, and with every Node-only module that had to be replaced or left out to make it build, each named.
4. IF the spike fails in either run, or needs a replacement the Developer cannot justify, THEN the Developer SHALL stop and put the choice to the user as a selection: `yaml` for reading with the JSON editor's schema service over the document's data, completion written in the module; or validation in the backend. Nothing SHALL be worked around silently.

### Requirement 2: The module and what it claims

**User Story:** As a user, I want a `.yml` file to open in an editor that knows YAML, with nothing to register.

#### Acceptance Criteria

1. WHEN the module is organised THEN it SHALL be `src/editors/yaml/` in the family's four folders, with the editor id `yaml`, the client mime `editor/yaml`, the title "YAML editor", and the claims `.yaml` and `.yml` (Q1).
2. WHEN a YAML file that no diagram claims is opened THEN it SHALL open in this editor, with no registration written to the project.
3. WHEN a YAML file is the body of a diagram THEN opening it SHALL open the diagram, as today, and "Open as text" SHALL open it in this editor.
4. WHEN a save is made in this editor on a file a diagram also shows THEN the diagram SHALL learn of it as it learns of any change on disk; this editor adds no path of its own.
5. WHEN the module is added THEN no core file and no shell file SHALL change for it.
6. WHEN the backend starts THEN no claim conflict SHALL be reported.

### Requirement 3: Colour and editing that respects indentation

**User Story:** As a reader of a YAML file, I want keys, values and structure told apart, and I want the keyboard to keep my indentation right.

#### Acceptance Criteria

1. WHEN a document is shown THEN these SHALL each be told apart through the shared colour scheme: a key; a string; a number; a boolean and null; a comment; an anchor and an alias; a tag; the document markers `---` and `...`; the indicator of a block scalar and the text of the block.
2. WHEN a plain scalar is a string only because of its content (`yes` under YAML 1.2, a version such as `1.10`) THEN its colour SHALL be the one of the type it is read as, so that the colour tells the reader what a tool will see.
3. WHEN the shell is in light mode or in dark mode THEN every colour SHALL hold the contrast of `json-editor` Requirement 3.3 and follow a switch between the two.
4. WHEN the text is not well-formed THEN everything the parser can still read SHALL keep its colour.
5. WHEN the user presses Tab THEN spaces SHALL be inserted, never a tab character, to the document's own indentation width where it can be read from the document and two otherwise.
6. WHEN the user presses Enter THEN the new line SHALL start at the indentation of the line left, one level deeper after a key that opens a mapping or a sequence.
7. WHEN a mapping, a sequence or a block scalar spans more than one line THEN it SHALL be foldable from the gutter, and folding SHALL change nothing in the text.

### Requirement 4: Mistakes in the YAML itself

**User Story:** As someone editing YAML by hand, I want an indentation mistake marked where it is, whether or not there is a schema.

#### Acceptance Criteria

1. WHEN the text is not well-formed YAML THEN each error the parser reports SHALL be marked at its place with a sentence saying what is wrong, through the shared presentation of `json-editor` Requirement 9.3.
2. WHEN a tab character is used for indentation THEN it SHALL be marked as an error on that character.
3. WHEN a mapping holds the same key twice THEN the second SHALL be marked as an error.
4. WHEN an alias names an anchor that does not exist THEN the alias SHALL be marked as an error.
5. WHEN a file holds more than one document THEN each SHALL be read on its own, and a mistake in one SHALL NOT hide the findings of another.
6. WHEN the text changes THEN the findings SHALL be those of the text as it now is, within the bound of the Performance section.

### Requirement 5: Which schema applies

**User Story:** As an author, I want the editor to use the schema my file names, and to tell me plainly when it cannot.

#### Acceptance Criteria

1. WHEN a comment line of the form `# yaml-language-server: $schema=` followed by a reference stands before the first content of a document THEN that reference is the document's schema reference (Q2).
2. WHEN a file holds more than one document THEN each document MAY carry its own line, and a document without one has no schema; a line SHALL NOT reach past the next document marker.
3. WHEN a reference is resolved THEN it SHALL be resolved by the shared resolver, under the rules of `json-editor` Requirement 5.2, 5.3, 5.5, 5.8 and 5.9 and its Q2, unchanged: the project and the bundle, never beyond the project root, a failure said once on the line that names the reference.
4. WHEN a schema applies THEN the status line SHALL name it; WHEN none applies THEN it SHALL say "No schema".
5. WHEN the schema is itself written in YAML THEN it SHALL be read as one; a JSON Schema is a data structure and may be stored in either format.

### Requirement 6: Validation against the schema

**User Story:** As an author, I want to see where my document breaks its schema while I am still typing it.

#### Acceptance Criteria

1. WHEN a schema applies THEN every place the document's data breaks the schema SHALL be marked on the key or the value concerned, as an error, with a sentence that names what the schema asks.
2. WHEN a required key is missing THEN the finding SHALL sit on the mapping that lacks it and SHALL name the key.
3. WHEN a value is reached through an alias or a merge key THEN it SHALL be validated as the data it stands for, and a finding SHALL sit on the alias where the document uses it, not only at the anchor.
4. WHEN the schema marks a key as deprecated THEN its use SHALL be marked as a warning.
5. WHEN the document is not well-formed THEN the findings of Requirement 4 SHALL be shown, and no schema finding SHALL be invented for a part that could not be read.
6. WHEN the schema's dialect is not supported THEN the editor SHALL say so as `json-editor` Requirement 6.5 says.

### Requirement 7: Completion from the schema

**User Story:** As an author, I want the editor to offer the keys and values the schema allows at the place and depth I am typing.

#### Acceptance Criteria

1. WHEN the caret is where a key may be written THEN the editor SHALL offer the keys the schema allows in that mapping that are not yet present, required ones first. Which mapping that is SHALL follow from the caret's indentation.
2. WHEN the caret is where a value may be written and the schema limits the value THEN the editor SHALL offer those values.
3. WHEN the caret is on an empty line inside a sequence whose items the schema describes THEN the editor SHALL offer a new item.
4. WHEN a key is accepted THEN what is inserted SHALL be well-formed YAML at that depth: the key, a colon, and for a mapping or a sequence a new line at the right indentation. Spaces only.
5. WHEN the list is open THEN it SHALL be the shared presentation, with the keys of `json-editor` Requirement 7.5, and SHALL also open on Ctrl+Space on an empty line.
6. WHEN the pointer rests on, or keyboard focus is at, a key the schema describes THEN the schema's title and description for it SHALL be shown.
7. WHEN no schema applies THEN no schema completion SHALL be offered.

### Requirement 8: The family's contract holds

**User Story:** As a user, I want my comments, my blank lines and my quoting left exactly as I wrote them.

#### Acceptance Criteria

1. WHEN a file is opened and saved without an edit THEN it SHALL be byte-identical.
2. WHEN the editor reads the document to validate or to complete THEN what it read SHALL never be written back: the text the user typed is the only source of what is saved. No comment, blank line, quote style, key order or anchor name SHALL change because the editor understood the file.
3. WHEN the user saves THEN the save SHALL be the family's one `SaveText` call, and a finding SHALL never block a save.
4. WHEN the file changes on disk while open THEN the shared panel's conflict presentation SHALL apply unchanged.
5. WHEN the module's examples are tested THEN its `Examples.Tests` SHALL hold the family's rule, with a replica under `src/examples/editors/yaml/`. The examples SHALL include a CRLF file and a file of more than one document, since both are places a YAML reader commonly loses bytes.

### Requirement 9: Nothing shared is written twice

#### Acceptance Criteria

1. WHEN this editor colours, shows a finding, offers a completion or resolves a schema THEN it SHALL use the pieces of `json-editor` Requirement 9 and SHALL add none of its own beside them.
2. IF a shared piece cannot carry something YAML needs (a finding that belongs to one document of several, a completion that depends on indentation) THEN the shared piece SHALL be widened in a way the JSON editor still passes its tests on, and the change SHALL be named in this specification's design.

### Requirement 10: What EDSL would have to state

#### Acceptance Criteria

1. WHEN the module is finished THEN the list `json-editor` Requirement 10.1 describes SHALL exist for this editor, with what YAML adds to it: indentation rules of the keyboard, the comment line that names a schema, more than one document in a file.
2. WHEN that list exists THEN it SHALL be added to the issue `json-editor` Requirement 10.2 opened in etalii-adp/etalii.adp, or to a new one linked from it, and linked from the module's readme.
3. WHEN the module is written THEN no `.eds` file SHALL be invented for it (Q5), and no code SHALL read one.

### Requirement 11: Documentation, catalog and gates

#### Acceptance Criteria

1. WHEN the editor is implemented THEN `docs/tools.md` SHALL carry its row under Editors: implemented, `editor/yaml`, "YAML editor", kind Editor.
2. WHEN a dependency is added THEN `docs/dependencies.md` SHALL carry its row with the reason and the licence read from the package's own metadata at that moment.
3. WHEN the examples are chosen THEN they SHALL include a document that names a schema of the project and satisfies it, one that breaks its schema in ways Requirement 6 names, one with anchors and aliases, and one with no schema. Data taken from elsewhere SHALL follow the vendored example data rule.
4. WHEN the branch is pushed THEN the four gates SHALL exit zero, and every defect found on the way SHALL have left a guard that was seen to fail.

## Non-Functional Requirements

### Code Architecture and Modularity

- What YAML is lives in `src/editors/yaml/`. Nothing about a tool that happens to use YAML (Helm, Ansible, a pipeline) lives in it.
- With the default answers the module has no backend logic beyond the family's session, and `YamlDotNet` is not used by it.

### Performance

- Typing SHALL never wait for validation.
- For a document of 5,000 lines against a schema of 2,000 lines, findings SHALL be on screen within one second of the last keystroke and a completion list within 300 milliseconds of being asked for, measured and recorded in the design.
- Above the size the JSON editor's design names, the editor colours and no longer validates, and says so in its status line.

### Security

- A schema is data, and so is a document. No tag in a document SHALL construct anything or run anything: the document is read to a plain data structure and nothing else.
- An alias that expands to a very large structure SHALL be bounded: the editor SHALL stop expanding at a limit the design names and say so, and SHALL NOT hang.
- No schema SHALL be read from outside the project root or from a network address, unless `json-editor` Q2 is answered otherwise.

### Reliability

- IF the service throws THEN the editor SHALL keep colour, and SHALL say once that findings and completion stopped and why.

### Usability

- Every finding and every completion SHALL be reachable without a pointer.
- A sentence about indentation SHALL say which column was expected.

## Out of scope

- Formatting, re-indenting or sorting a document.
- Knowing any tool's files: Kubernetes kinds, Helm values, Ansible playbooks, pipeline definitions. Those are schemas a project may name, or the business of a module of their own.
- Finding a schema by file name from a public catalogue such as SchemaStore.
- Templating inside YAML (Helm's and Jinja's braces): such a file is not YAML until rendered, and its findings will say so.
- YAML 1.1 unless Q4 says so.

## Sources

- npm registry metadata, read 2026-10-12: `@codemirror/lang-yaml` 6.1.3 MIT; `@lezer/yaml` 1.0.4 MIT; `yaml-language-server` 1.25.0 MIT; `yaml` 2.9.1 ISC; `js-yaml` 5.4.3 MIT; `monaco-yaml` 5.5.1 MIT; `ajv` 8.20.0 MIT. Download counts are npm's last-week figures of the same day.
- `docs/dependencies.md` for `YamlDotNet`; `src/client/package.json`; the `Diagram.cs` files of `src/diagrams/` at `eae4c2221`.
- [`json-editor` requirements](../json-editor/requirements.md), for everything this document takes by reference.
- [ADP terminology](https://github.com/etalii-adp/etalii.adp/blob/develop/docs/terminology.md) and the EDSL placeholder, etalii.adp at `73ec4a7`.
