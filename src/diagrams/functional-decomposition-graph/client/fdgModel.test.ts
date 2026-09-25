import { describe, expect, it } from "vitest";
import { create, toBinary } from "@bufbuild/protobuf";
import { DeltaSchema } from "@client/generated/deltas_pb";
import { ElementSchema, type Element } from "@client/generated/elements_pb";
import {
  FdgConnectionPayloadSchema,
  FdgElementPayloadSchema,
} from "@client/generated/functional-decomposition-graph_pb";
import { FDG_TYPE_PREFIX } from "./fdgIds";
import { applyDelta, emptyModel } from "./fdgModel";

function element(id: string, type: string, payload: Uint8Array, x = 0, y = 0): Element {
  return create(ElementSchema, {
    id: { value: id },
    type: `${FDG_TYPE_PREFIX}${type}`,
    position: { x, y },
    payload: { typeUrl: `type.googleapis.com/${type}`, value: payload },
  });
}

const add = (...elements: Element[]) => create(DeltaSchema, { action: { case: "add", value: { elements } } });
const remove = (...ids: string[]) =>
  create(DeltaSchema, { action: { case: "remove", value: { elementIds: ids.map((value) => ({ value })) } } });

const elementPayload = (name: string, width = 140, height = 48, text = "") =>
  toBinary(FdgElementPayloadSchema, create(FdgElementPayloadSchema, { name, text, width, height }));
const connectionPayload = (from: string, to: string, name = "") =>
  toBinary(FdgConnectionPayloadSchema, create(FdgConnectionPayloadSchema, { fromElementId: from, toElementId: to, name }));

describe("the functional decomposition graph's delta fold", () => {
  it("folds elements and connections apart by their type, before decoding either", () => {
    // Act.
    const model = applyDelta(
      emptyModel,
      add(
        element("planning", "ui-element", elementPayload("Planning"), 670, 64),
        element("note", "comment", elementPayload("", 260, 96, "Offline first."), 150, 548),
        element("c-1", "ui-child", connectionPayload("planning", "task-list", "one per task")),
      ),
    );

    // Assert.
    expect(model.elements.get("planning")).toMatchObject({ type: "ui-element", x: 670, y: 64, payload: { name: "Planning", width: 140, height: 48 } });
    expect(model.elements.get("note")).toMatchObject({ type: "comment", payload: { text: "Offline first.", height: 96 } });
    expect(model.connections.get("c-1")).toMatchObject({ type: "ui-child", payload: { fromElementId: "planning", toElementId: "task-list", name: "one per task" } });
    expect(model.elements.has("c-1")).toBe(false);
  });

  it("treats an add of a held id as an upsert, never a second copy", () => {
    // Arrange.
    const before = applyDelta(emptyModel, add(element("planning", "ui-element", elementPayload("Planning"), 670, 64)));

    // Act: renamed and widened, arriving as an add of the same id.
    const after = applyDelta(before, add(element("planning", "ui-element", elementPayload("Plan", 200), 700, 64)));

    // Assert.
    expect(after.elements.size).toBe(1);
    expect(after.elements.get("planning")).toMatchObject({ x: 700, payload: { name: "Plan", width: 200 } });
    expect(before.elements.get("planning")!.payload.name).toBe("Planning");
  });

  it("removes only on a remove, and only what it names", () => {
    // Arrange.
    const before = applyDelta(
      emptyModel,
      add(
        element("planning", "ui-element", elementPayload("Planning")),
        element("task-list", "ui-element", elementPayload("Task list")),
        element("c-1", "ui-child", connectionPayload("planning", "task-list")),
      ),
    );

    // Act.
    const after = applyDelta(before, remove("task-list", "c-1"));

    // Assert.
    expect([...after.elements.keys()]).toEqual(["planning"]);
    expect(after.connections.size).toBe(0);
    expect(before.elements.size).toBe(2);
  });

  it("passes over a type this module does not know rather than guessing at it", () => {
    // Act.
    const model = applyDelta(
      emptyModel,
      add(
        element("odd", "hexagon", elementPayload("Odd")),
        create(ElementSchema, { id: { value: "foreign" }, type: "generic/dependencies+node", payload: { value: elementPayload("x") } }),
      ),
    );

    // Assert.
    expect(model.elements.size).toBe(0);
    expect(model.connections.size).toBe(0);
  });
});
