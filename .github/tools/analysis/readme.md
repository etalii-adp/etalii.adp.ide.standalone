# Measurement scripts

The figures the milestone reviews of etalii.adp's spec 025, [*The Structure of the Standalone Host's C# Code*](https://github.com/etalii-adp/etalii.adp/tree/develop/specs/025-standalone-code-structure), compare from one milestone to the next. They are tools, not product code: nothing here is part of the test suite or the build.

```bash
bash .github/tools/analysis/measure.sh --out <folder>   # measure the checked-out commit
bash .github/tools/analysis/measure.sh --self-test      # measure fixtures/tree and compare with fixtures/expected.json
```

`measure.sh` runs the five scripts below and writes `measurements.json` (everything) and `measurements.md` (the summary a review reads). Each script also runs on its own and prints JSON: `python3 lines.py --root <repository>`. They need Python 3 and nothing else.

## What each figure means

Every script reads the projects of `src/backend/EtAlii.Adp.slnx` and the `.cs` files in their folders, leaving out `bin/` and `obj/`. A project is **core** when its solution path has no leading `../`, as [docs/solution-structure.md](../../../docs/solution-structure.md) says it must be classified, and otherwise a **diagram**, **designer** or **editor** project by the folder it sits under. It is a **test** project when its name ends in `.Tests`. **Test code** is the test projects and the two testing projects they reference, `EtAlii.Adp.TestSupport` and `EtAlii.Adp.TestSupport.Host`, which are not named `.Tests` and hold nothing but test helpers; everything else is **production** code. `lines.py`, `methods.py` and `waits.py` split by test code, so a helper moved out of a test project into a testing project stays test code rather than showing as test code removed and production code added. `clones.py --tests` still scans the test projects alone.

| Script | Task | Figures |
| --- | --- | --- |
| `lines.py` | T002 | Projects, files, lines and code lines per group (core and the three tool kinds, each split into production and test code), and production lines per tool module. *Lines* are physical lines; *code lines* leave out blank lines and lines holding only a comment. |
| `clones.py` | T003 | The normalized clone scan. Each file is reduced to its code lines, with the module's own name (its project's last segment, its folder in Pascal case, its initials, and the prefix most of its file names start with) replaced by `MOD`, string literals by `S` and numbers by `0`. Every window of ten such lines is hashed; a window is *shared* when two or more files hold it. Per role pair (a role is a file's name without the module prefix: `SankeyDocumentStore.cs` is `DocumentStore`) it gives the distinct shared *windows* and the *file pairs* that share them, so a window fifteen resolvers share counts once and 105 times. Windows of only braces and usings are skipped. `--tests` scans the test projects instead. |
| `methods.py` | T004 | Methods of production code (the testing projects left out) with a block body (methods, constructors, operators, local functions), each measured in code lines from the line of its name to its closing brace. Braces are matched with comments and literals blanked. Expression-bodied members and primary constructors are not methods here. The count over 50 and over 80 code lines, the twenty longest, and the three largest types per project. |
| `graph.py` | T005 | Every `ProjectReference`, `PackageReference`, `FrameworkReference` and `InternalsVisibleTo` (item or assembly attribute) as an edge, those of the `.csproj` files and those the top-level item groups of `src/Directory.Build.targets` attach to every project they apply to (such as the reference to `EtAlii.Adp.TestSupport` it gives every project named `.Tests`), the latter marked `"via": "src/Directory.Build.targets"`; a condition there other than none or `` '$(MSBuildProjectName.EndsWith(`...`))' == 'true' `` stops the script. `src/Directory.Build.props` is not read, and the source files `Directory.Build.targets` compiles into every test project are no project's files and are not counted by any script. Then the most referenced projects, the projects without a `.cs` file, and the core projects that reference a tool module or give it their internals. |
| `waits.py` | T006 | Every `Task.Delay` and `Thread.Sleep` call outside comments and strings, in test code (the testing projects included) and in production code, and every declaration of a polling helper (`Eventually`, `WaitUntil…`, `…Until`, `…UntilAsync`, `Poll…`). |

`summary.py` turns `measurements.json` into `measurements.md`.

## Limits

These are text scans, not a compiler. A method signature split in an unusual way, or a brace inside a preprocessor branch, can be missed or miscounted; the self-test fixture holds the shapes that are known to matter (a brace in a string, an interpolated string, a primary constructor, a comment that mentions `Task.Delay`, the two testing projects, and a `Directory.Build.targets` with a reference for every test project, a compiled-in file and a target's item group). Figures are for comparing one commit with another under the same scripts, and a change to a script changes its baseline: re-measure the previous commit when a script changes.
