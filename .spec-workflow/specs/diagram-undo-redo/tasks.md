# Tasks Document

> **Nothing blocks this spec.** Everything it needs exists: `HistoryStack`, `CommandDispatcher`, the three hierarchy command handlers with their inverses, the context stream, `IContextActionProvider`, and the ribbon's contextual-group rendering. Unlike `mindmap-diagram`, this spec does not wait on `grpc-core-communication`.
>
> **Worktree.** Per CLAUDE.md, implement in one dedicated worktree for this spec (e.g. `.claude/worktrees/diagram-undo-redo/`). For the manual pass (task 17) use a free port pair and revert `vite.config.ts` and `appsettings.developer.json` before merging.
>
> **Regenerate the client stubs.** Task 5 edits `context.proto`. `src/client/src/generated/context_pb.ts` is **gitignored and untracked**, so it does not update itself and a stale copy fails silently — the client simply never sees the new message case. Run `npm run generate` in `src/client/` as part of task 5, before any client task.
>
> **Order.** 1–4 are self-contained and can land in any order. 5 is the contract. 6–11 are the backend, and 7+9+10+11 must land together: task 7 changes `IContextSelectionStore.Register`'s signature and task 10 changes two provider constructors, after which the solution does not compile until the callers follow. 12 proves the backend. 13–16 are the client, in order. 17 is the manual pass.
>
> **Deviations are approved.** Design deviations 1 (project actions on their own `ContextMessage` member rather than the "nothing selected" baseline) and 2 (`Ctrl+Shift+Z` normalised on the client) were approved with the design. Implement them as designed; do not implement Requirement 5.5 or 5.4 literally.
>
> **Bugs.** Per CLAUDE.md, any bug found while implementing or verifying a task is covered by a unit or integration test, or recorded in `tests.md`.

- [ ] 1. Add `HistoryAvailability` and expose it on `IHistoryStack`
  - File: `src/backend/EtAlii.Adp.Backend/History/_Model/HistoryAvailability.cs` (new), `src/backend/EtAlii.Adp.Backend/History/IHistoryStack.cs` (modify), `src/backend/EtAlii.Adp.Backend/History/HistoryStack.cs` (modify), `src/backend/EtAlii.Adp.Backend.Tests/Unit Tests/History/HistoryStack.Tests.cs` (modify)
  - `public sealed record HistoryAvailability(bool CanUndo, bool CanRedo, int UndoCount, int RedoCount);` and `HistoryAvailability Availability { get; }` on the interface. The implementation reads both sides **under one acquisition of `_sync`**, not by calling the four existing properties in turn, so a caller can never describe Undo from one moment and Redo from another. The existing four properties stay
  - Purpose: one consistent read of what can be undone, which the provider needs to describe two actions at once (Requirement 5.2)
  - _Leverage: src/backend/EtAlii.Adp.Backend/History/HistoryStack.cs (the `_sync` lock and the existing CanUndo/CanRedo/UndoCount/RedoCount), src/backend/EtAlii.Adp.Backend/History/_Model/ (one entity per file)_
  - _Requirements: 5.2_
  - _Prompt: Implement the task for spec diagram-undo-redo, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Add the `HistoryAvailability` record and an `Availability` property to `IHistoryStack`/`HistoryStack` per design.md | Restrictions: one lock acquisition for all four values; do not remove or change the existing properties; no new dependency on anything outside History/ | Success: `dotnet test --solution EtAlii.Adp.slnx` passes, and a test asserts Availability agrees with the four properties after an execute, an undo and a redo_

- [ ] 2. Raise `Changed` from `HistoryStack`
  - File: `src/backend/EtAlii.Adp.Backend/History/HistoryStack.cs` (modify), `src/backend/EtAlii.Adp.Backend.Tests/Unit Tests/History/HistoryStack.Tests.cs` (modify)
  - `public event EventHandler? Changed;` raised after `ExecuteAsync` records an entry, after a successful `UndoAsync`, after a successful `RedoAsync`, and in `Clear`. **Not** raised when a command is rejected, when a command succeeds without an inverse, or when an inverse fails to apply — none of those change what can be undone. Raise it **outside** the `_gate` critical section, after the semaphore is released, so a subscriber that re-enters cannot deadlock
  - Purpose: the trigger for pushing updated availability, without the store polling (Requirement 5.3)
  - _Leverage: src/backend/EtAlii.Adp.Backend/History/HistoryStack.cs (the existing `_gate` try/finally in all three methods)_
  - _Requirements: 5.3, 3.1, 3.2_
  - _Prompt: Implement the task for spec diagram-undo-redo, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer working on concurrent code | Task: Add a `Changed` event to `HistoryStack`, raised exactly where design.md says and outside the gate | Restrictions: never raise while holding `_gate` or `_sync`; do not change any existing return value or message; a throwing subscriber must not corrupt the stack | Success: tests assert Changed fires on a recorded execute, a successful undo and a successful redo, and does not fire on a rejected command, a success without an inverse, or a failed undo_

- [ ] 3. Implement `IHistoryStackStore` / `HistoryStackStore`
  - File: `src/backend/EtAlii.Adp.Backend/History/IHistoryStackStore.cs` (new), `src/backend/EtAlii.Adp.Backend/History/HistoryStackStore.cs` (new), `src/backend/EtAlii.Adp.Backend/History/_Model/HistoryChangedEventArgs.cs` (new), `src/backend/EtAlii.Adp.Backend.Tests/Unit Tests/History/HistoryStackStore.Tests.cs` (new)
  - `IHistoryStack Get(string rootPath)` creating on first ask; `Retain`/`Release` reference counting; on the last release, dispose and drop the stack behind the same idle-timer grace `ContextSelectionStore` uses, so a reconnect within the window keeps its history; `event EventHandler<HistoryChangedEventArgs>? Changed` aggregating every held stack's `Changed` and carrying the root path. Keyed by the **resolved root path**, not `project_id`
  - Purpose: one history per project instead of one per process (Requirements 1.1, 1.5, 4.1, 4.2, 4.5)
  - _Leverage: src/backend/EtAlii.Adp.Backend/Hierarchy/HierarchyModelStore.cs (ConcurrentDictionary plus idle-timer eviction, the pattern to copy), src/backend/EtAlii.Adp.Backend/History/HistoryStack.cs (constructed unchanged)_
  - _Requirements: 1.1, 1.5, 4.1, 4.2, 4.5_
  - _Prompt: Implement the task for spec diagram-undo-redo, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement the per-project history store per design.md's component spec, modelled on HierarchyModelStore's eviction | Restrictions: `Get` never returns null and never throws for an unknown path; unsubscribe from a stack's Changed when dropping it, or the store leaks; key by root path exactly as given, no normalisation the callers do not also do | Success: tests cover same-instance-per-path, different-instance-per-path, release-past-last-retain disposes, retain inside the grace window keeps it, and Changed carrying the right root path_

- [ ] 4. Carry the project root on `ContextTarget`
  - File: `src/backend/EtAlii.Adp.Backend/Context/_Model/ContextTarget.cs` (modify), `src/backend/EtAlii.Adp.Backend/Hierarchy/HierarchyContextSourceResolver.cs` (modify), `src/backend/EtAlii.Adp.Backend/Context/ContextServiceImpl.Actions.cs` (modify)
  - Add `string RootPath = ""` as a defaulted positional member. Set it in `HierarchyContextSourceResolver` and in `ContextServiceImpl.RootTarget`, both of which already hold the root path. The default keeps existing test construction sites compiling
  - Purpose: a provider reaches its project's history without resolving the project a second time (Requirement 1.1)
  - _Leverage: src/backend/EtAlii.Adp.Backend/Context/_Model/ContextTarget.cs, src/backend/EtAlii.Adp.Backend/Hierarchy/HierarchyContextSourceResolver.cs (already receives rootPath)_
  - _Requirements: 1.1_
  - _Prompt: Implement the task for spec diagram-undo-redo, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Add `RootPath` to `ContextTarget` and populate it at every production construction site | Restrictions: defaulted so tests keep compiling, but every production site passes it explicitly; do not reorder existing positional members | Success: solution builds, all tests pass, and no production `new ContextTarget(` relies on the default_

- [ ] 5. Extend `context.proto` and regenerate both sides
  - File: `src/api/context.proto` (modify), `src/client/src/generated/context_pb.ts` (regenerated, untracked)
  - `ContextScope.PROJECT = 2`; `ContextSource` gains `google.protobuf.Empty project = 2`; new `ContextProjectActions { repeated ContextActionGroup actions = 1; }`; `ContextMessage.project_actions = 3` added to the `oneof`. Comment each addition with why, in the style the file already uses. Then run `npm run generate` in `src/client/`
  - Purpose: the wire shape for project-scoped actions (Requirements 5.1, 5.3)
  - _Leverage: src/api/context.proto (existing ContextMessage oneof and ContextActionGroup), src/client/package.json (the generate script)_
  - _Requirements: 5.1, 5.3_
  - _Prompt: Implement the task for spec diagram-undo-redo, first run spec-workflow-guide to get the workflow guide then implement the task: Role: API contract developer | Task: Add the scope value, the source member, the ContextProjectActions message and the ContextMessage case per design.md, then regenerate the client stubs | Restrictions: additive only — no existing field number or name changes; `project` is `google.protobuf.Empty`, never a project id; you MUST run `npm run generate`, because context_pb.ts is gitignored and a stale copy fails silently | Success: `dotnet build` regenerates the C# types, `npm run typecheck` passes, and `context_pb.ts` contains the projectActions case_

- [ ] 6. Implement `HistoryContextActionProvider`
  - File: `src/backend/EtAlii.Adp.Backend/History/HistoryContextActionProvider.cs` (new), `src/backend/EtAlii.Adp.Backend.Tests/Unit Tests/History/HistoryContextActionProvider.Tests.cs` (new)
  - `Scope => ContextScope.Project`. `DiscoverAsync` returns one group with `history.undo` and `history.redo` — icons `mdi-undo`/`mdi-redo`, shortcuts `Ctrl+Z`/`Ctrl+Y`, `Available` from `Availability`, unavailable reasons being the stack's own `There is nothing to undo.`/`There is nothing to redo.`. Always offered, never omitted. `ExecuteAsync` maps to `UndoAsync`/`RedoAsync` and returns `Completed` or `Failed(result.Error)`. `ValidateAsync` and `CommitAsync` throw `NotSupportedException` — neither action prompts, so reaching them is a programming error
  - Purpose: undo and redo offered as ordinary context actions, with no new RPC (Requirements 5.1, 5.2, 5.4, 5.6)
  - _Leverage: src/backend/EtAlii.Adp.Backend/Hierarchy/HierarchyContextActionProvider.cs (the provider shape and its available/unavailable-reason handling), src/backend/EtAlii.Adp.Backend.Tests/Unit Tests/Hierarchy/HierarchyContextActionProvider.Tests.cs (test style), src/backend/EtAlii.Adp.Backend.Tests/Unit Tests/Support/TestHistory.cs_
  - _Requirements: 5.1, 5.2, 5.4, 5.6_
  - _Prompt: Implement the task for spec diagram-undo-redo, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement the provider per design.md's component table, with unit tests over a real history from TestHistory | Restrictions: both actions are always in the result, only their Available differs; the unavailable reasons come from the stack rather than being restated as literals where that is practical; no filesystem or gRPC dependency | Success: tests cover both offered with an empty history and both unavailable, availability tracking a recorded command, execute performing the undo, and an unknown action id rejected_

- [ ] 7. Push project actions from `ContextSelectionStore`
  - File: `src/backend/EtAlii.Adp.Backend/Context/IContextSelectionStore.cs` (modify), `src/backend/EtAlii.Adp.Backend/Context/ContextSelectionStore.cs` (modify), `src/backend/EtAlii.Adp.Backend/Context/ContextMessageMapper.cs` (modify), `src/backend/EtAlii.Adp.Backend.Tests/Unit Tests/Context/ContextSelectionStore.Tests.cs` (modify)
  - `Register` gains `rootPath` and `projectActions`, writing the project-actions message as part of the baseline it already writes. New `PushProjectActions(string rootPath, IReadOnlyList<ContextActionGroupDefinition> actions)` writes one `ContextMessage` to every entry whose root path matches, under each entry's existing gate, touching no other project. Project actions are held per entry so a re-registration can re-send them, and never mix into a `ContextSelectionChanged`
  - Purpose: the carrier for Requirement 5.3, per design deviation 1
  - _Leverage: src/backend/EtAlii.Adp.Backend/Context/ContextSelectionStore.cs (Register, the per-entry gate, the baseline write), src/backend/EtAlii.Adp.Backend/Context/ContextMessageMapper.cs (ToProto for action groups)_
  - _Requirements: 5.3, 2.1, 2.2_
  - _Prompt: Implement the task for spec diagram-undo-redo, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Extend the selection store with project-actions registration and pushing per design.md | Restrictions: a project-actions push must never produce a ContextSelectionChanged; respect each entry's existing gate; a connection in another project must receive nothing; the signature change will break ContextServiceImpl until task 9 — that is expected | Success: tests assert the baseline carries project actions, a push reaches every connection in one project and none in another, and no selection message is emitted by a push_

- [ ] 8. Implement `HistoryActionsBroadcaster`
  - File: `src/backend/EtAlii.Adp.Backend/Context/HistoryActionsBroadcaster.cs` (new), `src/backend/EtAlii.Adp.Backend.Tests/Unit Tests/Context/HistoryActionsBroadcaster.Tests.cs` (new)
  - Singleton started with the host. Subscribes once to `IHistoryStackStore.Changed`; on an event discovers `ContextScope.Project` actions for that root path **once** through `IContextActionResolver`, then calls `PushProjectActions`. Consecutive events for one project within a short window collapse into a single discovery-and-push. A failing discovery is logged and pushes nothing, leaving clients on their last known availability
  - Purpose: availability follows the history without the client polling (Requirement 5.3 and the no-storm Performance rule)
  - _Leverage: src/backend/EtAlii.Adp.Backend/Context/ContextActionResolver.cs, src/backend/EtAlii.Adp.Backend/Hierarchy/HierarchyModelStore.cs (timer handling), src/backend/EtAlii.Adp.Backend.Tests/Unit Tests/Support/TestHistory.cs_
  - _Requirements: 5.3_
  - _Prompt: Implement the task for spec diagram-undo-redo, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Implement the broadcaster per design.md, including the coalescing window and the failure behaviour | Restrictions: exactly one discovery per collapsed burst; a discovery failure must not throw out of the event handler or kill the subscription; dependency direction stays History raises, Context subscribes — History gains no reference to Context | Success: tests assert one change gives one push, a burst collapses to one, and a throwing resolver results in no push and no exception_

- [ ] 9. Wire the store, the broadcaster and the project target into `ContextServiceImpl`
  - File: `src/backend/EtAlii.Adp.Backend/Context/ContextServiceImpl.cs` (modify), `src/backend/EtAlii.Adp.Backend/Context/ContextServiceImpl.Actions.cs` (modify)
  - `Watch` discovers project actions beside the root actions it already discovers, passes both to `Register`, calls `Retain(rootPath)` and `Release(rootPath)` in its existing `finally`. `TryResolveTargetAsync` resolves an explicit `project` source to a project target — scope `Project`, `ResolvedFullPath` and `RootPath` both the root — while the no-source path keeps today's behaviour exactly
  - Purpose: the connection's lifetime drives the history's, and a client can name the project as an action target (Requirements 4.2, 4.5, 5.1)
  - _Leverage: src/backend/EtAlii.Adp.Backend/Context/ContextServiceImpl.cs (Watch's existing discovery, Register and finally), src/backend/EtAlii.Adp.Backend/Context/ContextServiceImpl.Actions.cs (TryResolveTargetAsync and RootTarget)_
  - _Requirements: 4.2, 4.5, 5.1_
  - _Prompt: Implement the task for spec diagram-undo-redo, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Make Watch retain and release the project's history and register project actions, and resolve the project source in TryResolveTargetAsync, per design.md | Restrictions: the no-source path must behave exactly as today — the explorer's act-on-selection flow is not this spec's business; Release must run even when the stream faults; do not add a second project resolution where ProjectRootResolver already ran | Success: solution builds with task 7's signature change, existing context integration tests stay green_

- [ ] 10. Point the hierarchy providers at the per-project store
  - File: `src/backend/EtAlii.Adp.Backend/Hierarchy/HierarchyContextActionProvider.cs` (modify), `src/backend/EtAlii.Adp.Backend/Hierarchy/AddDiagramContextActionProvider.cs` (modify), `src/backend/EtAlii.Adp.Backend.Tests/Unit Tests/Hierarchy/HierarchyContextActionProvider.Tests.cs` (modify), `src/backend/EtAlii.Adp.Backend.Tests/Unit Tests/Hierarchy/AddDiagramContextActionProvider.Tests.cs` (modify), `src/backend/EtAlii.Adp.Backend.Tests/Unit Tests/Support/TestHistory.cs` (modify)
  - Both constructors take `IHistoryStackStore` instead of `IHistoryStack`; each `CommitAsync` dispatches through `Get(target.RootPath)`. `TestHistory` gains a store-returning helper beside its existing `Create()`, which stays for tests that want a bare stack
  - Purpose: a rename, delete or add lands on its own project's history (Requirement 1.1)
  - _Leverage: src/backend/EtAlii.Adp.Backend/Hierarchy/HierarchyContextActionProvider.cs (the DispatchAsync helper, which becomes the one place Get is called), src/backend/EtAlii.Adp.Backend.Tests/Unit Tests/Support/TestHistory.cs_
  - _Requirements: 1.1_
  - _Prompt: Implement the task for spec diagram-undo-redo, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Switch both hierarchy providers from a single history to the per-project store per design.md, and update their tests | Restrictions: resolve the stack once per CommitAsync, not per command; the providers still never touch the filesystem; the existing undo/redo assertions in both test classes must keep passing against a real store | Success: `dotnet test --solution EtAlii.Adp.slnx` green_

- [ ] 11. Register everything and delete the process-wide stack
  - File: `src/backend/EtAlii.Adp.Backend/History/ServiceCollection.AddCommands.cs` (modify), `src/backend/EtAlii.Adp.Backend.Service/Program.cs` (modify)
  - `AddCommands` registers `IHistoryStackStore` instead of the singleton `IHistoryStack`, and its comment about a process-wide history naming this spec as the fix is removed — the fix has landed. `Program.cs` registers `HistoryContextActionProvider` through `IContextActionProvider` and the broadcaster as a singleton started with the host
  - Purpose: Requirement 1.5, and the wiring for everything above
  - _Leverage: src/backend/EtAlii.Adp.Backend/History/ServiceCollection.AddCommands.cs, src/backend/EtAlii.Adp.Backend.Service/Program.cs (the existing provider registrations)_
  - _Requirements: 1.5_
  - _Prompt: Implement the task for spec diagram-undo-redo, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Move the registration from a single IHistoryStack to IHistoryStackStore, register the provider and the broadcaster, and remove the now-stale comment | Restrictions: keep AddCommands as the one place command handlers are registered; the broadcaster must be resolved eagerly at startup or it never subscribes; do not leave a registration for the old singleton | Success: the host starts, a rename still works end to end, and no code resolves a bare IHistoryStack from DI_

- [ ] 12. Integration tests for the whole loop
  - File: `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/UndoRedoFlow.Tests.cs` (new)
  - Over the real host: rename an entry through the context action, assert the project actions arrive with Undo available, execute `history.undo` through `ExecuteAction`, assert the file is back under its old name and the `EntryRenamed` reached the hierarchy stream. Plus: two projects, a command in each, undo in one leaves the other untouched; two connections in one project, a command on A makes Undo available on B and B's undo is visible to A; deleting an entry records nothing, so Undo still reports the entry before it
  - Purpose: proves Requirements 1, 2 and 3.5 against the real wiring rather than against mocks
  - _Leverage: src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/ExplorerContextActionsFlow.Tests.cs (the fixture shape, CreateMessageTimeout, the stream-startup grace)_
  - _Requirements: 1.2, 1.3, 1.5, 2.1, 2.2, 3.5_
  - _Prompt: Implement the task for spec diagram-undo-redo, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# integration test developer | Task: Write UndoRedoFlow.Tests covering the four scenarios in design.md's Integration Testing section | Restrictions: use the existing integration fixture shape and CreateMessageTimeout; pass TestContext.Current.CancellationToken to every call that takes one; assert on observable behaviour — files on disk and messages on streams — not on internal state | Success: all four scenarios pass repeatedly, and the per-project isolation test fails if the store is keyed by anything process-wide_

- [ ] 13. Carry project actions through `ContextConnectionProvider`
  - File: `src/client/src/shell/context/ContextConnectionProvider.tsx` (modify), `src/client/src/shell/context/ContextConnectionProvider.test.tsx` (modify)
  - Handle the `projectActions` message case, holding it in the same state object as `selection`/`levels`/`actions`, exposed through a `useProjectActions()` hook. `executeAction` gains the ability to name the project as the source so the History group and the shortcut act on the project rather than the selection
  - Purpose: the client half of Requirement 5.3
  - _Leverage: src/client/src/shell/context/ContextConnectionProvider.tsx (the existing message switch and the selection context split)_
  - _Requirements: 5.3, 6.2_
  - _Prompt: Implement the task for spec diagram-undo-redo, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React/TypeScript developer | Task: Handle the new message case and expose useProjectActions, per design.md | Restrictions: project actions are separate state from the selection — a project-actions message must not re-render selection consumers or re-run their effects; do not add a client-side history or availability guess | Success: `npm run typecheck` and `npm test` pass, with a test asserting a projectActions message updates that hook and leaves the selection untouched_

- [ ] 14. Implement `RibbonHistoryGroup`
  - File: `src/client/src/shell/ribbon/RibbonHistoryGroup.tsx` (new), `src/client/src/shell/ribbon/RibbonHistoryGroup.test.tsx` (new)
  - One `ribbon-group` rendered from `useProjectActions()`, in the shape `RibbonContextualGroups` uses: a button per action, `disabled={!action.available}`, `title={tooltipFor(action)}`. No state, no last-shown fallback — project actions are not selection-dependent, so there is no gap to bridge
  - Purpose: Requirements 6.1, 6.2, 6.3
  - _Leverage: src/client/src/shell/ribbon/RibbonContextualGroups.tsx (tooltipFor and the button shape), src/client/src/shell/ribbon/RibbonContextualGroups.test.tsx (test style)_
  - _Requirements: 6.1, 6.2, 6.3_
  - _Prompt: Implement the task for spec diagram-undo-redo, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React/TypeScript developer | Task: Build the history ribbon group from pushed project actions per design.md | Restrictions: reuse tooltipFor rather than reimplementing the reason-as-tooltip rule; the component calls executeAction and nothing else; no pressed state, no local disabled logic | Success: tests assert it renders from pushed actions, shows a disabled button whose tooltip is the unavailable reason, and calls executeAction with the action id on click_

- [ ] 15. Implement `useProjectShortcuts`
  - File: `src/client/src/shell/context/useProjectShortcuts.ts` (new), `src/client/src/shell/context/useProjectShortcuts.test.ts` (new)
  - A shell-level `keydown` listener matching against the pushed project actions, the global counterpart of the per-entry matching `ExplorerTreePanel` already does. Two guards: the event is left alone when its target is an `input`, `textarea` or anything `contenteditable`, and when a modal prompt is open. `Ctrl+Shift+Z` is normalised to `Ctrl+Y` before matching (design deviation 2)
  - Purpose: Requirement 5.4 as designed, with no key-to-action table on the client
  - _Leverage: src/client/src/shell/panels/ExplorerTreePanel.tsx (the existing shortcut matching against pushed actions), src/client/src/shell/context/ContextPromptHost.tsx (how to tell a prompt is open)_
  - _Requirements: 5.4_
  - _Prompt: Implement the task for spec diagram-undo-redo, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React/TypeScript developer | Task: Implement the global project shortcut hook per design.md, including both guards and the Ctrl+Shift+Z alias | Restrictions: match against the backend's pushed shortcut data — the only client-side mapping permitted is the key-to-key alias; do not preventDefault for a key you did not handle; remove the listener on unmount | Success: tests cover Ctrl+Z running the matched action, being ignored in input, textarea and contenteditable, being ignored while a prompt is open, and Ctrl+Shift+Z reaching redo_

- [ ] 16. Delete the ribbon's mock Undo and Redo
  - File: `src/client/src/shell/ribbon/RibbonBar.tsx` (modify)
  - Remove the `Edit` group from `RIBBON_GROUPS` and render `<RibbonHistoryGroup />` in its place; mount `useProjectShortcuts`. `File` and `View` keep their mock buttons and the `toggle` handler that serves them — they belong to other specs. The `TODO(diagram-ide-mockup): wire real command handler` goes only if nothing still relies on it
  - Purpose: Requirement 6.4
  - _Leverage: src/client/src/shell/ribbon/RibbonBar.tsx_
  - _Requirements: 6.1, 6.4_
  - _Prompt: Implement the task for spec diagram-undo-redo, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React/TypeScript developer | Task: Replace the mock Edit group with RibbonHistoryGroup and mount the project shortcuts | Restrictions: do not remove the File and View mock buttons or the toggle they use; keep the group's position in the ribbon so the layout does not shift | Success: `npm test` and `npm run typecheck` pass, and no Undo or Redo entry remains in RIBBON_GROUPS_

- [ ] 17. Manual verification pass
  - File: `tests.md` (modify, if anything is found)
  - With the app running from the worktree on a free port pair: rename a file, confirm the ribbon's Undo enables and its tooltip is gone; press Ctrl+Z with a file selected and confirm the rename reverses while the selection stays put; empty the history and confirm Undo greys with "There is nothing to undo." as its tooltip; open the rename dialog and confirm Ctrl+Z edits the text instead of undoing the project; delete a file and confirm Undo does not offer to bring it back. Revert the two port files before merging
  - Purpose: the parts only a running app shows — focus, tooltips and keyboard modality
  - _Leverage: tests.md (existing manual-check format), CLAUDE.md (the worktree port rule)_
  - _Requirements: 3.5, 5.2, 5.4, 6.3_
  - _Prompt: Implement the task for spec diagram-undo-redo, first run spec-workflow-guide to get the workflow guide then implement the task: Role: QA engineer | Task: Run the manual pass listed above against the running app and record anything found | Restrictions: change the client and backend ports before starting and revert both files before merging, per CLAUDE.md; every bug found becomes a unit or integration test where one can express it, and a tests.md entry naming this spec and task only where it cannot | Success: all five checks pass, or each failure has a guard behind it_
