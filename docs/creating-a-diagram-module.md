# Creating a diagram module

How to give ADP a new diagram type. This is a walkthrough of a path that already exists — the repository ships dozens of diagram modules under [`src/diagrams/`](../src/diagrams/) — traced against one real, complete module: **timeline** (`src/diagrams/timeline/`, the `generic/timeline` type). Every section names timeline's actual files, so every abstract statement has a concrete neighbour you can open.

Two companion documents: [creating an editor module](creating-an-editor-module.md) covers the second plugin family, which shares most of this mechanism; [the diagram catalog](diagrams.md) tracks every type ADP could support and where each one stands.

The rules this document leans on live where they already live — [CLAUDE.md](../CLAUDE.md) for the repository's working rules, [`tech.md`](../.spec-workflow/steering/tech.md) and [`structure.md`](../.spec-workflow/steering/structure.md) for the architecture — and are linked rather than restated, because two copies of one rule drift.

## What a module is

A diagram type is a folder under `src/diagrams/<diagram>/` with up to four subfolders — `backend/`, `api/`, `client/`, `examples/` — as [`src/diagrams/readme.md`](../src/diagrams/readme.md) and [`structure.md`](../.spec-workflow/steering/structure.md) define. Timeline has all four:

- `backend/` — `EtAlii.Adp.Diagram.Timeline` and `EtAlii.Adp.Diagram.Timeline.Tests`, both listed in `src/backend/EtAlii.Adp.slnx`.
- `api/` — `timeline.proto`, the type's own wire payloads.
- `client/` — the canvas, its registration, and a small `package.json`.
- `examples/` — documents a reader can open and click around in.

The dependency direction is absolute: a module depends on core abstractions (`EtAlii.Adp.Backend`, `EtAlii.Adp.Diagram`, the shared contracts), and **nothing in core depends on any module**. Core compiles and runs with your module's assembly absent. Everything below is a consequence of that rule.

## Specify first

A diagram type gets a spec before it gets code. What that spec must decide — the file format and its round-trip guarantee, who owns positions, what the toolbox contributes, which context actions exist with which inverses — is defined once in tech.md's [Specifying a diagram type](../.spec-workflow/steering/tech.md#specifying-a-diagram-type) section. This document owns the implementation path only; read that section before starting, and settle its questions in a `.spec-workflow/` spec of your own.

## The definition

The module's identity is one static class, `Diagram`, in the backend project — timeline's is [`Diagram.cs`](../src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline/Diagram.cs). It carries:

- **The origin** — `new DiagramOrigin("generic", "timeline")`. This `vendor/type` pair is the type's MIME identity everywhere: the first line of an `.adp` registration file, the catalog's Origin tag, the key core resolves every seam by, and the mime the client canvas matches on. Choose it per CLAUDE.md's catalog convention and never change it.
- **The document extension** — `DocumentExtension = ".tml"`. A diagram's body lives in a sibling file named by this extension, beside the `.adp` that registers it. A type that owns its extension outright (no other tool writes `.tml`) also routes a *bare* body file to itself, with no `.adp` needed. A format with a second serialization may declare it as `AlternateExtension` (the RDF family's `.nt` beside `.ttl`): the alternate routes and is claimed like the primary, but derives no registration sibling — a registered body carrying it names itself with an explicit `body:` header, which Add writes anyway.
- **The named definition** — a `public static DiagramDefinition Timeline { get; }` property carrying title, description, icon, extension and the `Build` delegate. Named, so the module's own registrations and tests write `Diagram.Timeline.Origin` rather than indexing into an array — tech.md's [Declaring a diagram definition](../.spec-workflow/steering/tech.md#declaring-a-diagram-definition) defines the two permitted shapes (stub and implemented) and when a module moves between them.
- **`Definitions`** — the array startup discovery reads: `[Timeline]`.

**Why adding a module edits no core file:** at startup, `DiagramDefinitionDiscovery` ([`src/backend/EtAlii.Adp.Diagram/DiagramDefinitionDiscovery.cs`](../src/backend/EtAlii.Adp.Diagram/DiagramDefinitionDiscovery.cs)) scans the application's assemblies for `Diagram.Definitions` arrays, and `Program.cs` hands every found definition to `AddDiagramDefinitions`, which invokes each definition's `Build` delegate. Timeline's delegate is one line — `builder => builder.Services.AddTimeline()` — and that is the entire hookup. The host names no module; deleting the folder removes the type.

## The registration surface

`Build` calls one extension method that registers everything the module contributes — timeline's is [`ServiceCollection.AddTimeline.cs`](../src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline/ServiceCollection.AddTimeline.cs), and it is deliberately the module's whole integration surface in one screen. The seams, each one line:

| Seam | Timeline's implementation | Needed when |
|---|---|---|
| `IDiagramDocumentFactory` | `TimelineDocumentFactory` | **Mandatory once you declare an extension** — startup fails, naming your type, if the factory that writes an empty body is missing. |
| `IDiagramSessionFactory` | `TimelineSessionFactory` | Mandatory to open the diagram at all. |
| `ICommandHandler<T>` per command | the `Commands/` folder | For every edit the type supports. |
| `IDiagramDocumentReloader` | `TimelineDocumentReloader` | So an external write to the body (another editor, a `git pull`) reaches every open session. |
| `IContextSourceResolver` | `TimelineContextSourceResolver` | To make elements selectable. |
| `IContextActionProvider` | `TimelineContextActionProvider` | To offer verbs — reaching the ribbon, right-click menu and keyboard through one path. |
| `IContextPropertyProvider` | `TimelineContextPropertyProvider` | To fill the property grid. |
| `IDiagramToolboxProvider` | `TimelineToolboxProvider` | To offer palette entries. A type that registers none simply has an empty toolbox. |
| `IDiagramValidator` | `TimelineValidator` | To report problems to the Errors and Warnings panel. A type with no rules registers nothing. |

Everything below `IDiagramSessionFactory` is optional in the mechanical sense — the app runs without it — but a type without selection, actions and properties is a picture, not an editor. Register the document store with `TryAddSingleton` (one store shared by every connection; a test that registers its own keeps it). Splitting a second `AddTimelineCommands` method out is done only when a concrete consumer needs the handlers alone — tech.md's [Registering a diagram module's services](../.spec-workflow/steering/tech.md#registering-a-diagram-modules-services) has the rule.

## The document path

Build bottom-up, per tech.md's [Implementation order](../.spec-workflow/steering/tech.md#implementation-order): the document first, because everything above it is written against something that already exists and already passes its tests.

The shape every shipped module converges on, in timeline's files:

- **A document that preserves what it read** — [`TimelineDocument.cs`](../src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline/TimelineDocument.cs) holds the file's lines with their own line terminators, so what came in goes out.
- **A parser that never throws** — [`TimelineParser.cs`](../src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline/TimelineParser.cs) turns the document into a model (`_Model/TimelineModel.cs`), reading what it understands and passing over what it does not. A construct the module does not model survives by never being touched; a semantically broken document still opens, and the validator reports it rather than the parser refusing it.
- **A writer that splices** — [`TimelineWriter.cs`](../src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline/TimelineWriter.cs) edits the lines a change touches and re-serialises nothing.
- **One store owning documents** — [`TimelineDocumentStore.cs`](../src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline/TimelineDocumentStore.cs) behind `ITimelineDocumentStore`: loads a document once, shares the instance across every connection, saves atomically, and raises a changed event sessions subscribe to.

Around these four, a module grows whatever domain helpers its notation needs, in the same project and namespace — timeline has a `TimelineScale` for its time axis, `TimelineRows` for row packing, `TimelineInstants` for date arithmetic — and the client canvas does the same (timeline's ruler, scrollbars and tick calculation are its own files beside the canvas). Those are ordinary internals, not seams: nothing outside the module knows them, and this walkthrough names only what the outside world touches.

All of it serves one promise: **a document ADP did not change comes back byte-identical** — comments, blank lines, odd indentation, line endings, a missing final newline. The round-trip tests compare bytes, which is also why the fixtures need a `.gitattributes` line (below). The model layer is deliberately usable without gRPC, the filesystem or a browser: text in, model out.

## The session, and the layout fork

A session is one open diagram on one connection: [`TimelineSession.cs`](../src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline/TimelineSession.cs), opened by [`TimelineSessionFactory.cs`](../src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline/TimelineSessionFactory.cs), which core resolves by origin. It answers a baseline (the whole diagram as element adds), reacts to the store's changed event by sending deltas (the difference against what this connection last saw — computed by [`TimelineElementMapper.cs`](../src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline/TimelineElementMapper.cs)), and handles moves.

Before writing it, decide the single biggest fork in any diagram type's design, and write the decision down: **who owns position?**

- **Authored in the document** — a position is the author's claim, and dragging is a *document edit* through a command. Timeline (x is authored time, y an authored row) and wardley-map (both coordinates are the statement) are the shipped examples.
- **Computed by the module** — layout is derived and never written back; dragging re-parents or is refused. Mindmap is the shipped example.
- **Computed with authored overrides in a sidecar** — c4 stores dragged positions in a `.layout.json` beside the body, never inside a file another tool owns.

Core is indifferent to the choice; the two move gestures (positional move, re-parenting move) are both on the session seam, and a session refuses the one that means nothing for its type.

## Commands

Every edit is an `ICommand` with a matching handler, dispatched through the history stack — never an inline mutation. Timeline's [`Commands/`](../src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline/Commands/) folder is the worked example: add, remove, rename, connect, disconnect, relabel, placement and end-date commands, each handler validating its own preconditions against *current* state (undo and redo dispatch it again later) and reporting the command that reverses it. A rejected command returns a `CommandResult` with a sentence for the user, never an exception. The full rules are tech.md's [Commands](../.spec-workflow/steering/tech.md#commands) section; the payoff is that undo, redo, validation and multi-client delivery come from core, not from your module.

## The wire

The core contract carries every diagram the same way: elements with an id, a position, a mime-style element type, and a `google.protobuf.Any` payload core never interprets. Your module defines those payloads in its own `api/` folder — timeline's [`timeline.proto`](../src/diagrams/timeline/api/timeline.proto) — and packs them in its element mapper. Core's `.proto` files in [`src/api/`](../src/api/) are not edited.

A module with its own proto joins two generation paths:

- Backend: the `.csproj` includes the proto through `Grpc.Tools`, compiled at build time.
- Client: the `generate` script in [`src/client/package.json`](../src/client/package.json) gains one `buf generate ../diagrams/<diagram>/api` step, which every client build and test run executes first.

## The client

The client half mirrors the backend's discovery, so the shell holds no list of diagram types either:

- **`client/register.ts`** — timeline's ([`register.ts`](../src/diagrams/timeline/client/register.ts)) exports `registrations`: a predicate on the mime type (`mimeType === "generic/timeline"` — match exactly, not by prefix) and the canvas component to mount. The shell discovers every module's file through `import.meta.glob` in [`diagramCanvases.ts`](../src/client/src/shell/panels/diagramCanvases.ts); adding the file is the whole hookup.
- **`client/package.json`** — a small workspace package ([timeline's](../src/diagrams/timeline/client/package.json)) declaring what the module's client code imports. It is picked up by the `diagrams/*/client` workspaces glob in [`src/package.json`](../src/package.json); run `npm install` from `src/` after adding it so the lock file learns the new package — `npm ci`, which CI runs first, refuses a lock that missed it.
- **The canvas** — timeline's `TimelineCanvas.tsx` with its stream hook (`useTimelineStream.ts`) subscribing to the diagram stream and unpacking the module's payloads. Before drawing anything by hand, read the shared canvas library under [`src/client/src/canvas/`](../src/client/src/canvas/): its [elements](../src/client/src/canvas/elements/readme.md) and [connections](../src/client/src/canvas/connections/readme.md) readmes catalogue the box, symbol, span, frame and connector shapes earlier modules grew and generalised — timeline draws its bars with the `span` element and its relations with the `interactive-bezier` connection rather than reinventing either.
- **Styling** — the shared appearance lives in [`canvas.css`](../src/client/src/canvas/canvas.css): your `register.ts` imports it (`import "@client/canvas/canvas.css";`) beside your own stylesheet, and your canvas composes its `canvas-*` classes alongside your module's own names (`my-type-node canvas-node`) — the module names carry behaviour and tests, the shared ones carry the look. Your own stylesheet keeps only what is genuinely yours (a ruler, a run-state colouring), states no colour as a literal, and reaches only for theme variables that actually exist in **both** modes of [`index.css`](../src/client/src/index.css) — a `var()` against a variable the theme lacks renders its hardcoded fallback in every theme, which is how one module shipped a dark-only canvas. A colour the theme genuinely lacks is added to the theme, in both modes, never as a module literal. The scrollbars are the shared [`CanvasScrollbars`](../src/client/src/canvas/scroll/CanvasScrollbars.tsx); a module owns only their placement offsets.

## The view-delta loop

**Every diagram module implements this, and the test is behavioural: a view *change* produces deltas.** A module that reports its viewport once when the diagram opens has implemented the first frame of the loop, not the loop; so has one whose `UpdateView` accepts a viewport and returns `[]`. Eleven modules implement it today, so a twelfth that skips it is the odd one out rather than the norm.

The mechanism is two correlated legs, and neither is yours to build: `Open` streams a baseline and every later change, while `UpdateView` is a unary call saying what the reader can currently see, correlated to the stream by `watch_id` and path. The backend answers a view report with the deltas that bring the connection into line.

**The client half is written once**, in [`src/client/src/diagrams/`](../src/client/src/diagrams/readme.md) — its readme is the reference. A module builds `reportView` with `viewReportOf(client, projectId, watchId, path)` in its stream hook and drives it from its canvas with `useViewReport({ view, report, convert, ready })`. It supplies only its viewport rectangle, in its own units, and nothing else: the request shape, the debounce, the readiness guard and the view-box conversion are all shared. Adopting the report does **not** mean adopting `useDiagramStream` — three modules hand-roll their open loop and report perfectly well. A guard, [`noPrivateViewReports.test.ts`](../src/client/src/diagrams/noPrivateViewReports.test.ts), fails naming any module that builds its own.

**The backend half is yours**, because deciding what a viewport admits is the one part that is genuinely per-module. Store the reported viewport, render, diff against what this connection last held, and return Add for what appeared and Remove for what left — **Add before Remove**, which is what every shipped implementation emits. Nothing here is centralized: the shipped sessions diff visible sets inline, delegate to a mapper's `Diff`, diff over expansion state as well as viewport, or consult a document store first. They resemble one another and are not the same, and merging them was refused deliberately.

Four things about writing that filter cost this specification real time, and none of them is obvious in advance.

**Lay out the whole document, then filter.** Project and position everything whatever the viewport says, and cull only afterwards. A layout computed over the *visible* set repacks as the reader pans — the diagram crawls under them — and it is the only way an element somebody pans to can have a position at all. Cache the projection and layout per session and drop it when the document changes; a pan is not a document change, and re-projecting a large document on every settled pan is work with no new answer.

**Ask what your module emits at a default position before you write the filter.** Four modules hit this independently — wardley's links and evolution axis, databricks' edges, sparql's edges and unplaced nodes and regions, rdf's registration header. An element the layout never placed carries `(0, 0)` or `default`, and a plain point-in-rectangle test keeps it only while the reader happens to be looking at the top-left corner, then culls it on the first pan. It reads as sensible code and passes any test that merely checks the element appeared. The working shape: a frame is always kept; an edge is kept structurally, on whether its endpoints survived, never on its own position; a visible node pulls in what it links to, so no edge is delivered with one end missing; and anything spanning a region is judged on the rectangle it spans rather than on its corner. Pin it with a test.

The scale of what that convention holds up is worth knowing before deciding it is fussy. One vendored thesaurus alone, `business-economics.ttl`, carries **725 `skos:related` links, every one of them packed at the origin** — not by oversight but because an edge has no position of its own. They are delivered correctly today only because all four RDF readings filter edges structurally; the first author to judge an edge by its own coordinate would cull all 725 on the first pan. This is not a bug waiting to be found, it is a correctness property currently held by convention across every reading that exists and every one that comes later, which is why it is stated here rather than left to be rediscovered.

**One filter for every path that decides what to send.** `Baseline`, the document-changed handler and `UpdateView` must render through the same method. A change path that renders unfiltered while `UpdateView` renders filtered leaves the two disagreeing about what the client holds, and an ordinary edit silently re-delivers everything the viewport just culled. It is invisible until somebody edits a document while zoomed in. The same rule caught a second form from the other side: an `UpdateView` that computes its own before-and-after set without updating what the connection is recorded as holding leaves the change path diffing against the whole diagram. Both are the same lesson — **use the same seam, not a variant of it.**

**An unbounded viewport is not "everything".** It is tempting to collapse an unfiltered rendering into `Visible(..., Unbounded)` and delete one of them. Two modules were measured and it is not safe in either: helm admits a node only when the layout gave it a box, and timeline withholds a connection unless both endpoints were sent — and its context resolver looks selections up in the unfiltered method, so the collapse would have made a dangling connection silently unselectable. On well-formed input the two agree exactly; that is a property of the layout placing every node, not of the mapper. Both modules now carry a test pinning the agreement *and* a second naming the deliberate divergence.

## Tests, fixtures and examples

- **The test project** — `EtAlii.Adp.Diagram.Timeline.Tests`, xUnit v3, `<OutputType>Exe</OutputType>` (a v3 test project hosts its own tests). Run the suite with the exact command CLAUDE.md's [Running the backend tests](../CLAUDE.md#running-the-backend-tests) section defines, and read its warnings about `Zero tests ran` before trusting any green.
- **Byte-compared fixtures** — the round-trip corpus in [`Fixtures/`](../src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline.Tests/Fixtures/): a `crlf-line-endings` / `lf-line-endings` pair, `no-trailing-newline`, comments, unmodelled constructs. Because their *bytes* are the test subject, the type's extension gets one `-text` line in [`.gitattributes`](../.gitattributes) (`*.tml -text` is timeline's) — the reasoning is written out in that file so it does not have to be rediscovered; without the line, git's line-ending conversion makes the tests measure git instead of your writer.
- **Examples** — `examples/` holds documents a reader opens and clicks around in, distinct from fixtures, which pin the parser one construct at a time. Every module seeds its examples into the combined [`src/examples/`](../src/examples/) project — the one folder that shows every type in a single explorer tree — and that is how a new type joins the showcase. **The two copies are not held in sync.** They were, byte-for-byte, under a guard that was removed on 2026-09-04: the combined project is a working surface, so arranging a diagram in the running app writes a `layout:` block into the showcase copy while the module copy stays put, and the guard read ordinary product use as drift. What still holds for both trees is that every example registration opens against the deployed catalog, which [`ExampleRegistrationTests`](../src/backend/EtAlii.Adp.Backend.Tests/Integration%20Tests/ExampleRegistration.Tests.cs) walks in both places.

## The catalog, and staying true

`docs/diagrams.md` is how "which diagram types does ADP have?" is answered, and CLAUDE.md's [Diagram type catalog](../CLAUDE.md#diagram-type-catalog) rule requires its row to move with your module's state — add or update the row as the type is identified, specified, implemented. And this walkthrough carries the same duty in return: a change that moves a touch point named here updates this document in the same change.
