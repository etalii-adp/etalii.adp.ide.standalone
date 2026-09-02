# Requirements Document

## Introduction

This spec gives the repository its **first build pipeline**: a GitHub Actions workflow that builds and gates every change, computes the product's version automatically from git history via **Nerdbank.GitVersioning**, shows that version in the product itself (below the login panel), and publishes every release-worthy build to GitHub as a downloadable ZIP.

Today there is no CI at all — CLAUDE.md says so in as many words ("there is no CI here, so this is a step a person (or Claude) performs, not automation"), and the repository's gates (backend tests, `dotnet format`, client tests and typecheck) are run by whoever is merging. That works only as long as everyone always does it. There is also no version anywhere: no `version.json`, no stamped assemblies, no way for a user to say which build they are running, and no way to hand someone a build at all short of cloning the repository.

**Hard-won local knowledge the pipeline must not re-learn**, recorded here because each item cost a debugging session once:

* The test runner is **Microsoft.Testing.Platform** (xUnit v3): the suite runs as `dotnet test --solution EtAlii.Adp.slnx` from `src/backend` — the positional form is rejected — and `src/global.json` carries the runner opt-in.
* A **zero-test run exits non-zero** (5 for zero tests, 8 for a filter matching nothing) but prints no `failed`: a CI step must check **exit codes**, never grep output. A `Zero tests ran` summary means a broken build (historically: `MAX_PATH` swallowing the apphost), not an empty suite.
* The line-ending policy (`* text=auto eol=crlf` in `.gitattributes`) is machine-independent by design — it holds on a Linux runner exactly as on Windows, which is what keeps the byte-sensitive editor examples and fixtures honest in CI.
* The client's gates are `npm test` and `npm run typecheck` from `src/client` (installing from `src/`, the workspace root), each regenerating the proto stubs first via `buf`.

**Dependencies.** This spec redefines none of them:

* CLAUDE.md's gate definitions — the pipeline automates exactly the gates that exist, it does not invent softer ones.
* The client hosting seam (`AddClientAppHosting`/`MapClientApp`) — production mode serves the built client as static files, which is what makes a single-ZIP deliverable possible.
* The login page (`src/client/src/pages/LoginPage.tsx`) — the surface gaining the version line.

## Alignment with Product Vision

A tool whose product vision is *files are the source of truth* should hold itself to the same standard: the version is derived from the repository's own history rather than maintained by hand, the build is reproducible by a machine rather than a ritual, and the artifact anyone downloads is traceable to the exact commit that produced it.

## Requirements

### Requirement 1 — A GitHub Actions pipeline that runs the real gates

**User Story:** As a maintainer, I want every push and pull request built and gated automatically, so that a missed manual gate can no longer reach `develop` unnoticed.

#### Acceptance Criteria

1. WHEN a commit is pushed to `develop` or a pull request targets it THEN a GitHub Actions workflow SHALL build the backend and the client and run all four existing gates: `dotnet test --solution EtAlii.Adp.slnx` (from `src/backend`), `dotnet format style --verify-no-changes --severity info`, `npm test` and `npm run typecheck` (from `src/client`).
2. WHEN any gate fails THEN the workflow SHALL fail, judged by **exit codes** — a zero-test run (exit 5) fails the build as the broken build it is, and no step greps output for `failed`.
3. WHEN the workflow defines its steps THEN it SHALL run the same commands a person runs locally, so a green pipeline and a green local run mean the same thing; any deviation a runner forces (an environment variable, a long-path setting) SHALL be written into the workflow with a comment saying why.
4. WHEN the pipeline is in place THEN CLAUDE.md's "there is no CI here" passages SHALL be updated to describe the new reality — the manual gate discipline remains (worktrees still gate before merging), and the pipeline becomes the net under it rather than a replacement.

### Requirement 2 — Automatic versioning via Nerdbank.GitVersioning

**User Story:** As a maintainer, I want the version computed from git history, so that no human ever edits a version number and every build is traceable to its commit.

#### Acceptance Criteria

1. WHEN versioning is set up THEN a `version.json` SHALL define the version root for the repository, and **Nerdbank.GitVersioning** SHALL compute the full version (base version plus git height, with the commit id embedded) for every build, local and CI alike.
2. WHEN the backend builds THEN every C# assembly SHALL be stamped with the computed version through NB.GV's standard MSBuild integration — no `<Version>` property maintained by hand anywhere in the tree.
3. WHEN the client builds THEN the same computed version SHALL be available to it, by whatever carrier the design chooses (the backend as the runtime authority, or a build-time injection) — with **one source of truth**: the version the login panel shows, the version the assemblies carry, and the version the release ZIP is named after SHALL never be able to disagree.
4. WHEN a developer builds locally without any pipeline THEN the version SHALL still compute and show — a locally built product says what it is, including NB.GV's marker for non-release builds.

### Requirement 3 — The version shown below the login panel

**User Story:** As a user (or someone triaging a bug report), I want the product to say which version it is, so that "which build are you on?" has an answer anyone can read off the screen.

#### Acceptance Criteria

1. WHEN the login page renders THEN the computed version SHALL be shown directly below the login panel (the `auth-card` form), in a quiet, muted style that does not compete with the form — a single line such as the version number, styled through the centralized theme variables.
2. WHEN the version is displayed THEN it SHALL be the NB.GV-computed version from Requirement 2's single source of truth, not a string maintained in the client.
3. WHEN the version cannot be determined at runtime (the carrier fails, a dev-server session without the backend piece) THEN the line SHALL be omitted or show an honest placeholder — never a stale or invented number.

### Requirement 4 — Every release build published to GitHub as a ZIP

**User Story:** As anyone wanting to try ADP, I want to download a versioned ZIP from GitHub, so that running it does not require cloning and building the repository.

#### Acceptance Criteria

1. WHEN the pipeline succeeds on `develop` THEN it SHALL publish the build to GitHub as a **release with a ZIP asset**, tagged and named with the computed version — automatically, with no manual step.
2. WHEN the ZIP is assembled THEN it SHALL contain a runnable deployment: the published backend service with the production-built client in place as its static content (the existing production hosting mode), plus a short readme naming the version, the commit, and how to start it. Whether the payload is framework-dependent or self-contained, and for which platforms, is the design's decision — recorded there with its trade-off.
3. WHEN a pre-release or non-`develop` build runs THEN it SHALL NOT publish a release — pull requests build and gate only; publishing is the merge's reward.
4. WHEN publishing THEN the workflow SHALL use only the repository's built-in `GITHUB_TOKEN` — no personal tokens, no external secrets.
5. WHEN the same version would be published twice (a re-run of a published commit) THEN the workflow SHALL fail or skip loudly rather than overwrite a published asset in place.

## Non-Functional Requirements

### Code Architecture and Modularity
- The workflow lives in `.github/workflows/`, versioned like everything else; version knowledge lives in `version.json` and NB.GV's integration points, and nowhere else.
- The version's path to the login panel goes through an existing seam (the design chooses which), not a new parallel channel.

### Performance
- The pipeline should cache what is safe to cache (NuGet, npm) but never at the cost of correctness; a cache-poisoned green build is worse than a slow one.

### Security
- Only the built-in `GITHUB_TOKEN` with the minimal permissions the publish step needs (`contents: write`); gates run with read-only permissions.

### Reliability
- The pipeline must be honest about the known flaky teardown tests: if they are still flaky when this lands, the workflow documents the retry policy explicitly rather than silently re-running everything until green.

### Usability
- The release page's ZIP name, the tag, the login panel and the assemblies all read the same version — one build, one number, four places.
