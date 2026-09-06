# Tasks Document

**One developer owns this specification until every task is done.** The pieces are ordered so nothing observable changes until a reference canvas lands, and the two references are the proof the schema is real — a library green only on its own unit tests is asserted, not demonstrated (Requirement 9.3).

**Two requirements are satisfied by a document rather than by a task, and are named here so the coverage diff does not read them as gaps:**

- **Requirements 9.1 and 9.2** — the schema checked against each of the twelve modules, and that check covering the named hard cases (wardley's intrinsic space, timeline's axis, causal-loop's arcs and self-connections, c4 nesting, rdf's budget, sparql grouping, mindmap's tree, databricks' shared inner canvas) — are discharged by the design's per-module coverage table and its hard-cases section, both measured from the canvases before the schema was fixed. No task re-does them; a task would only re-assert what the design already records.
- **Requirement 9.4** — a gap recorded with its consequence — is discharged by the design's "one gap" section (the custom-route hatch that causal-loop's self-loop needs). Should implementation of a reference canvas surface a *new* gap, task 5 or 6 records it the same way rather than widening scope silently.

**Worktree.** `.claude/worktrees/dlib` — short, because a long name pushes the deepest project paths past `MAX_PATH` and the symptom is `dotnet test` reporting `Zero tests ran` rather than failing. This is a client-only specification, so `npm test` and `npm run typecheck` are the gates that matter — but all four run before every merge, and `dotnet format`/`dotnet test` must still exit zero. `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_USE_MSBUILD_SERVER=0` before the backend gates, every gate's exit captured **into a variable before any pipe**.

**Identity, once, at creation**: `git config --worktree user.name "developer-N-diagram-library"`. Every command run there inherits it, merges included. Read it back with plain `git config user.name` before your first commit in any tree you did not create.

**A fresh worktree is built before the first green is trusted** — the codegen under `src/client/src/generated/` is the input every other gate reads from a cache, so `npm install` and a clean generate in the new tree come before believing any gate about upstream work.

**Merging** through a **per-agent** scratch worktree, reset, merge, gates and fast-forward chained into one command: develop moves faster than a gate cycle here.

## Tasks

- [x] 1. The definition schema and its validators
  - Files: `src/client/src/canvas/library/definition/` (new), and its tests
  - The plain-data types from the design: `DiagramDefinition`, `ElementTypeDefinition`, `RelationTypeDefinition` with its `endpoints` constraint, `RouteKind` (the built-ins plus `CustomRouteRef`), `BuiltInShape` plus `CustomShapeRef`, `AnchorSet`, `LayoutDefinition`, `DraggingPolicy`.
  - **Data only — no React, no closures the shell must serialize.** A definition is configuration a module states, read every render (Requirement 1.3).
  - Validators: a definition with zero layout modes is rejected at construction (design Error Handling 4); an endpoint constraint naming an unknown element type is rejected; a `CustomRouteRef`/`CustomShapeRef` with no renderer is rejected.
  - Tests: each validator seen to reject its bad input and accept a good one; the built-in `RouteKind` set is exhaustive over the four families the design's table names (straight, cubic-bezier, arc, and the interactive/adjustable variant).
  - _Requirements: 4.1, 4.2, 8.1, 8.2, 9.4_
  - _Prompt: Implement the task for spec diagram-library, first run spec-workflow-guide to get the workflow guide then implement the task: Role: TypeScript developer | Task: Define the diagram-definition schema and its validators as plain data under canvas/library/definition | Restrictions: no React or non-serializable values in a definition; reject a zero-layout-mode definition, an endpoint naming an unknown element type, and a custom shape/route with no renderer; the built-in route set must cover the design's four families | Success: the schema types exist and the validators reject each bad shape, seen to do so. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 2. The API surface: events and runtime configuration
  - Files: `src/client/src/canvas/library/api/` (new), and its tests
  - The `DiagramEvent` union — one member per behaviour the design lists: `elementDropped`, `elementDeleted`, `elementMoved`, `connectionDrawn`, `connectionDeleted`, `connectionAdjusted`, `selectionChanged`, `labelCommitRequested`, `viewChanged`, `layoutModeChanged` — and the `runtimeConfig` shape (`dragging`, `activeLayoutMode`, `activeTool`, definition overrides).
  - **Every event is a request, never a mutation** (Requirement 1.2): the types carry what the module needs to answer and nothing the library would act on itself.
  - **The selection type is set-shaped from the start** and constrained to one member until `multi-select` lands (Requirement 7.4), so adopting the set later changes no module.
  - Nothing renders yet; this fixes the surface both reference canvases are written against.
  - _Requirements: 1.1, 1.2, 1.5, 7.4_
  - _Prompt: Implement the task for spec diagram-library, first run spec-workflow-guide to get the workflow guide then implement the task: Role: TypeScript developer | Task: Define the DiagramEvent union and runtimeConfig types under canvas/library/api | Restrictions: every event is a request the module answers, not an action the library takes; the selection type is set-shaped but single until multi-select lands; a capability reachable only by forking the canvas is a defect in this surface | Success: the event and config types exist and a handler map over them typechecks. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 3. The canvas component over the existing parts
  - Files: `src/client/src/canvas/library/DiagramCanvas.tsx` (new), and its tests
  - Render elements through the seven shape components and custom shapes; render connections through the route builders; **resolve each anchor once for rendering, the connect preview and hit-testing** so the three cannot disagree (Requirement 3.4); own pan/zoom/selection state; drive every gesture through `usePointerGesture` (Requirement 5.1) — no gesture machinery beside it.
  - **The component mutates nothing.** Each gesture calls the matching handler and waits for `model` to change, as the backend-fed canvases already behave (Requirement 1.2).
  - **Enforcement at gesture time** (Requirement 4.3): as a connect drag moves, targets and anchors no relation rule admits render non-connectable; a drop on a forbidden position refuses; the event is raised only for a gesture the definition already allows.
  - Labels edit through the shared `InlineLabelEditor` with placement resolved by the library, committing as `labelCommitRequested` (Requirements 2.3, 3.3, 6.1); no private editor (Requirement 6.3).
  - Selection flows through the existing `selection.ts` seam and the context menu through `useElementContextMenu` — the library adds hit-testing and rendering of selection, never a second policy (Requirements 7.1, 7.2). A gesture payload the context channel cannot carry is handed to the module in the event, not sent through an invented side channel (Requirement 7.3).
  - Registers view controls and toolbox **as a pair by construction** (Requirement 1.4); the toolbox derives from element types by default and feeds `useRegisterDiagramToolbox` unchanged (Requirement 4.4).
  - Shapes, connectors and labels resolve colour through theme tokens (Requirements 2.2, 3.2).
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 2.1, 2.2, 2.3, 2.4, 2.5, 3.1, 3.2, 3.3, 3.4, 3.5, 4.3, 4.4, 4.5, 5.1, 5.2, 5.3, 5.4, 5.5, 6.1, 6.2, 6.3, 7.1, 7.2, 7.3_
  - _Prompt: Implement the task for spec diagram-library, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React/TypeScript developer | Task: Build DiagramCanvas over the existing canvas parts - shapes, route builders, anchors, selection, context menu, inline editor, and usePointerGesture | Restrictions: the component mutates nothing and raises an event per gesture; anchors resolve once for render, preview and hit-test; enforcement happens at gesture time not by post-hoc veto; no private label editor, no second selection policy, no invented side channel; register view controls and toolbox as a pair; colours through theme tokens | Success: a definition plus a model plus handlers renders and operates a canvas, and a forbidden gesture cannot raise an event. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 4. The layout seam, with manual/external and tree
  - Files: `src/client/src/canvas/library/layout/` (new), and its tests
  - The `LayoutAlgorithm` seam and its built-ins. **Manual/external returns the model's own positions untouched** — the mode every backend-laid-out diagram uses, and the one both references need (Requirement 8.3). **Tree** (directional hierarchy) is the one automatic mode implemented here, because mindmap needs it and it exercises the seam; horizontal-flow, vertical-flow and layered-graph are declared in the schema and land as `diagram-library-adoption` reaches a module that needs each.
  - A definition allowing one mode presents no switching surface; switching raises `layoutModeChanged` (Requirement 8.2). Where automatic layout is active and dragging permitted, the definition says whether a drag repins to manual or is a reclaimed displacement (Requirement 8.4).
  - **No layout computation crosses the wire** (Requirement 8.3): a client algorithm runs only where a definition opts into it.
  - _Requirements: 8.1, 8.2, 8.3, 8.4_
  - _Prompt: Implement the task for spec diagram-library, first run spec-workflow-guide to get the workflow guide then implement the task: Role: TypeScript developer | Task: Build the layout seam with manual/external and tree algorithms behind one interface | Restrictions: manual/external returns model positions untouched; no layout computation crosses the wire; one mode means no switching surface; the definition decides what a drag means under automatic layout | Success: the seam runs manual/external and tree, and a further algorithm is an addition rather than a change. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [-] 5. rdf as the graph-shaped reference, end to end
  - Files: `src/diagrams/rdf/client/RdfCanvas.tsx`, its definition, and tests
  - Box elements, straight routes, edge anchors, manual/external layout, at the 1,000-element budget the model already enforces (`RdfProjection.DefaultBudget`) — proving the library at the sizes that started `drag-and-drop-centralization`, and that a plain graph is a short definition.
  - `RdfCanvas` builds `fitToView` at `RdfCanvas.tsx:127` today; the migrated canvas keeps that behaviour through the library rather than by hand.
  - **This is a migration of one canvas as proof, not the adoption pass** — the other nine are `diagram-library-adoption`. No new capability rides along (that spec's rule, honoured early here).
  - Tests, mounted: a permitted connect raises `connectionDrawn` with the right anchors; a forbidden one cannot; a drop on a forbidden position refuses; dragging disabled by config does not start a move. Each seen to fail against a deliberate break before it is trusted (Requirement 10.3).
  - _Requirements: 9.3, 2.1, 3.1, 3.4, 4.3, 5.2, 5.4_
  - _Prompt: Implement the task for spec diagram-library, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Full-stack developer | Task: Rebuild RdfCanvas on the library as the graph-shaped reference, at the existing 1000-element budget | Restrictions: no new capability rides along; keep today's fitToView behaviour; mounted tests seen to fail against a deliberate break first | Success: rdf renders and operates through the library and its guards fail-then-pass. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 6. timeline as the axis-shaped reference, end to end
  - Files: `src/diagrams/timeline/client/TimelineCanvas.tsx`, its definition, and tests
  - Span elements on a **time-scaled axis declared as a background**, cubic-bezier routes with facing anchors (`sideAnchorOf` + `facingAnchorsBetween`), the three label placements timeline uses (aside, centred, midpoint) — proving the intrinsic-axis/background mechanism and along-route labels that wardley and causal-loop will also need.
  - `TimelineCanvas` builds `fitToView` at `TimelineCanvas.tsx:209`; keep that behaviour through the library.
  - **If the axis-as-background mechanism turns out not to express the time scale, that is a new gap** — record it in this document with its consequence per Requirement 9.4 and raise it, rather than bending the schema silently.
  - Tests, mounted, as task 5.
  - _Requirements: 9.3, 9.4, 2.3, 3.1, 3.3, 8.1_
  - _Prompt: Implement the task for spec diagram-library, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Full-stack developer | Task: Rebuild TimelineCanvas on the library as the axis-shaped reference, with the time axis as a declared background and three route-label placements | Restrictions: no new capability rides along; keep today's fitToView behaviour; if the background mechanism cannot express the axis, record the gap with its consequence rather than bending the schema | Success: timeline renders through the library with its axis and along-route labels, guards fail-then-pass. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 7. The guards, mounted, and gate and merge
  - Files: shared guards beside the client's diagram tests; then the four gates and the merge
  - Three guards, each **mounting rather than grepping** and carrying a named-member canary (Requirement 10.2), because a grep over canvas sources has already reported three correct databricks canvases as offenders here:
    - a definition with a forbidden pair, mounted, cannot produce a `connectionDrawn` for it (Requirement 10.1) — seen to fail against a canvas that raises first and vetoes after;
    - no route builder constructs a path outside `connectors.ts` (Requirement 10.1);
    - a library canvas registers view controls and toolbox as a pair (Requirements 10.1, 1.4).
  - Four gates, exit codes captured before any pipe, on the merged tree; `npm test` and `npm run typecheck` are the load-bearing pair for this client-only spec, and a fresh-tree build precedes trusting the first green.
  - _Requirements: 10.1, 10.2, 10.3_
  - _Prompt: Implement the task for spec diagram-library, first run spec-workflow-guide to get the workflow guide then implement the task: Role: TypeScript developer | Task: Write the three mounted guards and gate and merge the specification | Restrictions: mount never grep; each guard carries a named-member canary and is seen to fail before it passes; four gates judged by exit codes captured before any pipe; fresh-tree build before trusting a green | Success: the guards fail-then-pass, four gates zero on the merged tree. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._
