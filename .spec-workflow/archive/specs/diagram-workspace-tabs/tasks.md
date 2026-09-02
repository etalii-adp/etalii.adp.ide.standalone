# Tasks Document

Ordered per tech.md's implementation order: the wire field first, its backend resolution and proof, then the client presentation from the bottom up, and the failure behaviour and manual pass last.

> **Regenerate the client stubs.** Task 1 edits `context.proto`. `src/client/src/generated/context_pb.ts` is **gitignored and untracked**, so it does not update itself and a stale copy fails silently — the client simply never sees the new field. Run `npm run generate` in `src/client/` as part of task 1, before any client task.

- [x] 1. Add `diagram_mime_type` to `EntryDetail` and regenerate both sides
  - File: `src/api/context.proto` (modify), `src/client/src/generated/context_pb.ts` (regenerated, untracked)
  - `string diagram_mime_type = 3;` on `EntryDetail`, commented in the file's own style: resolved by the backend through the same routing that opens a diagram, empty for anything not openable, including an ambiguous bare body. Then run `npm run generate` in `src/client/`
  - Purpose: the backend's one-word answer to "is this entry a diagram, and which kind" (Requirement 1)
  - _Leverage: src/api/context.proto (EntryDetail, and the commenting style of its neighbours), src/client/package.json (the generate script)_
  - _Requirements: 1.1, 1.2, 1.3, 1.4_
  - _Prompt: Implement the task for spec diagram-workspace-tabs, first run spec-workflow-guide to get the workflow guide then implement the task: Role: API contract developer | Task: Add the field per design.md's Data Models section and regenerate the client stubs | Restrictions: additive only — no existing field number, name or meaning changes; you MUST run `npm run generate`, because context_pb.ts is gitignored and a stale copy fails silently | Success: `dotnet build` regenerates the C# type with DiagramMimeType, `npm run typecheck` passes, and context_pb.ts carries diagramMimeType | Mark this task in progress ([-]) in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete ([x])_

- [x] 2. Fill the field in `HierarchyContextSourceResolver`
  - File: `src/backend/EtAlii.Adp.Backend/Hierarchy/HierarchyContextSourceResolver.cs` (modify), `src/backend/EtAlii.Adp.Backend.Tests/Unit Tests/Hierarchy/HierarchyContextSourceResolver.Tests.cs` (modify)
  - Where the `EntryDetail` is built, for files only: `_router.Route(fullPath)` → `Routed` carries the definition's MIME type, `UnknownType` carries the named type, `Ambiguous`/`Unreadable`/`NotADiagram` stay empty. The resolver already holds the router; no new dependency
  - Purpose: Requirements 1.1–1.5 — the backend, and only the backend, says what is a diagram
  - _Leverage: src/backend/EtAlii.Adp.Backend/Hierarchy/HierarchyContextSourceResolver.cs (the router it already uses in NestingOf, and the detail construction site), src/backend/EtAlii.Adp.Backend/Hierarchy/_Model/DiagramRouting.cs_
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_
  - _Prompt: Implement the task for spec diagram-workspace-tabs, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# developer | Task: Map the router's answer onto EntryDetail.DiagramMimeType per design.md's component 2, with unit tests over a real temp folder and a hand-built catalog | Restrictions: folders always carry empty; do not modify DiagramFileRouter; one Route call per resolved file level, none for folders | Success: tests cover a registered .adp mindmap, a bare .mm body, an .adp naming an unknown type, an ambiguous bare body, a plain file and a folder — with exactly the mapped values | Mark this task in progress ([-]) in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete ([x])_

- [x] 3. Prove the field over the real host
  - File: `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/ContextSelectionFlow.Tests.cs` (modify)
  - Selecting a registered mindmap in the temp project pushes a level whose `EntryDetail.DiagramMimeType` is `freeplane/mindmap`; selecting a plain file pushes it empty
  - Purpose: Requirement 1 against the real resolver, router and stream rather than mocks
  - _Leverage: src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/ContextSelectionFlow.Tests.cs (the session fixture, ReadSelectionAsync/ReadBaselineAsync)_
  - _Requirements: 1.1, 1.2_
  - _Prompt: Implement the task for spec diagram-workspace-tabs, first run spec-workflow-guide to get the workflow guide then implement the task: Role: C# integration test developer | Task: Extend the selection flow tests with the two assertions above, writing a real .adp/.mm pair into the temp project | Restrictions: use the existing fixture and read helpers; pass TestContext.Current.CancellationToken to every call that takes one; do not add a new fixture class | Success: `dotnet test --solution EtAlii.Adp.slnx` green, with both assertions in place | Mark this task in progress ([-]) in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete ([x])_

- [x] 4. Extend `TabbedPane` with controlled, closable tabs and an empty state
  - File: `src/client/src/shell/panes/TabbedPane.tsx` (modify), `src/client/src/shell/panes/TabbedPane.test.tsx` (new)
  - All additions optional so the three static call sites stay untouched: `activeTabId` + `onSelectTab` (controlled mode), `onCloseTab` (close control per tab, middle-click close, strip visible even for one tab), `emptyState` (rendered when `tabs` is empty), `tooltip` on `TabDef`. Styling through the centralized stylesheet, no inline styles
  - Purpose: the presentation Requirement 4 needs, in the one tab component the shell already has
  - _Leverage: src/client/src/shell/panes/TabbedPane.tsx (markup, roles and classes), src/client/src/index.css or the shell's central stylesheet (wherever .tab/.tab-strip live)_
  - _Requirements: 4.1, 4.2, 4.3_
  - _Prompt: Implement the task for spec diagram-workspace-tabs, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React/TypeScript developer | Task: Extend TabbedPane per design.md's component 3, with a new test file | Restrictions: every new prop optional; uncontrolled behaviour byte-for-byte as today when none are passed; close is a control inside the tab that does not also select it; no inline styles | Success: tests cover controlled selection, the close control, middle-click close, the strip showing for one closable tab, the empty state, and the untouched uncontrolled default; existing suites stay green | Mark this task in progress ([-]) in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete ([x])_

- [x] 5. Name the type a canvas is missing for in `DiagramPanel`
  - File: `src/client/src/shell/panels/DiagramPanel.tsx` (modify), `src/client/src/shell/panels/DiagramPanel.test.tsx` (new)
  - `freeplane/mindmap` keeps routing to `MindmapCanvas`; any other non-empty type renders a placeholder naming it ("No canvas can render `{mimeType}` diagrams yet."). The no-diagram fallback stays as-is
  - Purpose: Requirement 4.4 — an openable type without a view explains itself instead of showing a blank canvas
  - _Leverage: src/client/src/shell/panels/DiagramPanel.tsx, src/client/src/shell/panels/PanelPlaceholder.tsx_
  - _Requirements: 4.4, 1.4_
  - _Prompt: Implement the task for spec diagram-workspace-tabs, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React/TypeScript developer | Task: Add the named-type placeholder branch per design.md's component 6, with tests | Restrictions: do not change the OpenDiagram shape; the mindmap branch and the no-diagram fallback stay exactly as they are; the placeholder names the MIME type verbatim | Success: tests cover the mindmap route, the named placeholder for an unknown type, and the no-diagram fallback | Mark this task in progress ([-]) in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete ([x])_

- [x] 6. Implement `DiagramTabsPanel`
  - File: `src/client/src/shell/panels/DiagramTabsPanel.tsx` (new), `src/client/src/shell/panels/DiagramTabsPanel.test.tsx` (new)
  - The centre pane: `useState` for `tabs`/`activeKey`, one effect on the pushed selection guarded by a last-handled `selection` object reference. Opens on a new non-transient push whose innermost level is an entry id with a non-empty `diagramMimeType` and `innermostAction(selection) === ACTIVATE`; key `base64(entryId) + "|" + path.join("/")`; open key → focus, new key → append and focus. Close removes, focuses the nearest neighbour, and lets unmounting end the stream. Renders the extended `TabbedPane`: label = base name without `.adp` (a bare body keeps its extension), icon `mdi-graph-outline`, tooltip = project-relative path, content = `DiagramPanel`, `emptyState` = one inviting sentence
  - Purpose: Requirements 2, 4.1–4.3, 4.5, 5.2 — the one consumer of the open/focus rule
  - _Leverage: src/client/src/shell/context/ContextConnectionProvider.tsx (useContextSelection, innermostAction, innermostKey), src/client/src/shell/panels/DiagramPanel.tsx (OpenDiagram), src/client/src/shell/panes/TabbedPane.tsx_
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 4.1, 4.2, 4.3, 4.5, 5.2_
  - _Prompt: Implement the task for spec diagram-workspace-tabs, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React/TypeScript developer | Task: Implement the tab model and rendering per design.md's component 4, with tests mocking useContextSelection the way RibbonContextualGroups.test.tsx mocks its hooks | Restrictions: a pure subscriber — no RPC, no stream, no client-side file-type inference; transient previews and re-observed selection objects must not trigger; nothing in the file names a diagram type | Success: tests cover open-on-ACTIVATE, plain and preview pushes opening nothing, focus-not-duplicate, a changed path for one entry opening a second tab, close focusing the neighbour and unmounting content, the empty state after the last close, and the same selection object not re-triggering | Mark this task in progress ([-]) in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete ([x])_

- [x] 7. Replace the placeholder tabs in `WorkspaceShell`
  - File: `src/client/src/shell/WorkspaceShell.tsx` (modify)
  - The hard-coded "Diagram 1"/"Diagram 2" `TabbedPane` in the centre position becomes `<DiagramTabsPanel projectId={projectId} />`; nothing else in the layout moves
  - Purpose: Requirement 4.3 — the placeholders are gone entirely
  - _Leverage: src/client/src/shell/WorkspaceShell.tsx_
  - _Requirements: 4.3_
  - _Prompt: Implement the task for spec diagram-workspace-tabs, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React/TypeScript developer | Task: Swap the static centre TabbedPane for DiagramTabsPanel | Restrictions: the surrounding SplitPane structure, sizes and every other pane stay untouched; no "Diagram 1"/"Diagram 2" string remains | Success: `npm run typecheck` and `npm test` pass; the shell renders the empty state where the mock tabs used to be | Mark this task in progress ([-]) in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete ([x])_

- [x] 8. Activate the revealed entry after a create
  - File: `src/client/src/shell/panels/ExplorerTreePanel.tsx` (modify), its existing reveal test (modify)
  - In the reveal effect, after `focusNode(leafKey)`: `selectNode(leafKey, { case: "action", value: ContextSelectionAction.ACTIVATE })`. The existing `gestureKeyRef` suppresses the duplicate plain focus-select, exactly as for a double-click; the timeout path is untouched
  - Purpose: Requirement 3 — creation opens through the same rule as a double-click, with no caller special-casing (`mindmap-diagram` 1.7)
  - _Leverage: src/client/src/shell/panels/ExplorerTreePanel.tsx (the reveal effect, selectNode, gestureKeyRef)_
  - _Requirements: 3.1, 3.2, 3.3, 3.4_
  - _Prompt: Implement the task for spec diagram-workspace-tabs, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React/TypeScript developer | Task: Upgrade the reveal to an activation per design.md's component 5 and update the reveal test to expect the gesture | Restrictions: exactly one ACTIVATE selection per reveal, no duplicate plain select for the same key; the give-up timeout still clears without selecting | Success: the updated reveal test asserts one ACTIVATE gesture selection and the timeout path unchanged; the rest of the explorer suite stays green | Mark this task in progress ([-]) in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete ([x])_

- [x] 9. A dead diagram degrades: `useMindmapStream` failure state
  - File: `src/client/src/shell/panels/mindmap/useMindmapStream.ts` (modify), `src/client/src/shell/panels/mindmap/MindmapCanvas.tsx` (modify), tests beside them (modify/new)
  - The hook returns `failed`: a stream error whose Connect code is `FailedPrecondition`, `NotFound` or `Unimplemented` stops the retry loop and sets it; every other error keeps the existing back-off-and-reopen. `MindmapCanvas` renders "This diagram is no longer available at `{path}`." when failed, instead of the canvas
  - Purpose: Requirements 5.1, 5.3 — today a deleted file means a silent infinite reconnect; permanent codes must end it, transient ones must not
  - _Leverage: src/client/src/shell/panels/mindmap/useMindmapStream.ts (the reconnect loop), @connectrpc/connect's ConnectError/Code_
  - _Requirements: 5.1, 5.3_
  - _Prompt: Implement the task for spec diagram-workspace-tabs, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React/TypeScript developer | Task: Add the bounded failure policy per design.md's component 7, with tests | Restrictions: transient errors keep exactly today's retry and re-baseline behaviour; the failed message names the project-relative path; the tab itself is not closed by the canvas | Success: tests cover a FailedPrecondition error yielding failed with no further reconnect, a generic error still reconnecting, and the canvas rendering the unavailable message when failed | Mark this task in progress ([-]) in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete ([x])_

- [x] 10. Manual verification pass — and settle the mindmap spec's debt
  - File: `tests.md` (modify, if anything is found), `.spec-workflow/archive/specs/mindmap-diagram/tasks.md` (modify), `docs/diagrams.md` (modify)
  - With the app running from the worktree on a free port pair: add a mindmap through the dialog and confirm it opens in a focused tab named after it; type into the root node; double-click it in the explorer and confirm the existing tab focuses, no duplicate; close it and reopen it; activate a plain `.txt` and confirm no tab; delete the open mindmap's files on disk and confirm the tab degrades to the unavailable message; arrow-key through the tree and confirm no tab opens. Then: mark `mindmap-diagram` task 22 complete (this spec delivered it), run that spec's task 24 manual pass, and on success mark it complete and move the catalog row to ✅ implemented. Revert the two port files before merging
  - Purpose: the parts only a running app shows, plus closing `mindmap-diagram` 1.7's delivery loop
  - _Leverage: tests.md, CLAUDE.md (worktree port rule, diagram catalog rule), .spec-workflow/archive/specs/mindmap-diagram/tasks.md_
  - _Requirements: 2, 3, 4, 5 (behavioural), mindmap-diagram 1.7_
  - _Prompt: Implement the task for spec diagram-workspace-tabs, first run spec-workflow-guide to get the workflow guide then implement the task: Role: QA engineer | Task: Run the manual pass above against the running app, then settle the mindmap spec's open tasks 22 and 24 as described | Restrictions: change the client and backend ports before starting and revert both files before merging, per CLAUDE.md; every bug found becomes a unit or integration test where one can express it, and a tests.md entry naming this spec and task only where it cannot; do not flip the catalog row unless the mindmap manual pass actually succeeded | Success: all checks pass or each failure has a guard behind it; mindmap-diagram tasks 22 and 24 and the catalog row reflect reality | Mark this task in progress ([-]) in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete ([x])_
