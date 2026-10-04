import { create, toBinary } from "@bufbuild/protobuf";
import { describe, expect, it } from "vitest";
import { DeltaSchema } from "@client/generated/deltas_pb";
import { ElementSchema } from "@client/generated/elements_pb";
import { AbmChildPayloadSchema, AbmNodePayloadSchema } from "@client/generated/agent-behavior-modelling_pb";
import { applyDelta, emptyModel, kindOf } from "./abmModel";

function node(id: string, kind: string, label: string) {
  return create(ElementSchema, {
    id: { value: id },
    type: `etalii/agent-behavior-modelling+${kind}`,
    position: { x: 10, y: 20 },
    payload: { typeUrl: "x", value: toBinary(AbmNodePayloadSchema, create(AbmNodePayloadSchema, { keyword: "Do", label, width: 200, height: 60 })) },
  });
}

function line(id: string, from: string, to: string) {
  return create(ElementSchema, {
    id: { value: id },
    type: "etalii/agent-behavior-modelling+child",
    payload: { typeUrl: "x", value: toBinary(AbmChildPayloadSchema, create(AbmChildPayloadSchema, { fromElementId: from, toElementId: to, index: 1 })) },
  });
}

describe("the agent behavior model's fold", () => {
  it("decodes nodes and lines by their type, and an add upserts", () => {
    // Act.
    let model = applyDelta(emptyModel, create(DeltaSchema, { action: { case: "add", value: { elements: [node("1", "sequence", "Work"), node("1.1", "action", "Act"), line("child:1.1", "1", "1.1")] } } }));
    model = applyDelta(model, create(DeltaSchema, { action: { case: "add", value: { elements: [node("1.1", "check", "Ready")] } } }));

    // Assert.
    expect([...model.nodes.keys()]).toEqual(["1", "1.1"]);
    expect(model.nodes.get("1.1")?.kind).toBe("check");
    expect(model.nodes.get("1.1")?.payload.label).toBe("Ready");
    expect(model.lines.get("child:1.1")?.payload.toElementId).toBe("1.1");
  });

  it("removes by id, and ignores a type it does not know", () => {
    // Act.
    let model = applyDelta(emptyModel, create(DeltaSchema, { action: { case: "add", value: { elements: [node("1", "sequence", "Work"), node("2", "nonsense", "?")] } } }));
    model = applyDelta(model, create(DeltaSchema, { action: { case: "remove", value: { elementIds: [{ value: "1" }] } } }));

    // Assert.
    expect(model.nodes.size).toBe(0);
    expect(kindOf("etalii/agent-behavior-modelling+repeat")).toBe("repeat");
    expect(kindOf("etalii/functional-decomposition-graph+action")).toBeUndefined();
  });
});
