# Requirements Document

## Introduction

A test suite earns trust by failing. This specification is about the tests in this repository that **cannot** — the ones that go green whether or not the thing they describe is true, because the collection they iterate is allowed to be empty and nothing says otherwise.

A read-only scan of the backend suites (`src/backend/*.Tests`, `src/diagrams/*/backend/*.Tests`, `src/editors/*/backend/*.Tests`) and of `tests.md` found **25 such sites across 20 files**. None of them is currently wrong: every one passes today because the collection it walks happens to be non-empty. That is the point. Each is a guard whose failure mode is silence, and the repository has already been bitten by exactly this shape twice — the `docs/diagrams.md` HTML table that went unparsed and dropped 48 stub origins out of a check that stayed green, and the `Zero tests ran` build failure that reads like an empty suite rather than a broken one.

**The house answer already exists and is written down in the suite itself.** `ProblemStoreIsolation.Tests.cs` states it in a comment before its own floor assertion:

> Assert, first, that the walk found the suite at all. Without this the test passes loudest exactly when it has stopped looking at anything.

That test is the model this specification generalises. It anchors its tree walk on a four-segment distinctive path (`ProblemStoreIsolation.Tests.cs:100`), and before comparing anything it asserts the walk found at least 20 sources (`ProblemStoreIsolation.Tests.cs:67`). Twenty-four match today, so the floor holds with four to spare. Nothing in this document asks for a new idea; it asks for that existing idea to be applied where it is missing.

**The scope is deliberately narrow: a floor, not a rewrite.** Every finding below is repaired by one added assertion. No test's subject changes, no fixture moves, and no guard is widened to cover more than it covers now. Widening is a separate decision and is explicitly not smuggled in here.

## Alignment with Product Vision

* [tech.md](../../steering/tech.md)'s **gate discipline** — the four local gates and `.github/workflows/build.yml` are judged by exit code precisely because a gate that cannot fail is a gate nobody reads. A vacuous test is the same defect one level down.
* [product.md](../../steering/product.md)'s **"Value from day one"** — the guards named here protect example corpora, licence files and read-only surfaces, which are what a reader meets first.
* The repository's own **bug-guard rule** (CLAUDE.md): every bug found must leave a guard behind that cannot silently return. A guard that passes on an empty set does not satisfy that rule, whatever it was written for.

## Requirements

### Requirement 1 — A guard that walks a collection asserts it found one

**User Story:** As a maintainer reading a green suite, I want a test that swept nothing to fail, so that green means checked rather than skipped.

#### Acceptance Criteria

1. WHEN a test method's assertions all sit inside a loop over a collection that is discovered at run time, THEN the method SHALL assert a floor on that collection before the loop.
2. The floor SHALL be a lower bound on size, not merely a non-null check, and its failure message SHALL say that the walk stopped finding what it guards — following the wording already used at `ProblemStoreIsolation.Tests.cs:67`.
3. WHERE the expected population is known and stable, the floor SHALL be a number with headroom rather than the exact count, so that adding a case does not require editing the guard.
4. This requirement SHALL NOT apply to a loop over a literal array declared in the test itself, which cannot be empty. Twelve such loops were examined and are correct as written; they are named in the design document so that a later pass does not re-open them.

**The sites.** Eight sweep files discovered on disk:

| Site | What goes unchecked if the sweep is empty |
| --- | --- |
| `src/diagrams/sparql/backend/EtAlii.Adp.Diagram.Sparql.Tests/ExampleCorpus.Tests.cs:69` | Every vendored folder's `readme.md` and `LICENSE.md` |
| `src/diagrams/azure-pipeline/backend/EtAlii.Adp.Diagram.AzurePipeline.Tests/PipelineElementMapper.Tests.cs:81` | Element id uniqueness across the fixture corpus |
| `src/diagrams/azure-pipeline/backend/EtAlii.Adp.Diagram.AzurePipeline.Tests/PipelineElementMapper.Tests.cs:488` | That every fixture maps without throwing |
| `src/diagrams/azure-pipeline/backend/EtAlii.Adp.Diagram.AzurePipeline.Tests/PipelineGraphBuilder.Tests.cs:345` | That every fixture builds a graph |
| `src/diagrams/azure-pipeline/backend/EtAlii.Adp.Diagram.AzurePipeline.Tests/PipelineRuleSet.Tests.cs:46` | That valid fixtures report nothing |
| `src/diagrams/azure-pipeline/backend/EtAlii.Adp.Diagram.AzurePipeline.Tests/PipelineWriter.Tests.cs:444` | That every editable stage still parses after a rename |
| `src/diagrams/wardley-map/backend/EtAlii.Adp.Diagram.WardleyMap.Tests/WardleyElementMapper.Tests.cs:214` | Fixture coverage of the mapper |
| `src/diagrams/wardley-map/backend/EtAlii.Adp.Diagram.WardleyMap.Tests/WardleyWriter.Tests.cs:301` | That every fixture round-trips unchanged |

The five azure-pipeline sites share a second weakness worth fixing in the same pass: each enumerates `Directory.GetFiles("Fixtures", ...)` by **relative** path, which resolves against the process working directory rather than the test binary's folder. Their siblings elsewhere in the tree use `AppContext.BaseDirectory`.

**Eleven more sweep a collection produced by the code under test**, where an empty result is not a missing fixture but the regression itself:

| Site | Turns green when |
| --- | --- |
| `src/diagrams/helm-charts/backend/EtAlii.Adp.Diagram.HelmCharts.Tests/HelmContextPropertyProvider.Tests.cs:32` | The derived graph has no nodes or edges |
| `src/diagrams/wardley-map/backend/EtAlii.Adp.Diagram.WardleyMap.Tests/WardleyToolboxProvider.Tests.cs:83` | The toolbox offers no items |
| `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4.Tests/BigBankPlc.Tests.cs:148` | The workspace has no views |
| `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4.Tests/C4LayoutCrowding.Tests.cs:161` | The workspace has no views |
| `src/diagrams/mindmap/backend/EtAlii.Adp.Diagram.Mindmap.Tests/MindmapLayout.Tests.cs:88` | The document has no nodes |
| `src/diagrams/mindmap/backend/EtAlii.Adp.Diagram.Mindmap.Tests/MindmapLayout.Tests.cs:113` | The named node has no children |
| `src/diagrams/mindmap/backend/EtAlii.Adp.Diagram.Mindmap.Tests/MindmapLayout.Tests.cs:223` | No node has children with boxes |
| `src/diagrams/ansible-structure/backend/EtAlii.Adp.Diagram.AnsibleStructure.Tests/ZeroWrites.Tests.cs:64` | The graph is empty, so no read path is exercised |
| `src/diagrams/helm-charts/backend/EtAlii.Adp.Diagram.HelmCharts.Tests/ZeroWrites.Tests.cs:48` | The graph is empty, so no read path is exercised |
| `src/diagrams/azure-pipeline/backend/EtAlii.Adp.Diagram.AzurePipeline.Tests/PipelineContextActionProvider.Tests.cs:454` | No element ids are produced |
| `src/diagrams/azure-pipeline/backend/EtAlii.Adp.Diagram.AzurePipeline.Tests/PipelineContextActionProvider.Tests.cs:496` | No writing action is offered |

`WardleyToolboxProvider.Tests.cs` is the sharpest of these, and the reason is worth stating precisely because an earlier draft of this document overstated it. **Three of its four tests pass vacuously on an empty palette** — the uniqueness check at line 80 compares an empty count against an empty count, and the loops at lines 72 and 96 do not run. The file is saved by a single assertion: `ItOffersWhatRequirement132Asks` at line 58 compares the labels against an exact eight-element array, which an empty palette fails. So the file's honesty rests entirely on one exact-equality check. Loosen it to a subset or a `Contains` — exactly the edit someone makes to stop a test looking brittle — and all four go vacuous at once. The floor's value here is that it makes the protection structural rather than incidental.

### Requirement 2 — `Assert.All` over a collection that may be empty carries a floor

**User Story:** As a maintainer, I want `Assert.All` to mean "all of several", so that a provider returning nothing is a failure rather than a pass.

#### Acceptance Criteria

1. WHEN a test calls `Assert.All` on a collection produced at run time, THEN it SHALL assert that collection non-empty first.
2. The scan found 132 `Assert.All` call sites; the great majority sit in files that already pin a count nearby and are out of scope. The five in files that pin nothing SHALL be corrected:
   * `src/backend/EtAlii.Adp.Backend.Tests/Unit Tests/Hierarchy/EditorFilePropertyProvider.Tests.cs:51`
   * `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4.Tests/C4Export.Tests.cs:40`
   * `src/diagrams/databricks/backend/EtAlii.Adp.Diagram.Databricks.Tests/Diagram.Tests.cs:25`
   * `src/diagrams/databricks/backend/EtAlii.Adp.Diagram.Databricks.Tests/Diagram.Tests.cs:45`
   * `src/diagrams/rdf/backend/EtAlii.Adp.Diagram.Rdf.Tests/RdfLayout.Tests.cs:61`
3. `RdfLayout.Tests.cs:61` SHALL be read as the worst of the five: it asserts that every projected node has a layout position, which is exactly the assertion a projection returning no nodes satisfies vacuously.

### Requirement 3 — A tree walk anchors on something that identifies the repository

**User Story:** As a maintainer, I want a guard's root walk to find the right root or fail, so that it never quietly guards a different folder.

#### Acceptance Criteria

1. WHEN a test locates a root by walking up from `AppContext.BaseDirectory`, THEN its anchor SHALL be either two co-located paths or one distinctive multi-segment path.
2. Seventeen of the eighteen walks in the suite already satisfy this and SHALL be left alone. `WorkspaceLock.Tests.cs:37` (a folder beside a file) and `DocumentationLinks.Tests.cs:43` (two folders) are the two-part form; `ProblemStoreIsolation.Tests.cs:100` and `RdfExamples.Tests.cs:21` are the distinctive-path form.
3. `src/diagrams/sparql/backend/EtAlii.Adp.Diagram.Sparql.Tests/ExampleCorpus.Tests.cs:17` SHALL be brought into line. It anchors on the bare folder name `examples`, and a second `examples` directory sits further up the same walk path at `src/examples`. It is correct today only because the nearer one wins.
4. The correction SHALL follow the module's own sibling, `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4.Tests/Examples.Tests.cs:36`, which requires `examples` and `backend` to sit together.

### Requirement 4 — The vendoring rule is guarded the same way everywhere

**User Story:** As the repository's owner, I want the licence rule enforced identically for every vendored corpus, so that no module's examples can arrive without their licence.

#### Acceptance Criteria

1. Vendored example data is admitted only under a permissive licence, and the licence file is downloaded with the data and kept beside it. Two guards enforce this today and they are not equivalent.
2. `src/diagrams/rdf/backend/EtAlii.Adp.Diagram.Rdf.Tests/RdfExamples.Tests.cs:110` is the sound one: it asserts `readme.md` and `LICENSE.md` for every vendored folder, and its file carries a floor.
3. `src/diagrams/sparql/backend/EtAlii.Adp.Diagram.Sparql.Tests/ExampleCorpus.Tests.cs:69` states the same rule with no floor and on the weak anchor of Requirement 3. It SHALL be raised to the rdf guard's standard.
4. WHEN a module vendors example data in future, THEN its licence guard SHALL carry a floor from the outset.

### Requirement 5 — An extraction that finds nothing is a failure

**User Story:** As a maintainer, I want a parser inside a guard to prove it parsed something, so that a broken pattern cannot switch the guard off.

#### Acceptance Criteria

1. `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/DocumentationLinks.Tests.cs:66` walks five delivered documents and checks that every relative link resolves. Its final assertion is that the dead-link list is empty.
2. Nothing asserts that any link was **extracted**. IF `LinkExpression()` stopped matching — a regex edit, or a document adopting reference-style links — THEN zero links would be checked and the test would pass.
3. The guard SHALL assert a floor on the number of relative links it extracted across the delivered set.
4. The per-target tolerance documented at `DocumentationLinks.Tests.cs:113` — that a target the pattern misses goes unchecked rather than failing wrongly — SHALL be preserved. This requirement is about total extraction failure, not about individual targets, and the distinction SHALL be stated in the comment beside the new floor.

### Requirement 6 — `tests.md` records outcomes, not only intentions

**User Story:** As a maintainer, I want to see which manual checks have actually been run, so that the document is a record rather than a backlog.

#### Acceptance Criteria

1. The document holds 66 entries. Its accuracy is good and this specification asserts no defect in its content: all 19 file references resolve, every entry names a spec that exists in `.spec-workflow/specs/` or its archive, and 65 of 66 carry an explicit **Expected**. The 66th, *No canvas grows a scrollbar of its own*, deliberately has none because it points at an automated test; it is correct as written.
2. Nine entries carry a **Verified** line. Fifty-seven have no recorded outcome, so the document cannot presently distinguish a check that passed from one nobody has run.
3. Each entry SHALL carry an explicit verification state.
4. The five entries currently recorded as pending SHALL NOT be modified by this specification's implementation; they are owned elsewhere and are being executed.
5. Two entries tag an informal origin (`build tooling`, `editor/undo batch`) where every other entry names a spec. This is cosmetic, is recorded here so it is not rediscovered, and SHALL be left alone unless a later pass has reason to touch those entries.

## Non-Functional Requirements

### Reliability

* Every added assertion SHALL be verified to fail before it passes, by temporarily emptying the collection it floors. A floor added without that check is the same defect this specification exists to remove.
* No change SHALL alter what a test asserts about its subject. A change under this specification that alters an expected value has exceeded its scope.

### Maintainability

* The floors SHALL be spelled the same way across the suite, so that a later scan can find them mechanically. `ProblemStoreIsolation.Tests.cs:67` is the reference spelling.
* WHERE a floor number is chosen, the comment beside it SHALL say what population it is a floor on, so that a future reader can tell a deliberate margin from a stale constant.

### Verification

* The four gates SHALL pass locally before merge, judged by exit code: `dotnet test --solution EtAlii.Adp.slnx` and `dotnet format style --verify-no-changes --severity info` from `src/backend/`, and `npm test` and the typecheck from `src/client/`.
* The scan behind this document was read-only and deliberately did not run the suite. Implementation SHALL run it.

## Out of Scope

* The client suite (`*.test.ts(x)` under `src/client/src`) was not scanned for this pattern and is not covered here.
* Coverage gaps — subjects with no test at all — are a separate question from guards that cannot fail, and are not addressed.
* Widening any guard's scope. `DocumentationLinks` guards five delivered documents by deliberate decision; this specification adds a floor to that walk and does not enlarge it.
