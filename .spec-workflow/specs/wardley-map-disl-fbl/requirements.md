# Requirements Document

## Introduction

The user's request, verbatim (2026-10-09):

> Create spec workflow MCP specifications. One specification for converting one of the standalone diagram tools to FBL and DISL. i.e. analyse the current implementation, define the DISL and FBL and then write the specification on the refactoring., also focus on finding issues and aspects where the DISL and FBL languages do not yet cover everything.

This is one of the fifteen specifications that reading gives (the series and the survey behind it are in `causal-loop-disl-fbl`, *Introduction*). It converts the **Wardley map** (`wardley/map`, `.owm`, the OnlineWardleyMaps text format).

**What "converted" means here** is what it means for the hype cycle graph today: the toolbox, context menus, property rows, findings and refusals are derived from a bundled DISL definition by `EtAlii.Adp.Specification.Disl`, the body is read and written through an FBL binding by `EtAlii.Adp.Specification.Fbl`, and the module keeps code only for what neither language can state. Nothing a user sees or a file holds changes unless this document names the change and the user has ruled on it.

**Three things make this tool unlike the others of the series.** Its elements have no id in the body, so FBL 5.3 gives it the sidecar id ("a Wardley map's components are named, not identified") and FBL 8.7 names its `{base}.identities.json`. Its positions are in the body itself (`component Name [visibility, maturity]`), so a drag is a body edit and the registration holds no layout. And the format belongs to another ecosystem: every fixture was certified against the real OnlineWardleyMaps parser, so a reading the binding cannot give is a file somebody else's tool wrote. The first of the three is where FBL's own text describes this tool's file wrongly (finding B8), and the third is where the `blocks` family cannot read the form the format is written in (finding B1).

## What was measured

Read on 2026-10-09: the module at `develop` `9a646009`; `definitions/diagrams/wardley-map.dis` and `.md`, the DISL specification (0.3 draft) and the FBL specification (0.2 draft) in etalii-adp/etalii.adp at `develop` `da64ff0`; and the two libraries in this repository.

| Thing | State |
| --- | --- |
| The module's backend | 6,446 lines of C# in 55 files. `WardleyParser` (681 lines) and `WardleyWriter` (371) read and write the body; `WardleyRuleSet` (253) and `WardleyValidator` (100), `WardleyContextActionProvider` (618), `WardleyContextPropertyProvider` (522) and `WardleyToolboxProvider` (94) hold the findings, menus, rows and palette as code; `WardleyIdentities` (234) and `WardleyIdentityKeys` (99) hold the ids. 24 test files, 13 `.owm` fixtures. |
| The module's client | 1,333 lines outside tests. `WardleyCanvas.tsx` (671) is a hand-written canvas. |
| The DISL definition | Exists in etalii.adp, 818 lines, **written against DISL 0.1**. It is not bundled here (`src/diagrams/wardley-map/definition/` does not exist). It declares `persistence.format` as a plugin. |
| The FBL binding | **None.** `specifications/fbl/` has eight example bindings and none for this tool, although FBL section 17 lists the Wardley map among the formats that get "a declared binding". The appendix drafts one. |
| The real-file check | None. `ModuleCrossCheck.Tests` and `RealFiles/divergences.json` have no Wardley entry, because there is no binding to check. |
| A parity transcript | None. The module's tests have no `Parity/` folder. |
| The fixtures | 13 in the test project and 29 `.owm` files in the two example trees, each certified against the parser vendored by `cli-owm` 0.0.2 (`Fixtures/readme.md`). No fixture holds a name with a hyphen, a plus, a slash or a bracket, or two links or two notes of one natural key. |

**How each finding is known:** *recorded* is a fixture, a test or a sentence in the module's code that already says so; *measured* is a search of this repository whose command is in *Sources*; *read* is a reading of the code against the specification text, not run. Nothing was built or run for this document. Requirement 1 turns every *read* finding into a fixture before anything is changed.

## Findings

The three kinds are those of `causal-loop-disl-fbl`: a **definition defect** is repaired in the definition or the binding, a **runtime gap** in this repository's libraries, a **language gap** in etalii-adp/etalii.adp. A fourth appears here more than elsewhere: a **module defect**, where the tool itself does something its own code says it does not.

### A. The DISL definition against the module

| # | Finding | Kind | Known |
| --- | --- | --- | --- |
| A1 | The definition is DISL 0.1 (`"disl": "0.1"`). The client's `parseDisl` refuses anything but 0.3. It must be rewritten at 0.3 before it can be bundled. | Definition defect | Read |
| A2 | Its companion document, section 11, lists twelve things "DISL 0.1 lacks for this type". **All twelve have since been answered**: the text format with a byte-preserving round trip (`persistence.format: "fbl"`), ids held outside the document (FBL `id.sidecar`), references by name that may dangle (FBL `reference` with `typeMap`, DISL 0.3 change 4), named unequal ranges on a linear axis (`axis.ranges`), end labels (`endLabels`), a canvas that stretches (`fit: "stretch"`), a label offset in the model (a label `offset` bound to an attribute, 6.12), labels that follow state and a confirmation that names the element (a CEL Message), an entry answered by a picker (`target: "pick"` with `candidates`), a reason on a read-only row (`readOnlyReasons`), a rule that consults the file system (`fs.exists`), and clamping a drag while refusing a typed value (`outOfRange`). The DISL text uses the Wardley map as its own example for four of them. The definition still works around each. | Definition defect | Read |
| A3 | Three shapes of the draft's metamodel cannot be read from the body by any binding. `urls` is a diagram attribute holding a list, and each `url name [address]` statement is an entry, so it has to be a node type of its own. `decorators` is one list attribute, and the body stores it as up to five separate words (finding B6). `AnnotationPin` is a child element, and an annotation's occurrences are numbers inside one statement, not entries (finding B7). | Definition defect | Read |
| A4 | The definition gives no wire ids. The module has 22 action ids (17 named and five of the form `wardley.toggle-<decorator>`), 16 property ids, 8 toolbox ids, 8 finding codes and 7 element type ids, which the client and every test use. | Language gap, already ruled: wire ids stay an `x-` block (Peter, 2026-10-06). This definition gets an `x-wardley` block. | Measured |
| A5 | **DISL's own example for this tool disagrees with the tool.** Section 12.4 shows `wardley.submap-missing` as `fs.exists(self.url)` on a `Submap`. In the tool a submap's `url(name)` is the name of a definition, not an address; the rule runs over every `url` definition's address; the path is resolved against the project root (`request.RootPath`), where `fs.exists` resolves against the folder of the subject; and the sentence is "'X' points at 'address', which is not in this project." or "This map defines 'address', which is not in this project.", located on the definition's line. | Language gap (the base folder) and a defect of the specification's example | Read |
| A6 | The standalone runtimes implement none of the constructs A2 and A5 need for this tool. `DislCelLibrary` declares twelve element and diagram methods and a few value functions; `fs.exists` and `location()` are not among them. `ranges`, `endLabels`, `stretch` and `outOfRange` occur in no file of `EtAlii.Adp.Specification.Disl` or of `src/client/src` outside tests. | Runtime gap | Measured |
| A7 | `wardley.duplicate-name` is reported once per name, at the first holder, with the lines of all holders in its sentence ("'X' is declared more than once (lines 3, 7), so every link, evolve and pipeline that names it is ambiguous."). DISL's `oncePerGroup` reports at the second or the last holder, never the first. The module's location is also built from the element's **name** where an element id is expected (`new DiagramProblemElementLocation(WardleyIdentityKeys.Of(group.First()))`), so today it points at nothing. | Language gap (minor), and a module defect | Read |
| A8 | `wardley.coordinate-out-of-range` writes the value with the invariant pattern `0.####` ("The maturity of 'X' is 1.2, and a Wardley map's axes run from 0 to 1."). DISL's `formatNumber` is ICU formatting in `env.locale`, so the sentence would follow the user's locale. | Language gap (minor) | Read |
| A9 | An evolution is a node type in the definition and a property of its component in the tool: the rows `wardley.evolve` ("Evolves to") and `wardley.evolve-name` ("Becomes") sit on the component and add, rewrite or remove an `evolve` statement, and only the first `evolve` of a component is used. DISL can state rows that write another element (a form item's `parse.write` actions). The draft also adds a warning for a second `evolve`, which the tool does not give. | Definition defect, to confirm in design; the warning is a behaviour change (Q4) | Read |
| A10 | The element mapper and its viewport filter, the session, the document store with its reload rules, and the `.proto` payloads are the host's and stay code. The synthetic element `wardley/map+evolution-axis` goes once the client reads `axis.ranges`. This is not a gap. | None | Read |

### B. The FBL binding against the module's parser and writer

There is no binding, so this table compares the drafted one (appendix) with the code. Positions need no finding of their own: the two numbers are two groups of the statement, a drag is a `replace-value` on each, `number: {"decimals": 4}` rounds as the module's `0.####` does, and the registration's `layout:` block stays unused.

| # | Finding | Kind | Known |
| --- | --- | --- | --- |
| B1 | **A pipeline's brace is on the line below its statement.** The one fixture with pipelines writes `pipeline Kettle`, then `{` on its own line, and the module writes the same when it starts a pipeline. FBL 4.7 opens a block only for "a statement whose last non-whitespace character" is `{`, so the `{` line is a statement of its own that opens a block, and the pipeline statement above it is not its parent. Under `lines` there is no containment at all, and a pipeline component (`component Name [maturity]`) cannot be told from a malformed top-level statement. Neither family reads this format's one nested construct. | Language gap; it blocks the body half of the conversion (Q5) | Recorded (`pipelines-both-forms.owm`, certified; `SetWardleyPipelineMembershipCommandHandler`) |
| B2 | **Braces that do not balance.** The parser keeps the children of a pipeline that is never closed ("rather than losing them to a missing brace") and the map stays editable; only adding to that pipeline is refused. Under `blocks` such a body is unreadable: empty, read-only, one finding. The same family rule makes any statement that happens to end in `{` open a block: a title, a note's text, a link's context, or a trailing comment. | Language gap and behaviour change (Q4) | Read |
| B3 | **A comment may trail a statement.** The module cuts every line at the first `//` that is not preceded by a colon, then reads it. FBL's `comment` matches whole lines only (4.6). Each rule's expression therefore has to restate the cut; the subset has no lookbehind for "not after a colon"; a `word` slot over a trailing group reads words that stand inside the comment (`// (market)`); and a `re-emit-line` rewrites the statement's own span, which includes the comment, so it is lost. | Language gap | Recorded (`comments-everywhere.owm` holds the form); the consequences are read |
| B4 | **What follows the coordinates is read in any order and by search.** `label [dx, dy]`, `(decorator)`, `inertia` and `url(name)` are each found anywhere in the trailer, with spaces allowed inside the parentheses and brackets, and `inertia` matched by lookaround (`(?<![A-Za-z])inertia(?![A-Za-z])`). A `word` slot reads one whitespace-separated word, so `label [-30, 12]` (three words) and `( market )` are not words, and lookaround is outside the subset FBL 2.5 allows. A single `line` expression can only fix one order. | Language gap | Read |
| B5 | **A keyword claims its line even when the statement is malformed.** The parser never reads a line that starts with one of 21 keywords as a link, so `y_axis Value chain->Invisible->Visible` is not a link although it holds two arrows. FBL gives a statement to the first rule that matches, so the link rule would take it and report two missing ends. The draft answers with a catch-all rule before the link rules, mapped out of the model. | Definition technique, to settle by fixture | Recorded (`unmodelled-statements.owm`, and the comment on `KnownKeywords`) |
| B6 | **Decorators are a set stored as separate words.** FBL binds one attribute to exactly one place (3.3), and has no slot whose value is the set of words present. The binding needs five boolean attributes; the tool's one row `wardley.decorators` (a comma list) and its five toggles then read and write five attributes. An unknown decorator, `(someDecoratorFromLater)`, is unbound and must survive every edit. | Language gap, with a workaround | Read |
| B7 | **An annotation holds a list of positions in one statement**: `annotation 1 [[0.43,0.49],[0.08,0.79]] text`. A `lines` or `blocks` slot yields one string per group. The occurrences can be bound only as the text between the outer brackets, so `wardley.coordinate-out-of-range`, which the tool reports per occurrence, and the pins the canvas draws need the numbers parsed again in CEL or in code. | Language gap | Recorded (`annotations-and-labels.owm`) |
| B8 | **FBL describes this tool's identity file wrongly, and the library follows the text.** FBL 8.7: "`{base}.identities.json` for the Wardley map: a JSON object mapping a natural key to an id". The file the tool writes is a JSON **array** of entries, each `{"id", "kind", "key"}`. `LegacySidecar` treats anything that "is not one JSON object" as unreadable, "neither applied nor written". Read through the binding, every existing map would get new ids and keep a stale file beside it. | Language gap (the specification's text) and runtime gap | Measured (`annotations-and-labels.identities.json` against FBL 8.7 and `LegacySidecar.cs`) |
| B9 | **The key space.** The tool keys an id by kind and key together ("a component and a note can hold the same text"), with U+001F between the parts of a composite key. FBL 8.6 has one `identities:` block of `<natural key>: <id>` lines and does not say that keys are per rule, so each rule's `sidecar.key` must write the kind into the key itself. Two elements with one key (the same link stated twice, two notes with one text) get **the same id** from `Reconcile` and no finding; FBL gives the second `std.duplicateId` and an id that is not stored. An attitude's key holds two numbers written by .NET; CEL may write them otherwise. | Definition defect, a module defect, and a behaviour change (Q4) | Read |
| B10 | **A rename and the key.** `Rekey` moves the component's own entry only; its comment says the links move too ("renaming a component rekeys the links that reach it too"), and the code compares `entry.Key == oldKey`, which no link's composite key equals. So a rename gives every link, pipeline and pipeline component of that element a new id today, and an undone rename gives the component itself a new id, because the restore command does not rekey back. FBL says what happens to a layout entry when a stored id is renamed (8.4) and nothing about an `identities:` entry when the attribute its key is computed from changes, nor whether an identity write belongs to the edit's history. The library has one change for it, `Identify`, and no rekey. | Language gap, runtime gap and a module defect | Read |
| B11 | **What an edit writes.** A move replaces both numbers, so an untouched `0.90` comes back `0.9`. Setting a link's context, or an evolution's target or new name, replaces the whole line with a canonical statement (`A->B; context`, `evolve Name 0.6`), which drops a trailing comment and the author's spacing. A new decorator goes after the last decorator, else after the coordinates; `inertia` goes directly after the coordinates, before any decorator or label; a removal takes one space with it. FBL replaces the one value, inserts a word where `emit` orders it, and removes "a word and the whitespace before it". The bytes differ, and FBL's are the smaller change wherever it does not fall back to `re-emit-line` (finding B3). | Behaviour change to rule on (Q1) | Read |
| B12 | **Where a new statement goes.** The module appends at the end of the document, which is FBL's `end-of-document`. Two differences remain. Appending sets the previous last line's ending to the document's dominant one, so in `mixed-line-endings.owm` (three CRLF, three LF, last line LF) a line nobody edited changes from LF to CRLF; FBL uses the ending of the line at the insertion point. And a new pipeline component is written with two spaces before the first line that trims to `}`; FBL's `last-child` writes it after the last child with that child's indentation. The module reads LF and CRLF as endings; FBL 2.6 also reads a lone CR. | Behaviour change to rule on (Q1) | Read |
| B13 | **The parser and the writer hold two grammars.** A name is read up to the first coordinate pair and renamed up to the first `[`, so a rename of `Tea [hot]` writes a different element. A link's target is read up to `;` and rewritten only when it holds no `/`, so renaming an element leaves `A->CI/CD` behind. A link's source may not hold `-` or `+` (`[^-+]+?`), so the link the tool itself writes for a component named `Self-service` is never read back, and "Link to" then appends it again. Add and Rename accept a name holding `//`, which is then cut as a comment, and the element disappears. | Module defects; one binding expression per statement removes the first two by construction, and the others need a refusal | Read |
| B14 | **Numbers.** The expressions match `-?[\d.]+`, so `[1.2.3, 0]` matches and then fails to parse, and the parser skips the statement in silence. Under FBL a matched entry whose value does not convert is an unreadable entry, reported by `std.unreadableEntry`. | Behaviour change to rule on (Q4) | Read |
| B15 | **Nothing is reported for a line that is not read.** The parser "never fails" and reports no unreadable line; `unmatched: "keep"` is the same. The new document is `title {base}` with an LF and no header, which FBL's template and its `{base}` placeholder state exactly. A second `title` wins over the first in the tool; what two entries mapped to the diagram mean is not stated in DISL 11.2. | None, and one point to settle by fixture | Read |

### C. Language gaps, as they would be reported to etalii-adp/etalii.adp

1. **FBL: a block whose brace is on the line below its statement** (B1). Candidate: a rule option that lets a statement own the block the next statement opens, or a `blocks` body option naming the brace style. This one blocks the body half.
2. **FBL: braces that do not balance, and a brace that opens nothing** (B2). Candidate: only statements matched by a rule with `opens`, or by a block rule, open a block; an unclosed block ends at the end of the body with a finding.
3. **FBL: a comment that trails a statement** (B3). Candidate: a binding-level `trailingComment` expression, cut before rules match and kept outside every re-emitted span.
4. **FBL: a slot that is found by search inside a group** (B4), for segments of several words in any order.
5. **FBL: one set-valued attribute stored as the presence of words** (B6).
6. **FBL: a list of structured values inside one statement** (B7).
7. **FBL 8.7: the Wardley map's legacy identity file is an array of `{id, kind, key}`**, not an object (B8).
8. **FBL 8.6: the key space of `identities:`, what a change of the key's attribute does to an entry, and whether identity writes are undone** (B9, B10).
9. **DISL 12.4: `fs.exists` against the project root, and the Wardley example** (A5).
10. **DISL: `oncePerGroup` at the first member, and a number written by a fixed invariant pattern** (A7, A8).

These are candidates. Requirement 10 says they are recorded with their evidence and reported, and never worked around silently.

## Open questions

Each is put to the user as a selection. The option marked **(default)** is what this document is written against until the user rules otherwise.

| # | Question | Options | Criteria affected |
| --- | --- | --- | --- |
| Q1 | When an edit changes one value of a statement, what is written (findings B11, B12)? | **(default) What FBL writes**: the one value, a trailing comment and the author's spacing kept, an untouched `0.90` left alone, a new line with the ending and indentation of its neighbours; the transcript's edit hunks are regenerated once and reviewed as a diff. · **What the writer writes today**: the transcript stays byte-identical, and FBL needs a way to force both numbers and a whole-line rewrite, which is a language change first. · Other. | 5.2, 5.3, 1.4 |
| Q2 | The lenient spellings the parser reads today (B4, and keywords in any case): keep reading them? | **(default) Yes**: no file that opens today opens differently, and what FBL cannot express stays module code named as standing in for a gap. · **No**: the binding's grammar becomes the format (one order after the coordinates, no space inside parentheses); every `.owm` in this repository already satisfies it, and files from other tools may not. · Other. | 4.2 |
| Q3 | Where do the ids live after the conversion (B8)? | **(default) An existing `{base}.identities.json` is read and written in the shape it has**, which needs FBL 8.7 and `LegacySidecar` corrected first; a new map keeps its ids in the registration's `identities:` block, as FBL requires. · **Move every map's ids into the registration** on its next save and delete the file. · **Mint new ids once**: nothing stored refers to an id (positions are in the body), so only open selections and undo entries are lost. · Other. | 6.1, 6.2 |
| Q4 | Findings and refusals the languages would add that the tool does not give today (`std.duplicateId` for two links or two notes of one key, `std.unreadableEntry` for `[1.2.3, 0]`, a second `evolve`, an unreadable body for an unclosed pipeline). | **(default) Switched off or read as today in this conversion**, each raised afterwards as a small change of its own. · **Adopted now**, each named in the transcript's diff. · Other. | 3.3, 4.5 |
| Q5 | Findings B1 to B4 mean FBL 0.2 cannot read this format as the tool does. What happens meanwhile? | **(default) Convert in two steps, as the timeline did**: the definition, the derived toolbox, menus, rows and findings now, with the model built from `WardleyParser`'s reading; the body through the binding when FBL answers gap 1, and gaps 2 to 4 as far as Q2 needs them. · **A persistence plugin now** (FBL 11): the parser and the writer behind `read` and `plan`, so the host owns history, drift and the registration, although FBL 11.5 does not expect this format to stay a plugin. · **Narrow the format**: ADP writes the brace on the pipeline's line and reads only that; refused by every certified fixture. · Other. | 4, 5 |
| Q6 | The module defects of A7, B9, B10 and B13. | **(default) Repaired in this specification**, each by a guard seen to fail first, each named in the transcript's diff. · **Reproduced**, so the transcript stays byte-identical, and repaired afterwards. · Other. | 5.8, 6.3 |
| Q7 | Is the client's canvas in scope? | **(default) Yes**: compiled from the bundled definition with `compileNotation`, today's hand-written canvas kept in a test as the oracle; this needs `axis.ranges`, `endLabels` and `fit: "stretch"` in the shared library first (A6). · **No**: backend only. · Other. | 8 |

## Alignment with Product Vision

The product direction (2026-09-26) is that every tool is a markup definition interpreted by a core in each IDE, with code only where a definition cannot reach. The Wardley map is the tool both specifications chose as their example for the hard cases: ids outside the document, a stretched canvas, unequal ranges, a picker, a file-system rule. It is therefore the conversion that tests whether those sections describe a real tool, and three of them do not yet (A5, B1, B8). It follows `product.md`'s *Don't reinvent, integrate*: the format is OnlineWardleyMaps' own, and the binding has to fit the format rather than the reverse.

## Requirements

### Requirement 1: An oracle before any change

**User Story:** As the owner of the tool, I want what it answers today frozen before anything is converted, so that every later step is held to it.

#### Acceptance Criteria

1. WHEN the work starts THEN a parity transcript `Parity/wardley-map.transcript.json` SHALL be generated from today's hand-written code and committed before any provider, parser or writer is changed, in the shape of the timeline's: per document, the context menu of every element, of the empty canvas and of an unknown id; the property rows of every element; the findings; the payload of every mapped element; and a scripted edit sequence with each step's answer, the hunks it made to the body, and the identity entries after it.
2. The corpus SHALL be the 13 fixtures, the `.owm` files of both example trees, and **one new fixture for each finding of tables A and B marked Read**, written so that the finding either shows in the transcript or is struck from this document with the reason. A fixture for another tool's syntax SHALL be certified as `Fixtures/readme.md` describes.
3. A test SHALL require today's code to reproduce the checked-in transcript byte for byte, and SHALL have been seen to fail against a deliberately changed sentence before it is trusted.
4. WHEN a later step changes the transcript THEN the change SHALL be one this document names (Q1, Q4, Q6), regenerated on purpose, and the diff of the checked-in file SHALL be the review of it. Any other difference is a regression.

### Requirement 2: The definition, at DISL 0.3, bundled

**User Story:** As a tool engineer, I want one definition of the Wardley map that every host runs.

#### Acceptance Criteria

1. `definitions/diagrams/wardley-map.dis` in etalii-adp/etalii.adp SHALL be rewritten at DISL 0.3, using the construct finding A2 names in place of each workaround, with the metamodel shapes of finding A3 changed so that a binding can read them, and SHALL validate against the DISL schema in that repository's checks.
2. It SHALL carry an `x-wardley` block mapping its tools, actions, properties and element types to the wire ids the module uses today (finding A4), and `persistence.ids` that make a new id as the module does: 25 base-36 characters.
3. WHILE the body is read by the module's parser (Q5) the definition's `persistence` SHALL say so truthfully, as the timeline's does; WHEN the body is read through the binding it SHALL become `format: "fbl"` with `binding` and the `typeMap` of the appendix.
4. Its companion `wardley-map.md` SHALL be rewritten to say what DISL 0.3 and FBL 0.2 cannot express, which is section C and whatever Requirement 10 adds, and nothing that they can.
5. The definition SHALL be bundled into `src/diagrams/wardley-map/definition/` by `src/diagrams/tools/bundle-disl.sh`, never edited here, embedded in the backend project, held by a `BundledDefinition.Tests` like the other bundled modules', and SHALL load with no diagnostic of severity error.

### Requirement 3: Toolbox, menus, rows and findings are derived

**User Story:** As a maintainer, I want the palette, the menus, the rows and the findings to come from the definition, so that changing the tool is changing one file.

#### Acceptance Criteria

1. The toolbox, the context menu of every target, the property rows of every element, the removal confirmation and the reason on every read-only row SHALL be derived from the bundled definition through `ToolboxDerivation`, `ContextMenuDerivation` and `FormDerivation`, mapped to the wire ids of `x-wardley`, and the providers SHALL compute none themselves.
2. Every sentence a user reads today SHALL be read unchanged, the nine read-only reasons of `WardleyContextPropertyProvider`, "This map's file is read-only." and the two refusals of a typed coordinate included.
3. The eight findings SHALL be derived by `ConstraintEvaluator` with the tool's codes (`wardley.link-target-missing` to `wardley.submap-missing`), severities, sentences, locations and order; a missing link end SHALL be reported once per end; and no finding the tool does not give today SHALL appear (Q4).
4. `wardley.submap-missing` SHALL resolve an address as the tool does today (finding A5). WHERE DISL cannot state that THEN the rule SHALL stay module code, named in the companion document as standing in for the gap.
5. WHEN the derived answers are compared with the transcript THEN the menus, rows, findings and payloads SHALL be byte-identical, except the location of `wardley.duplicate-name` (Q6).
6. The hand-written providers' logic SHALL be kept in the test project as the oracle (`Parity/HandWrittenWardley.cs`) and removed from the product code.

### Requirement 4: The body is read through the binding

**User Story:** As a user, I want every `.owm` that opens today to open the same.

#### Acceptance Criteria

1. The module SHALL own its binding, `backend/EtAlii.Adp.Diagram.WardleyMap/wardley-map.fbl`, embedded in the backend project, and the binding SHALL be offered to etalii.adp as the example FBL section 17 promises.
2. WHEN a body is read THEN the elements, their attributes and their order SHALL be those `WardleyParser` reads today for every document of the corpus, the pipeline whose brace is on its own line, the legacy pipeline, the empty pipeline, trailing comments and the lenient spellings of finding B4 included (Q2).
3. This requirement SHALL NOT be started before FBL can state a pipeline as the fixtures write it (Q5). Until then the model SHALL be built from the parser's reading, in one class named as standing in for gap 1 of section C.
4. A link, an evolution or a pipeline that names no element SHALL be kept, not drawn, and reported with today's code and sentence, through `typeMap` reference ends and not by module code.
5. A body SHALL never be unreadable for its content: an unclosed pipeline SHALL open as it does today (Q4), and a statement that is not read SHALL be kept byte for byte and reported by nothing.
6. A statement that starts with one of the parser's 21 keywords SHALL never be read as a link (finding B5).

### Requirement 5: Every edit is written by splices

**User Story:** As a user whose map is also edited in other tools, I want an edit to change what I changed and nothing beside it.

#### Acceptance Criteria

1. Every command of the module SHALL write through `EtAlii.Adp.Specification.Fbl` as a model change planned into splices; no module code SHALL build a statement's text or replace a line, once Requirement 4 is done.
2. WHEN one value changes THEN only its bytes SHALL change (Q1): a trailing comment, an unknown decorator, a label offset and the author's spacing SHALL survive every edit of the corpus, and no edit SHALL be planned as `re-emit-line` on a statement that holds a comment.
3. A new statement SHALL go at the end of the document; a new pipeline SHALL be written with its brace on its own line, as today; and WHERE line endings or indentation differ from today's (finding B12) THEN the difference SHALL be in the regenerated transcript.
4. A coordinate SHALL be written with at most four decimals, halves away from zero, without trailing zeros, and a drag past an axis SHALL be clamped while a typed value outside 0 to 1 is refused with today's sentence.
5. Renaming an element SHALL rewrite every link, evolution, pipeline and pipeline component that names it in the same edit; removing one SHALL remove its links and evolutions and SHALL leave its pipeline, as today; each is one undo step.
6. Every refusal SHALL keep today's sentence, the legacy pipeline's and the unclosed pipeline's included.
7. Undo SHALL restore the body byte for byte, and a document ADP did not change SHALL be written back byte-identical, for every document of the corpus.
8. A name the format cannot hold (one containing `//`, or one that a link could not be read back from) SHALL be refused from every path that sets one, each with a test seen to fail against today's code first (finding B13, Q6).

### Requirement 6: Identities

**User Story:** As a user with a map open, I want my selection and my undo history to survive a save, a reload and a rename.

#### Acceptance Criteria

1. Every id stored for a map today SHALL be the id of the same element after the conversion (Q3).
2. A map opened and not edited SHALL cause no write of any file, and a new id SHALL be written with the next save that writes the body, as today.
3. A rename SHALL keep the id of the renamed element and of every link, pipeline and pipeline component whose key holds its name, and an undone rename SHALL restore the ids it had (finding B10, Q6).
4. Two elements of different kinds with one key SHALL have different ids, and the key of an existing entry SHALL not change by the conversion (finding B9).
5. A failed write of the identities SHALL keep the edit and warn with today's sentence.

### Requirement 7: What stays code, and why

**User Story:** As a maintainer, I want the code that remains to be exactly what the languages cannot state, each piece with its reason.

#### Acceptance Criteria

1. The module SHALL keep: the element mapper and the viewport filter, the session, the document store and its reload rules, the `.proto` payloads, and each stand-in a requirement above names.
2. `WardleyRuleSet`, `WardleyValidator` and the logic of the three providers SHALL leave the product code with Requirement 3; `WardleyParser`, `WardleyWriter`, `WardleyIdentities` and `WardleyIdentityKeys` with Requirements 4 to 6.
3. WHEN each step is complete THEN the companion document SHALL list every remaining module class beside the gap or the host concern that keeps it, and a class with neither SHALL be removed.

### Requirement 8: The client

**User Story:** As a user, I want the canvas to look and behave as it does, drawn from the definition the backend runs.

#### Acceptance Criteria

1. The canvas SHALL be compiled from the bundled definition with `parseDisl` and `compileNotation` (Q7), with what the library cannot read stated in a `NotationBindings`, and today's canvas kept in a test as the oracle.
2. IF `compileNotation` cannot read something the definition states (the four stage bands and their labels, the end labels, the stretch to the pane, the evolution ring and its dashed join, the inertia bar, a label at the document's own offset) THEN it SHALL be added to the shared library for every module and SHALL NOT be written in this module.
3. A `tests.md` entry SHALL cover in a real browser, in both themes: the bands and both axes, each of the three marks, a label at its offset, a dependency and a flow, an evolution, an attitude region, an accelerator and a deaccelerator, a note, an annotation pinned twice, a drag clamped at an edge, a pipeline component dragged along its row only, and each of the eight toolbox entries.

### Requirement 9: The library work this tool needs

**User Story:** As a maintainer of the shared libraries, I want each thing this tool needs added once, for every tool.

#### Acceptance Criteria

1. `EtAlii.Adp.Specification.Disl` SHALL implement what the rewritten definition calls and `DislCelLibrary` lacks (finding A6), and the derivations SHALL support the constructs of finding A2 they do not support yet; the design SHALL list which, from a load of the rewritten definition. Each SHALL be proven by a library test seen to fail first.
2. `EtAlii.Adp.Specification.Fbl` SHALL read and write a legacy identities file in the shape FBL 8.7 gives it once that text is corrected (finding B8), and SHALL implement what FBL gains for section C, each with a conformance fixture vendored from etalii.adp.
3. A defect of a library that a fixture of Requirement 1 shows SHALL be repaired in the library; a language gap SHALL NOT be repaired here.
4. No module name SHALL appear in a library change.

### Requirement 10: Gaps are recorded and reported, never tuned away

**User Story:** As the owner of DISL and FBL, I want every place the languages fall short reported with its evidence.

#### Acceptance Criteria

1. Every language gap, the ten of section C and any the implementation adds, SHALL be recorded in the companion document with the construct concerned, the fixture that shows it, and what would resolve it.
2. A gap SHALL NOT be closed by weakening a check, skipping a fixture, or stating in the definition or the binding something the tool does not do.
3. WHEN the binding first reads the real files THEN the tool SHALL be added to `ModuleCrossCheck.Tests`, and every disagreement SHALL be an entry of `RealFiles/divergences.json` until it no longer occurs.
4. The delivery report SHALL list every gap as a candidate change for etalii-adp/etalii.adp, and every definition and module defect that was repaired.
5. WHEN a *read* finding turns out not to occur THEN it SHALL be struck here with the fixture that shows it, as a small amendment.

### Requirement 11: Documentation, catalog and gates

**User Story:** As a reader of the repository, I want its pages to stay true through the conversion.

#### Acceptance Criteria

1. `src/diagrams/readme.md`, `docs/creating-a-diagram-module.md`, `docs/diagram-module-client-api.md`, and `docs/architecture.md` and `docs/solution-structure.md` where a sentence becomes false, SHALL be updated in the change that makes them so.
2. `docs/tools.md`'s row for `wardley/map` SHALL name this specification.
3. All four gates SHALL exit zero on every pull request of this specification, judged by captured exit codes, and a newly created worktree SHALL be built before a green gate is trusted about the bundled definition.
4. WHEN the tasks document is written THEN every acceptance criterion here SHALL be claimed by a task, established by diffing the two sets, and two traces SHALL be read back to their artefacts.

## Non-Functional Requirements

### Code Architecture and Modularity

- The module ends as declarations, a binding, and the code Requirement 7.1 names. Library additions are named for what they do, never for this tool.
- The house rules apply: `src/.editorconfig`, no blocking call in an `async` method (`WardleyIdentities.Read` calls `File.ReadAllText` in a synchronous method and may stay so), `TestContext.Current.CancellationToken` in tests, no unneeded `using`. `.owm` is byte-compared and already `-text` in its fixture folders; `.dis` and `.fbl` follow what the hype cycle's have.

### Performance

- Opening, validating, dragging and editing the largest `.owm` of the corpus SHALL not be perceptibly slower than today. A drag is one edit of the body.

### Reliability

- No file that opens today opens differently (Requirement 4.2). An unchanged document is written back byte-identical and undo restores bytes (Requirement 5.7). No id is lost (Requirement 6.1). A missing, unreadable or stale identities file SHALL never keep a map from opening. A `url` address is never fetched or opened, and a path that leaves the project root is never looked for, as today.
- Every test written for a finding is seen to fail against that finding before it is trusted.

### Usability

- Every sentence a user reads today is read unchanged, unless this document names the change.

## Out of scope

- Any change to what the tool does beyond the seven questions above.
- Changing DISL or FBL. Gaps are reported (Requirement 10); a language change is etalii.adp's own specification.
- Editing the `.owm` as text, the annotation legend, and what `style` and `size` draw: the tool does none today.
- The other tools of the series. What this one adds to the libraries (Requirement 9) is theirs to use.

## Appendix: the binding, as drafted

A draft against FBL 0.2, to be settled in the design, and **not a binding that reads this format today**. It is written in the `blocks` family because only that family has containment, and four things in it are not valid FBL 0.2. Each stands for a finding:

- `"brace": "next-line"` on the pipeline rule stands for finding B1.
- `"trailingComment"` stands for finding B3. Without it every `line` below would need the cut written into it.
- `"segment"` slots (the label offset, a decorator with spaces inside its parentheses) stand for finding B4.
- `"list"` on the annotation's positions stands for finding B7.

The five decorator flags are valid and are the workaround of finding B6. The `keyword` rule is the answer to finding B5 and must stay before the two link rules.

```json
{
  "fbl": "0.2",
  "bindings": {
    "owm": {
      "title": "OnlineWardleyMaps text",
      "claims": { "extensions": [".owm", ".wm"], "origins": ["wardley/map"] },
      "body": { "kind": "file", "family": "blocks" },
      "reader": "declared",
      "text": { "newline": "lf", "indent": 2, "finalNewline": true },
      "comment": "^\\s*//",
      "trailingComment": "(?<!:)//.*$",
      "unmatched": "keep",
      "elements": [
        { "name": "title", "type": "Title", "within": ["^"], "caseInsensitive": true,
          "line": "^\\s*title(?:\\s+(?<title>.*?))?\\s*$",
          "attributes": { "title": { "group": "title", "empty": "keep" } } },
        { "name": "component", "type": "Component", "within": ["^"], "caseInsensitive": true,
          "line": "^\\s*component\\s+(?<name>.+?)\\s*\\[\\s*(?<visibility>-?[\\d.]+)\\s*,\\s*(?<maturity>-?[\\d.]+)\\s*\\](?<rest>.*)$",
          "id": { "sidecar": { "key": "'component\\u001f' + groups.name" } },
          "attributes": {
            "name": { "group": "name", "empty": "refuse" },
            "visibility": { "group": "visibility", "number": { "decimals": 4 } },
            "maturity": { "group": "maturity", "number": { "decimals": 4 } },
            "inertia": { "group": "rest", "word": "^inertia$", "flag": true },
            "market": { "group": "rest", "word": "^\\(market\\)$", "flag": true },
            "ecosystem": { "group": "rest", "word": "^\\(ecosystem\\)$", "flag": true },
            "build": { "group": "rest", "word": "^\\(build\\)$", "flag": true },
            "buy": { "group": "rest", "word": "^\\(buy\\)$", "flag": true },
            "outsource": { "group": "rest", "word": "^\\(outsource\\)$", "flag": true },
            "url": { "group": "rest", "word": "^url\\((?<value>[^)]+)\\)$", "readOnly": true },
            "labelOffset": { "group": "rest", "segment": "label\\s*\\[\\s*(?<dx>-?[\\d.]+)\\s*,\\s*(?<dy>-?[\\d.]+)\\s*\\]", "readOnly": true }
          },
          "insert": { "place": "end-of-document", "emit": "component {name} [{visibility}, {maturity}][ {inertia}][ {market}][ {ecosystem}][ {build}][ {buy}][ {outsource}]" },
          "remove": { "cascade": ["dependency", "flow", "evolve"] } },
        { "name": "evolve", "type": "Evolve", "within": ["^"], "caseInsensitive": true,
          "line": "^\\s*evolve\\s+(?<component>.+?)\\s*(?:->\\s*(?<becomes>[^\\d]+?)\\s*)?(?<maturity>-?[\\d.]+)\\s*$",
          "attributes": {
            "component": { "group": "component", "reference": { "to": ["component", "anchor", "submap"], "by": "name" } },
            "becomes": { "group": "becomes", "empty": "remove" },
            "maturity": { "group": "maturity", "number": { "decimals": 4 } }
          },
          "insert": { "place": "end-of-document", "emit": "evolve {component}[->{becomes}] {maturity}" },
          "remove": {} },
        { "name": "legacyPipeline", "type": "LegacyPipeline", "within": ["^"], "caseInsensitive": true, "readOnly": true,
          "line": "^\\s*pipeline\\s+(?<component>.+?)\\s*\\[\\s*(?<start>-?[\\d.]+)\\s*,\\s*(?<end>-?[\\d.]+)\\s*\\].*$",
          "id": { "sidecar": { "key": "'pipeline\\u001f' + groups.component" } },
          "attributes": { "component": { "group": "component", "reference": { "to": ["component", "anchor", "submap"], "by": "name" } } } },
        { "name": "pipeline", "type": "Pipeline", "within": ["^"], "caseInsensitive": true, "opens": true, "brace": "next-line",
          "line": "^\\s*pipeline\\s+(?<component>.+?)\\s*\\{?\\s*$",
          "id": { "sidecar": { "key": "'pipeline\\u001f' + groups.component" } },
          "attributes": { "component": { "group": "component", "reference": { "to": ["component", "anchor", "submap"], "by": "name" } } },
          "insert": { "place": "end-of-document", "emit": "pipeline {component}" } },
        { "name": "pipelineComponent", "type": "PipelineComponent", "within": ["pipeline"], "caseInsensitive": true,
          "parent": { "rules": ["pipeline"], "slot": "components" },
          "line": "^\\s*component\\s+(?<name>.+?)\\s*\\[\\s*(?<maturity>-?[\\d.]+)\\s*\\].*$",
          "id": { "sidecar": { "key": "'pipeline-child\\u001f' + parent.component + '\\u001f' + groups.name" } },
          "attributes": { "name": { "group": "name", "readOnly": true }, "maturity": { "group": "maturity", "number": { "decimals": 4 } } },
          "insert": { "place": "last-child", "emit": "component {name} [{maturity}]" },
          "remove": {} },
        { "name": "annotation", "type": "Annotation", "within": ["^"], "caseInsensitive": true,
          "line": "^\\s*annotation\\s+(?<number>\\d+)\\s*(?<positions>\\[.*\\])\\s*(?<text>.*)$",
          "id": { "sidecar": { "key": "'annotation\\u001f' + groups.number" } },
          "attributes": { "number": { "group": "number", "readOnly": true }, "positions": { "group": "positions", "list": "pairs", "readOnly": true }, "text": { "group": "text", "readOnly": true } },
          "insert": { "place": "end-of-document", "emit": "annotation {number} {positions} {text}" } },
        { "name": "url", "type": "Url", "within": ["^"], "caseInsensitive": true, "readOnly": true,
          "line": "^\\s*url\\s+(?<name>\\S+)\\s*\\[\\s*(?<address>[^\\]]+?)\\s*\\]\\s*$",
          "attributes": { "name": { "group": "name" }, "address": { "group": "address" } } },
        { "name": "keyword", "type": "Keyword", "caseInsensitive": true, "readOnly": true,
          "line": "^\\s*(?:component|anchor|submap|pipeline|title|size|style|evolve|note|annotations|annotation|url|pioneers|settlers|townplanners|accelerator|deaccelerator|y_axis|x_axis|evolution|presentation)(?:\\s.*)?$" },
        { "name": "dependency", "type": "Dependency", "within": ["^"],
          "line": "^\\s*(?<from>[^-+]+?)\\s*->\\s*(?<to>[^;]+?)\\s*(?:;\\s*(?<context>.*?))?\\s*$",
          "id": { "sidecar": { "key": "'link\\u001f' + groups.from + '\\u001f' + groups.to + '\\u001fDependency'" } },
          "attributes": {
            "from": { "group": "from", "reference": { "to": ["component", "anchor", "submap"], "by": "name" } },
            "to": { "group": "to", "reference": { "to": ["component", "anchor", "submap"], "by": "name" } },
            "context": { "group": "context", "empty": "remove" }
          },
          "insert": { "place": "end-of-document", "emit": "{from}->{to}[; {context}]" },
          "remove": {} }
      ],
      "registration": { "createOnFirstPlacement": false, "legacyIdentities": "{base}.identities.json" },
      "template": { "text": "title {base}\n" }
    }
  }
}
```

Left out for length, and each the shape of a rule shown: `anchor` and `submap` (as `component`, sharing its identity kind `component`), `flow` (as `dependency`, with `+>` and `Flow`), `style`, `size` and `annotations` (as `title`, read-only), `note`, `accelerator` and `deaccelerator` (a text or a name before a coordinate pair, as `component` without a trailer), and the three attitudes with four numbers. Unsettled and the design's to close with fixtures: that the `keyword` rule's entries can be mapped out of the model without a finding; what word a `flag` placeholder writes when its `word` is an expression; and that `groups.from` in a sidecar key is the trimmed name the parser yields.

## Appendix: the definition's persistence and wire ids, as drafted

```json
{
  "persistence": {
    "format": "fbl",
    "binding": "https://raw.githubusercontent.com/etalii-adp/etalii.adp.ide.standalone/develop/src/diagrams/wardley-map/backend/EtAlii.Adp.Diagram.WardleyMap/wardley-map.fbl#owm",
    "ids": { "strategy": "uuid-v4", "encoding": "base36", "stable": true },
    "view": { "store": [] },
    "typeMap": {
      "Title": { "as": "diagram" },
      "Keyword": { "as": "header" },
      "Evolve": { "as": "Evolution" },
      "LegacyPipeline": { "as": "Pipeline" },
      "Dependency": { "as": "Dependency", "attributes": { "from": "source", "to": "target" } },
      "Flow": { "as": "Flow", "attributes": { "from": "source", "to": "target" } }
    }
  },
  "x-wardley": {
    "tools": {
      "component": "wardley.toolbox.component", "anchor": "wardley.toolbox.anchor", "market": "wardley.toolbox.market", "ecosystem": "wardley.toolbox.ecosystem",
      "submap": "wardley.toolbox.submap", "pipeline": "wardley.toolbox.pipeline", "note": "wardley.toolbox.note", "annotation": "wardley.toolbox.annotation"
    },
    "actions": {
      "addComponent": "wardley.add-component", "addAnchor": "wardley.add-anchor", "addSubmap": "wardley.add-submap",
      "addMarket": "wardley.add-market", "addEcosystem": "wardley.add-ecosystem", "addNote": "wardley.add-note", "addAnnotation": "wardley.add-annotation",
      "rename": "wardley.rename", "remove": "wardley.remove", "setEvolve": "wardley.set-evolve", "clearEvolve": "wardley.clear-evolve",
      "toggleInertia": "wardley.toggle-inertia", "toggleDecorator": "wardley.toggle-{decorator}",
      "link": "wardley.link", "flow": "wardley.flow", "unlink": "wardley.unlink",
      "addToPipeline": "wardley.add-to-pipeline", "removeFromPipeline": "wardley.remove-from-pipeline"
    },
    "properties": {
      "name": "wardley.name", "kind": "wardley.kind", "visibility": "wardley.visibility", "maturity": "wardley.maturity",
      "stage": "wardley.stage", "labelOffset": "wardley.label-offset", "evolvesTo": "wardley.evolve", "becomes": "wardley.evolve-name",
      "inertia": "wardley.inertia", "decorators": "wardley.decorators", "url": "wardley.url", "source": "wardley.link.source", "target": "wardley.link.target", "linkKind": "wardley.link.kind", "context": "wardley.link.context",
      "components": "wardley.pipeline.children"
    },
    "elementTypes": {
      "Positioned": "wardley/map+element", "Link": "wardley/map+link", "Note": "wardley/map+note", "Annotation": "wardley/map+annotation", "Accelerator": "wardley/map+accelerator", "Attitude": "wardley/map+attitude", "evolutionAxis": "wardley/map+evolution-axis"
    }
  }
}
```

The ids are those of the three providers and of `WardleyElementTypes`. An evolution has no identity kind in the tool and no element on the wire, so the binding gives it no `id` and it takes the one DISL derives.

## Sources

- The module `src/diagrams/wardley-map/` at `develop` `9a646009`: `WardleyParser.cs`, `WardleyWriter.cs`, `WardleyDocument.cs`, `WardleyIdentities.cs`, `WardleyIdentityKeys.cs`, `WardleyDocumentStore.cs` (`Rekey`, `Save`), `WardleyRuleSet.cs`, `WardleyValidator.cs`, `WardleyDocumentFactory.cs`, `WardleyEditability.cs`, `Commands/WardleyElementCommandHandlers.cs`, `Commands/MoveWardleyElementCommand.cs`, the ids and sentences of the three providers, `Diagram.cs` for `.wm`, and `Fixtures/readme.md` with the fixtures it describes. Counts are from `git ls-files` of the module piped to `wc -l`.
- etalii-adp/etalii.adp at `develop` `da64ff0`, read with `git show origin/develop:`: `definitions/diagrams/wardley-map.dis` and `.md`; `specifications/fbl/FBL-specification.md` (0.2 draft: sections 2.4, 2.5, 3.3, 4.6, 4.7, 5.1 to 5.7, 6.1 to 6.3, 7.4, 7.5, 8.1 to 8.7, 11, 13, 17); `specifications/disl/DISL-specification.md` (0.3 draft: *Changes from 0.1*, *Changes from 0.2*, sections 5.3, 6.12, 6.13, 7.3, 7.5, 8.1, 11.2, 11.5, 12.4, D.2). A listing of `specifications/fbl/` at that commit shows no Wardley binding.
- For A6: the quoted names in `src/backend/EtAlii.Adp.Specification.Disl/Expressions/DislCelLibrary.cs`, and a search for `ranges`, `endLabels`, `"stretch"`, `outOfRange`, `fs\.exists` and `location()` over `src/backend/EtAlii.Adp.Specification.Disl` and `src/client/src` (`*.cs`, `*.ts`, `*.tsx`, test files left out), which found no file.
- For B8: `src/diagrams/wardley-map/examples/example 2/annotations-and-labels.identities.json` and `src/backend/EtAlii.Adp.Specification.Fbl/Registration/LegacySidecar.cs`; for B10, a search of `EtAlii.Adp.Specification.Fbl` for `Identify`, found in `ModelChange.cs`, `EditPlanner.cs` and `OpenRegistration.cs` only.
- A search of `src/backend/EtAlii.Adp.Specification.Fbl.Tests` for `wardley` and `.owm`, which found only a registration example and a registration test, and no entry in `RealFiles/divergences.json`.
- The hype cycle graph module as the pattern: `GhgDefinition.cs`, `GhgBody.cs`, its `Parity/` folder, and `definition/gartner-hype-cycle-graph.dis` for `persistence.typeMap` and `x-ghg`.
- `causal-loop-disl-fbl` and `timeline-disl-fbl` for the series, the survey and the shape of this document.
