import type { HierarchySurfaceModel, HierarchySurfaceNode } from "./api/hierarchySurfaceModel";

/** The width of a level's own box, in pixels: each level that fits takes this much of the page. */
export const LEVEL_WIDTH = 240;

/** The least width the entries of the deepest level shown keep, in pixels. */
export const ENTRY_MIN_WIDTH = 280;

/** The most columns of levels the page counts; more than any document holds. */
const MOST_COLUMNS = 1000;

/**
 * How many levels fit a page of `width` pixels, at least one, the page's columns from the left having the widths
 * `columnWidth` gives them, counted from 0. An unmeasured page, width 0, shows them all.
 */
export function levelsThatFit(width: number, columnWidth: (column: number) => number = () => LEVEL_WIDTH): number {
  if (width <= 0) {
    return Number.POSITIVE_INFINITY;
  }
  let used = ENTRY_MIN_WIDTH;
  let fit = 0;
  while (fit < MOST_COLUMNS && used + columnWidth(fit) <= width) {
    used += columnWidth(fit);
    fit++;
  }
  return Math.max(1, fit);
}

/** The nodes of one model by their place in the hierarchy. */
export interface HierarchyIndex {
  byId: ReadonlyMap<string, HierarchySurfaceNode>;
  /** The children of each node, in the file's order; the top under "". */
  children: ReadonlyMap<string, readonly HierarchySurfaceNode[]>;
}

export function indexOf(model: HierarchySurfaceModel): HierarchyIndex {
  const byId = new Map<string, HierarchySurfaceNode>();
  const children = new Map<string, HierarchySurfaceNode[]>();
  for (const node of model.nodes) {
    if (!byId.has(node.id)) {
      byId.set(node.id, node);
    }
  }
  for (const node of model.nodes) {
    // A parent that is not in the model leaves its node at the top, where it is still seen.
    const parent = node.parentId !== "" && byId.has(node.parentId) ? node.parentId : "";
    const list = children.get(parent) ?? [];
    list.push(node);
    children.set(parent, list);
  }
  return { byId, children };
}

/** The levels above `id`, from the top down; empty for a node at the top. */
export function ancestorsOf(index: HierarchyIndex, id: string): HierarchySurfaceNode[] {
  const chain: HierarchySurfaceNode[] = [];
  const seen = new Set<string>([id]);
  for (let node = index.byId.get(id); node !== undefined && node.parentId !== ""; ) {
    const parent = index.byId.get(node.parentId);
    if (parent === undefined || seen.has(parent.id)) {
      break;
    }
    seen.add(parent.id);
    chain.unshift(parent);
    node = parent;
  }
  return chain;
}

/** How many levels stand below `id`, counting the levels nested in it to their deepest. */
export function levelsBelow(index: HierarchyIndex, id: string, seen: Set<string> = new Set()): number {
  if (seen.has(id)) {
    return 0;
  }
  seen.add(id);
  let deepest = 0;
  for (const child of index.children.get(id) ?? []) {
    if (child.role === "level") {
      deepest = Math.max(deepest, 1 + levelsBelow(index, child.id, seen));
    }
  }
  return deepest;
}

/** Every node inside `id`, to any depth. */
export function descendantsOf(index: HierarchyIndex, id: string): HierarchySurfaceNode[] {
  const found: HierarchySurfaceNode[] = [];
  const seen = new Set<string>([id]);
  const walk = (parent: string) => {
    for (const child of index.children.get(parent) ?? []) {
      if (!seen.has(child.id)) {
        seen.add(child.id);
        found.push(child);
        walk(child.id);
      }
    }
  };
  walk(id);
  return found;
}
