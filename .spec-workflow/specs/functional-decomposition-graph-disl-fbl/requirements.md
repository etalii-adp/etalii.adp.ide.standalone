# Requirements Document

## Introduction

The user's request, verbatim (2026-10-09):

> Create spec workflow MCP specifications. One specification for converting one of the standalone diagram tools to FBL and DISL. i.e. analyse the current implementation, define the DISL and FBL and then write the specification on the refactoring., also focus on finding issues and aspects where the DISL and FBL languages do not yet cover everything.

This is one of the fifteen specifications that reading gives (the series and the survey behind it are in `causal-loop-disl-fbl`, *Introduction*). It converts the **functional decomposition graph** (`etalii/functional-decomposition-graph`, `.fdg`), a tool on neither language.

**What "converted" means here** is what it means for the hype cycle graph today: the toolbox, context menus, property rows, findings and refusals are derived from a bundled DISL definition by `EtAlii.Adp.Specification.Disl`, the body is read and written through an FBL binding by `EtAlii.Adp.Specification.Fbl`, and the module keeps code only for what neither language can state. Nothing a user sees or a file holds changes unless this document names the change and the user has ruled on it.

**Why this tool is a useful test.** Its body is ADP's own YAML, the same family as the hype cycle's `.ghg`, so the hype cycle's binding answers most of the reading. What is new is that an entry's type is a value (`type: ui-element`) and not the list it sits in, that the four ownership relations carry cardinality and a shared cycle rule, and that positions and sizes are in the body and not in the registration. Each of the three finds something.

## What was measured

Read on 2026-10-09: the module at `develop` `9a646009`; `definitions/diagrams/functional-decomposition-graph.dis` and `.md`, `specifications/disl/functional-decomposition.dis`, the DISL specification (0.3 draft) and the FBL specification (0.2 draft) in etalii-adp/etalii.adp at `develop` `da64ff0`; the two libraries in this repository; and the hype cycle graph module as the pattern.

| Thing | State |
| --- | --- |
| The module | 90 files. The backend is 3,207 lines of C# in 44 files: `FdgParser` (255 lines) reads with YamlDotNet, `FdgWriter` (327) writes through the shared `LineSplice`; `FdgRuleSet` (210), `FdgRelations` (106), `FdgOwnership` (169), `FdgConnectVerdict` (73), `FdgContextActionProvider` (270), `FdgContextPropertyProvider` (157) and `FdgToolboxProvider` (29) hold the findings, the rules, the refusals, the menus, the rows and the palette as code. 11 test files, 15 fixtures. |
| The module's client | 536 lines outside tests. `FdgCanvas.tsx` holds a hand-written canvas definition; `fdgIds.ts` holds the wire ids. |
| The DISL definition | Exists in etalii.adp, 572 lines, **written against DISL 0.1**. It is not bundled here (`src/diagrams/functional-decomposition-graph/definition/` does not exist). Its `persistence.format` is a plugin. |
| A second DISL description | `specifications/disl/functional-decomposition.dis`, 341 lines, DISL 0.2, language id `org.example.functional-decomposition`: the specification's own worked example of this tool. It uses the constructs the 0.1 definition works around, and it is an example, not the tool (finding A3). |
| The FBL binding | None exists. One is drafted in the appendix, `yaml` family, declared reader. The format is not one FBL 11.5 expects to stay a plugin. |
| The real-file check | None. `RealFiles/divergences.json` and `ModuleCrossCheck.Tests.cs` do not mention this tool. All 16 `.fdg` files of the repository are inside the module (one example, 15 fixtures). |
| A parity transcript | None. |

**How each finding below is known** is marked: *read* is a reading of the code or the definition against the specification text, not run; *recorded* is something a test, a fixture or a comment in the code already holds; *measured* is a search whose command is in *Sources*. Nothing was built or run. Requirement 1 turns every *read* finding into a fixture before anything is changed.

## Findings

The three kinds are those of `causal-loop-disl-fbl`: a **definition defect** is repaired in the definition or the binding, a **runtime gap** in this repository's libraries, a **language gap** in etalii-adp/etalii.adp. A fourth appears here more than once: a **module defect**, something the hand-written code does wrong that the conversion would repair by accident and must repair on purpose.

### A. The DISL definition against the module

| # | Finding | Kind | Known |
| --- | --- | --- | --- |
| A1 | The definition is DISL 0.1. The client's `parseDisl` refuses anything but 0.3, and every bundled definition here is 0.3. It must be rewritten at 0.3 before it can be bundled. | Definition defect | Read |
| A2 | Its companion lists six "DISL gaps this diagram exposed". Five are answered since: a palette entry that is dropped (`mode: "drop"`, DISL 7.2), the superellipse (a built-in shape with `exponent`, 6.7), a deletion confirmation asked only above a count (`Confirmation` with `count` and `threshold`, 9.5), an attribute that is constant and never stored (`fixed`, 4.3), and `acyclic` on an abstract relation type (0.2, change 12). The definition still works around each, and around six more: the right-button drag from an element's body (`pointer`, 0.3), the tool's refusal sentences (`refusal` on a built-in, 8.1), its finding codes (`code`), one finding for a group of duplicates (`oncePerGroup`), the order of findings (`constraints.order`) and base-36 ids (`encoding`). The sixth listed gap stands (A9). | Definition defect | Read |
| A3 | Two DISL descriptions of this tool exist and disagree. The 0.2 example in `specifications/disl/` draws `Shows` dashed, adds a read-only Height row, a computed row "Owns, directly or through others" and a `findings` form item; the tool has none of these (`fdg.css` has no dash; `FdgContextPropertyProvider` has no such rows). Its built-in settings and its deletion confirmation are the tool's sentences and are the source for the rewrite. | Definition defect | Read; the dash is measured |
| A4 | The toolbox declares a "Links" group of five drag tools. The tool's palette has five entries and no link tool: a relation is drawn by a right-button drag and its type follows from the two ends. The element tools are `mode: "click"`; the tool drops. | Definition defect | Read |
| A5 | Two menus are missing. A placement offers "Add ui element here", "Add data element here", "Add action here", "Add function here", "Add comment here", in the order of `FdgElementTypes.All`, which is not the palette's order. A finished connect gesture offers all five relations whatever the two ends are, labelled "ui child", "owns action", "owns data", "owns function", "shows". The definition labels the relations "UI child", "Action", "Data", "Function", "Shows". | Definition defect | Read |
| A6 | The rows Width (every element) and Height (a Comment), in a group "Size", are not in the definition's forms. They are the route a canvas resize takes. The row shows two decimals (`0.##`) where the file holds four (`0.####`). A value that is not a positive number is refused with "'x' is not a size this graph can draw."; a value below the minimum is clamped to it and not refused. | Definition defect; the clamp is to confirm in design (C9) | Read |
| A7 | `persistence` names a plugin. With the binding it becomes `format: "fbl"` with `binding` and a `typeMap`, and the keys DISL 11.2 forbids beside a binding (`encoding`, `newline`, `files`, `ordering`, `omitDefaults`, `precision`, `definition`) go. `view.store` says `bounds`: wrong, because a position and a size are attributes in the body and the registration holds an origin line and `body:` only. | Definition defect | Read |
| A8 | The definition gives no wire ids. The module's 14 action ids, 6 property ids and 5 toolbox ids are what the client and the backend share; an element's wire id is its stored id, with no prefix. | Language gap, already ruled: wire ids stay an `x-` block (Peter, 2026-10-06). This definition gets an `x-fdg` block. | Read |
| A9 | **Which relation a body drag draws.** Every relation of this tool is told apart by its target type, so the pair of ends admits exactly one, and the canvas library picks it (`relationOnto`). In DISL each of the five relation types would declare the same `pointer`, and the text does not say which type a gesture creates when several claim it. | Language gap | Read |
| A10 | **The standalone DISL runtime has no cardinality rule and no cycle rule.** A search of `EtAlii.Adp.Specification.Disl` for `std.multiplicity` and for `std.acyclic` finds nothing, and `DislCelLibrary` declares twelve methods, none of them `reachable`, `inCycle` or `cycles`. Three of the eight finding codes and two of the three connect refusals depend on them. | Runtime gap | Measured |
| A11 | **The module's link checks are a chain; DISL's built-ins are independent.** A connection is reported once: as dangling, else as a self link, else as an unknown relation, else as an inadmissible pair. (The class's own remark that one connection may break several is true only across the chain and the cardinality check.) A dangling connection names its first missing end only. Cardinality is counted on the text of `from` and `to`, so two connections to an id no element has are reported as a second parent. Under DISL each built-in reports what it sees, and an unset end counts for nothing. | Behaviour change to rule on (Q4) | Read |
| A12 | **An ownership loop is one finding, rotated to start at its smallest id**, on that member's line: "Ownership loops: a -> b -> a." The walk visits each element once, so it does not enumerate every elementary cycle (the code says so at `WouldClose`). `diagram.cycles` starts each cycle at its first member in model order and returns all of them. The 0.1 definition reports once per element in the loop. | Definition defect; the rotation is to settle in design | Read |
| A13 | **The order of findings.** The module lists duplicate ids, then what the parser passed over (elements, then connections, then the header, although the header is on line 1), then sizes, then links, then cardinality by relation, then loops. `constraints.order` orders by code and then by line. Within `fdg.unreadable-entry` the two differ. | Behaviour change to rule on (Q4), or a language gap (C8) | Read |
| A14 | The definition's `uniqueName` function calls `lists.range`, which DISL does not define (a search of the specification finds no such name), and counts names of named elements only. The module counts the `name` of every element, a Comment's stray one and an unknown type's included. | Definition defect | Measured; the second half read |
| A15 | Two sentences format a number with the machine's culture (the width in "is 12 wide; the minimum is 80." is interpolated without one), so a width of 12.5 reads "12,5" on a machine whose decimal separator is a comma. | Module defect | Read |
| A16 | Descriptions are never sent to the canvas, a resize from a left or top edge is two edits, and the viewport filter decides what is sent. These are the host's and stay code. This is not a gap. | None | Recorded (the `.proto`, the client readme) |

### B. The FBL binding against the module's reader and writer

| # | Finding | Kind | Known |
| --- | --- | --- | --- |
| B1 | **A value is the text it was written with.** A name written `1.0`, `true` or `null` is that text to this tool (YamlDotNet's representation model types nothing). FBL's YAML reading types a plain scalar by the core schema and gives a slot no way to ask for the scalar as written. The hype cycle turns the typed value back into text in module code (`GhgParser.Text`), which loses the spelling: `1.0` comes back `1`. | Language gap | Recorded for the timeline (`TimelineDisl.cs`); read here |
| B2 | **A number is read from any scalar, by .NET.** `x: "120"` in quotes reads 120, `x: 1e2` reads 100, and `x: 0x10` is "not a number; zero was used." where the YAML core schema reads 16. A value that will not read keeps the element at zero and is reported on the value's line. FBL 7.4 makes a value that does not convert an unreadable entry. | Definition defect, or a language gap (C10), to settle by fixture | Read |
| B3 | **The type of an entry is a value.** FBL fixes a rule's `type`, and `typeMap` maps one binding type to one metamodel type, so the binding needs ten rules told apart by `when`, and catch-all rules behind them. Two things follow. A new entry must write `type:` as its second key, and no slot holds a constant: `skeleton` writes after the keys. And an entry whose type is unknown stays in the model today (its id counts, a connection to it is `fdg.forbidden-link`, a connection of an unknown relation is kept and reported as `fdg.forbidden-link`); mapped `unreadable` it leaves the model, and those findings change code and severity. | Language gap (C2), and a behaviour to keep (Requirement 4.4) | Read; `malformed-entries.fdg` holds the unknown element type |
| B4 | **A connection whose end names no element** is kept, not drawn, and reported as `fdg.dangling-reference`. FBL 5.4 creates no relation. The hype cycle's pattern answers it: the entry is read as an element with reference attributes and `typeMap` makes it a relation with an unset end. | Definition defect, with a known pattern | Recorded (`rule-dangling-reference.fdg`) |
| B5 | **An id written twice** is read as written, reported once on the last holder's line with the count, across elements and connections together; an edit goes to the first holder and the canvas draws the first. FBL gives a stored id to one holder. The hype cycle's `storedId` host attribute and `oncePerGroup: "last"` answer it. | Definition defect, with a known pattern | Recorded (`rule-duplicate-id.fdg`) |
| B6 | **The reader's own sentences**: a key it does not read (``"`colour` is not a key this module reads on a element; the line is kept."``, on the key's line), a `name` on a Comment, an entry that is not a mapping, and the three header sentences. `typeMap` keeps host attributes out of CEL and constraints, so the hype cycle words these in module code from an `unknownKeys` attribute. The same stand-in is needed here. No fixture holds an entry that is not a mapping. | Language gap, with a known stand-in | Read |
| B7 | **The header.** A missing header is reported as `fdg.unreadable-entry` on line 1, and only when the root is a mapping: an empty file reports nothing. A header present but not first is not reported. A version that is not 1 has its own sentence. FBL's `header` reports `fbl.header-mismatch` with one sentence. The hype cycle reads the header as an element of its own. | Definition defect, with a known pattern | Read |
| B8 | **A body that is not YAML opens editable.** The model is empty, the finding is `fdg.unreadable-entry` (a warning) and its sentence ends in YamlDotNet's own message. An add then splices into text the parser could not read, or is refused for a missing section. FBL 7.5 opens it empty and read-only with `std.unparseable`, worded by its own reader. | Module defect and a behaviour change to rule on (Q3) | Read; `not-yaml.fdg` holds the reading, nothing holds an edit |
| B9 | **A key written twice in one mapping.** YamlDotNet refuses the whole document, so it takes the path of B8. FBL 7.4 reports `fbl.duplicate-key`, a warning, and binds the first. | Behaviour change to rule on (Q4) | Read |
| B10 | **What a changed value writes.** `LineSplice.SetKey` replaces the whole line with the key and a value in the writer's form, which drops a trailing comment and the author's quotes. Setting a Height also rewrites the width line, and a move writes `x` and `y` both, changed or not. FBL replaces the value's span in its own style, and only the value that changed. | Behaviour change to rule on (Q1) | Read |
| B11 | **Where a new key goes.** A key the entry lacks is written directly after the entry's first line, so a first description lands between `id` and `type`; a Comment's text block lands at the end of the entry. FBL writes it where the binding's `keys` order puts it. | Behaviour change to rule on (Q1) | Read |
| B12 | **A name the writer does not quote.** `LineSplice.Quote` quotes for a colon, a hash, a leading dash, an outer space, a leading quote and a line break, and nothing else. A name `[draft]` or `{a}` is written plain and read back as a collection, so the name becomes empty; a name `*x` is written plain and read back as an alias to nothing, which makes the whole document unreadable. FBL's plain-safe rule quotes each. | Module defect, to fix on purpose with its own guard | Read |
| B13 | **A Comment's text.** The writer always rewrites it as a literal block scalar, a one-line text and a quoted one included; FBL keeps the replaced value's style when the new value fits it. A text whose first line begins with a space is written with no indentation indicator and reads back differently or not at all. FBL 6.3 says a literal is written with its lines one step deeper and does not mention the indicator. | Module defect, and a language gap (C3), to settle by fixture | Read |
| B14 | **An empty flow list.** The first add opens `elements: []` into a bare key. The writer recognises `[]` and `[ ]` only, so `elements: [] # none yet` gets an item under a key that still holds `[]`, and the file no longer parses. FBL 4.3 makes a flow collection writable only as a whole and its splice catalogue has no operation that opens one; this repository's `YamlFamily` opens it all the same. | Module defect; a gap in the language text, not in the runtime (C4) | Read; the library's case is measured |
| B15 | **Removing an element under a comment.** The module removes the entry's own lines. FBL removes the line span, which reaches up over comment lines directly above at the same indentation. In the example, `# The screens.` sits directly above the first element, and removing that element would remove the comment. | Behaviour change to rule on (Q2) | Read |
| B16 | **A missing section.** With no `connections:` key a connect is refused: ``"The document has no `connections:` section to add to."`` FBL either creates the container (`insert.create`) or rejects the edit in the planner's words. | Behaviour change to rule on (Q4) | Read |
| B17 | **The indentation of a new entry** is copied from the first entry of its list, the gap after the dash included. FBL copies the previous sibling, and does not say the gap is copied (the timeline's finding B7). They differ in a list whose entries are not indented alike. | To settle by fixture | Read |
| B18 | **An entry with no id** is read with an empty one and nothing reports it, although the model's own remark says the rules do. DISL's `std.missingId` would, and `ids.missing` defaults to assigning one. A `height` on a named element is read by nobody and reported by nobody; a `fixed` attribute's reader should warn when a stored value differs. | Behaviour change to rule on (Q4) | Read |
| B19 | **Numbers** are written `0.####`, invariant; `number` with four decimals rounds halves away from zero and drops trailing zeros. They are expected to agree; a value that rounds to zero from below is the case to try. An empty or blank description removes the key; FBL's `empty` is the empty string only. | To settle by fixture | Read |

### C. Language gaps, as they would be reported to etalii-adp/etalii.adp

1. **FBL: a slot that reads a YAML scalar as written** (B1). The timeline's first gap, now shown by a second tool, and worked around lossily by a third.
2. **FBL: a rule chosen by a value cannot write that value** (B3). Candidate: a `constant` map on `insert`, placed by `keys`; or a rule whose `type` is read from a key through a map, which would also make ten rules two.
3. **FBL: the indentation indicator of a block scalar** (B13), and whether a one-line value that becomes several lines changes style.
4. **FBL: opening an empty flow collection** (B14). The runtime does it; the splice catalogue does not name it.
5. **FBL: a removal that leaves the comment above** (B15), if the user rules that it stays.
6. **FBL: the wording of a body that cannot be read and of a planner's refusal** (B8, B16). A binding cannot word either.
7. **DISL: which relation a pointer gesture creates when several relation types claim it** (A9).
8. **DISL: an order of findings within one code that is not by line** (A13), if the user rules that today's order stays.
9. **DISL: a size below its minimum that is clamped on commit, not refused** (A6).
10. **FBL: a number written in quotes** (B2), if the fixture shows FBL refuses what the tool reads.

These are candidates. Requirement 9 says they are recorded with their evidence and reported, and never worked around silently.

## Open questions

Each is put to the user as a selection. The option marked **(default)** is what this document is written against until the user rules otherwise.

| # | Question | Options | Criteria affected |
| --- | --- | --- | --- |
| Q1 | When an edit changes one value or adds one key, what is written? | **(default) What FBL writes**: the value's own span in its own style, a new key where the binding orders it, and no line that did not change; a trailing comment and the author's quotes survive, and the transcript's edit hunks are regenerated once and reviewed as a diff. · **What the writer writes today**: the transcript stays byte-identical, and FBL needs a way to state a whole-line rewrite and a key's place after the first line, which is a language change first. · Other. | 5.2, 5.3, 1.4 |
| Q2 | Removing an element that has a comment line directly above it (finding B15). | **(default) The comment stays, as today**: the gap is reported, and until FBL answers it the removal is narrowed by a stand-in the companion document names. · **The comment goes with the element**, as FBL's line span says: the transcript's hunk is regenerated and named. · Other. | 5.5 |
| Q3 | A body that is not YAML (finding B8). | **(default) It opens empty and read-only**, as FBL 7.5 says, and keeps today's code (`fdg.unreadable-entry`), severity and sentence up to the reader's own message, which becomes FBL's. Editing text the reader could not read is treated as a defect. · **It stays editable**, as today. · **It becomes `std.unparseable`, an error**, as FBL reports it. · Other. | 4.6 |
| Q4 | Findings and refusals the languages would add or change (two findings on one connection, a missing id, a stray height, a key written twice, the header's code, a created section, the order inside one code). | **(default) Switched off or held to today's in this conversion**, so the findings before and after are the same list in the same order, and each is raised afterwards as a small change of its own. · **Adopted now**, each named in the transcript's diff. · Other. | 3.3, 3.4, 4.5, 5.6 |
| Q5 | Finding B1 has no answer in FBL 0.2. What happens meanwhile? | **(default) Whatever is ruled for Q3 of `timeline-disl-fbl`**, so that two tools with one gap get one answer. · **Work around it as the hype cycle does**: the typed value is turned back into text, and a name written `1.0` reads `1`. · **Narrow the format**: a name that reads as a number, a boolean or null must be quoted, and the example is checked for it. · Other. | 4.2 |
| Q6 | The module defects B12, B13 and B14, and A15. | **(default) Repaired in this conversion, each with a test seen to fail against today's code first**, because FBL's writing repairs three of them whether asked or not. · **Left as they are**, which for B12 to B14 needs module code that restates the defect. · Other. | 5.8, 3.2 |
| Q7 | Is the client's canvas definition in scope? | **(default) Yes**: compiled from the bundled definition with `compileNotation`, with today's hand-written definition kept in a test as the oracle. · **No**: backend only. · Other. | 7 |

## Alignment with Product Vision

The product direction (2026-09-26) is that every tool is a markup definition interpreted by a core in each IDE, with code only where a definition cannot reach. This tool is ADP's own notation and the one the DISL specification uses as a worked example, so it is the tool for which a definition that does not match the code is least excusable. It follows `product.md`'s *Don't reinvent, integrate* and `structure.md`'s rule that a diagram type adds declarations and no core code: what the libraries lack (finding A10) is added to the libraries for every tool.

## Requirements

### Requirement 1: An oracle before any change

**User Story:** As the owner of the tool, I want what it answers today frozen before anything is converted, so that every later step is held to it and not to somebody's memory of it.

#### Acceptance Criteria

1. WHEN the work starts THEN a parity transcript `Parity/functional-decomposition-graph.transcript.json` SHALL be generated from today's hand-written code and committed before any provider, parser or writer is changed, in the shape of the hype cycle's: per document, the context menu of every element and connection, of a placement, of a connect gesture and of an unknown id; the property rows; the findings; the payload of every mapped element; and a scripted edit sequence with each step's answer and the hunks it made to the text.
2. The corpus SHALL be the module's example, its 15 fixtures, and **one new fixture for each finding of tables A and B marked Read that a document can show** (A11 to A15, B1 to B3, B6 to B19), written so that the finding either shows in the transcript or is struck from this document with the reason.
3. A test SHALL require today's code to reproduce the checked-in transcript byte for byte, and SHALL have been seen to fail against a deliberately changed sentence before it is trusted.
4. WHEN a later step changes the transcript THEN the change SHALL be one this document names (Q1 to Q4, Q6), regenerated on purpose, and the diff of the checked-in file SHALL be the review of it. Any other difference is a regression.

### Requirement 2: The definition, at DISL 0.3, bundled

**User Story:** As a tool engineer, I want one definition of the functional decomposition graph that every host runs, so that the standalone tool is the definition's and not a second opinion.

#### Acceptance Criteria

1. `definitions/diagrams/functional-decomposition-graph.dis` in etalii-adp/etalii.adp SHALL be rewritten at DISL 0.3, using the constructs finding A2 names in place of each workaround, and SHALL validate against the DISL schema in that repository's checks.
2. It SHALL state what the tool does where findings A3 to A6 and A14 say the present text does not: no link tools, dropped element tools, the placement menu and the connect entries with today's labels and order, the Width and Height rows, and the name of a new element.
3. It SHALL declare `persistence.format` `fbl` with `binding` naming the module's binding (Requirement 4.1), a `typeMap`, `view.store` empty, and an `x-fdg` block mapping its tools, actions and properties to the wire ids the module uses today (findings A7, A8).
4. Its companion `functional-decomposition-graph.md` SHALL be rewritten to say what DISL 0.3 cannot express, which is the DISL items of section C and whatever Requirement 9 adds, and nothing that 0.3 can. The example `specifications/disl/functional-decomposition.dis` SHALL be brought in line with the tool or its differences stated as the example's own (finding A3).
5. The definition SHALL be bundled into `src/diagrams/functional-decomposition-graph/definition/` by `src/diagrams/tools/bundle-disl.sh`, never edited here, embedded in the backend project, and held by a `BundledDefinition.Tests` like the other bundled modules'.
6. WHEN the bundled definition is loaded by `EtAlii.Adp.Specification.Disl` THEN it SHALL load with no diagnostic of severity error.

### Requirement 3: Toolbox, menus, rows, findings and refusals are derived

**User Story:** As a maintainer, I want the palette, the menus, the rows, the findings and the refusals to come from the definition, so that changing the tool is changing one file.

#### Acceptance Criteria

1. The toolbox, the context menu of every target, the property rows, the deletion confirmation and every refusal of a connect SHALL be derived from the bundled definition through `ToolboxDerivation`, `ContextMenuDerivation`, `FormDerivation` and the constraint evaluation, mapped to the wire ids of `x-fdg`, and the providers SHALL hand them out and compute none themselves.
2. Every sentence a user reads today SHALL be read unchanged: "Rename…" and "Edit text…", "Remove" and "Remove connection", the confirmation that names 1 connection or several and is not asked for none, the three refusals that begin "Refused by the type check", "Refused by the cardinality check" and "Refused by the cycle check", "That id is already used in this graph." and "That is no longer in this graph.". The two sentences of finding A15 SHALL format their number the same on every machine (Q6).
3. The eight findings SHALL be derived with the tool's codes (`fdg.forbidden-link`, `fdg.self-link`, `fdg.second-parent`, `fdg.second-shows`, `fdg.ownership-cycle`, `fdg.dangling-reference`, `fdg.duplicate-id`, `fdg.unreadable-entry`), sentences and line locations, `fdg.unreadable-entry` a warning and the others errors, in today's order (finding A13, Q4).
4. No finding the tool does not give today SHALL appear, and a connection SHALL be reported by the first link check it fails and no later one (finding A11, Q4).
5. An ownership loop SHALL be one finding with today's sentence and member order, and a navigation loop that closes only through `Shows` SHALL give none, as the example's does (finding A12).
6. A connect SHALL be checked on the backend against the document as it is, in the order type, cardinality, cycle, whatever the canvas that sent it believed.
7. WHEN the derived answers are compared with the transcript of Requirement 1 THEN the menus, rows, findings and payloads SHALL be byte-identical.
8. The hand-written providers' and rules' logic SHALL be kept in the test project as the oracle (`Parity/HandWrittenFdg.cs`), as the hype cycle keeps `HandWrittenGhg`, and removed from the product code.

### Requirement 4: The body is read through the binding

**User Story:** As a user, I want every `.fdg` that opens today to open the same, read by the binding and not by a parser only this host has.

#### Acceptance Criteria

1. The module SHALL own its binding, `backend/EtAlii.Adp.Diagram.FunctionalDecompositionGraph/functional-decomposition-graph.fbl`, embedded in the backend project, as the hype cycle owns its own.
2. Every name, description, text, id and end SHALL be read as the text it was written with (finding B1), by what Q5 rules.
3. A position and a size SHALL be read as today from every scalar the tool reads a number from, and a value that does not read SHALL keep its element at zero and be reported with today's sentence on its line (finding B2).
4. An entry whose type the notation does not have, and a connection whose relation it does not have, SHALL stay what they are today: kept byte for byte, not drawn, counted where ids are counted, and reported with today's codes and sentences (finding B3).
5. A connection whose end names no element SHALL be kept, not drawn and reported as `fdg.dangling-reference` (finding B4); an id written more than once SHALL be reported once on its last holder's line with its count, and found by its first holder (finding B5); and no finding of FBL's own SHALL reach the user where the tool gives none today (findings B7, B9, B18, Q4).
6. A body that is not YAML SHALL open as Q3 rules. Reading SHALL never throw on content.
7. The sentences of finding B6 SHALL be read unchanged. WHERE neither language can word them THEN they SHALL come from module code that reads what FBL reports, and that code SHALL be named in the companion document as standing in for a language gap.
8. The reading SHALL be checked against today's parser for every `.fdg` under `src/`, ids per type, in `ModuleCrossCheck.Tests`, with any difference recorded in `RealFiles/divergences.json` and never tuned away.

### Requirement 5: Every edit is written by splices

**User Story:** As a user editing a file I also edit by hand, I want an edit to change what I changed and nothing beside it.

#### Acceptance Criteria

1. Every command of the module SHALL write through `EtAlii.Adp.Specification.Fbl` as a model change planned into splices; no module code SHALL build a line of YAML, and the module SHALL no longer use `LineSplice`. This replaces criterion 2.4 of the `functional-decomposition-graph` specification, which names `LineSplice`.
2. WHEN a value changes THEN only its span SHALL change, in its own style (Q1): a trailing comment and the author's quotes SHALL survive, a Height SHALL not rewrite the width, and a move SHALL not rewrite a coordinate that did not change.
3. WHEN a key is added THEN it SHALL go where the binding's `keys` order puts it (Q1); WHEN an entry is added THEN `type` SHALL be its second key with the document's spelling (finding B3), and its indentation SHALL be settled by the fixture of finding B17.
4. The first element and the first connection SHALL be addable to a new document, and to one whose empty list carries a trailing comment (finding B14).
5. Removing an element SHALL remove the connections that name it, in one undo step, with the count known beforehand as today, and SHALL leave a comment above it as Q2 rules.
6. Every refusal SHALL keep today's sentence, the missing section's included (finding B16, Q4), and a refused edit SHALL leave nothing changed in memory or on disk.
7. A new element SHALL get a ShortGuid and a redo SHALL keep it; a new named element SHALL get today's name, numbered from 2 when taken; a new Comment SHALL read "New comment"; a width below 80 and a Comment's height below 48 SHALL be written as the minimum.
8. A name or a text SHALL read back as it was typed, whatever characters it holds, with a test for each case of findings B12 and B13 seen to fail against today's writer first (Q6).
9. Undo SHALL restore the body byte for byte, through the host's shared restore command.
10. A document ADP did not change SHALL be written back byte-identical, for every fixture of the round-trip corpus, the pair of line-ending fixtures and the one without a final newline included.

### Requirement 6: What stays code, and why

**User Story:** As a maintainer, I want the code that remains in the module to be exactly what the languages cannot state, each piece with its reason.

#### Acceptance Criteria

1. The module SHALL keep the element mapper and the viewport filter, the session, the document store and its reloader, the `.proto` payloads, and the stand-ins of Requirements 4.7 and 5.5.
2. `FdgParser`, `FdgWriter`, `FdgRuleSet`, `FdgRelations`, `FdgOwnership`, `FdgConnectVerdict` and the logic of the three providers SHALL leave the product code. What Requirement 3.8 keeps as an oracle lives in the test project.
3. WHEN the conversion is complete THEN the companion document SHALL list every remaining module class beside the gap or the host concern that keeps it, and a class with neither SHALL be removed.

### Requirement 7: The client

**User Story:** As a user, I want the canvas to look and behave as it does, drawn from the same definition the backend runs.

#### Acceptance Criteria

1. The client's canvas definition SHALL be compiled from the bundled definition with `parseDisl` and `compileNotation` (Q7), with what the library cannot read from it stated in a `NotationBindings`.
2. Today's hand-written `FDG_DEFINITION` SHALL be kept in a test as the oracle, and the compiled one SHALL allow the same pairs, the same limits and the same cycle rule, and draw the same markup for the example.
3. IF `compileNotation` cannot read something the definition states (the superellipse, the diode, the two slanted outlines, the right-button body drag, the relation chosen by the pair of ends) THEN it SHALL be added to the shared library for every module, named for its geometry or its gesture, and SHALL NOT be written in this module.
4. A `tests.md` entry SHALL cover in a real browser, in both themes: the five shapes and their fills, a drop of each palette entry, a right-button drag for each of the five relations and a refused one of each of the three checks, a Shows from an Action to the page that owns it, inline rename of a name and of a Comment's text, a resize of a named element and of a Comment from each edge, and a removal with and without connections.
5. The client readme's section "What is not settled yet", which the companion document records as stale, SHALL be corrected.

### Requirement 8: The library work this tool needs

**User Story:** As a maintainer of the shared libraries, I want each thing this tool needs added once, for every tool.

#### Acceptance Criteria

1. `EtAlii.Adp.Specification.Disl` SHALL implement `std.multiplicity` and `std.acyclic` as DISL 8.7 defines them, with `detail`, `violation`, `code`, `message` and `refusal`, and every graph function of DISL 12.4 the rewritten definition calls (finding A10). Each SHALL be proven by a library test seen to fail first.
2. The derivations SHALL support the constructs of finding A2 that they do not support yet; the design SHALL list which, from a load of the rewritten definition.
3. `EtAlii.Adp.Specification.Fbl` SHALL implement what FBL gains for findings B1 and B3; a defect of the library that a fixture of Requirement 1 shows SHALL be repaired in the library; a language gap SHALL NOT be repaired here.
4. No module name SHALL appear in a library change.

### Requirement 9: Gaps are recorded and reported, never tuned away

**User Story:** As the owner of DISL and FBL, I want every place the languages fall short reported back with its evidence.

#### Acceptance Criteria

1. Every language gap found, the ten of section C and any the implementation adds, SHALL be recorded in the companion document with the construct concerned, the evidence (a fixture or a test), and what would resolve it.
2. A gap SHALL NOT be closed by weakening a check, skipping a fixture, or stating in the definition something the tool does not do.
3. The delivery report SHALL list every gap as a candidate change for etalii-adp/etalii.adp, every definition defect that was repaired, and every module defect with its guard.
4. WHEN a *read* finding of this document turns out not to occur THEN it SHALL be struck here with the fixture that shows it, as a small amendment.

### Requirement 10: Documentation, catalog and gates

**User Story:** As a reader of the repository, I want its pages to stay true through the conversion.

#### Acceptance Criteria

1. `src/diagrams/readme.md`'s list of modules with a `definition/` folder, `docs/creating-a-diagram-module.md`, `docs/diagram-module-client-api.md`'s list of modules that compile their notation, and `docs/architecture.md` and `docs/solution-structure.md` where a sentence becomes false SHALL be updated in the change that makes them so.
2. `docs/tools.md`'s row for `etalii/functional-decomposition-graph` SHALL name this specification and keep its state, Prototype.
3. All four gates SHALL exit zero on every pull request of this specification, judged by captured exit codes, and a newly created worktree SHALL be built before a green gate is trusted about the bundled definition.
4. WHEN the tasks document is written THEN every acceptance criterion here SHALL be claimed by a task, established by diffing the two sets, and two traces SHALL be read back to their artefacts.

## Non-Functional Requirements

### Code Architecture and Modularity

- The module ends as declarations, a binding, and the code Requirement 6.1 names. Library additions are named for what they do, never for this tool.
- The house rules apply: `src/.editorconfig`, no blocking call in an `async` method, `TestContext.Current.CancellationToken` in tests, no unneeded `using`, CRLF in the working tree; `.fdg` and `.dis` are byte-compared and already `-text`, and `.fbl` in the module follows what the hype cycle's has.

### Performance

- Opening, validating and editing the example SHALL not be perceptibly slower than today. The cycle check of a connect stays a reachability question from the target, not an enumeration of cycles.

### Reliability

- No file that opens today opens differently (Requirement 4). An unchanged document is written back byte-identical (Requirement 5.10). Undo restores bytes (Requirement 5.9). A scripted or stale request cannot write what the canvas would not offer (Requirement 3.6).
- Every test written for a finding is seen to fail against that finding before it is trusted.

### Usability

- Every sentence a user reads today is read unchanged (Requirements 3.2, 5.6), unless this document names the change.

## Out of scope

- Any change to what the tool does beyond the seven questions above.
- Changing DISL or FBL. Gaps are reported (Requirement 9); a language change is etalii.adp's own specification.
- The other tools of the series. What this one adds to the libraries (Requirement 8) is theirs to use.
- Automatic layout: positions are the author's, and the tool has none.
- The tool's catalog state. It stays a Prototype (Peter, 2026-09-26).

## Appendix: the binding, as drafted

A draft against FBL 0.2, to be settled in the design; no binding existed. It follows the hype cycle's: the header and every entry are elements, a connection is an element with reference attributes (finding B4), every entry carries the id it was written with (B5) and the keys the tool does not read (B6). Two properties in it are **not FBL**: `"as": "text"` marks where the construct of finding B1 is needed, and `"constant"` on an `insert` marks where that of finding B3 is. The rules for `data-element`, `action` and `function` are the first rule with the other name, type and value, and those for `owns-action`, `owns-data`, `owns-function` and `shows` likewise; they are left out here for length.

```json
{
  "fbl": "0.2",
  "bindings": {
    "fdg": {
      "title": "Functional decomposition graph",
      "claims": { "extensions": [".fdg"], "origins": ["etalii/functional-decomposition-graph"] },
      "body": { "kind": "file", "family": "yaml" },
      "reader": "declared",
      "text": { "newline": "crlf", "indent": 2, "sequenceIndent": "indented", "finalNewline": true, "quote": "double" },
      "elements": [
        {
          "name": "graph", "type": "Graph", "at": "/",
          "readOnly": "The graph's version is changed in the file itself.",
          "attributes": { "version": { "key": "functional-decomposition-graph", "as": "text" } }
        },
        {
          "name": "ui-element", "type": "UiElement", "at": "/elements/*",
          "when": "entry.all(k, true) && has(entry.type) && entry.type == 'ui-element'",
          "id": { "from": { "key": "id" } },
          "attributes": {
            "storedId": { "key": "id", "as": "text", "readOnly": true },
            "unknownKeys": { "value": "entry.filter(k, !(k in ['id', 'type', 'name', 'description', 'text', 'x', 'y', 'width', 'height']))" },
            "name": { "key": "name", "as": "text", "empty": "keep" },
            "description": { "key": "description", "as": "text", "empty": "remove" },
            "x": { "key": "x", "number": { "decimals": 4 } }, "y": { "key": "y", "number": { "decimals": 4 } }, "width": { "key": "width", "number": { "decimals": 4 } }
          },
          "insert": { "place": "after-last", "container": "/elements", "constant": { "type": "ui-element" }, "keys": ["id", "type", "name", "description", "x", "y", "width"] },
          "remove": { "cascade": ["ui-child", "owns-action", "owns-data", "owns-function", "shows", "unknown-connection"] }
        },
        {
          "name": "comment", "type": "Comment", "at": "/elements/*",
          "when": "entry.all(k, true) && has(entry.type) && entry.type == 'comment'",
          "id": { "from": { "key": "id" } },
          "attributes": {
            "storedId": { "key": "id", "as": "text", "readOnly": true },
            "unknownKeys": { "value": "entry.filter(k, !(k in ['id', 'type', 'name', 'description', 'text', 'x', 'y', 'width', 'height']))" },
            "strayName": { "key": "name", "as": "text", "readOnly": true },
            "text": { "key": "text", "as": "text", "style": "literal", "empty": "keep" },
            "description": { "key": "description", "as": "text", "empty": "remove" },
            "x": { "key": "x", "number": { "decimals": 4 } }, "y": { "key": "y", "number": { "decimals": 4 } }, "width": { "key": "width", "number": { "decimals": 4 } }, "height": { "key": "height", "number": { "decimals": 4 } }
          },
          "insert": { "place": "after-last", "container": "/elements", "constant": { "type": "comment" }, "keys": ["id", "type", "text", "description", "x", "y", "width", "height"] },
          "remove": { "cascade": ["ui-child", "owns-action", "owns-data", "owns-function", "shows", "unknown-connection"] }
        },
        {
          "name": "unknown-element", "type": "UnknownElement", "at": "/elements/*", "when": "entry.all(k, true)",
          "readOnly": "This entry's type is not one this notation has, so it is kept as it is.",
          "attributes": { "storedId": { "key": "id", "as": "text" }, "documentType": { "key": "type", "as": "text" }, "strayName": { "key": "name", "as": "text" } }
        },
        { "name": "element-not-a-mapping", "type": "Unreadable", "at": "/elements/*", "readOnly": "This entry is not a mapping, so it is kept as it is." },
        {
          "name": "ui-child", "type": "UiChild", "at": "/connections/*",
          "when": "entry.all(k, true) && has(entry.type) && entry.type == 'ui-child'",
          "id": { "from": { "key": "id" } },
          "attributes": {
            "storedId": { "key": "id", "as": "text", "readOnly": true },
            "unknownKeys": { "value": "entry.filter(k, !(k in ['id', 'type', 'from', 'to', 'name', 'description']))" },
            "from": { "key": "from", "as": "text", "reference": { "to": ["ui-element", "data-element", "action", "function", "comment", "unknown-element"], "by": "storedId" } },
            "to": { "key": "to", "as": "text", "reference": { "to": ["ui-element", "data-element", "action", "function", "comment", "unknown-element"], "by": "storedId" } },
            "name": { "key": "name", "as": "text", "empty": "keep" },
            "description": { "key": "description", "as": "text", "empty": "remove" }
          },
          "insert": { "place": "after-last", "container": "/connections", "constant": { "type": "ui-child" }, "keys": ["id", "type", "from", "to", "name", "description"] },
          "remove": {}
        },
        {
          "name": "unknown-connection", "type": "UnknownConnection", "at": "/connections/*", "when": "entry.all(k, true)",
          "readOnly": "This entry's relation is not one this notation has, so it is kept as it is.",
          "attributes": { "storedId": { "key": "id", "as": "text" }, "documentType": { "key": "type", "as": "text" }, "from": { "key": "from", "as": "text" }, "to": { "key": "to", "as": "text" } },
          "remove": {}
        },
        { "name": "connection-not-a-mapping", "type": "Unreadable", "at": "/connections/*", "readOnly": "This entry is not a mapping, so it is kept as it is." }
      ],
      "registration": { "createOnFirstPlacement": false },
      "template": { "text": "functional-decomposition-graph: 1\r\nelements: []\r\nconnections: []\r\n" }
    }
  }
}
```

Six things in it are known to be unsettled and are the design's to close with fixtures. It declares no `header`, so that a missing one raises no finding of FBL's (B7, Q4). No `insert` has a `create`, so that a missing section is still refused (B16). `after-last` places a new entry after the last of its own rule, where today it goes after the last entry of the list whatever its type; `end` may be what is meant. The slots `x`, `y`, `width` and `height` are typed, which is finding B2. What `UnknownElement` and `UnknownConnection` become in the metamodel is finding B3: mapped `unreadable` they leave the model, and nothing else in `typeMap` keeps an entry of no type in it. And the bundled bindings here still declare `"fbl": "0.1"` while using `typeMap` names, so which version string this one carries follows theirs.

## Appendix: the definition's persistence and wire ids, as drafted

The rest of the definition is the existing file brought to 0.3 (Requirement 2). These two blocks are new. A position and a size are attributes of the metamodel that the notation's `placement` binds, with the top-left anchor DISL 5.8 has by default; a named element's `height` is `fixed` at 48 and therefore not bound.

```json
{
  "persistence": {
    "format": "fbl",
    "binding": "https://raw.githubusercontent.com/etalii-adp/etalii.adp.ide.standalone/develop/src/diagrams/functional-decomposition-graph/backend/EtAlii.Adp.Diagram.FunctionalDecompositionGraph/functional-decomposition-graph.fbl#fdg",
    "ids": { "strategy": "uuid-v4", "encoding": "base36", "stable": true },
    "view": { "store": [] },
    "typeMap": {
      "Graph": { "as": "header" },
      "UiElement": { "as": "UiElement", "hostAttributes": ["storedId", "unknownKeys"] },
      "DataElement": { "as": "DataElement", "hostAttributes": ["storedId", "unknownKeys"] },
      "Action": { "as": "Action", "hostAttributes": ["storedId", "unknownKeys"] },
      "Function": { "as": "Function", "hostAttributes": ["storedId", "unknownKeys"] },
      "Comment": { "as": "Comment", "hostAttributes": ["storedId", "unknownKeys", "strayName"] },
      "UiChild": { "as": "UiChild", "attributes": { "from": "source", "to": "target" }, "hostAttributes": ["storedId", "unknownKeys"] },
      "OwnsAction": { "as": "OwnsAction", "attributes": { "from": "source", "to": "target" }, "hostAttributes": ["storedId", "unknownKeys"] },
      "OwnsData": { "as": "OwnsData", "attributes": { "from": "source", "to": "target" }, "hostAttributes": ["storedId", "unknownKeys"] },
      "OwnsFunction": { "as": "OwnsFunction", "attributes": { "from": "source", "to": "target" }, "hostAttributes": ["storedId", "unknownKeys"] },
      "Shows": { "as": "Shows", "attributes": { "from": "source", "to": "target" }, "hostAttributes": ["storedId", "unknownKeys"] },
      "UnknownElement": { "as": "unreadable" }, "UnknownConnection": { "as": "unreadable" }, "Unreadable": { "as": "unreadable" }
    }
  },
  "x-fdg": {
    "tools": {
      "uiElement": { "id": "fdg.toolbox.ui-element", "drop": "fdg.add.ui-element" }, "action": { "id": "fdg.toolbox.action", "drop": "fdg.add.action" },
      "dataElement": { "id": "fdg.toolbox.data-element", "drop": "fdg.add.data-element" }, "function": { "id": "fdg.toolbox.function", "drop": "fdg.add.function" }, "comment": { "id": "fdg.toolbox.comment", "drop": "fdg.add.comment" }
    },
    "actions": {
      "editLabel": "fdg.rename",
      "delete": "fdg.remove",
      "FdgRelation/editLabel": "fdg.rename-connection",
      "FdgRelation/delete": "fdg.disconnect",
      "addUiElementHere": "fdg.add.ui-element", "addDataElementHere": "fdg.add.data-element", "addActionHere": "fdg.add.action", "addFunctionHere": "fdg.add.function", "addCommentHere": "fdg.add.comment",
      "connectUiChild": "fdg.connect.ui-child", "connectOwnsAction": "fdg.connect.owns-action", "connectOwnsData": "fdg.connect.owns-data", "connectOwnsFunction": "fdg.connect.owns-function", "connectShows": "fdg.connect.shows"
    },
    "properties": {
      "name": "fdg.name", "text": "fdg.text", "description": "fdg.description",
      "width": "fdg.width", "height": "fdg.height", "connectionName": "fdg.connection-name"
    }
  }
}
```

A connection's Name row needs a form item `id` (`connectionName`, DISL 0.3) because its attribute is called `name` as an element's is and the two have different wire ids. Whether an action key may name the abstract `FdgRelation`, as written, or needs one key per relation type is not known and is the design's to settle from `x-ghg`'s rule.

## Sources

- The module `src/diagrams/functional-decomposition-graph/` at `develop` `9a646009`: `FdgParser.cs`, `FdgWriter.cs`, `FdgRuleSet.cs`, `FdgRelations.cs`, `FdgOwnership.cs`, `FdgConnectVerdict.cs`, `FdgValidator.cs`, the three providers, `FdgDocumentFactory.cs`, `_Model/`, `Commands/`, the fixtures `malformed-entries.fdg`, `rule-duplicate-id.fdg` and `rule-unreadable-entry.fdg`, the example, `client/FdgCanvas.tsx`, `client/fdgIds.ts`, `client/readme.md`. Counts are from `git ls-files src/diagrams/functional-decomposition-graph` piped to `wc -l`, and `git ls-files` filtered on `.fdg`.
- `src/backend/EtAlii.Adp.Documents/LineSplice.cs` (`SetKey`, `Quote`, `IndentOf`, `InsertionPointFor`, `KeyIndentWithin`) for findings B10 to B14 and B17.
- etalii-adp/etalii.adp at `develop` `da64ff0`: `definitions/diagrams/functional-decomposition-graph.dis` and `.md`; `specifications/disl/functional-decomposition.dis`; `specifications/disl/DISL-specification.md` (0.3 draft: *Changes from 0.1*, *Changes from 0.2*, sections 4.3, 6.7, 6.10, 7.2, 7.3, 8.1, 8.6, 8.7, 9.5, 11.2, 11.5, 12.4, D.2); `specifications/fbl/FBL-specification.md` (0.2 draft: sections 4.1, 4.3, 5.1 to 5.7, 6.1 to 6.4, 7.4, 7.5, 11.5).
- For finding A10: `grep -rn -F` for `std.multiplicity` and for `std.acyclic` over `src/backend/EtAlii.Adp.Specification.Disl`, no result for either (`std.duplicateId` gives five, which shows the search sees built-ins), and `Expressions/DislCelLibrary.cs` for the twelve methods.
- For finding A14: `grep -n -i 'lists\.range'` over the DISL specification, no result. For A3: `grep -n -i dash` over `client/fdg.css`, no result. For A9: a search of the specification for a sentence on several relation types claiming one gesture, which found none; that is an absence in a search, not a proof.
- For finding B14: `src/backend/EtAlii.Adp.Specification.Fbl/Files/Yaml/YamlFamily.cs`, the case commented "An empty flow sequence ... becomes a block sequence". This contradicts the sentence in `timeline-disl-fbl`, finding B4, that the planner has no code for it.
- `src/backend/EtAlii.Adp.Specification.Fbl.Tests/RealFiles/`: a search for `fdg` and `FunctionalDecomposition` over its files, `divergences.json` included, found nothing.
- The hype cycle graph module as the pattern: `gartner-hype-cycle-graph.fbl`, `GhgParser.cs` (`Text`, the reader's sentences), `definition/gartner-hype-cycle-graph.dis` for `persistence.typeMap` and `x-ghg`, and its `Parity/` folder.
- `.spec-workflow/specs/functional-decomposition-graph/requirements.md` for the tool's own requirements (2.2 to 2.5, 5.4, 5.5, 7.1), and `causal-loop-disl-fbl` and `timeline-disl-fbl` for the series and the shape of this document.
