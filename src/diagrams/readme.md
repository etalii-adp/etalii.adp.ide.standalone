# Diagrams

This folder contains every diagram type ADP supports, one module folder per diagram type. Each is a self-contained plugin: core code (`src/backend`, `src/client`) never depends on a specific diagram type, only on shared abstractions — a diagram module may depend on those, never the other way around.

For the full catalog of tool types — diagrams, designers and editors, including ones only identified or specified, not yet implemented here — see [`docs/tools.md`](../../docs/tools.md). That document tracks each type's status (identified, specified, to-do, work-in-progress, implemented); this folder only contains the ones that have reached at least work-in-progress.

## Module layout

Each diagram type gets its own folder, named after the diagram, containing up to four subfolders:

```
src/diagrams/<diagram>/
├── backend/   EtAlii.Adp.Diagram.<Diagram> and EtAlii.Adp.Diagram.<Diagram>.Tests
├── client/    TypeScript and related code for the web client
├── api/       .proto extensions (messages, services) specific to this diagram type
└── examples/  documents a reader can open and click around in
```

- **`backend/`** — a C# project named `EtAlii.Adp.Diagram.<Diagram>`, plus a corresponding `EtAlii.Adp.Diagram.<Diagram>.Tests` project. This is where the diagram type's own persistence, validation, and any other server-side logic live.
- **`client/`** — the TypeScript (and related) code the web client uses to render and interact with this diagram type. Lives beside `backend/`, not inside it, mirroring the top-level `src/backend` / `src/client` split.
- **`api/`** — any `.proto` messages and services this diagram type adds on top of the shared core contract in `src/api/`. Kept separate from `src/api/` so core contracts and diagram-type contracts don't drift into the same file.
- **`examples/`** — example documents with their `.adp` registrations, one folder per set. Every registration in them is opened against the deployed catalog by `ExampleRegistrationTests`, and they are seeded into `src/examples/`, where the copies are not held byte-for-byte in sync unless a module's own tests insist on it ([creating-a-diagram-module.md](../../docs/creating-a-diagram-module.md#tests-fixtures-and-examples) says why).

The first three mirror `src/`'s own top-level split (`api/`, `backend/`, `client/`), applied per diagram type instead of once for the whole app.
