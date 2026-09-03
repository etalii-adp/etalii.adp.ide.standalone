# Requirements Document

## Introduction

This spec covers **one diagram type, `w3c/shacl`** — a SHACL shapes file drawn as the description it states. It is a reading over the machinery [`rdf-diagram`](../rdf-diagram/requirements.md) defines for the family: the same store, the same serializations, the same edit discipline, referenced below by their bold names and never restated. A rule that appears both here and in the anchor means one of the two is wrong.

| Origin | File visualized | What it shows |
| --- | --- | --- |
| `w3c/shacl` | A Turtle (`.ttl`) or N-Triples (`.nt`) shapes graph | The shapes: node shapes as cards, property shapes as constraint rows, targets as declarations, shape-to-shape references as edges — drawn, never executed |

**A shapes graph is about another graph.** That is the fact this spec bends around, and the trap its siblings do not have. An RDF, OWL or SKOS file mostly describes the terms it contains; a SHACL file describes constraints over data that is somewhere else entirely, and its targets — `sh:targetClass`, `sh:targetNode`, `sh:targetSubjectsOf`, `sh:targetObjectsOf` — name that absent data. A naive graph rendering therefore draws edges pointing into nothing, or worse, invents nodes for data it has never seen. This spec's first owned position (Requirement 1) is that a target is a **declaration drawn on the shape** — the card says what it aims at — and that this reading never fabricates an element for a term the file does not describe.

**The format, as researched.** SHACL (W3C Recommendation, 2017) is itself RDF: a shapes graph is a set of triples in the serializations the family already parses. Node shapes constrain focus nodes; property shapes constrain the values reached over a `sh:path` — a predicate, or a path expression (inverse, sequence, alternative, zero-or-more and friends). Targets select focus nodes by class, node, subject-of or object-of, plus the implicit class target (a subject that is both class and shape targets itself). Constraint components cover cardinality (`sh:minCount`/`sh:maxCount`), value type (`sh:datatype`, `sh:class`, `sh:nodeKind`), string and range facets (`sh:pattern`, `sh:minLength`, `sh:minInclusive`, …), enumerations (`sh:in`, `sh:hasValue`), shape references (`sh:node`, `sh:property`, `sh:qualifiedValueShape`), logic (`sh:and`, `sh:or`, `sh:xone`, `sh:not`) and closedness (`sh:closed` with `sh:ignoredProperties`). Shapes carry `sh:severity` (Violation, Warning, Info), `sh:message`, `sh:name`/`sh:description`, and `sh:deactivated`. SHACL-SPARQL adds `sh:sparql` constraints holding query text. A subject is a shape by declaration or by use — typed `sh:NodeShape`/`sh:PropertyShape`, the object of `sh:node` or `sh:property`, or the subject of a target or constraint parameter — the recommendation's own definition, adopted verbatim for the projection. Crucially for Requirement 3: the recommendation's examples and the shapes graphs the world publishes write property shapes as **anonymous blank nodes** (`sh:property [ sh:path … ]`) almost without exception.

## What this spec references, and what it owns

Referenced from the anchor, by name, rules unrepeated: the **triplestore document store**, the **serialization boundary**, the **splice discipline**, the **blank-node identity boundary**, the **drawn-element budget**, the **layout overlay**, the **routing arrangement**, the **no-network, no-inference rule**, and the **example vendoring rule**.

The anchor's tenth item, the **registration header facility**, is deliberately not consumed — Requirement 4.5 states the choice and its reason.

Owned here: the `w3c/shacl` projection (which triples become which elements), its visual language with the research below, its reading-specific validation, its editing surface and refusals, its vendored examples, and its catalog row. The module code joins `src/diagrams/rdf/` as further `DiagramDefinition`s over the family store — the C4/databricks one-engine arrangement — naming no new module folder.

## How SHACL shapes are visualized — research, and the positions taken

**What the shapes tools show.** SHACL Play — the tool the catalog row already names — generates application-profile documentation as a table of allowed properties per class, and draws shapes as **UML diagrams** (SVG via PlantUML): a node shape is a class box, a datatype-constrained property shape is an attribute line with a `[min..max]` multiplicity, and an object-constrained one is an association to the box of what it points at. The WESO group's RDFShape renders shapes in the same UML register. TopBraid EDG comes at it from the editing side and renders shapes as data-entry forms — a different surface, same insight: a shapes graph is a schema, and readers want it shown as one. The documented failure mode is equally clear: open a shapes file in a generic RDF grapher and every `[ sh:path …; sh:minCount 1 ]` becomes a three-node blank constellation — the soup this reading exists to replace.

**Adopted, with reasons.**

- **The UML-card convention** (SHACL Play's, transposed to this repository's card idiom): a node shape is a card titled by its prefixed name; each property shape it references is one **row** — the path, a constraint summary, the cardinality as `[min..max]` (`*` where `sh:maxCount` is absent), `sh:name` shown when the author wrote one. Rows-not-nodes is also what dissolves the blank-node problem — Requirement 3.
- **Targets as declarations on the card** — "targets class `ex:Person`" as a chip, one per target triple. Every surveyed tool that draws targets draws them on the shape, because the data end of the arrow does not exist in the file.
- **Shape references as edges** — `sh:node` to the referenced shape's card; a logical operand (`sh:and`/`sh:or`/`sh:xone`/`sh:not`) to its card with the operator on the edge; `sh:class` to a card only where exactly one drawn card claims that class as its target, a row otherwise, so the mapping stays deterministic.
- **Severity and deactivation worn, not hidden** — a non-default `sh:severity` as a badge; a deactivated shape dimmed with a badge, because a reader auditing shapes needs to see what is switched off.
- **Complex paths as path expressions** — `^ex:parent/ex:member` printed on the row as SHACL path syntax, never expanded into plumbing.

**Rejected, with reasons.** Drawing constraint blank nodes as free-floating nodes (the soup — the exact naive projection the tradition documents as unreadable). Fabricating nodes for targeted-but-absent data (a diagram that lies about what the file contains). Any execution or preview validation (Requirement 4). Form-style rendering (TopBraid's) — an editor's projection rather than a diagram's; that instinct lands in this repository's property grid instead.

## Alignment with Product Vision

- [product.md](../../steering/product.md)'s **"Files are the source of truth"** — the diagram renders the shapes file a repository already validates its data with; edits are reviewable splices; positions live in the `.adp`'s `layout:` block, never in the RDF.
- product.md's **"Don't reinvent, integrate"** — SHACL is a W3C recommendation and the visual language is adopted from its tooling tradition with citations, not invented.
- product.md's **"Linkage over illustration"** — every card is a shape in a real file; selection resolves it; edits land as diffs.
- [tech.md](../../steering/tech.md)'s **Diagram storage** and **Commands** — byte-identical round trips through the family store; every edit an `ICommand` with an inverse.
- [structure.md](../../steering/structure.md)'s **dependency direction** — further definitions inside `src/diagrams/rdf/`, depending on core only.

## Requirements

### Requirement 1 — The projection: shapes, and only shapes

**User Story:** As a developer maintaining SHACL shapes, I want the shapes file drawn as the schema it states — shapes, constraints, targets — so that I can review a profile without mentally assembling blank-node syntax.

#### Acceptance Criteria

1. WHEN the diagram opens THEN every IRI-named node shape — a shape by the recommendation's declaration-or-use definition — SHALL draw as a card titled with its prefixed name (the store's fallback chain), and subjects that are not shapes SHALL NOT draw in this reading: co-resident ontology or instance content belongs to the sibling readings of the same file over the same **triplestore document store**, not to this one.
2. WHEN a card's shape references property shapes via `sh:property` THEN each SHALL draw as a row on that card — `sh:path` as written (complex paths as SHACL path expressions), a constraint summary, cardinality as `[min..max]`, `sh:name` where present — never as a separate node.
3. WHEN a shape carries target declarations THEN each SHALL draw as a declaration chip on its card naming the target kind and term, and the projection SHALL NOT fabricate a node or an edge for a targeted term the file does not otherwise describe — the absent-data position stated in the introduction.
4. WHEN a shape references another drawn shape via `sh:node`, or via a logical combinator whose operand is IRI-named and drawn THEN the reference SHALL draw as a labeled edge (`node`, `and`, `or`, `xone`, `not`); blank-node operands SHALL summarize inline in the referring row.
5. WHEN a property shape constrains by `sh:class` THEN the projection SHALL draw an edge to a card only where exactly one drawn card claims that class through its target; otherwise the constraint stays in the row — the mapping SHALL be deterministic either way.
6. WHEN an IRI-named property shape is referenced by no card THEN it SHALL draw as its own card so it cannot be invisible; where referenced, it draws as rows of each referencing card.
7. WHEN a shape carries `sh:sparql` THEN the constraint SHALL draw as an opaque row labeled as SPARQL, its query text readable in the property grid, never parsed beyond extraction and never executed.
8. WHEN a shape carries a non-default `sh:severity` or `sh:deactivated` true THEN the card or row SHALL wear the badge, and deactivated shapes SHALL render dimmed.

### Requirement 2 — Registration and routing

**User Story:** As a user, I want the shapes reading to be a deliberate choice on a file the family already routes, so that a bare Turtle file stays a data graph and nothing competes for it.

#### Acceptance Criteria

1. WHEN the family module declares itself THEN it SHALL expose the `w3c/shacl` `DiagramDefinition` — description, fitting icon — as a further definition over the **triplestore document store**, inside `src/diagrams/rdf/`.
2. WHEN a bare `.ttl`/`.nt` file exists THEN this reading SHALL NOT claim it, per the **routing arrangement**; Add SHALL suggest `w3c/shacl` off its marker triples as the arrangement already names.
3. WHEN this reading and others are registered on one file THEN each SHALL be its own view of the same parsed document with one shared history, per the anchor's coexistence rule.

### Requirement 3 — Blank-node property shapes: the intersection, head on

**User Story:** As a user of real published shapes — where nearly every property shape is a blank node — I want the diagram fully drawn and honestly editable, so that the **blank-node identity boundary** does not hollow out the module.

#### Acceptance Criteria

1. WHEN property shapes are blank nodes (the dominant authored form) THEN they SHALL draw as rows of their referencing card and carry no element identity of their own: a row has no stored position because it has no position at all — the card's is the only one — so the boundary's stored-position refusal is satisfied by construction rather than by a refusal the user ever meets.
2. WHEN a card is IRI-named THEN repositioning SHALL store through the **layout overlay** keyed by its IRI-derived id; WHEN a card is blank-node-rooted (an anonymous node shape drawn via `sh:node`) THEN repositioning SHALL be refused with the boundary's sentence and the card takes its computed place.
3. WHEN an edit would mutate or remove an existing blank-node-rooted property shape or constraint THEN it SHALL be refused before any splice with a sentence naming the boundary and naming the text editor as the path — Requirement 5 enumerates the gestures that remain, all keyed to IRI-named subjects.
4. WHEN a card is selected THEN its rows and their constraint values SHALL be readable through the property grid via the card's identity, so blank-node content is fully inspectable even where it is not editable.

### Requirement 4 — Drawn, never executed

**User Story:** As a user opening a shapes file, I want to be told clearly that nothing is being validated, so that a diagram of constraints is never mistaken for a conformance report.

#### Acceptance Criteria

1. WHEN a shapes file opens THEN nothing SHALL be validated against it: no data graph is loaded, no conformance is evaluated, no report exists — the **no-network, no-inference rule**'s no-SHACL-execution clause is this diagram's load-bearing statement, because the natural expectation of a SHACL tool is that it runs.
2. WHEN a user looks for validation THEN what this module offers instead SHALL be exactly two things: the shapes' structure made readable (Requirement 1), and findings about the shapes file itself (Requirement 7) — the problems panel speaks about the shapes graph, never about data conformance.
3. WHEN a target names a term absent from the file THEN that absence SHALL NOT be a finding — a shapes graph aiming at data that is elsewhere is the medium working as designed, not an error.
4. A future spec MAY add deliberate execution — choose a data file, run, report — without contradicting this one: this spec constrains what opening a diagram does, not what a future explicitly invoked action may do; until such a spec exists the action does not either.
5. The anchor's **registration header facility** — whose own text names a SHACL target hint as a possible claimant — is available and SHALL remain deliberately unclaimed by this reading: a shapes graph is written to constrain any number of data graphs, so a registration bound to one particular data file would misstate the medium; and annotating targets against a chosen data graph (present, absent, counted) is the first step of the execution path, which 4.4 assigns to a future spec as a whole rather than admitting piecemeal. That spec is the facility's natural claimant, under the anchor's claim-before-use rule.

### Requirement 5 — Editing: this reading's writers under the splice discipline

**User Story:** As a shapes author, I want the edits a schema canvas is good at — new shape, new target, new property row, deactivate, rename, remove — landing as minimal Turtle diffs, each one undo away.

#### Acceptance Criteria

1. WHEN any edit is made THEN it SHALL dispatch as a command with a byte-restoring inverse, written under the **splice discipline** — these gestures are that discipline's triple-writer under this reading's names.
2. WHEN a node shape is created (toolbox placement id, name dialog validated against the file's prefixes) THEN the writer SHALL emit a subject with one `rdf:type sh:NodeShape` triple.
3. WHEN a target is added to an IRI-named shape (menu; kind and term dialog) THEN exactly one target triple SHALL be spliced, and removing a target declaration SHALL remove exactly that triple.
4. WHEN a property row is added to an IRI-named shape (dialog: path required; optional datatype, min and max counts) THEN one `sh:property [ … ]` block SHALL be spliced into that shape's statement — permitted under the **blank-node identity boundary** because the command and its inverse are keyed to the IRI-named parent whose lines the splice touches, never to the blank node itself.
5. WHEN a shape is deactivated or reactivated (menu, IRI-named subjects only) THEN the writer SHALL add or remove its `sh:deactivated true` triple.
6. WHEN an IRI-named shape is renamed THEN the family rename applies as-is; WHEN an IRI-named shape is removed THEN the removal SHALL take its triples and every blank-node constraint subtree reachable only from it, as one command, with the count stated before anything runs.
7. IF an edit targets blank-node-rooted existing content, a refused serialization, or a truncated view THEN it SHALL be refused before any splice with a sentence naming the **blank-node identity boundary**, the **serialization boundary**, or the **drawn-element budget** respectively.

### Requirement 6 — Toolbox, context actions, property grid

**User Story:** As a user, I want the palette, menus and grid to speak SHACL — shapes, targets, paths, constraints — through the standard provider paths.

#### Acceptance Criteria

1. WHEN the diagram is open THEN the toolbox SHALL offer a Node shape entry (placement id) and a Property row entry whose drop on a card runs the add-property action — described as data, per the standard toolbox path.
2. WHEN a card is selected THEN context menus SHALL discover the Requirement 5 gestures — add target, add property row, deactivate or reactivate, rename, remove — with every gesture unavailable-with-reason where its subject is blank-node-rooted, per the standard provider path.
3. WHEN a card is selected THEN the property grid SHALL show its full IRI (read-only, naming rename as the identity path), prefixed name, targets, closedness, severity, `sh:name`/`sh:description`/`sh:message`, and each property row with its path and constraint values — blank-rooted values readable, editable only where Requirement 5 permits.
4. WHEN an action cannot apply THEN it SHALL be discovered unavailable with its reason or refused with the same sentence — never silently absent.

### Requirement 7 — Validation: facts about the shapes file, never about data

**User Story:** As a shapes author, I want the problems panel to catch what makes a shapes file broken or self-contradictory, without pretending to be a SHACL engine.

#### Acceptance Criteria

1. WHEN a property shape lacks `sh:path`, or carries more than one THEN validation SHALL report it with its line — the recommendation requires exactly one.
2. WHEN `sh:minCount` exceeds `sh:maxCount` on one shape THEN validation SHALL warn: nothing can conform.
3. WHEN `sh:datatype` and `sh:class` constrain the same shape THEN validation SHALL warn that the pair is jointly unsatisfiable — no value is both literal and instance.
4. WHEN a term in the `sh:` namespace is not defined by the recommendation THEN validation SHALL warn with the line — the typo that silently disables a constraint is this format's classic failure.
5. WHEN `sh:node` or `sh:property` references an IRI the file does not describe THEN validation SHALL report an info naming it as described elsewhere or missing — this tool reads one file, per the **no-network, no-inference rule** — while a target naming an absent term SHALL NOT be a finding (Requirement 4.3).
6. WHEN validation runs THEN it SHALL consult nothing outside the file and SHALL never evaluate data conformance; family-generic findings (parse errors, prefix redeclarations) stay the anchor's and are not duplicated here.

### Requirement 8 — Scale: the budget over this projection

**User Story:** As a user opening a machine-generated shapes file, I want the same honest degradation every reading in this family has.

#### Acceptance Criteria

1. WHEN the diagram opens THEN the **drawn-element budget** SHALL be measured against this projection with cards as the counted elements — rows travel with their card — and truncation SHALL behave exactly as the budget prescribes: deterministic first-N order, the banner, edits withheld.

### Requirement 9 — Examples: real shapes, found online, vendored honestly

**User Story:** As a user meeting this diagram type, I want real published shapes graphs opening from the explorer, so the reading proves itself on constraints the world actually enforces.

#### Acceptance Criteria

1. WHEN the module ships this reading THEN its examples SHALL join the family's `src/diagrams/rdf/examples/` under the **example vendoring rule**, each license verified individually at acquisition. Candidates verified during this spec's research: the **W3C SHACL recommendation's example shapes** (W3C Software and Document License) as the per-construct corpus exercising Requirement 1's list; the recommendation's own **shapes-that-validate-shapes-graphs appendix** (same license) as a real, substantial, published shapes graph — self-referential in the best way, since opened here it draws the very structures it constrains; and the **FAIR Data Point metadata shapes** (FAIRDataTeam on GitHub, MIT per the repository license, re-verified at acquisition) as a deployed application profile with targets, datatypes and cardinalities in anger.
2. WHEN candidates were rejected THEN the rejections stand recorded: the SEMICeu **DCAT-AP shapes** (ISA Open Metadata Licence v1.1 — not in the vendoring rule's verified-permissive list, so rejected by that list, not by quality) and **schema.org-derived shapes** (share-alike upstream).
3. IF at acquisition no candidate passes verification or exercises the model properly — published shapes graphs being scarcer than ontologies — THEN the provenance readme SHALL state the gap and a clearly-labeled authored file MAY stand in, marked as authored for ADP, never misattributed to an upstream.
4. WHEN the examples ship THEN at least one SHALL exercise blank-node property shapes, targets of at least two kinds, a logical combinator and a complex path; provenance readmes, license texts and replication follow the anchor's example requirements as-is — replication meaning the showcase copy is seeded, not held byte-identical: the guard that byte-compared the two example trees was deleted on 2026-09-03 when they were deliberately disconnected.
5. WHEN the examples open THEN every registration SHALL resolve and validation SHALL report zero findings above info level.

## Non-Functional Requirements

### Code Architecture and Modularity

- Further definitions inside `src/diagrams/rdf/` — no new module folder; the projection a pure, deterministic function over the family store's parsed model; writers as named splices refusing before any splice; element ids IRI-derived per the family rule.

### Performance

- Projection linear in triples; everything past projection bounded by the **drawn-element budget**.

### Security

- The **no-network, no-inference rule**, with its no-SHACL-execution clause load-bearing here: shapes are data to draw, never programs to run.

### Reliability

- Every refusal sentence of Requirements 3, 5 and 6 carried in a test; round-trip byte-identity rides the family's fixture corpus, extended with shapes-specific constructs (anonymous property shapes, path expressions, logical combinators, `sh:sparql` text blocks).

### Usability

- Prefixed names everywhere a human reads, full IRIs one selection away; every target chip readable as a sentence; every unavailable gesture names its reason where the user is looking.

## Sources

- SHACL — Shapes Constraint Language, W3C Recommendation — w3.org/TR/shacl (core language, targets, constraint components, paths, severities, deactivation; the shapes-for-shapes appendix; the shape declaration-or-use definition)
- SHACL Play — shacl-play.sparna.fr (application-profile documentation tables; UML diagrams as SVG via PlantUML — feature set verified 2026-09-03)
- RDFShape, WESO research group — rdfshape.weso.es (UML-register shape rendering)
- TopBraid EDG — topquadrant.com (shapes rendered as data-entry forms; the editing-side convention this spec routes to the property grid)
- FAIR Data Point, FAIRDataTeam — github.com/FAIRDataTeam (MIT per repository license, to be re-verified at acquisition)
- SEMICeu DCAT-AP SHACL — github.com/SEMICeu/dcat-ap_shacl (ISA Open Metadata Licence v1.1 — the named rejection)
- W3C Software and Document License — w3.org/copyright/software-license
