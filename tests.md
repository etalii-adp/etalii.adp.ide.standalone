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
