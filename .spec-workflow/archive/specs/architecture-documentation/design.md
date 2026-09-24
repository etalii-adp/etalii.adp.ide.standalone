# Design Document

## Overview

Two pages in `docs/`, a guard that fails the build when either stops being true, and a pass over the agent files that **removes** what the pages now hold rather than adding a third copy.

**The design's centre of gravity is the inventory in *What moves, what stays, what is false*.** Requirement 9.1 asks for every architecture passage in the agent files with the page and section it belongs to; that table is the specification of the removal, and everything else here serves it.

**Sizes, fixed by Requirement 7 and asserted by the guard:** each page at most 150 lines and 3 diagrams.

## Steering Document Alignment

### Technical Standards (tech.md)

This specification **edits** `tech.md`, which is unusual and is the point: of its eighteen sections, **five move whole** (*Technology stacks*, *Runtime*, *Diagram storage*, *Frontend-backend synchronization*, *gRPC call shapes*) and **two in part** (*Development tools*, *Naming & Identity*), because they describe what the software is rather than how the team works - and **one of the five is false**. The verdict table below accounts for all eighteen, so nothing is left unclassified by silence. The sections that state how the team works are untouched.

### Project Structure (structure.md)

Likewise. `structure.md`'s *Project structure* and *Naming* sections are the seed of `docs/solution-structure.md`; its *Code organization principles* and *Documentation standards* stay where they are.

## Code Reuse Analysis

- **`DocumentationLinks.Tests`** already checks relative links in six delivered documents; both pages join its list (R5.5), and Requirement 9.6 extends it to `CLAUDE.md` and the four steering files rather than adding a second link guard.
- **`GuardInventory.Tests`** is the shape to copy for the new guard: locate the repository root by walking up for `src/diagrams` beside `docs`, parse the document, assert, and carry a floor so an emptied page fails.
- **`docs/guards.md`** gains one row (R5.6), and its own test then asserts the new guard's file exists.

## Architecture

### The two pages, and what each section prevents

**Requirement 2.1 asks for the re-derivation each section prevents. That is the table, and a section that cannot fill the third column does not belong on the page.**

#### `docs/architecture.md`

| Section | Content | What it prevents |
| --- | --- | --- |
| What ADP is | five lines, then a link to `product.md` | re-reading the product steering document to orient |
| The three parts, and the single boundary | **Diagram 1**: browser client, one ASP.NET Core process, the filesystem | assuming a separate frontend server exists; assuming the client can touch files |
| The two call legs | the unary `Action` leg, the one server-streaming `Watch`, correlated by connection id, one stream per connection | adding a bespoke RPC for a new surface; opening a second stream per consumer |
| Where a diagram lives | a text file in the user's workspace, plus its `.adp` registration; layout in the registration when the format carries none | looking for a database; re-deriving where authored layout goes |
| How a diagram type plugs in | a module is `backend` + `api` + `client`, discovered at runtime; link to `creating-a-diagram-module.md` | reading 74 projects to learn the shape of one |
| What the canvas is | **SVG, drawn by the shared canvas library** | the Konva/PixiJS claim, and hunting for a renderer that was never adopted |
| The subsystems, named not described | one line each for Documents, Context, Commands and history, Problems, Projects, Sessions, Hierarchy, with links | guessing which project owns a concern |
| What this page is not | descriptive not normative; the refresh rule by link; the guard and its limits | treating the page as a specification, or trusting it to catch a wrong description |

#### `docs/solution-structure.md`

| Section | Content | What it prevents |
| --- | --- | --- |
| The shape of `src/` | **Diagram 2**: `api`, `backend`, `client`, `diagrams`, `editors`, `examples` | assuming one solution holds everything |
| The solution, with its counts | 105 projects - 27 core, 74 diagram, 4 editor; 78 production, 27 test - and 121 tracked `.csproj`, the 16-file difference being fixtures and examples of `dotnet-dependency-graph` | **both measured wrong answers below** |
| The relative-path trap | the solution's paths are relative to `src/backend/`, so core projects carry no `backend` segment | classifying projects by path segment and getting a plausible wrong split |
| The core projects | the 15 production and 12 test project names, by concern | `structure.md`'s stale list; re-deriving which project owns what |
| A module's folder shape | `diagrams/<type>/{backend,api,client,examples}`, editors the same under `editors/` | re-deriving the convention from an example |
| Namespaces, and the folders that do not contribute | `EtAlii.Adp.<Area>`; `_Model`, `Commands`, `History`, `Support` stay in the parent namespace; the `.DotSettings` mechanism by link to `CLAUDE.md` | "correcting" a namespace that is deliberately not folder-shaped |
| Where tests and fixtures live | `<Project>.Tests` beside its subject; `Fixtures/**` is test input, not product | adding a fixture to the showcase tree or vice versa |
| What this page is not | as above | as above |

### The two worked cases the count guard exists for

**Both were produced while writing the requirements, by the author of this design, in the course of one hour** - which is the argument for guarding counts rather than trusting them:

1. **121 `.csproj` tracked under `src/` against 105 in the solution.** A page stating "121 projects" would be wrong, and wrong in the confident direction. The difference is 16 files that are **fixture and example data of the `dotnet-dependency-graph` module**, which reads `.csproj` files as its subject matter.
2. **"78 backend, 27 other" from classifying the solution's project paths by segment.** Wrong, because the paths are relative to `src/backend/`. The true split is 74 diagram, 27 core, 4 editor - and the coincidence that made it plausible is that **78 and 27 are also the production and test counts**, two different sets of the same sizes.

### What moves, what stays, what is false

**THE TABLE IS THE AUTHORITY, NOT THE PROSE AROUND IT.** Any count or summary elsewhere in this document - including the overview's - is derived from these rows and may be re-derived from them at any time. The overview said "four sections" until the table was audited against itself and answered five whole and two in part; **the row is what a reader should act on.**

**This is Requirement 9's specification. Verdicts: MOVE (the page holds it and the agent file links), STAY (normative - how the team works, not what the software is), CORRECT (false, so the page states the truth and the passage goes), NAME (the page names the subsystem and links; nothing moves this pass).**

| File and section | Verdict | Goes to / why |
| --- | --- | --- |
| `tech.md` *Technology stacks* | **CORRECT + MOVE** | .NET, React + TypeScript, gRPC over grpc-web, ASP.NET Core hosting move to *The three parts*. **The canvas claim is false**: no Konva or PixiJS dependency exists in `src/client/package.json` and the canvas draws SVG. |
| `tech.md` *Development tools* | **SPLIT** | the Vite/ASP.NET proxy topology moves to *The three parts*; "we prefer Rider" and the F5 expectation STAY as team practice. |
| `tech.md` *Naming & Identity* | **SPLIT** | the `EtAlii.Adp.<Area>` shape and ShortId move to the structure page's *Namespaces*; **the agent commit-identity rules STAY** - they are normative and belong beside `CLAUDE.md`'s. |
| `tech.md` *Runtime* | **MOVE** | *The three parts*. |
| `tech.md` *Diagram storage* | **MOVE** | *Where a diagram lives*, including that the backend is the sole filesystem owner. |
| `tech.md` *Frontend-backend synchronization* | **MOVE** | *The two call legs*. |
| `tech.md` *gRPC call shapes* | **MOVE** | *The two call legs*. The six bullets condense; nothing is lost because the page is the only copy. |
| `tech.md` *Implementation order*, *Specifying a diagram type*, *Declaring a diagram definition*, *Registering a module's services*, *Testing & quality*, *Checking the conventions*, *Frontend*, *Backend* | **STAY** | procedures and conventions. *Declaring a diagram definition* carries measured evidence for a rule; a descriptive page would strip the rule from its reason. |
| `tech.md` *Context*, *Commands* | **NAME** | subsystem architecture, and Requirement 1.3 puts subsystem internals out of scope for this pass. The page names each in one line and links here. |
| `tech.md` *Decision log* | **STAY** | a log, referenced by `structure.md`. |
| `structure.md` *Project structure* | **CORRECT + MOVE** | the solution page's first four sections. **The core-project list is false**: `EtAlii.Adp.Backend.Diagrams` and its test project do not exist, the abstractions are in `EtAlii.Adp.Diagram`, and eleven decomposed core projects go unmentioned. |
| `structure.md` *Naming* | **MOVE** | *Namespaces, and the folders that do not contribute*. |
| `structure.md` *Module boundaries* | **MOVE** | *The three parts* and *How a diagram type plugs in*. |
| `structure.md` *Code organization principles* | **STAY** | normative. |
| `structure.md` *Documentation standards* | **STAY** | already only a pointer. |
| `product.md` (all) | **STAY** | product description, not architecture. The page links it. |
| `roles.md` (all) | **STAY** | how the team is organised. |
| `CLAUDE.md` (all) | **STAY + ADD** | normative throughout. Its architecture-adjacent sentences - `src/.editorconfig` being one file for the tree, Serilog's pipeline in `Program.cs` - stay **because they are inseparable from the rule they serve**: the reader is being told where to put a style rule, not what the system is. **`CLAUDE.md` gains a short section naming both pages and when to read them (R9.5).** |

**The implementer checks the rest of both files rather than trusting this sample.** Three claims were tested and two were false; that ratio is a reason to check every claim, not a licence to assume the remaining ones are sound.

**And no fact is dropped in a removal (R9.2).** Where a steering passage says something true that no page yet says, it goes onto the page first and is removed second.

## Components and Interfaces

### Component 1 - `docs/architecture.md`

Eight sections above, at most 150 lines, **one** mermaid diagram (`flowchart LR`: browser, ASP.NET Core process with its two legs, filesystem).

### Component 2 - `docs/solution-structure.md`

Eight sections above, at most 150 lines, **one** mermaid diagram (`flowchart TB` of `src/`'s six folders).

**Two diagrams, not six.** Requirement 4.4 wants each readable at dashboard width, and the budget of three per page is a ceiling rather than a target.

### Component 3 - `ArchitecturePages.Tests`

One class, in `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/`, modelled on `GuardInventory.Tests`.

- **Paths and project names (R5.1, R9.7).** Every backtick-quoted repo-relative path on either page exists. Every `EtAlii.Adp.*` name is a real project in `EtAlii.Adp.slnx`. **The same two checks run over `CLAUDE.md`, `structure.md`, `tech.md`, `product.md` and `roles.md`**, so a stale project name in a steering file reddens exactly as one on a page does.
- **Counts (R5.2, R3.1).** The pages state counts in a fixed form the test parses - `**105** projects in `EtAlii.Adp.slnx``, `**74** diagram`, `**121** tracked `.csproj` under `src/``, `**16**` fixtures and examples - and the test recomputes each from the tree and compares. **A stated count that the test cannot recompute is forbidden by R3.3**, which is why the page states these and not, say, a line count of the client.
- **Size (R7.2).** At most 150 lines and 3 fenced mermaid blocks per page.
- **Floor (R5.3).** A minimum number of parsed paths and counts per page, set below the actual, so an emptied or reshaped page fails while an ordinary edit does not - the reasoning `GuardInventory.Tests` states for its own floor.
- **Failure messages name the page and the fix** (NFR), because the reader will be a session that did not write the page.

**What it cannot do, stated on both pages (R8.3):** it checks existence, counts and size. **It cannot check whether a description is still accurate** - the Konva claim would have passed every check here, because `src/client/package.json` exists and the sentence names no count.

### Component 4 - the mermaid check

**Reuse, not a second installation (R4.3).** `module-client-api-readme` task 1 spikes whether `mermaid.parse` runs under the client's jsdom; as of 2026-09-23 it has not run. **Whichever specification lands that spike first owns the dependency**; this one adds nothing.

**Until then the diagrams are validated by a recorded manual render check**, and the implementation log states which held. **Worth knowing: the only tracked `mermaid` block in the repository today is in `.spec-workflow/templates/design-template.md`**, so nothing yet demonstrates that mermaid renders in the dashboard either - a diagram nobody can render is a page that fails silently.

## Testing Strategy

- **The guard is seen to fail before it is trusted (R5.4):** a planted wrong path, and a planted wrong count, each reddening for its own reason, both recorded in the implementation log with the message they produced.
- **`DocumentationLinks.Tests` extension** is verified the same way: a planted dangling link in a steering file must redden it.
- **The manual render check** is one entry appended to `tests.md`, naming the dashboard and the repository host as the two surfaces.

## Out of scope, restated

Per-module internals; wire contracts and generated code; the client component tree below the canvas library; deployment, hosting and configuration; the editor family's internals; per-diagram-type behaviour; runtime sequence diagrams. **Each is a later specification and each is deliberately absent.**
