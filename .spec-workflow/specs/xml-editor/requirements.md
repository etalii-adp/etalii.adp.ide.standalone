# Requirements Document

## Introduction

The request, as the project's coordinator relayed it on 2026-10-12:

> Write three separate spec-workflow (MCP) specs, one each for an XML, a JSON and a YAML editor tool. Each must cover syntax colouring, schema validation where a schema is available, and completion based on the schema where available. Third-party editor libraries are allowed only if they are Apache-2.0 compatible and the de-facto standard; name the choice and why in each spec.

**This is the XML editor.** It is an **editor** in ADP's terms: typing text is the core interaction. Its origin is `editor/xml`, its module is `src/editors/xml/`, and its specification language is EDSL (`.eds`), which is a placeholder today (Requirement 10).

**What the tool is.** An `.xml` file opens in the plain editor today: one colour, no word about a tag that was never closed. The XML editor is the same text surface with colour, with mistakes marked where they are made, and, where the document names an XSD or a DTD, checked against it; for an XSD, what the schema allows is also offered while typing.

**The series.** This is one of three specifications: [`json-editor`](../json-editor/requirements.md), [`yaml-editor`](../yaml-editor/requirements.md) and this one, each worked by one Developer. **This one starts after `json-editor` Requirement 9 has merged**: the shared colour scheme, the shared presentation of findings and completions, the shared schema resolver and the `editors/*/client` workspace are that specification's. It does not wait for `yaml-editor`.

**Where this editor differs from the other two.** JSON and YAML are checked against a JSON Schema by a client library. XML has its own schema languages, no browser library for them is a standard, and the platform this host's backend runs on has carried a complete one for twenty years. So this is the one editor of the three whose schema work is done in the backend, and the one that adds no third-party library for it.

## What was measured

Read on 2026-10-12 at `develop` `eae4c2221`, and etalii-adp/etalii.adp at `develop` `73ec4a7`. The state of the editor family, the engine, and the absence of colour, findings and completion today are measured in `json-editor` and not repeated.

| Thing | State | Known |
| --- | --- | --- |
| XML files in this repository | 15 tracked `.xml` files. No `.xsd`, `.xsl` or `.xslt` file is tracked. | Counted with `git ls-files` |
| XML under another extension | 151 `.csproj`, 41 `.DotSettings`, 7 `.slnx`, 6 `.props`, 6 `.svg` and 1 `.targets`. | Counted with `git ls-files` |
| Files that name a schema | None: no tracked `.xml` file contains `schemaLocation` or a `DOCTYPE`. So this repository holds no example of the feature, and the examples of Requirement 11.3 are new. | Searched |
| Diagrams whose body is XML | None declares the body extension `.xml`. | Searched `Diagram.cs` files |
| XML in ADP's own data | The knowledge designer's examples include `cities.xml`. | Read the file list |
| An XML library in the client | None. | Read `src/client/package.json` |
| The editor family's calls | `EditorService` has one unary call, `SaveText`. An editor's text arrives on the shared stream. | Read `src/api/editors.proto` |
| EDSL | A placeholder: no construct, no schema, no example. | Read |

## Library choice

The rule set for this specification: a third-party library is allowed only if its licence is compatible with Apache-2.0 and it is the de-facto standard for what it does. npm licences and weekly downloads were read from the registry's own metadata on 2026-10-12.

| Need | Choice | Licence | Why it is the standard | What was passed over |
| --- | --- | --- | --- | --- |
| The text surface | **CodeMirror 6**, already here | MIT | The family's engine; see `json-editor`. | Monaco (MIT), for the reason given there. |
| Grammar and colour, closing a tag as it is typed | **`@codemirror/lang-xml`** with `@lezer/xml` | MIT | CodeMirror's own XML package, 4.9 million downloads a week. | Nothing else is maintained for CodeMirror 6. |
| Well-formedness, XSD and DTD validation, and what a schema allows at a position | **`System.Xml` and `System.Xml.Schema`**, part of .NET | MIT, as .NET is | Not a third-party library: it ships with the runtime the backend already runs on, and it is what .NET software validates XML with. It validates against XSD 1.0 and DTD, and its `XmlSchemaValidator` can be asked which elements and which attributes the schema expects next, which is the question completion asks. | `xmllint-wasm` (MIT, a build of libxml2, 398 thousand a week): libxml2 is the standard outside .NET, and this build validates in the browser. It answers "is it valid" only, so completion would be written here by hand from a second reading of the XSD, and it adds a WebAssembly payload to the client. · `fast-xml-parser` (MIT, 84 million a week): reads XML, knows no schema. |

**Three things are not known**, and Requirement 1 settles them before anything is built on them. First, whether `XmlSchemaValidator` gives useful answers for a document that stops at the caret: it is fed a document piece by piece, so it is expected to, and that is a reading of its interface, not a run. Second, how its answers behave inside content models that allow a choice or any order. Third, what a round trip to the backend costs per pause in typing on a real document.

**What the choice does not cover.** XSD 1.1, RELAX NG and Schematron. .NET implements none of them and no library that does was judged a standard. A document that names one is told so (Requirement 5.6).

## Open questions

Each is a selection with a default. The requirements below are written for the defaults. Q2 of `json-editor` (a schema named by web address) is **not asked again**: its answer there holds here. Q3 and Q4 of `json-editor` are asked again below, because for XML the facts differ.

| # | Question | Options | Changes |
| --- | --- | --- | --- |
| Q1 | Which files does the editor claim? | **(default) `.xml`, `.xsd`, `.xsl` and `.xslt`.** The format and its two closest relatives. · **Those, and the XML this repository is built from: `.csproj`, `.props`, `.targets`, `.slnx`, `.DotSettings`.** 206 more files here get colour and well-formedness; none of them names a schema. · **Those, and `.svg`.** An SVG file is XML, and is also a picture a user may expect to see drawn. · Other. | 2.1 |
| Q2 | Where does schema validation run? | **(default) In the backend**, on a unary call that carries the text as typed. The library is already there, completion comes from the same reading, and nothing is added to the client. Costs a call per pause in typing. · **In the client**, with `xmllint-wasm`. No call; completion written by hand in the module, and a WebAssembly payload in the bundle. · Other. | 1, 6, 7, 9 |
| Q3 | Do findings also appear in the Errors and Warnings panel? | **(default) No, in the editor only**, as the other two editors, so that the three behave alike. · **Yes, for the file as saved.** With the default of Q2 the backend can already produce them, so for this editor alone the cost is small; the three editors would then differ. · Other. | 4, 6 |
| Q4 | A document with a DTD: are entities it declares expanded? | **(default) Internal entities yes, bounded; external entities only from files of the project, never from a network address.** A document that needs more is told what was not loaded. · **No DTD processing at all**: a `DOCTYPE` is coloured and otherwise ignored, and DTD validation and completion are dropped from this specification. · Other. | 5.3, Security |
| Q5 | Is an `.eds` file written for this editor? | **(default) No**, as `json-editor` Q6: the needs are listed and reported. · **Yes, as a draft.** · Other. | 10 |

## Alignment with Product Vision

- **Files are the source of truth.** An XML file that is not well-formed should say so where it is edited.
- **Bringing clarity to textual data.** XML is the most verbose of the three formats; colour, folding and tags that match are what let a reader see the document through the markup.
- **Don't reinvent the wheel.** XSD and DTD are how an XML file says what it may hold, and the validator chosen is the platform's own. ADP writes neither a parser nor a validator.
- **Specified, not coded.** An editor type, to be specified in EDSL once the language has content (Requirement 10).

## Requirements

### Requirement 1: The unknowns are settled before anything is built on them

**User Story:** As the Developer of this specification, I want to know the platform's validator can answer completion's question before I design around it.

#### Acceptance Criteria

1. WHEN work starts THEN the first task SHALL be a spike, as tests in the module's test project, that feeds `XmlSchemaValidator` a document that stops in the middle of an element and asks which elements and which attributes are expected there, for three content models: a sequence, a choice, and any order.
2. WHEN the spike runs THEN it SHALL also measure, for a document of 5,000 lines and an XSD of 2,000 lines, the time to validate the whole document and the time to answer one completion, on the development machine.
3. WHEN the spike has run THEN its answers and its timings SHALL be recorded in the design, each marked as measured.
4. IF the validator cannot answer for a document that stops at the caret, or the timings miss the Performance section by more than a factor the design could close, THEN the Developer SHALL stop and put Q2 to the user again with the measurement. Nothing SHALL be worked around silently.

### Requirement 2: The module and what it claims

**User Story:** As a user, I want an `.xml` file to open in an editor that knows XML, with nothing to register.

#### Acceptance Criteria

1. WHEN the module is organised THEN it SHALL be `src/editors/xml/` in the family's four folders, with the editor id `xml`, the client mime `editor/xml`, the title "XML editor", and the claims `.xml`, `.xsd`, `.xsl` and `.xslt` (Q1).
2. WHEN a claimed file that no diagram claims is opened THEN it SHALL open in this editor, with no registration written to the project.
3. WHEN a claimed file is shown by a diagram or a designer THEN opening it SHALL do what it does today, and "Open as text" SHALL open it in this editor.
4. WHEN the module is added THEN no core file and no shell file SHALL change for it, apart from the family's seam of Requirement 9.2. *(Amended 2026-10-12 by the user's chat ruling A1 on the design.)*
5. WHEN the backend starts THEN no claim conflict SHALL be reported.

### Requirement 3: Colour and tags that stay matched

**User Story:** As a reader of an XML file, I want to see the document through the markup.

#### Acceptance Criteria

1. WHEN a document is shown THEN these SHALL each be told apart through the shared colour scheme: an element name; an attribute name; an attribute value; text; a comment; a CDATA section; a processing instruction and the XML declaration; an entity reference; a `DOCTYPE`.
2. WHEN a name has a namespace prefix THEN the prefix SHALL be told apart from the local name.
3. WHEN the shell is in light mode or in dark mode THEN every colour SHALL hold the contrast of `json-editor` Requirement 3.3 and follow a switch between the two.
4. WHEN the text is not well-formed THEN everything the parser can still read SHALL keep its colour.
5. WHEN the caret is in a start tag or an end tag THEN its partner SHALL be marked.
6. WHEN the user types the `>` that completes a start tag THEN the matching end tag SHALL be inserted after the caret; WHEN the user types `</` THEN the name of the open element SHALL be offered. Either SHALL be one undo away.
7. WHEN an element spans more than one line THEN it SHALL be foldable from the gutter, as SHALL a comment and a CDATA section, and folding SHALL change nothing in the text.

### Requirement 4: Mistakes in the XML itself

**User Story:** As someone editing XML by hand, I want an unclosed tag marked where it is, whether or not there is a schema.

#### Acceptance Criteria

1. WHEN the text is not well-formed XML THEN the error SHALL be marked at its place with a sentence saying what is wrong, through the shared presentation of `json-editor` Requirement 9.3.
2. WHEN an end tag does not match the element that is open THEN the finding SHALL name both, and SHALL sit where the mismatch is, not at the end of the file.
3. WHEN an element carries the same attribute twice THEN the second SHALL be marked.
4. WHEN a name uses a namespace prefix that is not declared THEN the name SHALL be marked.
5. WHEN the XML declaration names an encoding other than the one the file is stored in THEN the declaration SHALL be marked as a warning.
6. WHEN the first error has been found THEN the editor SHALL NOT pretend to more: a reader that stops at the first error reports one, and the status line SHALL say that the rest of the document was not checked.
7. WHEN the text changes THEN the findings SHALL be those of the text as it now is, within the bound of the Performance section; findings that belong to an older text SHALL be taken away when the text they were found in is gone, not when the answer about the new text arrives.

### Requirement 5: Which schema applies

**User Story:** As an author, I want the editor to use the schema my file names, and to tell me plainly when it cannot.

#### Acceptance Criteria

1. WHEN the root element carries `xsi:noNamespaceSchemaLocation` THEN its value is the reference to the XSD of the elements without a namespace.
2. WHEN the root element carries `xsi:schemaLocation` THEN its value is read as pairs, a namespace and the reference to the XSD of that namespace; a document MAY so name several schemas, and all SHALL apply together.
3. WHEN the document has a `DOCTYPE` with a system identifier THEN that is the reference to its DTD; an internal subset SHALL be read as part of it (Q4).
4. WHEN a reference is resolved THEN it SHALL be resolved by the shared resolver, under the rules of `json-editor` Requirement 5.2, 5.3, 5.5 and 5.8 and its Q2, unchanged. An `xs:include`, `xs:import` or `xs:redefine` inside a schema, and an external entity inside a DTD, SHALL be resolved under the same rules and never beyond the project root.
5. WHEN the file is itself a schema (its root is `xs:schema`) THEN it SHALL be checked as a schema: what `System.Xml.Schema` reports when compiling it SHALL be shown as findings in it.
6. WHEN the document names a schema this editor does not support (an `xml-model` processing instruction that names RELAX NG or Schematron, an XSD that declares version 1.1) THEN the editor SHALL say so once, as one information finding where it is named, and SHALL offer no schema findings from it.
7. WHEN a schema applies THEN the status line SHALL name it, by target namespace or by file name; WHEN none applies THEN it SHALL say "No schema".
8. WHEN an XSD that an open document uses changes on disk, or any file it includes, THEN the document SHALL be validated again with no reopening.

### Requirement 6: Validation against the schema

**User Story:** As an author, I want to see where my document breaks its schema while I am still typing it.

#### Acceptance Criteria

1. WHEN a schema applies and the document is well-formed THEN every place the validator reports SHALL be marked at the line and column it gives, as an error where it says error and as a warning where it says warning, with its sentence.
2. WHEN an element is not allowed where it stands THEN the finding SHALL sit on that element's start tag and SHALL say what was expected there.
3. WHEN a required attribute or a required child is missing THEN the finding SHALL sit on the element that lacks it and SHALL name what is missing.
4. WHEN a value does not fit its simple type THEN the finding SHALL sit on the value.
5. WHEN the document is not well-formed THEN the finding of Requirement 4 SHALL be shown, schema findings SHALL be withheld, and the status line SHALL say that the schema was not checked. A schema finding from before the document broke SHALL NOT stay on screen as if it were current.
6. WHEN the schema itself does not compile THEN the editor SHALL say so once, naming the schema file and the first error in it, and SHALL offer no schema findings.

### Requirement 7: Completion from the schema

**User Story:** As an author, I want the editor to offer the elements and attributes the schema allows at the place I am typing.

#### Acceptance Criteria

1. WHEN the user types `<` inside an element, or presses Ctrl+Space there, THEN the editor SHALL offer the elements the schema allows at that position, given the element's content model and the children already present before the caret.
2. WHEN the caret is inside a start tag after the name THEN the editor SHALL offer the attributes the schema allows on that element that are not yet present, required ones first.
3. WHEN the caret is inside an attribute value, or in the text of an element, and the schema limits the value to an enumeration or a boolean THEN the editor SHALL offer those values.
4. WHEN an element is accepted THEN what is inserted SHALL be well-formed: the start tag with its required attributes, each with an empty value and the caret in the first, and the end tag, or a self-closing tag where the schema allows no content.
5. WHEN an offered element belongs to a namespace THEN it SHALL be offered with the prefix the document declares for that namespace; an element whose namespace the document does not declare SHALL be offered with a note saying so and SHALL NOT silently add a declaration.
6. WHEN an offered item has documentation in the schema (`xs:annotation` with `xs:documentation`) THEN it SHALL be shown beside the item, and on hover and on keyboard focus for a name already in the document.
7. WHEN the text before the caret is not well-formed in a way that hides where the caret is THEN no schema completion SHALL be offered, and none SHALL be guessed.
8. WHEN no schema applies, or the only schema is a DTD, THEN the only completion SHALL be the end tag of Requirement 3.6; for a DTD the status line SHALL say that it is checked and offers no suggestions. *(Amended 2026-10-12 by the user's chat ruling A2 on the design.)*

### Requirement 8: The family's contract holds

**User Story:** As a user, I want this editor to give my file back as it found it, apart from what I typed.

#### Acceptance Criteria

1. WHEN a file is opened and saved without an edit THEN it SHALL be byte-identical: encoding, byte-order mark, each line's own terminator, the final newline, and every space inside every tag.
2. WHEN the backend reads the document to validate or to complete THEN what it read SHALL never be written back, and attribute defaults a schema supplies SHALL NOT appear in the text.
3. WHEN the user saves THEN the save SHALL be the family's one `SaveText` call, and a finding SHALL never block a save.
4. WHEN the file changes on disk while open THEN the shared panel's conflict presentation SHALL apply unchanged.
5. WHEN the module's examples are tested THEN its `Examples.Tests` SHALL hold the family's rule, with a replica under `src/examples/editors/xml/`. The examples SHALL include a file in an encoding other than UTF-8 whose declaration says so, since that is where an XML reader and a text reader disagree.

### Requirement 9: Asking the backend

**User Story:** As a user, I want findings for what I have typed, not for what I last saved.

#### Acceptance Criteria

1. WHEN the editor needs findings or completions THEN it SHALL ask on unary calls that carry the text as typed and, for completion, the position. No new stream SHALL be added.
2. WHEN the calls are defined THEN they SHALL be the family's, on the shared `EditorService`, and SHALL carry no format: the module answers them through a seam of the family, and the shared `EditorService` SHALL NOT learn what XML is. *(Amended 2026-10-12 by the user's chat ruling A1 on the design.)*
3. WHEN an answer arrives for a text that has since changed THEN it SHALL be dropped; an answer SHALL be shown only against the text it was asked about.
4. WHEN the user keeps typing THEN at most one request for findings SHALL be waiting, and an older one still in flight SHALL be abandoned.
5. WHEN the backend cannot be reached THEN the editor SHALL keep colour and tag matching, and SHALL say once in its status line that checking is unavailable, with the shell's own wording for a lost connection.
6. WHEN the shared resolver of `json-editor` Requirement 9.5 is asked from the backend, as here, and from the client, as there, THEN it SHALL be one set of rules, tested once from each side against the same cases.

### Requirement 10: What EDSL would have to state

#### Acceptance Criteria

1. WHEN the module is finished THEN the list `json-editor` Requirement 10.1 describes SHALL exist for this editor, with what XML adds to it: paired tags and how they are closed, the three places a schema is named, and that the schema languages are XML's own.
2. WHEN that list exists THEN it SHALL be added to the issue `json-editor` Requirement 10.2 opened in etalii-adp/etalii.adp, or to a new one linked from it, and linked from the module's readme.
3. WHEN the module is written THEN no `.eds` file SHALL be invented for it (Q5), and no code SHALL read one.

### Requirement 11: Documentation, catalog and gates

#### Acceptance Criteria

1. WHEN the editor is implemented THEN `docs/tools.md` SHALL carry its row under Editors: implemented, `editor/xml`, "XML editor", kind Editor.
2. WHEN a dependency is added THEN `docs/dependencies.md` SHALL carry its row. With the default answers that is `@codemirror/lang-xml` and nothing on the backend.
3. WHEN the examples are chosen THEN they SHALL include a document with an XSD beside it that it satisfies, a document that breaks its XSD in the ways Requirement 6 names, a document with a DTD, a document that uses two namespaces with a schema each, and a document with no schema. Data taken from elsewhere SHALL follow the vendored example data rule.
4. WHEN the branch is pushed THEN the four gates SHALL exit zero, and every defect found on the way SHALL have left a guard that was seen to fail.

## Non-Functional Requirements

### Code Architecture and Modularity

- What XML is lives in `src/editors/xml/`: colour in its client, schemas in its backend project. Core and the shared editor project learn nothing of XML.
- The backend half reads through the family's `TextFileBuffer` where it reads a file of the project, so that encoding is decided in one place.

### Performance

- Typing SHALL never wait for the backend.
- For a document of 5,000 lines against an XSD of 2,000 lines, findings SHALL be on screen within one second of the last keystroke and a completion list within 300 milliseconds of being asked for, measured as Requirement 1.2 says.
- A compiled schema SHALL be kept while a document that uses it is open and no file of it has changed; it SHALL NOT be compiled again for every request.
- Above the size the JSON editor's design names, the editor colours and no longer validates, and says so in its status line.

### Security

- **No external entity and no schema SHALL be read from outside the project root or from a network address**, unless `json-editor` Q2 is answered otherwise. The platform's default resolver SHALL NOT be used; the one given to every reader and every schema set SHALL be the shared resolver.
- **Entity expansion SHALL be bounded** by a limit the design names, so that a document built to expand without end is refused with a sentence and does not exhaust the backend.
- Both of these SHALL be held by tests that were seen to fail with the bound removed: a document that names a file outside the project, and a document of nested entities.
- A request carries text a client sent; its size SHALL be bounded as the family's save is.
- No XSLT SHALL be run, and no script inside one.

### Reliability

- IF validation throws THEN the call SHALL answer with a sentence, the editor SHALL keep colour, and the backend SHALL log the exception once with the document's project-relative path.

### Usability

- Every finding and every completion SHALL be reachable without a pointer.
- The validator's sentences are shown as it gives them, in English.

## Out of scope

- XSD 1.1, RELAX NG and Schematron.
- Running or previewing XSLT, and evaluating XPath.
- Formatting or re-indenting a document.
- Drawing an SVG.
- Knowing MSBuild, or any other vocabulary that publishes no schema a project can name.
- A catalogue that maps a namespace to a schema without the document saying where it is.

## Sources

- npm registry metadata, read 2026-10-12: `@codemirror/lang-xml` 6.1.0 MIT; `@lezer/xml` 1.0.6 MIT; `xmllint-wasm` 5.3.0 MIT; `fast-xml-parser` 5.11.2 MIT. Download counts are npm's last-week figures of the same day.
- `System.Xml.Schema.XmlSchemaValidator` and its `GetExpectedParticles` and `GetExpectedAttributes`: named from the .NET documentation as remembered, not run. Requirement 1 runs them.
- `src/api/editors.proto`, `src/client/package.json`, the `Diagram.cs` files of `src/diagrams/` at `eae4c2221`.
- [`json-editor` requirements](../json-editor/requirements.md), for everything this document takes by reference.
- [ADP terminology](https://github.com/etalii-adp/etalii.adp/blob/develop/docs/terminology.md) and the EDSL placeholder, etalii.adp at `73ec4a7`.
