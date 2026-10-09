# Requirements Document

## Introduction

The user's request, verbatim (2026-10-09):

> Create spec workflow MCP specifications. One specification for converting one of the standalone diagram tools to FBL and DISL. i.e. analyse the current implementation, define the DISL and FBL and then write the specification on the refactoring., also focus on finding issues and aspects where the DISL and FBL languages do not yet cover everything.

This is the second of the fifteen specifications that reading gives (the series and the survey behind it are in `causal-loop-disl-fbl`, *Introduction*). It converts the **timeline** (`generic/timeline`, `.tml`).

**The timeline is half converted.** Its toolbox, context menus, property rows and findings are derived from a bundled DISL 0.3 definition today, and a parity transcript holds them. Its body is still read by `TimelineParser` and written by `TimelineWriter`, and its DISL model is built from the parser's reading and not from FBL's. This specification is the other half: the body read and written through an FBL binding, and the parser and the writer gone from the product code.

**The half that was left was left on purpose, and the reason is a language gap.** `TimelineDisl.cs` says why the model is built "from the module's own reader rather than through FBL": FBL's YAML reading types a plain scalar (`1.0`, `null`, `yes`), and a timeline keeps every value as the text it was written with. That is finding B1, and this conversion cannot finish until it is answered.

## What was measured

Read on 2026-10-09: the module at `develop` `9a646009`; its bundled definition (etalii.adp `8f52fc5d`); `specifications/fbl/timeline.fbl` and the FBL specification (0.2 draft) in etalii-adp/etalii.adp at `develop` `da64ff0`; and `EtAlii.Adp.Specification.Fbl` here.

| Thing | State |
| --- | --- |
| The module's backend | 3,908 lines of C#. `TimelineParser` (149 lines) reads with YamlDotNet; `TimelineWriter` (213) writes through the shared `LineSplice`; `TimelineDisl` builds the DISL model from the parser's reading. |
| The DISL definition | Bundled, DISL 0.3, with an `x-timeline` wire-id block and two plugins (the reading of times, and the row arrangement). Its `persistence` still says `format: "yaml"` with the keys of a DID definition. |
| The FBL binding | Exists in etalii.adp as an example, `yaml` family, vendored unchanged in `EtAlii.Adp.Specification.Fbl.Tests/Conformance/`. Nothing names it from the definition. |
| The parity transcript | Exists: `Parity/timeline.transcript.json`, over 17 fixtures, the examples and the shipped copies, with a scripted edit sequence per document and the hunks each step made. |
| The real-file check of the binding | `ModuleCrossCheck.Tests` reads the 23 `.tml` files under `src/` through the binding and compares ids per type with `TimelineParser`. Four divergences are recorded (findings B2, B3 and the unparseable fixture, which agrees in effect). |

**How each finding is known:** *recorded* is an entry of `RealFiles/divergences.json` or a sentence in the module's code, which a test or a reviewer already holds; *read* is a reading of the code against the specification text, not run. Requirement 1 turns every *read* finding into a fixture first.

## Findings

The three kinds are those of `causal-loop-disl-fbl`: a **definition defect** is repaired in the definition or the binding, a **runtime gap** in this repository's libraries, a **language gap** in etalii-adp/etalii.adp.

### A. The DISL definition

| # | Finding | Kind | Known |
| --- | --- | --- | --- |
| A1 | `persistence` declares `format: "yaml"` with `files`, `encoding`, `indent`, `newline`, `ordering`, `omitDefaults` and `timestamps`. With the binding it becomes `format: "fbl"` with `binding` and a `typeMap`, and DISL 11.2 forbids those keys beside it. The definition changes in etalii.adp and is bundled again, so its recorded sha changes. | Definition defect | Read |
| A2 | The definition's functions read an end that names nothing through a host attribute the module supplies (`writtenEnd`, from `TimelineTimes`). Under `typeMap` a dangling end is DISL's own (`std.references` with `detail.end`), and the function and its plugin entry may go. | Definition defect, to confirm in design | Read |

### B. The FBL binding against the module's parser and writer

| # | Finding | Kind | Known |
| --- | --- | --- | --- |
| B1 | **A value is the text it was written with.** A label written `yes`, `1.0` or `null` is that text to the timeline. FBL's YAML reading types a plain scalar by the core schema, and gives a slot no way to ask for the scalar as written. | Language gap | Recorded (`TimelineDisl.cs`, the reason the model is not built through FBL) |
| B2 | **A relation whose end names no element** is kept and reported as `timeline.dangling-connection`. FBL 5.4 creates no relation. The pattern the hype cycle uses answers it: the binding reads the entry as an element with reference attributes, and `typeMap` makes it a relation with an unset end. | Definition defect, with a known pattern | Recorded (`dangling.tml`, `combined.tml`) |
| B3 | **An id written more than once** is read as written by the module, to report it (`timeline.duplicate-id`) and to find the first holder. FBL gives a stored id to one holder and addresses a later one by its place. The hype cycle's `storedId` host attribute answers it. | Definition defect, with a known pattern | Recorded (`identity.tml`) |
| B4 | **The first element of a new timeline.** A new document is `timeline: 1` and `elements: []`, which is also the binding's own template. The writer opens the empty flow sequence into a bare `elements:` key before it appends. FBL 4.3 makes a flow collection one value whose only writable span is the whole collection, and the splice catalogue has no operation that opens one into block form; the planner here has no code for it and no fixture covers it. If that reading holds, FBL cannot add the first element to the document its own template creates. | Language gap and runtime gap | Read |
| B5 | **What a changed value writes.** The writer replaces the whole line with the key and a value quoted only where YAML needs it. That drops a trailing comment on the line and the author's choice of quotes. FBL replaces the value's span and keeps its style, so the comment and the quotes survive. The bytes after an edit differ, and FBL's are the smaller change. | Behaviour change to rule on (Q1) | Read |
| B6 | **Where a new key goes.** Giving a moment an end writes `end:` directly after the entry's first line. FBL writes it where the binding's `keys` order puts it, after `begin`. | Behaviour change to rule on (Q1) | Read |
| B7 | **The gap after a dash.** A new entry copies the indentation and the spaces after the `-` of the entries already there, so a document written `-   id:` stays so. FBL copies the previous sibling's indentation and aligns keys "with the column after `- `"; it does not say the gap is copied. `indentation.tml` exists to hold this. | Language gap, or none, to settle by fixture | Read |
| B8 | **A sequence entry that is not a mapping** is passed over in silence by the parser; the transcript shows no finding for the one in `combined.tml`. FBL reads it as an unreadable entry, which DISL reports. | Behaviour change to rule on (Q2) | Recorded (the transcript) |
| B9 | **The header.** The module reports nothing when `timeline: 1` is missing or says another version. The binding's `header` reports `fbl.header-mismatch`. | Behaviour change to rule on (Q2) | Read |
| B10 | **A key written twice in one mapping.** YamlDotNet refuses the document, so the timeline reports `timeline.unparseable`. FBL 4.4 says what a duplicate member means in JSON and 4.3 says nothing for YAML. | Language gap | Read |
| B11 | **Period or moment.** The parser reads an entry as a period when its `end` key holds a scalar, the empty one included. The binding asks `has(entry.end)`. They differ where `end` holds a mapping or a sequence, and may differ on an empty `end:`, which FBL may present as null. `times.tml` holds an empty `end:` and its ids agree per type today. | Definition defect or none, to settle by fixture | Read; the agreement is recorded |
| B12 | **Times.** The module reads every form .NET reads (`2026/07/01`, `July 5, 2026`) and writes what its commands format. The binding writes `time: "keep-precision"`. Whether a moved element whose begin was written in a .NET-only form comes back in the same bytes is not known. | To settle by fixture | Read |

### C. Language gaps, as they would be reported to etalii-adp/etalii.adp

1. **FBL: a slot that reads a YAML scalar as written** (B1). Candidate: an attribute option `as: "text"`, or the rule that a slot bound to a DISL `string` attribute reads the scalar's text whatever the core schema would type it as. This one blocks the conversion.
2. **FBL: opening an empty flow collection** (B4). Candidate: an `open-flow` splice beside `open-block`, replacing `[]` by nothing and writing the first entry in block form under the key.
3. **FBL: a relation with an end that names nothing** (B2), and **an id held twice** (B3). DISL 0.3 answers both from its side, but only when the binding reads a relation as an element and carries a `storedId`. Every binding of a hand-edited format will need the same two detours; FBL 5.4 and 5.3 could say it once.
4. **FBL: the gap after a dash in a new sequence item** (B7), if the fixture shows FBL's rule writes other bytes.
5. **FBL: a duplicate key in YAML** (B10).

## Open questions

Each is put to the user as a selection; the **(default)** is what this document is written against.

| # | Question | Options | Criteria affected |
| --- | --- | --- | --- |
| Q1 | When an edit changes one value or adds one key, what is written? | **(default) What FBL writes**: the value's own span, in its own style, a new key where the binding orders it; a trailing comment and the author's quotes survive; the transcript's edit hunks are regenerated once and reviewed as a diff. · **What the writer writes today**: the transcript stays byte-identical, and FBL needs a way to state a whole-line rewrite and a key's place after the first line, which is a language change first. · Other. | 3.2, 3.3, 1.3 |
| Q2 | Findings FBL and DISL would add that the timeline does not give today (a missing header, a sequence entry that is not a mapping). | **(default) Switched off in this conversion**, each raised afterwards as a small change of its own. · **Adopted now**, each named in the transcript's diff. · Other. | 2.5 |
| Q3 | Finding B1 blocks the conversion until FBL answers it. What happens meanwhile? | **(default) Report the gap and wait**: the requirements stand, the design is written when FBL has the construct, and nothing is built on a workaround. · **Work around it**: the module keeps a small reader of its own for the scalars FBL types, named as standing in for the gap. · **Narrow the format**: a label that reads as a number or a boolean must be quoted, and the examples are checked for it. · Other. | 2.2 |

## Alignment with Product Vision

The product direction (2026-09-26) is one definition per tool, interpreted by a core in each IDE. The timeline is the tool FBL's own examples lead with ("the simplest complete binding"), and it is the one whose module could not use that binding. Closing that is the most direct evidence that FBL fits a hand-edited YAML format, and what it finds is owed to the three other hosts before they build on the same binding.

## Requirements

### Requirement 1: Every finding has a fixture first

**User Story:** As the owner of the tool, I want each claimed difference shown before anything is changed.

#### Acceptance Criteria

1. WHEN the work starts THEN one fixture SHALL be added to the corpus for each finding of table B marked Read (B4 to B7, B9 to B12), the transcript regenerated from today's unchanged code, and the diff of the checked-in file reviewed as showing only the new documents.
2. Each *read* finding SHALL then be shown by a test that reads or edits its fixture through the vendored binding and compares with the transcript, seen to fail for the reason the finding gives, or the finding SHALL be struck from this document with that test as the reason.
3. WHEN a later step changes the transcript THEN the change SHALL be one this document names (Q1, Q2), regenerated on purpose. Any other difference is a regression.

### Requirement 2: The body is read through the binding

**User Story:** As a user, I want every `.tml` that opens today to open the same.

#### Acceptance Criteria

1. The module SHALL own its binding, `backend/EtAlii.Adp.Diagram.Timeline/timeline.fbl`, embedded in the backend project; the definition's `persistence` SHALL name it with a `typeMap` (finding A1), and the definition SHALL be bundled again with `bundle-disl.sh`.
2. Every value SHALL be read as the text it was written with (finding B1), by the construct FBL gains for it (Q3).
3. The DISL model SHALL be built by `DislModelBuilder` from the binding's reading, and `TimelineDisl` SHALL leave the product code.
4. A relation whose end names no element SHALL be kept, not drawn, and reported as `timeline.dangling-connection` with today's sentence (finding B2); an id written more than once SHALL be reported as `timeline.duplicate-id` on the lines it is reported on today, and an element found by its written id as today (finding B3).
5. No finding the timeline does not give today SHALL appear (Q2), and every finding it gives SHALL keep its code, severity, sentence, location and place in the order.
6. A body that is not YAML SHALL open empty and read-only with the one finding `timeline.unparseable`, as today.
7. WHEN the answers are compared with the transcript THEN the menus, rows, findings and payloads of every document SHALL be byte-identical.

### Requirement 3: Every edit is written by splices

**User Story:** As a user editing a file I also edit by hand, I want an edit to change what I changed and nothing beside it.

#### Acceptance Criteria

1. Every command of the module SHALL write through `EtAlii.Adp.Specification.Fbl` as a model change; no module code SHALL build a line of YAML, and the module SHALL no longer use `LineSplice`.
2. WHEN a value changes THEN only its span SHALL change, in its own style (Q1): a trailing comment on its line and the author's quotes SHALL survive.
3. WHEN a key is added THEN it SHALL go where the binding's `keys` order puts it (Q1), and WHEN an entry is added THEN its indentation SHALL be that of the entries already there, the gap after the dash included (finding B7).
4. The first element SHALL be addable to a new document (finding B4), and the first relation SHALL create the `connections:` key at the end of the document, as today.
5. Removing an element SHALL remove the relations that name it, in one undo step, with the count known beforehand as today.
6. Undo SHALL restore the body byte for byte, through the host's shared restore command, a `connections:` key the edit created included.
7. A document ADP did not change SHALL be written back byte-identical, for every document of the round-trip corpus, the pair of line-ending fixtures and the one without a final newline included.
8. Every refusal SHALL keep today's sentence.

### Requirement 4: What stays code, and what goes

**User Story:** As a maintainer, I want what remains in the module to be what the languages cannot state.

#### Acceptance Criteria

1. `TimelineParser`, `TimelineWriter` and `TimelineDisl` SHALL leave the product code. What a test needs of them as an oracle SHALL live in the test project.
2. The module SHALL keep the row arrangement and the reading of times (the definition's two plugins), the scale, the element mapper, the session and the document store.
3. WHEN the conversion is complete THEN `timeline.md` in etalii.adp SHALL list every remaining module class beside the gap or the host concern that keeps it.

### Requirement 5: The library work this tool needs

**User Story:** As a maintainer of the shared libraries, I want each thing this tool needs added once, for every tool.

#### Acceptance Criteria

1. `EtAlii.Adp.Specification.Fbl` SHALL implement what FBL gains for findings B1 and B4, each proven by a library test seen to fail first and by a conformance fixture vendored from etalii.adp.
2. A defect of the library that a fixture of Requirement 1 shows SHALL be repaired in the library; a language gap SHALL NOT be repaired here.
3. No module name SHALL appear in a library change.

### Requirement 6: Gaps are recorded and reported, never tuned away

**User Story:** As the owner of FBL, I want every place it falls short reported with its evidence.

#### Acceptance Criteria

1. Each language gap of section C, and any the implementation adds, SHALL be recorded in `timeline.md` with the construct concerned, the fixture that shows it, and what would resolve it.
2. The timeline's entries of `RealFiles/divergences.json` SHALL be removed when they no longer occur and SHALL NOT be removed otherwise; the example binding in etalii.adp SHALL be brought in line with the module's or the difference recorded.
3. A gap SHALL NOT be closed by weakening a check, skipping a fixture, or changing a fixture that was written before the code.
4. The delivery report SHALL list every gap as a candidate change for etalii-adp/etalii.adp.

### Requirement 7: Documentation and gates

**User Story:** As a reader of the repository, I want its pages to stay true.

#### Acceptance Criteria

1. `docs/creating-a-diagram-module.md`, which describes the timeline as its worked example, and `docs/architecture.md` and `docs/solution-structure.md` where a sentence becomes false, SHALL be updated in the change that makes them so.
2. All four gates SHALL exit zero on every pull request of this specification, judged by captured exit codes, with a newly created worktree built before a green gate is trusted about the bundled definition.
3. WHEN the tasks document is written THEN every acceptance criterion here SHALL be claimed by a task, established by diffing the two sets, and two traces SHALL be read back to their artefacts.

## Non-Functional Requirements

### Code Architecture and Modularity

- The module ends as its definition, its binding and the code Requirement 4.2 names. The house rules of `src/.editorconfig` and the three rules the gates refuse apply.

### Performance

- Opening, validating, editing and arranging the largest `.tml` of the corpus SHALL not be perceptibly slower than today; an arrangement that writes many rows SHALL be planned as one edit, as the hype cycle's is.

### Reliability

- No file that opens today opens differently. An unchanged document is written back byte-identical. Undo restores bytes. Every test written for a finding is seen to fail against it first.

### Usability

- Every sentence a user reads today is read unchanged, unless this document names the change.

## Out of scope

- The toolbox, menus, rows and findings: they are derived already, and this specification changes none.
- The client: it compiles its notation from the bundled definition already.
- Changing FBL. The gaps are reported, and a language change is etalii.adp's own specification; Q3 says what this work does until then.
- The other tools of the series.

## Appendix: the binding, as drafted

A draft against FBL 0.2. It differs from the example in etalii.adp where findings B2 and B3 say so: a relation is read as an element with reference attributes, and every entry carries the id it was written with as a host attribute. `"as": "text"` is not FBL: it marks where the construct of finding B1 is needed.

```json
{
  "fbl": "0.2",
  "bindings": {
    "timeline": {
      "title": "Timeline Markup Language",
      "claims": { "extensions": [".tml"], "origins": ["generic/timeline"] },
      "body": { "kind": "file", "family": "yaml" },
      "reader": "declared",
      "text": { "newline": "crlf", "indent": 2, "sequenceIndent": "indented", "finalNewline": true, "quote": "double" },
      "elements": [
        {
          "name": "period", "type": "Period", "at": "/elements/*", "when": "has(entry.end)",
          "id": { "from": { "key": "id" } },
          "attributes": {
            "storedId": { "key": "id", "as": "text", "readOnly": true },
            "label": { "key": "label", "as": "text", "empty": "keep" },
            "begin": { "key": "begin", "as": "text", "time": "keep-precision" },
            "end": { "key": "end", "as": "text", "time": "keep-precision", "empty": "remove" },
            "row": { "key": "row", "default": 0 }
          },
          "insert": { "place": "after-last", "container": "/elements", "keys": ["id", "label", "begin", "end", "row"] },
          "remove": { "cascade": ["connection"] }
        },
        {
          "name": "moment", "type": "Moment", "at": "/elements/*", "when": "!has(entry.end)",
          "id": { "from": { "key": "id" } },
          "attributes": {
            "storedId": { "key": "id", "as": "text", "readOnly": true },
            "label": { "key": "label", "as": "text", "empty": "keep" },
            "begin": { "key": "begin", "as": "text", "time": "keep-precision" },
            "row": { "key": "row", "default": 0 }
          },
          "insert": { "place": "after-last", "container": "/elements", "keys": ["id", "label", "begin", "row"] },
          "remove": { "cascade": ["connection"] }
        },
        {
          "name": "connection", "type": "Connection", "at": "/connections/*",
          "id": { "from": { "key": "id" } },
          "attributes": {
            "storedId": { "key": "id", "as": "text", "readOnly": true },
            "from": { "key": "from", "as": "text", "reference": { "to": ["period", "moment"], "by": "storedId" } },
            "to": { "key": "to", "as": "text", "reference": { "to": ["period", "moment"], "by": "storedId" } },
            "label": { "key": "label", "as": "text", "empty": "remove" }
          },
          "insert": { "place": "after-last", "container": "/connections", "create": { "at": "end-of-document" }, "keys": ["id", "from", "to", "label"] },
          "remove": { "container": "keep" }
        }
      ],
      "registration": { "createOnFirstPlacement": false },
      "template": { "text": "timeline: 1\r\nelements: []\r\n" }
    }
  }
}
```

It declares no `header`, so that a missing one raises no finding (Q2). `storedId` is stated as the hype cycle's binding states it. The template still writes `elements: []`, which is finding B4 and the design's to close.

## Appendix: the definition's persistence, as drafted

```json
{
  "persistence": {
    "format": "fbl",
    "binding": "https://raw.githubusercontent.com/etalii-adp/etalii.adp.ide.standalone/develop/src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline/timeline.fbl#timeline",
    "ids": { "strategy": "uuid-v4", "encoding": "base36", "stable": true },
    "view": { "store": [] },
    "typeMap": {
      "Period": { "as": "Period", "hostAttributes": ["storedId"] },
      "Moment": { "as": "Moment", "hostAttributes": ["storedId"] },
      "Connection": { "as": "Connection", "attributes": { "from": "source", "to": "target" }, "hostAttributes": ["storedId"] }
    }
  }
}
```

## Sources

- The module `src/diagrams/timeline/` at `develop` `9a646009`: `TimelineParser.cs`, `TimelineWriter.cs`, `TimelineDisl.cs`, `TimelineDocumentFactory.cs`, `definition/timeline.dis` and its `provenance.json`, `Parity/timeline.transcript.json` (the findings of `combined.tml` for B8), and `Fixtures/readme.md`.
- `src/backend/EtAlii.Adp.Documents/LineSplice.cs` (`SetKey`, `Quote`, `IndentOf`, `InsertionPointFor`) for findings B4 to B7.
- etalii-adp/etalii.adp at `develop` `da64ff0`: `specifications/fbl/timeline.fbl`; `specifications/fbl/FBL-specification.md` sections 4.3, 5.3, 5.4, 6.1 and 6.3; `specifications/disl/DISL-specification.md` section 11.2.
- `src/backend/EtAlii.Adp.Specification.Fbl.Tests/RealFiles/divergences.json` for B2 and B3, and the conformance fixtures `timeline-edits` and `timeline-bom-lf`, neither of which adds an entry to an empty flow sequence; a search of `EtAlii.Adp.Specification.Fbl/Planning/` for `flow` found nothing.
- The hype cycle graph module for the patterns of B2 and B3: `gartner-hype-cycle-graph.fbl` and the `typeMap` of its definition.
- `causal-loop-disl-fbl` for the series, the survey and the shape of this document.
