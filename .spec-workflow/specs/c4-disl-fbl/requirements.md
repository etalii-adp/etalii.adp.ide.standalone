# Requirements Document

## Introduction

The user's request, verbatim (2026-10-09):

> Create spec workflow MCP specifications. One specification for converting one of the standalone diagram tools to FBL and DISL. i.e. analyse the current implementation, define the DISL and FBL and then write the specification on the refactoring., also focus on finding issues and aspects where the DISL and FBL languages do not yet cover everything.

This is the specification for **C4**, one of the fifteen that reading gives (the series and the survey behind it are in `causal-loop-disl-fbl`, *Introduction*). It converts the module `src/diagrams/c4/`, which is on neither language.

**C4 is one module and seven tool types.** Six of them (`c4/context`, `c4/container`, `c4/component`, `c4/system-landscape`, `c4/dynamic`, `c4/deployment`) are six readings of one Structurizr DSL workspace (`.dsl`), each drawing one kind of view of it. The seventh, `c4/code`, has no canvas and a document factory that refuses with a sentence; it is not converted. One specification covers the module, because the six share one parser, one writer, one rule set and one layout, and because what makes C4 hard for the languages is exactly that sharing: several readings of one body (FBL section 9), a view chosen by a registration header, and positions kept in a sidecar FBL calls legacy (FBL 8.7).

**What "converted" means** is what it means for the hype cycle graph: toolbox, menus, property rows, findings and refusals derived from bundled DISL definitions by `EtAlii.Adp.Specification.Disl`, the body read and written through an FBL binding by `EtAlii.Adp.Specification.Fbl`, and module code only for what neither language can state. Nothing a user sees or a file holds changes unless this document names the change and the user has ruled on it.

## What was measured

Read on 2026-10-09: the module at `develop` `9a646009`; the six drafts `definitions/diagrams/c4-*.dis` with their `.md`, `specifications/fbl/structurizr.fbl`, the DISL specification (0.3 draft) and the FBL specification (0.2 draft) in etalii-adp/etalii.adp at `develop` `da64ff0`; and the two libraries here.

| Thing | State |
| --- | --- |
| The module's backend | 5,943 lines of C# in 40 files. `C4Parser` (469 lines) with `C4Tokens` (206) reads; `C4Commands` (223), `C4Placement` (288) and `AddC4ViewCommand` (233) write by line surgery on `C4Document`; `C4RuleSet` (479) holds sixteen findings; `C4ContextActionProvider` (290), `C4ContextPropertyProvider` (182) and `C4ToolboxProvider` (78) hold menus, rows and palette; `C4LayoutSidecar` (261) holds positions. 38 test files, 12 `.dsl` fixtures. |
| The module's client | 560 lines outside tests. `C4Canvas.tsx` holds a hand-written canvas definition; nothing calls `parseDisl` or `compileNotation`. |
| The DISL definitions | Six drafts in etalii.adp, 2,230 to 2,383 lines each, **all DISL 0.1**, none bundled here (`src/diagrams/c4/definition/` does not exist). Each names a persistence plugin and a split into `{name}.dsl` and `{name}.layout.json`. None has `language.origin`, and none has an `x-` wire-id block. |
| The FBL binding | `structurizr.fbl`, binding `workspace`, 111 lines, `blocks` family, declared `"fbl": "0.1"`, vendored unchanged in `EtAlii.Adp.Specification.Fbl.Tests/Conformance/` with one conformance fixture (`structurizr-open-block`). Nothing names it from a definition. |
| The real-file check | `ModuleCrossCheck.Tests` reads the 16 module and example `.dsl` files through the binding and compares ids per type with `C4Parser`; `Registrations.Tests` reads the 26 C4 registrations and the four `*.layout.json` files. **33 divergences are recorded** in `RealFiles/divergences.json`: 8 `cross-check`, 24 `registration-view`, 1 `unreadable` (findings B1, B2, B3). |
| A parity transcript | None. The module has no `Parity/` folder. |

**How each finding is known:** *recorded* is an entry of `divergences.json`, a test, or a sentence in the code or in a fixture's readme; *measured* is a search of this repository whose command is in *Sources*; *read* is a reading of code against specification text, not run. Nothing was built or run. Requirement 1 turns every *read* finding into a fixture first.

## Findings

The three kinds are those of `causal-loop-disl-fbl`: a **definition defect** is repaired in a definition or the binding, a **runtime gap** in this repository's libraries, a **language gap** in etalii-adp/etalii.adp.

### A. The DISL definitions against the module

| # | Finding | Kind | Known |
| --- | --- | --- | --- |
| A1 | All six drafts say `"disl": "0.1"`. The client refuses anything but 0.3 and every bundled definition here is 0.3. Each must be rewritten at 0.3 before it can be bundled. | Definition defect | Measured |
| A2 | The companions list as inexpressible what DISL 0.2 and 0.3 answer, and DISL's own table of worked examples names `c4-container.dis` for each: view membership with wildcards (3.5, `members` with `matchesGlob`), relationships lifted to the nearest drawn ancestor and merged (4.11.3), a legend computed from what is drawn and a header band (6.13), whole-model rules (`over: "model"`, `diagram.views`), the tool's own finding codes (`code`; the drafts carry them as `tags`), labels that follow state (a CEL Message: "Add description…" and "Edit description…"), a dangling end that is kept (`typeMap`), and identifiers compared without case (`ids.compare`). The drafts work around each or leave it out. | Definition defect | Read |
| A3 | `persistence` names `plugin:net.etalii.adp.c4.structurizrDsl` with `files`, `encoding`, `omitDefaults` and `precision`. With the binding it becomes `format: "fbl"` with a `typeMap`, the keys DISL 11.2 forbids go, and each definition gains `language.origin`, which a registration's first line names. | Definition defect | Measured |
| A4 | The drafts give no wire ids. The module answers 9 action ids (`c4.rename`, `c4.edit-description`, `c4.set-technology`, `c4.relabel`, `c4.set-protocol`, and `c4.add-<kind>` for four kinds), 10 property ids, and toolbox ids `c4.toolbox.<kind>` for eight kinds. | Language gap, already ruled: wire ids stay an `x-` block (Peter, 2026-10-06). Each definition gets `x-c4`. | Read |
| A5 | The standalone DISL runtime lacks what the rewritten definitions call. `matchesGlob`, `over`, `diagram.views` and `ignore-case` occur nowhere in `EtAlii.Adp.Specification.Disl`; a viewpoint's `members` is named only in the expression walker's list of keys; `location()` likewise; and `DislLoader` refuses a specification that declares `imports` ("This runtime does not resolve imports"). Derived relations are implemented. | Runtime gap | Measured |
| A6 | **One module, six definitions.** `bundle-disl.sh` copies `definitions/diagrams/<module>.dis`, one per module. The six drafts are "deliberately self-contained and nearly identical"; DISL 3.3 lets them import one shared model, and the runtime cannot (A5). | Runtime gap and tooling gap (Q8) | Read |
| A7 | **A relationship's id is its line.** The wire id is `<source>-><destination>@<line>`, so it changes when a line is inserted above it; a lifted line keeps the first relationship's line with the drawn ends. DISL's default for a derived relation is `<Type>:<source.id>-><target.id>` with `#2` for repeats. A `derived.id` expression can state today's id only with `location()`, which the runtime lacks. | Runtime gap; a behaviour change otherwise (Q7) | Read |
| A8 | **An element declared without an identifier** gets `<kind>_<name letters and digits>_<n>`, with n counting such elements in document order. The id is put on the wire and accepted as a sidecar key, and it is never written. DISL offers `ids.missing: "assign"`, which writes an identifier into another ecosystem's file at the next save, or `"ephemeral"`, which refuses to store a position where the module stores one today. Six documents of the corpus declare such elements. | Behaviour change to rule on (Q7) | Read; the occurrence measured |
| A9 | **A new element's identifier** is the name's letters and digits with the first lowered, `_` before a leading digit, `element` when nothing is left, then `web`, `web2`, `web3`. DISL 11.5.1 says its `natural` example gives exactly that, and `natural` composes the key attributes as written, so the name `Web App` gives `Web App`, which the pattern refuses. | Definition defect in the language's own example, or a language gap, to settle in design | Read |
| A10 | **A drawn line that is not a model relationship** (a lifted relationship, a dynamic view's interaction, a line between two instances) can be selected, and gets no menu and no rows: both providers look the id up among the model's relationships and find none. A derived relation with `edits` would offer edits the tool does not offer. | Behaviour kept: derived without `edits` | Read |
| A11 | **The order of findings is by element, not by rule.** `C4RuleSet` reports the includes, then for each element its description, technology, connection and placement findings, then the relationships, then each view, then coverage. DISL orders by rule (8.6), and `constraints.order` orders by code, then line. | Language gap, or a behaviour change (Q4) | Read |
| A12 | **A view's `autoLayout` sets the layout's direction and separations** (`tb`, 120, 60 otherwise), and a view that declares one ignores every stored position, without telling the user. DISL's `LayoutConfig.direction` and `spacing` are literals, not bindable values, and nothing says that view data is ignored while a diagram attribute is set. | Language gap | Read |
| A13 | `c4.dangling-relationship` is reported once per relationship, for the source when both ends are missing, with the line in its sentence, and a dangling relationship gets no label or technology finding. `std.references` under `typeMap` reports per end. The sentence needs `location()` (A5). | Definition defect, to settle by fixture | Read |
| A14 | The empty-canvas menu offers "Add person&" and "Add software system&": the labels end in a literal `&` where every other label ends in an ellipsis, and the container and component entries are filtered out by a check against an empty model. | Module defect, to fix on purpose with its own guard | Recorded (`c4-container.md`) |
| A15 | The `styles` block's headers are recorded and its properties never read, so a document's styles change nothing. The definitions state the default theme only, and the binding binds no style. | None: kept as it is | Recorded (`c4-container.md`) |
| A16 | The `c4Ranks` layout, the boundaries and the pushing of outsiders, the box measurement, the viewport filter and the `.proto` payloads are the host's and stay code. DISL names a layout as a plugin (D.2, open question 9) and has no streaming model on purpose. | None | Read |

### B. The FBL binding against the module's parser and writers

| # | Finding | Kind | Known |
| --- | --- | --- | --- |
| B1 | **Rules for three of eight kinds.** The binding reads people, software systems and containers. `C4Parser` also reads `component`, `deploymentNode`, `infrastructureNode`, `containerInstance` and `softwareSystemInstance`, so every relationship with such an end is dropped as dangling (FBL 5.4). The binding also lacks a `deploymentEnvironment` block. | Definition defect | Recorded (8 `cross-check` entries, binding `structurizr`) |
| B2 | **A view's key is read from the wrong quoted string.** The view block rule takes the last quoted string before the brace as the key; a view written with a key and a description gives the description, and the registration's `view:` selects nothing. The module reads the first string after the scope, and for a deployment view the environment first; it also gives a view written without a key a default (`landscape`, the kind in lower case, the environment), which the rule cannot. | Definition defect | Recorded (24 `registration-view` entries, binding `workspace`); the default key read |
| B3 | **A comment after an opening brace makes the body unreadable.** FBL 4.7 opens a block only when the statement's last non-whitespace character is the brace, so `model { // …` opens none and the braces no longer balance. Structurizr accepts it and the module reads it. | Language gap | Recorded (1 `unreadable` entry, `comments-everywhere.dsl`) |
| B4 | **A comment after any statement.** The module cuts a line at the first `//` or `#` outside a quoted string before reading it. FBL's `comment` is a whole line (4.6), so every rule's expression would have to admit a comment tail itself, and the block test of 4.7 is not an expression a binding can widen. | Language gap | Read |
| B5 | **Block comments.** The module skips `/* … */`, within a line and across lines. FBL has no comment that spans lines: the lines inside one are statements, a declaration inside one becomes an element, and a brace inside one counts toward the balance. The fixture's block comments happen to match no rule. | Language gap | Read |
| B6 | **Words, not expressions.** The module splits a line into words and reads positions. It therefore reads an unquoted argument (`person User`), an escaped quote inside a string, any number of further arguments (a deployment node's instance count after its tags), any word as an identifier, and keywords in any case. The binding's expressions require quotes, stop a string at an escaped quote, admit a fixed number of arguments and a narrower identifier. In one place the binding is wider: it reads `a->b`, which the module does not. | Definition defect (Q2) | Read |
| B7 | **An escaped quote cannot be written.** The module reads `\"` as a quote and writes a quote back escaped. FBL has escaping rules for xml, yaml and json, and none for a quoted group of `lines` or `blocks`. | Language gap | Read |
| B8 | **Relationship forms the binding lacks:** `-> destination "…"` inside an element's block, whose source is the enclosing element (FBL's `parent` slot states it); a relationship that opens a block; and a relationship inside a deployment node or environment. A relationship whose end names nothing is kept by the module and reported; FBL 5.4 creates none, and the hype cycle's pattern (an element with reference attributes, mapped by `typeMap`) answers it. | Definition defect, with a known pattern | Read |
| B9 | **Tags come from two places.** A `tags "A" "B"` line inside an element's block adds to the tags of its declaration, and tags decide the muted style (`External`, `Existing System`) and the cylinder (`Database`). FBL binds a value to one place (3.3), and a computed slot sees its own entry's groups only. | Language gap, minor: tags are read-only in the tool | Read |
| B10 | **Where an element may stand.** The binding's `within` admits a container only inside a software system or a group. The module reads an element keyword in any block that is not a view, which is how a container written straight in the model is read and reported as `c4.misplaced-element`. Under the binding that line is unbound and the finding is lost. | Definition defect | Read |
| B11 | **`!include`.** The module records the path and reports `c4.include-not-followed` once per include, without a location. The binding leaves it unbound; `unmatched: "report"` would report every construct the tool does not model. A read-only rule for the directive, mapped to a type no viewpoint draws, lets a DISL rule give the sentence. | Definition defect | Read |
| B12 | **Hierarchical identifiers.** `!identifiers hierarchical` is carried and not honoured: a dotted reference resolves to nothing and is reported as dangling, and two elements of one name under different parents resolve to the first. Neither language states an id that depends on a directive of the body. | Kept unsupported, and recorded so that nobody reads the conversion as adding it | Recorded (`C4DocumentFactory.cs`, `c4-container.md`) |
| B13 | **The view's own statements reach nothing.** FBL names a view by its block (4.7) and says its content "is the reading's DISL viewpoint's, not FBL's" (9.3). The drafts declare `includes`, `excludes`, `title`, `autoLayout`, `rankSeparation`, `nodeSeparation` and `scope` as diagram attributes, and a dynamic view's interactions are statements inside its block. Nothing in FBL reads the selected view's statements into the diagram's attributes, and a rule `within` a view block would read every view's. | Language gap; this one blocks the reading | Read |
| B14 | **Which view a reading shows.** Without a `view:` header the module shows the document's first view of any kind; FBL 9.3 says the first "of the kinds the reading's tool type shows". The module draws the view by the view's own kind, so a `c4/context` registration naming a container view shows a container view; under six definitions the registration's origin decides. A `view:` naming no view opens an empty canvas and says so only in the log; FBL does not say what it opens. | Behaviour change to rule on (Q6); the last a language gap | Read |
| B15 | **Positions live in the sidecar FBL calls legacy.** The module's only store is `<base>.layout.json`, which it creates at the first drag; no C4 registration in the repository has a `layout:` block. FBL 8.7 reads such a file while the registration has no block, writes back to it while it exists, and forbids creating one. | Behaviour change to rule on (Q3) | Recorded (`Registrations.Tests`); the absence measured |
| B16 | **What the sidecar holds, byte for byte.** The module rewrites the whole file, indented, with each number as .NET writes a double, and rounds to two decimals when it reads; element keys match without case. FBL writes json splices and, in a `layout:` block, three decimals; the library's `LegacySidecar` matches element keys exactly. FBL 8.7 states neither the number form of a legacy file nor whether its stale entries are pruned as 8.5 prunes a block's. | Language gap, minor; a behaviour change (Q1) | Read; the comparer measured |
| B17 | **Undoing a first drag forgets too much.** `C4LayoutSidecar.Remove` removes the element from every view of the file, not from the view the drag was made on. | Module defect, to fix on purpose with its own guard | Read |
| B18 | **What a changed value writes.** The module replaces the one argument with a double-quoted value, so an unquoted argument becomes quoted. Where the argument is absent it appends it and pads the positions before it with `""`, so a technology set on `container "Web"` gives `container "Web" "" "…"`. FBL's `insert-key` writes the one segment, which would put the technology in the description's place; `re-emit-line` loses unbound words, and what it does to a statement that opens a block is not stated. | Language gap (positional optional arguments); a behaviour change (Q1) | Read |
| B19 | **An appended argument can land inside a comment.** When the argument is absent and the line has no brace, the module appends at the end of the whole line, comment included, while it found the arguments on the line without its comment. The edit succeeds and reads back unchanged. | Module defect, to fix on purpose with its own guard | Read |
| B20 | **Where a new element goes.** A top-level element is written above the closing brace of the model, indented by eight spaces whatever the document uses; a child above its parent's closing brace, four spaces deeper than the parent, the parent gaining a block when it has none. The binding places a person or a software system after the last of its own rule, and FBL indents like the previous sibling. | Behaviour change to rule on (Q1) | Read |
| B21 | **Undo is a command, not bytes.** Each C4 edit undoes by an inverse command that finds the element again by its id, so it survives an edit elsewhere in the file. FBL undoes by inverse splices and refuses when the body has drifted (7.2). | Behaviour change to rule on (Q1) | Read |
| B22 | **An unbalanced body.** The parser never fails: a document whose braces do not balance opens with what could be read. FBL 4.7 makes it unreadable: empty, read-only, one finding. A document being typed in the text editor is unbalanced between two keystrokes. | Language gap for a tolerant tool; a behaviour change otherwise (Q5) | Read |
| B23 | **The template.** The binding has three texts (the default, `c4/context`, `c4/system-landscape`); the factory writes six, with other bytes: blank lines between the blocks, no description on the placeholder system, a nested `application` container for a component view, an environment with one node for a deployment view, a dynamic view without `include`, and the keys `context`, `containers`, `components`, `scenario`, `deployment`, `landscape`. The factory's identifier keeps every Unicode letter and falls back to `system`; FBL's `{key}` keeps ASCII and falls back to `untitled`; the factory escapes a quote in the name and `{base}` does not. | Definition defect; the identifier a language gap, minor | Read |
| B24 | **Adding a view.** `AddC4ViewCommand` writes a view block and a registration and undoes both. No gesture constructs it: only its own file and two test files do. FBL gives a block rule no `insert`, and creates a registration only for a new document or at a first placement. | Language gap; stays code | Read; the reach measured |

### C. Language gaps, as they would be reported to etalii-adp/etalii.adp

1. **FBL: the statements of the selected view** (B13). Candidate: rules `within` a view block are read for the registration's view only, and `typeMap` maps them `as: "diagram"`; or a view block may be an element rule that a registration header selects.
2. **FBL 4.7 and 4.6: a comment after a brace or a statement** (B3, B4). Candidate: the binding's `comment`, or a second expression, is stripped from the end of a statement outside quoted strings before the block test and before any rule.
3. **FBL: a comment that spans lines** (B5). Candidate: `blockComment: {open, close}`.
4. **FBL: an escape inside a quoted group** (B7), read and written.
5. **FBL: positional optional arguments** (B18): writing argument n writes an empty value for each absent argument before it, and never re-emits a statement that opens a block.
6. **FBL 4.7 and 7.5: a body whose braces do not balance** (B22), for a binding that asks to read what does.
7. **FBL 8.7 and 9.3: the legacy layout and the view header** (B14, B16): the number form, the comparison of element keys, the pruning of stale entries, and what a `view` that names nothing opens.
8. **FBL: adding a block** (B24), and a value gathered from two places (B9).
9. **DISL 10: a layout's direction and spacing from a diagram attribute**, and view data ignored while one is set (A12).
10. **DISL 8.6: findings ordered by element** (A11).
11. **DISL 11.5.1: the identifier of a new element made from its name** (A9).
12. **Both: an id that depends on a directive of the body** (B12).

These are candidates. Requirement 11 says they are recorded with their evidence and reported, and never worked around silently.

## Open questions

Each is put to the user as a selection. The option marked **(default)** is what this document is written against; a ruling that differs is a small amendment to the criteria named.

| # | Question | Options | Criteria affected |
| --- | --- | --- | --- |
| Q1 | What does an edit write, and how is it undone (B16, B18, B20, B21)? | **(default) What FBL writes**: the one value in its own style, a new element indented like its sibling, the sidecar by splices, undo by bytes with FBL's drift refusal; the transcript's edit hunks are regenerated once and reviewed as a diff. · **What the module writes today**: the transcript stays byte-identical, and FBL needs constructs it does not have first. · Other. | 5.2, 5.4, 5.6, 1.4 |
| Q2 | The lenient spellings the parser reads (B6, B10): keep reading them? | **(default) Yes**: the binding is widened until every statement the parser reads is read the same. · **No**: the binding's grammar becomes the format; every `.dsl` in the repository already satisfies it, and such a line becomes unbound and loses its element. · Other. | 4.2 |
| Q3 | Where do positions go (B15)? | **(default) As FBL 8.7 says**: an existing `*.layout.json` is read and written while it exists, none is created, and a document without one stores positions in its registration's `layout:` block. · **Migrate once**: each registration takes its view's positions into `layout:` when first written, and an emptied sidecar is deleted. · **Sidecar only**, as today, which needs FBL to allow creating one. · Other. | 7.1, 7.2 |
| Q4 | Findings the languages would add (two elements of one identifier, a location on the include finding, one finding per missing end) and the order of findings (A11, A13). | **(default) Switched off, and today's order kept**, each raised afterwards as a small change of its own; where the order cannot be stated it is module code named for gap C10. · **Adopted now**, each named in the transcript's diff. · Other. | 3.3 |
| Q5 | Gaps C1, C2 and C6 stop the reading: a legal body does not open (B3) and a view's content does not arrive (B13). What happens meanwhile? | **(default) Derive first, read later**: Requirements 1 to 3 and 9 are built on a DISL model made from `C4Parser`'s reading, as the timeline's is today, and Requirements 4 to 7 wait for FBL. · **Work around it**: a module pass removes comments and reads the view block before FBL reads the body, named as standing in for the gaps. · **Narrow the format**: trailing comments and unbalanced bodies stop opening. · Other. | 4, 5, 6, 7 |
| Q6 | Which view does a reading show, and as what (B14)? | **(default) As today**: the named view whatever its kind, drawn by the definition of the view's kind; without a header the first view of any kind. · **As FBL 9.3 says**: the registration's origin decides the definition, and without a header the first view of that kind. · Other. | 6.2 |
| Q7 | Ids the body does not hold (A7, A8). | **(default) Ephemeral, and nothing written into the `.dsl`**: wire ids stay as they are, and a drag of an element without an identifier is refused with a reason, where today it is stored under an id that the next insertion above it breaks; sidecar entries under such ids are reported and dropped at the next write. · **Assign**: an identifier is written into the declaration at the next save. · Other. | 3.4, 7.5 |
| Q8 | Six definitions over one model (A6). | **(default) Six self-contained files**, as in etalii.adp today, bundled into one module, with a test that holds their shared layers equal. · **One shared library that the six import**, after the runtime resolves imports. · Other. | 2.3, 10.3 |
| Q9 | Is the client's canvas definition in scope? | **(default) Yes**: compiled from the bundled definition of the view's kind, with today's hand-written definition kept in a test as the oracle. · **No**: backend only. · Other. | 9 |

## Alignment with Product Vision

The product direction (2026-09-26) is that every tool is a markup definition interpreted by a core in each IDE, with code only where a definition cannot reach. C4 is the tool both specifications use as their example of several readings of one body: DISL's table of worked examples and FBL sections 8.7 and 9.2 name it. It is also the one whose vendored binding disagrees with its real files 33 times. Converting it tests the part of the languages that the W3C types will need next, and `structure.md`'s rule that a diagram type adds declarations and no core code: what the libraries lack (A5, A6) is added to the libraries for every tool.

## Requirements

### Requirement 1: An oracle before any change

**User Story:** As the owner of the tool, I want what it answers today frozen before anything is converted, so that every later step is held to it.

#### Acceptance Criteria

1. WHEN the work starts THEN a parity transcript `Parity/c4.transcript.json` SHALL be generated from today's hand-written code and committed before any provider, parser or writer is changed, in the shape of the timeline's: per document and per view, the toolbox of the view's kind; the context menu of every element, every drawn line, the empty canvas and an unknown id; the property rows; the findings; the payload of every mapped element; and a scripted edit sequence with each step's answer and the hunks it made to the body and to the sidecar.
2. The corpus SHALL be the 12 fixtures, the module's two examples with their registrations and sidecars, the copies under `src/examples/diagrams/c4/`, a new document of each of the six types, and **one new fixture for each finding of tables A and B marked Read**, written so that the finding shows in the transcript or is struck from this document with the reason.
3. A test SHALL require today's code to reproduce the checked-in transcript byte for byte, and SHALL have been seen to fail against a deliberately changed sentence before it is trusted.
4. WHEN a later step changes the transcript THEN the change SHALL be one this document names (Q1, Q4, Q7, findings A14, B17, B19), regenerated on purpose, and the diff of the checked-in file SHALL be the review of it.

### Requirement 2: Six definitions, at DISL 0.3, bundled

**User Story:** As a tool engineer, I want one definition per C4 tool type that every host runs.

#### Acceptance Criteria

1. The six `definitions/diagrams/c4-*.dis` in etalii-adp/etalii.adp SHALL be rewritten at DISL 0.3, using the constructs finding A2 names in place of each workaround, and SHALL validate against the DISL schema there.
2. Each SHALL declare `language.origin`, `persistence.format` `fbl` with `binding` naming the module's binding and a `typeMap`, an id rule (findings A7 to A9, Q7), and an `x-c4` block mapping its tools, actions and properties to the wire ids of finding A4.
3. The layers the six share (metamodel, notation, forms, constraints, behavior, persistence) SHALL be equal in all six, held by a test that compares them as data (Q8); the six SHALL differ in their viewpoint, toolbox, labels and origin only.
4. Each companion `.md` SHALL be rewritten to say what DISL 0.3 cannot express and nothing that it can.
5. The six SHALL be bundled into `src/diagrams/c4/definition/` by `src/diagrams/tools/bundle-disl.sh`, extended to bundle several definitions for one module, never edited here, embedded in the backend project, and held by a `BundledDefinition.Tests`.
6. WHEN a bundled definition is loaded by `EtAlii.Adp.Specification.Disl` THEN it SHALL load with no diagnostic of severity error.
7. `c4/code` SHALL keep its registration and its refusing factory, with no definition.

### Requirement 3: Toolbox, menus, rows and findings are derived

**User Story:** As a maintainer, I want the palette, the menus, the rows and the findings to come from the definitions.

#### Acceptance Criteria

1. The toolbox of each view kind, the context menu of every target, the property rows and every read-only reason SHALL be derived from the bundled definition through `ToolboxDerivation`, `ContextMenuDerivation` and `FormDerivation`, mapped to the wire ids of `x-c4`.
2. Every sentence a user reads today SHALL be read unchanged: labels that follow state ("Add description…" and "Edit description…", "Set technology…" and "Change technology…"), "Every C4 element needs a name.", the two read-only reasons of the structure rows and of a relationship's ends, "Tags are edited in the document until an action exists for them.", and the toolbox descriptions.
3. The sixteen findings (`c4.missing-description` to `c4.element-not-on-any-view`) SHALL be derived by `ConstraintEvaluator` with today's codes, severities, sentences and locations, in today's order, the two suppressions while the model declares no view included; and no finding the tool does not give today SHALL appear (Q4).
4. Lifted relationships, a deployment view's lines between instances and a dynamic view's numbered interactions SHALL be derived relations with today's ids (finding A7), and SHALL be offered no menu and no rows (finding A10).
5. The title and the legend SHALL be the definition's (DISL 6.13), with today's wording.
6. WHEN the derived answers are compared with the transcript THEN the toolboxes, menus, rows, findings and payloads SHALL be byte-identical.
7. The hand-written providers and rule set SHALL be kept in the test project as the oracle and removed from the product code.
8. The empty-canvas labels SHALL end in an ellipsis (finding A14), with a test seen to fail against today's `&` first.

### Requirement 4: The body is read through the binding

**User Story:** As a user, I want every `.dsl` that opens today to open the same.

#### Acceptance Criteria

1. The module SHALL own its binding, `backend/EtAlii.Adp.Diagram.C4/structurizr.fbl`, embedded in the backend project. The example in etalii.adp SHALL be brought in line with it or the difference recorded.
2. WHEN a body is read THEN the elements of all eight kinds, their parents, names, descriptions, technologies and tags SHALL be those `C4Parser` reads today for every document of the corpus, the lenient spellings of finding B6 and the placements of finding B10 included (Q2).
3. Every relationship form of finding B8 SHALL be read; a relationship whose end names nothing SHALL be kept, not drawn, and reported as `c4.dangling-relationship` with today's sentence.
4. Every view SHALL be read with the key, scope and environment the module reads, a view written without a key included (finding B2), and its `include`, `exclude`, `title`, `autoLayout` and interactions SHALL reach the reading that shows it (finding B13).
5. Each `!include` SHALL be reported as `c4.include-not-followed` and never followed (finding B11); `!identifiers hierarchical` SHALL stay unsupported as today (finding B12).
6. A comment of any of the three forms, wherever the module accepts one today, SHALL not change what is read (findings B3 to B5), and a body whose braces do not balance SHALL open as it does today (finding B22, Q5).
7. Reading SHALL never throw on content, and everything no rule binds SHALL be kept byte for byte.

### Requirement 5: Every edit is written by splices

**User Story:** As a user editing a file another tool also owns, I want an edit to change what I changed and nothing beside it.

#### Acceptance Criteria

1. Every command of the module SHALL write through `EtAlii.Adp.Specification.Fbl` as a model change planned into splices; no module code SHALL build or replace a line.
2. WHEN one value changes THEN only its bytes SHALL change (Q1), a quote in it SHALL be written escaped (finding B7), and every other argument, word and comment of the line SHALL survive.
3. WHEN the argument is absent THEN it SHALL be written in its own position, with an empty value for each absent argument before it, before any brace and before any comment (findings B18, B19), with a test for the comment case seen to fail against today's code first.
4. A new element SHALL be written with today's text (`<identifier> = <keyword> "<Name>" "Describe <Name>."`, and `"Technology"` for a container or a component), placed and indented as the binding declares (Q1); a parent without a block SHALL gain one, and undoing the add SHALL take it away again.
5. Every refusal SHALL keep today's sentence: the placement refusals of `C4Placement`, "This model already has something called '…'.", and the sentences for an element or a relationship that is gone.
6. Undo SHALL restore the body byte for byte (Q1).
7. A document ADP did not change SHALL be written back byte-identical, for every fixture, the pair of line-ending fixtures and the one without a final newline included.
8. A new document of each of the six types SHALL have the bytes `C4DocumentFactory` writes today (finding B23); WHERE the template's placeholders cannot give them THEN the difference SHALL be named in the pull request and recorded under Requirement 11.

### Requirement 6: Several readings of one body

**User Story:** As an author who models once and views many times, I want every view of a workspace to stay one model.

#### Acceptance Criteria

1. Every open reading of one `.dsl` SHALL share one open body, one model and one history, and an edit through any of them SHALL reach the others without a reload, as today.
2. The view a reading shows, and the definition it is drawn by, SHALL be as Q6 rules: by default the view the registration names, matched without case, drawn as its own kind, and the first view of any kind without a header.
3. A `view:` that names no view SHALL open an empty canvas, as today.
4. A registration SHALL be read as today: the origin line, then `body:` and `view:`; a registration without `body:` SHALL find the `.dsl` of its own base name.
5. `AddC4ViewCommand` and its inverse SHALL stay module code, named in the companion documents as standing in for gap C8.

### Requirement 7: Positions

**User Story:** As an author who arranged a view, I want it to open where I left it.

#### Acceptance Criteria

1. Every position stored in a `*.layout.json` today SHALL be applied after the conversion, per view, with no file rewritten to make it so.
2. A move SHALL be written where Q3 rules, as one undo step; undoing a first placement SHALL remove that element's entry for that view only (finding B17), with a test seen to fail against today's code first.
3. A view that declares `autoLayout` SHALL keep ignoring stored positions, as today (finding A12).
4. A position that cannot be stored SHALL leave the move in place and give today's two sentences.
5. An element whose id the body does not hold SHALL be treated as Q7 rules.

### Requirement 8: What stays code, and why

**User Story:** As a maintainer, I want the code that remains to be exactly what the languages cannot state.

#### Acceptance Criteria

1. The module SHALL keep: the layout (`C4Layout`, `C4Metrics`), the element mapper and the viewport filter, the session, the session factory and the document store, the `.proto` payloads, `C4Theme` where the notation does not replace it, the `c4/code` factory, the view command of Requirement 6.5, and each stand-in a ruling of Q4 or Q5 creates.
2. `C4Parser`, `C4Tokens`, `C4Document`, `C4Placement`, the edit commands' line surgery, `C4LayoutSidecar`, `C4RuleSet`, `C4Validator` and the logic of the three providers SHALL leave the product code.
3. WHEN the conversion is complete THEN the companion documents SHALL list every remaining module class beside the gap or the host concern that keeps it, and a class with neither SHALL be removed.

### Requirement 9: The client

**User Story:** As a user, I want the canvas to look and behave as it does, drawn from the same definitions.

#### Acceptance Criteria

1. The canvas definition SHALL be compiled from the bundled definition with `parseDisl` and `compileNotation` (Q9), with what the library cannot read stated in a `NotationBindings`.
2. Today's hand-written definition SHALL be kept in a test as the oracle, and the compiled one SHALL draw the same markup for the corpus.
3. IF `compileNotation` cannot read something the definitions state (the person and cylinder shapes, the three card labels, the dashed boundary that ignores gestures, the title band and the legend) THEN it SHALL be added to the shared library for every module and SHALL NOT be written in this module.
4. A `tests.md` entry SHALL cover in a real browser, in both themes, each of the six view kinds of the reference example, a drag and its undo, a drop of each toolbox entry on its parent kind and on the empty canvas, inline rename of an element and of a relationship's label, and a lifted relationship selected.

### Requirement 10: The library work this tool needs

**User Story:** As a maintainer of the shared libraries, I want each thing this tool needs added once, for every tool.

#### Acceptance Criteria

1. `EtAlii.Adp.Specification.Disl` SHALL implement what finding A5 lists and the rewritten definitions call: `matchesGlob`, a viewpoint's `members`, constraints `over` the model and the view with `diagram.views`, `ids.compare`, and `location()`. Each SHALL be proven by a library test seen to fail first.
2. The derivations SHALL answer for one of several definitions over one body, chosen by origin.
3. The runtime SHALL resolve `imports` only if Q8 is ruled that way.
4. `EtAlii.Adp.Specification.Fbl` SHALL implement what FBL gains for the gaps of section C, each with a conformance fixture vendored from etalii.adp; a defect a fixture of Requirement 1 shows SHALL be repaired in the library, and a language gap SHALL NOT be repaired here.
5. No module name SHALL appear in a library change.

### Requirement 11: Gaps are recorded and reported, never tuned away

**User Story:** As the owner of DISL and FBL, I want every place the languages fall short reported with its evidence.

#### Acceptance Criteria

1. Every language gap, the twelve of section C and any the implementation adds, SHALL be recorded in the companion documents with the construct concerned, the fixture that shows it, and what would resolve it.
2. The 33 entries of `RealFiles/divergences.json` for `structurizr` and `workspace` SHALL be removed when they no longer occur and SHALL NOT be removed otherwise.
3. A gap SHALL NOT be closed by weakening a check, skipping a fixture, or stating in a definition something the tool does not do.
4. The delivery report SHALL list every gap as a candidate change for etalii-adp/etalii.adp, and every definition defect and module defect that was repaired.
5. WHEN a *read* finding turns out not to occur THEN it SHALL be struck here with the fixture that shows it, as a small amendment.

### Requirement 12: Documentation, catalog and gates

**User Story:** As a reader of the repository, I want its pages to stay true through the conversion.

#### Acceptance Criteria

1. `src/diagrams/readme.md`, `docs/creating-a-diagram-module.md`, `docs/diagram-module-client-api.md`, and `docs/architecture.md` and `docs/solution-structure.md` where a sentence becomes false SHALL be updated in the change that makes them so.
2. `docs/tools.md`'s seven C4 rows SHALL name this specification.
3. All four gates SHALL exit zero on every pull request of this specification, judged by captured exit codes, and a newly created worktree SHALL be built before a green gate is trusted about the bundled definitions.
4. WHEN the tasks document is written THEN every acceptance criterion here SHALL be claimed by a task, established by diffing the two sets, and two traces SHALL be read back to their artefacts.

## Non-Functional Requirements

### Code Architecture and Modularity

- The module ends as six definitions, one binding, and the code Requirement 8.1 names. Library additions are named for what they do, never for this tool.
- The house rules apply: `src/.editorconfig`, no blocking call in an `async` method, `TestContext.Current.CancellationToken` in tests, no unneeded `using`. `.dsl` and `.dis` are byte-compared and already `-text`; the module's `.fbl` follows what the hype cycle's has.

### Performance, Reliability and Usability

- Opening, validating, editing and arranging the largest `.dsl` of the corpus (`bottling-mes.dsl`, 329 lines) SHALL not be perceptibly slower than today, with six readings of it open.
- No file that opens today opens differently, no stored position is lost, and an unchanged document is written back byte-identical. A `.dsl` ADP wrote is still accepted by the Structurizr CLI, as `C4Interop.Tests` checks today.
- Every sentence a user reads today is read unchanged, unless this document names the change. Every test written for a finding is seen to fail against that finding before it is trusted.

## Out of scope

- Any change to what the tool does beyond the nine questions and the module defects A14, B17 and B19.
- What the tool does not do today: deleting, connecting, drill-down, document styles, hierarchical identifiers, following `!include`, a prompt when `autoLayout` ignores positions; and `c4/code`.
- Changing DISL or FBL. Gaps are reported; a language change is etalii.adp's own specification.
- The layout algorithm, the viewport streaming model, and the other tools of the series.

## Appendix: the binding, as drafted

A draft against FBL 0.2, to be settled in the design. It differs from the example in etalii.adp where findings B1, B2, B8, B10, B11 and B23 say so. Quoted groups are written in the short form `"[^"]*"` for legibility; Requirement 4.2 widens each (finding B6). `person`, `softwareSystem`, `component`, `deploymentNode` and `infrastructureNode` follow `container`, and `softwareSystemInstance` follows `containerInstance`; they are left out, as are five of the six template texts.

```json
{
  "fbl": "0.2",
  "bindings": {
    "workspace": {
      "title": "Structurizr DSL workspace",
      "claims": { "extensions": [".dsl"], "origins": ["c4/context", "c4/container", "c4/component", "c4/system-landscape", "c4/dynamic", "c4/deployment"] },
      "body": { "kind": "file", "family": "blocks" },
      "reader": "declared",
      "text": { "newline": "lf", "indent": 4, "finalNewline": true },
      "comment": "^\\s*(#|//)",
      "trailingComment": "(#|//).*$", "blockComment": { "open": "/*", "close": "*/" }, "escape": { "\\\"": "\"" }, "unbalanced": "read",
      "registration": { "legacyLayout": "{base}.layout.json", "createOnFirstPlacement": true },
      "blocks": [
        { "name": "model", "line": "^model\\s*\\{\\s*$", "caseInsensitive": true },
        { "name": "group", "line": "^group\\s+\"[^\"]*\"\\s*\\{\\s*$", "caseInsensitive": true },
        { "name": "environment", "line": "^deploymentEnvironment\\s+\"[^\"]*\"\\s*\\{\\s*$", "caseInsensitive": true },
        { "name": "views", "line": "^views\\s*\\{\\s*$", "caseInsensitive": true },
        { "name": "view", "within": ["views"], "caseInsensitive": true, "view": "key",
          "line": "^(?:systemLandscape|systemContext|container|component|dynamic)(?:\\s+(?<scope>\\*|[^\\s\"{]+))?(?:\\s+\"(?<key>[^\"]*)\")?(?:\\s+\"[^\"]*\")?\\s*\\{\\s*$" },
        { "name": "deploymentView", "within": ["views"], "caseInsensitive": true, "view": "key",
          "line": "^deployment\\s+(?<scope>\\*|[^\\s\"{]+)\\s+\"(?<environment>[^\"]*)\"(?:\\s+\"(?<key>[^\"]*)\")?(?:\\s+\"[^\"]*\")?\\s*\\{\\s*$" }
      ],
      "elements": [
        { "name": "workspace", "type": "Workspace", "opens": true, "caseInsensitive": true, "readOnly": true, "attributes": { "name": { "group": "name" } },
          "line": "^workspace(?:\\s+\"(?<name>[^\"]*)\")?(?:\\s+\"[^\"]*\")?\\s*(?:\\{\\s*)?$" },
        { "name": "include", "type": "IncludedFile", "caseInsensitive": true, "readOnly": true,
          "line": "^!include\\s+(?<path>\\S+)", "attributes": { "path": { "group": "path" } } },
        { "name": "container", "type": "Container", "opens": true, "caseInsensitive": true,
          "within": ["model", "group", "environment", "person", "softwareSystem", "container", "component", "deploymentNode"],
          "line": "^(?:(?<id>\\S+)\\s*=\\s*)?container(?:\\s+\"(?<name>[^\"]*)\")?(?:\\s+\"(?<description>[^\"]*)\")?(?:\\s+\"(?<technology>[^\"]*)\")?(?:\\s+\"(?<tags>[^\"]*)\")?(?<rest>(?:\\s+[^\\s{]+)*)\\s*(?:\\{\\s*)?$",
          "parent": { "rules": ["softwareSystem", "container", "component", "person", "deploymentNode"], "slot": "children" },
          "id": { "from": { "group": "id" } },
          "attributes": {
            "identifier": { "group": "id", "readOnly": true }, "name": { "group": "name", "empty": "refuse" },
            "description": { "group": "description", "empty": "keep", "pad": true },
            "technology": { "group": "technology", "empty": "keep", "pad": true }, "tags": { "group": "tags", "readOnly": true }
          },
          "insert": { "place": "last-child", "emit": "{id} = container \"{name}\" \"{description}\" \"{technology}\"" } },
        { "name": "containerInstance", "type": "ContainerInstance", "opens": true, "caseInsensitive": true,
          "line": "^(?:(?<id>\\S+)\\s*=\\s*)?containerInstance\\s+(?<of>[^\\s\"{]+)(?:\\s+\"(?<tags>[^\"]*)\")?(?<rest>(?:\\s+[^\\s{]+)*)\\s*(?:\\{\\s*)?$",
          "parent": { "rules": ["deploymentNode"], "slot": "children" },
          "id": { "from": { "group": "id" } },
          "attributes": {
            "identifier": { "group": "id", "readOnly": true }, "tags": { "group": "tags", "readOnly": true },
            "of": { "group": "of", "readOnly": true, "reference": { "to": ["container"], "by": "identifier" } }
          } },
        { "name": "relationship", "type": "Relationship", "opens": true,
          "line": "^(?<source>[^\\s\"{]+)\\s+->\\s+(?<destination>[^\\s\"{]+)(?:\\s+\"(?<description>[^\"]*)\")?(?:\\s+\"(?<technology>[^\"]*)\")?(?<rest>(?:\\s+\"[^\"]*\")*)\\s*(?:\\{\\s*)?$",
          "attributes": {
            "from": { "group": "source", "readOnly": true, "reference": { "to": ["person", "softwareSystem", "container", "component", "deploymentNode", "infrastructureNode", "containerInstance", "softwareSystemInstance"], "by": "identifier" } },
            "to": { "group": "destination", "readOnly": true, "reference": { "to": ["person", "softwareSystem", "container", "component", "deploymentNode", "infrastructureNode", "containerInstance", "softwareSystemInstance"], "by": "identifier" } },
            "description": { "group": "description", "empty": "keep" }, "technology": { "group": "technology", "empty": "keep", "pad": true }
          } },
        { "name": "nestedRelationship", "type": "Relationship", "opens": true,
          "line": "^->\\s+(?<destination>[^\\s\"{]+)(?:\\s+\"(?<description>[^\"]*)\")?(?:\\s+\"(?<technology>[^\"]*)\")?(?<rest>(?:\\s+\"[^\"]*\")*)\\s*(?:\\{\\s*)?$",
          "attributes": {
            "from": { "parent": "identifier" },
            "to": { "group": "destination", "readOnly": true, "reference": { "to": ["person", "softwareSystem", "container", "component"], "by": "identifier" } },
            "description": { "group": "description", "empty": "keep" }, "technology": { "group": "technology", "empty": "keep", "pad": true }
          } },
        { "name": "viewInclude", "type": "ViewInclude", "within": ["view", "deploymentView"], "selectedViewOnly": true, "caseInsensitive": true, "readOnly": true,
          "line": "^include(?<members>(?:\\s+\\S+)+)\\s*$", "attributes": { "members": { "group": "members" } } },
        { "name": "interaction", "type": "Interaction", "within": ["view"], "selectedViewOnly": true, "readOnly": true,
          "line": "^(?<source>[^\\s\"{]+)\\s+->\\s+(?<destination>[^\\s\"{]+)(?:\\s+\"(?<description>[^\"]*)\")?",
          "attributes": { "from": { "group": "source" }, "to": { "group": "destination" }, "description": { "group": "description" } } }
      ],
      "template": {
        "text": "workspace \"{base}\" {\n\n    model {\n        {key} = softwareSystem \"{base}\"\n    }\n\n    views {\n        container {key} \"containers\" {\n            include *\n            autoLayout lr\n        }\n    }\n\n}\n"
      }
    }
  }
}
```

**Not FBL 0.2**, each marking where a gap is: `trailingComment` (C2), `blockComment` (C3), `escape` (C4), `pad` (C5), `unbalanced` (C6), `selectedViewOnly` (C1). The `exclude`, `title` and `autoLayout` statements follow `viewInclude`. Unsettled, and the design's to close with fixtures: whether two block rules may share one `view` namespace; what a view rule whose `key` group is absent is named; whether an element rule and a block rule that both match `container` are told apart by `within` alone; and whether `{key}` gives the factory's identifier (finding B23).

## Appendix: the definitions' persistence and wire ids, as drafted

The same in all six definitions, apart from the tools each offers. `Relationship` is read as an element with two reference attributes, so that a dangling one is kept (finding B8).

```json
{
  "persistence": {
    "format": "fbl",
    "binding": "https://raw.githubusercontent.com/etalii-adp/etalii.adp.ide.standalone/develop/src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/structurizr.fbl#workspace",
    "ids": { "strategy": "natural", "stable": true, "pattern": "^[A-Za-z_][A-Za-z0-9_]*$", "suffix": "{n}", "compare": "ignore-case", "missing": "ephemeral" },
    "view": { "store": ["bounds"] },
    "typeMap": {
      "Workspace": { "as": "diagram", "attributes": { "name": "workspaceName" } },
      "ViewInclude": { "as": "diagram", "attributes": { "members": "includes" } },
      "IncludedFile": { "as": "IncludedFile" },
      "Interaction": { "as": "Interaction", "attributes": { "from": "source", "to": "target" } },
      "Relationship": { "as": "Relationship", "attributes": { "from": "source", "to": "target" } }
    }
  },
  "x-c4": {
    "tools": {
      "person": { "id": "c4.toolbox.person", "drop": "c4.add-person" },
      "softwareSystem": { "id": "c4.toolbox.softwaresystem", "drop": "c4.add-softwaresystem" },
      "deploymentNode": { "id": "c4.toolbox.deploymentnode", "drop": "c4.add-deploymentnode" }
    },
    "actions": {
      "editLabel": "c4.rename", "editDescription": "c4.edit-description", "setTechnology": "c4.set-technology",
      "Relationship/editLabel": "c4.relabel", "setProtocol": "c4.set-protocol"
    },
    "properties": {
      "name": "c4.name", "description": "c4.description", "technology": "c4.technology", "tags": "c4.tags",
      "Kind": "c4.kind", "identifier": "c4.identifier",
      "relationshipDescription": "c4.relationship.description", "relationshipTechnology": "c4.relationship.technology",
      "From": "c4.relationship.source", "To": "c4.relationship.destination"
    }
  }
}
```

Three of the eight tools are shown; the other five follow the same shape, with the kind in lower case. The toolbox names an add action for each of the eight kinds, and the module answers four; dropping one of the other four is refused with "Adding a deployment node from the toolbox is not supported yet." and its like, which the definitions state as refusals. The synthetic ids `c4:view` and `boundary:<id>` are the mapper's and stay code.

## Sources

- The module `src/diagrams/c4/` at `develop` `9a646009`: `C4Parser.cs`, `C4ParseState.cs`, `_Model/C4Tokens.cs`, `_Model/C4Line.cs`, `_Model/C4Document.cs`, `_Model/C4Element.cs`, `C4RuleSet.cs`, `C4Rules.cs`, the three providers, `Commands/`, `C4LayoutSidecar.cs`, `C4DocumentFactory.cs`, `C4SessionFactory.cs`, `C4Session.cs`, `C4ContextSourceResolver.cs`, `C4ElementMapper.cs`, `C4Layout.cs`, `Diagram.cs`, `ServiceCollection.AddC4.cs`, and `Fixtures/readme.md`. Line and file counts are from `git ls-files src/diagrams/c4` piped to `wc -l`.
- etalii-adp/etalii.adp at `develop` `da64ff0`: the six `definitions/diagrams/c4-*.dis` (the `"disl"` line of each; the top-level keys, `persistence`, `viewpoints` and rule list of `c4-container.dis`) and `c4-container.md`; `specifications/fbl/structurizr.fbl`; `specifications/disl/DISL-specification.md` (0.3 draft: *Changes from 0.1*, *Changes from 0.2*, sections 3.3, 3.5, 4.11.3, 8.2, 10, 11.2, 11.5 and D.2, and the row for `c4-container.dis` in its table of examples); `specifications/fbl/FBL-specification.md` (0.2 draft: sections 3 to 9, 11.5, 13 and 17).
- `src/backend/EtAlii.Adp.Specification.Fbl.Tests/RealFiles/divergences.json`, counted by binding and reason with a script, for findings B1 to B3; `ModuleCrossCheck.Tests.cs` and `Registrations.Tests.cs` for what the entries compare. Finding A5: a search of `src/backend/EtAlii.Adp.Specification.Disl` for `matchesGlob`, `"over"`, `diagram.views`, `ignore-case`, `"members"`, `location` and `imports`, and `Expressions/DislCelLibrary.cs` for the twelve declared methods.
- Finding A8: a search of every `.dsl` under `src/` for lines that begin with an element keyword. It also matches view declarations of the same keywords, so it shows that such elements occur and gives no count. Finding B15: a search of the 26 C4 registrations under `src/` for a line `layout:`, with no result. Finding B16: `Registration/LegacySidecar.cs`, whose position map uses `StringComparer.Ordinal`. Finding B24: a search of `src/` for `AddC4ViewCommand(`, found in `AddC4ViewCommand.cs`, `AddC4View.Tests.cs` and `C4Interop.Tests.cs`.
- The hype cycle graph module as the pattern: `definition/gartner-hype-cycle-graph.dis` for `persistence.typeMap` and `x-ghg`, and `src/diagrams/tools/bundle-disl.sh` for finding A6; `causal-loop-disl-fbl` and `timeline-disl-fbl` for the series, the survey and the shape of this document.
