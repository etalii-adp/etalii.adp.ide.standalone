// What a drag of one node means, before the backend answers: where its row goes down, where the
// node lands among its siblings, and how far every node in that row and beneath it moves. Pure
// functions over the folded model, so the canvas can show a drop while it is still a drag.
//
// It mirrors the backend's `AbmArrangement` and `AbmLayout.Arrange`: across, a node's place is its
// order; down, a parent's children share one row; everything beneath a node moves with it.

import { ABM_MINIMUM_GAP } from "./abmIds";
import type { AbmModel, AbmNode } from "./abmModel";

/** How far one node is drawn from where the model has it. */
export interface Offset {
  dx: number;
  dy: number;
}

/** A drop's meaning: its place before and after among its siblings, the row's move, and every node's offset. */
export interface Arrangement {
  from: number;
  to: number;
  dy: number;
  /** Whether the drop changes nothing the backend would write. */
  nothing: boolean;
  /** The row's nodes and everything beneath them, each where the drop puts it, the dragged node included. */
  offsets: ReadonlyMap<string, Offset>;
}

/** Each child's parent, by the parent lines. */
function parentsOf(model: AbmModel): Map<string, string> {
  const parents = new Map<string, string>();
  for (const line of model.lines.values()) {
    parents.set(line.payload.toElementId, line.payload.fromElementId);
  }
  return parents;
}

/** The nodes sharing a parent with `node` (the roots for a root), left to right - which is their order. */
function rowOf(model: AbmModel, parents: ReadonlyMap<string, string>, node: AbmNode): AbmNode[] {
  const parent = parents.get(node.id);
  return [...model.nodes.values()].filter((other) => parents.get(other.id) === parent).sort((one, other) => one.x - other.x);
}

/** A node and every node beneath it. */
export function subtreeOf(model: AbmModel, id: string): string[] {
  const children = new Map<string, string[]>();
  for (const line of model.lines.values()) {
    children.set(line.payload.fromElementId, [...(children.get(line.payload.fromElementId) ?? []), line.payload.toElementId]);
  }
  const ids: string[] = [];
  const visit = (current: string) => {
    if (!model.nodes.has(current) || ids.includes(current)) {
      return;
    }
    ids.push(current);
    (children.get(current) ?? []).forEach(visit);
  };
  visit(id);
  return ids;
}

/** The left and right edge of everything a subtree draws. */
function extentOf(model: AbmModel, ids: readonly string[]): { left: number; right: number } {
  const nodes = ids.flatMap((id) => model.nodes.get(id) ?? []);
  return {
    left: Math.min(...nodes.map((node) => node.x - node.payload.width / 2)),
    right: Math.max(...nodes.map((node) => node.x + node.payload.width / 2)),
  };
}

/**
 * What dropping `id` with its CENTRE at (`centreX`, `centreY`) means. The node goes before the first
 * other sibling whose middle is right of the drop's; the row moves down by what the node did, but
 * never closer to the parent than the minimum gap. Each sibling's subtree is then packed left to
 * right in the new order, from where the first one began and with the gap they had.
 */
export function arrangementOf(model: AbmModel, id: string, centreX: number, centreY: number): Arrangement | null {
  const node = model.nodes.get(id);
  if (node === undefined) {
    return null;
  }

  const parents = parentsOf(model);
  const row = rowOf(model, parents, node);
  const from = row.findIndex((sibling) => sibling.id === id);
  const others = row.filter((sibling) => sibling.id !== id);
  const to = others.filter((sibling) => sibling.x < centreX).length;

  const parent = model.nodes.get(parents.get(id) ?? "");
  const floor = parent !== undefined ? parent.y + parent.payload.height / 2 + ABM_MINIMUM_GAP + node.payload.height / 2 : -Infinity;
  const dy = Math.max(centreY, floor) - node.y;

  const subtrees = new Map(row.map((sibling) => [sibling.id, subtreeOf(model, sibling.id)]));
  const extents = new Map(row.map((sibling) => [sibling.id, extentOf(model, subtrees.get(sibling.id) ?? [])]));
  const first = extents.get(row[0].id)!;
  const gap = row.length > 1 ? extents.get(row[1].id)!.left - first.right : 0;

  const order = [...others];
  order.splice(to, 0, node);
  const offsets = new Map<string, Offset>();
  let left = first.left;
  for (const sibling of order) {
    const extent = extents.get(sibling.id)!;
    for (const member of subtrees.get(sibling.id) ?? []) {
      offsets.set(member, { dx: left - extent.left, dy });
    }
    left += extent.right - extent.left + gap;
  }

  return { from, to, dy, nothing: from === to && Math.abs(dy) < 0.5, offsets };
}

/**
 * What to draw while the node is still under the pointer: the row's others where the drop would
 * put them, and the node's own subtree following the pointer exactly. The dragged node itself is
 * the library's to draw, so it has no offset here.
 */
export function previewOf(model: AbmModel, id: string, centreX: number, centreY: number): ReadonlyMap<string, Offset> | null {
  const node = model.nodes.get(id);
  const arrangement = arrangementOf(model, id, centreX, centreY);
  if (node === undefined || arrangement === null) {
    return null;
  }

  const offsets = new Map(arrangement.offsets);
  const following = { dx: centreX - node.x, dy: centreY - node.y };
  for (const member of subtreeOf(model, id)) {
    offsets.set(member, following);
  }
  offsets.delete(id);
  return offsets;
}

/** Whether two sets of offsets draw the same thing - so a frame that changes nothing re-renders nothing. */
export function sameOffsets(one: ReadonlyMap<string, Offset>, other: ReadonlyMap<string, Offset>): boolean {
  return one.size === other.size && [...one].every(([id, offset]) => {
    const match = other.get(id);
    return match !== undefined && match.dx === offset.dx && match.dy === offset.dy;
  });
}
