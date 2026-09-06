# Design Document

## Overview

**Every remaining hand-built canvas is rewritten as a definition, a model mapping and event handlers on `DiagramCanvas`, and its private orchestration is deleted.** The library landed as `bef12fc4` with two reference migrations proving the pattern: `RdfCanvas` (820 lines to ~330) and `TimelineCanvas` (1,006 to ~430). This design applies that proven pattern to the remainder, ordered so that the schema mechanisms **no reference exercised** are tested first, and specifies the guards that make the deletion permanent.

This is a migration design, not a library design: every mechanism named here already exists in `src/client/src/canvas/library/`. Where a module needs something the library does not offer, that is a **stop-and-report against `diagram-library`** with a gap record in the tasks document — the precedent is task 6's frozen-time-scale record — never a private workaround, per Requirement 1.3's "a module is on the library or it is not."

## Steering Document Alignment

### Technical Standards (tech.md)

No `.proto`, backend or wire change. The backend keeps owning meaning; a migrated canvas raises library events and answers them with the same backend calls the hand-built canvas made. `client/register.ts` stays each module's seam with the shell (Requirement 5.1).

### Project Structure (structure.md)

All changes live inside `src/diagrams/<module>/client/` plus the guard files under `src/client/src/canvas/`. Nothing new is added to the library itself by this specification.

## The population, measured 2026-09-06

Per Requirement 1.1 the set is measured, not inherited. Today, against `src/diagrams/*/client/register.ts` and the canvases importing `@client/canvas/library`:

| Module | Registrations | Canvas files to migrate | Lines today |
| --- | --- | --- | --- |
| wardley-map | 1 | WardleyCanvas.tsx | 784 |
| mindmap | 1 | MindmapCanvas.tsx | 694 |
| c4 | 2 | C4Canvas.tsx | 723 |
| causal-loop | 1 | CausalLoopCanvas.tsx | 510 |
| rdf (3 of 4 remain) | 4 | SkosCanvas, OwlCanvas, ShaclCanvas | 765 + 802 + 602 |
| sparql | 1 | SparqlCanvas.tsx | 626 |
| dependency-graph | 1 | DependencyGraphCanvas.tsx | 924 |
| azure-pipeline | 1 | PipelineCanvas.tsx | 583 |
| ansible-structure | 1 | AnsibleCanvas.tsx | 523 |
| helm-charts | 1 | HelmCanvas.tsx | 466 |
| databricks | 3 | DatabricksCanvas.tsx + 3 thin wrappers | 866 + 34 |

Eleven modules, **fourteen canvas files, ~8,900 lines** of hand-built orchestration. `RdfCanvas` and `TimelineCanvas` are already on the library and are out of this population. **The first task re-runs this measurement** and records it; the numbers above are the design-time baseline the final re-measurement is compared against, not a list to trust at implementation time.

The private-machinery baseline for Requirement 3.1, measured the same day: `dragRef`/`panRef`/`connectRef` naming survives in seven files (ansible 9, causal-loop 4, databricks 22, dependency-graph 22, helm 9, sparql 11, wardley 9); azure-pipeline, c4 and mindmap already gesture through `usePointerGesture` but keep private orchestration around it. After adoption every one of these counts is zero.

## The migration pattern, from the references

A migration produces exactly four kinds of code, and deletes everything else:

1. **A definition** — `DiagramDefinition` validated at module load by `assertValidDiagramDefinition`: element types (shape, style, label rule, anchors, sizing), relation types (route, style, endpoint constraints, `allowSelf`), toolbox, layout modes, dragging policy.
2. **A model mapping** — the module's folded stream model expressed as the library's `DiagramModel`: elements with positions in the module's own units, connections in the definition's terms. Coordinate conversions (corner-anchored to centre, scale) live here and only here.
3. **Event handlers** — one per raised `DiagramEvent`, each answering with the backend call the old canvas made: `element-moved` becomes `moveElementTo`, `connection-drawn` becomes the module's connect action with a `rel:` source, `element-dropped` passes the backend action id with a `new:` placement, deletion events run the backend Delete shortcut, `view-changed` feeds `useViewReport`, `label-commit-requested` goes through the property channel. The module answers, transforms or ignores; it never mutates.
4. **Custom shapes and routes where the notation needs them** — through `CustomShapeRef`/`CustomRouteRef`, which are first-class citizens of hit-testing, anchoring and connecting. The rdf resource card and the timeline span are the precedents.

Deleted per module: private drag/pan/connect/zoom state, private anchor rendering, private connector path assembly, private toolbox-drop handling, private hit surfaces the library now provides, and the wrapper plumbing around selection, context menu and keyboard that the library's integration props replace.

**What stays module-side, legitimately:** notation-specific rendering behind the custom-shape/route contract, module-unit coordinate conversions in the mapping, and module-owned mechanisms the library's gap records sanction (timeline's frozen scale is the precedent). Requirement 3.3's guard exclusions name these with reasons.

## Per-module analysis — what each migration proves and risks

Ordered as they will be worked (the rationale is the next section).

| Module | Library mechanisms first exercised here | Known hard points |
| --- | --- | --- |
| wardley-map | **Intrinsic 0..1 extent + in-canvas background axis** (schema landed with task 6, zero reference coverage), symbol shape | Evolution axis and stage labels as the declared background; `label [-x, y]` authored offsets in the label rule; selection landed only days ago — parity list is fresh |
| mindmap | **Tree layout mode** (built in library task 4, unexercised), centered-box, `branchAnchorsBetween` | Side-balancing of the tree; fold/unfold as model concerns, not layout ones |
| c4 | **Frames and nesting** — container elements, children within bounds, inner-first hit-testing; styled-box | Two registrations over one canvas; boundary-with-contents rendering; per-style theming |
| causal-loop | **Custom route** (self-loop `causalLoopArc` ellipse pair), arc route, along-route labels (polarity, delay adorners) | `allowSelf: true`; the delay/polarity adorners just gained selected-state styling — must survive; backend self-organizing Arrange stays a backend action |
| rdf family (skos, owl, shacl) | Nothing new — three near-copies of the landed RdfCanvas pattern | Family consistency: one card mechanism, three vocabularies; skos budget banner is model-side |
| sparql | Region grouping via frames (proven by c4 by this point) | Grouping style differs from c4's boundaries, mechanism does not |
| dependency-graph | Adjustable route (`connection-adjusted`), span elements | The span-viewport line rule (landed 2026-09-06) is backend-side and untouched — parity check names it explicitly |
| azure-pipeline | Composite cards via inset label rules, fixed-bezier route | Stage/job two-level cards; F2 and inline rename landed recently — fresh parity list |
| ansible-structure | Nothing new — plain graph | Near-twin of helm; unresolved-target edges drawn to nothing stay a mapping decision |
| helm-charts | Nothing new — plain graph | As ansible |
| databricks | **Three readings, one inner canvas, migrated as one unit** (Requirement 1.2) | Largest single canvas; mixed straight+fixed-bezier, frames for targets; edges not selectable today — parity means still not selectable, recorded, not "fixed" |

## Sequencing — hardest evidence first (Requirement 4)

Requirement 4.2 fixes the first slot: of the axis-shaped pair, **timeline was the reference and wardley-map was not**, so wardley-map goes first — it is also the only module exercising a schema mechanism (intrinsic extent + background) that no reference touched at all. After it, the order continues by untested-mechanism risk: mindmap (tree layout), c4 (nesting), causal-loop (custom route) — then the pattern-copy and plain-graph work where the library holds no surprises — and **databricks last**, because it is the largest, touches four files as one landing, and by then every mechanism it composes is proven. Every migration lands independently through the four gates; migrated and unmigrated modules coexist throughout (the library's coexistence support is explicitly not removed here).

## Behaviour parity — the method (Requirement 2)

Per module, in this order:

1. **Before touching code, write the capability inventory** into the task's implementation log: elements drawn, relations drawable, context actions reachable, labels editable inline, keyboard behaviour, view controls, gesture repertoire — read from the canvas and its tests, not recalled.
2. **Migrate**, checking the inventory item by item. A capability the library expresses differently is a **deliberate unification recorded per module** (the timeline precedent: right-button pan became left-button; empty-press deselection moved to release time). A capability the library cannot express is a stop-and-report.
3. **No new capability rides along** — anything the library newly makes possible (a `fitToView` a canvas never had, a newly cheap relation type) is recorded as follow-up and left.
4. **Unit tests keep passing or are honestly replaced**: a test of deleted private machinery is deleted with it; a behaviour test is rewritten against the library seam and **seen to fail against a deliberate behaviour break** before it is trusted — never accepted on a green run.
5. **The module's `tests.md` entries are re-run** against a local build and outcomes recorded with the build observed. These runs need a signed-in session; where the implementing agent cannot sign in, the entries are updated to be runnable and the run is handed to a Tester or the user, stated plainly rather than recorded as passes.
6. **Performance**: on the module's largest document under `src/examples/`, drag latency, pan smoothness and open-to-first-paint are compared before and after in the same eyes-on session as the `tests.md` re-run. A felt regression blocks that module's merge.

## The deletion made permanent — guards (Requirement 3)

Two changes to the standing guard set, both following the house guard shape (walk-and-name-all-offenders with limits stated, or mounted with a named canary; every new guard seen to fail before trusted):

1. **`noPrivateGestures`** joins the `noPrivate*` family beside `noPrivateLabelEditors` and `noPrivateScrollbars`: no migrated module's client sources may hold private gesture state (`dragRef`/`panRef`/`connectRef` naming, raw pointer-capture handling outside the library, private connector path assembly for library-expressible routes). It carries an explicit **`NOT_YET_MIGRATED` exclusion list that only shrinks**: each migration deletes its module's entry in the same commit, and the guard fails if an entry is ever added. Its text-reading limit is stated in the file, as the family does.
2. **The pair-registration guard grows per-module coverage**: the library guard proving view-controls-and-toolbox-as-a-pair today mounts a synthetic definition; it gains a mounted case per migrated module's registered canvas, so the half-registration defect class is closed for real registrations, not just the mechanism. Exclusions (unmigrated modules) shrink to empty by the last task and never gain an entry (Requirement 5.2).

Requirement 3.1's final measurement — the surveyed private-machinery counts re-run across migrated modules, before/after, recorded — is the last task's deliverable alongside the guards.

## Error Handling

1. **A library gap found mid-migration** — stop; record it in this specification's tasks document with its consequence (the task-6 gap record is the template); raise it against `diagram-library`. The module either proceeds on a sanctioned module-side mechanism (as timeline's frozen scale) or stays hand-built and the gap blocks its task, never a half-migration.
2. **A parity break found after landing** — a field report reopens the module's task scope as a bug with a seen-to-fail guard, per the standing bug rule; the migration pattern itself is not rolled back.
3. **The coexistence seam** — throughout adoption the shell runs migrated and unmigrated canvases side by side; nothing in this specification may remove or narrow that support (it has its own moment after the last module).

## Testing Strategy

### Unit
Per module: the definition validates at load; the mapping's coordinate conversions pinned with exact numbers (the corner-to-centre class of defect is the reference precedent); each event handler's backend call asserted through the module's existing test seams. Rewritten behaviour tests seen to fail against a deliberate break first.

### Mounted guards
`noPrivateGestures` (seen to fail against a planted `dragRef`), the per-module pair-registration cases (seen to fail against an emptied toolbox), and the existing library guards staying green throughout.

### Manual
Per module: the `tests.md` re-run plus the performance comparison, eyes-on against a local build, outcomes recorded with the build observed.

## Sequencing summary

1. wardley-map — intrinsic extent and background axis, first exercise
2. mindmap — tree layout, first exercise
3. c4 — frames and nesting, first exercise
4. causal-loop — custom route and along-route labels, first exercise
5. rdf family (skos, owl, shacl) — pattern copies, family consistency
6. sparql — frames as region grouping
7. dependency-graph — adjustable routes
8. azure-pipeline — composite cards
9. ansible-structure and helm-charts — plain graphs, near-twins
10. databricks — four files, one unit, last
11. Final measurement, guard exclusions empty, documentation refresh (`docs/creating-a-diagram-module.md` touch points, `docs/screenshots/` retakes where misleading)
