# Requirements Document

## Introduction

This spec standardizes **how files are read and written** across ADP's backend. Every diagram and editor module reads its document from disk and writes it back, and each has grown its own reader and writer. Some of that per-module code is genuinely specific — a `.dsl` and a `.tml` are parsed by different grammars, and nothing centralizes a grammar. But the *file-handling* underneath — how the bytes are opened, how a save is made atomic, which line endings are written, how a read that fails is treated — is the same problem solved repeatedly, and solved inconsistently, and the inconsistencies have been shipping the same bug more than once.

The goal is to find the reusable chunks, centralize exactly those, and prove — with a guard, not a promise — that new modules use the central path instead of hand-rolling the next copy of the same defect.

### Survey (as of develop `764c176b`)

The tree is moving under this spec — six agents are implementing, several adding new stores and writers — so every number here is taken at one commit and re-measured rather than inherited. These are this author's counts, verified directly, and they correct the starting inventory in three places.

**Stores — 11.** One per module: `AnsibleProjectStore`, plus ten `*DocumentStore`: azure-pipeline, c4, databricks, dependency-graph, helm-charts (`HelmChartStore`), mindmap, rdf, sparql, timeline, wardley.

**`*Document` classes — 8, not 7.** `C4Document`, `DatabricksDocument`, `DependencyGraphDocument`, `MindmapDocument`, `PipelineDocument`, `RdfDocument`, `TimelineDocument`, `WardleyDocument`. Each is `sealed` and stands alone: no shared base type, no common interface. (`C4Document` was missing from the starting inventory.)

**Writers — 10 classes, and *not* one per module.** `AdpFileWriter` (central) plus nine module-owned: `BundleWriter` and `JobWriter` (both helm), `FreeplaneXmlWriter` (mindmap), `DependencyGraphWriter`, `PipelineWriter`, `RdfWriter`, `TimelineWriter`, `WardleyWriter`, and `NoWriter` (a null-object). So helm has two writers, mindmap's is an XML serializer, and c4, databricks and sparql have no `*Writer` class at all — the "one writer per module" shape does not hold and the design must not assume it.

**Raw file-API usage in production code** (excluding tests, `bin`, `obj`): `File.ReadAllText` in 9 files, `File.WriteAllText` in 18, `File.ReadAllBytes` in 1, `new StreamReader` in 5, `File.Move` in 6. `SharedDocumentReader` is referenced in 12 files (the sites converted in the shared-read audit).

**The two central helpers that already exist**, both under `src/backend/EtAlii.Adp.Backend/Hierarchy/`:

- `SharedDocumentReader` — opens with `FileShare.ReadWrite | FileShare.Delete` so a concurrent writer wins and a temp-then-move publish can replace the file mid-read, accepting a possibly-torn read that every caller already treats as recoverable.
- `AdpFileWriter` — writes each file to a `~adp-<guid>.tmp` temporary and `File.Move`s it into place (atomic publish), and writes new content with `NewLine = "\r\n"` from that one constant.

### The duplication, measured — same vs merely similar

Several writers implement a *line-oriented parse-and-splice*: the document holds its raw lines, the model carries line references, and a write splices rather than regenerates — which is what keeps a hand-authored file byte-stable. But they are not all the same shape, and this spec must not merge two things that only rhyme:

- **Whole-line splice by line range** — `TimelineWriter` and `DependencyGraphWriter` are near-identical: the same `LineRange`, the same `document.Remove(new LineRange(...))` / `document.Replace(new LineRange(...), […])`, the same `FindKey`/`SetKey`/`RemoveKey` helpers. Dependency-graph was forked from timeline, and the two are *genuinely the same idea*. This is the clearest centralization win in the tree.
- **Intra-line fragment splice by character offset** — `WardleyWriter` edits *within* a line (`document.ReplaceLine(line, Splice(line, index, length, …))`), and `RdfWriter` goes furthest, mapping a byte/character `TerminatorOffset` to a line and column so a splice can touch one item inside a comma-separated list. These share the *concept* of offset-splice with each other but differ in sophistication, and differ in kind from whole-line ranges. They are similar, not same.

### The disciplines a centralization must preserve, not flatten

Each of these is a rule the codebase learned from a specific defect; a naive "one way to read, one way to write" would break at least one on purpose:

1. **Shared reads, but not everywhere.** `SharedDocumentReader` deliberately tolerates a torn read so a save is never refused. `TextFileBuffer` deliberately does the opposite — a strict `throwOnInvalidBytes` UTF-8 read that *refuses* rather than guesses, because an editor buffer that loaded a torn read would save the damage back. Both are correct; a single read policy cannot serve both.
2. **Atomic writes.** Temp-then-move, so a reader sees the file whole or not at all (`AdpFileWriter`, and the same pattern in the Wardley identities file and the C4 layout sidecar).
3. **Line endings, two rules that only look contradictory.** New content is written CRLF from one constant; a rewritten existing line keeps *its own* original terminator, because byte-stability of hand-authored files is a headline correctness property.
4. **Byte-identical round-tripping.** Fourteen `-text` entries in `.gitattributes` stop git normalizing files whose bytes are the test subject.
5. **Read-modify-write refusal semantics.** A refused read must refuse the *modify* rather than persist an empty over data it could not read, and a corrupt read (parseable-but-wrong) is a different case from an unreadable one.

### The bug this is really about

The duplication is not hypothetical. `RdfRegistrationHeaders` (line 37) and `DatabricksHeaders` (line 23) each read a registration with a plain `new StreamReader(registrationPath)` at default sharing — the exact contention that the shared-read audit removed from twelve sites. Two header helpers, written independently, neither existing when the audit ran, each reproducing the identical flaw. A copy of a file-handling pattern is a copy of its latent defects.

## Alignment with Product Vision

*Files are the source of truth.* If the tool cannot read and write those files consistently — same sharing, same atomicity, same byte-preservation — the truth is only as reliable as the least careful copy of the read/write code. Centralizing the file layer makes "files are the source of truth" a property of the whole product rather than of each module's diligence.

## Requirements

### Requirement 1 — A surveyed, evidence-backed catalog of what is reusable

**User Story:** As a maintainer, I want the design to name exactly which file-handling pieces are shared-worthy and which are module-specific by nature, so that centralization is targeted rather than a rewrite.

#### Acceptance Criteria

1. WHEN the design is written THEN it SHALL carry the survey above, refreshed against its own named commit, and classify every reader/writer as *centralizable*, *module-specific by nature* (a grammar, a serializer like `FreeplaneXmlWriter`), or *a duplicate to be replaced*.
2. WHEN two pieces of code are proposed for merging THEN the design SHALL show they are the same idea with evidence, not merely similarly named — the timeline/dependency-graph whole-line splice qualifies; intra-line offset splice (wardley, rdf) is a separate question answered separately.
3. WHEN a piece is left un-centralized THEN the design SHALL say why (genuinely specific, or too few consumers per Requirement 4).

### Requirement 2 — Centralize the read and write primitives, preserving every discipline

**User Story:** As a developer adding a module, I want one obvious way to read a document and one to write it, so I inherit the sharing, atomicity and line-ending rules instead of re-deriving them.

#### Acceptance Criteria

1. WHEN a module reads a user-editable document THEN it SHALL go through the central shared-read helper (`SharedDocumentReader`'s policy), and the two flawed header helpers (`RdfRegistrationHeaders`, `DatabricksHeaders`) SHALL be converted to it.
2. WHEN a module writes a document THEN it SHALL go through the central atomic-write helper (`AdpFileWriter`'s temp-then-move), and any hand-rolled atomic write SHALL be converted or justified.
3. WHEN new content is written THEN it SHALL use the single CRLF constant; WHEN an existing line is rewritten THEN its original terminator SHALL be preserved — the design states how one helper offers both without a caller choosing wrongly.
4. WHERE a strict read is required (`TextFileBuffer`) THEN the centralization SHALL keep it strict — a shared-read default must not silently apply to a buffer that would save a torn read back.
5. WHEN a read fails THEN the central helper SHALL distinguish *unreadable* (refuse the modify) from *corrupt/unparseable* (a recoverable state the write-side change event corrects), preserving today's read-modify-write semantics.

### Requirement 3 — A shared line-oriented document, where the evidence supports one

**User Story:** As a maintainer, I want the whole-line splice that timeline and dependency-graph both implement to live in one place, so a fix to byte-stable line editing is made once.

#### Acceptance Criteria

1. WHEN the whole-line `LineRange` splice model is centralized THEN `TimelineDocument`/`TimelineWriter` and `DependencyGraphDocument`/`DependencyGraphWriter` SHALL both consume it, and their byte-identical round-trip tests SHALL still pass unchanged.
2. WHEN intra-line offset splicing (wardley, rdf) is considered THEN the design SHALL decide explicitly whether it extends the same shared model or stays separate, with the reason — RDF's fragment-offset sophistication is the stress test for that decision.
3. WHEN a shared document/line abstraction is introduced THEN it SHALL NOT force a module whose format does not fit it (the XML mindmap, for one) to adopt it.

### Requirement 4 — The rule of three, and no rule forked into two homes

**User Story:** As a maintainer bitten before by one rule living in two files, I want centralization to happen only where it removes duplication, not create a premature abstraction.

#### Acceptance Criteria

1. WHEN a candidate has three or more real consumers THEN it is a pattern and SHALL be centralized; WHEN it has two THEN the design SHALL treat it as a coincidence unless it argues otherwise explicitly.
2. WHEN centralization completes THEN no discipline in this document SHALL exist in two independent implementations — one home per rule.

### Requirement 5 — Verified, not asserted

**User Story:** As a maintainer, I want a guard that fails when a module reaches for a raw file API where a central one exists, so the next copy of the header-helper bug cannot be written unnoticed.

#### Acceptance Criteria

1. WHEN a module uses a raw file API (`File.ReadAllText`, `new StreamReader` at default sharing, a hand-rolled temp-then-move) where a central helper exists THEN a test SHALL fail, naming the offending file and the central call to use instead — the analogue of the diagram-catalog and dependency guards already in the suite.
2. WHEN the guard is written THEN it SHALL allow the deliberate exceptions (`TextFileBuffer`'s strict read, `AdpFileWriter`'s own internals) by explicit allow-list with a reason, never by silence.
3. WHEN the centralization lands THEN the existing gates (backend tests, `dotnet format`, client tests, typecheck) SHALL stay green, and every converted module's byte-identical round-trip test SHALL still pass.

## Non-Functional Requirements

### Code Architecture and Modularity
- One home per file-handling rule; module code keeps its grammar and semantics and borrows only the file layer.
- The shared primitives live in `src/backend/EtAlii.Adp.Backend/` beside the existing `SharedDocumentReader`/`AdpFileWriter`, not in any module.

### Reliability
- Every discipline in the Introduction is preserved; the design names each and how it survives. No change may make a hand-authored file's unchanged save non-byte-identical.

### Performance
- Centralization must not turn a bounded read (a header scan that stops early) into a whole-file read; the central reader keeps the early-stop path (`OpenText`).

### Usability (for maintainers)
- Adding a new module's reader/writer should be *easier* through the central path than hand-rolling one — the guard makes the central path the path of least resistance, not an extra hoop.
