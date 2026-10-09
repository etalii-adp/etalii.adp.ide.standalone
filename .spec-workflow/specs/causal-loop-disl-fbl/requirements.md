# Requirements Document

## Introduction

The user's request, verbatim (2026-10-09):

> Create spec workflow MCP specifications. One specification for converting one of the standalone diagram tools to FBL and DISL. i.e. analyse the current implementation, define the DISL and FBL and then write the specification on the refactoring., also focus on finding issues and aspects where the DISL and FBL languages do not yet cover everything.

**The reading taken** is one specification per diagram tool that is not yet fully on DISL and FBL, and this is the first of them: the **causal loop diagram** (`systems/causal-loop-diagram`, `.cld`). It is first because it is the one unconverted tool for which a DISL definition and an FBL example binding both exist already, so the analysis is a comparison of three things that each claim to describe the same tool, and every disagreement between them is a finding.

**What "converted" means here** is what it means for the hype cycle graph today: the module's toolbox, context menus, property rows, findings and refusals are derived from a bundled DISL definition by `EtAlii.Adp.Specification.Disl`, its body is read and written through an FBL binding by `EtAlii.Adp.Specification.Fbl`, and the module keeps code only for what neither language can state. Nothing a user sees or a file holds changes unless this document names the change and the user has ruled on it.

**The series.** A survey of `src/diagrams/` at `develop` `9a646009` found 66 module folders: 48 are 17-line stubs, 18 are implemented. Of the 18:

| State | Tools |
| --- | --- |
| On DISL and FBL | hype cycle graph, agent behavior modelling, mind map |
| On DISL, body still read and written by the module's own parser and writer | timeline, dependency graph, .NET dependency graph |
| On neither | ansible structure, Azure DevOps pipeline, C4, **causal loop diagram**, Databricks, functional decomposition graph, Helm chart, RDF, Sankey, SPARQL, supply chain, Wardley map |

So fifteen tools need a specification like this one. The stubs need none: there is nothing to convert.

## What was measured

Read on 2026-10-09: the module at `develop` `9a646009`; `definitions/diagrams/causal-loop-diagram.dis` and `.md`, `specifications/fbl/causal-loop-diagram.fbl`, the DISL specification (0.3 draft) and the FBL specification (0.2 draft) in etalii-adp/etalii.adp at `develop` `da64ff0`; and the two libraries in this repository.

| Thing | State |
| --- | --- |
| The module's backend | 5,223 lines of C# in 56 files. `CausalLoopParser` (209 lines) and `CausalLoopWriter` (583) read and write the body; `CausalLoopValidator` (177), `CausalLoopContextActionProvider` (462), `CausalLoopContextPropertyProvider` (397) and `CausalLoopToolboxProvider` (42) hold the findings, menus, rows and palette as code. 22 test files. |
| The module's client | 925 lines. `CausalLoopCanvas.tsx` holds a hand-written canvas definition. |
| The DISL definition | Exists in etalii.adp, 684 lines, **written against DISL 0.1**. It is not bundled here (`src/diagrams/causal-loop-diagram/definition/` does not exist). It declares `persistence.format` as a plugin, not as FBL. |
| The FBL binding | Exists in etalii.adp as an example, 67 lines, `lines` family, and is vendored unchanged in `EtAlii.Adp.Specification.Fbl.Tests/Conformance/`. Nothing names it from the definition. |
| The real-file check of the binding | `ModuleCrossCheck.Tests` already reads the four `.cld` files of this repository through the binding and compares element ids per type with `CausalLoopParser`. They agree. One divergence is recorded, on the registrations (finding B10). |
| A parity transcript | None. The six converted modules each have a `Parity/` folder with a frozen transcript; this module has not. |

**How each finding below is known** is marked, because none of them was run: *read* is a reading of the code or the definition against the specification text; *recorded* is an entry of `RealFiles/divergences.json`, which a test holds; *measured* is a search of this repository whose command is in *Sources*. Requirement 1 turns every *read* finding into a fixture before anything is changed, so that each is either seen to happen or struck.

## Findings

Three kinds are kept apart, because they are repaired in three different places:

- a **definition defect**: the DISL definition or the FBL binding says something the tool does not do. Repaired in the definition or the binding.
- a **runtime gap**: the language can state it and this repository's library does not implement it. Repaired in `EtAlii.Adp.Specification.Disl` or `.Fbl`.
- a **language gap**: DISL 0.3 or FBL 0.2 cannot state it. Reported to etalii-adp/etalii.adp; until it is answered the behaviour stays module code.

### A. The DISL definition against the module

| # | Finding | Kind | Known |
| --- | --- | --- | --- |
| A1 | The definition is DISL 0.1. The client's `parseDisl` refuses anything but 0.3, and every bundled definition here is 0.3. It must be rewritten at 0.3 before it can be bundled. | Definition defect | Read |
| A2 | Its companion document lists twelve things "DISL cannot express" that DISL 0.2 and 0.3 have since answered: enumerating cycles (`diagram.cycles`, `cyclesTruncated`), one finding per unclaimed cycle (`forEach`), the order of findings (`constraints.order`), the tool's own finding codes (`code`), a reason on an unavailable entry (`unavailable`), labels that change with state (a CEL Message), a count in a menu label, a toolbox entry dropped onto a variable (`drop.targets`), the empty-canvas sentence (`canvas.empty`), the mirrored loop marker (`flip`), the fields that parse `+`, `s`, `-`, `o` (`parse`), and Arrange's refusal sentences (`layout.refusals`). The definition still works around each. | Definition defect | Read |
| A3 | A link is declared `routing: "arc"` with `curvature: 0.2`. Since DISL 0.2 an `arc` is a circular arc; what the module draws, a quadratic curve whose control point sits 0.2 of the chord off its midpoint, left of the direction of travel, is `curved`. | Definition defect | Read |
| A4 | `persistence` names a plugin that reads and writes the body. With the binding it becomes `format: "fbl"` with a `typeMap`, and the keys DISL 11.2 forbids beside a binding (`newline`, `finalNewline`, `files`, `ordering`, `omitDefaults`) go. | Definition defect | Read |
| A5 | The definition gives no wire ids. The module's 14 action ids, 13 property ids, 3 toolbox ids and its element ids (`variable:<name>`, `link:<from>\|<to>`, `loop:<identifier>`) are what the client and every stored registration use. | Language gap, already ruled: wire ids stay an `x-` block (Peter, 2026-10-06, "Spec all but wire ids"). This definition gets an `x-cld` block. | Read |
| A6 | The standalone DISL runtime implements none of the graph functions the definition needs. `DislCelLibrary` declares twelve element and diagram methods (`isA`, `descendants`, `ancestors`, `childrenOfType`, `incomingOf`, `outgoingOf`, `positionIn`, `nodesOfType`, `relationsOfType`, `elementById`, `label`, `other`) and refuses at load a specification that calls anything else. `diagram.cycles` and `cyclesTruncated` are absent. | Runtime gap | Measured |
| A7 | The module reports nothing when two variables share a name. DISL's `std.duplicateId` would. Deriving the findings therefore adds a finding the tool does not give today, unless the definition switches it off. | Behaviour change to rule on (Q4) | Read |
| A8 | A loop whose label disagrees with its arrows is drawn with a **wavy** underline. DISL text decoration has `underline` and no wavy form. | Language gap | Read |
| A9 | The ring a document opens on has a seating order (a depth-first walk of the links in declaration order) and a radius rule. DISL names `circular` and defines neither; the definition carries them as `adp.ring.*` options no runtime is required to read. | Language gap (DISL D.2, open question 9) | Read |
| A10 | Choosing the cycle a loop already runs through succeeds and puts nothing on the undo stack. DISL makes every committed field one transaction and does not say that an unchanged value lands none. | Language gap | Read |
| A11 | A variable's width is measured on the backend from its label, and positions depend on it. DISL 0.3 limits `textMetric` to layout-time measurement; whether `autoSize: "fitWidth"` on the wire is such a measurement is not stated. | Language gap, to confirm in design | Read |
| A12 | The self-organizing layout, the viewport filter (what is sent as it comes into view), and the `.proto` payloads are the host's and stay code. This is not a gap: DISL names the layout as a plugin and has no streaming model on purpose. | None | Read |

### B. The FBL binding against the module's parser and writer

| # | Finding | Kind | Known |
| --- | --- | --- | --- |
| B1 | **Statements the module reads and the binding does not.** The parser splits a line into words and is lenient about each; the binding matches one regular expression per statement. `variable a Label` (an unquoted label), `variable "my id"` (a quoted identifier), a statement with leading whitespace, a loop whose name is not quoted, `S` and `O` as polarity, and a link's note written as a bare word are all read by the module. Under the binding the first four are `fbl.unbound-statement`, the fifth reads as no polarity, and the sixth is not read. | Definition defect (the binding is narrower than the tool) | Read |
| B2 | **The module's sentences for a line it cannot read are its own**: "'x' is not a statement this reading knows.", "A link reads 'link &lt;from> -> &lt;to>', optionally followed by…", "'weight=x' does not read as a weight.", "A variable needs an id of at least one character." FBL reports `fbl.unbound-statement` with one sentence and gives a rule no way to say why a statement of its keyword did not read. | Language gap | Read |
| B3 | **A link whose end names no variable.** The module keeps the link, does not draw it, and reports `causal-loop.dangling-link` once per missing end. FBL 5.4 creates no relation and reports `fbl.dangling-reference`. The hype cycle solved the same with DISL 0.3's `typeMap`: the binding reads the entry as an element with reference attributes and the definition maps them to the relation's ends. | Definition defect, with a known pattern | Read; the timeline's same case is recorded |
| B4 | **Removing a variable** removes the links and the loops that name it. The binding's `remove.cascade` lists `link` only, so a loop through a removed variable would stay and name nothing. | Definition defect | Read |
| B5 | **What an edit writes.** The module rewrites the whole statement in one canonical shape: an edited `s` link comes back as `+`, extra spaces collapse, words the grammar did not know are lost. FBL replaces the one value (`replace-value`), or inserts the one word (`insert-key`), and re-emits the line only when it must. The bytes after an edit therefore differ, and FBL's are the smaller change. | Behaviour change to rule on (Q1) | Read |
| B6 | **Where a new statement goes.** Drawing a link that closes loops writes the link and its loop claims together, after the last link. The binding places each new entry after the last of its own rule, so the claims would go after the last loop. Both put a first statement of a kind at the end; the module at the end of the file, FBL after the last entry, which differ when the file ends in comments or blank lines. | Behaviour change to rule on (Q1) | Read |
| B7 | **A loop has no id in the binding** (no `id` on the rule), so FBL addresses it by its place in the file; the module knows it as `loop:<identifier>`. | Definition defect | Read |
| B8 | **The header.** The module skips any line whose first word is `causal-loop`, wherever it is, and requires none. The binding checks that the first statement is one and reports `fbl.header-mismatch` when it is not: a finding the tool does not give today. | Behaviour change to rule on (Q4) | Read |
| B9 | **The template.** The binding's template is two variables `a` and `b` and one link. The module's new document is the *Births beget births* starter, which the definition's `starter` template also describes. Two sources say what a new document is and they disagree. | Definition defect | Read |
| B10 | **Positions are stored under the module's wire ids.** A registration's `layout:` block holds `variable:<name>`; the binding reads a variable's id as the bare name, so every stored position names no element and FBL would prune it as stale at the next write. | Definition defect; the id strategy is DISL's (`persistence.ids` with a `prefix`) | Recorded (`cld`, `registration-layout`, both example registrations) |
| B11 | **A weight is written by .NET's shortest round-trip form.** FBL writes ECMAScript's. They agree on ordinary values and differ in the exponent form of very large and very small ones. | Definition defect or none, to settle by test | Read |
| B12 | **A note with two candidates.** The module takes the last word that is nothing else as the note; an FBL `word` slot takes the first that matches. | Language gap, minor | Read |
| B13 | **A label may hold a quote through the property grid's label row.** The rename dialog refuses a quote; `SetVariableLabel` itself does not, and a label holding `"` writes a statement that reads back differently. The definition's `pattern` forbids it. This is a defect of the module the conversion would repair by accident. | Module defect, to fix on purpose with its own guard | Read |

### C. Language gaps, as they would be reported to etalii-adp/etalii.adp

1. **FBL: a reason for a statement that almost matched** (B2). A rule can say what it matches, not what a line that starts like it and fails should be told. Candidate: a `fallback` on a `lines` or `blocks` rule, a looser expression with a message, tried when no rule matched.
2. **FBL: a tolerant word grammar** (B1, B12). Free word order after an arrow is expressible with `word`; "any other word is the note, the last one winning" and "quotes are optional when the value has no space" are not, short of regular expressions that restate the tokenizer.
3. **FBL: placing several new entries of different rules as one group** (B6), if the user rules that the tool's present placement is to be kept.
4. **DISL: a wavy underline** (A8).
5. **DISL: the standard `circular` layout's seating order and radius** (A9).
6. **DISL: a committed field whose value did not change** (A10).
7. **DISL: whether a size sent on the wire is a layout-time measurement** (A11).

These are candidates. Requirement 10 says they are recorded with their evidence and reported, and never worked around silently.

## Open questions

Each is put to the user as a selection. The option marked **(default)** is what this document is written against until the user rules otherwise; a ruling that differs is a small amendment to the criteria named.

| # | Question | Options | Criteria affected |
| --- | --- | --- | --- |
| Q1 | When an edit changes one value of a statement, what is written? | **(default) Only that value**, as FBL does: an `s` link stays `s`, unknown words survive, and the parity transcript's recorded edit hunks are regenerated once and reviewed as a diff. · **The whole statement in canonical form**, as today: the transcript stays byte-identical, and FBL needs a new way to force a re-emit, which is a language change in etalii.adp first. · Other. | 5.2, 5.3, 1.4 |
| Q2 | The lenient spellings the parser reads today (finding B1): keep reading them? | **(default) Yes**: the binding is widened until every statement the parser reads is read the same, so no file that opens today opens differently. · **No**: the binding's stricter grammar becomes the format, and such a line is reported as unreadable; every `.cld` in this repository already satisfies it. · Other. | 4.2 |
| Q3 | Stored positions are keyed `variable:<name>` (finding B10). | **(default) Keep the keys**: the definition's id strategy gives a variable that id, and no registration changes. · **Migrate**: ids become the bare name and every registration is rewritten once on open. · Other. | 6.1 |
| Q4 | Findings the languages would add that the tool does not give today (a missing header, two variables of one name). | **(default) Switched off in this conversion**, so the findings before and after are the same list, and each is raised afterwards as a small change of its own. · **Adopted now**, each named in the transcript's diff. · Other. | 3.3 |
| Q5 | Is the client's canvas definition in scope? | **(default) Yes**: it is compiled from the bundled definition with `compileNotation`, as the six converted modules do, with today's hand-written definition kept in a test as the oracle. · **No**: backend only, and the client keeps its hand-written definition until a later specification. · Other. | 8 |

## Alignment with Product Vision

The product direction (2026-09-26) is that every tool is a markup definition interpreted by a core in each IDE, with code only where a definition cannot reach. Three tools prove it here. This one is the first that is converted from a definition and a binding written by somebody who was not converting it, so it tests the languages rather than the author. It follows `product.md`'s *Don't reinvent, integrate* and `structure.md`'s rule that a diagram type adds declarations and no core code: what the libraries lack (finding A6) is added to the libraries for every tool.

## Requirements

### Requirement 1: An oracle before any change

**User Story:** As the owner of the tool, I want what it answers today frozen before anything is converted, so that every later step is held to it rather than to somebody's memory of it.

#### Acceptance Criteria

1. WHEN the work starts THEN a parity transcript `Parity/causal-loop.transcript.json` SHALL be generated from today's hand-written code and committed before any provider, parser or writer is changed, in the shape of the timeline's: per document, the context menu of every element, of the empty canvas, of a placement, of a connect gesture and of an unknown id; the property rows of every element; the findings; the payload of every mapped element; and a scripted edit sequence with each step's answer and the hunks it made to the text.
2. The corpus SHALL be the module's `examples/`, the copies under `src/examples/diagrams/causal-loop-diagram/`, and **one new fixture for each finding of table B and for A7 marked Read**, written so that the finding either shows in the transcript or is struck from this document with the reason.
3. A test SHALL require today's code to reproduce the checked-in transcript byte for byte, and SHALL have been seen to fail against a deliberately changed sentence before it is trusted.
4. WHEN a later step changes the transcript THEN the change SHALL be one this document names (Q1, Q4), regenerated on purpose, and the diff of the checked-in file SHALL be the review of it. Any other difference is a regression.

### Requirement 2: The definition, at DISL 0.3, bundled

**User Story:** As a tool engineer, I want one definition of the causal loop diagram that every host runs, so that the standalone tool is the definition's and not a second opinion.

#### Acceptance Criteria

1. `definitions/diagrams/causal-loop-diagram.dis` in etalii-adp/etalii.adp SHALL be rewritten at DISL 0.3, using the constructs findings A2 to A4 name in place of each workaround, and SHALL validate against the DISL schema in that repository's checks.
2. It SHALL declare `persistence.format` `fbl` with `binding` naming the module's binding (Requirement 4.1) and a `typeMap` (finding B3), and an `x-cld` block mapping its tools, actions and properties to the wire ids the module uses today (finding A5).
3. Its companion `causal-loop-diagram.md` SHALL be rewritten to say what DISL 0.3 cannot express, which is findings A8 to A11 and whatever Requirement 10 adds, and nothing that 0.3 can.
4. The definition SHALL be bundled into `src/diagrams/causal-loop-diagram/definition/` by `src/diagrams/tools/bundle-disl.sh`, never edited here, embedded in the backend project, and held by a `BundledDefinition.Tests` like the other bundled modules'.
5. WHEN the bundled definition is loaded by `EtAlii.Adp.Specification.Disl` THEN it SHALL load with no diagnostic of severity error.

### Requirement 3: Toolbox, menus, rows and findings are derived

**User Story:** As a maintainer, I want the palette, the context menus, the property rows and the findings to come from the definition, so that changing the tool is changing one file.

#### Acceptance Criteria

1. The toolbox, the context menu of every target, the property rows of every element, the deletion confirmation and the reason on every unavailable action SHALL be derived from the bundled definition through `ToolboxDerivation`, `ContextMenuDerivation` and `FormDerivation`, mapped to the wire ids of `x-cld`, and the providers SHALL hand them out and compute none themselves.
2. Every sentence a user reads today SHALL be read unchanged: labels that follow state ("Delayed" and "Not delayed", "Flip the curve" and "Curve back the other way"), "Remove variable (with 3 references)", "This link already states +.", the read-only reasons of a link's ends and of the computed polarity, and the stated-polarity row that appears only on a disagreement.
3. The seven findings SHALL be derived by `ConstraintEvaluator` with the tool's codes (`causal-loop.unreadable-line` to `causal-loop.cycle-bound-reached`), severities, sentences, line locations and order, one finding per unclaimed cycle; and no finding the tool does not give today SHALL appear (Q4).
4. WHEN the derived answers are compared with the transcript of Requirement 1 THEN the menus, rows, findings and payloads SHALL be byte-identical.
5. The hand-written providers' logic SHALL be kept in the test project as the oracle (`Parity/HandWrittenCausalLoop.cs`), as the hype cycle keeps `HandWrittenGhg`, and removed from the product code.

### Requirement 4: The body is read through the binding

**User Story:** As a user, I want every `.cld` that opens today to open the same, read by the binding instead of by a parser only this host has.

#### Acceptance Criteria

1. The module SHALL own its binding, `backend/EtAlii.Adp.Diagram.CausalLoopDiagram/causal-loop-diagram.fbl`, embedded in the backend project, as the hype cycle owns its own. The example in etalii.adp SHALL be brought in line with it or the difference recorded (Requirement 10).
2. WHEN a body is read THEN the variables, links and loops the binding reads SHALL be those `CausalLoopParser` reads today, for every document of the corpus of Requirement 1.2, including the lenient spellings of finding B1 (Q2).
3. A link whose end names no variable SHALL be kept, not drawn, and reported as `causal-loop.dangling-link` with today's sentence, once per missing end (finding B3).
4. A statement that cannot be read SHALL be kept byte for byte and reported as `causal-loop.unreadable-line` on its line. WHERE FBL cannot give the tool's own sentence for it (finding B2) THEN the sentence SHALL come from module code that reads the line FBL reports, and that code SHALL be named in the definition's companion document as standing in for a language gap.
5. A loop SHALL have the id `loop:<identifier>` and a link `link:<from>|<to>` (finding B7), stated by the definition's id strategy and the binding together, with no id computed in module code.
6. Reading SHALL never throw on content, and a body that cannot be read at all SHALL open empty and read-only with today's sentence, "This causal loop diagram could not be read, so nothing can be edited until it is fixed."

### Requirement 5: Every edit is written by splices

**User Story:** As a user editing a file I also edit by hand, I want an edit to change what I changed and nothing beside it.

#### Acceptance Criteria

1. Every command of the module SHALL write through `EtAlii.Adp.Specification.Fbl` as a model change planned into splices; no module code SHALL build a statement's text or replace a line.
2. WHEN one value of a statement changes THEN only that value's bytes SHALL change (Q1): the spelling of an untouched polarity, the spacing and any word the binding does not bind SHALL survive.
3. A new statement SHALL be placed as the binding declares; WHERE that differs from today's placement (finding B6) THEN the difference SHALL be in the regenerated transcript and named in the pull request.
4. Renaming a variable's identifier SHALL rewrite every link and loop that names it in the same edit, and removing a variable SHALL remove the links and the loops that name it (finding B4), each as one undo step.
5. Drawing a link SHALL claim, in the same edit, every decidable cycle it closes that no loop names, numbered as today, by the definition's hook and not by module code.
6. Every refusal SHALL keep today's sentence: an unusable name, a name already declared, a link already stated, a loop identifier already used, and the three "nothing to change" sentences.
7. Undo SHALL restore the body byte for byte, as today, through the host's shared restore command.
8. A document ADP did not change SHALL be written back byte-identical.
9. A label holding a double quote SHALL be refused from every path that sets one, with a test seen to fail against today's property-grid path first (finding B13).

### Requirement 6: Positions and the registration

**User Story:** As an author who arranged a diagram, I want it to open where I left it after the conversion.

#### Acceptance Criteria

1. Every position stored in a registration today SHALL be applied after the conversion, with no registration rewritten to make it so (Q3), and the `cld` entries of `RealFiles/divergences.json` SHALL be removed because they no longer occur.
2. Moving a variable and Arrange diagram SHALL write the registration's `layout:` block through the library's registration writer, each as one undo step that restores the registration byte for byte.
3. A document opened without a registration SHALL refuse a move and an arrangement with today's sentences.

### Requirement 7: What stays code, and why

**User Story:** As a maintainer, I want the code that remains in the module to be exactly what the languages cannot state, each piece with its reason.

#### Acceptance Criteria

1. The module SHALL keep: the self-organizing layout and the ring layout (finding A9), the element mapper and the viewport filter, the session and the document store, the `.proto` payloads, and the stand-in of Requirement 4.4.
2. `CausalLoopParser`, `CausalLoopWriter`, `CausalLoopValidator` and the logic of the three providers SHALL leave the product code. What Requirement 3.5 keeps as an oracle lives in the test project.
3. WHEN the conversion is complete THEN the definition's companion document SHALL list every remaining module class beside the gap or the host concern that keeps it, and a class with neither SHALL be removed.
4. Cycle enumeration SHALL be the library's `diagram.cycles` (Requirement 9.1), not the module's `CycleFinder`, once the library's is shown to return the same cycles in the same order for the corpus.

### Requirement 8: The client

**User Story:** As a user, I want the canvas to look and behave as it does, drawn from the same definition the backend runs.

#### Acceptance Criteria

1. The client's canvas definition SHALL be compiled from the bundled definition with `parseDisl` and `compileNotation` (Q5), with what the library cannot read from it stated in a `NotationBindings`.
2. Today's hand-written definition SHALL be kept in a test as the oracle, and the compiled one SHALL draw the same markup for the corpus.
3. IF `compileNotation` cannot read something the definition states (the quadratic curve and its flipped variant, the self-link, the delay strokes, the polarity mark, the weight ladder, a node with no body placed at the centre of its members) THEN it SHALL be added to the shared library for every module, named for its geometry, and SHALL NOT be written in this module.
4. A `tests.md` entry SHALL cover in a real browser, in both themes: the arcs of a two-variable loop bowing apart, a flipped link, a self-link, the delay strokes, the polarity marks and none on an unstated link, the three weights, the loop badge turning each way and its colours, the wavy underline on a disagreeing loop, dropping each of the three toolbox entries, inline rename of a label, and Arrange diagram with its undo.

### Requirement 9: The library work this tool needs

**User Story:** As a maintainer of the shared libraries, I want each thing this tool needs added once, for every tool.

#### Acceptance Criteria

1. `EtAlii.Adp.Specification.Disl` SHALL implement `diagram.cycles` and `cyclesTruncated` as DISL 12.4 defines them, bounded, in a stable order, and every other function of DISL 12.4 that the rewritten definition calls and `DislCelLibrary` lacks (finding A6). Each SHALL be proven by a library test seen to fail first.
2. `ConstraintEvaluator`, `ToolboxDerivation`, `ContextMenuDerivation` and `FormDerivation` SHALL support the constructs of finding A2 that they do not support yet; the design SHALL list which, from a load of the rewritten definition.
3. `EtAlii.Adp.Specification.Fbl` SHALL need no change for this tool unless a fixture of Requirement 1.2 shows a defect in it; a language gap is not repaired here (Requirement 10).
4. No module name SHALL appear in a library change.

### Requirement 10: Gaps are recorded and reported, never tuned away

**User Story:** As the owner of DISL and FBL, I want every place the languages fall short reported back with its evidence, so that the languages are fixed where they are written.

#### Acceptance Criteria

1. Every language gap found, the seven of section C and any the implementation adds, SHALL be recorded in the definition's companion document with the construct concerned, the evidence (a fixture or a test), and what would resolve it.
2. A gap SHALL NOT be closed by weakening a check, skipping a fixture, or stating in the definition something the tool does not do.
3. The delivery report SHALL list every gap as a candidate change for etalii-adp/etalii.adp, and every definition defect that was repaired.
4. WHEN a *read* finding of this document turns out not to occur THEN it SHALL be struck here with the fixture that shows it, as a small amendment.

### Requirement 11: Documentation, catalog and gates

**User Story:** As a reader of the repository, I want its pages to stay true through the conversion.

#### Acceptance Criteria

1. `src/diagrams/readme.md`'s list of modules with a `definition/` folder, `docs/creating-a-diagram-module.md`, `docs/diagram-module-client-api.md`'s list of modules that compile their notation, and `docs/architecture.md` and `docs/solution-structure.md` where a sentence becomes false SHALL be updated in the change that makes them so.
2. `docs/tools.md`'s row for `systems/causal-loop-diagram` SHALL name this specification.
3. All four gates SHALL exit zero on every pull request of this specification, judged by captured exit codes, and a newly created worktree SHALL be built before a green gate is trusted about the bundled definition.
4. WHEN the tasks document is written THEN every acceptance criterion here SHALL be claimed by a task, established by diffing the two sets, and two traces SHALL be read back to their artefacts.

## Non-Functional Requirements

### Code Architecture and Modularity

- The module ends as declarations, a binding, and the code Requirement 7.1 names. Library additions are named for what they do, never for this tool.
- The house rules apply: `src/.editorconfig`, no blocking call in an `async` method, `TestContext.Current.CancellationToken` in tests, no unneeded `using`, CRLF in the working tree; `.dis` and `.cld` are byte-compared and already `-text`, and `.fbl` in the module follows what the hype cycle's has.

### Performance

- Opening, validating and editing the largest `.cld` of the corpus SHALL not be perceptibly slower than today. Cycle enumeration stays bounded at 1,000 cycles, and the bound is reported when reached.

### Reliability

- No file that opens today opens differently (Requirement 4.2). No stored position is lost (Requirement 6.1). An unchanged document is written back byte-identical (Requirement 5.8). Undo restores bytes (Requirement 5.7).
- Every test written for a finding is seen to fail against that finding before it is trusted.

### Usability

- Every sentence a user reads today is read unchanged (Requirements 3.2, 5.6), unless this document names the change.

## Out of scope

- Any change to what the tool does beyond the five questions above and finding B13.
- Changing DISL or FBL. Gaps are reported (Requirement 10); a language change is etalii.adp's own specification.
- The other fourteen tools. Each gets its own specification; what this one adds to the libraries (Requirement 9) is theirs to use.
- The self-organizing layout's algorithm and the viewport streaming model.

## Appendix: the binding, as drafted

A draft against FBL 0.2, to be settled in the design. It differs from the example in etalii.adp where findings B1, B3, B4, B7 and B9 say so: links are read as elements with reference attributes so that a dangling one is kept, the regular expressions admit the lenient spellings, a loop has an id, removing a variable cascades to loops, and the template is the tool's starter. Finding B2 has no place in it: that is the language gap.

```json
{
  "fbl": "0.2",
  "bindings": {
    "cld": {
      "title": "Causal loop diagram",
      "claims": { "extensions": [".cld"], "origins": ["systems/causal-loop-diagram"] },
      "body": { "kind": "file", "family": "lines" },
      "reader": "declared",
      "text": { "newline": "crlf", "finalNewline": true },
      "comment": "^\\s*#",
      "unmatched": "report",
      "elements": [
        {
          "name": "header", "type": "Header",
          "line": "^\\s*causal-loop(\\s.*)?$"
        },
        {
          "name": "variable", "type": "Variable",
          "line": "^\\s*variable\\s+(?<name>\"[^\"]+\"|[^\\s\"]+)(?:\\s+(?<label>\"[^\"]*\"|[^\\s\"]+))?(?<rest>.*)$",
          "id": { "from": { "group": "name" } },
          "attributes": {
            "name": { "group": "name", "empty": "refuse" },
            "label": { "group": "label", "empty": "remove" }
          },
          "insert": { "place": "after-last", "emit": "variable {name}[ \"{label}\"]" },
          "remove": { "cascade": ["link", "loop"] }
        },
        {
          "name": "link", "type": "Link",
          "line": "^\\s*link\\s+(?<from>\"[^\"]+\"|[^\\s\"]+)\\s+->\\s+(?<to>\"[^\"]+\"|[^\\s\"]+)(?<tail>(?:\\s+(?:\"[^\"]*\"|[^\\s\"]+))*)\\s*$",
          "attributes": {
            "from": { "group": "from", "reference": { "to": ["variable"], "by": "name" } },
            "to": { "group": "to", "reference": { "to": ["variable"], "by": "name" } },
            "polarity": { "group": "tail", "word": "^[+\\-sSoO]$", "map": { "+": "positive", "s": "positive", "S": "positive", "-": "negative", "o": "negative", "O": "negative" } },
            "delayed": { "group": "tail", "word": "^delayed$", "flag": true },
            "flipped": { "group": "tail", "word": "^flipped$", "flag": true },
            "weight": { "group": "tail", "word": "^weight=(?<value>.+)$", "number": "shortest", "empty": "remove" },
            "note": { "group": "tail", "word": "^\".*\"$", "empty": "remove" }
          },
          "insert": { "place": "after-last", "emit": "link {from} -> {to}[ {polarity}][ {delayed}][ {flipped}][ weight={weight}][ \"{note}\"]" },
          "remove": {}
        },
        {
          "name": "loop", "type": "Loop",
          "line": "^\\s*loop\\s+(?<identifier>[^\\s\"]+)\\s+(?<title>\"[^\"]*\"|[^\\s\"]+)(?<members>(?:\\s+[^\\s\"]+)+)\\s*$",
          "id": { "from": { "group": "identifier" } },
          "attributes": {
            "identifier": { "group": "identifier", "empty": "refuse" },
            "title": { "group": "title", "empty": "keep" },
            "members": { "group": "members", "reference": { "to": ["variable"], "by": "name" } }
          },
          "insert": { "place": "after-last", "emit": "loop {identifier} \"{title}\" {members}" },
          "remove": {}
        }
      ],
      "template": {
        "text": "causal-loop 1\r\n\r\nvariable population \"Population\"\r\nvariable births \"Births\"\r\n\r\nlink population -> births +\r\nlink births -> population +\r\n\r\nloop R1 \"Births beget births\" population births\r\n"
      }
    }
  }
}
```

Three things in it are known to be unsettled and are the design's to close with fixtures: whether a quoted group reads without its quotes outside a `word` slot; that the bare-word note of finding B1 is still not read by the `note` slot; and that the header is read as an entry of its own, so that a missing one raises no finding (Q4), which the definition's `typeMap` then maps to `header`.

## Appendix: the definition's persistence and wire ids, as drafted

The rest of the definition is the existing file brought to 0.3 (Requirement 2.1). These two blocks are new.

```json
{
  "persistence": {
    "format": "fbl",
    "binding": "https://raw.githubusercontent.com/etalii-adp/etalii.adp.ide.standalone/develop/src/diagrams/causal-loop-diagram/backend/EtAlii.Adp.Diagram.CausalLoopDiagram/causal-loop-diagram.fbl#cld",
    "ids": { "strategy": "natural", "stable": true, "prefix": { "Variable": "variable:", "CausalLink": "link:", "Loop": "loop:" } },
    "view": { "store": ["bounds"] },
    "typeMap": {
      "Header": { "as": "header" },
      "Variable": { "as": "Variable" },
      "Link": { "as": "CausalLink", "attributes": { "from": "source", "to": "target" } },
      "Loop": { "as": "Loop" }
    }
  },
  "x-cld": {
    "tools": {
      "variable": { "id": "causal-loop.toolbox.variable", "drop": "causal-loop.add-variable" },
      "link": { "id": "causal-loop.toolbox.link", "drop": "causal-loop.add-link" },
      "loop": { "id": "causal-loop.toolbox.loop", "drop": "causal-loop.add-loop" }
    },
    "actions": {
      "arrange": "causal-loop.arrange",
      "connect": "causal-loop.connect",
      "claimLoop": "causal-loop.add-loop",
      "Variable/editLabel": "causal-loop.rename-variable",
      "Variable/delete": "causal-loop.remove-variable",
      "makePositive": "causal-loop.make-positive",
      "makeNegative": "causal-loop.make-negative",
      "toggleDelay": "causal-loop.toggle-delay",
      "flipCurve": "causal-loop.flip-curvature",
      "CausalLink/delete": "causal-loop.remove-link",
      "renameLoop": "causal-loop.rename-loop",
      "Loop/delete": "causal-loop.remove-loop"
    },
    "properties": {
      "label": "causal-loop.variable-label",
      "name": "causal-loop.variable-id",
      "Cause (from)": "causal-loop.link-from",
      "Effect (to)": "causal-loop.link-to",
      "polarity": "causal-loop.link-polarity",
      "delayed": "causal-loop.link-delayed",
      "weight": "causal-loop.link-weight",
      "note": "causal-loop.link-label",
      "identifier": "causal-loop.loop-identifier",
      "title": "causal-loop.loop-name",
      "Computed polarity": "causal-loop.loop-computed",
      "Stated polarity": "causal-loop.loop-stated",
      "members": "causal-loop.loop-variables"
    }
  }
}
```

The toolbox ids are those of `CausalLoopToolboxProvider`. A link's natural id joins its ends with `|` after the prefix, which is DISL 11.5's composition and the module's `link:<from>|<to>`.

## Sources

- The module `src/diagrams/causal-loop-diagram/` at `develop` `9a646009`: `CausalLoopParser.cs`, `CausalLoopWriter.cs`, `CausalLoopDocument.cs`, `CausalLoopValidator.cs`, the three providers, `Commands/`, `client/CausalLoopCanvas.tsx`.
- etalii-adp/etalii.adp at `develop` `da64ff0`: `definitions/diagrams/causal-loop-diagram.dis` and `.md`; `specifications/fbl/causal-loop-diagram.fbl`; `specifications/disl/DISL-specification.md` (0.3 draft: *Changes from 0.1*, *Changes from 0.2*, sections 7.2, 8.2, 11.2, 11.5, 12.4, D.2); `specifications/fbl/FBL-specification.md` (0.2 draft: sections 4.6, 5.1 to 5.7, 6.1 to 6.3).
- `src/backend/EtAlii.Adp.Specification.Disl/Expressions/DislCelLibrary.cs` for finding A6, searched for `cycles`, `reachable`, `inCycle` and `successors` across the project with no result outside the twelve declared methods.
- `src/backend/EtAlii.Adp.Specification.Fbl.Tests/RealFiles/divergences.json` and `ModuleCrossCheck.Tests.cs` for finding B10 and for the agreement on the four real files.
- The hype cycle graph module as the pattern: `GhgDefinition.cs`, `GhgBody.cs`, its `Parity/` folder, and `definition/gartner-hype-cycle-graph.dis` for `persistence.typeMap` and `x-ghg`.
- The survey: a count of C# and TypeScript lines per module folder from `git ls-files src/diagrams`, with the presence of `definition/*.dis` and `*.fbl`.
- The `fbl-implementation` and `gartner-hype-cycle-graph` specifications for the shape of this document.
