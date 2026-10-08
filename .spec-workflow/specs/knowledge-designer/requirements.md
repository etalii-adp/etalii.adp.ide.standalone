# Requirements Document

## Introduction

The user's request, 2026-10-08, verbatim:

> Add an idea to the tools page for a "Knowledge designer" => A designer that stores row and column based information in yaml, xml or json files. Inspired by Notion's database and other similar solutions, it brings the knowledge engineering capability to local systems. It's goal is to assist users with the modelling of knowledge. Both for personal use but also  facilitate human-tailored LLM knowledge engineering.
>
> The idea is that:
>
> * Properties can quickly be generated/changed. Text based, numeric, date times, time, selection, multiple select etc.
> * That multiple of such database files can also be linked together by defining properies as (multi) directional relations.
> * That each file also contains views, in which property visibility, sorting, filtering and row grouping (by parent, select property etc.) can be applied.

The idea is in the catalogue as `etalii/knowledge`, kind Designer, state Identified ([`docs/tools.md`](../../../docs/tools.md), standalone pull request 140). This document specifies it.

**It is ADP's first designer.** Every tool ADP offers today is a diagram or an editor. So this specification has two halves: the Knowledge designer itself (Requirements 2 to 8), and what any designer needs from the languages and from this host before one can exist (Requirements 1 and 9). The second half is deliberately no larger than the first designer needs.

**Two repositories.** The designer type is specified in [etalii.adp](https://github.com/etalii-adp/etalii.adp), where the languages and the tool definitions live; this host implements it. Everything Requirement 1 names is delivered there as pull requests, before the work here that depends on it.

## Terms

| Term | Meaning |
| --- | --- |
| **Knowledge file** | One file in the user's workspace holding one table: its properties, its views and its rows. Written in YAML, JSON or XML. |
| **Property** | A column: a name, a type, and what the type needs (the options of a selection, the target of a relation). |
| **Row** | One entry of a knowledge file: an identity and one value per property. |
| **Title property** | The one text property whose value names a row wherever the row is shown outside its own table, such as in a relation cell. |
| **Relation** | A property whose values are rows of a knowledge file, the same one or another. |
| **View** | A named way of showing one knowledge file's rows: which properties are visible and in what order, sorting, filtering and grouping. Stored in the file. |
| **Finding** | As in ADP terminology: something a reading reports about a file, with a severity and a place. |

## Which languages apply

The designer pair is **DESL** (Designer Specification Language, `.des`), in which a tool engineer specifies a designer type, and **DED** (Designer Definition Language, `.ded`), in which a designer a user created is stored. DISL and DID are the diagram pair and do not apply: a knowledge file's rows are filled in, not drawn as connected elements. (DIDL and DEDL are retired names; the terminology lists them.)

- **DESL applies, and has no content yet.** The Knowledge designer type belongs in `definitions/designers/knowledge.des`, with `knowledge.md` beside it for what DESL cannot express. DESL is a placeholder today, so the first of these needs DESL to gain its first constructs (Requirement 1, Q2).
- **DED does not fit the request as written.** The request stores knowledge in YAML, XML or JSON files. In ADP's terms those are **bodies**: files in a general format, read and written through an **FBL** binding, changed by minimal splices. FBL already has a `yaml`, a `json` and an `xml` family, and this host already implements all three. A `.ded` file would be a fourth format the request did not ask for. This document is written against FBL bindings, with DED left a placeholder (Q1).

## What was measured

Read on 2026-10-08: this repository on `develop` at `61f802e0`, etalii.adp on `origin/develop` at `f8451b4`, and [vrenken/egui-data-table](https://github.com/vrenken/egui-data-table) on `master`.

| Subject | State | Where |
| --- | --- | --- |
| The designer module folder | **Exists, empty** | `src/designers/readme.md` |
| Discovery of designer types | **Exists, finds none** | `DesignerDefinitionDiscovery`, through `ToolDefinitionScan` |
| What a designer type declares | **An id and a title only** | `src/backend/EtAlii.Adp/_Model/DesignerDefinition.cs` |
| Client registration | **Exists** | `toolPanels.ts` globs `{diagrams,designers,editors}/*/client/register.ts` |
| Catalog and session seams for designers | **Missing**, by the account of `docs/creating-a-designer-module.md` (*What the first designer adds*); there is no `EtAlii.Adp.Designer` project beside `EtAlii.Adp.Diagram` and `EtAlii.Adp.Editor` | `src/backend` |
| A table or grid component in the client | **Missing**: no dependency and no folder for one | `src/client/package.json`, `src/client/src` |
| Reading and writing YAML, JSON and XML bodies | **Exists** | `EtAlii.Adp.Specification.Fbl` (`YamlFamily.cs`, `JsonFamily.cs`, `XmlFamily.cs`); FBL specification sections 4.3 to 4.5 |
| DESL and DED | **Placeholders**: no construct, no schema, no example | `specifications/desl`, `specifications/ded` in etalii.adp |
| Designer definitions | **Placeholder readme only** | `definitions/designers` in etalii.adp |
| The definition of a designer | "A form-based visual layout ... nothing in it is connected" | `docs/terminology.md` in etalii.adp |

**The test-data hint.** `correlate/` in egui-data-table is a table tool of the same family. Its column types are text, number, date and time, boolean, select, multiple select and relation (`correlate/src/data/column_type.rs`); a column can be the key or the name of a row, visible or hidden, ordered and sized; a select option has a value and a colour; and a relation value names a source, a key and a value. Its `correlate/test/data` holds three sets: `cities` (German and Dutch cities, each as CSV, JSON and XLSX), `sheets` (multi-sheet workbooks) and `students` (one CSV). The repository is MIT-licensed; **no licence statement was found for the data sets themselves**, and none was looked for beyond the file listing. Requirement 8 handles that.

## Open questions

Each is a selection. The option marked **(default)** is what this document is written against until the user rules otherwise.

| # | Question | Options | Criteria affected |
| --- | --- | --- | --- |
| Q1 | How is a knowledge file stored? | **Three FBL bindings (default):** the file is a body in YAML, JSON or XML, read and written through one binding per format; DED stays a placeholder. Follows the request, reuses what the host has, and keeps every edit a minimal splice. Costs FBL whatever it lacks for tables (unknown until the design reads it against the three families). · **DED gains content:** a knowledge file is a `.ded` file in one format. One format to maintain, and it gives DED its first use, but it is not the YAML, XML or JSON the request asks for. · **Both:** DED defines the model, and FBL bindings carry it into three formats. The most complete, and the most language work before anything runs. · Other. | 1.2, 1.3, 2.1, 2.2 |
| Q2 | How much of DESL does the first designer bring? | **DESL 0.1 with what this designer needs, and a hand-written module checked against it (default):** `knowledge.des` is real and normative; the host's module is written by hand and a guard fails when the two disagree on property types or view settings. Keeps the first designer small. · **DESL 0.1 and a DESL runtime in this host:** the designer is derived from `knowledge.des`, as DISL-derived diagrams are. The right end state, and a much larger first step. · **No DESL yet:** only `knowledge.md` in prose; DESL waits for a second designer. Fastest, and leaves the definitions folder without a specification file. · Other. | 1.1, 1.5, 9.5 |
| Q3 | What identifies a row? | **A generated id stored with the row (default):** stable when the row is renamed, reordered or sorted; relations refer to it. Costs one short opaque value per row in the file. · **A key property the user names:** readable in the file and in relations, as correlate does; renaming a key must then rewrite every file that refers to it. · **The row's position:** nothing stored, and any reorder or outside edit breaks relations. · Other. | 4.1, 5.2, 5.6 |
| Q4 | Where is a two-way relation stored? | **On one side only (default):** the property that was created holds the values; its counterpart in the other file is declared and shows the reverse, computed. Two files can never disagree. Costs: reading the counterpart needs the other file open. · **On both sides:** each file is complete when read alone, which suits an LLM reading one file; ADP keeps them in step and reports a finding when an outside edit made them disagree. · Other. | 5.3, 5.4 |
| Q5 | Does a tool with relations between files still count as a designer? | **Yes, and the terminology says why (default):** a diagram *draws* connections between elements; here a relation is a value in a cell, and nothing is drawn connected. One sentence is added to `docs/terminology.md`. · **Yes, without a change:** leave the definition as it is and accept the question being asked again. · **No:** it needs a new kind or a different one. That reopens the catalogue row. · Other. | 1.4 |

## Alignment with Product Vision

`product.md` describes specialized, task-tuned tools over plain text files the user owns. This designer brings the "bringing clarity to textual data" and "humans and agents together" halves of the project's goal to tabular knowledge: the file is plain text, diffable and readable without ADP, by a person or by a language model, and ADP is one way of working in it rather than its owner. It follows `structure.md`'s rule that a tool type is a module on the shared core: what a designer needs from the core is added once, for every designer (Requirement 9), and is named for what it does, never for this designer.

## Requirements

### Requirement 1 - Specified in etalii.adp first

**User Story:** As a tool engineer, I want the Knowledge designer type specified where every other tool type is, so that each host implements one definition.

#### Acceptance Criteria

1. WHEN the work starts THEN `definitions/designers/knowledge.des` and `definitions/designers/knowledge.md` SHALL be added to etalii.adp, the first holding everything about the designer type that DESL can express and the second everything it cannot (Q2).
2. WHEN a knowledge file's formats are specified THEN there SHALL be one FBL binding per format (YAML, JSON, XML) in etalii.adp, each with a fixture that proves reading and every kind of edit this document names (Q1).
3. WHEN DESL or FBL lacks a construct this designer needs THEN the construct SHALL be added to that language's specification and schema in etalii.adp, named for what it does and not for this designer. Nothing SHALL be added to DISL or DID for this designer.
4. WHEN the designer is specified THEN `docs/terminology.md` in etalii.adp SHALL say why a tool whose cells hold relations is a designer and not a diagram (Q5), and its Designer row SHALL name the Knowledge designer as an example.
5. WHEN anything in this requirement is delivered THEN it SHALL be a pull request into etalii.adp's `develop`, merged before the task in this repository that depends on it starts.
6. WHEN the definition in etalii.adp and this document disagree THEN the definition SHALL win and this document SHALL be amended, since the other hosts read the definition and not this document.

### Requirement 2 - The knowledge file

**User Story:** As an author, I want one table in one plain text file that I own, so that I can read it, diff it, version it and hand it to a language model without ADP.

#### Acceptance Criteria

1. WHEN a knowledge file is stored THEN it SHALL be one file in the user's workspace holding that table's properties, its views and its rows, and nothing about it SHALL be stored anywhere else except its `.adp` registration.
2. WHEN a knowledge file is created THEN the author SHALL choose YAML, JSON or XML, and the three SHALL be equivalent: the same table written in each SHALL show the same properties, views, rows and findings.
3. WHEN a knowledge file is read THEN it SHALL declare the version of its schema, and a file of a newer version than the host knows SHALL open read-only with a finding rather than be rewritten.
4. WHEN an edit is made THEN only the bytes that edit concerns SHALL change: comments, key order, indentation, line endings and everything the edit did not touch SHALL stay byte for byte as they were.
5. WHEN a file holds keys or elements the designer does not know THEN they SHALL be kept on every write and reported as a finding of severity info.
6. WHEN the same edits are made to the same file THEN the bytes written SHALL be the same, on every machine.
7. WHEN a knowledge file is read without ADP THEN a reader SHALL be able to tell every property's name and type, every option of a selection, and every row's values from that file alone, without reading another file or knowing a property's position.

### Requirement 3 - Properties

**User Story:** As an author, I want to add and change properties in a moment, so that the table's shape follows my understanding as it develops.

#### Acceptance Criteria

1. WHEN a property is added THEN it SHALL have one of these types: **text**, **number**, **checkbox**, **date**, **date and time**, **time**, **selection**, **multiple selection** or **relation**. (The request names text, numeric, date times, time, selection and multiple select, and ends in "etc."; checkbox and date are this document's reading of that, and relation comes from the request's second point.)
2. WHEN the author adds a property THEN it SHALL take one gesture from the table's header plus a choice of type, and the new property SHALL be visible in the current view at once.
3. WHEN the author works on a property THEN they SHALL be able to rename it, change its type, reorder it and delete it, each from the property's header and each as one undoable step.
4. WHEN a property's type is changed THEN every value that has a meaning in the new type SHALL be converted, and every value that has none SHALL be kept in the file as it was and reported as a finding on its cell. No value SHALL be discarded by a type change.
5. WHEN a property is a selection or a multiple selection THEN its options SHALL each have a name and a colour, SHALL be addable while filling in a cell, and SHALL be renamable, reorderable and deletable. Renaming an option SHALL rename it in every row.
6. WHEN an option is deleted while rows use it THEN the author SHALL be told how many rows use it before it is deleted, and those rows' values SHALL be cleared in the same undoable step.
7. WHEN a knowledge file exists THEN exactly one text property SHALL be its title property. It SHALL NOT be deletable or changed to another type, and another text property SHALL be assignable in its place.
8. WHEN a property is deleted THEN its values SHALL be removed from every row and it SHALL be removed from every view's settings, in one undoable step.
9. WHEN two properties would have the same name THEN the second SHALL be refused, with the reason shown where the name is typed.

### Requirement 4 - Rows and cells

**User Story:** As an author, I want to fill in rows as fast as I can type, with each cell edited in the way its type calls for.

#### Acceptance Criteria

1. WHEN a row is added THEN it SHALL receive its identity (Q3) and SHALL appear in the current view even when the view's filter would exclude an empty row, until the view is next opened.
2. WHEN a row is added inside a group THEN it SHALL be created with the value that puts it in that group.
3. WHEN a cell is edited THEN its editor SHALL match its property's type: free text; a number; a checkbox; a date, a date and time or a time, typed or picked; one option or several, picked or typed to create; rows of the target file, searched by title.
4. WHEN a value is typed that its type cannot hold THEN it SHALL be refused where it is typed, with the reason, and the cell's stored value SHALL be unchanged.
5. WHEN the author uses the keyboard THEN they SHALL be able to move between cells, start and finish editing, and add a row without the mouse.
6. WHEN one or more rows are selected THEN they SHALL be deletable in one undoable step, and the properties of a single selected row SHALL be shown in the property grid.
7. WHEN any edit is made THEN undo and redo SHALL work as for every other tool in this host, and the file SHALL be saved as for every other tool.
8. WHEN a date, a date and time or a time is stored THEN it SHALL be written in ISO 8601, and a date and time SHALL carry its offset.

### Requirement 5 - Relations between knowledge files

**User Story:** As an author, I want a property whose values are rows of another table, so that several tables together model one body of knowledge.

#### Acceptance Criteria

1. WHEN a relation property is created THEN the author SHALL choose its target knowledge file, which MAY be the file itself, and whether each cell holds one row or several.
2. WHEN a relation value is stored THEN it SHALL name the target row by its identity (Q3) and the target file by a path relative to the file that holds the value.
3. WHEN a relation property is created THEN the author SHALL choose **one-way** or **two-way**. A two-way relation SHALL appear as a property in the target file too, under a name the author gives it, and each side SHALL show the rows that point at it from the other.
4. WHEN a two-way relation is edited from either side THEN both sides SHALL show the change at once, and the two SHALL never be left disagreeing by an edit made in ADP (Q4).
5. WHEN a relation cell is shown THEN it SHALL show each related row's title, and each SHALL open its row in its own file.
6. WHEN a relation's target file is missing, unreadable or not a knowledge file, or a value names a row that is not there THEN the value SHALL be kept in the file, shown as unresolved, and reported as a finding. It SHALL NOT be removed by ADP.
7. WHEN a target file is renamed or moved inside ADP THEN every relation that names it in an open workspace SHALL be rewritten in the same step.
8. WHEN a row is deleted while rows in other files relate to it THEN the author SHALL be told how many before it is deleted.
9. WHEN a relation's target is the file itself THEN the property SHALL be assignable as the file's **parent** relation, holding at most one row per cell, and a row SHALL NOT be made its own ancestor.

### Requirement 6 - Views

**User Story:** As an author, I want several named ways of looking at one table, kept with the table, so that the same knowledge answers different questions.

#### Acceptance Criteria

1. WHEN a knowledge file is stored THEN its views SHALL be stored in it, and a file SHALL always have at least one view.
2. WHEN the author works on views THEN they SHALL be able to add, rename, duplicate, reorder and delete a view and switch between views, and the last remaining view SHALL NOT be deletable.
3. WHEN a view is set up THEN the author SHALL be able to choose which properties it shows, in what order and at what width. The title property SHALL always be shown.
4. WHEN a view is sorted THEN it SHALL sort by one or more properties in a stated order, each ascending or descending, in the way its type calls for: numbers as numbers, dates as dates, selections in their options' order.
5. WHEN a view is filtered THEN it SHALL combine one or more conditions with *all* or *any*, each condition offering the comparisons its property's type calls for, including *is empty* and *is not empty* for every type.
6. WHEN a view is grouped by a selection, a multiple selection, a checkbox or a relation THEN rows SHALL be shown under one collapsible heading per value, with a heading for rows that have none, and a row with several values SHALL appear under each.
7. WHEN a view is grouped by the parent relation (Requirement 5.9) THEN rows SHALL be shown nested under their parents to any depth, each parent collapsible.
8. WHEN a view's settings are changed THEN no row and no property SHALL change, the change SHALL be an edit of the file like any other (undoable, saved, visible in a diff), and other views SHALL be unaffected.
9. WHEN a view names a property that no longer exists, by an edit outside ADP THEN the view SHALL still open, without that setting, and report a finding.

### Requirement 7 - Readable and writable by people and by language models

**User Story:** As someone engineering knowledge with a language model, I want the model and me to work in the same file, so that neither of us needs the other's tool.

#### Acceptance Criteria

1. WHEN a knowledge file is changed outside ADP, by a person, a script or a language model THEN an open designer SHALL show the change without being reopened, as every tool in this host does for its file.
2. WHEN a file changed outside ADP holds something the designer cannot accept (an unknown type, a value that does not fit its property, a duplicate row identity, a view naming a missing property) THEN the file SHALL still open, everything readable SHALL be shown, and each such thing SHALL be a finding that says where it is. Nothing unreadable SHALL be removed on the next write.
3. WHEN a file cannot be read at all THEN the designer SHALL say so with the format's own error and its position, and SHALL NOT write to the file.
4. WHEN the designer type is published THEN `knowledge.md` SHALL describe the file in each of the three formats completely enough to write a valid one by hand, with one complete example per format, and a JSON Schema SHALL be published for the JSON and YAML forms.
5. WHEN a row is added without ADP and carries no identity THEN the designer SHALL give it one on the first edit made in ADP, and until then SHALL show the row and report a finding of severity info.

### Requirement 8 - Examples and test data

**User Story:** As the user, I want the designer proven against data that looks like real knowledge work, not against toys.

#### Acceptance Criteria

1. WHEN the module ships THEN it SHALL ship examples in `src/examples/designers/knowledge` that between them show every property type, a one-way and a two-way relation between two files, a parent relation, and views using visibility, sorting, filtering and each kind of grouping; and at least one example SHALL exist in all three formats.
2. WHEN example data is taken from `correlate/test/data` of egui-data-table or from anywhere else THEN it SHALL be vendored under the rule in `CLAUDE.md` (*Vendored example data*): the licence read from the data set's own statement at acquisition, share-alike refused, the licence file vendored beside the data, and the readme saying what the corpus does not demonstrate. A data set whose own licence cannot be established SHALL NOT be vendored; its **shape** MAY be reproduced with data written for ADP.
3. WHEN the examples are tested THEN a backend test SHALL open every one, assert it reads without findings of severity error, and assert that writing it unchanged leaves its bytes unchanged.
4. WHEN the three formats are tested THEN one test SHALL read the same table from each and compare the three models, and for each kind of edit this document names a test SHALL apply it to each format and compare the three again.
5. WHEN a fixture's bytes are what a test compares THEN the fixture SHALL be exempt from line-ending conversion by a rule in `.gitattributes` that does not also exempt ordinary YAML, JSON and XML files in this repository.
6. WHEN a view is tested at size THEN an example of at least ten thousand rows SHALL be used (Non-Functional Requirements, Performance).

### Requirement 9 - What this host gains for every designer

**User Story:** As a maintainer, I want what the first designer needs from the core added once, so that the second designer is a module and nothing more.

#### Acceptance Criteria

1. WHEN the module is added THEN it SHALL live in `src/designers/knowledge` with the four folders its siblings use (`backend`, `api`, `client`, `examples`), SHALL be found by `DesignerDefinitionDiscovery` and by the shell's registry with no core file naming it, and deleting the folder SHALL remove the designer type.
2. WHEN `DesignerDefinition` is widened, and when the host gains the catalog and session seams it serves designers through THEN each SHALL be shaped as the diagram and editor families' are, SHALL live in core, and SHALL name no designer type.
3. WHEN the client gains a table (rows, typed cells, headers, grouping, a view bar) THEN it SHALL be a shared component beside the canvas library, named for what it shows, which the module declares against. The module's client SHALL hold declarations and event handlers and no rendering of its own.
4. WHEN a knowledge file is created from the workspace tree THEN it SHALL be offered where every other tool type is, with the choice of format (Requirement 2.2), and SHALL be created with its registration, one title property, one view and no rows.
5. WHEN the designer is specified in DESL (Q2) THEN a guard in this repository SHALL read `knowledge.des` and fail when the module's property types or view settings differ from it.
6. WHEN the module pushes changes to an open designer THEN they SHALL ride the connection's existing `Watch` stream as a member of its message, never a second stream.
7. WHEN the work completes THEN `docs/creating-a-designer-module.md` SHALL describe a designer module from end to end with this one as its worked example, `docs/architecture.md` and `docs/solution-structure.md` SHALL be true of the result, and the `etalii/knowledge` row of `docs/tools.md` SHALL move with the designer's state.

### Requirement 10 - Seen working, and covered

**User Story:** As the user, I want the designer seen working where it is seen, and every criterion here claimed.

#### Acceptance Criteria

1. WHEN the work completes THEN a `tests.md` entry SHALL cover, in a real browser and in both themes: creating a knowledge file in each format; adding a property of each type and filling in a cell of each; changing a property's type with values that do and do not convert; a two-way relation edited from both sides; a parent relation shown nested; a view with hidden properties, two sorts, a filter and a grouping; a second view left unchanged by the first; an edit made outside ADP appearing in the open designer; undo and redo across each of these; and the saved file's diff showing only what was edited.
2. THEN jsdom SHALL NOT be taken as evidence of any of these, since it applies no CSS and lays out no text.
3. WHEN a guard is written for any criterion THEN it SHALL be seen to fail against a planted defect before it is trusted, and the task SHALL name that defect.
4. WHEN the tasks document is written THEN every acceptance criterion here SHALL be claimed by a task, established by diffing the two sets before the tasks card is raised and again after implementing.

## Non-Functional Requirements

### Code Architecture and Modularity

- The module depends on core and never the reverse; the table component, the designer seams and any language construct are named for what they do (Requirements 1.3, 9.2, 9.3).
- The backend is the only reader and writer of knowledge files; the client speaks gRPC, as for every tool.

### Performance

- A view of ten thousand rows SHALL scroll without a perceptible pause and SHALL draw only the rows in sight.
- An edit to one cell of a ten-thousand-row file SHALL be shown at once and SHALL write a splice, not the whole file.
- Sorting, filtering and grouping ten thousand rows SHALL take effect without a perceptible pause.

### Reliability

- The designer never discards what it cannot read (Requirements 2.5, 3.4, 5.6, 7.2), and never writes to a file it could not read at all (Requirement 7.3).
- Viewing never writes: opening a file, switching views, collapsing a group and scrolling SHALL leave the file's bytes unchanged.

### Usability

- A value that cannot be accepted is refused where it is typed, with its reason (Requirements 3.9, 4.4).
- Every gesture has a keyboard path (Requirement 4.5), and every control exposes its role and state to assistive technology.

## Out of scope

Each is a later specification if it is wanted, and nothing here should be read as deciding it:

- Formulas, rollups and any other computed property besides the reverse side of a relation.
- Views that are not a table: board, calendar, gallery, timeline.
- Importing or exporting CSV, XLSX or a Notion database, and synchronising with Notion.
- Several tables in one file.
- The other hosts (IntelliJ, VS Code, Eclipse, Notion). They read the same definition (Requirement 1) under their own specifications.
- A DESL runtime in this host, unless Q2 is ruled that way.

## Sources

- The user's request in the project chat, 2026-10-08.
- This repository at `develop` `61f802e0`: `docs/tools.md`, `docs/creating-a-designer-module.md`, `docs/architecture.md`, `src/designers/readme.md`, `src/backend/EtAlii.Adp/_Model/DesignerDefinition.cs`, `src/backend/EtAlii.Adp.Specification.Fbl`.
- etalii.adp at `origin/develop` `f8451b4`: `docs/terminology.md`, `specifications/desl/DESL-specification.md`, `specifications/ded/DED-specification.md`, `specifications/fbl/FBL-specification.md` (section headings 4 and 12), `definitions/designers/README.md`.
- [vrenken/egui-data-table](https://github.com/vrenken/egui-data-table) at `master`: `correlate/src/data` (`column_type.rs`, `column_configuration.rs`, `relation.rs`, `data_source_configuration.rs`) and the listing of `correlate/test/data`.
- [Notion: intro to databases](https://www.notion.com/help/intro-to-databases), named by the catalogue row as the inspiration.
