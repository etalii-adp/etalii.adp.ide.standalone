# Tasks Document

**Every task below is blocked until `centralized-selection`'s implementation is on `develop`** (Requirement 7.2). Establish it with `git -C C:\git\EtAlii.Adp merge-base --is-ancestor {that spec's last commit} develop`, and record the commit checked in the first implementation log. Developer 1 reports when the whole specification has landed; its task 21, which removes `selection`, `context`, `onSelectionChanged` and `DiagramContextIntegration` from the contract, lands last.

**Where that work stood when these tasks were written**, measured at `develop` `8866455b` rather than asked: 12 of the 16 canvases call `expectLibrarySelection` in their tests, and `DiagramCanvas` still declares the `selection` prop — so task 21 had not landed and this specification could not start. **Two checks before task 1, not one:** the merge-base check above, and that the withdrawn names are gone from the contract. A readme written while both spellings exist would document whichever the author happened to read.

- [ ] 1. Spike: can mermaid's parser run in the client test environment?
  - File: src/client/package.json, src/package-lock.json, docs/dependencies.md, src/client/src/mermaidParseSpike.test.ts (temporary)
  - Add `mermaid` as a client dev dependency **with its row in docs/dependencies.md**, whose inventory is test-guarded, and run `mermaid.parse` on four throwaway diagrams (one of each kind the readme will use) under the client's jsdom environment.
  - If it parses: keep the dependency; check 6 of task 9 becomes real. If it does not: **remove the dependency and its row again**, and record why in the implementation log.
  - Purpose: settle by measurement whether the diagrams can be asserted at all, before the readme promises anything about them
  - _Leverage: src/client/vitest.config.ts (jsdom), docs/dependencies.md_
  - _Requirements: 5.4, 7.2_
  - _Prompt: Implement the task for spec module-client-api-readme, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Client developer with vitest and jsdom experience | Task: Add mermaid as a client dev dependency with its docs/dependencies.md row, write a temporary test calling mermaid.parse on a flowchart, a sequence diagram, a class diagram and a deliberately broken diagram under jsdom, and record whether parse runs and whether the broken one is rejected | Restrictions: Do not keep the dependency if parse cannot run - remove it and its row in the same change; do not add mermaid to the client bundle; the spike test is temporary and is deleted in this task | Success: the implementation log states, with the observed output, whether mermaid.parse runs under jsdom and rejects a broken diagram; the tree is left either with the dependency documented or without it at all, never installed and unused; set the task to [-] before starting, log with log-implementation, then [x]_

- [ ] 2. Compute the module-facing surface, with its canaries
  - File: src/client/src/diagramModuleClientApi.surface.ts (new), src/client/src/diagramModuleClientApi.test.ts (new)
  - Parse Set A (names module clients import from shared `@client/...` specifiers, a module's own generated payloads excluded, **plus what module test files import from `@client/canvas/library/testing/`**) and Set B (the by-value closure from `DiagramDefinition`, `DiagramCanvasProps`, `DiagramEventHandlers`, `DiagramModel`) with the TypeScript compiler's `createSourceFile`.
  - Assert the canaries: a floor on names found, `DiagramDefinition` in Set B, `useViewReport` in Set A, and that every surface file parsed.
  - Purpose: the boundary the readme is checked against, computed rather than listed
  - _Leverage: src/client/src/canvas/library/noPrivateGestures.test.ts (the walk-up idiom), typescript 5.7_
  - _Requirements: 2.1, 2.2, 2.3, 6.4_
  - _Prompt: Implement the task for spec module-client-api-readme, first run spec-workflow-guide to get the workflow guide then implement the task: Role: TypeScript developer comfortable with the compiler API | Task: Implement the two-set surface computation of the design's Architecture section and its canaries, exporting it for the test file | Restrictions: Parse, never pattern-match; no type checker, createSourceFile only; a module's own generated payload types are excluded while the six core contracts stay; fail loudly if a named surface file cannot be read | Success: the sets are computed on every run, the canaries fail when a surface file is unreadable, and the counts are reported in the failure message; set the task to [-] before starting, log with log-implementation, then [x]_

- [ ] 3. The readme's skeleton, and how to read it
  - File: docs/diagram-module-client-api.md (new)
  - The sixteen headings in the design's order, the entry shape (`**Declarations:**`, `Source:`), what is authoritative here and what is linked, and the record of the measurement: the test is the command, plus the commit its quoted figures were read at.
  - Purpose: the document's frame, so entries can be filled in without re-deciding its shape
  - _Leverage: docs/creating-a-diagram-module.md (voice and linking style), src/client/src/diagrams/readme.md_
  - _Requirements: 1.1, 2.3, 3.1, 3.2_
  - _Prompt: Implement the task for spec module-client-api-readme, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Technical writer who reads code | Task: Write the readme's frame - the sixteen sections, the entry conventions, the authority statement, the measurement record - with no entries yet | Restrictions: No mermaid yet; no entry content yet; every count stated must say what it counts; link the per-folder readmes rather than restating them | Success: the frame matches the design's heading list exactly and states the entry conventions the test will parse; set the task to [-] before starting, log with log-implementation, then [x]_

- [ ] 4. Entries: registration, the stream and the model
  - File: docs/diagram-module-client-api.md (continue)
  - Sections 3 and 4: `register.ts`, `DiagramCanvasRegistration`, `DiagramClientModule`, the workspace `package.json`, the shared stylesheet import; then `useDiagramStream`, the core `Delta` and `Element` contracts, payload unpacking, `DiagramModel`, `DiagramModelElement`, `DiagramModelConnection`, `useAuth`.
  - Each entry answers the six questions in order, with a `Source:` excerpt from `dependency-graph` where it exercises the aspect.
  - Purpose: the first half of a module client's life, documented
  - _Leverage: src/diagrams/dependency-graph/client/*, src/client/src/diagrams/readme.md_
  - _Requirements: 3.1, 3.3, 4.1, 4.2_
  - _Prompt: Implement the task for spec module-client-api-readme, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Technical writer who reads code | Task: Write the registration and stream/model entries with verbatim excerpts naming their source files | Restrictions: Excerpts are copied, never retyped or tidied; no invented example; state what happens when a part is missing | Success: every declaration in those sections' Declarations lines is in the computed surface, and each excerpt occurs verbatim in its named file; set the task to [-] before starting, log with log-implementation, then [x]_

- [ ] 5. Entries: the definition, field by field
  - File: docs/diagram-module-client-api.md (continue)
  - Section 5: `DiagramDefinition` and one entry per declaration group reached from it - element types and shapes, custom shapes, labels and bindings, anchors, decorations, relation types with routes, markers and constraints, custom routes, background, chrome, toolbox, layout, dragging, snap, extent, drop targets, drag bounds, actions with shortcuts and enablement, `selectable`, `backgroundMenu`, and validation.
  - Every member of every covered interface is named in its entry (Requirement 3.3). **And name `ActionInvocation`'s `{ kind: "menu" }` deliberately**: centralized-selection gave an existing name a new meaning (a menu-invoked action reaches the module as `action-invoked` and never the backend), and a check keyed on declarations and member names cannot demand an entry for a meaning.
  - Purpose: the largest part of the surface, and the one a module writes by hand
  - _Leverage: src/client/src/canvas/library/definition/*, the modules that exercise each aspect (measured, per Requirement 2.4)_
  - _Requirements: 2.4, 3.1, 3.3, 4.1, 4.2, 4.3_
  - _Prompt: Implement the task for spec module-client-api-readme, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Technical writer who reads code | Task: Write one entry per declaration group reachable from DiagramDefinition, naming every member, with a shipped excerpt for each aspect some module exercises | Restrictions: Which module exercises an aspect comes from a parse, never a text search - the requirements record a survey that was wrong on the first claim checked; an aspect no module exercises gets a type-checked example file under canvas/library/examples/ and says so in words | Success: the field-coverage check passes for every covered interface; no entry claims a shipped example that is not one; set the task to [-] before starting, log with log-implementation, then [x]_

- [ ] 6. Entries: the canvas, events, selection, context, toolbox, view report, geometry, styling
  - File: docs/diagram-module-client-api.md (continue)
  - Sections 6 to 13, including selection as `centralized-selection` leaves it (`source`, `selectable`, what a handler receives, and no entry for the names it withdrew), `onActionRefused`, the context channel's resolve-never-reject rule, and the view report linking `src/client/src/diagrams/readme.md`.
  - Purpose: the rest of the surface
  - _Leverage: src/client/src/canvas/library/DiagramCanvas.tsx, canvas/library/api/diagramEvents.ts, shell/context/*, canvas/connectors.ts_
  - _Requirements: 3.1, 3.3, 7.1, 7.3_
  - _Prompt: Implement the task for spec module-client-api-readme, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Technical writer who reads code | Task: Write the canvas, events, selection, context-channel, toolbox, view-report, geometry and styling entries | Restrictions: Do not document the withdrawn selection names as the way to wire a canvas; do not restate the per-folder readmes; keep every excerpt verbatim | Success: the selection entries match the landed centralized-selection surface, and the withdrawn names appear nowhere as guidance; set the task to [-] before starting, log with log-implementation, then [x]_

- [ ] 7. Entries: tests a module writes, the guards, and the internal list
  - File: docs/diagram-module-client-api.md (continue)
  - Section 14: the shared test helpers a module's canvas test must call (`expectLibrarySelection`, and whatever else `canvas/library/testing/` holds), then every test that walks module clients, what it forbids and the shared mechanism instead. Section 16: the library-internal exports, listed by name.
  - Purpose: what a module must not do is part of the API, and the boundary's other side is a list
  - _Leverage: the eight guards named in the requirements, plus centralized-selection's text and behavioural guards_
  - _Requirements: 2.2, 8.1_
  - _Prompt: Implement the task for spec module-client-api-readme, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Technical writer who reads code | Task: Write the test-helper entries, the guards section and the internal-exports list | Restrictions: The guard list is measured from the client's tests, not recalled; the internal list must equal exactly what the surface computation leaves over | Success: the Listed = Internal check passes, and every walking test found by the detection rule is named; set the task to [-] before starting, log with log-implementation, then [x]_

- [ ] 8. The walkthrough, and the diagrams
  - File: docs/diagram-module-client-api.md (continue)
  - Section 15: a minimal module client built end to end, each step linking to its entries. Sections 2 and beside: the five mermaid diagrams of the design's Component 2.
  - Purpose: the path a reader follows the first time, and the pictures that make the parts fit
  - _Leverage: src/diagrams/dependency-graph/client/* as the worked path_
  - _Requirements: 3.4, 5.1_
  - _Prompt: Implement the task for spec module-client-api-readme, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Technical writer who reads code | Task: Write the end-to-end walkthrough and the five mermaid diagrams | Restrictions: Diagrams name only declarations that exist; mermaid appears in the readme only, never in the spec documents; the walkthrough links entries rather than repeating them | Success: a reader can follow the walkthrough from an empty client folder to a mounted canvas, and every API name in a diagram is in the computed surface; set the task to [-] before starting, log with log-implementation, then [x]_

- [ ] 9. The test's five checks, each seen to fail first
  - File: src/client/src/diagramModuleClientApi.test.ts (continue from task 2)
  - Coverage and field coverage; use outside the surface; stale entries and the internal-list equality; verbatim excerpts; diagram identifiers and the guard list. Plus check 6, mermaid parse, if task 1 showed it can run.
  - Plant one defect per check and record the observed failure before trusting it: an entry's name deleted, a module importing an undocumented name, an entry naming a removed declaration, an excerpt edited in its source, an undocumented walking test added, a broken diagram.
  - Purpose: the readme is held to the code by a gate, not by review
  - _Leverage: src/client/src/diagramModuleClientApi.surface.ts from task 2_
  - _Requirements: 4.4, 5.2, 6.1, 6.2, 6.3, 6.4, 6.5, 8.2_
  - _Prompt: Implement the task for spec module-client-api-readme, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Client developer writing repository guards | Task: Implement the five (or six) checks, each collecting every offender and failing once naming them all, then plant one defect per check and record each observed failure in the implementation log | Restrictions: No check may pass when its input is missing - an unreadable readme or surface file fails; the walking-test detection rule must be calibrated to find every known guard, and the calibration recorded; remove every planted defect afterwards and confirm the tree is clean | Success: the log shows each check failing against its planted defect and green afterwards, with the canaries in place; set the task to [-] before starting, log with log-implementation, then [x]_

- [ ] 10. Make the readme the one home: the walkthrough, the link guard, the steering rule
  - File: docs/creating-a-diagram-module.md, src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/DocumentationLinks.Tests.cs, .spec-workflow/steering/processes.md
  - Shrink *The client* to a summary plus a link; reduce the client halves of *Renaming in place* and *The view-delta loop* to a sentence and a link each; add the readme to `DocumentationLinksTests`' document list; name it in *Keeping documentation true*.
  - Purpose: two documents describing one API is how they come to disagree
  - _Leverage: docs/creating-a-diagram-module.md, DocumentationLinks.Tests.cs_
  - _Requirements: 1.2, 1.3, 1.4_
  - _Prompt: Implement the task for spec module-client-api-readme, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Technical writer who reads code | Task: Reduce the walkthrough's client material to summaries and links, add the readme to the link guard's document list, and record it in processes.md Keeping documentation true | Restrictions: Remove no backend material; leave no API detail in two places; the processes.md change is a spec-workflow edit and goes in its own commit | Success: the walkthrough names what the client half is and links for how; DocumentationLinksTests covers the readme and passes; set the task to [-] before starting, log with log-implementation, then [x]_

- [ ] 11. See the diagrams render, and close the specification
  - File: (no source change) implementation log
  - Open the readme on GitHub's file view and in Rider's markdown preview with its Mermaid extension, confirm all five diagrams render, and record it. State in the readme what a parse cannot catch: a diagram that parses but shows the wrong flow.
  - Purpose: the one criterion a person owns, done and recorded rather than assumed
  - _Leverage: the repository's GitHub remote_
  - _Requirements: 5.3, 5.4_
  - _Prompt: Implement the task for spec module-client-api-readme, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Reviewer | Task: Confirm the five diagrams render on GitHub and in Rider, record it in the implementation log, and make sure the readme states the limit of the parse check | Restrictions: Do not claim a render nobody looked at; if a diagram does not render somewhere, fix it and say where it failed | Success: the log names both places, the date, and the five diagrams; set the task to [-] before starting, log with log-implementation, then [x]_
