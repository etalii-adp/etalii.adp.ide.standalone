import { describe, expect, it } from "vitest";
import { create, toBinary } from "@bufbuild/protobuf";
import { DeltaSchema, type Delta } from "@client/generated/deltas_pb";
import {
  AnsibleEdgeKind,
  AnsibleElementKind,
  AnsibleElementPayloadSchema,
} from "@client/generated/ansible-structure_pb";
import { anchorsOf, applyDelta, edgesOf, emptyModel, nodesOf, paletteSlotOf } from "./ansibleModel";

function node(id: string, type: string, kind: AnsibleElementKind, playIndex = -1) {
  const payload = create(AnsibleElementPayloadSchema, { name: id, kind, playIndex, width: 120, height: 32 });
  return {
    id: { value: id },
    position: { x: 10, y: 20 },
    type,
    payload: { typeUrl: "type.googleapis.com/etalii.adp.ansible.AnsibleElementPayload", value: toBinary(AnsibleElementPayloadSchema, payload) },
  };
}

function edge(id: string, sourceId: string, targetId: string) {
  const payload = create(AnsibleElementPayloadSchema, {
    name: targetId,
    kind: AnsibleElementKind.EDGE,
    edge: { sourceId, targetId, kind: AnsibleEdgeKind.USES_ROLE, directive: "roles:", targetAsWritten: targetId },
  });
  return {
    id: { value: id },
    position: { x: 0, y: 0 },
    type: "ansible/structure+edge",
    payload: { typeUrl: "type.googleapis.com/etalii.adp.ansible.AnsibleElementPayload", value: toBinary(AnsibleElementPayloadSchema, payload) },
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
  it("adds an element of every kind this type puts on the wire", () => {
    // Arrange.
    const model = applyDelta(
      emptyModel,
      addDelta(
        node("playbook:site.yml", "ansible/structure+playbook", AnsibleElementKind.PLAYBOOK),
        node("role:nginx", "ansible/structure+role", AnsibleElementKind.ROLE),
        node("inventory:inventories/production", "ansible/structure+inventory", AnsibleElementKind.INVENTORY),
        node("vars:inventories/production/group_vars", "ansible/structure+vars", AnsibleElementKind.VARIABLE_FOLDER),
      ),
    );

    // Assert.
    expect(model.elements.size).toBe(4);
    expect(model.elements.get("role:nginx")?.payload.kind).toBe(AnsibleElementKind.ROLE);
    expect(model.elements.get("role:nginx")?.x).toBe(10);
  });

  it("upserts on a repeated id rather than duplicating", () => {
    // Arrange and act.
    const first = applyDelta(emptyModel, addDelta(node("role:nginx", "ansible/structure+role", AnsibleElementKind.ROLE)));
    const second = applyDelta(first, addDelta(node("role:nginx", "ansible/structure+role", AnsibleElementKind.ROLE)));

    // Assert.
    expect(second.elements.size).toBe(1);
  });

  it("removes by id", () => {
    // Arrange.
    const model = applyDelta(emptyModel, addDelta(node("role:nginx", "ansible/structure+role", AnsibleElementKind.ROLE)));

    // Act.
    const after = applyDelta(model, removeDelta("role:nginx"));

    // Assert.
    expect(after.elements.size).toBe(0);
  });

  it("never mutates its input", () => {
    // Arrange.
    const before = applyDelta(emptyModel, addDelta(node("role:nginx", "ansible/structure+role", AnsibleElementKind.ROLE)));

    // Act.
    applyDelta(before, removeDelta("role:nginx"));

    // Assert.
    expect(before.elements.size).toBe(1);
  });

  it("ignores an element of a type this diagram does not draw", () => {
    // Act.
    // The canvas draws what it understands and never guesses at what it does not.
    const model = applyDelta(emptyModel, addDelta(node("node-1", "freeplane/mindmap+node", AnsibleElementKind.ROLE)));

    // Assert.
    expect(model.elements.size).toBe(0);
  });

  it("ignores a group delta rather than inventing fold state", () => {
    // Arrange.
    const model = applyDelta(emptyModel, addDelta(node("role:nginx", "ansible/structure+role", AnsibleElementKind.ROLE)));

    // Act.
    // This type never emits one; if one arrives, something upstream is confused and doing
    // nothing beats inventing state a read-only diagram has no use for.
    const after = applyDelta(model, create(DeltaSchema, {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      action: { case: "group", value: { elementIds: [{ value: "role:nginx" }] } },
    } as any));

    // Assert.
    expect(after.elements.size).toBe(1);
  });
});

describe("nodesOf and edgesOf", () => {
  it("separate nodes from edges, each in a stable order", () => {
    // Arrange.
    const model = applyDelta(
      emptyModel,
      addDelta(
        node("role:nginx", "ansible/structure+role", AnsibleElementKind.ROLE),
        node("playbook:site.yml", "ansible/structure+playbook", AnsibleElementKind.PLAYBOOK),
        edge("edge:a", "playbook:site.yml", "role:nginx"),
      ),
    );

    // Assert.
    expect(nodesOf(model).map((element) => element.id)).toEqual(["playbook:site.yml", "role:nginx"]);
    expect(edgesOf(model).map((element) => element.id)).toEqual(["edge:a"]);
  });
});

describe("anchorsOf", () => {
  it("finds both ends when both are in view", () => {
    // Arrange.
    const model = applyDelta(
      emptyModel,
      addDelta(
        node("playbook:site.yml", "ansible/structure+playbook", AnsibleElementKind.PLAYBOOK),
        node("role:nginx", "ansible/structure+role", AnsibleElementKind.ROLE),
        edge("edge:a", "playbook:site.yml", "role:nginx"),
      ),
    );

    // Act.
    const anchors = anchorsOf(model, edgesOf(model)[0]!);

    // Assert.
    expect(anchors?.from.id).toBe("playbook:site.yml");
    expect(anchors?.to.id).toBe("role:nginx");
  });

  it("returns null when an end is not in view, rather than drawing to nowhere", () => {
    // Arrange.
    const model = applyDelta(
      emptyModel,
      addDelta(
        node("playbook:site.yml", "ansible/structure+playbook", AnsibleElementKind.PLAYBOOK),
        edge("edge:a", "playbook:site.yml", "role:nginx"),
      ),
    );

    // Act and assert.
    expect(anchorsOf(model, edgesOf(model)[0]!)).toBeNull();
  });
});

describe("paletteSlotOf", () => {
  it("wraps a play index into the palette", () => {
    // Arrange.
    const model = applyDelta(
      emptyModel,
      addDelta(node("play:a#0", "ansible/structure+play", AnsibleElementKind.PLAY, 7)),
    );

    // Act and assert.
    expect(paletteSlotOf(model.elements.get("play:a#0")!, 5)).toBe(2);
  });

  it("gives a node belonging to no play the neutral slot", () => {
    // Arrange.
    const model = applyDelta(
      emptyModel,
      addDelta(node("role:nginx", "ansible/structure+role", AnsibleElementKind.ROLE)),
    );

    // Act and assert.
    expect(paletteSlotOf(model.elements.get("role:nginx")!, 5)).toBe(-1);
  });
});
