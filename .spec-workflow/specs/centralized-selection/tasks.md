# Tasks Document

One worktree for the whole specification (`.claude/worktrees/csel`, per CLAUDE.md's one-worktree-per-specification rule), and one Developer owning it until every task is done. Every landing goes through the shared gate: `gate.sh` on the implementer's own scratch tree, then the printed `land.sh` line run by hand in the foreground. The worktree is retired with `retire.sh`. Running the app for the browser pass happens from the worktree on the implementing Developer's reserved ports, with both port files reverted before merging.

**Two design points were raised for the user's objection and approved without annotation on 2026-09-11.** They are approved, not merely unchallenged: the selected look changes mechanism, to an outline outside the shape with *accept* as a dashed outline at a larger offset; and Requirement 9.2 is met through each module's own test harness, with the text guard enforcing it.

**A dependency another specification carries too.** Architect 1's `module-client-api-readme` documents the module-facing API **as this specification leaves it**, so its implementation **does not start until this specification's implementation is on develop**, checked with `git merge-base --is-ancestor`. Any rename, addition or removal of a module-facing name here is sent to it at the same time.

**The order is load-bearing.** The approved design's end state removes `selection`, `context` and `onSelectionChanged` from the contract, so that typecheck refuses a module still wiring them. Removing them in the library task would break the build for all sixteen modules at once and make "each module its own landing" impossible. So the library **adds** the new mechanism while the old props still work, every module migrates in its own landing, and the old props are **removed** after the sixteenth (task 21). While both exist, the rule is simple: a canvas that passes `source` and none of the old props has its selection owned by the library, and a canvas still passing the old props behaves exactly as today.

---

## Group 1 — The library, landed before any module changes

- [x] 1. `selectable` on element and relation types
  - Files: `src/client/src/canvas/library/definition/diagramDefinition.ts`, `validateDiagramDefinition.ts`, tests
  - `selectable?: boolean` on `ElementTypeDefinition` and `RelationTypeDefinition`. **The doc-comment says in words that the default is selectable**, so "not selectable" is only ever a declared value and never the absence of one.
  - _Requirements: 2.3_

- [x] 2. The selection model inside `DiagramCanvas`
  - Files: `src/client/src/canvas/library/DiagramCanvas.tsx`, `DiagramCanvas.test.tsx`
  - A `source: { entryId, path }` prop. **Inbound**: the pushed selection is resolved against the model the library already holds — a connection if `model.connections` has it, an element if `model.elements` has it, otherwise nothing, and nothing for an unselectable type. **Outbound**: `select(...)` pushes `elementSelectionOf(...)`, or `null` for an empty selection. **The click rules**, stated once: a press selects and replaces; a background press clears; a press on an unselectable type behaves as a background press; a vanished item pushes `null` once. **A drag does not select** — `select` stays reachable only from `onPress`.
  - `DiagramSelection` arrays end to end: one pushed id becomes a one-item array, and outbound sends the single member. **Nothing of multi-select** — no Ctrl+click, marquee or wire change.
  - Active only when a canvas passes `source` and none of the old props; otherwise today's behaviour stands unchanged.
  - _Requirements: 1.1, 1.2, 2.1, 2.2, 2.3, 3.1, 3.2, 3.3, 3.4, 7.1, 7.2_

- [x] 3. The menu integration, owned by the library
  - Files: `DiagramCanvas.tsx`, tests
  - The library builds `selectionKey`, `actions`, `selectForMenu` and `executeAction` itself from the pushed selection. A backend refusal of a shared-menu action is raised to the module as an **`action-refused`** event carrying the backend's message, because each module keeps its own rejection display.
  - _Requirements: 1.3_

- [x] 4. Two looks, each with one class
  - Files: `src/client/src/canvas/canvas.css`, `DiagramCanvas.tsx` (`LibraryElement`), `noUnstyledLibraryClasses.test.ts`
  - The shared rule styling `.canvas-selected .canvas-node` and `.canvas-connect-target .canvas-node` together is **split**. *Selected*: an outline the library draws outside the shape's own boundary, independent of fill and stroke, so it is legible on backend-chosen colours. *Accept*: a dashed outline at a larger offset in its own colour token, as `library-connect-target` only. The anchors keep `1df91874`'s behaviour for *selected* and take the accept colour under a connect target. A selected connection keeps its centrally defined line treatment.
  - _Requirements: 5.2, 5.3, 6.1, 6.2_

- [x] 5. The composition rule
  - Files: `DiagramCanvas.tsx` (`LibraryElement`), tests
  - `{ selected, dragging, connectTarget }` stay three independent, additive flags that do not read one another. Tested: **dropped, held and selected** shows the selected outline at the held position with no dragging look; **selected and a valid drop target** shows both outlines; a drag leaves the selection unchanged.
  - _Requirements: 5.3, 6.2_

- [x] 6. The shared mounted assertion
  - Files: `src/client/src/canvas/library/testing/expectLibrarySelection.ts` (new), its own test
  - `expectLibrarySelection(mount)` asserts that a pushed element selection highlights that element, that a pushed connection selection highlights that connection wherever the relation type is selectable, and that a background press pushes `null`. Each module's canvas test calls it with that module's real canvas and model.
  - _Requirements: 9.2_

- [x] 7. Gate and land Group 1
  - Four gates green on the merged tree. **Every existing module test passes unchanged**, since no module has migrated yet and the old props still work.
  - _Requirements: 11.1, 11.2_

---

## Group 2 — The reference migration

- [x] 8. `timeline`
  - It already uses only the shared look and already selects connections, so migrating it proves the central model reproduces the user's working behaviour with the least visible change. Pass `source`; delete the inbound `useMemo<DiagramSelection>`, the `onSelectionChanged` handler and the `context` prop. Its canvas test calls `expectLibrarySelection`. **Its existing tests pass unchanged**, with no exemption needed, because it declares no private class.
  - _Requirements: 1.4, 9.2, 11.1, 11.2_

---

## Group 3 — The four the user named broken

- [x] 9. `helm-charts` — **the natural red first**
  - **Before migrating**, run `expectLibrarySelection` against today's helm canvas and **record it failing two of its three checks**: the background press pushes nothing, and a connection is never highlighted. That is the guard seen red against the real defect.
  - Then migrate: the glue goes, `helm-node-selected` goes, connections become selectable, and a background press clears. The same assertion now passes.
  - _Requirements: 2.1, 3.2, 5.1, 9.2, 9.3, 11.1_

- [x] 10. `ansible-structure`
  - The glue goes and `ansible-node-selected` goes. **`focusedId` is deleted**: its one consumer, Enter or Space revealing the focused node's file, becomes a **declared action** (`invokedBy` Enter and Space, applying to elements) whose handler runs against the library's selection.
  - _Requirements: 3.2, 4.1, 5.1, 9.2, 11.1_

- [x] 11. `azure-pipeline`
  - The glue goes. **`focusedId`, the `payload.focused` field computed from it, and `pipeline-focused` are all deleted**, so its highlight is `canvas-selected`, driven by the selection like everyone else's.
  - **Backend: `PipelineContextSourceResolver` resolves a connection id.** Today its `Find` walks stages, jobs and steps and rejects everything else as "Unknown element.", so an arrow press would push an id the backend refuses and the arrow would never highlight, which is worse than today's deliberate fall-through to a deselect. `Find` also walks the stage graph (`PipelineGraphBuilder.OfStages`) and each stage's job graph (`OfJobs`), matching ids through **`PipelineElementMapper.EdgeId`, never a re-spelled format**. Implicit and broken edges are drawn, so they resolve too. An edge answers **as its declaring side**, the way `ansible-structure`'s does: the path is the waiting element's, the text names the waiting element first, as in *"Deploy waits for Build"*, there are no children, and it is linked when the waiting element comes from a template. `Track` shares `Find`, so an edge selection survives an unrelated edit and clears when the edge goes (3.3). A backend test, **seen red against today's resolver**, shows an explicit, an implicit and a broken edge id resolving and an unknown id still rejected, and that the property and action providers answer a selected edge without throwing.
  - _Requirements: 2.1, 2.2, 3.3, 4.1, 4.2, 5.1, 9.2, 11.1_
  - *The backend bullet and the added claims 2.1, 2.2 and 3.3 were added on 2026-09-11 during implementation. Developer 1 found the rejection, and task 26 is how the same gap is found everywhere else.*

- [x] 12. `dotnet-dependency-graph`
  - The glue goes and `dotnet-dependency-selected` goes. A connection press now reaches the backend **as a connection** rather than mislabelled as an element, and is highlighted.
  - _Requirements: 2.1, 2.2, 5.1, 9.2, 11.1_

---

## Group 4 — The other eleven, each its own landing

Each: pass `source`, delete the glue, delete its private selected class, and call `expectLibrarySelection` from its canvas test. Existing tests pass unchanged except those asserting a removed class (Requirement 11.1), each citing the requirement that removed it.

- [x] 13. `dependency-graph` — `dependency-graph-selected`, and its private connect-target declaration
  - _Requirements: 1.4, 5.1, 6.1, 9.2, 11.1_
- [x] 14. `causal-loop` — its redundant second declaration of `canvas-selected`
  - _Requirements: 1.4, 5.1, 9.2, 11.1_
- [x] 15. `c4` — `c4-node-focused`, and **its boundary type declares `selectable: false`**, replacing the hand-written check in `onSelectionChanged`
  - _Requirements: 1.4, 2.3, 5.1, 9.2, 11.1_
- [x] 16. `databricks` — `databricks-selected`, and its two private connect-target declarations
  - **Its three simulated actions are declared `invokedBy: { kind: "menu" }`**, so the shared menu dispatches them to the module's handler and nothing reaches the backend, the history or a file (the approved design; `databricks` Requirements 8.6 and 11.6). Their ids come from the simulation engine's own list rather than being retyped. A test: a simulated entry chosen from the shared menu plays the show and calls no `executeAction`, **seen red first**. The backend no-ops a simulated id that reaches it anyway, so a miss would otherwise be silent.
  - **Its edges become selectable under 2.1, with no declaration.** Today `onSelectionChanged` drops any press that includes a connection, so an edge press pushes nothing at all. `JobCanvas.test.tsx`'s "ignores a press on an edge" changes its Assert to the edge's id being pushed, naming 2.1 under the amended 11.1; its Arrange and Act stay. The backend already resolves edge ids (`DatabricksSelection.TryEdge`).
  - **`src/client/src/canvas/label/readme.md` says this canvas has no edge selection.** That becomes false here, so it is corrected in the same change. The note that the edge relabel prompt then qualifies and should be marked is **left for the label owner**: marking it is not this specification's work.
  - _Requirements: 1.3, 1.4, 2.1, 2.2, 5.1, 6.1, 9.2, 11.1_
  - *The three bullets above were added on 2026-09-12 during implementation, with the approved design amendment and the amended 11.1.*
- [x] 17. `rdf`, all four readings — `owl-selected`, `shacl-selected`, `skos-selected`, and the nine private connect-target declarations (seven in the OWL reading, two in SKOS)
  - _Requirements: 1.4, 5.1, 6.1, 9.2, 11.1_
- [x] 18. `sparql` — `sparql-selected`
  - _Requirements: 1.4, 5.1, 9.2, 11.1_
- [x] 19. `mindmap` — `mindmap-node-focused`; **its relation types declare `selectable: false`**, and its readme records that as a decision about the notation
  - _Requirements: 1.4, 2.4, 5.1, 9.2, 11.1_
- [x] 20. `wardley-map` — `wardley-selected`; **its relation types declare `selectable: false`**, with the same readme note
  - _Requirements: 1.4, 2.4, 5.1, 9.2, 11.1_

---

## Group 5 — Close the old path and prove it stays closed

- [x] 21. Remove the old props from the contract
  - Files: `DiagramCanvas.tsx`
  - `selection`, `context` and `events.onSelectionChanged` go, and `DiagramContextIntegration` stops being exported. **From here typecheck refuses any module wiring selection by hand**, which is the approved design's end state. Only now, because all sixteen have migrated.
  - _Requirements: 1.4_

- [x] 22. The text guard
  - Files: `src/client/src/canvas/library/noModuleSelection.test.ts` (new)
  - Walks every module client and fails, naming each offender, on: a `DiagramSelection` derivation, an `onSelectionChanged` handler, a `selection=` or `context=` prop, a declared class bound to `state.selected` or `state.connectTarget`, or module-held focus state. **It also fails a registered module whose canvas test does not call `expectLibrarySelection`**, which is what makes the mounted assertion complete.
  - Keyed per canvas file; both canary shapes, a floor on **files walked** (structure, not content, per `processes.md`) and a named member present. Its doc-comment states its limit: glue under an unrecognisable name goes unseen, and the mounted assertion closes that.
  - **Seen red** against a planted offender in a scratch copy before acceptance.
  - _Requirements: 9.1, 9.3, 9.4_

- [ ] 26. The backend names everything its canvas draws — **added during implementation; numbered 26 so no existing number moves**
  - Files: one shared backend test helper (new) in **the existing `src/backend/EtAlii.Adp.TestSupport`**, which gains a `Context` reference; one call from each module's backend test project; and a completeness fact in `EtAlii.Adp.Backend.Tests` (new). **No new project.** *Corrected on 2026-09-12 by ruling, recorded on 2026-09-15: this line first said no shared test-support project existed, which was wrong. `EtAlii.Adp.TestSupport` is the shared, referenceable one.*
  - **Why it exists.** The library pushes a pressed connection's own id, and the highlight follows the backend's answer. So Requirement 2 holds on a canvas only if **that module's context resolver resolves every connection id its canvas draws**. The design left this implicit, and every client test mocks the backend, so no guard written so far can see it. `azure-pipeline` rejected every edge id (task 11). By reading alone, rdf's shared resolver is a suspect: `RdfSelection.EdgeOf` returns nothing for an edge with a non-resource end, though its comment says such edges "select for describing", and SKOS draws edges (a `broader` read from a `narrower` triple) that a triple lookup cannot match. **These are suspects, not findings.** This task's measurement settles them.
  - **The helper**, the backend twin of `expectLibrarySelection`: given a module's shipped examples, it projects each the way the module's session does, resolves the id of every element of a connection type through the module's registered resolver the way the context service does, and fails naming the diagram type, the example and the id.
  - **Which elements are connections.** The backend has no notion of a connection: `DiagramElement` is an id, a position, a type string and a payload. So each module's backend test **passes its full type set, connection types and element types, as references to its own mapper's constants**, never string literals. The emitting mapper already declares every one (`PipelineElementMapper.EdgeType`, `WardleyElementTypes.Link`, `DatabricksElementMapper`'s three, the four rdf mappers' `EdgeType`, and so on), and a client relation type has to equal that string or nothing renders as that relation, so this is the emitter's own declaration rather than a second copy of client knowledge. The helper then asserts: **every element the projection emits across the examples has a type in the union**, so a new connection type cannot slip past; **every named type appears at least once**, so a stale or misspelled constant fails loudly; and **a module with no connection types states why in words** (`mindmap`: branches are derived client-side from `parentId`), with the union check still applying to it. *Added on 2026-09-15, recording the 2026-09-12 ruling on Developer 1's fifth gap: the first wording, "every connection element the projection emits", assumed a distinction the backend does not have.*
  - **No exemption list.** Every drawn connection resolves, selectable or not, so selectability stays a client declaration alone, and making `mindmap`'s or `wardley-map`'s connections selectable later needs no backend work. A list of exempted types would be a second copy of the client's `selectable: false`, and it would stay silent exactly when the two disagree.
  - **The completeness fact** walks `src/diagrams/*/backend/` and fails a module whose test project does not call the helper, **discovering modules by artifact, not by a list**. It carries both canary shapes: a floor on modules walked (structure, not content) and a named member present.
  - **As measured and built, 2026-09-15. Where this bullet and the ones above disagree, this one holds.** The helper's first run answered four questions the text above could not:
    - **One test over the real host, not a call per module.** `EtAlii.Adp.TestSupport/DrawnConnections.cs` (the helper, with `Context` and `Diagram` references) and `EtAlii.Adp.Backend.Tests/Integration Tests/DrawnConnections.Tests.cs`, over `WebApplicationFactory<Program>`, with one entry per example folder and the constants still referenced. Only 3 of 16 module test projects had a provider to reuse, and the real host exercises what isolated builds cannot: every other module's resolver refusing a foreign id.
    - **Completeness discovers by the two artifacts that make a canvas openable, not by module folder.** There are 61 folders under `src/diagrams/*/backend` and 16 under `src/examples/diagrams`, so a folder walk would force stub modules with no examples and no canvas into the list. Instead: (a) every `src/examples/diagrams/*` folder is in the list, with a floor of 16 and `timeline` named; and (b) every `IDiagramSessionFactory` the host registers has its type opened by a checked example, which is the check that catches a new module with a canvas.
    - **A named type must be seen only if the corpus can show it.** With the constants referenced, the compiler catches a misspelling, so "seen at least once" would only measure the corpus. Types no example contains (helm `ArchiveType` and `CrdsType`; owl, shacl and sparql `TruncationType`; skos `CollectionType`) are left out of the passed set, and the union check still fails the day one is emitted. **`azure-pipeline` declares `ExpandViews`** (every stage opened), because its job-level edges exist only in an expanded stage and task 11's job-graph fix would otherwise go untested. Its `StepType` is left out because nothing emits it: `PipelineElementMapper.StepsOf` has no caller. **That is a missing feature, not dead code** (Architect 1): archived `azure-pipeline` Requirement 8.2 says a job is "in turn expandable to its steps", and its task 18 is marked done, but only stage expansion exists. It is with the user as a question. When steps are emitted, `StepType` goes back into this entry, and until it does the union check fails loudly.
    - **Resolution is tried from both the `.adp` and a routable body, for every module.** `dotnet-dependency-graph` assumed the body and failed every edge when opened from its `.adp`, while pipeline, c4 and ansible route either way.
    - **Offenders found and fixed here**, all with no wire or payload change, since their module tasks had landed: shacl, all 14 edges (a new `ShaclSelection` wired into `RdfContextSourceResolver`); databricks override and flow edges (`DatabricksSelection`); c4's elevated relationships (`C4ContextSourceResolver`); dotnet opened from its `.adp`. **These connections become selectable in the app**, which is Requirement 2.1's own change under the general 11.1 rule, and the browser pass (task 24) covers them.
    - *Recorded on 2026-09-15 from Developer 1's measurement, with the rulings given that day.*
  - **Order.** Write the helper first and run it across all modules in a scratch tree. That run is the measurement, and `azure-pipeline`'s rejection is its natural red. Each offender it names is fixed in that module's task above, or here if that task has already landed. The guard lands green, after the fixes it demands.
  - _Requirements: 2.1, 2.2, 9.3_

- [x] 27. The background menu, and `causal-loop`'s follow-up — **added on 2026-09-12; a task of its own because task 14 has already landed**
  - Files: `diagramDefinition.ts`, `validateDiagramDefinition.ts`, `DiagramCanvas.tsx`, `CausalLoopCanvas.tsx`, their tests
  - `DiagramDefinition.backgroundMenu?: boolean`, **whose doc-comment says in words that the default is no background menu**, so a background menu is only ever a declared value. On a declaring canvas, a background right-click converts the pointer the way a toolbox drop already does, pushes that placement (`new:x,y`, canvas coordinates) through the library's own `selectForMenu`, and opens the shared menu on the backend's answer. A right-drag that drew a relation is still not a menu.
  - `causal-loop` declares it, and **its hand-written pointer conversion, its placement push, its read of the pushed selection and its own `ContextMenu` are deleted**. It is the only module with a background menu, so nothing else changes.
  - Tests: a declaring canvas opens the shared menu on a background right-click and an undeclared one opens nothing; `causal-loop`'s existing menu tests keep their Act and Assert, since the behaviour is the same menu from the library.
  - _Requirements: 1.3, 1.4, 11.2_

- [ ] 23. The `declarative-diagram-modules` Requirement 1.2 annotation — **its own card**
  - Files: `.spec-workflow/specs/declarative-diagram-modules/requirements.md` (main checkout, spec bookkeeping)
  - A **labelled annotation only** beside 1.2 — *"superseded by `centralized-selection` Requirement 8: selection is not a module's to handle"* — with **no rewording of the approved text**. **Raised as its own dashboard card** so the user approves the moved text rather than meeting it by reading, and the Scrum master told as it is raised.
  - _Requirements: 8.1, 8.2_

- [ ] 24. The browser pass
  - Files: `tests.md`
  - All sixteen canvases in a real browser: select an element, select a connection where selectable, press the background, and **confirm a drag does not select**. The outline must read clearly on each diagram's fills, including backend-chosen colours; a connection's highlight must run its whole route; *selected* and *accept* must be distinguishable when both hold. **The user's oracle, recorded as such**: the four named broken behave exactly as the four named working. jsdom is not taken as evidence for any of this.
  - _Requirements: 5.2, 10.1, 10.2, 10.3_

- [ ] 25. Gate, merge, and the coverage diff against the code
  - Four gates on the merged tree; ports reverted; the coverage diff re-run against files and strings rather than task claims.
  - _Requirements: 11.1, 11.2_

---

## Requirement coverage

Every acceptance criterion in the approved requirements was checked against every task claim above, mechanically, in both directions. Thirty-two criteria (4, 4, 4, 2, 3, 2, 2, 2, 4, 3 and 2 across Requirements 1 to 11), all claimed, and no claim names a criterion that does not exist.

**No criterion is unclaimed, so there is no *no task can claim it* item to name.** Two criteria are **prohibitions rather than work** — 7.1 (single-selection, no multi-select mechanism) and 10.3 (jsdom is not evidence of the look). They are claimed by the tasks that must observe them (2 and 24). A claim is a promise, not proof: the post-implementation run of the diff, in task 25, traces each of the two to the code and the browser record rather than to these claims.
