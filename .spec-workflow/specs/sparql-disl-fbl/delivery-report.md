# Delivery report: sparql-disl-fbl

The delivery report that task 11 asks for. It closes the conversion of the SPARQL query diagram (`w3c/sparql`, `.rq`) to DISL and FBL. It covers every language gap as a candidate change for etalii-adp/etalii.adp, every definition defect repaired, every module defect found and whether it was repaired, the changes of Q3 left for afterwards, and every *read* finding with whether it occurred (Requirement 11.3, 11.4). It also accounts for every change to a fixture or to a task-1 test since task 1 landed (Requirement 11.2), records the follow-ups left unbuilt, and holds the second coverage diff (Requirement 12.4). Written 2026-10-23, against standalone `develop` `786e8447e` and etalii.adp `develop` `c3fc0dc2f`.

## The pull requests

| Task | Repository | Pull request | Merged as |
| --- | --- | --- | --- |
| 1. The oracle: fixtures and the frozen transcript | standalone | #612 | `a15157862` |
| 2. The definition at DISL 0.3, the companion, the example binding | etalii.adp | #458 | `5d74618cd` |
| 3. Spans and columns in the parser | standalone | #616 | `df1633f61` |
| 4. Library: a derived relation's `owner`, a derived `display` attribute | standalone | #620 | `efaf79c43` |
| 5. Library: ephemeral placements, notation refusals, the diagram's own form | standalone | #623 | `2e1800da1` |
| 6. Bundle the definition | standalone | #625 | `4a94e8271` |
| 7. Derive the drawn query | etalii.adp, standalone | #476, #630 | `4690b4f18`, `c7e9bd199` |
| 7, follow-up. Keep a DISL diagram's elements by type and by id | standalone | #638 | `a33398bb8` |
| 8. Derive the rows, the findings, the refusals and the empty toolbox | etalii.adp, standalone | #485, #640 | `7de3204a1`, `77e10c65e` |
| 9. Read the query through the binding and its plugin | standalone | #647 | `dc622db31` |
| 10. Compile the client's canvas definition | etalii.adp, standalone | #487, #643 | `c8c4a7f85`, `cd85034b7` |
| 10, follow-up. A selectable element of shape `none` takes a press | standalone | #649 | `13e127b07` |
| 11. The companion's closing text | etalii.adp | #490 | `c3fc0dc2f` |
| 11. The bundled companion, the pages and this report | standalone | the pull request that carries this file | open, after #490 |

## Language gaps, as candidate changes for etalii-adp/etalii.adp

The seven of the requirements' section C, the eighth of `SparqlChrome` that the design added, and two the implementation added. The companion (`definitions/diagrams/sparql-query.md`, section 4, bundled as `src/diagrams/sparql/definition/sparql-query.md`) records each with the same construct, fixture and resolution. None was worked around in the definition (Requirement 11.2).

| # | Candidate change | Construct concerned | Shown by | Held meanwhile by |
| --- | --- | --- | --- | --- |
| 1 | **FBL: who projects a plugin-read format** (B2). FBL 11.2 says that `read` delivers stored elements only, one per entry, and 11.5's "why a plugin" column says "parsing" where it says "projection". | FBL 11.1, 11.5 against DISL 4.11.1 | The transcript, reproduced by both the hand-written projection and the definition (`SparqlDerivedDrawing.Tests.TheDerivedDrawing_IsTheHandWrittenOne`) | Q1 at its default: the plugin delivers one record per entry and the definition derives the drawing |
| 2 | **FBL: the span of an element with no entry of its own** (B3), if gap 1 is answered with a plugin that may deliver one | FBL 11.2 | `construct-where-short.rq`; `SparqlRecords.Tests.TheTemplateOfConstructWhere_StatesNoEntryOfItsOwn` | The `CONSTRUCT WHERE` template's `Scope` takes its where clause's span |
| 3 | **FBL: the sentence of a placement refused without a registration** (B9). `registration.createOnFirstPlacement` takes a text, as `readOnly` does. | FBL 8.4 | `SparqlSession.Tests.ABareUnregisteredQuery_RefusesNamingTheMissingRegistration`; the transcript's refusal | `SparqlSession`'s sentence |
| 4 | **DISL and FBL: ids numbered by place among siblings** (B10). DISL 11.5.3 says whether a place-numbered id of a stored element is ephemeral by default, and FBL 8.5 what a host owes when it is not. | DISL 11.5.3, FBL 8.5 | `second-optional-positioned.rq` with its `.adp` | Q5 at its default: region and subquery ids stay stored names |
| 5 | **DISL: a stored container position that moves its computed contents** (A3). `respect` gains a value, or a container's notation a property. | DISL `layout.respect` | `SparqlLayout.Tests.AnAuthoredRegionAnchor_MovesTheFrameWithItsContentsFollowing`; the transcript's region move | `SparqlLayout` |
| 6 | **DISL: a percent-encoding function** (A10) | DISL 12.4 | `literals-datatypes-end-alike.rq`, `path-with-spaces.rq`; `SparqlRecords.Tests.ALiteralsKey_IsItsLexicalFormPercentEncoded_WithItsLanguageOrDatatype` | The plugin delivers each end's encoded `Key` |
| 7 | **DISL: what chrome shows for an unreadable body** (B6) | DISL 6.13 `canvas.header` | `broken.rq`; `SparqlSession.Tests.AQueryThatDoesNotParse_OpensWithOnlyItsHeader` | The header reads the diagram's default form, `SELECT` (reproduced) |
| 8 | **DISL: chrome on the wire, and its refusals** (A12) | DISL 6.13 | The transcript's `header:query` and `truncation` entries | `SparqlChrome`, with the two ids in `x-sparql` |
| 9 | **DISL: whether an end of a relation can be its owner.** 4.11.3 says whether the nearest common ancestor is taken over the ends' ancestors alone or may be one of the ends. Found in task 4; the library reads it strictly (an end is never its own ancestor, as `ancestors()` reads in 12.2), the option recorded there for this list. | DISL 4.11.3 | The DISL library's `DerivedRelationOwner.Tests.AStoredRelation_BelongsToTheNearestCommonAncestorOfItsEnds_OrTheDiagram` (`l4`, from a node to its own child, belongs to the diagram) | This definition states `owner` on both derived relations, so its drawing does not depend on the reading |
| 10 | **DISL: a chrome band row repeated over a list.** A header row takes a `forEach` over a list attribute. Found in task 10. | DISL 6.13 `canvas.header.rows` | `sparqlCompiledDefinition.test.tsx`, which states the difference against its oracle | The plugin joins the modifiers into one `modifiers` text with " · " |

## Definition defects repaired

| Finding | Defect | Repaired in | Held by |
| --- | --- | --- | --- |
| A1 | The draft was DISL 0.1 | etalii.adp #458, rewritten at 0.3; bundled by #625 | `BundledDefinition.Tests.TheBundledDefinition_LoadsWithoutDiagnostics` |
| A2 | Every drawn type a stored type "the plugin fills", `limits.maxElements` for the cut, one joined unused-prefix sentence | #458: derived types with `parent` and `owner`, `ephemeral`, `location`, `std.unparseable`, `canvas.header`, `limits.budgets`, `refusals`, `readOnlyReasons` | `SparqlDerivedDrawing.Tests`, `SparqlDerivedFindings.Tests`, `SparqlDerivedRefusals.Tests`, `SparqlDerivedRows.Tests` against the oracle `HandWrittenSparql` |
| A4 | `persistence` named a plugin format with `files` and `metadata` | #458: `format: "fbl"` with `binding`; the module's `sparql.fbl` (#647) | `SparqlBinding.Tests` |
| A6 | No `std.unparseable` entry | #458: `sparql.unparseable`, severity error, today's sentence | `SparqlDerivedFindings.Tests.EveryFinding_IsTheHandWrittenValidators` (`broken.rq`) |
| A7 | One unused-prefix sentence for all prefixes | #458: a rule on the stored `Prefix`, at its line | the same test; `SparqlRecords.Tests.APrefix_IsUsed_WhereTheTextUsesItOutsideItsDeclaration` |
| A10 (definition half) | `ids.pattern` `\S+` refused an edge id holding a path written with spaces | #458: any character after the prefix | `path-with-spaces.rq` in the transcript and the derived drawing |
| found in task 2 | The attribute name `kind` is reserved in DISL 0.3 | #458: `scopeKind` and `constraintKind` | the load test above |
| found in task 7 | The variables' order and `from` cost more than the CEL limit past about 400 patterns, so the derived drawing came out empty | etalii.adp #476: `from` is `mentions()`, a `firstScope` attribute, the `drawOrder` key `[1, 0, e.firstScope, e.name]` | `SparqlDerivedDrawing.Tests` over `over-the-bound.rq` and the generated 400-pattern query |
| found in task 8 | Four computed form values written as CEL literals inside strings (`"'No'"`, `"''"`), so the inspector showed the quotes | etalii.adp #485 | `SparqlDerivedRows.Tests.EveryRow_IsTheHandWrittenProvidersRow` |
| found in task 10 | The notation drew other than today: an open edge marker, hover tooltips, the label offset, an ellipsis on the defining expression, the banner's sentence | etalii.adp #487 | `sparqlCompiledDefinition.test.tsx` over the 32 transcript documents |

## Module defects found

| Finding | Defect | Fixture | Repaired |
| --- | --- | --- | --- |
| A9 | The unbound-projection finding is given for a variable bound only by `VALUES` or by a subquery's projection | `values-bound-projection.rq`, `subquery-bound-projection.rq` | No (Q4 at its default): reproduced by the definition, raised afterwards |
| A10 (module half) | Two literals whose datatype IRIs end alike share an id, `lit:1^int` | `literals-datatypes-end-alike.rq` | No (Q4): reproduced; repairing it changes a stored position's key |
| A11 | The variables' order followed the machine's culture | `scope-paths-by-culture.rq` (the transcript's `cultures` section: invariant, `haw-US`, `th-TH`) | **Yes, by the conversion**: the definition orders by code point, which is the invariant order the transcript records (`SparqlDerivedDrawing.Tests.TheVariableOrder_IsTheInvariantOne_UnderEveryCulture`). The hand-written oracle keeps the defect. |
| B6 | An unreadable query draws a header that says `SELECT` | `broken.rq` | No (Q4): reproduced, and gap 7 |
| found in task 9 | `SparqlRecords` gave spans in characters, not UTF-8 bytes | `SparqlQueryPlugin.Tests` puts 2-, 3- and 4-byte characters before every entry | Yes, in #647; spans counted in characters fail 38 of its 131 tests |
| found in task 10 (library) | An element of shape `none` had nothing to press, so an annotation could not be selected and the refusal of its move was unreachable | `tests.md`, *A defect, found by using the application*; `DiagramCanvas.declared.test.tsx` | Yes, for every module: #649 draws a transparent press target over the bounds of a selectable bodyless element, and this task gives the target its own inline `pointer-events: all`, because the browser re-run found the annotation's `canvas-hint` class switching it off by inheritance. Each jsdom test failed before its half (`expected null not to be null`; `expected '' to be 'all'`), and the stylesheet guard `noUnstyledLibraryClasses.test.ts` failed until the class had its rule. The `tests.md` entry passes in a browser in both themes: each annotation is pressed, refused with its sentence, and both files are unchanged. |

## The open questions, and the changes of Q3 left for afterwards

Q1, Q2, Q4, Q5 and Q6 stand at their defaults. Of Q3's three changes:

- **A8, the unbound projection at line 1 on no element**: left as today; raised afterwards.
- **B11, a finding for a stale position**: left as today; a position naming no element stays in the registration and is reported by nothing (`SparqlSession.Tests.AStoredPositionNamingNoElement_IsLeftInTheRegistration_AndGivesNoFinding`); raised afterwards.
- **A13, the Toolbox panel's text**: changed in task 10, not left. The canvas hands the backend's empty palette through `useToolboxItems`, so the panel says "This diagram type offers no toolbox elements."; #643 states it as a behaviour change. Finding A13's premise was stale: develop's panel already listed what a backend derives when the canvas was compiled. The companion's section 3 said otherwise until task 11 corrected it.

Task 10 also states four drawing differences against its oracle, each pinned in `sparqlCompiledDefinition.test.tsx`: a region is a box on the background layer beneath the connections, a region with an empty label draws none, the modifiers are one row (gap 10), the banner is a `role=status` region, and an annotation can be dragged so that its move reaches the backend and is refused with the definition's sentence.

## The *read* findings, and whether each occurred

Every *read* finding a document can show has its fixture from task 1, and each occurred: A9 (two fixtures), A10 (two), A11, a self reference, B10 with its `.adp`, and the query over the 500 bound. **No *read* finding was struck.** The other *read* findings (A1 to A4, A6 to A8, A12, A14, B1 to B4, B7 to B10) are statements about the code and the languages, each confirmed by the task that acted on it.

Two findings that were not *read* turned out stale, and are put to the Scrum master as small amendments rather than edited here:

- **A13** (*recorded*): the panel did not keep its no-diagram text once derived items were listed; see above.
- **A5** (*measured* at `9a646009`): by task 4, `DislModelBuilder` already computed derived node types (L1) and the `key` half of L2, and by task 5 a constraint's `location` and `subject` (L4), `limits.budgets` with `budget()` (L5) and half of L3 were evaluated. Tasks 4 and 5 built only what was still missing.

## Changes to fixtures and task-1 tests since task 1 landed

Listed with `git log a151578627..HEAD` and `git diff a151578627 HEAD` over the module test project's `Fixtures/`, `Parity/sparql.transcript.json`, `Parity/SparqlTranscript.Tests.cs`, `Parity/SparqlTranscript.cs` and `Parity/SparqlCorpus.cs`. **No fixture and no byte of the transcript changed**, and no check was weakened or skipped.

| Commit | Task | File | Change | Why it is not a gap closed by changing the evidence |
| --- | --- | --- | --- | --- |
| `6fa70d1f0` | 7 | `Parity/SparqlTranscript.cs` | The `cultures` section calls `HandWrittenSparql.Project` where it called `SparqlProjection.Project`, which left the product code under that name; two comments say so. | The same code under its oracle name, recording what finding A11 found in it; the transcript is byte-identical. |
| `e78e880c6` | 8 | `Parity/SparqlTranscript.cs` | The fallback row id is `HandWrittenSparql.QueryFormProperty`, the constant having moved with the hand-written provider. | The same constant; the transcript is byte-identical. |

## Requirement 8.1's list, checked against the module

The tokenizer and the parser behind the plugin (`SparqlTokenizer`, `SparqlParser`, wrapped by `SparqlQueryPlugin`), the scope-grid layout (`SparqlLayout`), the element mapper with its viewport filter (`SparqlElementMapper.Visible`), the session (`SparqlSession`), the store on the shared read-only lifecycle (`SparqlDocumentStore` over `DocumentLifecycle`), the selection resolver (`SparqlSelection`, `SparqlContextSourceResolver`) and the `.proto` payloads (`api/sparql.proto`) are all there. **The reloader with its `BodyDeleted` override** is there in the shape spec 025 gave it on 2026-10-14 (`3aeca29a9`), after this specification's requirements were measured and before its task 1: the module's `SparqlDocumentReloader` class became the shared reloader (`AddDiagramDocumentReloader`), and its override is the store's `BodyDeletedAsync`, which clears the entry rather than reloading it (`SparqlDocumentReloader.Tests.ADeletedQuery_OpensAsTheMissingOneDoes_NamingTheTextEditor`). The module's former `SparqlDocumentFactory` and `SparqlSessionFactory` went the same way, into `AddEmptyDocument` and `AddDiagramSession`. Every class left is in the companion's section 5 with its reason; none had neither, so none was removed.

## No library change names the module (Requirement 9.4)

Searched: the lines each of this specification's library pull requests added (`git diff -U0 <merge>^1 <merge>` for #620, #623, #630, #638, #640 and #643) under `src/backend/EtAlii.Adp.Specification.Disl`, `…Disl.Tests`, `src/backend/EtAlii.Adp.Specification.Fbl`, `…Fbl.Tests` and `src/client/src/canvas/library/`, for `sparql`, `.rq` and `query diagram`, case-insensitive. #625 and #647 change no library file, and #649 and this task's own library change name no module.

- **Controls, so that an empty result means something:** the same pipeline over the module's folder finds 354 lines naming `sparql`; over the library paths it finds 47 added lines with `owner` (task 4's construct) and 7 with `ChromeBand` (task 10's).
- **Result before this task: two lines**, both comments in tests #643 added: `compileNotation.chromeBand.test.ts` ("The SPARQL query diagram was the first to state both") and `compileNotation.textMarks.test.ts` ("The SPARQL query diagram was the first to state all three"). **Both are reworded in this task** to say what the construct is for, with no module named. After it the search over the library at this branch's head finds no added line naming the module.

## Follow-ups, recorded and not built

1. **The cost of evaluating the definition.** After #638, `over-the-bound.rq` opens in about 73 ms (Release) against about 2.3 ms for the hand-written projection, some thirty times as long; the growth from 1,000 to 2,000 generated patterns is 2.2 times, so the quadratic term is gone. What remains is the CEL evaluator's constant cost, spread over every expression with no single hot spot. Recorded in the companion's section 6.
2. **A move of an id the model does not hold is not refused by its prefix.** The hand-written session refused any `anon:`, `edge:` or `note:` id by its prefix; the derived refusal asks the element, so an `anon:` id no element has is not refused and its position is stored and left (Requirement 7.3). Only a client holding an earlier reading's id can send one; the transcript's unknown id has no prefix. Recorded in the companion's section 6.
3. **An annotation's place is the client's.** The definition states no placement for an `Annotation` and the scope grid places none, so `SparqlCanvas.tsx` keeps `annotationAnchor`. Recorded in the companion's section 6.
4. **A binding's `args` (B12, Requirement 9.3)**: none of the 57 `.fbl` bindings in etalii.adp or standalone declares `args` (task 5's search), so it is not built and `EtAlii.Adp.Specification.Fbl` is unchanged.
5. **The names the tasks gave.** Tasks 7 and 9 named the store's entry `_Model/SparqlDocumentEntry.cs`; it is `_Model/SparqlDocument.cs`, as #630 noted.
6. **The local inspector can be blind.** In task 7 the local `inspect.sh` reported nothing because a compiler error in an unrelated test file stopped it, and CI's `inspection` job was the check. A run is trusted only with a fresh `INSPECT_CACHES_HOME` and a non-empty file count in its output.

## The second coverage diff

Every requirement reference was extracted from `tasks.md`, every acceptance criterion listed from `requirements.md`, and the two sets diffed: **53 criteria, 53 claimed, none unclaimed, none claimed that does not exist.** Each criterion was then traced to a file and a string on this branch, and a script checked that each string occurs in its file: all 77 strings of the 53 criteria were found. A first run, before this report and the bundled companion existed, missed the 10 strings that live in them, which is the check failing where it should.

| Criterion | Traced to |
| --- | --- |
| 1.1 | `SparqlTranscript.Tests.cs` `TheModule_ReproducesTheCheckedInTranscript_ByteForByte`; `SparqlTranscript.cs` `the five refused moves; a refused re-parenting; a refused property` |
| 1.2 | `values-bound-projection.rq` `VALUES`; `scope-paths-by-culture.rq` `SELECT`; `second-optional-positioned.adp` `layout` |
| 1.3 | `SparqlTranscript.Tests.cs` `ADP_WRITE_PARITY_TRANSCRIPTS` |
| 1.4 | `SparqlTranscript.Tests.cs` `EveryScriptedStep_LeavesTheQueryByteIdentical` |
| 1.5 | `SparqlTranscript.Tests.cs` `the diff of the` |
| 2.1 | `definition/sparql-query.dis` `"disl": "0.3"` |
| 2.2 | `definition/sparql-query.dis` `"x-sparql"`; `definition/sparql-query.dis` `"ephemeral": true` |
| 2.3 | `definition/sparql-query.md` `## 4. Language gaps` |
| 2.4 | `definition/sparql-query.provenance.json` `sha256`; `BundledDefinition.Tests.cs` `TheEmbeddedDefinition_IsTheBundledFile` |
| 2.5 | `BundledDefinition.Tests.cs` `TheBundledDefinition_LoadsWithoutDiagnostics` |
| 3.1 | `sparql.fbl` `"readOnly"`; `SparqlBinding.Tests.cs` `AFirstPlacement_CreatesNoRegistration`; `etalii.adp specifications/fbl/w3c-sparql-query.fbl` `net.etalii.adp.w3c.sparqlQuery` |
| 3.2 | `SparqlQueryPlugin.cs` `SparqlParser`; `SparqlParser.Tests.cs` `ASparqlUpdateDocument_IsRefusedNamingSparqlUpdate` |
| 3.3 | `SparqlQueryPlugin.Tests.cs` `EverySpan_SlicedFromTheBytes_IsTheEntrysText_WithWideCharactersBeforeIt` |
| 3.4 | `SparqlQueryPlugin.Tests.cs` `AnUnparseableBody_IsUnreadable_WithOneUnparseableFinding_AtTheLineAndColumnTheParserStopped`; `SparqlQueryPlugin.Tests.cs` `ReadingAnyPrefixOfADocument_NeverThrows` |
| 3.5 | `SparqlSession.Tests.cs` `AnExternalEdit_ReachesTheOpenSessionAsDeltas`; `SparqlSession.Tests.cs` `AQueryThatDoesNotParse_OpensWithOnlyItsHeader` |
| 3.6 | `ServiceCollection.AddSparql.cs` `SparqlBody.Binding.Template!.Text`; `SparqlBinding.Tests.cs` `TheTemplate_IsTheTextANewQueryIsCreatedWith` |
| 4.1 | `SparqlDefinition.cs` `DislModelBuilder.From(reading, Specification, Env)` |
| 4.2 | `SparqlDerivedDrawing.Tests.cs` `TheDerivedDrawing_IsTheHandWrittenOne`; `SparqlSession.Tests.cs` `AStoredPosition_OverlaysTheComputedOne` |
| 4.3 | `HandWrittenSparql.cs` `class HandWrittenSparql`; `definition/sparql-query.dis` `shallowestScope` |
| 4.4 | `SparqlDerivedDrawing.Tests.cs` `TheCutAt500_KeepsTheHandWrittenElements_AndReportsItsTwoNumbers` |
| 4.5 | `SparqlTranscript.Tests.cs` `TheModule_ReproducesTheCheckedInTranscript_ByteForByte` |
| 5.1 | `SparqlDerivedRows.Tests.cs` `EveryRow_IsTheHandWrittenProvidersRow` |
| 5.2 | `SparqlDerivedRefusals.Tests.cs` `TheDerivedToolbox_IsEmpty`; `SparqlDerivedRefusals.Tests.cs` `NoElement_IsOfferedAnAction` |
| 5.3 | `SparqlDerivedFindings.Tests.cs` `EveryFinding_IsTheHandWrittenValidators` |
| 5.4 | `SparqlDerivedRefusals.Tests.cs` `EveryMoveRefusal_IsTheHandWrittenSessions`; `SparqlSession.Tests.cs` `ABareUnregisteredQuery_RefusesNamingTheMissingRegistration` |
| 5.5 | `SparqlDerivedFindings.Tests.cs` `expected.SequenceEqual(actual, StringComparer.Ordinal)`; `SparqlDerivedRows.Tests.cs` `AnElementTheBudgetCut_HasNoRows_AsTheHandWrittenProviderGaveNone` |
| 5.6 | `HandWrittenSparql.cs` `NoActionsReason` |
| 6.1 | `SparqlQueryPlugin.cs` `PluginPlanResult.Refused`; `sparql.fbl` `"readOnly"` |
| 6.2 | `NoWriterSurface.Tests.cs` `NoCodeInTheModule_PlansASplice_WritesABody_OrHoldsAWriter` |
| 6.3 | `SparqlBinding.Tests.cs` `TheBinding_IsReadOnly_WithTheSentenceTheDiagramShows` |
| 6.4 | `NoWriterSweep.Tests.cs` `EverySurfaceThisModuleOffers_LeavesTheQueryByteIdentical` |
| 6.5 | `NoWriterSurface.Tests.cs` `TheModuleReferencesNoHttpClient` |
| 7.1 | `SparqlLayout.Tests.cs` `AnAuthoredRegionAnchor_MovesTheFrameWithItsContentsFollowing`; `second-optional-positioned.adp` `region:` |
| 7.2 | `SparqlSession.Tests.cs` `Repositioning_IsOneUndoAway_AndUndoAlsoLeavesTheQueryAlone` |
| 7.3 | `SparqlSession.Tests.cs` `AStoredPositionNamingNoElement_IsLeftInTheRegistration_AndGivesNoFinding` |
| 8.1 | `definition/sparql-query.md` `## 5. What stays in the standalone module, and why`; `SparqlDocumentStore.cs` `BodyDeletedAsync` |
| 8.2 | `SparqlValidator.cs` `SparqlDefinition`; `HandWrittenSparql.cs` `Project(` |
| 8.3 | `definition/sparql-query.md` `No class is kept for neither, so none was removed.` |
| 9.1 | `DerivedRelationOwner.Tests.cs` `ADerivedRelationsOwner_IsTheOneItStates_NullBeingTheDiagram`; `DerivedNodeTypes.Tests.cs` `class` |
| 9.2 | `EphemeralIds.Tests.cs` `class`; `GestureRefusals.Tests.cs` `class` |
| 9.3 | `.spec-workflow/specs/sparql-disl-fbl/delivery-report.md` `` none of the 57 `.fbl` bindings `` |
| 9.4 | `.spec-workflow/specs/sparql-disl-fbl/delivery-report.md` `## No library change names the module` |
| 10.1 | `client/SparqlCanvas.tsx` `compileNotation(parseDisl(disText), SPARQL_BINDINGS)`; `client/sparqlCompiledDefinition.test.tsx` `oracle` |
| 10.2 | `compileNotation.chromeBand.test.ts` `canvas.header` |
| 10.3 | `tests.md` `the projection mark` |
| 11.1 | `definition/sparql-query.md` `\| 10 \| **A chrome band row repeated over a list**` |
| 11.2 | `.spec-workflow/specs/sparql-disl-fbl/delivery-report.md` `## Changes to fixtures and task-1 tests since task 1 landed` |
| 11.3 | `.spec-workflow/specs/sparql-disl-fbl/delivery-report.md` `## Language gaps, as candidate changes for etalii-adp/etalii.adp` |
| 11.4 | `.spec-workflow/specs/sparql-disl-fbl/delivery-report.md` `**No *read* finding was struck.**` |
| 12.1 | `src/diagrams/readme.md` `` `sparql/definition/` holds the one definition `sparql-query` ``; `ServiceCollection.AddSparql.cs` `What stops a write is the body's` |
| 12.2 | `docs/tools.md` `sparql-disl-fbl/requirements.md` |
| 12.3 | `.spec-workflow/specs/sparql-disl-fbl/delivery-report.md` `## The gates` |
| 12.4 | `.spec-workflow/specs/sparql-disl-fbl/delivery-report.md` `## The second coverage diff` |

**Two traces read back to their artefacts.** The question for each was whether the evidence is that criterion's, not whether it exists.

- **5.5.** The first trace named `SparqlDerivedFindings.Tests.TheDocuments_ReachEveryCode`. Read back, it asserts only that the corpus and the extra texts reach the hand-written validator's three codes: it guards the evidence, so that the comparisons beside it are not vacuous, and is not the evidence. **The trace was corrected to `AssertSame` in the same file**, whose `expected.SequenceEqual(actual, StringComparer.Ordinal)` compares the derived findings with the hand-written ones line for line, severity, code, line and sentence, over every document and every extra text, so an extra finding fails it. The rows half is `SparqlDerivedRows.Tests` (`EveryRow_IsTheHandWrittenProvidersRow`, and a cut element has none) and the refusals half `SparqlDerivedRefusals.Tests.EveryMoveRefusal_IsTheHandWrittenSessions`. **Read back, 5.5 does not hold for one thing it names, the panel text**: the Toolbox panel's sentence changed in task 10 (see *The open questions*), a stated behaviour change against Q3's default; the trace does not cover it and none is claimed.
- **7.3.** `SparqlSession.Tests.AStoredPositionNamingNoElement_IsLeftInTheRegistration_AndGivesNoFinding` appends `var:x: 640 480` and `var:nobody: 10 20` to a registration of `groups.rq`, opens the session, validates, and moves `var:x`. It asserts that nothing is drawn for `var:nobody`, that `var:x` is at 640, that opening rewrote nothing, that the validator reports nothing, and that after the move the registration holds both `var:x: 320 200` and `var:nobody: 10 20`. That is 7.3's "left in the registration and unreported", both halves; #647 records it failing when perturbed to name a real variable. The evidence is 7.3's and not 7.2's: the undo of a move is `Repositioning_IsOneUndoAway_AndUndoAlsoLeavesTheQueryAlone`.

## The gates

This task's pull request, in a newly created worktree (`.claude/worktrees/spq11`, from `develop` `786e8447e`, built from nothing before any gate was trusted), each exit code captured on its own:

- `npm test`: 0 (3,070 tests); `npm run typecheck`: 0; `dotnet format style --verify-no-changes --severity info`: 0; `dotnet test --solution EtAlii.Adp.slnx`: 0 (16,016 total: 15,955 passed, 61 skipped, 0 failed).
- After merging `develop` at `13e127b07` (#649) and adding the inline `pointer-events`, a client-only change: `npm test` 0 (3,073 tests) and `npm run typecheck` 0.
- `inspect.sh` with a fresh `INSPECT_CACHES_HOME`: clean, 2,906 C# files, no compiler errors.

Every earlier pull request of this specification records its own gates in its description and its implementation log.

## The pages

- **Updated in this task:**
  - The bundled companion and its provenance, from etalii.adp #490's merge commit `c3fc0dc2f`: two more gaps, the class table (section 5), the standalone host's differences (section 6) and the Toolbox bullet.
  - `docs/tools.md`: the `w3c/sparql` row names this specification and links the companion (Requirement 12.2).
  - `SparqlSession.cs`'s remark, which said the round trip "holds by construction rather than by care"; it now names the two stops of a write, as task 9 reworded `Diagram.cs`, `ServiceCollection.AddSparql.cs` and the store (Requirement 12.1).
  - The two library test comments that named the module (*No library change names the module*).
  - The library's bodyless press target states its own `pointer-events`, and `tests.md`'s annotation entry records the browser re-run and is closed.
- **Checked, and no sentence had become false:** `src/diagrams/readme.md` (task 6 named `sparql/definition/` there), `docs/creating-a-diagram-module.md` (its one mention, sparql's elements at a default position, still holds: annotations are sent with none), `docs/architecture.md` and `docs/solution-structure.md` (no project was added or removed; neither names the module), `src/diagrams/sparql/client/readme.md` (the module still offers no context action).
