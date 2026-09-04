# Tasks Document

Eight tasks in six files, all under `src/client/src/`. **Every task is independently landable except one pair**, which is stated below rather than left to be discovered — so take them in any order, in parallel, one worktree per agent, short names (`.claude/worktrees/cc1` and the like: a long worktree path is what turns a broken build into `Zero tests ran`).

## What this specification is actually fixing

Four defects with one cause: `ContextConnectionProvider` awaits its gRPC calls with no rejection path, and every consumer is written as though they cannot reject. Stated as consequences rather than as a category, because these are what justify the work:

- **A property write that fails leaves the row silently unwritable for the rest of the session.** [PropertyRow.tsx:64-66](../../../src/client/src/shell/panels/PropertyRow.tsx) sets `committing.current = true`, awaits, and clears it after — with no `try`/`finally`. One transient fault latches the guard, and every later edit on that row returns early at `:49` without writing and without saying anything.
- **A dropdown or checkbox edit that fails displays as applied.** [:146](../../../src/client/src/shell/panels/PropertyRow.tsx) and [:177](../../../src/client/src/shell/panels/PropertyRow.tsx) do `void onCommit(next).then(...)`; on rejection that callback never runs, so no error appears *and* the draft keeps the user's new value.
- **A failed describe leaves the grid asserting stale values.** [PropertyGridPanel.tsx:102](../../../src/client/src/shell/panels/PropertyGridPanel.tsx).
- **A failed proposal freezes a dialog's verdict**, possibly leaving confirm enabled for a value the backend never accepted. [ChoicePromptDialog.tsx:141](../../../src/client/src/shell/context/ChoicePromptDialog.tsx), [ContextPromptHost.tsx:180](../../../src/client/src/shell/context/ContextPromptHost.tsx).

## Four rules that apply to every task

**1. A guard is not done until it has been *seen* to fail.** Break the thing it guards, run the suite, confirm the named test fails **with the message naming what you broke**, then restore. Red is not enough: a guard that has stopped reaching its subject also goes red for the wrong reason, and that reads as proof. For task 6 specifically, "seen to fail" means seen to fail *against a transport that rejects* — the exact condition the guard exists for.

**2. Assert your sabotage matched before you run.** A patch written with LF newlines matches nothing in this CRLF working tree: it changes no bytes, the suite passes, and the result is indistinguishable from a guard that survived. Make any scripted patch fail loudly when its pattern is not found, and prefer breaking the narrowest thing — a sabotage wide enough to keep the system self-consistent can leave the very test you are validating green, and one that makes the subject *throw* fails the test for the wrong reason and proves nothing about the guard.

**3. Gates before merge, judged by exit code, captured before any pipe.** From `src/backend/`: `dotnet test --solution EtAlii.Adp.slnx` and `dotnet format style --verify-no-changes --severity info`. From `src/client/`: `npm test` and `npm run typecheck`. Set `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_USE_MSBUILD_SERVER=0` first. Nothing here touches backend code, but the gate set is the gate set. **A fresh worktree needs `npm install` in `src/` before any client command** — `npx vitest` and `npx tsc` bypass the `pretest`/`pretypecheck` hooks that generate `src/client/src/generated/`, only 7 of its 22 files are tracked, and re-running never fixes it. Restore that folder with `git checkout -- src/client/src/generated/` before staging: `buf generate` rewrites it with LF endings, which shows as modified and is line-ending churn only.

**4. Committing and merging in the shared main checkout**, per CLAUDE.md (`db3bc0c0`). Commit with `git commit -F msg -- <paths>` and **name each file, never a directory** — a directory pathspec in this checkout is a sweep by construction, because somebody's uncommitted work is nearly always sitting there. Commit your implementation log the moment `log-implementation` writes it: an uncommitted file of yours is what turns somebody else's careless pathspec into a wrong commit. Before merging, read `git status` for files you did not touch; neither `git stash` nor `git checkout --` is acceptable on another session's work.

## Landability, stated once

- **Task 1 is independent** and is the best first pick: one file, two lines and a test, and it fixes the worst-consequence defect on its own terms.
- **Tasks 2 and 3 must land together.** The guard *is* the statement of the rule; landing the rule without it leaves the next method to guess, which is how four instances arrived in four files at different times.
- **Tasks 4, 5, 6, 7 and 8 are independent of everything, including each other.**

- [x] 1. PropertyRow releases its in-flight guard whatever happens
  - Files: `src/client/src/shell/panels/PropertyRow.tsx`, `src/client/src/shell/panels/PropertyRow.test.tsx`
  - Wrap the body of `commit()` so `committing.current = false` runs in a `finally`. Two lines.
  - **Do this even though task 2 removes the defect**, and the reason is the point: with a non-rejecting channel this code is correct *because of an invariant enforced in another file*, which a reader of `PropertyRow` cannot see. `onCommit` is a prop; nothing stops a later caller passing something that rejects. A component whose correctness depends on an unstated property of its props is one refactor from the bug returning.
  - Test: a commit whose `onCommit` **rejects** leaves the row able to accept the next edit. Write it first and watch it fail — against today's code the second edit is swallowed by the latched guard.
  - _Requirements: 1.2_
  - _Prompt: Implement task 1 for spec client-consistency. Role: React/TypeScript developer | Task: release PropertyRow's committing guard in a finally, and add the regression test for a rejecting onCommit | Restrictions: change no other behaviour in the file; do not touch the channel - that is task 2 | _Leverage: the existing commit() at PropertyRow.tsx:47 | Success: the new test fails against today's code and passes after, and all four gates pass by exit code. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 2. The channel resolves with failure instead of rejecting
  - Files: `src/client/src/shell/context/ContextConnectionProvider.tsx`, `src/client/src/shell/panels/PropertyGridPanel.tsx`
  - **The rule, which is read off the contract rather than invented:** a channel method that returns a value its caller acts on resolves with failure expressed in that value; one returning `void` is advisory, swallows its fault, and says so in a comment. `select` and `onCancel` already do the second — this is one convention applied twice and forgotten on the rest, not two conventions in conflict. **Write the rule into the file as a comment**, because what is missing is the sentence that tells the seventh method which kind it is.
  - `setProperty`, `executeAction`, `executeShortcut` → `{ accepted: false, error }` on a transport fault. `onPropose` → `{ revision, valid: false, reason }`, carrying the revision it was asked about so a stale reply stays recognisable. `onSubmit` → its own failure value on the same pattern.
  - **`describeProperties` is the exception**: its `ContextProperty[]` has nowhere to put a failure, and an empty array cannot say whether the selection has no properties or could not be read — which is exactly what the grid must distinguish. Give it `{ properties, error }` and have `PropertyGridPanel` render an unavailable state when `error` is set. This is the only caller change in the task.
  - **Three call sites are expected to need no edit at all** — `PropertyRow.tsx:146`, `:177` and both dialog sites already handle the failure value. If you find yourself editing them, stop: either the shape is wrong or the rule is.
  - _Requirements: 1.1, 2.1, 3.1, 4.1, 4.2, 4.3_
  - _Prompt: Implement task 2 for spec client-consistency. Role: React/TypeScript developer | Task: make every value-returning context-channel method resolve with a failure value rather than rejecting, give describeProperties a result shape, and render the grid's unavailable state | Restrictions: do not add .catch at the call sites - the point is that they need none; keep select and onCancel exactly as they are; write the rule into the file as a comment | _Leverage: the existing ActionOutcome and ContextPromptVerdict shapes, and the advisory catches at :222 and :433 | Success: the four defect paths surface their failure, the three named call sites are unedited, and all four gates pass by exit code. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 3. The guard that keeps the rule true
  - Files: `src/client/src/shell/context/ContextConnectionProvider.test.tsx` (or a new sibling test file)
  - One test asserting the property the rule states: **for every value-returning method on the context channel, a transport that rejects produces a resolved failure value rather than a rejection.**
  - **Iterate the methods rather than naming them one at a time**, so a method added later is covered by having been added. That is what makes this a guard on the class of defect rather than five more assertions to forget — the same shape as `themeTokens.test.ts`, which closed the phantom-token class.
  - Message: say the rule stopped holding, not that a call failed. "`setProperty` rejected instead of resolving with a failure value" sends the reader to the rule; "network error" sends them to the wrong code.
  - **Acceptance: the guard has been seen to fail against a rejecting transport.** Revert one method to a bare `await` — asserting the patch applied — and watch the test name that method. Restore.
  - **Lands with task 2**, in the same worktree and the same gate run.
  - _Requirements: 4.1, 4.2, 4.3_
  - _Prompt: Implement task 3 for spec client-consistency, together with task 2. Role: React/TypeScript developer | Task: write the guard asserting every value-returning channel method resolves rather than rejects under a rejecting transport, iterating the methods rather than listing them | Restrictions: no lint rule - the client has no ESLint, and after task 2 the void-then call sites are correct, so no-floating-promises would demand churn where the code is right; do not widen the guard beyond the channel | _Leverage: themeTokens.test.ts as the shape | Success: the guard has been seen to fail with one method reverted to a bare await, naming that method, and all four gates pass by exit code. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 4. One shape for acquiring a service client
  - Files: `src/client/src/diagrams/useDiagramStream.ts`, `src/client/src/shell/panels/useToolboxItems.ts`, `src/client/src/pages/LoginPage.tsx`
  - Three shapes are in use for one job: `useMemo(() => createClient(S, transport), [transport])` at four sites, `useRef(createClient(S, transport))` at two, and an inline construction per call at one. Settle on the `useMemo` form, which is the majority and the only one that would survive a changing transport.
  - **Record why the `useRef` form was safe**, in a comment where the choice now lives: `transport` is memoised on a `[]`-stable callback and reads its token through a ref, so it never changes identity. That is the reason nobody noticed, and the next reader should not have to re-derive it. The cost being removed is small and real — `createClient` is evaluated on every render and discarded, and `useDiagramStream` backs every open diagram tab.
  - _Requirements: 5.1, 5.2_
  - _Prompt: Implement task 4 for spec client-consistency. Role: React/TypeScript developer | Task: move the two useRef and one inline client constructions onto the useMemo shape, with a comment recording that transport is identity-stable | Restrictions: change no behaviour; do not touch the four sites that already use useMemo | _Leverage: AuthContext.tsx:49 as the reference shape | Success: one shape remains, and all four gates pass by exit code. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 5. One empty-state component that is not the mockup scaffolding
  - Files: `src/client/src/shell/panels/PropertyGridPanel.tsx`, `src/client/src/shell/panels/DiagramTabsPanel.tsx`, and whichever component the shared markup ends up in
  - `div.panel-placeholder > p.panel-placeholder-title + p.panel-placeholder-description` is rebuilt by hand in two real panels while `PanelPlaceholder` renders it.
  - **The nuance that makes this not a one-line import:** `PanelPlaceholder` carries a `data-future-spec` attribute and a `TODO(diagram-ide-mockup)` saying the branch is scaffolding to be replaced. A real panel reusing it as-is would inherit mockup semantics. Separate the markup contract from the mockup component rather than importing the mockup.
  - _Requirements: 6.1_
  - _Prompt: Implement task 5 for spec client-consistency. Role: React/TypeScript developer | Task: give the empty-state markup one home that is not the mockup placeholder, and use it from the two panels that hand-roll it | Restrictions: do not make real panels import the mockup component or inherit data-future-spec; change no rendered output | _Leverage: PanelPlaceholder.tsx:32 for the markup, and its TODO for what not to inherit | Success: the markup has one definition, the rendered DOM is unchanged, and all four gates pass by exit code. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 6. Stop exporting three symbols nothing outside their file uses
  - Files: `src/client/src/editors/TextEditorPanel.tsx` (`normalizeLineEndings`), `src/client/src/shell/context/ChoicePromptDialog.tsx` (`descriptionFor`, `suggestionFor`)
  - **Check repository-wide before removing each `export`, not within `src/client/src`.** A lane-scoped search lies about this: `isTextTarget` and `structuralShortcutFor` in `canvas/interaction.ts` look unused inside the client and are imported by ten files under `src/diagrams/*/client/`. The three above were confirmed the wide way; confirm again rather than trusting this list, since the tree moves.
  - If a test imports one, keep the `export` and add a comment saying so. That is a reason, not an oversight.
  - _Requirements: 7.1_
  - _Prompt: Implement task 6 for spec client-consistency. Role: TypeScript developer | Task: drop the export keyword from the three named file-local symbols | Restrictions: search the whole repository, not just src/client/src, before removing each one; keep any export a test depends on and say why in a comment | _Leverage: the caution recorded in Requirement 7 | Success: each removal verified repository-wide, and all four gates pass by exit code. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 7. Key the property grid's levels by identity, not position
  - Files: `src/client/src/shell/panels/PropertyGridPanel.tsx`
  - `key={index}` at `:151` over `chain`, which is the selection path and changes length and contents whenever the selection does.
  - **Recorded as drift rather than a defect, and the mitigation is worth knowing before you touch it:** the rows inside are keyed by `property.id`, so a changed property set remounts them. The residual risk is narrow — two selections producing the same property id at the same depth while a row is mid-edit, where `PropertyRow`'s sync effect is guarded by `if (!editing)` and would not overwrite the draft. Derive the key from the level's own identity; do not restructure the component.
  - _Requirements: 8.1_
  - _Prompt: Implement task 7 for spec client-consistency. Role: React/TypeScript developer | Task: key each property-grid level by its own identity rather than its array index | Restrictions: one-line change in character; do not restructure the component or touch the row keys, which are already correct | _Leverage: the row keying at PropertyGridPanel.tsx:164 | Success: levels are keyed by identity, and all four gates pass by exit code. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 8. Documentation and the closing gate run
  - Files: `docs/creating-a-diagram-module.md` or its client sibling, whichever documents the context channel
  - Record the rule where a module author will meet it: **a channel method that returns a value resolves with failure in that value; one returning `void` is advisory and swallows.** One paragraph, naming the guard that keeps it true.
  - Then the four gates on a tree with every task landed, exit codes captured before any pipe.
  - **Do this last**, and only after tasks 1-7 are merged — it is the one task with an ordering constraint, and it is a documentation task rather than a code one.
  - _Requirements: 4.3_
  - _Prompt: Implement task 8 for spec client-consistency. Role: Technical writer with the repository open | Task: record the channel's failure rule in the module-authoring documentation and run the four closing gates | Restrictions: do not restate the whole specification - one paragraph naming the rule and its guard; do not start until tasks 1-7 are merged | _Leverage: the rule as written into ContextConnectionProvider by task 2 | Success: the rule is documented where an author will meet it, and all four gates pass by exit code. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._
