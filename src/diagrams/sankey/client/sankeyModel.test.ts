import { describe, expect, it } from "vitest";
import { create, toBinary } from "@bufbuild/protobuf";
import { DeltaSchema, type Delta } from "@client/generated/deltas_pb";
import { SankeyFlowPayloadSchema, SankeyNodePayloadSchema } from "@client/generated/sankey_pb";
import { applyDelta, columnPlaceOf, emptyModel, NODE_GAP, type SankeyModel } from "./sankeyModel";
import { bandPath, centrePath, paintOf } from "./SankeyCanvas";

function add(...elements: { id: string; type: string; bytes: Uint8Array; x?: number; y?: number }[]): Delta {
  return create(DeltaSchema, {
    action: {
      case: "add",
      value: {
        elements: elements.map((element) => ({
          id: { value: element.id },
          type: element.type,
          position: { x: element.x ?? 0, y: element.y ?? 0 },
          payload: { typeUrl: "", value: element.bytes },
        })),
      },
    },
  });
}

const node = toBinary(SankeyNodePayloadSchema, create(SankeyNodePayloadSchema, { name: "Revenue", width: 24, height: 100, color: "grey" }));
const flow = toBinary(SankeyFlowPayloadSchema, create(SankeyFlowPayloadSchema, { fromElementId: "a", toElementId: "b", thickness: 12 }));

describe("the sankey fold", () => {
  it("sorts nodes and flows by their wire type, and ignores a type it does not know", () => {
    // Act.
    const model = applyDelta(emptyModel, add(
      { id: "a", type: "etalii/sankey+node", bytes: node, x: 12, y: 50 },
      { id: "a->b", type: "etalii/sankey+flow", bytes: flow },
      { id: "odd", type: "etalii/sankey+group", bytes: node },
      { id: "alien", type: "etalii/supply-chain+flow", bytes: flow },
    ));

    // Assert.
    expect([...model.nodes.keys()]).toEqual(["a"]);
    expect([...model.flows.keys()]).toEqual(["a->b"]);
    expect(model.nodes.get("a")).toMatchObject({ x: 12, y: 50 });
    expect(model.nodes.get("a")!.payload.name).toBe("Revenue");
    expect(model.flows.get("a->b")!.payload.thickness).toBe(12);
  });

  it("removes by id, and never touches the model it was given", () => {
    // Arrange.
    const before = applyDelta(emptyModel, add({ id: "a", type: "etalii/sankey+node", bytes: node }, { id: "a->b", type: "etalii/sankey+flow", bytes: flow }));

    // Act.
    const after = applyDelta(before, create(DeltaSchema, { action: { case: "remove", value: { elementIds: [{ value: "a" }] } } }));

    // Assert.
    expect(after.nodes.size).toBe(0);
    expect(after.flows.size).toBe(1);
    expect(before.nodes.size).toBe(1);
  });
});

/** Three nodes stacked in the first column, 100 tall and the gap apart, and one in the second. */
function column(): SankeyModel {
  const at = (column: number, height: number) => create(SankeyNodePayloadSchema, { width: 24, height, column });
  return {
    nodes: new Map([
      ["a", { id: "a", x: 12, y: 50, payload: at(0, 100) }],
      ["b", { id: "b", x: 12, y: 50 + 100 + NODE_GAP, payload: at(0, 100) }],
      ["c", { id: "c", x: 12, y: 50 + 2 * (100 + NODE_GAP), payload: at(0, 100) }],
      ["z", { id: "z", x: 332, y: 50, payload: at(1, 300) }],
    ]),
    flows: new Map(),
  };
}

describe("where a dragged node lands in its column", () => {
  it("stays put when it passes nobody", () => {
    // Act.
    const place = columnPlaceOf(column(), "b", 186 + 30);

    // Assert.
    expect(place).toMatchObject({ index: 1, moved: false, slotY: 186 });
    expect(place!.shifts.size).toBe(0);
  });

  it("moves the nodes it passes going down up into the room it left, and lands below them", () => {
    // Act: a's middle dragged just past c's.
    const place = columnPlaceOf(column(), "a", 322 + 1);

    // Assert: b and c each move up by a's height and the gap; a takes c's old place.
    expect(place!.index).toBe(2);
    expect(Object.fromEntries(place!.shifts)).toEqual({ b: -136, c: -136 });
    expect(place!.slotY).toBe(322);
  });

  it("moves the nodes it passes going up down below it, and lands where the first of them was", () => {
    // Act.
    const place = columnPlaceOf(column(), "c", 49);

    // Assert.
    expect(place!.index).toBe(0);
    expect(Object.fromEntries(place!.shifts)).toEqual({ a: 136, b: 136 });
    expect(place!.slotY).toBe(50);
  });

  it("never moves a node of another column", () => {
    // Act.
    const place = columnPlaceOf(column(), "a", 10_000);

    // Assert.
    expect(place!.shifts.has("z")).toBe(false);
  });
});

describe("the band", () => {
  /** The y of a cubic Bézier path's segment at x, by bisection - the band's edge where it crosses x. */
  function yAt(d: string, segment: number, x: number): number {
    const curves = [...d.matchAll(/(?:M|L)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+C\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)/g)]
      .map((match) => match.slice(1).map(Number));
    const [x0, y0, x1, y1, x2, y2, x3, y3] = curves[segment];
    const at = (t: number, a: number, b: number, c: number, e: number) => (1 - t) ** 3 * a + 3 * (1 - t) ** 2 * t * b + 3 * (1 - t) * t ** 2 * c + t ** 3 * e;
    let [low, high] = [0, 1];
    for (let i = 0; i < 60; i++) {
      const mid = (low + high) / 2;
      const ascending = x3 >= x0;
      if ((at(mid, x0, x1, x2, x3) < x) === ascending) low = mid;
      else high = mid;
    }
    return at(low, y0, y1, y2, y3);
  }

  it("is as thick as its value, measured down, all the way along - even on the slope", () => {
    // Arrange: a steep band, 30 thick, falling 200 over 296.
    const d = bandPath({ x: 24, y: 100 }, { x: 320, y: 300 }, 30);

    // Act + Assert.
    for (const x of [24, 100, 172, 250, 320]) {
      expect(yAt(d, 1, x) - yAt(d, 0, x)).toBeCloseTo(30, 3);
    }
  });

  it("leaves and arrives level, so it meets each bar square", () => {
    // Act.
    const d = centrePath({ x: 24, y: 100 }, { x: 320, y: 300 });

    // Assert: the first control point shares the start's y, the second the end's.
    expect(d).toBe("M 24 100 C 172 100 172 300 320 300");
  });

  it("is painted by its palette word's theme token, or by its own colour", () => {
    // Act + Assert.
    expect(paintOf("red", "")).toBe("--color-diagram-sankey-red");
    expect(paintOf("", "")).toBe("--color-diagram-sankey-grey");
    expect(paintOf("", "#336699")).toBe("#336699");
  });
});
