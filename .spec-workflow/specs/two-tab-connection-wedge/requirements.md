# Requirements Document

## Introduction

**The application cannot be open in two browser tabs at once.** Opening a second tab on the same
origin exhausts the browser's per-origin connection pool, after which **every request from that
browser profile to that origin stops completing** - silently, with no error, no console message,
and no visible failure. The canvas still draws, the explorer still shows its tree, and every
action the user takes does nothing at all.

The recovery is to close the other tab. **No user would guess that**, and nothing in the product
says it.

This is a product defect rather than a development-environment artefact. The user has confirmed
that ADP is served over **plain HTTP** as this repository is configured, and browsers do not
negotiate HTTP/2 over cleartext - so every user gets HTTP/1.1 and its per-origin connection cap.

## What the investigation established, and with which instrument

Every figure below was measured. Each is given with the instrument that produced it, because
**seven earlier counts in this investigation were wrong** and every one of them was correct about
the set it happened to measure. See *Method and its limits*.

1. **The per-origin connection cap is 6.** Measured against a purpose-built origin that sleeps
   five seconds per response - the wedged origin answers in 1-5 ms and nothing on it stacks - with
   25 concurrent requests, counting established TCP connections with `netstat`. The count was
   exactly six and steady for the whole window.
2. **A tab holds three long-lived streams.** `HierarchyService/WatchHierarchy`,
   `ContextService/Watch`, and one `DiagramService/Open` for the active document.
3. **Three is exhaustive.** The whole API declares exactly three server-streaming RPCs -
   `hierarchy.proto:11`, `context.proto:29` and `diagrams.proto:20`. There is no fourth stream to
   discover later.
4. **Extra documents cost nothing.** Measured: five documents opened in succession in one tab,
   probe healthy at every step. Opening a document closes the previous document's `Open` stream,
   so only the active document holds one.
5. **Two tabs wedge it.** One tab on an origin with a project and document open is healthy and a
   probe settles in 4 ms. Two tabs, same origin, each with a document, and the probe never settles.
   Same server, same document, same minute; the only variable is the second tab.
6. **The protocol is HTTP/1.1.** `nextHopProtocol` on the entries that report it, and `curl`
   agrees. The client speaks grpc-web over it (`createGrpcWebTransport`, `AuthContext.tsx:58`),
   translated server-side by `UseGrpcWeb` (`Program.cs:119`).
7. **The pool is per origin per browser profile, shared across tabs.** This is why every
   "reproduce it in a fresh tab" attempt was worthless: a fresh tab joins an already-exhausted
   pool. It is also why `127.0.0.1:5097` appeared healthy while `localhost:5097` did not - those
   are different origin keys.

**There is no server defect.** The hung `DiagramService/UpdateView` that started this
investigation never returned because **it was never sent**. Dispatch had already stopped before it
was issued. `curl` works because it is outside the browser's pool; another origin works because
pools are per origin; established streams keep working because they already hold their
connections.

## What this specification does NOT establish

- **Whether the same wedge occurs in every browser.** Every observation was taken through the
  embedded browser pane. The cap of 6 is Chromium's documented HTTP/1.1 limit and was measured in
  that pane; a browser with a different limit wedges at a different tab count, not never.
- **Whether anything other than tabs can reach the cap.** The pool is shared with everything else
  on that origin in that profile. Two tabs is the reported case, not necessarily the only one.

Neither gap blocks the fix, because the fix is not sensitive to the exact number.

## Alignment with the existing specifications

- `backend-centralization` and `client-centralization` both touch how sessions and streams are
  opened; a change to the number of long-lived streams per tab belongs beside them rather than
  inside them.
- `diagram-workspace-tabs` owns the pane whose behaviour currently keeps extra documents free.
  Requirement 4 constrains that pane and its owner should be told.
- `tests.md`'s manual passes are affected directly: a wedged origin makes a canvas that reports
  nothing indistinguishable from a canvas with a real defect, so any manual check run after the
  wedge is worthless and cannot be told from a genuine finding by looking.

## Requirements

### Requirement 1 - The application works in more than one browser tab

**User Story:** As a user, I want to open ADP in a second tab - to compare two diagrams, or
because I forgot the first was open - without the application silently ceasing to function.

#### Acceptance Criteria

1. WHEN the application is open in two or more browser tabs on the same origin in the same
   browser profile THEN every tab SHALL continue to issue and complete requests normally.
2. WHEN a document is opened in a second tab while a document is already open in the first THEN
   both tabs SHALL continue to receive their diagram deltas.
3. THE number of browser tabs at which the application stops working SHALL NOT be a number this
   specification has to state, because the mechanism that imposes one is removed rather than
   raised.

### Requirement 2 - The fix removes the class, not the instance

**User Story:** As a maintainer, I want the connection budget to stop being a correctness
question, so that no future change has to re-derive how many streams fit.

#### Acceptance Criteria

1. THE application SHALL be served over TLS, so that browsers negotiate HTTP/2 and multiplex
   every request onto a single connection, at which point the per-origin connection cap does not
   apply to it.
2. THE change SHALL cover local development as well as deployment. This repository has no HTTPS
   configuration today - no `UseHttps`, no Kestrel `HttpProtocols`, no https `applicationUrl`, no
   `Urls` - so the honest cost includes a development certificate and a change to how the
   application is run locally.
3. WHERE the application is nonetheless reached over cleartext HTTP - a deployment that
   terminates TLS elsewhere and forwards plain, or a developer overriding the URL - the defect
   SHALL be understood to return in full, and this SHALL be stated in the design rather than
   left for someone to rediscover.

### Requirement 3 - Fewer streams is robustness, and is explicitly not the fix

**User Story:** As a maintainer, I want the per-tab connection cost reduced, while nobody mistakes
that reduction for a solution to the defect.

#### Acceptance Criteria

1. THE three long-lived streams a tab holds SHOULD be reduced, preferably to one, because a
   smaller fixed cost is worth having independently of the protocol.
2. THIS reduction SHALL NOT be recorded as the fix for Requirement 1. Reducing three to one buys
   headroom of four tabs rather than removing the limit, and the arithmetic must then be
   re-checked against every stream anyone adds afterwards.
3. IF Requirement 2 is deferred THEN this requirement SHALL NOT be treated as satisfying it in
   the interim without the design saying plainly how many tabs the product then supports.

### Requirement 4 - The accident that keeps documents free is made deliberate

**User Story:** As the next person to change the document pane, I want to know what I am spending
when I keep an inactive tab mounted, so that I do not reintroduce a worse defect without a single
test failing.

#### Acceptance Criteria

1. THE reason extra documents cost no connections SHALL be recognised as incidental rather than
   designed. `DiagramTabsPanel.tsx` renders through `TabbedPane`, which "renders one tab's content
   at a time", so inactive document tabs are unmounted and their `Open` streams aborted. The
   comment recording that behaviour explains a React **key** bug - a second markdown file showing
   the first one's editor - and does not mention connections.
2. THERE SHALL be a test asserting that the number of concurrently open diagram streams does not
   grow with the number of open documents.
3. THERE SHALL be a comment at that place in `DiagramTabsPanel` stating that unmounting inactive
   tabs is holding up the connection budget, so that a change made for an ordinary reason -
   preserving canvas zoom and pan, an editor's undo stack, or avoiding a re-baseline flicker on
   every tab switch - is made knowingly.
4. WHERE Requirement 2 lands, criteria 2 and 3 SHALL remain, as insurance rather than as the
   guard of last resort.

### Requirement 5 - A wedged application does not look like a working one

**User Story:** As a user, I want to be told that the application has stopped talking to the
server, rather than clicking things that silently do nothing.

#### Acceptance Criteria

1. WHEN a request the user's action depends on has not completed within a bounded time THEN the
   application SHALL surface that, rather than leaving the interface in a state indistinguishable
   from success.
2. THE mechanism SHALL NOT attempt to detect connection-pool exhaustion, which cannot be observed
   from inside the page: a request queued by the browser and a request the server never answers
   are identical to the page, which is the distinction that cost this investigation six of its
   seven wrong answers.
3. WHERE the application can offer the recovery it SHOULD, because closing another tab is not a
   remedy any user would arrive at unaided.

### Requirement 6 - The implementer is handed the reproduction, not the reasoning

**User Story:** As the developer who implements this, I want the evidence rather than the
argument, because the argument was wrong seven times before it was right.

#### Acceptance Criteria

1. THE two-tab reproduction SHALL be the acceptance test: one tab healthy, two tabs wedged, same
   server and document and minute.
2. THE fix SHALL be demonstrated against that reproduction, and a green run without it SHALL NOT
   be accepted as evidence.
3. THE design SHALL carry the measurements and their instruments rather than the chain of
   inference that produced them. Seven counts were wrong on the way here and every one was
   correct about the set it measured; a reader who inherits the reasoning inherits the
   opportunity to make the eighth.

## Non-functional requirements

### Scope

In scope: how the application is served, how many long-lived streams a tab holds, the guard on the
document pane, and telling the user when requests stop completing.

Out of scope: the content of any diagram module, the grpc-web translation layer, and the dev-server
proxy - all three were candidates during the investigation and all three were eliminated.

### Method and its limits

Every measurement in this document is stated with its instrument because the instruments were the
difficulty. Seven counts were wrong before the answer was reached: a network listing filtered to
one URL pattern, ordinal request ids read as connection handles, a `window.fetch` wrapper blind to
the module loader, `startTime` counting queued requests as concurrent, a capacity figure that would
not reproduce, a numerator that omitted what else held a connection, and a stream creation site
counted without asking what unmounts it.

**All seven were correct about the set they measured.** The rule that emerged, and which the design
should follow rather than restate: read a population over time, not at an instant - who is in it,
who owns it, and how long they stay.
