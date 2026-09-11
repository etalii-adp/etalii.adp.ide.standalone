# Requirements Document

## Introduction

The user's instruction, verbatim:

> *"Add a specification for a new readme that describes the client side API that should be used to create a new diagram module. Be systematic in describing all aspects, as well as use markdown examples + mermaid diagrams on how to apply it."*

**The deliverable is a readme; this specification says what it must cover, how "all aspects" is decided, and how the readme proves it is right and stays right.** "Systematic" and "all aspects" are the load-bearing words. A readme that covers what its author thought of is the failure this specification exists to prevent, so "all" is defined below as a measured set with a boundary, and completeness is asserted by a test rather than by review.

One decision was taken by the user on 2026-09-11, on the recommended option: **the readme lives in `docs/`, beside `docs/creating-a-diagram-module.md`, and is authoritative for every statement about the client-side API.** The alternatives offered were a readme beside `src/client/src/canvas/library/` (rejected: the module-facing API spans five folders, not one) and expanding the existing walkthrough in place (rejected: not the new readme asked for).

## What was measured

Measured at `develop` `5befeb0a` on 2026-09-11. Commands are recorded under *Sources* so every figure can be re-run.

### Who uses the client API today

- **13 modules** register a client (`src/diagrams/*/client/register.ts`), with **19 mime registrations** and **19 canvas components**. **16 of those components render `DiagramCanvas` directly**; the other three are `databricks`' wrappers. `centralized-selection` counts the 16 and this specification counts the 19 — both are right about different things, and the readme states which it means wherever it gives a count.
- A further 48 modules have a `client/` folder but no registration; they are the future readers of this readme rather than users of the API today.

### The surface they use, parsed from their imports

- **70** non-test client files in those 13 modules import from **42** distinct `@client/...` paths — **27 shared** and **15** that are one module's own generated payload types — naming **122** distinct names.
- **Every one of the 13 modules** imports: `DiagramCanvas`; `DiagramDefinition` and `assertValidDiagramDefinition`; `DiagramModel` and `DiagramModelElement`; `DiagramEventHandlers` and `DiagramSelection`; the selection helpers in `canvas/selection`; `useViewReport`, `viewReportOf` and `Viewport`; the generated `Delta`; `useContextConnection` and `useContextSelection`; `DiagramCanvasRegistration`; and the shared stylesheet `canvas/canvas.css`.
- Most of the declared vocabulary is **not imported by name**: a module fills in `DiagramDefinition` as an object literal, so an import count sees the type and none of its fields. The used surface and the declared surface are therefore two measurements, and "all aspects" needs both.

### The surface the library declares

- The library's definition, api and layout files, together with the module-facing hooks outside the library (`canvas/selection`, `canvas/interaction`, `canvas/connectors`, `diagrams/`, `shell/panels/diagramCanvas`, `shell/panels/useToolboxItems`, `shell/context/inlineLabelPrompt`), export **at least 198 declarations** across 23 files. `DiagramDefinition` alone has 13 top-level fields. **This figure is a line count, not a parse**, and it counts internals the library exports for its own tests alongside what modules use. Drawing the module-facing boundary is Requirement 2's job.
- A pattern-based survey of which module uses which aspect was run and **is not cited**: it reported that no module uses bindings, which `ShaclCanvas` contradicts (it binds through `template:`, a form the pattern did not know). A survey that can miss a form reads its own blindness as absence. Requirement 2.4 requires the map to be parsed.

### The guards a module client is subject to

At least eight tests walk every module's client code and fail on a forbidden pattern: `noPrivateGestures`, `noPrivateLabelEditors`, `noPrivateScrollbars`, `noPrivateViewReports`, `declarativeModules`, `libraryGuards`, `sharedStylesheetImports` and `themeTokens`. **"At least"**: this is what one search found, and the first search tried found none of them, because it looked for the wrong walking idiom. The design measures the full list.

### What already documents it

- `docs/creating-a-diagram-module.md` (181 lines) covers the whole module path and gives the client one dense paragraph, plus the client halves of *Renaming in place* and *The view-delta loop*. `processes.md` *Keeping documentation true* makes it update in the same change as any touch point it names.
- Six per-folder readmes document library internals: `canvas/label`, `canvas/gesture`, `canvas/scroll`, `canvas/elements`, `canvas/connections`, and `src/client/src/diagrams/readme.md`, which is the reference for the view report.

### What is moving under it

`centralized-selection` (requirements committed at `b6b687ee`; its design awaiting approval) removes per-module selection wiring from every canvas: the selection derivations all 13 modules write today, hand-written `onSelectionChanged`, the `context` prop, and module-private selected classes. **A readme written against today's code would document a pattern that specification makes forbidden.** Its author, Architect 2, has listed the module-facing names its design removes, keeps and adds; Requirement 7 records them, and follows that specification wherever its approved documents differ.

## Alignment with Product Vision

`structure.md` requires a new diagram type to be addable without touching core code, and `product.md`'s **"Don't reinvent, integrate"** asks that shared mechanisms be used rather than rewritten. Both depend on a module author knowing what the shared client API offers and what it forbids. Today that knowledge lives in one paragraph, six folder readmes, eight guards and the code of thirteen modules; the guards say what is wrong only after it has been written.

## Requirements

### Requirement 1 — One readme, with a stated authority

**User Story:** As a module author, I want one place that describes the client API completely, so that I do not assemble it from a paragraph, six readmes and thirteen modules.

#### Acceptance Criteria

1. WHEN the readme is delivered THEN it SHALL be a new document in `docs/`, named in the design, describing the client-side API a diagram module uses.
2. WHEN a statement about the module-facing client API is needed anywhere THEN the readme SHALL be its one home. `docs/creating-a-diagram-module.md`'s *The client* section SHALL shrink to a summary that links to the readme, and the client halves of *Renaming in place* and *The view-delta loop* SHALL link to it rather than restate it. The walkthrough stays authoritative for everything that is not the client API: the backend, the wire, discovery, tests and the catalog.
3. WHEN a per-folder readme already documents a folder's internals THEN the new readme SHALL link to it rather than repeat it, and SHALL say in one sentence what the reader will find there.
4. WHEN the readme is delivered THEN it SHALL join the documents `DocumentationLinksTests` checks, and `processes.md` *Keeping documentation true* SHALL name it beside the two module walkthroughs.

### Requirement 2 — "All aspects" is a measured set with a boundary

**User Story:** As a reviewer, I want "all" to be a list I can check, so that completeness is not a matter of the author's memory.

#### Acceptance Criteria

1. WHEN the module-facing surface is defined THEN it SHALL be the union of two measured sets: **(a)** every name a module client imports from shared client code, a module's own generated payload types excluded, parsed from import statements; and **(b)** every declaration a module supplies by value, reached by following types from `DiagramDefinition`, `DiagramCanvasProps`, `DiagramEventHandlers` and every event type, and `DiagramModel`.
2. WHEN a declaration is exported by the files that surface lives in but belongs to neither set THEN it SHALL be listed by name as library-internal, so the boundary is a list and not a judgement.
3. WHEN either set is measured THEN the command and the commit it ran against SHALL be recorded beside the result.
4. WHEN the readme says which shipped module uses an aspect THEN that SHALL come from a parse of the module's code, never from a text pattern.

### Requirement 3 — Systematic: every aspect gets an entry of the same shape

**User Story:** As a module author, I want every part of the API described the same way, so that I can find what I need and trust that nothing is left out.

#### Acceptance Criteria

1. WHEN an aspect is documented THEN its entry SHALL answer the same questions in the same order: what it is for; whether a module needs it and what happens without it; its shape, as the declaration's type or a link to it; a worked example; the guards that constrain it; and the aspects it relates to.
2. WHEN the entries are arranged THEN they SHALL follow the life of a module client, from registration and discovery, through the stream and the model, the definition, the events and how a module answers them, selection, the context channel, the view report and styling, to the tests and guards. The design fixes the headings.
3. WHEN the definition is documented THEN every field reachable from `DiagramDefinition` SHALL have an entry or be covered by its parent's entry by name, including element types, shapes and custom shapes, labels and bindings, anchors, decorations, relation types with routes, markers and endpoint constraints, background, chrome, toolbox, layout, dragging, snap, extent, drop targets, drag bounds, and actions with their shortcuts and enablement.
4. WHEN the readme is read in order THEN a walkthrough SHALL show a minimal module client built end to end — registration, stream hook, definition, canvas, event handlers, view report — each step linking to the entries it uses.

### Requirement 4 — Examples are shipped code, and stay shipped code

**User Story:** As a module author, I want examples I can trust to compile, so that copying one does not teach me something the library no longer accepts.

#### Acceptance Criteria

1. WHEN a code example is given THEN it SHALL be an excerpt of a shipped module's source, SHALL name its file, and SHALL be written as a fenced code block.
2. WHEN a module is chosen for examples THEN one reference module SHALL be used throughout — `dependency-graph`, the reference migration of `declarative-diagram-modules`, unless the design shows another covers more of the surface — and another module SHALL be used only for an aspect the reference does not exercise.
3. IF no shipped module exercises an aspect THEN the entry SHALL say so, and its example SHALL be checked by the type checker. It SHALL NOT be presented as an excerpt of shipped code.
4. WHEN an excerpt is given THEN a test SHALL fail if it no longer occurs, verbatim, in the file it names.

### Requirement 5 — Mermaid diagrams show how the parts fit

**User Story:** As a module author, I want to see how my module and the library talk to each other, so that I understand the flow before I read the entries.

#### Acceptance Criteria

1. WHEN the readme is delivered THEN it SHALL include mermaid diagrams for at least: how a module is discovered and mounted; the round trip of one open diagram, from the backend stream through the model and the canvas to an event and back through a command and a delta; the structure of `DiagramDefinition`; and the life of one gesture that the module accepts or refuses.
2. WHEN a diagram names an API declaration THEN that name SHALL exist in the module-facing surface, and the completeness test SHALL check it as it checks the entries.
3. WHEN mermaid is used THEN it SHALL appear in the readme only, never in this specification's own documents: the spec-workflow dashboard renders these, and does not take what a markdown viewer does.
4. WHEN the readme is delivered THEN its diagrams SHALL be seen to render in the places it is read, which the design names.

### Requirement 6 — Completeness is asserted, never reviewed

**User Story:** As a maintainer, I want the readme to fail a gate when it falls behind the code, so that it cannot drift the way a review criterion drifts.

#### Acceptance Criteria

1. WHEN a module-facing declaration has no entry in the readme THEN a test SHALL fail, naming each one.
2. WHEN a module client imports a shared name outside the documented surface THEN the same test SHALL fail, naming the module and the name: new use gets documented, or is refused.
3. WHEN an entry names a declaration that no longer exists THEN the test SHALL fail, naming the entry.
4. WHEN the test is accepted THEN it SHALL first have been **seen to fail** against each of the three cases above and against Requirement 4.4's excerpt check, and SHALL carry both canary shapes: a floor on the number of names checked, and a named member that must be present.
5. WHEN the test runs THEN it SHALL run inside one of the four gates, so a stale readme fails a gate rather than a reader.

### Requirement 7 — The readme describes the API after `centralized-selection`

**User Story:** As a module author, I want the readme to describe the API I am allowed to use, not one that is being withdrawn.

#### Acceptance Criteria

1. WHEN selection is documented THEN the readme SHALL describe selection as `centralized-selection` leaves it — owned by the library, with unselectable types declared in the definition — and SHALL NOT present deriving or pushing a selection as a module's to write. As that specification's design proposes it (pending approval on 2026-09-11), that means no module-facing entry for `elementSelectionOf`, `selectedElementIdOf`, `elementIdOfKey` and `innermostKey`, nor for `DiagramCanvas`'s `selection` and `context` props or `onSelectionChanged`. What it keeps module-facing for action handlers (`elementSourceOf`, `useContextConnection`, `useContextPrompt`) and what it adds (a declared `selectable` flag on element and relation types, a `source` prop, a refused-action event) are documented like any other aspect.
2. WHEN this specification's implementation is scheduled THEN it SHALL NOT start until `centralized-selection`'s implementation is on `develop`, established with `git merge-base --is-ancestor`. Requirements and design may proceed before then.
3. IF `centralized-selection`'s approved documents change what a module does about selection THEN this specification SHALL follow them.

### Requirement 8 — What a module must not do is part of the API

**User Story:** As a module author, I want to know what the library forbids before a guard tells me, so that I use the shared mechanism the first time.

#### Acceptance Criteria

1. WHEN the readme is delivered THEN it SHALL name every test that walks module client code to forbid a pattern, what each forbids, and the shared mechanism to use instead. The list SHALL be measured, not recalled, and SHALL start from the eight found above.
2. WHEN a guard is added later that walks module client code THEN the completeness test SHALL fail until the readme names it.

## Non-Functional Requirements

### Code Architecture and Modularity

- The readme adds no API and changes no code outside its own test and the walkthrough's client section. It documents the surface; it does not redesign it.

### Reliability

- Every relative link resolves: `DocumentationLinksTests` covers the readme (Requirement 1.4).
- One home per statement (Requirement 1.2): what the readme owns is not repeated elsewhere, and what a folder readme owns is linked.

### Usability

- A reader who has read `docs/creating-a-diagram-module.md` can build a minimal module client from the readme alone (Requirement 3.4).
- Examples are fenced code blocks naming their source; diagrams are mermaid fences; nothing depends on raw HTML.

## Sources

- The 13 registered module clients and their 70 non-test files at `5befeb0a`, imports parsed with a script reading `import ... from "@client/..."` statements rather than by name matching.
- The library's definition, api and layout files and the module-facing hooks, exports counted per file at `5befeb0a` with a line grep — a floor the design replaces with a parser.
- The eight guards, found by searching the client's tests for the walk-up-to-`diagrams` idiom they share.
- `centralized-selection` requirements at `b6b687ee`, and `declarative-diagram-modules`, whose Requirement 1.2 that specification withdraws.
- `docs/creating-a-diagram-module.md`, the six per-folder library readmes, and `processes.md` *Keeping documentation true*.
