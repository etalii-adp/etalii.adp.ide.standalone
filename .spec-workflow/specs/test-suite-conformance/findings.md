# Findings: the collection-walk census

**275 is an enumeration, not a defect count.** It counts every collection walked at run time across the nine groups this specification still had open; many of those walks are correctly floored already, and some are floored by construction. Which ones need a floor is a per-site judgement that stays with the specification: the twenty files named in the tasks are repaired, and everything else below is recorded with its path and left alone.

Written by the measurement lane, deliberately separate from the lane that states the count. The original defect was that one pass enumerated one thing and the confirmation reported another; splitting measurement from record is the control against repeating it.

Enumerated against the working tree at `935eb6d0`.

## The finding is 72, not 275

More than a quarter of every collection walked in these nine groups — **72 of 275** — sits in a private helper, a property getter or at file scope, and none of it was ever in view.

The original survey enumerated `[Fact]` bodies whose assertions sat inside a *top-level* `foreach`. An inner loop was never in scope, and neither was a helper. So the confirmation pass could truthfully verify *every site the survey named carries a floor* and report it as *every collection needing a floor has one* — two different claims, and only the first was earned. The 44 nested walks and the 72 in helpers are the structural reason the first count was wrong. The total is downstream of that.

The sharpest illustration is a helper rather than a test. `SparqlLayout.Tests.cs:29`, `AssertContainment`, is a private helper whose outer walk is over `projection.Regions` and whose two inner walks are filtered. If `Regions` comes back empty, the containment invariant asserts nothing — **for every test that calls it**. No site-based method could reach that site, because it is not a site.

## What was counted

A structural parse, not a search. Comments and string literals are blanked first — raw, verbatim and interpolated alike, because a single unbalanced brace inside a string wrecks brace matching — and the remaining text is walked by brace structure, which is what makes real nesting depth visible. Grep cannot see nesting, and nesting is the defect.

A construct counts as a collection-walk when an empty collection means its body never runs or its assertion is vacuously satisfied:

| Construct | Why it counts |
| --- | --- |
| `foreach (… in X)`, `X.ForEach(…)` | body never runs |
| `Assert.All(X, …)` | passes over an empty collection |
| `for (i = 0; i < X.Count; i++)` | runs zero times |
| `Assert.True(X.All(…))`, `Assert.False(X.Any(…))` | vacuously true of nothing |

**The last row is a taxonomy change and Agent 8 owns it.** `Assert.True(X.All(…))` and `Assert.False(X.Any(…))` are not among the design's four shapes, but they are satisfied by an empty collection in exactly the way `Assert.All` is. They were flagged rather than adopted unilaterally, and the ruling was to count them: the four shapes were written before anyone knew what the unit of measurement was, and excluding these to stay inside them would repeat the narrowing this recount exists to fix. Two sites in these groups match.

Counted **wherever it appears**: every method body, private helpers included, plus property getters and file scope. Restricting a scan to `[Fact]` bodies is the precise narrowing that produced the original undercount.

Scope: all 301 `.cs` files under the nine test projects, `bin` and `obj` excluded. 104 of them contain at least one walk.

## Why the total is evidence rather than a number

The same constructs were counted a second way, by a method that necessarily *over*counts — a raw regex over untouched text, comments and strings included — and every difference was resolved by reading it.

| | Count |
| --- | --- |
| Raw matches over untouched text | 304 |
| Reported by the structural scan | 275 |
| Difference, all accounted for | 29 |
| Matches falling outside anything the scan reported | **0** |

All 29 are `for` loops not bounded by a collection count: literal generators such as `i < 50` and `i < 400`, one `maturity <= 1d`, and fourteen walk-upward loops of the form `for (var directory = new DirectoryInfo(…); directory is not null; directory = directory.Parent)`. Each was sampled and read; none walks a collection.

**That cross-check is the part worth copying, because it changed the answer.** The first version of the parser missed seven walks. Three constructs defeat a method-declaration regex: a property getter (`public static TheoryData<string> Corpus { get { … } }`), and two signatures whose tuple types carry parentheses — `(byte[] Bytes, DateTime Written)` in a parameter list, `IReadOnlyList<(string, string, string)>` as a return type — where the regex stopped at the first `)`. The fix removed the dependency rather than patching it: walks are now enumerated across the whole file and method bodies only name them. **Without the cross-check this document would have said 268, with exactly the same confidence.** Counting a second way by a method that overcounts, then reading every difference, is what turns a count into evidence.

## One exempt category, verified rather than reasoned about

Emptying a `TheoryData` provider fails its theory by itself:

```
Xunit.MicrosoftTestingPlatform.XunitException: No data found for
EtAlii.Adp.Diagram.AzurePipeline.Tests.PipelineDocumentTests.EveryFixture_RoundTripsByteForByte
```

So `[MemberData]`-driven sites need no floor, alongside the literal arrays the design's Deviations already exempt. This applies **only** to a provider returning nothing; an `Assert.All` or `foreach` *inside* a theory body still needs its own floor and is counted below.

## The census

| Group | Files | Walks | In helper | Nested | Filtered | Literal |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| ansible-structure | 10 | 46 | 16 | 2 | 5 | 0 |
| backend-core | 30 | 64 | 36 | 12 | 8 | 3 |
| c4 | 14 | 42 | 3 | 9 | 1 | 4 |
| databricks | 3 | 4 | 0 | 0 | 0 | 0 |
| helm-charts | 8 | 20 | 6 | 1 | 0 | 0 |
| mindmap | 5 | 19 | 0 | 5 | 0 | 2 |
| rdf | 16 | 41 | 3 | 11 | 3 | 2 |
| sparql | 7 | 17 | 5 | 4 | 2 | 0 |
| wardley-map | 11 | 22 | 3 | 0 | 0 | 4 |
| **Nine groups** | **104** | **275** | **72** | **44** | **19** | **15** |

In-helper, nested, filtered and literal are overlapping subsets: one walk can be all four. Literal-array walks are exempt and are listed only so nobody "fixes" them.

Two scope notes. The brief said eight groups; tasks 2–10 are nine, so all nine are here. And c4 has two test projects — `EtAlii.Adp.Diagram.C4.Tests`, which the tasks name, and `EtAlii.Adp.C4.Tests`, which contains no `.cs` files at all. Only the first contributes.

## Three sites opened and read

The scan enumerates; it does not judge. These three were read by hand. Whether they get floors is a per-site judgement under the scope rule above.

**`sparql/…/SparqlLayout.Tests.cs:29` — `AssertContainment`.** A private helper, walks nested, inner two filtered. An empty `projection.Regions` means the containment invariant asserts nothing for every test that calls it.

```csharp
foreach (var region in projection.Regions)          // empty => nothing below ever runs
{
    foreach (var node in projection.Nodes.Where(…))  // filtered: empties on its own
        Assert.True(frame.Contains(nodeRect), …);
}
```

**`mindmap/…/MindmapLayout.Tests.cs:28` — `Compute_IsDeterministic`.** Both assertions are satisfied by emptiness, so a layout returning nothing for every document passes as loudly as a correct one. This is the self-comparing pattern and the vacuous-walk pattern in one test.

```csharp
Assert.Equal(first.Count, second.Count);
Assert.All(first, entry => Assert.Equal(entry.Value, second[entry.Key]));
```

**`ansible-structure/…/AnsibleLayout.Tests.cs:42` — `EveryNode_IsPlaced`.** Its own comment states the gap it leaves.

```csharp
// A node with no box would simply not be drawn, and nothing else would say so.
Assert.Equal(graph.Nodes.Count, boxes.Count);
Assert.All(graph.Nodes, node => Assert.True(boxes.ContainsKey(node.Id), …));
```

## The pattern to hunt: `Assert.Equal(x.Count, y.Count)`

It reads `0 == 0` on an empty pair. It looks like an assertion and floors nothing. Found independently in wardley by another agent, so it is hunted here as a pattern rather than recorded as an instance. **13 sightings across these nine groups.**

| Group | Site | Method |
| --- | --- | --- |
| ansible-structure | `AnsibleElementMapper.Tests.cs:380` | `UnauthoredElements_KeepTheirComputedPositions` |
| ansible-structure | `AnsibleElementMapper.Tests.cs:404` | `AStoredIdTheGraphNoLongerProduces_ChangesNothing` |
| ansible-structure | `AnsibleGraph.Tests.cs:225` | `EachPlay_GetsItsOwnColourIndex` |
| ansible-structure | `AnsibleLayout.Tests.cs:42` | `EveryNode_IsPlaced` |
| ansible-structure | `ZeroWrites.Tests.cs:144` | `AReposition_WritesTheRegistrationAndNothingOfAnsibles` |
| helm-charts | `HelmLayout.Tests.cs:87` | `TheLayout_IsDeterministic` |
| mindmap | `MindmapLayout.Tests.cs:28` | `Compute_IsDeterministic` |
| rdf | `OwlLayout.Tests.cs:112` | `TheLayout_IsDeterministic` |
| rdf | `RdfProjection.Tests.cs:61` | `TheConstructCorpus_ProjectsTheAdoptedPositions` |
| rdf | `ShaclLayout.Tests.cs:28` | `EveryCard_GetsAPosition_AndTheSameFileAlwaysOpensTheSameWay` |
| wardley-map | `WardleyDocument.Tests.cs:65` | `CodeLines_AreNumberedFromOne` |
| wardley-map | `WardleyDocument.Tests.cs:135` | `ReplaceLine_ChangesExactlyOneLine_AndLeavesEveryOtherByteAlone` |
| wardley-map | `WardleyToolboxProvider.Tests.cs:91` | `EveryEntryIsDescribed` |

## Filtered collections

A filtered walk empties without its source being empty, so a floor on the source does not cover it. **19 across these nine groups.**

| Group | Site | Collection |
| --- | --- | --- |
| ansible-structure | `AnsibleElementMapper.Tests.cs:233` | `delivered.Where(element => element.Type == AnsibleElementMapper.` |
| ansible-structure | `AnsibleElementMapper.Tests.cs:381` | `computed.Zip(arranged).Where(pair => !follows.Contains(pair.Firs` |
| ansible-structure | `AnsibleGraph.Tests.cs:88` | `targets.Where(e => e.SourceId == )` |
| ansible-structure | `AnsibleGraph.Tests.cs:91` | `targets.Where(e => e.SourceId == )` |
| ansible-structure | `ZeroWrites.Tests.cs:154` | `before.Where(pair => !string.Equals(pair.Key, registration, Stri` |
| backend-core | `AddDiagramFlow.Tests.cs:163` | `actions.Where(action => action.Id is not AddDiagramContextAction` |
| backend-core | `AzurePipelineFlow.Tests.cs:300` | `response.Entries.Entries_.Where(entry => entry.HasChildren && !I` |
| backend-core | `DatabricksFlow.Tests.cs:338` | `response.Entries.Entries_.Where(entry => entry.HasChildren && en` |
| backend-core | `DependencyInventory.Tests.cs:111` | `table.Keys.Where(name => !manifest.ContainsKey(name)).OrderBy(na` |
| backend-core | `DependencyInventory.Tests.cs:164` | `manifests.Where(File.Exists)` |
| backend-core | `NestedEntryLookup.cs:32` | `root.Entries.Entries_.Where(entry => entry.HasChildren)` |
| backend-core | `OwlFlow.Tests.cs:304` | `response.Entries.Entries_.Where(entry => entry.HasChildren && en` |
| backend-core | `RdfFlow.Tests.cs:309` | `response.Entries.Entries_.Where(entry => entry.HasChildren && en` |
| c4 | `C4LayoutCrowding.Tests.cs:126` | `layout.Boxes.Where(entry => entry.Key.StartsWith( , StringCompar` |
| rdf | `OwlProjection.Tests.cs:223` | `graph.Nodes.Where(node => node.OwnerId.Length > 0)` |
| rdf | `RdfProjection.Tests.cs:53` | `projection.Nodes.Where(node => node.Blank)` |
| rdf | `SkosSession.Tests.cs:302` | `held.Keys.Where(id => id.StartsWith( , StringComparison.Ordinal)` |
| sparql | `SparqlLayout.Tests.cs:34` | `projection.Nodes.Where(node => node.ScopePath == region.ScopePat` |
| sparql | `SparqlLayout.Tests.cs:46` | `projection.Regions.Where(nested => nested.ScopePath.StartsWith(p` |

## Two rulings from the specification lane

Both were referred here because they are judgement rather than measurement. Recorded, not repaired — the same treatment as the c4 anchor.

### The nine walks over an awaited call are exempt, and exemption is not soundness

**A floor there would assert the opposite of the module's contract.** `NoWriterSweep.Tests.cs:84` is the clearest case, and the code says so itself at the site: *"Every action the provider offers, executed - expected: there are none, so this loop is empty, which is the finding rather than a gap in the test."* sparql is a read-only reading. Demanding that this walk find something would require the module to offer context actions, which is precisely what it promises not to do.

So the discriminator is not the shape but the contract: **if an empty result would be a defect, the walk needs a floor; if an empty result is the promise, it must not have one.** The nine are the second kind.

**But none of them actually asserts the absence it relies on.** The loop at `:84` executes whatever it finds; find nothing and it passes, find something and it also passes, provided nothing throws. The sound form is the *inverse* of a floor — `Assert.Empty(await actions.DiscoverAsync(target, …))` — which would make the promise explicit and fail the day the module grew a writer.

That is not repaired here, and the reason is a rule this specification set for itself: *no change shall alter what a test asserts about its subject.* Adding `Assert.Empty` gives the test a new assertion it never made. It is a real gap, it is the right fix, and it belongs to whoever owns the no-writer promise — not to a pass whose entire discipline is one added assertion that changes no subject.

### The forked `LogCapture` is out of scope, and the reason it still matters

Three copies, differing by exactly one line each — the namespace — verified by diff rather than assumed from the line count:

* `src/backend/EtAlii.Adp.Backend.Tests/Support/LogCapture.cs` (118 lines)
* `src/backend/EtAlii.Adp.Diagram.Tests/Support/LogCapture.cs` (118 lines)
* `src/backend/EtAlii.Adp.Editor.Tests/Support/LogCapture.cs` (118 lines)

Each has a 10-line `LogCaptureCaptureSink.cs` beside it, so the fork is six files, not three.

**Out of scope**, because this specification is about guards that exist and cannot fail. A guard that was never written is a coverage gap, which the requirements put out of scope in as many words. Repairing it also breaks the design's one invariant — every repair is a single added assertion — since de-forking is a structural change, not an assertion.

**The consequence is recorded because it is the part that will matter later:** the fork has already cost a guard. A missing-file assertion was declined because writing it would have meant three more copies. That is duplication acting as a tax on new guards, which is a slower and less visible failure than a vacuous one, and worth someone's specification rather than a footnote in this one.

**Resolved on 2026-09-04, after this ruling was written.** The fork is gone: `ddbf5ef3` moved `LogCapture` into `src/TestSupport/`, six files across three test projects becoming two shared ones, compiled as source into every `*.Tests` project by the same condition that already shared `TestFolder` (`src/Directory.Build.targets:39-40`). `a812c7af` added a guard in a module test project against someone "tidying" it into a referenced assembly, which would silently break log capture everywhere because a `[ModuleInitializer]` runs only for the assembly it is compiled into. **So the blocked-guard consequence above is historical: the tax that made two agents decline a log assertion no longer exists.** The paragraph is kept rather than deleted, because the argument it makes — that duplication acts as a tax on new guards, and that this is slower and less visible than a vacuous assertion — outlived the instance that demonstrated it.

**The mechanism that fixed it**, which makes the future repair cheap: `src/Directory.Build.targets:9` compiles `src/TestSupport/TestFolder.cs` into every `*.Tests` project by an `EndsWith('.Tests')` condition. `TestFolder.cs` is the only file there today. Moving `LogCapture` beside it and adding one `Compile Include` line would de-fork all three copies without any project gaining a reference. Whoever takes this does not need to invent the arrangement — only to use it.

## The per-site pass, in progress

**51 of the 275 walks sit inside the twenty files this specification names**, at commit `e69fa531`. That is the scope the ruling set, and it is stated with its boundary because a bare count decays: azure-pipeline is not among them, having been recounted separately, and the other 224 are recorded here and left alone.

**Twelve of the 51 are exempt by rule**, and three of those rules are new — the census could not have applied them, because each needs a judgement about meaning rather than structure:

| Exemption | N | Sites |
| --- | :-: | --- |
| Literal array at the loop site | 2 | `C4LayoutCrowding.Tests.cs:107`, `MindmapLayout.Tests.cs:194` |
| **Field-held literal array** | 2 | `DocumentationLinks.Tests.cs:58` and `:73` walk `Documents`, a `static readonly string[]` of five names |
| `[MemberData]` provider | 1 | `ExampleCorpus.Tests.cs:54` — xUnit fails an empty provider itself |
| **Fixture setup, not a guard** | 6 | The `Directory.GetDirectories`/`GetFiles` pairs inside `CopyTree`-style helpers: `ansible ZeroWrites:259,263`, `HelmContextPropertyProvider:150,155`, `helm ZeroWrites:200,204`. These copy files; they assert nothing, and an empty copy surfaces downstream as a failing test rather than a passing one. |
| **Index loop, not a collection walk** | 1 | `WardleyWriter.Tests.cs:25` — `for` over `Math.Max(left, right)`, a bound rather than a collection |

The field-held literal array is worth naming: the census reads it as `derived`, correctly, because at the loop site it is an identifier. Only reading the declaration shows it cannot be empty. **That is the boundary between measurement and judgement in one line** — the census could not classify it and should not have tried.

**Three confirmed unfloored so far, all in the newly-visible class.** Two are the same helper in two modules:

* `ansible-structure ZeroWrites.Tests.cs:179` and `helm-charts ZeroWrites.Tests.cs:183` — `AssertUnchanged`, walking `before.OrderBy(…)`. **Its two pre-loop assertions do not save it:** `Assert.True(appeared.Length == 0)` and `Assert.True(vanished.Length == 0)` are *also* vacuously true of an empty snapshot. So if the fixture copy produced nothing, all three assertions pass and the module's zero-writes promise is asserted about no files at all. The same helper, byte-identical in shape, in two modules.
* `ansible-structure ZeroWrites.Tests.cs:154` — a filtered walk, `before.Where(pair => pair.Key != registration)`, with no floor on the filtered result. Weaker than the two above, because the test's primary assertion `Assert.Equal([registration], changed)` is sound; only the byte-comparison over the remaining files is vacuous.

**The rest of the 39 are still being read.** The sites the tasks named are repaired and verified; the residue is the nested and helper walks the original survey could not see, which is exactly where the census predicted the work would be. No total is claimed until every one of the 39 has been judged.

## Every walk, by group

`d` is nesting depth among walks: `d1` sits inside one other walk, `d2` inside two. `helper` marks a walk outside any `[Fact]`/`[Theory]` body. The last column is a **triage hint only** — it says whether the enclosing method contains something floor-shaped, which is a method-level answer to a per-collection question. It found all five floors placed by hand in azure-pipeline, and that is the whole of its validation. No statement in this document rests on it.

### ansible-structure — 46

| Site | Kind | d | Where | Class | Collection | Hint |
| --- | --- | :-: | --- | --- | --- | :-: |
| `AnsibleContextPropertyProvider.Tests.cs:49` | Assert.All |  | test | derived | `properties` | y |
| `AnsibleContextPropertyProvider.Tests.cs:72` | Assert.All |  | test | derived | `properties` |  |
| `AnsibleContextSourceResolver.Tests.cs:212` | foreach |  | helper | derived | `Directory.GetDirectories(source, , SearchOption.AllDirec` |  |
| `AnsibleContextSourceResolver.Tests.cs:216` | foreach |  | helper | derived | `Directory.GetFiles(source, , SearchOption.AllDirectories` |  |
| `AnsibleElementMapper.Tests.cs:58` | Assert.All |  | test | derived | `types` |  |
| `AnsibleElementMapper.Tests.cs:173` | Assert.All |  | test | derived | `plays` | y |
| `AnsibleElementMapper.Tests.cs:233` | foreach |  | test | filtered | `delivered.Where(element => element.Type == AnsibleElemen` |  |
| `AnsibleElementMapper.Tests.cs:257` | Assert.All |  | test | derived | `deltas` |  |
| `AnsibleElementMapper.Tests.cs:258` | Assert.All |  | test | derived | `deltas` |  |
| `AnsibleElementMapper.Tests.cs:305` | Assert.All |  | test | derived | `deltas` |  |
| `AnsibleElementMapper.Tests.cs:322` | foreach |  | helper | derived | `Directory.GetDirectories(source, , SearchOption.AllDirec` |  |
| `AnsibleElementMapper.Tests.cs:326` | foreach |  | helper | derived | `Directory.GetFiles(source, , SearchOption.AllDirectories` |  |
| `AnsibleElementMapper.Tests.cs:381` | foreach |  | test | filtered | `computed.Zip(arranged).Where(pair => !follows.Contains(p` |  |
| `AnsibleElementMapper.Tests.cs:406` | foreach |  | test | derived | `computed.Zip(arranged)` |  |
| `AnsibleGraph.Tests.cs:34` | Assert.All |  | test | derived | `uses` |  |
| `AnsibleGraph.Tests.cs:45` | Assert.All |  | test | derived | `imports` | y |
| `AnsibleGraph.Tests.cs:72` | Assert.All |  | test | derived | `dependencies` | y |
| `AnsibleGraph.Tests.cs:88` | Assert.All |  | test | filtered | `targets.Where(e => e.SourceId == )` | y |
| `AnsibleGraph.Tests.cs:91` | Assert.All |  | test | filtered | `targets.Where(e => e.SourceId == )` | y |
| `AnsibleGraph.Tests.cs:107` | Assert.All |  | test | derived | `EdgesOf(graph` |  |
| `AnsibleGraph.Tests.cs:108` | Assert.All |  | test | derived | `EdgesOf(graph` |  |
| `AnsibleGraph.Tests.cs:198` | Assert.All |  | test | derived | `targets` |  |
| `AnsibleGraph.Tests.cs:213` | Assert.All |  | test | derived | `plays` | y |
| `AnsibleGraph.Tests.cs:226` | Assert.All |  | test | derived | `plays` | y |
| `AnsibleGraph.Tests.cs:240` | Assert.All |  | test | derived | `graph.Edges` | y |
| `AnsibleLayout.Tests.cs:43` | Assert.All |  | test | derived | `graph.Nodes` |  |
| `AnsibleLayout.Tests.cs:102` | Assert.All |  | test | derived | `plays` |  |
| `AnsibleLayout.Tests.cs:126` | Assert.All |  | test | derived | `bandTop` | y |
| `AnsibleLayout.Tests.cs:155` | for-count |  | test | derived | `boxes` |  |
| `AnsibleLayout.Tests.cs:157` | for-count | 1 | test | derived | `boxes` |  |
| `AnsibleProjectReader.Tests.cs:68` | Assert.All |  | test | derived | `site.Imports` |  |
| `AnsibleProjectReader.Tests.cs:275` | foreach |  | helper | derived | `project.Playbooks` |  |
| `AnsibleProjectReader.Tests.cs:279` | foreach | 1 | helper | derived | `playbook.Plays` |  |
| `AnsibleProjectReader.Tests.cs:286` | foreach |  | helper | derived | `project.Roles` |  |
| `AnsibleProjectReader.Tests.cs:294` | foreach |  | helper | derived | `project.Inventories` |  |
| `AnsibleProjectStore.Tests.cs:265` | foreach |  | helper | derived | `Directory.GetDirectories(source, , SearchOption.AllDirec` |  |
| `AnsibleProjectStore.Tests.cs:269` | foreach |  | helper | derived | `Directory.GetFiles(source, , SearchOption.AllDirectories` |  |
| `AnsibleRuleSet.Tests.cs:185` | Assert.All |  | test | derived | `problems` |  |
| `AnsibleRuleSet.Tests.cs:260` | foreach |  | helper | derived | `files` |  |
| `AnsibleSession.Tests.cs:377` | foreach |  | helper | derived | `Directory.GetDirectories(source, , SearchOption.AllDirec` |  |
| `AnsibleSession.Tests.cs:381` | foreach |  | helper | derived | `Directory.GetFiles(source, , SearchOption.AllDirectories` |  |
| `ZeroWrites.Tests.cs:98` | foreach |  | test | derived | `ids` | y |
| `ZeroWrites.Tests.cs:154` | foreach |  | test | filtered | `before.Where(pair => !string.Equals(pair.Key, registrati` |  |
| `ZeroWrites.Tests.cs:179` | foreach |  | helper | derived | `before.OrderBy(pair => pair.Key, StringComparer.Ordinal)` |  |
| `ZeroWrites.Tests.cs:259` | foreach |  | helper | derived | `Directory.GetDirectories(source, , SearchOption.AllDirec` |  |
| `ZeroWrites.Tests.cs:263` | foreach |  | helper | derived | `Directory.GetFiles(source, , SearchOption.AllDirectories` |  |

### backend-core — 64

| Site | Kind | d | Where | Class | Collection | Hint |
| --- | --- | :-: | --- | --- | --- | :-: |
| `AddDiagramFlow.Tests.cs:121` | foreach |  | helper | derived | `options` |  |
| `AddDiagramFlow.Tests.cs:163` | Assert.All |  | test | filtered | `actions.Where(action => action.Id is not AddDiagramConte` | y |
| `AddDiagramFlow.Tests.cs:211` | Assert.All |  | test | derived | `prompt.ChoiceDialog.Options` | y |
| `AnsibleStructureFlow.Tests.cs:171` | foreach |  | test | derived | `before` |  |
| `AnsibleStructureFlow.Tests.cs:231` | foreach |  | test | derived | `ansibleFilesBefore` | y |
| `AnsibleValidationFlow.Tests.cs:176` | foreach |  | test | derived | `before` |  |
| `AzurePipelineFlow.Tests.cs:300` | foreach |  | helper | filtered | `response.Entries.Entries_.Where(entry => entry.HasChildr` |  |
| `AzurePipelineFlow.Tests.cs:306` | foreach | 1 | helper | derived | `children.Entries.Entries_` |  |
| `CreateDiagramFileFlow.Tests.cs:136` | foreach |  | helper | derived | `options` |  |
| `DatabricksFlow.Tests.cs:338` | foreach |  | helper | filtered | `response.Entries.Entries_.Where(entry => entry.HasChildr` |  |
| `DatabricksFlow.Tests.cs:343` | foreach | 1 | helper | derived | `children.Entries.Entries_` |  |
| `DependencyInventory.Tests.cs:95` | foreach |  | helper | derived | `manifest.OrderBy(pair => pair.Key, StringComparer.Ordina` |  |
| `DependencyInventory.Tests.cs:111` | foreach |  | helper | filtered | `table.Keys.Where(name => !manifest.ContainsKey(name)).Or` |  |
| `DependencyInventory.Tests.cs:128` | foreach |  | helper | derived | `XDocument.Load(path).Descendants( )` |  |
| `DependencyInventory.Tests.cs:152` | foreach |  | helper | derived | `globs.EnumerateArray()` |  |
| `DependencyInventory.Tests.cs:164` | foreach |  | helper | filtered | `manifests.Where(File.Exists)` |  |
| `DependencyInventory.Tests.cs:169` | foreach | 1 | helper | literal-array | `new[] { , }` |  |
| `DependencyInventory.Tests.cs:176` | foreach | 2 | helper | derived | `dependencies.EnumerateObject()` |  |
| `DependencyInventory.Tests.cs:227` | foreach |  | helper | derived | `Directory.EnumerateDirectories(prefix)` |  |
| `DependencyInventory.Tests.cs:248` | foreach |  | helper | derived | `File.ReadAllLines(path)` | y |
| `DiagramDiscoveryStartup.Tests.cs:256` | foreach |  | helper | derived | `File.ReadLines(LocateCatalog())` |  |
| `DocumentationLinks.Tests.cs:58` | foreach |  | test | derived | `Documents` |  |
| `DocumentationLinks.Tests.cs:73` | foreach |  | test | derived | `Documents` | y |
| `DocumentationLinks.Tests.cs:82` | foreach | 1 | test | derived | `LinkExpression().Matches(File.ReadAllText(fullPath))` | y |
| `ExampleRegistration.Tests.cs:90` | foreach |  | helper | derived | `Directory.EnumerateDirectories(Locate())` |  |
| `ExampleRegistration.Tests.cs:109` | foreach |  | helper | derived | `ExampleRoots()` |  |
| `ExampleRegistration.Tests.cs:111` | foreach | 1 | helper | derived | `Directory.EnumerateFiles(examples, , SearchOption.AllDir` |  |
| `ExampleRegistration.Tests.cs:207` | foreach |  | helper | derived | `ExampleRoots()` |  |
| `ExampleRegistration.Tests.cs:209` | foreach | 1 | helper | derived | `Directory.EnumerateFiles(examples, , SearchOption.AllDir` |  |
| `ExampleRegistration.Tests.cs:258` | foreach |  | test | derived | `placements` |  |
| `ExampleRegistration.Tests.cs:299` | foreach |  | test | derived | `bySubject` |  |
| `HelmChartsFlow.Tests.cs:213` | foreach |  | helper | derived | `before` |  |
| `HelmValidationFlow.Tests.cs:146` | foreach |  | test | derived | `before` |  |
| `NestedEntryLookup.cs:32` | foreach |  | helper | filtered | `root.Entries.Entries_.Where(entry => entry.HasChildren)` |  |
| `OwlFlow.Tests.cs:304` | foreach |  | helper | filtered | `response.Entries.Entries_.Where(entry => entry.HasChildr` |  |
| `OwlFlow.Tests.cs:309` | foreach | 1 | helper | derived | `children.Entries.Entries_` |  |
| `ProblemsFlow.Tests.cs:112` | foreach |  | test | literal-array | `new[] { mine, theirs }` | y |
| `RdfFlow.Tests.cs:309` | foreach |  | helper | filtered | `response.Entries.Entries_.Where(entry => entry.HasChildr` |  |
| `RdfFlow.Tests.cs:314` | foreach | 1 | helper | derived | `children.Entries.Entries_` |  |
| `ShapeOfFileAccess.Tests.cs:134` | for-count |  | helper | derived | `lines` |  |
| `ShapeOfFileAccess.Tests.cs:194` | foreach |  | helper | derived | `ProductionSources()` |  |
| `ShapeOfFileAccess.Tests.cs:198` | foreach | 1 | helper | derived | `Offences(relative, lines)` |  |
| `WardleyMapFlow.Tests.cs:450` | foreach |  | helper | derived | `options` |  |
| `WorkspaceLock.Tests.cs:102` | foreach |  | helper | derived | `globs.EnumerateArray()` |  |
| `WorkspaceLock.Tests.cs:130` | foreach | 1 | helper | derived | `Directory.EnumerateDirectories(prefix).OrderBy(path => p` |  |
| `AdpFileWriter.Tests.cs:55` | for-count |  | test | derived | `text` | y |
| `DiagramOptionTree.Tests.cs:39` | Assert.All |  | test | derived | `tree` |  |
| `DiagramOptionTree.Tests.cs:40` | Assert.All |  | test | derived | `tree` |  |
| `DiagramOptionTree.Tests.cs:57` | Assert.All |  | test | derived | `c4.Children` | y |
| `DiagramOptionTree.Tests.cs:159` | foreach |  | helper | derived | `nodes` |  |
| `DiagramOptionTree.Tests.cs:162` | foreach | 1 | helper | derived | `Flatten(node.Children ?? [])` |  |
| `EditorFilePropertyProvider.Tests.cs:51` | Assert.All |  | test | derived | `properties` |  |
| `HierarchyContextActionProvider.Tests.cs:415` | Assert.All |  | test | derived | `untouchable` | y |
| `HierarchyContextActionProvider.Tests.cs:416` | Assert.All |  | test | derived | `untouchable` | y |
| `HierarchyContextSourceResolver.Tests.cs:50` | foreach |  | helper | derived | `segments` |  |
| `HierarchyModel.Nesting.Tests.cs:74` | foreach |  | test | literal-array | `new[] { , }` |  |
| `HierarchyModel.Nesting.Tests.cs:197` | Assert.All |  | test | derived | `children` | y |
| `HierarchyNesting.Tests.cs:111` | Assert.All |  | test | derived | `placed` | y |
| `OpenAsTextContextActionProvider.Tests.cs:102` | Assert.All |  | test | derived | `actions` | y |
| `OpenAsTextContextActionProvider.Tests.cs:142` | Assert.All |  | test | derived | `choice.Request.Options` | y |
| `ProblemMaintenance.Tests.cs:127` | Assert.All |  | test | derived | `_store.Mutations` |  |
| `ProblemStore.Tests.cs:193` | Assert.All |  | test | derived | `raised` | y |
| `ProjectValidator.Tests.cs:253` | Assert.All |  | test | derived | `outcome.Problems` | y |
| `ProjectValidator.Tests.cs:349` | foreach |  | test | derived | `before` |  |

### c4 — 42

| Site | Kind | d | Where | Class | Collection | Hint |
| --- | --- | :-: | --- | --- | --- | :-: |
| `AwsDeployment.Tests.cs:165` | for-count |  | test | derived | `boxes` |  |
| `AwsDeployment.Tests.cs:167` | for-count | 1 | test | derived | `boxes` |  |
| `BigBankPlc.Tests.cs:158` | foreach |  | test | derived | `Workspace.Views` | y |
| `BigBankPlc.Tests.cs:162` | for-count | 1 | test | derived | `boxes` | y |
| `BigBankPlc.Tests.cs:164` | for-count | 2 | test | derived | `boxes` | y |
| `BigBankPlc.Tests.cs:210` | Assert.All |  | test | derived | `C4RuleSet.Validate(Workspace)` |  |
| `C4ElementMapper.Tests.cs:72` | Assert.All |  | test | derived | `nodes` | y |
| `C4Export.Tests.cs:45` | Assert.All |  | test | derived | `diagrams` | y |
| `C4Interop.Tests.cs:167` | foreach |  | test | derived | `edits` |  |
| `C4Interop.Tests.cs:308` | foreach |  | test | derived | `today` |  |
| `C4Layout.Tests.cs:54` | Assert.All |  | test | derived | `layout.Boxes.Values` | y |
| `C4Layout.Tests.cs:88` | for-count |  | test | derived | `boxes` |  |
| `C4Layout.Tests.cs:90` | for-count | 1 | test | derived | `boxes` |  |
| `C4Layout.Tests.cs:110` | for-count |  | test | derived | `boxes` |  |
| `C4Layout.Tests.cs:112` | for-count | 1 | test | derived | `boxes` |  |
| `C4Layout.Tests.cs:174` | foreach |  | test | literal-array | `new[] { , }` | y |
| `C4Layout.Tests.cs:220` | foreach |  | test | literal-array | `new[] { , }` | y |
| `C4Layout.Tests.cs:259` | for-count |  | test | derived | `boxes` |  |
| `C4Layout.Tests.cs:261` | for-count | 1 | test | derived | `boxes` |  |
| `C4LayoutCrowding.Tests.cs:60` | for-count |  | helper | derived | `boxes` |  |
| `C4LayoutCrowding.Tests.cs:62` | for-count | 1 | helper | derived | `boxes` |  |
| `C4LayoutCrowding.Tests.cs:107` | foreach |  | test | literal-array | `new[] { , }` | y |
| `C4LayoutCrowding.Tests.cs:126` | foreach |  | test | filtered | `layout.Boxes.Where(entry => entry.Key.StartsWith( , Stri` | y |
| `C4LayoutCrowding.Tests.cs:178` | foreach |  | test | derived | `workspace.Views` | y |
| `C4RuleSet.Tests.cs:340` | Assert.All |  | test | derived | `undrawn` | y |
| `C4RuleSet.Tests.cs:591` | Assert.All |  | test | derived | `problems` | y |
| `C4RuleSet.Tests.cs:604` | Assert.All |  | test | derived | `Validate(dsl)` |  |
| `C4StructurizrMirror.Tests.cs:64` | Assert.All |  | test | derived | `C4StructurizrMirror.MirroredRules` | y |
| `C4StructurizrMirror.Tests.cs:75` | Assert.All |  | test | derived | `C4StructurizrMirror.AdpOnlyRules` | y |
| `C4StructurizrMirror.Tests.cs:76` | Assert.All |  | test | derived | `C4StructurizrMirror.NotImplementedRules` | y |
| `C4StructurizrMirror.Tests.cs:119` | Assert.All |  | test | derived | `added` |  |
| `C4ToolboxProvider.Tests.cs:70` | Assert.All |  | test | derived | `Items(C4ViewKind.Container)` | y |
| `C4Verdict.cs:63` | for-count |  | helper | derived | `lines` |  |
| `Diagram.Tests.cs:55` | Assert.All |  | test | derived | `Diagram.Definitions` |  |
| `Diagram.Tests.cs:73` | Assert.All |  | test | derived | `withBodies` | y |
| `Diagram.Tests.cs:82` | Assert.All |  | test | derived | `Diagram.Definitions` | y |
| `Examples.Tests.cs:95` | foreach |  | test | derived | `registrations` | y |
| `Examples.Tests.cs:128` | foreach |  | test | derived | `Directory.GetFiles(root, , SearchOption.AllDirectories)` |  |
| `Examples.Tests.cs:191` | foreach |  | test | derived | `workspace.Views` |  |
| `Examples.Tests.cs:194` | for-count | 1 | test | derived | `boxes` |  |
| `Examples.Tests.cs:196` | for-count | 2 | test | derived | `boxes` |  |
| `Fixtures.Tests.cs:126` | foreach |  | test | literal-array | `new[] { , }` |  |

### databricks — 4

| Site | Kind | d | Where | Class | Collection | Hint |
| --- | --- | :-: | --- | --- | --- | :-: |
| `DatabricksValidator.Tests.cs:66` | Assert.All |  | test | derived | `problems` | y |
| `Diagram.Tests.cs:32` | Assert.All |  | test | derived | `Diagram.Definitions` | y |
| `Diagram.Tests.cs:59` | Assert.All |  | test | derived | `Diagram.Definitions` | y |
| `JobParser.Tests.cs:93` | for-count |  | test | derived | `job.Tasks` |  |

### helm-charts — 20

| Site | Kind | d | Where | Class | Collection | Hint |
| --- | --- | :-: | --- | --- | --- | :-: |
| `DependencyResolution.Tests.cs:111` | Assert.All |  | test | derived | `result.Dependencies` | y |
| `Examples.Tests.cs:43` | Assert.All |  | test | derived | `chart.Dependencies` | y |
| `Examples.Tests.cs:44` | Assert.All |  | test | derived | `graph.Resolution.Dependencies` | y |
| `HelmContextPropertyProvider.Tests.cs:47` | foreach |  | test | derived | `everything` | y |
| `HelmContextPropertyProvider.Tests.cs:57` | Assert.All | 1 | test | derived | `rows` | y |
| `HelmContextPropertyProvider.Tests.cs:150` | foreach |  | helper | derived | `Directory.EnumerateDirectories(source, , SearchOption.Al` |  |
| `HelmContextPropertyProvider.Tests.cs:155` | foreach |  | helper | derived | `Directory.EnumerateFiles(source, , SearchOption.AllDirec` |  |
| `HelmElementMapper.Tests.cs:25` | foreach |  | helper | derived | `graph.Nodes` |  |
| `HelmElementMapper.Tests.cs:45` | Assert.All |  | test | derived | `elements` |  |
| `HelmGraph.Tests.cs:26` | Assert.All |  | test | derived | `declares` | y |
| `HelmGraph.Tests.cs:83` | Assert.All |  | test | derived | `includes` | y |
| `HelmLayout.Tests.cs:26` | Assert.All |  | test | derived | `graph.Nodes` | y |
| `HelmLayout.Tests.cs:27` | Assert.All |  | test | derived | `boxes.Values` | y |
| `HelmLayout.Tests.cs:88` | foreach |  | test | derived | `first` |  |
| `HelmLayout.Tests.cs:107` | Assert.All |  | test | derived | `graph.Nodes` |  |
| `HelmRuleSet.Tests.cs:121` | Assert.All |  | test | derived | `problems` | y |
| `ZeroWrites.Tests.cs:82` | foreach |  | test | derived | `everything` | y |
| `ZeroWrites.Tests.cs:183` | foreach |  | helper | derived | `before.OrderBy(pair => pair.Key, StringComparer.Ordinal)` |  |
| `ZeroWrites.Tests.cs:200` | foreach |  | helper | derived | `Directory.GetDirectories(source, , SearchOption.AllDirec` |  |
| `ZeroWrites.Tests.cs:204` | foreach |  | helper | derived | `Directory.GetFiles(source, , SearchOption.AllDirectories` |  |

### mindmap — 19

| Site | Kind | d | Where | Class | Collection | Hint |
| --- | --- | :-: | --- | --- | --- | :-: |
| `Commands.Tests.cs:331` | Assert.All |  | test | derived | `saved.Nodes` |  |
| `Fixtures.Tests.cs:60` | Assert.All |  | test | derived | `nodes` | y |
| `Fixtures.Tests.cs:89` | foreach |  | test | literal-array | `new[] { , , , , , , , , }` |  |
| `Fixtures.Tests.cs:91` | Assert.True(.All/.Any) | 1 | test | derived | `document.Descendants(name)` |  |
| `MindmapDocument.Tests.cs:104` | Assert.All |  | test | derived | `document.Nodes` |  |
| `MindmapLayout.Tests.cs:29` | Assert.All |  | test | derived | `first` |  |
| `MindmapLayout.Tests.cs:99` | Assert.True(.All/.Any) |  | test | derived | `document.Nodes` |  |
| `MindmapLayout.Tests.cs:104` | foreach |  | test | derived | `document.Nodes` |  |
| `MindmapLayout.Tests.cs:107` | foreach | 1 | test | derived | `node.Children.GroupBy(child => boxes[child.Id].X >= 0)` |  |
| `MindmapLayout.Tests.cs:110` | for-count | 2 | test | derived | `children` |  |
| `MindmapLayout.Tests.cs:133` | foreach |  | test | derived | `document.Find( )!.Children` | y |
| `MindmapLayout.Tests.cs:142` | foreach |  | test | derived | `document.Find( )!.Children` | y |
| `MindmapLayout.Tests.cs:194` | foreach |  | test | literal-array | `new[] { , , , }` |  |
| `MindmapLayout.Tests.cs:254` | foreach |  | test | derived | `parents` | y |
| `MindmapLayout.Tests.cs:270` | foreach | 1 | test | derived | `childBoxes.GroupBy(box => box.CenterX > boxes[parent.Id]` | y |
| `MindmapLayout.Tests.cs:291` | for-count |  | test | derived | `boxes` |  |
| `MindmapLayout.Tests.cs:293` | for-count | 1 | test | derived | `boxes` |  |
| `MindmapSession.Tests.cs:67` | Assert.All |  | test | derived | `elements` |  |
| `MindmapSession.Tests.cs:245` | Assert.All |  | test | derived | `document.Root.Children` |  |

### rdf — 41

| Site | Kind | d | Where | Class | Collection | Hint |
| --- | --- | :-: | --- | --- | --- | :-: |
| `OwlLayout.Tests.cs:94` | Assert.All |  | test | derived | `graph.Nodes` |  |
| `OwlLayout.Tests.cs:113` | Assert.All |  | test | derived | `first` |  |
| `OwlLayout.Tests.cs:161` | Assert.All |  | test | derived | `graph.Nodes` |  |
| `OwlLayout.Tests.cs:173` | for-count |  | test | derived | `boxes` |  |
| `OwlLayout.Tests.cs:175` | for-count | 1 | test | derived | `boxes` |  |
| `OwlProjection.Tests.cs:223` | Assert.All |  | test | filtered | `graph.Nodes.Where(node => node.OwnerId.Length > 0)` | y |
| `OwlProjection.Tests.cs:226` | Assert.All | 1 | test | derived | `graph.Edges` | y |
| `OwlSession.Tests.cs:251` | foreach |  | test | derived | `readmitted` | y |
| `RdfExamples.Tests.cs:38` | foreach |  | helper | derived | `Documents()` |  |
| `RdfExamples.Tests.cs:108` | Assert.All |  | test | derived | `folders` |  |
| `RdfLayout.Tests.cs:67` | Assert.All |  | test | derived | `projection.Nodes` | y |
| `RdfParser.Tests.cs:87` | Assert.All |  | test | derived | `model.Triples` | y |
| `RdfProjection.Tests.cs:53` | Assert.All |  | test | filtered | `projection.Nodes.Where(node => node.Blank)` | y |
| `RdfProjection.Tests.cs:88` | Assert.All |  | test | derived | `projection.Edges` | y |
| `RdfSession.Tests.cs:199` | foreach |  | test | literal-array | `new[] { new DiagramViewport(anchor.X, anchor.Y, anchor.X` |  |
| `RdfSession.Tests.cs:206` | foreach | 1 | test | derived | `session.UpdateView(viewport)` |  |
| `RdfSession.Tests.cs:211` | foreach | 2 | test | derived | `add.Elements` |  |
| `RdfSession.Tests.cs:218` | foreach | 2 | test | derived | `remove.ElementIds` |  |
| `RdfSession.Tests.cs:231` | foreach | 1 | test | derived | `edges` |  |
| `RdfSession.Tests.cs:406` | foreach |  | test | derived | `readmitted` | y |
| `ShaclActions.Tests.cs:124` | Assert.All |  | test | derived | `actions` |  |
| `ShaclExamples.Tests.cs:58` | foreach |  | test | derived | `bodies` | y |
| `ShaclExamples.Tests.cs:112` | Assert.All |  | test | derived | `chips` | y |
| `ShaclExamples.Tests.cs:115` | Assert.All |  | test | derived | `projection.Edges` | y |
| `ShaclLayout.Tests.cs:29` | Assert.All |  | test | derived | `projection.Cards` |  |
| `ShaclLayout.Tests.cs:33` | Assert.All |  | test | derived | `positions` |  |
| `ShaclProjection.Tests.cs:48` | Assert.All |  | test | derived | `card.Rows` | y |
| `ShaclProjection.Tests.cs:214` | Assert.All |  | test | derived | `projection.Cards` | y |
| `ShaclProjection.Tests.cs:215` | Assert.All |  | test | derived | `projection.Cards` | y |
| `ShaclProperties.Tests.cs:118` | Assert.All |  | test | derived | `readOnly` |  |
| `ShaclProperties.Tests.cs:188` | Assert.All |  | test | derived | `rows` | y |
| `ShaclSession.Tests.cs:350` | foreach |  | test | derived | `readmitted` | y |
| `ShaclTargetChips.Tests.cs:35` | Assert.All |  | helper | derived | `projection.Cards` |  |
| `ShaclTargetChips.Tests.cs:36` | Assert.All | 1 | helper | derived | `projection.Edges` |  |
| `ShaclTargetChips.Tests.cs:132` | Assert.All |  | test | derived | `card.Targets` | y |
| `SkosSession.Tests.cs:224` | foreach |  | test | derived | `readmitted` | y |
| `SkosSession.Tests.cs:272` | foreach |  | test | literal-array | `new[] { new DiagramViewport(anchor.X, anchor.Y, anchor.X` | y |
| `SkosSession.Tests.cs:279` | foreach | 1 | test | derived | `session.UpdateView(viewport)` | y |
| `SkosSession.Tests.cs:284` | foreach | 2 | test | derived | `add.Elements` | y |
| `SkosSession.Tests.cs:291` | foreach | 2 | test | derived | `remove.ElementIds` | y |
| `SkosSession.Tests.cs:302` | foreach | 1 | test | filtered | `held.Keys.Where(id => id.StartsWith( , StringComparison.` | y |

### sparql — 17

| Site | Kind | d | Where | Class | Collection | Hint |
| --- | --- | :-: | --- | --- | --- | :-: |
| `ExampleCorpus.Tests.cs:54` | foreach |  | helper | derived | `Directory.EnumerateFiles(ExamplesRoot(), , SearchOption.` |  |
| `ExampleCorpus.Tests.cs:113` | foreach |  | test | derived | `folders` | y |
| `ExampleCorpus.Tests.cs:153` | foreach |  | helper | derived | `scope.Children.SelectMany(Scopes)` |  |
| `NoWriterSurface.Tests.cs:50` | foreach |  | test | derived | `storeTypes` |  |
| `NoWriterSurface.Tests.cs:52` | foreach | 1 | test | derived | `type.GetMethods(BindingFlags.Public \| BindingFlags.NonPu` |  |
| `NoWriterSweep.Tests.cs:84` | foreach |  | test | derived | `await actions.DiscoverAsync(target, CancellationToken.No` |  |
| `NoWriterSweep.Tests.cs:86` | foreach | 1 | test | derived | `group.Actions` |  |
| `NoWriterSweep.Tests.cs:97` | foreach |  | test | derived | `await properties.DescribeAsync(target, CancellationToken` |  |
| `SparqlLayout.Tests.cs:29` | foreach |  | helper | derived | `projection.Regions` |  |
| `SparqlLayout.Tests.cs:34` | foreach | 1 | helper | filtered | `projection.Nodes.Where(node => node.ScopePath == region.` |  |
| `SparqlLayout.Tests.cs:46` | foreach | 1 | helper | filtered | `projection.Regions.Where(nested => nested.ScopePath.Star` |  |
| `SparqlLayout.Tests.cs:82` | Assert.All |  | test | derived | `projection.Nodes` |  |
| `SparqlLayout.Tests.cs:83` | Assert.All |  | test | derived | `projection.Regions` |  |
| `SparqlParser.Tests.cs:90` | Assert.All |  | test | derived | `union.Children` | y |
| `SparqlProjection.Tests.cs:276` | Assert.All |  | test | derived | `result.Edges` | y |
| `SparqlProviders.Tests.cs:50` | foreach |  | test | derived | `(string[]) [ , , SparqlElementMapper.HeaderId]` | y |
| `SparqlProviders.Tests.cs:58` | Assert.All |  | test | derived | `everyRow` | y |

### wardley-map — 22

| Site | Kind | d | Where | Class | Collection | Hint |
| --- | --- | :-: | --- | --- | --- | :-: |
| `WardleyCommands.Tests.cs:545` | foreach |  | test | literal-array | `new ICommand[] { new RemoveWardleyElementCommand(_path, ` |  |
| `WardleyCommands.Tests.cs:571` | foreach |  | test | literal-array | `new Func<string, ICommand>[] { _ => new AddWardleyElemen` |  |
| `WardleyContextPropertyProvider.Tests.cs:143` | foreach |  | test | literal-array | `new[] { IdOf( ), IdOf( ), LinkId(), ChildId() }` |  |
| `WardleyContextPropertyProvider.Tests.cs:149` | Assert.All |  | test | derived | `rows` |  |
| `WardleyContextPropertyProvider.Tests.cs:160` | foreach |  | test | literal-array | `new[] { IdOf( ), IdOf( ), IdOf( ), LinkId(), ChildId(), ` |  |
| `WardleyContextPropertyProvider.Tests.cs:166` | Assert.All |  | test | derived | `rows` |  |
| `WardleyContextPropertyProvider.Tests.cs:259` | Assert.All |  | test | derived | `rows` |  |
| `WardleyDocument.Tests.cs:18` | foreach |  | helper | derived | `Directory.EnumerateFiles(FixturesPath, ).Order(StringCom` |  |
| `WardleyDocument.Tests.cs:66` | for-count |  | test | derived | `lines` |  |
| `WardleyDocument.Tests.cs:136` | for-count |  | test | derived | `after` |  |
| `WardleyElementMapper.Tests.cs:226` | foreach |  | test | derived | `fixtures` | y |
| `WardleyElementMapper.Tests.cs:325` | Assert.All |  | test | derived | `elements` | y |
| `WardleyEvolution.Tests.cs:40` | for-count |  | test | derived | `stages` | y |
| `WardleyIdentities.Reconcile.Tests.cs:29` | Assert.All |  | test | derived | `entries` | y |
| `WardleyParser.Tests.cs:20` | foreach |  | helper | derived | `Directory.EnumerateFiles(FixturesPath, ).Order(StringCom` |  |
| `WardleyParser.Tests.cs:74` | Assert.All |  | test | derived | `map.Components` |  |
| `WardleyParser.Vocabulary.Tests.cs:201` | Assert.All |  | test | derived | `submaps` | y |
| `WardleyRuleSet.Tests.cs:228` | Assert.All |  | test | derived | `problems` | y |
| `WardleyToolboxProvider.Tests.cs:83` | foreach |  | test | derived | `_toolbox.Items` |  |
| `WardleyToolboxProvider.Tests.cs:107` | foreach |  | test | derived | `_toolbox.Items` |  |
| `WardleyWriter.Tests.cs:25` | for-count |  | helper | derived | `Math.Max(left` |  |
| `WardleyWriter.Tests.cs:314` | foreach |  | test | derived | `fixtures` | y |

