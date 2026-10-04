import { describe, expect, it } from "vitest";
import { create, toBinary } from "@bufbuild/protobuf";
import { DeltaSchema, type Delta } from "@client/generated/deltas_pb";
import {
  SupplyChainFlowPayloadSchema,
  SupplyChainGroupPayloadSchema,
  SupplyChainNodePayloadSchema,
} from "@client/generated/supply-chain_pb";
import { applyDelta, emptyModel, formatAmount } from "./supplyChainModel";
import { bandWidthOf, flowPath } from "./SupplyChainCanvas";

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

const node = toBinary(SupplyChainNodePayloadSchema, create(SupplyChainNodePayloadSchema, { name: "Mine", width: 192, height: 96 }));
const group = toBinary(SupplyChainGroupPayloadSchema, create(SupplyChainGroupPayloadSchema, { name: "North" }));
const flow = toBinary(SupplyChainFlowPayloadSchema, create(SupplyChainFlowPayloadSchema, { fromElementId: "mine", toElementId: "plant", product: "Ore" }));

describe("the supply chain fold", () => {
  it("sorts groups, stages and flows by their wire type, and ignores a type it does not know", () => {
    // Act.
    const model = applyDelta(emptyModel, add(
      { id: "north", type: "etalii/supply-chain+group", bytes: group },
      { id: "mine", type: "etalii/supply-chain+raw-material", bytes: node, x: 10, y: 20 },
      { id: "ore", type: "etalii/supply-chain+flow", bytes: flow },
      { id: "odd", type: "etalii/supply-chain+quarry", bytes: node },
    ));

    // Assert.
    expect([...model.groups.keys()]).toEqual(["north"]);
    expect(model.nodes.get("mine")).toMatchObject({ stage: "raw-material", x: 10, y: 20 });
    expect(model.flows.get("ore")?.payload.product).toBe("Ore");
    expect(model.nodes.has("odd")).toBe(false);
  });

  it("removes by id, whatever the id was", () => {
    // Arrange.
    const model = applyDelta(emptyModel, add({ id: "mine", type: "etalii/supply-chain+raw-material", bytes: node }, { id: "ore", type: "etalii/supply-chain+flow", bytes: flow }));

    // Act.
    const removed = applyDelta(model, create(DeltaSchema, { action: { case: "remove", value: { elementIds: [{ value: "mine" }, { value: "ore" }] } } }));

    // Assert.
    expect(removed.nodes.size).toBe(0);
    expect(removed.flows.size).toBe(0);
  });
});

describe("what a card and a flow show", () => {
  it("formats amounts plainly, with thousands grouped", () => {
    expect(formatAmount(5500)).toBe("5,500");
    expect(formatAmount(2.8)).toBe("2.8");
    expect(formatAmount(0.16)).toBe("0.16");
    expect(formatAmount(0)).toBe("0");
  });

  it("draws a heavier flow broader, within a band that never vanishes", () => {
    expect(bandWidthOf(0)).toBeGreaterThan(0);
    expect(bandWidthOf(1)).toBeGreaterThan(bandWidthOf(0.5));
    expect(bandWidthOf(5)).toBe(bandWidthOf(1));
  });

  it("runs a flow from the supplier's right side to the consumer's left, square to both", () => {
    // Act.
    const d = flowPath({ x: 0, y: 0 }, { x: 0, y: 0 }, {
      source: { x: 0, y: 0, width: 100, height: 50 },
      target: { x: 300, y: 100, width: 100, height: 50 },
    });

    // Assert: it starts at (100, 25), ends at (300, 125), and both control points keep their end's height.
    expect(d).toBe("M 100 25 C 200 25 200 125 300 125");
  });
});
