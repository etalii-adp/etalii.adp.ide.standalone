# Requirements Document

## Introduction

The user's instruction, verbatim, because its last sentence is the whole boundary:

> *"Create a specification that standardized the code for the diagrams even more. There should be no imperative code in the modules client anymore. only declarative code that in properties and values describe how the diagram should look and behave. Only thing what is allowed is the handling of events for actions and logic validations."*

And, on scope: *"also be expansive. This now needs to be done right."* — read here as licence for the specification to be as large as the problem is, and as an instruction not to trade correctness for landability. It is **not** licence to widen the boundary above; anything outside it is a question for the user rather than a requirement.

Two decisions the user took directly on 2026-09-07, both against the recommendation offered:

- **Shape renderers go.** Not exempted, not kept behind an escape hatch. With the vocabulary's own shape named: *"the labels in an element should be configureable, and if those can be edited. what their layout and font size/boldness/italic are. Where the anchor points are and when they are enabled and/or not. In terms of anchor points it should not about how they look like but rather if they are visible or not."*
- **Scope is client and wire**, so a module's model may arrive already shaped for the canvas rather than being translated by hand.

This is the second standardisation pass. `diagram-library-adoption` was the first and is archived: zero private `dragRef`/`panRef`/`connectRef` remain, all thirteen modules render through `DiagramCanvas`, and canvases fell from a 466–924 line baseline to 294–621. **That pass moved the mechanics into the library. This one moves the descriptions into data.**

## What was measured

Every line of every module canvas was attributed to exactly one category by walking the TypeScript AST. The categories are disjoint and sum to the file: **6,595 of 6,595 lines, checksum exact.**

**The sixteen canvases**, named so the measurement is reproducible rather than merely stated — nineteen files match `*Canvas.tsx` under `src/diagrams/*/client/`; three are `databricks`' eleven-to-thirteen-line wrappers over one inner canvas and are excluded as trivial:

`AnsibleCanvas`, `PipelineCanvas` (azure-pipeline), `C4Canvas`, `CausalLoopCanvas`, `DatabricksCanvas`, `DependencyGraphCanvas`, `DotNetDependencyGraphCanvas`, `HelmCanvas`, `MindmapCanvas`, `OwlCanvas`, `RdfCanvas`, `ShaclCanvas`, `SkosCanvas`, `SparqlCanvas`, `TimelineCanvas`, `WardleyCanvas` — sixteen across thirteen modules, `rdf` contributing four readings.

| Category | Lines | Share | Under the user's rule |
| --- | --- | --- | --- |
| Diagram definition — properties and values | 571 | 9% | **What should remain** |
| Shape and route renderers | 997 | 15% | Imperative |
| Event handlers | 470 | 7% | **Permitted** — the carve-out |
| Model mapping (module model → `DiagramModel`) | 665 | 10% | Imperative |
| Chrome JSX (loading, failure, title, legend, background) | 731 | 11% | Imperative |
| Types, constants, helpers, hook wiring | 1,545 | 23% | Imperative |
| Imports / comments / blank | 1,616 | 25% | — |

**Nine percent of a module client is declarative. Seven percent is the permitted carve-out. Fifty-nine percent is imperative code the instruction says should not exist.**

### The vocabulary exists, is fully implemented, and has no adopters

`BuiltInShape` declares **thirteen** shapes — `box`, `centered-box`, `ellipse`, `frame`, `span`, `styled-box`, `symbol`, `rounded-rectangle`, `diamond`, `hexagon`, `pill`, `parallelogram`, `cylinder` — and **all thirteen render**: seven through components under `canvas/elements/`, and six inline in `DiagramCanvas` (`rounded-rectangle` and `pill` as `BoxElement` with a different corner radius, three polygons through a shared helper, `cylinder` as a composed group). *A declared shape without its own component folder is not an unkept promise; that reading was checked and withdrawn.*

Beside it: `LabelRule` carries placement, editability, wrap and truncation; `ElementStyle` carries fill, stroke, dash, corner radius and a `LabelTypography` of size, weight and colour — **the user's label guidance describes fields that already exist**; `AnchorSet` carries `edge`, `compass`, `sides` and `points`.

And yet:

- **Zero of the sixteen canvases declare a built-in shape.** All twenty-eight element types across thirteen modules declare a `CustomShapeRef` instead. The editor modules declare none at all, so this is a diagram-module pattern exactly.
- **All twenty-nine anchor declarations in the tree are `{ kind: "edge" }`.** Compass, sides and points have never been used once.
- **`BUILT_IN_SHAPES`, the exported enumeration, has exactly one reference in the tree: its own definition** — under a comment describing a guard and a toolbox that derive from it. Both consumers are imagined. This is a stale record in the file this specification builds on.

### Why the vocabulary went unused — three gaps, measured not guessed

All twenty-eight renderers were parsed. Not one draws a freehand diagram; every one either wraps a shared component or draws a small fixed decoration:

| What the renderer does | Count |
| --- | --- |
| Wraps exactly one shared component, no raw SVG | **12** |
| Wraps a component **and adds raw `<text>`** | ~10 |
| Uses **no** shared component — a stub, a badge, an annotation, a target dot | **5** |

The cause is one sentence: **every built-in shape carries exactly one `label`.** Only `styled-box` carries more, and its three are fixed as name, type-line and description. So a module drawing a card with a second line of text has no declarative way to say so, and must escape. That is the user's guidance about configurable labels arriving as **the diagnosis of the failure** rather than as a request.

Three gaps follow, and they are different from each other:

1. **Multiple labels.** One label per shape, no way to declare a second line, its position, or its typography. Ten renderers hand-roll `<text>` for exactly this.
2. **Decorations.** A stub reaching to nothing, a loop badge, an annotation, an evolution target — five renderers draw a small fixed ornament with no component and no vocabulary.
3. **A notation background.** `wardley-map` draws axes, evolution stage bands, attitude regions and annotations under everything — a coordinate-space backdrop, drawn in the diagram's own units beneath the elements, and nothing declares it.

   *Corrected on 2026-09-08, during task 4.* This gap originally read *"`timeline` draws a ruler"* as a second instance of the same thing. **It is not.** `TimelineRuler.tsx` is 38 lines of HTML — a `<div>` of `<span>`s, `aria-hidden`, absolutely positioned over the scrolling surface so it never scrolls out of sight — whose tick positions are **pixels computed from the viewport** and whose tick *set* changes with zoom. Its own doc-comment says *"chrome fixed to the view, not to the diagram"*. **Wardley's is coordinate-space; timeline's is view-fixed chrome, and they share only the word "drawn around the diagram".** The ruler belongs with Requirement 6, and the sentence that grouped them was true about what each draws and false about what either is.

**The proof case is not what it appears.** `WardleyCanvas` is the largest at 621 lines and the only canvas with substantial raw SVG — but of its twenty-four raw-SVG lines, **three are an element** (a circle and a label) and **twenty-one are its background**. Wardley's shapes are trivial; its *background* is the gap. A specification that reads "wardley is the hard shape" would solve the wrong problem.

### Behaviour is not declared either — it is wired by hand, and it has drifted

The user's revision feedback, anchored on the shapes requirement: *"Not only how they look, but also if and how they are enabled, how actions work (rename, delete, connect etc.)"*. Measured against the tree, that is the same finding as labels, one layer over.

**Enablement is half-declared.** `dragging: DraggingPolicy` states it canvas-wide; `draggable` and `deletable` override per element type; `label.editable` and the route label's `editable` state it per label; `connectOnRightDrag` states one gesture; relation definitions constrain endpoints and `allowSelf`; `layout.modes` states which arrangements a type allows. **So the contract knows how to say "enabled", and says it for six things and not for the rest.**

**Actions are not declared at all.** Which actions a diagram type offers is inferred from which handlers its author happened to write, and the wiring is copied per module:

- **Eleven canvases hand-write a structural shortcut list**, and there are **four different lists for the same intent**: `["F2"]` in seven, `["F2", "Insert"]` in c4, `["F2", "Insert", "Tab", "Enter"]` in dependency-graph and timeline, and `["Insert", "Enter", "F2", " ", "Tab"]` in mindmap. Same rule, four spellings, hand-copied apart — precisely what `usePointerGesture`'s own comment records about the movement threshold before it was centralised.
- **Nine canvases synthesise a keystroke to express an action**, building `{ key: "Delete", ctrl: false, shift: false, alt: false, meta: false }` by hand and sending it to the backend. A module manufacturing a fake key event to say "delete this" is the sharpest evidence available that the action itself has nowhere to be declared.
- **Six canvases wire no structural action at all**, and nothing distinguishes "this type has no rename" from "nobody wired one".

**The line the feedback draws is sharper than the one the first draft drew.** The user's boundary permits *"handling of events for actions"*. So **an action's wiring — that it exists, whether it is enabled, what it applies to, what invokes it — is description and belongs in the declaration; the handler that runs when it fires stays imperative.** "Event handlers are allowed" was not testable. "An action is declared, its handler is code" is.

### Why review will not hold this

`dotnet-dependency-graph`'s own requirements carried *"consistency with the authored `generic/dependencies` canvas"* as **a review criterion rather than an assertable test. It was never reviewed and it is not satisfied.** This tree already has the mechanism that does not drift: `noPrivateGestures`, `noPrivateScrollbars`, `noPrivateLabelEditors` and `sharedStylesheetImports` each walk every module, collect every offender, fail once naming all of them, and carry a population canary so an empty offender list means compliance rather than a walk that matched nothing.

## Alignment with Product Vision

`structure.md` requires canvas infrastructure not to depend on any diagram type's schema, and a new type to be addable without touching core code. **A module that ships a renderer holds a private rendering path; a module that ships a declaration does not.** This applies that rule to the last thing modules still hold — the drawing itself.

`product.md`'s **"Don't reinvent, integrate"**, read inward: twenty-eight renderers re-implementing prop-passing over a vocabulary that already exists is reinvention inside our own walls, and the table above is its size.

## Requirements

### Requirement 1 — The permitted surface is defined so a build can check it

1. WHEN the permitted surface is stated THEN it SHALL name exactly what a module client may contain — a **declaration** of properties and values, **event handlers** for actions, **validation logic**, and the registration binding them — and SHALL name nothing else.
2. WHEN "event handling for actions" is defined THEN it SHALL mean receiving a library event and dispatching a backend action, command or selection — **not** computing appearance, position or geometry, which is description.
3. WHEN "validation logic" is defined THEN it SHALL mean answering whether an operation is permitted, and SHALL NOT be read as licence for arbitrary computation under a validating name.
4. WHEN the rule is applied THEN a module SHALL be judged by what its client contains, not by how its author described it.
5. WHERE a module cannot comply without a change to shared code THEN that SHALL be a stop-and-report producing a **central** change, never a local exception.

### Requirement 2 — Appearance AND behaviour are declared, never written

**User Story:** As a module author, I want to state in properties what my elements look like, whether they are enabled, and how their actions work — rename, delete, connect — so that no module ships a renderer or hand-wires an action.

1. WHEN a module declares an element type THEN it SHALL name a built-in shape and configure it with properties, and **no module client SHALL contain a shape or route render function.**
2. WHEN a label is declared THEN its content, position, editability and typography SHALL all be properties.
3. WHEN an element needs more than one label THEN the declaration SHALL express **as many as it draws**, each with its own position and typography — the gap that sent ten renderers to raw `<text>`.
4. WHEN anchors are declared THEN the declaration SHALL say where they are and **whether they are visible or enabled**, and SHALL NOT describe how they look.
5. WHEN appearance varies with data THEN it SHALL be a **binding from a model field to a property**, never a function computing the property.
6. WHEN an element type's actions are declared THEN the declaration SHALL state **which actions it offers** — rename, delete, connect and the module's own — **whether each is enabled**, what it applies to, and what invokes it; and no module client SHALL hand-write a shortcut key list or synthesise a keystroke to express an action.
7. WHEN an action is declared THEN **only its handler SHALL remain imperative** — the code that runs when it fires — because that is precisely the carve-out the user permitted, and the declaration is what makes the carve-out checkable.
8. WHEN enablement is declared THEN it SHALL be stated uniformly, so that a type offering no rename is distinguishable from a type whose rename nobody wired — a distinction the tree cannot currently make.
9. WHEN this requirement is complete THEN **no function-valued escape hatch SHALL remain reachable from a module client** — a contract member whose value is a function the library calls, by any name. `CustomShapeRef`, `CustomRouteRef` and **`DiagramBackgroundRef`** are the three that exist; each is removed from the contract or made unreachable, and the choice recorded with its reason.

   *Amended on 2026-09-08, during task 4.* This criterion originally named two members by hand and a third existed — `DiagramBackgroundRef`, `{ background: string; render: (view) => unknown }`, used by `wardley-map` alone. **The defect was not the missing name; it was that the criterion was a list where it needed to be a property.** A list of two admits a third silently, which is the failure this specification is about, appearing inside its own requirements.

### Requirement 3 — The vocabulary gains exactly what the escape hatch was covering

1. WHEN the vocabulary is extended THEN it SHALL close the four measured gaps — **multiple labels, decorations, a notation background, and declared actions with their enablement** — and the design SHALL treat each as its own addition rather than one general mechanism.
2. WHEN a decoration is declared THEN a stub, a badge, an annotation and a target dot SHALL all be expressible, since those are the five that use no component today.
3. WHEN a notation background is declared THEN `wardley-map`'s axes, stage bands, regions and annotations SHALL be expressible in the diagram's own coordinate space. **`timeline`'s ruler is not a second instance and SHALL NOT be made one** — it is view-fixed chrome and belongs to Requirement 6. A background stretched until it could express a view-pinned tick ladder whose contents change with zoom would be able to express anything, which is the property this specification refuses everywhere else.
4. WHEN actions are declared THEN the four drifted shortcut lists SHALL collapse to one stated rule, and the nine synthesised `Delete` keystrokes SHALL disappear — a module SHALL never build a key event to name an action.
5. WHEN an addition is proposed THEN it SHALL be justified by a module that needs it, named, and SHALL NOT be added speculatively.
6. **THEN the shared library change SHALL be recognised as its own body of work**, sequenced before module migration, and SHALL NOT be discovered inside a module task.

### Requirement 4 — Sufficiency is proven, not asserted

1. WHEN the vocabulary is designed THEN its sufficiency SHALL be established against **all sixteen canvases**, not a representative sample.
2. WHEN a shape is examined THEN the record SHALL state which built-in replaces it and which properties carry what its renderer computed.
3. WHERE a canvas cannot be expressed THEN that SHALL be reported as a gap **before** migration, never resolved by leaving a module imperative.
4. WHEN sufficiency is claimed THEN it SHALL come with sixteen worked answers attached, not from the vocabulary existing.

### Requirement 5 — The model arrives shaped for the canvas

1. WHEN a module's model reaches its client THEN it SHALL arrive in the shape the canvas consumes, so the 665 lines of per-module mapping become declared bindings or nothing.
2. WHEN the mapping is removed THEN the change MAY reach the wire and the module backends — scope decided by the user on 2026-09-07 — and the design SHALL state what crosses and why.
3. WHEN the wire changes THEN each module's backend SHALL keep owning what its elements mean; only the handover shape changes.
4. WHEN this is designed THEN the cost of a protocol change SHALL be weighed against a client-side binding and the choice recorded — the backend completed an eleven-task decomposition the day before this was written.

### Requirement 6 — Chrome and background are declared

1. WHEN a canvas shows loading, unavailable, a title, a legend **or a view-fixed ruler** THEN those SHALL be declared, with content bound to model fields, rather than hand-written per module. **`timeline`'s ruler is a member of this set**, moved here from Requirement 3.3 on 2026-09-08 when its mechanism was read rather than its purpose: view-pinned, tick set derived from the viewport, HTML rather than SVG.
2. WHEN a module wants none THEN the absence SHALL be as explicit as the presence.
3. WHEN two modules show the same state THEN a reader SHALL see the same thing.

### Requirement 7 — Compliance is asserted, never reviewed

1. WHEN the rule is guarded THEN one shared check SHALL walk every module client, collect every offender, and fail once naming all of them with file and rule.
2. WHEN the check is written THEN it SHALL carry a population canary in **both** shapes — a floor on modules walked, and a named member asserted present.
3. WHEN the check discovers modules THEN it SHALL discover **by artifact rather than by folder**, keyed per the file owning the property, so composition cannot misreport.
4. WHEN the check is accepted THEN it SHALL first have been **seen to fail** against a deliberately non-compliant module.
5. WHERE the check reads text and can be evaded by a renderer under an unrecognisable name THEN that limit SHALL be stated in its own doc-comment.
6. **THEN `BUILT_IN_SHAPES`' stale comment SHALL be made true or corrected** — it describes a guard and a toolbox that do not exist, and this specification creates the guard.

### Requirement 8 — Nothing a user sees changes

1. WHEN a module is migrated THEN its existing client tests SHALL pass unchanged; a test that must change is **evidence of a behaviour change**, not a test to update.
2. WHEN a migration alters what is drawn THEN it is a defect in the vocabulary or the declaration, not an acceptable cost.
3. WHEN typography moves from a stylesheet into declared properties THEN that is a visible-outcome change and SHALL be verified rather than assumed.
4. WHEN the work completes THEN a manual check SHALL confirm in a real browser that a representative diagram of each shape family draws as before.

### Requirement 9 — Thirteen modules is the migration, and its shape is stated

1. WHEN the migration is planned THEN **one module SHALL be migrated first as the reference**, and its diff SHALL be the pattern.
2. WHEN the reference is chosen THEN it SHALL be justified, and the gaps it exposes closed centrally before the second begins.
3. WHEN modules are migrated THEN each SHALL be its own landing with tests passing unchanged, never swept together.
4. WHEN a module is migrated THEN its readme SHALL record what it declares.
5. WHERE a module is under active change THEN ordering SHALL account for it.
6. WHEN every module is migrated THEN the measured shares SHALL be **re-measured and recorded**, so the outcome is a number rather than a claim.

## Non-Functional Requirements

### Code Architecture and Modularity

- The vocabulary is shared code; what a module's elements *mean* stays in the module. A property whose name contains a diagram type is a design error.
- A module adopts by **deleting**: the declaration replaces the renderer, the mapping and the chrome rather than sitting beside them.

### Reliability

- A migrated module draws what it drew. Requirement 8.1's pass-unchanged bar is the instrument and it is nearly free.

### Usability

- A module author should express a new diagram type without reading another module's client — the test `structure.md` already sets and that twenty-eight renderers currently fail.

## Sources

- Line attribution: every module canvas parsed with the TypeScript compiler at `0a8c6137`; categories disjoint, checksum exact at 6,595 lines. A first brace-counting heuristic returned three canvases with zero definition lines and was discarded rather than quoted.
- Renderer analysis: all 28 `CustomShapeRef` declarations parsed for the components and raw SVG each uses.
- `diagramDefinition.ts` — `BuiltInShape` (13), `LabelRule`, `ElementStyle`, `LabelTypography`, `AnchorSet`, `BUILT_IN_SHAPES`.
- `DiagramCanvas.tsx` — the shape switch, thirteen cases, verified individually.
- Behaviour analysis: every `structuralShortcutFor` key list and every synthesised `key: "Delete"` object across the sixteen canvases, counted at `0a8c6137`.
- The user's revision feedback on card `approval_1788853854128_nsokaved5`, 2026-09-08, anchored on the shapes requirement.
- The archived `diagram-library-adoption` — the first pass and its reference-migration shape.
