import { describe, expect, it } from "vitest";
import { create, toBinary } from "@bufbuild/protobuf";
import { DeltaSchema } from "@client/generated/deltas_pb";
import { ElementSchema, type Element } from "@client/generated/elements_pb";
import { TimelineConnectionPayloadSchema, TimelineElementPayloadSchema } from "@client/generated/timeline_pb";
import { CONNECTION, MOMENT, PERIOD, applyDelta, emptyModel } from "./timelineModel";

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

function periodPayload(begin: string, end: string, row: number, label: string): Uint8Array {
  return toBinary(
    TimelineElementPayloadSchema,
    create(TimelineElementPayloadSchema, { begin, end, row, dateOnly: !begin.includes("T"), label }),
  );
}

describe("timelineModel", () => {
  it("folds a period, a moment and a connection out of one add", () => {
    // Arrange.
    const delta = addDelta(
      element("aaa", PERIOD, periodPayload("2026-01-05", "2026-02-13", 0, "Discovery"), 1_767_571_200, 0),
      element("bbb", MOMENT, periodPayload("2026-02-16", "", 2, "Go"), 1_771_200_000, 120),
      element(
        "ccc",
        CONNECTION,
        toBinary(
          TimelineConnectionPayloadSchema,
          create(TimelineConnectionPayloadSchema, { fromElementId: "aaa", toElementId: "bbb", label: "gates" }),
        ),
      ),
    );

    // Act.
    const model = applyDelta(emptyModel, delta);

    // Assert.
    expect(model.elements.get("aaa")).toMatchObject({ label: "Discovery", isPeriod: true, row: 0, dateOnly: true });
    expect(model.elements.get("bbb")).toMatchObject({ label: "Go", isPeriod: false, row: 2 });
    expect(model.connections.get("ccc")).toMatchObject({ fromElementId: "aaa", toElementId: "bbb", label: "gates" });
  });

  it("skips a foreign element without decoding it", () => {
    // Arrange.
    // Several modules share one delta stream, and this payload is NOT a timeline payload: bytes
    // that would throw if decoded as one. A sibling module shipped a decode-before-type-check
    // once, and one foreign element took the whole delta with it.
    const foreign = element("zzz", "wardley/map+element", new Uint8Array([255, 255, 255, 255, 255, 255]));
    const ours = element("aaa", PERIOD, periodPayload("2026-01-05", "2026-02-13", 0, "Ours"));

    // Act.
    const model = applyDelta(emptyModel, addDelta(foreign, ours));

    // Assert.
    expect(model.elements.has("zzz")).toBe(false);
    expect(model.elements.get("aaa")?.label).toBe("Ours");
  });

  it("treats an add as an upsert, so an edit never drops the element", () => {
    // Arrange.
    const first = applyDelta(emptyModel, addDelta(element("aaa", PERIOD, periodPayload("2026-01-05", "2026-02-13", 0, "Before"))));

    // Act.
    const second = applyDelta(first, addDelta(element("aaa", PERIOD, periodPayload("2026-01-05", "2026-02-13", 0, "After"))));

    // Assert.
    expect(second.elements.size).toBe(1);
    expect(second.elements.get("aaa")?.label).toBe("After");
  });

  it("removes elements and connections by id", () => {
    // Arrange.
    const populated = applyDelta(emptyModel, addDelta(
      element("aaa", PERIOD, periodPayload("2026-01-05", "2026-02-13", 0, "Doomed")),
      element("ccc", CONNECTION, toBinary(
        TimelineConnectionPayloadSchema,
        create(TimelineConnectionPayloadSchema, { fromElementId: "aaa", toElementId: "aaa", label: "" }),
      )),
    ));

    // Act.
    const model = applyDelta(populated, create(DeltaSchema, {
      action: { case: "remove", value: { elementIds: [{ value: "aaa" }, { value: "ccc" }] } },
    }));

    // Assert.
    expect(model.elements.size).toBe(0);
    expect(model.connections.size).toBe(0);
  });
});
