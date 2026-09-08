# Sufficiency — twenty-eight element types against the declared vocabulary

Produced **after** Group 1 landed and **before** any module is migrated, which is the whole point of it: at this moment the vocabulary has unit guards and no callers, so the table checks the design rather than plans the work. Every row is read out of the module's own `CustomShapeRef`, not out of memory.

**Twenty-eight rows** — one per `CustomShapeRef` declaration, counted by `grep -c` rather than by hand, across sixteen canvases in thirteen modules (`rdf` has four readings and therefore four canvases). The count is the same 28 the requirements recorded, and no module today uses a built-in shape.

## The procedure when a row cannot be filled

Fixed in advance, because it is needed at the moment everyone wants to be finished:

1. **Record the gap** in the register below, with the module and what it needs.
2. **Extend the vocabulary centrally** (Requirement 1.5) — in the library, for every module, never in the module that surfaced it.
3. **Re-check the table.**

**A module is never marked an exception, and migration does not begin with an unfilled row.** An unfillable row is the design being wrong; it is not a module that is unusual.

And the converse (Requirement 3.5): **a property added because a row needed it is justified by that row and named in it.** Nothing in the register below exists because it seemed generally useful.

## The table

`Built-in` names the shape the row's custom renderer collapses onto. `Gaps` names the register entries the row needs before it can be declared; a row with no gaps is fillable today.

| # | Module · shape | Built-in | Labels | Decorations | Background | Actions | Gaps |
|---|---|---|---|---|---|---|---|
| 1 | ansible-structure · nodeShape | box | name | — | — | activate, contextSelect, Enter | G1, G2, G3, G7 |
| 2 | ansible-structure · stubShape | — (glyph only) | edge text | line stub | — | — | G1, G8 |
| 3 | azure-pipeline · stageShape | rounded-rectangle | displayName; job count when collapsed | status indicators, problem mark | — | F2, Enter, expand/collapse | G1, G2, G4, G5, G6, G9 |
| 4 | azure-pipeline · boxShape | box | displayName; tooltip when unresolved | status indicators, problem mark | — | F2, Enter | G1, G2, G4, G6, G9 |
| 5 | c4 · cardShape | styled-box | name, type line, description (3 stacked) | — | — | F2, Insert, Enter, Delete | G1, G2, G10 |
| 6 | c4 · boundaryShape | frame | `name [kind]` | — | — | — | G1 |
| 7 | causal-loop · variableShape | pill | display | — | — | rename | G1, G2 |
| 8 | causal-loop · loopShape | — (glyph only) | polarity caption | polarity arc + arrowhead | — | — | G1, G2, G8, G11 |
| 9 | databricks · nodeShape | box | label; badges joined | — | — | F2, Enter, Delete | G1, G2, G4 |
| 10 | databricks · frameShape | frame | label; mode/default/overrides joined | — | — | — | G1, G2, G4, G5 |
| 11 | dependency-graph · nodeShape | span | label ∥ id | — | — | F2, Insert, Tab, Enter, Delete | G1, G2 |
| 12 | dotnet-dependency-graph · nodeShape | span | name; versions/frameworks below; tooltip | — | — | activate, contextSelect | G1, G2, G4, G7 |
| 13 | helm-charts · nodeShape | box | name; tooltip | — | — | activate, contextSelect | G1, G2, G7 |
| 14 | helm-charts · stubShape | — (glyph only) | edge text | line stub | — | — | G1, G8 |
| 15 | mindmap · nodeShape | centered-box | text; conditional indicator glyphs | drop-target ring, dashed branch preview | — | F2, Insert, Tab, Enter, Delete | G1, G2, G4, G12 |
| 16 | rdf/Owl · cardShape | box | display; badges joined; one line per row, truncated | — | — | F2, Delete | G1, G2, G4, G13 |
| 17 | rdf/Owl · datatypeShape | box | display; tooltip | — | — | F2, Delete | G1, G2, G7 |
| 18 | rdf/Owl · roundShape | ellipse | display, truncated to width | inner ring when doubled | — | F2, Delete | G1, G2, G14 |
| 19 | rdf/Rdf · cardShape | box | display; badges joined; one line per row | — | — | F2, Delete | G1, G2, G4, G13 |
| 20 | rdf/Shacl · cardShape | box | display ∥ `display — name`; badges joined right-aligned; one line per target; per row: path, cardinality (right), summary | — | — | F2, Delete | G1, G2, G4, G13, G15 |
| 21 | rdf/Skos · conceptShape | box | label; notation; language chip | — | — | F2, Delete | G1, G2, G7 |
| 22 | rdf/Skos · regionShape | box | `label (memberCount)` ∥ label; "ordered" when ordered | — | — | — | G1, G2 |
| 23 | sparql · nodeShape | box | display; annotation when present | projection arrow when projected | — | rename | G1, G2 |
| 24 | sparql · regionShape | frame | label | — | — | — | G1 |
| 25 | sparql · annotationShape | — (text only) | text | — | — | — | G1, G8 |
| 26 | timeline · spanShape | span | label ∥ id; drag hint | moment dot when not a period | ruler (now chrome, task 7) | F2, Insert, Tab, Enter, Delete | G1, G2, G16, G17 |
| 27 | wardley-map · markShape | symbol | name at a model-supplied pixel offset; decorator badges | square / circle / double circle by kind; inertia mark | axes, bands, attitudes, accelerators, notes, annotations | F2, Enter | G1, G2, G4, G14, G18, G19 |
| 28 | wardley-map · evolveTargetShape | symbol | override name when present | dot | (as row 27) | — | G1, G14 |

## The gap register

Nineteen entries. Each names the rows that justify it; none is speculative. **Every one is a central extension** — a property on the library's declaration types — and none is a module carve-out.

| Id | What is missing | Justified by |
|---|---|---|
| **G1** | **A class hook on a built-in shape.** Every built-in hard-codes `library-shape` and offers no way to add a type's own class, so a migrated element loses its entire visual identity. Needs a declared class list on the element type, and per-element classes derived from bound values. | Every row, 1–28 |
| **G2** | **A condition over interaction state** — `selected`, `dragging`, `connectTarget`, `focused`, `expanded`. `Condition` reads the model only, and twenty-three rows put a class on one of these. | 1, 3–5, 7–13, 15–23, 26, 27 |
| **G3** | **A categorical slot assigned across the model** — ansible's play palette index, which is an ordinal over the model's plays rather than a field on the element. | 1 |
| **G4** | **A collection joined into one line with a separator.** Labels stack lines; badges are `join(" · ")` on one. | 3, 4, 9, 10, 12, 15, 16, 19, 20, 27 |
| **G5** | **A line assembled from conditional parts** — `mode`, `"default"` when default, `"N overrides"` when non-zero — where an absent part leaves no separator behind. | 3, 10 |
| **G6** | **The problem mark.** A severity badge fed by the problems service rather than the diagram model, drawn at a corner of whichever element carries a problem. | 3, 4 |
| **G7** | **A tooltip with a conditional tail** — `kind name` alone, or `kind name — hosts: …` when hosts are known. `tooltip` exists on a label; a tooltip on the element as a whole does not. | 1, 12, 13, 17, 21 |
| **G8** | **An element with no body** — a stub line, a loop badge, a bare annotation. `shape` is required and every built-in draws something. | 2, 8, 14, 25 |
| **G9** | **A formatted count** — `3 jobs` / `1 job`. A template can interpolate the number; nothing can decline the plural. | 3 |
| **G10** | **Per-element style bound from the model.** c4 stores shape, background and colour on the payload; `ElementStyle` takes theme token names fixed in the declaration. | 5 |
| **G11** | **A decoration path chosen by a condition** — the reinforcing sweep clockwise, the balancing sweep anticlockwise, with an end marker. Two `path` decorations with `when` cover it only if a decoration may carry `markerEnd`. | 8 |
| **G12** | **Drag-time preview requiring a model query** — the candidate parent under the pointer, ringed, with the branch that would be created. This is the one row where the honest answer may be "an event handler", and saying so here is the point: it is recorded, not hidden. | 15 |
| **G13** | **A row-per-item block with per-item internal layout** — predicate, value and annotation composed into one line, one line per collection item, offset by index. `CollectionBinding` plus `stack` reaches this; what is missing is a **second and third column within the item's line**. | 16, 19, 20 |
| **G14** | **A shape variant chosen per element** — square / circle / double circle, ellipse doubled. `symbol` and `ellipse` render one form each. | 18, 27, 28 |
| **G15** | **Right-aligned and column-positioned label placement.** Slots are vertical fractions; `textAnchor="end"` at the box's right edge and a fixed x-column have no expression. | 20 |
| **G16** | **A label from a value computed against the module's scale** — the drag hint's formatted time and row. Bindings read paths; this is arithmetic over `secondsPerUnit` and `originSeconds`. | 26 |
| **G17** | **`span` has no `moment`** — a point in time rather than a period, which is a different drawing rather than a class. | 26 |
| **G18** | **Background items driven by a model collection at model coordinates** — attitudes, accelerators, notes, numbered annotations. Task 4's background takes fixed lists in extent fractions. | 27 |
| **G19** | **Typography that scales with the view** — Wardley's stage and axis labels hold a readable size as the map zooms, while the boundaries they name do not. | 27 |

## Status

**Nineteen gaps, no unfillable rows once they are closed, and no module marked an exception.** The register is the input to the central extension that must land before task 10 begins; each closure is verified by re-reading the row that justified it, not by the extension's own tests.

Two entries deserve their honesty stated rather than buried. **G12** may turn out to be an event handler rather than a declaration — the permitted carve-out — and if it does, that is recorded as a decision with a reason, not as mindmap being unusual. **G6** crosses a boundary this specification does not own: the problem mark is fed by the problems service, so its declaration says *where a problem mark goes*, never *what a problem is*.
