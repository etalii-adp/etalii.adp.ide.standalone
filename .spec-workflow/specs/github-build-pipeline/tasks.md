# Tasks Document

Grouped the way the repository works them: each group in one worktree, gated with the four existing gates before merging into `develop`. The workflow itself can only be proven by GitHub running it, so group 4 ends with a push-and-watch step rather than a local gate alone.

- [ ] 1. Versioning: Nerdbank.GitVersioning wired in
  - Worktree: `.claude/worktrees/pipeline-version`
  - _Requirements: 2.1, 2.2, 2.4_

- [ ] 1.1 `version.json` and the central package reference
  - Files: `version.json` (new, repository root), `src/Directory.Build.props`, `src/Directory.Packages.props`
  - `version.json`: an initial `0.1-alpha` version with `publicReleaseRefSpec` naming `develop`, per the design; the NB.GV package referenced once in `Directory.Build.props` with `PrivateAssets="all"`, its version pinned centrally
  - Verify locally: `dotnet build` any backend project and confirm the produced assembly's `AssemblyInformationalVersion` carries semver+height+commit; a worktree build must work too (NB.GV supports worktrees, but this repository lives in them - measure, don't assume)
  - _Requirements: 2.1, 2.2, 2.4_
  - _Prompt: Implement the task for spec github-build-pipeline, first run spec-workflow-guide to get the workflow guide then implement the task: Role: .NET build engineer familiar with Nerdbank.GitVersioning | Task: Add version.json at the repository root (version 0.1-alpha, publicReleaseRefSpec for refs/heads/develop) and reference Nerdbank.GitVersioning once in src/Directory.Build.props (version pinned in src/Directory.Packages.props, PrivateAssets=all), so every C# assembly is stamped with the computed AssemblyInformationalVersion on every build, local and CI alike | Restrictions: no per-project version properties anywhere; do not break the git-worktree build - verify a build from a .claude/worktrees checkout stamps correctly before calling this done | _Leverage: src/Directory.Build.props and src/Directory.Packages.props central-management pattern | Success: dotnet build stamps semver+height+commit into AssemblyInformationalVersion, from the main checkout and from a worktree, with no hand-maintained version anywhere. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 1.2 Gate and merge
  - Run all four gates (backend test + format from `src/backend`, npm test + typecheck from `src/client`), exit codes checked; merge `pipeline-version` into `develop`, retire the worktree
  - _Requirements: (gate only)_
  - _Prompt: Implement the task for spec github-build-pipeline, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Release engineer | Task: Run dotnet test --solution EtAlii.Adp.slnx and dotnet format style --verify-no-changes --severity info from src/backend, and npm test/npm run typecheck from src/client, checking all exit codes; if clean, merge the worktree into develop and retire it | Restrictions: do not merge on a failing gate | Success: all gates pass, worktree merged and retired. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 2. The version wire: DescribeProduct
  - Worktree: `.claude/worktrees/pipeline-wire`
  - _Requirements: 2.3, 3.2_

- [ ] 2.1 Proto and backend handler
  - Files: `src/api/authentication.proto`, the `AuthenticationService` backend implementation, `src/backend/EtAlii.Adp.Backend/Sessions/SessionInterceptor.cs`
  - Add `rpc DescribeProduct(DescribeProductRequest) returns (DescribeProductResponse)` with `string version = 1`; the handler reads the executing assembly's `AssemblyInformationalVersionAttribute` once and caches it; `SessionInterceptor` exempts `/DescribeProduct` beside `/Login`, with a comment saying why
  - Tests: a handler unit test (non-empty, informational-version-shaped answer); an interceptor test pinning that `/DescribeProduct` passes without a token while other calls still refuse; a `WebApplicationFactory` fact calling it over an unauthenticated channel
  - _Requirements: 2.3, 3.2_
  - _Prompt: Implement the task for spec github-build-pipeline, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Senior C# gRPC developer | Task: Add the DescribeProduct unary to AuthenticationService per the design - proto message pair, handler reading the cached AssemblyInformationalVersion, SessionInterceptor exemption beside /Login with an explanatory comment - plus the three tests the design names (handler unit, interceptor pin, unauthenticated integration call) | Restrictions: no new service and no REST side-channel; the version string comes from the stamped assembly, never from configuration | _Leverage: SessionInterceptor's existing /Login exemption; the WebApplicationFactory harness in EtAlii.Adp.Backend.Tests | Success: an unauthenticated channel gets the stamped version; a tokenless call to any other method still refuses; all tests green. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 2.2 Gate and merge
  - The proto changed, so the client gates regenerate stubs; run all four gates, merge `pipeline-wire` into `develop`, retire the worktree
  - _Requirements: (gate only)_
  - _Prompt: Implement the task for spec github-build-pipeline, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Release engineer | Task: Run all four gates with exit codes checked (the client gates regenerate proto stubs; commit the genuinely changed generated file, excluding line-ending-churn-only files per this repository's practice), merge the worktree into develop, retire it | Restrictions: do not merge on a failing gate | Success: all gates pass, worktree merged and retired. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 3. The login panel's version line
  - Worktree: `.claude/worktrees/pipeline-login`
  - _Requirements: 3.1, 3.2, 3.3_

- [ ] 3.1 LoginPage calls DescribeProduct and renders the line
  - Files: `src/client/src/pages/LoginPage.tsx`, its test, the page's stylesheet
  - On mount, call `describeProduct` fire-and-forget into state; render `<p className="auth-version">` below the `auth-card` form only when a version arrived; style centered/small/`--color-text-muted` through the centralized variables, no literal colors
  - Tests: version shown when the mocked call answers; nothing rendered when it fails or answers empty (R3.3)
  - Manual check: add the four-places-one-number entry to `tests.md` per the design's End-to-End strategy
  - _Requirements: 3.1, 3.2, 3.3_
  - _Prompt: Implement the task for spec github-build-pipeline, first run spec-workflow-guide to get the workflow guide then implement the task: Role: React developer familiar with this client's auth pages | Task: Show the DescribeProduct version below the login form per the design - fire-and-forget on mount, .auth-version styled through the centralized theme variables, omitted entirely on failure or empty answer - with the two component tests and the tests.md end-to-end entry | Restrictions: no version string maintained in the client; no literal colors; the line must not shift the form's layout when absent | _Leverage: LoginPage.tsx's existing auth-card structure and stylesheet; the client's existing service-mocking test patterns | Success: version renders when answered, nothing renders when not, tests green, tests.md entry added. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 3.2 Gate and merge
  - Run all four gates; merge `pipeline-login` into `develop`, retire the worktree
  - _Requirements: (gate only)_
  - _Prompt: Implement the task for spec github-build-pipeline, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Release engineer | Task: Run all four gates with exit codes checked; if clean, merge the worktree into develop and retire it | Restrictions: do not merge on a failing gate | Success: all gates pass, worktree merged and retired. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 4. The workflow itself, and the docs that stop lying
  - Worktree: `.claude/worktrees/pipeline-workflow`
  - _Requirements: 1.1-1.4, 4.1-4.5_

- [ ] 4.1 `.github/workflows/build.yml`
  - Files: `.github/workflows/build.yml` (new)
  - The design's two jobs verbatim: gates on push-to-develop and PRs (checkout `fetch-depth: 0`, setup-dotnet honouring `src/global.json`, setup-node, NuGet/npm caching, `npm ci` at `src/`, the four gate commands as their own exit-code-judged steps, no retries); release only on push to develop (`permissions: contents: write`, `nbgv get-version`, the tag-exists loud-fail guard, client `npm run build`, `dotnet publish` framework-dependent, `dist/` into `publish/wwwroot/`, the readme naming version/commit/how-to-run, `EtAlii.Adp-$V.zip`, `gh release create v$V`)
  - _Requirements: 1.1, 1.2, 1.3, 4.1, 4.2, 4.3, 4.4, 4.5_
  - _Prompt: Implement the task for spec github-build-pipeline, first run spec-workflow-guide to get the workflow guide then implement the task: Role: GitHub Actions engineer | Task: Write .github/workflows/build.yml exactly as the design specifies - the gate job running the repository's four real gate commands from their real directories judged by exit codes, and the develop-only release job with the nbgv version, duplicate-tag guard, framework-dependent publish with the client dist as wwwroot, readme, and gh release create using only the built-in GITHUB_TOKEN | Restrictions: no third-party release actions; no output grepping; no automatic retries - a flake fails visibly; any runner-forced deviation from the local commands gets an explaining comment | _Leverage: the gate commands and directory layout exactly as CLAUDE.md records them; gh preinstalled on runners | Success: the YAML validates, encodes every design decision, and runs the identical commands a person runs locally. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 4.2 CLAUDE.md tells the truth again
  - Files: `CLAUDE.md`
  - Rework the two "there is no CI here" passages per the design: the pipeline is the net, the local worktree-gate discipline stands, the exit-code lesson now points at the workflow as its embodiment
  - _Requirements: 1.4_
  - _Prompt: Implement the task for spec github-build-pipeline, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Technical writer familiar with this repository's CLAUDE.md voice | Task: Update the passages that say no CI exists (Backend code style, Running the backend tests) to describe .github/workflows/build.yml as the enforcement net while keeping the local gate-before-merge discipline and the zero-tests-exit-code lesson intact | Restrictions: do not weaken any local discipline; do not rewrite unrelated sections | _Leverage: the existing CLAUDE.md prose style | Success: a reader of CLAUDE.md learns both that CI enforces the gates and that they still gate locally before merging. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._

- [ ] 4.3 Gate, merge, push, and watch the first run
  - Run all four local gates; merge `pipeline-workflow` into `develop` and retire the worktree; then - with the user's go-ahead, since nothing has been pushed for a while - push `develop` and watch the first Actions run: the gate job must go green, and the release job must publish `v0.1.x` with its ZIP; download that ZIP, run it, and confirm the login panel shows the release's own version (the tests.md entry from task 3.1, executed once for real)
  - _Requirements: 1.1, 4.1, Non-Functional Usability_
  - _Prompt: Implement the task for spec github-build-pipeline, first run spec-workflow-guide to get the workflow guide then implement the task: Role: Release engineer | Task: Run the four local gates and merge as usual; then coordinate with the user to push develop (many local commits are unpushed - the push is theirs to authorize), watch the first workflow run to green, confirm the release and ZIP appear, and execute the tests.md four-places-one-number check against the downloaded ZIP | Restrictions: do not push without the user's explicit go-ahead; a red first run is fixed forward with ordinary commits, never by force-push | Success: gates green locally and on GitHub, a versioned release with a runnable ZIP exists, and the ZIP's login panel shows the matching version. Mark this task in-progress in tasks.md before starting, log the implementation with log-implementation when done, then mark it complete._
