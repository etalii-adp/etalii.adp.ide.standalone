# Requirements Document

## Introduction

This spec covers **one diagram type, `w3c/sparql`** — a SPARQL query file drawn as the question it asks. It is the deliberate odd one out of the semantic-web family: [`rdf-diagram`](../rdf-diagram/requirements.md) and its OWL/SKOS/SHACL siblings are four readings of the same data bytes, while a `.rq` file is **a query, not a graph of data**. The catalog's section 14 draws the line this spec stands on: a query visualizes *asking*, not modeling.

| Origin | File visualized | What it shows |
| --- | --- | --- |
| `w3c/sparql` | A SPARQL 1.1 query file (`.rq`) | The query's basic graph pattern as nodes and edges, its variables as the joins they are, and its form and modifiers as the frame around that pattern |

**The format, as researched.** SPARQL 1.1 is the W3C query language for RDF. A query has a **form** (`SELECT`, `CONSTRUCT`, `ASK`, `DESCRIBE`), a prologue (`BASE`, `PREFIX`), a **where clause built of triple patterns** — like RDF triples, but any position may hold a variable (`?person`) — and the machinery around that pattern: `OPTIONAL`, `UNION`, `MINUS`, `FILTER`, `BIND`, `VALUES`, `GRAPH`, `SERVICE`, property paths, subqueries, and the solution modifiers (`DISTINCT`, `GROUP BY`, `HAVING`, `ORDER BY`, `LIMIT`, `OFFSET`). The pattern part is genuinely a graph and draws well. The rest is not, and the research below is largely about refusing to pretend otherwise.

## What this spec consumes from the family — and what it deliberately does not

The anchor spec names ten pieces of shared machinery. This spec consumes **two of them, plus the vendoring discipline**, and the paragraph below exists so nobody later wires this module into the wrong engine.

**Consumed:**

- **The family's canvas and visual conventions** (the anchor's research positions): prefixed names everywhere a human reads with full IRIs one selection away, directed labeled edges through the central canvas library, deterministic layout with force-directed physics rejected, and every unavailable state naming its reason.
- **The layout overlay** (anchor item 6): authored positions live in the `.adp` registration's `layout:` block, keyed by stable element ids, computed layout otherwise. Nothing presentation-shaped ever enters the `.rq`.
- **The example vendoring rule** (anchor item 9): real found-online queries, licenses individually verified before copying, provenance readme, upstream license text travelling along (Requirement 8).

**Explicitly not consumed, with the reasons:**

- **The triplestore document store** (item 1) and **the serialization boundary** (item 2): a `.rq` file is not a serialization of an RDF graph and never touches that store. This module has **its own parser** into a query model (Requirement 1). If a future change finds this spec citing the triplestore store, the spec has drifted and needs rethinking, not patching.
- **The splice discipline** (item 3): nothing here writes into a `.rq` — the first version is read-only (Requirement 6), so there is no splice to discipline.
- **The blank-node identity boundary** (item 4): a blank node in a query pattern *is a variable* by SPARQL semantics; it is drawn as an anonymous variable, and with no edits in scope there is nothing left for the boundary to guard. The one residue — anonymous nodes cannot carry authored positions — is stated locally (Requirement 5.4).
- **The drawn-element budget** (item 5): hand-written queries hold tens of patterns, not thousands; the budget solves a data-scale problem queries do not have. A local sanity bound exists so a generated monster degrades honestly (Requirement 7.5), but it is this spec's own number, not the family's machinery.
- **The routing arrangement** (item 7): that arrangement is about `.ttl`/`.nt` bodies and marker-triple suggestions. `.rq` routes on sight by its own extension, competing with nothing (Requirement 2).
- **The no-network rule** (item 8) is not inherited as machinery but holds here with this spec's own, sharper reason: executing a query needs an endpoint, so **results are out of scope entirely** (Requirement 7.4).
- **The registration header facility** (item 10): available to this spec — it is a property of the `.adp` registration, not of the engine being avoided — and deliberately **not used**. A `.rq` needs no reading-specific configuration: the query carries its own prologue (`BASE`/`PREFIX`), so there is no prefix source to name; nothing executes, so there is no dataset or endpoint to select; and a query file holds one query, so there is nothing to choose among. This spec claims no header name in the anchor's item 10, and a future need for one goes through that claim, not around it.

## How SPARQL queries are visualized — research, and the positions taken

Query visualization has a real tradition, and its documented failure mode is uniform: tools drown the readable graph pattern in boxes for the non-graph machinery until the diagram is harder to read than the text.

**What the established tools do.** [NITELIGHT](https://ceur-ws.org/Vol-1684/paper21.pdf) (2008) established the graph-based query editor: triple patterns as node-link structure, a graphical notation for SPARQL constructs. [QueryVOWL](https://link.springer.com/chapter/10.1007/978-3-319-25639-9_51) mapped SPARQL onto VOWL's visual language — variables and classes as circles, literals as rectangles, whole queries composable visually. [Gruff](https://allegrograph.com/products/gruff/) (AllegroGraph) ships a graphical query view where variables and constants are positioned on a canvas with constraints on the edges between them. [RDF Explorer](https://www.researchgate.net/publication/336596931_RDF_Explorer_A_Visual_SPARQL_Query_Builder) builds queries by navigating, with variables as first-class nodes. [ViziQuer](https://arxiv.org/pdf/2304.14825) covers diagrammatic queries at the analytic end (aggregation-heavy). Against all of these stands the mainstream: **YASGUI/YASQE**, the de-facto standard SPARQL editing surface, is deliberately *textual* — highlighting and completion, no canvas — and the **Wikidata Query Builder** offers only a simplified form because full visual query *editing* keeps proving harder than it looks. The catalog names **Neo4j Browser** as the Cypher-world sibling: notably, it draws query *results* as a graph and leaves the query itself as text.

**Adopted, with reasons:**

- **One variable, one node** (QueryVOWL, Gruff, RDF Explorer): every occurrence of `?person` across every triple pattern is the same node. A variable shared between patterns is a join, and that shared identity is the query's real shape — the thing a reader is trying to see when they look at a query at all. This is the single most important drawing rule in the spec (Requirement 4).
- **Variables visually distinct from concrete terms** (the QueryVOWL styling, simplified): a variable draws as a dashed-outline node labeled `?name`; a concrete IRI as a solid node with its prefixed name; a literal as a rectangle with its lexical form. A reader tells "what is being asked for" from "what is pinned down" at a glance.
- **Structure for what is a graph, annotation for what is not** (the lesson of the tradition's failures): triple patterns draw as edges; group-shaped constructs (`OPTIONAL`, `UNION`, `MINUS`, `GRAPH`, `SERVICE`) draw as labeled containment regions because they genuinely contain patterns; everything expression-shaped (`FILTER`, `BIND`, `VALUES`) attaches as annotation showing the text as written; and the query's frame — form, projection, solution modifiers — is a header band and panel content, not canvas structure (Requirement 3).
- **Text-first editing** (the YASGUI lesson, and Neo4j Browser's division of labour): the diagram's value here is *reading* a query's join structure; the mature editing surface for SPARQL is text. The first version is read-only, stated as a deliberate position rather than a gap (Requirement 6).

**Rejected, with reasons:** visual query *construction* (the NITELIGHT/QueryVOWL ambition — decades of these tools have not displaced text editing, and ADP would be building an editor for a language its users author in better tools); drawing filter expressions as expression trees (an abstract syntax view nobody asked for — the expression as written is shorter and truer); drawing subquery internals inline (nesting a whole second diagram inside a node is how these diagrams become unreadable — a subquery collapses to one node stating its projection, Requirement 3.6); force-directed layout (rejected family-wide, non-deterministic).

## Alignment with Product Vision

- [product.md](../../steering/product.md)'s **"Files are the source of truth"** — the diagram renders the `.rq` a repository already has; the file is never rewritten, and authored positions live in the `.adp`'s `layout:` block.
- product.md's **"Don't reinvent, integrate"** — SPARQL 1.1 is a W3C recommendation; the visual conventions are adopted from the query-builder tradition with citations, and the parts of that tradition that failed are rejected by name.
- product.md's **"Linkage over illustration"** — every node is a term or variable in a real query file; selection resolves it, and the property grid tells the truth about it.
- [tech.md](../../steering/tech.md)'s **Diagram storage** — byte-identical round trips of a body another ecosystem owns, held trivially by never writing it (Requirement 1.4).
- [structure.md](../../steering/structure.md)'s **dependency direction** — one module under `src/diagrams/sparql/`, depending on core only; deliberately **not** part of `src/diagrams/rdf/`, because it shares no engine with it.

## Requirements

### Requirement 1 — The query document: own parser, full grammar read, bytes never touched

**User Story:** As a developer with SPARQL queries in my repository, I want ADP to read the queries I actually write — the whole language, not a subset — and never change a byte of them.

#### Acceptance Criteria

1. WHEN a `.rq` file is opened THEN this module's **own parser** SHALL read it against the SPARQL 1.1 Query grammar: all four query forms, prologue, triple patterns with variables in any position, predicate/object lists, blank-node and collection syntax in patterns, property paths, `OPTIONAL`/`UNION`/`MINUS`/`GRAPH`/`SERVICE` groups, `FILTER`/`BIND`/`VALUES`, subqueries, aggregates and solution modifiers — parsing the full language even though only part of it is drawn as structure, so no real query is refused for using a legal construct.
2. WHEN the file is a SPARQL **Update** document (`INSERT`/`DELETE`/`LOAD`/`CLEAR`, conventionally `.ru`) THEN the diagram SHALL open unavailable naming SPARQL Update as out of scope with its reason — update is a write instruction, not a question, and drawing it well is a different problem.
3. IF a file does not parse THEN the diagram SHALL open unavailable naming file, line and reason, exactly one such finding reaching validation (Requirement 7).
4. WHEN anything at all happens in ADP THEN the `.rq` bytes SHALL be unchanged: this module registers **no writer** for the query body, so the byte-identical round-trip guarantee is held by construction, and a test SHALL pin that the module deploys no code path that opens the file for writing.
5. WHEN the file changes on disk THEN open diagrams SHALL update without manual refresh, per the reload behaviour every store here has.

### Requirement 2 — Routing: a query file opens on sight

**User Story:** As a user, I want double-clicking a `.rq` to open the query diagram with no ceremony.

#### Acceptance Criteria

1. WHEN the module declares itself THEN it SHALL expose the `w3c/sparql` `DiagramDefinition` — description in the reader's terms, a fitting icon — with `.rq` as its extension, routing a bare `.rq` on sight; no other type claims the extension and this type claims no other.
2. WHEN a user invokes Add on a `.rq` file THEN the choice tree SHALL offer `w3c/sparql`, creating only the `.adp` registration beside it — which is also what makes authored positions possible (Requirement 5.3).
3. WHEN a registration's `body:` header names the file THEN it SHALL resolve against the `.adp`'s own folder per the established rule.

### Requirement 3 — What is drawn as structure, and what is not

**User Story:** As a reader of a query I did not write, I want the graph pattern drawn as a graph and the machinery kept out of its way, so the diagram is easier to read than the text — which is its only reason to exist.

#### Acceptance Criteria

1. WHEN the where clause's patterns are drawn THEN each triple pattern SHALL be a directed labeled edge between term nodes — the predicate (or property path, as written: `foaf:knows+`) as the edge label — through the central canvas library.
2. WHEN group-shaped constructs appear THEN each SHALL draw as a labeled containment region holding its patterns: `OPTIONAL` as a dashed region, `UNION` as side-by-side alternative regions under one label, `MINUS` as a region labeled as exclusion, `GRAPH ?g`/`GRAPH <iri>` as a region labeled with its graph term, `SERVICE` as a region labeled with its endpoint — drawn, never contacted.
3. WHEN expression-shaped constructs appear THEN they SHALL attach as annotations showing the expression **as written**, never as parsed trees: a `FILTER` as a badge on the group it constrains, a `BIND` as an annotation on the variable it defines, a `VALUES` block as a small table annotation on the variables it feeds.
4. WHEN the query's frame is shown THEN the form (`SELECT`/`CONSTRUCT`/`ASK`/`DESCRIBE`), `DISTINCT`/`REDUCED`, and the solution modifiers (`GROUP BY`, `HAVING`, `ORDER BY`, `LIMIT`, `OFFSET`) SHALL render as a header band on the diagram — frame, not structure — with the full detail in the property grid (Requirement 6.2).
5. WHEN a `CONSTRUCT` template exists THEN it SHALL draw as its own clearly-separated region using the same pattern conventions, labeled as the shape being built, distinct from the where clause being matched.
6. WHEN a subquery appears THEN it SHALL draw as **one collapsed node** stating its projection (`SELECT ?x (COUNT(?y) AS ?n)`), its internals not drawn inline; selecting it shows the subquery text in the property grid. One level of honesty beats nested unreadability.
7. WHEN concrete terms are drawn THEN an IRI SHALL show its prefixed name (falling back per the family convention) as a solid node, and a literal its lexical form with datatype or language tag as a rectangle — the QueryVOWL-derived distinction the research adopts.

### Requirement 4 — Variables: the joins are the diagram

**User Story:** As a reader, I want to see at a glance which patterns share a variable, because that sharing is what the query means.

#### Acceptance Criteria

1. WHEN a variable appears in any number of triple patterns within one scope THEN it SHALL draw as **one node**, every pattern edge meeting it there — never one node per occurrence — so a variable's degree on the canvas is its join count in the query.
2. WHEN a variable is drawn THEN it SHALL be visually distinct from concrete terms (dashed outline, `?name` label), and a **projected** variable — named in the `SELECT` clause or `SELECT *`-included — SHALL carry a further mark, so "what leaves this query" reads without consulting the header.
3. WHEN a blank node appears in a pattern THEN it SHALL draw as an anonymous variable node (its SPARQL meaning), labeled as anonymous, joined exactly as written.
4. WHEN an expression aliases a variable (`BIND (... AS ?x)`, `(COUNT(?y) AS ?n)`) THEN `?x`/`?n` SHALL be ordinary variable nodes whose defining expression is their annotation (Requirement 3.3).
5. WHEN `UNION` branches each use a variable of the same name THEN the shared name SHALL read as shared: the variable draws once at the scope that joins the branches, with each branch's edges reaching it — matching SPARQL's semantics, where the branches feed one solution variable.

### Requirement 5 — Positions in the registration, never in the query

**User Story:** As a user arranging a query diagram, I want my layout kept without the `.rq` changing by a byte.

#### Acceptance Criteria

1. WHEN a user repositions an element THEN the position SHALL be stored via the family's **layout overlay** — the `.adp` `layout:` block — and the `.rq` SHALL be byte-identical before and after, which Requirement 1.4 already makes unconditional.
2. WHEN element ids are derived THEN a variable node SHALL key by its name, a concrete term node by its IRI or literal form, and a region by its construct kind and ordinal position among siblings — stable across reparses of an unchanged file, and stated in the design.
3. WHEN a bare `.rq` opens with no registration THEN repositioning SHALL be refused with the reason that there is nowhere to store the position; registering the file (Requirement 2.2) lifts the refusal.
4. WHEN an element is an anonymous variable THEN repositioning it SHALL be refused with the reason that an unnamed node has no stable identity to key a position by; it takes its computed place. This is this spec's own local rule, not the family's blank-node boundary.
5. WHEN layout is computed THEN it SHALL be pure and deterministic — same file, same picture — with containment regions laid out so contained patterns sit inside their region's bounds.

### Requirement 6 — Read-only, said out loud

**User Story:** As an author, I edit my queries in a text editor; I want the diagram to be honest about being a reading surface, with every affordance saying so rather than dangling.

#### Acceptance Criteria

1. WHEN this version ships THEN it SHALL offer **no editing gestures**: no toolbox entries, no mutating context actions, no canvas edits that write the `.rq`. This is the researched position (the YASGUI lesson, Requirement 3's research), not a deferral by silence — visual query *construction* is rejected, and any future editing is its own spec with its own splice story.
2. WHEN an element is selected THEN the property grid SHALL show its facts — a variable's name, projection status, join count and defining expression; a term's full IRI and prefixed name; a region's kind and its constraint text; the query's form and modifiers when the diagram itself is the selection — every row **read-only with a reason** naming the text editor as where the query is edited, per the property-grid contract that a reason is enforced, not decorative.
3. WHEN repositioning happens (Requirement 5) THEN that SHALL be the single mutating gesture in the module, landing on the `.adp` through the standard command with its byte-restoring inverse — one undo away, and the only thing there is to undo.

### Requirement 7 — Validation, and the deliberate exclusion of results

**User Story:** As an author, I want the problems panel to catch what makes a query file wrong or misleading — and I do not want ADP pretending it can run my query.

#### Acceptance Criteria

1. WHEN a file does not parse THEN validation SHALL report exactly one finding naming file, line and reason.
2. WHEN a projected variable never appears in the where clause THEN validation SHALL warn — the query is legal SPARQL and the column will be silently unbound, which is exactly the kind of lie a reader wants caught.
3. WHEN a prefix is declared but never used, or used but never declared (a parse error in strict SPARQL — reported under 7.1), THEN the unused declaration SHALL be an info-level finding with its line.
4. WHEN it comes to **results** THEN executing the query SHALL be out of scope entirely: no endpoint configuration, no HTTP, no result grid — stated here so the exclusion is a decision with a reason (running a query requires the network this family refuses, and a query tool that sometimes executes is a different product) rather than an omission a reader wonders about. `SERVICE` clauses draw as regions (Requirement 3.2) and are never contacted.
5. WHEN a query is degenerately large — beyond a local sanity bound of 500 drawn elements, this module's own number — THEN the diagram SHALL open truncated with an honest showing-N-of-M banner rather than hang; hand-written queries never meet this bound.

### Requirement 8 — Examples: real queries, found online, vendored honestly

**User Story:** As a user meeting this diagram type, I want real published queries — not toys — opening from the explorer, so the diagram proves itself on questions people actually ask.

#### Acceptance Criteria

1. WHEN the module ships THEN `src/diagrams/sparql/examples/` SHALL hold vendored real-world queries under the family's **example vendoring rule**, each candidate's license verified individually at acquisition time. Candidates verified during this spec's research: the **W3C SPARQL 1.1 Query recommendation's own examples** (w3.org/TR/sparql11-query — W3C Software and Document License; the every-construct corpus, exercising each form and Requirement 1.1's list), and the **SIB/UniProt example queries** (github.com/sib-swiss/sparql-examples and sparql.uniprot.org — UniProt content is CC BY 4.0, attribution-conditional and acceptable only with attribution carried; real bioinformatics queries with heavy `OPTIONAL`/`FILTER` use). The **Wikidata example-queries wiki page** was checked and is **rejected**: wiki page text is CC BY-SA, and share-alike is rejected under the vendoring rule — named here so the verification is visible. Substitution at acquisition time is allowed under the same verification.
2. WHEN an example is vendored THEN its folder SHALL carry the upstream license text and a provenance readme — source URL, version or retrieval date, verified license, exact local changes — with unmodified content unmodified.
3. WHEN the examples ship THEN among them SHALL be: one of each query form; one query exercising `OPTIONAL`, `UNION` and `FILTER` together; one with a property path; one with a subquery or aggregate — so every drawing rule in Requirement 3 is demonstrable on real material.
4. WHEN the examples are replicated THEN copies SHALL live at `src/examples/diagrams/sparql/`, covered by the central `ExampleRegistrationTests` by construction. (Corrected 2026-09-03: this criterion read "byte-identical copies … covered by `ExampleReplicationTests`". That guard byte-compared the two example trees and was deleted when they were deliberately disconnected, so byte-identity is no longer required or asserted; seeding the copy remains part of the work.)
5. WHEN the examples open THEN every registration SHALL resolve in the deployed catalog, and validation SHALL report zero findings above info level.

## Non-Functional Requirements

### Code Architecture and Modularity

- **One module**, `src/diagrams/sparql/` (backend, client, api, examples), depending on core only — deliberately **not** a definition inside `src/diagrams/rdf/`, because it shares that module's engine at no point: own parser, own model, own projection. The two consumed family pieces arrive through core (the canvas library, the `.adp` layout mechanism), not through the rdf module.
- **Element ids** per Requirement 5.2, shared by stream, resolver, layout block and tests.

### Performance

- Parsing linear in file size; any hand-written query parses imperceptibly; the local sanity bound (Requirement 7.5) is the only scale mechanism.

### Security

- No network, unconditionally: no endpoint is ever contacted, `SERVICE` is drawn and never resolved, and there is no configuration surface that could name an endpoint. Files are read within the registered project root under existing containment.

### Reliability

- The no-writer guarantee (Requirement 1.4) pinned by test; reload-on-external-change and unavailable-with-reason states each carry a test naming their sentence.
- Parser fixtures per query form and per construct in Requirement 1.1's list, including CRLF/LF and no-trailing-newline variants; if `.rq` fixtures are byte-compared, the `.gitattributes` entry follows the established by-extension rule with its reasoning recorded there.

### Usability

- Every refusal and unavailable state names its reason where the user is looking; prefixed names everywhere a human reads, full IRIs one selection away; the header band always says what form the query is, because that is the first thing a reader needs and the easiest to miss in text.

## Sources

- SPARQL 1.1 Query Language — w3.org/TR/sparql11-query (grammar, forms, the specification's own example corpus)
- SPARQL 1.1 Overview — w3.org/TR/sparql11-overview
- NITELIGHT and the query-builder survey — ceur-ws.org/Vol-1684/paper21.pdf (SPARQL Query Builders: Overview and Comparison)
- QueryVOWL: A Visual Query Notation for Linked Data — link.springer.com/chapter/10.1007/978-3-319-25639-9_51
- Gruff graphical query view — allegrograph.com/products/gruff
- RDF Explorer: A Visual SPARQL Query Builder — researchgate.net/publication/336596931
- ViziQuer: visual diagrammatic queries — arxiv.org/pdf/2304.14825
- YASGUI/YASQE (the text-first mainstream this spec's read-only position leans on) — yasgui.org
- SIB sparql-examples corpus — github.com/sib-swiss/sparql-examples · UniProt SPARQL — sparql.uniprot.org (CC BY 4.0)
- W3C Software and Document License — w3.org/copyright/software-license
