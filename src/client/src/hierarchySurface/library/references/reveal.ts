import type { HierarchySurfaceNode } from "../api/hierarchySurfaceModel";
import { ancestorsOf, type HierarchyIndex } from "../layout";

/** What the page changes to show a node: the focus it takes, and the levels it expands, from the top down. */
export interface RevealPlan {
  /** The page's new left edge: unchanged when the node is already in sight of it; "" for the whole page. */
  focus: string;
  /** The collapsed levels above the node, each of which hides it. */
  expand: readonly HierarchySurfaceNode[];
}

/**
 * How to show `targetId` from where the page stands (FR-018): the nearest focus that keeps the node in sight, and every
 * collapsed level above it; null when the document has no such node. The focus is kept when the node is inside it and
 * its level fits the `fit` columns the page shows. Otherwise it moves to the deepest level the two have in common, the
 * whole page when they share none, and from there down the node's levels only as far as its own level needs to fit.
 * Nothing here writes the document: a level is expanded by the page's own collapse, which the registration keeps.
 */
export function revealOf(index: HierarchyIndex, targetId: string, focused: string, fit: number, isCollapsed: (node: HierarchySurfaceNode) => boolean): RevealPlan | null {
  const target = index.byId.get(targetId);
  if (target === undefined) {
    return null;
  }
  const above = ancestorsOf(index, targetId);
  // The levels from the top down to the one the node stands in: itself for a level, its parent for an entry.
  const levels = target.role === "level" ? [...above, target] : above;

  // The level the page starts from, as its place in `levels`; -1 for the whole page.
  let edge = levels.findIndex((level) => level.id === focused);
  if (focused !== "" && edge < 0) {
    const shared = new Set(ancestorsOf(index, focused).map((node) => node.id));
    edge = levels.reduce((deepest, level, at) => (shared.has(level.id) ? at : deepest), -1);
  }

  // The page's first column holds the edge's level, or the top levels for the whole page; the node's level stands
  // as many columns to the right as it is deeper.
  while (levels.length - 1 - Math.max(edge, 0) >= fit) {
    edge += 1;
  }
  return { focus: edge < 0 ? "" : levels[edge]!.id, expand: above.filter((level) => isCollapsed(level)) };
}
