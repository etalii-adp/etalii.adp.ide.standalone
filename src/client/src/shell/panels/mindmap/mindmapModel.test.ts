import { describe, expect, it } from "vitest";
import { create, toBinary } from "@bufbuild/protobuf";
import { DeltaSchema, type Delta } from "../../../generated/deltas_pb";
import { ElementSchema } from "../../../generated/elements_pb";
import { MindmapNodePayloadSchema } from "../../../generated/mindmap_pb";
import { applyDelta, emptyModel, isFolded, nodeIdForPath, type MindmapModel } from "./mindmapModel";

function node(id: string, text: string, x = 0, y = 0): ReturnType<typeof create<typeof ElementSchema>> {
  return create(ElementSchema, {
    id: { value: id },
    position: { x, y },
    type: "freeplane/mindmap+node",
    payload: {
      typeUrl: "type.googleapis.com/etalii.adp.mindmap.MindmapNodePayload",
      value: toBinary(MindmapNodePayloadSchema, create(MindmapNodePayloadSchema, { text })),
    },
  });
}

const add = (...elements: ReturnType<typeof node>[]): Delta =>
  create(DeltaSchema, { action: { case: "add", value: { elements } } });

function seeded(): MindmapModel {
  return applyDelta(emptyModel, add(node("root", "Root"), node("a", "Alpha"), node("b", "Beta")));
}

describe("mindmapModel", () => {
  it("adds elements from an add delta, decoding the payload", () => {
    const model = seeded();

    expect(model.elements.size).toBe(3);
    expect(model.elements.get("a")?.payload.text).toBe("Alpha");
  });

  it("treats add as an upsert, replacing an element with the same id", () => {
    const model = applyDelta(seeded(), add(node("a", "Alpha renamed", 5, 7)));

    expect(model.elements.size).toBe(3);
    expect(model.elements.get("a")?.payload.text).toBe("Alpha renamed");
    expect(model.elements.get("a")?.x).toBe(5);
  });

  it("removes elements by id", () => {
    const model = applyDelta(seeded(), create(DeltaSchema, { action: { case: "remove", value: { elementIds: [{ value: "a" }] } } }));

    expect(model.elements.has("a")).toBe(false);
    expect(model.elements.size).toBe(2);
  });

  it("hides a branch on group and marks the parent folded", () => {
    const model = applyDelta(seeded(), create(DeltaSchema, {
      action: { case: "group", value: { sourceElementIds: [{ value: "a" }, { value: "b" }], groupElement: node("root", "Root") } },
    }));

    expect(model.elements.has("a")).toBe(false);
    expect(model.elements.has("b")).toBe(false);
    expect(isFolded(model, "root")).toBe(true);
  });

  it("brings a branch back on ungroup and clears the fold", () => {
    const folded = applyDelta(seeded(), create(DeltaSchema, {
      action: { case: "group", value: { sourceElementIds: [{ value: "a" }], groupElement: node("root", "Root") } },
    }));

    const model = applyDelta(folded, create(DeltaSchema, {
      action: { case: "ungroup", value: { groupElementId: { value: "root" }, elements: [node("a", "Alpha")] } },
    }));

    expect(model.elements.has("a")).toBe(true);
    expect(isFolded(model, "root")).toBe(false);
  });

  it("never mutates the input model", () => {
    const before = seeded();
    const sizeBefore = before.elements.size;

    applyDelta(before, add(node("c", "Gamma")));

    expect(before.elements.size).toBe(sizeBefore);
  });

  it("ignores an element of a type it does not render", () => {
    const other = create(ElementSchema, { id: { value: "x" }, type: "uml/class+box", payload: { typeUrl: "", value: new Uint8Array() } });
    const model = applyDelta(emptyModel, create(DeltaSchema, { action: { case: "add", value: { elements: [other] } } }));

    expect(model.elements.size).toBe(0);
  });

  it("finds a node id for a selection path by its innermost text", () => {
    expect(nodeIdForPath(seeded(), ["Root", "Alpha"])).toBe("a");
    expect(nodeIdForPath(seeded(), ["Root", "nope"])).toBeUndefined();
  });
});
