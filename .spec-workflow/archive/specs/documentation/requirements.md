# Requirements Document

## Introduction

This repository has no front door. There is no readme at the root, so the first thing a visitor sees is a folder listing, and the first explanation of what ADP *is* lives in `.spec-workflow/steering/product.md` — a file written for the people building the product, in a folder nobody browsing a repository would think to open. Meanwhile the answer to "how do I add a diagram type?" exists only as a trail through dozens of existing modules, a handful of scattered folder readmes, and the steering documents — enough for the agents that built those modules, and far too little for a newcomer.

This spec covers three deliverables that fix that:

1. **The primary readme** at the repository root: why ADP exists, whether the build is green, how to run it, what it looks like, and where to go next.
2. **Developer documentation for creating a new diagram or editor module**: a written path through the two plugin families the repository already ships — dozens of diagram modules under `src/diagrams/` and the editor family under `src/editors/` — naming the touch points a newcomer cannot guess.
3. **A dependency inventory** (`docs/dependencies.md`): what the backend and the client depend on, at which version, why, and under which license — readable at a glance and guarded against going stale.

All three deliverables document a system that already exists. Nothing here changes product behaviour; the risk this spec manages is not "will it work" but "will it be true" — documentation that drifts from the code is worse than none, so the requirements below put as much weight on verification and refresh rules as on the prose itself.

## Alignment with Product Vision

`product.md` positions ADP for "developers and architects who already live inside a code repository", with "minimal footprint, incremental value" — a team should get something useful from ADP after adding it to one repository. That promise fails at the doorstep if the repository itself cannot explain what it is for or how to start it. A readme that leads with the *why* (the same why `product.md` records) is that principle applied to ADP's own repository.

The developer documentation serves the pluggability that `structure.md` mandates ("new diagram types can be added without touching core code") and that the `wardley-map` spec's Requirement 12.5 treats as an acceptance test. A plugin architecture whose extension path exists only in the heads of its authors is pluggable in name only; writing the path down — and verifying it against a real module — is what makes the claim usable by someone who was not here.

The dependency inventory serves the same "don't reinvent, integrate" principle from the other side: a product built deliberately on established formats and libraries should be able to say what it stands on, why, and under which licenses — an answer a team evaluating ADP for adoption needs before any wider rollout decision.

## Requirements

### Requirement 1 — A readme at the root, leading with the why

**User Story:** As a visitor landing on the repository, I want the first page to tell me what ADP is for and what problem it solves, so that I can decide whether it is relevant to me without reverse-engineering the folder tree.

#### Acceptance Criteria

1. WHEN the repository is opened on GitHub or cloned THEN a readme SHALL exist at the repository root and SHALL be the file GitHub renders on the repository's front page. It SHALL be named `readme.md`, lower-case, matching the naming of every other readme in this repository (`src/diagrams/readme.md`, `src/editors/readme.md`, fixture readmes throughout).
2. WHEN the readme opens THEN its first section SHALL explain **why ADP exists**: the tension it answers (architecture tooling that keeps up with AI-accelerated development instead of slowing it down) and its core stance (architectural artifacts live in the solution repository, as plain diffable files, under the same version control as the code they describe). This section SHALL be derived from `.spec-workflow/steering/product.md` and SHALL NOT contradict it.
3. WHEN the why is written THEN it SHALL be prose about the problem and the approach, NOT a bulleted feature list. Features may be *named* in service of the why (diagramming on files, element-to-code linkage, live pushed updates, a VS Code-like surface), but a reader SHALL be able to answer "what is this for?" from the first section alone.
4. WHEN the readme names the product THEN it SHALL spell out the acronym once — ADP, "A Different Perspective" — and use the organisation and product names as `structure.md` defines them (organisation "EtAlii", product "ADP").
5. IF a statement in the readme duplicates a statement in `product.md` THEN the readme is the derived copy: WHEN `product.md` changes in a way that falsifies the readme THEN the readme SHALL be updated with it. The readme SHALL NOT become a second, competing product definition.

### Requirement 2 — A build badge that is honest about a private repository

**User Story:** As a reader of the readme, I want to see at a glance whether the build is green, so that I know the state of the code I am about to clone.

#### Acceptance Criteria

1. WHEN the readme renders THEN it SHALL show the status badge for the `Build` workflow (`.github/workflows/build.yml`) of `vrenken/EtAlii.Adp`, using this markdown **exactly as given by the user**: `[![Build](https://github.com/vrenken/EtAlii.Adp/actions/workflows/build.yml/badge.svg)](https://github.com/vrenken/EtAlii.Adp/actions/workflows/build.yml)` — the badge image linking to the workflow's runs page.
2. WHEN the badge mechanism is chosen THEN the choice SHALL account for the repository being **private**. GitHub's native workflow badge (`…/actions/workflows/build.yml/badge.svg`) is served only to viewers authenticated with read access; third-party badge services (e.g. shields.io) cannot see a private repository at all without a token, and embedding a token in a readme is not acceptable. The requirement is therefore: use the **native GitHub badge**, and accept that its audience is exactly the readme's audience — anyone who can see a private repository's readme is, by definition, an authenticated reader for whom the badge resolves. The readme SHALL NOT embed tokens, and SHALL NOT use a third-party badge service for this repository's private state.
3. IF the repository is later made public THEN the same native badge keeps working unchanged; the spec SHALL note this so the choice is understood as future-proof rather than a compromise to revisit.
4. The given markdown carries no `?branch=develop` parameter, so which run the badge reports is left to GitHub's defaulting rules; a branch pin remains the noted alternative if the badge is ever observed reporting a run other than the latest on `develop`, but the markdown as given in criterion 1 is the requirement.

### Requirement 3 — How to use: from download or checkout to a running app

**User Story:** As a developer who wants to try ADP, I want the readme to tell me what to run and what to do once it is up, so that my first session does not start with source-diving.

#### Acceptance Criteria

1. WHEN the readme explains running a **release** THEN it SHALL match what the release pipeline actually publishes: a platform-neutral ZIP per release on the repository's Releases page, run with `dotnet EtAlii.Adp.Backend.Service.dll` on a machine with the .NET 10 runtime, followed by browsing to the address it logs — and it SHALL state the one setup step the shipped configuration deliberately requires: setting a username and credential under `LocalAuthenticator` in `appsettings.json` before the first login.
2. WHEN the readme explains running **from source** THEN it SHALL match the developer workflow the repository actually uses: the .NET SDK pinned by `src/global.json`, `npm install` at `src/`, the backend service started from `src/backend/EtAlii.Adp.Backend.Service/` in the `developer` environment, and the Vite dev server started from `src/client/` — including the developer-environment credentials `appsettings.developer.json` ships (`admin` / `changeme`) and the default ports (backend 5080, client dev server 5174, with the backend the address the browser uses).
3. WHEN the readme explains what to do **once the app is up** THEN it SHALL walk the first session in the app's own terms: log in, add a project by pointing ADP at a folder, and open diagrams from the explorer — and it SHALL name `src/examples/` as the folder to point at first, since that combined project exists precisely to show every implemented type in one explorer tree.
4. WHEN commands are given THEN each SHALL be one the repository's own gates or docs already use, verbatim where one exists (CLAUDE.md's test command, the release readme's run command) — the readme SHALL NOT invent variant command lines that then drift from the enforced ones.
5. IF a how-to-use step duplicates a rule CLAUDE.md already states (how to run the backend tests, the style gate) THEN the readme SHALL summarise in one line and link, not restate. CLAUDE.md is written for agents working on the repository; the readme is written for people using it — the two SHALL reference rather than fork each other, because this repository has already been bitten by two copies of one rule drifting.

### Requirement 4 — Screenshots, treated as real work

**User Story:** As a reader deciding whether ADP is worth my time, I want to see the actual editors and diagrams, so that the claims about a VS Code-like surface and rich diagram types are shown rather than asserted.

#### Acceptance Criteria

1. WHEN the readme shows the product THEN it SHALL include screenshots covering at least: the overall workspace (explorer, an open diagram tab, and side panels visible in one shot), and a selection of individual diagram types drawn from the example documents under `src/examples/diagrams/` — which SHALL include at least four of: ansible-structure, azure-pipeline, c4, dependency-graph, mindmap, timeline, wardley-map — plus one text editor from `src/examples/editors/`.
2. WHEN a screenshot is captured THEN its source SHALL be a document from `src/examples/` opened in the running app, so every screenshot is reproducible by anyone from repository content alone — no private projects, no invented data.
3. WHEN screenshot files are stored THEN they SHALL live under `docs/` in a dedicated folder (e.g. `docs/screenshots/`), as PNG, referenced from the readme by relative path so they render on GitHub and in local clones alike. Each image SHALL be capped at a sensible size for a readme (target ≤ 300 KB each; the workspace overview may be larger but SHALL stay under 1 MB), captured at a consistent window size so the gallery does not look ransom-note assembled.
4. WHEN binary images enter this repository THEN the `.gitattributes` question SHALL be answered rather than assumed: the existing root rule is `* text=auto eol=crlf`, and `text=auto` already leaves files git detects as binary (PNG included) untouched — so **no new attribute line is required for correctness**. The spec records this reasoning explicitly; IF the design nevertheless adds a `*.png -text` line for belt-and-braces clarity THEN that is acceptable, but it SHALL carry a comment saying it is documentation of intent, not a fix for a real hazard.
5. WHEN screenshots are added THEN a capture procedure SHALL be recorded beside them (a readme in the screenshots folder): which example document, which theme, which window size, what is selected — enough that a person (or an agent with the in-app browser) can retake any screenshot after a UI change and get a comparable image.
6. WHEN the UI changes in a way that makes a screenshot misleading (a renamed panel, a redesigned canvas, a different chrome) THEN the screenshot SHALL be retaken via that procedure. The readme SHALL NOT keep screenshots of a UI that no longer exists; a stale screenshot is a false claim with a picture attached.

### Requirement 5 — The readme's onward links

**User Story:** As a reader whose question the readme does not answer, I want the readme to hand me to the right deeper document, so that the front page stays short without becoming a dead end.

#### Acceptance Criteria

1. WHEN the readme ends THEN it SHALL link to `docs/diagrams.md` as the catalog of every diagram type ADP could support, described as such — including that the catalog tracks each type's state from identified through implemented.
2. WHEN the readme ends THEN it SHALL link to the developer documentation of Requirements 6–8 for readers who want to extend ADP with a module of their own.
3. WHEN the readme ends THEN it SHALL link to the dependency inventory of Requirement 9, so what ADP is built on is one click from the front page.
4. WHEN links are written THEN every link to repository content SHALL be relative (working on GitHub, in clones, and in forks), and every linked target SHALL exist — a readme link landing on a 404 fails this requirement.

### Requirement 6 — Developer documentation: creating a diagram module

**User Story:** As a developer who wants ADP to support a new diagram type, I want a written walkthrough of the module path, so that I can build one from the documentation instead of reverse-engineering a neighbouring module.

#### Acceptance Criteria

1. WHEN the developer documentation is written THEN it SHALL live under `docs/`, be linked from the root readme, and cover the diagram-module path end to end at the level of *named touch points with repository paths* — the things a newcomer cannot guess:
   - **Module layout**: `src/diagrams/<diagram>/` with `backend/` (`EtAlii.Adp.Diagram.<Diagram>` + `.Tests`), `api/`, `client/` and `examples/`, per `structure.md` and `src/diagrams/readme.md`.
   - **The definition**: the static `Diagram` class exposing `Definitions`, the `DiagramOrigin` MIME-type convention (`vendor/type`, e.g. `wardley/map`, matching the catalog's Origin tag and the first line of an `.adp` registration), the `DocumentExtension`, and the `Build` delegate through which startup discovery wires the module's services — so adding a module edits **no core file**.
   - **Registration**: the single `Add<Diagram>` service-collection extension gathering the module's seams, and which seams exist (document factory, session factory, context source resolver, context action provider, toolbox provider, context property provider, validator, command handlers) with one sentence each on what a seam is for and which are optional.
   - **The document path**: the store/parser/writer shape the implemented modules share — one store owning documents and identities, a parser that never throws, a writer that splices rather than re-serialises — and the round-trip guarantee (a document ADP did not change comes back byte-identical) that shape exists to honour.
   - **The session**: `IDiagramSessionFactory`/`IDiagramSession`, the baseline/delta contract, and the layout fork every type must decide (positions computed by the module vs authored in the document), pointing at one shipped example of each.
   - **Commands**: every edit as an `ICommand` with an inverse, dispatched through the history stack.
   - **The client**: `client/register.ts` discovered by the shell's `import.meta.glob` (no registry to edit), the per-module `package.json` under the npm workspace in `src/package.json`, the buf `generate` step for a module with its own `.proto`, and the shared canvas library under `src/client/src/canvas/` (elements, connections) with its own readmes as the deeper reference.
   - **Tests and examples**: the `.Tests` project (xUnit v3, executable, run via CLAUDE.md's command), byte-compared fixtures and the `.gitattributes` extension rule they require, the module's `examples/` folder, and the replication of examples into the combined `src/examples/` project.
   - **The catalog**: the CLAUDE.md rule that `docs/diagrams.md` is updated when a type changes state, linked rather than restated.
2. WHEN a touch point is described THEN the documentation SHALL name a real file in a real shipped module as its worked example, so every abstract statement has a concrete neighbour the reader can open.
3. WHEN the walkthrough recommends an order THEN it SHALL follow `tech.md`'s bottom-up implementation order (document → model → session → wire → client) rather than inventing a different one.
4. WHERE `tech.md`'s "Specifying a diagram type" section already defines what a diagram-type *spec* must decide THEN the developer documentation SHALL reference it for the specification step rather than duplicating its list — the documentation's own job is the implementation path.

### Requirement 7 — Developer documentation: creating an editor module

**User Story:** As a developer who wants ADP to open a file kind in a purpose-built text editor, I want the editor family's path documented beside the diagram family's, so that the second plugin family is as approachable as the first.

#### Acceptance Criteria

1. WHEN the developer documentation covers editors THEN it SHALL describe the editor-module path in the same terms as Requirement 6, at minimum: the `src/editors/<editor>/` layout mirroring diagrams; the static `Editor.Definitions` with an `EditorDefinition` claiming files by **extension or exact file name**; the `plain` editor as the fallback that claims nothing and catches everything; the rule that two editors claiming one extension is a startup error, not a race; the session factory seam; and the client's `register.ts` discovered by the same glob that finds diagram canvases.
2. WHEN the two families are documented THEN what they share SHALL be written once and referenced from both (discovery, registration-by-definition, client glob, examples replication), and only the genuine differences SHALL be family-specific — two parallel documents that restate each other drift exactly like two copies of a rule.
3. WHEN the editor walkthrough names its worked example THEN it SHALL use a shipped editor module (`markdown` or `plain`), and IF a described piece turns out to be a placeholder in that module THEN the documentation SHALL say so rather than describing intent as fact.

### Requirement 8 — Verified against reality, and kept there

**User Story:** As the reader following the walkthrough, I want confidence that every named path, type and command exists in the repository today, so that the documentation is a map of the territory and not of somebody's memory of it.

#### Acceptance Criteria

1. WHEN the developer documentation is written THEN every repository path, project name, type name, and command it states SHALL be verified against the repository at time of writing — by opening the named files, not from memory — and the verification SHALL be part of the implementation tasks, with the reference modules used named in the documentation itself.
2. WHEN the walkthrough is complete THEN its accuracy SHALL be checked by tracing one entire shipped module against it, step by step: every touch point the documentation names must exist in that module (or be documented as optional), and anything the module has that the documentation cannot explain is a finding to resolve before the documentation ships.
3. IF the documentation and CLAUDE.md state the same rule THEN one of them SHALL link to the other rather than both stating it (CLAUDE.md wins for rules that bind agents working in the repository; the developer docs summarise and link). The same holds against `tech.md` and `structure.md`: the steering documents are upstream, the developer documentation is the readable path through them.
4. WHEN a later change moves a documented touch point (a renamed seam, a new registration shape) THEN the documentation SHALL be updated in the same change or flagged as stale — the same standing rule the diagram catalog already has in CLAUDE.md, extended to these documents. The design SHALL decide where this refresh rule is recorded so it binds future work.

### Requirement 9 — The dependency inventory

**User Story:** As a reader assessing ADP — a developer, a security reviewer, or someone checking license compatibility — I want one document listing what the backend and the client depend on, at which version, why, and under which license, so that the answer does not require walking the package manifests myself.

#### Acceptance Criteria

1. WHEN the inventory is written THEN it SHALL be a `dependencies.md` under `docs/`, linked from the root readme (Requirement 5.3), and split into **two sections: one for the backend and one for the client**.
2. WHEN each section is written THEN it SHALL be a table with exactly these columns: **dependency name**, **version**, **reason for usage**, and **license**.
3. WHEN the backend section is filled THEN its rows SHALL come from `src/Directory.Packages.props` — the central version manifest every backend package version already goes through — one row per `PackageVersion` entry, with the version column matching that file. Direct dependencies only; transitive packages are the package manager's business, not this document's.
4. WHEN the client section is filled THEN its rows SHALL come from the npm workspace manifests: `src/package.json` and the workspace packages it declares (`src/client/package.json` and the `src/diagrams/*/client/package.json` files), covering `dependencies` and `devDependencies`. One row per distinct package, with the version column matching the manifests.
5. WHEN versions are recorded THEN the document SHALL be kept true by a **guard, not by discipline**: a test in the backend test suite SHALL parse the manifests of criteria 3 and 4 and `docs/dependencies.md`, and fail when a dependency is missing from the document, listed but no longer referenced, or version-mismatched. This position is chosen deliberately over a generation step (which would overwrite the hand-written reason column) and over accepted staleness (which this repository's own history argues against — a copy without a guard drifts): a package bump then updates one more line, and the same CI gates that run the backend tests enforce it. The guard checks **names and versions only**.
6. WHEN the **reason for usage** column is filled THEN it SHALL be written by the author from how the repository actually uses the package — one sentence in the reader's terms, not the package's own tagline. This column cannot be generated, and that is the point of having it. IF a dependency's reason cannot be stated THEN that is a **finding** — either the usage is discovered and documented, or the dependency is questioned — never a blank cell.
7. WHEN the **license** column is filled THEN each value SHALL be read from the package's own machine-readable metadata — the NuGet package's license expression for backend rows, the `license` field of the npm package for client rows — at time of writing, not guessed or copied from a project homepage. The guard of criterion 5 does not verify licenses (doing so would make a unit test depend on package metadata availability); instead, WHEN a version changes THEN the row's license SHALL be re-checked as part of the same edit the guard already forces.
8. WHEN the tables are formatted THEN they SHALL use standard markdown tables — this document is read on GitHub and has no need of the raw-HTML style `docs/diagrams.md` uses for its colspan-heavy catalog.

## Non-Functional Requirements

### Code Architecture and Modularity

- **Documentation, plus its guard**: this spec adds and edits markdown and image files (plus, at most, a commented `.gitattributes` line) and one test — the dependency-inventory guard of Requirement 9.5, which lives in the existing backend test suite and adds no new build step. No production code, no behaviour change.
- **Single source of truth**: every rule stated in the new documents is either original to them or a link to where the rule already lives (`CLAUDE.md`, `tech.md`, `structure.md`, `docs/diagrams.md`, module readmes). No forked copies.
- **House style**: the new markdown files follow the repository's line-ending policy as-is (ordinary text files under the root `.gitattributes` rule; no exemptions needed) and use lower-case `readme.md` naming where a readme is created.

### Rendering

- The spec's own documents (this file, design, tasks) render in the spec-workflow dashboard: no raw HTML, no code fences indented inside list items. (`docs/diagrams.md`'s HTML tables are that catalog's own style and are not imported here.)
- The root readme and developer docs are written for GitHub's renderer: relative links, standard fenced code blocks, images by relative path.

### Performance

- Readme images are size-capped (Requirement 4.3) so the front page loads acceptably on a slow connection; there is no other performance surface.

### Security

- No tokens, credentials or secrets in any documentation file. The developer-environment credentials (`admin`/`changeme`) may be named because they ship in the repository already and only exist in the `developer` environment; the readme states that production configuration deliberately ships empty and must be set by the operator.

### Reliability

- Every relative link in the delivered documents resolves at time of commit; the tasks include a link check.

### Usability

- The readme answers, in order and above the fold: what this is, whether it builds, how to run it, what it looks like. A reader who stops after one screen has the essentials; everything deeper is one link away.
