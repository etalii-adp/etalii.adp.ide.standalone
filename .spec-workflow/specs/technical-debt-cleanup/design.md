# Design Document

## Overview

This spec removes duplicated and inconsistent structure across the backend, the client and the diagram modules, without changing what the system does. Requirement 1 makes that a gate: every piece lands only once `dotnet test --solution EtAlii.Adp.slnx` reports `failed: 0` (exit code checked, not the printed text) and `dotnet format style --verify-no-changes --severity info` exits zero.

The requirements document was written against measurements taken at `5844814d` and explicitly invited re-measurement rather than trust. This design re-ran or extended nearly every one of those measurements before deciding anything, and four of them came out differently than the requirements document's own framing suggested. Those corrections are called out inline, in the requirement they belong to, because each one changes what that requirement's design actually has to do.

There are no open design questions left unresolved here: R4.4, R7's extraction calls, and R5.1's registration shape are all decided below, with the evidence each decision rests on.

## Steering Document Alignment

### Technical Standards (tech.md)

- The new shared backend type (R2) goes in `EtAlii.Adp`, per structure.md's definition of that project and its existing flat layout (`ShortGuid.cs`, `PathTruncator.cs`).
- R5's registration-shape rule and R6's two-declaration-shapes rule are written as new tech.md entries, per tech.md's own instruction to record a convention once it stops being a matter of taste.
- R9's fix makes tech.md's "never nest a type" rule (Backend section) actually true; R10 corrects the paragraph that currently overstates its own backlog.
- R7 is applied through tech.md's InspectCode paragraph's own logic: "fix it, or decide deliberately that the rule does not fit... and record that decision where the rule lives" - here, that means recording a decision not to extract, with the reason, rather than leaving the question open.
- R8's negative result is recorded in tech.md's decision log, per structure.md's Documentation standards ("non-obvious architectural decisions belong in tech.md's decision log").

### Project Structure (structure.md)

- R2's shared type follows the one-entity-per-file rule and lands at namespace level, no nesting.
- R3's shared hook follows the client's existing precedent of cross-module shared code living outside any one diagram module's folder: `src/client/src/canvas/` is already imported by four diagram modules' canvas components the same way the new hook will be.
- R4's consolidation follows the precedent already set inside this codebase: the "every definition carries a description" check was moved from a per-module unit test into `EtAlii.Adp.Backend.Tests/Integration Tests/DiagramDiscoveryStartup.Tests.cs`, which builds the real host and sees every deployed module without core naming any of them (`d125bfb6`). The origin and title checks are the same shape of check and join it in the same file.

## Code Reuse Analysis

### Existing Components to Leverage

- **`TestDiagramDefinitionCatalog` / `WardleyTestDiagramDefinitionCatalog` pattern**: R2's new shared type gets its own unit test following the same arrange/act/assert shape already used throughout the backend test suite; no new test infrastructure is needed.
- **`DiagramDiscoveryStartupTests`**: already builds a real `WebApplicationFactory<Program>` and reads `IDiagramDefinitionCatalog` from it. R4's collapsed checks are two more `[Fact]`s in this file, not a new fixture.
- **`useMindmapStream.test.ts`'s mock harness**: mocks `@connectrpc/connect`'s `createClient`, `useAuth` and `useContextConnection`, and drives the hook with `renderHook`/`waitFor` from `@testing-library/react` against a controllable fake async-iterable stream. This is the template for R12's three new test files - not `useAnsibleStream.test.ts`'s pattern, which only asserts on `hook.toString()` and cannot observe the reconnect-timing behaviour R3.3 needs characterised.

### Integration Points

- **`EtAlii.Adp.Diagram`'s existing reference to `EtAlii.Adp`**: `.csproj` `ProjectReference`s in this SDK are transitively visible, and `EtAlii.Adp.Diagram` already references `EtAlii.Adp`. Every diagram module already references `EtAlii.Adp.Diagram`, so R2's shared type is visible to `wardley-map`, `c4` and `azure-pipeline` with **no `.csproj` changes** - confirmed by reading all three modules' project files.
- **`docs/diagrams.md`'s catalog table**: R4's collapsed origin/title check reads this file as its source of truth, exactly as the module doc-comments already claim ("cataloged in docs/diagrams.md as...").

## Re-measurements that change the design

Four corrections, found while designing rather than assumed from the requirements document:

1. **R4's "48 of 50" is two numbers conflated, and both matter differently.** 50 files are literally named `DiagramTests.cs` (no dot - a tech.md filename-convention violation on its own, since the rule is `<Classname>.Tests`). Of those, **49** share the exact origin+title structure (48 true stubs plus `mindmap`'s, which was never removed when mindmap was implemented). The 50th, `wardley-map`'s, carries three extra facts (`Definition_Extension_...`, `Definition_HasDocumentSibling_...`, `Definition_MimeType_...`) that are not duplicated anywhere else and must survive. Separately, **exactly 48** modules have a `.Tests` project whose *only* file is this one - confirmed by listing every candidate project's directory. That 48 is the number that answers R4.4 (which projects would become empty), and it is a coincidence that it matches the finding's "48" - the finding's 48 was counting file structure, not project emptiness, and got the right number by defining "the 48" differently.
2. **R5's own justification, quoted from the code, is wrong about C4.** `ServiceCollectionAddWardleyMapCommandsExtension`'s doc comment claims "That is how the mindmap and C4 modules are split" - but `ServiceCollection.AddC4.cs` is one file; C4 has no `AddC4Commands`. Worse, the real trigger for splitting is not file size at all (see R5 below): it is measurable, and it is not what the comment says.
3. **R7.4's premise is wrong: `ansible-structure` has both a DocumentStore and an unsubscriber.** They are named `IAnsibleProjectStore`/`AnsibleProjectStore` and `AnsibleNodeSubscription` - correctly named for what they are (a reference-counted store over a watched folder, not a single in-memory document; a node subscription, not a generic element one), which is exactly why a name-based grep for "DocumentStore" and "Unsubscriber" missed them. `AnsibleNodeSubscription` is also a **fourth** near-duplicate of R2's callback-disposable idea, uncounted in R2's finding, and it is idempotent - like `PipelineChangeUnsubscriber`, unlike the two being deleted.
4. **Three title mismatches exist between code and `docs/diagrams.md`, not zero**, found by cross-referencing every `Diagram.cs` title against the catalog table: `azure-devops/pipeline` ("Azure DevOps pipeline" vs "Azure DevOps pipeline diagram"), `ansible/structure` (code's parenthetical is shorter than the doc's), and `plantuml/uml` (the doc's cell has a spec-link clause appended that the code title does not carry). These bear directly on R4's design - see below.

## Requirement-by-Requirement Design

### R1 - Behaviour preservation (the gate)

No design content of its own: every requirement below is written to satisfy R1.1-R1.5 as it lands. Two things worth stating once rather than in each requirement:

- **The `IReadOnlyList<DiagramDefinition>` DI gap fixed at `d125bfb6` is the pattern to check for anywhere a new consolidation moves code across a DI boundary**: a `ServiceCollection` built by hand in a test needs the same registrations `AddCommands` needs, explicitly, before calling it.
- **R1.2's exception for test-count drops** covers exactly R4 (48 origin/title pairs collapse into 2 parameterised facts) and R12 (new tests added, not consolidated - reported as an addition, not a drop).

### R2 - One callback-disposable, not three (now four)

**Finding, corrected**: four near-identical types exist, not three. `WardleyElementUnsubscriber` and `C4ElementUnsubscriber` are byte-identical apart from name and namespace - a five-line sealed class invoking an `Action` from `Dispose()`, non-idempotent. `PipelineChangeUnsubscriber` and the newly-found `AnsibleNodeSubscription` are both a thirty-line variant that is **idempotent** (a `_disposed`/`_detach` guard so a second `Dispose()` call does nothing), written independently of each other in different modules.

**Design**:

- A new type, `EtAlii.Adp.CallbackDisposable`, in `src/backend/EtAlii.Adp/CallbackDisposable.cs`:
  ```
  public sealed class CallbackDisposable(Action dispose) : IDisposable
  {
      public void Dispose() => dispose();
  }
  ```
  Deliberately **not** idempotent - this is the exact behaviour `WardleyElementUnsubscriber` and `C4ElementUnsubscriber` have today, and preserving it exactly is what makes the migration behaviour-preserving by inspection rather than by argument.
- `WardleyContextSourceResolver.cs` and `C4ContextSourceResolver.cs` (one call site each) construct `CallbackDisposable` instead; the two old files and their `internal` types are deleted.
- **R2.3's examination, applied to both idempotent variants**: idempotency is not incidental in either `PipelineChangeUnsubscriber` or `AnsibleNodeSubscription` - both exist specifically so a caller cannot double-detach, and `PipelineChangeUnsubscriber`'s doc comment already says so. Both are **kept**, each with its comment extended by one sentence naming what `CallbackDisposable` does not do (idempotent dispose, and - for Pipeline only - a null-argument check).
- **Not building a second shared "idempotent callback disposable" type**: two independently-written, near-identical implementations is real evidence something is shared, but R7.2's veto reasoning applies here too - a second abstraction for two call sites, neither of which is a live pain point, is speculative. If a third idempotent variant appears, that is the point to revisit this, not now.
- R2.4's unit test: `EtAlii.Adp.Tests/CallbackDisposable.Tests.cs` - constructs with an `Action` incrementing a counter, disposes once, asserts the counter is 1; disposes a second time, asserts the counter is 2 (proving non-idempotence is the *specified* behaviour, not an oversight).

### R3 - One diagram stream hook, not five

**Behavioural differences found** (R3.3's required list), from reading all five hooks line by line:

| Difference | Hooks affected | Chosen behaviour |
|---|---|---|
| On reconnect after a **thrown, transient** error, `loading` is not reset to `true` before the retry delay | `useC4Stream`, `useWardleyStream`, `useAnsibleStream`, `useMindmapStream` (not `usePipelineStream`) | Adopt `usePipelineStream`'s: reset `loading` to `true` before the delay, so the canvas can show "reconnecting" rather than a silently-empty, not-loading model |
| On a **clean stream end with no exception**, four hooks loop back immediately with **no backoff**; `usePipelineStream` applies the same backoff as the error path | `useC4Stream`, `useWardleyStream`, `useAnsibleStream`, `useMindmapStream` | Adopt `usePipelineStream`'s: one reconnect path for both a clean end and a transient error, so a server that closes the stream repeatedly cannot hot-loop the client |
| `reportView` present/absent | Present: `useC4Stream`, `usePipelineStream`, `useMindmapStream`, `useAnsibleStream`. Absent: `useWardleyStream` (deliberately - its own doc comment explains a bounded map has nothing to gain from viewport reporting) | Not shared: stays a per-module addition on top of the shared hook, present only where the module calls for it |
| Move mutation shape | `useWardleyStream`/`useC4Stream`: `moveElementTo(elementId, x, y)`. `useMindmapStream`: `moveElement(elementId, newParentId)`. `usePipelineStream`/`useAnsibleStream`: none | Not shared: genuinely different operations (reposition vs. re-parent vs. none), stays per-module |
| Retry delay | All five: bare literal `500` | Named constant, one place |

The first two rows are real behaviour changes for four of five modules, and are explicitly what R3.3 authorises ("the shared hook SHALL adopt one behaviour... Silently picking whichever copy was refactored first is the failure mode this criterion exists to prevent"). Adopting `usePipelineStream`'s shape here is not "whichever was refactored first" - it is chosen because it is strictly more defensive (never spins without backoff, never shows a stale non-loading empty canvas) and because R12 will have characterised all three currently-untested hooks' current behaviour before this lands, so the four changed assertions are visible as failing tests, not silent.

**Design**:

- New file `src/client/src/diagrams/useDiagramStream.ts`, generic in the model type:
  ```
  export function useDiagramStream<TModel>(
    projectId: Uint8Array,
    path: readonly string[],
    emptyModel: TModel,
    applyDelta: (current: TModel, delta: DiagramDelta) => TModel,
  ): { model: TModel; loading: boolean; failed: boolean; client: DiagramServiceClient }
  ```
  Owns: `createClient`, the `AbortController`/`active` lifecycle, the open/read loop, the permanent-vs-transient error split, the `loading`/`failed`/`model` states, and the named `RECONNECT_DELAY_MS = 500` constant (R3.4). Returns the raw `client` so each module's thin wrapper can add its own unary calls without a second `useRef(createClient(...))`.
- Each of the five hooks becomes a thin wrapper: call `useDiagramStream`, add only what R3.2 reserves to it - its model type, its empty value, its `applyDelta`, and (where present) its `reportView`/`moveElementTo`/`moveElement` built on the returned `client`. `useAnsibleStream`'s own doc comment about *not* exposing a move stays, now describing the wrapper rather than the whole hook.
- Files deleted: none of the five hook files disappear (each keeps its own exported `interface` and function name, which callers depend on) - only their internals shrink to the wrapper shape above.

### R4 - Collapse the 48 (49, one is 50) duplicated stub tests

**Design**:

- Two new `[Fact]`s in `DiagramDiscoveryStartupTests` (`EtAlii.Adp.Backend.Tests/Integration Tests/DiagramDiscoveryStartup.Tests.cs`), beside the existing description checks, both iterating `catalog.All.Where(definition => definition.Build is null)` - the structural definition of "stub" that R6 also uses, so the population needs no hand-maintained exclusion list:
  - `AfterStartup_EveryStubDefinitionsOriginMatchesItsCatalogedTag`
  - `AfterStartup_EveryStubDefinitionsTitleMatchesItsCatalogedName`

  Both parse `docs/diagrams.md`'s table once (origin key → title), and for every stub definition assert its `Origin.Key`/`Title` against the parsed row, failing with a message naming the origin key and both strings (R4.2). R4.2's own verification step - break one definition, read the failure - is a task, not a design decision, but the message shape is decided here: `$"{origin.Key}: definition title '{actual}' does not match docs/diagrams.md's '{expected}'"`.
  - **Filtering on `Build is null` is also why this design is safe against the three title mismatches found above**: all three (`azure-devops/pipeline`, `ansible/structure`, `plantuml/uml`) are outside or would trip this population differently. The first two are implemented (`Build` is not null) and are excluded by construction. `plantuml/uml` **is** a stub and **would** trip the new check as designed, because `docs/diagrams.md`'s cell for it carries an appended spec-link clause the code title does not have.
- **Prerequisite fix, landing with R4 and not deferred**: correct `docs/diagrams.md`'s `plantuml/uml` row to drop the appended clause (the wardley row shows where that kind of annotation belongs: the Theory column, not the Diagram column). This is a documentation-only edit - no `.cs` file, no `DiagramDefinition`, no user-visible behaviour changes - so it is not the "rewrite of the 48 stub modules' identity data" this spec's non-goals rule out; it is the opposite, a correction to the document the definitions are checked against. The `azure-devops/pipeline` and `ansible/structure` mismatches are for **implemented** modules, out of this check's population and out of this spec's scope either way (fixing either side would be a user-visible title change to a shipped module) - both are recorded here and routed as findings per CLAUDE.md's bug rule, not touched by this spec.
- **`wardley-map`'s `DiagramTests.cs`** is renamed to `Diagram.Tests.cs` (fixing the filename-convention violation as a side effect) and loses its two now-centralised facts, keeping the three that are genuinely its own.
- **`mindmap`'s `DiagramTests.cs`** is deleted outright - once its two facts are centralised, nothing is left in the file, and `Mindmap.Tests` keeps its other 14 files untouched.
- **R4.4, decided: delete the 48 emptied test projects**, not keep them as scaffolding. Three independent reasons, none of which depends on the others:
  1. A test project that legitimately has zero test methods forever (until its module is implemented) is indistinguishable, to `dotnet test --solution`'s aggregate exit code, from the `Zero tests ran`/MAX_PATH failure CLAUDE.md specifically warns against normalising ("read `Zero tests ran` as a broken build, never as an empty suite" stops being reliable advice if 48 projects are *supposed* to report exactly that, permanently).
  2. The stub shape is not what an implemented module's real test suite ends up looking like - `mindmap` and `wardley-map`, the two modules that graduated from stub to implemented, both grew an entirely different set of files (`Commands.Tests.cs`, `MindmapDocument.Tests.cs`, ... / `WardleyParser.Tests.cs`, `WardleyRuleSet.Tests.cs`, ...) and neither kept anything resembling the stub file. There is nothing here to reuse when a module is actually implemented.
  3. Recreating a `.csproj` and a first test file when a module's implementation spec actually starts is mechanical - copying an existing module's project file and updating names - not novel work, so the "deleting and re-creating costs more than it saves" risk the open question raised does not hold.

  This needs one verification spike before it is trusted at scale, run as the first task under R4's own worktree: empty one project down to zero facts, run `dotnet test --solution EtAlii.Adp.slnx` including it, and confirm what the aggregate exit code does. If a lone zero-test project among 50+ others does *not* poison the aggregate (Microsoft.Testing.Platform may already handle "some projects have zero tests, the solution as a whole does not" gracefully), reason 1 above weakens but 2 and 3 do not - the decision (delete) does not change either way, only how much weight reason 1 carries in explaining it.
- Both `.slnx` and any solution-folder grouping drop the 48 removed `.Tests` project entries; the 48 non-test `EtAlii.Adp.Diagram.<X>` projects (carrying `Diagram.cs`) are untouched - only their test project goes.

### R5 - One shape for module registration

**Design, corrected from the requirements' framing**: the split is not a file-size decision at all, and no size threshold is recorded, because none governs the codebase's actual practice. The real, falsifiable trigger, read directly off the code: `wardley-map` and `mindmap` split `AddX`/`AddXCommands` because a real caller - a test, in both cases - needs to register the command handlers **without** the session factory, toolbox provider and validator, and does so today (`Commands.Tests.cs`, `MindmapSession.Tests.cs`, `MindmapTestProject.cs`, and `AddWardleyMap.Tests.cs`'s `TheCommandsCanBeRegisteredOnTheirOwn`, four real call sites total). `c4`, `azure-pipeline` and `ansible-structure` never split because grepping their entire test suites for a hand-rolled commands-only setup finds none - nothing has ever needed the narrower surface.

- **Chosen shape, recorded in tech.md**: single file is the default. Split into `ServiceCollection.AddX.cs` + `ServiceCollection.AddXCommands.cs` **only once a concrete consumer - a test or another feature - needs the module's command handlers registered without its other seams**. This is R5.3's "at what point to split," answered as a need, not a line count.
- **R5.2, "the five implemented modules SHALL be brought to that shape" - already true, zero code changes required.** `c4`, `azure-pipeline` and `ansible-structure` have no such consumer and are already single-file. `wardley-map` and `mindmap` have one and four such consumers respectively and are already split. Applying the recorded rule to the measured evidence produces the current state exactly - R5's actionable work is documenting the rule, not moving code.
- **`ServiceCollectionAddWardleyMapCommandsExtension`'s doc comment is corrected**: "That is how the mindmap and C4 modules are split" becomes "That is how the mindmap module is split; `c4`, `azure-pipeline` and `ansible-structure` have never needed the narrower surface, so they keep one file" - both because it is currently false about C4, and because leaving it uncorrected while writing the tech.md rule down would immediately contradict what tech.md now says.

### R6 - One shape for the diagram definition declaration

**Design**:

- **Two legitimate shapes, recorded in tech.md**: **stub** - an inline `DiagramDefinition[]` array literal, no `Build` delegate, no named property (49 of 53 files today, dropping as modules implement) - and **implemented** - one or more `public static DiagramDefinition` properties, named for what they identify, each with a `Build` delegate, referenced by the `Definitions` array rather than duplicated. **Trigger**: a module moves from stub to implemented exactly when it gains a `Build` delegate - which is exactly when its own implementation spec starts, so the trigger needs no separate tracking.
- **`DocumentExtension`'s presence is not a third shape**: `ansible-structure` has no `DocumentExtension` constant because it is folder-subject (`Subject: DiagramSubject.Folder`) rather than document-subject - a folder has no sibling body to name an extension for. This is recorded explicitly in tech.md's two-shapes entry so a future reader does not "fix" the omission.
- **The one real outlier: `azure-pipeline`**. It is implemented (`Build` is set) but is the only one of the five with no named property - `Diagram.Definitions` is a one-element inline array. Its own test file already shows the cost: `Diagram.Definitions[0].Origin` in `ServiceCollectionAddAzurePipelineTests.TheSessionFactory_AnswersForThisModulesOrigin`, indexing into the array by position - exactly what R6.2 says a module's own registrations should never need to do.
  - Fix: add `public static DiagramDefinition Pipeline { get; }` to `Diagram.cs` (named for the origin's type part, matching `mindmap`'s precedent of a type-only name rather than `wardley-map`/`ansible-structure`'s vendor+type name - `AzurePipeline.Pipeline` would otherwise stutter against the namespace), with `Definitions` becoming `[Pipeline]`. Update the one indexing call site to `Diagram.Pipeline.Origin`.

### R7 - Reduce the parallel-role duplication (approached role by role, per 7.1)

Two roles have a real measurement to decide from; the rest do not yet, and this design does not manufacture one.

- **`ContextSourceResolver` (58/158 shared, c4 vs. wardley) and `SessionFactory` (17/39, wardley vs. azure-pipeline): decision is not to extract**, recorded here per R7.3. 35-45% overlap is, in the requirements document's own framing, "exactly the band where extraction costs more than it saves," and R7.2's veto is written to be applied exactly here: a shared base that captured the ~35-45% these pairs agree on would still leave each module's session factory or resolver holding the majority of its own logic beside a base class a reader has to hold in mind to understand the rest - the veto's stated failure mode. No abstraction is introduced for either role.
- **`ansible-structure`'s "missing" `DocumentStore` and unsubscriber (R7.4): not a gap.** Both exist, correctly named for a project-tree-with-a-filesystem-watcher rather than a single in-memory document (`IAnsibleProjectStore`/`AnsibleProjectStore`, reference-counted `Acquire`/`Release` rather than a plain singleton store; `AnsibleNodeSubscription`, idempotent like `PipelineChangeUnsubscriber`). Recorded as a decision, per 7.3: the apparent gap was a naming mismatch in the original grep-based finding, not a structural one, and nothing here needs building.
- **The remaining roles - `Session`, `Parser`, `Writer`, `RuleSet`, `ElementMapper`, `ToolboxProvider`, `ContextActionProvider`, `ContextPropertyProvider` - are explicitly deferred, not extracted and not decided against, pending their own measurement.** R7.1 requires each role be justified independently; producing eight more overlap measurements at the rigour the two above got would be its own piece of work, not a paragraph inside this design. Each deferred role, if picked up, follows the same shape already established: measure shared lines between two real implementations, apply the R7.2 veto, record whichever way it comes out under R7.3. No task is generated for these roles now; a future decision to pursue one is a new, small, independently-approved slice, exactly as R11.1 asks for.
- **R7.5** (no extraction crosses the module boundary in the wrong direction): satisfied vacuously, since no extraction is being made under this requirement at all.

### R8 - Orphaned code: the negative result

R8's own finding, in the requirements document, is already the honest negative result: 710 types scanned, 155 single-file, 137 test classes and 14 `ServiceCollection*Extension` classes explained away as expected/false-positive, the final 4 checked by hand and found used. No further scanning is designed here - it is re-runnable as written, and re-running it is exactly what this design did to arrive at the R2/R5/R7.4 corrections above (a hand check of "why does a name-based grep say X is missing" is the same discipline the original scan applied to its own 14+4 candidates).

- **R8.1/R8.2**: the scan method (grep every declared type name, count files it appears in, then hand-check the ones a false-positive rule doesn't explain) is written down as a decision-log entry in tech.md, not as new code - there is nothing to automate here that would not itself need the same hand-verification step the manual pass already did.
- **R8.3**: both stale claims (`.bld` artifacts, `WardleyTestDiagramDefinitionCatalog`) are recorded as withdrawn **only** in this spec's own `requirements.md` today - nowhere else currently states them as open work, so there is nothing further to edit. Recording the negative result in tech.md's decision log (previous bullet) gives the fact a home that survives this spec being archived, which `requirements.md` will not.
- **R8.4**: no finer-grained scan (members, generated messages, client exports) is attempted; this section says so explicitly rather than by omission.

### R9 - Remove the last two nested types

- **`_Model/C4Document.cs`**: `private readonly record struct Line(string Text, string Terminator)` lifts to a new file `_Model/C4DocumentLine.cs`, renamed `C4DocumentLine` (a nested `Line` is not specific enough to stand alone, per tech.md's own worked example). `C4Document`'s internals change `List<Line>` to `List<C4DocumentLine>`; nothing about `C4Document`'s public surface changes.
- **`EtAlii.Adp.Diagram.C4.Tests/Examples.Tests.cs`**: `private sealed class StubCatalog : IDiagramDefinitionCatalog` lifts to a new file in the same test project, `C4ExamplesStubCatalog.cs`, renamed `C4ExamplesStubCatalog` (has behaviour - implements an interface - so it sits beside its parent rather than in `_Model`, per tech.md's rule). Confirmed no existing reusable stub catalog already exists in this test project to use instead.
- **R9.3's verification**: re-run R8's scan method with the search narrowed to nested-type declarations (`grep`-shaped: any type declaration whose enclosing braces sit inside another type declaration) - the same method, applied to a stricter pattern, so it does not need separate tooling.

### R10 - Correct the steering document the measurements have overtaken

tech.md line 108 (*Testing & quality*, "Expect a backlog...") currently reads:

> The rule about nested types under *Backend* already notes roughly a hundred pre-existing offenders; other rules will have their own.

- **R10.1**: corrected to the measured count as of this design (2, dropping to 0 once R9 lands).
- **R10.2**: once R9 lands, rewritten from a backlog framing to a holding one - "The rule about nested types under *Backend* holds with no exceptions; keep it holding" - and the sentence's job (illustrating "expect *a* backlog") is handed to whichever other rule has one, if any does, rather than left to imply nested types still do.
- **R10.3**: the one other steering claim this design's measurements touched - `ServiceCollectionAddWardleyMapCommandsExtension`'s doc comment about C4 (R5) - is not a tech.md claim, so R10 does not cover it; it is corrected under R5 instead, in the file where it lives.

### R11 - Sequence this work around in-flight specs

Ordered by blast radius, per R11.2, each in its own worktree with a short name (`.claude/worktrees/<name>`), each merged to `develop` independently before the next starts, per R11.1:

| Order | Requirement(s) | Worktree | Files touched (approx.) |
|---|---|---|---|
| 1 | R9 + R10 (bundled: R10.2 depends on R9 landing) | `debt-nested` | 4 |
| 2 | R2 | `debt-callback` | 6 (2 deleted, 1 new, 2 call sites, 1 test) |
| 3 | R6 | `debt-defshape` | 3 (azure-pipeline `Diagram.cs`, one test call site, tech.md) |
| 4 | R5 | `debt-regshape` | 2 (tech.md, wardley-map doc comment) |
| 5 | R8 | `debt-orphan` | 1 (tech.md decision log) |
| 6 | R12 | `debt-hooktests` | 3 new test files |
| 7 | R3 (after R12) | `debt-stream` | 1 new hook + 5 thin wrappers |
| 8 | R4 | `debt-stubtests` | 2 files edited, ~96 files/48 projects deleted, `.slnx` edited, `docs/diagrams.md` one row |
| - | R7 | *(not scheduled - deferred per R7's own design above)* | - |

- **R11.3**: before each worktree starts, check the dashboard's active specs against the module(s) that requirement touches (R4 and R7 touch every implemented module; R2 touches `wardley-map`/`c4`; R3/R12 touch all five client hooks) and time the merge against anything mid-flight there - a process step, not a static list, since which specs are active changes weekly and a list written into this design would be stale before implementation starts.
- **R11.4**: if a feature spec's worktree and one of these collide, the feature spec's merge goes first and the cleanup worktree rebases - this is a standing rule for whichever worktree is mid-flight when the collision is noticed, not a per-requirement decision.
- **R4 is last** both because it is the largest (R11.2's stated reason) and because R6's fix (task 3) touches `azure-pipeline/Diagram.cs`, which R4's collapsed check then verifies - landing R6 first means R4 lands against a catalog that is already in its final shape for R6 purposes, rather than needing to special-case the one module still being converged.
- **R3 after R12**, not the reverse, per R12.1 explicitly.

### R12 - Close the client test-coverage gap

- Three new files: `useC4Stream.test.ts`, `useWardleyStream.test.ts`, `usePipelineStream.test.ts`, following `useMindmapStream.test.ts`'s harness (mocked `createClient`, mocked `useAuth`/`useContextConnection`, `renderHook`/`waitFor`, a controllable fake async-iterable stream) - not `useAnsibleStream.test.ts`'s shallower `hook.toString()` pattern, which cannot observe timing.
- **R12.2**: each new test file characterises its hook's *current* behaviour on the two axes R3.3 found diverge - whether `loading` resets to `true` before a reconnect retry, and whether a clean stream end applies the same backoff as a thrown error - using fake timers (`vi.useFakeTimers()`) to make the 500ms delay assertable without a real wait. These are the assertions that will need updating when R3 lands `useDiagramStream`'s chosen behaviour; that is deliberate; per 12.2 this is what makes R3.3's list of differences derived from tests rather than from reading.
- **R12.3**: reported under R1.2 as three added test files, not a consolidation.

## Data Models

### `CallbackDisposable` (R2)

```
namespace EtAlii.Adp;

public sealed class CallbackDisposable(Action dispose) : IDisposable
{
    public void Dispose() => dispose();
}
```

### `useDiagramStream<TModel>` (R3)

```
interface DiagramStreamResult<TModel> {
  model: TModel;
  loading: boolean;
  failed: boolean;
  client: DiagramServiceClient; // for a module's own reportView/moveElement, built on top
}

function useDiagramStream<TModel>(
  projectId: Uint8Array,
  path: readonly string[],
  emptyModel: TModel,
  applyDelta: (current: TModel, delta: DiagramDelta) => TModel,
): DiagramStreamResult<TModel>
```

## Error Handling

### Error Scenarios

1. **R4's collapsed check fails for a stub definition.** The failure names the origin key and both the actual and expected title/tag (message shape decided above), so a maintainer sees exactly which module drifted without opening 48 files to find it - this is R4.2's safety property, verified by deliberately breaking one definition as an implementation task.
2. **R4.4's deletion spike (verification step) finds that a lone zero-test project does poison `dotnet test --solution`'s aggregate exit code.** The decision to delete does not change (see R4.4's three independent reasons), but the write-up of *why* gains a fourth, stronger reason; no design change follows either way.
3. **A future module's implementation needs the commands-only split R5 does not force on it today.** Nothing prevents it - R5's rule is additive ("split once a consumer needs it"), not a ban on ever splitting again.

## Testing Strategy

### Unit Testing

- R2's `CallbackDisposable.Tests.cs` (new).
- R6's azure-pipeline fix is covered by its own existing `ServiceCollectionAddAzurePipelineTests` once `Diagram.Definitions[0]` becomes `Diagram.Pipeline` - no new test needed, an existing one changes what it reads.
- R9's two lifted types keep the coverage their parent types already had; `C4Document.Tests.cs` and `Examples.Tests.cs` are otherwise unchanged.

### Integration Testing

- R4's two new facts in `DiagramDiscoveryStartupTests`, run against the real host exactly as the existing description checks are.

### End-to-End / Characterisation Testing

- R12's three new hook tests, required to land **before** R3's extraction per R12.1, using fake timers to characterise the exact reconnect-timing behaviour R3.3 must then choose between.

### The R1 gate, applied to every item above

Every worktree in R11's table runs, before its own merge: `dotnet test --solution EtAlii.Adp.slnx` (exit code checked, not grepped) and `dotnet format style --verify-no-changes --severity info` (exit zero). R4's worktree additionally reports its test-count drop under R1.2, and R12's reports its addition the same way.
