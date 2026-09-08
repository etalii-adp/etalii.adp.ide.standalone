# Requirements Document

## Introduction

The user's instruction, verbatim, because its last sentence is the whole specification:

> *"Create a specification that standardized the code for the diagrams even more. There should be no imperative code in the modules client anymore. only declarative code that in properties and values describe how the diagram should look and behave. Only thing what is allowed is the handling of events for actions and logic validations."*

So a module's client becomes **properties and values** describing appearance and behaviour, and the **only** permitted imperative code is event handling for actions and validation logic.

This is the second standardisation pass. `diagram-library-adoption` was the first: it is archived, its criteria hold tree-wide — zero private `dragRef`/`panRef`/`connectRef` in any module, all thirteen modules rendering through `DiagramCanvas`, and canvases falling from a 466–924 line baseline to 294–621. **That pass moved the mechanics into the library. This one moves the descriptions into data.**

## What the measurement found

**Every line of every module canvas was attributed to exactly one category by walking the TypeScript AST** — 16 canvases across 13 modules (`rdf` carries four readings; `databricks`' three wrappers delegate to one inner canvas and are excluded as trivial). The categories are disjoint and sum to the file, checked: **6,595 of 6,595 lines, exact.**

| Category | Lines | Share | Under the user's rule |
| --- | --- | --- | --- |
| Diagram definition — properties and values | 571 | 9% | **This is what should remain** |
| Shape and route renderers (`CustomShapeRef.render`) | 997 | 15% | Imperative |
| Event handlers (`DiagramEventHandlers`) | 470 | 7% | **Permitted** — the user's carve-out |
| Model mapping (module model → `DiagramModel`) | 665 | 10% | Imperative |
| Chrome JSX (loading, failure, title, legend, host) | 731 | 11% | Imperative |
| Types, constants, helpers, hook wiring | 1,545 | 23% | Imperative |
| Imports | 383 | 6% | — |
| Comments | 714 | 11% | — |
| Blank | 519 | 8% | — |

**Nine percent of a module client is declarative today. Seven percent is the permitted carve-out. Fifty-nine percent is imperative code the instruction says should not exist.**

### The central finding: the vocabulary exists and has no adopters

`diagramDefinition.ts` already declares everything the instruction describes, and the tree already holds the components it names:

- **`BuiltInShape`** is exactly `"box" | "centered-box" | "ellipse" | "frame" | "span" | "styled-box" | "symbol"` — the seven components under `canvas/elements/`.
- **`LabelRule`** carries `placement`, `editable`, `wrap`, `truncate` and the inset offsets.
- **`ElementStyle`** carries `fill`, `stroke`, `strokeWidth`, `dash`, `cornerRadius` and **`LabelTypography`** — `fontSize`, `fontWeight`, `color`.
- **`AnchorSet`** carries `edge`, `compass`, `sides` and `points`.
- `sizing`, `draggable`, `deletable`, `beneathConnections` are all declared properties.

**Zero of the sixteen canvases declare a built-in shape. All sixteen declare a `CustomShapeRef` instead** — and **not one of those renderers draws arbitrary graphics: every one wraps one of the same seven components.** The 997 lines are overwhelmingly prop-shuffling: reading model fields and handing them to a component the definition could have named.

**All twenty-nine anchor declarations in the tree are `{ kind: "edge" }`.** The compass, sides and points vocabularies have never been used once.

So this specification is not building a vocabulary. **It is closing an escape hatch that was taken universally, and extending the vocabulary exactly where the escape hatch was covering for a gap.** That is a far smaller problem than 997 lines suggests, and it has a precedent tonight: the shared appearance layer was built, six modules adopted it, five bypassed it, and nothing failed — a shared capability with an unguarded alternative is a shared capability with drift.

### Why review will not hold this

`dotnet-dependency-graph`'s own requirements carried *"consistency with the authored `generic/dependencies` canvas"* as **a review criterion rather than an assertable test. It was never reviewed and it is not satisfied.** A standardisation rule that cannot fail a build will drift the same way, and this tree already has the mechanism that does not: `noPrivateGestures.test.ts`, `noPrivateScrollbars.test.ts`, `noPrivateLabelEditors.test.ts` and `sharedStylesheetImports.test.ts` each walk every module, collect every offender, fail once naming all of them, and carry a population canary so an empty offender list means compliance rather than a walk that matched nothing.

## Alignment with Product Vision

`structure.md` requires that canvas rendering infrastructure not depend on any single diagram type's schema, and that a new diagram type be addable without touching core code. **A module that ships a renderer has a private rendering path; a module that ships a declaration does not.** This is that rule applied to the last thing the modules still hold: the drawing itself.

`product.md`'s **"Don't reinvent, integrate"**, read inward: sixteen canvases re-implementing prop-passing over seven shared components is reinvention inside our own walls, and the measurement above is its size.

## Requirements

### Requirement 1 — The permitted surface is defined so a build can check it

**User Story:** As the person who set this rule, I want "no imperative code except event handling and validation" to be a statement a test can evaluate, so that compliance is a fact rather than an opinion.

#### Acceptance Criteria

1. WHEN the permitted surface is stated THEN it SHALL name exactly what a module client may contain: a **diagram definition** of properties and values, **event handlers** for actions, **validation logic**, and the registration that binds them — and SHALL name nothing else.
2. WHEN "event handling for actions" is defined THEN it SHALL mean a handler that receives a library event and dispatches a backend action, command or selection — **not** a handler that computes appearance, position or geometry, which is description and belongs in the declaration.
3. WHEN "validation logic" is defined THEN it SHALL mean a module answering whether an operation is permitted — a refusal, a guard on a gesture — and SHALL NOT be read as licence for arbitrary computation under a validating name.
4. WHEN the rule is applied THEN it SHALL be expressible as a check over every module client, and a module SHALL be judged by what its client contains rather than by how its author described it.
5. WHERE a module cannot comply without a change to shared code THEN that SHALL be a stop-and-report producing a central change, never a local exception.

### Requirement 2 — Shapes are declared, never rendered

**User Story:** As a module author, I want to say what my elements look like in properties, so that no module ships a renderer.

#### Acceptance Criteria

1. WHEN a module declares an element type THEN it SHALL name a **built-in shape** and configure it with properties, and **no module client SHALL contain a shape or route render function.**
2. WHEN a label is declared THEN its **content, layout, editability and typography SHALL all be properties** — which line of the element it is, where it sits, whether it may be edited in place, and its size, weight and slant — so that a module never writes markup to place or style text.
3. WHEN anchors are declared THEN the declaration SHALL say **where they are and whether they are visible or enabled**, and SHALL NOT describe how they look — appearance is the shared layer's, and a module choosing an anchor's appearance is the drift this specification removes.
4. WHEN an element's appearance varies with its data — a status colour, a kind-specific silhouette — THEN that SHALL be expressed as a **binding from a model field to a property**, never as a function computing the property.
5. WHEN a module's current renderer does something no property can express THEN the missing property SHALL be **added to the shared vocabulary**, and the finding SHALL be recorded with the module that needed it.
6. WHEN this requirement is complete THEN `CustomShapeRef` and `CustomRouteRef` SHALL either be removed from the definition contract or be unreachable from any module client, and the choice SHALL be recorded with its reason.

### Requirement 3 — The vocabulary is proven sufficient, not asserted sufficient

**User Story:** As the developer doing this, I want to know before I start that all thirteen modules can be expressed, so that I do not discover on the last one that the vocabulary cannot say what it needs.

#### Acceptance Criteria

1. WHEN the vocabulary is designed THEN its sufficiency SHALL be established against **all sixteen canvases**, not against a representative sample — the appearance-layer finding this week measured two offenders where five existed, because the detector was keyed on the fix rather than the defect.
2. WHEN a shape is examined THEN the record SHALL state which built-in shape replaces it and which properties carry what its renderer computed.
3. WHERE a canvas cannot be expressed THEN that SHALL be reported as a vocabulary gap with the property it needs, **before** migration begins, rather than resolved by leaving that module imperative.
4. WHEN the hard cases are considered THEN `wardley-map` — the largest canvas at 621 lines, the only one drawing substantial raw SVG, and the one whose notation carries a fixed coordinate space and a map background — SHALL be treated as the proof case, since a vocabulary that expresses it expresses the rest.
5. WHEN sufficiency is claimed THEN it SHALL be claimed with the sixteen worked answers attached, not from the seven built-in shapes existing.

### Requirement 4 — The model arrives shaped for the canvas

**User Story:** As a module author, I want the data I receive to be what the canvas draws, so that no module writes a translation loop.

#### Acceptance Criteria

1. WHEN a module's model reaches its client THEN it SHALL arrive in the shape the canvas consumes, so that the 665 lines of per-module mapping become **declared field bindings or nothing at all**.
2. WHEN the mapping is removed THEN the change MAY reach the wire and the module backends — this specification's scope is client **and** wire, decided by the user on 2026-09-07 — and the design SHALL state what crosses and why.
3. WHEN the wire changes THEN each module's backend SHALL keep owning what its elements mean; only the shape in which they are handed over changes.
4. WHEN a module is migrated THEN what the user sees SHALL be identical, and the module's existing tests SHALL pass unchanged.
5. WHEN this requirement is designed THEN the cost of a protocol change SHALL be weighed against a purely client-side binding, and the choice recorded — the backend has just completed an eleven-task decomposition, and its contract layer settled hours before this specification was written.

### Requirement 5 — The surrounding chrome is declared too

**User Story:** As a reader, I want loading, failure, titles and legends to look the same in every diagram, because they are the same thing in every diagram.

#### Acceptance Criteria

1. WHEN a canvas shows a loading state, an unavailable state, a title or a legend THEN those SHALL be **declared** — present or absent, with their content bound to model fields — rather than hand-written per module, which is where 731 lines currently sit.
2. WHEN chrome is declared THEN a module that wants none SHALL declare none, and the absence SHALL be as explicit as the presence.
3. WHEN two modules show the same state THEN a reader SHALL see the same thing, since the only reason they differ today is that each wrote its own.

### Requirement 6 — Compliance is asserted, never reviewed

**User Story:** As a maintainer, I want the rule to fail a build, because a standardisation rule this repository could only review has already drifted once.

#### Acceptance Criteria

1. WHEN the rule is guarded THEN one shared check SHALL walk **every module client**, collect every offender, and fail once naming all of them with the file and the rule — the shape `noPrivateGestures`, `noPrivateScrollbars`, `noPrivateLabelEditors` and `sharedStylesheetImports` already use.
2. WHEN the check is written THEN it SHALL carry a **population canary in both shapes** the record names: a floor on how many modules it walked, and a named member asserted present — so an empty offender list means compliance rather than a walk that matched nothing.
3. WHEN the check discovers modules THEN it SHALL discover them **by artifact rather than by folder**, and be keyed per the file that owns the property, so that composition — `databricks`' three wrappers over one inner canvas — cannot misreport.
4. WHEN the check is accepted THEN it SHALL first have been **seen to fail** against a deliberately non-compliant module, because a guard that has only ever passed is asserting its own regex.
5. WHEN the check reports THEN its message SHALL name the module, the rule and the file in one sentence a module author can act on without opening this specification.
6. WHERE the check reads text and can therefore be evaded by a renderer under an unrecognisable name THEN that limit SHALL be stated in the check's own doc-comment, as the sibling guards state theirs.

### Requirement 7 — Nothing a user sees changes

**User Story:** As someone using these diagrams, I want a standardisation to cost me nothing.

#### Acceptance Criteria

1. WHEN a module is migrated THEN its existing client tests SHALL pass unchanged, and a test that must change SHALL be **evidence of a behaviour change** rather than a test to update.
2. WHEN a migration alters what is drawn THEN it SHALL be treated as a defect in the vocabulary or the declaration, not as an acceptable cost of standardising.
3. WHEN a module declared appearance through CSS classes THEN its stylesheet SHALL keep working, or the change SHALL be stated: typography moving from a stylesheet into declared properties is a visible-outcome change and SHALL be verified rather than assumed.
4. WHEN the work is complete THEN a manual check SHALL confirm in a real browser that a representative diagram of each shape family draws as it did before.

### Requirement 8 — Thirteen modules is the migration, and its shape is stated

**User Story:** As the developer who will do this, I want to know whether I am making one change or sixteen, before I start.

#### Acceptance Criteria

1. WHEN the migration is planned THEN **one module SHALL be migrated first as the reference**, and its diff SHALL be the pattern the rest follow — the shape `diagram-library-adoption` used.
2. WHEN the reference is chosen THEN it SHALL be justified, and the vocabulary gaps it exposes SHALL be closed centrally before the second module begins.
3. WHEN modules are migrated THEN each SHALL be its own landing with its own tests passing unchanged, and they SHALL NOT be swept together.
4. WHEN a module is migrated THEN its readme SHALL record what it declares, so the next author copies a declaration rather than inferring one.
5. WHERE a module is being changed by other work while this proceeds THEN the ordering SHALL account for it — `dotnet-dependency-graph` was under active change when this specification was written.
6. WHEN every module is migrated THEN the measured shares in the table above SHALL be re-measured and recorded, so the outcome is a number rather than a claim.

## Non-Functional Requirements

### Code Architecture and Modularity

- The vocabulary is shared code and belongs to the canvas library; what a module's elements *mean* stays in the module. A property whose name contains a diagram type is a design error.
- A module adopts by deleting, not by adding: the declaration replaces the renderer, the mapping and the chrome rather than sitting beside them.

### Reliability

- A migrated module draws what it drew. Requirement 7.1's pass-unchanged bar is the instrument, and it is nearly free.

### Usability

- A module author meeting the vocabulary should be able to express a new diagram type without reading another module's client — which is the test `structure.md` already sets and that a tree of sixteen renderers currently fails.

## Sources

- The line attribution: every module canvas parsed with the TypeScript compiler at `0a8c6137`, categories disjoint, checksum exact at 6,595 lines.
- `src/client/src/canvas/library/definition/diagramDefinition.ts` — `BuiltInShape`, `LabelRule`, `ElementStyle`, `LabelTypography`, `AnchorSet`, and the `CustomShapeRef` escape hatch.
- `src/client/src/canvas/elements/` — the seven components the built-in shapes name.
- `noPrivateGestures.test.ts`, `noPrivateScrollbars.test.ts`, `noPrivateLabelEditors.test.ts`, `sharedStylesheetImports.test.ts` — the guard shape Requirement 6 follows.
- The archived `diagram-library-adoption` specification — the first pass, its criteria, and its reference-migration shape.
