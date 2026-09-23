# Design Document

## Overview

The application holds three long-lived HTTP/1.1 connections per browser tab against a per-origin
cap of six, so a second tab exhausts the pool and every request from that browser profile to that
origin stops being dispatched. This design removes the cap rather than fitting under it, guards the
accident that currently keeps the cost at three, and stops a wedged interface from looking like a
working one.

**It also names one decision this team cannot take on its own.** The work divides cleanly into a
part the team can land and a part that is the user's to decide, and the split is not where it first
appears. See *The TLS question, and who owns it*.

## Steering Document Alignment

### Technical Standards (tech.md)

- **gRPC call shapes.** `diagrams.proto:14` records why the diagram service is two correlated
  one-way legs rather than a bidirectional call: a browser on grpc-web cannot stream a request
  body. That constraint is a consequence of grpc-web over HTTP/1.1 and is unaffected by this
  design - moving to HTTP/2 changes the transport's *multiplexing*, not what grpc-web permits, and
  the client keeps `createGrpcWebTransport`.
- **Serilog.** Any diagnostic added under *Error Handling* uses the established
  `private static readonly ILogger _logger = Log.ForContext<T>();` shape.

### Project Structure (structure.md)

- Endpoint and protocol configuration belongs in `EtAlii.Adp.Backend.Service`
  (`Program.cs`, `appsettings*.json`, `Properties/launchSettings.json`).
- The connection-count guard belongs with the client shell tests, beside
  `DiagramTabsPanel.test.tsx`, because the invariant it protects is a property of the pane.

## The TLS question, and who owns it

The requirements state that TLS is the fix. This design states what it costs, and it is not what
the requirements assumed.

**The certificate is not the obstacle, and there is no per-agent cost.** A valid ASP.NET Core
development certificate already exists on this machine - `CN=localhost`, valid to 2027-05-09, per
`dotnet dev-certs https --check`. It lives in the user's certificate store, so **every session's
dev server on every port shares the one certificate**. The requirements' concern about "a
development certificate for every agent's dev server" does not arise.

**Two things genuinely do cost something, and only one of them is ours.**

1. **Trusting the certificate is a machine-level action requiring the user.**
   `dotnet dev-certs https --trust` writes to the Windows Trusted Root store and prompts for
   consent. An agent must not do that silently, and this design does not ask anyone to.
   **It is also optional**: untrusted works, at the price of one clickthrough per browser profile.
   Requirement 2.3 already records that an untrusted certificate delivers the full fix.
2. **How ADP is served in production is a user decision, not an implementation detail.**
   The user has confirmed it is served over plain HTTP as configured. Changing that is a
   deployment decision about the product, and no task in this specification can assume it.

**So the design splits the fix, and the tasks must follow the split:**

- **Landable by this team, now:** an HTTPS endpoint for local development, the client pointed at
  it, and every gate still green. This alone proves the mechanism and removes the defect for
  everyone developing the product.
- **Not landable by this team:** the production decision. **This is the question for the user, and
  it should be put to them rather than assumed:** *should ADP be served over TLS in production -
  which removes this defect for real users - or does it remain plain HTTP, in which case every
  user wedges at two tabs and Requirement 3's mitigation becomes load-bearing rather than
  optional?*

Recording it here rather than as a task is deliberate. A task that says "serve over TLS" reads as
implementable and would be attempted; the part that is implementable is the development half, and
the rest is a decision.

## Code Reuse Analysis

### Existing components to leverage

- **`ClientDevServerProxy` / `MapClientApp`** (`ServiceCollection.AddClientAppHosting.cs`) already
  routes every non-gRPC request to Vite. It is unchanged: it proxies whatever scheme the endpoint
  was reached on, and the backend's own hop to Vite stays plain HTTP, being server-to-server and
  not subject to a browser's pool.
- **`DiagramViewportRegistry`** already keys by `(watchId, bodyPath)` and already has `Register`
  and `Remove` called from `DiagramService.Open`'s registration and its `finally`. The stream-count
  guard reads that lifecycle rather than introducing bookkeeping of its own.
- **`TabbedPane`** already unmounts inactive tabs. Nothing changes; the design *documents* and
  *guards* the existing behaviour.

### What is deliberately NOT reused

- **No new abstraction over the three streams.** Requirement 3 permits collapsing them and this
  design does not do it, because under TLS the count stops being a correctness question and a
  multiplexing layer built now would be built against a constraint about to be removed.
- **No detection of pool exhaustion.** Requirement 5.2 forbids it and the reason is structural: a
  queued request and an unanswered one are identical from inside the page. Every attempt to tell
  them apart during this investigation failed, six times.

### Integration points

- `AuthContext.tsx:58` builds the transport; its base URL follows the endpoint.
- `vite.config.ts` `server` and `appsettings.developer.json` `Client:DevServerUrl` must continue to
  agree, as the existing comment in `vite.config.ts` already warns.

## Architecture

### Removing the cap

Kestrel's default for an endpoint is `Http1AndHttp2`. Over TLS, ALPN negotiates h2 with any modern
browser and every request multiplexes onto one connection, so three streams and any number of
subsequent requests share it. **No application code decides this** - it is a property of the
endpoint, which is why it removes the class rather than an instance.

**Cleartext HTTP/2 is not an option and the design records why**, so it is not revisited: no major
browser implements h2c, so a browser meeting an `http://` origin speaks HTTP/1.1 regardless of what
Kestrel offers. There is no warning and no fallback to configure - there is no attempt.

### What happens where cleartext survives

Requirement 2.5. Wherever ADP is still reached over plain HTTP - a proxy terminating TLS and
forwarding plain, a developer overriding the URL, or a production deployment that stays as it is -
**the defect returns in full and unchanged**. The design therefore keeps the other two pieces
rather than treating them as redundant:

| piece | under TLS | under cleartext |
|---|---|---|
| Stream-count guard (Req 4) | insurance | the thing preventing a worse defect |
| Incomplete-request bound (Req 5) | rarely fires | the user's only signal |
| Stream reduction (Req 3) | unnecessary | the only lever, and it raises the limit to six tabs |

### The stream-count guard

Requirement 4.2 asks that concurrent diagram streams not grow with open documents. The guard
asserts the **count of live `Open` streams**, not the count of mounted canvases or of hook calls -
those are different populations, and reading one for another is exactly how this defect stayed
hidden. Developer 3 reached the same error from the other side: *per diagram* says how many times
the hook runs, not how many streams exist at once.

The assertion: open N documents in one tab, and the number of `Open` streams that have been started
and not yet closed is 1 for every N.

**And a comment at `DiagramTabsPanel.tsx`** saying what unmounting is paying for. The comment there
today explains a React key bug and does not mention connections, which is why the invariant is
undefended. *An invariant nobody wrote down is a coincidence with tenure.*

### Telling the user, without detecting the cause

Requirement 5. The page cannot observe dispatch, so the mechanism is a **bound on time, not a
diagnosis**: a request the user's action depends on that has not completed within a bounded
interval surfaces as "not responding", through the existing errors-and-warnings surface rather than
a new one. It says the application has stopped talking to the server; it does not claim to know
why, because from inside the page it cannot.

Where the recovery can be offered it should be - closing other tabs - phrased as a suggestion
rather than a diagnosis.

## Error Handling

### Error scenarios

1. **A request exceeds the bound.** Surface it; do not retry automatically, because a retry on an
   exhausted pool joins the same queue and makes the symptom worse while appearing to act.
2. **An untrusted certificate is refused by the browser.** The application is simply unreachable
   and says so in the ordinary way; this is not a state the application can improve.
3. **The dev certificate expires** (2027-05-09). `dotnet dev-certs https --check` reports it; the
   design does not automate renewal.

## Testing Strategy

### The acceptance test is the reproduction

One tab with a project and a document open: healthy, a probe settles in milliseconds. Two tabs,
same origin, each with a document: wedged, the probe never settles. Same server, same document,
same minute. **A fix is demonstrated against that, and a green run without it is not evidence** -
Requirement 6.2.

### Guard testing

The stream-count guard must be seen to fail before it is trusted. Perturb it by keeping an inactive
tab mounted - the exact change the guard exists to catch - and the count must rise with N. A guard
that passes against the defect converts "unverified" into "verified" while nothing has changed.

### What cannot be tested here

Whether the wedge occurs in every browser. Every measurement was taken through one embedded pane;
the cap of six is Chromium's documented HTTP/1.1 limit and was measured there. A browser with a
different limit wedges at a different tab count, not never. This is recorded rather than resolved.

## Sequencing

1. HTTPS endpoint for local development, client pointed at it, four gates green.
2. The stream-count guard and the `DiagramTabsPanel` comment - independent of 1, and the piece that
   matters most if 1 is deferred.
3. The incomplete-request bound.
4. Requirement 3's stream reduction: **only if the production decision comes back as cleartext**,
   at which point it stops being optional.

Steps 2 and 3 do not depend on step 1 and should not be sequenced behind it, because they are
exactly what protects the product in the case where the TLS decision goes the other way.
