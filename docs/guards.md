# The guards, and what each one answers

**A guard nobody can find only speaks after the mistake.** On 2026-09-22 three of these caught something nobody was looking for — a host-booting test class that would have written into the developer's real profile, a raw file write that had been invisible to the rule meant to catch it, and a test-support exclusion that had regressed. Each was found by a guard, not by review. **So this page exists to make the set findable before the next mistake, not after it.**

**Read the third column.** A guard's name says what it is called and its file says what it asserts; neither tells you whether it covers the thing you are about to do. **The question it answers is what does that**, and it is the reason this is not an inventory.

**This page is guarded.** `GuardInventory.Tests.cs` asserts that every file named below exists, so a renamed or deleted guard breaks this document instead of quietly outliving it, and that the list has not silently emptied. It does **not** check that a description is still true — no test can — so **when you change what a guard asserts, change its row.**

## Tree-wide guards, backend

| Guard | Where | The question it answers |
| --- | --- | --- |
| Documentation links | `src/backend/EtAlii.Adp.Repository.Tests/DocumentationLinks.Tests.cs` | Does every relative link and image in the delivered documentation still resolve to a file? Markdown links **and** HTML `href`s — it once read only the first form and three catalog links were dead for a day with the build green. |
| The architecture pages | `src/backend/EtAlii.Adp.Repository.Tests/ArchitecturePages.Tests.cs` | Does every repo path and `EtAlii.Adp.*` project name written on `docs/architecture.md`, `docs/solution-structure.md`, `CLAUDE.md` and the steering documents still exist — and does every count the structure page states recompute from the tree? It also holds each page's size and a floor of parsed paths and counts, so an emptied page fails rather than passing vacuously. **It cannot tell whether a description is still true**: a claim that the canvas used Konva outlived every check for as long as it was written down, and finding a phantom project name in the naming convention's own example is what this guard is for. |
| The shape of file access | `src/backend/EtAlii.Adp.Repository.Tests/ShapeOfFileAccess.Tests.cs` | Does production code reach for the central file helpers rather than a raw file API? Carries an allow-list where a raw access is deliberate, each entry with its reason. **Its rule knew one spelling of a write until 2026-09-22**, which is why a `FileStream` write sat unseen. |
| Namespace providers | `src/backend/EtAlii.Adp.Repository.Tests/NamespaceProviders.Tests.cs` | Does every declared namespace match its project plus unskipped folders, with the skip entries read from each `.csproj.DotSettings` — escaping decoded, so a mis-escaped key that matches nothing surfaces rather than passing? |
| Source line endings | `src/backend/EtAlii.Adp.Repository.Tests/SourceLineEndings.Tests.cs` | Does any C# file under `src` carry a bare carriage return? |
| Dependency inventory | `src/backend/EtAlii.Adp.Repository.Tests/DependencyInventory.Tests.cs` | Is `docs/dependencies.md` still true of the package manifests — missing, spurious or version-mismatched rows, each named with its fix? |
| Example registration | `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/ExampleRegistration.Tests.cs` | Does every tracked example registration open against the **real deployed catalog**? |
| Shipped examples | `src/backend/EtAlii.Adp.Repository.Tests/ShippedExamples.Tests.cs` | Do the shipped example documents carry placeholder junk? |
| Problem-store isolation | `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/ProblemStoreIsolation.Tests.cs` | Does every integration test that boots the real host replace the problem store with one on its own temp root? **Twenty-three instances caught so far** — the count is the argument against deleting it. |
| Drawn connections | `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/DrawnConnections.Tests.cs` | Does every connection any canvas draws resolve to a selection, over every module's shipped examples on the real composed host? |
| The gate's own self-test | `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/GateScript.Tests.cs` | Does the shared gate still pass its own self-test - **whose case count is pinned in the script itself, so a case that silently stops running reddens rather than passing** — including that the scratch-worktree guard extracts from `processes.md` as exactly one intact block? |
| The log capture reaches module test projects | `src/diagrams/timeline/backend/EtAlii.Adp.Diagram.Timeline.Tests/LogCaptureReachesModuleTestProjects.Tests.cs` | Is `LogCapture` still COMPILED into each test project rather than referenced from one? `src/Directory.Build.targets` compiles it into every `*.Tests` project, because its `[ModuleInitializer]` runs once per assembly and must beat that assembly's static loggers to their binding. **Referenced instead, it would initialise the support assembly, the code under test would bind to the default `Log.Logger`, and every capture would be empty while every test stayed green.** It lives in a MODULE test project deliberately: a core one cannot tell the two arrangements apart. |
| The instrument speaks after late configuration | `src/backend/EtAlii.Adp.Documents.Tests/AdpFileWriter.SpeaksAfterLateConfiguration.Tests.cs` | Does the failure record still reach a Serilog pipeline installed **after** the writer was first used - the ordinary case in a host, and **the case that silenced the instrument in the field**? A `private static readonly ILogger` bound before configuration is the silent logger for the process's life, so the writer and the holder query resolve theirs at the call site instead. **The guard exists because the failure is silence**: four occurrences of a publish race were read for their holder evidence, and at least one had none to give. |
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
| One text metric | `src/client/src/canvas/label/textMetrics.test.ts` | Does any client code estimate a text's width from a character count, divide by a per-character advance, or cut an ellipsis by slice outside `textMetrics.ts` - and is a text the metric sized never then trimmed by the fit? It reads text, so it finds the copy made from an existing canvas, which is how the five it replaced arose. |
| One builder per gesture id | `src/client/src/canvas/gestureIds.test.ts` | Does any client code write a `new:x,y` or `rel:a->b` id by hand, or take a prefix off an id without checking it is there? causal-loop stripped `variable:` by length, so a bare prefix became an empty end and an unprefixed id lost nine characters. |
| No private scrollbars | `src/client/src/canvas/scroll/noPrivateScrollbars.test.ts` | Does any module build its own scrollbar? |
| No private view reports | `src/client/src/diagrams/noPrivateViewReports.test.ts` | Does any module build its own view report? |
| No unstyled library classes | `src/client/src/canvas/library/noUnstyledLibraryClasses.test.ts` | Does every visual class the library emits have a rule behind it? |
| Shared stylesheet imports | `src/client/src/canvas/sharedStylesheetImports.test.ts` | Does every module that registers a canvas import the shared stylesheet from its own `register.ts`? |
| Theme tokens | `src/client/src/themeTokens.test.ts` | Does any `var(--color-…)` name a token the theme never defines — which renders its fallback and looks fine in review? |
| One stream opener | `src/client/src/diagrams/diagramStreamOpensOnlyInHook.test.ts` | Does anything but `useDiagramStream` open a diagram's delta stream? |
| The library's standing guards | `src/client/src/canvas/library/libraryGuards.test.tsx` | The library's three standing guards, each **mounting rather than grepping** — a grep once reported three correct canvases as offenders — and each carrying a named-member canary so its population cannot quietly empty. |
| Highlight survives module styles | `src/client/src/canvas/highlightSurvivesModuleStyles.test.tsx` | Can any stylesheet repaint the highlight — and **does every shape the library can draw receive it at all**? It walks the library's own `BUILT_IN_SHAPES` and renders each one selected, so **a shape added to that list and not to the switch fails on the day it is added, rather than on the day a module first declares it**. Its predecessor rendered one element type and could only see shapes some module happened to declare: **covering the modules rather than covering the code.** That is why c4 showed no highlight for a week with the guard green — `renderShapeBody` never received the paint, so there was nothing for a rendered assertion to compare, and a canvas-by-canvas human pass then recorded wardley-map as working while `symbol`'s default `circle` was bare. **Its limit, stated in the test: it asserts at least ONE drawn node wears the highlight, not all of them** — so it catches a whole case forgetting the paint, not one node of a multi-part shape. |
| No module reaches library shapes | `src/client/src/canvas/library/noModuleReachesLibraryShapes.test.ts` | Does any module stylesheet select an SVG element type - `.mindmap-node rect`, which matched the library's own selection ring and painted a box over the label? **It reads selectors rather than rendering**, because the latent case (`.wardley-annotation circle`, setting a colour the shape already had) looked right and no eye would have found it; the rendered half is *Highlight survives module styles*. |
| No stylesheet rule without an emitter | `src/client/src/canvas/library/noStylesheetRuleWithoutAnEmitter.test.tsx` | Does every class a canvas stylesheet rules appear when every canvas is **mounted on the shipped examples**, and does every class those canvases draw have a rule - each exception listed with why? Mounted rather than read, because `sparql-region-{payload.kind}` is composed from a payload; the backend's `ShippedExampleModelsTests` exports each example as the stream a canvas receives, to `src/fixtures/cross-tier/example-models/`, and fails when that copy is stale. It sees classes, not selectors, and the examples at rest. |
| File URL paths | `src/client/src/fileUrlPaths.test.ts` | Does a file URL's pathname always begin with a slash? |

## Guards that are not tests

| Guard | Where | The question it answers |
| --- | --- | --- |
| The four gates | `.github/tools/gate/gate.sh` | `npm test`, `npm run typecheck`, `dotnet format style --verify-no-changes --severity info`, `dotnet test` — each status captured before any pipe, and a verdict that **starts refused** and needs positive evidence that tests ran. |
| The scratch-worktree guard | `.spec-workflow/steering/processes.md`, extracted at run time | Is the path being gated a scratch worktree, and not a husk, a feature worktree or the main checkout? **Read from `develop`'s copy, never from the branch being gated.** |
| Undeleted test folders | `gate.sh`'s `UNDELETED_FOLDERS=` line | Did this run leave test folders it could not delete? **`none` on a green run; `1 file, 2 lines` means the test-support exclusion has regressed.** |
| Who holds a gate | `for d in .git/worktrees/*/adp-gate.lock; do [ -d "$d" ] && cat "$d/owner"; done` | Which gates are running, and since when — `pid <n> since <utc> gating <branch>` per holder, nothing when none is held. **It cannot say whether the board is yours**: the lock is per scratch worktree by design, so two agents with different `mrg<N>` names are MEANT to gate at once, and serialising them is a decision somebody makes rather than a fact the tree enforces. Looking for one shared lock at `.git/adp-gate.lock` prints "free" whether or not a gate is running, which is no evidence at all. |
| Retirement refusals | `.github/tools/gate/retire.sh` | Is this worktree safe to delete — uncommitted work, untracked files, links out of the tree, and commits with no patch match on `develop` (`unmatched-commits`, which is **not** the same as unlanded). |

## What the suite cannot host, and why green says nothing about it

**A harness can be structurally blind to a whole class of defect, and then being green tells you nothing about that class.** One case is measured and worth stating, because it explains a puzzle that cost an afternoon:

**`LogCapture` installs a process-wide Serilog pipeline from a `[ModuleInitializer]`, compiled into every `*.Tests` project, before any static logger field can bind.** So in a test assembly `Log.Logger` is **never** the silent logger. The production defect where `private static readonly ILogger _logger = Log.ForContext<T>()` binds to `SilentLogger` and stays mute for the process's life **cannot occur in a test assembly by construction** - **no test in this suite could ever have caught it, and none ever will.**

**And the blindness is DELIBERATE, documented and guarded - not an oversight anybody made.** `src/Directory.Build.targets` says in as many words *do not turn this into a referenced TestSupport assembly*, and why: the initializer runs once per **assembly**, so compiled in it beats the test project's static loggers to their binding, while referenced it would initialise the support assembly and capture nothing. **That failure would be silent, so it has a guard rather than only a comment** - the row above, which lives in a module test project because a core one cannot tell the two arrangements apart.

**So the fixture is correct, load-bearing, and would be wrong to remove; this section is not an argument for changing it.** The consequence is what nobody had drawn: **the same mechanism that makes captures work at all is what makes a class of production defect unobservable in every test.**

**It is a limit of the suite rather than a weakness of any test**, and it is why the defect was found in a gate log rather than by a red. **Before concluding that a class of defect does not exist here, ask whether the harness can host it at all.**

## What is deliberately not here

- **Ordinary unit and integration tests.** A guard asserts a property of the tree or of a whole type; a test asserts the behaviour of one subject. The line is not sharp, and a row here is a judgement rather than a fact.
- **`dotnet format` and the analyzers**, which are configured rather than written: `src/.editorconfig` raises `IDE0001`/`IDE0002` to warnings, and `src/Directory.Build.props` makes `xUnit1031` and `xUnit1051` errors. **A warning nothing fails on is a finding nobody reads**, so a rule that matters is raised rather than left printing.
- **JetBrains InspectCode**, which is authoritative for this team and is not one of the four gates.
