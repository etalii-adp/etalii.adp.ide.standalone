# Manual verification checks

Step-by-step checks for bugs that only reproduce through the running app, so they can be
re-executed as part of a manual verification pass. Each entry names the spec and task it
came from. (See CLAUDE.md, "Bugs found during implementation or verification".)

## Two things every entry below assumes

**Signing in.** The app opens on a sign-in form and nothing below is reachable until somebody is
through it. An agent cannot type the credential, so **a person signs the tester in once and the
pass runs from there** — and note that reloading the page ends the session, because it is held in
memory only. A reload therefore costs a person's attention, not just a moment.

**Finding the Properties panel.** Thirteen entries below say "read the Properties panel" or "the
property grid" as though it were on screen. It often is not. `Properties` shares a tabbed pane
with `Toolbox` on the right, and `TabbedPane` measures the available width and collapses what does
not fit — at 1440x900 that pane is 253px, which fits one tab, so `Properties` sits behind a
**"1 more tabs" overflow button** beside `Toolbox`. That is the pane working as designed rather
than a defect. Widening the pane or clicking the overflow both reach it; hunting for it is what
costs the time, and it is written here once rather than in each of the thirteen.

## The context surface is reachable on a causal loop diagram (causal-loop-diagram, resolver fix)

**Found by opening the app, and invisible to every test until then.** The module shipped without
an element source resolver — the `*ContextSourceResolver.cs` every other diagram module has. A
canvas selection resolves level by level (project, folders, the `.adp`, then the element inside
it) and that last step is each diagram type's own. With none, no `ContextTarget` was ever built,
so neither the action provider nor the property provider was ever consulted.

Sixteen provider tests passed throughout, because they build a target by hand and ask the
provider directly — skipping exactly the step that did not exist.

- **Preconditions**: backend + client running; `src/examples` open as a project.
- **Actions**: open `diagrams/causal-loop/on-call/on-call.adp`. Right-click a variable. Press
  Escape, then right-click empty canvas. Then choose **Arrange diagram**.
- **Expected**:
  - Right-click on a variable opens a menu with **Add link from here…**, **Claim a loop from
    here…**, **Rename…** and **Remove variable (with N references)** — the count read from the
    model, not a guess.
  - The variable shows as selected on the canvas.
  - Right-click on empty canvas offers **Add variable…** and **Arrange diagram**.
  - The ribbon offers the same actions as the menu (Requirement 5.3).
  - **Arrange diagram** rearranges the variables with no two boxes overlapping, writes a
    `layout:` block into the `.adp`, leaves the `.cld` untouched, and **one** Undo restores the
    registration.

**Verified on 2026-09-05** up to and including the menus, the selection and the ribbon — the
backend log shows `Selected …/on-call.cld … as ContextMenu, with 1 action groups`. **Arrange
itself was NOT executed** in that pass: no `ExecuteAction` ever reached the backend, so the
undo half of this entry is still unverified.

- **Result 2026-09-05**: **partly verified.** Menus, selection and ribbon confirmed in the running
  app. Arrange and its undo remain to be run.

## A feedback loop is drawn as a loop (causal-loop-diagram, arcs fix)

**A defect found by looking at the running app.** The diagram type is named after loops and drew
none. Both links of a two-variable loop took the shared tree connector, which anchors on the sides
facing each other, so `a -> b` and `b -> a` produced the same path and rendered as **one line with
an arrowhead at each end**. Longer cycles read as a concertina rather than a ring.

The fix follows the notation's own convention — and Vensim's, which gives each arrow a single
curvature handle: one control point, offset perpendicular to the chord, always to the same side of
**travel**. Reversing a link reverses travel, which flips the offset, which is what encloses the
loop.

- **Preconditions**: backend + client running; `src/examples` open as a project.
- **Actions**: open `diagrams/causal-loop/on-call/on-call.adp`, then
  `diagrams/causal-loop/reference/reference.adp`.
- **Expected**:
  - Two variables joined both ways draw an **ellipse** between them, not a single line. This is
    the case that was broken.
  - A longer cycle bows outward consistently and reads as a **ring**.
  - Each link is a single arc, not an S-curve.
  - A loop's identifier sits inside a small **circular arrow** at the loop's centre, turning
    clockwise for a reinforcing loop and anticlockwise for a balancing one.
  - A delayed link's two strokes cross the curve **at right angles to it**, wherever on the arc
    they fall — not drawn vertically against a near-vertical link, where they used to vanish into
    the line.
  - The polarity mark sits beside the arc near the arrowhead, not underneath it.

**Why the unit tests did not catch the original**: every canvas test asserted that link elements
existed and carried their classes and marks. Nothing asserted anything about the **shape**, so two
links drawing the identical path was invisible. The tests added with this fix compare the two
paths and the sign of each curve's control point, and were seen to fail against the old geometry.

- **Result**: **not run.** No agent on this specification can sign in — see the preamble.

## A loop labelled R is arithmetically balancing, and the tool says so without changing the file (causal-loop-diagram, task 5.3)

The claim this diagram type exists to make: an `R`/`B` label is **checkable**, and a disagreement
is reported rather than corrected. The unit tests prove the arithmetic and the finding; what they
cannot prove is that a reader meets the finding, in the problems panel and in the property grid,
without the document changing under them.

- **Preconditions**: backend + client running; `src/examples` open as a project.
- **Actions**: open `diagrams/causal-loop/reference/reference.adp`. Read the problems panel.
  Select the loop labelled `R3` on the canvas and read the Properties panel (see the preamble on
  finding it). Then close the tab and reopen it.
- **Expected**:
  - The problems panel carries a warning naming `R3`, both polarities, and the negative count —
    something of the shape *"Loop R3 is labelled reinforcing but its links make it balancing: it
    runs through 3 negative links…"*.
  - The property grid shows **Computed polarity: balancing** and, because they differ, a second
    row **Stated polarity: reinforcing** whose read-only reason says neither is corrected for you.
  - `reference.cld` on disk is **byte-for-byte unchanged** — check with `git status`. Reporting is
    not correcting, and this is where that would break first.
  - Reopening shows the same finding: it is derived from the document, not remembered.
- **Also worth seeing while here**: loop `B4` reads **undecidable** rather than reinforcing or
  balancing, because the link into it states no polarity. Unknown is not none.

- **Result**: **not run.** No agent on this specification can sign in — see the preamble. Needs a
  person, or a session permitted to enter the placeholder credential.
- **Text reviewed 2026-09-05 (Tester 2)**: **accurate against the implementation**, checked without running the app. Every named document, variable, loop, property row, action label and reason string exists and matches. `reference.cld` carries `loop R3` and `loop B4`; `Computed polarity` and `Stated polarity` are in `CausalLoopContextPropertyProvider.cs`.

## A delayed link draws its strokes, and the grid agrees with the drawing (causal-loop-diagram, task 5.3)

A delay is drawn as short strokes across the arrow. That is the whole of its visual existence, and
nothing in the backend suite can see whether it was painted.

- **Preconditions**: backend + client running; `src/examples` open as a project.
- **Actions**: open `diagrams/causal-loop/reference/reference.adp`. Find the link from
  `crowding` to `sanitation`. Select it and read the Properties panel. Then right-click it.
- **Expected**:
  - The link is drawn with the conventional strokes across it, and its polarity mark reads `−` at
    the arrowhead end. The other links carry no strokes.
  - The grid shows **Delayed: true** as a toggle, **Polarity: negative** as a list, and a
    **Weight** row that is empty rather than `0` — this link has no weight, and an unweighted link
    is not a link of weight zero.
  - The context menu offers **Not delayed** (the opposite of what the link states), **Same
    direction (+)** as available, and **Opposite direction (−)** as *unavailable* with the reason
    *"This link already states −."*
- **Then**: toggle the delay off from the menu and confirm the strokes disappear; one Undo brings
  them back and the `.cld` returns byte-for-byte.

- **Result**: **not run.** Same reason as above.
- **Text reviewed 2026-09-05 (Tester 2)**: **accurate against the implementation**, checked without running the app. Every named document, variable, loop, property row, action label and reason string exists and matches. `link crowding -> sanitation - delayed` is in `reference.cld` and carries no weight, while its sibling `population -> crowding` carries `weight=0.8` - so the empty-not-zero Weight row is genuinely testable. `"This link already states −."` is verbatim at `CausalLoopContextActionProvider.cs:338`.

## The self-organizing layout rearranges a diagram, and one undo puts it back exactly (causal-loop-diagram, task 5.3)

Requirement 6 in one gesture. The layout is deterministic, it is invoked rather than automatic, it
writes authored positions rather than touching the body, and it is **one** undo away rather than
one per variable — that last point is the one a test in the backend can assert but only a person
can feel.

- **Preconditions**: backend + client running; `src/examples` open as a project.
- **Actions**: open `diagrams/causal-loop/on-call/on-call.adp`. Drag two or three variables into a
  deliberate mess. Right-click the canvas **background** — with nothing selected — and choose
  **Arrange diagram**. Then press Undo **once**.
- **Expected**:
  - Arrange is offered on the background and **not** on a selected variable: it is diagram-wide.
  - The diagram rearranges. No two variables overlap.
  - `on-call.cld` is **unchanged**; `on-call.adp` gained a `layout:` block. An arrangement is an
    opinion about where things are drawn, not a change to what the document says.
  - **One** Undo restores the registration exactly, including any positions that were authored
    before the arrange. Not one undo per variable.
- **And the determinism**: with the diagram arranged, note two or three positions, restart the
  backend, reopen and arrange again from the same starting registration. The positions are
  identical. If they are not, Requirement 6.3 has broken in a way the two-process unit test did
  not reach.
- **A refusal is a pass, not a failure**: if a diagram is refused with a sentence naming its size,
  that is the specified behaviour (Requirement 6.7) and should be recorded as such rather than
  retried until it succeeds.

- **Result**: **not run.** Same reason as above.
- **Text reviewed 2026-09-05 (Tester 2)**: **accurate against the implementation**, checked without running the app. Every named document, variable, loop, property row, action label and reason string exists and matches. `Arrange diagram` is at `CausalLoopContextActionProvider.cs:104` and `on-call` is present. Requirement 6.7 is conditional - a refusal only if determinism and overlap-freedom cannot both hold - so no refusal path in code is not a gap, and the entry is right to call a refusal a pass.

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
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## Collapse in the node's context menu offers Expand afterwards (mindmap-diagram, bezier-connector pass)

Guarded by `DiagramElementActionFlowTests.ACompletedAction_RepushesTheSelectionsActions_SoACollapseOffersExpand`;
kept here because the stale label was found through the menu and is quickest to spot there.

- **Preconditions**: a mindmap with a branch node (has children) open in a diagram tab.
- **Actions**: right-click the branch node, choose **Collapse**; right-click the same node again.
- **Expected**: the branch's descendants disappear on the first click, and the re-opened menu
  reads **Expand** (not Collapse). Choosing Expand restores the branch.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

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
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## A C4 boundary contains only what is inside it (c4-diagrams, manual pass)

Covered by `C4LayoutTests.WhatIsNotInsideTheSystem_IsNotDrawnInsideItsBoundary`, kept here for
the same reason: the containers looked right, and only the boundary's geometry gave it away.

- **Preconditions**: a `.dsl` with a system containing two or more containers, plus a person and
  an external software system that talk to them; a `c4/container` `.adp` naming that view.
- **Actions**: open the container view.
- **Expected**: the system in scope is drawn as the dashed boundary and **not** also as a box;
  every container sits inside it; the person and the external system sit outside it; and nothing
  overlaps anything else.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

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
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

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
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

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
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## The Ansible diagram opens from Add and draws its folder (ansible-structure-diagram, task 26)

The task 26 pass verified the backend half live - the type was discovered, both fixture folders
registered, and the problems panel showed all five rules with each problem's own file and line -
but could not drive the explorer's context menu or the canvas, because the Browser pane was not
displayed and so did not composite frames. Synthetic `contextmenu` and `dblclick` events do not
take. These are the checks that needs a real pair of eyes.

- **Preconditions**: the app running; a project open whose folder contains an Ansible project
  laid out the recommended way (the module's `Fixtures/infrastructure` tree is one - copy it
  somewhere outside the repository first, since nothing may write into the committed fixtures).
- **Actions and expected results**:
  1. Right-click the `infrastructure` folder in the explorer. → An **Add…** submenu offers
     **Ansible project structure (read-only)** among the diagram types.
  2. Choose it. → Exactly one file appears in the folder, `infrastructure.adp`, whose first line
     is `ansible/structure`. Nothing else in the tree changes - check `git status` if the folder
     is under version control.
  3. Double-click that `.adp`. → The diagram opens: `site.yml` leftmost, `webservers.yml` and
     `dbservers.yml` to its right, the three roles right of those, `tls.yml` right of `nginx`,
     and the two inventories with their variable folders in a band **below** the flow.
  4. Look at the edge styles. → `roles:` and `import_playbook` draw solid; the `include_tasks`
     from `nginx` to `tls.yml` draws dashed; the two `dependencies` edges into `common` draw in
     a third style. Each carries its directive as a label, and the `postgres` edge also shows
     its `when:` as written.
  5. Double-click the `nginx` role. → The explorer reveals `roles/nginx`, expanding to it.
  6. Press **Enter** with a node focused. → The same reveal happens from the keyboard.
  7. Select the `nginx` role and look at **Properties**. → Every row is greyed with a reason
     beside it, and no row has an editable field. The **Depends on** row's reason names
     `roles/nginx/meta/main.yml`, not the role folder.
  8. Look at the **Toolbox**. → It says the type offers no elements, and nothing can be dragged
     onto the canvas.
  9. Delete `roles/common/` in a text editor or file manager, leaving the app open. → The
     diagram loses the `common` node without being reopened, and the problems panel gains
     `ansible.role-missing` entries pointing at `webservers.yml` and `dbservers.yml`.
 10. Put `roles/common/` back. → Both the diagram and the panel return to their earlier state.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## An Ansible diagram in a subfolder reveals the right file (ansible-structure-diagram, task 26)

Guards the bug the integration flow found: the rule set works in the diagram folder's terms and
core in the project's, so a diagram that is not at the project root once attributed every problem
to a path that did not exist. Automated now, but the panel's reveal is the part a person sees.

- **Preconditions**: a project whose root contains the module's `Fixtures/broken` tree in a
  **subfolder**, registered with a `.adp` inside that subfolder.
- **Actions**: open the Errors and Warnings panel and double-click the
  `The role 'absent-role' has no folder under roles/.` row.
- **Expected**: the explorer reveals `<subfolder>/playbooks/deploy.yml` - the playbook that
  named the role - and not the `.adp`, and not a path that fails to resolve.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

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
- **Verified 2026-09-03** in a 39-character worktree (`a-deliberately-long-worktree-name-x`,
  develop at 228721e7): full `dotnet test --solution` run, exit 0 with tests discovered and
  executed, and no `ADP0001`, `ADP0002` or `MSB3030` anywhere in the output.

## A real pipeline opens, reads and still runs (azure-pipeline-diagram, task 29)

The round trip is this module's headline promise and the one thing no unit test can fully settle:
that a pipeline ADP has opened and edited is still a pipeline Azure DevOps will run. The fixtures
prove byte-identical round trips over a corpus; this proves it over a pipeline somebody's releases
actually depend on.

- **Preconditions**: backend + client running; a project whose folder contains a real
  `azure-pipelines.yml` from a repository you have, ideally one with several stages, a `dependsOn`
  or two and at least one template reference. The folder is a git checkout, so `git diff` works.
- **Actions**:
  1. In the explorer, select the `.yml` and choose **Add as diagram…**, then pick **Azure DevOps
     pipeline**. An `.adp` appears beside it.
  2. Open the `.adp`.
  3. Read the canvas against the file: every stage present, arrows matching each `dependsOn`, and
     an arrow between consecutive stages that declare none.
  4. Right-click a stage with jobs and choose **Show jobs**; then **Hide jobs** again.
  5. Select a stage. In the Property Grid, change **Display name**, then press Enter.
  6. Run `git diff` in the project folder.
  7. Press Ctrl+Z, and run `git diff` again.
- **Expected**:
  - Step 3: the drawing says what the file says. An arrow nobody wrote is drawn differently from
    one that is written down.
  - Step 4: the stage's jobs appear inside it and disappear again. Nothing is written to the file
    at any point — `git diff` after this step alone is empty.
  - Step 6: exactly one changed line, the `displayName` you set. No re-indentation, no reordered
    keys, no quoting changes, no lost comments, no changed line endings.
  - Step 7: `git diff` is empty. Byte for byte, not merely equivalent.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## An edited pipeline still validates against Azure DevOps (azure-pipeline-diagram, task 29)

A file that round-trips byte-identically is safe by construction; a file this module *edited* is
only safe if the lines it wrote are lines Azure accepts. Nothing in ADP knows Azure's schema, so
this check is the only thing that closes that gap.

- **Preconditions**: as above, plus either the Azure Pipelines VS Code extension (which validates
  YAML against the published schema) or an Azure DevOps project you can queue a run in.
- **Actions**:
  1. On the open pipeline, add a stage from the Toolbox, add a job to it, and add a step to that
     job.
  2. Select the new stage and set **Depends on** to an earlier stage from the list.
  3. Save nothing by hand — the edits write themselves.
  4. Open the resulting `.yml` in the editor with the schema extension, or queue it in Azure
     DevOps.
- **Expected**: no schema errors, and the pipeline queues. Specifically the added stage has a job
  and the job has a step — an empty stage or job is a schema error, and this module writes the
  smallest runnable block for exactly that reason.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## A pipeline shows what it cannot do rather than doing it wrongly (azure-pipeline-diagram, task 29)

The editable set is deliberately narrow, and the value of that only shows up when the diagram
refuses something. Worth a look because a refusal that is silent reads as a bug.

- **Preconditions**: a pipeline open, containing a `template:` reference that resolves inside the
  project (so its jobs appear on the canvas).
- **Actions**: right-click a job that came from the template; then select it and look at the
  Property Grid.
- **Expected**: the context menu offers nothing at all for that job. The Property Grid shows its
  properties with a reason beside each naming the template file the value lives in — not a greyed
  box with no explanation.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## A map authored in onlinewardleymaps.com opens looking like the same map (wardley-map, task 26)

The corpus test proves ADP's parser and the reference parser agree about what the text *means*;
nothing automated can say the picture agrees. The failure this guards against is a map that
parses perfectly and is drawn wrong - an axis inverted, a band in the wrong place - which every
unit test in the module would still pass.

- **Preconditions**: backend + client running; a project folder on disk; a browser open at
  [onlinewardleymaps.com](https://onlinewardleymaps.com/).
- **Actions**: in that editor, load the built-in tea shop example (or paste
  `src/diagrams/wardley-map/backend/EtAlii.Adp.Diagram.WardleyMap.Tests/Fixtures/tea-shop.owm`).
  Save the same text as `tea.owm` in the project folder, beside a `tea.adp` whose only line is
  `wardley/map`. Open `tea.adp` in ADP and put the two windows side by side.
- **Expected**: the same components in the same relative places. The anchor sits at the top,
  Cup of Tea below it, Kettle and Power towards the bottom left; every link joins the same pair.
  A component that is high in one and low in the other means the visibility axis is inverted;
  one that is left in one and right in the other means maturity is.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## The four evolution stage labels are legible at the default zoom (wardley-map, task 26)

`WardleyEvolutionTests` pins where the three boundaries are and the canvas test asserts the
labels exist in the DOM. Neither can say whether a reader can read them - and the labels carry
their parentheticals ("Product (+rental)", "Commodity (+utility)") precisely because the short
forms are a different claim, which only helps if the long forms fit.

- **Preconditions**: an `.owm` open in a diagram tab, at the zoom the tab opens with.
- **Actions**: look along the bottom of the map without zooming or panning.
- **Expected**: four labels - Genesis, Custom Built, Product (+rental), Commodity (+utility) -
  each fully readable, none clipped at the edge of the canvas, none overlapping its neighbour,
  and each sitting under the band it names.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## Dragging a component and reopening the file elsewhere shows it moved (wardley-map, task 26)

The round-trip and command tests assert ADP writes what it means. This asserts the other
ecosystem agrees, which is the whole point of Requirement 3: a map ADP has touched must still
be a map onlinewardleymaps.com can open, with the drag visible in it.

- **Preconditions**: `tea.adp` / `tea.owm` open in ADP, as above.
- **Actions**: drag `Kettle` clearly to the right (more evolved) and drop it. Open `tea.owm` in a
  text editor, copy its whole text, and paste it into onlinewardleymaps.com.
- **Expected**: the file's `component Kettle [...]` line carries a larger second number than
  before and the same first number; the rest of the file - comments, blank lines, the order of
  the statements, the line endings - is untouched; and the map in the browser draws Kettle in
  its new place with everything else where it was.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## An element selected on the canvas fills the property grid (wardley-map, task 26)

`WardleyContextPropertyProviderTests` proves the rows are contributed and `WardleyMapFlowTests`
proves they reach a caller. Neither can say the panel renders them usefully: a read-only reason
is a `display: block` span with no truncation, so one that is too long grows its row rather than
clipping, and that is only visible on screen.

- **Preconditions**: `tea.adp` open in a diagram tab, with the Property Grid panel visible.
- **Actions**: click `Kettle` on the canvas and read the panel. Then click the Evolution stage
  row and try to type into it.
- **Expected**: the panel's heading is `Kettle`; the rows are grouped under Identity, Position
  and Strategy; Visibility and Maturity show the file's own numbers; Evolution stage shows
  `Custom Built` and cannot be typed into, with a sentence beside it saying to change Maturity
  instead - one or two lines, not a paragraph that pushes the rest of the panel down.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## The ruler stays fixed to the view while its labels slide with the content (timeline-diagram, task 31)

- **Preconditions**: a project holding a `.tml` with several elements spanning a few months
  (for example three elements over January-June); the timeline open on the canvas.
- **Actions**: note the ruler strip's vertical position at the bottom of the view and the
  horizontal position of one month label. Drag the canvas background right by roughly 80 pixels
  (a pan), including some vertical movement.
- **Expected**: the ruler strip itself does not move at all - it stays at the bottom of the view
  whatever the pan's vertical component - while the month label slides horizontally by exactly
  the pan distance, staying over the time it names. A label panned out of the view disappears
  rather than piling up at the edge.
- **Verified 2026-09-01** against the running app: ruler top identical before and after a pan
  with a 100px vertical component; "Mar 2026" slid exactly +80px for an 80px pan; "2026"
  disappeared once panned off-view.

## The ruler's labels change their unit with the zoom (timeline-diagram, task 31)

- **Preconditions**: as above.
- **Actions**: zoom in (ribbon Zoom In or mouse wheel) until the visible span is a few weeks;
  then zoom out far past the starting view.
- **Expected**: the labels change unit rather than crowding or vanishing - months at the
  starting span, day labels ("Mar 12", "Mar 19") once weeks are visible, then years and
  multi-year steps ("2020", "2022", "2024") when zoomed far out. At no zoom level do labels
  overlap or disappear entirely.
- **Verified 2026-09-01** against the running app across that whole range.

## Connections follow a drag while it is in progress, and Escape abandons it (timeline-diagram, task 31)

- **Preconditions**: as above, with at least one connection between two elements.
- **Actions**: press on a connected element and move the pointer without releasing; watch the
  connection's curve and the hint above the element; press Escape; then check the `.tml` file.
- **Expected**: the curve is redrawn continuously as the element moves - not only on release -
  and the hint shows the times and row the element would land on (for example
  "2026-01-31 · row 1"). Escape returns the element and its curves to where they started, and
  the file on disk is byte-for-byte untouched.
- **Verified 2026-09-01** against the running app: the connection path changed mid-drag, the
  hint read "2026-01-31 · row 1", Escape restored the original path, and the file still read
  `begin: 2026-01-05`.

## A toolbox drop lands an Element where it was dropped (timeline-diagram, tview pass)

- **Preconditions**: a `.tml` timeline open on the canvas; the Toolbox showing Element and Moment.
- **Actions**: drag Element from the Toolbox and drop it on empty canvas at a chosen time and
  row; repeat over an existing element.
- **Expected**: a "New element" appears at the dropped time and row immediately - no dialog -
  and one undo removes it. Dropping never answers "That action is not available for this item"
  (the placement target must discover the add actions, or executing by id resolves nothing).
- **Verified 2026-09-01** against the running app after fixing exactly that: a placement target
  that discovered no actions made every drop a no-op with that message.

## The context menu removes elements and relations (timeline-diagram, tview pass)

- **Preconditions**: as above, with at least one relation.
- **Actions**: right-click an element with no relations and choose Remove; right-click a
  relation and choose Remove relation; right-click an element with relations and confirm the
  dialog that names the relation count.
- **Expected**: each removal happens and is one undo away. A relation-free Remove must act
  immediately - an execute that answers Completed without dispatching has done nothing, because
  the commit leg only runs after a dialog.
- **Verified 2026-09-01** against the running app after fixing exactly that: menu-Remove on a
  relation-free element silently did nothing until the execute leg dispatched the command.

## A relation dragged onto empty space creates the element it reaches (timeline-diagram, tview pass)

- **Preconditions**: as above; an element selected so its anchors show.
- **Actions**: drag from a side anchor and release over empty canvas.
- **Expected**: a "New element" appears at the release point with a relation from the source to
  it - one history entry, one undo removing both. Releasing back on the source cancels quietly.
- **Verified 2026-09-01** against the running app: elements 4→5 and relations 2→3 from one
  gesture.

## Right-drag pans; Tab and Enter add; scrollbars pan (timeline-diagram, tview pass)

- **Preconditions**: as above.
- **Actions**: right-press empty canvas and drag (no browser menu may appear); select an
  element and press Tab, then Enter; drag each scrollbar thumb.
- **Expected**: the right-drag pans exactly as a left-drag; Tab adds an element two days after
  the selected one on its row, Enter adds one below on the next row, neither asking anything;
  the thumbs pan the view within the content's extent.
- **Verified 2026-09-01** against the running app (Tab and right-drag live; Enter and the
  thumbs through the same handlers in the test suite).

## A dependency graph shows no ruler, no dates and no moments (dependency-graph, task 4.1)

- **Preconditions**: `src/examples/diagrams/dependency-graph/example-1/` opened as a project,
  with `services.dgr` on the canvas.
- **Actions**: look at the whole canvas; open the Toolbox; select a node and read the property
  grid; select an edge and read it too.
- **Expected**: no ruler strip along any edge, no date anywhere on the canvas or in either grid,
  and no moment marker. The Toolbox offers **Node** and nothing else. A node's grid rows are
  Label, X and Row - begin, end and duration are **absent**, not blank. An edge's rows are its
  label plus its two ends, read-only.
- **Why manual**: the absences are the requirement (Requirement 3.3 calls a ruler, a date or a
  moment appearing here a defect), and an absence in a running UI is what a test suite is least
  able to see.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## Every edge visibly points at its dependency (dependency-graph, task 4.1)

- **Preconditions**: as above.
- **Actions**: follow the edge from Checkout web to API gateway, and the one from Order service
  to Orders database; then select a node, drag from its **left** anchor onto another node, and
  look at the new edge.
- **Expected**: each edge carries an arrowhead at the end it depends on - the arrow on the first
  points at API gateway, not at Checkout web. The left-anchor drag produces an edge pointing at
  the node the drag *started* from, because what depends on a node arrives at it.
- **Why manual**: an arrowhead drawn at the wrong end still renders a plausible graph, so the
  failure is silent to anything that only checks that an edge exists.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## Edges follow a drag while it is in progress, and Escape abandons it (dependency-graph, task 4.1)

- **Preconditions**: as above, with at least one edge between two nodes.
- **Actions**: press on a connected node and move the pointer without releasing; watch the
  edge's curve and the hint above the node; press Escape; then check the `.dgr` file.
- **Expected**: the curve is redrawn continuously as the node moves - not only on release - and
  the hint shows the coordinate and row the node would land on (for example `612 · row 1`),
  never a date. Escape returns the node and its curves to where they started, and the file on
  disk is byte-for-byte untouched.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## A toolbox drop lands a Node where it was dropped (dependency-graph, task 4.1)

- **Preconditions**: as above; the Toolbox showing Node.
- **Actions**: drag Node from the Toolbox and drop it on empty canvas at a chosen place; repeat
  over an existing node.
- **Expected**: a "New node" appears at the dropped coordinate and row immediately - no dialog -
  and one undo removes it. Dropping never answers "That action is not available for this item"
  (the placement target must discover the add action, or executing by id resolves nothing).
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## The context menu removes nodes and dependencies (dependency-graph, task 4.1)

- **Preconditions**: as above, with at least one edge.
- **Actions**: right-click a node with no edges and choose Remove; right-click an edge and
  choose Remove dependency; right-click a node with edges and confirm the dialog that names the
  dependency count.
- **Expected**: each removal happens and is one undo away. A dependency-free Remove must act
  immediately - an execute that answers Completed without dispatching has done nothing, because
  the commit leg only runs after a dialog. The confirmation counts dependencies at **both** ends
  of the node, not only the ones leaving it.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## A dependency dragged onto empty space creates the node it reaches (dependency-graph, task 4.1)

- **Preconditions**: as above; a node selected so its anchors show.
- **Actions**: drag from the right anchor and release over empty canvas; undo; then drag from
  the left anchor and release over empty canvas.
- **Expected**: a "New node" appears at the release point with an edge between it and the source
  - one history entry, one undo removing both. From the right anchor the source depends on the
  new node; from the left anchor the new node depends on the source. Releasing back on the
  source cancels quietly.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## Right-drag pans; Tab and Enter add; scrollbars pan (dependency-graph, task 4.1)

- **Preconditions**: as above.
- **Actions**: right-press empty canvas and drag (no browser menu may appear); select a node and
  press Tab, then Enter; drag each scrollbar thumb.
- **Expected**: the right-drag pans exactly as a left-drag; Tab adds a node a fixed step to the
  right on the same row and Enter adds one on the next row at the same coordinate, both depended
  upon by the node they grew from and neither asking anything; the thumbs pan the view within
  the content's extent.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## The text editor is drawn in the application's colors, caret included (modular-text-editors, manual pass)

Guarded here because it is pure styling: without a theme extension CodeMirror runs its
built-in light theme regardless of the app's, and the failure mode is subtle - the text
stays perfectly editable while the black caret is invisible on the dark surface and the
gutter keeps its light-grey background. jsdom loads neither the app's CSS variables nor
CodeMirror's injected styles, so no unit test can see any of this.

- **Preconditions**: backend + client running with the OS (or browser) in dark mode; a
  project holding a plain text file and a markdown file.
- **Actions**: open each file in its editor tab and click into the text.
- **Expected**: in both editors the line-number gutter has the same dark background as the
  text surface with lighter, muted numbers (not a light-grey strip with dark numbers); a
  blinking caret is clearly visible at the click position; and switching the OS to light
  mode flips the whole editor - gutter, text, caret - along with the rest of the app.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## An editor save is one undo away on the ribbon (editor/undo batch)

The save travels through the project history (`SaveTextFileCommand`), so the ribbon's
Undo/Redo must reflect it - verified live, and kept here because it needs a running app,
a keyboard save and the ribbon together.

- **Preconditions**: backend + client running; a project with a markdown or plain-text
  file open in its editor tab; ribbon Undo greyed ("There is nothing to undo").
- **Actions**: type a marker into the editor, press Ctrl+S, then click the ribbon's Undo;
  then click Redo.
- **Expected**: after the save the ribbon Undo enables; Undo restores the file's previous
  content on disk (and the editor follows once it reloads the pushed change); Redo brings
  the save back. No conflict banner should appear for the editor's own save.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## One build, one number, four places (github-build-pipeline task 3.1; two halves automated by contracts-and-build-hygiene task 1)

The version below the login panel, the version stamped into the assemblies, the release
tag, and the ZIP's name must all be the same number - a claim only a person with a real
release artifact can check end to end.

- **Preconditions**: a published GitHub release with its ZIP asset (or, before the first
  release exists: a local `dotnet publish` of the service - the project's own
  `PublishClientApp` target builds the client and places it in `wwwroot/`, so no copy
  step is needed and adding one nests a second bundle at `wwwroot/dist/`).
- **What the pipeline now settles, and what it does not.** Since contracts-and-build-hygiene
  task 1 (`a95c3b51`) the release job passes `--target ${{ github.sha }}` and then re-reads the
  tag from the remote, failing the run when it does not point at the commit that was built. So
  **the tag's commit is checked** on every release. Separately, **the tag name and the ZIP's file
  name cannot disagree** - both are written from the single `steps.version.outputs.semver`, which
  is construction rather than a check, and worth knowing because no test would catch it if the
  two were ever computed twice.
  **The assembly stamp is NOT checked by the pipeline.** Nothing in the job opens the artifact or
  reads `ProductVersion`; the assembly and the tag agree because both descend from one `nbgv`
  computation, which is an argument, not evidence. So the manual half is still two things: **the
  login line inside the running app**, and **the assembly stamp inside the downloaded ZIP**.
- **Actions** (the manual half): download and unzip the release; run `dotnet EtAlii.Adp.Backend.Service.dll`
  (requires the .NET 10 runtime); browse to the listening address; read the line below the
  login panel. Compare it with the release tag, the ZIP file name, and
  `[System.Diagnostics.FileVersionInfo]::GetVersionInfo("EtAlii.Adp.Backend.dll").ProductVersion`.
- **Expected**: all four carry the same version, in Nerdbank.GitVersioning's two shapes -
  the tag and the ZIP name spell SemVer2 (`0.1.402-alpha`), while the assemblies and the
  login line append the commit as build metadata (`0.1.402-alpha+99796ebafe`). Deliberate,
  not drift: the release names the version, the running binary names the commit it came
  from. Anything that disagrees on the digits before the `+` is the failure this check is
  looking for. The login line is quiet and centered under the card; killing the backend and
  reloading the page shows no version line at all rather than a stale one.
- **First release from the corrected job, 2026-09-04**: push `eedc8d6e` produced exactly one
  tag, `v0.1.688-alpha`, pointing at `eedc8d6e` - the commit that was pushed. **This does not by
  itself prove the fix**, and the entry says so deliberately: the push before it produced a
  correct tag too, from the *broken* job, because that commit also happened to be the branch tip
  when the API call ran. No other session pushed during this run, so the conditions that expose
  the defect never arose. What did change is the failure mode - the tag is pinned by `--target`
  rather than resolved from the default branch, and a mismatch now fails the run instead of
  passing silently. The decisive observation is still owed: the first push that races another.
  The ZIP name and login line for this release have not been checked by anyone yet.
- **Verified end to end 2026-09-04**, against the first release this pipeline has ever
  produced. The run went green on the Linux runner and tagged `v0.1.402-alpha` at commit
  `99796eba` - the commit that was pushed. The user downloaded the ZIP, ran
  `dotnet EtAlii.Adp.Backend.Service.dll` from it, and the line below the login panel read
  **`0.1.402-alpha+99796ebafe`**. Four places, one number. Note this could only be finished
  by someone signed in to GitHub: the repository is private, and a development machine with
  neither `gh` nor a token cannot fetch a release asset at all - the unauthenticated API
  answers `Not Found`.
- **Publish layout confirmed 2026-09-04**: `dotnet publish` alone produces `wwwroot/` holding
  `index.html`, `favicon.svg` and `assets/`, and the published `EtAlii.Adp.Backend.dll` reads
  ProductVersion `0.1.388-alpha+e051580749`, naming the commit it was built from. This run
  checked the layout and the assembly stamp only - not the login line, which the entry below
  covers - and it was a branch build rather than a release, so tag and ZIP name are untested.
- **Partially verified 2026-09-03** against a locally running developer build: the login line
  and `EtAlii.Adp.Backend.dll`'s ProductVersion both read `0.1.80-alpha+af55382d1f` - the
  stamp's first live trip from assembly through gRPC to the login panel. The release tag and
  ZIP name halves still await the first published release.

## Shared scroll view: thumbs track the view, dragging a thumb pans (small-refinements, task 1.4)

The unit tests drive the bars with hand-sized tracks; only the running app shows the thumbs
and the canvas moving together. Checked on the mindmap and the timeline side by side, since
the timeline was migrated onto the same component and must look unchanged.

- **Preconditions**: backend + client running; a project with a mindmap and a timeline each
  open in a diagram tab.
- **Actions**: on the mindmap - wheel-zoom in and out, drag the canvas, then drag each
  scrollbar thumb; repeat the same three gestures on the timeline.
- **Expected**: on both diagrams the thumbs shrink on zoom-in, grow on zoom-out, and slide as
  the canvas is dragged; dragging a thumb pans the view in that axis only, without changing
  the zoom. On a freshly opened (fitted) mindmap the thumbs claim nearly the whole track, and
  dragging one takes over from the fitted state just as a canvas drag does. The timeline's
  bars sit above its ruler, exactly where they were before the migration.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## Rewritten examples look presentable in the running app (small-refinements, task 2.5)

Executed 2026-09-03 against the task 2.2/2.3 rewrites, from the spec worktree on its own
port pair. Re-run this after any future example rewrite.

- **Preconditions**: backend + client running; a project opened on `src/examples`.
- **Actions**: open `diagrams/mindmap/example 1` (both `mindmap.mm` and `design.mm`) and
  `diagrams/timeline/example-1` and `example-2`; zoom and pan around each, dragging the
  scrollbar thumbs.
- **Expected**: every node and element carries real themed text (rendering engine,
  developer onboarding, product/data-platform roadmaps) - no `Test`, `sdfsdf`, `New
  element` or keyboard-mash anywhere; the mindmaps show their full trees, the timelines
  show distinct, non-overlapping elements across their rows; thumbs track zoom and pan on
  both canvases, and dragging a thumb pans.
- **Result 2026-09-03**: pass - all four diagrams presentable on first look; mindmap
  fitted-state thumb drag and timeline vertical thumb drag both pan as the shared scroll
  view intends.

## A simulated job run ripples the DAG, honoring run_if and outcome (databricks-diagrams, task 5.3)

Guarded by `useSimulatedRun.test.ts` for the state machine itself; kept here because the visible
show - badges walking the canvas with nothing written anywhere - is the requirement, and only a
running app shows it.

- **Preconditions**: backend + client running; the lakehouse example open as the job diagram
  (`src/diagrams/databricks/examples/lakehouse/resources/nightly-ingest.adp`).
- **Actions**: right-click any task and choose **Run job (simulated)**; watch the ripple; then
  check the file on disk and the Edit menu.
- **Expected**: a "Simulated: job run" banner appears; `land_raw` runs first, `quality_gate`
  after it, then `publish` and `refresh_dashboard` follow the `"true"` outcome while
  `alert_on_empty` greys out as skipped; every touched node wears a simulation border. Neither
  `nightly-ingest.yml` nor the `.adp` changes, and undo offers nothing new - the show never
  reaches the history. Clicking the banner dismisses the whole state.
- **Result 2026-09-03**: pass - banner up, land_raw ran first, the gate followed, publish and
  refresh_dashboard went succeeded-green down the "true" edge while alert_on_empty faded as
  skipped; "finished" banner; git status clean on the example, Undo stayed "nothing to undo";
  dismiss cleared everything.

## A simulated deploy progresses over a target's resources (databricks-diagrams, task 5.3)

- **Preconditions**: the lakehouse example open as the bundle diagram (`databricks.adp`).
- **Actions**: right-click the `prod` target frame and choose **Deploy here (simulated)**.
- **Expected**: a "Simulated: deploy" banner appears and the bundle's resources light up
  running → succeeded one after another, left to right; the banner ends with "finished". No
  file changes, no history entry.
- **Result 2026-09-03**: pass - "Deploy here (simulated)" on the prod frame played the ripple,
  bronze_to_gold ended succeeded-green, the banner reached "finished", git status stayed clean
  and the history untouched.

## A reposition lands in the .adp, survives a reopen, and undoes byte-for-byte (databricks-diagrams, task 5.3)

Guarded end-to-end by `DatabricksFlowTests.AReposition_LandsInTheAdp_SurvivesReopen_AndUndoReturnsIt_WithTheBodyUntouchedThroughout`;
kept here because the tab-close/reopen half runs through the real shell.

- **Preconditions**: the lakehouse example open as the job diagram; `nightly-ingest.adp` and
  `nightly-ingest.yml` visible in an editor or on disk.
- **Actions**: drag the `publish` task somewhere distinctive; close the diagram tab; reopen it;
  then press undo.
- **Expected**: after the drag, `nightly-ingest.adp` gains a `layout:` block with a
  `task:publish` entry while `nightly-ingest.yml` is byte-identical to before; the reopened
  diagram shows `publish` exactly where it was dropped; undo removes the entry (and the block,
  if it was the only one) and the task returns to its computed place on every open connection.
- **Result 2026-09-03**: pass - the drag wrote `task:publish: 548.546 377.104` into a fresh
  layout: block with nightly-ingest.yml byte-identical (git saw only the .adp); the reopened
  tab showed publish exactly at the drop; undo removed entry and block, git status went clean,
  and the canvas snapped publish back to its computed spot.
## Explorer state icons are distinguishable in both modes (small-refinements, task 3.5)

The unit tests assert the classes; only a person (or a computed-style probe) can say the
three colors actually read apart on both surfaces. Checked live from the spec worktree.

- **Preconditions**: backend + client running; a project on `src/examples` open; the
  ansible-structure example expanded down to `infrastructure/`.
- **Actions**: look at the tree in dark mode, then in light mode (emulate
  `prefers-color-scheme` or switch the OS theme).
- **Expected**: registrations and registered subjects (`structure.adp`, `mindmap.mm`, the
  `infrastructure` folder once expanded) wear the accent limegreen; registrable files
  (`*.yml`, `*.md`) a clearly darker green; unclaimed files (`ansible.cfg`, `Makefile`
  with no make editor deployed) stay the muted neutral - three visibly distinct colors in
  BOTH modes, and folders without a folder-subject registration never green.
- **Result 2026-09-03**: pass - dark mode: limegreen / rgb(63,145,66) / muted slate;
  light mode: limegreen / rgb(22,101,52) / muted slate. Found and fixed live: the
  folder's own state update was swallowed on listing-triggered scans, so an expanded
  `infrastructure` stayed neutral until the raise was made unconditional for the folder.

## The wardley-map and azure-pipeline toolboxes fill for an open diagram (documentation, task 9)

Found while capturing the readme screenshots: with a wardley map or an Azure pipeline open
from the combined `src/examples/` project, the Toolbox panel stays on "Open a diagram to see
the elements its type offers" - while the c4, mindmap, timeline and dependency-graph tabs all
fill their toolbox on the same flow. Deterministic across headless capture runs (byte-identical
screenshots), so not a timing race. Both modules register an `IDiagramToolboxProvider`, and the
wardley one is covered by a passing `DescribeToolbox` integration test at the project root -
the reproduction here differs in the document living in a subfolder whose name contains a
space, which is one candidate to rule out.

- **Preconditions**: backend + client running; `src/examples/` added as a project.
- **Actions**: open `diagrams/wardley-map/example 1/tea.adp` (nested under `tea.owm` in the
  explorer) in a diagram tab; look at the Toolbox panel. Repeat for
  `diagrams/azure-pipeline/example 1/multi-stage.adp`.
- **Expected**: the wardley toolbox lists its eight entries (Component, Anchor, Market,
  Ecosystem, Submap, Pipeline, Note, Annotation) and the azure-pipeline toolbox lists that
  module's entries - not the "Open a diagram" placeholder. Once this passes, retake
  `docs/screenshots/wardley-map.png` and `docs/screenshots/azure-pipeline.png` per
  `docs/screenshots/readme.md`.
- **Result 2026-09-03**: pass - the cause was client-side: neither WardleyCanvas nor
  PipelineCanvas called useRegisterDiagramToolbox(useToolboxItems(...)), so the panel was
  never told a diagram was open and DescribeToolbox was never asked. The folder-name space
  was ruled out: mindmap shares it and worked, and a new backend integration test serves the
  full wardley palette from "example 1/tea.adp" (DiagramToolboxFlow). Both canvases wired
  (AnsibleCanvas too, so its read-only type reports "offers no toolbox elements" instead of
  the placeholder), each guarded by a canvas test that fails while unregistered. Verified
  live: tea.adp lists all eight entries, multi-stage.adp lists Stage / Job / Deployment job /
  Script step. Both screenshots retaken; capture.mjs now raises dblclick in the page, since
  a fresh puppeteer-core install opened nothing through CDP click({ clickCount: 2 }).

## The Add flow lists helm/chart on a folder, and the registration opens the chart (helm-charts, task 7.4)

- **Preconditions**: backend + client running; `src/examples/` added as a project; a scratch
  folder inside the project containing a `Chart.yaml` (name + version) and nothing else.
- **Actions**: right-click the scratch folder in the explorer, choose Add; find `helm/chart`
  in the choice tree, pick it, accept the suggested name; double-click the created `.adp`.
- **Expected**: `helm/chart` appears in the tree with its ship-wheel icon (listed, not
  pre-selected - content-based pre-selection is a recorded finding, not a shipped feature);
  the created `.adp` holds only the MIME line; the diagram opens showing the chart node and
  the metadata band.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## Every helm node kind navigates on double-click (helm-charts, task 7.4)

- **Preconditions**: backend + client running; `src/examples/` added as a project.
- **Actions**: open `diagrams/helm-charts/nginx/helm-chart.adp`; double-click, in turn: the
  chart node, `values.yaml`, `values-prod.yaml`, a template, the `_helpers.tpl` partial, the
  `charts/common` subchart, and the `Chart.lock` node; then double-click the `cache`
  dependency node.
- **Expected**: each file-backed node reveals its artifact in the explorer (the subchart
  reveals its folder, beside its own registration if one exists); the dependency node reveals
  nothing, and its Properties panel says the declaration lives in `Chart.yaml` with every row
  read-only.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## A helm reposition lands in the .adp, survives a reopen, and undoes byte-for-byte (helm-charts, task 7.4)

- **Preconditions**: backend + client running; `src/examples/` added as a project;
  `diagrams/helm-charts/hello-world/helm-chart.adp` unmodified (one MIME line).
- **Actions**: open the diagram; drag the chart node somewhere new; inspect the `.adp` in a
  text editor; close and reopen the diagram tab; press the undo shortcut; inspect the `.adp`
  again; run `git status` over the example.
- **Expected**: after the drag the `.adp` carries a `layout:` block with one `chart: <x> <y>`
  entry and no chart file changed; the reopened tab shows the node at the dragged spot; undo
  returns the `.adp` to its single MIME line; `git status` shows the example clean at the end.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## The over-budget RDF file draws its first thousand and says so (rdf-diagram, task 5.3)

The drawn-element budget's honest degradation (Requirement 8): the whole Nobel laureates
dataset holds 1,657 resources against the 1,000-node budget, so the diagram must show the
truncated first-N view with its banner, and withhold edits naming the reason.

- **Preconditions**: backend + client running; `src/examples/` added as a project.
- **Actions**: open `diagrams/rdf/nobel/laureates.adp`; read the banner; right-click any
  resource card and try an edit from its menu.
- **Expected**: the canvas draws cards (categories, prizes, then laureates in document order)
  with a "Showing 1000 of 1657 resources — edits are withheld on this truncated view" banner
  at the top; the context menu offers no edits, and any attempted edit answers with the
  withheld sentence rather than touching the file.
- **Result 2026-09-03**: pass - the banner reads exactly as expected, the layout bands are
  visible (categories, then prizes, then laureates), and right-clicking `laureate:1` (Wilhelm
  Conrad Röntgen, correctly first) selects the card and opens no menu at all: nothing
  discovers under truncation, so there is no edit to attempt.

## A statement splice leaves the neighbouring bytes untouched (rdf-diagram, task 5.3)

The splice discipline live: removing one statement of a `;` predicate list takes exactly its
own line, keeping every neighbouring byte - comments, abbreviations and the `,` object list on
the line below it included. (The intra-line `,`-member splice itself is byte-pinned by the
`RdfWriter` unit tests; this check exercises the discipline through the whole running stack.)

- **Preconditions**: backend + client running; `src/examples/` added as a project; a git
  client or diff view on `src/examples/diagrams/rdf/w3c-turtle/example-1.ttl`.
- **Actions**: open `diagrams/rdf/w3c-turtle/example-1.adp`; select the edge from
  `<#spiderman>` to `<#green-goblin>` (`rel:enemyOf`); remove the statement from its context
  menu; inspect the file's diff; then undo.
- **Expected**: only the one statement's tokens leave the file - the surrounding lines,
  comments and abbreviation style are byte-identical - and one undo returns the file
  byte-for-byte, with the removal one redo away.
- **Result 2026-09-03**: pass - the diff after removal was exactly one line
  (`    rel:enemyOf <#green-goblin> ;` gone), and `cmp` confirmed byte identity after undo.
  Found and fixed live before the check could run at all: nothing on the canvas could SELECT
  an edge - `renderEdge` wired no handlers, so the removal Requirement 6 promises was
  unreachable by mouse. The edge `<g>` now selects on click and opens its menu on
  right-click, with an invisible fat `canvas-connection-hit` twin because a thin stroke is no
  target, guarded by a canvas test that fails while unwired.

## A reposition on a vendored example survives reopen and undoes byte-for-byte (rdf-diagram, task 5.3)

Layout lives in the registration, never in the RDF (Requirement 4), proven on real Wikidata
data rather than a fixture.

- **Preconditions**: backend + client running; `src/examples/` added as a project.
- **Actions**: open `diagrams/rdf/wikidata/marie-curie.adp`; drag the `wd:Q7186` card to a
  clearly different spot; close and reopen the diagram; then undo.
- **Expected**: the position lands in `marie-curie.adp`'s `layout:` block keyed
  `res:http://www.wikidata.org/entity/Q7186` while `marie-curie.ttl` never changes by a byte
  (its LF endings included); the reopened diagram draws the card at the stored spot; one undo
  returns the `.adp` byte-for-byte.
- **Result 2026-09-03**: pass - the drag wrote
  `res:http://www.wikidata.org/entity/Q7186: 866.348 158.78` into the block, `cmp` held the
  `.ttl` byte-identical throughout, the reopened tab drew the card at the stored spot, and
  one undo returned the `.adp` byte-for-byte. The validator panel showed "No problems found"
  on the real Wikidata export the whole time.

## A language chip shows a translation gap, and only a gap (skos-diagram, task 5.2)

The label chain is pinned exhaustively as a pure function, and the chip's condition is decided
backend-side so the chip can never disagree with the label beside it. What no test can settle is
whether a reader takes the point: a quiet tag beside a name is meant to read as "this vocabulary
has not been translated here", not as decoration.

- **Preconditions**: backend + client running; a project holding a multilingual vocabulary whose
  translations are incomplete (the vendored EuroVoc extract is one), registered as `w3c/skos`
  with a `language:` header naming a language the vocabulary does not translate everything into
  — the header sits after `body:` and before `layout:`.
- **Actions**: open the diagram and read a band of concepts.
- **Expected**: concepts translated into the header's language show their translated label and
  no chip; concepts that are not show a fallback label with a small language tag beside it. The
  chip is legible but quiet - it should never out-shout the label. Removing the `language:`
  header and reopening moves the chips to the concepts that lack English instead.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## A misplaced language: header changes nothing, and says why (skos-diagram, task 5.2)

The hazard this guards is silent: core's registration scan stops at the first line it does not
recognise, so a `language:` line above `body:` would sever the diagram from its document with no
error at all. The reading refuses to honour such a header and reports it instead - the panel
message is the only place a user learns why their header did nothing.

- **Preconditions**: as above.
- **Actions**: move the `language:` line above the `body:` line, save, and reopen the diagram.
- **Expected**: the diagram still opens on its document - the pairing is intact - and draws in
  the default language order rather than the header's. The Errors and Warnings panel carries one
  `skos.misplaced-header` entry naming the line and saying to move it below `body:`. Moving it
  back and reopening restores the header's language and clears the entry.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## Filing a concept under another is one gesture and one undo (skos-diagram, task 5.2)

The hierarchy is the picture, so the gesture that builds it is the one to see working end to
end: dragged from the top anchor, written as a single `skos:broader` on the narrower end - the
direction thesauri are authored in - with no inverse invented, and reversible byte-for-byte.

- **Preconditions**: a vendored SKOS example open as `w3c/skos`, in a git checkout so
  `git diff` works; two concepts visible that are not yet related.
- **Actions**: select a concept, drag from its **top** anchor onto another concept and release;
  run `git diff`; press Ctrl+Z; run `git diff` again. Then repeat from the **side** anchor.
- **Expected**: the top-anchor drag adds exactly one line, a `skos:broader` naming the target,
  on the dragged concept - no `skos:narrower` anywhere, no re-indentation, no reordered keys, no
  changed line endings. The canvas re-layers so the concept sits below its new parent. Ctrl+Z
  leaves `git diff` empty. The side-anchor drag does the same with one `skos:related` line, and
  does not change the layering.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## A shared variable draws once, with edges crossing region borders (sparql-diagram, task 5.3)

The one drawing rule the whole module is built around: every occurrence of a variable is one
node, so its degree on the canvas is its join count in the query (Requirement 4.1), and an
`OPTIONAL` pattern that mentions an outside variable reaches out to it rather than drawing a
second copy inside the region (Requirement 3.2).

- **Preconditions**: backend + client running; `src/examples/` added as a project.
- **Actions**: open `diagrams/sparql/w3c-sparql/optional.adp`. Count the nodes labelled `?x`,
  `?name` and `?mbox`. Look at where `?mbox` sits relative to the dashed `OPTIONAL` frame, and
  follow the edge that ends at it.
- **Expected**: exactly one node per variable name - three in total, plus no others. `?mbox` is
  used only inside the `OPTIONAL`, so its node sits inside the dashed frame; `?x` is used both
  inside and outside, so its node sits outside and the `OPTIONAL`'s edge crosses the frame's
  border to reach it. The property grid on `?x` states its join count.

- **Result 2026-09-05**: **passes, every clause.** Run against a worktree build with
  `src/examples` open; the user signed in. `diagrams/sparql/w3c-sparql/optional.adp`.

  **One node per variable, three in total, no others.** The canvas holds exactly three
  `sparql-node-variable` elements - `?name`, `?x` and `?mbox` - so no occurrence is drawn twice.

  **`?mbox` sits inside the dashed `OPTIONAL` frame and `?x` outside it.** Measured rather than
  eyeballed: the region's box is x 844-1116, `?mbox`'s centre falls inside it, `?x`'s and
  `?name`'s fall outside, and the region's stroke is dashed `4px, 4px`.

  **The edge crosses the border rather than a second `?x` being drawn.** The `foaf:mbox` edge
  spans x 794 to 896 while the frame's left border is at 844, so it starts outside the region at
  `?x` and ends inside it at `?mbox` - which is Requirement 3.2 exactly.

  **The property grid on `?x` states its join count**: `STRUCTURE / Joins: 2`, matching its two
  triple patterns. Every row carries the read-only reason *"This diagram reads the query; edit
  the .rq file in a text editor and the diagram follows."*

## The projection mark matches the SELECT list (sparql-diagram, task 5.3)

What leaves a query should read off the canvas without consulting the header (Requirement 4.2).

- **Preconditions**: as above.
- **Actions**: open `diagrams/sparql/w3c-sparql/optional.adp` and note which variable nodes
  carry the projection mark; compare against the `SELECT ?name ?mbox` line in the header band.
  Then open `diagrams/sparql/w3c-sparql/aggregate.adp` and do the same.
- **Expected**: in the first, `?name` and `?mbox` are marked and `?x` is not. In the second,
  `?totalPrice` is marked - the alias the query projects - and `?org`, `?auth`, `?book` and
  `?lprice` are not, with `GROUP BY ?org` and `HAVING (SUM(?lprice) > 10)` shown in the header
  band rather than drawn on the canvas.

- **Result 2026-09-05**: **passes, both files.** Same session and build as the check above.

  **`optional.adp`**: `?name` and `?mbox` carry `sparql-node-projected` and `?x` does not, which
  is `SELECT ?name ?mbox`. Two projection marks are drawn, one per projected variable. `?x`'s
  property grid independently reports `Projected: No`, so the mark and the grid agree.

  **`aggregate.adp`**: `?totalPrice` is the only projected node - the alias the query projects -
  while `?org`, `?auth`, `?book` and `?lprice` are not. The header band reads
  `SELECT … GROUP BY ?org HAVING (SUM(?lprice) > 10)`, so the modifiers are stated in the band
  and not drawn on the canvas, which is what Requirement 4.2 asks for.

## A reposition leaves the query untouched in a visible diff (sparql-diagram, task 5.3)

The read-only position, proven on a real vendored query rather than a fixture: the one gesture
this diagram has writes the registration and never the `.rq` (Requirements 5.1, 6.3).

- **Preconditions**: as above; a shell in `src/examples/diagrams/sparql/uniprot/`.
- **Actions**: `git status` to confirm a clean tree. Open
  `diagrams/sparql/uniprot/proteome-location-of-gene.adp`; drag the `?proteomeData` node to a
  clearly different spot; close and reopen the diagram; then undo. Run `git status` and
  `git diff` after each step.
- **Expected**: the position lands in `proteome-location-of-gene.adp`'s `layout:` block keyed
  `var:proteomeData`, and `git diff` never shows a single changed line in
  `proteome-location-of-gene.rq` at any point; the reopened diagram draws the node at the
  stored spot; one undo returns the `.adp` byte-for-byte, leaving the tree clean again.

- **Result 2026-09-05**: **passes, every step.** Re-run from a fresh sign-in after an earlier
  attempt recorded here was withdrawn — see the correction below, which matters more than the pass.

  `git status` clean before. Dragging `?proteomeData` moved **that node alone** — `dx 11, dy 212`
  while the other three moved `0, 0`, so the canvas did not pan — and wrote
  `var:proteomeData: 516.659 411.066` into a `layout:` block the registration did not have,
  keyed exactly as this check specifies. Closing and reopening the diagram drew it at the stored
  position: `?proteomeData` at y 451 while its row-mates sat at 244 and 247. **Undo put it back**
  in line at y 244 and removed the `layout:` block entirely, leaving the file byte-identical to
  its committed state — so the pass needed no cleanup.

  `proteome-location-of-gene.rq`'s checksum was `d8e37162c7480bb8f6326b470af8b3a3` at every step,
  and `git diff` showed **not one changed line** across the whole sequence.

  **The correction, recorded because the withdrawn version is in this file's history.** An earlier
  attempt concluded the node "would not move" and that this looked like a defect, on the evidence
  that two drags produced `dx 0, dy 0` with no message while an owl class moved under the same
  method. **That was my error, and a specific one worth naming: the drag never touched the node.**
  `document.elementFromPoint` at the node's own bounding-box centre returned
  `HEADER.sparql-header-band` — the header band overlays the canvas, and after a pan three of the
  four variable nodes had their centres underneath it. I was dragging the header, which pans, and
  reading "the node did not move" as a fact about the node.

  A bounding box is where an element *is*; it is not proof anything there can be *hit*. Checking
  what is actually under a point costs one call and is the difference between a defect report and
  an apology. The earlier version was recorded as inconclusive rather than as a defect only
  because I had disturbed that session in another way — so the habit of not concluding from a
  contaminated run is what stopped a wrong defect report reaching anyone.

  **One real observation survives from it**: the header band overlays canvas content, and a node
  panned underneath it cannot be clicked or dragged until it is panned clear. That is conventional
  for a sticky header and not a defect, but it is worth knowing before dragging anything near the
  top of a sparql canvas.

## The context menu offers no edits (sparql-diagram, task 5.3)

The read-only position is meant to be visible, not merely enforced: a menu that offered an
action the backend refuses would be worse than a menu with nothing in it (Requirement 6.1).

- **Preconditions**: as above.
- **Actions**: open `diagrams/sparql/uniprot/proteome-location-of-gene.adp`. Right-click a
  variable node, a concrete term, an edge and a region frame in turn. Select a variable and
  look at the property grid; try to type into a row.
- **Expected**: no mutating action appears on any of them, and the toolbox panel offers this
  diagram no entries. Every property row is read-only and states the same reason - that the
  diagram reads the query and the `.rq` is edited in a text editor.

**Status of the four sparql-diagram checks above (2026-09-03): specified, not yet executed.**
The app was built and both dev servers started cleanly against this module (backend on 5090,
client on 5194, worktree ports since reverted), but the app opens on a sign-in form, and
entering a credential into a login field is outside what the implementing agent may do — even
the checked-in developer placeholder. The behaviours themselves are covered automatically:
the byte-identity of the query across every offered surface by `NoWriterSweepTests`, the
one-node-per-variable and shallowest-scope placement rules by `SparqlProjectionTests`, the
projection mark and the absence of any editing affordance by `SparqlCanvas.test.tsx`, and the
read-only reason on every property row by `SparqlProvidersTests`. What these four checks add is
the eyes-on confirmation that the drawing reads correctly to a person, which is exactly the part
a test cannot assert - so they are left here to be run by someone who can sign in.

- **Result 2026-09-05**: **text reviewed against the implementation, not executed** — the client
  dev server is down and the pass is waiting on `developer-sign-in-bypass` rather than on another
  of the user's sign-ins.

  **Two of its three clauses are sound.** `SparqlContextActionProvider.DiscoverAsync` returns an
  empty list for every target, so no mutating action can appear on a node, a term, an edge or a
  region; and it holds a `NoActionsReason` sentence for an action that arrives anyway. The
  property rows were seen read-only during the reposition check, each carrying *"This diagram
  reads the query; edit the .rq file in a text editor and the diagram follows."*

  **The third clause will fail, and the entry is right while the application is wrong.** It
  expects *"the toolbox panel offers this diagram no entries"*. `ToolboxPanel` has two empty
  states: `items === null` renders **"Open a diagram to see the elements its type offers"**, and
  `items.length === 0` renders **"This diagram type offers no toolbox elements."** `SparqlCanvas`
  never calls `useRegisterDiagramToolbox`, so `items` stays `null` and an **open** sparql diagram
  is told to open a diagram. Observed in the running app earlier in this pass, with
  `proteome-location-of-gene` open, before the server went down.

  See the entry below — it is not only sparql.

## Dragging an Ansible node stores the position, and reopening keeps it (ansible-refinements, task 4.2)

Guarded end to end by `AnsibleStructureFlowTests.ARepositionOverTheWire_LandsInTheRegistration_ReopensThere_UndoesBack_AndNeverTouchesAnsiblesFiles`;
kept here because only the running client proves the gesture itself - that a drag feels like a
drag, that the node follows the pointer, and that a click still selects rather than moving.

- **Preconditions**: backend + client running; a project containing an Ansible folder with an
  `.adp` registration open in a diagram tab, showing at least one role and one playbook.
- **Actions**: drag a role node a visible distance and release. Note where it lands. Close the
  diagram tab and reopen the same diagram. Then press Ctrl+Z.
- **Expected**: the node follows the pointer while dragging and stays where it was dropped;
  the reopened diagram draws it in the same place, not back at its computed position; Ctrl+Z
  returns it to where it was. Open the `.adp` in a text editor: it has a `layout:` block naming
  the element and its position, and every playbook, role and inventory file is untouched.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## An Ansible click still selects, and an edge cannot be dragged (ansible-refinements, task 4.2)

- **Preconditions**: as above.
- **Actions**: single-click a node. Then press and drag on an edge between two nodes.
- **Expected**: the click selects the node - its border takes the accent colour - and does not
  move it by even a pixel. The edge does not move and nothing is written to the `.adp`; edges
  follow their endpoints, so only the nodes are arrangeable.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## The Ansible canvas scrolls like its siblings (ansible-refinements, task 4.2)

- **Preconditions**: an Ansible project large enough that its diagram exceeds the viewport -
  the `lamp_haproxy` or `wordpress-nginx` showcase folders are big enough.
- **Actions**: observe the scrollbars at the canvas edges; drag the horizontal thumb, then the
  vertical one. Then open a small project whose diagram fits entirely.
- **Expected**: two thin bars appear, their thumbs describing where the view sits in the
  content; dragging a thumb pans the canvas without zooming it; the thumbs track the view when
  panning by dragging the canvas itself. On the diagram that fits, the thumbs claim nearly the
  whole track and invite no pan.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## Both themes render the Ansible canvas deliberately (ansible-refinements, task 4.2)

Three tokens this canvas used were referenced but never defined - `--color-surface-raised`,
`--color-warning` and `--color-accent` - so parts of it rendered from fallbacks, inherited
values, or nothing. A screenshot proves what a unit test cannot here.

- **Preconditions**: an Ansible diagram open with several plays, at least one unresolved edge,
  and one node selected.
- **Actions**: view it in light mode, then switch the OS/browser to dark mode and view it again.
- **Expected**: in both themes the node fills are visibly tinted per play and legible against
  the canvas; the selected node's border is clearly the accent colour and unmistakably
  different from unselected nodes; an unresolved edge is drawn in the warning colour; nothing
  is black-on-black, white-on-white, or invisible.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## Reordering plays moves stored positions with the index (ansible-refinements, task 4.2)

The accepted consequence of a play's id being positional (`play:<relative-path>#<index>`),
recorded in the design so it is expected rather than discovered: a play's identity in Ansible
genuinely is its order, so a name-keyed id would break the ordinary unnamed or duplicate-named
case.

- **Preconditions**: an Ansible diagram whose playbook has at least two plays, both dragged to
  distinctive positions and both visible.
- **Actions**: in a text editor, swap the order of the two plays inside the playbook `.yml`
  and save. Watch the open diagram.
- **Expected**: the diagram updates, and the two play nodes keep their authored places while
  their contents swap - the play now first sits where the previously-first play was put.
  Nothing overlaps, disappears, or lands at a phantom position, and one drag per play
  re-authors them. No warning is shown, deliberately.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## A click on an Ansible node still selects it after the drag feature (ansible-refinements, task 4.3)

The regression this exists for, found in this spec's own manual pass: the node drag captured
the pointer on the `<svg>`, and a capture retargets the pointerup, so the browser fired `click`
on the svg instead of the node. Click-to-select stopped working while every unit test passed,
because jsdom implements no pointer capture at all and cannot reproduce the consequence. The
capture now goes on the node; `AnsibleCanvas.test.tsx` asserts which element takes it, and this
check is what catches the behaviour itself.

- **Preconditions**: an Ansible diagram open with several nodes.
- **Actions**: single-click a role node, without moving the mouse.
- **Expected**: the node becomes selected - its border turns the accent colour (limegreen) -
  and the Properties panel fills with that node's details. The node does not move.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## An ontology draws its classes, restrictions and individuals (owl-diagram, task 4.3)

The adopted VOWL vocabulary on a real published ontology, which is the whole point of the
reading: shapes carry kind, and the axioms are visible without opening the Turtle.

- **Preconditions**: backend + client running; `src/examples/` added as a project.
- **Actions**: open `diagrams/rdf/owl-time/owl-time.adp`; look at the drawn ontology; then open
  `diagrams/rdf/prov-o/prov-o.adp` beside it.
- **Expected**: classes draw as ellipses and datatypes as rectangles; the days of the week and
  the temporal units draw as cards with their type badges; restriction nodes sit beside the
  classes whose axioms attach them, labelled in Manchester style (`∀ :hasBeginning.:Instant`
  and kin); subclass edges are dotted while property edges carry their names; the two
  deprecated OWL-Time classes are visibly dimmed; and the Errors and Warnings panel reports
  nothing for either file.

- **Result 2026-09-05**: **passes, with one assertion unverifiable and one defect found.** Run
  against a worktree build (backend 5091, client 5191) with `src/examples` opened as a project.
  The user signed in; see the note under *Signing in* below.

  Verified in the running app: **classes draw as ellipses and datatypes as rectangles** - the
  drawn nodes carry `owl-class`/`owl-datatype` and resolve to `<ellipse>` and `<rect>`
  respectively. **Restriction nodes sit beside their classes with Manchester labels** - 42 of
  them, reading `∀ days du…`, `≤ 1 month`, `= 1 Tempo…`, `∋ Tempora…`. **Subclass edges are
  dashed and property edges carry names** - three dash patterns alongside 48 solid, with labels
  `has beginning`, `has end`, `has duration description`, `in time zone`. **Both deprecated
  classes are dimmed** - `January` and `Year` carry `owl-deprecated` at computed opacity 0.55
  against 1.0 for their neighbours. **`prov-o` opens beside it** and draws 30 classes.

  **Not verifiable as written: "the Errors and Warnings panel reports nothing for either file."**
  The panel renders 999 rows and states `524 more problems are not shown` - the showcase carries
  1,524 problems, nearly all of them SKOS typing warnings from `business-economics`. No owl-time
  or prov-o row appears among the 999 that render, but a quarter of the set cannot be inspected
  from the panel at all. **This assertion needs a project scoped to the ontology folder rather
  than the whole showcase**, and stating it against `src/examples` was a mistake in the check
  rather than a fault in the app.

  **Two corrections to this entry's own instructions**, both found by following them: the file is
  at `diagrams/owl/owl-time/owl-time.adp`, not `diagrams/rdf/owl-time/` - the OWL examples moved
  to their own folder. And the `.adp` is nested **under** `owl-time.ttl` in the explorer rather
  than beside it, so it takes two expands to reach.

## Four diagram types tell you to open a diagram while one is open (found 2026-09-05, by text review)

**A defect found without running the application**, by checking an entry's text against the
implementation rather than against a recorded result.

`ToolboxPanel` distinguishes two empty states deliberately: `items === null` means *nothing is
open* and renders "Open a diagram to see the elements its type offers", while `items.length === 0`
means *this type offers nothing* and renders "This diagram type offers no toolbox elements." A
canvas that never registers an answer leaves `items` at `null`, so an open diagram wears the
placeholder that says nothing is open.

**Four mounted canvases never register**, so four diagram types show it:

- `SparqlCanvas` — `w3c/sparql`
- `BundleCanvas` — `databricks/bundle`
- `JobCanvas` — `databricks/job`
- `PipelineCanvas` — `databricks/pipeline`

All four are live: each is the `Canvas` its module's `register.ts` mounts. Fourteen other canvases
call `useRegisterDiagramToolbox` and are correct. (`DatabricksCanvas.tsx` registers but is not the
component `register.ts` mounts, which is worth a look when this is fixed.)

**This is a known shape, already fixed once.** The ansible entry above records it in its own
words: *"Unregistered, an open structure diagram wears the misleading 'Open a diagram' placeholder
— the same defect the wardley and pipeline canvases had."* Three canvases were fixed then; four
were missed, and nothing guards the rule.

- **Preconditions**: backend + client running; a project containing a `.rq` and a databricks
  bundle.
- **Actions**: open a sparql query diagram and look at the Toolbox panel. Repeat for a databricks
  bundle, job and pipeline.
- **Expected**: "This diagram type offers no toolbox elements", the state that describes what is
  true. **Observed**: "Open a diagram to see the elements its type offers", while one is open.

- **Result 2026-09-05**: **open.** Confirmed in the running app for sparql; established from the
  source for the other three. Not fixed here — the Tester found it, and the canvases belong to
  whoever holds their specifications. **Worth a guard rather than four fixes**: a test asserting
  every mounted canvas registers a toolbox answer would have caught all four, and would catch the
  fifth.

## Fit to View shows less of a viewport-filtered diagram than the reader already had (view-delta-adoption, found 2026-09-05)

**A defect, found by using the application, in work I merged myself.** `Fit to View` is the one
control that means "show me the whole diagram", and on any canvas that filters by viewport it
shows *less* than was on screen a moment earlier.

- **Preconditions**: backend + client running; `src/examples` open as a project.
- **Actions**: open `diagrams/owl/owl-time/owl-time.adp`; press `Zoom Out` five or six times,
  watching the element count grow as the backend delivers what comes into view; then press
  `Fit to View`.
- **Expected**: the whole ontology, or at least no less than was already delivered.
- **Observed**: the drawn set fell from **114 nodes to 85** and stayed there across four further
  presses of `Fit to View`. It is a stable fixed point, not a transient.

**Mechanism, confirmed in the source rather than inferred.** `OwlCanvas.fitToView` computes its
bounds from `model.nodes` - the elements the **client currently holds**, which is exactly what
the last viewport admitted. Fitting to that subset reports a viewport smaller than the one the
reader had zoomed out to, the backend culls to the smaller window, and the client drops the
difference. Pressing it again re-fits the now-smaller set, which is why it settles rather than
oscillating.

**Scope**: every canvas whose `fitToView` reads the delivered model, which after
`view-delta-adoption` is all eleven. Verified on `owl`; the same shape is in the other canvases'
fit helpers.

**Why no unit test caught it**: the view-delta tests assert that a view *change* produces deltas,
which is true here - the culling is the loop working exactly as specified. Nothing asserts that
`Fit to View` shows the whole document, because until the loop landed it always did.

- **Result 2026-09-05**: **open**, reported to the scrum master. Not fixed here: the Tester found
  it, and the canvases belong to whoever holds their specification.

## The whole View group is dead on a causal loop diagram (causal-loop-diagram, found 2026-09-05)

**Found while reading the fit-to-view report against my own module, not by using the app.** The
adjacent failure to the entry above, and the opposite one: on `owl` the View group works and shows
too little, and on `causal-loop` it does not work at all.

`CausalLoopCanvas` never calls `useRegisterDiagramView`, so with a `.cld` open the shell's
registry still holds `null`. The ribbon disables **all three** buttons — Zoom In, Zoom Out and Fit
to View — and titles them *"Open a diagram to use this."*, which is wrong twice over: a diagram is
open, and opening one is not what would help.

- **Preconditions**: backend + client running; a project containing a `.cld` document.
- **Actions**: open the `.cld`; look at the ribbon's View group; hover one of its buttons.
- **Expected**: the three buttons enabled and acting on the canvas, as on every other diagram
  type (causal-loop-diagram Requirement 9.1 — behave like the others).
- **Observed**: all three greyed out, tooltip *"Open a diagram to use this."*

**Why no unit test caught it**: nothing asserts that a canvas registers its view controls. Each
canvas that does has tests for what its own zoom and fit compute; a canvas that registers nothing
has nothing to test, so its absence is invisible. The guard worth having is a shared one — every
registered canvas module supplies view controls — rather than a twelfth per-canvas test.

**Deliberately not fixed on sight.** The correct `fitToView` for a viewport-filtered canvas needs
the document's own extent, the client is not sent one today, and `fit-to-view-extent` is the spec
being written for exactly that. Wiring `setView(null)` now would buy an enabled button by
importing the defect in the entry above, so this waits on that spec rather than racing it.

- **Result 2026-09-05**: **open**, reported to the scrum master. Blocked on `fit-to-view-extent`
  by choice, not by permission.

## An expression node refuses to be dragged, and says why (owl-diagram, task 4.3)

The blank-node identity boundary as the user meets it — the refusal has to be readable, not a
silent no-op.

- **Preconditions**: backend + client running; `src/examples/` added as a project.
- **Actions**: open `diagrams/rdf/owl-time/owl-time.adp`; drag a class (say `:Interval`) to a
  clearly different spot; then drag one of the restriction nodes beside a class.
- **Expected**: the class moves and its position lands in `owl-time.adp`'s `layout:` block
  keyed `res:http://www.w3.org/2006/time#Interval`, while `owl-time.ttl` never changes by a
  byte; the restriction node does not move, and the canvas shows the sentence saying its
  identity does not survive an edit to the file, so a stored position could not be trusted.

- **Result 2026-09-05**: **passes, both halves.** Run against a worktree build with `src/examples`
  open as a project; the user signed in.

  **The class moves and lands in the registration.** Dragging a class wrote
  `res:http://www.w3.org/2006/time#DateTimeDescription: 432.945 1049.833` into a `layout:` block
  that `owl-time.adp` did not have before, and `owl-time.ttl`'s checksum was identical before and
  after - `f5316ced41b4ac0e37e86705b1c2fb9a` either side, so the ontology never changed by a byte.
  (The check suggests `:Interval`; any class exercises the same path, and this is the one the
  viewport had delivered.)

  **The restriction node refuses, and says why in a sentence a reader can act on.** Dragging one
  moved it by `dx: 0, dy: 0`, and the canvas showed: *"That is an anonymous class expression, whose
  identity does not survive an edit to the file, so a stored position could not be trusted. It takes
  its place beside the class that uses it; edit the expression as text."* The `layout:` block still
  held exactly one entry afterwards - the class's - so nothing was written for the expression.

  The example file was restored to its committed state after the check, so the pass leaves no stray
  `layout:` block in the shared checkout for another session to sweep up.

## The full class expression is one selection away (owl-diagram, task 4.3)

Requirement 3.4's answer to nesting: the canvas label is capped, the property grid is not.

- **Preconditions**: backend + client running; `src/examples/` added as a project.
- **Actions**: open `diagrams/rdf/owl-time/owl-time.adp`; select a restriction node; read the
  Properties panel; then select the class that owns it and read its axiom rows.
- **Expected**: the panel shows the expression's full Manchester rendering as a read-only row
  whose reason names the identity boundary; the owning class lists the same expression among
  its "Subclass of" rows. Where a label on the canvas ends in `…`, the panel's text is longer
  than the label — the elision is real and recoverable.

- **Result 2026-09-05**: **passes.** Run against a worktree build with `src/examples` open; the
  user signed in.

  **The elision is real and recoverable.** The canvas label reads `∀ day…`; selecting that node
  puts `Expression: ∀ day.xsd:gDay` in the Properties panel - longer than the label, and the
  full Manchester rendering. It is a read-only row whose reason names the boundary: *"That is an
  anonymous class expression, whose identity does not survive an edit to the file, so nothing
  about it can be edited from the diagram. Edit the expression as text."*

  **The owning class lists the same expression.** Selecting `:DateTimeDescription` shows an
  AXIOMS section whose `Subclass of` rows are `:GeneralDateTimeDescription`, `∀ day.xsd:gDay`,
  `∀ month.xsd:gMonth`, `∀ year.xsd:gYear` and `∋ Temporal reference system used.Gregorian` -
  the first row a named class, the rest the expressions, each carrying the same read-only reason.
  So the expression a reader meets on the canvas and the one listed against its class are the
  same string.

  **One thing a tester should know before following this check: the Properties panel is not
  visible by default.** At 1440x900 the right-hand pane is 253px wide, which fits one tab, so
  `TabbedPane` collapses `Properties` behind a "1 more tabs" overflow button beside `Toolbox`.
  That is the pane working as designed rather than a defect, but the check says "read the
  Properties panel" as though it were on screen, and it takes a click to find. Widening the pane
  or using the overflow both work.

## An ontology is offered for a marked file, and a bare one still opens as a graph (owl-diagram, task 4.3)

The routing arrangement: a reading is chosen, never assumed, and the anchor keeps the bare body.

- **Preconditions**: backend + client running; a project folder containing a copy of
  `owl-time.ttl` with **no** `.adp` beside it.
- **Actions**: double-click the unregistered `.ttl`; close it; then right-click it and choose
  "Add as diagram…".
- **Expected**: the unregistered file opens as the RDF data graph (resource cards with literal
  rows), not as an ontology; the Add dialog offers both "RDF Graph" and "OWL Ontology" because
  the file carries an `owl:Ontology` marker; choosing the ontology writes an `.adp` naming
  `w3c/owl` and the file reopens in the ontology reading.

- **Result 2026-09-04**: **pending**, same reason - sign-in. Covered meanwhile by
  `OwlFlowTests.ARegisteredOntology_StreamsItsShapesAndAxioms_AndABareBodyStaysTheGraphReadings`
  (a bare marked file opens as the data graph over the real host), by
  `DiagramFileRouterSharedExtensionTests.AFamilysSharedReadings_NeverWinTheBareBodyFromTheAnchor`
  and by the two marker facts in `AddDiagramContextActionProviderRegistrationTests`.

## The scrollbars describe a real viewport, not a jsdom one (canvas-scrollbars, task 6)

Everything about the bars that a unit test can hold was pinned per canvas: a thumb drag pans the
view, panning by other means moves the thumb, and zoom changes the thumb's size rather than only
its offset. One thing it cannot hold. jsdom gives every element a zero-sized
`getBoundingClientRect()`, so a track has no length, a thumb has no pixels, and the tests reason
entirely in the fractions the geometry returns. Whether those fractions land as a bar a person
can see and grab is a question only a browser answers.

- **Preconditions**: backend + client running; open one canvas of each shape, because they
  compute the same fractions from different inputs - `diagrams/c4/**` (an SVG view box) and
  `diagrams/timeline/**` (pixels per unit).
- **Actions**: on each, drag the horizontal thumb from one end of its track to the other, then
  the vertical; wheel-zoom in a long way and drag again; wheel-zoom out until the whole diagram
  fits and try to drag once more.
- **Expected**: the thumb stays under the pointer through the drag rather than lagging, leading
  or jumping when the drag starts; the canvas moves with it and lands where the thumb was
  released. Zoomed in, the thumb is smaller and the same drag covers more content. Zoomed out to
  the fitted view, the thumb fills its track and dragging moves nothing - correct, not stuck:
  there is nothing outside the view to pan to. Nothing about the canvas jumps when the drag ends.

- **Result 2026-09-04**: **pending** - the app was not run for this pass. Covered meanwhile by
  the per-canvas drag, pan and zoom tests in each `*Canvas.test.tsx`, each verified by sabotage:
  unwiring `onPan`, feeding the fitted box instead of the live view, and hard-coding the view
  span each fail their named test and nothing else.

## No canvas grows a scrollbar of its own (canvas-scrollbars, task 6)

This one is automated - `noPrivateScrollbars.test.ts` - and is noted here only because its
failure message is the instruction. A module that needs bars imports
`@client/canvas/scroll/CanvasScrollbars` and converts its view at its own call site; if it needs
them placed differently it passes a `className` and overrides only the offsets. Reaching for a
private thumb or track is the one thing the guard will not allow, and reading its message as a
prompt to loosen the guard is reading it backwards.
- **Result 2026-09-05**: **not run** — needs a person to sign in once; runnable thereafter. The steps require the application UI, and `Gate()` in `src/client/src/App.tsx` renders `LoginPage` until `isAuthenticated`; the client carries no developer bypass, and an agent cannot type the credential. Not a defect in the check. Recorded by Tester 2 without running it.

## A shapes file aims at data that is elsewhere, and says so calmly (shacl-diagram, task 4.2)

Requirement 4.3 is the one a SHACL canvas is most likely to get wrong. A shapes graph almost
always targets classes it does not itself describe, so the naive drawing either dangles an edge
into nothing or invents a placeholder node for the target — and either one turns the medium
working as designed into something that reads like a defect.

- **Preconditions**: backend + client running; `src/examples/` added as a project.
- **Actions**: open `diagrams/shacl/fair-data-point/navigation-shapes.adp`; read the target
  chips on the cards; select a card and read the Targets group in the property grid; then check
  the Errors and Warnings panel.
- **Expected**: every target appears as a chip inside its card's target band — never as an
  arrow, and never as a node of its own; the chips for these shapes all name classes from other
  vocabularies, and they are styled exactly like a chip whose class *is* described here, with no
  warning colour, badge or icon; the grid lists each one under Targets, valued
  `dcat:Catalog (not described in this file)` and kin, worded as a fact; nothing anywhere on the
  canvas is left pointing at empty space; and the panel reports nothing at all for the file — an
  absent target is explicitly not a finding.

- **Result 2026-09-04**: **pending** - the app renders `LoginPage` unless authenticated
  (`src/client/src/App.tsx:12`), so reaching any diagram means typing a password into a form,
  which is outside what an agent does here - the checked-in developer placeholder included.
  Verified in the code rather than assumed from the earlier owl-diagram entries, and recorded
  rather than skipped. What stands in meanwhile:
  `ShaclTargetChipsTests` is the structural half - `AnAbsentTargetTerm_YieldsAChipAndNothingElse`
  and `ATargetNamingAnotherDrawnShape_StillDrawsNoEdge` run through
  `AssertElementUniverseIsShapesOnly`, which asserts the drawn universe holds shapes and
  nothing else, so neither a placeholder node nor a dangling edge can exist to be seen.
  `ShaclExamplesTests.TheDeployedShapes_DrawTheirOutwardTargetsAsChips_AndTheirCyclesAsEdges`
  says the same of the real FAIR Data Point file: every chip comes back `DescribedInFile:
  false`, and every drawn edge has both endpoints among the cards. The visual half -
  absence styled exactly like presence - is `ShaclCanvas.test.tsx`'s "renders an absent
  target exactly like a present one", which compares the rendered class lists rather than
  trusting the eye. The grid wording is `ShaclPropertiesTests.AnAbsentTarget_ReadsAsAFact
  RatherThanAFinding`, and "not a finding" is
  `EveryVendoredShapesGraph_ParsesAndValidatesClean`, which admits nothing above Info on
  either vendored file.

## A constraint written as a blank node is fully readable, and refuses edits in one sentence (shacl-diagram, task 4.2)

The blank-node identity boundary as a user meets it. Both halves matter: the content has to be
legible (Requirement 4.1) *and* the refusal has to be a sentence, identical wherever it is met,
rather than a disabled control with nothing to say (Requirements 3.3, 6.4).

- **Preconditions**: backend + client running; `src/examples/` added as a project.
- **Actions**: open `diagrams/shacl/w3c-shacl/spec-examples.adp`; find a card with property
  rows; select it and read the Constraints group in the grid; try to edit a row's value there;
  then open the context menu on the card, and try to drag a card that draws as anonymous.
- **Expected**: each property shape reads as one row — path on the left, cardinality on the
  right, constraint summary between them — with no anonymous node drawn anywhere on the canvas;
  the grid lists every row with its values readable; the row's box will not accept an edit, and
  the reason shown is *"This constraint is written as a blank node, which has no identity that
  survives a reparse, so an edit keyed to it could not be undone reliably. Open the file as text
  to change it, or give the shape an IRI of its own."*; the same sentence, word for word, is
  what the menu shows against an unavailable gesture and what the canvas reports when an
  anonymous card is dragged. Three places, one sentence — if any of them paraphrases, that is
  the bug.

- **Result 2026-09-04**: **pending** - the app renders `LoginPage` unless authenticated
  (`src/client/src/App.tsx:12`), so reaching any diagram means typing a password into a form,
  which is outside what an agent does here - the checked-in developer placeholder included.
  Verified in the code rather than assumed from the earlier owl-diagram entries, and recorded
  rather than skipped. What stands in meanwhile:
  The one thing this entry exists to catch - three places drifting to three wordings - is
  pinned by string equality against the single `ShaclRefusals.BlankRooted` constant in each
  of them: `ShaclDoubleRefusalTests.BothLayersSayTheIdenticalSentence` for writer and gate,
  `ShaclSessionTests.TheSession_RefusesToMoveAnAnonymousShape_WithTheBoundarySentence` for
  the drag, and `ShaclPropertiesTests.ABlankRootedRow_IsFullyReadableAndRefusedWithTheOne
  Sentence` for the grid - that last one also reading the row's path, datatype and
  cardinality back, which is the readable half. That the canvas shows the backend's refusal
  rather than deciding one of its own is `ShaclCanvas.test.tsx`'s "sends a drag as a layout
  edit, and shows the backend's refusal rather than deciding it". What no test replaces is
  whether the sentence reads well in place, which is why the entry stays.

## A SPARQL constraint is drawn, and pointedly not run (shacl-diagram, task 4.2)

Requirement 4's load-bearing statement, at the one place where the temptation is strongest: the
natural expectation of a SHACL tool is that it executes, and a query sitting in the file is the
most executable-looking thing in it.

- **Preconditions**: backend + client running; `src/examples/` added as a project.
- **Actions**: open `diagrams/shacl/w3c-shacl/spec-examples.adp`; find the card carrying a
  `sh:sparql` constraint; read its row on the canvas; select the card and read the Constraints
  group in the grid; look over the context menu; check the Errors and Warnings panel.
- **Expected**: the constraint draws as a single opaque row badged as SPARQL — the query is not
  parsed into parts and no part of it becomes an element; the grid shows the `sh:select` text in
  full and readable, read-only, with the reason naming that it is never executed; no menu entry
  anywhere offers to validate, run, check conformance or pick a data file; and the panel speaks
  only about the shapes file itself, never about data conforming to it. Nothing in the UI should
  leave a user thinking a validation has happened.

- **Result 2026-09-04**: **pending** - the app renders `LoginPage` unless authenticated
  (`src/client/src/App.tsx:12`), so reaching any diagram means typing a password into a form,
  which is outside what an agent does here - the checked-in developer placeholder included.
  Verified in the code rather than assumed from the earlier owl-diagram entries, and recorded
  rather than skipped. What stands in meanwhile:
  `ShaclProjectionTests.SparqlConstraints_AreOpaqueRows` pins the row as one opaque line
  with nothing of the query parsed into elements, and
  `ShaclPropertiesTests.ASparqlConstraint_ShowsItsQueryTextAndSaysItIsNotRun` pins the query
  text readable in the grid, read-only, with a reason naming that it is never executed.
  That no menu anywhere offers to run anything is structural rather than asserted: no
  validate-data action id exists in `ShaclActions`, and Requirement 4.4 assigns the whole
  execution path to a future spec. The manual check remains worth running for the thing
  those cannot see - whether a user comes away thinking a validation has happened.

## Panning a large ontology brings its content in (view-delta-adoption, task 11)

The one thing no unit test here can hold. jsdom gives every element a zero-sized
`getBoundingClientRect()`, so `shownRectOf`'s surface branch never runs in the suite and every
reported rectangle is the bare box. Whether the loop actually delivers content as a person moves
around a large document is a question only a browser answers.

`rdf` is the module to check, because it is the one where the cost was visible: before this work
it opened large documents behind a first-N banner that discarded by document order, so panning
could never recover the rest however far it went.

- **Preconditions**: backend + client running; a project containing an RDF document of more than
  1,000 resources — `src/examples/` has the vendored ontologies, and any of the larger ones will
  do. Sign in with the checked-in developer placeholder.
- **Actions**: open the document; note that a truncation banner appears and that the diagram is
  read-only; pan steadily to the far edge of the laid-out content, well past what was drawn at
  open; then zoom out until the whole plane is in view, and zoom back in on a region that was
  empty at open.
- **Expected**: resources arrive as you pan into their region, rather than the canvas staying
  empty beyond the first screenful. Nodes already on screen **do not move** as new ones arrive —
  the layout is computed over the whole document, so panning must not repack it. No edge is ever
  drawn with one end missing. The banner and the read-only refusal stay exactly as they were:
  reach improves, what the document may do does not.
- **Also worth watching**: a pan produces one report per settle, not one per frame. A backend log
  showing a burst of `UpdateView` calls during a single drag means the debounce is not doing its
  job.

- **Result 2026-09-04**: **pending** — the app was not run for this pass. Covered meanwhile by
  `RdfSessionTests.PanningReachesResourcesTheBudgetDiscarded` (a 1,200-resource document, a
  viewport over the far end, resources arriving that the baseline never sent),
  `PanningDoesNotMoveTheNodesItBringsIntoView`, `NoEdgeIsEverDeliveredWithOneEndMissing`,
  `ADocumentOverTheBudget_KeepsItsBannerAndItsRefusal`, and the shared hook's debounce test
  asserting exactly one call for a burst of changes.

## Renaming a label in place (inline-rename, task 8)

The parts that carry this feature are the parts jsdom models least faithfully. Focus, the caret,
the selection and the blur that commits are all approximations there, and `getBBox` is not
implemented at all — so a relationship label's *measured* width has no unit coverage by
construction, and the unit tests exercise the per-character fallback instead. This check is where
the measured path and the real focus behaviour are verified.

- **Preconditions**: backend + client running from this worktree; sign in with the checked-in
  developer placeholder (`admin` / `changeme`, per CLAUDE.md's sign-in ruling). Open a project
  containing a mindmap and a C4 diagram — `src/examples/` has both.
- **Actions**:
  1. In the mindmap, select a node and press **F2**. Type a new name and press **Enter**.
  2. Press **F2** on another node, type something, and press **Escape**.
  3. Press **F2**, clear the field completely, and press **Enter**.
  4. Press **F2**, type a few characters, and *click on the empty canvas* without pressing a key.
  5. Press **F2**, type a few characters, then drag the empty canvas to pan.
  6. In the C4 diagram, press **F2** on an element; then close it and press **F2** on a
     relationship arrow that shows a technology in brackets.
- **Expected**:
  1. A textbox replaces the node's label in place — not a modal dialog — opening with the old
     text **selected**, so the first keystroke replaces it. Enter commits and the node shows the
     new name.
  2. Escape leaves the node exactly as it was, and the keyboard returns to the canvas: the next
     F2 opens an editor again rather than going nowhere.
  3. The editor **stays open with the typed text intact** and shows the module's own refusal
     underneath. It does not close and discard what was typed.
  4. The click **commits** the edit. Losing typed text to a stray click is the failure this rule
     exists to prevent, so a discarded edit here is a regression, not a preference.
  5. The editor rides with its node as the canvas moves — it does not stay put on screen while
     the node slides away — and the typed text survives. (Strictly, the pan commits first, so
     what is verified is that the commit happens rather than the editor drifting.)
  6. The element's editor covers its **name line only**, not the whole box with its type line and
     description. The relationship's editor sits at the middle of the arrow, is about as wide as
     the label drawn there, and opens on the **description alone** — the `[technology]` part is
     not in the field. Committing leaves the technology untouched in the arrow's label.
- **Also worth watching**: only one editor is ever open at a time, and the rest of the app's
  input prompts are unchanged — renaming a file in the explorer still opens the ordinary dialog.

- **Result 2026-09-04**: **PASSED, with two things it could not reach.** Run against a local
  developer build from the inline-rename worktree, signed in with the checked-in placeholder, on a
  freshly loaded page.

  Verified: F2 draws the editor in place - `foreignObject.inline-label-editor` inside the canvas's
  own `svg`, at the label's coordinates - focused, with the whole label selected, and no dialog.
  Enter commits (node relabelled, `SetNodeTextCommand` in the log, document written), and one Undo
  restores it. Escape abandons with the label untouched. **Clicking elsewhere commits** rather than
  discarding. Starting a pan commits the open edit first. On C4 the editor covers the name line
  only (20 units tall, not the 80-tall box), and clearing the name shows the module's own refusal
  inline - "Every C4 element needs a name." - with the editor staying open, the text intact, and
  nothing written to the document.

  **The relationship leg was blocked and is now open.** A C4 relationship could not be selected on
  the canvas at all - `C4RelationshipShape` rendered no click or context-menu handler - so the
  backend offered relabel on one and no gesture could invoke it. Relationships are now selectable
  through the same pattern RDF's edges use, and the leg is verified: clicking one selects it, the
  ribbon offers Relabel and Set technology, and F2 opens the editor on the description **alone** -
  "Scans parcels using", with the "[Touch]" that is drawn beside it absent from the field.

  **Enter confirmed by hand on 2026-09-04**, against a clean build of merged `develop`: F2, type,
  Enter, and the node takes the new text. Worth knowing for whoever automates this next - a
  synthesised Return does not reach an input inside a `foreignObject`, while Escape through the
  identical mechanism does and typing reaches the field, so an automated pass will see Enter do
  nothing and be wrong about it.

  Four defects were found by this pass, every one invisible to the suite. In order: the editor
  cancelling its own interaction the instant it appeared; an empty placement registry read as "the
  element has gone"; `StrictMode`'s double-invoked effects latching the editor's closing flag so
  that nothing could ever commit; and the editor sending a value the module had already refused,
  which wrote `container ""` into a real example document before it was undone. All four are fixed
  and each has a test that was seen to fail without its fix.
