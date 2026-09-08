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
| 11 | dependency-graph · nodeShape | span | label ∥ id | — | — | F2, Insert, Tab, Enter, Delete | G1, G2, G20, G21 |
| 12 | dotnet-dependency-graph · nodeShape | span | name; versions/frameworks below; tooltip | — | — | activate, contextSelect | G1, G2, G4, G7, G20, G21 |
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
| 26 | timeline · spanShape | span | label ∥ id; drag hint | moment dot when not a period | ruler (now chrome, task 7) | F2, Insert, Tab, Enter, Delete | G1, G2, G16, G17, G20, G21 |
| 27 | wardley-map · markShape | symbol | name at a model-supplied pixel offset; decorator badges | square / circle / double circle by kind; inertia mark | axes, bands, attitudes, accelerators, notes, annotations | F2, Enter | G1, G2, G4, G14, G18, G19 |
| 28 | wardley-map · evolveTargetShape | symbol | override name when present | dot | (as row 27) | — | G1, G14 |

## The gap register

Nineteen entries. Each names the rows that justify it; none is speculative. **Every one is a central extension** — a property on the library's declaration types — and none is a module carve-out.

Every entry is now **closed**, and the third column says what closed it — the property added and where it lives, so a reader can check the claim rather than take it. The two rulings are marked as rulings, because a ruling is a different kind of answer from a mechanism.

| Id | What is missing | Justified by | Closed by |
|---|---|---|---|
| **G1** | **A class hook on a built-in shape.** Every built-in hard-codes `library-shape` and offers no way to add a type's own class, so a migrated element loses its entire visual identity. Needs a declared class list on the element type, and per-element classes derived from bound values. | Every row, 1–28 | `ElementTypeDefinition.classNames` — stated, bound or conditioned, on every built-in shape |
| **G2** | **A condition over interaction state** — `selected`, `dragging`, `connectTarget`, `focused`, `expanded`. `Condition` reads the model only, and twenty-three rows put a class on one of these. | 1, 3–5, 7–13, 15–23, 26, 27 | `BindingSource.state`, a third root beside `element` and `payload`, read as `state.selected` |
| **G3** | **A categorical slot assigned across the model** — ansible's play palette index, which is an ordinal over the model's plays rather than a field on the element. | 1 | `NumberFormat.modulo`, sign-corrected |
| **G4** | **A collection joined into one line with a separator.** Labels stack lines; badges are `join(" · ")` on one. | 3, 4, 9, 10, 12, 15, 16, 19, 20, 27 | `CollectionBinding.join` |
| **G5** | **A line assembled from conditional parts** — `mode`, `"default"` when default, `"N overrides"` when non-zero — where an absent part leaves no separator behind. | 3, 10 | `PartsBinding` |
| **G6** | **The problem mark.** A severity badge fed by the problems service rather than the diagram model, drawn at a corner of whichever element carries a problem. | 3, 4 | `DecorationDeclaration.className` becomes bindable, plus `tooltip` — the module's model still says what a problem is |
| **G7** | **A tooltip with a conditional tail** — `kind name` alone, or `kind name — hosts: …` when hosts are known. `tooltip` exists on a label; a tooltip on the element as a whole does not. | 1, 12, 13, 17, 21 | `ElementTypeDefinition.tooltip` |
| **G8** | **An element with no body** — a stub line, a loop badge, a bare annotation. `shape` is required and every built-in draws something. | 2, 8, 14, 25 | the `none` built-in shape |
| **G9** | **A formatted count** — `3 jobs` / `1 job`. A template can interpolate the number; nothing can decline the plural. | 3 | `FieldBinding.plural` |
| **G10** | **Per-element style bound from the model.** c4 stores shape, background and colour on the payload; `ElementStyle` takes theme token names fixed in the declaration. | 5 | `ElementTypeDefinition.boundStyle`, and `ShapeSelection` for the shape itself |
| **G11** | **A decoration path chosen by a condition** — the reinforcing sweep clockwise, the balancing sweep anticlockwise, with an end marker. Two `path` decorations with `when` cover it only if a decoration may carry `markerEnd`. | 8 | `DecorationDeclaration.markerEnd`, with the two sweeps as two `when`-conditioned paths |
| **G12** | **Drag-time preview requiring a model query** — the candidate parent under the pointer, ringed, with the branch that would be created. This is the one row where the honest answer may be "an event handler", and saying so here is the point: it is recorded, not hidden. | 15 | **a ruling, not a mechanism** — see below |
| **G13** | **A row-per-item block with per-item internal layout** — predicate, value and annotation composed into one line, one line per collection item, offset by index. `CollectionBinding` plus `stack` reaches this; what is missing is a **second and third column within the item's line**. | 16, 19, 20 | `LabelDeclaration.columns`, paired to their row by `resolveEntries` |
| **G14** | **A shape variant chosen per element** — square / circle / double circle, ellipse doubled. `symbol` and `ellipse` render one form each. | 18, 27, 28 | `ShapeSelection`, plus the `double-ellipse` built-in |
| **G15** | **Right-aligned and column-positioned label placement.** Slots are vertical fractions; `textAnchor="end"` at the box's right edge and a fixed x-column have no expression. | 20 | `LabelDeclaration.align` and a column's `insetX` |
| **G16** | **A label from a value computed against the module's scale** — the drag hint's formatted time and row. Bindings read paths; this is arithmetic over `secondsPerUnit` and `originSeconds`. | 26 | `FieldBinding.number` — `times`, then `plus`, then `modulo` |
| **G17** | **`span` has no `moment`** — a point in time rather than a period, which is a different drawing rather than a class. | 26 | the `moment` built-in, selected per element |
| **G18** | **Background items driven by a model collection at model coordinates** — attitudes, accelerators, notes, numbered annotations. Task 4's background takes fixed lists in extent fractions. | 27 | `MarkDeclaration` — one positioned item per collection entry; nested collections refused |
| **G19** | **Typography that scales with the view** — Wardley's stage and axis labels hold a readable size as the map zooms, while the boundaries they name do not. | 27 | `LabelTypography.scaleWithView`, clamped by the declaration |
| **G20** | **Edge attachment constrained to one axis.** Three canvases attach on the left or right side facing the other end whatever the angle, because the notation reads left-to-right; a true edge intersection leaves through the top for a steep pair. Each spells it in a custom shape's `edgePoint`, which is one of the two reasons those shapes exist. | 11, 12, 26 | `AnchorEnablement.edgeSides` — beside `visible` and `enabled`, because it governs the fallback a named-anchor type still uses |
| **G21** | **An editor over a *declared* label.** `labels` replaced `label` for drawing in task 2, and the inline-editor placement was left reading only the deprecated rule — so a migrated type marked editable silently got no editor. | 11, 12, 26, and every editable row | `placementOfLabel` reads the editable declaration, and opens over the line rather than over the element |

## Status after the re-check

**Twenty-one gaps found, twenty-one closed, no module marked an exception, and no unfillable row left.** Nineteen came from the table; **two came from the reference migration doing what the migration is for** — G20 and G21 below. That two of twenty-one were invisible to a row-by-row reading is the honest measure of what a table can and cannot check: it reads what an element *draws*, and both of those are about what happens *around* the drawing. Every closure is a central extension in the library — nothing was added to a module, and nothing was added that no row asked for.

**What is proven and what is not.** Each extension has unit guards, and each guard was seen to fail against the defect it covers: nine sabotages across the three landings — state dropped from the root, an empty collection joined to a blank line, parts joined without filtering, `plus` applied before `times`, an absent number read as zero, declared classes dropped from the shape, a shape selection always taking its fallback, a bound decoration class dropped, the view scale left unclamped. What is **not** proven is that a migrated module draws what it drew before; that is Group 3's job, one module at a time, and the table is its order and its acceptance.

**A sabotage found a hole in the reference migration's own safety net.** Removing `edgeSides` from `dependency-graph` left all forty-five of its tests green: the module routes with its own path builder, which takes the endpoint *bounds* and picks its own anchors, so the attachment point never reaches the drawn line there. The guard for that behaviour therefore lives in the library, where it stands for rows 12 and 26, which have no such shelter. Without the sabotage the declaration would have looked verified and been nothing of the kind.

**One defect was found by writing the guards rather than by review**: `Number(null)` is `0`, so a missing count read as *"0 jobs"* and a missing instant as *1 Jan 1970* — data where there was none, breaking the rule the binding resolver rests on. It is fixed, and it is the kind of thing this table exists to surface before thirteen modules depend on it.

### The two answers that are rulings rather than mechanisms

**G12 — mindmap's drag preview.** The candidate parent under the pointer, ringed, with the branch that would be created: it needs a query over the model at drag time, which no binding can express and none should. The ruling splits it where the specification already draws the line: **the decision stays an event handler** — the permitted carve-out — **and the drawing becomes declared decorations** on the candidate, conditioned on a field the module's own fold sets while the drag is in flight. No new mechanism, and mindmap keeps no renderer. It is a ruling rather than a proof: it is verified when mindmap migrates in task 11, and if it fails there the failure is the design's, not mindmap's.

**G6 — the problem mark.** The problems service is not this specification's to move, so the declaration says *where a problem mark goes and what colours it*, and the module's model still says *what a problem is*. That boundary is deliberate: a library that knew what a problem was would own two things at once.

### What the design got right, stated as plainly as what it got wrong

Fourteen of the nineteen gaps are one property each on an existing type, and not one required a new mechanism beside `binding`. The four features — labels, decorations, background, actions — absorbed twenty-eight renderers' worth of drawing without gaining a fifth. The one structural addition, `resolveEntries`, exists to make a whole class of bug impossible rather than to add an ability: a column reads the item its line came from, so it cannot pair row 2's cardinality with row 3's path.
