# Delivery report: functional-decomposition-graph-disl-fbl

The delivery report that task 12 asks for. It closes the conversion of the functional decomposition graph (`etalii/functional-decomposition-graph`, `.fdg`) to DISL and FBL. It covers four things that Requirement 9.3 and task 12 name: every language gap as a candidate change for etalii-adp/etalii.adp, every definition defect repaired, every module defect with its guard, and every *read* finding that did not occur, with its fixture. It also records the second coverage diff and the questions still open. Written 2026-10-22, against standalone `develop` `454fc5094` and etalii.adp `develop` `d44959ea7`.

## The pull requests

| Task | Repository | Pull request | Merged as |
| --- | --- | --- | --- |
| 1. Freeze the parity transcript | standalone | #567 | `4af3665fe` |
| 2. The definition at DISL 0.3 | etalii.adp | #407 | `cc1ef27b5` |
| 3. Library: the cardinality rule, the cycle rule | standalone | #569 | `047cec664` |
| 4. Library: a connect refused by the built-ins, a fixed attribute | standalone | #571 | `61dd5e80c` |
| 5. Bundle the definition and hold it | standalone | #574 | `59c3ef0b3` |
| 6. Derive the toolbox, the menus, the rows, the confirmation | standalone | #578 | `bd7f3651e` |
| 7. Derive the findings and the connect refusals | etalii.adp, then standalone | #412, #579 | `1ea259d1c`, `c24791691` |
| 8. Compile the client's canvas definition | etalii.adp, then standalone | #414, #580 | `dad00e55a`, `24058669c` |
| 9. Read the body through the binding | standalone | #591 | `afb2c22e0` |
| 10. Write every edit by splices | standalone | #599 | `823efed6a` |
| 11. Guard the writer's repaired defects | standalone | #603 | `454fc5094` |
| 12. The companion's closing text | etalii.adp | #449 | `d44959ea7` |
| 12. The bundled companion, the pages and this report | standalone | the pull request that carries this file | open, after #449 |

FBL's insert `constant`, which tasks 9 and 10 use, was specified as etalii.adp spec 033 (#420, `75599e9a8`) and built into the standalone library by #588 (`7a34e82e0`), outside this specification.

## Language gaps, as candidate changes for etalii-adp/etalii.adp

Each is recorded with its construct, its evidence and what would resolve it in the companion, `definitions/diagrams/functional-decomposition-graph.md`, section *Language gaps* (bundled into `src/diagrams/functional-decomposition-graph/definition/`). The fixtures are in `src/diagrams/functional-decomposition-graph/backend/EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests/Fixtures/`.

| # | Candidate change | Construct concerned | Shown by | Held meanwhile by |
| --- | --- | --- | --- | --- |
| 1 | None: answered by FBL 0.5 (4.1.5, 5.2, `read`) | FBL: a slot that reads a scalar as written (B1) | `scalars-as-written.fdg`; `FdgBody.Tests.AValue_ReadsAsWritten` | Nothing |
| 2 | **FBL: a rule whose `type` is read from a key through a map.** The writing half is answered by FBL 0.8's insert `constant` (spec 033). | FBL 5.1, 6.2: a rule's `type` is fixed, so a value-typed entry needs one rule per value (B3) | `unknown-types.fdg`, `malformed-entries.fdg`; the binding's fifteen rules | The concrete types `UnknownElement` and `UnknownConnection` in the definition |
| 3 | **FBL: the indentation indicator of a literal block scalar** in 6.3, whenever its first line begins with a space; and whether a one-line value that becomes several lines changes style. The case also breaks FBL 0.5's own **MUST** that a value written for a text slot reads back as the same text. | FBL 6.3 (B13) | `comment-text-forms.fdg` (`t3`); `FdgWrittenAsTyped.Tests.ABlockText_BeginningWithASpace_IsGap3_AndCannotBeRead` | Nothing: reported, not worked around |
| 4 | **FBL: a splice operation that opens an empty flow collection** into a block one, keeping what follows it on its line | FBL 4.3 and the splice catalogue (B14) | `empty-list-comment.fdg`; `FdgFirstEntry.Tests` | Nothing: the library does it already |
| 5 | **FBL: a `remove` option that leaves an entry's leading comments** | FBL 4.x, an entry's line span (B15, Q2) | `comment-above-element.fdg`; `FdgDocument.Tests.RemovingTheFirstElement_KeepsTheCommentAboveIt` | **`FdgCommentKeeper`** |
| 6 | **FBL: a binding's own sentences** for its reader's findings and its planner's refusals, keyed by their reasons | FBL 7.4, 7.5, the planner (B2, B6, B7, B8, B16) | `not-yaml.fdg`, `key-twice.fdg`, `no-connections-section.fdg`, `unread-keys.fdg`, `header-missing.fdg`, `header-version.fdg`; `FdgDocument.Tests.AConnectWithNoConnectionsSection_IsRefusedInTheToolsSentence` | **`FdgReaderSentences`** |
| 7 | **DISL: which relation a gesture creates when several relation types claim it**: the one whose endpoint constraints admit the pair, else a choice | DISL 7.x, 9.x, `connect.pointer` (A9) | client `fdgConnect.test.tsx`; the transcript's connect cases | The canvas library's `relationOnto`, for every module |
| 8 | **DISL: an `order` per code, or a `by` of `document`, `relation` or `line` on `constraints.order`** | DISL 8.6 (A13, Amendment 1) | `finding-order.fdg`, `link-order.fdg`; `FdgDerivedFindings.Tests` | **`FdgFindingOrder`** |
| 9 | None: answered by DISL 0.2's `outOfRange` (4.3). The standalone runtime's form commit does not yet implement `outOfRange.typed`. | DISL 4.3 (A6) | `FdgDocument.Tests.AResizeBelowTheMinimum_IsClamped` | **`FdgSizeClamp`**, for the runtime |
| 10 | **FBL: a number slot whose accepted scalars and fallback the binding states**, so a value that does not convert is kept at a default and reported rather than made unreadable | FBL 7.4 (B2) | `number-forms.fdg` | **`FdgReading`** converts, **`FdgReaderSentences`** reports |
| 11 | **DISL: `sourceId` and `targetId` on a relation in the CEL context**, holding the ids as written | DISL 12.x, 8.7 (A11, design) | `cardinality-unknown-end.fdg`, `link-chain.fdg` | **`FdgLinkChain`** |
| 12 | **DISL: `detail.cycle` for `std.acyclic` findings, and a stated location for `std.acyclic` and `std.multiplicity` findings** | DISL 8.7 (task 2) | `ownership-loop-rotation.fdg`, `ownership-loops-sharing-a-member.fdg`, `rule-second-parent.fdg` | The standalone runtime's choice (task 3) |
| 13 | **FBL: a sentence in 6.3 on whether the gap after a sibling's dash is copied** into a new entry | FBL 6.3, *Indentation* (B17) | `mixed-indentation.fdg` | Nothing: the tool now does what FBL says, named in the transcript (task 10) |
| 14 | **FBL: a sentence in 6.3 that a number which rounds to zero is written `0`** | FBL 6.3, *Numbers* (B19) | `number-rounding.fdg` | Nothing: recorded, not repaired |

## Definition defects repaired

| Finding | Defect | Repaired in | Held by |
| --- | --- | --- | --- |
| A1 | The definition was DISL 0.1 | etalii.adp #407, at DISL 0.3; bundled by #574 | `BundledDefinition.Tests.TheBundledDefinition_LoadsWithoutErrors` |
| A2 | Eleven constructs worked around (drop tools, superellipse, conditional confirmation, `fixed`, `acyclic` on an abstract type, `pointer`, built-in `refusal`, `code`, `oncePerGroup`, `constraints.order`, base-36 ids) | #407 | `Parity/FdgDerivedMenus.Tests`, `FdgDerivedFindings.Tests`, `FdgDerivedRefusals.Tests` |
| A3 | Two descriptions of the tool disagreed | #407: `specifications/disl/functional-decomposition.dis` brought in line, its remaining differences stated in its `doc.description` | etalii.adp's `validate-examples.py` |
| A4 | A "Links" group of five drag tools the tool does not have | #407 | `FdgDerivedMenus.Tests.ThePalette_IsTheHandWrittenOne` |
| A5 | The placement and connect menus were missing; relation labels differed | #407 | `FdgDerivedMenus.Tests.EveryMenu_IsTheHandWrittenOne` |
| A6 | The Width and Height rows were missing | #407 | `FdgDerivedRows.Tests.EveryRow_IsTheHandWrittenOne`; `FdgProviders.Tests.ASizeWithThreeDecimals_IsShownAsTheFileHoldsIt` |
| A7 | `persistence` named a plugin; `view.store` said `bounds` | #407: `format: "fbl"`, `binding`, `typeMap`, `view.store` empty | `BundledDefinition.Tests`; the body read through the binding (task 9) |
| A12 | The 0.1 definition reported a loop once per member | #407: one finding per loop | `FdgValidator.Tests.EveryElementaryOwnershipLoop_IsReported` |
| A14 | `uniqueName` called `lists.range`, which DISL does not define, and counted named elements only | #407: built from `positionIn`, counting every `name` | `names-counted.fdg` in the transcript |
| B4, B5, B7 | No pattern for a dangling end, an id written twice, and the header | The module's binding `functional-decomposition-graph.fbl` (#591): a connection as an element with reference attributes, `storedId`, the header as an element | `rule-dangling-reference.fdg`, `rule-duplicate-id.fdg`, `header-missing.fdg` in the transcript; `Parity/FdgCrossCheck.Tests` |
| (task 7) | `allowSelfLoops: false` was stated on the abstract `FdgRelation` only, which the runtime does not inherit | etalii.adp #412 | `rule-self-link.fdg`; `FdgDerivedRefusals.Tests` |
| (task 8) | The Comment text was not stated as the tool draws it | etalii.adp #414 | client `fdgCompiledDefinition.test.tsx` |

## Module defects and their guards

| Finding | Defect | Repaired by | Guard, seen to fail against the code before its repair |
| --- | --- | --- | --- |
| A15 | Two sentences formatted a number with the machine's culture ("12,5") | #579 | `FdgCultureFreeSentences.Tests.ASizeBelowItsMinimum_IsWrittenWithAPoint_UnderACommaCulture` (failed with "12,5" under nl-NL) |
| B8 | A body that is not YAML opened editable, so an add spliced into text the parser could not read | #591 (Q3: empty and read-only) | `FdgProviders.Tests.ABodyThatIsNotYaml_IsRefusedUpFront_AndWritesNothing`; `FdgCommands.Tests.ADocumentThatCouldNotBeRead_IsNeverWritten`; `not-yaml.fdg` in the transcript |
| B12 | Struck: does not occur (see below) | None needed | None: a guard passed against the code it would guard and was deleted (#603) |
| B13 | Every Comment text was rewritten as a literal block, and a leading space was lost | #599 (written through FBL) | `FdgWrittenAsTyped.Tests.AText_ReadsBackAsTyped_InItsOwnStyle`: 3 cases failed against `b8b878a8e` (#603). The literal-block case is gap 3. |
| B14 | The first add into `elements: [] # none yet` left the file unreadable | #599 | `FdgFirstEntry.Tests`: the 2 trailing-comment cases failed against `b8b878a8e`; the 2 template cases, which hold criterion 5.4, failed with the library's empty-flow opening switched off (#603) |

Two library defects that fixtures showed were repaired in the FBL library, each with a test seen to fail first and no module name (L7, #599): `YamlScalars` took `e1`, `E5` and `.e2` for numbers (3 cases of `YamlScalars.Tests` failed before), and `NewText.Number` rounded the binary double, so `200.00005` became `200` (3 of 9 cases of `Planning/NewTextNumbers.Tests` failed before).

## Read findings that did not occur

| Finding | What did not occur | Fixture | Ruling |
| --- | --- | --- | --- |
| B12 | `[draft]`, `{a}` and `*x` were already written double-quoted and read back unchanged, since `LineSplice.Quote` was fixed in `62fabd1eb`; a guard of them passed against the commit before task 10 | `names-to-quote.fdg` | Struck, chat 2026-10-21 |
| A6, its second sentence | The row shows four decimals, as the file holds them, not two | `number-rounding.fdg` (`140.1235`) | Struck, chat 2026-10-21 |
| B8, in part | The body did not open editable: every edit was already refused | `not-yaml.fdg` | Reworded, chat 2026-10-21 |

Every other *read* finding of tables A and B showed in the transcript of task 1.

## The second coverage diff

**56 acceptance criteria in the requirements; 56 claimed by the tasks; none unclaimed, nothing claimed that is not a criterion.** Each is traced below to the tests, files and strings the work produced, not to a task's promise.

| Criterion | Task | Traced to |
| --- | --- | --- |
| 1.1 | 1 | `Parity/functional-decomposition-graph.transcript.json`, generated by `Parity/FdgTranscript.cs`: per document `menus`, `rows`, `findings`, `payloads`, `edits` |
| 1.2 | 1 | The example, its shipped copy, 14 fixtures and 27 new ones under `Fixtures/` (`names-to-quote.fdg` to `width-fraction.fdg`) |
| 1.3 | 1 | `Parity/FdgTranscript.Tests.TodaysCode_ReproducesTheCheckedInTranscript_ByteForByte`, seen to fail on "Remove link" |
| 1.4 | 10 | The transcript's diffs in #579 (the comma-culture probe), #591 (`key-twice.fdg`) and #599 (edit hunks, eight named kinds, and three read-backs) |
| 2.1 | 2 | etalii.adp `definitions/diagrams/functional-decomposition-graph.dis` at `"disl": "0.3"` (#407); `validate-examples.py` |
| 2.2 | 2 | The same file: drop tools, no Links group, the five "Add … here" entries, the five connect entries, the `width` and `height` items, `uniqueName` |
| 2.3 | 2 | `persistence.format: "fbl"`, `binding`, `typeMap`, `view.store: []`, and `x-fdg` in the same file |
| 2.4 | 2 | `definitions/diagrams/functional-decomposition-graph.md`; `specifications/disl/functional-decomposition.dis`'s `doc.description` |
| 2.5 | 5 | `src/diagrams/functional-decomposition-graph/definition/` with `provenance.json`; `BundledDefinition.Tests.TheEmbeddedDefinition_IsTheBundledFile` |
| 2.6 | 5 | `BundledDefinition.Tests.TheBundledDefinition_LoadsWithoutErrors` |
| 3.1 | 6 | `FdgDefinition.Menus`, `.Rows`, `.Confirmation`, `.ConnectRefusal`; `Parity/FdgDerivedMenus.Tests`, `FdgDerivedRows.Tests`, `FdgDerivedRefusals.Tests` |
| 3.2 | 7 | The sentences in the transcript's menus and refusals; `FdgDerivedMenus.Tests.EveryConfirmation_IsTheHandWrittenOne`; `FdgCultureFreeSentences.Tests` |
| 3.3 | 7 | `Parity/FdgDerivedFindings.Tests.EveryFinding_IsTheHandWrittenOne` (code, severity, sentence, line, order); `FdgValidator.Tests.TheSeverity_FollowsTheRule` |
| 3.4 | 7 | `FdgLinkChain`; `link-chain.fdg`; `FdgValidator.Tests.EveryElementaryOwnershipLoop_IsReported` for the amended fourth loop |
| 3.5 | 7 | `FdgRules.Tests.AnOwnershipLoop_IsReportedOnceWithItsMembers`, `AShowsLoop_IsNotACycle`; `ownership-loop-rotation.fdg` |
| 3.6 | 7 | `FdgCommands.Tests.ACycleClosingLink_IsRefusedByTheBackend_AndNothingIsWritten`; `GestureConstraintEvaluator.Connect` |
| 3.7 | 7 | The transcript test, unchanged in menus, rows, findings and payloads through #578, #579 and #599 |
| 3.8 | 7 | `Parity/HandWrittenFdg.cs`; `FdgRuleSet`, `FdgRelations`, `FdgOwnership`, `FdgConnectVerdict` under `Parity/` |
| 4.1 | 9 | `backend/EtAlii.Adp.Diagram.FunctionalDecompositionGraph/functional-decomposition-graph.fbl`, an `EmbeddedResource` of the project |
| 4.2 | 9 | `FdgBody.Tests.AValue_ReadsAsWritten`; `scalars-as-written.fdg` |
| 4.3 | 9 | `FdgReading`; `number-forms.fdg` in the transcript: its payloads and its four findings "`x: …` is not a number; zero was used." |
| 4.4 | 9 | `unknown-types.fdg` and `malformed-entries.fdg` in the transcript |
| 4.5 | 9 | `rule-dangling-reference.fdg`, `rule-duplicate-id.fdg`, `key-twice.fdg`, `no-id.fdg`, `header-missing.fdg` in the transcript; `FdgDerivedFindings.Tests` |
| 4.6 | 9 | `FdgBody.Tests.Reading_NeverThrowsOnContent`; `FdgProviders.Tests.ABodyThatIsNotYaml_IsRefusedUpFront_AndWritesNothing` |
| 4.7 | 9 | `FdgReaderSentences`; the companion's gap 6 |
| 4.8 | 9 | `Parity/FdgCrossCheck.Tests.TheBindingReadsTheIdsTheParserReads`; `RealFiles/divergences.json` unchanged |
| 5.1 | 10 | The nine handlers on `FdgDefinition.Apply`; a search of the product project finds no `LineSplice`, `YamlDotNet`, `FdgParser` or `FdgWriter` |
| 5.2 | 10 | `FdgDocument.Tests.AnEdit_RewritesOnlyItsOwnEntrysLines`; `trailing-comments.fdg` in the transcript |
| 5.3 | 10 | `key-placement.fdg` and `mixed-indentation.fdg` in the transcript |
| 5.4 | 11 | `FdgFirstEntry.Tests.TheFirstElementAndConnection_AreAdded`, `TheFirstConnection_IsAdded_ToAListWithATrailingComment` |
| 5.5 | 10 | `FdgCommands.Tests.DeletingAnElementWithTwoConnections_RestoresAllThreeOnUndo`; `FdgDocument.Tests.RemovingTheFirstElement_KeepsTheCommentAboveIt` |
| 5.6 | 10 | `FdgCommands.Tests.EveryRefusal_WritesNothing_AndSaysWhy`; `FdgDocument.Tests.AConnectWithNoConnectionsSection_IsRefusedInTheToolsSentence` |
| 5.7 | 10 | `FdgProviders.Tests.ADrop_AddsItsTypeCentredWhereItLanded`; `FdgDocument.Tests.AResizeBelowTheMinimum_IsClamped`; `names-counted.fdg` |
| 5.8 | 11 | `FdgWrittenAsTyped.Tests.AText_ReadsBackAsTyped_InItsOwnStyle` |
| 5.9 | 10 | `FdgRoundTrip.Tests.EveryEditUndone_GivesTheBytesBack`; `FdgCommands.Tests.EveryEdit_ThenItsUndo_GivesTheOriginalBytes` |
| 5.10 | 10 | `FdgRoundTrip.Tests.ADocumentSavedUnchanged_IsWrittenAsItWasRead`, with `crlf-line-endings.fdg`, `lf-line-endings.fdg` and `no-trailing-newline.fdg` |
| 6.1 | 10 | The product project's classes, each in the companion's table |
| 6.2 | 10 | `FdgParser`, `FdgWriter`, `_Model/` and the four rule classes under the test project's `Parity/`; none in the product project |
| 6.3 | 12 | The companion's *What stays code in the standalone module* |
| 7.1 | 8 | `client/FdgCanvas.tsx` (`compileNotation(parseDisl(...))`), `client/fdgBindings.ts` |
| 7.2 | 8 | `client/handWrittenDefinition.ts`; `client/fdgCompiledDefinition.test.tsx` |
| 7.3 | 8 | `src/client/src/canvas/library/disl/compileNotation.ts` and its test (`maxFromSource`, `maxIntoTarget`, `acyclic` over subtypes) |
| 7.4 | 8 | The `tests.md` entry of task 8, run in both themes |
| 7.5 | 8 | `client/readme.md`, *What is not settled yet* |
| 8.1 | 3 | `StdMultiplicity.Tests.cs`, `StdAcyclic.Tests.cs` in `EtAlii.Adp.Specification.Disl.Tests` |
| 8.2 | 4 | `ConnectRefusal.Tests.cs`, `FixedAttribute.Tests.cs` |
| 8.3 | 9 | FBL 0.5 `read` and 0.8 `constant` in the library (#588); `YamlScalars.Tests.cs`, `Planning/NewTextNumbers.Tests.cs` (#599) |
| 8.4 | 3 | A search of `EtAlii.Adp.Specification.Disl` and `EtAlii.Adp.Specification.Fbl` for `Fdg` and `functional` finds nothing |
| 9.1 | 12 | The companion's *Language gaps*: fourteen gaps, each with construct, evidence and resolution |
| 9.2 | 12 | The same section's statement, and a search of the module's tests for a skipped test, which finds none |
| 9.3 | 12 | This report |
| 9.4 | 12 | The struck findings B12 and A6 and the reworded B8 in the requirements, each naming its fixture (chat 2026-10-21) |
| 10.1 | 12 | `src/diagrams/readme.md` (#574, #579), `docs/creating-a-diagram-module.md` (#591, #599), `docs/diagram-module-client-api.md` (#580); `docs/architecture.md` and `docs/solution-structure.md` read, with no sentence made false |
| 10.2 | 12 | `docs/tools.md`'s row for `etalii/functional-decomposition-graph`: ⚗️ Prototype, naming this specification |
| 10.3 | 12 | Every pull request of the table above, its four gates exiting zero by captured codes, and CI green |
| 10.4 | 12 | The coverage table of `tasks.md`; this diff |

**Two traces read back to their artefacts.**

- **4.3**, "a position and a size read as today from every scalar, and a value that does not read keeps its element at zero, reported on its line". Read back to the transcript's entry for `number-forms.fdg`: the payload of `quoted` (`x: "120"`, width 100) is centred at 170, of `exponent` (`x: 1e2`) at 150, and of `hex` (`x: 0x10`) at 50, so the quoted and the exponent forms read as numbers and the hexadecimal one stays at zero; its findings are four warnings `fdg.unreadable-entry`, the first "line 19 | `x: 0x10` is not a number; zero was used.", on the value's own line. Both halves are this criterion's, and no neighbour's (4.2 is the written text of names and ids, which this fixture does not exercise).
- **5.4**, "the first element and the first connection addable to a new document, and to one whose empty list carries a trailing comment". Read back to `FdgFirstEntry.Tests.cs`: `TheFirstElementAndConnection_AreAdded` writes `FdgBody.Template` or `empty-list-comment.fdg` to disk, adds `first` and `second` and connects them through the handlers, and reads each back with `FdgBody`; `TheFirstConnection_IsAdded_ToAListWithATrailingComment` adds the first connection into `connections: [] # none yet`. The evidence is the criterion's own, and not 5.8's, whose test is the other class of #603.

## Open

- **The canvas's "Add … here" menu on empty canvas.** The compiled canvas opens the definition's placement menu on a right-click on empty canvas (`backgroundMenu`), which the hand-written one did not. Task 8 took the option "offer it" while the question was open, and `fdgCompiledDefinition.test.tsx` compares that key apart. The ruling is the user's.
- **Gap 3 and FBL 0.5's MUST.** The standalone FBL library writes a literal block whose first line begins with a space as `|-`, which FBL 6.3 allows and FBL 0.5's read-back rule forbids. Whether the indicator becomes the language's (a candidate change, as recorded) or the library is held to the read-back rule now is for etalii-adp/etalii.adp to say. Meanwhile the case is reported, not worked around.
- **B12 has no guard.** A guard of `[draft]`, `{a}` and `*x` passed against the code before task 10, so it was deleted (#603). The transcript of `names-to-quote.fdg` holds the three names.
- **Findings B17 and B19 are settled** by their fixtures (gaps 13 and 14, and the library repair of the rounding). The requirements still read "To settle by fixture" for both. Whether to note the settlement there is a small amendment, put to the user and not applied here.
