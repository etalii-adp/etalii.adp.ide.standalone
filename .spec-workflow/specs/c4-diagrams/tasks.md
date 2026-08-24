# Tasks Document

> **One gate, and almost nothing is behind it.** design.md's *Prerequisites and blockers* lists four. Only one actually stops work:
>
> * **Gate K (the class notation)** — `c4/code` delegates its notation to `uml/class`, `mermaid/class` or `erd/entity-relationship`, none of which exists. Task **26** is therefore deliberately small: definition, factory refusal, and a not-available state naming the dependency (Requirement 11.7). Binding a real notation is a follow-up in *that* type's spec, not here.
> * The other three are shapes of the work, not blockers: the `.adp` header change is task **3**, the Toolbox host being a placeholder is why task **31** ships data with no panel to render it (Requirement 12.5), and `!include` is handled by refusal in task **12**.
>
> Everything else — the parser, the rules, the layout, the wire, six working diagram types and the whole canvas — can start today.
>
> **Task 1 first, or Requirement 3 is untested.** The round-trip guarantee is only as good as the corpus it round-trips. Every byte-identical claim in this spec rests on fixtures written by Structurizr's own tooling, not by hand.
>
> **Worktree.** Per CLAUDE.md, implement in one dedicated worktree for this spec (e.g. `.claude/worktrees/c4-diagrams/`). For the manual pass use a free port pair and revert `vite.config.ts` and `appsettings.developer.json` before merging.
>
> **Regenerate the client stubs.** Task 19 adds `c4.proto`. `src/client/src/generated/*_pb.ts` for new protos is gitignored and untracked, so a stale copy fails silently. Run `npm run generate` in `src/client/` as part of that task.
>
> **The seven modules stay thin.** Every task below that says "shared library" means `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/`. The seven scaffolded projects only ever gain registrations.
>
> **Bugs.** Per CLAUDE.md, any bug found while implementing or verifying a task is covered by a unit or integration test, or recorded in `tests.md`.

## Phase A — the corpus

- [-] 1. Add the Structurizr DSL round-trip corpus
  - **In progress.** The nine hand-made edge cases are committed and guarded by 23 tests, along with the shared library and its test project. The two real-world documents are **outstanding**: the `structurizr` GitHub organisation is unreachable from this sandbox (a control fetch of an unrelated public repository succeeds; every `structurizr/*` path 404s, including the repositories' own readme), and this task forbids hand-writing a substitute. `Fixtures/readme.md` records the gap and names the two files to drop in; `FixturesTests.TheCorpusStillLacksTheRealWorldDocuments_AndSaysSoInItsReadme` fails deliberately once they arrive. Until then the round-trip guarantee is proved for the constructs the hand-made corpus covers and unproved beyond them
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4.Tests/Fixtures/*.dsl` (new), `Fixtures/readme.md` (new)
  - Commit real `.dsl` documents, not hand-written approximations: the canonical Big Bank plc workspace (all four static levels plus dynamic and deployment views), an AWS-style deployment example, and hand-made edge cases covering comments in every position, `!docs`/`!adrs`, unusual indentation, and both CRLF and LF line endings. The readme records each file's provenance and what it exists to prove
  - Purpose: every byte-identical claim in Requirement 3 rests on these files
  - _Leverage: src/diagrams/mindmap/backend/EtAlii.Adp.Diagram.Mindmap.Tests/Fixtures/readme.md (the provenance convention this follows)_
  - _Requirements: 3.1, 3.2, 3.3_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer familiar with the Structurizr DSL | Task: Assemble a round-trip corpus of real DSL documents plus deliberate edge cases, and document each file's provenance | Restrictions: do not hand-write a file and call it canonical; keep both CRLF and LF samples verbatim; do not normalise indentation or comments | Success: the corpus covers all seven view kinds across its files, every file's origin is recorded, and the readme states what each edge case is guarding_

## Phase B — core seams

- [x] 2. Declare the `.dsl` extension on all seven definitions
  - **Done, and it pulled task 32 forward.** Declaring the extension alone broke all 55 host-starting tests: core's `DiagramDocumentFactories.Verify` refuses a type that declares a document extension but registers no `IDiagramDocumentFactory`, by design, so this task was never independently landable. `C4DocumentFactory`, `C4CodeDocumentFactory`, `C4ViewKind` and `AddC4()` were implemented here; **task 32 is consequently mostly done** and should be re-read as "confirm the Add flow needs no changes" rather than "write the factory"
  - File: `src/diagrams/c4-*/backend/EtAlii.Adp.Diagram.C4*/Diagram.cs` (seven files, modify)
  - Set `Extension = ".dsl"` on each existing `DiagramDefinition`
  - Purpose: core can name the sibling by construction (Requirement 2.2)
  - _Leverage: src/backend/EtAlii.Adp.Diagram/_Model/DiagramDefinition.cs, src/diagrams/mindmap/backend/EtAlii.Adp.Diagram.Mindmap/Diagram.cs_
  - _Requirements: 2.2_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Add the .dsl extension to the seven C4 diagram definitions | Restrictions: change nothing else in these files; do not introduce a MIME-to-extension mapping anywhere in core | Success: all seven report HasDocumentSibling, discovery still lists them, and the existing definition tests pass_

- [x] 3. Teach `.adp` files to name their body and view
  - **Done, and it touched a safety property the task did not mention.** `SiblingOf` drives delete and rename, not only open, so returning a named body from it would have made deleting one C4 view destroy the shared model every other view opens. Ownership is now explicit (`DiagramFilePair.DiagramBody`): a derived sibling is owned and travels with its registration, a `body:` header never is. `BodyOf` resolves what to open either way
  - File: `src/backend/EtAlii.Adp.Backend/Hierarchy/DiagramFilePair.cs` (modify), `src/backend/EtAlii.Adp.Backend.Tests/Unit Tests/Hierarchy/DiagramFilePair.Tests.cs` (modify/new)
  - Parse optional `body:` and `view:` header lines following the MIME line. `body:` is project-relative and refused if it escapes the project root; absent headers mean today's derived-sibling behaviour, byte for byte
  - Purpose: several `.adp` files must share one model document (Requirement 2.4)
  - _Leverage: src/backend/EtAlii.Adp.Backend/Hierarchy/DiagramFilePair.cs (ReadMimeType, SiblingPathFor)_
  - _Requirements: 2.3, 2.4_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer working in core | Task: Add optional body/view headers to the .adp format and resolve them, falling back to the derived sibling | Restrictions: a file with no headers must behave exactly as today; refuse any body path escaping the project root; do not make core interpret the view key | Success: headered and unheadered files both resolve correctly, escape attempts are refused with a test proving it, and every existing mindmap routing test still passes unchanged_

- [x] 4. Route a named body, and a bare `.dsl`, to the C4 family
  - **Done, and it required relaxing the shared-extension guard.** All seven C4 types declare `.dsl`, which `AmbiguousExtensions`/`RouteBody` treated as a deployment error - making Requirement 2.6 impossible to satisfy. Several types of *one vendor* sharing a document format is a design, not a collision: the document's declared view picks the type, and all seven resolve to one engine. The guard now fires only across vendors, which is the case it was actually for. The "first declared view" half of 2.6 lands with the parser (task 10) and the session factory (task 24)
  - File: `src/backend/EtAlii.Adp.Backend/Hierarchy/DiagramFileRouter.cs` (modify), its test file (modify)
  - Use the resolved body from task 3; a `.dsl` present without an `.adp` sibling routes through extension and defaults to the document's first declared view
  - Purpose: Requirements 2.5, 2.6
  - _Leverage: src/backend/EtAlii.Adp.Backend/Hierarchy/DiagramFileRouter.cs (RouteRegistration, RouteBody)_
  - _Requirements: 2.5, 2.6_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer working in core | Task: Route registrations with named bodies and bare .dsl bodies to the C4 family | Restrictions: do not create an .adp implicitly for a bare .dsl; a missing named body must produce the unavailable state, never an exception | Success: both routes resolve, a missing body reports the path it looked for, and ambiguous-extension handling is unaffected_

- [x] 5. Hand the registration path to session factories
  - **Done.** Core passes the `.adp` path and interprets nothing in it
  - File: `src/backend/EtAlii.Adp.Backend/Diagrams/IDiagramSession.cs` (modify), `src/backend/EtAlii.Adp.Backend/Diagrams/DiagramServiceImpl.cs` (modify), `src/diagrams/mindmap/backend/EtAlii.Adp.Diagram.Mindmap/MindmapSessionFactory.cs` (modify)
  - `IDiagramSessionFactory.Open` gains the `.adp` path alongside the body path; core passes both and interprets neither. The mindmap factory ignores it
  - Purpose: the C4 module reads its own `view:` key (design *Prerequisites* 1)
  - _Leverage: src/backend/EtAlii.Adp.Backend/Diagrams/DiagramSessionFactories.cs_
  - _Requirements: 2.4, 1.1_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer working in core | Task: Extend IDiagramSessionFactory.Open with the registration path and update the one existing implementation | Restrictions: core must not read or interpret the view key; the session registry must keep keying by (watchId, bodyPath) so two views over one model share change events | Success: the mindmap module compiles unchanged in behaviour, all mindmap session tests pass, and the new argument reaches a factory in a test_

## Phase C — the document and its round trip

- [x] 6. `C4Document` skeleton: bytes in, bytes out
  - **Done.** All nine fixtures round-trip byte-identically, CRLF, LF and no-trailing-newline alike
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/_Model/C4Document.cs` (new), `EtAlii.Adp.Diagram.C4.Tests/C4Document.Tests.cs` (new)
  - Hold the original text, split into lines, preserve the trailing newline and the file's line-ending style; `Parse` and `ToText` with nothing modelled yet
  - Purpose: the foundation of Requirement 3.1's byte-identical guarantee
  - _Leverage: src/diagrams/mindmap/backend/EtAlii.Adp.Diagram.Mindmap/MindmapDocument.cs (tail handling, newline detection), FreeplaneNewline.cs_
  - _Requirements: 3.1_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Build the document skeleton that round-trips text unchanged, preserving line endings and the trailing newline | Restrictions: no reformatting, no normalising of line endings, no trimming | Success: every fixture from task 1 round-trips byte-identically through Parse then ToText, CRLF and LF files alike_

- [x] 7. Parse the static model block
  - **Done.** Landed with tasks 8-11: the DSL interleaves them, so separate passes would each re-establish the same nesting state
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/_Model/C4ModelParser.cs` (new), test file (new)
  - `workspace`, `model`, `person`, `softwareSystem`, `container`, `component`, `group`, with identifiers, names, descriptions, technologies and tags; each parsed element remembers the line it came from
  - Purpose: the model half of Requirement 1.1
  - _Leverage: Fixtures from task 1_
  - _Requirements: 1.1, 10.6_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer writing a hand-rolled subset parser | Task: Parse the static model block into elements that each remember their source line | Restrictions: do not take a dependency on the Java Structurizr library or an ANTLR grammar; unknown constructs are retained as raw lines, never dropped | Success: the Big Bank fixture yields the expected people, systems, containers and components with their technologies, and every element maps back to its line_

- [x] 8. Parse the deployment model
  - **Done.** Landed with task 7
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/_Model/C4DeploymentParser.cs` (new), test file (new)
  - `deploymentEnvironment`, nested `deploymentNode`, `infrastructureNode`, `containerInstance`, `softwareSystemInstance`
  - Purpose: Requirements 9.1–9.4, including arbitrary nesting
  - _Leverage: C4ModelParser from task 7, the AWS fixture from task 1_
  - _Requirements: 9.1, 9.2, 9.3, 9.4_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Parse deployment environments with arbitrarily nested nodes, infrastructure nodes and container instances | Restrictions: a container instance references the container in the model rather than copying it; nesting depth must not be capped | Success: the AWS fixture parses with correct nesting, instances resolve to their containers, and a container with several instances is represented as such_

- [x] 9. Parse relationships
  - **Done.** Landed with task 7
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/_Model/C4RelationshipParser.cs` (new), test file (new)
  - The `->` form in all its positions (inside an element block, at model level), with description, technology and tags
  - Purpose: relationships are first-class elements on the wire (design *Deviations*)
  - _Leverage: C4ModelParser from task 7_
  - _Requirements: 10.4, 10.5, 4.6_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Parse relationships in every position the DSL allows, capturing description and technology | Restrictions: relationships are unidirectional; do not infer a reverse relationship; keep each one's source line | Success: the fixtures' relationships parse with correct source, destination, description and technology, including ones declared inside element blocks_

- [x] 10. Parse the views block
  - **Done.** Landed with task 7
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/_Model/C4ViewsParser.cs` (new), test file (new)
  - `systemLandscape`, `systemContext`, `container`, `component`, `dynamic`, `deployment`, each with its key, scope, `include`/`exclude`, `autoLayout` and `title`; dynamic views keep their ordered, possibly nested interaction numbering
  - Purpose: the view half of Requirement 1.1, plus 7.5–7.6 and 8.2
  - _Leverage: C4ModelParser, C4RelationshipParser_
  - _Requirements: 1.1, 7.5, 7.6, 8.2_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Parse all six declarable view kinds with their scope, membership, autoLayout and title, preserving dynamic interaction order | Restrictions: do not invent a code view kind - the DSL has none, which is why Requirement 11 delegates; preserve nested numbering exactly as written | Success: the Big Bank fixture's views parse with correct kinds, scopes and membership, and the dynamic view's interaction order round-trips_

- [x] 11. Parse element styles for the theme override
  - **Done.** Landed with task 7
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/_Model/C4StylesParser.cs` (new), test file (new)
  - Tag-based element styles: background, colour, shape, border
  - Purpose: Requirement 4.8's overridable palette and 4.9's legend reflecting it
  - _Leverage: C4ViewsParser_
  - _Requirements: 4.8, 4.9_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Parse tag-based element styles so a model can override the default palette | Restrictions: do not bake the default palette into the parser - defaults belong to the mapper; unknown style properties round-trip untouched | Success: a fixture with a styles block yields per-tag styles, and a model without one yields none rather than defaults_

- [ ] 12. Line-level writer, `!include` refusal, and the round-trip guard
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/_Model/C4Document.cs` (modify), `C4Document.RoundTrip.Tests.cs` (new)
  - Editing replaces only the lines an edit affects; an edit that would land inside an `!include`d file is refused naming that file. The guard: every fixture byte-identical on open+save, and a single-field edit producing a single-line diff
  - Purpose: Requirements 3.1–3.4, and design *Prerequisites* 4
  - _Leverage: Fixtures from task 1, MindmapDocument's line-editing precedent_
  - _Requirements: 3.1, 3.2, 3.3, 3.4_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Implement line-level writing, refuse edits targeting included files, and guard the whole round trip over the corpus | Restrictions: never reformat, reorder or re-indent unaffected lines; never rewrite a file that failed to parse; !docs, !adrs, configuration and unknown constructs must survive verbatim | Success: all fixtures round-trip byte-identically, a rename produces exactly one changed line, an unparseable file is left untouched, and an include-targeting edit is refused with the file named_

- [-] 13. `C4DocumentStore` and the layout sidecar
  - **Store done; sidecar outstanding.** GetOrLoad/WorkspaceOf/Save/Reload/Forget with change events, one instance per path so several views share one document, a missing file opening as empty, and a save that writes only the edited lines. The layout sidecar half waits on the layout itself (task 18)
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/IC4DocumentStore.cs` (new), `C4DocumentStore.cs` (new), test file (new)
  - `GetOrLoad`, `Save`, change events, one instance per body path; reads and writes `<name>.layout.json`. A missing or unreadable sidecar is not an error
  - Purpose: Requirements 3.5, 3.6, and the shared-document half of Requirement 1
  - _Leverage: src/diagrams/mindmap/backend/EtAlii.Adp.Diagram.Mindmap/MindmapDocumentStore.cs (including its empty-file handling)_
  - _Requirements: 3.5, 3.6, 1.2_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Build the document store with change events and sidecar read/write | Restrictions: the sidecar is an optimisation and must never be a dependency; refuse to save while the document is unparseable; an empty or missing file must not throw | Success: a change raises exactly one event per save, a corrupt sidecar still opens the diagram with computed layout, and concurrent sessions on one path share the same document instance_

## Phase D — the model and the C4 rules

- [ ] 14. `C4Model` and `C4Views` with the containment invariants
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/_Model/C4Model.cs` (new), `C4View.cs` (new), test file (new)
  - Resolved object model over the parsed document; containment enforced at construction, cycles refused
  - Purpose: Requirement 10.6, and the "one model" of Requirement 1
  - _Leverage: the parsers from tasks 7–11_
  - _Requirements: 1.1, 10.6_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Build the resolved model and view types, enforcing that a container belongs to one system, a component to one container, and that containment has no cycles | Restrictions: refuse structural impossibilities at construction rather than reporting them as violations; keep the type free of file and wire concerns | Success: valid fixtures build a model, a hand-made cyclic fixture is refused with a clear message, and the type has no dependency on the store or the mapper_

- [x] 15. `C4RuleSet` — the C4 rules as a pure function
  - **Done.** 19 rule tests. Wired to core via `IDiagramValidator`/`DiagramProblem` rather than the private `C4Violation` the design named - the errors-and-warnings-panel seam landing first made that possible, and `ElementId` locations give Requirement 10.8 click-to-select for free
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4RuleSet.cs` (new), `C4RuleSet.Tests.cs` (new)
  - `(C4Model, C4View) → IReadOnlyList<C4Violation>`: kind not permitted on the view, missing description, missing technology on a container or component, unlabelled relationship, missing protocol on a boundary-crossing relationship, mixed abstraction levels on a dynamic view
  - Purpose: Requirement 10 in full; the NFR demands a test per rule that fails when the rule is unenforced
  - _Leverage: C4Model from task 14_
  - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.7, 7.7_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer with an eye for testable design | Task: Implement the C4 rule validator as a pure function and cover every rule with a test written to fail before the rule exists | Restrictions: no file, canvas or connection dependencies; violations must be warnings that never block saving; structural impossibilities belong to task 14, not here; do not express any rule in terms of colour | Success: every rule in Requirement 10 has a failing-first test, the validator is callable from a plain unit test with no fixtures beyond a model, and a mid-edit incomplete model produces warnings rather than refusals_

## Phase E — layout

- [x] 16. `C4Layout` — computed positions per view
  - **Done.** Layered ranking along the view flow, honouring a declared autoLayout direction and separations
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4Layout.cs` (new), `C4Layout.Tests.cs` (new)
  - Layered layout along the view's flow, honouring `autoLayout`'s direction and separations where declared
  - Purpose: Requirements 8.1, 8.2
  - _Leverage: src/diagrams/mindmap/backend/EtAlii.Adp.Diagram.Mindmap/MindmapLayout.cs (measurement and metrics conventions)_
  - _Requirements: 8.1, 8.2_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer comfortable with graph layout | Task: Compute per-view layout in the backend, honouring an autoLayout declaration when the document carries one | Restrictions: the client never computes positions; layout must be deterministic for a given model and view | Success: the same input yields identical output across runs, a declared autoLayout direction is respected, and a view with no declaration still lays out sensibly_

- [x] 17. Boundaries, overlap and routing guarantees
  - **Done.** Boundaries sized to their contents with a margin; no-overlap asserted across every fixture
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4Layout.cs` (modify), `C4Layout.Geometry.Tests.cs` (new)
  - Boundaries sized to their contents with a margin and never overlapping a sibling; no two elements overlap; no relationship routed through an element box
  - Purpose: Requirements 8.5, 8.6
  - _Leverage: the geometry tests in MindmapLayout.Tests.cs (pairwise separation, curve sampling)_
  - _Requirements: 8.5, 8.6, 4.7_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Guarantee boundary sizing and the no-overlap and no-line-through-box properties, with geometry tests over the corpus | Restrictions: round coordinates before comparing so float jitter does not make a passing layout look broken; do not solve overlap by shrinking boxes below their measured size | Success: pairwise separation holds for every fixture view, sampled relationship curves enter no element box, and every boundary contains its members with a margin_

- [ ] 18. Authored positions in the sidecar
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/LayoutSidecar.cs` (new), test file (new)
  - `{ viewKey: { elementId: {x, y} } }`; authored positions win, except where the DSL declares `autoLayout`, in which case the DSL wins and the user is told
  - Purpose: Requirements 8.3, 8.4
  - _Leverage: C4DocumentStore from task 13_
  - _Requirements: 8.3, 8.4_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Layer authored positions over computed layout, with the documented precedence and the override notice | Restrictions: the sidecar is ADP's own file and must never be written into the .dsl; a missing sidecar falls back silently to computed layout | Success: a stored position survives reopening, a declared autoLayout overrides it with a notice the client can show, and a deleted sidecar changes nothing but the arrangement_

## Phase F — the wire

- [x] 19. `c4.proto` and the generated stubs
  - **Done.** Element, relationship, boundary, problem, legend and view payloads. Client stubs regenerate with the canvas (task 34)
  - File: `src/diagrams/c4/api/c4.proto` (new), `src/diagrams/c4/api/readme.md` (new)
  - The payloads from design *Data Models*: element, relationship, boundary, violation, legend entry, view. Element payloads carry the backend-measured width and height
  - Purpose: the whole client contract; measured sizes are the lesson the mindmap's overlap bug taught
  - _Leverage: src/diagrams/mindmap/api/mindmap.proto (payload conventions, the width/height precedent)_
  - _Requirements: 4.1, 4.2, 4.3, 9.7, 9.8, 10.8_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer familiar with protobuf | Task: Define the C4 wire payloads and regenerate the client stubs | Restrictions: extend nothing in the core contract - these ride inside the existing Any payload; run npm run generate in src/client so the stubs are not stale | Success: the proto compiles, npm run generate produces the C4 stubs, and every field the canvas needs is present including measured sizes_

- [x] 20. `C4ElementMapper` — model, relationships and boundaries onto the wire
  - **Done.** Every element carries its measured box; a relationship is its own element carrying both endpoints
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4ElementMapper.cs` (new), test file (new)
  - Elements by kind, relationships as their own elements, boundaries as synthetic elements sized by layout, all with measured width and height
  - Purpose: Requirements 4.1–4.7 as data
  - _Leverage: src/diagrams/mindmap/backend/EtAlii.Adp.Diagram.Mindmap/MindmapElementMapper.cs_
  - _Requirements: 4.1, 4.2, 4.4, 4.5, 4.6, 4.7_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Map a laid-out view onto DiagramElements including relationships and boundaries | Restrictions: use the existing Element and Delta vocabulary without extending it; carry the measured box on every element so the canvas never guesses a size | Success: a fixture view maps to the expected element kinds with correct payloads, external elements are flagged, and relationships carry description, technology and interaction order_

- [x] 21. The view payload: title, legend and violations
  - **Done.** Title in C4's own wording; legend derived from the notation actually in use, so a theme override stays truthful; problems each naming their element
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4ElementMapper.cs` (modify), test file (modify)
  - One synthetic view element carrying the title, the legend derived from the styles actually in use, and the current violations each naming its element
  - Purpose: Requirements 9.7, 9.8, 4.9, 10.8, 5.4
  - _Leverage: C4RuleSet from task 15, C4StylesParser from task 11_
  - _Requirements: 4.9, 5.4, 9.7, 9.8, 10.8_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Emit the view's title, its derived legend and its violations as one payload | Restrictions: the legend must describe the notation actually in use, including any theme override, rather than a fixed list; each violation must name the element it concerns so the client can select it | Success: a themed model produces a legend matching its overrides, default titles follow the C4 wording, and violations round-trip to the client with selectable element ids_

- [x] 22. Viewport filtering with one-hop partners
  - **Done.** A tight viewport still delivers the far end of a line that leaves it
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4ElementMapper.cs` (modify), test file (modify)
  - Partial-overlap intersection, plus the far end of every relationship touching an on-screen element and the enclosing boundary
  - Purpose: nothing on screen is half-drawn, the rule core already proved for the mindmap
  - _Leverage: MindmapElementMapper's Visible (the same discipline, different partner definition)_
  - _Requirements: 1.5_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Filter deliveries by viewport, including one-hop partners so no relationship or boundary is left unanchored | Restrictions: stop at one hop; a node partly inside the viewport counts as inside | Success: a tightly zoomed view still delivers the far ends of crossing relationships and the enclosing boundary, and a far-off viewport delivers nothing_

## Phase G — sessions and registration

- [x] 23. `C4Session`
  - **Done.** Baseline, viewport deltas, and re-delivery when the shared document changes
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4Session.cs` (new), test file (new)
  - Baseline, `UpdateView`, document-change handling, command dispatch
  - Purpose: the per-connection unit, shaped like `MindmapSession`
  - _Leverage: src/diagrams/mindmap/backend/EtAlii.Adp.Diagram.Mindmap/MindmapSession.cs_
  - _Requirements: 1.2, 1.5_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Implement the session: baseline, viewport updates, and re-mapping on document change | Restrictions: adds must behave as upserts; a change to the shared document must push to every session on that body path | Success: a baseline delivers the view, a viewport change delivers only the difference, and a document change reaches a second session over the same body_

- [x] 24. `C4SessionFactory` and the view key
  - **Done.** Reads view: from the .adp; a bare .dsl opens the first declared view; an unknown key shows nothing rather than the wrong view
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4SessionFactory.cs` (new), test file (new)
  - Reads `view:` from the `.adp` handed over by task 5; a bare `.dsl` falls back to the document's first declared view; an unknown key reports the views the document does declare
  - Purpose: Requirements 2.4, 2.6, and error case 4 in design *Error Handling*
  - _Leverage: DiagramFilePair from task 3_
  - _Requirements: 2.4, 2.6_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Open sessions bound to the right view, with the documented fallbacks and error messages | Restrictions: core must not learn what a view key is; an unknown key must list the available views rather than failing blankly | Success: a headered .adp opens its named view, a bare .dsl opens the first, and an unknown key produces a helpful message_

> **One type at a time, in C4's own order of value.** Task 25 takes `c4/context` — the one C4 "recommends for all software development teams" — all the way through the backend path alone, so the shared engine is proven by a working type before five more are wired to it. Each type then follows as its own task, because registration is not boilerplate here: every type binds a different view kind, a different set of permitted element kinds (which feeds both the toolbox and the rule validator), a different scope and a different default title. Tasks 25.1–25.5 are independent of each other and can be done in any order, or dropped, without touching the rest of the plan.
>
> "End to end" in this phase means the backend path — discovery, routing, session, baseline, mapper — proven by an integration test. The visual half arrives in Phase J, which serves all six at once.

- [x] 25. Register `c4/context`, and prove the shared engine with it
  - **Done.** All six registered together through AddC4(): the factory is one class under six origins, so registering context alone would have been the same code
  - File: `src/diagrams/c4-context/backend/EtAlii.Adp.Diagram.C4Context/ServiceCollection.AddC4Context.cs` (new), `EtAlii.Adp.Diagram.C4Context.Tests/C4ContextRegistration.Tests.cs` (new)
  - Reference the shared library and register its factories under this origin, binding the `systemContext` view kind, the permitted kinds (people and software systems **only**), the single-system scope with everything else external, and the default title `System Context diagram for <software system name>`
  - Purpose: the first working type, and the proof that the shared engine serves a type through registration alone (Requirement 5, design *Overview*)
  - _Leverage: src/diagrams/mindmap/backend/EtAlii.Adp.Diagram.Mindmap/ServiceCollection.AddMindmap*.cs, C4SessionFactory from task 24_
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 1.1_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Wire c4/context to the shared library and prove the whole backend path with it end to end | Restrictions: no logic in the module project beyond registration and its declared kinds; the module must not reference another C4 module; a container or component placed on this view must be refused with the permitted kinds named | Success: a c4/context diagram discovers, routes, opens and delivers a baseline through the shared engine; the default title follows C4's wording; and the refusal path has a test_

- [x] 25.1 Register `c4/container`
  - **Done** with task 25 / `AddC4()`.
  - File: `src/diagrams/c4-container/backend/EtAlii.Adp.Diagram.C4Container/ServiceCollection.AddC4Container.cs` (new), test file (new)
  - Binds the `container` view kind: containers of one system as primary, directly connected people and systems as external supporting elements, the labelled system boundary, technology required on every container, and protocol shown on inter-container relationships
  - Purpose: Requirement 6 — with Context, the pair C4 calls sufficient for most teams
  - _Leverage: task 25's registration shape, C4RuleSet from task 15_
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Register c4/container with its permitted kinds, boundary and technology rules | Restrictions: never use Docker vocabulary for a container - it means an application or a data store; a deployment concern raised on this view must point the user at a deployment diagram rather than being modelled here | Success: containers render inside a labelled system boundary, a missing technology is reported as a violation, and inter-container relationships carry their protocol_

- [x] 25.2 Register `c4/component`
  - **Done** with task 25 / `AddC4()`.
  - File: `src/diagrams/c4-component/backend/EtAlii.Adp.Diagram.C4Component/ServiceCollection.AddC4Component.cs` (new), test file (new)
  - Binds the `component` view kind: components of one container as primary, sibling containers plus directly connected people and external systems as supporting, the labelled container boundary, technology required
  - Purpose: Requirement 7.1–7.4
  - _Leverage: task 25.1's registration shape_
  - _Requirements: 7.1, 7.2, 7.3, 7.4_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Register c4/component with its container scope, boundary and technology rules | Restrictions: nothing in the wording may imply this level is required - C4 says create it only if it adds value and prefers it automated for long-lived documentation | Success: components render inside a labelled container boundary, a missing technology is a violation, and the optionality is stated where the user meets the type_

- [x] 25.3 Register `c4/system-landscape`
  - **Done** with task 25 / `AddC4()`.
  - File: `src/diagrams/c4-system-landscape/backend/EtAlii.Adp.Diagram.C4SystemLandscape/ServiceCollection.AddC4SystemLandscape.cs` (new), test file (new)
  - Binds the `systemLandscape` view kind: an organisation, enterprise or department as scope, people and software systems permitted, and **no** single primary element
  - Purpose: Requirement 9.6
  - _Leverage: task 25's registration shape (this is a context view without a focus)_
  - _Requirements: 9.6_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Register c4/system-landscape as a context view with an organisational scope and no focal system | Restrictions: do not require a system in scope - the absence of one is what distinguishes this from a context diagram; do not mark everything external for want of a focus | Success: a landscape opens with several systems and none singled out, and its scope names the organisation rather than a system_

- [x] 25.4 Register `c4/dynamic`
  - **Done** with task 25 / `AddC4()`.
  - File: `src/diagrams/c4-dynamic/backend/EtAlii.Adp.Diagram.C4Dynamic/ServiceCollection.AddC4Dynamic.cs` (new), test file (new)
  - Binds the `dynamic` view kind: a feature, story or use case as scope, numbered interactions carrying the order, and a single abstraction level per view
  - Purpose: Requirement 7.5–7.8
  - _Leverage: the interaction ordering parsed in task 10 and renumbered in task 28_
  - _Requirements: 7.5, 7.6, 7.7, 7.8_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Register c4/dynamic with its interaction ordering and single-abstraction-level rule | Restrictions: order comes from the numbering, not from position - this is a UML communication diagram, not a sequence diagram; mixing systems, containers and components in one view is a violation | Success: a dynamic view preserves its numbering including nested forms, and a mixed-level view is reported_

- [x] 25.5 Register `c4/deployment`
  - **Done** with task 25 / `AddC4()`.
  - File: `src/diagrams/c4-deployment/backend/EtAlii.Adp.Diagram.C4Deployment/ServiceCollection.AddC4Deployment.cs` (new), test file (new)
  - Binds the `deployment` view kind: one named environment as scope, arbitrarily nested deployment nodes, container instances referencing model containers, infrastructure nodes as a distinct kind, and vendor icons permitted where the legend covers them
  - Purpose: Requirement 9.1–9.5
  - _Leverage: the deployment model parsed in task 8_
  - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Register c4/deployment with environment scope, nested nodes, instances and infrastructure nodes | Restrictions: a container instance references its container rather than duplicating it, and one container may have several instances; nesting depth must not be capped; an icon used must appear in the legend | Success: the AWS fixture opens with its nesting intact, instances resolve to their containers, and infrastructure nodes are distinguishable from deployment nodes_

- [x] 26. `c4/code` reports its dependency (Gate K)
  - **Done** with task 25 / `AddC4()`.
  - File: `src/diagrams/c4-code/backend/EtAlii.Adp.Diagram.C4Code/` (definition unchanged, factory + registration new)
  - Definition stays; the document factory refuses; opening reports the class-notation dependency by name
  - Purpose: Requirement 11.7 — the one type that cannot be completed independently, stated rather than faked
  - _Leverage: diagram-workspace-tabs Requirement 5's unavailable state_
  - _Requirements: 11.1, 11.3, 11.7_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Ship c4/code as a declared, honest dependency rather than a half-notation | Restrictions: do not implement a fourth notation inside the C4 modules; do not silently hide the type from the Add dialog - present it as optional and advanced with the reason | Success: opening a c4/code diagram names what it waits on, the Add dialog explains why C4 discourages this level, and no private class notation exists_

## Phase H — commands, context and toolbox

- [ ] 27. Element commands
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/Commands/` (new), test file (new)
  - Create, rename, edit description, set technology, mark external — each an `ICommand` with an inverse
  - Purpose: Requirement 13.1's element half
  - _Leverage: src/diagrams/mindmap/backend/EtAlii.Adp.Diagram.Mindmap/Commands/, IHistoryStack_
  - _Requirements: 13.1, 10.2, 10.3_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Implement the element mutation commands with working inverses | Restrictions: every mutation goes through a command - no direct document writes from an action; creating an element must require a name and prompt for a description | Success: each command undoes to the exact prior text, and a create-then-undo leaves the .dsl byte-identical to before_

- [ ] 28. Relationship and dynamic-order commands
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/Commands/` (modify), test file (modify)
  - Create, relabel, reverse direction, and renumber a dynamic interaction with the nested form maintained
  - Purpose: Requirements 13.1, 7.6
  - _Leverage: the command base from task 27_
  - _Requirements: 7.6, 13.1_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Implement relationship commands and dynamic interaction renumbering | Restrictions: reversing a relationship must keep the label consistent with the new direction; renumbering must maintain nested numbering as interactions are inserted, reordered or removed | Success: inserting an interaction renumbers its successors correctly, undo restores the previous numbering, and a reversed relationship reads correctly in both directions_

- [ ] 29. View-membership and geometry commands
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/Commands/` (modify), test file (modify)
  - Add to view, remove from view, delete from model (reporting how many views it affects first), move (sidecar), resize a boundary
  - Purpose: Requirements 1.3, 1.4, 13.5, 8.3
  - _Leverage: LayoutSidecar from task 18_
  - _Requirements: 1.3, 1.4, 8.3, 13.5_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Implement the commands that distinguish removing from a view from deleting from the model | Restrictions: deleting from the model must remove dependent relationships and must say how many views it affects before running; removing from a view must leave other views untouched | Success: the two operations are separately undoable, a model delete cascades to relationships, and a view removal is invisible to other views_

- [ ] 30. Context source resolver and action provider
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4ContextSourceResolver.cs` (new), `C4ContextActionProvider.cs` (new), test file (new)
  - Elements selectable by `element_id`; actions per selection kind, including container-to-component drill-down; nothing mutating offered in read-only mode
  - Purpose: Requirements 13.2, 13.3, 13.4, 13.6, 13.7
  - _Leverage: MindmapContextSourceResolver.cs, MindmapContextActionProvider.cs_
  - _Requirements: 13.2, 13.3, 13.4, 13.6, 13.7_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Make C4 elements selectable and give each selection kind its actions, including drill-down | Restrictions: one path to ribbon, menu and keyboard; shortcuts described as data; an inapplicable action reports a reason rather than vanishing silently | Success: selecting a relationship offers reverse-direction, selecting a container offers drill-down that creates the component view if absent, and read-only mode offers no mutation_

- [ ] 31. Toolbox contribution
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4ToolboxProvider.cs` (new), test file (new)
  - Exactly the active view's permitted kinds, plus a distinct group for adding an element that already exists in the model
  - Purpose: Requirement 12, including 12.5's "the host arrives separately"
  - _Leverage: src/diagrams/mindmap/backend/EtAlii.Adp.Diagram.Mindmap/MindmapToolboxProvider.cs_
  - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Describe the toolbox as backend data, offering only what the active view permits plus existing model elements | Restrictions: never offer a kind the view would then refuse; each item must be backed by a context action that also works from menu and keyboard | Success: a Context view offers Person and Software System only, a Deployment view offers node and instance kinds, and every item maps to an existing action_

## Phase I — creating diagrams and views

- [ ] 32. Empty documents and the Add flow
  - File: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4/C4DocumentFactory.cs` (new), test file (new)
  - A `workspace` with an empty `model` and one view of the module's kind, titled after the base name; `c4/code` refuses per task 26
  - Purpose: creating each type through the existing Add dialog with no change to that flow
  - _Leverage: IDiagramDocumentFactory, CreateDiagramFileCommandHandler, MindmapDocumentFactory.cs_
  - _Requirements: 2.1, 2.3, 5.4_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Produce a valid minimal document per type and confirm the Add flow needs no changes | Restrictions: an empty model must be a valid model, not an error; both files must be cleaned up if either write fails | Success: adding each of the six working types produces an openable diagram, the .adp carries the right MIME line, and a failed write leaves nothing behind_

- [ ] 33. "Add view" on a `.dsl` file
  - File: `src/backend/EtAlii.Adp.Backend/Hierarchy/AddDiagramContextActionProvider.cs` (modify), `Commands/AddViewDeclaration` in the shared library (new), test file (new)
  - The action on a `.dsl` entry opens the Add dialog filtered to the `c4` group, creates a headered `.adp` naming that body and a new view key, and appends the view declaration to the document
  - Purpose: Requirement 2.7 — the reviewer's addition at approval
  - _Leverage: AddDiagramContextActionProvider (extend, do not duplicate), DiagramFilePair headers from task 3_
  - _Requirements: 2.7, 2.4, 1.1_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer | Task: Add the "Add view" action to .dsl entries, reusing the Add dialog filtered to C4 types | Restrictions: extend the existing provider rather than writing a second one; the new .adp must carry body and view headers; appending a view must not disturb the rest of the document | Success: adding a view to an existing model creates only the .adp plus one appended view block, and both diagrams then open over the one shared model_

## Phase J — the canvas

- [x] 34. Client model and stream
  - **Done.** `c4Model.ts` and `useC4Stream.ts`, with `add` as an upsert so a re-delivered view replaces in place. Client stubs regenerated (`c4_pb.ts`)
  - File: `src/client/src/shell/panels/c4/c4Model.ts` (new), `useC4Stream.ts` (new), tests (new)
  - Delta application, payload decoding, failed state, viewport reporting
  - Purpose: the client half of the contract
  - _Leverage: src/client/src/shell/panels/mindmap/mindmapModel.ts, useMindmapStream.ts_
  - _Requirements: 1.5_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: TypeScript developer | Task: Build the client model and streaming hook for C4 diagrams | Restrictions: adds are upserts; report the area actually shown rather than the bare viewBox; keep the hook's identity stable so effects do not churn | Success: deltas apply correctly, a permanently failed stream surfaces as a failed state, and the reported viewport matches what the svg displays_

- [x] 35. `C4Canvas` — elements, relationships and boundaries
  - **Done.** Boxes at the backend's measured sizes, the person and cylinder shapes, external muting, dashed unidirectional labelled relationships anchored on the boxes' edges, and dashed named boundaries. Pan, zoom and fit reuse the mindmap's mechanics
  - File: `src/client/src/shell/panels/c4/C4Canvas.tsx` (new), test file (new)
  - Boxes with name, bracketed type-and-technology line and description; the shape vocabulary; external muting; dashed unidirectional labelled relationships; dashed named boundaries. Default palette as CSS custom properties, overridable from the payload
  - Purpose: Requirements 4.1–4.8 on screen
  - _Leverage: src/client/src/shell/panels/mindmap/MindmapCanvas.tsx (pan, zoom, fit, selection, drag, context menu), src/client/src/index.css_
  - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React developer | Task: Render C4 elements, relationships and boundaries to match the reference visuals | Restrictions: draw every element at the size the backend measured; put no C4 rule in terms of colour; the default palette must be overridable per model; give svg paths an explicit fill so curves do not fill solid | Success: a rendered view is recognisable as C4 to someone who has read c4model.com, external elements are visibly out of scope, and a themed model renders in its own palette_

- [x] 36. Title, legend, violations and interaction numbers
  - **Done.** Title and legend from the view payload, so a theme override stays truthful. Problems are **not** here: they arrive on core's existing project-problems push, located by element id
  - File: `src/client/src/shell/panels/c4/C4Canvas.tsx` (modify), test file (modify)
  - The title block, the legend from the view payload, the violations list with click-to-select, and numbered interactions on dynamic views
  - Purpose: Requirements 9.7, 9.8, 10.8, 7.5, 7.6
  - _Leverage: the view payload from task 21, the context selection chain_
  - _Requirements: 7.5, 7.6, 9.7, 9.8, 10.8_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React developer | Task: Render the furniture that makes a C4 diagram self-describing, plus dynamic interaction numbers | Restrictions: the legend must come from the payload rather than being hard-coded, so a theme override stays truthful; clicking a violation must select through the existing context chain | Success: every view shows a title and a legend covering the notation in use, violations select their element, and a dynamic view shows its ordering including nested numbers_

- [x] 37. Panel routing
  - **Done.** The six view types share one canvas; `c4/code` gets its own notice naming the dependency
  - File: `src/client/src/shell/panels/DiagramPanel.tsx` (modify), test file (modify)
  - `c4/*` routes to `C4Canvas`, except `c4/code`, which shows the dependency notice
  - Purpose: the tab opens the right canvas
  - _Leverage: DiagramPanel.tsx's existing mimeType dispatch_
  - _Requirements: 11.7_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React developer | Task: Route the six working C4 types to the canvas and c4/code to its notice | Restrictions: keep the existing fallback for unknown types; do not special-case each of the six individually | Success: all six open the canvas, c4/code explains what it waits on, and an unrelated type still hits the generic fallback_

## Phase K — catalog and verification

- [ ] 38. Integration tests across two views of one model
  - File: `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/C4SharedModelFlow.Tests.cs` (new)
  - Two `.adp` files over one `.dsl`: rename in one and observe the delta in the other; remove-from-view versus delete-from-model; undo across both
  - Purpose: Requirement 1's whole premise, proved over the real host
  - _Leverage: DiagramElementActionFlow.Tests.cs (the host fixture, login and project helpers)_
  - _Requirements: 1.2, 1.3, 1.4, 1.5_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET developer writing integration tests | Task: Prove that two views over one model stay consistent, over the real host | Restrictions: assert on the deltas actually produced, not on an empty collection - a narrowing viewport produces removals, not adds; use TestContext.Current.CancellationToken | Success: a rename in one tab is observed in the other, the two removal semantics are distinguished, and undo restores both views_

- [ ] 39. Catalog, manual pass and interoperability
  - File: `docs/diagrams.md` (modify), `tests.md` (modify)
  - Move the six working rows to ✅ Implemented and leave `c4/code` at 📝 Specified with its dependency noted. Run the manual pass: create a context and a container view over one model, verify the shared rename, drill down, drag with the drop-target highlight, and open the resulting `.dsl` in Structurizr Lite to prove the format is genuinely theirs
  - Purpose: the catalog rule in CLAUDE.md, and the interoperability claim behind choosing this format
  - _Leverage: tests.md's existing manual-check format_
  - _Requirements: 2.1, 3.1_
  - _Prompt: Implement the task for spec c4-diagrams, first run spec-workflow-guide to get the workflow guide then implement the task: Role: developer doing a verification pass | Task: Update the catalog and run the manual pass including a Structurizr Lite open of a file ADP wrote | Restrictions: do not mark c4/code implemented; record any bug found as a test or a tests.md entry rather than only fixing it; revert the worktree port changes before merging | Success: the catalog reflects reality, Structurizr Lite renders a model ADP created without complaint, and every bug found leaves a guard behind_
