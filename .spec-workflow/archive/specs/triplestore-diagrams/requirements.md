# Requirements Document

## Introduction

This spec covers a **family of four triplestore diagram types** over one text format — the RDF graphs a repository already carries as Turtle or N-Triples files. The user asked for triplestores "especially the text based W3C ones", and this document takes that narrowing literally: the subject is **files on disk**, the artifacts W3C's own recommendations define and every triplestore imports and exports — not a live connection to a running store (see out of scope). The family follows the shape [`databricks-diagrams`](../databricks-diagrams/requirements.md) proved end to end: one engine, one document store, several MIME types, and the `.adp` registration choosing which reading of the same bytes a diagram shows.

| Origin | Reading of the file | What it shows |
| --- | --- | --- |
| `w3c/rdf` | The data graph itself | Resources as nodes, triples as labeled edges, literals as property rows inside their subject's box |
| `w3c/owl` | The ontology in the graph | Classes and their subclass hierarchy, object/datatype properties with domain and range, individuals attached to their classes |
| `w3c/skos` | The concept scheme in the graph | Schemes as frames, concepts, broader/narrower as hierarchy edges, related as cross links |
| `w3c/shacl` | The shapes graph | Node shapes with their property shapes and constraint badges, target edges pointing at what they constrain |

These four are one table row family, not four formats: **an OWL ontology, a SKOS scheme and a SHACL shapes graph are all just RDF graphs**, usually in Turtle. The same bytes parse once into the same triples; what differs is which triples each reading projects and how it draws them. That is exactly the databricks arrangement (one `databricks.yml`, three readings selected by registration) applied to the format family where it fits most naturally of all.

**The format, as researched.** RDF 1.1 is a graph data model: a graph is a set of triples (subject, predicate, object), where subjects and predicates are IRIs or blank nodes and objects may also be literals (typed by datatype IRI, or language-tagged). Turtle (`.ttl`) is the dominant hand-authored serialization: `@prefix`/`@base` declarations, prefixed names (`foaf:name`), abbreviated predicate lists (`;`), object lists (`,`), anonymous blank nodes (`[ ... ]`), collections (`( ... )`), and `a` as shorthand for `rdf:type`. N-Triples (`.nt`) is its line-oriented subset: one fully-written triple per line, no prefixes, no abbreviation. OWL 2 expresses ontologies as RDF triples (`owl:Class`, `rdfs:subClassOf`, `owl:ObjectProperty`, `rdfs:domain`/`rdfs:range`, `owl:equivalentClass`, `owl:disjointWith`, individuals via `rdf:type`). SKOS expresses concept schemes (`skos:ConceptScheme`, `skos:Concept`, `skos:inScheme`, `skos:broader`/`narrower`/`related`, `skos:prefLabel`/`altLabel`, `skos:Collection`). SHACL expresses shapes (`sh:NodeShape`, `sh:property` to property shapes, `sh:targetClass`/`targetNode`, constraint parameters such as `sh:minCount`, `sh:datatype`, `sh:pattern`). Sources are listed at the end of this document.

**What is genuinely new here, and what is not.** Three things are new. First, **a default reading among shared readings**: `.ttl` and `.nt` belong to this family outright — a bare Turtle file is an RDF graph the way a bare `.tml` is a timeline — so `w3c/rdf` routes bare bodies on sight, while `w3c/owl`, `w3c/skos` and `w3c/shacl` are chosen by explicit registration over the same file. No prior family mixes a routing default with registration-selected siblings, and Requirement 2 pins how that resolves. Second, **a scale budget stated up front**: real RDF files reach millions of triples, so "it opens" is only a plausible claim with a drawn-element budget and a defined over-budget behaviour (Requirement 12) — the first type whose requirements bound what is drawn rather than assuming files stay small. Third, **blank nodes break the identity assumption**: an anonymous node has no stable name across a reparse, which Requirement 7 and 8 handle by refusing exactly the things that need stable identity (stored positions, undoable edits) on blank-node-rooted content, with the reason on the surface. Everything else — the line-CST-and-splice writers, the `.adp` `layout:` block, routing, commands and undo, the toolbox, property grid, validation, examples under the central replication guard — is consumed unchanged.

**Where it sits among the types already built.** [`databricks-diagrams`](../databricks-diagrams/requirements.md) proved one engine serving several MIME types over one document store, the `resource:`-style registration-selected reading, and the core `layout:` block for authored positions in the `.adp` — this family is that block's next consumer, alongside [`helm-charts`](../helm-charts/requirements.md), which is in flight as its second. The timeline proved byte-identical round-trips of a hand-authored text body; Turtle's prefixes and abbreviations make that discipline earn its keep here (Requirement 1). The azure-pipeline and databricks families proved shared-extension registration for files the whole world writes; this family needs the inverse case too, an owned extension, and both exist in core already.

**Reused, not redefined:** the shell and canvas, `Element`/`Delta` payloads, tabs, the Add flow and `.adp` MIME-line registration, `DiagramFilePair`'s `body:` resolution, the context service, property grid, undo/redo and tech.md's Commands rule, the central canvas library and its shared appearance stylesheet, `CanvasScrollbars`, the problems panel, `ExampleRegistrationTests` and `ExampleReplicationTests`. **No core changes are expected**: the `layout:` block exists, shared-extension and owned-extension routing both exist, and multi-definition modules exist. Anything found necessary during implementation is a finding to report, not a silent change.

**Deliberately out of scope.**

- **Live triplestore connectivity.** The word "triplestore" promises a server; this spec deliberately does not. No SPARQL endpoints, no GraphDB/Fuseki/Blazegraph connections, no named-graph browsing of a running store, no authentication. The subject is the files a store imports and exports. A later connectivity spec can add endpoints behind these same diagram types.
- **SPARQL.** A `.rq` file is a query — a basic graph pattern with variables, filters and solution modifiers — and visualizing it means drawing *asking*, not *modeling*, which is exactly the framing of the catalog's section 14 (where `neo4j/cypher` already sits). It shares this family's visual vocabulary but none of its machinery: a query parser is not a graph parser, and a query has no graph to lay out, only a pattern. SPARQL therefore joins the catalog as a `w3c/sparql` row in section 14, identified for a sibling spec, and is out of scope here.
- **Serializations beyond Turtle and N-Triples.** RDF/XML (XML round-tripping is a different splice discipline, and hand-authoring has moved away from it), JSON-LD (its `@context` may live at a remote IRI, and resolving it violates the no-network rule below; its round-trip is structurally unstable byte-wise), TriG and N-Quads (they add the named-graph dimension, which multiplies every reading). Each is refused by name with its reason (Requirement 1.6); a later spec can add any of them behind the same store.
- **Inference and entailment.** No RDFS/OWL reasoning: the diagram shows what the file says, never what a reasoner could derive. A subclass edge is drawn because the triple is written, not because it is entailed. SHACL shapes are drawn, never *executed* — no constraint evaluation against a data graph.
- **Remote anything.** No dereferencing of IRIs, no fetching of imported ontologies (`owl:imports` is drawn as a labeled marker, not followed), no remote JSON-LD contexts.
- **LinkML** (`linkml/schema` stays an identified catalog row — YAML, a different format family).

## Alignment with Product Vision

- [product.md](../../steering/product.md)'s **"Files are the source of truth"** — the diagrams render Turtle and N-Triples files a repository already has; edits are splices that leave every untouched byte alone, and authored positions live in the `.adp`'s diffable `layout:` block, never in the RDF file (Requirement 7).
- product.md's **"Don't reinvent, integrate"** — RDF, OWL, SKOS and SHACL are W3C recommendations with decades of tooling; ADP adds no format of its own, and the ontology reading leans on the visual grammar VOWL established rather than inventing a new one.
- product.md's **"Linkage over illustration"** — every node is a term in a real file; selection resolves it, the property grid shows its IRI and labels, and edits land in the file as reviewable diffs.
- [tech.md](../../steering/tech.md)'s **Diagram storage** rule — the body formats belong to the W3C and round-trip byte-identically; the layout-in-`.adp` rule this repo added for databricks applies verbatim (Requirement 7).
- tech.md's **Commands** — every edit is an `ICommand` with an inverse, one undo away (Requirement 8).
- [structure.md](../../steering/structure.md)'s **dependency direction** — one module under `src/diagrams/triplestore/`, depending on core only.

## Requirements

### Requirement 1 — Two serializations, byte-identical round trips

**User Story:** As a developer with RDF files in my repository, I want ADP to read and write the serializations I actually hand-author without ever reformatting them, so that a diagram edit produces a diff my reviewers can read.

#### Acceptance Criteria

1. WHEN a Turtle (`.ttl`) file is opened THEN the module SHALL parse it — prefixes, base, prefixed names, `a`, predicate lists (`;`), object lists (`,`), anonymous blank nodes (`[ ]`), labeled blank nodes (`_:name`), collections (`( )`), typed and language-tagged literals, and comments — into one set of triples plus a line-level concrete syntax the writers splice.
2. WHEN an N-Triples (`.nt`) file is opened THEN the module SHALL parse it as one triple per line, through the same store and into the same model.
3. WHEN a document ADP did not change is saved THEN the bytes written SHALL be identical to the bytes read — line endings, comments, prefix style, abbreviation style, indentation and blank lines included — for both serializations.
4. WHEN a document is edited THEN only the lines the edit concerns SHALL change (the smallest-splice discipline the timeline established); an untouched abbreviated statement SHALL keep its abbreviation, and an edit SHALL NOT expand or normalize constructs it does not touch.
5. IF a file does not parse THEN the diagram SHALL open unavailable naming the file, line and reason, every edit SHALL be withheld with the same reason, and saving SHALL be refused so a broken file is never made worse.
6. WHEN a file carries an out-of-scope serialization (`.rdf`/`.owl` as RDF/XML, `.jsonld`, `.trig`, `.nq`) THEN registration SHALL be possible but the diagram SHALL open unavailable naming the serialization and the reason it is not read, so the user learns the boundary from the tool rather than from silence.
7. WHEN constructs beyond the modelled vocabularies appear (any triple no reading projects) THEN they SHALL survive every edit byte-identically, and the `w3c/rdf` reading SHALL still draw them — there is no "unknown" in a format whose every statement is the same shape.

### Requirement 2 — Routing: one default reading, three chosen ones

**User Story:** As a user, I want a bare Turtle file to open as a graph without ceremony, and to choose the ontology, concept-scheme or shapes reading explicitly when that is what the file is, so that the common case is instant and the specific cases are deliberate.

#### Acceptance Criteria

1. WHEN the module declares itself THEN it SHALL expose four `DiagramDefinition`s — `w3c/rdf`, `w3c/owl`, `w3c/skos`, `w3c/shacl` — each with a description and a fitting icon, all served by one engine over one shared document store.
2. WHEN a bare `.ttl` or `.nt` file exists with no registration THEN it SHALL route to `w3c/rdf` on sight — these extensions belong to this family the way `.tml` belongs to the timeline — and the other three definitions SHALL NOT claim a bare body: they are readings a user selects, declared so they never compete for routing.
3. WHEN a user invokes Add on a `.ttl` or `.nt` file THEN the choice tree SHALL offer all four readings, and SHALL suggest `w3c/owl` when the file declares an `owl:Ontology`, `w3c/skos` when it declares a `skos:ConceptScheme`, and `w3c/shacl` when it declares a `sh:NodeShape` or `sh:PropertyShape` — suggestions, never automatic claims.
4. WHEN several registrations point at one file THEN each SHALL open its own reading of the same parsed document, and an edit made through any SHALL be visible in all — one store, one document, one history, as the databricks family proved.
5. WHEN a registration's `body:` header names the file THEN it SHALL resolve against the `.adp`'s own folder per the established rule.

### Requirement 3 — The data graph reading (`w3c/rdf`)

**User Story:** As a developer, I want to see an RDF file as the graph it states — resources, relationships, values — so that I can read a dataset's shape without mentally executing Turtle syntax.

#### Acceptance Criteria

1. WHEN the data graph opens THEN every IRI-named subject and every IRI object SHALL draw as a node labeled with its prefixed name (falling back to the IRI's local name, then the full IRI), and every triple between two such nodes SHALL draw as a directed edge labeled with the predicate's prefixed name.
2. WHEN a triple's object is a literal THEN it SHALL render as a property row inside its subject's node — predicate, value, and datatype or language tag — not as a graph node, so a resource with twenty literals is one box, not a starburst.
3. WHEN a subject has an `rdf:type` THEN the node SHALL wear the type as a badge, and the computed layout SHALL group nodes of one type together so a dataset's populations read at a glance.
4. WHEN blank nodes appear THEN they SHALL draw — labeled by their `_:name` where written, or as anonymous markers — with their triples as edges, so nothing in the file is invisible.
5. WHEN a predicate or object IRI uses a prefix THEN hovering or selecting SHALL reveal the full IRI through the property grid (Requirement 10).

### Requirement 4 — The ontology reading (`w3c/owl`)

**User Story:** As an ontology author, I want the class hierarchy and property structure drawn the way the ontology community draws them, so that the diagram is legible to people who know VOWL and Protégé.

#### Acceptance Criteria

1. WHEN the ontology reading opens THEN every declared class (`owl:Class`, and `rdfs:Class` where used) SHALL draw as a class node, `rdfs:subClassOf` SHALL draw as a hierarchy edge, and the computed layout SHALL layer classes by subclass depth.
2. WHEN object properties are declared THEN each SHALL draw as a labeled edge from its `rdfs:domain` class to its `rdfs:range` class; a property missing either end SHALL draw to a marked open end rather than vanish, mirroring the dangling-reference treatment every family here uses.
3. WHEN datatype properties are declared THEN each SHALL render as a property row on its domain class — name and range datatype — not as an edge into a datatype node.
4. WHEN individuals are declared (`rdf:type` naming a declared class) THEN each SHALL draw attached to its class, visually subordinate, so instance data enriches the ontology view without flooding it.
5. WHEN `owl:equivalentClass` or `owl:disjointWith` triples appear THEN each SHALL draw as a distinctly-styled cross edge carrying its meaning as the label.
6. WHEN `owl:imports` names another ontology THEN the import SHALL render as a labeled marker on the ontology header node and SHALL NOT be fetched or followed (out of scope: remote anything).
7. WHEN the file declares no ontology-reading vocabulary at all THEN the diagram SHALL open empty with a message saying what it looked for, not fail.

### Requirement 5 — The concept scheme reading (`w3c/skos`)

**User Story:** As a taxonomist, I want a SKOS file drawn as the hierarchy it encodes, so that broader/narrower structure and cross-references read as the tree-with-links they are.

#### Acceptance Criteria

1. WHEN the scheme reading opens THEN each `skos:ConceptScheme` SHALL draw as a frame, each `skos:Concept` as a node inside its scheme's frame (placed by `skos:inScheme` or `skos:topConceptOf`; unassigned concepts outside any frame), labeled by `skos:prefLabel` (preferring an unlagged or English label, falling back to any) or its prefixed name.
2. WHEN `skos:broader`/`skos:narrower` triples appear THEN each pair SHALL draw once as a hierarchy edge from broader to narrower, and the computed layout SHALL layer concepts by hierarchy depth.
3. WHEN `skos:related` triples appear THEN each SHALL draw as a distinctly-styled cross link, drawn once per pair.
4. WHEN `skos:Collection`s appear THEN each SHALL draw as a labeled grouping of its members.
5. WHEN a concept carries `skos:altLabel`, `skos:definition` or `skos:notation` THEN those SHALL render as property rows on the concept (and in the property grid), not as nodes.

### Requirement 6 — The shapes reading (`w3c/shacl`)

**User Story:** As a data steward, I want a SHACL file drawn as shapes pointing at what they constrain, so that the validation surface of a dataset is visible without reading constraint syntax.

#### Acceptance Criteria

1. WHEN the shapes reading opens THEN each `sh:NodeShape` SHALL draw as a shape node, its `sh:property` property shapes as rows on it — path, and a compact badge summary of constraints (`sh:minCount`/`maxCount` as cardinality, `sh:datatype`, `sh:class`, `sh:pattern` marked as present) — with any constraint parameter beyond that set summarized by name rather than dropped.
2. WHEN a shape declares `sh:targetClass` or `sh:targetNode` THEN the target SHALL draw as a node with a distinctly-styled target edge from the shape; a target term not otherwise present in the file SHALL still draw, marked as declared-only.
3. WHEN shapes reference each other (`sh:node`, `sh:property` to a named shape) THEN those references SHALL draw as edges.
4. WHEN this reading opens THEN it SHALL be read-only: the property grid states constraints, the canvas arranges them, and every edit action is withheld with the reason that constraint editing is beyond this spec (Requirement 8.6).

### Requirement 7 — Positions in the registration, never in the RDF

**User Story:** As a user arranging a graph, I want my layout kept without the RDF file changing by a byte, so that files other tools and pipelines own are never polluted by presentation data.

#### Acceptance Criteria

1. WHEN any of the four readings opens THEN elements SHALL land by a deterministic computed layout for that reading (Requirements 3–6), with no physics and no randomness, so the same file always opens the same way.
2. WHEN a user repositions an element THEN the position SHALL be stored in the diagram's `.adp` registration file's `layout:` block — the core mechanism `databricks-diagrams` defined and `helm-charts` also consumes — keyed by an id derived from the term's IRI, and the RDF file SHALL be byte-identical before and after.
3. WHEN a diagram reopens THEN stored positions SHALL override computed ones element by element; elements without stored positions keep their computed places, and stale ids (terms no longer in the file) SHALL be ignored on read and dropped on the next layout write.
4. WHEN a reposition is undone THEN the `.adp` SHALL return byte-for-byte, per the core command's contract.
5. IF the element is blank-node-rooted THEN repositioning SHALL be refused with the reason that an anonymous node has no stable identity to key a stored position by; such elements always take their computed place.
6. WHEN a bare `.ttl`/`.nt` opens with no registration THEN repositioning SHALL be refused with the reason that there is no registration to store the position in — matching the databricks behaviour — and registering the file lifts the refusal.

### Requirement 8 — Editing: a small set of splices, each one undo away

**User Story:** As an author, I want the edits a diagram is actually good at — connecting, renaming, adding and removing statements — to land in my Turtle file as minimal diffs, undoable like everything else in the IDE.

#### Acceptance Criteria

1. WHEN an edit is made through any reading THEN it SHALL dispatch as a command through the project's history, with an inverse that restores the file byte-for-byte, and only the lines the edit concerns SHALL change.
2. WHEN a triple is added between two existing IRI-named terms (the `rel:` gesture on the canvas, or a menu action) THEN the writer SHALL append a statement using the file's existing prefix for each term where one is declared and the full IRI otherwise; the writer SHALL NOT invent prefix declarations as a side effect — adding a prefix is its own explicit action.
3. WHEN a statement is removed THEN the splice SHALL respect Turtle's abbreviations: removing one triple from a `;`-list or `,`-list SHALL remove exactly that fragment and any separator it strands, and removing a subject's last triple SHALL remove the subject's statement block.
4. WHEN a term is renamed (its local name edited) THEN every occurrence in the file — subject, predicate and object positions, prefixed or written in full — SHALL be rewritten in one command with one inverse, so no reference is ever stranded; a rename that would collide with an existing term SHALL be refused before any splice.
5. WHEN an element is removed through the canvas THEN the removal SHALL take every triple whose subject or object it is, as one command, with the count stated before anything runs.
6. IF an edit targets blank-node-rooted content, an out-of-scope serialization, or the shapes reading THEN it SHALL be refused before any splice, with a sentence naming the reason.
7. WHEN reading-specific gestures apply — adding a `rdfs:subClassOf` edge in the ontology reading, a `skos:broader` edge in the scheme reading — THEN they SHALL be the same triple-writer underneath, discovered per reading through the standard context-action path.

### Requirement 9 — Toolbox and context actions per reading

**User Story:** As a user, I want each reading's palette and menus to speak that reading's language, so that the ontology view offers classes and the scheme view offers concepts, while everything lands through one command path.

#### Acceptance Criteria

1. WHEN the data graph reading is open THEN the toolbox SHALL offer a resource node (dropped via placement id, named by dialog with prefix-aware validation); WHEN the ontology reading is open, a class and — anchored on a class — an individual; WHEN the scheme reading is open, a concept scheme and a concept. The shapes reading offers no toolbox, with the read-only reason.
2. WHEN elements are selected THEN context menus SHALL discover through the standard provider path: rename, remove-with-references (count stated first), disconnect per edge, and the reading-specific additions of Requirement 8.7 — every mutating entry dispatching a command, exactly one undo each.
3. WHEN an action cannot apply (blank node, unregistered bare file, shapes reading) THEN it SHALL be discovered as unavailable with its reason, or refused on execution with the same sentence — never silently absent.

### Requirement 10 — The property grid tells the whole truth about a term

**User Story:** As a user, I want a selected element's full identity — IRI, prefixed form, labels, literals — in the property grid, so that the diagram's compact labels never hide what a thing actually is.

#### Acceptance Criteria

1. WHEN an element is selected THEN the grid SHALL show its full IRI (read-only, with the reason naming Requirement 8.4's rename as the path that changes identity), its prefixed name, and its `rdf:type`s.
2. WHEN the subject carries literal-valued properties THEN each SHALL be a grid row — predicate, value, datatype/language — and rows for `rdfs:label`, `rdfs:comment`, `skos:prefLabel` and `skos:definition` SHALL be editable, dispatching the standard splice through the property path.
3. WHEN an edge is selected THEN the grid SHALL show the predicate's IRI and prefixed name, both read-only with reconnect-on-the-canvas as the stated path.
4. WHEN the shapes reading is open THEN all rows SHALL be read-only with the Requirement 6.4 reason.

### Requirement 11 — Validation: what the file says, judged without inference

**User Story:** As an author, I want the problems panel to catch the mistakes that make an RDF file lie or wobble, without a reasoner's opinions, so that findings are facts about the text.

#### Acceptance Criteria

1. WHEN a file does not parse THEN validation SHALL report exactly one finding naming file, line and reason (the Requirement 1.5 state), and SHALL NOT bury it under consequences.
2. WHEN a prefix is declared twice with different IRIs THEN validation SHALL report each redeclaration with its line.
3. WHEN relative IRIs appear with no `@base` in the file THEN validation SHALL warn once, naming the first occurrence — the file's meaning then depends on where it is read from.
4. WHEN `skos:broader` (in the scheme reading's vocabulary) or `rdfs:subClassOf` (in the ontology's) forms a cycle THEN validation SHALL warn naming the members — legal RDF, near-certainly a mistake, and the layouts must survive it regardless (they layer with back-edges excluded, per the established cycle treatment).
5. WHEN a `skos:Concept` is in no scheme, or a SHACL shape targets nothing present in the file THEN validation SHALL note it at info level — often intentional, worth a glance.
6. WHEN validation runs THEN it SHALL consult nothing outside the file — no imports followed, no IRIs dereferenced, no entailment — and the shipped examples SHALL report zero findings above info level.

### Requirement 12 — Scale: a stated budget, never a hang

**User Story:** As a user opening a large dataset, I want the diagram to stay honest about what it can draw, so that a million-triple file degrades into something useful instead of a frozen tab.

#### Acceptance Criteria

1. WHEN a file is opened THEN parsing SHALL be bounded only by the file (streamed line-CST, no quadratic passes), and the reading SHALL project into elements only up to a **drawn-element budget** — default 1,000 nodes, a module constant the design may tune — counted after the reading's projection, not raw triples.
2. WHEN the budget is exceeded THEN the diagram SHALL open in a truncated view: the first budget-many elements in the reading's deterministic order, a persistent banner stating "showing N of M" with the real totals, and a validation info naming the truncation — never a hang, never a silent partial view.
3. WHEN a file is truncated THEN edits SHALL be withheld with the truncation as the reason — a splice computed against a partially-projected model is not trustworthy — while the property grid and selection keep working on what is drawn.
4. WHEN the databricks-style readings project small slices of large files (an ontology reading of a file that is mostly instance data) THEN the budget SHALL apply to the projection, so a 100,000-triple dataset with forty classes still opens fully in the ontology reading.

### Requirement 13 — Examples: one coherent domain, replicated under the central guard

**User Story:** As a user meeting these diagram types for the first time, I want examples that open from the explorer with no setup and read as a believable knowledge-modeling scenario, so that each reading demonstrates itself.

#### Acceptance Criteria

1. WHEN the module ships THEN `src/diagrams/triplestore/examples/` SHALL hold one coherent scenario, **self-authored** so no third-party license machinery is needed on the primary path: a small domain (for instance a library-and-publications world) expressed as an ontology file (classes, properties, individuals — opening as both `w3c/owl` and `w3c/rdf`), a SKOS subject taxonomy, a SHACL shapes file targeting the ontology's classes, and a plain data file in both Turtle and N-Triples — each config beside its routing `.adp`, plus a readme naming what each file demonstrates.
2. WHEN the examples are replicated THEN byte-identical copies SHALL live at `src/examples/diagrams/triplestore/`, covered by the existing central `ExampleReplicationTests` by construction — no second guard beside it, per the amended databricks 5.2 precedent.
3. IF a well-known third-party vocabulary is vendored as an additional example THEN it SHALL follow the `helm-charts` Requirement 11 discipline verbatim — license verified permissive before anything is copied, upstream license text carried, provenance readme, unmodified content unmodified — and W3C-published vocabulary files (under the W3C Software and Document License) are the intended candidates; this is optional and the self-authored scenario SHALL NOT depend on it.
4. WHEN the examples open THEN every registration SHALL resolve in the deployed catalog (joining `ExampleRegistrationTests` by existing), each reading SHALL show the structures its requirement names, and validation SHALL report zero findings above info level.
5. WHEN the catalog is updated THEN the `w3c/rdf` row SHALL be added to `docs/diagrams.md` section 13 and the `w3c/sparql` row to section 14, and the states of the four covered rows SHALL advance per the catalog's own legend as this spec progresses.

## Non-Functional Requirements

### Code Architecture and Modularity

- **One module**, `src/diagrams/triplestore/` (backend, client, api, examples), depending on core only, following the databricks module's layout: one shared document store and parser, four definitions, per-reading projections and layouts as pure functions, writers as named splice operations refusing before any splice.
- **One parser, four projections**: the Turtle/N-Triples parser produces triples plus line ranges once; each reading is a projection over that model, never a re-parse with different rules.
- **Element ids are derived from IRIs** (prefixed where possible), stable across reparses, shared by the stream, the resolver, the layout block and the tests, so the selection, the layout overlay and the providers can never disagree about identity.

### Performance

- Parsing SHALL be linear in file size; a 100,000-triple N-Triples file SHALL parse within low single-digit seconds on developer hardware, and the drawn-element budget (Requirement 12) bounds everything downstream of parsing.
- Reading projections and computed layouts SHALL be pure and cheap enough to re-run on every document change without perceptible lag at budget scale.

### Security

- No network access of any kind: no IRI dereferencing, no `owl:imports` fetching, no remote contexts, no endpoints. Files are read within the registered project root under the existing containment rules.

### Reliability

- Byte-identity round trips are the headline property, guarded by a fixture corpus per serialization: CRLF/LF/no-trailing-newline variants, comments, every Turtle abbreviation Requirement 1.1 names, and a `.gitattributes` entry for the fixture files per the established byte-compared-fixture rule (the by-extension form fits: `.ttl`/`.nt` would be new, but a scoped glob like the databricks one is the fallback if the extensions prove shared — decided at design time with the reasoning recorded in `.gitattributes` either way).
- The undo contract — file restored byte-for-byte — SHALL be tested through the real command pipeline for every writer operation.
- Blank-node refusals, truncation refusals and serialization refusals SHALL each carry their sentence in tests, so "refused with a reason" is behaviour, not intention.

### Usability

- Every unavailable state (unparseable, out-of-scope serialization, truncated, shapes read-only, blank-node identity) SHALL name its reason where the user is looking — the canvas message, the refused action, or the property grid row.
- Prefixed names everywhere a human reads; full IRIs one selection away.

## Sources

- RDF 1.1 Concepts and Abstract Syntax — w3.org/TR/rdf11-concepts
- RDF 1.1 Turtle — w3.org/TR/turtle
- RDF 1.1 N-Triples — w3.org/TR/n-triples
- OWL 2 Web Ontology Language Overview and Mapping to RDF Graphs — w3.org/TR/owl2-overview, w3.org/TR/owl2-mapping-to-rdf
- SKOS Simple Knowledge Organization System Reference — w3.org/TR/skos-reference
- Shapes Constraint Language (SHACL) — w3.org/TR/shacl
- SPARQL 1.1 Query Language (for the out-of-scope boundary and the section 14 row) — w3.org/TR/sparql11-query
- VOWL: Visual Notation for OWL Ontologies (the ontology reading's visual tradition) — vowl.visualdataweb.org
- W3C Software and Document License (the vendoring candidate license) — w3.org/copyright/software-license
