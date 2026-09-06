# Design Document

## Overview

**The backend splits into functional projects standing on one shared project beneath them, and each `.proto` is generated into the project it belongs to.** The user's direction settled the shape:

> *"Put each .proto contract file in the corresponding project, and adopt `EtAlii.Adp.[PROJECT NAME].Wire` for the namespace the proto types generate into. Add a `EtAlii.Adp.Common` project for things shared by other projects that cannot be put in `EtAlii.Adp.Backend` due to dependencies. Try to not make a dependency from `EtAlii.Adp.Backend` onto the functional projects (Hierarchy, Diagrams etc.)."*

Three prior decisions are settled with it: **scope** — contracts and cycle-breaking first, then merge Diagrams, then extract areas one per landing; **naming** — top-level `EtAlii.Adp.Common` and `EtAlii.Adp.<Area>`, siblings of `EtAlii.Adp.Diagram`, never `EtAlii.Adp.Backend.<Area>`; **the bottom seam** — answered by evidence below, not by choice.

**This design supersedes approved Requirement 4, and says so rather than absorbing it quietly.** R4 says generation is *single-owned at the bottom by exactly one project below all others*. The user's later direction says *each proto goes in its corresponding project*. Those are incompatible, and the later instruction from the requirement's own author wins: generation is **distributed per project** into `.Wire` namespaces, with only the universally-shared protos sinking to `EtAlii.Adp.Common`. Approving this design ratifies that change to R4; it does not need its own card, because the contradiction is between an approved requirement and a direction from the same person, and this document is where that person sees and approves the resolution.

## The measurements this rests on, corrected and verified

**All verified at the current tree for this design; Developer 3's committed `findings.md` (`7955135c`) is the single authoritative dependency map and is cited rather than re-derived.**

- **The Introduction of the approved requirements overstates History's consumers, and the design carries the correction.** It says Problems and Sessions reference History's types; **comment-stripped, they reference it zero times** — both were `/// <see cref="…AddCommands"/>` doc comments (`AddProblems.cs:18`, `AddSessions.cs:17`). The real in-code consumers are **Context (6 sites) and Hierarchy (23 sites)** and nothing else. **Why the wrong figure arose matters here specifically**: a decomposition is planned from exactly this kind of survey, and *a grep cannot tell a call from a `<see cref>`* — the type name reads identically in both, so a name-occurrence detector answers "the name appears" when the question is "the code depends." A phantom edge buys a project boundary that never needed to exist. Developer 3's comment-stripped pass is the instrument that answers it, and it caught what two grep-shaped passes (mine and its own first) missed.
- **`shared.proto` declares `csharp_namespace = "EtAlii.Adp.Contracts"`; every other `src/api/*.proto` declares `EtAlii.Adp`.** So `ShortGuid.Cast.cs`'s `EtAlii.Adp.Contracts` namespace was never an incidental stray — it is the generated namespace of `shared.proto`, and that file extends those generated cast operators. A consequence, not a seam someone left. Under the `.Wire` scheme it becomes `EtAlii.Adp.Common.Wire`.
- **The proto import graph** (verified): `shared` and `connection` import nothing and are imported by nearly everything; `hierarchy → shared`; `elements → connection`; `deltas → elements`; `projects → connection, shared`; `diagrams → shared, connection, deltas`; `context → shared, connection, hierarchy, elements`. So per-project protos inherit their own dependency graph, and `context.proto → hierarchy.proto` is a **wire-level edge between two functional projects**, independent of any code edge.
- **Generation today is single-owned already**: `EtAlii.Adp.Backend.csproj` compiles `..\..\api\*.proto` with `GrpcServices="Both"` — one project generates every contract. That is the R4 shape the user is now distributing.
- **An orphan proto**: `src/backend/EtAlii.Adp.Backend/Hierarchy/hierarchy.proto` exists, **differs** from `src/api/hierarchy.proto`, and is **in no build** (the csproj globs `..\..\api`, not this path). "Put each proto in its project" starts from the single canonical set in `src/api/`; this orphan is reconciled against the canonical one and removed, not carried along.
- **`.Wire` is already partially adopted**: 3 of 14 module protos (`ansible-structure`, `causal-loop`, `helm-charts`) use `.Wire`; 11 do not. The user's scheme regularises an inconsistent existing convention rather than inventing one.
- **The code cycles** (from `findings.md`, anchor independently confirmed): the folders form one strongly-connected lump — `Authentication↔Sessions`, `Projects↔Hierarchy`, and loops through Context and Problems. **Context↔Hierarchy is 28 types one way and one — `Line` — the other**; I confirmed Context references exactly one Hierarchy type in code. Only `Client` (3 files, zero cross-folder edges) separates as-is.

## Steering Document Alignment

- **The backend owns meaning; the contract is the boundary — now literally per project.** Each functional project owns its slice of the contract (`<Area>.Wire`) and its behaviour, and shares only what is genuinely common. This is the same move `Diagram.Definitions` and the client `registrations` already made: a capability is a project discovered by reference, not a folder in a monolith.
- **CRLF, EditorConfig, the four gates, worktree-and-identity discipline** all apply unchanged; §Verification records the two checks a decomposition needs beyond the four gates.

## Architecture

### Two layers move, and they are independent

A **wire layer** (generated proto types) and a **code layer** (hand-written types) each have their own dependency graph, and the decomposition moves both. Keeping them distinct is what stops the design conflating a proto import with a code dependency.

### The wire layer — `.Wire` per project, `Common.Wire` at the bottom

Each `src/api/*.proto` generates into the project it belongs to, in an `EtAlii.Adp.<Area>.Wire` namespace:

| proto | generates into | wire dependencies |
| --- | --- | --- |
| `shared.proto` | `EtAlii.Adp.Common.Wire` | — |
| `connection.proto` | `EtAlii.Adp.Common.Wire` | — |
| `elements.proto` | `EtAlii.Adp.Common.Wire` | connection |
| `authentication.proto` | `EtAlii.Adp.Authentication.Wire` | — |
| `hierarchy.proto` | `EtAlii.Adp.Hierarchy.Wire` | Common.Wire |
| `projects.proto` | `EtAlii.Adp.Projects.Wire` | Common.Wire |
| `deltas.proto` | `EtAlii.Adp.Diagram.Wire` | Common.Wire (elements) |
| `diagrams.proto` | `EtAlii.Adp.Diagram.Wire` | Common.Wire, deltas |
| `context.proto` | `EtAlii.Adp.Context.Wire` | Common.Wire, Hierarchy.Wire |

**The design question the user's instruction opens — may `.Wire` assemblies reference each other, or must everything shared sink to Common? — is decided: they may reference each other along the proto import graph, and only the universally-imported protos (`shared`, `connection`, `elements`) sink to `Common.Wire`.** The reasoning: forcing everything shared down would drag `hierarchy.proto`'s domain types (`EntryKind`) into Common merely because `context.proto` imports them, which defeats per-project ownership. Letting `Context.Wire` reference `Hierarchy.Wire` mirrors the proto graph exactly and keeps each project's generated types with it. `shared`/`connection`/`elements` sink to Common because they are foundational identity types (`ShortGuid`, `Path`, `ElementId`) imported across unrelated areas, not any one area's domain. **This wire-level `Context.Wire → Hierarchy.Wire` edge is functional-to-functional and does not violate the user's constraint, which is specifically about `EtAlii.Adp.Backend` depending on functional projects.**

### The code layer — `EtAlii.Adp.Common` at the bottom

`EtAlii.Adp.Common` depends on nothing (except its own `Common.Wire`) and holds what the cycle-breaking pulls down. Measured members:

- **The command/history contract** — `ICommand`, `ICommandHandler`, `ICommandDispatcher`, `IHistoryStack`, `IHistoryStackStore`, `IContextNoticeSink`, and the `_Model` records (`CommandResult`, `HistoryEntry`, `HistoryAvailability`, `HistoryChangedEventArgs`). These sit in the root `EtAlii.Adp.Backend` namespace today and are consumed with no `using`, invisibly, by Context (6 sites) and Hierarchy (23 sites). **This is Requirement 1 and it moves first**, because nothing can be extracted while the contract everyone shares sits in a peer folder. The concrete `HistoryStack`/`CommandDispatcher` implementations and the sixteen command/handler *registrations* are composition-root wiring and go to the DI root (`Program.cs`), not to Common.
- **The `Line` document family** — `Line`, `LineDocument`, `LineRange`, `LineSegment`, `LineSplice`, with `AdpFileWriter` and `SharedDocumentReader`. Shared text-document primitives consumed by modules, Problems, Projects and Context. **The single `Line` type carries the entire Context→Hierarchy back-edge**, so moving the family down turns the fattest cycle (28→1) into a one-directional edge (28→0) and simultaneously shrinks the modules' widest Hierarchy dependency.
- **Context's provider contract** — consumed uniformly by Hierarchy/History/Problems/modules; a bottom-project citizen whole. (`HistoryActionsBroadcaster` is declared in Context but consumed by History and Service — resolved to its real home when Context is carved, a misplacement not a cycle.)
- **`SessionContext`** — consumed by four folders; the type whose descent breaks `Authentication↔Sessions`.

### Cycle-breaking, per edge

Each cycle breaks by pulling the shared type down, never by leaving a back-edge:

- **Context↔Hierarchy**: move the `Line` family to Common. The 28 Hierarchy→Context references remain (one-directional, legal); the 1 Context→Hierarchy reference is gone.
- **History↔Hierarchy**: the interfaces and `_Model` to Common; the concrete dispatcher/stack and the command/handler registrations to the composition root. Hierarchy then depends on Common's interfaces, not on History.
- **Authentication↔Sessions**: `SessionContext` and the ~5 shared types to Common; the residual edge resolves to one direction.
- **Projects↔Hierarchy**: broken by the `Line`-family descent (the shared text primitives are what Projects reaches Hierarchy for).

**39 of Hierarchy's 73 types and 18 of Context's 49 are consumed only within their own folder** — free movers with no edge to break, so the extraction is far less work than the raw folder sizes (65, 49) suggest.

### The 22 back-edges — now a requirement, honoured with a stated exception budget

Backend uses `EtAlii.Adp.Diagram` from **22 files** (verified). The user's *"try not to make a dependency from `EtAlii.Adp.Backend` onto the functional projects"* makes eliminating these an aim in its own right, and phase one breaks them **before** the Diagrams merge — which is exactly why contracts-and-cycles-first is coherent rather than arbitrary. Each back-edge resolves by depending on a contract in Common, not on Diagram. **The user said "try to," so this is an aim with an exception budget**: where a back-edge cannot be broken without disproportionate change, the design records the edge, why it resists, and what keeping it costs — rather than forcing an inversion that harms the code or silently leaving the dependency. The per-edge resolution is task-level work built on `findings.md`'s type map.

### Phases

1. **Common project + `Common.Wire`, contract and `Line` family down, cycles broken, 22 back-edges eliminated.** Nothing merges yet; the tree becomes acyclic.
2. **Merge `EtAlii.Adp.Backend.Diagrams` into `EtAlii.Adp.Diagram`** — now possible because the back-edges are gone. `Diagram.Wire` owns `diagrams.proto`/`deltas.proto`.
3. **Extract each functional area to `EtAlii.Adp.<Area>`**, one per landing, `Client` first (zero dependencies), each carrying its own `.Wire`.

## Sequencing sized for a moving `develop`

**This is a first-class design input, not a process footnote.** `develop` takes a commit roughly every two minutes; a backend gate cycle runs 15–45 minutes; and this specification's steps touch namespaces imported by ~360 use-sites across 60 module projects. A step that big **cannot win a fast-forward race by being careful** — a four-line docs change lost two full cycles to that today and landed only inside a coordinated hold. So steps are sized against the merge window, and the design states the blast radius of each so tasks can be sequenced against a moving target rather than discovering it.

**Two classes of step:**

- **Small, normal-cadence steps** — creating the `Common` project shell, moving a folder's internal-only types, extracting `Client`. These touch few or no module imports and land in the ordinary cadence.
- **Wide, hold-requiring steps** — the ones that rename a namespace imported across the module tree. The `Line`-family descent touches **all 12 module backends and 9 test projects** (as a namespace re-import, since they already reference Backend); the `.Wire` migration and each area's namespace change touch tens to ~150 use-sites. These are identified in the design as **needing a coordinated merge hold**, because they are structurally unable to win a fast-forward at develop's cadence, and the tasks flag them so the Scrum master schedules a window rather than an implementer losing cycles discovering it.

**Expected shape: roughly 10–14 landings** — the `Common` shell and `Common.Wire`; the contract descent; the `Line`-family descent (wide); each of the four cycle cuts; the back-edge elimination; the Diagrams merge; and one per extracted area (wide for the namespace rename). The tasks document sizes these exactly against `findings.md`; the estimate here is so the merge cadence is planned, not discovered.

## Data Models

No persisted change, no wire-shape change. The same generated messages, relocated to per-project `.Wire` namespaces; the same hand-written types, relocated across project boundaries. Every gRPC contract keeps its shape and its `GrpcServices="Both"` generation — the `csharp_namespace` option and the project each `.proto` compiles in are what move.

## Error Handling / Risks

- **A wire-level `.Wire→.Wire` cycle** would be as fatal as a code cycle. The proto import graph is acyclic (verified), so per-project `.Wire` assemblies following it stay acyclic — but the design pins this: no `.proto` may be moved into a project whose `.Wire` its own imports would then depend on circularly.
- **The orphan `hierarchy.proto`** is reconciled against `src/api/hierarchy.proto` and removed in phase one; leaving two divergent copies while distributing protos would generate one area's types from the wrong source.
- **A generated-code break is invisible to the four gates** because they read `obj/`. Every step that relocates generation or a namespace is accepted only on a **fresh-tree build** (§Verification).
- **The "try to" back-edges**: an edge that cannot be broken cheaply is recorded with its cost, not forced. Forcing a dependency inversion that contorts the code is a worse outcome than a documented, bounded exception the user flagged as acceptable.

## Verification

- **The four gates green on the merged tree**, exit codes captured before any pipe, `Zero tests ran` read as a broken build.
- **A fresh-tree build on every step that moves generation, a namespace, or a project reference** — the only check that reads a different input than the `obj/` cache the other gates trust.
- **`jb inspectcode` shown clean for the affected projects** — it sees the namespace-provider warnings `dotnet format` cannot, and a decomposition is the change most likely to produce them at scale. Where a folder should stop contributing to a namespace, that is fixed in `.DotSettings` `NamespaceFoldersToSkip` (escaping `_005C`/`_005F`, verified by `inspectcode`, never by eye — a mis-escaped key parses fine and matches nothing).
- **No behaviour change**: a test whose meaning changes is a signal the move changed behaviour and is a defect, not an expected edit.

## Sequencing summary

1. `EtAlii.Adp.Common` + `Common.Wire` (`shared`, `connection`, `elements` protos); reconcile and remove the orphan `hierarchy.proto`.
2. Command/history contract and `_Model` down to Common; registrations to the composition root.
3. `Line` family down to Common — **wide step, coordinated hold** — breaking Context↔Hierarchy and Projects↔Hierarchy.
4. `SessionContext` and shared auth/session types down — breaking Authentication↔Sessions.
5. Eliminate the 22 `Backend→Diagram` back-edges, recording any that resist with their cost.
6. Merge `EtAlii.Adp.Backend.Diagrams` into `EtAlii.Adp.Diagram`; `Diagram.Wire` owns `diagrams`/`deltas`.
7. Extract `Client` (clean), then each functional area to `EtAlii.Adp.<Area>` with its `.Wire` — **each a wide step, coordinated hold for the namespace rename**.
8. Per step: four gates, fresh-tree build, `inspectcode` clean.
