# Requirements Document

## Introduction

**A locally running developer build should not present a sign-in form.** Not because signing in is onerous, but because the one thing standing between this repository and 71 executable manual checks is an action no agent may perform: **entering a credential into a field**.

That constraint is not a preference and it is not this project's to lift. It sits in every agent's own instructions, it explicitly survives being authorised by the user, and `processes.md:128` now records that no repository document can override it. The user re-issued permission twice on 2026-09-04, and permission was never what was missing. **So the remedy is not to allow the action. It is to remove the need for it.**

That distinction decides the design, and it is the reason a narrower fix will not do: **a bypass that still presents a form and fills it in solves nothing**, because an agent that must click "Sign in" on a filled form is still driving a credential dialog, and an agent whose autofill silently fails is back where it started. The form must not appear.

### What is actually blocked, measured

Measured against `tests.md` on develop:

- **71 entries.**
- **8 recorded `pending` for exactly this reason at the time of writing** — a figure that has since been measured as 39, 43, 44, 45 and 46 by different readings, which is why Requirement 3 was amended to a rule rather than a count — four `owl-diagram` task 4.3 checks, three `shacl-diagram` task 4.2 checks, and `view-delta-adoption` task 11's "Panning a large ontology brings its content in". Their text says so plainly: *"the app opens on a sign-in form, and entering a credential into a login field is outside what the implementing agent may do — even with the user's permission."*
- **1 more is pending for an unrelated reason** (a jsdom viewport), which is worth separating so this spec is not credited with unblocking it.

**How many of the 71 carry a recorded outcome cannot be answered exactly, and that is a second finding.** A strict count of outcome markers gives 12; a loose count including any dated line gives 29. The entries have no consistent marker, so the completion of this document cannot be computed — only estimated. That is out of scope here, but it should not be lost: a manual-test register nobody can count is one nobody can report on.

### What it costs, and why the prize is larger than eight checks

The two most valuable defects found on 2026-09-04 — a C4 relationship rendered with no click handler, and a wardley canvas importing nothing from the context channel — were both found **by someone using the application**, and both were larger than the specifications that surfaced them. Neither was reachable by a unit test, because both were about what the running app does rather than what a function returns. With no form, all 71 entries become executable unattended rather than queued behind a person.

## Alignment with Product Vision

The product's own premise is that *files are the source of truth* and that the application is the thing a person uses to look at them. A check that cannot be run is a claim about the product that nobody has tested — and this repository already prefers a measured answer to a confident one. This spec buys the ability to measure.

## Requirements

### Requirement 1 — A developer build opens already authenticated, with no form

**User Story:** As anyone — agent or person — running a local developer build, I want the application to open on the workspace rather than on a sign-in form, so that using it requires no credential entry at all.

#### Acceptance Criteria

1. WHEN a developer build starts and the bypass is active THEN the application SHALL open **already authenticated** as the developer identity, and the sign-in form SHALL NOT be rendered at any point in that session.
2. WHEN the bypass is active THEN it SHALL NOT be implemented by pre-filling or auto-submitting the existing form — **the form must not appear**, because a filled form still requires the action this spec exists to remove, and a failed autofill returns to it.
3. WHEN the application opens under the bypass THEN the session SHALL be indistinguishable from a signed-in one **to the rest of the application**: the same identity, the same transport, the same authorisation on every call. A bypass that produces a subtly different session would make every check run under it untrustworthy.
4. WHERE the identity is concerned THEN it SHALL be the existing developer credential's identity (`LocalAuthenticator.Username`, `admin` in `appsettings.developer.json`), not a new or anonymous principal, so what a check exercises is what a person would exercise.

### Requirement 2 — It cannot reach a non-developer build

**User Story:** As the person who owns this repository, I want it to be impossible — not merely unlikely — for a shipped build to skip authentication, so that a development convenience is never a production hole.

**This is the requirement a reviewer should scrutinise hardest, and the one this spec would deserve to be rejected over.** A sign-in bypass is, described plainly, an authentication bypass. What makes it acceptable is that it cannot exist outside a developer build; what would make it unacceptable is a switch that merely defaults to off.

#### Acceptance Criteria

1. WHEN the bypass is gated THEN it SHALL be excluded by **the build or the host environment**, not by a configuration value alone. A setting nobody sets is one somebody eventually sets, and a config file is copied between environments far more often than a compilation is.
2. WHEN a Release or non-development build is produced THEN the bypass SHALL be **absent from it** rather than present-and-disabled, and a test SHALL demonstrate that absence rather than asserting the flag's default.
3. WHEN the published release artifact is inspected THEN it SHALL contain no path by which authentication can be skipped — this is checkable, and it connects to the release work already landed: the ZIP is what a user outside this repository actually runs.
4. IF a configuration value participates at all THEN it SHALL be an **additional** condition and never a sufficient one, so that setting it in a production `appsettings.json` achieves nothing.
5. WHEN the guard is written THEN it SHALL be seen to fail against a build with the bypass wrongly enabled, because a guard never observed failing is an assertion about itself.

### Requirement 3 — Every check blocked on sign-in becomes runnable, and is then run

**User Story:** As someone reading `tests.md`, I want an entry to describe a check that can actually be performed, so the register records evidence rather than intentions.

**Amended 2026-09-05: this requirement no longer names a count.** It said "the eight blocked checks", which was measured and true when written. The reason for the change is not that eight became forty-something — it is that **the number depends on who counts**. Six figures were produced within a day by people all counting carefully: 8, 39, 43, 44, 45, 46. None was wrong. Each answered a slightly different question — headings versus `Result` lines, entries naming sign-in explicitly versus inheriting it through "same reason as above", entries whose note says the app was not run for this pass, annotations rather than entries.

A count in an acceptance criterion is therefore **doubly unusable**: stale by the time it is read, and disputable by whoever reads it. The rule below is checkable at the only moment that matters, which is when the work is done.

#### Acceptance Criteria

1. WHEN the bypass exists THEN **every entry in `tests.md` recorded not-run because of sign-in, as measured at the time of implementation**, SHALL have its preconditions rewritten, since each currently begins at a form that will no longer appear.
2. WHERE an entry inherits its blockage indirectly — "same reason as above", or a note that the application was not run for that pass — THEN it SHALL be counted as blocked on sign-in. **The set is defined by what stopped the check, not by whether the entry spells out the word.**
3. WHEN preconditions are rewritten THEN the checks SHALL be **executed and their outcomes recorded** — including "unchanged" or "still fails", which are results. Marking a check runnable and leaving it unrun moves the blockage rather than removing it.
4. WHERE an entry carries a note that a unit test covers it meanwhile THEN that note SHALL NOT retire the manual check. These entries exist because the bug class is invisible to jsdom, and CLAUDE.md is explicit on the point; they are the ones most at risk of being quietly dropped while somebody triages forty of them at once.
5. WHERE an entry is not-run for a reason **other** than sign-in THEN it SHALL be left alone and this spec SHALL NOT claim it — at the time of writing that is two causal-loop checks and one jsdom viewport check.
6. WHEN an outcome is recorded THEN it SHALL say which build it was observed on, because a check run under the bypass is evidence about a developer build and not about a released one.

### Requirement 4 — The credential path stays exercised

**User Story:** As a maintainer, I want the real sign-in to keep working and keep being tested, so that removing the need to use it does not quietly remove the ability to.

If nothing signs in any more, the sign-in code stops being covered, and the first person to discover it broke will be a user rather than a test.

#### Acceptance Criteria

1. WHEN the bypass lands THEN the existing integration tests that authenticate with `admin` / `changeme` SHALL be **kept** and SHALL continue to exercise the real credential path end to end.
2. WHEN the bypass is inactive — every non-developer build, and a developer build with it turned off — THEN the sign-in form SHALL behave exactly as it does today, including its failure messages.
3. WHEN this spec is implemented THEN no authentication code SHALL be deleted as newly-unreachable; the bypass adds a path, it does not replace one.
4. WHERE `AuthContext.test.tsx` guards that the provider hands out **one transport identity for its lifetime** THEN that invariant SHALL survive unchanged — the bypass supplies an identity earlier, not a second one.

### Requirement 5 — A bypassed session says so

**User Story:** As someone looking at a running application, I want to be able to tell that it did not ask anyone to sign in, so I never mistake a developer build for a real one.

#### Acceptance Criteria

1. WHEN the application is running under the bypass THEN it SHALL be visibly apparent — a marker in the shell, in the same quiet register as the version line below the login panel.
2. WHEN a screenshot or a recorded check is taken under the bypass THEN that marker SHALL be present in it, so evidence carries its own provenance and a later reader cannot mistake the conditions.
3. WHERE the marker is placed THEN it SHALL NOT obscure or alter the surface under test, since the checks it accompanies are about that surface.

## Non-Functional Requirements

### Security

- The bypass is a **development affordance, not a feature**. It exists so that a prohibited action is never required; it must never become a way to avoid a required one. Requirement 2 is where that is enforced, and it is enforced by the build rather than by discipline.
- Nothing in this spec changes what a **released** build does. If the answer to "what does this do in production" is anything other than "it is not there", the design is wrong.

### Reliability

- A check run under the bypass must exercise the same code as a check run after signing in. If the two diverge, every outcome recorded under the bypass is evidence about the bypass rather than about the product.

### Usability

- The affordance serves people as well as agents: a developer restarting the app forty times in an afternoon also does not want to sign in forty times. It should not be built as agent scaffolding.

## Out of Scope

- **Changing what an agent is permitted to do.** The constraint is platform-level, it survives user authorisation, and no document in this repository can lift it. This spec routes around the need; it does not touch the rule, and any design that reads as an attempt to would be the wrong shape.
- **The credential itself.** `admin` / `changeme` in `appsettings.developer.json` is a checked-in developer placeholder and stays exactly as it is.
- **The 42 entries with no recorded outcome that are not blocked on sign-in.** Their blockage, where they have one, is elsewhere. Only the eight are this spec's to unblock.
- **Fixing `tests.md`'s missing outcome convention.** Recorded in the Introduction because it was measured on the way past; it wants its own decision, not a rider on this one.
