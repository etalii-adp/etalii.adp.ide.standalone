import { describe, expect, it } from "vitest";
import { create } from "@bufbuild/protobuf";
import { ElementSchema } from "@client/generated/elements_pb";
import { connectionOf, elementOf, isConnectionElement, structuralModelOf } from "./structuralModel";

const node = (id: string, overrides: Record<string, unknown> = {}) =>
  create(ElementSchema, {
    id: { value: id },
    position: { x: 10, y: 20 },
    type: "dotnet/dependency-graph+project",
    ...overrides,
  });

const edge = (id: string, from: string, to: string, overrides: Record<string, unknown> = {}) =>
  create(ElementSchema, {
    id: { value: id },
    type: "dotnet/dependency-graph+edge",
    sourceId: { value: from },
    targetId: { value: to },
    ...overrides,
  });

describe("the structural model — what the wire now sends shaped", () => {
  it("splits nodes from connections by their ENDS, not by a naming rule", () => {
    // The discriminator that lets this live here instead of in thirteen canvases with thirteen
    // rules: an element naming two ends is an edge, in every notation. dotnet-dependency-graph
    // parses them out of the element id today; others read them from their payload.
    const model = structuralModelOf([node("a"), node("b"), edge("a->b", "a", "b")]);

    expect(model.elements.map((e) => e.id)).toEqual(["a", "b"]);
    expect(model.connections.map((c) => c.id)).toEqual(["a->b"]);
  });

  it("takes identity, position and size straight off the wire", () => {
    const mapped = elementOf(node("a", { size: { width: 220, height: 56 } }));

    expect(mapped).toMatchObject({ id: "a", x: 10, y: 20, width: 220, height: 56 });
  });

  it("prefers the definition's own element type over the mime-style one", () => {
    // Every module hard-codes this mapping in its canvas today - `type: "node"` beside a wire
    // type of "dotnet/dependency-graph+project".
    expect(elementOf(node("a", { elementType: "node" })).type).toBe("node");
  });

  it("falls back to the mime type when the backend sends no element type", () => {
    // The inertness that keeps the twelve unmigrated modules working: an element setting none
    // of the new fields maps to what its module would have built by hand.
    expect(elementOf(node("a")).type).toBe("dotnet/dependency-graph+project");
  });

  it("omits size entirely rather than inventing zeroes when the wire sends none", () => {
    // A zero size is a drawn point; an absent one lets the type's sizing rule decide. Sending
    // 0x0 for "unknown" would draw every unmigrated element as nothing.
    const mapped = elementOf(node("a"));

    expect(mapped.width).toBeUndefined();
    expect(mapped.height).toBeUndefined();
  });

  it("carries the anchors a connection names, and omits them when it names none", () => {
    expect(connectionOf(edge("e", "a", "b", { sourceAnchor: "e", targetAnchor: "w" }))).toMatchObject({
      sourceAnchor: "e",
      targetAnchor: "w",
    });

    const plain = connectionOf(edge("e", "a", "b"));
    expect(plain.sourceAnchor).toBeUndefined();
    expect(plain.targetAnchor).toBeUndefined();
  });

  it("hands the module's decoded payload through untouched", () => {
    // The one thing a module still supplies, and it DECODES rather than maps: it knows how to
    // read its own Any and nothing here does.
    const model = structuralModelOf([node("a")], () => ({ name: "Pipeline.Core" }));

    expect(model.elements[0]!.payload).toEqual({ name: "Pipeline.Core" });
  });

  it("emits a connection whose ends have not both arrived, rather than dropping it", () => {
    // The canvas already declines to draw a connector to an element it does not hold. Dropping
    // it here would hide a delivery order from the module that may resolve on the next delta.
    const model = structuralModelOf([edge("a->b", "a", "b")]);

    expect(model.connections).toHaveLength(1);
    expect(model.elements).toEqual([]);
  });

  it("treats a half-declared edge as a node, because one end is not a connection", () => {
    const halfEdge = create(ElementSchema, { id: { value: "x" }, type: "t", sourceId: { value: "a" } });

    expect(isConnectionElement(halfEdge)).toBe(false);
    expect(structuralModelOf([halfEdge]).elements).toHaveLength(1);
  });
});
