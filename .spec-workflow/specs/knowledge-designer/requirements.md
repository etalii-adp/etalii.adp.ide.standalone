# Requirements Document

## Introduction

The user's request, 2026-10-08. It is verbatim except in two places, marked with square brackets, where it names a retired acronym that this repository's terminology check refuses; *Which languages apply* says what was written and how it is read.

> add a spec workflow MCP specification to implement the idea of a "Knowledge Designer" tool:
>
> A designer that stores row and column based information in yaml, xml or json files. Inspired by Notion's database and other similar solutions, it brings the knowledge engineering capability to local systems. It's goal is to assist users with the modelling of knowledge. Both for personal use but also facilitate human-tailored LLM knowledge engineering.
> The idea is that:
>
> * Properties can quickly be generated/changed. Text based, numeric, date times, time, selection, multiple select etc.
> * That multiple of such database files can also be linked together by defining properies as (multi) directional relations.
> * That each file also contains views, in which property visibility, sorting, filtering and row grouping (by parent, select property etc.) can be applied.
>
> Make sure that the styling and functionalities represent how Notion has implement it. Grab screenshots and/or search the internet for understanding on how it functions.
>
> The implementation should follow a FBL and [a retired language name], so design those first. If the specification languages are not sufficient then let it know so that we can extend it.
>
> Persist also all settings in the yaml/xml/json file. For example if columns (properties) are visible or hidden, what their type is, what the potential values can be and what the width of the column is. Also all filters, sortings, groupings and view related aspects.
>
> Internally use a short guid for the identification ID of all things that need it. e.g. rows, columns, cells, views and select options.
>
> A bit dated but there was an attempt before to write this in Rust. It is far from elegant so the code base is illogical. However the test data might show some hints on how to engineer this:
>
> https://github.com/vrenken/egui-data-table/tree/master/correlate/test/data
>
> The option of what file format is to be used should be done when adding such a file. It is nasty to change it afterwards.
>
> So there should be two files: a json/xml/yaml, and of course the smaller .adp file. The latter should solely be used for identification, all functional data should be stored in the data file.
>
> Also: Introduce a way to add (bidirectional) relation properties. My gut feeling is that this requires the selection of another database file using a file open dialog.
>
> Vital is that the [retired language name] and FBL files get defined correctly, as those will later also be used to implement the same tool in the other IDE's.

The idea is in the catalogue as `etalii/knowledge`, kind Designer, state Identified ([`docs/tools.md`](../../../docs/tools.md)). This document specifies it.

**This replaces a first version of this document**, written from the shorter catalogue request before the request above was read. That version's card should be rejected; what changed is listed under *Sources*.

**It is ADP's first designer.** Every tool ADP offers today is a diagram or an editor. So this specification has two halves: the Knowledge designer itself (Requirements 2 to 9), and what any designer needs from the languages and from this host before one can exist (Requirements 1 and 10).

**Two repositories, and the definition comes first.** The designer type is defined in [etalii.adp](https://github.com/etalii-adp/etalii.adp), where the languages and the tool definitions live and where the other hosts will read them; this host implements that definition. Requirement 1 is delivered there as pull requests, before the work here that depends on it.

## Terms

| Term | Meaning |
| --- | --- |
| **Knowledge file** | The data file: one file in the user's workspace holding one table, its properties, its views and its rows. Written in YAML, JSON or XML. |
| **Registration** | The `.adp` file beside a knowledge file. It identifies the file as a Knowledge designer document and holds nothing else. |
| **Property** | A column: an id, a name, a type, and what the type needs (the options of a selection, the target of a relation). |
| **Row** | One entry of a knowledge file: an id and one value per property. |
| **Cell** | One row's value for one property. |
| **Title property** | The one text property whose value names a row wherever the row is shown outside its own table, such as in a relation cell. |
| **Option** | One of the values a selection or multiple selection can take: an id, a name and a colour. |
| **Relation** | A property whose values are rows of a knowledge file, the same one or another. |
| **View** | A named way of showing one knowledge file's rows: which properties are visible, in what order and at what width, and sorting, filtering and grouping. Stored in the knowledge file. |
| **Id** | A ShortGuid, as this host already generates them (`EtAlii.Adp.ShortGuid`). |
| **Finding** | As in ADP terminology: something a reading reports about a file, with a severity and a place. |

## Which languages apply

The request names **FBL and a second language**. FBL is right as named. **The second is a retired name**: the acronym of an earlier draft of the diagram definition language, the one that differs from DISL only in its third letter being D (`docs/terminology.md` in etalii.adp, *Retired uses*), and this document reads the retired name as "the file that specifies the tool type". For a designer that language is **DESL**, the Designer Specification Language (`.des`); DISL is its counterpart for diagrams and does not apply, since a knowledge file's rows are filled in rather than drawn as connected elements. Q1 asks whether that reading is right.

- **FBL** declares how the knowledge file is read and written: one binding per format. FBL has a `yaml`, a `json` and an `xml` family, and this host already implements all three (`EtAlii.Adp.Specification.Fbl`).
- **DESL** specifies the designer type: its property types, what a view can set, its gestures and how it looks. **DESL is an empty placeholder today**: no construct, no schema, no example. The Knowledge designer is what gives it its first content.
- **DED**, the Designer Definition Language (`.ded`), stays a placeholder. It is where a designer would be stored if it had no other file; the request stores it in YAML, JSON or XML, which is what FBL is for.

## Whether the languages suffice

The request asks to be told where the specification languages fall short, so they can be extended.

**DESL: not sufficient, because it does not exist yet.** Everything `knowledge.des` needs to say has to be added to DESL first. Nothing in it can be reused from a previous designer; the pattern to follow is DISL's.

**FBL: assessed against a knowledge file's shape, with the result below.**

**FBL 0.2 cannot bind a knowledge file as it stands.** It was read in full on etalii.adp's `origin/develop` against what Requirements 2 to 6 need. This is a reading of the specification's text, not a trial with a binding; a few of its sentences were checked a second time and held, and the design repeats the reading against a draft binding before any extension is proposed.

| # | What a knowledge file needs | What FBL 0.2 has | Blocks |
| --- | --- | --- | --- |
| G1 | **List values in all three formats**, with one item added, removed or moved by its own splice: a multiple selection's options, a relation's rows, a view's sorts. | A YAML flow collection is one value whose "only writable span is the whole collection" (section 4.3); no list slot for block sequences, JSON arrays or XML. | Multiple selection, relations to several rows, deleting an option that rows use. |
| G2 | **A row's values keyed by property id**: keys that the file itself defines. | A selector can match and capture any key (sections 4.2, 5.2), but no slot reads or writes the matched member's own scalar value in YAML or JSON. | Storing a row as a map from property to value. The way round it is a list of property-and-value pairs per row, which is a different file shape (Q4). |
| G3 | **A reference into another file, and one edit that writes two files** and is undone as one. | References name rules of the same binding and body (section 5.7); one that finds nothing is reported as dangling. History is per body; several files in one edit exist only for a body with its registration and for folder subjects (sections 8.4, 10.4). | Relations between files; two-way relations stored on both sides (Q5). |
| G4 | **A model that is not a diagram's.** | FBL's rules produce "the DISL node type" (section 5.1), an edit is "one DISL transaction" (section 6.4), and ids and findings are DISL's. DESL defines none of these. | Everything: either DESL defines the same contract (types, ids, findings, transactions), or FBL's model side is stated independently of DISL. |
| G5 | **Three formats for one tool type**, chosen when the file is added, with a statement that they are equivalent. | One binding per specification (section 1.2). YAML and JSON could share a binding through `alsoRead`; XML's slots differ, so it needs its own. Templates for a new file are not per format. | Choosing the format when adding; XML at all. |
| G6 | **The same new id used twice in a new file's template**, so the first view can name the first property. | Each id placeholder gives a fresh id (section 13). | Creating a file with one property and one view that shows it. |

What FBL already covers: the version mark in a header; elements with ids read from and written to the file; nested entries such as a selection's options and a filter's groups; ordered entries with move; renaming through references; unknown content kept byte for byte and unreadable entries reported, never dropped; and recognising a generic `.yaml`, `.json` or `.xml` file by a marker and its registration.

**G4 decides how large the language work is**, and it is the same question as Q1 seen from FBL's side.

Requirement 1.4 turns each gap into a decision for the user before anything is added to a language.

## What was measured

Read on 2026-10-08: this repository on `develop` at `61f802e0`, etalii.adp on `origin/develop` at `f8451b4`, [vrenken/egui-data-table](https://github.com/vrenken/egui-data-table) on `master`, and Notion's published help pages.

| Subject | State | Where |
| --- | --- | --- |
| The designer module folder | **Exists, empty** | `src/designers/readme.md` |
| Discovery of designer types | **Exists, finds none** | `DesignerDefinitionDiscovery`, through `ToolDefinitionScan` |
| What a designer type declares | **An id and a title only** | `src/backend/EtAlii.Adp/_Model/DesignerDefinition.cs` |
| Client registration | **Exists** | `toolPanels.ts` globs `{diagrams,designers,editors}/*/client/register.ts` |
| Catalog and session seams for designers | **Missing**, by the account of `docs/creating-a-designer-module.md` (*What the first designer adds*); there is no `EtAlii.Adp.Designer` project beside `EtAlii.Adp.Diagram` and `EtAlii.Adp.Editor` | `src/backend` |
| A table or grid component in the client | **Missing**: no dependency and no folder for one | `src/client/package.json`, `src/client/src` |
| A dialog for choosing a workspace file | **None found** under the names file picker, file dialog or pick file in the client sources and the `.proto` contracts. That is a search, not proof of absence; the design establishes it. | `src/client/src`, `src/api` |
| Short ids | **Exists** | `src/backend/EtAlii.Adp/ShortGuid.cs` |
| Reading and writing YAML, JSON and XML bodies | **Exists** | `EtAlii.Adp.Specification.Fbl` (`YamlFamily.cs`, `JsonFamily.cs`, `XmlFamily.cs`); FBL specification sections 4.3 to 4.5 |
| DESL and DED | **Placeholders** | `specifications/desl`, `specifications/ded` in etalii.adp |
| Designer definitions | **Placeholder readme only** | `definitions/designers` in etalii.adp |
| The definition of a designer | "A form-based visual layout ... nothing in it is connected" | `docs/terminology.md` in etalii.adp |

**The earlier Rust attempt.** `correlate/` in egui-data-table has the column types text, number, date and time, boolean, select, multiple select and relation (`correlate/src/data/column_type.rs`). A column can be the key or the name of a row, visible or hidden, ordered and sized; a select option has a value and a colour; a relation value names a source, a key and a value. It keeps these settings in a `.correlate` file **beside** the data file, which is the opposite of what this request rules: here every setting is in the data file. Its `correlate/test/data` holds three sets: `cities` (German and Dutch cities, each as CSV, JSON and XLSX), `sheets` (multi-sheet workbooks) and `students` (one CSV). The repository is MIT-licensed; **no licence statement was found for the data sets themselves**, and none was looked for beyond the file listing. Requirement 9 handles that.

**Notion.** Read from its help pages on database properties, on views, filters and sorts, and on relations ([properties](https://www.notion.com/help/database-properties), [views](https://www.notion.com/help/views-filters-and-sorts), [relations](https://www.notion.com/help/relations-and-rollups)). What Requirement 7 takes from them: views as tabs above the table; one settings place per view for property visibility, filter, sort and group; a `+` after the last column to add a property, and insert left or right from a column's menu; a column's menu for renaming, filtering, sorting, hiding, duplicating and deleting; options as coloured tags created by typing; sorts ordered by dragging; filters as simple conditions or nested all/any groups; groups that can be hidden, ordered and collapsed, with empty groups optionally hidden; and a relation created by choosing a target, optionally shown on the target too under its own name, limited to one row or unlimited. Sub-items, which are how Notion groups rows by parent, are not on those pages; Requirement 5.9 and 6.7 describe them from general knowledge of the product, and the design verifies them against the product before relying on them. **No screenshot of Notion is stored in this repository**: they are Notion's, and the links above are the reference.

## Open questions

Each is a selection. The option marked **(default)** is what this document is written against until the user rules otherwise.

| # | Question | Options | Criteria affected |
| --- | --- | --- | --- |
| Q1 | "FBL and [a retired language name]": which file specifies the tool type? | **DESL, in `knowledge.des` (default):** the designer language, as the terminology assigns it. It needs DESL's first constructs, which other designers then reuse. · **DISL, in `knowledge.dis`:** the diagram language, which has content and a runtime in this host today; a table would have to be expressed as elements without relations, and the catalogue row's kind comes into question. · **Prose only, in `knowledge.md`:** no specification file until a second designer shows what DESL should be. Fastest, and the other hosts get a description instead of a definition. · Other. | 1.1, 1.2, 10.5 |
| Q2 | Does this host derive the designer from `knowledge.des`, or check a hand-written one against it? | **Hand-written and checked (default):** storage goes through the FBL bindings by the host's FBL runtime; the table itself is written by hand, and a guard fails when it differs from `knowledge.des`. Keeps the first designer small. · **Derived:** a DESL runtime in this host builds the designer from `knowledge.des`, as DISL-derived diagrams are built. The right end state, and a much larger first step. · Other. | 10.5 |
| Q3 | Does a cell have an id of its own? | **No, a cell is its row's id and its property's id (default):** nothing extra is stored, and the pair is already unique. · **Yes, every cell stores a ShortGuid:** the request lists cells among the things with an id; it costs one id per value in the file and nothing yet refers to it. · Other. | 2.6 |
| Q4 | How does a row name its values in the file: by property id or by property name? | **By property id (default):** renaming a property changes one place, and a name can never collide; a reader resolves ids through the property list in the same file. · **By property name:** a row reads on its own, which suits a person or a language model reading the raw file; renaming a property rewrites every row. · Other. | 2.6, 2.8, 3.3 |
| Q5 | Where is a two-way relation stored? | **On one side only (default):** the property that was created holds the values; its counterpart in the other file is declared and shows the reverse, computed. Two files can never disagree. Costs: reading the counterpart needs the other file open. · **On both sides:** each file is complete when read alone, as Notion shows it and as suits a language model reading one file; ADP keeps the two in step, writes both in one undoable step, and reports a finding when an outside edit made them disagree. · Other. | 5.4, 5.5 |
| Q6 | Is collapsing a group or a parent stored? | **Yes (default):** the request stores every view-related aspect in the data file, so a collapse is an edit: undoable, saved and visible in a diff. · **No:** collapse is forgotten when the file is closed, and viewing never changes the file. · Other. | 6.9 |
| Q7 | Does a tool with relations between files still count as a designer? | **Yes, and the terminology says why (default):** a diagram *draws* connections between elements; here a relation is a value in a cell, and nothing is drawn connected. One sentence is added to `docs/terminology.md`. · **Yes, without a change.** · **No:** it needs a different kind, which reopens the catalogue row. · Other. | 1.5 |

## Alignment with Product Vision

`product.md` describes specialized, task-tuned tools over plain text files the user owns. This designer brings the project's goals of clarity in textual data and of humans and agents working together to tabular knowledge: the file is plain text, diffable and usable without ADP, by a person or by a language model, and ADP is one way of working in it rather than its owner. It follows `structure.md`'s rule that a tool type is a module on the shared core: what a designer needs from the core is added once, for any later designer (Requirement 10), and is named for what it does.

## Requirements

### Requirement 1 - The definition files come first

**User Story:** As a tool engineer, I want the Knowledge designer defined once, correctly, where every host reads it, so that the same tool can be built in the other IDEs from the same files.

#### Acceptance Criteria

1. WHEN the work starts THEN its first deliverables SHALL be, in etalii.adp: one FBL binding per format (YAML, JSON, XML) for the knowledge file, and `definitions/designers/knowledge.des` with `definitions/designers/knowledge.md` beside it, the first holding everything about the designer type DESL can express and the second everything it cannot (Q1). No implementation task in this repository SHALL start before the definition it implements is merged there.
2. WHEN `knowledge.des` is written THEN DESL SHALL first gain, in its specification and its schema, the constructs that file needs, each named for what it does and not for this designer.
3. WHEN the bindings are written THEN each SHALL have fixtures in etalii.adp that prove reading and every kind of edit this document names, and one fixture SHALL prove that the same table read from each of the three formats gives the same model.
4. WHEN FBL or DESL cannot express something this designer needs THEN the gap SHALL be reported to the user as a selection (extend the language, and how; or change the designer; or other) before anything is added to a language. The gaps known today are listed under *Whether the languages suffice*.
5. WHEN the designer is defined THEN `docs/terminology.md` in etalii.adp SHALL say why a tool whose cells hold relations is a designer and not a diagram (Q7), and SHALL name the Knowledge designer as the example of a designer.
6. WHEN anything in this requirement is delivered THEN it SHALL be a pull request into etalii.adp's `develop`.
7. WHEN the definition files and this document disagree THEN the definition files SHALL win and this document SHALL be amended, since the other hosts read the definition and not this document.
8. WHEN the definition files are complete THEN nothing a second host needs to build the same tool SHALL exist only in this repository's code or in this document.

### Requirement 2 - Two files, and everything in the data file

**User Story:** As an author, I want one table in one plain text file that I own, so that I can read it, diff it, version it and hand it to a language model without ADP.

#### Acceptance Criteria

1. WHEN a knowledge document exists THEN it SHALL be exactly two files: the knowledge file, and its `.adp` registration beside it.
2. WHEN the registration is written THEN it SHALL hold only what identifies the document: the origin `etalii/knowledge` and which file is its knowledge file. It SHALL hold no property, row, view, width, position or other setting, and the designer SHALL work the same after the registration is deleted and created again.
3. WHEN a knowledge file is stored THEN it SHALL hold everything else: each property's id, name, type, options and relation target; each row and its values; and each view with every setting it has, including which properties are visible, their order and width, every sort, every filter and the grouping. No setting of the designer SHALL be kept in the browser, in the host's own storage or in any other file.
4. WHEN a knowledge file is added THEN the author SHALL choose YAML, JSON or XML at that moment, and the three SHALL be equivalent: the same table written in each SHALL show the same properties, views, rows and findings.
5. WHEN a knowledge file exists THEN ADP SHALL NOT offer to change its format.
6. WHEN a property, a row, a view or an option is created THEN it SHALL receive a ShortGuid as its id, stored in the knowledge file, never reused and never changed by renaming, reordering or sorting. Everything in the file that refers to one of these SHALL refer to it by that id (Q3 for cells, Q4 for a row's values).
7. WHEN an edit is made THEN only the bytes that edit concerns SHALL change: comments, key order, indentation, line endings and everything the edit did not touch SHALL stay byte for byte as they were. The same edits to the same file SHALL write the same bytes on every machine, apart from the ids generated.
8. WHEN a knowledge file is read without ADP THEN a reader SHALL be able to tell every property's name and type, every option of a selection and every row's values from that file alone.
9. WHEN a knowledge file is read THEN it SHALL declare the version of its schema, and a file of a newer version than the host knows SHALL open read-only with a finding rather than be rewritten.
10. WHEN a file holds keys or elements the designer does not know THEN they SHALL be kept on every write and reported as a finding of severity info.

### Requirement 3 - Properties

**User Story:** As an author, I want to add and change properties in a moment, so that the table's shape follows my understanding as it develops.

#### Acceptance Criteria

1. WHEN a property is added THEN it SHALL have one of these types: **text**, **number**, **checkbox**, **date**, **date and time**, **time**, **selection**, **multiple selection** or **relation**. (The request names text, numeric, date times, time, selection and multiple select, and ends in "etc."; checkbox and date are this document's reading of that, and relation comes from the request.)
2. WHEN the author adds a property THEN it SHALL take one click on the `+` after the last column, or *insert left* or *insert right* from a column's menu, then a name and a type, and the new property SHALL be visible in the current view at once.
3. WHEN the author works on a property THEN they SHALL be able to rename it, change its type, duplicate it, move it by dragging its header, and delete it, each from the column's header or its menu and each as one undoable step.
4. WHEN a property's type is changed THEN every value that has a meaning in the new type SHALL be converted, and every value that has none SHALL be kept in the file as it was and reported as a finding on its cell. No value SHALL be discarded by a type change.
5. WHEN a property is a selection or a multiple selection THEN its options SHALL each have an id, a name and a colour from a fixed palette, SHALL be creatable by typing a new name while filling in a cell, and SHALL be renamable, recolourable, reorderable by dragging and deletable.
6. WHEN an option is deleted while rows use it THEN the author SHALL be told how many rows use it before it is deleted, and those rows' values SHALL be cleared in the same undoable step.
7. WHEN a knowledge file exists THEN exactly one text property SHALL be its title property. It SHALL NOT be deletable, hidden or changed to another type.
8. WHEN a property is deleted THEN its values SHALL be removed from every row and it SHALL be removed from every view's settings, in one undoable step.
9. WHEN two properties would have the same name THEN the second SHALL be refused, with the reason shown where the name is typed.

### Requirement 4 - Rows and cells

**User Story:** As an author, I want to fill in rows as fast as I can type, with each cell edited in the way its type calls for.

#### Acceptance Criteria

1. WHEN the author adds a row THEN it SHALL take one click on the *new* row at the bottom of the table or of a group, and the row SHALL stay visible in the current view until that view is next opened, even when its filter would exclude it.
2. WHEN a row is added inside a group THEN it SHALL be created with the value that puts it in that group.
3. WHEN a cell is edited THEN its editor SHALL match its property's type: free text; a number; a checkbox; a date, a date and time or a time, typed or picked; one option or several, picked or typed to create; rows of the target file, searched by title.
4. WHEN a value is typed that its type cannot hold THEN it SHALL be refused where it is typed, with the reason, and the cell's stored value SHALL be unchanged.
5. WHEN the author uses the keyboard THEN they SHALL be able to move between cells, start and finish editing, and add a row without the mouse.
6. WHEN one or more rows are selected THEN they SHALL be deletable in one undoable step and movable by dragging where the view has no sort, and the properties of a single selected row SHALL be shown in the property grid.
7. WHEN any edit is made THEN undo and redo SHALL work as for every other tool in this host, and the file SHALL be saved as for every other tool.
8. WHEN a date, a date and time or a time is stored THEN it SHALL be written in ISO 8601, and a date and time SHALL carry its offset.

### Requirement 5 - Relations between knowledge files

**User Story:** As an author, I want a property whose values are rows of another table, so that several tables together model one body of knowledge.

#### Acceptance Criteria

1. WHEN a relation property is created THEN the author SHALL choose its target knowledge file in a dialog that shows the workspace's files and offers only knowledge files, with *this file* offered first.
2. WHEN the target is chosen THEN the author SHALL choose whether each cell holds one row or any number, and whether the relation is **one-way** or **two-way**.
3. WHEN a relation value is stored THEN it SHALL name the target row by its id, and the property SHALL name the target file by a path relative to the file that holds it.
4. WHEN a relation is two-way THEN it SHALL appear as a property in the target file too, under a name the author gives it when creating the relation, and each side SHALL show the rows that point at it from the other (Q5).
5. WHEN a two-way relation is edited from either side THEN both sides SHALL show the change at once, the change SHALL be one undoable step, and the two SHALL never be left disagreeing by an edit made in ADP.
6. WHEN a relation cell is shown THEN it SHALL show each related row's title, and each SHALL open its row in its own file.
7. WHEN a relation's target file is missing, unreadable or not a knowledge file, or a value names a row that is not there THEN the value SHALL be kept in the file, shown as unresolved, and reported as a finding. It SHALL NOT be removed by ADP.
8. WHEN a target file is renamed or moved inside ADP THEN every relation that names it in the open workspace SHALL be rewritten in the same step. WHEN a row is deleted while rows in other files relate to it THEN the author SHALL be told how many before it is deleted.
9. WHEN a relation's target is the file itself THEN the property SHALL be assignable as the file's **parent** relation, holding at most one row per cell, and a row SHALL NOT be made its own ancestor.
10. WHEN one side of a two-way relation is deleted THEN the author SHALL choose between deleting the other side too and keeping it as a one-way relation.

### Requirement 6 - Views

**User Story:** As an author, I want several named ways of looking at one table, kept with the table, so that the same knowledge answers different questions.

#### Acceptance Criteria

1. WHEN a knowledge file is stored THEN its views SHALL be stored in it, each with an id, and a file SHALL always have at least one view.
2. WHEN the author works on views THEN they SHALL be able to add, rename, duplicate, reorder by dragging and delete a view and switch between views, and the last remaining view SHALL NOT be deletable.
3. WHEN a view is set up THEN the author SHALL be able to choose which properties it shows and in what order, to set each column's width by dragging its border, and to choose per property whether long text wraps.
4. WHEN a view is sorted THEN it SHALL sort by one or more properties in an order set by dragging, each ascending or descending, in the way its type calls for: numbers as numbers, dates as dates, selections in their options' order.
5. WHEN a view is filtered THEN it SHALL combine conditions with *all* or *any*, in groups nested up to three levels, each condition offering the comparisons its property's type calls for, including *is empty* and *is not empty* for every type.
6. WHEN a view is grouped by a selection, a multiple selection, a checkbox or a relation THEN rows SHALL be shown under one collapsible heading per value showing the value and its row count, with a heading for rows that have none; a row with several values SHALL appear under each; and the author SHALL be able to hide a group, order the groups and hide empty groups.
7. WHEN a view is grouped by the parent relation (Requirement 5.9) THEN rows SHALL be shown nested under their parents to any depth, each parent with a toggle that collapses its children.
8. WHEN a view's settings are changed THEN no row and no property SHALL change, the change SHALL be an edit of the file like any other (undoable, saved, visible in a diff), and other views SHALL be unaffected.
9. WHEN a group or a parent is collapsed or expanded THEN that SHALL be stored in the view (Q6). Scroll position, the selection and which cell is being edited SHALL NOT be stored.
10. WHEN the file is opened THEN it SHALL open in the view it was last left in, and which view that is SHALL be stored in the knowledge file.
11. WHEN a view names a property that no longer exists, by an edit outside ADP THEN the view SHALL still open, without that setting, and report a finding.

### Requirement 7 - It looks and behaves as Notion's table does

**User Story:** As someone who knows Notion's databases, I want this designer to look and work the way I already know, so that I do not have to learn it.

#### Acceptance Criteria

1. WHEN a knowledge file is shown THEN its layout SHALL follow Notion's table view: the views as tabs above the table; beside them the controls for filter, sort, group and property visibility; a header row of property names, each with an icon for its type; rows beneath; a `+` after the last column; and a *new* row at the bottom of the table and of every group.
2. WHEN a column's header is clicked THEN it SHALL open a menu offering what Notion's offers for the types this designer has: rename, change type, filter by it, sort ascending or descending, group by it, hide in this view, wrap, insert left, insert right, duplicate and delete.
3. WHEN a view has filters or sorts THEN each SHALL be shown as a removable item in a bar between the tabs and the table, as Notion shows them, and clicking one SHALL open it for editing.
4. WHEN a selection or multiple selection value is shown THEN it SHALL be a rounded tag in its option's colour, and the palette SHALL be Notion's ten named colours with a variant of each for the light and the dark theme.
5. WHEN a cell is edited THEN the editor SHALL open in place over the cell, and for options and relations SHALL be a searchable list that also creates, as Notion's are.
6. WHEN a relation is created THEN the steps SHALL follow Notion's: choose the target, choose one row or no limit, optionally switch on showing it in the target and name that side, then confirm.
7. WHEN this designer and Notion differ THEN the difference SHALL be one this document names: properties, views and files are local and plain text; there are no pages inside rows, no people, no comments and no sharing; and the property types are those of Requirement 3.1.
8. WHEN the designer is drawn THEN it SHALL use this host's own theme colours, fonts and controls in both themes. It follows Notion's layout and behaviour, and SHALL NOT copy Notion's icons, fonts or artwork.

### Requirement 8 - Readable and writable by people and by language models

**User Story:** As someone engineering knowledge with a language model, I want the model and me to work in the same file, so that neither of us needs the other's tool.

#### Acceptance Criteria

1. WHEN a knowledge file is changed outside ADP, by a person, a script or a language model THEN an open designer SHALL show the change without being reopened, as every tool in this host does for its file.
2. WHEN a file changed outside ADP holds something the designer cannot accept (an unknown type, a value that does not fit its property, a duplicate id, a view naming a missing property) THEN the file SHALL still open, everything readable SHALL be shown, and each such thing SHALL be a finding that says where it is. Nothing unreadable SHALL be removed on the next write.
3. WHEN a file cannot be read at all THEN the designer SHALL say so with the format's own error and its position, and SHALL NOT write to the file.
4. WHEN the designer type is published THEN `knowledge.md` SHALL describe the file in each of the three formats completely enough to write a valid one by hand, with one complete example per format, and a JSON Schema SHALL be published for the JSON and YAML forms.
5. WHEN a row, a property, a view or an option is added without ADP and carries no id THEN the designer SHALL show it, report a finding of severity info, and give it an id on the first edit made in ADP.

### Requirement 9 - Examples and test data

**User Story:** As the user, I want the designer proven against data that looks like real knowledge work, not against toys.

#### Acceptance Criteria

1. WHEN the module ships THEN it SHALL ship examples in `src/examples/designers/knowledge` that between them show every property type, a one-way and a two-way relation between two files, a parent relation, and views using visibility, widths, sorting, filtering and each kind of grouping; and at least one example SHALL exist in all three formats.
2. WHEN example data is taken from `correlate/test/data` of egui-data-table or from anywhere else THEN it SHALL be vendored under the rule in `CLAUDE.md` (*Vendored example data*): the licence read from the data set's own statement at acquisition, share-alike refused, the licence file vendored beside the data, and the readme saying what the corpus does not demonstrate. A data set whose own licence cannot be established SHALL NOT be vendored; its **shape** MAY be reproduced with data written for ADP.
3. WHEN the examples are tested THEN a backend test SHALL open every one, assert it reads without findings of severity error, and assert that writing it unchanged leaves its bytes unchanged.
4. WHEN the three formats are tested THEN one test SHALL read the same table from each and compare the three models, and for each kind of edit this document names a test SHALL apply it to each format and compare the three again.
5. WHEN a fixture's bytes are what a test compares THEN the fixture SHALL be exempt from line-ending conversion by a rule in `.gitattributes` that does not also exempt ordinary YAML, JSON and XML files in this repository.
6. WHEN a view is tested at size THEN an example of at least ten thousand rows SHALL be used (Non-Functional Requirements, Performance).

### Requirement 10 - What this host gains for the designer family

**User Story:** As a maintainer, I want what the first designer needs from the core added once, so that the second designer is a module and nothing more.

#### Acceptance Criteria

1. WHEN the module is added THEN it SHALL live in `src/designers/knowledge` with the four folders its siblings use (`backend`, `api`, `client`, `examples`), SHALL be found by `DesignerDefinitionDiscovery` and by the shell's registry with no core file naming it, and deleting the folder SHALL remove the designer type.
2. WHEN `DesignerDefinition` is widened, and when the host gains the catalog and session seams it serves designers through THEN each SHALL be shaped as the diagram and editor families' are, SHALL live in core, and SHALL name no designer type.
3. WHEN the client gains a table (rows, typed cells, headers, grouping, a view bar) THEN it SHALL be a shared component beside the canvas library, named for what it shows, which the module declares against. The module's client SHALL hold declarations and event handlers and no rendering of its own.
4. WHEN a knowledge file is added from the workspace tree THEN it SHALL be offered where every other tool type is, with the choice of format (Requirement 2.4), and SHALL be created as the two files of Requirement 2.1 with one title property, one view and no rows.
5. WHEN the knowledge file is read or written THEN it SHALL go through the FBL bindings of Requirement 1 by the host's FBL runtime, with no parser or writer of its own in the module; and a guard in this repository SHALL read `knowledge.des` and fail when the module's property types or view settings differ from it (Q2).
6. WHEN the host gains a dialog for choosing a workspace file (Requirement 5.1) THEN it SHALL be a core capability any tool can ask for, with a filter the asking tool supplies.
7. WHEN the module pushes changes to an open designer THEN they SHALL ride the connection's existing `Watch` stream as a member of its message, never a second stream.
8. WHEN the work completes THEN `docs/creating-a-designer-module.md` SHALL describe a designer module from end to end with this one as its worked example, `docs/architecture.md` and `docs/solution-structure.md` SHALL be true of the result, and the `etalii/knowledge` row of `docs/tools.md` SHALL move with the designer's state.

### Requirement 11 - Seen working, and covered

**User Story:** As the user, I want the designer seen working where it is seen, and every criterion here claimed.

#### Acceptance Criteria

1. WHEN the work completes THEN a `tests.md` entry SHALL cover, in a real browser and in both themes: adding a knowledge file in each format and finding exactly two files; adding a property of each type and filling in a cell of each; changing a property's type with values that do and do not convert; resizing, hiding and reordering columns and finding each in the saved file; a two-way relation created through the file dialog and edited from both sides; a parent relation shown nested; a view with two sorts, a nested filter and a grouping; a second view left unchanged by the first; an edit made outside ADP appearing in the open designer; undo and redo across each of these; and the saved file's diff showing only what was edited.
2. WHEN the browser pass is made THEN each part of Requirement 7 SHALL be checked side by side against Notion's own help pages, and every difference found SHALL be either corrected or added to Requirement 7.7 by a ruling.
3. THEN jsdom SHALL NOT be taken as evidence of any of these, since it applies no CSS and lays out no text.
4. WHEN a guard is written for any criterion THEN it SHALL be seen to fail against a planted defect before it is trusted, and the task SHALL name that defect.
5. WHEN the tasks document is written THEN every acceptance criterion here SHALL be claimed by a task, established by diffing the two sets before the tasks card is raised and again after implementing.

## Non-Functional Requirements

### Code Architecture and Modularity

- The module depends on core and never the reverse; the table component, the designer seams, the file dialog and any language construct are named for what they do (Requirements 1.2, 10.2, 10.3, 10.6).
- The backend is the only reader and writer of knowledge files; the client speaks gRPC, as for every tool.

### Performance

- A view of ten thousand rows SHALL scroll without a perceptible pause and SHALL draw only the rows in sight.
- An edit to one cell of a ten-thousand-row file SHALL be shown at once and SHALL write a splice, not the whole file.
- Sorting, filtering and grouping ten thousand rows SHALL take effect without a perceptible pause.

### Reliability

- The designer never discards what it cannot read (Requirements 2.10, 3.4, 5.7, 8.2), and never writes to a file it could not read at all (Requirement 8.3).
- Opening a file, scrolling and selecting SHALL leave the file's bytes unchanged.

### Usability

- A value that cannot be accepted is refused where it is typed, with its reason (Requirements 3.9, 4.4).
- Every gesture has a keyboard path (Requirement 4.5), and every control exposes its role and state to assistive technology.

## Out of scope

Each is a later specification if it is wanted, and nothing here should be read as deciding it:

- Notion's other property types: status, person, files, URL, email, phone, formula, rollup, created and edited time and by, id, button and place.
- Views that are not a table: board, calendar, gallery, list, timeline, chart. Sub-groups, calculations under a column and frozen columns.
- Pages inside rows, comments, sharing and permissions.
- Changing a knowledge file's format (Requirement 2.5), and importing or exporting CSV, XLSX or a Notion database.
- Several tables in one file.
- The other hosts (IntelliJ, VS Code, Eclipse, Notion). They build the same tool from the same definition files (Requirement 1.8) under their own specifications.
- A DESL runtime in this host, unless Q2 is ruled that way.

## Sources

- The user's request in the project chat, 2026-10-08.
- This repository at `develop` `61f802e0`: `docs/tools.md`, `docs/creating-a-designer-module.md`, `docs/architecture.md`, `src/designers/readme.md`, `src/backend/EtAlii.Adp/_Model/DesignerDefinition.cs`, `src/backend/EtAlii.Adp/ShortGuid.cs`, `src/backend/EtAlii.Adp.Specification.Fbl`.
- etalii.adp at `origin/develop` `f8451b4`: `docs/terminology.md`, `specifications/desl/DESL-specification.md`, `specifications/ded/DED-specification.md`, `specifications/fbl/FBL-specification.md`, `definitions/designers/README.md`.
- [vrenken/egui-data-table](https://github.com/vrenken/egui-data-table) at `master`: `correlate/src/data` (`column_type.rs`, `column_configuration.rs`, `relation.rs`, `data_source_configuration.rs`) and the listing of `correlate/test/data`.
- Notion's help pages on [database properties](https://www.notion.com/help/database-properties), [views, filters and sorts](https://www.notion.com/help/views-filters-and-sorts) and [relations and rollups](https://www.notion.com/help/relations-and-rollups), read 2026-10-08.

**What changed from the first version.** Ruled by the request and no longer questions: storage is a data file through FBL plus an identifying `.adp` (was Q1), and ids are ShortGuids (was Q3). Added: the definition files as the first deliverable and the report on whether the languages suffice (Requirement 1, *Whether the languages suffice*); the two-file rule and every setting in the data file, widths included (Requirement 2); ids for properties, views and options; no change of format after adding; the file dialog for a relation's target (Requirements 5.1, 10.6); the Notion requirement (Requirement 7); and four new questions (Q1, Q3, Q4, Q6).
