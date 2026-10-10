import { describe, expect, it } from "vitest";
import type { LayoutDefinition } from "../definition/diagramDefinition";
import type { LayoutElement, LayoutInput, LayoutPlacement } from "./layoutAlgorithm";
import { tieredForceLayout } from "./tieredForceLayout";

/**
 * The radiating layout, as a pure function (agent-activity-diagram Requirements 6.5 to 6.10).
 *
 * Asserted on a generated diagram shaped like the one it was written for - projects, their
 * specifications, the agents on them, each agent's locations and the environments those run on -
 * because the properties below are about a population and a toy of five elements shows none of
 * them: nothing overlaps in a toy whatever the algorithm does.
 */

const TIERS = [["project"], ["specification"], ["agent"], ["location"], ["environment"]];
const definition: LayoutDefinition = { modes: ["tiered-force"], tiers: TIERS };

interface Generated {
  input: LayoutInput;
  byId: Map<string, LayoutElement>;
}

/** A deterministic diagram: `projects` projects, three specifications each, and so on outwards. */
function generate(projects: number): Generated {
  const elements: LayoutElement[] = [];
  const connections: { sourceId: string; targetId: string }[] = [];
  const add = (id: string, type: string, width: number, height: number) => elements.push({ id, type, x: 0, y: 0, width, height });
  const environments = ["e-local", "e-cloud", "e-container"];
  for (const id of environments) {
    add(id, "environment", 160, 48);
  }
  for (let p = 0; p < projects; p++) {
    add(`p${p}`, "project", 180, 48);
    for (let s = 0; s < 3; s++) {
      const spec = `p${p}s${s}`;
      // Specifications differ a great deal in height: one has its task groups unfolded.
      add(spec, "specification", 240, s === 0 ? 260 : 96);
      connections.push({ sourceId: `p${p}`, targetId: spec });
      for (let a = 0; a < 2; a++) {
        const agent = `${spec}a${a}`;
        add(agent, "agent", 150, 44);
        connections.push({ sourceId: spec, targetId: agent });
        const location = `${agent}l`;
        add(location, "location", 200, a === 0 ? 140 : 72);
        connections.push({ sourceId: agent, targetId: location });
        connections.push({ sourceId: location, targetId: environments[(p + s + a) % environments.length] });
      }
    }
  }
  // One of each outer type with no relation at all.
  add("idle-agent", "agent", 150, 44);
  add("spare-environment", "environment", 160, 48);
  return { input: { elements, connections }, byId: new Map(elements.map((element) => [element.id, element])) };
}

function placed(input: LayoutInput, previous?: ReadonlyMap<string, LayoutPlacement>): ReadonlyMap<string, LayoutPlacement> {
  return tieredForceLayout.place(input, definition, previous) as ReadonlyMap<string, LayoutPlacement>;
}

function overlapping(input: LayoutInput, positions: ReadonlyMap<string, LayoutPlacement>): string[] {
  const boxes = input.elements.map((element) => {
    const at = positions.get(element.id) ?? { x: element.x, y: element.y };
    return { id: element.id, left: at.x - element.width / 2, right: at.x + element.width / 2, top: at.y - element.height / 2, bottom: at.y + element.height / 2 };
  });
  const pairs: string[] = [];
  for (let i = 0; i < boxes.length; i++) {
    for (let j = i + 1; j < boxes.length; j++) {
      const a = boxes[i];
      const b = boxes[j];
      if (a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom) {
        pairs.push(`${a.id}~${b.id}`);
      }
    }
  }
  return pairs;
}

function meanDistanceByType(input: LayoutInput, positions: ReadonlyMap<string, LayoutPlacement>): Map<string, number> {
  const sums = new Map<string, { total: number; count: number }>();
  for (const element of input.elements) {
    const at = positions.get(element.id)!;
    const entry = sums.get(element.type!) ?? { total: 0, count: 0 };
    entry.total += Math.hypot(at.x, at.y);
    entry.count++;
    sums.set(element.type!, entry);
  }
  return new Map([...sums].map(([type, { total, count }]) => [type, total / count]));
}

describe("the radiating layout", () => {
  it("places the tiers outwards from the centre, in the declared order", () => {
    const { input } = generate(2);
    const means = meanDistanceByType(input, placed(input));

    const order = TIERS.map((tier) => means.get(tier[0])!);
    expect(order).toEqual([...order].sort((a, b) => a - b));
    expect(order[0]).toBeLessThan(order[1]);
  });

  it("leaves no two elements overlapping, tall and short alike", () => {
    // The planted defect this was seen to fail against: the separating pass removed.
    for (const projects of [1, 2, 4]) {
      const { input } = generate(projects);

      expect(overlapping(input, placed(input)), `${projects} project(s)`).toEqual([]);
    }
  });

  it("gives the same picture twice", () => {
    const first = placed(generate(3).input);
    const second = placed(generate(3).input);

    expect([...second]).toEqual([...first]);
  });

  it("places an element with no relation on its own tier, not in the middle and not adrift", () => {
    const { input } = generate(2);
    const positions = placed(input);
    const means = meanDistanceByType(input, positions);
    const distance = (id: string) => Math.hypot(positions.get(id)!.x, positions.get(id)!.y);

    expect(distance("idle-agent")).toBeGreaterThan(means.get("specification")! * 0.8);
    expect(distance("idle-agent")).toBeLessThan(means.get("location")! * 1.2);
    expect(distance("spare-environment")).toBeGreaterThan(means.get("location")! * 0.8);
  });

  it("puts a single project in the centre", () => {
    const { input } = generate(1);
    const at = placed(input).get("p0")!;

    expect(Math.hypot(at.x, at.y)).toBeLessThan(120);
  });
});

describe("the radiating layout and a locked element", () => {
  it("returns no placement for it, and keeps the others clear of where it is", () => {
    const { input } = generate(2);
    const pinned = input.elements.map((element) => (element.id === "p0s0" ? { ...element, x: 900, y: -700, pinned: true } : element));
    const withPinned: LayoutInput = { elements: pinned, connections: input.connections };

    const positions = placed(withPinned);

    expect(positions.has("p0s0")).toBe(false);
    expect(positions.size).toBe(input.elements.length - 1);
    expect(overlapping(withPinned, positions)).toEqual([]);
  });

  it("draws what is connected to it towards it", () => {
    const { input } = generate(1);
    const free = placed(input);
    const pinned = input.elements.map((element) => (element.id === "p0s1" ? { ...element, x: 1500, y: 0, pinned: true } : element));
    const moved = placed({ elements: pinned, connections: input.connections });

    // Its two agents end up nearer the locked place than they were without the lock.
    for (const agent of ["p0s1a0", "p0s1a1"]) {
      const before = Math.hypot(free.get(agent)!.x - 1500, free.get(agent)!.y);
      const after = Math.hypot(moved.get(agent)!.x - 1500, moved.get(agent)!.y);
      expect(after, agent).toBeLessThan(before);
    }
  });
});

describe("the radiating layout from where the picture is", () => {
  it("moves the elements a change does not touch only a little", () => {
    // The planted defect this was seen to fail against: the previous placements ignored, so the
    // pass starts from nothing and the same addition moves everything.
    const { input } = generate(4);
    const settled = placed(input);
    const grown: LayoutInput = {
      elements: [...input.elements, { id: "new-agent", type: "agent", x: 0, y: 0, width: 150, height: 44 }],
      connections: [...input.connections, { sourceId: "p0s2", targetId: "new-agent" }],
    };

    const after = placed(grown, settled);

    // Everything on the other three projects' side of the picture.
    const untouched = input.elements.filter((element) => !element.id.startsWith("p0") && !element.id.startsWith("e-") && element.type !== "environment");
    const moves = untouched.map((element) => Math.hypot(after.get(element.id)!.x - settled.get(element.id)!.x, after.get(element.id)!.y - settled.get(element.id)!.y));
    expect(Math.max(...moves)).toBeLessThan(60);
    expect(overlapping(grown, after)).toEqual([]);
  });
});

describe("what a layout pass depends on", () => {
  it("is unchanged by anything that does not place differently, and changed by anything that does", () => {
    const { input } = generate(1);
    const layoutSignatureOf = tieredForceLayout.signature!;
    const signature = layoutSignatureOf(input);

    // An unpinned element's own position is not an input: the layout decides it.
    expect(layoutSignatureOf({ ...input, elements: input.elements.map((element) => ({ ...element, x: 5, y: 9 })) })).toBe(signature);
    expect(layoutSignatureOf({ ...input, elements: input.elements.map((element, index) => (index === 0 ? { ...element, height: element.height + 18 } : element)) })).not.toBe(signature);
    expect(layoutSignatureOf({ ...input, elements: input.elements.map((element, index) => (index === 0 ? { ...element, pinned: true } : element)) })).not.toBe(signature);
    expect(layoutSignatureOf({ ...input, connections: input.connections.slice(1) })).not.toBe(signature);
  });
});

/**
 * At size (agent-activity-diagram Requirement 11.2): about two hundred elements, which is a team
 * of a dozen projects. Measured on 2026-10-09 at 197 elements: a first pass took 93 ms and a pass
 * after one addition 45 ms, with nothing overlapping after either. The step counts in the
 * algorithm are fixed from that measurement; its time is held as a multiple of a
 * reference workload timed beside it, not in milliseconds: a fixed bound of 150 ms passed here and
 * failed on the shared build machine, where the same pass took 200 ms.
 */
describe("the radiating layout at about two hundred elements", () => {
  const { input } = generate(12);

  it("still leaves nothing overlapping and still gives the same picture twice", () => {
    const first = placed(input);

    expect(input.elements.length).toBeGreaterThanOrEqual(190);
    expect(overlapping(input, first)).toEqual([]);
    expect([...placed(input)]).toEqual([...first]);
  });

  it("places one addition without laying everything out from nothing", () => {
    // The planted defect this was seen to fail against: the step counts raised tenfold.
    const settled = placed(input);
    const grown: LayoutInput = {
      elements: [...input.elements, { id: "new-agent", type: "agent", x: 0, y: 0, width: 150, height: 44 }],
      connections: [...input.connections, { sourceId: "p0s2", targetId: "new-agent" }],
    };

    const after = placed(grown, settled);
    expect(overlapping(grown, after)).toEqual([]);

    // The pass is held to a multiple of a reference workload timed here, beside it: one sweep over
    // every pair of elements, the arithmetic a single step of the layout does. A slower machine
    // slows both alike, so the ratio says how many sweeps' worth of work a pass is - which is
    // what a regression changes - and not how fast the machine is. The fastest of several runs of
    // each is used, since a busy machine slows a run and never speeds one up.
    const count = grown.elements.length;
    const sweep = () => {
      let total = 0;
      for (let i = 0; i < count; i++) {
        for (let j = i + 1; j < count; j++) {
          const dx = (j - i) * 1.5 + total * 1e-12;
          const dy = (i + 1) * 0.5;
          const distance = Math.hypot(dx, dy);
          total += (dx / distance) * (26000 / (distance * distance));
        }
      }
      return total;
    };
    // The two are timed in turn, round after round, and the fastest of each is kept: a busy
    // machine slows a run and never speeds one up, and timing them in turn gives both the same
    // chance of a quiet moment. Timed apart, three runs each, a loaded machine once put a pass at
    // 252 sweeps and a quiet one at 105.
    const timed = (work: () => unknown) => {
      const started = performance.now();
      work();
      return performance.now() - started;
    };
    let reference = Number.POSITIVE_INFINITY;
    let pass = Number.POSITIVE_INFINITY;
    for (let round = 0; round < 12; round++) {
      // Fifty sweeps a round, so the reference is long enough to time.
      reference = Math.min(reference, timed(() => { for (let n = 0; n < 50; n++) { sweep(); } }) / 50);
      pass = Math.min(pass, timed(() => placed(grown, settled)));
    }

    // Measured at about 105 sweeps; with the step counts raised tenfold, about 555. Beside the whole
    // backend suite a pass was once timed at 314, so the bound sits between that and the defect.
    expect(pass / reference).toBeLessThan(420);
    // The ratio is the bound, not the clock: on a slow build server the measuring itself outran
    // the runner's default five seconds, so this test names its own deadline.
  }, 60_000);
});
