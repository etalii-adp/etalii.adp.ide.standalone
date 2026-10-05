import { create } from "@bufbuild/protobuf";
import { describe, expect, it } from "vitest";
import { AbmChildPayloadSchema, AbmNodePayloadSchema } from "@client/generated/agent-behavior-modelling_pb";
import { arrangementOf, previewOf } from "./abmDrag";
import type { AbmLine, AbmModel, AbmNode } from "./abmModel";

function node(id: string, x: number, y: number): AbmNode {
  return { id, kind: "action", x, y, payload: create(AbmNodePayloadSchema, { keyword: "Do", label: id, width: 200, height: 60 }) };
}

function line(from: string, to: string): AbmLine {
  return { id: `child:${to}`, payload: create(AbmChildPayloadSchema, { fromElementId: from, toElementId: to, index: 1 }) };
}

/** A root over three children 28 apart, the first with a child of its own - centres, as the backend lays them out. */
function tree(): AbmModel {
  const nodes = [node("1", 328, 30), node("1.1", 100, 146), node("1.2", 328, 146), node("1.3", 556, 146), node("1.1.1", 100, 262)];
  const lines = [line("1", "1.1"), line("1", "1.2"), line("1", "1.3"), line("1.1", "1.1.1")];
  return { nodes: new Map(nodes.map((each) => [each.id, each])), lines: new Map(lines.map((each) => [each.id, each])) };
}

describe("a drag in an agent behavior model", () => {
  it("swaps a node past its sibling's middle, carrying its subtree to the slot", () => {
    // Act.
    const arrangement = arrangementOf(tree(), "1.1", 340, 146)!;

    // Assert.
    expect([arrangement.from, arrangement.to, arrangement.nothing]).toEqual([0, 1, false]);
    expect(arrangement.offsets.get("1.2")).toEqual({ dx: -228, dy: 0 });
    expect(arrangement.offsets.get("1.1")).toEqual({ dx: 228, dy: 0 });
    expect(arrangement.offsets.get("1.1.1")).toEqual({ dx: 228, dy: 0 });
    expect(arrangement.offsets.get("1.3")).toEqual({ dx: 0, dy: 0 });
    expect(arrangement.offsets.has("1")).toBe(false);
  });

  it("moves the whole row, and everything beneath it, as far down as the node went", () => {
    // Act.
    const arrangement = arrangementOf(tree(), "1.2", 328, 196)!;

    // Assert.
    expect(arrangement.nothing).toBe(false);
    for (const id of ["1.1", "1.2", "1.3", "1.1.1"]) {
      expect(arrangement.offsets.get(id)).toEqual({ dx: 0, dy: 50 });
    }
  });

  it("never lifts a row closer to its parent than the minimum gap", () => {
    // Act: far above the root.
    const arrangement = arrangementOf(tree(), "1.3", 556, -400)!;

    // Assert: the root's bottom (60) plus 16, plus half the node, is where the row's middle stops.
    expect(arrangement.dy).toBe(30 + 30 + 16 + 30 - 146);
  });

  it("changes nothing when the node stays between its neighbours at its own height", () => {
    // Act.
    const arrangement = arrangementOf(tree(), "1.2", 300, 146)!;

    // Assert.
    expect(arrangement.nothing).toBe(true);
  });

  it("while the node is under the pointer, has its subtree follow the pointer and its row follow it down", () => {
    // Act.
    const offsets = previewOf(tree(), "1.1", 150, 186)!;

    // Assert.
    expect(offsets.has("1.1")).toBe(false);
    expect(offsets.get("1.1.1")).toEqual({ dx: 50, dy: 40 });
    expect(offsets.get("1.2")).toEqual({ dx: 0, dy: 40 });
  });
});
