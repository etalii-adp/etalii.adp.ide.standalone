# Requirements Document

## Introduction

The user's request, verbatim:

> Also write a specification to create architectural documentation with the aim to optimize the agents understanding. Use markdown files and mermaid diagrams. Focus on the high-level architecture first, and on the solution structure. Keep it lightweight as we will dive deeper later.

**The audience is an agent, and that decides what belongs on a page.** A new human hire needs orientation; a session needs to stop re-deriving things and to avoid wrong turns the team has already taken. So the test applied to every section of these documents is one question: **does this stop a session working something out from scratch, or spare it a mistake somebody has actually made here?** A paragraph that passes neither test is removed rather than shortened.

**"Keep it lightweight" is treated as a requirement, not a mood.** This pass produces **two pages**, with a stated size budget and a stated out-of-scope list, because the user said the deeper dive comes later and a specification is where that line is held for them.

**One measurement made while writing these requirements, because it is exactly what the documents are for.** Counting `.csproj` files under `src/` gives 121; the solution holds 105; the sixteen-file difference is fixture and example data belonging to the `dotnet-dependency-graph` module, which reads `.csproj` files as its subject matter. A first attempt at classifying those 105 by area also produced "78 backend, 27 other" - wrong, because the solution's paths are relative to `src/backend/`, so the core projects carry no `backend` segment while the diagram projects do. **Both are wrong answers that look right, and both are the sort of thing a session will derive again next week unless a page says otherwise.**

**The user's verdict on the first version asked for one more thing, and measuring it produced the strongest
argument in this document.** The request was that the existing agent files and rules be looked into, that the new
documentation be enforced in them, and that any architecture content now belonging to the new pages be removed
from them "to ensure centralization and consistency". Three claims were then checked in the steering documents
that agents read every session:

- `tech.md` says the web client renders "the diagram canvas via a canvas/WebGL-based library (e.g. Konva or
  PixiJS) rather than raw SVG/DOM". **There is no Konva or PixiJS dependency in `src/client/package.json`, and
  the canvas draws SVG** - `DiagramCanvas.tsx` is full of `<svg>`, `<rect>`, `<ellipse>` and `<path>`. The
  statement is not merely stale; it would send a session looking for a renderer that was never adopted.
- `structure.md` lists `EtAlii.Adp.Backend.Diagrams` and `EtAlii.Adp.Backend.Diagrams.Tests` among the core
  projects. **Neither exists.** The abstractions live in `EtAlii.Adp.Diagram`, and the core set was decomposed
  into eleven further projects - `Authentication`, `Common`, `Context`, `Documents`, `Editor`, `Hierarchy`,
  `History`, `Problems`, `Projects`, `Sessions`, `TestSupport` - none of which the list mentions.
- `structure.md`'s claim that shared contracts live in a dedicated `api/` folder **is true**, and is named here
  so that the other two are read as measurements rather than as a case being built.

**Two false architecture statements out of three checked, in the files agents are told to read.** That is what
duplication costs: the same fact in two places drifts in one of them, and the reader cannot tell which. So
Requirement 9 makes the agent files point at the new pages rather than restate them, and removes what they
duplicate - which is the user's word, centralization, applied to documentation rather than to code.

## Alignment with Product Vision

`product.md` describes ADP as a tool whose diagrams are its own repository's text files. The work here is carried out almost entirely by agent sessions, so **the repository's legibility to an agent is part of its fitness** rather than a courtesy. This specification adds the smallest documentation that measurably reduces re-derivation, and guards it so that it cannot quietly stop being true.

## Requirements

### Requirement 1: Two pages, and nothing else in this pass

**User Story:** As an agent session starting work in this repository, I want one page on the high-level architecture and one on the solution structure, so that I can orient myself without reading 105 project files.

#### Acceptance Criteria

1. The repository SHALL contain exactly two new documentation pages from this specification: one covering the high-level architecture, one covering the solution structure.
2. Each page SHALL state, in itself, what it deliberately does not cover.
3. This specification SHALL name the out-of-scope subjects for this pass: per-module internals, the wire contracts, the client component tree, deployment and hosting, the editor family's internals, and per-diagram-type behaviour. WHEN a later pass adds any of them THEN it SHALL be a separate specification.
4. IF a third page appears necessary while implementing THEN the implementer SHALL raise it rather than adding it, because the page count is the user's constraint and not the author's.

### Requirement 2: Written against re-derivation, not against a reader's ignorance

**User Story:** As an agent session, I want each section to tell me something I would otherwise work out wrongly or slowly, so that reading the page is cheaper than reading the tree.

#### Acceptance Criteria

1. The design SHALL state, for every section of both pages, which re-derivation or which wrong turn it prevents.
2. WHERE the team has actually taken a wrong turn, the page SHALL name it rather than only stating the correct fact. At minimum it SHALL carry the two named in the introduction: that the solution's project paths are relative to `src/backend/`, and that a `.csproj` under `src/` is not necessarily a project of the product.
3. The pages SHALL NOT restate `CLAUDE.md` or `.spec-workflow/steering/processes.md`; they SHALL link to them. WHEN a subject is covered there THEN the page SHALL carry at most one sentence and a link. **The rule runs both ways**: where a steering document currently describes the architecture or the solution structure, that description moves to the page and the steering document links to it, per Requirement 9.
4. The pages SHALL NOT duplicate `docs/creating-a-diagram-module.md`, `docs/creating-an-editor-module.md`, `docs/dependencies.md`, `docs/diagrams.md` or `docs/guards.md`; they SHALL link to them where a reader needs them.

### Requirement 3: Every count says what it counts, and can be checked

**User Story:** As an agent session, I want the numbers on these pages to be ones I can reproduce, so that I neither trust a stale figure nor recount what is already counted.

#### Acceptance Criteria

1. WHEN a page states a count THEN it SHALL name what was counted and the instrument that counted it.
2. The solution-structure page SHALL state the project count with its areas, distinguishing test from production projects, and SHALL state the difference between tracked `.csproj` files under `src/` and projects in the solution, naming the reason for that difference.
3. IF a count cannot be asserted by the guard of Requirement 5 THEN it SHALL be omitted rather than approximated.

### Requirement 4: Mermaid diagrams whose nodes are real, and whose parse risk is stated

**User Story:** As an agent session, I want diagrams that name things I can find in the tree, so that a diagram is a map rather than an impression.

#### Acceptance Criteria

1. Diagrams SHALL be mermaid in fenced code blocks, and SHALL render both in the specification dashboard and on the repository host.
2. Every node in every diagram SHALL name a real project, folder or component, spelled as the tree spells it, so that Requirement 5's guard can check it.
3. **The mermaid parse risk SHALL be resolved by reuse rather than by a second attempt.** `module-client-api-readme` task 1 is a spike asking whether `mermaid.parse` can run in the client test environment, and as of 2026-09-23 it has NOT run - none of that specification's eleven tasks are done. WHEN this specification is implemented THEN it SHALL either reuse that spike's outcome if it has landed, or validate its diagrams by a recorded manual render check, and the implementation log SHALL say which held. This specification SHALL NOT add the `mermaid` dependency itself.
4. A diagram SHALL be small enough to read at the dashboard's width; one that needs scrolling to be understood SHALL be split or removed.

### Requirement 5: Guarded, or it rots

**User Story:** As an agent session, I want a page that has stopped being true to fail the build, so that a confident wrong answer cannot outlive its subject.

#### Acceptance Criteria

1. A test SHALL assert that every repository-relative path, project name and file named by either page exists.
2. The test SHALL assert every count stated by either page against the tree, so that a count going stale reddens rather than misleads.
3. The test SHALL carry a floor, so that an emptied page - or one reshaped into a form the test no longer parses - fails rather than passing vacuously.
4. The test SHALL be seen to fail before it is trusted: the implementation log SHALL record a planted wrong path and a planted wrong count, each reddening the test for its own reason.
5. Both pages SHALL be added to the document list of `DocumentationLinks.Tests`, so that their relative links are checked with the rest of the delivered documentation.
6. Both pages and their guard SHALL be listed in `docs/guards.md`, per that page's own instruction that a changed guard changes its row.

### Requirement 6: Placement is stated, with its reason

**User Story:** As a reader, I want these pages where the other delivered documentation lives, so that finding one leads to the others.

#### Acceptance Criteria

1. Both pages SHALL live in `docs/`, beside `creating-a-diagram-module.md`, `dependencies.md`, `diagrams.md` and `guards.md`, because they are delivered documentation about the product rather than process steering: `.spec-workflow/steering/` holds how the team works, and these pages hold what the software is.
2. `readme.md` SHALL link both pages under *Going deeper*, because a page nobody links to has the discoverability problem it was written to solve.
3. The pages SHALL NOT be placed in `.spec-workflow/steering/`, and the design SHALL record this as a decision rather than leaving it implicit.

### Requirement 7: Lightweight, measurably

**User Story:** As the user who asked for something lightweight, I want a size limit somebody can check, so that the deeper dive stays a later decision rather than happening now by accident.

#### Acceptance Criteria

1. Each page SHALL be at most 150 lines and SHALL carry at most three diagrams.
2. The guard of Requirement 5 SHALL assert both limits, so that growth is a deliberate amendment rather than a drift.
3. WHEN a later pass needs more room THEN it SHALL raise the limit in an amendment to this specification, stating what the extra room buys.

### Requirement 8: The pages say what they are, and what keeps them true

**User Story:** As an agent session, I want to know a page's authority and its staleness rule, so that I neither treat it as a specification nor leave it stale after a change.

#### Acceptance Criteria

1. Each page SHALL state that it is descriptive rather than normative: it says what the tree is, while `CLAUDE.md` and `processes.md` say what to do and a specification says what to build.
2. Each page SHALL state its refresh rule by link to `CLAUDE.md`'s *Documentation refresh* section: a change that moves something a page names updates the page in the same change.
3. Each page SHALL name the guard that protects it, and SHALL say plainly that the guard checks existence, counts and size but **cannot** check whether a description is still accurate.

### Requirement 9: The agent files point at these pages, and stop duplicating them

**User Story:** As an agent session reading the rules at the start of my work, I want one place that describes
the architecture, so that I am never choosing between two descriptions of it and never acting on the stale one.

#### Acceptance Criteria

1. The implementer SHALL read `CLAUDE.md`, `.spec-workflow/steering/structure.md`, `tech.md`, `product.md` and
   `roles.md`, and SHALL produce a list of every passage that describes the high-level architecture or the
   solution structure, with the page and section each belongs to. **The list SHALL be in the design**, so that
   what moved and what stayed is reviewable rather than discovered later.
2. WHEN a passage duplicates content that now lives on one of the two pages THEN it SHALL be removed from the
   agent file and replaced by a link to the page's section. **A passage SHALL NOT be left in both places**, and a
   removal SHALL NOT drop a fact: anything true and not yet on the page moves onto the page first.
3. **Where a passage is NORMATIVE it stays where it is.** A rule about how the team works belongs in `CLAUDE.md`
   or `processes.md` and is not architecture description - the commit-identity rules in `tech.md`'s naming
   section are the clearest example, and they SHALL NOT move to a descriptive page. The design SHALL state this
   division for every passage it lists, because the two kinds read alike and the wrong move would put process
   guidance where nothing enforces it.
4. WHERE a passage is found to be FALSE rather than merely duplicated, the correction SHALL land on the page and
   the false passage SHALL be removed, and the implementation log SHALL name each one. **Two corrections are named
   work of this specification rather than examples of it:** `tech.md`'s claim that the client renders the canvas
   "via a canvas/WebGL-based library (e.g. Konva or PixiJS) rather than raw SVG/DOM", and `structure.md`'s core
   project list, which names `EtAlii.Adp.Backend.Diagrams` and its test project - neither of which exists - while
   omitting the eleven decomposed core projects that do. The three claims checked while writing these requirements
   are the starting list and are not assumed to be the only ones; the implementer SHALL check the rest.
5. `CLAUDE.md` SHALL carry a short section naming both pages and saying when to read them, so that a session
   meets them before it starts re-deriving the tree. **This is the enforcement the user asked for**: the pages
   are reachable from the file every session already reads, rather than only from `readme.md`.
6. A guard SHALL assert that every steering document and `CLAUDE.md` link resolves, so that a moved section
   cannot leave a dangling pointer where a duplicated paragraph used to be. IF `DocumentationLinks.Tests` does
   not currently cover those files THEN this specification SHALL extend it rather than write a second guard.
7. **An architecture claim in an agent file is a claim the guard must cover, wherever the claim lives.** The guard
   of Requirement 5 checks the two pages; a project name, path or count left behind in `CLAUDE.md` or a steering
   document is the same kind of statement and drifts the same way - which is how two false statements came to sit
   in the files every session reads. So the guard SHALL assert the project names, paths and counts stated in
   `CLAUDE.md` and the steering documents too, and the design SHALL say which statements those are. **Otherwise
   this work moves the drift one directory over rather than ending it.**
8. The design SHALL state how much of `structure.md` and `tech.md` is expected to remain after the removal. IF a
   steering document would be left with nothing but links THEN the implementer SHALL raise it rather than delete
   the file, because a steering document's existence is the user's decision and not an implementation detail.

## Non-Functional Requirements

### Documentation quality

- **Every fact traceable.** A statement on either page is one a reader can check against the tree in one command, or it does not belong there.
- **No raw HTML and no indented fenced blocks** in the specification documents, so that the dashboard renders them.
- **CRLF in the working tree, LF in the index**, as `.gitattributes` and `src/.editorconfig` require of every text file here.

### Maintainability

- **One guard, not six.** The checks of Requirement 5 belong in a single test class named for the pages it protects, following `DocumentationLinks.Tests` and `GuardInventory.Tests` rather than inventing a shape.
- **The guard's failure messages SHALL say what to do**, naming the page and the fix, because those failures will be met by a session that did not write the page.

### Out of scope, stated once

Per-module internals; the wire contracts and generated code; the client component tree below the canvas library boundary; deployment, hosting and configuration; the editor family's internals; per-diagram-type behaviour; sequence diagrams of runtime flows. **Each is a later specification, and each is deliberately absent rather than forgotten.**
