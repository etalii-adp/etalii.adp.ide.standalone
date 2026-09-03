# RDF examples

Real, found-online RDF on the main path — not toy strawmen — per rdf-diagram Requirement 9 and
the vendoring discipline the helm-charts specification established: every source's license was
individually verified before anything was copied, each folder's `readme.md` records provenance
(source URL, retrieval date, verified license, exact local changes), and unmodified content is
unmodified.

- **`w3c-turtle/`** — the Turtle specification's own canonical example, verbatim: the smallest
  real document exercising prefixes, `a`, predicate lists, object lists and a language-tagged
  literal.
- **`wikidata/`** — Marie Curie's Wikidata statements, in both of the family's serializations:
  the Turtle statement block byte-identical from Wikidata's own export, and the same truthy
  statements as N-Triples. A dense single-subject graph: one card with many literal rows, many
  outgoing edges.
- **`nobel/`** — the Nobel Prize dataset, derived from the official CC0 API by the committed
  script beside it: `nobel-1903.ttl` is one coherent year (the year both Curies stood in
  Stockholm), and `laureates.ttl` is the whole dataset — 1,657 resources, deliberately past the
  drawn-element budget, so the truncated first-N view has a real file to be honest about
  (Requirement 8).
