# Requirements Document

**Amended 2026-10-12:** Requirement 8 added, as the approved design proposed (its card was approved in the dashboard on 2026-10-12). The Code Architecture requirement is widened for that requirement only, to name `EtAlii.Adp.Specification.Disl`.

## Introduction

FBL 0.5 (etalii-adp/etalii.adp, pull request 115, merged 2026-10-10 as `42da8ee`) says what a slot reads from a YAML or JSON scalar: its **written text** for a text attribute, an id and a reference, and its typed value for every other attribute, with three new options on an attribute binding (`read`, `quoted`, `fallback`), two new CEL variables (`text`, `style`), one new finding (`fbl.unconverted-value`) and attribute values in a fixture's `read`.

This specification brings `EtAlii.Adp.Specification.Fbl` to it. It exists because the second half of seven approved conversion specifications waits on exactly this: `timeline-disl-fbl`, `dependency-graph-disl-fbl`, `supply-chain-disl-fbl`, `sankey-disl-fbl`, `functional-decomposition-graph-disl-fbl`, `databricks-disl-fbl` and `azure-devops-pipeline-disl-fbl` each found that the library reads a label written `yes` as a boolean and an id written `007` as the number seven, and each stops there. The user asked on 2026-10-09 and again on 2026-10-10 and 2026-10-11 that this work proceeds.

**It is library work and nothing else.** No module is converted here, and no module's behaviour changes: every tool that reads through the library today is held to its own transcript and tests (Requirement 6).

## What was measured

Read on 2026-10-11 at `develop` `68df9dd3`.

| Thing | State |
| --- | --- |
| How the library reads a scalar | `TreeFamily.Cel` gives every scalar its typed value (a string, a long, a double, a bool or null), and a slot's read value is that typed value (`TreeFamily.cs`, `SlotRead`). There is no written text beside it. |
| How an id is read | `BodyReading.AssignId` makes the id from the slot's typed value through `NewText.Plain`. `dependency-graph-disl-fbl` finding B1 reports that `id: 007` becomes `7`. Marked *read*; Requirement 1 shows it first. |
| Attribute types | A caller may hand the library the type its specification gives an attribute (`FblOptions.AttributeType`), and `TypedValue.Reads` then makes an entry unreadable when a value does not convert. The Knowledge designer uses it. A caller that hands over nothing gets no type check. |
| The binding model | No `read`, `quoted` or `fallback`. The loader warns for any minor version above 0.1 and ignores what it does not know. |
| The conformance corpus | Vendored from etalii.adp at `4590c56` with additions at `2d5c11d` and, for the Knowledge designer, at `1559b53`. None is at 0.5. The runner compares the ids and types of a fixture's `read`, and has no attribute values to compare. |
| Who references the library today | Seven projects: the Knowledge designer, the agent activity diagram, the behavior model, the dependency graph, the hype cycle graph, the mind map and the timeline. How each uses it (a declared binding, a plugin reader, or its change types only) is the design's to establish before anything is changed. |

## Alignment with Product Vision

One definition per tool, interpreted by a core in each host, needs the core to read a file as its tool does. Until this lands, a hand-edited YAML format cannot be bound without changing what its files mean, which is why seven conversions stop halfway. It follows `structure.md`: the capability is added once to the shared library, named for what it does, and no module works around it.

## Requirements

### Requirement 1: The findings are shown first

**User Story:** As the owner of the library, I want each claimed defect seen to happen before it is repaired.

#### Acceptance Criteria

1. WHEN the work starts THEN a test SHALL read a body through a binding with a string attribute written `1.0`, `null` and `~`, an id written `007` and a reference written `007`, and SHALL be seen to fail against today's library for the reason the measurement gives: the values come back typed, and the id comes back as `7`.
2. IF a measured defect does not occur THEN it SHALL be struck from this document with the test that shows it, as a small amendment.

### Requirement 2: Written text

**User Story:** As a tool engineer, I want a text attribute to be what the author wrote.

#### Acceptance Criteria

1. The library SHALL give every scalar of the `yaml` and `json` families its written text as FBL 0.5 section 4.1.5 defines it: a plain scalar's characters, a quoted scalar's content with its escapes resolved, a block scalar's content, the empty text for a key with no value, a JSON string's content, and the literal characters of a JSON number, `true`, `false` and `null`.
2. A slot bound to an attribute whose type the caller gives as a string, and a slot with `read: "text"`, SHALL read the written text. A slot with `read: "typed"`, and a slot bound to an attribute of another type, SHALL read the typed value converted to that type (FBL 0.5 section 5.2).
3. WHEN the caller gives no type for an attribute and the binding gives no `read` THEN the slot SHALL read as it does today, so that a caller that hands over no types sees no change.
4. An id stored in a slot, and a reference, SHALL be read and compared as written text (FBL 0.5 sections 5.2, 5.3).
5. `map` SHALL translate the written text.
6. WHEN a value read as text is written THEN it SHALL read back through the same binding as the same text, the plain-safe test of FBL section 6.3 SHALL hold for it, and an attribute whose `style` is `plain` SHALL be written plain wherever YAML's syntax allows it.

### Requirement 3: A value that does not convert

**User Story:** As a tool engineer, I want one bad value not to cost the element.

#### Acceptance Criteria

1. WHEN an attribute binding has a `fallback` and its slot is present with a value that does not convert THEN the entry SHALL stay readable, the attribute SHALL be the fallback's `value`, and the bytes of the slot SHALL be kept (FBL 0.5 sections 5.2, 7.4).
2. The finding `fbl.unconverted-value` SHALL be reported as a warning at the value's span, with the attribute and the written text in its detail, unless the fallback's `report` is `false`.
3. WHEN there is no `fallback` THEN the entry SHALL be unreadable, as today.
4. WHEN an attribute binding has `quoted: "convert"` THEN a quoted YAML scalar SHALL convert from its written text; without it a quoted scalar SHALL not convert to a number or a bool.
5. WHEN another attribute of an element read through a fallback is written THEN the unconverted bytes SHALL NOT be touched.

### Requirement 4: Expressions

**User Story:** As a tool engineer, I want a rule to ask what was written.

#### Acceptance Criteria

1. A rule's `when`, a slot's `value` and a sidecar's `key` SHALL have `text` and `style` beside `entry`, for the `yaml` and `json` families, as FBL 0.5 section 2.4 defines them; `entry` SHALL keep its meaning.
2. `has(entry.k)` SHALL be true for a key with no value and false for an absent key.
3. An expression that uses `text` or `style` in a binding of another family SHALL be reported when the document is loaded.

### Requirement 5: Loading and conformance

**User Story:** As the owner of FBL, I want this library held to the fixtures every host must pass.

#### Acceptance Criteria

1. The loader SHALL read `read`, `quoted` and `fallback` on an attribute binding, SHALL refuse them where FBL 0.5 section 5.2 forbids them (a slot of the `xml`, `lines` or `blocks` family, a `value` or `capture` slot), and SHALL read a document of version 0.5 without the warning for a newer minor version.
2. The conformance corpus SHALL be vendored again from etalii.adp at the merge of pull request 115 or later, unchanged, with the readme naming the commit, and `timeline.fbl` and the fixture `timeline-written-text` SHALL be among it.
3. The conformance runner SHALL compare the `attributes` of a fixture's `read`: each attribute named has that value and that JSON type, a string for a text; attributes not named are not compared. It SHALL be given the attribute types of the timeline's definition for the timeline fixtures, as it is given the Knowledge designer's.
4. Every vendored fixture SHALL pass, the earlier ones unchanged in what they expect.
5. A test SHALL show the runner fails on a fixture whose expected attribute value is the typed value where the text is asked, so that the comparison is seen to compare.

### Requirement 6: Nothing that works today changes

**User Story:** As a user of a tool that already reads through the library, I want it to read as it did.

#### Acceptance Criteria

1. The transcripts and fixture tests of every project that references the library today SHALL be reproduced unchanged.
2. IF one of them changes THEN the change SHALL be shown to the Scrum master as a selection before anything is regenerated: the binding or the caller is adjusted so the tool reads as before, or the change is adopted on purpose.
3. The real-file suite of `fbl-implementation` SHALL pass, with `RealFiles/divergences.json` changed only where a listed divergence no longer occurs.

### Requirement 7: Documentation and gates

**User Story:** As a reader of the repository, I want its pages to stay true.

#### Acceptance Criteria

1. The conformance readme, and `docs/architecture.md` and `docs/solution-structure.md` where a sentence becomes false, SHALL be updated in the change that makes them so.
2. All four gates SHALL exit zero on every pull request of this specification, judged by captured exit codes.
3. The delivery report SHALL say, for each of the seven waiting specifications, which of its tasks this unblocks, and SHALL list anything in FBL 0.5 the implementation found unclear, as a candidate change for etalii.adp.
4. WHEN the tasks document is written THEN every acceptance criterion here SHALL be claimed by a task, established by diffing the two sets, and two traces SHALL be read back to their artefacts.

### Requirement 8: Attribute types from a DISL specification

**User Story:** As a tool engineer whose tool reads a declared binding under a DISL specification, I want the types of its attributes to come from that specification, so that no module hands the library a list of its own.

#### Acceptance Criteria

1. `EtAlii.Adp.Specification.Disl` SHALL give, from a DISL specification, the function `FblOptions.AttributeType` takes, and, from a DISL specification and a binding, the set `FblOptions.TimeAttributes` takes, each of its entries keyed `rule.attribute`.
2. A binding type SHALL be resolved through the specification's `persistence.typeMap`: to the element type its entry names, or to the diagram's own attributes where the entry names `diagram`. An attribute the entry renames SHALL be resolved to the attribute it is renamed to, and an attribute declared on a type the element type extends SHALL be found.
3. An attribute the `typeMap` keeps for the host SHALL be given the type `string`, so that it reads its written text (FBL 0.5 section 5.2).
4. A binding type the `typeMap` maps to `header` or to `unreadable` SHALL be given no type. A binding type the `typeMap` does not name SHALL be resolved as the `typeMap` resolves it everywhere else in that library, to the element type of its own name.
5. It SHALL be delivered once, in `EtAlii.Adp.Specification.Disl`, which references the FBL library and is not referenced by it, and SHALL need nothing from its caller but the specification and, for the time attributes, the binding, so that a module, and each of the seven waiting specifications, supplies no list of attribute types or of time attributes of its own.
6. `FblOptions.AttributeType` and `FblOptions.TimeAttributes` SHALL stay what the caller hands over: the library SHALL NOT apply the derivation by itself, and nothing SHALL call it within this specification, so that no caller changes and Requirement 6 holds for it as for every other step.
7. The derivation SHALL be tested against a specification with `extends`, with a renamed attribute and with a host attribute, and each test SHALL be seen to fail first.

## Non-Functional Requirements

### Code Architecture and Modularity

- The change is inside `EtAlii.Adp.Specification.Fbl` and its test project and, for Requirement 8 only, inside `EtAlii.Adp.Specification.Disl` and its test project. No module name appears in either library. The house rules of `src/.editorconfig` and the three rules the gates refuse apply.

### Performance

- Reading keeps one pass over a body: the written text is taken from the span the reader already holds, not from a second parse.

### Reliability

- A body read and saved without a change gives no splice and the same bytes, for every fixture, old and new. Every test written for a finding is seen to fail against it first.

### Usability

- The message of `fbl.unconverted-value` is a sentence a user can act on, naming the attribute and what was written.

## Out of scope

- Converting any tool. The seven specifications do that, each under its own tasks.
- The two neighbouring gaps FBL 0.5 leaves: a value that is one scalar or a sequence of scalars, and one entry read as several types.
- The FBL implementations of the other hosts.

## Sources

- etalii-adp/etalii.adp at `42da8ee`: `specifications/fbl/FBL-specification.md` (0.5: sections 2.4, 4.1.5, 5.2, 5.3, 6.3, 7.4, 15.3), `fbl.schema.json`, `timeline.fbl`, `fixtures/timeline-written-text/`, and `specs/015-fbl-scalar-as-written/`.
- `src/backend/EtAlii.Adp.Specification.Fbl/` at `develop` `68df9dd3`: `Rules/TreeFamily.cs`, `Rules/BodyReading.cs` (`AssignId`), `Rules/TypedValue.cs`, `FblModel.cs` (`FblOptions.AttributeType`), `Documents/FblDocumentLoader.cs`.
- `src/backend/EtAlii.Adp.Specification.Fbl.Tests/Conformance/readme.md` for the vendored commits.
- The seven conversion specifications named in the introduction, each its finding B1 (Databricks: B4).
