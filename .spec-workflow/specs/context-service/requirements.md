# Requirements Document

## Introduction

This spec adds a **context service**: one backend-owned, per-connection record of *what the user currently has selected*, with a gRPC `Select` call through which any surface reports a selection, and a `Watch` server-stream through which any interested party — on the client or inside the backend — learns about it. The first source feeding it is the workspace explorer from [`project-root-folder-explorer`](../project-root-folder-explorer/requirements.md): whenever the user selects a file or folder there, its path (the entry's location relative to the project root, resolved by the backend) together with its `ShortGuid` id is forwarded to `Select`. The selection message itself is deliberately generic — a **source** (which surface produced it), a **scope** (what kind of thing it refers to) and an **id** — so that a diagram canvas, a ribbon, a search result list, or a future diagram-type module can feed it without the contract, the service, or any existing consumer changing.

The service is meant to take a central role: the Property Grid, the ribbon's command enablement, "open the selected file in a diagram tab", per-diagram undo/redo, the Errors & Warnings panel and the context-action mechanism of [`rename-files-and-folders`](../rename-files-and-folders/requirements.md) all need to know *the current selection*, and today none of them can. The only thing resembling a selection in the codebase is `ExplorerTreePanel`'s private `focusedKey` React state, which never leaves that component.

A thorough search of the current specs, protos, backend and client (summarised in *Existing functionality this service replaces or absorbs* below) found one existing mechanism that overlaps with this one — the context-action RPCs (`DiscoverActions`, `ExecuteAction`, `ProposeInput`, `SubmitInteraction`, `CancelInteraction`) that [`rename-files-and-folders`](../rename-files-and-folders/design.md) placed on `HierarchyService` only because that service already held the one per-connection stream a browser can be pushed anything over. Their `scope` + `source` vocabulary (`context.proto`'s `ContextScope`/`ContextSource`) is exactly this spec's selection vocabulary; this spec adopts that vocabulary as-is, hosts the new `Select`/`Watch` RPCs in a dedicated `ContextService`, and migrates the existing context-action RPCs and prompt channel onto that same service so the hierarchy service goes back to being about the hierarchy only.

## Alignment with Product Vision

- **"A familiar surface"** ([product.md](../../steering/product.md)): in VS Code, Rider and Visual Studio, selecting something in one tool window drives every other one — the Properties window follows the selection, toolbar commands enable and disable against it, the editor opens it. That single shared "current selection" is what makes an IDE feel like one product rather than a set of unrelated panels; this spec introduces it for ADP.
- [tech.md](../../steering/tech.md)'s **frontend-backend synchronization** principle ("state changes flow from backend to client as a gRPC stream") and its note that the backend already "remembers the state of the view in the client per connection": a connection's selection is the same kind of per-connection client state as its view state, and is kept in the same place — the backend — for the same reason: so backend-side logic (context-action providers, future property providers, diagram modules) can act on it without a round trip.
- tech.md's **"The backend is the sole owner of reading/writing diagram files; the web client never touches the filesystem directly"** and `rename-files-and-folders` design's deviation 3: the client selects by **id**, never by path; the backend resolves the id to a location inside the project root and forwards *that*, so no filesystem path is ever accepted from, or leaked to, the browser beyond the project-relative path this spec explicitly allows consumers to display.
- [structure.md](../../steering/structure.md)'s **core-vs-diagram-type-plugin dependency direction**: the selection record is scope-agnostic data; a diagram-type module can publish a selection in its own scope and subscribe to selections in others without core code knowing that module exists, which is the same seam `IContextActionProvider` already established.
- [short-guid](../short-guid/requirements.md): selections reference what they refer to by the same `ShortGuid` the referenced thing already carries (hierarchy entries today; diagram elements and others later), rather than introducing a second identifier representation.
- [`reusable-context-menu`](../reusable-context-menu/requirements.md) / [`rename-files-and-folders`](../rename-files-and-folders/requirements.md) **"don't reinvent, integrate"**: the existing `ContextScope` and `ContextSource` messages are reused as the selection's scope and id rather than defining a parallel pair.

## Existing functionality this service replaces or absorbs

This section records the result of the search the spec was asked to do, so the decisions below are traceable. Each item names what exists today, where, and what this spec does with it.

| # | Existing functionality | Where | Decision |
|---|---|---|---|
| A | **Per-connection selection/focus of an explorer entry** — `focusedKey` state in `ExplorerTreePanel`, driven by click, right-click, and arrow-key navigation; never leaves the component. | `src/client/src/shell/panels/ExplorerTreePanel.tsx` | **Absorbed.** The tree keeps its focus state for keyboard navigation, but every time it changes the tree now forwards it to `Select` (Requirement 3). The tree remains the owner of *focus*; the context service becomes the owner of *selection*. |
| B | **Context-action RPCs on `HierarchyService`** — `DiscoverActions`, `ExecuteAction`, `ProposeInput`, `SubmitInteraction`, `CancelInteraction`, keyed by `watch_id`, with prompts pushed down `WatchHierarchy` via `HierarchyMessage.prompt`. The design doc says they live there only because that was the one open per-connection stream. | `src/api/hierarchy.proto`, `Hierarchy/HierarchyServiceImpl.Context.cs`, `Context/ContextInteractionStore.cs` | **Replaced: moved onto `ContextService`** (Requirement 6). `WatchHierarchy` goes back to carrying only `HierarchyChange`; prompts ride the new context `Watch` stream instead. The scope/source vocabulary and provider abstractions (`IContextActionProvider`, `IContextActionResolver`, `ContextTarget`) are kept unchanged. |
| C | **`ContextScope` / `ContextSource`** messages in `context.proto`. | `src/api/context.proto` | **Reused as-is** for the selection's `scope` and `id`; this spec adds only a `source` (Requirement 2). |
| D | **Per-connection `watch_id` discipline** — `IHierarchyModelStore` and `IContextInteractionStore` both key state 1:1 per client-generated `watch_id`. | `Hierarchy/HierarchyModelStore.cs`, `Context/ContextInteractionStore.cs` | **Reused.** The selection is keyed by the same `watch_id`, so the context service can resolve a hierarchy entry id through that connection's own `HierarchyModel` exactly as `TryResolveTarget` does today. |
| E | **`DiscoverActions` on focus** — the tree calls `DiscoverActions` for every entry that gains focus, caching the result so shortcuts can be answered locally. | `ExplorerTreePanel.tsx` (`actionsFor` / the focus `useEffect`) | **Replaced by a pushed message.** Since `Select` already tells the backend what is selected, the backend pushes the selected target's available actions on the `Watch` stream (Requirement 5) instead of the client asking for them separately. The client-side cache and the `DiscoverActions` round trip on focus go away; `DiscoverActions` itself remains available for on-demand use. |
| F | **Property Grid placeholder** — "Properties of the currently selected element", no data source. | `shell/panels/PropertyGridPanel.tsx` | **Becomes a consumer (first real consumer, minimal).** This spec only has it display the current selection (source, scope, path/name) — proving the subscription path — not edit properties (Requirement 7). |
| G | **Ribbon buttons** — static `RIBBON_GROUPS`, `// TODO(diagram-ide-mockup): wire real command handler`. | `shell/ribbon/RibbonBar.tsx` | **Not changed here, but unblocked.** Ribbon enablement against the selection is a natural follow-up once the selection and its actions are pushed (Requirement 5); explicitly out of scope. |
| H | **"Select a file node → open it"** — `project-root-folder-explorer` Requirement 3.4 / `adp-diagram-ide` Requirement 1.2. | specs only; `DiagramPanel` is a placeholder | **Not implemented here, but given its trigger.** Opening a file is a *consequence* of a selection in the hierarchy scope; the spec that implements diagram tabs subscribes to the context stream rather than wiring a callback from the tree (Requirement 8). |
| I | **Open-tab / unsaved-changes awareness** — `rename-files-and-folders` Requirements 8.1, 8.2, 10.3, 10.4, explicitly deferred for lack of an "open document" concept. | `rename-files-and-folders/design.md`, *Deviations* | **Not addressed here.** Selection ≠ open document; a later open-documents registry can be a second kind of per-connection context, but that is not this spec. |
| J | **`DiagramService.Connect` bidi stream** — declared, implemented nowhere. | `src/api/diagrams.proto` | **Unchanged.** Diagram-element selection will be published into this spec's service by the spec that implements the canvas; the existing contract already needs no change for that (a new `ContextScope` value and a new `ContextSource` member suffice). |

Searched, and found to hold no overlapping selection mechanism: `projects.proto`/`ProjectService` (project open is a navigation, not a selection — it already hands `projectId` down through props), `authentication.proto`, the `History/` command dispatcher and history stack (they act on commands, not on a selection), `SearchPanel`/`ToolboxPanel`/`ErrorsWarningsPanel` (placeholders with no state), the `diagram-undo-redo` and `mindmap-diagram` specs (both need "the current diagram" but define no way to know it — see Requirement 8).

## Requirements

### Requirement 1 — A dedicated context service

**User Story:** As a developer building IDE features, I want one backend gRPC service that owns "what is currently selected on this connection", so that every panel, command and backend provider reads the same answer instead of each surface wiring private callbacks to the others.

#### Acceptance Criteria

1. WHEN the backend starts THEN the system SHALL expose a gRPC service dedicated to context — `ContextService` — separate from `HierarchyService`, `ProjectService`, `DiagramService` and `AuthenticationService`, defined in the shared `src/api/` contract folder alongside the existing `context.proto` vocabulary it builds on.
2. WHEN `ContextService` is defined THEN it SHALL offer at least a unary `Select` call (Requirement 2) and a server-streaming `Watch` call (Requirement 4); both SHALL work over gRPC-Web from the browser, i.e. neither SHALL require client-streaming or bidirectional streaming, consistent with the constraint `rename-files-and-folders` design already established.
3. WHEN a `ContextService` call is made THEN the system SHALL authorize it the same way `HierarchyService` calls are authorized today (session interceptor plus `TryResolveRootPath`-style project authorization), so a selection can only be made or watched within a project the caller may access.
4. WHEN the context service keeps state THEN it SHALL keep it strictly **per connection**, identified by the same client-generated `watch_id` the hierarchy calls on that connection use, and SHALL never share it with another connection — not even one from the same user viewing the same project — following the 1:1-per-`watch_id` discipline `IHierarchyModelStore` and `IContextInteractionStore` already enforce.
5. WHEN the `Watch` stream for a `watch_id` ends (client closes it, navigates back to the project grid, or the connection drops) THEN the system SHALL discard that connection's selection and every other context state held for it, so nothing outlives its connection.

### Requirement 2 — The select message: source, scope and id

**User Story:** As a developer adding a new surface, I want to report a selection with a message that says *where it came from*, *what kind of thing it is* and *which one*, so that I never need to touch the contract or the service to make my surface participate.

#### Acceptance Criteria

1. WHEN a client reports a selection THEN the system SHALL accept a `Select` request carrying: the `project_id`, the connection's `watch_id`, a **source**, a **scope** and an **id** — and nothing surface-specific beyond those.
2. WHEN the **scope** is expressed THEN the system SHALL reuse `context.proto`'s existing `ContextScope` enum (today: `HIERARCHY`), so a selection's scope and a context action's scope are the same value and a provider registered for a scope serves both.
3. WHEN the **id** is expressed THEN the system SHALL reuse `context.proto`'s existing `ContextSource` message (today: `entry_id`, a `ShortGuid`), so a selection names the selected thing exactly the way a context action already names its target; a future scope (a diagram element, a search hit, a ribbon command) is added as one more `oneof` member, not a new message.
4. WHEN the **source** is expressed THEN the system SHALL identify the surface that produced the selection as a new `ContextSelectionSource` value (e.g. `EXPLORER`, with `DIAGRAM_CANVAS`, `SEARCH`, `RIBBON`, `PROPERTY_GRID` reserved for later surfaces) distinct from `ContextSource`, so a consumer can tell "the explorer selected entry X" from "the canvas selected entry X" even when scope and id are identical — e.g. so the explorer does not re-react to a selection it produced itself.
5. WHEN a `Select` request names an id the backend cannot resolve on that connection (unknown `watch_id`, an entry id belonging to another connection or project, or an entry that no longer exists) THEN the system SHALL reject the request with a clear, non-revealing error and SHALL leave the connection's current selection unchanged, consistent with `TryResolveTarget`'s "unknown and unauthorized are answered the same way" rule.
6. WHEN a client wants to clear the selection (nothing is selected any more) THEN the system SHALL accept a `Select` request with no id, recording "no selection" for that source, rather than requiring a separate RPC.
7. A `Select` request SHALL carry exactly **one** selected id; multi-selection is not in scope for this spec, matching `rename-files-and-folders` Requirement 4.4's single-entry rule. The message SHALL be designed so a later spec can extend it to several ids (e.g. by making the id repeated) without renaming or replacing it.

### Requirement 3 — Explorer selections are forwarded to `Select`

**User Story:** As a user, I want the file or folder I click or arrow onto in the explorer to become "the current selection" everywhere in the IDE, so that the properties panel, commands and editors follow what I'm looking at.

#### Acceptance Criteria

1. WHEN the user selects an entry in the explorer tree — by clicking it, by moving keyboard focus onto it with the arrow keys (`rename-files-and-folders` Requirement 1), or by right-clicking it (which also focuses it, that spec's Requirement 3.1) — THEN the client SHALL call `Select` with source `EXPLORER`, scope `HIERARCHY` and that entry's `ShortGuid` id, for the connection's `watch_id`.
2. WHEN the backend accepts an explorer selection THEN the system SHALL resolve the entry id, through that connection's own `HierarchyModel`, to its location under the project root and SHALL record **both** the id and the resolved path as the selection — so consumers (backend providers and, per Requirement 4.4, clients) receive the path and the id together, as the explorer's contribution to the context, rather than having to resolve it again.
3. WHEN the selected entry is renamed, moved or deleted on disk (`project-root-folder-explorer` Requirement 4) THEN the system SHALL update or clear the recorded selection accordingly — its id stays the same across a rename (that spec's Requirement 2.5), so only its path changes; a delete clears the selection — and SHALL notify watchers (Requirement 4) of the change without the client needing to call `Select` again.
4. WHEN the explorer moves focus purely for navigation in quick succession (e.g. holding `ArrowDown`) THEN the client SHOULD coalesce the resulting `Select` calls (e.g. by debouncing with the existing `useDebouncedValue` hook, or by only sending the last of a burst) so the backend is not asked to record every intermediate row; the final resting entry SHALL always be reported.
5. WHEN the explorer tree loses the selected entry from its rendered set (the entry's parent folder is collapsed, or the entry is removed by a pushed hierarchy change) THEN the client SHALL NOT silently keep reporting it as selected — a collapsed parent keeps the selection (the entry still exists), a removal clears it via Requirement 2.6, matching what the backend does independently under Requirement 3.3.
6. WHEN the explorer tree unmounts (back to the project grid) THEN the client SHALL close its context `Watch` stream, and the backend SHALL discard the selection per Requirement 1.5; no explicit clearing `Select` is required.

### Requirement 4 — Watching the context

**User Story:** As a developer building a panel or a backend feature that depends on the selection, I want to subscribe once and be told whenever the selection changes, so that I never poll and never need a direct reference to the surface that made the selection.

#### Acceptance Criteria

1. WHEN a client opens `Watch` for a `project_id` and `watch_id` THEN the system SHALL start a server stream on which it pushes the connection's context as it changes, and SHALL push the **current** selection (or an explicit "nothing selected") as the first message so a late subscriber is immediately consistent.
2. WHEN the connection's selection changes — by a `Select` call, or by a backend-side update per Requirement 3.3 — THEN the system SHALL push one message to every open `Watch` stream **for that same `watch_id` only**, never to another connection's stream, mirroring the per-connection delivery rule `project-root-folder-explorer` Requirement 4.5 establishes for hierarchy changes.
3. WHEN a selection message is pushed THEN it SHALL carry the same source, scope and id the selection was made with (Requirement 2), so a consumer can filter on any of the three.
4. WHEN a selection message is pushed for a `HIERARCHY`-scope selection THEN it SHALL additionally carry the entry's **project-relative path** (as `connection.proto`'s `Path` segments, relative to the project root — never an absolute filesystem path) and its kind (file/folder), so a consumer such as the Property Grid can show *what* is selected without its own hierarchy lookup; this is the one place the contract exposes a path to the client, and it is the same project-relative form `projects.proto` already shows on the project grid.
5. WHEN more than one client-side consumer on the same page needs the context (Property Grid, ribbon, diagram pane, explorer) THEN the client SHALL open **one** `Watch` stream per connection and fan it out in-process (e.g. a React context/provider at the workspace-shell level), not one gRPC stream per consumer — the `watch_id` is per connection, and so is its stream.
6. WHEN the `Watch` stream is interrupted THEN the client SHALL reopen it with the same `watch_id`, and the first message per Requirement 4.1 SHALL re-baseline it, the same way `grpc-core-communication` Requirement 6.2 re-baselines a diagram connection.
7. WHEN backend-side code needs the current selection of a connection (a context-action provider deciding what applies, a future property provider) THEN the system SHALL expose it in-process through an injectable store (e.g. `IContextSelectionStore`, a structural sibling of `IContextInteractionStore`), so backend consumers read it directly rather than through their own gRPC stream.

### Requirement 5 — The selection's available actions are pushed, not fetched

**User Story:** As a user, I want keyboard shortcuts and the context menu to be ready the moment I select something, and as a developer I don't want every surface to re-implement "ask for the actions of the focused thing".

#### Acceptance Criteria

1. WHEN a selection is recorded for a scope that has one or more `IContextActionProvider`s (today: `HIERARCHY`) THEN the system SHALL discover that target's available actions through the existing `IContextActionResolver` and push them on the `Watch` stream alongside the selection (as part of the same message or an immediately following one), in the same `ContextActionGroup` shape `DiscoverActions` returns.
2. WHEN the explorer receives the actions for its own current selection on the context stream THEN it SHALL use those to answer keyboard shortcuts and to populate the context menu, replacing its current per-focus `DiscoverActions` round trip and `actionsByKey` cache (*Existing functionality*, item E); the behaviour `rename-files-and-folders` Requirements 3 and 4 describe — one action implementation behind both triggers, no client-side key table — SHALL remain unchanged from the user's point of view.
3. WHEN the selection's actions change while the selection itself does not (e.g. the selected folder becomes empty, the selected file is locked) THEN the system MAY push a refreshed actions message; it SHALL at least do so when the selection's path changes under Requirement 3.3.
4. `DiscoverActions` SHALL remain available as an on-demand call after its move to `ContextService` under Requirement 6, for any surface that wants the actions of something it has *not* selected (the explorer itself never needs this: a right-click selects first, per `rename-files-and-folders` Requirement 3.1, so its actions always arrive via Requirement 5.1).

### Requirement 6 — Context actions move from `HierarchyService` to `ContextService`

**User Story:** As a developer, I want the context-action RPCs to live on the context service they belong to, so that `HierarchyService` is about the hierarchy only and a new scope's actions don't have to be bolted onto the hierarchy's stream.

#### Acceptance Criteria

1. WHEN this spec is implemented THEN the system SHALL move `DiscoverActions`, `ExecuteAction`, `ProposeInput`, `SubmitInteraction` and `CancelInteraction` from `HierarchyService` to `ContextService`, with their request/response messages unchanged in shape except for their new home, so `rename-files-and-folders`' behaviour is preserved verbatim.
2. WHEN a backend-initiated prompt (`ContextPrompt`: input dialog, confirm dialog, closed) has to reach the client THEN the system SHALL push it on the connection's context `Watch` stream rather than on `WatchHierarchy`; `HierarchyMessage`'s `prompt` member SHALL be removed and `WatchHierarchy` SHALL again stream only hierarchy changes (reverting the widening `rename-files-and-folders` design introduced).
3. WHEN `ContextPromptHost` is rendered THEN it SHALL be driven by the context stream and SHALL live at the workspace-shell level (next to the single `Watch` subscription of Requirement 4.5), not inside `ExplorerTreePanel`, so a prompt raised by an action in any scope can be shown regardless of which panel triggered it.
4. WHEN the move is complete THEN the system SHALL leave `IContextActionProvider`, `IContextActionResolver`, `ContextTarget`, `HierarchyContextActionProvider` and `IContextInteractionStore`'s responsibilities unchanged; only the gRPC surface they are reached through and the channel type prompts are written to change. `ContextInteractionStore.Register` SHALL be called from the context `Watch` stream's lifetime instead of `WatchHierarchy`'s.
5. WHEN an `ExecuteAction` arrives with no `source` (id) THEN the system SHALL apply it to the connection's **current selection** in the given scope, so a ribbon or a global shortcut can say "rename whatever is selected" without first looking the selection up; when a `source` is given it takes precedence, preserving today's explicit-target behaviour.
6. WHEN the existing `rename-files-and-folders` integration tests (`ExplorerContextActionsFlow.Tests.cs`) and explorer unit tests are re-pointed at `ContextService` THEN they SHALL pass with no change to what they assert about user-visible behaviour; tests that assert a prompt arrives on the `WatchHierarchy` stream SHALL be updated to assert it on the context stream.

### Requirement 7 — First consumer: the Property Grid shows the selection

**User Story:** As a user, I want the Properties panel to show what I currently have selected, so that I can see the shared selection mechanism actually works before richer property editing is built on it.

#### Acceptance Criteria

1. WHEN the Property Grid panel is rendered while a selection exists on the connection THEN the system SHALL display, from the context stream (Requirement 4.4), at minimum: the selected entry's name, its project-relative path, its kind (file/folder) and the source that selected it, styled per `diagram-ide-mockup` Requirement 7's theming rules.
2. WHEN nothing is selected THEN the Property Grid SHALL show a clearly intentional "nothing selected" state in place of its current static placeholder, following `diagram-ide-mockup` Requirement 5's placeholder standard, and SHALL keep the existing `futureSpec` hand-off comment for property *editing*, which remains out of scope.
3. WHEN the selection changes THEN the Property Grid SHALL update without any call of its own — it is a pure subscriber of the in-process fan-out (Requirement 4.5).
4. This spec SHALL NOT implement editing any property, nor diagram-element properties — only the display of the hierarchy-scope selection, as proof of the subscription path.

### Requirement 8 — Designed-for consumers that are out of scope here

**User Story:** As a developer planning the next features, I want the context service to be clearly the place they plug in, so that each of them becomes a subscriber rather than a new point-to-point wiring.

#### Acceptance Criteria

1. The spec that implements diagram tabs (`adp-diagram-ide` Requirement 1.2, `project-root-folder-explorer` Requirement 3.4, "select a file node → open it") SHALL be able to open a file purely by subscribing to the context stream for `HIERARCHY`-scope, file-kind selections that carry an explicit *activation* (double-click / `Enter`) — this spec SHALL therefore distinguish a plain selection from an activation in the `Select` message (e.g. an `activated` flag), since a single click selects but must not open.
2. The spec that implements the ribbon's real commands (`diagram-ide-mockup` Requirement 2.3's TODO) SHALL be able to enable/disable buttons from the pushed actions of Requirement 5 and invoke them through `ExecuteAction` against the current selection (Requirement 6.5), with no explorer-specific wiring.
3. The `diagram-undo-redo` spec (Requirement 5: "the diagram it is currently connected to") and `mindmap-diagram` Requirement 6.3 ("navigate to that file within the IDE") SHALL be able to treat "the current diagram" and "navigate to" as, respectively, a `DIAGRAM`-scope selection read from, and a `HIERARCHY`-scope activation written to, this service.
4. A diagram-type module SHALL be able to publish an element selection by adding one `ContextScope` value and one `ContextSource` member and registering its own `IContextActionProvider`, with no change to `ContextService`, the explorer, or the Property Grid's subscription code — the same extensibility `rename-files-and-folders` Requirement 2.6 already guarantees for actions.

## Non-Functional Requirements

### Code Architecture and Modularity

- **Single Responsibility**: `ContextService` owns selection and context interaction only; it SHALL NOT list, watch, rename or delete anything itself — it resolves an id through the owning scope's model (`HierarchyModel` for `HIERARCHY`) and delegates actions to providers, exactly as `HierarchyServiceImpl.Context.cs` does today.
- **Modular Design**: the backend gets a `Context/ContextServiceImpl.cs` (the RPC surface) and a per-connection `IContextSelectionStore`; the existing `Context/` abstractions move nowhere. The client gets one context provider at the shell level and small subscriber hooks; no panel talks to `ContextService` directly except through that provider.
- **Reuse over reinvention**: `ContextScope`, `ContextSource`, `ContextActionGroup`, `ContextPrompt`, `ShortGuid`, `Path`, the `watch_id` discipline and `useDebouncedValue` are all reused; the only new vocabulary is the selection source enum, the select/watch messages and the activation flag.
- **Dependency direction**: `Context/` SHALL NOT reference `Hierarchy/` types directly; resolving a `HIERARCHY` id SHALL go through an injected resolver registered by the hierarchy side (a per-scope `IContextSourceResolver`, the selection-side twin of `IContextActionProvider`), so a future scope registers its own resolver and `Context/` stays scope-agnostic, per structure.md.

### Performance

- A `Select` round trip plus the resulting `Watch` push SHALL complete fast enough that arrow-key navigation in the explorer feels unchanged; the explorer SHALL keep its own focus state locally and never wait for the backend to move the focus ring.
- Pushing the selection's actions (Requirement 5) SHALL NOT be slower than today's `DiscoverActions` call it replaces; it SHALL NOT walk the selected entry's subtree.
- The in-process fan-out (Requirement 4.5) SHALL not re-render every subscriber on every keystroke-driven selection burst — the coalescing of Requirement 3.4 applies before the stream, and subscribers receive only the resting selection.

### Security

- `Select` and `Watch` SHALL be rejected for a project the session's user may not access, and an id SHALL only ever be resolved through the calling connection's own per-`watch_id` model, so neither call can be used to probe entries of another project or connection.
- The only path the contract exposes to the client is the project-relative `Path` of Requirement 4.4; absolute paths SHALL never leave the backend.
- Selection messages SHALL only be pushed on the stream of the connection that made the selection.

### Reliability

- IF a `Watch` stream is opened before any `Select` on that connection THEN it SHALL receive an explicit "nothing selected" baseline, not silence.
- IF a `Select` arrives for a `watch_id` with no open `Watch` stream THEN the system SHALL still record the selection (so backend consumers can read it) and SHALL deliver it as the baseline when a stream opens later.
- Stream loss SHALL discard nothing the client cannot rebuild: on reconnect the client re-selects its current focus (Requirement 3.1) if the backend's baseline reports nothing selected.

### Usability

- From the user's point of view nothing in the explorer changes: focus ring, arrow keys, `F2`/`Delete`, right-click and `Shift+F10` behave exactly as `rename-files-and-folders` specified; the visible difference of this spec is that the Properties panel now follows the selection.
- A consumer that only cares about one scope or one source SHALL be able to ignore every other message by filtering on the message's `scope`/`source` fields, without any consumer-specific message types.
