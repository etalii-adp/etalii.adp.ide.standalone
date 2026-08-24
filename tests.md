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
