# Manual verification checks

Step-by-step checks for bugs that only reproduce through the running app, so they can be
re-executed as part of a manual verification pass. Each entry names the spec and task it
came from. (See CLAUDE.md, "Bugs found during implementation or verification".)

## Drop-target highlight actually paints (mindmap-diagram, bezier-connector pass)

The unit test can only assert the `mindmap-node-drop-target` class - jsdom does not load
`index.css`, so a later equal-specificity rule (`.mindmap-node rect`) silently overriding the
highlight's stroke is invisible to it. That is exactly how this bug shipped once: the class was
applied and the test passed while the node looked unchanged.

- **Preconditions**: backend + client running; a project with a mindmap of at least three
  nodes open in a diagram tab.
- **Actions**: press the mouse on a non-root node, drag it more than a few pixels, and hold
  the pointer over another node without releasing.
- **Expected**: the hovered node's border changes to the accent color (`--color-primary`,
  limegreen), thicker (2.5px) and dashed - visibly different from both the normal border and
  the focused node's solid accent border. Releasing the button clears it again.

## Collapse in the node's context menu offers Expand afterwards (mindmap-diagram, bezier-connector pass)

Guarded by `DiagramElementActionFlowTests.ACompletedAction_RepushesTheSelectionsActions_SoACollapseOffersExpand`;
kept here because the stale label was found through the menu and is quickest to spot there.

- **Preconditions**: a mindmap with a branch node (has children) open in a diagram tab.
- **Actions**: right-click the branch node, choose **Collapse**; right-click the same node again.
- **Expected**: the branch's descendants disappear on the first click, and the re-opened menu
  reads **Expand** (not Collapse). Choosing Expand restores the branch.

## C4 relationships are elevated to the level the view shows (c4-diagrams, manual pass)

Covered by `C4ElementMapperTests.AContextView_ElevatesAContainersRelationship_ToTheSystemThatContainsIt`,
kept here because the symptom is only obvious when you look at a rendered diagram: the context
view drew the systems it depends on and *no lines to them*, which reads as "no dependency".

- **Preconditions**: a project with a `.dsl` whose containers talk to an external software
  system, plus a `c4/context` `.adp` naming that model and view.
- **Actions**: open the context view.
- **Expected**: the system in scope has a labelled arrow to each external system its containers
  talk to, and no arrow to itself. Two containers talking to one external system draw one line,
  not two.

## A C4 boundary contains only what is inside it (c4-diagrams, manual pass)

Covered by `C4LayoutTests.WhatIsNotInsideTheSystem_IsNotDrawnInsideItsBoundary`, kept here for
the same reason: the containers looked right, and only the boundary's geometry gave it away.

- **Preconditions**: a `.dsl` with a system containing two or more containers, plus a person and
  an external software system that talk to them; a `c4/container` `.adp` naming that view.
- **Actions**: open the container view.
- **Expected**: the system in scope is drawn as the dashed boundary and **not** also as a box;
  every container sits inside it; the person and the external system sit outside it; and nothing
  overlaps anything else.

## A C4 document ADP wrote opens in Structurizr Lite (c4-diagrams, task 39)

The reason for choosing the Structurizr DSL over a format of ADP's own was interoperability, and
that claim is only worth something checked against the real tool.

**The parsing half of this is now automated** and no longer needs a manual pass. `C4InteropTests`
hands ADP's own output to the real Structurizr CLI - the `structurizr-dsl` library Lite itself
parses with - covering the new-document template for all six working types, a document ADP
created and then edited (rename, description, added view) and then undid, and every fixture in
the corpus. It skips unless a CLI is provided, so run it as:

```powershell
$env:ADP_STRUCTURIZR_CLI = "<structurizr-cli>lib"; dotnet test --solution EtAlii.Adp.slnx
```

That check found two defects in ADP's own templates on its first run: a component view scoped as
`system.container` (identifiers are flat unless a document says `!identifiers hierarchical`, so
the dotted form named nothing), and a `deploymentEnvironment` with no nodes in it (an environment
is a property of the nodes deployed into it, so an empty one does not exist and the view bound to
nothing). Both are fixed and guarded.

An earlier version of this entry said the check could not run here because there was neither
Docker nor a JRE. A JDK has since been installed, and the CLI comes from Maven Central, which was
reachable all along - the entry was wrong, not the environment.

**The rendering half is now automated too**, and this entry no longer asks anyone to open a
browser to answer it. `C4InteropTests.EveryExport_IsStillWhatStructurizrDraws` exports every
fixture through the real CLI to Mermaid and compares the result to the diagrams committed in
`Fixtures/exports`; `C4ExportTests` then reads those committed diagrams with no JDK at all and
asserts that every element ADP believes is on a view was actually drawn on it. That is the
question a person used to answer by looking at Lite, asked without the browser, the container
or the person.

It is a real check rather than an exit code: a view that renders empty exports perfectly
happily, and ADP shipped exactly that once - the `c4/deployment` template with an empty
`deploymentEnvironment`, which bound to nothing and drew nothing while every command
succeeded. Replacing a committed diagram with an empty frame fails both checks, which was
verified rather than assumed.

**What is still manual** is what no text format can answer: whether the picture is any good to
look at. Three things, and only these:

- **Styling and themes.** That `styles` blocks and `theme` directives ADP round-trips actually
  resolve to the colours and shapes intended, rather than merely surviving as text. ADP carries
  these through untouched and has no model of what they mean.
- **Layout quality.** Structurizr's own auto-layout on a document ADP wrote: whether the result
  is readable, not merely non-overlapping. `C4LayoutCrowdingTests` pins that boxes do not sit
  on top of each other; nothing can pin that a diagram is pleasant to read.
- **The sidecar staying out of the way.** That ADP's `.layout.json` beside the `.dsl` draws no
  complaint from Lite, since it is ADP's own file and not part of the workspace.

- **Preconditions**: Docker (or a JRE and the Structurizr Lite jar). A project containing a C4
  model ADP created and then edited - rename an element, set a technology, add a second view -
  so the file under test is one ADP wrote, not one it only read. Give it a `styles` block.
- **Actions**: run Structurizr Lite against the folder holding the `.dsl`
  (`docker run -it --rm -p 8080:8080 -v /path/to/folder:/usr/local/structurizr structurizr/lite`)
  and open `http://localhost:8080`.
- **Expected**: the styles resolve, the auto-layout is readable, and the `.layout.json` sidecar
  causes no complaint. That every view is present and every element is on it is no longer part
  of this pass - the export checks own that, and they run on every `dotnet test`.
## The property grid's keyboard cadence, in a real browser (property-grid, task 6)

The commit cadence is pinned by unit tests, but no manual pass has ever seen it in a real
browser: the in-app browser pane could not composite frames during the task 6 pass, so key
events never reached the page (typing worked; `Enter`, `Ctrl+Enter` and `Escape` did not).
The blur path was verified live instead. Run this on a machine where the browser pane
displays, or in an ordinary browser against the dev servers.

- **Preconditions**: a project holding a mindmap with at least one node; the Properties panel
  visible; a node selected so its Text, Notes and Link rows show.
- **Actions and expected results**:
  1. Click into **Text**, type, and press **Enter**. → The value commits once: the canvas
     updates, and exactly one write reaches the backend (`Set mindmap.text` in the log).
  2. Click into **Notes**, type, press plain **Enter**. → A newline is added and *nothing*
     commits. Then press **Ctrl+Enter**. → It commits once.
  3. Click into **Text**, type, press **Escape**. → The field returns to the value the backend
     last described and nothing is written.
  4. Click into **Text**, change nothing, click elsewhere. → Nothing is written, and Undo does
     not become available for it.

## A property edit reaches a second connection (property-grid, task 6)

Requirement 5.2 says a committed change reaches every connection viewing the document, and the
grid follows the push rather than its own copy. Nothing automated covers two connections, and
the task 6 pass could not drive a second browser tab's canvas selection through the
non-compositing pane.

- **Preconditions**: the same project open in two browser tabs, the same diagram open in both,
  and the same node selected in both, with the Properties panel visible in each.
- **Actions**: in tab A, edit the node's **Text** and commit (Enter or by clicking away).
- **Expected**: tab B's canvas shows the new text without any interaction, and tab B's
  Properties panel shows the new value in its Text row - it re-describes from the push. Then
  press **Undo** in tab A: both tabs return to the old value, canvas and grid alike.

## `dotnet test` discovers tests in a long worktree path (build tooling)

Windows' 260-character `MAX_PATH` silently breaks the backend build in deep checkouts. MSBuild's
`Exists()` returns false - without any error - for a path over 260 characters, so the SDK's
`_CreateAppHost` condition `Exists('@(IntermediateAssembly)')` evaluates false and `apphost.exe`
is never produced. Because every xUnit v3 test project is an executable whose apphost *is* the
test host, the run surfaces as `dotnet test` reporting `Zero tests ran` for project after
project, which reads like an empty suite rather than a broken build.

`src/Directory.Build.targets` guards this with error `ADP0001`, but the guard only proves it
fails loudly; this check proves it does not fail at all. The repository is inside the limit from
the main checkout - a worktree under `.claude/worktrees/<name>/` adds ~36 characters plus the
worktree name, and the longest project path needs the machine's long-path support.

- **Preconditions**: Windows; `HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled`
  is `1` (set it as admin; existing processes must be restarted to pick it up). A git worktree
  whose directory name is at least 35 characters, e.g.
  `git worktree add .claude/worktrees/a-deliberately-long-worktree-name develop`.
- **Actions**: from that worktree's `src/backend/`, run `dotnet test --solution EtAlii.Adp.slnx`.
- **Expected**: the run completes with a non-zero test total (1338 at the time of writing), exit
  code 0, and no `ADP0001`, `ADP0002` or `MSB3030` error. A summary of `Zero tests ran` with
  `total: 0`, or `error: 54`, means long-path support is not in effect for the process that ran
  the build - it is not a test failure.
- **Note**: a genuinely empty run does exit non-zero (5 for zero tests, 8 for a filter matching
  nothing), so any CI step must check the exit code rather than grepping the output for
  `failed`. Grepping alone reads a zero-test run as a pass.
