# Technology stacks

* For the time being the backend services and other capabilities will be implemented in .NET, with the latest SDK configured using global.json.
* For the time being the frontend applications will be web based. This might change with the right reasoning.
* The web client is built with React + TypeScript, rendering the diagram canvas via a canvas/WebGL-based library (e.g. Konva or PixiJS) rather than raw SVG/DOM, for virtualization performance on large diagrams.
* The communication between the frontend and backend services will be gRPC based. Authentication and authorization will be done as decorations on the corresponding gRPC initialization calls. From the browser this happens over grpc-web, backed by ASP.NET Core's gRPC-Web middleware.
* For hosting ASP.NET core is preferred. In production ASP.NET Core serves the built React client as static files and hosts the gRPC-Web endpoint in the same process - one process, no separate frontend server, no Blazor involved.

# Development tools

* The development will mostly be done using Jetbrains Rider.
* The development team prefers an F5 experience, which means that development, testing and debugging should be doable in one go. I.e. locally and without any complex dependencies. To achieve this it should be possible to ramp up all relevant services using local-only persistency and hosting.
* The React client uses Vite as its dev server/build tool, for fast hot-module-reload during local development and a static-file bundle for production. These two do not compete for hosting: in the F5 scenario, ASP.NET Core still owns the port the browser talks to and proxies frontend requests through to the Vite dev server (a standard SPA-proxy pattern) purely so edits hot-reload instantly without a full rebuild; in production Vite is not running at all, and ASP.NET Core simply serves Vite's static build output directly, alongside the gRPC-Web endpoint, in that same single process.

# Naming & Identity

* The organization is called 'EtAlii', mind the uppercase E and A.
* The product name is an abbreviation 'ADP', which stands for 'A Different Perspective'.
* When using namespaces, include the company name and product name. In .NET world this would be 'EtAlii.Adp', for other languages where appropriate it would be 'com.etalii.adp'.
* Use the Base36 based ShortId everywhere where an ID or identity is needed.

# Runtime

The idea is that the solution can:

* Run standalone locally.
  * Using a backend service that makes local file access possible.
  * A client that runs as a web page in the browser.
  * A client that runs as a locally hosted web application (i.e. without the chrome of a web browser).
* Can later on also be hosted somewhere to facilitate runtime architectural design changes.
* Can later be partially embedded in VS Code.

# Diagram storage

* Diagrams are persisted as plain, text-based files under the user's chosen workspace folder - never in a database - so they remain diffable and version-control-friendly.
* Wherever possible already existing text-based file formats will be used. If needed additional files will be used to store meta-data (for example to link to other elements/diagrams).
* The backend is the sole owner of reading/writing diagram files; the web client never touches the filesystem directly, it only talks gRPC to the backend.
* File-system access from the backend is scoped to explicitly opened workspace folders, not the whole machine.

# Frontend-backend synchronization

* State changes flow from backend to client as a gRPC stream per open diagram, so multiple clients (and external file edits picked up by the backend) stay in sync without manual refresh.
* The client is expected to reconcile incoming pushed changes against local, not-yet-saved edits without silently discarding user input. These changes are always deltas, i.e. add/remove/update and similar messages.&#x20;
* Large diagrams are handled through UI-side virtualization (only rendering what's visible) rather than by limiting what the backend can store. The virtualization is supported by view information that is send from the client to the backend, so that it can then decide which updates to send. This indirectly requires the backend to remember the state of the view in the client per connection.&#x20;

# gRPC call shapes

There is no bidirectional streaming in this system, and no client streaming either. A browser talking grpc-web cannot open either one, so a contract that needs two-way traffic is expressed as **two correlated one-way legs**.

* **Client to backend: unary `Action` calls.** Anything the client wants to do - report a selection, run an action, propose a value, submit or cancel an interaction, update its viewport - is a plain unary request/response. `ContextService`'s `Select`, `ExecuteAction`, `ProposeInput`, `SubmitInteraction` and `CancelInteraction` are the worked examples.
* **Backend to client: one server-streaming `Watch` call.** Everything the backend initiates - pushed state, change deltas, prompts, availability updates - rides the one stream that connection already has open. `ContextService.Watch` and `HierarchyService.WatchHierarchy` are the worked examples. A feature that needs to push something new adds a member to that stream's message, never a second stream.
* **The two legs are correlated by a connection id.** The client generates it once per mounted shell and carries it on every call of both legs; today it is the `watch_id` `ShortGuid` the hierarchy and context calls already share. It is what lets an `Action` call on one HTTP request affect the stream opened by another.
* **One stream per connection, not one per consumer.** The client opens `Watch` once and fans the result out in-process (a React context at the shell level). Several panels wanting the same push is not a reason for several streams.
* **Per-connection state is keyed by the connection id and dies with the stream.** `IHierarchyModelStore`, `IContextSelectionStore` and `IContextInteractionStore` all hold their state 1:1 per `watch_id` and discard it when the stream ends - never shared with another connection, not even one from the same user on the same project.
* **Reconnect by reopening `Watch` with the same connection id.** The stream's first message is always the current state, so a reconnected client is re-baselined without a reconciliation protocol of its own.

# Implementation order

Work a feature through these layers in this order, finishing each - including its tests - before starting the next:

1. **Data model.** The POCOs and records the feature is about, in the `_Model` folder of the area they belong to. They depend on nothing else and are testable on their own.
2. **Persistence.** Reading and writing that model - text files under the opened workspace folder, per *Diagram storage*, never a database.
3. **Wire protocol.** The `.proto` contract that carries it and the mapping between those messages and the model, per *gRPC call shapes*.
4. **Client user interface.** Rendering it, and letting the user act on it.
5. **Additional client and backend business logic.** Whatever the feature still needs once the layers under it hold.

* **Each step carries its own tests.** Unit tests where the step is a unit - a model, a parser/serializer, a mapper, a component - and integration tests where the point is that layers meet, such as a gRPC flow against the real host or a file written and read back. A step is done when something fails without it, not when it compiles. Naming and the arrange/act/assert shape are in *Testing & quality*.
* **A step with nothing in it is skipped, not invented.** A feature that persists nothing gets no persistence step. This is an order of what comes before what, not a checklist every feature has to fill.
* **The order runs bottom-up because the dependencies do.** A wire contract designed before the model it carries ends up shaped by the transport rather than by the domain, and a UI built before the contract invents state the backend then has to be talked into providing. Taken in this order, each layer is written against something that already exists and already passes its tests.

# Testing & quality

* Tests should be runnable as part of the same local "F5 experience" - no separate environment or manual setup required to run the test suite.
* Prefer fast, local unit/integration tests over end-to-end tests that depend on hosted infrastructure, given the local-first runtime model.
* To test the implementation of the modular diagrams use the following diagram visualizations:&#x20;
  * Mindmap (file extension \= .mm)&#x20;
* Test classes in C# should follow the filename '\<Classname>.Tests' and the class name \<Classname>Tests. Mind the dot.
* Tests should follow the tripple a pattern: arrange, act, assert.

# Frontend

* All styling for the application should be done in a centralized manner, avoiding inline styles and promoting consistency and maintainability as much as possible.

# Backend

* For a specific subsystem or component, place simple POCO objects and objects that represent data rather than functionalities in a dedicated subfolder called '\_Model'.
* Follow the one entity per file principle. One exception to this is a CommandHandler and the Command it handles.

# Context

The context service owns what is selected and what can be done with it. Every feature that has a notion of "the thing the user is working on" goes through it rather than growing a selection of its own.

* **One selection, held in one place.** `ContextService` is the single owner of a connection's selection. No surface keeps its own copy, and nothing calls the backend to ask what is selected - the answer is pushed over the context stream, with the resolved chain and the innermost level's actions in the same message.
* **Make something selectable by registering an `IContextSourceResolver`**, never by adding a bespoke RPC. A diagram element, a search hit, an entry in an error list: each is one resolver, registered once, and the service is not touched.
* **Offer what a user can do through an `IContextActionProvider`** for the relevant `ContextScope`. Actions discovered this way reach the ribbon, the right-click menu and the keyboard shortcut through the same path, so a feature never wires up three of them.
* **Consumers are pure subscribers.** The ribbon, menus and panels read the pushed selection and its actions and call nothing but `ExecuteAction`. A consumer that needs more information asks for it to be carried on the selection rather than resolving it again itself.
* **Extend the chain, don't add a parallel message.** A selection carries a source, a path, an id, and a nested `none | action | child` choice. A feature that needs to say more should nest a level or add detail to one, so every existing consumer keeps working unchanged.
* Registering a resolver or a provider is one line in `Program.cs`. If a new feature needs a change inside the context service itself, that is a sign the abstraction is wrong - fix the abstraction rather than special-casing the feature.

# Commands

Anything that changes state is a command. This is what makes undo/redo a property of the system rather than something each feature has to remember to support.

* **Every functional action is an `ICommand` with a matching `ICommandHandler<TCommand>`**, dispatched through `IHistoryStack`. A service, a gRPC method or a context action provider never performs the change inline - it builds the command and dispatches it.
* **A handler reports the command that reverses it** as `CommandResult.Inverse`, which is what puts the change on the undo stack. A change that is deliberately not undoable returns plain `CommandResult.Success()`, and the reason it is not undoable belongs in the handler's own documentation.
* **Handlers validate their own preconditions.** Undo and redo dispatch a handler again long after the original call, so it can trust nothing that was checked earlier - not the caller, and not the state of the disk.
* **A rejected command is a `CommandResult`, not an exception.** A name already taken or a file that has since vanished is an expected outcome carrying a message meant for the user. Exceptions stay reserved for programming errors, such as dispatching a command with no registered handler.
* A context action's `CommitAsync` is the point where the two rules above meet: it resolves what the user chose and dispatches a command, and does no filesystem work itself. The explorer's rename, delete and add are the worked examples.
* Handlers live in a `Commands` subfolder of the area they belong to, keeping the namespace of that area, and are registered in `AddCommands` rather than one by one in the host - so a new command has one place to be added and a test can wire up the same pipeline the host does.

# Decision log

1. **File-based storage over a database**: keeps diagrams reviewable and mergeable through normal repository tooling; revisit only if a hosted/multi-user scenario proves this insufficient.
2. **Bi-directional gRPC for frontend-backend communication**: chosen for strongly-typed contracts and native streaming support, which fits the "push changes to open clients" requirement better than plain REST/JSON.
3. **.NET/ASP.NET Core backend**: aligns with the team's Rider-based, F5-first development workflow and existing tooling familiarity.
4. **React + TypeScript client over Blazor WebAssembly**: the canvas/diagramming ecosystem available to React/TypeScript is significantly more mature for a virtualized, WebGL-rendered diagram surface than Blazor's, outweighing the appeal of an all-C# stack; revisit only if a specific chunk of C# logic clearly needs to run client-side.
5. **ASP.NET Core hosts the React build directly**: kept to a single process/runtime in production (static files + gRPC-Web endpoint together) rather than combining Blazor and React, avoiding a JS-interop serialization boundary on the path that streams diagram deltas to the canvas.
6. **One context service rather than per-surface selection**: selection and the actions that apply to it are cross-cutting - the explorer, the ribbon, the canvas and search all need the same answer - so they are resolved once and pushed, instead of each surface keeping its own state and asking its own questions. The cost is one indirection when adding a feature; the return is that a new selectable thing or a new action reaches every surface without any of them changing.
7. **Commands for every state change**: undo/redo is a property users expect of a design tool everywhere, not per feature. Routing changes through `ICommand`/`ICommandHandler` and the history stack makes reversibility the default and forces each change to name its own inverse, rather than leaving each feature to reimplement it or quietly skip it.
8. **Unary `Action` plus streaming `Watch` instead of bidirectional streaming**: grpc-web offers a browser neither bidirectional nor client streaming, so decision 2's "bi-directional gRPC" is realised as two correlated one-way legs rather than one two-way call. Splitting them is not only a workaround: it makes each client-to-backend call individually authorizable and retryable, and it forces per-connection state to have an explicit owner and an explicit lifetime - the connection id it is keyed by, and the stream whose end discards it.
9. **Bottom-up implementation order**: data model, persistence, wire protocol, client UI, then the remaining logic - each finished and tested before the next begins. Building in dependency order means every layer is written against one that already exists and already passes its tests, rather than against an assumption about it. The cost is that a feature shows nothing on screen until fairly late, which is accepted deliberately: discovering at the UI that the model is wrong is cheaper than discovering at the model that the UI already depends on it being wrong.