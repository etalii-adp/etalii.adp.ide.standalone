# The shared diagram client

Two things every diagram canvas needs and none of them should write: the stream that opens a
diagram and folds its deltas into a model, and the report that tells the backend what the reader
can currently see.

- `useDiagramStream.ts` — transport, lifecycle, retry and state for a diagram's delta stream,
  which rides the tab's one `WorkspaceService.Watch` stream. It folds the delta stream into a
  model with the module's own `applyDelta`, re-baselines on reconnect, and hands back the service client so a module can build its own unary calls on it.
  What stays per module is the model type, its empty value and the response-to-model mapping.
- `viewReport.ts` — the client half of the view-delta loop: the `Viewport` and `ViewBox` types,
  `VIEW_REPORT_DEBOUNCE_MS`, `shownRectOf` (view box to reported rectangle) and `viewReportOf`
  (the correlated `UpdateView` call, with the advisory failure swallowed).
- `useViewReport.ts` — the effect that reports a settled view and every later change.
- `noPrivateViewReports.test.ts` — the guard that stops a module writing its own.

## Using the view report

A module builds `reportView` in its stream hook and drives it from its canvas:

```ts
const reportView = viewReportOf(client, projectId, watchId, path);
```

```tsx
useViewReport({
  view: { x, y, w, h },              // the canvas's own view, for keying the change
  report: reportView,
  convert: () => shownRectOf(viewRef.current, surfaceRef.current),
  ready: !loading && !failed,
});
```

Four things about that shape are deliberate and are worth not undoing:

- **`convert` is a function, not a rectangle.** It runs when the debounce fires rather than
  during render, which is how a canvas reports its *live* view and its *measured* surface rather
  than a value captured a moment earlier.
- **`report` and `convert` reach the effect through refs.** A module's `reportView` is rebuilt
  every render; without the indirection the effect re-fires every render and the debounce
  protects nothing.
- **`ready` is not optional.** A report before the first delta describes a view of nothing, and
  one after a permanent failure is a call to a connection the backend has just said is
  unroutable. One module was sending both, because its effect sat before the component's early
  returns and hooks run regardless of a later return.
- **The report observes the view; it is not wired to a gesture.** A pan, a scrollbar thumb, a
  wheel zoom and a programmatic reveal all reach it the same way — they change the canvas's view
  state and the effect fires. That is what makes the loop hold for ways of moving a view nobody
  has thought of yet, and why centralizing the pan gesture elsewhere needs no change here.

## Two shapes of caller

- **A canvas driving an SVG `viewBox`** needs no measurement: its view's span in content units
  *is* `w` and `h`. It calls `shownRectOf(box, surfaceRef.current)` — the surface only matters
  because `preserveAspectRatio="meet"` centres the box and shows more of the diagram on whichever
  axis has room to spare, and reporting the bare box would have the backend cull elements the
  reader is looking straight at.
- **A canvas driving pixels-per-unit** measures its own element and divides, converting at its
  own call site. The shared code performs no unit conversion, deliberately: seconds, rows, a
  0..1 Wardley plane and RDF layout units are the modules' business.

`shownRectOf`'s surface argument is optional, and a canvas that has none passes nothing rather
than passing `null`. The no-surface branch returns the bare box, which is not a fallback bolted
on for the pixel-less case — it is character-for-character what a bare-`viewBox` canvas computed
for itself before this was shared, which is why an explicit test pins that equality.

## The ordering these three specifications share

`drag-and-drop-centralization` owns the pan gesture, `canvas-scrollbars` owns the thumb, and this
owns the report. They meet on one piece of state, and the ordering is stated the same way in both
designs so they cannot drift:

> The canvas's own view state is the single source of truth. State is the module's; gestures
> write it; scrollbars write it and read it back to place the thumb; reporting observes it.

## The rule, and what enforces it

A module never builds its own view report. `noPrivateViewReports.test.ts` fails, naming every
offender at once, when a governed file calls `.updateView(` itself or *declares* one of the three
shared names — `Viewport`, `shownRectOf`, `VIEW_REPORT_DEBOUNCE_MS` — rather than importing it.
Importing, re-exporting and calling are all fine, so nothing an adopting module normally writes
trips it. It governs every `src/diagrams/*/client/` file and `src/client/src/` itself, exempting
this folder and `generated/` by path construction rather than by a name list.

There is deliberately **no import-presence check**. Every adopting module legitimately imports
from here, so "imports the shared module" is true of a correct module and of one that imports it
and hand-rolls a request anyway — and that exact case defeated the first draft of the scrollbar
guard. The fixture pins it: a file importing `shownRectOf` for a real purpose with a
`client.updateView` call beside it is an offender.

## Verifying a change here

Three ways a verification of this code has come back green for the wrong reason. All three were
found while building it, and none of them announces itself.

1. **A sabotage written with LF newlines does nothing.** The working tree is CRLF, so an
   LF-pattern replace matches nothing, changes nothing, and the suite passes — which reads as
   robust code rather than as a sabotage that never ran. Assert the pattern matched *before*
   writing the file.
2. **Sabotaging both call sites can pass the test you are validating.** Making a module's render
   unfiltered in *both* the change path and `UpdateView` keeps the two consistent with each
   other, so an edit still yields one clean Add and the interaction test stays green. The actual
   bug shape is the change path alone — sabotage that one, and it fails that test and no other in
   a suite of thousands.
3. **A zoom assertion on a zoom-at-cursor canvas can be vacuous.** Asserting that a zoom produced
   another report passes even with zoom dropped from the view key entirely, because that gesture
   moves the origin as well and re-fires the report regardless. Compare the reported rectangle's
   *span*, which no origin change can fake.

A fourth, about the tree rather than this code: **a tree-walking guard must be run against the
real tree, not only its fixture.** This one's first draft matched the inline `type Viewport`
import every adopting module writes and indicted six compliant files; the scrollbar guard's
canary caught a walk that silently found nothing because `src/client/src/diagrams` shadows the
source root. Anchor such a walk on two independent markers and assert it found a plausible number
of files.

## Running the client suite in a fresh worktree

`src/client/src/generated/` is both gitignored and tracked, so a fresh worktree holds only the
tracked stubs until codegen runs. `npx vitest` bypasses the `pretest` hook that runs it, and the
suite then fails with dozens of `Failed to resolve import "../generated/..."` — which looks
exactly like a broken build and is not. Run `npm run generate` once, or `npm test`, which runs it
for you.
