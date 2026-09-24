# Project structure

Where each folder and project actually lives, what the solution holds, which project owns which concern, and the counting traps around all of it: [docs/solution-structure.md](../../docs/solution-structure.md). **That page is the inventory and it is guarded; this section keeps only the rules that constrain what you write.**

* Source lives under `src/`. A folder directly under `src/` may hold a whole .NET solution rather than a single project. The core solution is `src/backend/EtAlii.Adp.slnx`, and its projects are a flat list so it opens and builds as a whole in Rider.
* `EtAlii.Adp.Backend.Service` is the executable that hosts `EtAlii.Adp.Backend`, and holds `appsettings.json` and `appsettings.developer.json`. It runs as a console application, which is the primary way to test and debug the backend. Hosting it as a Windows service or in a docker container was intended and is not set up: nothing configures Windows-service hosting, and the tree has no Dockerfile.
* `EtAlii.Adp.Backend.Tests` keeps its unit and integration tests in separate folders, `Unit Tests` and `Integration Tests`.
* The web frontend lives in its own folder `client/` under `src/`, alongside and not inside the .NET solution folders it talks to over gRPC.
* Shared contracts - `.proto` files and the generated stubs - live in the shared `api/` folder, so backend and frontend build against one source of truth instead of copies that drift.
* A diagram's own code goes in `diagrams/<diagram>/`, split into `backend/`, `api/`, `client/` and `examples/`. The backend part holds at least `EtAlii.Adp.Diagram.<Diagram>` and `EtAlii.Adp.Diagram.<Diagram>.Tests`.
* Text editors take the same shape at `editors/<editor>/`, beside `diagrams/`, with `EtAlii.Adp.Editor.<Editor>` and its `.Tests`.
* **Dependency direction**: `EtAlii.Adp.Backend` depends on `EtAlii.Adp`, never the other way around.
* **Where a `PackageReference` goes**: the highest parent project that uses it and no deeper; where several children need the same package, the parent they share.
* A module's `examples/` folder holds documents a reader can open, kept distinct from the test fixtures under `backend/`, which exist to pin the parser one construct at a time. **Seeding those examples into `src/examples/` is how a type joins the showcase** - and the showcase nests by how a diagram reads rather than by module, so list that folder rather than building a path by rule. The two copies are not kept in sync tree-wide, but a module may hold its own identical from its own tests, and three do.

# Naming

The fuller treatment, including the identity type and what the skipped folders do to a namespace, is in [docs/solution-structure.md, *Namespaces, and the folders that do not contribute*](../../docs/solution-structure.md#namespaces-and-the-folders-that-do-not-contribute). These are applied while writing, so they stay here:

* The organization is 'EtAlii' (uppercase E and A). The product is 'ADP' - 'A Different Perspective'.
* .NET namespaces and assemblies are `EtAlii.Adp.<Area>` - `EtAlii.Adp.Backend`, `EtAlii.Adp.Context`, `EtAlii.Adp.Diagram`. Test projects append `.Tests`. **`EtAlii.Adp.Diagram` is singular and carries no `Backend` segment**; a plural or `Backend`-prefixed form has been written down as though it existed three times, once as this rule's own example. A guard rejects any `EtAlii.Adp.*` name in this file that is not a project in the solution, and it cannot tell a denial from a claim - which is why the wrong forms are not spelled out here.
* A project's `_Model/`, `Commands/`, `History/` and `Support/` folders stay in the project's own namespace rather than taking one of their own. A caller writes `using EtAlii.Adp.Diagram.C4;` and has the model, the commands and the history. Every other folder namespaces normally. (`IDE0130` is set to `none` in `src/.editorconfig` for this, with the counts that show it is a convention.)
* Non-.NET code mirrors the same naming intent in the language's own convention, e.g. `com.etalii.adp.<area>`.
* Files and types: `PascalCase` for .NET types and files; the frontend follows whatever its tooling defaults to, kept consistent within that project.
* Use `ShortGuid` wherever an ID or identity is needed - a `Guid` in a fixed-length base36 form.

# Module boundaries

How the parts fit together and why is in [docs/architecture.md](../../docs/architecture.md). The boundaries themselves constrain what you may write, so they stay here:

* **Backend vs frontend**: the backend owns all filesystem access and diagram persistence; the frontend never reads or writes files directly, only through the gRPC contract.
* **Core vs diagram-type plugins**: canvas rendering, storage and sync infrastructure must not depend on any single diagram type's schema, so a new diagram type can be added without touching core code.
* **Contracts vs implementation**: `.proto`-defined contracts are the stable boundary between backend and frontend; either side's implementation may change without breaking the other as long as the contract holds.
* **Dependency direction**: diagram-type-specific code may depend on core abstractions; core code must never depend on a specific diagram type.

# Code organization principles

1. **Single responsibility**: each file type has one clear purpose - a diagram-type plugin, a gRPC service, a UI component, etc.
2. **Modularity**: prefer small, composable projects/modules over one large project, so a solution stays fast to open and build under the F5 workflow.
3. **Testability**: business logic (diagram model, storage, sync reconciliation) should be testable without a running gRPC server or a browser.
4. **Consistency**: new areas of the codebase should follow patterns already established elsewhere in the solution rather than introducing a competing convention.

# Documentation standards

Moved to [processes.md, *Keeping documentation true*](processes.md#keeping-documentation-true): `.proto` files are the primary API documentation for the public gRPC contracts, and non-obvious architectural decisions belong in `tech.md`'s decision log rather than scattered through the code.
