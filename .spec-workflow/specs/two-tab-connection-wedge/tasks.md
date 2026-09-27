# Tasks Document

Five tasks against the approved design. **Tasks 2, 3 and 4 do not depend on task 1 and must not be
sequenced behind it** - that ordering is from the design and it is deliberate. A production decision
is not a shipped configuration, the window between them is unbounded, and the guard and the bound
are what protect the product during exactly that window.

**The production decision has been taken: ADP is to be served over TLS. It is the user's ruling,
given directly on 2026-09-24** in answer to the design's question, which was put to them as a
selection between TLS, staying on plain HTTP, and a third deployment answer nobody had.

*Provenance, recorded because it was briefly weaker than this.* The ruling first reached this
document as a relay from the Scrum master, and the design's approval carries no comment, so the
first version of this paragraph attributed it to the relay rather than to the user - a record that
overstates its own provenance being worse than one admitting a weak link. It was then confirmed by
the user directly, so the attribution above is theirs. The relay turned out to be accurate, which
is one trial and not evidence that relaying is sound.

The consequence is written closed rather than hedged: **Requirement 3's stream reduction is
optional**, task 5, and is not needed to make the product correct.

**The certificate is not an obstacle and there is no per-agent cost.** An earlier draft of the
requirements claimed the honest cost included "a development certificate", and **that claim was
measured and withdrawn**: `dotnet dev-certs https --check` reports a valid ASP.NET Core development
certificate already present - `CN=localhost`, valid to 2027-05-09 - living in the user's certificate
store, so **every session's dev server on every port shares the one certificate**. This note exists
because a reader meeting "one clickthrough per browser profile" in task 1 will otherwise reconstruct
the worry that was already killed.

**Worktree.** `.claude/worktrees/ttw` - short, because a long worktree path is what turns
`dotnet test` into `Zero tests ran`. Identity at creation:
`git config --worktree user.name "developer-N-two-tab-wedge"`, read back with plain
`git config user.name` before the first commit. `MSBUILDDISABLENODEREUSE=1` and
`DOTNET_CLI_USE_MSBUILD_SERVER=0` before gating. **Capture every gate's exit code into a variable
before any subsequent command, not merely before a pipe** - a trailing `; echo "EXIT=$?"` replaces
the runner's status with the echo's, which caught four sessions on 2026-09-23, and read the runner's
own exit from the log rather than a task notification.

**Merging.** Through your own scratch worktree (`.claude/worktrees/mrg<N>`, never a shared name),
via the shared gate, then the printed `land.sh` line run yourself in the foreground. Never chain the
fast-forward onto the gates.

**Specification documents are not worktree work**: this file, its approvals and the implementation
logs are written and committed on `develop` in the main checkout with an explicit pathspec.

**Coverage.** The requirements-to-tasks diff was run before this document was requested for
approval. **Twenty-two acceptance criteria; no real gaps.** Fourteen are claimed by a task below.
**Eight can be claimed by no task and are satisfied by the documents' own shape** - stated here so
the diff's silences read as checked rather than missed:

- **1.3** - the tab count is not a number this specification states. Satisfied by not stating one.
- **2.2** - the design shall not propose cleartext HTTP/2. Discharged by the design's *Architecture*
  section, which records why it is unavailable so it is not revisited.
- **2.5** - what happens where cleartext survives. Discharged by the design's table of what each
  piece is worth under TLS versus under cleartext.
- **2.6** - the separate-origin substitute is ruled out. A design ruling; its antecedent is now false
  anyway, and the prohibition stands.
- **3.2** and **3.3** - constraints on how the reduction is *recorded*, not on code. Discharged by
  this document's own wording, above and in task 5.
- **4.4** - criteria 4.2 and 4.3 remain once TLS lands. Discharged by task 3 being unconditional.
- **6.3** - the design carries measurements and their instruments. Discharged by the design as
  written.

**And a ninth silence that is neither a gap nor a document-shape satisfaction, which is why it is
listed separately: the production halves of 2.1 and 2.4.** Each of those criteria has a
local-development half a task claims (task 1) and a production half **no task can claim, because it
is a decision rather than work**. It has been taken, as recorded above. **No task is written for it,
and none should be added** - a task reading "serve over TLS in production" would look implementable
and be attempted.

- [x] 1. An HTTPS endpoint for local development, and the client pointed at it
  - Files: `src/backend/EtAlii.Adp.Backend.Service/Properties/launchSettings.json`,
    `appsettings.developer.json`, `src/client/vite.config.ts`,
    `src/client/src/auth/AuthContext.tsx`
  - Add an `https://` endpoint. Kestrel already defaults an endpoint to `Http1AndHttp2`, so **no
    application code decides the protocol** - over TLS, ALPN negotiates h2 with any modern browser
    and every request multiplexes onto one connection. That is why this removes the class rather
    than raising a limit.
  - Point `createGrpcWebTransport`'s base URL at the new scheme (`AuthContext.tsx:58`). The client
    keeps grpc-web: `diagrams.proto:14`'s constraint is about what grpc-web permits, not about
    multiplexing, and nothing in the call shapes changes.
  - `ClientDevServerProxy` is unchanged. It proxies whatever scheme the endpoint was reached on, and
    the backend's own hop to Vite stays plain HTTP - server-to-server, not subject to a browser's
    pool.
  - Keep `vite.config.ts`'s `server` and `appsettings.developer.json`'s `Client:DevServerUrl` in
    agreement, as the existing comment in `vite.config.ts` already warns.
  - **The trust step is the user's, not yours.** `dotnet dev-certs https --trust` writes to the
    Windows Trusted Root certificate store and prompts for consent; an agent must not do that, which
    is why this task does not include it and why no task may. **It is also optional**: an untrusted
    certificate delivers the full fix (Requirement 2.3), at one clickthrough per browser profile.
    If the warning is in your way, ask the user - do not run `--trust` on their machine.
  - **Coordination hazard worth announcing before you start.** These are tracked, shared files. Every
    session's dev server inherits the change on its next pull, and several sessions run their own
    backend on their own port. Tell the Scrum master before landing rather than after.
  - _Requirements: 1.1, 1.2, 2.1 (development half), 2.3, 2.4 (development half)_

- [x] 2. The reproduction, written down as a procedure rather than a test
  - Files: `tests.md`
  - **This cannot be an automated test and the task says so rather than pretending.** The
    reproduction needs two real browser tabs sharing one browser profile on one origin; no
    unit or integration harness can produce a shared per-profile connection pool. CLAUDE.md's rule
    applies directly: a bug only a running app can reproduce leaves a step-by-step entry in
    `tests.md`.
  - **The consequence, stated because a bare prohibition does not survive a reader who thinks they
    have found a way round it.** An integration test that stands in for this reproduction will pass.
    It will pass because it is measuring something else - one client, one pool, no contention - and
    it will then report health on the exact defect it was written to catch. **That is worse than no
    test at all**, because it converts "unverified" into "verified" while nothing has been verified.
    This repository has already produced three client tests that passed against the code they were
    written to catch. If you believe you have automated it, you have automated a different thing:
    say so and leave the procedure in place.
  - The entry: open the app, open a project and one document - healthy, a probe settles in
    milliseconds. Open a second tab on the **same origin**, open a document there. Before the fix:
    wedged, and nothing on that origin completes again, including a plain static file. After the
    fix: both tabs work.
  - Record the two traps that made this hard to see, because a later tester will hit both.
    **`127.0.0.1` and `localhost` are different origin keys**, so using one in each tab does not
    reproduce it and looks like the defect is absent. And **the pool is per profile, shared across
    tabs**, so a "fresh tab" joins an already-exhausted pool rather than starting clean - which is
    why every fresh-tab trial during the investigation was worthless.
  - Add the precondition this makes necessary for every other manual row: **a wedged origin makes a
    canvas that reports nothing indistinguishable from a canvas with a real defect.** Any manual
    check run after the wedge is worthless and cannot be told from a genuine finding by looking, so
    confirm the origin still answers before recording any negative row.
  - _Requirements: 6.1, 6.2_

- [x] 3. The stream-count guard, and the comment saying what unmounting pays for
  - Files: `src/client/src/shell/panels/DiagramTabsPanel.test.tsx`,
    `src/client/src/shell/panels/DiagramTabsPanel.tsx`
  - Assert the **count of live `Open` streams** - started and not yet closed - and that it does not
    grow with the number of open documents: 1 for every N. **Not** the count of mounted canvases and
    **not** the number of times a hook is called. Those are different populations, and reading one
    for another is how this defect stayed hidden: *per diagram* says how many times the hook runs,
    not how many streams exist at once.
  - **See the guard fail before trusting it.** Perturb it by keeping an inactive tab mounted - the
    exact change it exists to catch - and the count must rise with N. A guard that passes against the
    defect converts "unverified" into "verified" while nothing has changed. Record the failure,
    message and all, in the implementation log.
  - Add the comment at `DiagramTabsPanel.tsx` where `TabbedPane` renders one tab at a time, saying
    that unmounting inactive tabs is holding up the connection budget. **The comment there today
    explains a React key bug and never mentions connections**, which is precisely why the invariant
    is undefended: an invariant nobody wrote down is a coincidence with tenure. A future change made
    for an ordinary reason - preserving canvas zoom and pan, an undo stack, avoiding a re-baseline
    flicker - must be made knowingly.
  - **Independent of task 1, and the reason is here rather than only in the design, because this is
    the ordering a later reader will want to tidy away now the TLS answer landed on the comfortable
    side.** A production decision is not a shipped configuration. The window between deciding to
    serve over TLS and actually serving over TLS is unbounded, and during it this guard is the only
    thing standing between the product and the worse defect - four documents in one tab. Sequencing
    it behind the TLS work leaves the product unprotected in exactly the interval the guard exists
    for.
  - _Requirements: 4.1, 4.2, 4.3_

- [x] 4. A bound on how long an incomplete request may look like success
  - Files: the client's request path and the existing errors-and-warnings surface
  - A request the user's action depends on that has not completed within a bounded interval surfaces
    as "not responding", through the **existing** errors-and-warnings surface rather than a new one.
  - **Do not attempt to detect connection-pool exhaustion.** Requirement 5.2 forbids it and the
    reason is structural, not stylistic: from inside the page a request queued by the browser and a
    request the server never answered are identical. Six separate attempts to tell them apart failed
    during the investigation; the seventh needed `netstat` from outside the browser. **It is a bound
    on time, not a diagnosis** - say the application has stopped talking to the server, do not claim
    to know why.
  - **No automatic retry.** On an exhausted pool a retry joins the same queue and makes the symptom
    worse while appearing to act.
  - Where the recovery can be offered it should be - closing other tabs - phrased as a suggestion
    rather than a diagnosis, because no user arrives at it unaided.
  - **Independent of task 1, for the same reason task 3 states.** Until TLS is actually serving -
    not decided, serving - this bound is the user's only signal that the application has stopped
    working, and the interval between the decision and the deployment is unbounded. It is also the
    only piece here that helps if the cap is reached by something nobody has predicted.
  - _Requirements: 5.1, 5.2, 5.3_

- [x] 5. Optional: collapse the three per-tab streams
  - Files: `hierarchy.proto`, `context.proto`, `diagrams.proto` and their clients - a contract change
  - **Optional, because the production decision is TLS.** Under h2 the stream count stops being a
    correctness question, so this is robustness rather than the fix, and **it does not satisfy
    Requirement 1** (Requirement 3.2). Collapsing three to one buys six tabs instead of two rather
    than removing the limit, and the arithmetic must then be re-checked against every stream anyone
    adds afterwards.
  - The three are exhaustive today - `WatchHierarchy`, `ContextService/Watch`, `DiagramService/Open`
    are the only server-streaming RPCs the whole API declares - which is what makes "three per tab" a
    measured number rather than a survey.
  - Do not start this before tasks 1 to 4 are done. If it is never done, nothing in the
    specification is left unsatisfied.
  - _Requirements: 3.1_
