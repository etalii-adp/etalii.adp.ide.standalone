# ADP — A Different Perspective

Architecture that lives in the repository, as files, beside the code it describes.

[![Build](https://github.com/etalii-adp/etalii.adp.ide.standalone/actions/workflows/build.yml/badge.svg)](https://github.com/etalii-adp/etalii.adp.ide.standalone/actions/workflows/build.yml)

## Why

Software architecture tooling stands at a crossroad. The large enterprise modelling suites enforce strong, strict principles — and pay for them by slowing development down, which makes them steadily less tenable in a landscape where AI-assisted development keeps raising the pace. The usual escape is to stop doing architecture deliberately at all, and lose the shared picture just when a team (and its AI tools) needs it most.

ADP — "A Different Perspective" — takes a different stance: architectural artifacts belong **in the solution repository**, as plain, diffable files, under the same version control as the code they describe. A diagram here is not a picture exported from a modelling database; it is a text document you can review in a pull request, branch, merge, and blame — and open in ADP's web workspace to view and edit it as a live diagram. Where a well-known notation or file format already exists, ADP embraces it rather than inventing its own: a mindmap is a Freeplane file, a C4 model is a Structurizr DSL workspace, a Wardley map is an OnlineWardleyMaps document — each still openable by the tool that owns the format, byte-for-byte, after ADP has edited it.

Because the artifacts sit beside the code, they can do more than illustrate: diagram elements can link to the code they represent, so the architecture stays legible — to humans reading the repository, and to the AI tools increasingly discussing it — as the code evolves. The editing surface is a web workspace deliberately close to VS Code (an explorer, tabs, panels, a property grid), so adopting ADP does not mean learning a new tool from scratch. Changes to the underlying files — from ADP itself, another editor, or a `git pull` — are pushed live into open views.

And it starts small on purpose: one process, one repository, local files, no server infrastructure to roll out. ADP should already be worth having in a single repository, before any wider adoption decision is made. The full product thinking lives in [`product.md`](.spec-workflow/steering/product.md).

## What it looks like

The workspace, with a C4 architecture diagram open from the bundled examples:

![The ADP workspace](docs/screenshots/workspace.png)

A few of the diagram types, each backed by a plain text file:

| | |
|---|---|
| ![Mindmap](docs/screenshots/mindmap.png) | ![Wardley map](docs/screenshots/wardley-map.png) |
| ![Timeline](docs/screenshots/timeline.png) | ![Azure pipeline](docs/screenshots/azure-pipeline.png) |

And a dependency graph, plus the markdown editor with its live preview and heading outline:

| | |
|---|---|
| ![Dependency graph](docs/screenshots/dependency-graph.png) | ![Markdown editor](docs/screenshots/markdown-editor.png) |

Every screenshot is taken from the [`src/examples/`](src/examples/) project — how, exactly, is recorded in [`docs/screenshots/readme.md`](docs/screenshots/readme.md), so any of them can be retaken after the UI changes.

## Getting started

### From a release

Each green build of `develop` publishes a versioned ZIP on the [Releases page](https://github.com/etalii-adp/etalii.adp.ide.standalone/releases). It is platform-neutral and needs the .NET 10 runtime:

1. Unpack the ZIP.
2. Set a username and credential under `LocalAuthenticator` in `appsettings.json` — the shipped values are deliberately empty.
3. Run `dotnet EtAlii.Adp.Backend.Service.dll` and browse to the address it logs.

### From source

You need the .NET SDK pinned by [`src/global.json`](src/global.json) and Node 22.

1. `npm install` in `src/` — the client and the diagram modules form one npm workspace.
2. Start the backend from `src/backend/EtAlii.Adp.Backend.Service/` with `dotnet run` in the `developer` environment (Rider/VS: the default launch profile does this; it listens on port 5080, which is the address your browser uses).
3. Start the client dev server from `src/client/` with `npm run dev` (port 5174 — the backend proxies to it, so edits hot-reload without a rebuild).
4. Browse to `http://localhost:5080` and log in with the developer credentials from [`appsettings.developer.json`](src/backend/EtAlii.Adp.Backend.Service/appsettings.developer.json): `admin` / `changeme`.

To run the repository's own gates — backend tests, style, client tests, typecheck — use the exact commands [CLAUDE.md](CLAUDE.md) defines; the CI pipeline runs the same four.

### Your first session

1. Log in.
2. Add a project: point ADP at a folder. Start with this repository's own [`src/examples/`](src/examples/) — it is a ready-made ADP project showing every implemented diagram type and editor in one explorer tree.
3. Open diagrams from the explorer, drag things around, and watch the underlying text files change — then `git diff` them.

## Going deeper

- **[The architecture](docs/architecture.md)** — what ADP is and how its parts talk: one process serving the client and the gRPC endpoint, the two call legs that stand in for bidirectional streaming, where a diagram lives, and what the canvas actually is.
- **[The solution structure](docs/solution-structure.md)** — where the code is: the folders under `src/`, how the solution's projects split, which project owns which concern, and the counting traps that make a plausible wrong answer about all of it.
- **[The diagram type catalog](docs/diagrams.md)** — every diagram type ADP could support, from merely identified through specified to implemented, with the state of each tracked as the repository moves.
- **[Creating a diagram module](docs/creating-a-diagram-module.md)** — the developer walkthrough for adding a new diagram type, traced against a real shipped module.
- **[Creating an editor module](docs/creating-an-editor-module.md)** — the same for the text-editor plugin family.
- **[Dependencies](docs/dependencies.md)** — what ADP is built on: every direct dependency with its version, the reason it is there, and its license.
- **[The guards](docs/guards.md)** — the tests and checks that hold properties of the whole tree rather than the behaviour of one subject, each with the question it answers, so you can tell before you start whether one already covers what you are about to change.
