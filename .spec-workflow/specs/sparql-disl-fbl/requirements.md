# Requirements Document

## Introduction

The user's request, verbatim (2026-10-09):

> Create spec workflow MCP specifications. One specification for converting one of the standalone diagram tools to FBL and DISL. i.e. analyse the current implementation, define the DISL and FBL and then write the specification on the refactoring., also focus on finding issues and aspects where the DISL and FBL languages do not yet cover everything.

This is the specification for the **SPARQL query diagram** (`w3c/sparql`, `.rq`), one of the fifteen that reading gives (the series and the survey behind it are in `causal-loop-disl-fbl`, *Introduction*). The tool is on neither language: a DISL draft exists in etalii.adp, no FBL binding exists anywhere, and the module bundles no definition.

**Two things set this tool apart from the others of the series, and they shape the whole document.**

- **It never writes its body.** The module has no writer, no command and no toolbox, and three test layers hold that. So there is no insert, no removal, no splice and no byte-for-byte edit to convert. The conversion is reading plus derivation, and the write-side question is how "there is no writer" stays proven once the module sits on a library that can write.
- **Its format is a real grammar.** FBL 11.5 lists SPARQL 1.1 among the formats expected to stay persistence plugins. Finding B1 tests that against the files of this repository and confirms it. The binding is therefore a few lines that name a plugin, and the findings are about the plugin contract and about what DISL derives from what the plugin reads.

**What "converted" means here**: the property rows, the findings, the refusals and the (empty) toolbox are derived from a bundled DISL 0.3 definition by `EtAlii.Adp.Specification.Disl`; the body is read through an FBL binding whose reader is a persistence plugin wrapping the module's tokenizer and parser, by `EtAlii.Adp.Specification.Fbl`; and the module keeps code only for what neither language can state. Nothing a user sees changes unless this document names the change and the user has ruled on it.

## What was measured

Read on 2026-10-09: the module at `develop` `9a646009`; `definitions/diagrams/sparql-query.dis` and `.md`, the DISL specification (0.3 draft) and the FBL specification (0.2 draft) in etalii-adp/etalii.adp at `develop` `da64ff0`; and the two libraries in this repository.

| Thing | State |
| --- | --- |
| The module's backend | 4,492 lines of C# in 33 files. `SparqlTokenizer` (381 lines) and `SparqlParser` (1,436) read the body; `SparqlProjection` (442) turns the parsed query into drawn elements; `SparqlLayout` (281), `SparqlValidator` (133), `SparqlContextPropertyProvider` (185) and `SparqlContextActionProvider` (66) hold the arrangement, the findings, the rows and the refusal of every action as code. 11 test files, 2,094 lines. |
| A writer | None, on purpose. No file is named for one, `ISparqlDocumentStore` ends at reading, and `ServiceCollection.AddSparql.cs` registers no command and no toolbox provider. The one mutating gesture is a move, which dispatches core's `SetRegistrationLayoutCommand` and writes the registration. `NoWriterSurface.Tests`, `NoWriterSweep.Tests` and the provider tests hold it. |
| The store | `SparqlDocumentStore` is a thin use of the shared read-only `DocumentLifecycle` of `EtAlii.Adp.Documents`: no save path and no self-write bookkeeping. |
| The module's client | 924 lines of TypeScript in 5 files, 338 of them the canvas test. `SparqlCanvas.tsx` holds a hand-written canvas definition. |
| The DISL definition | Exists in etalii.adp as a draft, 589 lines, **written against DISL 0.1**. It is not bundled here (`src/diagrams/sparql/definition/` does not exist). It names a plugin as its `persistence.format`. |
| The FBL binding | None, in either repository. FBL 11.5 and 17 both say the SPARQL query is read by a plugin under the contract. |
| The real-file check | None: `RealFiles/divergences.json` has no entry for this tool, and no cross-check exists because there is no binding to read through. |
| The corpus | 31 `.rq` files under `src/`: 11 test fixtures, 10 examples in the module, and their 10 shipped copies under `src/examples/diagrams/sparql/`. Three of the shipped registrations carry a `layout:` block. |
| A parity transcript | None. |

**How each finding below is known** is marked, because none of them was run: *read* is a reading of the code or the draft against the specification text; *measured* is a search or a count whose command is in *Sources*; *recorded* is something a test or a comment in the code already says. Requirement 1 turns every *read* finding into a fixture before anything is changed.

## Findings

The three kinds are those of `causal-loop-disl-fbl`: a **definition defect** is repaired in the definition or the binding, a **runtime gap** in this repository's libraries, a **language gap** in etalii-adp/etalii.adp. A fourth appears here more than elsewhere: a **module defect**, something the tool does today that neither language would reproduce and that looks like a mistake.

### A. The DISL definition against the module

| # | Finding | Kind | Known |
| --- | --- | --- | --- |
| A1 | The draft is DISL 0.1. The client's `parseDisl` refuses anything but 0.3 and every bundled definition here is 0.3. It must be rewritten at 0.3 before it can be bundled. | Definition defect | Read |
| A2 | Its companion document ends with ten things "DISL 0.1 could not say". DISL 0.2 and 0.3 have since answered eight, several with this tool's own sentences as the specification's examples: a foreign model that is read and never written (`format: "fbl"` with a `readOnly` binding); nodes derived from text and containment computed from the model (`derived` node types with `parent`, 4.11.2, where 4.11.1 names "a SPARQL variable placed in its shallowest scope"); an edge owned by a scope other than its ends' (`owner`, 4.11.3); ids from term forms and scope paths, and ids never stored (`derived` id rules, 11.5.2, and `ephemeral`, 11.5.3); findings on lines and a parse failure as a finding (`location`, `subject`, and `std.unparseable` with a `code`, 8.6, whose example is `sparql.unparseable`); a frame outside the canvas and a banner (`canvas.chrome.header` and a budget's `notice`, 6.13); a refusal sentence per gesture and per kind and a reason on every row (`refusals`, whose two examples are this module's sentences, and `readOnlyReasons`); and a hard bound that cuts in a declared order (`limits.budgets` with `order`, 3.2.1, which names this tool). The draft still works around each: every drawn type is a stored type "the plugin fills", `limits.maxElements` stands in for the cut, and `unusedPrefix` joins every unused prefix into one sentence. | Definition defect | Read |
| A3 | The two left open are real. A stored position of a region moves the frame and what layout computed inside it, while an authored position inside stays absolute; DISL's `respect` has `none`, `pinned` and `all` and says nothing of a container. And what a connection is sent as its viewport moves is the host's on purpose. | Language gap (the first); none (the second) | Read |
| A4 | `persistence` names `format: "plugin:net.etalii.adp.w3c.sparqlQuery"` with `files` and `metadata`. With the binding it becomes `format: "fbl"` with `binding`, and DISL 11.2 forbids those two keys beside it. The plugin stays declared under `plugins`, now named by the binding's `reader`. | Definition defect | Read |
| A5 | **The library does not compute what A2 relies on.** `DislModelBuilder` computes derived ids and derived relations and no derived node type, and answers a derived relation that has a `key` with "groups its items by key, which this runtime does not compute yet." The keys `ephemeral`, `budgets`, `location`, `subject`, `refusals`, `chrome` and `notices` occur in `EtAlii.Adp.Specification.Disl` only in `DislExpressionWalker.cs`, which assigns their CEL contexts: they are checked and never evaluated. `readOnlyReasons` is evaluated, by `FormDerivation`. | Runtime gap | Measured |
| A6 | **The unparseable finding** is derivable today as the hype cycle derives its own: `constraints.builtIn` gives `std.unparseable` the code `sparql.unparseable`, severity `error` and the message "This is not a SPARQL query that can be read: " followed by `detail.reason`. The draft has no such entry. The reason is one of 33 sentences, 29 in the parser and 4 in the tokenizer, and stays the plugin's. | Definition defect | Read; the count is measured |
| A7 | **The unused-prefix finding** is one per prefix at the declaration's line. `forEach` with `location` states exactly that (it is DISL 8.2's example), and `location` is not evaluated here (A5). A stored `Prefix` record per declaration, never drawn, gives the same finding with today's evaluator, which takes a finding's line from its element. "Used" is decided on the source text, comments and strings included, so `used` is a value the plugin reads and not one DISL computes. | Definition defect, with a way round the runtime gap | Read |
| A8 | **The unbound-projection finding** is reported at line 1 and attached to no element. DISL attaches it to the variable, marks the node, and locates it at the first of the variable's sources. | Behaviour change to rule on (Q3) | Read |
| A9 | **The same finding is given where it is false.** The rule asks for no triple-pattern position and no defining expression. A variable bound only by a `VALUES` block, or only by a subquery's projection, has neither, so `SELECT ?x WHERE { VALUES ?x { 1 2 } }` is told that `?x` "never appears in the where clause, so its column will always be unbound". No fixture projects such a variable. The draft states the same rule. | Module defect (Q4) | Read |
| A10 | **Two literals can share an id.** A literal's id ends in the last segment of its datatype IRI, so `"1"` typed by two datatypes whose IRIs end alike is one node. An edge's id holds the predicate as written, sliced from the source, so a property path written with spaces gives an id with spaces, which the draft's `ids.pattern` (`\S+`) rejects and DISL then treats as a missing id. | Module defect (Q4); definition defect | Read |
| A11 | **The order of variable nodes depends on the machine.** `OrderedUsages` sorts by the first scope path with .NET's default string comparer, which follows the current culture, then by name ordinally. The order feeds the grid layout and the 500 cut. DISL orders strings by code point and a derived type by its `from`. Whether two scope paths this module can produce compare differently under a culture is not shown. | Module defect, to settle by fixture (Q4) | Read |
| A12 | **Wire ids.** The header band and the truncation banner are elements on the wire (`header:query`, `truncation`) with rows, a selection text and a move refusal each; in DISL both are chrome and not elements. The module's 14 property ids, 7 element type strings and those two ids are what the client uses. | Language gap, already ruled: wire ids stay an `x-` block. This definition gets `x-sparql`. | Read |
| A13 | **An empty toolbox, derived.** The module registers no toolbox provider and `SparqlCanvas` registers no toolbox with the panel, so the Toolbox panel is never told a query is open and keeps its no-diagram text. The Ansible canvas, also read-only, was wired the other way and says that its type offers no elements. A converted module that derives an empty toolbox and registers it changes what the panel says. | Behaviour change to rule on (Q3) | Recorded (`SparqlCanvas.tsx`'s own comment; `tests.md` for the Ansible canvas); not seen in a browser |
| A14 | The scope-grid layout, the element mapper with its viewport filter, the `.proto` payloads and the selection resolver are the host's or a named plugin's and stay code. | None | Read |

### B. The FBL binding and the plugin contract against the module's reader

| # | Finding | Kind | Known |
| --- | --- | --- | --- |
| B1 | **A declared binding cannot read a query, and the corpus shows it.** Of the module's 21 `.rq` files, 14 open or close a group in the middle of a line, which the `blocks` family does not read as a block; two of the W3C recommendation's own examples (`filter.rq`, `optional.rq`) open a group mid-line and close it on a line of its own, which `blocks` reads as unbalanced and so as an unreadable body. One statement spans lines (the `;` lists of `constructs.rq` and `adp/ask.rq`, a `SELECT` over five lines in the UniProt query) and one line holds several (three `UNION` branches in `groups.rq`), where `lines` has one statement per line. One written statement yields several patterns, a collection yields cells nobody wrote, a prefixed name needs the prologue to become an IRI, and one variable is every mention of its name. FBL 11.5's reason, "a recursive grammar projected onto scopes", holds. | None: the binding names a plugin | Measured (the counts); read |
| B2 | **FBL and DISL place the projection differently.** FBL 11.1 gives a plugin "projecting Turtle triples onto cards and rows" and 11.5 keeps "the projection" in code. DISL 4.11.1 says a format binding "produces the fine-grained stored records and leaves the drawn projection to DISL", and that an element which groups several entries, or whose parent is derived, is a DISL derived element and "MUST NOT have a source entry of its own". A plugin that delivers the variable nodes does what FBL describes and what DISL forbids. | Language gap (the two specifications disagree) (Q1) | Read |
| B3 | **The contract wants a source span for every element, and the parser keeps almost none.** FBL 11.2's `read` delivers "the source span of the entry" for each element, and `FblElement` has `OwnSpan` and `Line` as required members. `SparqlQueryModel` keeps a line for a prefix declaration and for the parse error; `TriplePattern`, `GroupScope`, `SparqlConstraint` and `SparqlSubSelect` carry no position. The tokenizer has every offset. The parser must carry them into its model before a plugin can deliver them. | Module work the contract demands | Read |
| B4 | **`read` must not fail on content, and the parser throws.** It stops at the first error with a sentence and a line; the token's column is known and dropped. The plugin catches, reports the body unreadable and delivers one `std.unparseable` with line and column. | Module work the contract demands | Read |
| B5 | **A reload that reads but does not parse.** The lifecycle keeps the last good query only when the file cannot be read. A text that reads and does not parse is installed, so while somebody types in the text editor, which is how this tool's file changes, each half-written state empties the diagram. FBL 7.3 keeps "the last readable model on screen, read-only, with the finding". | Behaviour change to rule on (Q2) | Read |
| B6 | **An unreadable query draws a header band that says `SELECT`.** The session renders the entry's model without asking whether it parsed; the empty model's form is `Select`; `AQueryThatDoesNotParse_OpensWithOnlyItsHeader` asserts the header's id and not its text. The entry's own sentence (a missing file, an unreadable one, the parse reason) is read by no product code of the module beyond `IsUsable`. FBL 7.5 opens an unreadable body empty. | Module defect (Q4) | Measured (the search for readers of `Error`); read |
| B7 | **"No writer" becomes a declaration.** Today nothing in the module can write. On FBL the module holds a `PluginBody`, which can save, and implements `IPersistencePlugin`, whose `Plan` returns splices. What stops a write is the binding's `readOnly`, which `PluginBody.Plan` and `Save` honour. The surface test would not notice a plugin that plans: it looks for `Writer` and `Command` in type names and for write-shaped members on the two store types only. | Behaviour to keep, with a guard to extend (Requirement 6) | Read |
| B8 | **The template.** The document factory's starter query and a binding's `template.text` are the same seven lines; with `template.text` the plugin's `template` operation is never asked. The factory stays because core refuses to start without one, and hands out the binding's text, so one place says what a new query is. | Definition, no defect | Read |
| B9 | **A move without a registration** is refused with "This query was opened without a registration, so there is nowhere to store a position. Register the file to arrange it." FBL 8.4 says the host "refuses the placement with a reason" when `createOnFirstPlacement` is false and gives the binding no place for that reason. | Language gap, minor | Read |
| B10 | **Ids numbered by place are stored as if they were names.** A region is `region:` and its scope path, `where/optional.1` being the second optional of the root; a subquery and an annotation are numbered the same way. A position stored for one is applied, after an `OPTIONAL` is written above it in the text editor, to a different group. DISL 11.5.3 calls "elements numbered by their position in the file" ephemeral, which would refuse the move; FBL 8.5 says a stale entry "is not applied to any other element", which a place-numbered id cannot promise. Only the anonymous variable is refused a position today. | Behaviour change to rule on (Q5); language gap | Read |
| B11 | **Stale positions.** Core's layout command sets and removes one entry and prunes nothing, so a position for a variable since renamed stays in the registration unreported. An FBL host reports it as `fbl.stale-view-data` and removes it at the next write. Moves stay core's command here, so this occurs only if the registration is ever written through the library. | Behaviour change to rule on (Q3) | Measured |
| B12 | **The library's plugin contract has no `args`.** FBL 11.2 hands `read` "the binding's `args`"; `PluginReader` holds a plugin id and a version and `PluginReadRequest` holds files. This tool passes none. | Runtime gap, harmless here | Measured |

### C. Language gaps, as they would be reported to etalii-adp/etalii.adp

1. **FBL and DISL: who projects a plugin-read format** (B2). FBL 11 reads as if the plugin delivers what is drawn; DISL 4.11 as if it delivers one record per entry. Candidate: FBL 11.2 says that `read` delivers stored elements only, one per entry, and 11.5's "why a plugin" column says "parsing" where it says "projection".
2. **FBL: an element with no entry of its own** (B3), if the answer to 1 is that a plugin may project. `read` then needs to say what the span of a merged element is.
3. **FBL: the sentence for a placement refused without a registration** (B9). Candidate: `registration.createOnFirstPlacement` takes a text as `readOnly` does.
4. **FBL and DISL: ids numbered by place among siblings** (B10). Neither a name nor ephemeral. Candidate: DISL 11.5.3 says whether a place-numbered id of a stored element is ephemeral by default, and FBL 8.5 says what a host owes when it is not.
5. **DISL: a stored container position that moves its computed contents** (A3).
6. **DISL: no percent-encoding in the function library** (A10). A literal's id is its lexical form percent-encoded; 12.4 has no function for it, so the plugin delivers the encoded form as an attribute.
7. **DISL: what chrome shows for an unreadable body** (B6). `canvas.chrome.header` rows read diagram attributes, which have their defaults when nothing was read.

These are candidates. Requirement 11 says they are recorded with their evidence and reported, and never worked around silently.

## Open questions

Each is put to the user as a selection. The option marked **(default)** is what this document is written against until the user rules otherwise.

| # | Question | Options | Criteria affected |
| --- | --- | --- | --- |
| Q1 | What does the plugin deliver (finding B2)? | **(default) One stored record per entry**: prefixes, groups, patterns, constraints and subqueries, each with its span; the definition derives the variable and term nodes, their scopes and the edges, as DISL 4.11.1 describes. `SparqlProjection` leaves the product code, and the library gains derived node types (Requirement 9). It is the larger piece of library work and the one that tests the languages. · **The drawn elements**: the plugin wraps `SparqlProjection` too; today's library suffices; the projection stays module code and the disagreement of B2 is reported as the reason. · Other. | 3.3, 4, 8.2, 9.1 |
| Q2 | A change in the text editor leaves a query that does not parse (finding B5). What does an open diagram show? | **(default) What it shows today**: the frame only, until the text parses again. · **The last query that parsed**, with the finding, as FBL 7.3 says. · Other. | 3.5 |
| Q3 | What the languages would add or change that the tool does not do today: the unbound-projection finding on its variable and at its line (A8), the Toolbox panel's text (A13), a finding for a stale position (B11). | **(default) None in this conversion**: each is stated as it is today or switched off, and raised afterwards as a small change of its own. · **Adopted now**, each named in the transcript's diff. · Other. | 5.3, 5.5, 7.3 |
| Q4 | The module defects found on the way (A9, A10, A11, B6). | **(default) Reproduced, each with a fixture that shows it**, and raised afterwards one by one, so that the transcript before and after the conversion is the same file. · **Repaired in this work**, each with a guard seen to fail first and named in the transcript's diff; repairing A10 changes a stored position's key. · Other. | 1.2, 4.2, 5.3 |
| Q5 | Positions of regions and subqueries are keyed by their place in the text (finding B10). | **(default) Kept as today**: stored, and applied to whatever holds that place. · **Ephemeral**: a region and a subquery take their computed place and a move is refused with a reason, as an anonymous variable's is; three shipped registrations hold no such key. · Other. | 7.1 |
| Q6 | Is the client's canvas definition in scope? | **(default) Yes**: compiled from the bundled definition with `compileNotation`, with today's hand-written definition kept in a test as the oracle. · **No**: backend only. · Other. | 10 |

## Alignment with Product Vision

The product direction (2026-09-26) is that every tool is a markup definition interpreted by a core in each IDE, with code only where a definition cannot reach. This tool is the test of the other end of that sentence: its reader is code and stays code, and the question is how thin the rest becomes. It is also the one tool whose DISL specification text already quotes it, so it measures how far this repository's library is behind the language (finding A5). It follows `structure.md`'s rule that a diagram type adds declarations and no core code: what the libraries lack is added to the libraries, for every tool.

## Requirements

### Requirement 1: An oracle before any change

**User Story:** As the owner of the tool, I want what it answers today frozen before anything is converted, so that every later step is held to it.

#### Acceptance Criteria

1. WHEN the work starts THEN a parity transcript `Parity/sparql.transcript.json` SHALL be generated from today's hand-written code and committed before any provider, parser or projection is changed: per document, the payload and position of every element, the property rows of every element and of an unknown id, the selection text of every element, the findings, and a scripted sequence of gestures with each step's answer, namely a move of a variable, of a region and of a concrete term, the five refused moves, a refused re-parenting, a refused property write and a refused action.
2. The corpus SHALL be the 31 `.rq` files of *What was measured* with their registrations, and **one new fixture for each finding marked Read** that a document can show: a `VALUES`-bound and a subquery-bound projected variable (A9), two literals whose datatypes end alike and a property path written with spaces (A10), scope paths chosen to sort differently by culture (A11), a query over 500 elements, a self-referring pattern, and a query whose second `OPTIONAL` carries a stored position (B10).
3. A test SHALL require today's code to reproduce the checked-in transcript byte for byte, and SHALL have been seen to fail against a deliberately changed sentence before it is trusted.
4. After every step of the scripted sequence the `.rq` SHALL be compared with its bytes before the step, so that the transcript is also the behavioural proof that nothing writes the query.
5. WHEN a later step changes the transcript THEN the change SHALL be one this document names (Q2 to Q5), regenerated on purpose, and the diff of the checked-in file SHALL be the review of it.

### Requirement 2: The definition, at DISL 0.3, bundled

**User Story:** As a tool engineer, I want one definition of the SPARQL query diagram that every host runs.

#### Acceptance Criteria

1. `definitions/diagrams/sparql-query.dis` in etalii-adp/etalii.adp SHALL be rewritten at DISL 0.3, using the construct finding A2 names in place of each workaround, and SHALL validate against the DISL schema in that repository's checks.
2. It SHALL declare `persistence.format` `fbl` with `binding` naming the module's binding, the id rules that give every element the id it has today, the anonymous variable's ids as `ephemeral` with today's sentence as the reason, and an `x-sparql` block mapping its rows and element types to the wire ids the module uses (finding A12).
3. Its companion `sparql-query.md` SHALL be rewritten to say what DISL 0.3 cannot express, which is what section C lists and whatever Requirement 11 adds, and nothing that 0.3 can.
4. The definition SHALL be bundled into `src/diagrams/sparql/definition/` by `src/diagrams/tools/bundle-disl.sh`, never edited here, embedded in the backend project, and held by a `BundledDefinition.Tests` like the other bundled modules'.
5. WHEN the bundled definition is loaded by `EtAlii.Adp.Specification.Disl` THEN it SHALL load with no diagnostic of severity error.

### Requirement 3: The binding and its plugin

**User Story:** As a user, I want every `.rq` that opens today to open the same, read through a binding every host shares and a reader that keeps this tool's sentences.

#### Acceptance Criteria

1. The module SHALL own its binding, `backend/EtAlii.Adp.Diagram.Sparql/sparql.fbl`, embedded in the backend project: a plugin reader, `readOnly` with the tool's sentence, the claim on `.rq` and `w3c/sparql`, `createOnFirstPlacement` false, and `template.text` equal to today's starter query. An example binding SHALL be added to `specifications/fbl/` in etalii.adp, since none exists.
2. The plugin SHALL wrap `SparqlTokenizer` and `SparqlParser` and SHALL keep the three tiers of the grammar as they are: what is parsed, what is kept as written, and the refusal of a SPARQL Update document by name.
3. `read` SHALL deliver what Q1 rules, each element with the span and the line of what it was read from (finding B3), in document order.
4. `read` SHALL never throw on content. A body that does not parse SHALL be reported unreadable with one `std.unparseable` carrying the parser's sentence, line and column (finding B4), and SHALL open with no element of the query.
5. WHEN a body changes on disk THEN open diagrams SHALL follow as today, through the shared read-only lifecycle, and a change that does not parse SHALL show what Q2 rules.
6. The document factory SHALL hand out the binding's template text and hold no text of its own (finding B8).

### Requirement 4: The model is the definition's

**User Story:** As a maintainer, I want the drawn query to follow from the reading and the definition, so that the tool is the definition's and not a second opinion.

#### Acceptance Criteria

1. The DISL model SHALL be built by `DislModelBuilder` from the plugin's reading under the bundled definition.
2. Every element SHALL have the id it has today (`var:`, `anon:`, `iri:`, `lit:`, `sub:`, `region:`, `note:`, `edge:`), stated by the definition and the reading together, so that the three shipped `layout:` blocks apply unchanged.
3. WHERE Q1 is the default THEN the variable, term and anonymous nodes, their scope and the pattern and subquery edges SHALL be derived types of the definition, one variable per name placed at the shallowest scope that mentions it and lifted out of a `UNION`, and `SparqlProjection` SHALL leave the product code.
4. The order of elements SHALL be today's for every document of the corpus, and the cut at 500 SHALL keep the same elements and report the same two numbers.
5. WHEN the model's elements are compared with the transcript THEN payloads and positions SHALL be byte-identical.

### Requirement 5: Rows, findings and refusals are derived

**User Story:** As a maintainer, I want the rows, the findings and the refusals to come from the definition.

#### Acceptance Criteria

1. The property rows of every element SHALL be derived through `FormDerivation` and mapped to the wire ids of `x-sparql`: their labels, which follow state ("Variable" and "Anonymous variable", "Predicate" and "Property path", the construct in capitals), their groups, their values, and on every row the reason "This diagram reads the query; edit the .rq file in a text editor and the diagram follows."
2. The toolbox SHALL be derived and empty, and no context action SHALL be offered for any target.
3. The three findings SHALL be derived by `ConstraintEvaluator` with today's codes, severities, sentences and lines, the unparseable one alone when it occurs (finding A6), the unused prefix once per prefix at its line (finding A7), and the unbound projection where and as it is given today (Q3, Q4).
4. Every refusal SHALL keep today's sentence: the five refused moves, the re-parenting, a property write, an action, a move without a registration and a move without a history.
5. No finding, row, panel text or refusal the tool does not give today SHALL appear (Q3).
6. The hand-written providers' and validator's logic SHALL be kept in the test project as the oracle and removed from the product code.

### Requirement 6: The query is never written, and that stays proven

**User Story:** As an author of queries, I want the guarantee that this tool cannot change my file to survive its move onto a library that writes files.

#### Acceptance Criteria

1. The plugin's `Plan` SHALL refuse every change with the tool's sentence, and the binding SHALL be `readOnly`, so that two independent things each stop a write (finding B7).
2. The surface test SHALL be extended to the whole module: no type of the assembly may return splices that are not a refusal, call `Save` on a body, or hold a stream or a writer; and it SHALL be seen to fail against a plugin that plans one splice.
3. A test SHALL load the embedded binding and require `readOnly`, and SHALL be seen to fail with the key removed.
4. The behavioural sweep SHALL keep comparing the `.rq` bytes across every surface, the plugin's operations included.
5. The module SHALL still reference no HTTP client.

### Requirement 7: Positions and the registration

**User Story:** As an author who arranged a diagram, I want it to open where I left it.

#### Acceptance Criteria

1. Every position stored today SHALL be applied after the conversion, with no registration rewritten to make it so, regions and subqueries included (Q5).
2. A move SHALL stay core's `SetRegistrationLayoutCommand`, one undo step that restores the registration byte for byte.
3. A stored position that names no element SHALL be left in the registration and unreported, as today (Q3).

### Requirement 8: What stays code, and why

**User Story:** As a maintainer, I want the code that remains to be exactly what the languages cannot state.

#### Acceptance Criteria

1. The module SHALL keep: the tokenizer and the parser behind the plugin, the scope-grid layout as the definition's layout plugin, the element mapper with its viewport filter, the session, the store on the shared read-only lifecycle, the reloader with its `BodyDeleted` override, the selection resolver and the `.proto` payloads.
2. `SparqlValidator`, the logic of the two providers and, where Q1 is the default, `SparqlProjection` SHALL leave the product code.
3. WHEN the conversion is complete THEN the companion document SHALL list every remaining module class beside the gap or the host concern that keeps it, and a class with neither SHALL be removed.

### Requirement 9: The library work this tool needs

**User Story:** As a maintainer of the shared libraries, I want each thing this tool needs added once, for every tool.

#### Acceptance Criteria

1. WHERE Q1 is the default THEN `EtAlii.Adp.Specification.Disl` SHALL compute derived node types as DISL 4.11.2 and 4.11.5 define them, with `key`, `parent` and `sources`, and derived relations with `key` and `owner`, each proven by a library test seen to fail first.
2. It SHALL evaluate `ephemeral` id rules with their `reason`, and a constraint's `location` and `subject`; the design SHALL list what else of finding A5 this tool's backend derivations need, from a load of the rewritten definition.
3. `EtAlii.Adp.Specification.Fbl` SHALL pass a binding's `args` to a plugin (finding B12) only if a second tool needs it; this tool does not.
4. No module name SHALL appear in a library change.

### Requirement 10: The client

**User Story:** As a user, I want the canvas to look and behave as it does, drawn from the definition the backend runs.

#### Acceptance Criteria

1. The canvas definition SHALL be compiled from the bundled definition with `parseDisl` and `compileNotation` (Q6), with today's hand-written definition kept in a test as the oracle, and the compiled one SHALL draw the same markup for the corpus.
2. IF `compileNotation` cannot read something the definition states (the header band as chrome, the truncation notice, a frame drawn behind its contents, an edge that crosses a frame's border, the second label, the projection mark) THEN it SHALL be added to the shared library for every module and SHALL NOT be written in this module.
3. A `tests.md` entry SHALL cover in a real browser, in both themes: a shared variable drawn once with its edges crossing a region's border, the projection mark, the four frame styles, a dashed property path, the header band, the banner over 500 elements, each refused move with its sentence, a move that leaves the `.rq` untouched, and its undo.

### Requirement 11: Gaps are recorded and reported, never tuned away

**User Story:** As the owner of DISL and FBL, I want every place the languages fall short reported back with its evidence.

#### Acceptance Criteria

1. Every language gap, the seven of section C and any the implementation adds, SHALL be recorded in `sparql-query.md` with the construct concerned, the fixture or test that shows it, and what would resolve it.
2. A gap SHALL NOT be closed by weakening a check, skipping a fixture, or stating in the definition something the tool does not do.
3. The delivery report SHALL list every gap as a candidate change for etalii-adp/etalii.adp, every definition defect repaired and every module defect found, repaired or not.
4. WHEN a *read* finding of this document turns out not to occur THEN it SHALL be struck here with the fixture that shows it, as a small amendment.

### Requirement 12: Documentation, catalog and gates

**User Story:** As a reader of the repository, I want its pages to stay true through the conversion.

#### Acceptance Criteria

1. `src/diagrams/readme.md`'s list of modules with a `definition/` folder, `docs/creating-a-diagram-module.md`, and `docs/architecture.md` and `docs/solution-structure.md` where a sentence becomes false SHALL be updated in the change that makes them so; the module's `Diagram.cs`, `ServiceCollection.AddSparql.cs` and store remarks that say nothing here can write SHALL be reworded to say what stops a write now.
2. `docs/tools.md`'s row for `w3c/sparql` SHALL name this specification.
3. All four gates SHALL exit zero on every pull request of this specification, judged by captured exit codes, and a newly created worktree SHALL be built before a green gate is trusted about the bundled definition.
4. WHEN the tasks document is written THEN every acceptance criterion here SHALL be claimed by a task, established by diffing the two sets, and two traces SHALL be read back to their artefacts.

## Non-Functional Requirements

### Code Architecture and Modularity

- The module ends as a definition, a binding, a plugin over its own parser, and the code Requirement 8.1 names. It references no other diagram module, as today. Library additions are named for what they do, never for this tool.
- The house rules apply: `src/.editorconfig`, no blocking call in an `async` method, `TestContext.Current.CancellationToken` in tests, no unneeded `using`, CRLF in the working tree; `.rq` and `.dis` are byte-compared and already `-text`, and `.fbl` in the module follows what the hype cycle's has.

### Performance

- Opening and re-reading the largest `.rq` of the corpus SHALL not be perceptibly slower than today, the derivation of Q1 included; a query over the bound still degrades to its first 500 elements and never hangs.

### Reliability

- No file that opens today opens differently. No stored position is lost. No byte of a `.rq` is ever written (Requirement 6). Reading never throws on content.
- Every test written for a finding is seen to fail against that finding before it is trusted.

### Security

- Nothing executes a query or contacts an endpoint, and a `SERVICE` clause is drawn and never resolved; the plugin is the module's own code and installs nothing.

### Usability

- Every sentence a user reads today is read unchanged, unless this document names the change.

## Out of scope

- Editing a query through the diagram, executing one, and SPARQL Update. Each is refused by decision today and stays so.
- Changing DISL or FBL. Gaps are reported (Requirement 11); a language change is etalii.adp's own specification.
- The scope-grid layout's algorithm, the viewport streaming model and the shared read-only lifecycle.
- The other tools of the series, the RDF family included: this module shares no reader with it and gains none here.

## Appendix: the binding, as drafted

A draft against FBL 0.2, to be settled in the design. Every key in it is FBL 0.2. What cannot be in it is the sentence of finding B9, which FBL gives no place.

```json
{
  "fbl": "0.2",
  "bindings": {
    "query": {
      "title": "SPARQL 1.1 query",
      "claims": { "extensions": [".rq"], "origins": ["w3c/sparql"] },
      "body": { "kind": "file" },
      "reader": { "plugin": "net.etalii.adp.w3c.sparqlQuery", "version": "^1.0.0" },
      "readOnly": "A query diagram offers no edits: it reads the .rq file, which is edited in a text editor.",
      "text": { "newline": "crlf", "finalNewline": true },
      "registration": { "createOnFirstPlacement": false },
      "template": {
        "text": "PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>\r\n\r\nSELECT ?subject ?label\r\nWHERE {\r\n  ?subject rdfs:label ?label .\r\n}\r\nLIMIT 100\r\n"
      }
    }
  }
}
```

It has no rule, as FBL 3.1 requires of a plugin reader, so there is nothing in it to compare with the parser: the comparison is between what the plugin delivers and what the definition expects.

## Appendix: the definition's persistence, ids and wire ids, as drafted

The rest of the definition is the existing draft brought to 0.3 (Requirement 2.1). The blocks below are new and are written for the default of Q1. `shallowestScope` and `mentions` stand for functions of the definition's `functions` that the design writes; `key` on a stored literal record is the percent-encoded form the plugin delivers (section C, item 6).

```json
{
  "persistence": {
    "format": "fbl",
    "binding": "https://raw.githubusercontent.com/etalii-adp/etalii.adp.ide.standalone/develop/src/diagrams/sparql/backend/EtAlii.Adp.Diagram.Sparql/sparql.fbl#query",
    "view": { "store": ["bounds"] },
    "typeMap": { "Query": { "as": "diagram" } },
    "ids": {
      "strategy": "natural",
      "types": {
        "Scope": { "strategy": "derived", "expression": "'region:' + self.path" },
        "SubQuery": { "strategy": "derived", "expression": "'sub:' + self.parentPath + '.' + string(self.ordinal)" },
        "Annotation": { "strategy": "derived", "expression": "'note:' + self.scopePath + '/' + self.kind + '.' + string(self.ordinal)" },
        "AnonymousVariable": { "ephemeral": true,
          "reason": "That is an anonymous variable - written as a blank node, it has no name to key a stored position by, so it takes its computed place." }
      }
    }
  },
  "metamodel": {
    "types": {
      "Variable": {
        "extends": "Term",
        "derived": {
          "from": "mentions(diagram)",
          "key": "item.name",
          "id": "'var:' + item.name",
          "attributes": {
            "name": "item.name",
            "projected": "diagram.projection.exists(p, p == item.name)",
            "joinCount": "group.filter(m, m.inPattern).size()"
          },
          "parent": "shallowestScope(group.map(m, m.scope))",
          "reason": "This diagram reads the query; edit the .rq file in a text editor and the diagram follows."
        }
      }
    }
  },
  "constraints": {
    "builtIn": {
      "std.unparseable": { "code": "sparql.unparseable", "severity": "error",
        "message": { "cel": "'This is not a SPARQL query that can be read: ' + detail.reason" } }
    }
  },
  "plugins": {
    "net.etalii.adp.w3c.sparqlQuery": { "version": "^1.0.0", "provides": ["persistenceFormat"], "required": true, "plans": [] }
  },
  "x-sparql": {
    "elements": { "header": "header:query", "truncation": "truncation" },
    "types": {
      "Variable": "w3c/sparql+variable", "AnonymousVariable": "w3c/sparql+variable",
      "Iri": "w3c/sparql+term", "Literal": "w3c/sparql+term", "SubQuery": "w3c/sparql+term",
      "TriplePattern": "w3c/sparql+edge", "ProjectedJoin": "w3c/sparql+edge",
      "Scope": "w3c/sparql+region", "Annotation": "w3c/sparql+annotation",
      "diagram": "w3c/sparql+header", "budget:drawn": "w3c/sparql+truncation"
    },
    "properties": {
      "Variable/name": "sparql.variable.name", "Variable/projected": "sparql.variable.projected",
      "Variable/joinCount": "sparql.variable.joins", "Variable/definingExpression": "sparql.variable.definition",
      "Term/display": "sparql.term.display", "Term/full": "sparql.term.full", "Literal/annotation": "sparql.term.annotation",
      "SubQuery/text": "sparql.subquery.text",
      "Scope/kind": "sparql.region.kind", "Scope/label": "sparql.region.label",
      "TriplePattern/predicate": "sparql.edge.label", "Annotation/text": "sparql.annotation.text",
      "diagram/headerForm": "sparql.query.form", "diagram/modifiers": "sparql.query.modifiers"
    }
  }
}
```

Three things in it are unsettled and are the design's to close with fixtures: whether an empty `plans` is how a plugin declares that it plans nothing, or whether the binding's `readOnly` alone says so; whether a variable's `definingExpression`, which today is the last `BIND` or alias the parser met, is a derived attribute or a value of the reading; and how the module's node order (finding A11) is stated as the order of a `from`.

## Sources

- The module `src/diagrams/sparql/` at `develop` `9a646009`: `SparqlTokenizer.cs`, `SparqlParser.cs` (`IndexScope`, `SliceTokens`, the 29 `throw Error(` sites), `SparqlProjection.cs` (`OrderedUsages`, `NodeId`, `Bound`), `SparqlValidator.cs`, the two providers, `SparqlSession.cs`, `SparqlDocumentStore.cs`, `SparqlDocumentFactory.cs`, `ServiceCollection.AddSparql.cs`, `NoWriterSurface.Tests.cs`, `SparqlSession.Tests.cs`, `client/SparqlCanvas.tsx`.
- Counts: `git ls-files src/diagrams/sparql` filtered by extension and piped to `wc -l`; `git ls-files 'src/**/*.rq'` for the 31 query files; `grep -c "throw Error("` on the parser and a search for `throw` in the tokenizer for the 33 sentences.
- Finding B1: every `.rq` of the module printed with `cat -A` and read line by line for a `{` that does not end its line and a `}` that is not alone on its line.
- Finding B6: a search of the module's product code for `.Error`, `IsUsable` and `ErrorLine`, which finds `IsUsable` read twice in `SparqlSelection.cs` and `Error` read nowhere.
- Finding A5: a search of `src/backend/EtAlii.Adp.Specification.Disl` for the quoted keys `"ephemeral"`, `"budgets"`, `"location"`, `"subject"`, `"refusals"`, `"chrome"` and `"notices"`, each found in `DislExpressionWalker.cs` alone; `Model/DislModelBuilder.cs` (`DerivedIds`, `DerivedRelations`) read for what it computes.
- Findings B7 and B12: `src/backend/EtAlii.Adp.Specification.Fbl/Plugins/` (`IPersistencePlugin.cs`, `PluginBody.cs`, `PluginReadRequest.cs`), `FblModel.cs` and `Documents/FblModelTypes.cs` (`PluginReader`).
- Finding B11: `src/backend/EtAlii.Adp.Hierarchy/Commands/SetRegistrationLayoutCommandHandler.cs`, searched for `stale`, `prune` and `remove`; a search of `src/diagrams/*/backend` for `SetRegistrationLayoutCommand`.
- `src/backend/EtAlii.Adp.Specification.Fbl.Tests/RealFiles/divergences.json`, searched for `sparql` and `.rq` with no result.
- etalii-adp/etalii.adp at `develop` `da64ff0`: `definitions/diagrams/sparql-query.dis` and `.md` (section 12 for finding A2); `specifications/disl/DISL-specification.md` (0.3 draft: 3.2.1, 4.3, 4.11, 6.9's *GestureRefusals*, 6.13, 8.2, 8.6, 11.2, 11.5.2 to 11.5.4, 13.1, *Changes from 0.1*, *Changes from 0.2*; searched for `sparql`, and for `percent`, `encodeURI`, `urlEncode` and `escapeUri` with no function found); `specifications/fbl/FBL-specification.md` (0.2 draft: 3.1 to 3.4, 4.6, 4.7, 7.3 to 7.5, 8.1 to 8.5, 11.1 to 11.5, 13, 17); `specifications/fbl/w3c-turtle.fbl` and `helm-chart.fbl` as the two examples of a plugin reader.
- The agent behavior modelling module as the pattern of a plugin-read body: `agent-behavior-modelling.fbl`, `AbmBody.cs`, `AbmMarkdownPlugin.cs`; the hype cycle graph's `definition/gartner-hype-cycle-graph.dis` for `constraints.builtIn` on `std.unparseable` and for `x-ghg`.
- `tests.md` (the toolbox entry of 2026-09-03 that wired the Ansible canvas) and `docs/tools.md` for the tool's catalog row; `causal-loop-disl-fbl` and `timeline-disl-fbl` for the series, the survey and the shape of this document.
