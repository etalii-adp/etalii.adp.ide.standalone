# Delivery report: dotnet-dependency-graph-disl-fbl

The delivery report that task 10 asks for. It closes the conversion of the .NET dependency graph (`dotnet/dependency-graph`) to FBL. The DISL half was in place before this specification began. The report covers four things: every language gap as a candidate change for etalii-adp/etalii.adp (Requirement 8.4), every definition defect repaired, every finding struck with its test, and every difference ruled on in tasks 8 and 9. It also accounts for every change to a fixture or to a task-1 test since task 1 landed (Requirement 8.3), and records the follow-ups left unbuilt. Written 2026-10-21, against standalone `develop` `f6f01ed5a` and etalii.adp `develop` `65a31d0`.

## The pull requests

| Task | Repository | Pull request | Merged as |
| --- | --- | --- | --- |
| 1. Fixtures and the frozen transcript | standalone | #545 | `3be00e21c` |
| 2. Library work L1 to L5 | standalone | #551 | `7771e79ce` |
| 3. The definition's persistence, the companion's gaps, the example binding | etalii.adp | #383 | `1db4137c8` |
| 4. Bundle the definition, embed the binding | standalone | #554 | `f67204c5e` |
| 5. Read the solution through the plugin | standalone | #557 | `de8367f32` |
| 6. Derive the rows from the plugin's reading | standalone | #560 | `08c2f3775` |
| 7. Watch the plugin's paths through the library | standalone | #562 | `4c9ca9300` |
| 8. Write a move through the library's registration writer | standalone | #563 | `eb64488a3` |
| 9. A new document from the binding's template, as a `.slnx` | standalone | #565 | `43a551209` |
| 10. The companion's closing text | etalii.adp | #405 | open |
| 10. The bundled companion, the pages and this report | standalone | the pull request that carries this file | open, after #405 |

## Language gaps, as candidate changes for etalii-adp/etalii.adp

These are the eight gaps of the requirements' section C. The companion (`definitions/diagrams/dotnet-dependency-graph.md`, *Language gaps*) records each one with the same construct, fixture and resolution. The implementation added none. The one point it met that FBL does not provide, a `settle` on a file body, belongs to gap 1.

| # | Candidate change | Construct concerned | Shown by | Held meanwhile by |
| --- | --- | --- | --- | --- |
| 1 | **FBL: a subject that is one file and the files it references** (B2, B4, B10, B15). Candidate: a plugin operation `resolve` for a file body. It receives the bytes read so far and delivers the paths and folder listings it needs next, repeated until it asks for nothing. The host reads them under section 16, hands all of them to `read`, and watches them as section 10.3 says. That includes a `settle` on such a body. | FBL 3.2, 7.5, 10, 10.3, 11.2: a file body's `read` gets its own bytes, and only a folder subject has `watch` and `settle` | `outside/` (`Findings.Tests.B2_…`: 4 files read, 2 outside the solution's folder, 1 handed over); 88 of 123 projects of `EtAlii.Adp.slnx` outside its folder; `DotNetWatching.Tests` (a wildcard's new match and a versions file created later, each missed by the module's earlier watcher); `Binding.Tests` (`settle: 250` on a file body) | **`DotNetSolutionSubject`**, the named stand-in, through which the plugin opens the other files. The module hands the binding's settle to the shared watcher itself (`DotNetSolutionSubject.Settle`). |
| 2 | **FBL: reading outside the workspace as part of a reading** (B6). Candidate: the binding declares the roots a plugin may be handed, such as the NuGet cache, so consent is asked once and the reading stays a function of its inputs. | FBL 16 (consent), FBL 11.4 (determinism) | `ProjectReader.cs` (the walk to the root), `PackageDescriptionReader.cs` (`NUGET_PACKAGES`, `~/.nuget/packages`), `cache/` and `outside/` | **`DotNetSolutionSubject`**. The binding's `x-environment` names the variable (Q3). |
| 3 | **FBL: an element with several source entries, or a file as its entry** (B7). **DISL: whether a plugin may deliver a grouped element as stored** (A5). | FBL 11.2 (one span per element); DISL 4.11.1 (grouped is derived) | `cpm/` (one wildcard `Include`, two relations); the transcript's *descriptions and conflicts*; `DotNetSolutionPlugin.Tests` (sources of a project, a reference, a wildcard reference and a package declared twice) | One source per element (Q6), with its file (`FblElement.File`, L2), its line, and its start tag's bytes in a UTF-8 file. The summaries stay stored (Q5). |
| 4 | **FBL: whether `read` may deliver a relation with an end that names nothing** (B8). Candidate: one sentence in FBL 11.2, either way. | FBL 5.4, 11.2; DISL 11.2 | No fixture; `DependencyGraph.Derive`, recorded in the companion's *Known gaps* | The plugin names both ends, and the shared builder leaves the missing end unset (Q5). |
| 5 | **FBL: a template per extension, and creating under a read-only binding** (B11, B12). Candidate: a template per extension, and a sentence in FBL 3.4 that creating a new body is not writing it. | FBL 13, 8.2, 3.4 | `new-document/` (`Findings.Tests.B11_…`), `headerless/` (`DotNetDependencyGraphFindings.Tests.B11_…`). Both differences are resolved by task 9. | `.slnx` first in `claims.extensions` and as the primary extension; `TemplateDocumentFactory`; core's sibling fallback (Q4). |
| 6 | **FBL: a body that is not UTF-8 where its format declares its encoding** (B16). Candidate: the `xml` family honours an XML declaration's encoding. The half on XML namespaces in selectors (B17) is struck; see *Findings struck*. | FBL 2.6 | `encoding/` (`Findings.Tests.B16_…`: today's reader reads `Encoded.Package 1.2.3`, and a declared `xml` binding reports `std.unparseable`) | The plugin reads every file itself, and gives a UTF-16 entry its line and no span (`DotNetSolutionPlugin.Tests.AProjectFileInUtf16_HasItsLine_AndNoSpan`). |
| 7 | **FBL: the sentence of a refused first placement** (B13). Candidate: a reason beside `createOnFirstPlacement: false`, as `readOnly` takes one. | FBL 8.4 | `DotNetRegistration.Tests.AMove_OfADocumentOpenedWithoutARegistration_IsRefusedWithTodaysSentence` | The session's sentence, with its three other host sentences. |
| 8 | **FBL 11.5's row for this tool** (B3). Candidate: the row says what is evaluated (wildcards, the nearest `Directory.Packages.props`) and what is not (imports, conditions, properties). | FBL 11.5, informative | The module readme, *What the readers resolve*; `cpm/` | The companion and the readme say it. |

## Definition defects repaired

| Finding | Defect | Repaired in | Held by |
| --- | --- | --- | --- |
| A1 | `persistence` was `plugin:net.etalii.adp.dotnet.solution` with a split `files` pattern | etalii.adp #383: `format: "fbl"`, `binding` to the module's `dotnet-dependency-graph.fbl#solution`, no `files`; bundled by #554 | `BundledDefinition.Tests.ThePersistence_IsFbl_WithoutFiles_AndAPatternThatAdmitsTodaysIds` |
| A2 | Two stale sentences in the companion ("FBL binds one file…", "A persistence plugin cannot report problems"), repeated in `DotNetDefinition.cs` | etalii.adp #383 (companion); #554 and #560 (the code comment) | The companion as bundled; `DotNetDefinition`'s class comment |
| A3 | No id `pattern`; DISL 11.5's default rejects today's ids | etalii.adp #383: `^(project|package|depends):.{1,1024}$` | The same `BundledDefinition` test |
| B13 | The refusal had two homes | etalii.adp #383: the binding says `"readOnly": true` and repeats no sentence; the sentence is `behavior.messages` `std.readOnly` alone | `Binding.Tests.TheRefusal_IsStatedOnce_InTheDefinition_AndReadsAsToday` |

The module defect of B11 was also repaired, in task 9: a new document was XML under `.sln`. It is now `New.slnx`, made from the binding's template (`DotNetNewDocument.Tests`). B3 is a defect in FBL's informative text, not in the definition. It is not repaired here and is candidate 8 above.

## Findings struck

| Finding | Struck because | Test | Ruling |
| --- | --- | --- | --- |
| B17 (XML namespaces) | It does not occur for a default namespace. The FBL library's `xml` family reads `Fixtures/namespace/Old/Old.csproj` by its written element names, as today's readers do. | `FindingsTests.B17_AProjectFileWithADefaultNamespace_IsReadByItsElementNames` (it reads `[Namespaced.Package 2.0.0]`) | User, in chat, 2026-10-20. The fixture stays in the corpus. |

No other finding was struck. B2, B11 (both halves), B14 and B16 were each seen to fail for their reasons in task 1 and still pin their sentences. B11's two tests now assert that the difference is gone (see *Changes to fixtures and task-1 tests*).

## Differences ruled on

### Task 8: the registration writer (finding B14 and Requirement 5.2)

Every registration of the corpus is written back byte-identical when nothing moves, through both writers (`DotNetDependencyGraphRegistrations.Tests`). Where the library and core differ when an entry is placed, the user ruled **"Library's way"** on 2026-10-21:

1. Entries are ordered by the byte order of the id's UTF-8 encoding (FBL 8.3), not by .NET's UTF-16 ordinal order. Shown by `registration-order/` (`DotNetDependencyGraphFindings.Tests.B14_APlacedId_GoesWhereEachWritersOrderPutsIt`, `DotNetRegistration.Tests.AMove_WritesTheEntryWhereTheLibraryOrdersIt_AndItsUndoRestoresTheBytes`).
2. A new line takes the registration's own line ending, not always CRLF (`AFirstMove_IntoARegistrationOfLfLines_WritesLfLines_AndItsUndoRestoresTheBytes`).
3. A position of -0.0001 is written `0`, not `-0`. It is held by the same test's expected string. Its assertion stopped at the line ending, so the `-0` part was not seen failing separately (recorded in #563).

On an undo after the registration changed on disk, the user ruled **"Refuse"** on 2026-10-21. The undo is refused with `SplicedFile.DriftUndo` and writes nothing, where core's inverse wrote over the change (`AnUndo_AfterTheRegistrationChangedOnDisk_IsRefused_AndWritesNothing`).

The placement was ruled in chat: `PlaceInRegistrationCommand` lives in `EtAlii.Adp.Hierarchy`, beside `SetRegistrationLayoutCommand`, as the design recommended.

### Task 9: the new document (Q4 and the amendment of 2026-10-11)

- **Ruled by the user, 2026-10-11:** `.slnx` is the primary extension, first in `claims.extensions`, and core's sibling derivation falls back to the alternate. A header-less registration beside a lone `.sln` still opens it. Beside both serializations it opens `Both.slnx`, the one opening Q4 changes (`DiagramFilePair.Derivation.Tests`, `DotNetDependencyGraphFindings.Tests.B11_…`).
- **A call made in task 9, on the recommended option:** the transcript diff has two entries, not the one the task named. They are the new-document entry (`New.sln` to `New.slnx`, answers unchanged) and `Fixtures/solution/Empty.slnx`, which became a `sameAs` of `New.slnx` because it now has the same bytes under the same extension. The Q4 opening change does not show in the transcript, which does not record which body a registration opens.
- **A call made in task 9, on the recommended option:** Add on a file offers the alternate extension too, so a lone `.sln` can still be registered. Side effect: `.slnx` and RDF `.nt` files get an "Add as diagram" offer. Put to the user below.

### Earlier rulings this specification relied on

- Task 2, 2026-10-20: the shared watcher is `WatchedFolderStore` in `EtAlii.Adp.Files`, not a `PathWatch` in the FBL library (the design records it under L4).
- The requirements' open questions Q1 to Q6 stand at their defaults, apart from Q4 as ruled.

## Changes to fixtures and task-1 tests since task 1 landed

Listed with `git log 3be00e21c..HEAD` and `git diff -M 3be00e21c HEAD` over the module test project's `Fixtures/`, `Findings.Tests.cs`, `CorpusUntouched.Tests.cs`, `Parity/DotNetTranscript.cs`, `Parity/dotnet.transcript.json`, and the FBL library's `RealFiles/DotNetDependencyGraphFindings.Tests.cs`. Nothing else under `Fixtures/` changed. No fixture's bytes changed. No check was weakened. No finding test was skipped.

| Commit | Task | File | Change | Why it is not a gap closed by changing the evidence |
| --- | --- | --- | --- | --- |
| `2f3c42a19` | 5 | `Parity/DotNetTranscript.cs` | Members made `internal` so the new builder parity test reads the same corpus. `WriteGraphAsync` takes the store's `DotNetBody` instead of the bare graph, and the transcript's `FixedStore` hands out that body. | The generator records the same fields from the same reading. The checked-in transcript did not change (task 5 kept it byte-identical). |
| `7ed688cb9` | 6 | `Parity/DotNetTranscript.cs` | The hand-built graphs are written through `DotNetBody.Of(graph)`, because the rows are now derived from the body's model. | Same: the transcript is byte-identical, and the test was seen to fail against a changed row derivation. |
| `0cebe9acc` | 9 | `Fixtures/new-document/New.sln` → `New.slnx` | Renamed, 100% similarity, bytes unchanged. | The fixture is "a new document as core creates it" (Requirement 1.1). Core creates it under the primary extension, which Q4 as ruled made `.slnx`. |
| `0cebe9acc` | 9 | `Findings.Tests.cs` | `B11_ANewDocument_IsTheSlnxSerializationUnderTheClassicExtension` renamed `…UnderItsOwnExtension`, and its pin is now `null`, with the old pinned sentence kept in a comment. `SerializationOf` follows the swapped constants. | The finding was resolved by the ruled change. The test still computes the same observation from the factory and the reader, and would show the old sentence again if the defect came back. |
| `0cebe9acc` | 9 | `RealFiles/DotNetDependencyGraphFindings.Tests.cs` | The B11 header-less test derives core's sibling from a definition with both extensions, and asserts no difference and that core opens `Both.slnx`, with the old pin in a comment. | Same: this is the Q4 change ruled on 2026-10-11. B14 in the same file is unchanged and still pins its difference. |
| `0cebe9acc` | 9 | `Parity/dotnet.transcript.json` | Regenerated on purpose: the new-document entry's path, and `Empty.slnx` recorded as `sameAs` it. | The change Requirement 1.3 allows (Q4), reviewed in #565. |
| this task | 10 | `Parity/DotNetTranscript.cs` | One comment: "finding B11's new document **was** Empty.slnx's bytes under .sln until task 9 made it a .slnx". | Comment only. The sentence had become false. |

`CorpusUntouched.Tests.cs` is unchanged since task 1.

## Follow-ups, recorded and not built

1. **A missing project file is not watched.** `DotNetSolutionPlugin.Watch` names the project files the solution *resolved*. One the solution names but that is not on disk becomes a failure and is left out, so its arrival causes no re-read, unless it lands in a folder a wildcard `Include` already lists (as in `DotNetWatching.Tests.AProjectAWildcardBeginsToMatch_IsReadAgain`). The design's error scenario 3 says "Its path stays in the watch paths". The code does that for an unreadable file, not for a missing one.
2. **A writable plugin's `Plan` forwards `Place`.** `PluginBody.Plan` refuses before the plugin only for a read-only binding (L5). For a writable plugin-backed binding, a `ModelChange.Place` reaches the plugin's `Plan` rather than staying with the registration. This does not affect this tool, whose binding is read-only.
3. **The undo after a hand edit returns `Failure` rather than `Outdated`.** `RestoreRegistrationCommandHandler` refuses with `CommandResult.Failure(SplicedFile.DriftUndo)`, so the step stays on the undo stack. The shared `RestoreDocumentCommand` uses `CommandResult.Outdated`, with which the history drops the steps of a file changed outside it.
4. **Add on a file offers the alternate extension**, so `.slnx` and RDF `.nt` files get an "Add as diagram" offer (task 9's call, made on the recommended option).

## The second coverage diff

Every requirement reference was extracted from `tasks.md`, every acceptance criterion was listed from `requirements.md`, and the two sets were diffed. **38 criteria, 38 claimed, none unclaimed, none claimed that does not exist.** Each criterion was then traced to a file and a string on the branch. A script checked that each string occurs in its file. Every trace was found, but 3.4 is held by stated text and by the absence of code rather than by a fixture (see below).

| Criterion | Traced to |
| --- | --- |
| 1.1 | `Findings.Tests.cs` `B2_…`, `B16_…`; `DotNetDependencyGraphFindings.Tests.cs` `B14_…`, `B11_…`; `Fixtures/headerless/`, `outside/`, `new-document/`, `registration-order/`, `encoding/`, `namespace/` in `dotnet.transcript.json` |
| 1.2 | `Findings.Tests.cs` `Shows(`, `B17_AProjectFileWithADefaultNamespace_IsReadByItsElementNames` (struck) |
| 1.3 | `dotnet.transcript.json` `"sameAs": "…/new-document/New.slnx"`; `DotNetTranscript.Tests.cs` `ADP_WRITE_PARITY_TRANSCRIPTS` |
| 2.1 | the backend project's `dotnet-dependency-graph.fbl` `EmbeddedResource`; the binding's `"reader": { "plugin": "net.etalii.adp.dotnet.solution"`; `Binding.Tests` `TheBinding_HandsTheSolutionToThePlugin_WithNoRuleOfItsOwn` |
| 2.2 | the binding's `"registrationOnly": true`; `Binding.Tests` `TheBinding_ClaimsBothSerializations_SharedAndThroughARegistrationOnly` |
| 2.3 | the bundled `.dis`'s `"pattern": "^(project|package|depends):.{1,1024}$"`; `BundledDefinition.Tests` `ThePersistence_IsFbl_WithoutFiles_AndAPatternThatAdmitsTodaysIds` |
| 2.4 | the binding's `"readOnly": true`; `Binding.Tests` `TheRefusal_IsStatedOnce_InTheDefinition_AndReadsAsToday` |
| 2.5 | the bundled companion's `## Language gaps` |
| 3.1 | `DotNetSolutionPlugin.cs` `public sealed class DotNetSolutionPlugin(DotNetSolutionSubject subject) : IPersistencePlugin` |
| 3.2 | `DotNetSolutionPlugin.cs` `ProjectReferenceType = "ProjectReference"`; `Parity/DotNetBuilderParity.Tests.cs` against `HandBuiltDotNetModel` |
| 3.3 | `DotNetSolutionSubject.cs` `public string? Description(string packageId` |
| 3.4 | the module readme's *Not resolved, deliberately* (conditions, imports, properties, `Exclude`, transitive packages); the companion's *What is deliberately not resolved*; no code in `ProjectReader.cs` evaluates a `Condition` or an `Import`. **No fixture exercises a condition or an import**, so this trace is to stated text and to the absence of code, not to a test. |
| 3.5 | `DotNetSolutionPlugin.Tests` `ABrokenSolution_OpensEmpty_WithItsReason_AndNoFinding` |
| 3.6 | `DotNetSolutionPlugin.Tests` `AProject_IsAtItsEntryInTheSolution_AReferenceAtItsDeclaration_AndAPackageAtItsFirstDeclaration` |
| 3.7 | `DotNetBody.cs` `DislModelBuilder.From(Reading, DotNetDefinition.Specification)`; `Parity/HandBuiltDotNetModel.cs` in the test project only |
| 3.8 | `DotNetSolutionPlugin.Tests` `ABrokenProjectFile_CostsThatProjectOnly` |
| 3.9 | `DotNetTranscript.Tests.cs` `CheckedInPath` (byte-for-byte comparison) |
| 4.1 | `DotNetSolutionPlugin.cs` `public IReadOnlyList<string> Watch(PluginReadResult last) => _dependsOn;`; `DependencyGraphStore.cs` `BodyOf(snapshot).WatchPaths` |
| 4.2 | `DotNetWatching.Tests` `TheSettle_IsTheBindings_250Milliseconds` |
| 4.3 | `DotNetDependencyGraphSession.Tests` `AReReadOfAnEditedCorpusSolution_KeepsEverySurvivingPosition_AndPushesOnlyTheDifferences` |
| 4.4 | `DotNetWatching.Tests` `AProjectAWildcardBeginsToMatch_IsReadAgain`, `AVersionsFileCreatedAboveAProjectLater_IsReadAgain` |
| 5.1 | `DotNetDependencyGraphSession.cs` `PlaceInRegistrationCommand`; `DotNetRegistration.Tests` `AMove_WritesTheEntryWhereTheLibraryOrdersIt_AndItsUndoRestoresTheBytes` |
| 5.2 | `DotNetDependencyGraphRegistrations.Tests` `TheLibrary_WritesTheRegistrationBackByteIdentical_WhenNothingMoved` (a theory over every registration of the corpus), with `TheCorpus_HoldsTheRegistrationWhoseIdsOrderDifferentlyInUtf8AndUtf16` guarding that the corpus holds `Order.adp`; `DotNetDependencyGraphFindings.Tests` `B14_APlacedId_GoesWhereEachWritersOrderPutsIt`; the rulings above |
| 5.3 | `ServiceCollection.AddDotNetDependencyGraph.cs` `new TemplateDocumentFactory(Diagram.DependencyGraph, DotNetBody.Binding)`; `DotNetNewDocument.Tests` `ANewDocument_IsNeverWrittenOverAnExistingSolution`, `ANewDocument_ReadsThroughTheBinding_WithNoFailure_AndHoldsNoProject` |
| 5.4 | `DotNetRegistration.Tests` `AMove_OfADocumentOpenedWithoutARegistration_IsRefusedWithTodaysSentence` |
| 6.1 | the companion's table row `` | `DotNetDependencyGraphLayout` | `` and the classes listed there, each present in the product project |
| 6.2 | `src/backend/EtAlii.Adp.Hierarchy/TemplateDocumentFactory.cs`; `git grep` for `SolutionWatcher`, `DotNetSolutionDocumentFactory` and `Build(` in `DotNetDefinition.cs` over product code: no match |
| 6.3 | the companion's `## What stays code in the standalone module, and why` |
| 7.1 | `PluginBody.cs` `WatchPaths`; `_Model/PluginReadRequest.cs` `Args` |
| 7.2 | `Plugins/PluginBody.Tests.cs` (the placement never reaches a read-only binding's plugin) |
| 7.3 | `git grep -i dotnet -- src/backend/EtAlii.Adp.Specification.Fbl/` matches only `YamlDotNet`, the package. The one match that named this specification, a remark on `OpenRegistration` added by task 8, was removed in this task. |
| 8.1 | the companion's gap rows, with `**`DotNetSolutionSubject`**, the module's named stand-in (Q1)` |
| 8.2 | etalii.adp `specifications/fbl/dotnet-dependency-graph.fbl` (#383) |
| 8.3 | *Changes to fixtures and task-1 tests* above; `Findings.Tests.cs` `Until then this pinned` |
| 8.4 | this report, *Language gaps, as candidate changes* |
| 9.1 | the module readme's `## How a solution is read`; `docs/creating-a-diagram-module.md` `TemplateDocumentFactory` |
| 9.2 | the gates of each pull request, and this task's, after a fresh-worktree build |
| 9.3 | `tasks.md`, every task's `_Requirements:` line; this diff |

**Two traces read back to their artefacts.** The question for each was whether the evidence is that criterion's, not whether it exists.

- **4.4.** `DotNetWatching.Tests.AProjectAWildcardBeginsToMatch_IsReadAgain` writes a host project whose `ProjectReference` is the wildcard `..\mods\*\*.csproj`. The solution names `mods/B/B.csproj`, which is not there yet, inside a folder the wildcard lists. After the diagram opened, it writes `mods/B/B.csproj`, waits for the store's re-read, and asserts the edge from the host to `B`. The file was never read, so only the wildcard's listed folder can hear it arrive. `AVersionsFileCreatedAboveAProjectLater_IsReadAgain` opens a project whose `PackageReference Include="Serilog"` has no version and no versions file above it. It then writes a `Directory.Packages.props` beside the solution, above the project, and asserts the package's versions gain `4.4.0`. Task 7 recorded both as failing first against develop `695b811e0` with "The project the wildcard began to match was not read." and "The versions file created above the project was not read." That is criterion 4.4's two cases exactly, and its "seen to fail against today's watcher first".
- **5.2.** The first trace named `DotNetDependencyGraphRegistrations.Tests.TheCorpus_HoldsTheRegistrationWhoseIdsOrderDifferentlyInUtf8AndUtf16`. Read back, that fact only asserts that the corpus holds `registration-order/Order.adp` and at least five registrations: it guards the evidence, it is not the evidence. **The trace was corrected to the theory beside it.** `TheLibrary_WritesTheRegistrationBackByteIdentical_WhenNothingMoved` runs once per registration of the corpus. It opens each with `OpenRegistration`, saves, places every `layout:` entry again at the position it holds, and asserts the bytes are identical and that no edit was recorded. That is the first half of 5.2. `Core_WritesTheRegistrationBackByteIdentical_WhenNothingMoved` holds core's writer to the same files. The second half, "where the order or line ending differs … shown by its fixture and ruled on before it lands", is `DotNetDependencyGraphFindings.Tests.B14_APlacedId_GoesWhereEachWritersOrderPutsIt` on the same fixture, ruled on 2026-10-21 before #563 merged. The evidence is 5.2's, not 5.1's: 5.1's byte-for-byte undo is held separately by `DotNetRegistration.Tests`.

## The pages

- **Updated in this task:**
  - The module readme gained *How a solution is read*. *What this type writes* now names the library's writer and the one created file. Its last sentence no longer says a refresh "can be asked for explicitly", since none is reachable.
  - `docs/creating-a-diagram-module.md`: the `IDiagramDocumentFactory` row said "It is never a class". It now names `TemplateDocumentFactory` for a type whose binding has a template.
  - The bundled companion and its `provenance.json`.
  - `ServiceCollection.AddDotNetDependencyGraph.cs`'s summary, which still said "Empty at task 1".
  - `OpenRegistration`'s remark in the FBL library, which named this specification (Requirement 7.3: no module name in a library change).
- **Checked, and no sentence had become false:**
  - `src/diagrams/readme.md`, `docs/architecture.md`, `docs/solution-structure.md` (167 tracked `.csproj` files, unchanged), `docs/tools.md` (the row's "the solution is never written" still holds; creating a new `.slnx` is not writing an existing one), and the module's `client/`, `api/` and `examples/` readmes.
  - The pages earlier tasks changed: `docs/solution-structure.md` (task 1), the module readme (task 5), `docs/creating-a-diagram-module.md` (task 9) and `tests.md` (task 9).
