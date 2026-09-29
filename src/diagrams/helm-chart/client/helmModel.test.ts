import { describe, expect, it } from "vitest";
import { create, toBinary } from "@bufbuild/protobuf";
import { DeltaSchema, type Delta } from "@client/generated/deltas_pb";
import {
  HelmEdgeKind,
  HelmElementKind,
  HelmElementPayloadSchema,
} from "@client/generated/helm-charts_pb";
import { anchorsOf, applyDelta, edgesOf, emptyModel, nodesOf } from "./helmModel";

const TYPE_URL = "type.googleapis.com/etalii.adp.helm.HelmElementPayload";

function node(id: string, type: string, kind: HelmElementKind) {
  const payload = create(HelmElementPayloadSchema, { name: id, kind, width: 200, height: 60 });
  return {
    id: { value: id },
    position: { x: 10, y: 20 },
    type,
    payload: { typeUrl: TYPE_URL, value: toBinary(HelmElementPayloadSchema, payload) },
  };
}

function edge(id: string, sourceId: string, targetId: string, openEnd = false) {
  const payload = create(HelmElementPayloadSchema, {
    name: targetId,
    kind: HelmElementKind.EDGE,
    edge: { sourceId, targetId, kind: HelmEdgeKind.DECLARES, label: "1.0.0", openEnd },
  });
  return {
    id: { value: id },
    position: { x: 0, y: 0 },
    type: "helm/chart+edge",
    payload: { typeUrl: TYPE_URL, value: toBinary(HelmElementPayloadSchema, payload) },
  };
}

function addDelta(...elements: ReturnType<typeof node>[]): Delta {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  return create(DeltaSchema, { action: { case: "add", value: { elements } } } as any);
}

function removeDelta(...ids: string[]): Delta {
  return create(DeltaSchema, {
    action: { case: "remove", value: { elementIds: ids.map((value) => ({ value })) } },
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
  } as any);
}

describe("applyDelta", () => {
  it("adds elements of this type's kinds and splits nodes from edges", () => {
    // Arrange & act.
    const model = applyDelta(
      emptyModel,
      addDelta(
        node("chart", "helm/chart+chart", HelmElementKind.CHART),
        node("values:values.yaml", "helm/chart+values", HelmElementKind.VALUES),
        node("dep:redis", "helm/chart+dependency", HelmElementKind.DEPENDENCY),
        edge("edge:chart|Declares|dep:redis|1.0.0", "chart", "dep:redis"),
      ),
    );

    // Assert.
    expect(model.elements.size).toBe(4);
    expect(nodesOf(model).map((element) => element.id)).toEqual(["chart", "dep:redis", "values:values.yaml"]);
    expect(edgesOf(model)).toHaveLength(1);
  });

  it("ignores element types it does not understand", () => {
    // Arrange & act.
    const model = applyDelta(
      emptyModel,
      addDelta(node("stranger", "mindmap/node", HelmElementKind.CHART)),
    );

    // Assert.
    expect(model.elements.size).toBe(0);
  });

  it("upserts on add and drops on remove, never mutating its input", () => {
    // Arrange.
    const first = applyDelta(emptyModel, addDelta(node("chart", "helm/chart+chart", HelmElementKind.CHART)));

    // Act.
    const second = applyDelta(first, removeDelta("chart"));

    // Assert.
    expect(first.elements.size).toBe(1);
    expect(second.elements.size).toBe(0);
  });

  it("ignores group and ungroup, which this type never emits", () => {
    // Arrange.
    const model = applyDelta(emptyModel, addDelta(node("chart", "helm/chart+chart", HelmElementKind.CHART)));
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const group = create(DeltaSchema, { action: { case: "group", value: {} } } as any);

    // Act & assert.
    expect(applyDelta(model, group)).toBe(model);
  });
});

describe("anchorsOf", () => {
  it("answers with both boxes when both ends are delivered", () => {
    // Arrange.
    const model = applyDelta(
      emptyModel,
      addDelta(
        node("chart", "helm/chart+chart", HelmElementKind.CHART),
        node("dep:redis", "helm/chart+dependency", HelmElementKind.DEPENDENCY),
        edge("e1", "chart", "dep:redis"),
      ),
    );

    // Act.
    const anchors = anchorsOf(model, edgesOf(model)[0]);

    // Assert.
    expect(anchors?.from.id).toBe("chart");
    expect(anchors?.to.id).toBe("dep:redis");
  });

  it("answers null for an open end, whose stub the canvas draws from the source", () => {
    // Arrange.
    const model = applyDelta(
      emptyModel,
      addDelta(
        node("dep:postgres", "helm/chart+dependency", HelmElementKind.DEPENDENCY),
        edge("e2", "dep:postgres", "", true),
      ),
    );

    // Act & assert.
    expect(anchorsOf(model, edgesOf(model)[0])).toBeNull();
  });
});
