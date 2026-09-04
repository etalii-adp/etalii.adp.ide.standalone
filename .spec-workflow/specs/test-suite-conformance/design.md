# Design Document

## Overview

The requirements name 25 test sites that cannot currently fail, one weak root anchor, and one extraction that never proves it extracted anything. This document exists to answer a single question well: **what does a floor assertion look like, so that 25 sites can adopt it without 25 inventions.**

The answer is three lines of C# at the top of a test, and this design's main job is to stop it becoming anything larger. A repair that needs a helper, a base class, a package reference or an analyzer will not be applied to 20 files across 10 test projects; a repair that is one `Assert.True` will. Every finding in the requirements is repairable by one added assertion in one file, touching no shared code and no other module — which also makes the whole specification trivially parallel.

Nothing here changes what any test asserts about its subject. If a change under this specification alters an expected value, it has left the specification.

## Steering Document Alignment

### Technical Standards (tech.md)

The gate discipline says every gate is judged by its exit code, because a gate that always prints something is a gate nobody reads. A test that passes on an empty collection is the same defect one level down: it reports success without having looked. The floor is what makes the test's exit code mean what the gate assumes it means.

### Project Structure (structure.md)

The repairs are per-module and stay inside each module's own test project. Nothing moves to core, and no test project gains a reference it did not have.

## Code Reuse Analysis

### The reference implementation

`ProblemStoreIsolation.Tests.cs` already contains the whole design. Adopters copy it rather than invent:

```csharp
var booting = sources.Where(source => File.ReadAllText(source).Contains(BootsTheHost, StringComparison.Ordinal)).ToArray();

// Assert, first, that the walk found the suite at all. Without this the test passes
// loudest exactly when it has stopped looking at anything - a renamed folder or a
// changed fixture type would otherwise turn the guard off in silence.
Assert.True(
    booting.Length >= 20,
    $"Only {booting.Length} integration test sources mention {BootsTheHost}; this guard has stopped finding the suite it guards.");
```

That is at `src/backend/EtAlii.Adp.Backend.Tests/Integration Tests/ProblemStoreIsolation.Tests.cs:62-69`, and its root anchor — a four-segment distinctive path — is at `:100`. Twenty-four sources match the floor of 20 today, so it holds with four to spare.

### What is deliberately not built

* **No shared helper, and no `AssertFound(...)` extension.** Twenty files sit in ten test projects. A helper means a shared source file compiled into each one — the arrangement `src/TestSupport/TestFolder.cs` already uses for teardown — and the cost of that arrangement is only worth paying for behaviour, not for a single `Assert.True` that reads perfectly well inline. A floor with a helper's name on it is also harder to read at the call site, and reading it at the call site is the entire point.
* **No analyzer and no meta-guard that scans for loops without floors.** This was considered seriously and rejected on measured evidence: the scan behind the requirements was run three times and was wrong twice, in opposite directions. A single-line pattern missed every multi-line `Assert.True(`; flattening the newlines still missed them until the carriage returns were stripped, because these files are CRLF; and the corrected scanner then classified three literal `new Func<>[]` arrays as discovered collections. A mechanical guard built on the same pattern-matching would either nag about the twelve literal-array loops that are correct as written, or stay silent about the multi-line ones — and a guard that cannot reliably find its own subject is the very defect this specification is about.
* **No widening.** `DocumentationLinks` guards five delivered documents by deliberate decision; it gains a floor and not a sixth document.

## Architecture

### The floor, stated once

A floor has three parts, and the third is the one that gets dropped:

1. **A count taken from the collection the test is about to loop over**, evaluated before the loop.
2. **A lower bound with headroom** — `>= 20` against 24 actual, not `== 24`. An exact count turns every added fixture into a failing guard, and a guard that fails for boring reasons gets its number bumped without being read.
3. **A message that says the guard stopped working, not that the subject is wrong.** This is the part that makes a floor worth having. `Assert.NotEmpty(files)` fails with "the collection is empty", which reads as a product defect and sends the reader to the wrong code. "…this guard has stopped finding the suite it guards" sends them to the walk.

Where the bound is a number, the comment beside it says what population it is a floor on, so a later reader can tell a deliberate margin from a stale constant.

### Four adoption shapes

Every one of the 25 sites is one of four shapes. An adopter matches the site to a shape and copies.

**Shape A — a sweep over files discovered on disk** (8 sites). The floor is on the file count, before the loop:

```csharp
var fixtures = Directory.GetFiles(FixturesFolder, "*.yml", SearchOption.AllDirectories);
Assert.True(
    fixtures.Length >= 12,
    $"Only {fixtures.Length} fixtures were found under {FixturesFolder}; this test has stopped finding the corpus it checks.");
```

The five azure-pipeline sites additionally enumerate `Directory.GetFiles("Fixtures", …)` by **relative** path, which resolves against the process working directory rather than the test binary's folder. They change to `AppContext.BaseDirectory`, matching their siblings elsewhere in the tree. That is the same one-line character of repair and belongs in the same change.

**Shape B — a sweep over a collection the code under test produced** (11 sites). The floor is on that collection, and it is a stronger assertion than shape A because an empty result is not a missing fixture but the regression itself:

```csharp
var everything = graph.Nodes.Select(node => node.Id).Concat(graph.Edges.Select(edge => edge.Id)).ToArray();
Assert.True(
    everything.Length >= 6,
    $"The derived graph produced {everything.Length} elements; this test cannot check rows for elements that do not exist.");
```

**Shape C — `Assert.All` on a run-time collection** (5 sites). One line immediately above it:

```csharp
Assert.NotEmpty(properties);
Assert.All(properties, property => Assert.NotEqual("", property.ReadOnlyReason));
```

This is the one shape where `Assert.NotEmpty` is enough on its own, because it sits against the same identifier on the very next line and no reader can mistake what it is for.

**Shape D — an extraction that may match nothing** (1 site, `DocumentationLinks.Tests.cs:66`). The floor is on what the pattern extracted across the whole delivered set, and its comment must preserve the existing per-target tolerance:

```csharp
// A target the pattern misses goes unchecked rather than failing wrongly, which is
// deliberate and documented below. Total extraction failure is a different thing: a
// regex that matches nothing would check nothing and pass.
Assert.True(
    checkedLinks >= 40,
    $"Only {checkedLinks} relative links were extracted from {Documents.Length} delivered documents; LinkExpression() has stopped matching.");
```

### The anchor repair

One site, `src/diagrams/sparql/backend/EtAlii.Adp.Diagram.Sparql.Tests/ExampleCorpus.Tests.cs:17`. It walks up looking for a folder named `examples`, and a second `examples` directory sits further up the same walk path at `src/examples`. It is correct today only because the nearer one wins. It adopts the two-part form its own sibling already uses at `src/diagrams/c4/backend/EtAlii.Adp.Diagram.C4.Tests/Examples.Tests.cs:36` — `examples` and `backend` required together.

This is the only anchor repair. The other seventeen walks in the suite already satisfy the rule and are not touched.

## Components and Interfaces

There are none. That is the design decision, not an omission: this specification adds assertions and introduces no type, method, file or reference.

## Testing Strategy

### Verification by sabotage

A floor is accepted only when it has been **seen to fail**. The adopter empties the collection the floor guards — comments out the fixture copy, points the walk at a folder that does not exist, or stubs the producing call to return nothing — runs the module's tests, and confirms that the named test fails. Then reverts.

This is house practice now rather than a proposal: four view-delta adopters proved their behavioural tests bite by reverting the implementation and watching exactly the named test fail.

**The sabotage must be checked to have taken effect.** A patch written with LF newlines silently matches nothing in this CRLF tree — it changes no bytes, the suite passes, and the result reads as a robust test that survived tampering. Assert that the pattern matched before writing the file.

### Regression

The four gates, judged by exit code, before every merge: `dotnet test --solution EtAlii.Adp.slnx` and `dotnet format style --verify-no-changes --severity info` from `src/backend/`, and `npm test` and `npm run typecheck` from `src/client/`. No repair here touches client code, but the gate set is the gate set.

A repair must not change the number of tests that pass for any other reason. If adding a floor turns another test red, the floor has found something and that is a separate finding to report, not a number to tune.

## A separate finding: the sabotage that silently did nothing

This is recorded as a finding in its own right, and deliberately **not** counted among the 25. It concerns how a guard is *verified*, not what is wrong with one — and the two have already been merged twice in conversation about this specification, which is why it gets its own heading rather than a line inside the testing strategy.

**The hazard.** A patch written with LF newlines matches nothing in this CRLF tree. It changes no bytes, the test suite passes, and the outcome is indistinguishable from a guard that genuinely survived having its subject taken away. It is worse than no verification at all, because it produces evidence. An agent recorded a verdict on this basis before catching it, and the three sabotages run elsewhere in this repository on the day this was written were each asserted to have matched the file before it was rewritten, for exactly that reason.

**Why it is not one of the 25.** The distinction matters and is easy to lose:

* **A sabotage that did nothing** makes someone wrongly believe a guard works. The guard itself may be perfectly sound; only the verification was worthless.
* **A missing floor** means the guard has nothing to fail on, however carefully anyone verified it. The defect is in the test, not in the checking of it.

The 25 are the second kind. They were found by reading the code, not by re-running anyone's verification, and no sabotage — sound or silent — was involved in creating any of them. None of the 25 can be explained by this hazard, and a later reader who assumes otherwise will go looking for a verification history that does not exist.

**What follows from it** is one rule, stated in the testing strategy above and repeated here because this is where the reasoning lives: a patch that sabotages a test must assert it matched the file before writing. A sabotage that cannot prove it changed something proves nothing about the test that survived it.

## Landability and parallelisation

**Every repair is independently landable.** Each is one assertion in one file; none shares code with another; none has an ordering constraint. The tasks document may claim one worktree per agent on this basis.

The natural unit is the module, because that is the merge unit. Twenty files group into ten:

| Group | Files | Sites |
| --- | --- | --- |
| azure-pipeline | 5 | 7 |
| wardley-map | 3 | 3 |
| c4 | 3 | 3 |
| backend core (`EditorFilePropertyProvider`, `DocumentationLinks`) | 2 | 2 |
| helm-charts | 2 | 2 |
| databricks | 1 | 2 |
| mindmap | 1 | 3 |
| sparql (the floor **and** the anchor) | 1 | 1 + the anchor |
| ansible-structure | 1 | 1 |
| rdf | 1 | 1 |

Twenty-five floors plus one anchor repair, in twenty files. sparql is the one group carrying two kinds of repair and is worth giving to whoever reads the requirements' Requirement 4 closely, since its licence guard is the one this specification most wants sound.

### The merge hazard these groups will meet

Ten groups means several agents merging into `develop` in the same window, so adopters are pointed at the two rules CLAUDE.md gained in `db3bc0c0` (lines 30-31) rather than discovering them:

* Commit with `git commit -F msg -- <paths>`. `git add <path>` stages one file, but a bare `git commit` commits the whole index, including whatever another session staged into it.
* Before merging in the main checkout, read `git status` for files you did not touch. A merge writes every differing path regardless of the index. Neither `git stash` nor `git checkout --` is acceptable on another session's files.

One refinement worth stating because it stops the rule being over-applied: this is a **shared-index** hazard. Each worktree has its own index under `.git/worktrees/<name>/index`, so inside a dedicated worktree no other session can have staged anything. The pathspec-limited commit costs nothing everywhere and is the habit to keep; the folder-staging ban only bites in the main checkout.

## Deviations and notes

1. **The requirements say 25 sites; this design describes 26 repairs.** The extra is the anchor at `ExampleCorpus.Tests.cs:17`, which is Requirement 3 rather than a floor. The counts are consistent: 25 floors, 1 anchor, 20 files.
2. **`WardleyToolboxProvider.Tests.cs` gets one floor, not two.** The requirements note its uniqueness check at `:80` is also vacuous on an empty collection. Both tests read `_toolbox.Items`, so a single floor asserting the palette is non-empty — placed in the file's first test — fixes both, and duplicating it would be the kind of ceremony this design is trying to avoid.
3. **`Databricks/Diagram.Tests.cs` has two sites in one file** (`:25` and `:45`), both `Assert.All(Diagram.Definitions, …)`. `Definitions` is a static array in module source, so an empty one means the module declares nothing — a real regression, but the least likely of the 25. Shape C applies unchanged.
4. **The twelve literal-array loops are correct as written and must not be "fixed".** They are named here so a later pass does not reopen them: `ProblemsFlow.Tests.cs:84`, `HierarchyModel.Nesting.Tests.cs:66`, `PipelineContextPropertyProvider.Tests.cs:207` and `:372`, `c4/Fixtures.Tests.cs:114`, `mindmap/Fixtures.Tests.cs:80`, `MindmapLayout.Tests.cs:169`, `SparqlProviders.Tests.cs:43`, `NoWriterSurface.Tests.cs:42`, `WardleyCommands.Tests.cs:537` and `:564`, `WardleyContextPropertyProvider.Tests.cs:137` and `:154`. A loop over an array declared in the test cannot be empty.
5. **`[Theory]` with `MemberData` is not in scope.** Under xUnit v3 an empty data set surfaces as a test error rather than a silent pass, so those sweeps are self-guarding. Only `[Fact]` bodies and `Assert.All` needed floors, which is why the count is 25 and not far larger.
