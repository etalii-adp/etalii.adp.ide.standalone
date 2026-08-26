import { describe, expect, it } from "vitest";
import { create, toBinary } from "@bufbuild/protobuf";
import { DeltaSchema } from "@client/generated/deltas_pb";
import { ElementSchema } from "@client/generated/elements_pb";
import {
  WardleyAnnotationPayloadSchema,
  WardleyElementKind,
  WardleyElementPayloadSchema,
  WardleyEvolutionAxisPayloadSchema,
  WardleyLinkPayloadSchema,
  WardleyDecorator,
} from "@client/generated/wardley-map_pb";
import { applyDelta, emptyModel, pipelineChildrenOf } from "./wardleyModel";

/** An Add delta carrying one element of `type` with `payload` at (x, y). */
function addDelta(id: string, type: string, payload: Uint8Array, x = 0, y = 0) {
  return create(DeltaSchema, {
    action: {
      case: "add",
      value: {
        elements: [
          create(ElementSchema, {
            id: { value: id },
            type,
            position: { x, y },
            payload: { typeUrl: `type.googleapis.com/${type}`, value: payload },
          }),
        ],
      },
    },
  });
}

function elementPayload(fields: Parameters<typeof create<typeof WardleyElementPayloadSchema>>[1]) {
  return toBinary(WardleyElementPayloadSchema, create(WardleyElementPayloadSchema, fields));
}

describe("applyDelta", () => {
  it("adds a component with the document's own axis values alongside its canvas position", () => {
    // Arrange. The property grid shows and edits these, so they travel rather than being
    // reconstructed from the point.
    const delta = addDelta(
      "a",
      "wardley/map+element",
      elementPayload({
        name: "Cup of Tea",
        kind: WardleyElementKind.COMPONENT,
        visibility: 0.79,
        maturity: 0.61,
        evolutionStage: "Product (+rental)",
      }),
      0.61,
      0.21,
    );

    // Act.
    const model = applyDelta(emptyModel, delta);

    // Assert.
    const element = model.elements.get("a");
    expect(element?.name).toBe("Cup of Tea");
    expect(element?.visibility).toBe(0.79);
    expect(element?.maturity).toBe(0.61);
    expect(element?.evolutionStage).toBe("Product (+rental)");
    expect(element?.x).toBe(0.61);
  });

  it("treats a second add for one id as an upsert rather than a duplicate", () => {
    // Arrange. Requirement 10.3 - an edit arrives as an add carrying the new state, never as a
    // remove and an add, which would drop the selection on the thing being edited.
    const first = addDelta("a", "wardley/map+element", elementPayload({ name: "Alpha", maturity: 0.1 }), 0.1, 0.1);
    const second = addDelta("a", "wardley/map+element", elementPayload({ name: "Alpha", maturity: 0.8 }), 0.8, 0.1);

    // Act.
    const model = applyDelta(applyDelta(emptyModel, first), second);

    // Assert.
    expect(model.elements.size).toBe(1);
    expect(model.elements.get("a")?.maturity).toBe(0.8);
  });

  it("keeps the five decorators as a set", () => {
    // Arrange.
    const delta = addDelta(
      "a",
      "wardley/map+element",
      elementPayload({
        name: "Payment",
        decorators: [WardleyDecorator.BUY, WardleyDecorator.MARKET],
      }),
    );

    // Act.
    const model = applyDelta(emptyModel, delta);

    // Assert.
    expect(model.elements.get("a")?.decorators).toEqual([WardleyDecorator.BUY, WardleyDecorator.MARKET]);
  });

  it("carries an evolve target so the canvas can draw both positions", () => {
    // Arrange. Requirement 6.1 - the pair is the point of the statement.
    const delta = addDelta(
      "a",
      "wardley/map+element",
      elementPayload({
        name: "Datacentre",
        maturity: 0.55,
        evolve: { maturity: 0.83, evolutionStage: "Commodity (+utility)", overrideName: "Cloud Hosting" },
      }),
    );

    // Act.
    const model = applyDelta(emptyModel, delta);

    // Assert.
    expect(model.elements.get("a")?.evolve?.maturity).toBe(0.83);
    expect(model.elements.get("a")?.evolve?.overrideName).toBe("Cloud Hosting");
  });

  it("keeps a link's written names so a dangling endpoint can still be named", () => {
    // Arrange. Requirement 14.2 - the map opens and the broken end is visible.
    const payload = toBinary(
      WardleyLinkPayloadSchema,
      create(WardleyLinkPayloadSchema, {
        sourceId: "a",
        targetId: "",
        sourceName: "Alpha",
        targetName: "Nowhere",
      }),
    );

    // Act.
    const model = applyDelta(emptyModel, addDelta("l", "wardley/map+link", payload));

    // Assert.
    expect(model.links.get("l")?.targetId).toBe("");
    expect(model.links.get("l")?.targetName).toBe("Nowhere");
  });

  it("keeps every occurrence of a multi-position annotation", () => {
    // Arrange. Requirement 6.8 - a core Element has one position and none of these may be lost.
    const payload = toBinary(
      WardleyAnnotationPayloadSchema,
      create(WardleyAnnotationPayloadSchema, {
        number: 1,
        text: "Two places",
        occurrences: [
          { x: 0.49, y: 0.57 },
          { x: 0.79, y: 0.92 },
        ],
      }),
    );

    // Act.
    const model = applyDelta(emptyModel, addDelta("n", "wardley/map+annotation", payload, 0.49, 0.57));

    // Assert.
    expect(model.annotations.get("n")?.occurrences).toHaveLength(2);
    expect(model.annotations.get("n")?.occurrences[1].x).toBe(0.79);
  });

  it("takes the evolution axis from the backend rather than holding its own constants", () => {
    // Arrange. Requirement 8.2 - the boundaries are not published in the DSL, so one copy.
    const payload = toBinary(
      WardleyEvolutionAxisPayloadSchema,
      create(WardleyEvolutionAxisPayloadSchema, {
        title: "Tea shop",
        stages: [
          { label: "Genesis", start: 0, end: 0.175 },
          { label: "Custom Built", start: 0.175, end: 0.4 },
          { label: "Product (+rental)", start: 0.4, end: 0.7 },
          { label: "Commodity (+utility)", start: 0.7, end: 1 },
        ],
      }),
    );

    // Act.
    const model = applyDelta(emptyModel, addDelta("axis", "wardley/map+evolution-axis", payload));

    // Assert.
    expect(model.axis?.stages).toHaveLength(4);
    expect(model.axis?.stages[0].end).toBe(0.175);
    expect(model.axis?.title).toBe("Tea shop");
  });

  it("removes an element by id", () => {
    // Arrange.
    const added = applyDelta(emptyModel, addDelta("a", "wardley/map+element", elementPayload({ name: "Alpha" })));

    // Act.
    const model = applyDelta(
      added,
      create(DeltaSchema, { action: { case: "remove", value: { elementIds: [{ value: "a" }] } } }),
    );

    // Assert.
    expect(model.elements.size).toBe(0);
  });

  it("ignores an element type this build does not know", () => {
    // Arrange. A newer backend must not break an older canvas.
    const delta = addDelta("x", "wardley/map+something-later", new Uint8Array([1, 2, 3]));

    // Act and assert.
    expect(() => applyDelta(emptyModel, delta)).not.toThrow();
  });

  it("does not mutate the model it was given", () => {
    // Arrange. React state has to change by identity, or the canvas never re-renders.
    const before = applyDelta(emptyModel, addDelta("a", "wardley/map+element", elementPayload({ name: "Alpha" })));

    // Act.
    const after = applyDelta(before, addDelta("b", "wardley/map+element", elementPayload({ name: "Beta" })));

    // Assert.
    expect(before.elements.size).toBe(1);
    expect(after.elements.size).toBe(2);
    expect(after.elements).not.toBe(before.elements);
  });
});

describe("pipelineChildrenOf", () => {
  it("finds the children a pipeline parent holds", () => {
    // Arrange.
    let model = applyDelta(emptyModel, addDelta("p", "wardley/map+element", elementPayload({ name: "Kettle" })));
    model = applyDelta(
      model,
      addDelta("c", "wardley/map+element", elementPayload({ name: "Electric", pipelineParentId: "p" })),
    );

    // Act.
    const children = pipelineChildrenOf(model, "p");

    // Assert.
    expect(children.map((child) => child.name)).toEqual(["Electric"]);
  });
});
