# Design Document

## Overview

The requirements fixed what the readme must cover and how it proves it. This design fixes the four things they left open: **the document** (its name, its headings, the shape of an entry), **the boundary** (how the module-facing surface is computed, and what is listed as internal), **the test** that holds the readme to the code, and **the edits** to the walkthrough and the steering documents that make the readme the one home for the client API.

One idea carries all four: **the readme is written in a shape a test can read.** Every entry declares the names it covers on one line, every example names its source on the line above it, and every internal export is on one list. So the questions "is everything covered?", "does this example still compile?" and "does this entry describe something that exists?" are answered by the test, and each answer names what is wrong.

Nothing here is implemented before `centralized-selection` is on `develop` (Requirement 7.2). This design describes the surface as that specification's approved design and tasks fix it (approved 2026-09-11; none of its module-facing names moved between design and tasks). Its implementation is Developer 1's: if a name moves during that migration, Developer 1 or Architect 2 tells this specification's author, and where the two differ when it lands, that specification wins (Requirement 7.3).

## Steering Document Alignment

### Technical Standards (tech.md)

- **The document owns no API.** It describes the surface and changes none of it; the only code it adds is its own test, and the few example files of Component 4 (Requirement 3, Non-functional: Code Architecture).
- **Assert, never review.** The test runs in the client gate (`npm test`) on every merged tree and in CI, so a stale readme fails a gate rather than a reader (Requirement 6.5).

### Project Structure (structure.md)

- The readme is `docs/diagram-module-client-api.md`, beside `docs/creating-a-diagram-module.md` and `docs/creating-an-editor-module.md`, per the user's placement decision.
- The test is `src/client/src/diagramModuleClientApi.test.ts`, at the root of the client source beside the other repository-wide client tests (`themeTokens.test.ts`, `viteFsAllow.test.ts`), because the surface it checks spans five folders and belongs to none of them.

## Code Reuse Analysis

### Existing Components to Leverage

- **The TypeScript compiler** (`typescript`, already a client dev dependency at 5.7): parses the surface files and every module client. The probe for this design used `createSourceFile` alone, with no type checker, and computed the by-value closure in well under a second.
- **The guard family's walk-up idiom** (`noPrivateGestures.test.ts`): finding `src/` from the test file, walking module client folders, collecting every offender and failing once naming them all.
- **`DocumentationLinksTests`**: checks the readme's links once the readme is in its document list.
- **The client typecheck gate** (`npm run typecheck`): type-checks the example files of Component 4 with no new mechanism.

### Integration Points

- **`docs/creating-a-diagram-module.md`**: its client material becomes a summary and links (Component 5).
- **`processes.md` *Keeping documentation true***: gains the readme beside the two walkthroughs.
- **`centralized-selection`**: defines the selection entries' content and the post-selection surface (Requirement 7).

## Architecture

### The surface, measured two ways

The module-facing surface is the union of two sets (Requirement 2.1), both computed by the test on every run, so the boundary moves with the code.

**Set A, what modules import.** For every non-test `.ts`/`.tsx` file under `src/diagrams/*/client/`, every name imported from an `@client/...` specifier, parsed with the compiler. **A module's test files count too, for one folder: what they import from the library's shared `testing/` helpers** (amended 2026-09-12; see *The amendment* below). Excluded are a module's own generated payload types, `@client/generated/{type}_pb` where `{type}` is not a core contract. The core contracts (`deltas_pb`, `elements_pb`, `context_pb`, `context-contract_pb`, `diagrams_pb`, `problems_pb`) are shared and stay in. At `5befeb0a` this was 122 names from 42 specifiers, 15 of them module-own.

**Set B, what modules supply by value.** Starting from the roots `DiagramDefinition`, `DiagramCanvasProps`, `DiagramEventHandlers` and `DiagramModel`, follow every type reference, `extends` clause and `typeof` query through the exported declarations of the surface files, syntactically. For a function or a constant, only its signature counts. **Measured for this design at `96c4a07b`: the 14 library files export 158 declarations, and the walk reaches 98 of them** — 49 in `diagramDefinition.ts`, 18 in `diagramEvents.ts`, 9 in `binding.ts`, 6 in `background.ts`, 5 in `chrome.ts`, 4 in `actions.ts`, 3 each in `DiagramCanvas.tsx` and `diagramModel.ts`, 1 in `diagramRuntimeConfig.ts`.

**The surface files** are the library's `definition/`, `api/` and `layout/` files, `DiagramCanvas.tsx`, and every file outside the library that Set A shows a module importing from. That second part is computed, not listed: a file enters the surface by being imported.

**Where the measurement is recorded (Requirement 2.3).** The test is the command. The readme's first section names it and the commit its figures were last read at, and every count the readme quotes says what it counts.

**The internal list.** Every export of a surface file that is in neither set is library-internal, and the readme's last section lists it by name (Requirement 2.2). Of the 60 library exports outside Set B at `96c4a07b`, two are in Set A — `DiagramCanvas` and `assertValidDiagramDefinition` — and the rest are candidates for the list: the `Resolved…` and `resolve…` families, the layout internals, `routePath`, `anchorPoints`, `snapToStep` and similar. The list is the test's to enforce in both directions: a listed name that is not exported, or that turns out to be imported by a module, fails.

### The document is written in a shape a test can read

Three conventions carry every check; nothing else in the readme is parsed.

```
### Element types
**Declarations:** `ElementTypeDefinition`, `BuiltInShape`, `CustomShapeRef`

What it is for. Whether a module needs it. Its shape. The example:

Source: [`src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx`](../src/diagrams/dependency-graph/client/DependencyGraphCanvas.tsx)
```

- **`**Declarations:**`** — one line per entry, naming in backticks every declaration the entry covers. The union of these lines is what the readme covers.
- **`Source:`** — the line immediately above a fenced code block, naming the file the block is excerpted from, as a link. The block must occur in that file verbatim, line endings aside (Requirements 4.1, 4.4). **The reference module is `dependency-graph`** (Requirement 4.2). A rough survey put it level with `wardley-map` for breadth; the parse at implementation settles it, and if another module covers more, the implementation log records the comparison and the reference changes by a revision of this design, not by drift.
- **`## Library-internal exports`** — the last section, a list of backticked names.

### The entries, and their order

An entry answers the six questions of Requirement 3.1 in order: what it is for; whether a module needs it and what happens without it; its shape; a worked example; the guards that constrain it; related entries. Headings follow the life of a module client (Requirement 3.2):

1. **How to read this document** — the entry shape, what is authoritative here and what is linked.
2. **The shape of a module client** — the two overview diagrams.
3. **Registration and discovery** — `register.ts`, `DiagramCanvasRegistration`, `DiagramClientModule`, the workspace `package.json`, the shared stylesheet import.
4. **The stream and the model** — `useDiagramStream`, the core `Delta` and `Element` contracts, unpacking a module's own payloads, `DiagramModel`, `DiagramModelElement`, `DiagramModelConnection`, `useAuth`.
5. **The definition** — `DiagramDefinition`, then one entry per reachable declaration group: element types and shapes, custom shapes, labels and bindings, anchors, decorations, relation types with routes, markers and constraints, custom routes, background, chrome, toolbox, layout, dragging, snap, extent, drop targets, drag bounds, actions with shortcuts and enablement, selectability, and validation with `assertValidDiagramDefinition`.
6. **The canvas** — `DiagramCanvas` and its props, `source` and `editing` among them.
7. **Events, and how a module answers them** — `DiagramEventHandlers` and every event type, answering through the module's own transport, refusal, and the refused-action event.
8. **Selection** — owned by the library, as `centralized-selection` leaves it; the declared `selectable` flag; what a handler receives in its event. No entry for the names Requirement 7.1 lists as leaving module use.
9. **The context channel** — `useContextConnection`, `useContextPrompt`, `elementSourceOf`, `contextShortcutOf`, inline label editing with `inlineLabelElementIdOf`, and the channel's resolve-never-reject rule.
10. **The toolbox** — `useToolboxItems` and `useRegisterDiagramToolbox`.
11. **The view report** — `useViewReport`, `viewReportOf`, `Viewport`, linking `src/client/src/diagrams/readme.md` for the mechanism.
12. **Geometry for custom shapes and routes** — `canvas/connectors`, linking the elements and connections readmes.
13. **Styling** — `canvas.css`, class composition, theme variables that exist in both modes.
14. **Tests a module writes, and what a module must not do** — the shared test helpers a module's canvas test is required to call, then every guard that walks module clients, what it forbids, and the shared mechanism instead (Requirement 8.1). The list starts from the eight the requirements found and adds `centralized-selection`'s text and behavioural guards when they land.
15. **A minimal module client, end to end** — the walkthrough of Requirement 3.4.
16. **Library-internal exports** — the list.

**Field coverage (Requirement 3.3).** An entry that declares an interface reachable from `DiagramDefinition` must name each of that interface's members in backticks in its own section, so a field added later fails the test until its entry describes it.

## Components and Interfaces

### Component 1 — The readme, `docs/diagram-module-client-api.md`

- **Purpose:** the one home for the module-facing client API (Requirements 1.1, 1.2).
- **Reuses:** links, rather than restates, the five per-folder readmes and the walkthrough (Requirement 1.3).

### Component 2 — The mermaid diagrams, in the readme only

Five diagrams, each beside the entries it introduces (Requirement 5.1):

- **Discovery and mount** (flowchart): `register.ts`, the shell's glob, the mime predicate, the canvas mounting.
- **One open diagram, round trip** (sequence): backend stream, stream hook, `DiagramModel`, `DiagramCanvas`, a `DiagramEvent`, the module's handler, a command through the module's transport, a delta, and the model again.
- **The definition** (class diagram): `DiagramDefinition` and its reachable declaration groups.
- **One gesture, accepted or refused** (sequence): a connection drawn, `onConnectionDrawn`, the backend's answer, and both outcomes, including the refused-action event.
- **The view report** (sequence): a pan, the debounced report, `UpdateView`, the deltas.

**Where they are seen to render (Requirement 5.4):** GitHub's file view, since the repository lives there, and Rider's markdown preview with its Mermaid extension enabled, since that is where the team reads documents. Both are checked by eye at implementation and recorded in the implementation log. The spec-workflow dashboard is not a target: these documents carry no mermaid (Requirement 5.3).

### Component 3 — The test, `src/client/src/diagramModuleClientApi.test.ts`

One file, five checks, each collecting every offender and failing once, naming them all:

1. **Coverage** — every name in Set A ∪ Set B appears on a `**Declarations:**` line (Requirement 6.1), and every member of a covered interface reachable from `DiagramDefinition` is named in its entry (Requirement 3.3).
2. **Use outside the surface** — this falls out of check 1: Set A is recomputed from the modules on every run, so a module importing a new shared name adds it to the surface, and the name fails check 1 until it has an entry (Requirement 6.2). The failure message names the importing module.
3. **Stale entries** — every name on a `**Declarations:**` line, and every name on the internal list, is an export of a surface file; and no internal-listed name is in Set A or Set B (Requirements 2.2, 6.3).
4. **Excerpts** — every fenced block preceded by a `Source:` line occurs verbatim in that file, with CRLF normalised to LF on both sides (Requirement 4.4).
5. **Diagrams and guards** — every identifier in a mermaid block that is an export of a surface file must be in the surface (Requirement 5.2); and every test file under `src/client/src` that walks the module client folders is named in the readme's guards section (Requirement 8.2). A walking test is recognised by the idiom the guard family shares: a string literal `"diagrams"` together with a directory read or a glob over module `client` paths. The rule is calibrated at implementation against the known guards, and must find all of them.

**Canaries (Requirement 6.4):** a floor on names checked (Set A ∪ Set B at least 90, set just below the measured size), and named members that must be present: `DiagramDefinition` in Set B, `useViewReport` in Set A, `noPrivateGestures` among the walking tests. **Seen to fail first**, one planted defect per check: an entry's name deleted, a module importing an undocumented name, an entry naming a removed declaration, an excerpt edited in its source, an undocumented walking test added. Each must fail naming exactly what was planted.

### Component 4 — Examples for aspects no shipped module exercises

Requirement 4.3 forbids passing off an invented example as shipped code. Where no module exercises an aspect, its example lives in `src/client/src/canvas/library/examples/{aspect}.example.ts`, which the client typecheck gate compiles. The readme excerpts it like any other source, with the same verbatim check, and its entry says in words that no shipped module uses the aspect. **Which aspects need one is decided at implementation by a parse of the modules (Requirement 2.4), never in advance.** This design's own pattern survey was wrong on the first claim checked, so it names none.

### Component 5 — The walkthrough and the steering documents

- **`docs/creating-a-diagram-module.md`, *The client*:** the four bullets and the context-channel paragraph become one summary paragraph and a link to the readme. The paragraph names what the client half is (a registration, a stream hook, a definition, event handlers) and nothing about how.
- **Its *Renaming in place* and *The view-delta loop*:** the backend halves stay, since they are backend material; the client paragraphs become one sentence and a link each.
- **`DocumentationLinksTests`:** `docs/diagram-module-client-api.md` joins its document list (Requirement 1.4).
- **`processes.md` *Keeping documentation true*:** the readme joins the two walkthroughs, with the note that its test enforces what the sentence asks.

### The amendment: test-facing helpers are part of the surface

**As first written, this design's Set A parsed non-test module files only.** A helper that only a
module's *tests* import would therefore never enter the surface, would never be forced into the
readme, and the completeness test would have passed green while the readme omitted something every
module is required to call. `centralized-selection` adds exactly such a helper:
`expectLibrarySelection(harness)`, which its text guard requires every module canvas test to call.
**Measured on `develop` at `7546e1a8`, once ten modules had migrated: 13 module test files in 10
modules import it, and no non-test module file imports anything from that folder at all.** So the gap
is not a possibility - the surface as first defined saw none of it.

**A completeness check that cannot see a whole class of import is complete only about the half it
looks at.** So Set A now also counts what a module's test files import from the library's shared
`testing/` folder - that folder and no other, because a module's tests import plenty that is not API
(`vitest`, its own fixtures), and widening further would document the test framework.

**How it was found is worth recording:** not by re-reading this design, but because Developer 1
reported the module-facing names from an implementation already on `develop`. The cross-check between
a specification and the implementation of the specification it depends on is what caught it.

## Data Models

### What the test computes

```
Surface      = SetA ∪ SetB
SetA         = { name | a module client imports name from a shared @client/... specifier }
             ∪ { name | a module client TEST imports name from @client/canvas/library/testing/... }
SetB         = closure over type references from the four roots, within the surface files
Internal     = exports(surface files) − Surface
Covered      = names on the readme's **Declarations:** lines
Listed       = names in the readme's Library-internal exports section
Pass when    Surface ⊆ Covered, Covered ⊆ exports, Listed = Internal
```

`Listed = Internal` is an equality on purpose: a name the list forgot fails like a name an entry forgot, and a name the list keeps after a module starts importing it fails too.

## Error Handling

### Error Scenarios

1. **A module starts using an undocumented name.**
   - **Handling:** check 1 fails in the client gate, naming the module and the name.
   - **User Impact:** the change that started using it documents it, or uses the documented mechanism instead.
2. **A library field is added, renamed or removed.**
   - **Handling:** checks 1 and 3 fail, naming the member and the entry.
   - **User Impact:** the readme changes in the same change as the code, as *Keeping documentation true* asks.
3. **An excerpted source moves.**
   - **Handling:** check 4 fails, naming the source file and the entry.
   - **User Impact:** the example is re-excerpted from the moved code, never left describing the old code.
4. **The parse itself breaks** (a surface file renamed, the compiler unable to read a file).
   - **Handling:** the canaries fail: the floor on names checked, and the named members. A test that silently checks nothing is the failure this family of guards exists to catch.

## Testing Strategy

### Unit Testing

- The five checks run in `npm test` against the real readme, the real library and the real module clients. There is no fixture readme: the subject is the repository as it stands.
- Each check is seen to fail against its planted defect before the test is trusted (Component 3). The plants are temporary, in a worktree, and never committed.

### Integration Testing

- `DocumentationLinksTests` covers the readme's links in the backend gate.
- The example files of Component 4 are compiled by the client typecheck gate.

### End-to-End Testing

- The readme's diagrams are seen to render in GitHub's file view and in Rider's markdown preview (Requirement 5.4), recorded in the implementation log.
- A reader test, stated rather than automated: the walkthrough section is followed from an empty `client/` folder to a mounted canvas, and anything it had to look up elsewhere is added to it (Non-functional: Usability).

## Sources

- Set B's size and the 60 exports outside it: a parse of the 14 library files with the TypeScript compiler's `createSourceFile`, walking type references from the four roots, at `96c4a07b`.
- Set A's size: the import parse recorded in the requirements, at `5befeb0a`.
- The post-selection surface: `centralized-selection`'s approved design and tasks (2026-09-11), as its author listed the module-facing names removed, kept and added, confirmed unchanged by the tasks.
- The guard family's idiom: `src/client/src/canvas/library/noPrivateGestures.test.ts`.
