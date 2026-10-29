# Requirements Document

## Introduction

FBL 0.9 (etalii-adp/etalii.adp, `88377a1`, 2026-10-25, spec 035 story 7) adds a seventh format family, `grammar`, for a format no other family reads: the binding carries an LALR(1) grammar in ATD grammar notation, inline or in a file beside the FBL document, which must build without a conflict or the binding is refused when it is loaded; its rules select the nodes of the tree that grammar reads by their names, a `{node}` slot reads the text of a node, with `match` when only part of it is the value, a node's splice is its byte range, text no rule maps is kept, new entries are written from `emit` and removed with their lines, and with `recover` a body that does not parse is read in part, the text that does not being kept and reported at its line (FBL sections 4.9, 5.2, 6.2, 6.3, 7.4, 14.1, 15.1).

This specification brings `EtAlii.Adp.Specification.Fbl` to it. The library reads FBL 0.7 today, implements 0.8's one construct without declaring 0.8, and has no `grammar` family at all. Peter chose on 2026-10-29 that the family comes to this library through a specification of its own, the recommended answer to the second open question of etalii.adp spec 066's plan (`specs/066-fbl-conversion-gaps/plan.md`, *Open questions*, 2; research R24).

It exists because two approved conversions wait on it before they can start the work they are waiting for. etalii.adp spec 066 extends the `grammar` family for Structurizr DSL (FBL 0.10, its pull request A) and for the Wardley map's `.owm` format (FBL 0.12, its pull request C), by the maintainer's ruling of 2026-10-27 that both formats are bound in that family. Every construct it adds is a construct of the 0.9 family, so `c4-disl-fbl` tasks 13 to 17 and `wardley-map-disl-fbl` tasks 11 to 13 cannot implement any of them in a library that has no family to extend. No standalone task names that prerequisite today; this specification is it.

**It is library work and nothing else.** No module reads through the family here, and no module's behaviour changes: every tool that reads through the library today is held to its own transcript and tests (Requirement 9). The 0.10 and 0.12 constructs are the next step, not this one (Requirement 10, *Out of scope*).

## What was measured

Read on 2026-10-29 at standalone `develop` `ad1a4680a` and etalii.adp `develop` `9676622`.

| Thing | State |
| --- | --- |
| The FBL version the library reads | `FblDocumentLoader` declares `new("FBL", 0, 7, …)`: a document of 0.8 or 0.9 loads with the newer-version warning of FBL section 2.7. |
| FBL 0.8 | 0.8 adds one construct, an insert's `constant` (FBL sections 6.2, 6.4, 14.1 step 6; the status paragraph says so). The library implements it (`Documents/BindingChecker.cs`, `FblModelTypes.cs`), and the conformance readme records that it does so "without declaring 0.8 in full": the four `insert-constant-*` fixtures and `refused/insert-constant-collision.fbl` are vendored and pass, loaded with the warning. Whether anything else of 0.8 is missing is the design's to establish (Requirement 2.2). |
| The families | `Files/` holds `Bullets`, `Json`, `Lines`, `Xml` and `Yaml`; `BindingReader.ParseFamily` knows no `grammar`. A body has no `grammar`, `start` or `recover`; a slot has no `{node}` or `match`. |
| Lark | No project of the solution references a Lark package. `EtAlii.Lark` is published on NuGet at 0.1.0, 0.2.1 and 0.4.0 (MIT, `net8.0` and `net10.0`). Its 0.4.0 documentation names LALR(1) and the contextual lexer, an interactive parser (`InteractiveParser` with `FeedToken`, `Accepts`, `Choices`, `Copy`), `Lark.Lex`, and terminal collision checks (`Regexes.RegexCollisions`). Whether that surface is what the family needs is the design's to establish; spec 066 research R6 asks the same of it. |
| The conformance corpus | `src/backend/EtAlii.Adp.Specification.Fbl.Tests/Conformance/`, vendored from etalii.adp at several commits, the latest `b057d7d` (2026-10-26). It holds `w3c-turtle.fbl`, the plugin binding the RDF module embeds, and none of the 0.9 files: not `w3c-turtle-grammar.fbl`, `w3c-turtle.lark`, `fixtures/w3c-turtle-grammar/` or `fixtures/w3c-turtle-grammar-partial/`. |
| What a grammar fixture asks | `w3c-turtle-grammar` reads two prefixes and two resources from a Turtle body with a leading and a trailing comment, saves it unchanged, sets a label inside its quotes, refuses a label holding `"` by the re-read check, adds a resource after the last one and removes one with its leading comment, each followed by its undo. `w3c-turtle-grammar-partial` reads a body whose fifth line has no closing quote, reports `fbl.unparsed-text` at line 5, reads the statements before and after it, and adds a resource with the skipped text kept. Both select by CEL over the tree (`entry.children[0]…type`) and by selector tests (`predicate_object[verb='rdfs:label']`). |
| What etalii.adp also publishes for the grammar | `specifications/atd/grammar/tables/w3c-turtle-grammar.turtle.tables.json`, the normalized LALR(1) automaton of that binding's grammar (ATD section 4.7), written from Python Lark. FBL section 4.9 says two implementations *can* show they build the same automaton with it; it does not require a host to. |
| Who references the library today | Sixteen projects: `EtAlii.Adp.Specification.Disl`, `EtAlii.Adp.Hierarchy`, two designers and twelve diagram modules, the RDF module among them. |

## Alignment with Product Vision

One definition per tool, interpreted by a core in each host, needs every host's core to read a body as the others do. The desktop host already reads FBL 0.9 with the `grammar` family on `etalii-lark` (spec 066 research R24), and its principle II requires the same bytes as this host for the same edit; until this library has the family, the reference host is the one that cannot read the format. It follows `structure.md`: the capability is added once to the shared library, named for what it does, and no module works around it. It follows Peter's rule of 2026-10-23 that every Lark and LALR(1) grammar use goes through the EtAlii Lark packages, so that a grammar has one verdict in every host.

## Requirements

### Requirement 1: The gap is shown first

**User Story:** As the owner of the library, I want the missing family seen to fail before it is built.

#### Acceptance Criteria

1. WHEN the work starts THEN the 0.9 files of Requirement 7.1 SHALL be vendored and the conformance runner SHALL be seen to fail on both grammar fixtures against today's library, for the reason the measurement gives: the binding's family is not one the library reads.
2. IF a fixture passes or fails for another reason THEN that reason SHALL be reported to the Scrum master as a selection before any library code is written.

### Requirement 2: Loading a 0.9 document

**User Story:** As a tool engineer, I want a binding in the `grammar` family loaded, or refused, as FBL says, so that a grammar that cannot read the same way in every host never reaches a body.

#### Acceptance Criteria

1. The loader SHALL read FBL 0.9 in full and SHALL read a document of version 0.8 or 0.9 without the newer-version warning of FBL section 2.7; a document of 0.10 or later SHALL still load with it.
2. IF the design finds a construct of FBL 0.8 that the library does not implement THEN it SHALL be implemented under this specification with a test seen to fail first, because declaring 0.9 declares 0.8.
3. The loader SHALL read `body.grammar` as the grammar's text or as `{"file": "<name>.lark"}` beside the FBL document, `body.start` with `start` as its default, and `body.recover` with its `unit` and `sync` (FBL section 3.2's table, section 4.9).
4. A grammar SHALL be read in ATD grammar notation (ATD section 4.1) with terminals in ATD's regular expression subset (section 3.5), and SHALL `%import` only from files beside the FBL document; a grammar outside the notation, an import from anywhere else, and a grammar file that does not exist SHALL refuse the binding with a problem at its JSON Pointer.
5. A grammar that does not build from `start` as an LALR(1) automaton with no shift/reduce conflict, no reduce/reduce conflict, and no two terminals a state accepts matching the same text SHALL refuse the binding, with a problem that names the conflict: its kind, the terminal and the rules (FBL section 14.1 step 6).
6. The loader SHALL refuse, each with a problem at its JSON Pointer: a `grammar` on a body of another family, and a `grammar` body without one; a `recover.unit` that is not a rule of the grammar, or a start rule whose one expansion is not `unit*`; a `recover.sync` entry that names no terminal of the grammar and is not the text of one of its anonymous string terminals; a segment of a rule's `at` or of a `{node}` slot that names no rule, alias or terminal of the grammar and is not `*`, `**` or, in a slot, `.`; a rule of the family without `at` or with `line`; a `{key}`, `{attribute}`, `{text}`, `{group}`, `{continuation}` or `{capture}` slot in the family; a `{node}` slot in another family; and a `match` without the named group `value` (FBL sections 4.9, 5.2, 14.1 step 6).
7. The library SHALL bound the size of a grammar it builds and of the automaton it builds from it, and SHALL refuse a binding beyond those bounds with a problem that says which was exceeded (FBL section 16). The bounds SHALL be stated in the design and SHALL admit every grammar etalii.adp publishes.
8. Each refusal of 2.4 to 2.7 SHALL be tested with a binding written for the test, seen to load before the defect is planted in it and refused after, since etalii.adp keeps no refused `grammar` binding yet.

### Requirement 3: The grammar is built with EtAlii.Lark

**User Story:** As the owner of ADP, I want every host to give a grammar the same verdict and the same tree, so that two hosts read one body alike.

#### Acceptance Criteria

1. The grammar SHALL be built, lexed and parsed with the `EtAlii.Lark` NuGet package, at 0.4.0 or later (Peter's rule of 2026-10-23); the library SHALL contain no table builder, lexer or parser of its own for the family.
2. Tokens SHALL be cut by the contextual lexing rule of ATD section 3.6: at each position the candidates are the terminals the parser's state can shift and the ignored ones; the longest match wins, then the higher terminal priority, then a string terminal over a pattern terminal.
3. IF the design finds that `EtAlii.Lark` lacks a surface the family needs THEN it SHALL be reported to the Scrum master as a selection naming the surface, so that it is requested from EtAlii.Lark, and SHALL NOT be written in the library.
4. The package SHALL be referenced through the solution's central package versions, as every other package is; its licence, MIT, is permissive.

### Requirement 4: Reading a body

**User Story:** As a tool engineer, I want the rules of a `grammar` binding to address the format's own constructs by their names, so that a format with a syntax of its own is bound declaratively instead of by a plugin.

#### Acceptance Criteria

1. Reading a body SHALL give the tree ATD section 4.5 shapes: a node per rule, named by the rule or by the alias of the alternative that matched; a `?rule` replaced by its child when it has one and a `_rule` by its children; a token of a named terminal a leaf named by its terminal, except a `_TERMINAL`; anonymous strings left out; an optional part that matched nothing kept as an empty child; the root the node of the start rule.
2. Every node and token SHALL have its own span in bytes of the body as FBL section 4.9 gives it, the tokens the tree leaves out included, so that the `.` that ends a Turtle statement is inside the statement's span; a node that matched no token SHALL have no span; a node's or token's text SHALL be the bytes of its own span.
3. An ignored token holding a character other than whitespace SHALL be a comment, the leading comment of the entry below it, and the trailing comment of an entry's last line SHALL be part of that entry's line span (FBL sections 4.1.1, 4.9).
4. A selector SHALL match as FBL section 4.9's table says: a name, `*`, `**`, `name[path='v']`, and `.` as a `{node}` slot's whole selector; an absolute selector from the root, a slot's relative to its entry.
5. A `{node}` slot SHALL read the text of the first node or token, in document order, its selector reaches from the entry; with `match`, the group `value` of a regular expression the whole text must match, whose span is then the value's span (FBL section 5.2). FBL does not say what a text that `match` does not match reads as; the design SHALL choose a reading that keeps the entry's bytes and reading tolerant (section 7.4), and Requirement 10.4 SHALL list it.
6. CEL SHALL see a node of a `grammar` body as a map with `type`, `text` and `children`, an optional part that matched nothing being null and a token's `children` empty (FBL section 4.1.4), in a rule's `when`, a slot's `value` and every other place `entry` is given.
7. An id stored with `id.from` a `{node}` slot, and a reference, SHALL be read and compared as the text the slot reads.
8. Text no rule maps SHALL be unbound and kept byte for byte, and a body read and saved without a change SHALL give no splice and the same bytes.

### Requirement 5: Reading a body in part

**User Story:** As a user, I want a body with one broken statement to show everything else it holds, so that a file being typed stays open.

#### Acceptance Criteria

1. Without `recover`, a body that the grammar does not read, or in which text is matched by no terminal the parser's state can shift, SHALL be unreadable (FBL section 7.5).
2. With `recover`, the body SHALL be read unit by unit as FBL section 4.9 *Reading in part* steps 1 to 3 say: a unit ends where the parser can accept it and the next text gives no token it can shift; where reading stops, the text from the unit's first byte to the end of the first synchronising token that ends after that place is skipped, lexing with every terminal of the grammar as a candidate, or to the end of the body when none follows.
3. The units read SHALL be the root's children in order, and skipped text SHALL be unbound, kept byte for byte, matched by no rule, and reported once as `fbl.unparsed-text`, a warning, at the line and column where reading stopped, with the length of the skipped text from there (FBL section 7.4).
4. A body that parses as a whole SHALL read the same with and without `recover`.
5. An edit of a body read in part SHALL keep the skipped text byte for byte, and the finding SHALL still be reported after it.

### Requirement 6: Writing a body

**User Story:** As a user, I want an edit to change only the bytes it concerns, so that a file another tool owns survives ADP.

#### Acceptance Criteria

1. A set SHALL be one `replace-value` of the value's span, the group `value` of the slot's `match` or the node's own span, with the new value as text, unescaped and unquoted; a number written as FBL section 6.3 *Numbers* says, `true` and `false` as such.
2. An add SHALL be one `insert-entry` of the rule's `emit`, its placeholders replaced as for `lines`, each line feed in `emit` written as the body's line ending followed by the new entry's indentation; placed `after-last`, `end`, `next-sibling`, `start` or `end-of-document` in the container `insert.container` selects, the root when it is absent, exactly as FBL section 6.3 *Where it goes* says, the body keeping or lacking its final line ending.
3. A removal SHALL be one `remove-entry` of the entry's line span, its leading comment included, or of its own span when it does not start and end its lines; a reference SHALL be rewritten by `rewrite-reference` of its value's span (FBL sections 5.7, 6.3).
4. The family SHALL plan no `insert-key` or `remove-key`: a set of an attribute whose node is absent from the entry SHALL be refused with a reason, and an attribute whose node is present SHALL only be set, never removed (FBL section 4.9 *Writing*).
5. Before an edit is applied, the changed entry SHALL be read again: a set after which the entry does not read with the value set, and an add after which the body does not read with one more entry of the rule having the add's values, SHALL be refused with a reason, and nothing SHALL be written (FBL sections 4.9, 6.4).
6. Every edit of the family SHALL have an exact undo and redo by bytes, refused on drift, as every other family's (FBL section 7).

### Requirement 7: Conformance

**User Story:** As the owner of FBL, I want this library held to the fixtures every host that supports the family must pass.

#### Acceptance Criteria

1. `w3c-turtle-grammar.fbl`, `w3c-turtle.lark`, `fixtures/w3c-turtle-grammar/` and `fixtures/w3c-turtle-grammar-partial/` SHALL be vendored from etalii.adp's `specifications/fbl/` at `88377a1` or later, unchanged, with the conformance readme naming the commit, and their bytes SHALL be kept as the source holds them, line endings included, because the fixtures' offsets are byte offsets.
2. Both fixtures SHALL pass byte for byte with the same splices, in the order the fixture lists them (FBL sections 6.5, 15.1); a refused step SHALL be refused.
3. A test SHALL show the runner fails on a copy of a grammar fixture with one planted wrong offset, so that the comparison is seen to compare for the family.
4. Every fixture vendored before SHALL pass, unchanged in what it expects.
5. The library SHALL say that it supports the `grammar` family (FBL section 15.1), in the conformance readme and wherever the library states the families it reads.

### Requirement 8: A host that cannot read a binding

**User Story:** As a user, I want a document the library cannot read through its binding to open read-only with a reason, rather than not at all.

#### Acceptance Criteria

1. A binding of the `grammar` family that uses a construct of a newer FBL version than the library reads SHALL load with the warning of FBL section 2.7, the unknown construct ignored, and the library SHALL NOT claim to read it.
2. A binding refused under Requirement 2 SHALL leave its documents to open as FBL section 15.1's last paragraph says, read-only if they can be read at all, with the refusal as the reason.

### Requirement 9: Nothing that works today changes

**User Story:** As a user of a tool that already reads through the library, I want it to read as it did.

#### Acceptance Criteria

1. The transcripts and fixture tests of every project that references the library today SHALL be reproduced unchanged.
2. IF one of them changes THEN the change SHALL be shown to the Scrum master as a selection before anything is regenerated: the library is adjusted so the tool reads as before, or the change is adopted on purpose.
3. A test SHALL change only where it pinned behaviour a criterion here changes, naming that criterion and changing only that far; the tests that expect the newer-version warning on a 0.8 document are the ones Requirement 2.1 names.
4. No module SHALL read through the `grammar` family under this specification; the RDF module SHALL keep reading Turtle through `w3c-turtle.fbl` and its plugin.
5. The real-file suite of `fbl-implementation` SHALL pass with `RealFiles/divergences.json` unchanged.

### Requirement 10: Documentation, gates and the next step

**User Story:** As a reader of the repository, I want its pages to stay true and the work that follows this one to be named.

#### Acceptance Criteria

1. The conformance readme, and `docs/architecture.md` and `docs/solution-structure.md` where a sentence becomes false, SHALL be updated in the change that makes them so.
2. All four gates and `inspect.sh` SHALL exit zero on every pull request of this specification, judged by captured exit codes.
3. The delivery report SHALL name the next step: the constructs etalii.adp spec 066 adds to the family, FBL 0.10 (pull request A, Structurizr DSL and the constructs shared with the Wardley map) under `c4-disl-fbl` task 13 and FBL 0.12 (pull request C, `.owm`) under `wardley-map-disl-fbl` task 11, each once its etalii.adp pull request is merged; and SHALL say which of `c4-disl-fbl` tasks 13 to 17 and `wardley-map-disl-fbl` tasks 11 to 13 this delivery unblocks and what each still waits on.
4. The delivery report SHALL list anything in FBL 0.9 or ATD sections 3.5, 3.6, 4.1, 4.4 and 4.5 that the implementation found unclear, as a candidate change for etalii.adp.
5. WHEN the tasks document is written THEN every acceptance criterion here SHALL be claimed by a task, established by diffing the two sets, and two traces SHALL be read back to their artefacts.

## Non-Functional Requirements

### Code Architecture and Modularity

- The change is inside `EtAlii.Adp.Specification.Fbl` and its test project, the family beside the five it has. No module name appears in the library. The house rules of `src/.editorconfig`, the three rules the gates refuse, and the rule of 2026-10-24 that new C# has no local functions apply, so the design places every helper as an instance method, a `private static` method or a shared helper.

### Performance

- Reading is one pass of the parser over the body, linear in its tokens (FBL section 16). A grammar is built once per loaded binding, not once per body read.

### Reliability

- Reading never fails on content (FBL section 7.4). A body read and saved without a change gives no splice and the same bytes, for every fixture, old and new. Every test written for a finding is seen to fail against it first.

### Security

- A grammar is data: its files are read only from beside the FBL document, and the bounds of Requirement 2.7 hold for every binding, whoever wrote it.

### Usability

- A refusal of a grammar names the conflict in words a tool engineer can act on: its kind, the terminal and the rules. The message of `fbl.unparsed-text` says where reading stopped.

## Out of scope

- The constructs FBL 0.10 and 0.12 add to the family under etalii.adp spec 066 (comments after a brace and across lines, recovery inside blocks, escapes, absent and positional parts, views of a `grammar` binding, the next-line brace, trailer segments, the legacy identities), and the 0.11 `yaml` constructs for Databricks. They follow under `c4-disl-fbl` task 13, `wardley-map-disl-fbl` task 11 and `databricks-disl-fbl` task 10, once spec 066's pull requests are merged (Requirement 10.3).
- Converting any tool, the RDF module's Turtle included.
- Comparing the library's automaton with `w3c-turtle-grammar.turtle.tables.json`. FBL does not require it of a host, and a grammar's one verdict is held by `EtAlii.Lark`'s own conformance with Lark; the fixtures are this library's test.
- The FBL implementations of the other hosts.

## Sources

- etalii-adp/etalii.adp at `9676622`: `specifications/fbl/FBL-specification.md` (0.9: sections 2.1, 2.7, 3.2, 4.1.1, 4.1.4, 4.2, 4.9, 5.2, 5.7, 6.1 to 6.5, 7.4, 7.5, 14.1, 15.1, 16), `fbl.schema.json`, `w3c-turtle-grammar.fbl`, `w3c-turtle.lark`, `fixtures/w3c-turtle-grammar/`, `fixtures/w3c-turtle-grammar-partial/`; `specifications/atd/ATD-specification.md` (sections 3.5, 3.6, 4.1, 4.4, 4.5, 4.7); the commit `88377a1` that introduced FBL 0.9.
- etalii.adp `specs/066-fbl-conversion-gaps/`: `fbl-conversion-gaps.spec.md`, `plan.md` (*What each pull request unblocks*, *Open questions* 2), `research.md` (R2, R6, R24), `contracts/grammar-family.md`.
- `src/backend/EtAlii.Adp.Specification.Fbl/` at `develop` `ad1a4680a`: `Documents/FblDocumentLoader.cs`, `Documents/BindingReader.cs` (`ParseFamily`), `Documents/BindingChecker.cs`, `Files/`; `src/backend/EtAlii.Adp.Specification.Fbl.Tests/Conformance/readme.md` for the vendored commits.
- `.spec-workflow/specs/c4-disl-fbl/tasks.md` tasks 13 to 17 and `.spec-workflow/specs/wardley-map-disl-fbl/tasks.md` tasks 11 to 13.
- The `EtAlii.Lark` 0.4.0 package on NuGet: its `.nuspec` and its XML documentation.
