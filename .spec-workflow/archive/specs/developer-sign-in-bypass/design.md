# Design Document

## Overview

**The backend hands a developer build a real session before the client decides what to render, and in a Release build the code that could do so does not exist.**

That is the whole design. The client never sees a form because it is already authenticated when `App` first asks; the session is a genuine one from the same store every other call is validated against, so nothing downstream can tell the difference; and the mechanism is excluded at **compile time**, so there is no setting to leave wrong in production.

**One measurement moved since the requirements were approved, and it moved a lot.** `tests.md` held 71 entries with **8** recorded pending on sign-in when Requirement 3 was written. It now holds **78 entries, of which 46 are recorded not-run citing sign-in** — the wall grew as other agents added checks that all hit it. Three more are not-run for unrelated reasons (two causal-loop checks and the jsdom viewport one) and remain outside this spec.

**So Requirement 3's "the eight blocked checks" is stale, and it is an acceptance criterion rather than a motivation figure.** This design does not silently reinterpret it as forty-six. It is flagged here, and the requirements want a one-line amendment saying "every entry recorded not-run for sign-in at the time of implementation" instead of a fixed count — which is the durable form, since the number will move again before this lands.

## Steering Document Alignment

### Technical Standards (tech.md)

- **The backend owns authentication; the client renders what it is told.** The bypass therefore lives in the backend and reaches the client as an ordinary session token, not as a client-side flag that skips a screen. A client that decided for itself whether to show a form would be a second authentication policy.
- **The contract is the boundary.** The bypass is one new RPC on an existing service, compiled out of a Release build along with its exemption.

### Project Structure (structure.md)

`src/api/authentication.proto`, `src/backend/EtAlii.Adp.Backend/Authentication/` and `Sessions/`, `src/client/src/auth/`, and one marker in the shell. No new project.

## Code Reuse Analysis

### Existing Components to Leverage

- **`SessionInterceptor.IsExempt`** (`SessionInterceptor.cs:54`) already exempts `/Login` and `/DescribeProduct` from session validation, with the reason written out: a page that renders before anyone can log in must be able to call something. The bypass RPC joins that list **and leaves it again in a Release build**.
- **`DescribeProduct`** is the precedent in shape as well as in exemption: it is the call the login page already makes before any session exists.
- **`AuthenticationService.Login`** (`AuthenticationService.cs:27`) is where a token is minted today, and the session store behind it is what the bypass reuses — the same issuance path, not a parallel one.
- **`AuthProvider`'s `tokenRef` and `isAuthenticated`** (`AuthContext.tsx:32-33`): the bypass sets exactly these two, which is why nothing else in the client changes.
- **`App.tsx:12`** — `if (!isAuthenticated) return <LoginPage/>` — is the single decision point the design must beat to the answer.

### Integration Points

- `AuthContext.test.tsx`'s guard that the provider hands out **one transport identity for its lifetime**. The bypass must not create a second transport; it fills the existing one's token earlier.
- The integration tests that authenticate with `admin` / `changeme`, which stay and keep the real path covered.

## Architecture

### The mechanism

```
client mount ──► AuthProvider effect ──► DeveloperSession (exempt RPC)
                                              │
                        Release build: the method does not exist ──► client falls through to LoginPage, as today
                                              │
                        Developer build: SessionStore issues a real token
                                              ▼
                        tokenRef set, isAuthenticated true, marker on
                                              ▼
                        App renders the workspace; LoginPage never mounts
```

**One new RPC, `DeveloperSession`**, on the existing `AuthenticationService`. It takes nothing and returns either a session token or nothing at all. It mints that token through **the same `SessionStore` path `Login` uses**, for the identity in `LocalAuthenticator.Username` — so the token is indistinguishable from one obtained by typing the credential, which is what Requirement 1.3 demands. A parallel issuance path would be a second authentication system, and the checks run against it would be evidence about that system.

**The client calls it once on mount, before first paint decides.** `AuthProvider` gains an effect that calls `DeveloperSession`; on a token it sets `tokenRef.current` and `isAuthenticated`. On anything else — a Release build where the method is absent, an error, a disabled bypass — it does nothing at all and the existing behaviour runs unchanged. **`App.tsx` is not touched**: it still renders `LoginPage` when not authenticated, and simply never gets the chance.

**Why not pre-fill the form** (Requirement 1.2): a filled form still has to be submitted by someone, and a filled form whose autofill failed is a form. The requirement is that no credential is typed by anyone, and only "the form never renders" achieves it.

### How it cannot reach a non-developer build

Three conditions, and **only the first is load-bearing**:

1. **Compile-time exclusion.** The `DeveloperSession` handler, its service registration, **and its entry in `IsExempt`** sit inside `#if DEBUG`. In a Release build the method is not in the assembly and the exemption is not in the list, so there is nothing to call and nothing to enable. This is what makes the bypass *absent* rather than *present-and-disabled* (Requirement 2.2).
2. **Host environment**, `IHostEnvironment.IsDevelopment()`, checked at the top of the handler. Belt for the case where someone ships a Debug build by accident.
3. **Configuration**, a value under `LocalAuthenticator`, checked last. This exists so a developer can turn the bypass *off* locally. **It can never turn it on** — a Release build has no handler for it to reach (Requirement 2.4).

**The exemption is the part reviewers should look at hardest**, harder than the handler. A handler that is compiled in but unexempt is unreachable without a session and therefore harmless; an exemption that survives into Release while the handler does not is also harmless. The dangerous combination is neither of those, but it is worth stating that **both** are inside the same `#if`, so they cannot drift apart.

### The guard, and why the obvious one is worthless

**A unit test asserting the bypass is disabled cannot fail in the build that runs it.** The test suite compiles Debug, where the bypass exists by design — so a test that asserts absence passes only by asserting the wrong thing, and a test that asserts "the flag defaults to off" is a test about a default rather than about a Release artifact. That is exactly the shape of guard this repository has been burned by: one that cannot fail for the right reason.

**The guard must therefore inspect a Release-compiled artifact**, and there is already a place that produces one: the release job publishes the service. The check is a step there — inspect the published assembly's metadata and fail if a `DeveloperSession` symbol is present. It fails loudly on the artifact a user would actually run, which is the only thing worth guaranteeing (Requirement 2.3).

Two supporting tests that *can* fail honestly in a Debug suite:

- `IsExempt` returns false for `DeveloperSession` when the bypass is configured off — pinning that condition 3 really is consulted.
- The bypass token is accepted by `SessionInterceptor` exactly as a `Login` token is — pinning Requirement 1.3, and it fails if the token is minted through a parallel path.

### What a bypassed session looks like

`DeveloperSession`'s response carries a flag the client keeps beside `isAuthenticated`, and the shell renders a quiet marker in the same register as the version line under the login card — present, unobtrusive, and **in every screenshot taken under the bypass** (Requirement 5.2). It is not decoration: it is what stops a screenshot from a developer build being read later as evidence about a released one.

The marker is derived from the **session**, not from a build constant, so a Release build cannot render it and a bypassed session cannot fail to.

## Data Models

No persisted change. One new response message carrying a session token and the bypass flag, and two client state values that already exist being set earlier.

## Error Handling

1. **The RPC is absent** (Release build): the client's call fails, the effect swallows it, `LoginPage` renders exactly as today. This is the normal production path and must be silent — a console error on every production start would be its own defect.
2. **The bypass is configured off**: the handler returns no token, same outcome as above.
3. **The environment is not Development**: same, and logged at Warning, because a Debug build running outside Development is worth knowing about.
4. **Token issuance fails**: no token, `LoginPage` renders, and the real sign-in still works — the bypass never leaves the app unusable.

## Testing Strategy

### Unit

`IsExempt` excludes `DeveloperSession` when the bypass is off; the issued token validates through `SessionInterceptor` identically to a `Login` token.

### Integration

The existing `admin` / `changeme` tests stay, unchanged, and remain the coverage of the real credential path (Requirement 4.1). One new test signs in the ordinary way **with the bypass compiled in**, proving the two paths coexist.

### Client

`AuthProvider` sets `isAuthenticated` from a bypass response without a second transport being created — reusing `AuthContext.test.tsx`'s existing invariant rather than restating it. And `LoginPage` still renders when the call returns nothing.

### The artifact guard

A step in the release job inspecting the published assembly, as above. **It must be seen to fail**: run it once against a deliberately Debug-published output, confirm it reports the symbol, then against the real Release output.

### The forty-six

They are the point, and they are not this spec's tests. Once the bypass lands, each entry's preconditions are rewritten and the check is **run**, its outcome recorded with the build it was observed on (Requirement 3.4).

## Sequencing

1. The contract and the handler, behind `#if DEBUG`, with its exemption in the same block.
2. The client effect and the marker.
3. The artifact guard in the release job — **before** anyone relies on the bypass, so the production question is answered while the change is still small.
4. The `tests.md` sweep, which is the largest piece and the reason for the other three.
