# Requirements Document

## Introduction

This spec covers **one diagram type, `w3c/skos`** — a SKOS concept scheme drawn as the thesaurus it states. It is a sibling of [`rdf-diagram`](../rdf-diagram/requirements.md), which defines the shared machinery this spec consumes: the same Turtle/N-Triples bytes, parsed once by the family's engine, read here as concepts and their semantic relations rather than as a bare data graph.

| Origin | File visualized | What it shows |
| --- | --- | --- |
| `w3c/skos` | A Turtle (`.ttl`) or N-Triples (`.nt`) file holding a SKOS concept scheme | The vocabulary: concepts under their schemes, broader/narrower as a top-down hierarchy, related and mapping links across it, collections as groups |

**The format, as researched.** SKOS (a W3C recommendation) models knowledge organization systems — thesauri, taxonomies, subject headings, controlled vocabularies. A `skos:ConceptScheme` owns `skos:Concept`s; `skos:broader`/`skos:narrower` state the hierarchy (declared inverses, but *each triple stands alone in a file*); `skos:related` cuts across it; `skos:prefLabel`/`altLabel`/`hiddenLabel` carry language-tagged names; `skos:notation` carries codes; documentation properties (`definition`, `scopeNote`, `example`, `note`…) carry prose; `skos:Collection`/`OrderedCollection` group concepts; mapping properties (`exactMatch`, `closeMatch`, `broadMatch`…) link vocabularies. The reference's integrity conditions (S13, S14, S27) are what real files violate; Requirement 7 owns what this diagram says then.

## Shared machinery consumed, never restated

The pieces below are defined in `rdf-diagram` and referenced here **by their bold names**; if a rule about them appears in this document, this document is wrong. Load-bearing for this reading: the **triplestore document store** (this spec adds a projection, never a parser), the **serialization boundary**, the **splice discipline** (this reading's gestures are its triple-writer under SKOS names), the **blank-node identity boundary**, the **drawn-element budget** (measured against this projection), the **layout overlay**, the **routing arrangement** (a bare `.ttl`/`.nt` is `w3c/rdf`'s; this reading is Add-suggested off its marker triple `skos:ConceptScheme`), the **no-network, no-inference rule**, and the **example vendoring rule**.

## How SKOS schemes are visualized — research, and the positions taken

**What the established tools do.** The SKOS tradition is not the node-and-edge tradition. Skosmos — the vocabulary browser behind Finto and many national vocabulary services — presents a *hierarchy tree* plus an alphabetical index, multilingual, one concept page at a time. SKOS Play — the visualizer the catalog names — renders alphabetical and hierarchical indexes for print and offers D3 tree, partition and sunburst views: every one of them hierarchy-first. VocBench edits SKOS in a tree and offers a graph only as a secondary "rooted at this node" view. Thesaurus print conventions (ISO 25964's presentation heritage) are alphabetical-plus-hierarchical, with polyhierarchical concepts *repeated* under each parent. The tradition's verdict is consistent: a vocabulary is read as a hierarchy, not explored as a graph.

**The position taken: a tree-shaped diagram, drawn as a DAG.** The default and only presentation is a top-down layered hierarchy: schemes at the top, their top concepts beneath, narrower concepts below their broader ones, level by level. Where print thesauri repeat a polyhierarchical concept under each parent, a canvas has what paper lacks — edges — so a concept with several broader concepts draws **once**, with one hierarchy edge per parent; repetition would break selection, positions and editing, which all need one element per concept. `skos:related` draws as a dashed cross-link between placed concepts, never influencing the layering. A separate force-directed or radial "graph mode" is deliberately absent: the same file can carry a `w3c/rdf` registration beside this one (the **routing arrangement** allows several readings over one store), and that sibling *is* the graph view — two `.adp` files instead of a mode switch.

**Labels are the content.** In every surveyed tool the reader never sees an IRI: a concept *is* its `skos:prefLabel`. Skosmos and SKOS Play are multilingual by construction and make language a first-class switch. This reading follows: the canvas shows labels chosen by an explicit, deterministic language rule (Requirement 3), with the IRI one selection away in the property grid, and a concept with no label at all is treated as the defect it is rather than silently rendered as an IRI.

**Rejected, with reasons:** repeating polyhierarchical concepts per parent (print convention; breaks element identity on an editable canvas); sunburst/partition/treemap overviews (SKOS Play offers them for print; no stable per-concept addressability for selection and layout); a built-in graph mode (redundant beside the `w3c/rdf` sibling reading); alphabetical-index panes (the explorer and property grid already own lookup and text).

## Alignment with Product Vision

- [product.md](../../steering/product.md)'s **"Files are the source of truth"** — the diagram renders the vocabulary file a repository already has; hierarchy edits land as reviewable splices; positions live in the `.adp`'s `layout:` block, never in the SKOS.
- product.md's **"Don't reinvent, integrate"** — SKOS is the W3C's own vocabulary model; the visual conventions are adopted from Skosmos, SKOS Play and the ISO 25964 presentation heritage, with citations, and the family engine is reused whole.
- product.md's **"Linkage over illustration"** — every node is a concept in a real file; selection resolves it, edits land as diffs, and the problems panel names real integrity violations with lines.
- [tech.md](../../steering/tech.md)'s **Diagram storage** and **Commands** — byte-identical round trips and inverse-carrying commands, both inherited through the shared machinery.
- [structure.md](../../steering/structure.md)'s **dependency direction** — no new module folder: this reading is a further `DiagramDefinition` inside `src/diagrams/rdf/`, the C4/databricks multi-definition arrangement.

## Requirements

### Requirement 1 — The scheme projection

**User Story:** As a vocabulary maintainer, I want my SKOS file drawn as the thesaurus it states — schemes, concepts, hierarchy, cross-links — so that I can read and review a vocabulary without walking triples by hand.

#### Acceptance Criteria

1. WHEN the diagram opens THEN every `skos:ConceptScheme` SHALL draw as a titled region and every `skos:Concept` as a node placed under the scheme it belongs to, membership taken from `skos:inScheme`, `skos:topConceptOf` or `skos:hasTopConcept` as asserted; a concept asserted into no scheme SHALL still draw, in a distinct unfiled band (Requirement 7 reports it).
2. WHEN `skos:broader` or `skos:narrower` is asserted between two concepts THEN exactly one hierarchy edge SHALL draw between them, oriented broader-above-narrower regardless of which direction the file asserted; asserting both directions SHALL still draw one edge. This is a projection of asserted triples — the store SHALL NOT be handed a completed inverse, and no completion SHALL ever be written to the file (the **no-network, no-inference rule**).
3. WHEN `skos:related` is asserted THEN it SHALL draw as a visually distinct cross-link that does not affect the hierarchy layering; mapping properties (`skos:exactMatch`, `closeMatch`, `broadMatch`, `narrowMatch`, `relatedMatch`) SHALL draw as a third edge style **only when both ends are concepts in this file**, and otherwise appear as property-grid rows on the in-file end, so the canvas never grows stub nodes for the outside world.
4. WHEN a concept carries `skos:notation` THEN the notation SHALL render as a code badge beside the label — the descriptor-code convention of published thesauri.
5. WHEN a `skos:Collection` or `skos:OrderedCollection` is asserted THEN it SHALL draw as a labeled group of its members, order preserved for the ordered kind, and collections SHALL never be conflated with the hierarchy (SKOS S13's families are disjoint).
6. WHEN a concept is polyhierarchical — several broader concepts — THEN it SHALL draw once with one hierarchy edge per parent, never repeated per parent (the position taken above).

### Requirement 2 — Registration and routing

**User Story:** As a user, I want the scheme reading to be a deliberate choice over a file the graph reading also serves, so that a bare vocabulary file still opens with no ceremony and the readings never fight.

#### Acceptance Criteria

1. WHEN the family module declares itself THEN it SHALL expose the `w3c/skos` `DiagramDefinition` — description, fitting icon — as a further definition over the **triplestore document store**, inside `src/diagrams/rdf/`, with no new module folder.
2. WHEN a bare `.ttl`/`.nt` file exists THEN this reading SHALL NOT claim it (the **routing arrangement**); it opens as `w3c/rdf`, and `w3c/skos` is chosen by explicit registration.
3. WHEN a user invokes Add on a `.ttl`/`.nt` file whose triples assert a `skos:ConceptScheme` THEN the choice tree SHALL suggest `w3c/skos` per the **routing arrangement**'s marker-triple rule; a file with concepts but no scheme SHALL still be registrable as `w3c/skos` by explicit choice.
4. WHEN a file holds several concept schemes THEN one registration SHALL draw them all, each as its own region (Requirement 1.1), so the common one-file-many-microthesauri layout (EuroVoc's domains) needs no ceremony.
5. WHEN a `w3c/skos` and a `w3c/rdf` registration point at one file THEN each SHALL open its own reading of the same parsed document, and an edit through either SHALL be visible in both — one store, one document, one history.

### Requirement 3 — Labels are the content

**User Story:** As a reader of a multilingual vocabulary, I want every concept named in a predictable language with the alternatives one selection away, so that the diagram is legible without me knowing the file's triple order.

#### Acceptance Criteria

1. WHEN a concept is drawn THEN its displayed name SHALL be its `skos:prefLabel` chosen by a deterministic preference: the registration's declared display language first, then `en`, then the untagged literal, then the lexicographically smallest remaining language tag — the same file always naming every concept the same way.
2. WHEN the `.adp` registration carries a display-language line (the reading-specific header this definition contributes, following the precedent of C4's `view:`) THEN that language SHALL head the preference order; absent the line, the default order stands.
3. WHEN the displayed label's language differs from the preferred display language THEN the node SHALL wear a small language tag beside the label, so a gap in translation is visible rather than silent.
4. IF a concept has no `skos:prefLabel` in any language THEN the node SHALL fall back to a `skos:altLabel` (chosen by the same preference, marked visually as an alternate), and failing that to the IRI's prefixed name rendered in a dimmed code style — and Requirement 7 SHALL report the missing label, because an unlabeled concept is a defect in a vocabulary, not a style choice.
5. WHEN labels exist in several languages THEN the property grid SHALL list every `prefLabel`, `altLabel` and `hiddenLabel` with its language tag, grouped by kind, so the whole labeling of a concept is one selection away.
6. WHEN a file labels its concepts through SKOS-XL (`skosxl:prefLabel` reifying labels as resources) THEN this reading SHALL NOT resolve the indirection in its first version; the concepts draw with the plain-SKOS fallbacks of 3.4, and validation SHALL state, once per file, that SKOS-XL labels are present and unread — the boundary learned from the tool, not from silence.

### Requirement 4 — Layout: the hierarchy is the picture

**User Story:** As a user, I want the vocabulary laid out top-down as the tradition draws it, deterministic on every open, with my own arrangements kept outside the SKOS file.

#### Acceptance Criteria

1. WHEN layout is computed THEN it SHALL be a pure, deterministic top-down layering: schemes as top-level regions, top concepts (asserted via `topConceptOf`/`hasTopConcept`, or having no asserted broader within the scheme) on the first row, each concept below its broader concepts, unfiled concepts in their own band; the same file SHALL always open the same way.
2. WHEN the asserted hierarchy contains a cycle THEN layout SHALL break the cycle deterministically (at the edge whose subject-object IRI pair sorts lowest), still draw every edge, and Requirement 7 SHALL name the cycle — the diagram never hangs and never silently drops a concept.
3. WHEN a user repositions a concept THEN the position SHALL be stored via the **layout overlay**, keyed by the concept's IRI-derived id, the SKOS file byte-identical before and after; stored positions override computed ones element by element.
4. IF an element is blank-node-rooted THEN positioning and per-element edits SHALL follow the **blank-node identity boundary** verbatim.

### Requirement 5 — Editing: thesaurus gestures over the splice discipline

**User Story:** As a vocabulary author, I want the edits a hierarchy canvas is good at — file a concept under a parent, cross-link two concepts, add and remove concepts, fix a label — landing as minimal diffs, each one undo away.

#### Acceptance Criteria

1. WHEN two concepts are connected with the hierarchy gesture THEN one `skos:broader` triple SHALL be written **on the narrower concept** — the direction thesaurus data is conventionally authored in — through the **splice discipline**, reusing declared prefixes; the inverse `narrower` SHALL NOT be co-written (no completion, per Requirement 1.2), and undo SHALL restore bytes.
2. WHEN two concepts are connected with the related gesture THEN one `skos:related` triple SHALL be written the same way.
3. WHEN a concept is dropped from the toolbox THEN it SHALL be created as a subject with `rdf:type skos:Concept`, a `skos:prefLabel` taken from a dialog (in the preferred display language), and `skos:inScheme` naming the scheme region it was dropped into — one command, one inverse.
4. WHEN a label shown in the property grid is edited THEN the splice SHALL rewrite exactly that literal — its language tag preserved — never the concept's IRI; renaming the IRI itself remains the family rename with its collision refusal.
5. WHEN a concept is removed through the canvas THEN the removal SHALL take every triple whose subject or object it is, with the count stated before anything runs, as the shared machinery prescribes.
6. IF an edit targets blank-node-rooted content, a refused serialization, or a truncated view THEN it SHALL be refused before any splice with a sentence naming the reason.

### Requirement 6 — Toolbox, context actions, property grid

**User Story:** As a user, I want the palette, menus and grid to speak the vocabulary's language — concepts, labels, notes — with everything landing through the standard paths.

#### Acceptance Criteria

1. WHEN the diagram is open THEN the toolbox SHALL offer a Concept (dropped via placement id); context menus SHALL discover the hierarchy and related connect gestures, remove-with-references (count first), and disconnect per edge, per the standard provider path, every mutating entry one undo away.
2. WHEN a concept is selected THEN the property grid SHALL show: identity (full IRI read-only naming rename as the identity path; notation), every label by language (Requirement 3.5, `prefLabel` rows editable per Requirement 5.4), documentation properties (`skos:definition`, `scopeNote`, `example`, `note`, `historyNote`, `editorialNote`, `changeNote`) as rows editable through the standard property path, scheme memberships, and out-of-file mappings (Requirement 1.3) read-only with their reason.
3. WHEN a scheme region or collection group is selected THEN the grid SHALL show its label and its member count, labels editable, membership read-only naming the canvas gestures as the path.
4. WHEN an action cannot apply THEN it SHALL be discovered unavailable with its reason or refused with the same sentence — never silently absent.

### Requirement 7 — Validation: what a lying vocabulary looks like

**User Story:** As a maintainer, I want the problems panel to name the ways real SKOS files go wrong — inconsistent hierarchies, label defects, unfiled concepts — without a reasoner's opinions.

#### Acceptance Criteria

1. WHEN the asserted `broader`/`narrower` relation contains a cycle THEN validation SHALL report one error naming the concepts on the cycle (by displayed label) and the file lines of the edges, matched to Requirement 4.2's drawing.
2. WHEN a concept carries more than one `skos:prefLabel` in one language (SKOS S14) THEN validation SHALL report an error with the lines; the canvas draws the lexicographically first and says nothing else.
3. WHEN `skos:related` is asserted between two concepts that are **directly** related by an asserted `broader`/`narrower` triple (the direct case of S27) THEN validation SHALL warn with the lines. The transitive case — related cutting across a *chain* of broader — requires entailment and SHALL NOT be checked, with that boundary stated in the rule's documentation rather than checked badly (the **no-network, no-inference rule**).
4. WHEN a concept has no `skos:prefLabel` in any language THEN validation SHALL warn, naming the concept's IRI (Requirement 3.4 draws the fallback).
5. WHEN a concept is asserted into no scheme and reachable from no top concept THEN validation SHALL report an info naming it — an unfiled concept is legal SKOS and usually a mistake.
6. WHEN SKOS-XL label triples are present THEN validation SHALL report one info per file (Requirement 3.6).
7. WHEN a hierarchy or membership assertion targets something that is not a `skos:Concept` by assertion THEN validation SHALL warn with the line — typing is not inferred, so only the file's own silence is reported.

### Requirement 8 — Scale: the budget over this projection

**User Story:** As a user opening a national-scale thesaurus, I want the diagram honest about what it can draw.

#### Acceptance Criteria

1. WHEN the projection exceeds the **drawn-element budget** THEN the truncated view, banner, withheld edits and deterministic first-N ordering SHALL apply exactly as the shared machinery states, with N counted over this reading's drawn concepts, schemes and collections.
2. WHEN the view is truncated THEN the deterministic order SHALL be hierarchy-aware — schemes, then top concepts, then breadth-first by layer, ties broken by IRI — so the truncated view is the top of the vocabulary rather than an arbitrary triple-order slice.

### Requirement 9 — Examples: real vocabularies, vendored honestly

**User Story:** As a user meeting this diagram type, I want real published vocabularies — not toy fruit taxonomies — opening from the explorer with no setup.

#### Acceptance Criteria

1. WHEN the examples ship THEN `src/diagrams/rdf/examples/` SHALL gain vendored SKOS data under the **example vendoring rule**, each license verified individually at acquisition time. The candidates verified during this spec's research: an extract of the **NAL Agricultural Thesaurus** (USDA; published under CC0 1.0, offered upstream in SKOS Turtle and N-Triples), an extract of **EuroVoc** (EU Publications Office; listed CC0 on the EU open-data portal — re-verify at acquisition; 24 languages, domain-partitioned, the multilingual and many-schemes demonstration), and the **W3C SKOS reference's own examples** (W3C Software and Document License; the every-construct sample: collections, notations, documentation properties, mappings). The **STW Thesaurus for Economics** (ZBW; CC BY 4.0) MAY be vendored only with its attribution carried. Rejected by this research, with reasons recorded: the **UNESCO Thesaurus** (CC BY-SA IGO — share-alike, refused by the vendoring rule) and **AGROVOC** (license acceptable at CC BY, but its labels are SKOS-XL, which Requirement 3.6 leaves unread — a worst-foot-forward example). Substitution at acquisition time is allowed under the same verification.
2. WHEN an example is vendored THEN its folder SHALL carry the upstream license text and provenance readme per the **example vendoring rule**, extracts described as extracts.
3. WHEN the examples ship THEN at least one SHALL be polyhierarchical, at least one multilingual with an incomplete translation (Requirement 3.3's tag visible on real data), at least one SHALL carry collections and notations, and one SHALL exceed the **drawn-element budget** so the truncated view shows on a real vocabulary.
4. WHEN the examples are replicated THEN copies SHALL live at `src/examples/diagrams/skos/`, covered by the central `ExampleRegistrationTests` by construction. (Corrected 2026-09-03: this criterion read "byte-identical copies … covered by `ExampleReplicationTests`". That guard byte-compared the two example trees and was deleted when they were deliberately disconnected, so byte-identity is no longer required or asserted; seeding the copy remains part of the work.)
5. WHEN the examples open THEN every registration SHALL resolve in the deployed catalog, and validation SHALL report zero findings above info level.

## Non-Functional Requirements

### Code Architecture and Modularity

- **No new module**: the `w3c/skos` definition, its projection, layout, label chooser, writers and validator join `src/diagrams/rdf/` beside `w3c/rdf`, all over the one store — the C4/databricks multi-definition arrangement. The projection, layering and label choice are pure functions over the store's model.
- Element ids derive from concept IRIs through the family's shared id rule, so the stream, resolver, **layout overlay** and tests agree.

### Performance

- The projection and layering are linear in triples plus edges; everything drawn is bounded by the **drawn-element budget**; the label chooser does no per-frame work (chosen once per parse per language preference).

### Security

- The **no-network, no-inference rule** verbatim — mapping IRIs to other vocabularies are never dereferenced.

### Reliability

- The label-choice rule, the either-direction hierarchy projection, the cycle-breaking determinism and every validation rule of Requirement 7 SHALL each be pinned by tests over fixture vocabularies, including a fixture asserting only `narrower`, one asserting both directions, and one with a cycle.
- Every refusal (SKOS-XL, blank node, truncation, out-of-file mapping edit) carries its sentence in a test.

### Usability

- Labels everywhere a human reads; IRIs one selection away; every unavailable state names its reason where the user is looking; the language tag of Requirement 3.3 is visible but quiet.

## Sources

- SKOS Simple Knowledge Organization System Reference — w3.org/TR/skos-reference (integrity conditions S13, S14, S27; label and documentation properties)
- SKOS Primer — w3.org/TR/skos-primer
- Skosmos vocabulary browser — skosmos.org (the hierarchy-tree-plus-index browsing convention; multilingual display)
- SKOS Play — skos-play.sparna.fr (the catalog's named tool: alphabetical/hierarchical rendering, D3 tree and partition views)
- VocBench SKOS editing — vocbench.uniroma2.it (tree-first editing, graph as secondary rooted view)
- ISO 25964 thesaurus presentation heritage (alphabetical + systematic displays; polyhierarchy repeated in print — rejected here with reasons)
- NAL Agricultural Thesaurus — lod.nal.usda.gov/nalt (CC0 1.0, SKOS Turtle/N-Triples downloads)
- EuroVoc — op.europa.eu / data.europa.eu dataset listing (CC0 per the EU open-data portal; re-verify at acquisition)
- STW Thesaurus for Economics — zbw.eu/stw (CC BY 4.0)
- UNESCO Thesaurus — vocabularies.unesco.org (CC BY-SA IGO — rejected: share-alike)
- AGROVOC — fao.org/agrovoc (CC BY; rejected for v1: SKOS-XL labels)
