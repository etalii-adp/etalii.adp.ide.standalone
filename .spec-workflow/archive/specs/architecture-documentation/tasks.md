# Tasks Document

**Ten tasks. Two pages, one guard, two guard extensions, the removal pass, and a check to run.** The order is deliberate: **the pages before the guard that reads them, and the removal last**, because a passage may only leave a steering document once the page holds its content and a test proves the page still says so.

**The coverage diff is recorded at the end of this document**, per the project's own rule that it runs before the tasks card rather than after.

- [x] 1. `docs/architecture.md` - the page, with one diagram
  - File: `docs/architecture.md` (new)
  - The eight sections the design fixes, in its order: what ADP is, the three parts and the single boundary, the two call legs, where a diagram lives, how a diagram type plugs in, what the canvas is, the subsystems named not described, and what this page is not.
  - **The canvas section states the measured truth: SVG drawn by the shared canvas library.** No Konva, no PixiJS, and it says so, because the false claim is what this page exists to end.
  - **One mermaid diagram** (`flowchart LR`): browser client, the single ASP.NET Core process with its two legs, the filesystem. Every node names a real project or folder as the tree spells it.
  - At most 150 lines. Link `product.md`, `creating-a-diagram-module.md` and `tech.md`'s subsystem sections rather than restating them; at most one sentence where another document already covers a subject.
  - The closing section says it is descriptive rather than normative, names the refresh rule by link to `CLAUDE.md`, names its guard, and states plainly that the guard checks existence, counts and size and **cannot** check whether a description is still accurate.
  - _Requirements: 1.1, 1.2, 2.2, 2.3, 2.4, 4.1, 4.2, 4.4, 6.1, 8.1, 8.2, 8.3_
  - _Prompt: Role: Technical writer who reads code | Task: Write docs/architecture.md to the design's eight sections, with one mermaid flowchart whose nodes are real projects or folders | Restrictions: at most 150 lines and one diagram; no restatement of CLAUDE.md, processes.md or the other docs pages - link them; state the canvas as SVG and do not repeat the Konva claim; every path and project name must exist | Success: a session reading only this page can say what the parts are, how they talk, where a diagram lives and what the canvas is, and the page says what it is not_

- [x] 2. `docs/solution-structure.md` - the page, with its counts and one diagram
  - File: `docs/solution-structure.md` (new)
  - The eight sections the design fixes, including **the relative-path trap** - the solution's project paths are relative to `src/backend/`, so core projects carry no `backend` segment.
  - **The counts, in the fixed form the guard parses:** 105 projects in the solution (27 core, 74 diagram, 4 editor; 78 production, 27 test), 121 tracked `.csproj` under `src/`, and the 16-file difference being fixture and example data of `dotnet-dependency-graph`, which reads `.csproj` files as its subject.
  - The 15 production and 12 test core project names, grouped by concern.
  - **One mermaid diagram** (`flowchart TB`) of `src/`'s six folders.
  - At most 150 lines. Same closing section as task 1.
  - _Requirements: 1.1, 1.2, 2.2, 3.1, 3.2, 4.1, 4.2, 4.4, 6.1, 8.1, 8.2, 8.3_
  - _Prompt: Role: Technical writer who reads code | Task: Write docs/solution-structure.md to the design's eight sections, stating every count in the form the guard parses and naming the relative-path trap | Restrictions: at most 150 lines and one diagram; every count must be recomputable from the tree; state no count the guard cannot check | Success: the page's counts all recompute, the core project list matches the solution, and a reader learns why a .csproj count is not a project count_

- [x] 3. `ArchitecturePages.Tests` - the guard, seen to fail twice
  - File: `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/ArchitecturePages.Tests.cs` (new)
  - Modelled on `GuardInventory.Tests`: locate the repository root by walking up for `src/diagrams` beside `docs`.
  - Asserts, for both pages: every backtick-quoted repo-relative path exists; every `EtAlii.Adp.*` name is a project in `EtAlii.Adp.slnx`; **every stated count recomputes from the tree**; at most 150 lines and at most 3 fenced mermaid blocks; and a floor on parsed paths and counts so an emptied or reshaped page fails rather than passing vacuously.
  - Failure messages name the page and the fix.
  - **Seen to fail before it is trusted: a planted wrong path and a planted wrong count, each reddening for its own reason**, with both messages recorded in the implementation log.
  - _Requirements: 3.3, 5.1, 5.2, 5.3, 5.4, 7.1, 7.2_
  - _Prompt: Role: Backend developer with xUnit experience | Task: Write ArchitecturePages.Tests asserting paths, project names, recomputed counts, size limits and a floor for both pages | Restrictions: follow GuardInventory.Tests' shape rather than inventing one; a count the test cannot recompute is a reason to remove the count, not to skip the assertion | Success: the log records a planted wrong path and a planted wrong count each reddening the test for its own reason, with the messages quoted_

- [x] 4. Extend `DocumentationLinks.Tests` to the pages and the agent files
  - File: `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/DocumentationLinks.Tests.cs`
  - Add both new pages to the document list, and add `CLAUDE.md`, `.spec-workflow/steering/structure.md`, `tech.md`, `product.md` and `roles.md`, so a link left dangling by the removal pass reddens.
  - **Seen to fail: a planted dangling link in a steering file.**
  - _Requirements: 5.5, 9.6_
  - _Prompt: Role: Backend developer | Task: Extend the existing documentation-links guard to the two pages and the five agent files | Restrictions: extend, do not write a second link guard; keep its markdown-and-HTML link handling | Success: a planted dangling link in a steering file reddens it, recorded in the log_

- [x] 5. The guard follows the claim into the agent files
  - File: `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/ArchitecturePages.Tests.cs`
  - The same path and project-name assertions run over `CLAUDE.md` and the four steering documents, so **a stale project name in a steering file reddens exactly as one on a page does.**
  - **Counts in those files are NOT asserted, and the REASON belongs in the task and in a comment rather than only in the rule, because the next reader will otherwise "complete" the guard and break the build:** a steering count is usually a **dated measurement** - *"measured on 2026-09-22 over 1430 `.cs` files"* - which records what was true **then**, and is still true as a record after the tree moves. **A page's count is a claim about NOW.** Only claims about now can be recomputed; **asserting the dated ones would redden the build for being honest about its own history**, and the correct response to such a red would be to delete a true sentence.
  - So the test asserts paths and project names everywhere, and counts **only on the two pages**. A steering document that wants a live count states it as a page reference instead.
  - _Requirements: 9.7_
  - _Prompt: Role: Backend developer | Task: Extend ArchitecturePages.Tests to assert paths and project names in CLAUDE.md and the four steering documents | Restrictions: do not assert counts there; state in a comment why a dated measurement is not a claim about now | Success: a planted stale project name in tech.md reddens the guard, recorded in the log_

- [x] 6. The removal pass - what moves, what stays, what is false
  - File: `.spec-workflow/steering/tech.md`, `.spec-workflow/steering/structure.md`
  - Execute the design's verdict table: **five `tech.md` sections move whole, two in part, nine stay, two are named-and-linked**; `structure.md`'s *Project structure*, *Naming* and *Module boundaries* move, its principles and documentation standards stay.
  - **Correct the two false claims as part of the work:** `tech.md`'s Konva/PixiJS canvas claim, and `structure.md`'s core-project list naming `EtAlii.Adp.Backend.Diagrams` and its test project - neither exists - while omitting the eleven decomposed core projects.
  - **Check the rest of both files rather than trusting the design's three-claim sample**, and name every further false claim found in the implementation log.
  - **No passage is left in both places, and no fact is dropped:** anything true and not yet on a page goes onto the page first.
  - _Requirements: 9.1, 9.2, 9.3, 9.4_
  - _Prompt: Role: Technical writer who reads code | Task: Move, correct or leave each passage exactly as the design's verdict table says, replacing moved passages with links | Restrictions: normative passages stay - a rule about how the team works is not architecture description; never leave a passage in both places; never drop a fact; check every remaining claim in both files | Success: the log lists each passage with its verdict and names every false claim found beyond the design's three_

- [x] 7. `CLAUDE.md` names the pages
  - File: `CLAUDE.md`
  - A short section naming both pages and when to read them, so a session meets them before it starts re-deriving the tree.
  - **This is the enforcement the user asked for**: the pages are reachable from the file every session already reads, not only from `readme.md`.
  - _Requirements: 9.5_
  - _Prompt: Role: Technical writer | Task: Add a short section to CLAUDE.md naming docs/architecture.md and docs/solution-structure.md and when to read them | Restrictions: a pointer, not a summary - no architecture content in CLAUDE.md; keep it to a few lines | Success: a session reading CLAUDE.md learns both pages exist and what each answers_

- [x] 8. `readme.md` and `docs/guards.md`
  - File: `readme.md`, `docs/guards.md`
  - Link both pages under *Going deeper*; add the new guard's row to `docs/guards.md` with the question it answers, per that page's instruction that a changed guard changes its row.
  - _Requirements: 5.6, 6.2_
  - _Prompt: Role: Technical writer | Task: Link both pages from readme.md and add the guard's row to docs/guards.md | Restrictions: the row's third column is the question the guard answers, not a description of it | Success: GuardInventory.Tests still passes, and the row states what the guard cannot check_

- [x] 9. Mermaid: reuse the spike, or record a manual render check
  - File: the implementation log, and `tests.md` if the manual route is taken
  - **If `module-client-api-readme` task 1 has landed, reuse its outcome.** If it has not, validate both diagrams by rendering them in the dashboard and on the repository host, and record that as a `tests.md` entry naming both surfaces.
  - **This task adds no dependency.** Worth knowing: the only tracked mermaid block today is in `.spec-workflow/templates/design-template.md`, so nothing yet demonstrates that mermaid renders in the dashboard either.
  - _Requirements: 4.3_
  - _Prompt: Role: Client developer | Task: Establish whether mermaid.parse can validate the two diagrams by reusing the module-client-api-readme spike, or else record a manual render check in tests.md | Restrictions: do not add the mermaid dependency in this specification | Success: the log states which route held, and a manual check names the dashboard and the repository host_

- [x] 10. The final check - run it, do not assume it
  - File: the implementation log only
  - Re-run the coverage diff below against the finished tree, traced to files and strings rather than to a task's promise: **does the code show each criterion, not did somebody claim it.**
  - Confirm both pages are within 150 lines and 3 diagrams, that every count recomputes, and that no passage appears both on a page and in a steering file.
  - **Restate the out-of-scope list** and confirm nothing from it crept in.
  - _Requirements: 1.3, 7.1, 7.2_
  - _Prompt: Role: Reviewer | Task: Re-run the coverage diff against the finished tree and confirm the size, count and no-duplication properties | Restrictions: trace each criterion to a file and a string, never to a task's promise | Success: the log names, for every criterion, the file and text that satisfies it_

## The coverage diff, run before this document's card

**38 acceptance criteria in the requirements. 33 are claimed by a task above. Five are not, and each is the second kind rather than a gap.**

**Satisfied by the approved design rather than by a task:**

- **2.1** - the design states, per section, which re-derivation it prevents. It does, in two tables.
- **6.3** - the design records the `docs/` placement as a decision. It does, with its reason.
- **9.8** - the design states how much of `structure.md` and `tech.md` is expected to remain. It does, in the verdict table.

**Instructions to whoever implements, which no deliverable can carry:**

- **1.4** - if a third page appears necessary, raise it rather than adding it. A rule about what NOT to do silently; task 10 would catch the breach but cannot claim the criterion.
- **7.3** - a later pass raises the size limit by amendment, stating what the extra room buys. It binds a future specification, not this one.

**Stated here rather than left as silence**, because the project's own rule is that an unclaimed requirement is either a real gap or satisfied by the document's shape, **and that saying which kind it is finishes the job.**
