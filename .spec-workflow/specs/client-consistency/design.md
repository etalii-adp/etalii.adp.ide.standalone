# Design Document

## Overview

The approved requirements list thirteen findings. Twelve of them are ordinary tidying. The one that matters is a single cause with five symptoms, and this design is mostly about that cause: **`ContextConnectionProvider` awaits its gRPC calls with no rejection path, and every consumer of those promises is written as though they cannot reject.**

The fix is not five `.catch` clauses. It is a rule about what a channel method returns, applied to the channel — after which **three of the five call sites become correct without being edited at all**, because they already handle the failure value the method will now resolve with. That is the test of whether the rule is the right one: a rule that leaves every caller needing a change is a rule that has not found the cause.

## Steering Document Alignment

### Technical Standards (tech.md)

- **Commands** — an edit is one undo away. An edit that never reached the backend is not on the history at all, and the row currently says it landed. This design is about making that impossible rather than about the history mechanism.
- **Client code style** — `src/.editorconfig`'s TypeScript sections. Nothing here changes them, and nothing this design reports is a deviation that file already documents.

### Product Vision (product.md)

- **"Honest about what it knows"** is the standard the four defects fail, and the sentence that justifies the work. A row showing an edit as applied when nothing was written is the application asserting something untrue about the document.

## The rule

**A channel method that returns a value its caller acts on resolves with failure expressed in that value. A channel method that returns `void` is advisory, swallows its fault, and says so in a comment.**

That is not invented for this design. It is read off the contract, which already sorts its methods into exactly those two kinds:

| Method | Declared return | Kind |
| --- | --- | --- |
| `select` | `void` | advisory — already `.catch(() => {})` at [:222](../../../src/client/src/shell/context/ContextConnectionProvider.tsx) |
| `onCancel` | `void` | advisory — already `.catch(() => {})` at [:433](../../../src/client/src/shell/context/ContextConnectionProvider.tsx) |
| `executeAction` / `executeShortcut` | `Promise<ActionOutcome>` | value — `{ accepted, error }` |
| `setProperty` | `Promise<ActionOutcome>` | value — `{ accepted, error }` |
| `onPropose` | `Promise<ContextPromptVerdict>` | value — `{ revision, valid, reason }` |
| `onSubmit` | `Promise<…>` | value |
| `describeProperties` | `Promise<ContextProperty[]>` | value — **and the only one with nowhere to put a failure** |

The two advisory methods already follow the rule. The file therefore does not hold two conflicting conventions after all — it holds one convention applied to two methods and forgotten on the rest, which is a much cheaper thing to fix than a disagreement. What is missing is the sentence saying which is which, so the seventh method does not have to guess.

**`ActionOutcome` and `ContextPromptVerdict` are already failure-carrying shapes.** `{ accepted: false, error }` and `{ valid: false, reason }` are what a rejected call means. The callers already render them.

## Components and Interfaces

### Component 1 — the channel resolves instead of rejecting

Each value-returning method gains a `try`/`catch` that converts a transport fault into its own failure value.

- `setProperty`, `executeAction`, `executeShortcut` → `{ accepted: false, error: <a sentence naming that the change was not saved> }`.
- `onPropose` → `{ revision, valid: false, reason: <a sentence naming that it could not be checked> }`, carrying the revision it was asked about so a stale reply stays recognisable.
- `onSubmit` → its own failure value, on the same pattern.
- `describeProperties` is the exception and needs a shape it does not have. Its array cannot distinguish "this selection has no properties" from "the properties could not be read", and the difference is exactly what the grid must show. It returns `{ properties, error }`; an empty array with an empty error stays the ordinary no-properties case.

**What this does to the five call sites, which is the point:**

| Site | Change needed |
| --- | --- |
| [PropertyRow.tsx:146](../../../src/client/src/shell/panels/PropertyRow.tsx) (dropdown) | **none** — already does `setError(failure)` and restores the draft when `failure` is non-empty |
| [PropertyRow.tsx:177](../../../src/client/src/shell/panels/PropertyRow.tsx) (checkbox) | **none** — same |
| [ChoicePromptDialog.tsx:141](../../../src/client/src/shell/context/ChoicePromptDialog.tsx) | **none** — `then(setVerdict)` now receives an invalid verdict and renders its reason |
| [ContextPromptHost.tsx:180](../../../src/client/src/shell/context/ContextPromptHost.tsx) | **none** — same |
| [PropertyGridPanel.tsx:102](../../../src/client/src/shell/panels/PropertyGridPanel.tsx) | reads `error` and renders an unavailable state |

### Component 2 — the row is correct on its own terms as well

Component 1 removes the defect. It does not make [PropertyRow.tsx:64-66](../../../src/client/src/shell/panels/PropertyRow.tsx) *right*, and the distinction matters:

```
committing.current = true;
const failure = await onCommit(draft);
committing.current = false;
```

With a non-rejecting channel this is fine. It is fine **because of an invariant enforced somewhere else**, and a reader of `PropertyRow` cannot see it. `onCommit` is a prop; nothing stops a future caller passing something that rejects. So the guard is released in a `finally` regardless — not belt-and-braces for its own sake, but because a component whose correctness depends on an unstated property of its props is one refactor from the bug returning.

The consequence being prevented, stated plainly because it is what justifies the work: **one transient backend fault currently leaves that row silently unwritable for the rest of the session.** `committing.current` stays `true`, and every later edit returns early at [:49](../../../src/client/src/shell/panels/PropertyRow.tsx) without writing and without saying anything. The dropdown and checkbox paths fail differently and worse in one respect — they *display the edit as applied* while nothing was written, because the `.then` that would have shown the error and restored the old value never runs.

### Component 3 — the guard

**A test, not a lint rule.** The requirements left both on the table; this design closes it.

`no-floating-promises` was the obvious candidate and is rejected on two grounds. First, the client has **no ESLint at all** — no config, no dependency, no script — so adopting it means introducing type-aware linting, a config, a gate and its whole finding set, to close one class. Second and more decisive: after Component 1 those `void x.then(...)` call sites are **correct**, and `no-floating-promises` would demand churn at exactly the places that are now right. A rule that fights the fix is the wrong rule.

The guard instead asserts the property the rule states, against the channel itself:

> For every value-returning method on the context channel, a transport that rejects produces a resolved failure value rather than a rejection.

Written as a test over `ContextConnectionProvider` with a stubbed client whose every method rejects, iterating the methods rather than naming them one at a time — so a method added later is covered by having been added, which is what makes this the same shape as `themeTokens.test.ts` rather than five more assertions to forget.

### Component 4 — the nine drift items

Requirements 5-8 and the smaller findings. They share no cause and are listed here only to say they are separable: one shape for acquiring a service client, a shared empty-state component that is not the mockup scaffolding, three exports that nothing outside their file uses, and the index key on the selection path.

## Data Models

No wire change. `ContextProperty`, `ActionOutcome` and `ContextPromptVerdict` are client-side types; only `describeProperties`'s return is new, and it is a client shape that never reaches the proto.

## Error Handling

1. **Transport rejects during a property write** — `{ accepted: false, error }`; the row shows the error and restores the document's value.
2. **Transport rejects during a describe** — `{ properties: [], error }`; the grid shows unavailable rather than the previous selection's rows.
3. **Transport rejects during a proposal** — an invalid verdict carrying the asked-about revision; the dialog shows the reason and does not enable confirm.
4. **Transport rejects on an advisory call** — swallowed, as today, with the comment saying why.

## Testing Strategy

### Unit Testing

- The Component 3 guard, iterating the channel's value-returning methods against a rejecting client.
- `PropertyRow`: a commit whose `onCommit` **rejects** leaves the row writable — the `finally` — and one that resolves with a failure shows the error and restores the value. The first fails against today's code.
- `PropertyGridPanel`: a describe that fails renders unavailable, not stale rows.
- `ChoicePromptDialog`: a failing proposal leaves confirm disabled.

### Integration Testing

None. Every one of these is reachable from a stubbed channel, and an integration test here would be slower proof of the same thing.

## Independently landable

CLAUDE.md now permits one worktree per agent where a tasks document claims independence, so this is stated deliberately rather than assumed:

- **Component 1 + Component 3 land together.** The guard is the statement of the rule; landing the rule without it leaves the seventh method to guess again. Component 1 alone changes behaviour at five call sites, so it wants its own gate run.
- **Component 2 lands independently**, before or after — it is two lines and a test in one file, and it is correct either way.
- **Component 4's four items are independent of everything, including each other.** Requirement 7's dead exports and Requirement 8's index key touch one file each.

The sequence that reads best is Component 2 first — smallest, fixes the worst-consequence defect on its own terms — then 1 and 3 together, then 4 whenever. But nothing forces it.

## Sources

Written against `develop` after `db3bc0c0`. Method inventory and line references re-read at design time rather than carried from the requirements; the `f61d6c47` shared view-report half and the four completed viewport adoptions are the neighbouring precedent for extracting a duplicated client concern, and are why Requirement 5's client-acquisition drift is worth doing at all rather than tolerating.
