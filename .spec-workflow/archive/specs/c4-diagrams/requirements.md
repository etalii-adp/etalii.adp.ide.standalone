# Requirements Document

## Introduction

This spec covers the **C4 diagram modules** — the seven diagram types the C4 model defines, which `docs/diagrams.md` already catalogs as `c4/context`, `c4/container`, `c4/component`, `c4/code`, `c4/system-landscape`, `c4/dynamic` and `c4/deployment`. All seven are scaffolded under `src/diagrams/c4-*/`, and each already publishes its `DiagramDefinition` — `new DiagramOrigin("c4", "context")` and so on — so all seven are already discovered and already offered in the Add dialog. What is missing is everything behind those entries.

**Why one spec for seven types.** C4's defining premise is *"model once, view many"*: a System Context diagram and a Container diagram of the same software system are not two drawings, they are two views onto one model, and an element renamed in one is renamed in both. Seven specs would have to repeat one set of abstractions, one notation, one rule set, and would still leave the hard question — how the seven share a model — belonging to none of them. The spec-workflow rule is one spec at a time; this is the decomposition that rule and the subject agree on. Each type still gets its own requirement stating its scope, its permitted elements and its audience, because those are exactly what differ.

**Why these types, now.** [`mindmap-diagram`](../mindmap-diagram/requirements.md) proved a type whose positions are *computed* and never written back. [`wardley-map`](../wardley-map/requirements.md) specifies the mirror image, where coordinates are *authored* and carry meaning. C4 is the first type where **one document backs several diagrams**, and where the notation carries *rules that can be violated* — a Context diagram containing a Container is not a stylistic lapse, it is wrong. If core can serve that without knowing what C4 is, the pluggable model is proven a third time along a genuinely new axis.

**Where the C4 rules in this document come from.** Every rule below is taken from [c4model.com](https://c4model.com) rather than from memory or from a tool's interpretation of it, and the sourcing matters because two of them cut against what people assume. C4 is explicitly **notation independent** — it "doesn't prescribe any particular notation", and the familiar blue-and-grey palette "isn't something that is dictated by the C4 model". And C4 explicitly advises **against** hand-drawing the Code level. Requirements 4 and 11 carry those two through honestly rather than quietly asserting the opposite.

**Dependencies.** This spec redefines none of them:

* [`adp-diagram-ide`](../adp-diagram-ide/requirements.md) — the workspace shell, the pannable/zoomable canvas, read-only mode.
* [`grpc-core-communication-specification`](../grpc-core-communication-specification/requirements.md) — `Element`, `Delta`, `Point2D`, and the `Any` payload extension point.
* [`add-diagram-action`](../add-diagram-action/requirements.md) and [`create-diagram-file`](../create-diagram-file/requirements.md) — the Add action, the `.adp` file and its MIME first line.
* [`mindmap-diagram`](../mindmap-diagram/requirements.md) — `DiagramDefinition.Extension`, `IDiagramDocumentFactory`, `ContextSource.element_id`, `ContextScope.DIAGRAM_ELEMENT`, and `DiagramService`'s `Open` + `UpdateView` legs, all reused unchanged.
* [`context-service`](../context-service/requirements.md) — the selection chain and the pushing of a selection's actions.
* [`diagram-undo-redo`](../diagram-undo-redo/requirements.md) and `tech.md`'s **Commands** rule.

**What this spec changes in core.** One thing, and it is named rather than smuggled: today an `.adp` file's body is the sibling *derived from its own base name* (`DiagramFileRouter`, `DiagramFilePair.SiblingPathFor`). C4 needs several `.adp` files to share one model document, so Requirement 2.4 asks core for an `.adp` that **names** its body instead of deriving it. That is a bounded change to routing, it benefits every future type that has views over a shared model, and it is the only core change this spec asks for. If anything else in core turns out to need changing, that is a finding worth reporting rather than a licence to change it.

**Deliberately out of scope.**

* **Round-tripping every Structurizr DSL construct.** `!docs`, `!adrs`, `!script`, `!plugin` and workspace extension are read and preserved verbatim (Requirement 3.3) but are not modelled or editable here. Stated so their absence is a decision.
* **Rendering C4 through PlantUML or Mermaid.** `plantuml/c4`, `mermaid/c4` and `structurizr/c4` are separate catalog entries with separate origins; this spec is the C4 *notation*, not a bridge to any one renderer.
* **Generating Code-level diagrams from source.** Requirement 11 states why `c4/code` delegates instead.

## Alignment with Product Vision

* [product.md](../../../steering/product.md)'s **"Don't reinvent, integrate"** — the Structurizr DSL is the de-facto C4-as-code format, it is Apache-2.0 licensed, and it is the only text format that expresses all of C4's view types natively. ADP reads and writes what that ecosystem already reads and writes.
* product.md's **"Files are the source of truth"** — a C4 model is plain text in the project folder, and a model ADP did not change is byte-identical after a save.
* product.md's **"Value from day one"** — Requirement 5 makes a System Context diagram, the one C4 "recommends for all software development teams", the thing a user gets first.
* [tech.md](../../../steering/tech.md)'s **Diagram storage** — an established text format, with ADP's own data in a sidecar rather than smuggled into a file another tool owns.
* tech.md's **Specifying a diagram type** — its four mandatory aspects are Requirements 2–3 (file format), 6–9 (visualization), 12 (toolbox) and 13 (context actions and commands).
* tech.md's **Commands** — every edit, including a drag, is a command with an inverse.
* [structure.md](../../../steering/structure.md)'s **dependency direction** — the modules depend on core; core never depends on them.

## Requirements

### Requirement 1 — The seven C4 diagram types are one model with seven views

**User Story:** As an architect, I want the C4 diagrams of one system to share a single model, so that renaming a container does not leave six other diagrams lying about it.

#### Acceptance Criteria

1. WHEN any of the seven C4 types is opened THEN the system SHALL resolve it to a **view** over a shared C4 **model**, never to a private copy of the elements it draws.
2. WHEN an element's name, description or technology is edited in any view THEN every other view of the same model SHALL reflect that edit, because there is one element and not several.
3. WHEN an element is deleted from the model THEN it SHALL disappear from every view that showed it, and any relationship that referenced it SHALL be removed with it.
4. WHEN an element is removed from one **view** but not from the model THEN the other views SHALL be unaffected — removing from a view and deleting from the model SHALL be distinct actions with distinct commands (Requirement 13.5).
5. WHERE two views of one model are open in two tabs at once THEN an edit in one SHALL reach the other through the existing `DiagramService` delta stream, with no new mechanism.

### Requirement 2 — Registration in `.adp`, model in a shared Structurizr DSL document

**User Story:** As an architect, I want my C4 model in a real `.dsl` file, so that Structurizr, its CLI and its Lite container can read it without conversion.

#### Acceptance Criteria

1. WHEN a C4 diagram exists on disk THEN it SHALL be `<name>.adp`, whose first line is the type's MIME line (`c4/context`, `c4/container`, `c4/component`, `c4/code`, `c4/system-landscape`, `c4/dynamic` or `c4/deployment`), plus a Structurizr DSL document (in a dedicated .dsl file) holding the model and its views.
2. WHEN the modules declare themselves THEN each SHALL set `DiagramDefinition.Extension` to `".dsl"`.
3. WHEN a C4 `.adp` file is created alone THEN its body SHALL be the sibling `<name>.dsl`, exactly as every existing type behaves, so the simple case stays simple.
4. WHEN several C4 `.adp` files are to share one model THEN the `.adp` file SHALL be able to **name** its body document and the view within it, rather than having the body derived from its own base name. This is the one core change this spec asks for (see *What this spec changes in core*); the named path SHALL be project-relative and SHALL be refused if it escapes the project root.
5. IF the named model document is missing THEN the diagram SHALL open in the unavailable state `diagram-workspace-tabs` Requirement 5 already defines, naming the path it looked for — never a crash and never a silently empty canvas.
6. WHEN a `.dsl` file is present without an `.adp` sibling THEN it SHALL still be openable through extension routing, defaulting to its first declared view, and the `.adp` file SHALL NOT be created implicitly.
7. Actions on files with the .dsl extension are "Add view", this will be the add file dialog but filtered to the c4 diagram types.&#x20;

### Requirement 3 — Round-tripping the DSL without disturbing what a human wrote

**User Story:** As an architect, I want ADP to leave my hand-written DSL alone, so that using ADP on a model does not produce a diff nobody can review.

#### Acceptance Criteria

1. WHEN a `.dsl` document is opened and saved with no edit THEN the file SHALL be byte-identical, including its line endings, indentation, comment placement and blank lines — the guarantee `mindmap-diagram` Requirement 3 established for `.mm` and `wardley-map` Requirement 3 for `.owm`.
2. WHEN an edit is made THEN only the lines that edit affects SHALL change; the writer SHALL NOT reformat, reorder or re-indent the rest of the document.
3. WHEN the document contains constructs this spec does not model — `!docs`, `!adrs`, `!script`, `!plugin`, `!include`, workspace extension, `configuration`, custom `properties`, themes — THEN they SHALL be preserved verbatim across a round trip, and SHALL NOT be silently dropped.
4. IF the document is not well-formed DSL THEN the diagram SHALL open in the unavailable state with the parser's message and, where the parser gives one, the line number — and the file SHALL NOT be rewritten while it is unparseable, so a broken file is never made worse.
5. WHERE ADP holds data the DSL has no place for — manual element positions (Requirement 8.3), per-view canvas state — it SHALL go in a sidecar file ADP owns, never as a comment or a custom property inside the `.dsl`.
6. WHEN the sidecar is absent or unreadable THEN the diagram SHALL still open, falling back to computed layout; the sidecar SHALL be an optimisation, never a dependency.

### Requirement 4 — The visual representation matches the C4 reference visuals

**User Story:** As an architect, I want an ADP C4 diagram to look like the C4 diagrams people already know, so that nobody has to be taught a private notation.

#### Acceptance Criteria

1. WHEN elements are rendered THEN each SHALL be a box carrying, in this order: its **name**, its **type** in brackets, and its **description** — matching the reference diagrams on c4model.com.
2. WHEN the bracketed type line is rendered THEN it SHALL read `[Person]`, `[Software System]`, `[Container: <technology>]`, `[Component: <technology>]`, `[Deployment Node: <technology>]` or `[Infrastructure Node: <technology>]`, with the technology omitted from the brackets only when it is absent (which Requirement 10.3 flags).
3. WHEN the default theme is applied THEN it SHALL use the Structurizr default theme's values, which are what the c4model.com reference diagrams show: Person `#08427b` on white text with the person shape; Software System `#1168bd`, white text; Container `#438dd5`, white text; Component `#85bbf0`, **black** text; the base element shape a rounded box.
4. WHEN an element is marked external — outside the scope of the model being described — THEN it SHALL render in the muted grey (`#999999`, white text) the reference diagrams use for exactly that purpose, so scope is readable at a glance.
5. WHEN a container is a data store THEN it SHALL render as a cylinder, and WHERE a container is a browser, mobile app or message bus the corresponding shape SHALL be available, per the shape vocabulary Structurizr defines.
6. WHEN a relationship is rendered THEN it SHALL be a **unidirectional** arrow with a **dashed** line by default, labelled with its description and, where present, its technology in brackets.
7. WHEN a boundary is rendered — a system boundary on a Container diagram, a container boundary on a Component diagram — THEN it SHALL be a dashed rectangle enclosing its contents and labelled with the boundary's name and type.
8. **BECAUSE C4 is explicitly notation independent** and states that the colours "isn't something that is dictated by the C4 model", the palette in 4.3–4.4 SHALL be ADP's **default theme and not a rule**: it SHALL be overridable per model, and no C4 *rule* in Requirement 10 SHALL be expressed in terms of colour.
9. WHEN a theme overrides the defaults THEN the legend (Requirement 9.2) SHALL reflect the override, so the diagram stays self-describing.

### Requirement 5 — System Context diagram (`c4/context`)

**User Story:** As an architect, I want a System Context diagram, so that I can show one system's place in its world to a non-technical audience.

#### Acceptance Criteria

1. WHEN a `c4/context` view is opened THEN its scope SHALL be **a single software system**, and that system SHALL be the diagram's single primary element.
2. WHEN elements are offered or drawn THEN the permitted kinds SHALL be **people** and **software systems** only; containers, components and code elements SHALL NOT appear.
3. WHEN the system in scope is drawn THEN it SHALL be visually distinguished from the surrounding systems, which SHALL be rendered as external (Requirement 4.4).
4. WHEN the diagram is titled THEN the default SHALL be `System Context diagram for <software system name>`, per C4's guidance that a title describes the diagram type and its scope.
5. WHERE the user attempts to place a container or component on this view THEN the system SHALL refuse it and say why, per Requirement 10.1.

### Requirement 6 — Container diagram (`c4/container`)

**User Story:** As a developer, I want a Container diagram, so that I can see the shape of a system and the technology choices in it.

#### Acceptance Criteria

1. WHEN a `c4/container` view is opened THEN its scope SHALL be **a single software system**, and its primary elements SHALL be the **containers** within that system.
2. WHEN supporting elements are drawn THEN they SHALL be the **people and software systems directly connected** to those containers, rendered as external.
3. WHEN a container is defined THEN it SHALL mean an **application or a data store** — a server-side web application, a single-page application, a desktop or mobile application, a database schema, a file-system folder, a serverless function, an S3 bucket. It SHALL NOT be assumed to mean a Docker container, and the UI SHALL NOT use Docker vocabulary for it.
4. WHEN a container is created THEN a **technology** SHALL be requested, because C4 requires every container to have one explicitly specified (Requirement 10.3).
5. WHEN the system in scope is drawn THEN its containers SHALL be enclosed in a labelled system boundary (Requirement 4.7).
6. WHEN relationships between containers are drawn THEN their **technology/protocol** SHALL be shown on the label, since that is what this diagram exists to communicate.
7. WHERE deployment concerns — clustering, load balancers, replication, failover — are described THEN they SHALL NOT be modelled on this view; the system SHALL direct the user to a Deployment diagram (Requirement 9) instead.

### Requirement 7 — Component diagram (`c4/component`) and Dynamic diagram (`c4/dynamic`)

**User Story:** As a developer, I want Component and Dynamic diagrams, so that I can show what is inside a container and how a scenario plays out across it.

#### Acceptance Criteria

1. WHEN a `c4/component` view is opened THEN its scope SHALL be **a single container**, its primary elements the **components** within it, and its supporting elements the containers of the same system plus the people and external systems directly connected to those components.
2. WHEN a component is created THEN a **technology** SHALL be requested, on the same grounds as a container (Requirement 10.3).
3. WHEN the container in scope is drawn THEN its components SHALL be enclosed in a labelled container boundary.
4. WHERE the user creates a component diagram THEN the UI SHALL NOT imply it is required: C4 states component diagrams are optional and should be created "only if you feel they add value", and recommends automating them for long-lived documentation.
5. WHEN a `c4/dynamic` view is opened THEN its scope SHALL be **a feature, story or use case**, and it SHALL be based on a **UML communication diagram** — free-form placement, with **numbered interactions** giving the order rather than vertical position.
6. WHEN interactions are numbered THEN the numbering SHALL be maintained by the system as interactions are inserted, reordered or removed, and SHALL support the nested form (`1`, `1.1`, `1.2`) the notation allows.
7. WHEN a dynamic view is populated THEN it SHALL draw software systems, containers **or** components — whichever abstraction level the view is set to — and SHALL NOT mix levels within one view.
8. WHEN the same dynamic view is presented THEN a collaboration-style and a sequence-style rendering SHALL be interchangeable presentations of the identical content, since C4 states both convey the same information.

### Requirement 8 — Layout: computed by default, authored where the user says so

**User Story:** As an architect, I want a sensible layout without arranging every box, but I want my own arrangement kept when I make one.

#### Acceptance Criteria

1. WHEN a view has no stored positions THEN the module SHALL **compute** the layout, in the backend, exactly as `mindmap-diagram` Requirement 5.3 established — the client never computes positions.
2. WHEN the DSL declares `autoLayout` for a view THEN that declaration SHALL be honoured, including its rank direction and separations, rather than overridden by ADP's own arrangement.
3. WHEN the user drags an element THEN the new position SHALL become an **authored** position for that view, stored in ADP's sidecar (Requirement 3.5), and SHALL survive reopening. Dragging SHALL be a command with an inverse (Requirement 13.1).
4. WHERE a view has authored positions and `autoLayout` is later declared in the DSL THEN the DSL SHALL win, and the user SHALL be told their manual arrangement is being overridden rather than finding it silently gone.
5. WHEN positions are computed THEN elements SHALL NOT overlap one another, and a relationship SHALL NOT be routed through an element box — the guarantees the mindmap layout already has to meet.
6. WHEN a boundary encloses elements THEN the boundary SHALL be sized to contain them with a margin, and SHALL NOT overlap a sibling boundary.

### Requirement 9 — Deployment diagram (`c4/deployment`), System Landscape (`c4/system-landscape`), and the furniture every diagram needs

**User Story:** As an operations engineer, I want a Deployment diagram, and as an architect a System Landscape, and I want every C4 diagram to explain its own notation.

#### Acceptance Criteria

1. WHEN a `c4/deployment` view is opened THEN its scope SHALL be **one or more software systems within a single deployment environment** (production, staging, development), and the environment SHALL be named on the view.
2. WHEN deployment nodes are drawn THEN they SHALL represent where instances run — physical or virtual infrastructure, containerized environments, execution contexts — and they SHALL be **nestable** to arbitrary depth, since C4 states deployment nodes can be nested.
3. WHEN a container is deployed THEN it SHALL appear as a **container instance** inside a deployment node, referencing the container in the model rather than duplicating it, and a single container SHALL be able to have several instances.
4. WHEN supporting infrastructure is drawn THEN **infrastructure nodes** — DNS, load balancers, firewalls — SHALL be available as a distinct element kind from deployment nodes.
5. WHERE vendor icons (AWS, Azure) are used on a deployment view THEN they SHALL be permitted, and the legend SHALL include them, per C4's guidance that icons are fine if the key explains them.
6. WHEN a `c4/system-landscape` view is opened THEN its scope SHALL be **an organisation, enterprise or department** — a system context diagram without a specific focus on one system — and its permitted elements SHALL be people and software systems, with **no** single primary element.
7. WHEN **any** of the seven views is rendered THEN it SHALL carry a **title** describing its type and scope, because C4 requires every diagram to have one.
8. WHEN **any** of the seven views is rendered THEN it SHALL carry a **key/legend** explaining every shape, colour, border, line style and arrowhead it uses, because C4 requires every diagram to be self-describing without accompanying narrative.

### Requirement 10 — The C4 rules are enforced, and violations are explained rather than silently allowed

**User Story:** As an architect, I want ADP to stop me putting the wrong thing on a diagram, so that the model stays a C4 model rather than drifting into boxes-and-lines.

#### Acceptance Criteria

1. WHEN an element kind is not permitted on the view being edited THEN the system SHALL refuse it and SHALL say which kinds that view permits and why — the abstraction levels are Person → Software System → Container → Component → Code, and each view (Requirements 5.2, 6.1–6.2, 7.1, 7.7, 9.2–9.4, 9.6) permits a defined subset.
2. WHEN an element is created THEN it SHALL require a **name** and SHALL prompt for a **description**, since C4 requires every element to have a short description giving an at-a-glance view of its responsibilities.
3. WHEN a **container** or a **component** is created or edited THEN a missing **technology** SHALL be reported as a violation, because C4 states every container and component should have a technology explicitly specified.
4. WHEN a **relationship** is created THEN it SHALL require a label, and the label SHALL be consistent with the direction of the arrow, because C4 requires relationships to be labelled and unidirectional.
5. WHEN a relationship crosses a container boundary THEN a missing **technology/protocol** SHALL be reported as a violation.
6. WHEN the model nests elements THEN the hierarchy SHALL be enforced: a container SHALL belong to exactly one software system, a component to exactly one container, and a code element to exactly one component. A cycle in containment SHALL be refused.
7. WHEN violations exist THEN they SHALL be reported as **warnings that do not block saving** — a model mid-edit is routinely incomplete, and a tool that refuses to save an unfinished diagram is worse than one that says what is unfinished. Structural impossibilities in 10.1 and 10.6 SHALL be refused outright, because they cannot be a work-in-progress.
8. WHEN a violation is reported THEN it SHALL name the element or relationship, state which C4 rule it breaks, and be selectable so that clicking it selects the offending element through the existing context chain.
9. WHERE acronyms are used in names THEN the system SHALL NOT refuse them, but the description SHALL remain required (10.2), since C4's guidance against unexplained acronyms is about comprehensibility rather than a syntactic rule.

### Requirement 11 — Code diagram (`c4/code`), and why it delegates

**User Story:** As a developer, I want the Code level to behave the way C4 actually advises, so that ADP does not encourage me to maintain something by hand that my IDE can generate.

#### Acceptance Criteria

1. WHEN a `c4/code` view is opened THEN its scope SHALL be **a single component**, and its primary elements SHALL be code elements — classes, interfaces, objects, functions, database tables — within that component.
2. WHEN the notation is chosen THEN it SHALL be a **UML class diagram or an entity-relationship diagram**, since that is what C4 specifies for this level, rather than the box notation the other six views use.
3. **BECAUSE C4 explicitly advises against hand-drawing this level** — "not \[recommended], particularly for long-lived documentation because most IDEs can generate this level of detail on demand" — the Add dialog SHALL present `c4/code` as optional and advanced, and SHALL say why, rather than offering it as the natural fourth step after Component.
4. WHEN a code view is rendered THEN ADP SHALL delegate the notation to an existing class/ERD diagram type (`uml/class`, `mermaid/class` or `erd/entity-relationship`) rather than implementing a fourth notation inside the C4 modules, and the `c4/code` module's own job SHALL be limited to binding that drawing to the component it details.
5. WHEN a code view is populated THEN it SHALL show only the attributes and methods that tell the intended story, per C4's guidance, rather than every member of every type.
6. WHERE generation from source is not available THEN the view SHALL still be openable and hand-editable — the advice against it is guidance to the user, not a prohibition ADP enforces.
7. IF the delegated notation type is not yet implemented THEN `c4/code` SHALL report itself as not yet available, naming what it depends on, rather than shipping a private half-notation. This dependency is stated as a **finding**: `c4/code` is the one of the seven that cannot be completed independently.

### Requirement 12 — Toolbox

**User Story:** As an architect, I want the elements I am allowed to place on the current view, and only those.

#### Acceptance Criteria

1. WHEN a C4 view is active THEN the Toolbox SHALL be described **by the backend as data**, per `tech.md`'s Toolbox rule, and the client SHALL render a palette it does not interpret.
2. WHEN the Toolbox is populated THEN it SHALL offer exactly the element kinds the active view permits (Requirement 10.1) — so a Context view offers Person and Software System, a Deployment view offers Deployment Node, Infrastructure Node and Container Instance, and no view offers a kind it would then refuse.
3. WHEN an item is dragged onto the canvas THEN it SHALL create that element in the **model** and place it on the **view**, as one undoable command.
4. WHEN an element already in the model is not on the active view THEN the Toolbox SHALL offer it for **adding to the view** as a distinct group from creating a new element, since "model once, view many" makes that the common action.
5. WHERE the Toolbox host panel is not yet implemented THEN this requirement SHALL still define what the type contributes, per `tech.md`'s instruction that a type states its contribution even though the host arrives separately.

### Requirement 13 — Context actions and commands

**User Story:** As an architect, I want every C4 edit to be undoable and reachable the same way as every other edit in ADP.

#### Acceptance Criteria

1. WHEN any edit is made THEN it SHALL be an `ICommand` with an inverse, per `tech.md`'s Commands rule and `diagram-undo-redo`: create element, rename, edit description, set technology, mark external, create relationship, relabel relationship, reverse relationship direction, move (Requirement 8.3), resize a boundary, delete, and renumber a dynamic interaction (Requirement 7.6).
2. WHEN actions are offered THEN they SHALL come from an `IContextActionProvider` and reach the ribbon, the right-click menu and the keyboard through the one path `context-service` defines, with shortcuts described as data.
3. WHEN an element is selected THEN the actions offered SHALL depend on what it is: a relationship offers *reverse direction*, a container offers *open component view*, a deployment node offers *add child node*, and a view offers *set scope*.
4. WHEN a container is selected on a Container view THEN a **drill-down** action SHALL open the Component view of that container, creating it if it does not exist, since moving between levels is C4's central interaction.
5. WHEN an element is removed THEN **remove from view** and **delete from model** SHALL be separate actions with separate inverses (Requirement 1.4), and the destructive one SHALL say how many other views it affects before it runs.
6. WHERE the diagram is read-only THEN no mutating action SHALL be offered — not greyed out and silently inert, but absent or explained, per `adp-diagram-ide`'s read-only mode.
7. WHEN an action cannot apply to the current selection THEN it SHALL be reported unavailable with a reason, through the mechanism `context-service` already provides.

## Non-Functional Requirements

### Code Architecture and Modularity

* **Single Responsibility Principle**: parsing the DSL, computing layout, mapping to `Element`/`Delta`, and validating C4 rules are four separate concerns in four separate units — the split `mindmap-diagram` arrived at, applied from the start.
* **Modular Design**: the seven modules SHALL share one C4 model library rather than duplicating the abstractions seven times; that shared library depends on core, and core depends on none of them.
* **Dependency Management**: the C4 modules SHALL NOT reference one another except through the shared model library, so a type can be built or dropped independently — with `c4/code`'s stated exception (Requirement 11.7).
* **Clear Interfaces**: the rule validator SHALL be a pure function from model plus view to a list of violations, so every rule in Requirement 10 is testable without a canvas, a file or a connection.

### Performance

* A model of 500 elements and 1000 relationships SHALL open and render within the budget `adp-diagram-ide` sets for a diagram of that size.
* Layout SHALL be computed on the backend and delivered through the viewport contract, so a large landscape does not stream in full before the first paint.
* Rule validation SHALL run incrementally on edit rather than re-validating the whole model on every keystroke.

### Reliability

* A `.dsl` file ADP did not change SHALL be byte-identical after a save (Requirement 3.1), guarded by a corpus test over real-world Structurizr models including the canonical Big Bank plc example.
* An unparseable or partially written `.dsl` SHALL never be overwritten (Requirement 3.4).
* Every C4 rule in Requirement 10 SHALL have a test that fails when the rule is not enforced.

### Usability

* The notation SHALL be recognisable as C4 to someone who has read c4model.com and never seen ADP (Requirement 4).
* Violations SHALL read as guidance naming the rule, not as error codes (Requirement 10.8).
* Nothing in the UI SHALL imply the four static levels are all mandatory: C4 states the Context and Container diagrams are sufficient for most teams, Component diagrams are optional, and Code diagrams are discouraged.