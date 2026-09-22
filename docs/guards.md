# The guards, and what each one answers

**A guard nobody can find only speaks after the mistake.** On 2026-09-22 three of these caught something nobody was looking for — a host-booting test class that would have written into the developer's real profile, a raw file write that had been invisible to the rule meant to catch it, and a test-support exclusion that had regressed. Each was found by a guard, not by review. **So this page exists to make the set findable before the next mistake, not after it.**

**Read the third column.** A guard's name says what it is called and its file says what it asserts; neither tells you whether it covers the thing you are about to do. **The question it answers is what does that**, and it is the reason this is not an inventory.

**This page is guarded.** `GuardInventory.Tests.cs` asserts that every file named below exists, so a renamed or deleted guard breaks this document instead of quietly outliving it, and that the list has not silently emptied. It does **not** check that a description is still true — no test can — so **when you change what a guard asserts, change its row.**

## Tree-wide guards, backend

| Guard | Where | The question it answers |
| --- | --- | --- |
| Documentation links | `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/DocumentationLinks.Tests.cs` | Does every relative link and image in the delivered documentation still resolve to a file? Markdown links **and** HTML `href`s — it once read only the first form and three catalog links were dead for a day with the build green. |
| The shape of file access | `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/ShapeOfFileAccess.Tests.cs` | Does production code reach for the central file helpers rather than a raw file API? Carries an allow-list where a raw access is deliberate, each entry with its reason. **Its rule knew one spelling of a write until 2026-09-22**, which is why a `FileStream` write sat unseen. |
| Namespace providers | `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/NamespaceProviders.Tests.cs` | Does every declared namespace match its project plus unskipped folders, with the skip entries read from each `.csproj.DotSettings` — escaping decoded, so a mis-escaped key that matches nothing surfaces rather than passing? |
| Source line endings | `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/SourceLineEndings.Tests.cs` | Does any C# file under `src` carry a bare carriage return? |
| Dependency inventory | `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/DependencyInventory.Tests.cs` | Is `docs/dependencies.md` still true of the package manifests — missing, spurious or version-mismatched rows, each named with its fix? |
| Example registration | `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/ExampleRegistration.Tests.cs` | Does every tracked example registration open against the **real deployed catalog**? |
| Shipped examples | `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/ShippedExamples.Tests.cs` | Do the shipped example documents carry placeholder junk? |
| Problem-store isolation | `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/ProblemStoreIsolation.Tests.cs` | Does every integration test that boots the real host replace the problem store with one on its own temp root? **Twenty-three instances caught so far** — the count is the argument against deleting it. |
| Drawn connections | `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/DrawnConnections.Tests.cs` | Does every connection any canvas draws resolve to a selection, over every module's shipped examples on the real composed host? |
| The gate's own self-test | `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/GateScript.Tests.cs` | Does the shared gate still pass its own 151-case self-test — including that the scratch-worktree guard extracts from `processes.md` as exactly one intact block? |
| Diagram file pair | `src/backend/EtAlii.Adp.Hierarchy.Tests/DiagramFilePair.Tests.cs` | When a diagram is a pair of files, do rename, delete and create move both, remove both, create both — or neither? |
| Application assemblies | `src/backend/EtAlii.Adp.Tests/ApplicationAssemblies.Tests.cs` | Does the assembly walk work against the real entry assembly and real deployment manifest? |

## Per-module guards that state a whole type's promise

| Guard | Where | The question it answers |
| --- | --- | --- |
| Zero writes (ansible) | `src/diagrams/ansible-structure/backend/EtAlii.Adp.Diagram.AnsibleStructure.Tests/ZeroWrites.Tests.cs` | Does reading an Ansible project change anything of Ansible's in it? The claim the whole type turns on. |
| Zero writes (helm) | `src/diagrams/helm-charts/backend/EtAlii.Adp.Diagram.HelmCharts.Tests/ZeroWrites.Tests.cs` | The same for a chart — trivially today, because the module has no writer for chart content at all. |
| Shape of sources (causal-loop) | `src/diagrams/causal-loop/backend/EtAlii.Adp.Diagram.CausalLoop.Tests/ShapeOfSources.Tests.cs` | What must this module's own files be, as files, regardless of what they say? |
| Example corpus (sparql) | `src/diagrams/sparql/backend/EtAlii.Adp.Diagram.Sparql.Tests/ExampleCorpus.Tests.cs` | Does every vendored example parse, draw something, and report nothing above info? |

## Tree-wide guards, client

| Guard | Where | The question it answers |
| --- | --- | --- |
| Declarative modules | `src/client/src/canvas/library/declarativeModules.test.ts` | Does every diagram module draw through its declaration, with nothing reaching past it — and does every shape it names exist in the library? |
| No module selection | `src/client/src/canvas/library/noModuleSelection.test.ts` | Does any module client handle selection? |
| No private gestures | `src/client/src/canvas/library/noPrivateGestures.test.ts` | Does a migrated module hold private gesture state? |
| No private label editors | `src/client/src/canvas/label/noPrivateLabelEditors.test.ts` | Does any module build its own in-place editor? |
| No private scrollbars | `src/client/src/canvas/scroll/noPrivateScrollbars.test.ts` | Does any module build its own scrollbar? |
| No private view reports | `src/client/src/diagrams/noPrivateViewReports.test.ts` | Does any module build its own view report? |
| No unstyled library classes | `src/client/src/canvas/library/noUnstyledLibraryClasses.test.ts` | Does every visual class the library emits have a rule behind it? |
| Shared stylesheet imports | `src/client/src/canvas/sharedStylesheetImports.test.ts` | Does every module that registers a canvas import the shared stylesheet from its own `register.ts`? |
| Theme tokens | `src/client/src/themeTokens.test.ts` | Does any `var(--color-…)` name a token the theme never defines — which renders its fallback and looks fine in review? |
| One stream opener | `src/client/src/diagrams/diagramStreamOpensOnlyInHook.test.ts` | Does anything but `useDiagramStream` open a diagram's delta stream? |
| The library's standing guards | `src/client/src/canvas/library/libraryGuards.test.tsx` | The library's three standing guards, each **mounting rather than grepping** — a grep once reported three correct canvases as offenders — and each carrying a named-member canary so its population cannot quietly empty. |
| Highlight survives module styles | `src/client/src/canvas/highlightSurvivesModuleStyles.test.tsx` | Can any stylesheet in the application repaint the library's two rings? |
| File URL paths | `src/client/src/fileUrlPaths.test.ts` | Does a file URL's pathname always begin with a slash? |

## Guards that are not tests

| Guard | Where | The question it answers |
| --- | --- | --- |
| The four gates | `.github/tools/gate/gate.sh` | `npm test`, `npm run typecheck`, `dotnet format style --verify-no-changes --severity info`, `dotnet test` — each status captured before any pipe, and a verdict that **starts refused** and needs positive evidence that tests ran. |
| The scratch-worktree guard | `.spec-workflow/steering/processes.md`, extracted at run time | Is the path being gated a scratch worktree, and not a husk, a feature worktree or the main checkout? **Read from `develop`'s copy, never from the branch being gated.** |
| Undeleted test folders | `gate.sh`'s `UNDELETED_FOLDERS=` line | Did this run leave test folders it could not delete? **`none` on a green run; `1 file, 2 lines` means the test-support exclusion has regressed.** |
| Who holds a gate | `for d in .git/worktrees/*/adp-gate.lock; do [ -d "$d" ] && cat "$d/owner"; done` | Which gates are running, and since when — `pid <n> since <utc> gating <branch>` per holder, nothing when none is held. **It cannot say whether the board is yours**: the lock is per scratch worktree by design, so two agents with different `mrg<N>` names are MEANT to gate at once, and serialising them is a decision somebody makes rather than a fact the tree enforces. Looking for one shared lock at `.git/adp-gate.lock` prints "free" whether or not a gate is running, which is no evidence at all. |
| Retirement refusals | `.github/tools/gate/retire.sh` | Is this worktree safe to delete — uncommitted work, untracked files, links out of the tree, and commits with no patch match on `develop` (`unmatched-commits`, which is **not** the same as unlanded). |

## What is deliberately not here

- **Ordinary unit and integration tests.** A guard asserts a property of the tree or of a whole type; a test asserts the behaviour of one subject. The line is not sharp, and a row here is a judgement rather than a fact.
- **`dotnet format` and the analyzers**, which are configured rather than written: `src/.editorconfig` raises `IDE0001`/`IDE0002` to warnings, and `src/Directory.Build.props` makes `xUnit1031` and `xUnit1051` errors. **A warning nothing fails on is a finding nobody reads**, so a rule that matters is raised rather than left printing.
- **JetBrains InspectCode**, which is authoritative for this team and is not one of the four gates.
