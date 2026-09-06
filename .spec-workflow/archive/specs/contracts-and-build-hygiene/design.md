# Design Document

## Overview

Three pieces of work that share a spec but not a schedule.

**Piece 1 — the release job — is independently landable and should land alone.** It is three edits to one file, `.github/workflows/build.yml`, touching no C#, no TypeScript and no contract. 157 commits are unpushed because every push to develop currently mints another misattributed tag, so this piece is what unblocks the release path, and putting it in front of the user as a one-file change is worth more than bundling it with a naming sweep it has nothing to do with. **Nothing in pieces 2 or 3 is a prerequisite for it, and it is not a prerequisite for them.**

**Piece 2 — the naming alignment** — is a rename, mechanical and wide: it touches contracts, generated code on both sides, and 26 client interfaces. It is safe precisely because it is only a rename, and the design keeps it that way by separating the one member of it that is a behaviour change.

**Piece 3 — the catalog vocabulary** — is not a code change at all until a question is answered, and the design's job here is to put the question well rather than to answer it.

The smaller findings — dead contract vocabulary, publish-path separators, the warning policy, the screenshots — attach to whichever piece they naturally belong to and are called out below.

## Steering Document Alignment

### Technical Standards (tech.md)

- **The contract is the boundary and the source of truth.** Piece 2 makes that literal: where an implementation type and a wire message disagree about a name, the wire wins. The one exception is stated as a *question*, not a list.
- **Judge a gate by its exit code.** The release job's new checks follow the same rule the gate job already does: each is its own step, failing loudly, with no output grepping deciding anything.
- **No third-party actions.** The fixes use `git`, `gh` and workflow syntax already present. Nothing is added to the supply chain.

### Project Structure (structure.md)

Piece 1 touches `.github/workflows/build.yml` only. Piece 2 touches `src/api/*.proto`, `src/diagrams/*/api/*.proto`, `src/backend/**/_Model/`, and `src/diagrams/*/client/*Model.ts`. Piece 3 touches `docs/diagrams.md` and, if a state is added, `CLAUDE.md` — which is the user's document, so the design proposes and does not edit.

## Code Reuse Analysis

### Existing Components to Leverage

- **The duplicate-tag guard at `build.yml:105`** — the shape is right (a `git ls-remote` check, a loud `::error::`, `exit 1`); only its question is wrong. Requirement 2 adds a second question to the same step rather than a competing mechanism.
- **`actions/checkout@v5` with `fetch-depth: 0`**, already present in both jobs, is what makes a commit-level tag check possible at all: the tags and the full history are already there.
- **`nbgv get-version --variable SemVer2`**, already computing the version. Nothing about versioning changes.
- **`docs/screenshots/capture.mjs`** and its readme, the recorded procedure for the screenshot check.
- **`ExampleRegistrationTests`'s walk of the deployed catalog** is the precedent for deriving documentation claims from the tree rather than asserting them by hand — the model for a catalog guard, if piece 3 gets one.

### Integration Points

- The release job's `Publish the release` step (`build.yml:150-157`) and `Refuse to republish an existing version` (`:103-108`).
- `tests.md`'s "One build, one number, four places" entry, whose manual comparison piece 1 makes structural.

## Architecture

### Piece 1 — the release job

Three defects, three edits, one file. They are separable but should land together: each one alone leaves a way for a tag to be wrong.

#### 1a. The tag names the commit it was built from

```
      - name: Publish the release
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          gh release create "v${{ steps.version.outputs.semver }}" \
            "EtAlii.Adp-${{ steps.version.outputs.semver }}.zip" \
            --target "${{ github.sha }}" \
            --title "EtAlii.Adp ${{ steps.version.outputs.semver }}" \
            --generate-notes
```

One flag. Without it `gh release create` resolves the tag against the repository's **default branch at API time**, which is `develop` — so on a quiet repository the tag happens to be right, and on a busy one it lands wherever the branch has got to. That is the whole mechanism behind three tags on `f5a755c1` and a descendant carrying a lower version than its own ancestor.

#### 1b. The guard asks about the commit as well as the version

The existing step keeps its question and gains a second one:

```
      - name: Refuse to republish
        run: |
          semver="${{ steps.version.outputs.semver }}"
          if git ls-remote --tags origin "refs/tags/v$semver" | grep -q .; then
            echo "::error::v$semver is already published - not overwriting a released asset."
            exit 1
          fi
          existing=$(git ls-remote --tags origin | grep "^${{ github.sha }}" || true)
          if [ -n "$existing" ]; then
            echo "::error::${{ github.sha }} is already released as: $existing"
            exit 1
          fi
```

**One detail decides whether the second check works at all.** A tag created by `gh release create` may be annotated, in which case `refs/tags/v1` names a *tag object* and not the commit — a comparison against `github.sha` would miss it and the guard would pass on exactly the case it exists to catch. `git ls-remote --tags origin` with no refspec lists the peeled `refs/tags/v1^{}` entry alongside it, so matching the sha anywhere in that output covers annotated and lightweight tags alike. This is the kind of check that must be seen to fail before it is believed; the testing strategy says how.

#### 1c. Two runs cannot race

```
concurrency:
  group: release-${{ github.ref }}
  cancel-in-progress: false
```

`cancel-in-progress: false` is deliberate: a release job cancelled between `gh release create` and its asset upload leaves a tag with no ZIP, which is worse than a queued run. Serialising rather than cancelling costs a little runner time and cannot produce a half-release.

Serialisation alone still lets a queued run publish for a commit that is no longer the tip, which Requirement 3.2 forbids. A step decides that once the lock is held:

```
      - name: Skip a superseded commit
        id: tip
        run: |
          tip=$(git ls-remote origin refs/heads/develop | cut -f1)
          if [ "$tip" != "${{ github.sha }}" ]; then
            echo "::notice::${{ github.sha }} is no longer the tip of develop ($tip); not publishing."
            echo "publish=false" >> "$GITHUB_OUTPUT"
          else
            echo "publish=true" >> "$GITHUB_OUTPUT"
          fi
```

with `if: steps.tip.outputs.publish == 'true'` on the publishing steps that follow.

**The trade-off, stated because it is a behaviour change and not a bug fix.** A burst of five pushes now produces **one** release rather than five. That is what Requirement 3.2 asks for and it is the right default for a repository where a dozen agents push through the day — but it does mean an intermediate commit gets no release of its own, and anyone who wants one must push it alone or tag it by hand. The run is **green**, not failed: being superseded is not an error, and a red run would train everyone to ignore red runs.

#### 1d. The check that proves the fix, in the job itself

After publishing, the job verifies what it just did:

```
      - name: The tag names the commit it was built from
        run: |
          tagged=$(git ls-remote origin "refs/tags/v${{ steps.version.outputs.semver }}" | cut -f1)
          test "$tagged" = "${{ github.sha }}" || {
            echo "::error::v${{ steps.version.outputs.semver }} points at $tagged, not at ${{ github.sha }}."
            exit 1
          }
```

This is the structural half of `tests.md`'s "one build, one number, four places": two of the four places are now compared by the pipeline on every release, so the manual check shrinks to the two that need a person — the ZIP's name and the login line inside it.

#### 1e. The four misattributed tags — the user's decision, not this design's

`v0.1.402-alpha` is correct and stays. The other four are wrong, and this design presents the options rather than choosing:

| option | what it costs | what it keeps |
|---|---|---|
| **Leave them** | The tag history stays wrong, and anyone reading `v0.1.476-alpha` gets the wrong source. | Every published download URL keeps working; nothing anyone has bookmarked breaks. |
| **Delete the three duplicates on `f5a755c1`, keep `v0.1.469-alpha`** | Three download URLs stop working. | The remaining tags are one-per-commit, even if 469's commit is still not the one it was built from. |
| **Delete all four and re-release from the current tip** | Four download URLs stop working; the version numbers 461-476 are spent and will not recur. | The tag list means exactly what it says from that point on. |

Deleting a GitHub release deletes its assets and its URL — that is why this is not an implementation detail. **The design's recommendation, offered as one: delete all four.** They are alpha releases days old, the misattribution is not recoverable by any amount of explanation, and a tag that points at the wrong source is worse than a missing one because it looks like an answer.

### Piece 2 — one name per concept

The approved requirement settled the rule as a **question**: *does this type represent the wire data?* Decoded from a payload, or built in order to become one — the contract names it. Parsed from a document its module owns — the module names it. The design's job is to say where that question is written down and in what order the answers land.

**Where the question lives.** In `docs/creating-a-diagram-module.md`, beside the seams a module author is already reading when they create these very types, and referenced from `src/api/readme.md` if one exists or the contracts' own header comment if not. Not in a spec: a spec is read once, and this is a question asked every time someone adds a message.

**The three groups, in landing order.**

1. **Family C — the eight core divergences** (`ContextAction`/`ContextActionDefinition`, `ContextOption`/`ContextOptionNode`, `ContextTextField`/`ContextTextFieldRequest`, `Entry`/`EntryNode`, and four more). These exist only to become the wire message they are named after, so the contract names all eight. One suffix is chosen for the whole family; **`Definition` is already five of the eight**, so it is the incumbent and the other three converge on it — unless the user prefers the bare contract name with no suffix at all, which the design notes as the alternative because it is what "the contract is the source of truth" most literally means.
2. **Family B — the four `…Proto` suffixes**. `C4ElementKindProto` is deleted by Requirement 4 rather than renamed. The other three (`PipelineEdgeConditionProto`, `ShaclTargetKindProto`, `ShaclTargetChipProto`) lose the suffix in the contract, and the C# side takes whatever the family-C suffix decision produces.
3. **Family A2 — the 26 client interfaces**. `WardleyElement`, `TimelineConnection`, `SkosConcept` and 23 more take the contract's name. This is the widest edit and the least risky: they are interface declarations and their use sites, with the compiler finding every one.

**What must not travel with the rename.** `EntryNode`'s `bool IsFolder` against the contract's `EntryKind kind` is a **behaviour change**, not a rename: it makes a third entry kind representable where today it would silently read as "not a folder". It lands as its own commit, with its own test, after the renames — mixing it in would put a semantic change inside a diff that reviewers are skimming precisely because it is mechanical.

**Sequencing against the rest of the board.** These renames touch files that other agents hold. The design asks for the same treatment `folder-add-registration` got: the contract-and-backend halves land when the core is quiet, and the 26 client interfaces land per module or in one sweep when the view-delta adoptions are done, whichever the scrum master sequences. Nothing here is urgent; all of it is easy to redo if it conflicts.

### Piece 3 — the catalog, and the question under it

The rows are not the defect. `docs/diagrams.md` marks 64 rows 💡 Identified — "no spec yet" — while `EtAlii.Adp.Backend.Service` references **every** module by glob, so all ~62 registered definitions are discovered and offered in the Add dialog, including ~50 that are a title, a description and an icon with no parser behind them. There is **no state in the vocabulary that means "registered, offered, not implemented"**, so no row can be written that is both accurate and available. Correcting rows without correcting the vocabulary moves the lie rather than removing it.

Two questions, and the design deliberately answers neither:

**Question 1 — what should a registered placeholder's state be called?** Options: a new state (⚪ Registered, say) sitting between 💡 Identified and ⚗️ Prototype; or reuse ⏸️ To-do, which is currently unused by any row and whose legend text would have to change; or drop the placeholder definitions entirely so the catalog's 💡 becomes true again. Each is a different answer to what the catalog is *for*.

**Question 2 — should ~50 unimplemented types be offered in the Add dialog at all?** A user choosing `uml/class` today gets a registration for a type nothing can open. That is a product question this scan is not entitled to answer, and it may well be the real defect with the catalog merely reporting it.

**What the design does commit to**, whichever answer wins: the catalog's states become **derivable from the tree**, and a test derives them — the `ExampleRegistrationTests` precedent. A catalog checked by hand drifts again the week after it is corrected, and this one has drifted to 64 wrong rows.

### The smaller findings

- **Dead contract vocabulary** (Requirement 4) travels with piece 2: `C4ElementKindProto` and its nine values are deleted, `ContextSelectionAction.RENAME_REQUEST` and `FOCUS_ONLY` are deleted, and each field number is `reserved` so no later message can reuse it. The two `*_UNSPECIFIED = 0` values are **not touched** — proto3 wants them.
- **Publish-path separators** (Requirement 8) is a three-line change to the service csproj, verified by publishing and looking at the output directory rather than by building. It can ride with piece 1, since both are release-path work and both are verified the same way.
- **The warning policy** (Requirement 9) is a decision to record, not code to write: the design asks for the current warning count to be measured *first*, because promoting an unknown number of warnings turns one decision into an unbounded task.
- **Screenshots** (Requirement 10) is one pass with `capture.mjs`, recording "unchanged" where that is the answer — which it will be for most.

## Data Models

Nothing persisted changes anywhere in this spec. Piece 1 changes no data at all. Piece 2 changes type *names* and, in exactly one place, a type: `EntryNode`'s `bool IsFolder` becomes the contract's `EntryKind`. Piece 3 changes a documentation table and possibly a legend.

## Error Handling

### Error Scenarios

1. **A commit is already released** — the guard fails the release job loudly, naming the tag that already points there. The gate job's verdict is unaffected, as today.
2. **A version string is already published** — the existing refusal, unchanged.
3. **A run is superseded** — a `::notice::`, the publishing steps skipped, the run **green**. Being overtaken is not a failure.
4. **The post-publish verification disagrees** — the job fails after publishing, which is deliberate: the release exists and is wrong, and a red run is how anyone finds out. The alternative, a silent mismatch, is what produced the four bad tags.
5. **A rename breaks a use site** — the compiler and `npm run typecheck` catch it; there is no runtime failure mode for a rename.

## Testing Strategy

### The release job

A workflow cannot be unit-tested here, and the repository is private so its runs are unreadable from a development machine — no `gh`, no token, and the unauthenticated API answers `Not Found`. So the checks are designed to be **verifiable from their own output**:

- The commit-level guard is proven by pushing a commit that is already released and watching the job fail with that tag named. Until such a push happens naturally, the equivalent can be run locally: `git ls-remote --tags origin | grep "^<sha>"` against a known-released sha must print, and against an unreleased one must not.
- The post-publish verification is proven by the next successful release: its tag must point at its own commit, which `git ls-remote` shows from any machine.
- **The annotated-tag subtlety must be checked, not assumed.** Before trusting the guard, confirm against the existing tags whether `gh release create` produced annotated or lightweight ones, and confirm the `ls-remote` output form covers what it actually produced.

### Piece 2

Renames are proven by the four gates: `dotnet test`, `dotnet format style --verify-no-changes --severity info`, `npm test`, `npm run typecheck`, each judged by an exit code captured before any pipe. The `EntryNode` change gets its own test asserting a non-folder, non-file kind survives the round trip rather than reading as "not a folder".

### Piece 3

If a state is added, a test derives every row's state from the deployed catalog and fails naming any row that disagrees.

## Sequencing

1. **Piece 1, alone, first.** One file, three edits plus a verification step, no dependency on anything else in this spec. This is what unblocks 157 commits.
2. **The four bad tags**, once the user has chosen; independent of everything.
3. **Piece 2**, when the core and the client are quiet enough for a wide rename, with `EntryNode`'s behaviour change as its own commit afterwards.
4. **Piece 3**, when the two questions have answers. Not before: a corrected row under an uncorrected vocabulary is a lie in a new place.
