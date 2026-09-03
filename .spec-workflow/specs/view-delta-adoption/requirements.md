# Requirements Document

## Introduction

A diagram opens as two correlated one-way legs: `Open` streams a baseline and then every later change, and `UpdateView` is the unary call telling the backend what the client can currently see. The backend answers a view report with the deltas that bring the connection into line — elements that scrolled into view arrive, elements that left are taken back. This is the **view-delta mechanism**, and mindmap is its reference implementation.

The request is that every diagram implement it. Surveying the tree first changed what that should mean, and this document is written around what the survey found rather than around the assumption it started from.

**The mechanism is not unevenly adopted by accident. It is adopted exactly where it is used, and the two halves agree perfectly.** Counting both sides — the client reporting a viewport, and the backend actually filtering on one:

| Module | Client reports a viewport | Backend filters on it |
| --- | --- | --- |
| mindmap | yes | **yes** |
| c4 | yes | **yes** |
| ansible-structure | yes | **yes** |
| azure-pipeline | yes | **yes** |
| timeline | no | no — `return []`, with a reason in the code |
| dependency-graph | no | no — `return []`, with a reason in the code |
| wardley-map | no | no — `return []`, with a reason in the code |
| helm-charts | no | no — `return []`, with a reason in the code |
| databricks | no | no — `return []` |
| rdf | no | no — `return []` |
| sparql | no | no — `return []` |

**There is no module that reports a viewport without filtering on it, and none that filters without being told.** The "adopted in name only" case the survey was asked to look for does not exist: the four single-reference counts are hooks exposing `reportView` once and canvases calling it, which is the whole of what a client must do. Every abstaining module returns `[]` from a body that says why — *"the connection already holds the whole timeline, and the view transform is the client's own"* — so seven modules did not forget the mechanism; they declined it, in writing.

**That makes "all diagrams should implement it" the wrong shape for a requirement, and Requirement 1 states a criterion instead.** A diagram whose document yields a bounded element count gains nothing from viewport filtering and pays for it in complexity: a second code path that can disagree with the first about what the connection holds. Forcing it on the timeline would add a way for the timeline to be wrong. The honest reading of the request is that *no diagram should lack it because nobody got round to it* — which is a different and much sharper claim, and it identifies exactly one module.

**`rdf` is the genuine gap.** It opens documents with more than 1,600 resources against a drawn-element budget, and it carries a truncation banner — a first-N view with a notice saying the rest was cut — *precisely because* it has no viewport filtering. Its abstention is the one that is not a considered decision but a missing capability, and a first-N truncation is a strictly worse answer to the same problem the mechanism solves properly: it discards content by document order rather than by what the reader is looking at, so panning to the rest never brings it back.

**What is genuinely duplicated is on the client, and it is exact.** The four adopting modules' `reportView` functions are **character-identical** — the same centre-and-bounding-box arithmetic, the same swallowed rejection, the same shape — and each canvas additionally carries its own `const VIEW_REPORT_DEBOUNCE_MS = 200` and its own debouncing effect. Four copies of one function is past the rule of three by a clear margin, and the shared `useDiagramStream` hook that three of them already use is the obvious home: its own comment already anticipates `reportView` as a thing modules build on the client it returns.

**What is not duplicated is the backend, and this specification says so rather than padding its consumer count.** The four `UpdateView` bodies are genuinely different shapes: c4 diffs two visible-element sets inline, ansible-structure delegates to its mapper's `Diff`, azure-pipeline calls its own `Difference()` over expansion state as well as viewport, and mindmap consults its document store before deciding. They resemble each other only in returning deltas. Merging them would be the trap the file-io survey nearly fell into — a centralization that pads its numbers by treating similar-looking things as the same thing. Requirement 4 records this as a deliberate non-goal.

**Protocol history constrains the shape and nothing here changes it.** `diagrams.proto` records that this service was first declared as a bidirectional Connect stream and was split because a browser on grpc-web cannot stream a request body. `UpdateView` is therefore a unary call correlated to the stream by `watch_id` and path, and that pairing is load-bearing rather than incidental. No requirement below proposes changing the protocol.

## Alignment with Product Vision

* [product.md](../../steering/product.md)'s **"Live, pushed updates"** — the mechanism is how a change reaches an open view without a refresh, and how a large document stays openable at all.
* product.md's **"Value from day one"** — a reader who opens a 1,600-resource ontology should see the part they are looking at, not the first thousand things in file order.
* [tech.md](../../steering/tech.md)'s **gRPC call shapes** — the paired-leg design is already settled; this spec adopts it further rather than renegotiating it.
* [structure.md](../../steering/structure.md)'s **core-vs-module boundary** — what is identical across modules moves to the shared client library; what is genuinely each module's own stays in the module.

## Requirements

### Requirement 1 — Who needs the mechanism, stated as a criterion

**User Story:** As a maintainer, I want a test for whether a diagram needs viewport filtering, so that the answer survives new diagram types instead of being a list that ages.

#### Acceptance Criteria

1. WHEN a diagram type is assessed THEN it SHALL be judged to need the view-delta mechanism IF its documents can produce an element count that a canvas cannot usefully draw at once — the practical marker being that the module has, or would otherwise need, a truncation or budget mechanism.
2. WHEN a diagram type's element count is bounded by the shape of its document THEN it SHALL NOT be required to implement viewport filtering, and its `UpdateView` returning `[]` SHALL be a valid, final answer rather than an unfinished one.
3. WHEN a module declines the mechanism THEN its `UpdateView` SHALL say why in the code, as the seven abstaining modules already do — the abstention being a recorded decision, not a silence.
4. WHEN this criterion is applied to the tree as it stands THEN it SHALL select **`rdf`** and no other module, because rdf alone carries a truncation budget that exists to compensate for the missing mechanism.
5. IF a future module meets the criterion THEN it SHALL adopt the mechanism when it is built, rather than shipping a truncation and a later catch-up spec.

### Requirement 2 — rdf gains the mechanism, and its truncation stops being the answer

**User Story:** As a reader of a large ontology, I want to see the part of the graph I am looking at, so that panning brings me the rest instead of nothing.

#### Acceptance Criteria

1. WHEN an rdf-family diagram is open THEN its client SHALL report the visible viewport, and its backend `UpdateView` SHALL answer with the deltas that bring the connection into line.
2. WHEN the reader pans or zooms to a region that was not delivered THEN the elements in that region SHALL arrive, and elements that left the region MAY be taken back — the same contract the four adopting modules already honour.
3. WHEN viewport filtering is in place THEN the drawn-element budget SHALL be re-evaluated against it: the budget SHALL remain as a floor against a pathological view, but it SHALL NOT be the primary mechanism for keeping a large document drawable.
4. WHEN the truncation banner is shown THEN it SHALL mean what it says — that even the current view exceeds the budget — rather than that the document was cut by file order.
5. **The rdf family is one engine serving several readings**, so whichever readings share that session SHALL gain the mechanism together rather than one at a time.

### Requirement 3 — One `reportView`, in the shared client library

**User Story:** As a maintainer, I want the viewport report written once, so that the fifth module to need it does not copy a fourth version of the same twelve lines.

#### Acceptance Criteria

1. WHEN the client-side view report is centralized THEN it SHALL live in the shared client diagram library beside `useDiagramStream`, and the four existing copies SHALL be replaced by calls to it.
2. WHEN it is centralized THEN it SHALL carry the parts that are identical in all four copies: the centre-and-bounding-box conversion from a viewport rectangle, the correlation fields (`projectId`, `watchId`, path), and the deliberate swallowing of a failed report — a view report being advisory, so a failure leaves the backend on the last window it had rather than surfacing an error to the reader.
3. WHEN it is centralized THEN the debounce SHALL be centralized with it, since all four canvases carry the same 200 ms constant and the same effect shape, and an un-debounced report is one call per animation frame while panning.
4. WHEN a module uses it THEN the module SHALL supply only its own viewport rectangle in its own units, and SHALL NOT restate the message shape, the correlation or the error handling.
5. WHEN the centralization lands THEN no module client SHALL construct an `updateView` request itself, and a guard SHALL fail if one does — the same discipline the drag-and-drop and scrollbar specs apply, for the same reason: copied client plumbing spreads silently.

### Requirement 4 — The backend halves stay apart, deliberately

**User Story:** As a maintainer, I want the centralization to stop where the similarity stops, so that it does not merge four different things into one that fits none of them.

#### Acceptance Criteria

1. WHEN the four backend `UpdateView` implementations are compared THEN they SHALL be treated as **genuinely different**, because they are: c4 diffs two visible-element sets inline, ansible-structure delegates to its element mapper's `Diff`, azure-pipeline diffs over expansion state as well as viewport, and mindmap consults its document store first.
2. WHEN a shared backend helper is proposed THEN it SHALL be justified by three or more consumers wanting **the same** behaviour, not by three consumers whose code merely rhymes — the rule of three applied to behaviour rather than to shape.
3. IF a genuine common piece is found — the most likely candidate being "diff a previous delivered set against a current one and emit Remove-then-Add" — THEN it MAY be centralized on that evidence alone, and the modules whose behaviour differs SHALL keep their own.
4. WHEN this specification completes THEN merging the backend implementations SHALL NOT have been attempted merely to raise the count of things centralized.

### Requirement 5 — Nothing that works today stops working

**User Story:** As a user of the four diagrams that already filter, I want this to be invisible to me.

#### Acceptance Criteria

1. WHEN the shared `reportView` replaces a module's own THEN that module's behaviour SHALL be unchanged: the same reports at the same moments with the same contents.
2. WHEN a module already using `useDiagramStream` adopts the shared report THEN its existing tests SHALL pass unchanged, and any test needing adjustment SHALL have the adjustment explained.
3. WHEN a module that does not use `useDiagramStream` adopts the shared report THEN it MAY do so without also adopting the hook, so that this specification is not a second, larger migration wearing a smaller name.
4. WHEN the protocol is considered THEN `diagrams.proto` SHALL NOT change: the paired-leg shape exists because grpc-web cannot stream a request body, and nothing here needs a different shape.

### Requirement 6 — The mechanism is documented where the next module will look

**User Story:** As the author of the next diagram module, I want to know whether I need viewport filtering and how to wire it, before I have written a truncation instead.

#### Acceptance Criteria

1. WHEN the work completes THEN `docs/creating-a-diagram-module.md` SHALL describe the mechanism, state Requirement 1's criterion, and show the shared report being used.
2. WHEN it is documented THEN the abstaining case SHALL be documented too — that returning `[]` with a stated reason is a legitimate, final answer for a bounded diagram.
3. WHEN the shared report is added THEN the shared client library's own readme SHALL name it beside the pieces already there.

## Non-Functional Requirements

### Code Architecture and Modularity

- One implementation of the view report on the client; each module supplies only its viewport rectangle.
- No shared backend implementation without three consumers wanting the same behaviour.

### Performance

- View reports SHALL remain debounced, so panning costs one report per settle rather than one per frame.
- Adopting the mechanism in rdf SHALL reduce, not increase, the elements delivered for a large document — that being the entire point.

### Reliability

- A failed view report SHALL never surface as an error to the reader: the backend keeps the last window it had, which is the behaviour all four current copies already implement.
- A module that declines the mechanism SHALL remain correct: the whole diagram is delivered at baseline, and `UpdateView` answering `[]` is consistent with that.
