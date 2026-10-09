import type { LayoutDefinition, ShapePoint } from "../definition/diagramDefinition";
import type { LayoutAlgorithm, LayoutElement, LayoutInput, LayoutPlacement } from "./layoutAlgorithm";

/**
 * A layout that radiates outwards by element type: the first tier in the middle, each further
 * tier on a wider ring, connected elements drawn together and all elements held apart.
 *
 * It is a small force simulation and it is written out here, not taken from a library, for one
 * reason: <b>the same input must give the same picture, on every machine and in every host.</b>
 * A reader who opens a diagram twice must not find it rearranged, and the hosts that do not run
 * JavaScript have to be able to reproduce it from a description. So nothing here is random, the
 * number of steps is fixed, and every loop runs in the order of the input.
 *
 * What it does, in order:
 *
 * 1. <b>Rings.</b> Each tier gets a radius: wide enough for what the tier holds, and at least one
 *    ring gap beyond the tier inside it.
 * 2. <b>A start.</b> Tier by tier outwards, an element starts on its ring at the mean direction of
 *    the elements it is connected to on the rings inside it, or at a golden-angle step when it is
 *    connected to none - so two unconnected elements never start on top of each other.
 * 3. <b>Forces</b>, for a fixed number of steps that cool as they go: a spring along every
 *    connection, a push between every pair, and a pull of each element back to its ring.
 * 4. <b>No overlap.</b> Whatever the forces left touching is pushed apart along the axis that
 *    needs the least movement, until nothing overlaps.
 *
 * A locked element is where the model has it and stays there through all four; the others
 * arrange themselves around it. Given the placements a previous pass ended on, an element that
 * was placed then starts from there and the simulation runs cooler - so one change to the model
 * moves the rest a little, not everything from nothing.
 */

/** The distance from one ring to the next, where no ring needs more. */
const RING_GAP = 260;
/** The clear space kept between two elements, on a ring and by the overlap pass. */
const CLEARANCE = 28;
const STEPS = 240;
/** Steps taken from a previous pass's placements, and how hot they start. */
const WARM_STEPS = 60;
const WARM_HEAT = 0.08;
const SPRING = 0.06;
const REPULSION = 26000;
const RING_PULL = 0.09;
const MAX_STEP = 60;
const OVERLAP_PASSES = 400;
const GOLDEN_ANGLE = Math.PI * (3 - Math.sqrt(5));

interface Node {
  index: number;
  id: string;
  x: number;
  y: number;
  width: number;
  height: number;
  tier: number;
  fixed: boolean;
}

/** The tier an element's type is on; a type no tier names goes on the ring beyond the last. */
function tierOf(type: string | undefined, tiers: readonly (readonly string[])[]): number {
  const index = tiers.findIndex((tier) => type !== undefined && tier.includes(type));
  return index === -1 ? tiers.length : index;
}

function ringRadii(nodes: readonly Node[], tierCount: number): number[] {
  const radii: number[] = [];
  for (let tier = 0; tier < tierCount; tier++) {
    const members = nodes.filter((node) => node.tier === tier);
    // The circle that fits the tier's elements side by side with their clearance between them.
    const needed = members.reduce((sum, node) => sum + Math.max(node.width, node.height) + CLEARANCE, 0) / (2 * Math.PI);
    if (tier === 0) {
      radii.push(members.length <= 1 ? 0 : needed);
    } else {
      radii.push(Math.max(needed, radii[tier - 1] + RING_GAP));
    }
  }
  return radii;
}

function place(input: LayoutInput, definition: LayoutDefinition, previous?: ReadonlyMap<string, LayoutPlacement> | null): ReadonlyMap<string, LayoutPlacement> {
  const tiers = definition.tiers ?? [];
  const nodes: Node[] = input.elements.map((element: LayoutElement, index) => ({
    index,
    id: element.id,
    x: element.x,
    y: element.y,
    width: element.width,
    height: element.height,
    tier: tierOf(element.type, tiers),
    fixed: element.pinned === true,
  }));
  const byId = new Map(nodes.map((node) => [node.id, node]));
  const links = input.connections
    .map((connection) => ({ a: byId.get(connection.sourceId), b: byId.get(connection.targetId) }))
    .filter((link): link is { a: Node; b: Node } => link.a !== undefined && link.b !== undefined && link.a !== link.b);
  const radii = ringRadii(nodes, tiers.length + 1);

  // A start: from a previous pass where there is one, else on the ring.
  let warm = false;
  const started = new Set<string>();
  for (const node of nodes) {
    const was = previous?.get(node.id);
    if (node.fixed) {
      started.add(node.id);
    } else if (was !== undefined) {
      node.x = was.x;
      node.y = was.y;
      started.add(node.id);
      warm = true;
    }
  }
  let unconnected = 0;
  for (let tier = 0; tier < radii.length; tier++) {
    for (const node of nodes) {
      if (node.tier !== tier || started.has(node.id)) {
        continue;
      }
      // The mean direction of the neighbours that already have a place.
      let sumX = 0;
      let sumY = 0;
      for (const link of links) {
        const other = link.a === node ? link.b : link.b === node ? link.a : undefined;
        if (other !== undefined && started.has(other.id) && (other.x !== 0 || other.y !== 0)) {
          const length = Math.hypot(other.x, other.y);
          sumX += other.x / length;
          sumY += other.y / length;
        }
      }
      const angle = sumX !== 0 || sumY !== 0 ? Math.atan2(sumY, sumX) + unconnected * 1e-3 : unconnected * GOLDEN_ANGLE;
      unconnected++;
      node.x = Math.cos(angle) * radii[tier];
      node.y = Math.sin(angle) * radii[tier];
      started.add(node.id);
    }
  }

  const steps = warm ? WARM_STEPS : STEPS;
  const heat = warm ? WARM_HEAT : 1;
  const moveX = new Float64Array(nodes.length);
  const moveY = new Float64Array(nodes.length);
  for (let step = 0; step < steps; step++) {
    const alpha = heat * (1 - step / steps);
    moveX.fill(0);
    moveY.fill(0);

    for (let i = 0; i < nodes.length; i++) {
      for (let j = i + 1; j < nodes.length; j++) {
        let dx = nodes[j].x - nodes[i].x;
        let dy = nodes[j].y - nodes[i].y;
        if (dx === 0 && dy === 0) {
          // Two elements on one point have no direction to part in; their order gives one.
          dx = 1e-3 * (j - i);
          dy = 1e-3;
        }
        const distance = Math.hypot(dx, dy);
        const push = REPULSION / (distance * distance);
        moveX[i] -= (dx / distance) * push;
        moveY[i] -= (dy / distance) * push;
        moveX[j] += (dx / distance) * push;
        moveY[j] += (dy / distance) * push;
      }
    }

    for (const link of links) {
      const i = link.a.index;
      const j = link.b.index;
      const dx = link.b.x - link.a.x;
      const dy = link.b.y - link.a.y;
      const distance = Math.hypot(dx, dy) || 1e-3;
      // A connection wants its ends one ring gap apart per ring between them, and no closer than that.
      const rest = RING_GAP * Math.max(1, Math.abs(link.a.tier - link.b.tier));
      const pull = SPRING * (distance - rest);
      moveX[i] += (dx / distance) * pull;
      moveY[i] += (dy / distance) * pull;
      moveX[j] -= (dx / distance) * pull;
      moveY[j] -= (dy / distance) * pull;
    }

    for (let i = 0; i < nodes.length; i++) {
      const node = nodes[i];
      if (node.fixed) {
        continue;
      }
      const distance = Math.hypot(node.x, node.y);
      const radius = radii[node.tier];
      if (distance > 1e-6) {
        const toRing = RING_PULL * (radius - distance);
        moveX[i] += (node.x / distance) * toRing;
        moveY[i] += (node.y / distance) * toRing;
      } else if (radius > 0) {
        moveX[i] += RING_PULL * radius;
      }
      const length = Math.hypot(moveX[i], moveY[i]);
      const scale = length > MAX_STEP ? MAX_STEP / length : 1;
      node.x += moveX[i] * scale * alpha;
      node.y += moveY[i] * scale * alpha;
    }
  }

  separate(nodes);

  const placements = new Map<string, LayoutPlacement>();
  for (const node of nodes) {
    if (!node.fixed) {
      // Rounded, so two machines that differ in the last bits of a cosine still draw one picture.
      placements.set(node.id, { x: Math.round(node.x * 100) / 100, y: Math.round(node.y * 100) / 100 });
    }
  }
  return placements;
}

/** Pushes overlapping rectangles apart until none overlaps; a locked one never moves. */
function separate(nodes: Node[]): void {
  for (let pass = 0; pass < OVERLAP_PASSES; pass++) {
    let moved = false;
    for (let i = 0; i < nodes.length; i++) {
      for (let j = i + 1; j < nodes.length; j++) {
        const a = nodes[i];
        const b = nodes[j];
        if (a.fixed && b.fixed) {
          continue;
        }
        const overlapX = (a.width + b.width) / 2 + CLEARANCE - Math.abs(b.x - a.x);
        const overlapY = (a.height + b.height) / 2 + CLEARANCE - Math.abs(b.y - a.y);
        if (overlapX <= 0 || overlapY <= 0) {
          continue;
        }
        moved = true;
        // Along the axis that needs less; on a tie, and for two on one point, by their order.
        const alongX = overlapX <= overlapY;
        const sign = alongX ? (b.x > a.x || (b.x === a.x && j > i) ? 1 : -1) : b.y > a.y || (b.y === a.y && j > i) ? 1 : -1;
        const amount = (alongX ? overlapX : overlapY) + 0.01;
        const shareA = a.fixed ? 0 : b.fixed ? 1 : 0.5;
        const shareB = b.fixed ? 0 : a.fixed ? 1 : 0.5;
        if (alongX) {
          a.x -= sign * amount * shareA;
          b.x += sign * amount * shareB;
        } else {
          a.y -= sign * amount * shareA;
          b.y += sign * amount * shareB;
        }
      }
    }
    if (!moved) {
      return;
    }
  }
}

/**
 * Ids, sizes, types, what is pinned and where, and the connections. An unpinned element's own
 * position is not among them: this layout decides it, so the model moving it changes nothing.
 */
function signature(input: LayoutInput): string {
  const elements = input.elements.map((element) =>
    [element.id, element.type ?? "", element.width, element.height, element.pinned === true ? `p${element.x},${element.y}` : ""].join("|"),
  );
  const connections = input.connections.map((connection) => `${connection.sourceId}>${connection.targetId}`);
  return `${elements.join(";")}#${connections.join(";")}`;
}

/** Where a point in the placed space stands in the model's: the same point - nothing is transformed. */
const inverse = (point: ShapePoint): ShapePoint => point;

export const tieredForceLayout: LayoutAlgorithm = {
  mode: "tiered-force",
  place,
  inverse,
  signature,
};
