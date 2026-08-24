import { describe, expect, it } from "vitest";
import { create, toBinary } from "@bufbuild/protobuf";
import { ElementSchema } from "../../../generated/elements_pb";
import {
  C4BoundaryPayloadSchema,
  C4ElementPayloadSchema,
  C4RelationshipPayloadSchema,
  C4ViewPayloadSchema,
} from "../../../generated/c4_pb";
import { applyDelta, boxesOf, emptyModel, BOUNDARY_TYPE, NODE_TYPE, RELATIONSHIP_TYPE, VIEW_TYPE } from "./c4Model";

function element(id: string, type: string, payload: Uint8Array, x = 0, y = 0) {
  return create(ElementSchema, {
    id: { value: id },
    position: { x, y },
    type,
    payload: { typeUrl: `type.googleapis.com/${type}`, value: payload },
  });
}

function node(id: string, name: string, x = 0, y = 0, width = 160, height = 80) {
  return element(
    id,
    NODE_TYPE,
    toBinary(C4ElementPayloadSchema, create(C4ElementPayloadSchema, {
      name,
      typeLine: "[Software System]",
      description: "A system.",
      width,
      height,
      style: { background: "#1168bd", color: "#ffffff", shape: "RoundedBox" },
    })),
    x,
    y,
  );
}

function add(...elements: ReturnType<typeof element>[]) {
  return { action: { case: "add", value: { elements } } } as never;
}

describe("c4Model", () => {
  it("decodes nodes, relationships, boundaries and the view into their own collections", () => {
    const model = applyDelta(
      emptyModel,
      add(
        node("a", "Alpha", 10, 20),
        element("a->b", RELATIONSHIP_TYPE, toBinary(C4RelationshipPayloadSchema, create(C4RelationshipPayloadSchema, {
          sourceId: "a",
          destinationId: "b",
          description: "Uses",
          technology: "HTTPS",
        }))),
        element("boundary:s", BOUNDARY_TYPE, toBinary(C4BoundaryPayloadSchema, create(C4BoundaryPayloadSchema, {
          name: "System",
          kind: "Software System",
          width: 400,
          height: 300,
        }))),
        element("c4:view", VIEW_TYPE, toBinary(C4ViewPayloadSchema, create(C4ViewPayloadSchema, {
          title: "System Context diagram for Alpha",
          viewKind: "SystemContext",
          viewKey: "context",
          legend: [{ label: "Software System", style: { background: "#1168bd" } }],
        }))),
      ),
    );

    expect(model.nodes.get("a")?.payload.name).toBe("Alpha");
    expect(model.relationships.get("a->b")?.payload.technology).toBe("HTTPS");
    expect(model.boundaries.get("boundary:s")?.payload.name).toBe("System");
    expect(model.view?.title).toBe("System Context diagram for Alpha");
  });

  it("treats add as an upsert, so a re-delivered element replaces the old one", () => {
    // The backend re-delivers the whole view when the shared document changes, which is how a
    // rename made through another view reaches this one.
    const first = applyDelta(emptyModel, add(node("a", "Alpha")));

    const second = applyDelta(first, add(node("a", "Renamed")));

    expect(second.nodes.size).toBe(1);
    expect(second.nodes.get("a")?.payload.name).toBe("Renamed");
  });

  it("removes ids from whichever collection holds them", () => {
    const model = applyDelta(
      emptyModel,
      add(
        node("a", "Alpha"),
        element("a->b", RELATIONSHIP_TYPE, toBinary(C4RelationshipPayloadSchema, create(C4RelationshipPayloadSchema, {}))),
      ),
    );

    const after = applyDelta(model, { action: { case: "remove", value: { elementIds: [{ value: "a" }, { value: "a->b" }] } } } as never);

    expect(after.nodes.size).toBe(0);
    expect(after.relationships.size).toBe(0);
  });

  it("keeps the view payload when a later delta carries none", () => {
    // Only the baseline and a document change carry the view; a viewport delta must not blank
    // out the title and legend the diagram already showed.
    const model = applyDelta(
      emptyModel,
      add(element("c4:view", VIEW_TYPE, toBinary(C4ViewPayloadSchema, create(C4ViewPayloadSchema, { title: "A title" })))),
    );

    const after = applyDelta(model, add(node("a", "Alpha")));

    expect(after.view?.title).toBe("A title");
  });

  it("ignores element types it does not know", () => {
    const model = applyDelta(emptyModel, add(element("x", "some/other+thing", new Uint8Array([1, 2, 3]))));

    expect(model.nodes.size).toBe(0);
    expect(model.relationships.size).toBe(0);
  });

  it("never mutates the model it was given", () => {
    const before = applyDelta(emptyModel, add(node("a", "Alpha")));

    applyDelta(before, add(node("b", "Beta")));

    expect(before.nodes.size).toBe(1);
  });

  it("measures boxes from the backend's sizes, for fit-to-view", () => {
    const model = applyDelta(emptyModel, add(node("a", "Alpha", 100, 50, 160, 80)));

    const [box] = boxesOf(model);

    // Positions are centres on the wire; the box is what the canvas actually draws.
    expect(box).toEqual({ x: 20, y: 10, width: 160, height: 80 });
  });

  it("includes boundaries in the measured boxes, so fit-to-view does not clip them", () => {
    const model = applyDelta(
      emptyModel,
      add(element("boundary:s", BOUNDARY_TYPE, toBinary(C4BoundaryPayloadSchema, create(C4BoundaryPayloadSchema, {
        name: "System",
        kind: "Software System",
        width: 400,
        height: 300,
      })))),
    );

    expect(boxesOf(model)).toHaveLength(1);
  });
});
