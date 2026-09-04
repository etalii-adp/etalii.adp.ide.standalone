# Project structure

* We will have a source folder (`src/`) under which the code for all applications will be stored. Folders directly under `src/` might each contain a whole .NET solution (e.g. a backend service, a shared library, a client app) rather than a single project.
* The core `.NET` solution  will be located in  `backend/` as a .slnx file called  `EtAlii.Adp.slnx` .
* This folder will then store a flat list of projects (e.g. `EtAlii.Adp.Backend/`, `EtAlii.Adp.Backend.Tests/`) so the solution can be opened as a whole and built in Rider on its own.
* Under the core C# projects are:
  * EtAlii.Adp (Library)
    Contains all reusable helper classes, mechanisms and extension methods.
  * EtAlii.Adp.Tests
    Contains all unit tests for the code in the EtAlii.Adp project.
  * EtAlii.Adp.Backend (Library)
    Contains the core functionalities of the backend, including the ASP.NET hosting setup.
  * EtAlii.Adp.Backend.Tests
    Contains all unit and integration tests for the code in the EtAlii.Adp.Backend project. Both are separated in distinct folders called `Unit Tests` and `Integration Tests`.
  * EtAlii.Adp.Backend.Diagrams (Library)
    Contains the abstractions and interfaces that all modular diagrams need to implement, as well as all helper and factory logic.
  * EtAlii.Adp.Backend.Diagrams.Tests
    Contains all unit tests for the code in the EtAlii.Adp.Backend.Diagrams project.
  * EtAlii.Adp.Backend.Service (Executable)
    Provides the hosted setup from `EtAlii.Adp.Backend` as a executable that can be used as either a (Windows) service, console application and docker container.
    This application also contains the appsettings.json and appsettings.developer.json and can also be run as a console application. The latter is the primary way to test and debug the backend.
    
* The .NET projects will have their dependencies configured correctly, i.e. the project `EtAlii.Adp.Backend` will depend on  `EtAlii.Adp` and not the other way around. Also PackageReferences will be put at the highest 'parent' project where they are used, but not deeper. But if multiple 'child' projects use the same packages their references will be put in the shared parent project,.
* The web frontend lives in its own folder under `src/` called `client/` alongside, not inside, the .NET solution folders it talks to over gRPC.
* Shared contracts (`.proto` files and generated gRPC stubs) live in a dedicated shared folder `api/` so both backend and frontend build against the same source of truth, instead of copies drifting apart.
* For each diagram, it's specific code and logic will be put in a diagram module folder as `diagrams/<diagram>`, in which the functionalities are split into `backend/`, `api/` and `client/`. backend will contain at least a C# project called `EtAlii.Adp.Diagram.<diagram>` and `EtAlii.Adp.Diagram.<diagram>.Tests`. The `api/` folder contains all relevant .proto definitions. the `client/` will contain all files required for the web client to visualize the diagram and facilitate all interactions. The `examples/` folder holds one or more example ADP projects for that diagram type: documents a reader can open and click around in, kept distinct from the test fixtures under `backend/`, which exist to pin the parser one construct at a time.
* Text editors follow the same shape at `editors/<editor>/`, beside `diagrams/`: `backend/` holds `EtAlii.Adp.Editor.<Editor>` and `EtAlii.Adp.Editor.<Editor>.Tests`, `api/` its .proto definitions (if it needs any), `client/` everything the web client needs to render and drive it, and `examples/` its sample files - which the module's own tests open, so the examples cannot drift from what the code supports.
* The examples from every module, diagram or editor, are **copied** into the combined `src/examples/` folder (the steering docs used to say "the root of the repository"; `src/examples/` is where the tests-covered combined project actually lives). That folder is nested - `src/examples/<module>/<example>/` - because it doubles as a single ADP project that shows off every type at once: `src/examples/` is the folder you open, and every example appears in the one explorer tree. **The two copies are no longer held in sync**, as of 2026-09-04. They were, byte-for-byte, and a test enforced it - but the combined project is a working surface: arranging a diagram in the running app writes a `layout:` block into the showcase copy while the module copy stays put, so the guard read five instances of ordinary product use as drift. What still holds for both trees is that every example registration opens against the deployed catalog, which `ExampleRegistrationTests` walks in both places. Seeding a new module's examples into `src/examples/` is still how a type joins the showcase; keeping the two identical afterwards is not.

# Naming

* The organization is 'EtAlii' (uppercase E and A). The product is 'ADP' - 'A Different Perspective'.
* .NET namespaces/assemblies: `EtAlii.Adp.<Area>` (e.g. `EtAlii.Adp.Backend`, `EtAlii.Adp.Diagrams`). Test projects append `.Tests`.
* A project's `_Model/`, `Commands/`, `History/` and `Support/` folders stay in the project's own namespace rather than taking one of their own. A caller writes `using EtAlii.Adp.Diagram.C4;` and has the model, the commands and the history. Every other folder namespaces normally. (`IDE0130` is set to `none` in `src/.editorconfig` for this, with the counts that show it is a convention.)
* Non-.NET code (e.g. TypeScript on the frontend) mirrors the same naming intent using the language's own convention, e.g. `com.etalii.adp.<area>`.
* Files/types: `PascalCase` for .NET types and files; the frontend follows whatever convention its framework/tooling defaults to, kept consistent within that project.

# Module boundaries

* **Backend vs frontend**: the backend owns all filesystem access and diagram persistence; the frontend never reads/writes files directly, only through the gRPC contract.
* **Core vs diagram-type plugins**: canvas rendering, storage, and sync infrastructure must not depend on any single diagram type's schema, so new diagram types can be added without touching core code.
* **Contracts vs implementation**: `.proto`-defined contracts are the stable boundary between backend and frontend; implementation details on either side can change without breaking the other as long as the contract holds.
* **Dependency direction**: diagram-type-specific code may depend on core abstractions; core code must never depend on a specific diagram type.

# Code organization principles

1. **Single responsibility**: each file type has one clear purpose - a diagram-type plugin, a gRPC service, a UI component, etc.
2. **Modularity**: prefer small, composable projects/modules over one large project, so a solution stays fast to open and build under the F5 workflow.
3. **Testability**: business logic (diagram model, storage, sync reconciliation) should be testable without a running gRPC server or a browser.
4. **Consistency**: new areas of the codebase should follow patterns already established elsewhere in the solution rather than introducing a competing convention.

# Documentation standards

Moved to [processes.md, *Keeping documentation true*](processes.md#keeping-documentation-true): `.proto` files are the primary API documentation for the public gRPC contracts, and non-obvious architectural decisions belong in `tech.md`'s decision log rather than scattered through the code.
