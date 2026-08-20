# Technology stacks

* For the time being the backend services and other capabilities will be implemented in .NET, with the latest SDK configured using global.json.
* For the time being the frontend applications will be web based. This might change with the right reasoning.
* The communication between the frontend and backend services will be gRPC based. Authentication and authorization will be done as decorations on the corresponding gRPC initialization calls.
* For hosting ASP.NET core is preferred.

# Development tools

* The development will mostly be done using Jetbrains Rider.
* The development team prefers an F5 experience, which means that development, testing and debugging should be doable in one go. I.e. locally and without any complex dependencies. To achieve this it should be possible to ramp up all relevant services using local-only persistency and hosting.

# Naming

* The organization is called 'EtAlii', mind the uppercase E and A.
* The product name is an abbreviation 'ADP', which stands for 'A Different Perspective'.
* When using namespaces, include the company name and product name. In .NET world this would be 'EtAlii.Adp', for other languages where appropriate it would be 'com.etalii.adp'.

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

# Testing & quality

* Tests should be runnable as part of the same local "F5 experience" - no separate environment or manual setup required to run the test suite.
* Prefer fast, local unit/integration tests over end-to-end tests that depend on hosted infrastructure, given the local-first runtime model.
* To test the implementation of the modular diagrams use the following diagram visualizations:&#x20;
  * Mindmap (file extension \= .mm)&#x20;

# Decision log

1. **File-based storage over a database**: keeps diagrams reviewable and mergeable through normal repository tooling; revisit only if a hosted/multi-user scenario proves this insufficient.
2. **Bi-directional gRPC for frontend-backend communication**: chosen for strongly-typed contracts and native streaming support, which fits the "push changes to open clients" requirement better than plain REST/JSON.
3. **.NET/ASP.NET Core backend**: aligns with the team's Rider-based, F5-first development workflow and existing tooling familiarity.