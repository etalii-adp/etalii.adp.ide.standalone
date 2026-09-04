# Tasks Document

Twenty-five floors and one anchor repair, in twenty files, grouped into ten tasks by module — because the module is the merge unit. **Every task is independently landable**: each repair is one added assertion in one file, none shares code with another, and none has an ordering constraint. Take them in any order, in parallel, one worktree per agent. Keep the worktree name short: `.claude/worktrees/floor-c4` and the like, because a long worktree path is what produces `Zero tests ran` instead of a build error.

## Six rules that apply to every task below

**1. A repair is not done until its floor has been *seen* to fail, for the right reason.** Empty the collection the floor guards, run the module's tests, confirm the named test fails, then revert. A floor that has never failed is an assertion nobody has checked, which is the defect this specification exists to remove.

**Empty the collection, do not remove its source.** Deleting the fixture files makes the test fail with a `FileNotFoundException`, which is red for a reason that has nothing to do with the floor and proves nothing about it. Stub the producing call, or intercept the read, so that the only thing that changed is that the sweep came back empty. Agent 2's `C4Export.Read` sabotage is the model.

**2. Say which claim your message makes, because two are available and only one is usually true.** "The whole file passes vacuously" and "the file is one edit away from passing vacuously" are different statements. Check which one you have before wording the failure message: an adopter who asserts the stronger claim about a file that has a surviving exact-equality check somewhere has written something untrue into the tree. Both justify the floor — every site here lacks one, so no repair is unnecessary — but only one of them is accurate about your file. This rule exists because the survey behind this specification made exactly that error about `WardleyToolboxProvider.Tests.cs`, and it was caught by an adopter verifying before writing rather than after.

**3. A passing test is not evidence on its own.** This is the sharper form of rule 1 and it is worth stating separately, because it is the trap. A guard that no longer reaches its subject passes exactly as loudly as one that does. When the OWL showcase folders were moved (`a544e20e`), the registration walker passed afterwards — which proved nothing, because a walk that had stopped reaching those files would also have passed. What proved it was pointing the moved registration at a file that does not exist and watching the test fail **naming the new path**. Every repair here faces the same temptation: look for the failure message to name the thing you broke, not merely for red.

**4. Assert your sabotage matched before you write it.** A patch written with LF newlines matches nothing in this CRLF tree. It changes no bytes, the suite passes, and the result is indistinguishable from a guard that survived tampering — worse than no verification, because it produces evidence. If you patch a file with a script, make it fail loudly when the pattern is not found.

**5. Gates before merge, judged by exit code, captured before any pipe.** From `src/backend/`: `dotnet test --solution EtAlii.Adp.slnx` and `dotnet format style --verify-no-changes --severity info`. From `src/client/`: `npm test` and `npm run typecheck`. In a pipeline `$?` is the last command's status, so `dotnet test … | tail` reports `tail`'s success and a crashed or zero-test run reads as green. Set `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_USE_MSBUILD_SERVER=0` first. No repair here touches client code, but the gate set is the gate set. A fresh worktree needs `npm install` in `src/` before the client gates run at all.

**6. Committing and merging in the shared main checkout.** Commit with `git commit -F msg -- <paths>`: `git add <path>` stages one file, but a bare `git commit` commits the whole index including whatever another session staged into it. Before merging, read `git status` for files you did not touch — a merge writes every differing path regardless of the index, and neither `git stash` nor `git checkout --` is acceptable on another session's work. Both rules are CLAUDE.md's (`db3bc0c0`) and both are scoped to the **main checkout**: inside your own worktree the index is private and cannot sweep anyone.

## Reading a card's state, because ten groups means several agents will

**The approval tool is not reliably wrong; it is reliably late** — which is worse, because polling it looks like it is working. On the day this specification was written its own requirements read `pending` in the request JSON, in the snapshot `metadata.json`, and from `approvals action:"status"` (which answered `BLOCKED - Do not proceed`) for over an hour after the user had approved them. The write-back landed later and the same fields then read `approved`.

The durable signal is a snapshot file whose own `trigger` is `"approved"`. The `status` field *inside* that snapshot always reads `pending` and means nothing. Confirm by comparing against a spec known to be approved — every one in this repository shows `v1 trigger=initial` then `v2 trigger=approved`:

```bash
for f in .spec-workflow/approvals/<spec>/.snapshots/*/snapshot-*.json; do
  python -c "import json,sys;d=json.load(open(sys.argv[1]));print(sys.argv[1],'trigger='+str(d.get('trigger')))" "$f"
done
```

## What a floor looks like

The whole design in one place, copied from `ProblemStoreIsolation.Tests.cs:62-69`, which is the reference implementation:

```csharp
Assert.True(
    booting.Length >= 20,
    $"Only {booting.Length} integration test sources mention {BootsTheHost}; this guard has stopped finding the suite it guards.");
```

Three parts, and the third is the one that gets dropped: a count taken before the loop; a lower bound with headroom rather than an exact count; and **a message saying the guard stopped working, not that the subject is wrong**. `Assert.NotEmpty(files)` fails with "the collection is empty", which reads as a product defect and sends the reader to the wrong code. Where the bound is a number, say in a comment what population it is a floor on.

The design's four shapes — A (files discovered on disk), B (a collection the code under test produced), C (`Assert.All`), D (an extraction that may match nothing) — are named per site below. Match the site to the shape and copy; do not invent a fifth.

**Do not "fix" the twelve literal-array loops.** They are named in the design's Deviations and are correct as written: a loop over an array declared in the test cannot be empty.

- [ ] 1. azure-pipeline — seven floors and the relative-path fix
  - Files: `src/diagrams/azure-pipeline/backend/EtAlii.Adp.Diagram.AzurePipeline.Tests/` — `PipelineElementMapper.Tests.cs` (:81, :488), `PipelineGraphBuilder.Tests.cs` (:345), `PipelineRuleSet.Tests.cs` (:46), `PipelineWriter.Tests.cs` (:444), `PipelineContextActionProvider.Tests.cs` (:454, :496)
  - Shape A for the first five, shape B for the two in `PipelineContextActionProvider`.
  - **Also the relative path.** The five shape-A sites enumerate `Directory.GetFiles("Fixtures", …)`, which resolves against the process working directory rather than the test binary's folder. Change to `AppContext.BaseDirectory`, matching their siblings elsewhere in the tree. Same one-line character of repair, same change.
  - The largest group and the plainest; a good first pick.
  - _Requirements: 1.1, 1.2, 1.3_
  - _Prompt: Implement task 1 for spec test-suite-conformance. Role: C# developer | Task: add a floor assertion to each of the seven named sites and move the five relative "Fixtures" enumerations onto AppContext.BaseDirectory | Restrictions: change no expected value; add no helper, base class or shared file; do not touch other modules | _Leverage: ProblemStoreIsolation.Tests.cs:62-69 | Success: each floor has been seen to fail with its collection emptied, and all four gates pass by exit code. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 2. wardley-map — three floors, one of which is file-wide
  - Files: `src/diagrams/wardley-map/backend/EtAlii.Adp.Diagram.WardleyMap.Tests/` — `WardleyElementMapper.Tests.cs` (:214), `WardleyWriter.Tests.cs` (:301), `WardleyToolboxProvider.Tests.cs` (:83)
  - Shapes A, A, B.
  - **`WardleyToolboxProvider.Tests.cs` gets one floor, and it goes in the test class constructor.** Three of its four tests are vacuous on an empty palette — the uniqueness check at `:80` compares an empty count against an empty count and both loops skip — but the fourth, `ItOffersWhatRequirement132Asks` at `:58`, compares labels against an exact eight-element array and would fail. This file is therefore **one edit away from** passing vacuously, not already doing so; word the floor's message for that claim and not the stronger one.
  - **Placement, and it applies to every group:** a floor inside one test body cannot stop a different test in the same class passing vacuously. Where a group's sites are several sweeps in one class, put the floor in the class constructor, which xUnit runs before every test.
  - _Requirements: 1.1, 1.2, 1.3_
  - _Prompt: Implement task 2 for spec test-suite-conformance. Role: C# developer | Task: add floors to the three named sites, with one palette floor covering the whole toolbox file | Restrictions: change no expected value; add no helper or shared file; one floor in WardleyToolboxProvider.Tests.cs, not two | _Leverage: ProblemStoreIsolation.Tests.cs:62-69 | Success: each floor has been seen to fail, and all four gates pass by exit code. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 3. c4 — three floors
  - Files: `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4.Tests/` — `BigBankPlc.Tests.cs` (:148), `C4LayoutCrowding.Tests.cs` (:161), `C4Export.Tests.cs` (:40)
  - Shape B for the first two (both sweep `Workspace.Views`), shape C for `C4Export`.
  - `Examples.Tests.cs` in the same project is **already sound** — a two-part anchor at `:36` and a floor at `:93`. Leave it alone; it is a useful thing to read first.
  - _Requirements: 1.1, 1.2, 2.1, 2.2_
  - _Prompt: Implement task 3 for spec test-suite-conformance. Role: C# developer | Task: add floors to the three named c4 sites | Restrictions: change no expected value; add no helper; do not modify Examples.Tests.cs, which already satisfies the rule | _Leverage: c4 Examples.Tests.cs:36 and :93 as the in-module example | Success: each floor has been seen to fail, and all four gates pass by exit code. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 4. backend core — two floors, one of them an extraction
  - Files: `src/backend/EtAlii.Adp.Backend.Tests/Unit Tests/Hierarchy/EditorFilePropertyProvider.Tests.cs` (:51), `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/DocumentationLinks.Tests.cs` (:66)
  - Shape C and shape D.
  - **`DocumentationLinks` is the only shape-D site.** Its final assertion is that the dead-link list is empty; nothing asserts a link was ever *extracted*, so a `LinkExpression()` that stopped matching would check nothing and pass. Floor the count of relative links extracted across the delivered set, and **preserve the per-target tolerance documented at `:113`** — a target the pattern misses going unchecked is deliberate, and the new comment must say that total extraction failure is the different thing being guarded.
  - Do not widen the five-document list. That scope is a deliberate decision.
  - _Requirements: 2.1, 2.2, 5.1, 5.2, 5.3, 5.4_
  - _Prompt: Implement task 4 for spec test-suite-conformance. Role: C# developer | Task: add a floor to the Assert.All in EditorFilePropertyProvider.Tests.cs and an extraction floor to DocumentationLinks.Tests.cs | Restrictions: do not widen the delivered-document list; preserve the per-target tolerance at DocumentationLinks.Tests.cs:113 and say in the comment why total extraction failure differs from it | _Leverage: ProblemStoreIsolation.Tests.cs:62-69 | Success: both floors have been seen to fail, and all four gates pass by exit code. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 5. helm-charts — two floors
  - Files: `src/diagrams/helm-charts/backend/EtAlii.Adp.Diagram.HelmCharts.Tests/` — `HelmContextPropertyProvider.Tests.cs` (:32), `ZeroWrites.Tests.cs` (:48)
  - Both shape B, both sweeping a derived graph.
  - `HelmContextPropertyProvider.Tests.cs:32` is vacuous at two levels: the `foreach` skips on an empty graph, and the `Assert.All(rows, …)` inside it also passes on empty rows. One floor on the graph is what this task asks for; a second on `rows` is a judgement call the adopter may make and should say why in the comment if they do.
  - _Requirements: 1.1, 1.2, 2.1_
  - _Prompt: Implement task 5 for spec test-suite-conformance. Role: C# developer | Task: add floors to the two named helm-charts sites | Restrictions: change no expected value; add no helper | _Leverage: ProblemStoreIsolation.Tests.cs:62-69 | Success: both floors have been seen to fail, and all four gates pass by exit code. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 6. mindmap — three floors in one file
  - Files: `src/diagrams/mindmap/backend/EtAlii.Adp.Diagram.Mindmap.Tests/MindmapLayout.Tests.cs` (:88, :113, :223)
  - All shape B. `:88` sweeps `document.Nodes`, `:113` the named node's `Children`, `:223` the nodes that have children with boxes — three different collections, so three floors rather than one shared.
  - `:169` in the same file loops a literal array and is correct as written. Do not touch it.
  - _Requirements: 1.1, 1.2_
  - _Prompt: Implement task 6 for spec test-suite-conformance. Role: C# developer | Task: add three floors to MindmapLayout.Tests.cs at the named sites | Restrictions: three separate floors on three different collections; leave :169 alone, it loops a literal array | _Leverage: ProblemStoreIsolation.Tests.cs:62-69 | Success: each floor has been seen to fail, and all four gates pass by exit code. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 7. databricks — two floors in one file
  - Files: `src/diagrams/databricks/backend/EtAlii.Adp.Diagram.Databricks.Tests/Diagram.Tests.cs` (:25, :45)
  - Both shape C, both `Assert.All(Diagram.Definitions, …)`. `Definitions` is a static array in module source, so an empty one means the module declares nothing — a real regression, and the least likely of the 25. The smallest task; a good first pick for anyone new to the shape.
  - _Requirements: 2.1, 2.2_
  - _Prompt: Implement task 7 for spec test-suite-conformance. Role: C# developer | Task: add a non-empty floor before each of the two Assert.All calls in databricks Diagram.Tests.cs | Restrictions: change no expected value; add no helper | _Leverage: ProblemStoreIsolation.Tests.cs:62-69 | Success: both floors have been seen to fail, and all four gates pass by exit code. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [x] 8. sparql — the licence floor and the only anchor repair
  - Files: `src/diagrams/sparql/backend/EtAlii.Adp.Diagram.Sparql.Tests/ExampleCorpus.Tests.cs` (:69 floor, :17 anchor)
  - **Give this to whoever reads Requirement 4 closely.** It is the only group carrying two kinds of repair, and its guard is the one this specification most wants sound: `:69` is the guard for the house vendoring rule — every vendored folder carries its `readme.md` and `LICENSE.md` — and it currently has no floor at all, so an empty sweep asserts nothing about any corpus's licence.
  - **The anchor at `:17`** walks up looking for a bare folder named `examples`, and a second `examples` directory sits further up the same walk path at `src/examples`. It is correct today only because the nearer one wins. Adopt the two-part form its own sibling uses at `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4.Tests/Examples.Tests.cs:36`, which requires `examples` and `backend` together.
  - **The sound version of the same guard is `src/diagrams/rdf/backend/EtAlii.Adp.Diagram.Rdf.Tests/RdfExamples.Tests.cs:110`** — same rule, distinctive anchor, floor present. Raise this one to that standard rather than inventing a third spelling.
  - Verify the anchor repair the way the folder move was verified: it is not enough that the test still passes. Point the walk somewhere wrong and confirm it fails rather than silently guarding a different folder.
  - _Requirements: 1.1, 3.1, 3.3, 3.4, 4.1, 4.2, 4.3_
  - _Prompt: Implement task 8 for spec test-suite-conformance. Role: C# developer | Task: add a floor to ExampleCorpus.Tests.cs:69 and replace the single-part anchor at :17 with the two-part form | Restrictions: follow RdfExamples.Tests.cs:110 and c4 Examples.Tests.cs:36 rather than inventing a spelling; do not widen what the licence guard checks | _Leverage: RdfExamples.Tests.cs:110, c4 Examples.Tests.cs:36 | Success: the floor has been seen to fail with the sweep emptied, the anchor has been seen to fail when pointed at a wrong root, and all four gates pass by exit code. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 9. ansible-structure — one floor
  - Files: `src/diagrams/ansible-structure/backend/EtAlii.Adp.Diagram.AnsibleStructure.Tests/ZeroWrites.Tests.cs` (:64)
  - Shape B. The sweep is over the derived graph's node and edge ids concatenated; an empty graph exercises no read path at all, so the zero-writes promise is asserted about nothing.
  - _Requirements: 1.1, 1.2_
  - _Prompt: Implement task 9 for spec test-suite-conformance. Role: C# developer | Task: add a floor to ansible-structure ZeroWrites.Tests.cs:64 | Restrictions: change no expected value; add no helper | _Leverage: ProblemStoreIsolation.Tests.cs:62-69 | Success: the floor has been seen to fail, and all four gates pass by exit code. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 10. rdf — one floor, the worst of the shape-C five
  - Files: `src/diagrams/rdf/backend/EtAlii.Adp.Diagram.Rdf.Tests/RdfLayout.Tests.cs` (:61)
  - Shape C. `Assert.All(projection.Nodes, node => positions.ContainsKey(node.Id))` asserts every projected node has a layout position — which is exactly what a projection returning no nodes satisfies vacuously. Nothing in the file pins a count.
  - _Requirements: 2.1, 2.2, 2.3_
  - _Prompt: Implement task 10 for spec test-suite-conformance. Role: C# developer | Task: add a non-empty floor on projection.Nodes before the Assert.All in RdfLayout.Tests.cs:61 | Restrictions: change no expected value; add no helper | _Leverage: ProblemStoreIsolation.Tests.cs:62-69 | Success: the floor has been seen to fail, and all four gates pass by exit code. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 11. Confirm the count, once, by hand
  - Files: none changed unless a site was missed
  - After tasks 1-10 have merged, walk the 25 sites named in the requirements and confirm each carries a floor, and that `ExampleCorpus.Tests.cs:17` carries the two-part anchor. Record the result in the implementation log.
  - **This is a one-off confirmation, not a new guard.** The design rejected a permanent meta-guard on measured evidence — the scan behind the requirements was run three times and wrong twice, in opposite directions, and a guard that cannot reliably find its own subject is the defect this specification is about. Do not leave a scanner behind.
  - If a site turns out to have been missed, that is a finding to report and a task to add, not a number to adjust.
  - _Requirements: 1.1, 2.1, 3.3_
  - _Prompt: Implement task 11 for spec test-suite-conformance. Role: C# developer | Task: confirm by reading that all 25 named sites carry a floor and the sparql anchor is two-part | Restrictions: read-only; do not add a scanner or meta-guard to the tree; report a missed site rather than quietly fixing it | _Leverage: the site tables in requirements.md | Success: the log records each of the 25 as carrying a floor, or names those that do not. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._
