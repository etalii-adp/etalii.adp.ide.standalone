import { describe, expect, it } from "vitest";
import { create, toBinary } from "@bufbuild/protobuf";
import { DeltaSchema } from "@client/generated/deltas_pb";
import { ElementSchema, type Element } from "@client/generated/elements_pb";
import {
  DependencyGraphElementPayloadSchema,
  DependencyGraphRelationPayloadSchema,
} from "@client/generated/dependency-graph_pb";
import { NODE, RELATION, applyDelta, emptyModel } from "./dependencyGraphModel";

function element(id: string, type: string, payload: Uint8Array, x = 0, y = 0): Element {
  return create(ElementSchema, {
    id: { value: id },
    type,
    position: { x, y },
    payload: { typeUrl: `type.googleapis.com/${type}`, value: payload },
  });
}

function addDelta(...elements: Element[]) {
  return create(DeltaSchema, { action: { case: "add", value: { elements } } });
}

function nodePayload(x: number, row: number, label: string): Uint8Array {
  return toBinary(
    DependencyGraphElementPayloadSchema,
    create(DependencyGraphElementPayloadSchema, { x, row, label }),
  );
}

function relationPayload(from: string, to: string, label: string): Uint8Array {
  return toBinary(
    DependencyGraphRelationPayloadSchema,
    create(DependencyGraphRelationPayloadSchema, { fromElementId: from, toElementId: to, label }),
  );
}

describe("dependencyGraphModel", () => {
  it("folds two nodes and a dependency out of one add", () => {
    // Arrange.
    const delta = addDelta(
      element("aaa", NODE, nodePayload(240, 0, "API gateway"), 240, 0),
      element("bbb", NODE, nodePayload(480.5, 2, "Identity service"), 480.5, 120),
      element("ccc", RELATION, relationPayload("aaa", "bbb", "verifies tokens with")),
    );

    // Act.
    const model = applyDelta(emptyModel, delta);

    // Assert.
    expect(model.elements.get("aaa")).toMatchObject({ label: "API gateway", x: 240, row: 0 });
    expect(model.elements.get("bbb")).toMatchObject({ label: "Identity service", x: 480.5, row: 2 });
    expect(model.relations.get("ccc")).toMatchObject({
      fromElementId: "aaa",
      toElementId: "bbb",
      label: "verifies tokens with",
    });
  });

  it("keeps a dependency's ends in the order the backend sent them", () => {
    // Arrange.
    // Which end is which is the type's whole content: swapping them here would draw the
    // arrowhead at the wrong box while the diagram still looked like a graph.
    const delta = addDelta(element("ccc", RELATION, relationPayload("dependent", "dependency", "")));

    // Act.
    const model = applyDelta(emptyModel, delta);

    // Assert.
    expect(model.relations.get("ccc")!.fromElementId).toBe("dependent");
    expect(model.relations.get("ccc")!.toElementId).toBe("dependency");
  });

  it("skips a foreign element without decoding it", () => {
    // Arrange.
    // Several modules share one delta stream, and this payload is NOT one of ours: bytes that
    // would throw if decoded as one. A sibling module shipped a decode-before-type-check once,
    // and one foreign element took the whole delta with it.
    const foreign = element("zzz", "wardley/map+element", new Uint8Array([255, 255, 255, 255, 255, 255]));
    const ours = element("aaa", NODE, nodePayload(0, 0, "Ours"));

    // Act.
    const model = applyDelta(emptyModel, addDelta(foreign, ours));

    // Assert.
    expect(model.elements.has("zzz")).toBe(false);
    expect(model.elements.get("aaa")?.label).toBe("Ours");
  });

  it("skips a timeline element in particular, though the two types are one fork apart", () => {
    // Arrange.
    // The likeliest foreign neighbour of all: a timeline open beside a graph. Its element types
    // differ, and this is what makes the type check rather than the payload shape decide.
    const timelineElement = element("ttt", "generic/timeline+period", new Uint8Array([255, 255, 255, 255]));

    // Act.
    const model = applyDelta(emptyModel, addDelta(timelineElement));

    // Assert.
    expect(model.elements.size).toBe(0);
    expect(model.relations.size).toBe(0);
  });

  it("treats an add as an upsert, so an edit never drops the node", () => {
    // Arrange.
    const first = applyDelta(emptyModel, addDelta(element("aaa", NODE, nodePayload(240, 0, "Before"))));

    // Act.
    const second = applyDelta(first, addDelta(element("aaa", NODE, nodePayload(240, 0, "After"))));

    // Assert.
    expect(second.elements.size).toBe(1);
    expect(second.elements.get("aaa")?.label).toBe("After");
  });

  it("removes nodes and dependencies by id", () => {
    // Arrange.
    const populated = applyDelta(emptyModel, addDelta(
      element("aaa", NODE, nodePayload(240, 0, "Doomed")),
      element("ccc", RELATION, relationPayload("aaa", "aaa", "")),
    ));

    // Act.
    const model = applyDelta(populated, create(DeltaSchema, {
      action: { case: "remove", value: { elementIds: [{ value: "aaa" }, { value: "ccc" }] } },
    }));

    // Assert.
    expect(model.elements.size).toBe(0);
    expect(model.relations.size).toBe(0);
  });
});
