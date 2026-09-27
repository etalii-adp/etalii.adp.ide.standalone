import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { create, toBinary } from "@bufbuild/protobuf";
import { DeltaSchema } from "@client/generated/deltas_pb";
import { ElementSchema, type Element } from "@client/generated/elements_pb";
import { GhgNotePayloadSchema, GhgTriggerPayloadSchema } from "@client/generated/gartner-hypecycle-graph_pb";
import { DiagramCanvasCore } from "@client/canvas/library/DiagramCanvas";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import type { DiagramSelection } from "@client/canvas/library/api/diagramEvents";
import { validateDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { pointer } from "@client/canvas/library/testing/canvasHarness";
import { GHG_DEFINITION } from "./GhgCanvas";
import { GHG_TYPE_PREFIX, GhgElementTypes, GhgRelationTypes, GhgScale } from "./ghgIds";
import { applyDelta, emptyModel } from "./ghgModel";

/**
 * ghg-triggers-and-notes task 7: triggers and notes as the hype cycle DECLARES them, read off what
 * the real library draws and raises from this module's definition - never from the module's own
 * code, which holds none of it (Requirement 9.1).
 *
 * jsdom measures nothing, so the surface is given a size and a pointer is placed by reading the view
 * the canvas chose back from its viewBox, as `ghgConnect.test.tsx` does.
 */

const RECT = { width: 1000, height: 600 };
let restore: (() => void) | undefined;

beforeEach(() => {
  const original = SVGSVGElement.prototype.getBoundingClientRect;
  SVGSVGElement.prototype.getBoundingClientRect = () =>
    ({ left: 0, top: 0, x: 0, y: 0, width: RECT.width, height: RECT.height, right: RECT.width, bottom: RECT.height, toJSON: () => ({}) }) as DOMRect;
  restore = () => { SVGSVGElement.prototype.getBoundingClientRect = original; };
});

afterEach(() => restore?.());

const middleOf = (row: number) => row * GhgScale.rowStep + GhgScale.trendHeight / 2;

const trend = (id: string, x: number, row: number, phases: number, tags: string[]): DiagramModelElement => ({
  id, type: GhgElementTypes.trend, x, y: middleOf(row), width: 400, height: GhgScale.trendHeight, label: id,
  payload: { name: id, phases, boundaries: [], tags, snapX: 0, snapY: 0 },
});

/** The transistor, on a step line and row 2's middle, as the backend sends a trigger. */
const TRIGGER: DiagramModelElement = {
  id: "transistor", type: GhgElementTypes.trigger, x: 96, y: middleOf(2), width: GhgScale.triggerSize, height: GhgScale.triggerSize, label: "Transistor",
  payload: { name: "Transistor", when: "Dec 1947", whenLong: "December 1947", tags: ["invention"], snapX: -8, snapY: 8 },
};

const NOTE: DiagramModelElement = {
  id: "remark", type: GhgElementTypes.note, x: 600, y: 300, width: 160, height: 64,
  label: "Dates here are illustrative and not a historical claim about any of these trends",
  payload: { text: "Dates here are illustrative and not a historical claim about any of these trends", snapX: 0, snapY: 0 },
};

/** Radio shows all four phases; television only its Peak, so an influence onto its Slope is hidden. */
const MODEL: DiagramModel = {
  elements: [trend("radio", 400, 1, 4, ["radio"]), trend("television", 400, 4, 1, ["tv"]), TRIGGER, NOTE],
  connections: [
    { id: "t-radio", type: GhgRelationTypes.influence, sourceId: "transistor", targetId: "radio", targetAttachment: { edge: "top", region: 0, at: 0.5 } },
    { id: "t-television", type: GhgRelationTypes.influence, sourceId: "transistor", targetId: "television", targetAttachment: { edge: "top", region: 2, at: 0.5 } },
  ] satisfies DiagramModelConnection[],
};

function renderModel(model: DiagramModel = MODEL, selection?: DiagramSelection) {
  const onConnectionDrawn = vi.fn();
  const onElementMoved = vi.fn();
  const result = render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={GHG_DEFINITION} model={model} events={{ onConnectionDrawn, onElementMoved }} {...(selection !== undefined ? { selection } : {})} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
  return { ...result, onConnectionDrawn, onElementMoved };
}

/** The client point over a canvas point, through the view the canvas is showing. */
function clientOf(container: HTMLElement, x: number, y: number) {
  const [vx, vy, vw, vh] = container.querySelector("svg")!.getAttribute("viewBox")!.split(/\s+/).map(Number);
  return { clientX: ((x - vx) / vw) * RECT.width, clientY: ((y - vy) / vh) * RECT.height };
}

describe("the hype cycle's trigger and note, declared", () => {
  it("still passes the library's validation, in every layout mode", () => {
    expect(validateDiagramDefinition(GHG_DEFINITION)).toEqual([]);
  });

  it("lets an influence start at a trend or a trigger and end only at a trend", () => {
    const [influence] = GHG_DEFINITION.relationTypes;
    expect(influence.endpoints.source.elementTypes).toEqual([GhgElementTypes.trend, GhgElementTypes.trigger]);
    expect(influence.endpoints.target.elementTypes).toEqual([GhgElementTypes.trend]);
  });

  it("filters trends and triggers by tag, and never a note", () => {
    expect(GHG_DEFINITION.filter!.elementTypes).toEqual([GhgElementTypes.trend, GhgElementTypes.trigger]);
  });
});

describe("a trigger, drawn", () => {
  it("is a circle half a trend's height across, its name and date before it, and says it is a trigger", () => {
    const { container } = renderModel();
    const element = container.querySelector('[data-element-id="transistor"]')!;

    const circle = element.querySelector("ellipse")!;
    expect([circle.getAttribute("rx"), circle.getAttribute("ry")]).toEqual(["8", "8"]);
    expect(element.textContent).toContain("Transistor · Dec 1947");
    expect(element.querySelector("title")?.textContent).toBe("Trigger: Transistor, December 1947");
  });

  it("lands its CENTRE on a step line and a row's middle when dragged, not its edge", () => {
    const { container, onElementMoved } = renderModel();
    const circle = container.querySelector('[data-element-id="transistor"] ellipse')!;
    const from = clientOf(container, TRIGGER.x, TRIGGER.y);
    const to = clientOf(container, TRIGGER.x + 5, TRIGGER.y + 30);

    fireEvent(circle, pointer("pointerdown", { button: 0, ...from }));
    fireEvent(circle, pointer("pointermove", to));
    fireEvent(circle, pointer("pointerup", to));

    expect(onElementMoved).toHaveBeenCalledTimes(1);
    const { x, y } = onElementMoved.mock.calls[0][0].position;
    expect(x % GhgScale.unitsPerMonth, `the centre ${x} is off a step line`).toBe(0);
    expect((y - GhgScale.trendHeight / 2) % GhgScale.rowStep, `the centre ${y} is off a row's middle`).toBe(0);
  });

  it("offers no resize handle when selected, while a note offers all four", () => {
    const handlesOf = (id: string) =>
      [...renderModel(MODEL, [{ kind: "element", id }]).container.querySelectorAll(`[data-element-id="${id}"] .library-resize-handle`)].map((handle) => handle.getAttribute("data-resize"));

    expect(handlesOf("transistor")).toEqual([]);
    expect(handlesOf("remark")).toEqual(["left", "right", "top", "bottom"]);
  });

  it("draws no anchor dots, while its handles still take a press", () => {
    const { container } = renderModel();
    const element = container.querySelector('[data-element-id="transistor"]')!;

    expect(element.querySelectorAll(".library-anchor"), "the trigger is ringed with anchor dots").toHaveLength(0);
    expect(element.querySelectorAll(".library-anchor-hit")).toHaveLength(3);
  });

  it("starts an influence from a handle, and the line leaves its outline, not the handle", () => {
    const { container, onConnectionDrawn } = renderModel({ elements: MODEL.elements, connections: [] });
    const handle = container.querySelector('[data-element-id="transistor"] [data-anchor="n"]')!;
    expect(handle, "the trigger offers a handle to start an influence from").not.toBeNull();
    const press = clientOf(container, TRIGGER.x, TRIGGER.y - 8);
    // Over radio's Peak, the first quarter of its top edge.
    const over = clientOf(container, 250, middleOf(1) - 14);

    fireEvent(handle, pointer("pointerdown", { button: 0, ...press }));
    fireEvent(handle, pointer("pointermove", over));
    expect(container.querySelector('[data-element-id="radio"]')!.classList.contains("library-connect-target")).toBe(true);
    fireEvent(handle, pointer("pointerup", over));

    expect(onConnectionDrawn).toHaveBeenCalledTimes(1);
    const event = onConnectionDrawn.mock.calls[0][0];
    expect(event).toMatchObject({ sourceElementId: "transistor", targetElementId: "radio", targetAttachment: { edge: "top", region: 0 } });
    expect("sourceAttachment" in event || "sourceAnchor" in event, "the trigger's end carries something the document cannot store").toBe(false);
  });

  it("is never offered as a target, from a trend or from another trigger", () => {
    const second: DiagramModelElement = { ...TRIGGER, id: "second", x: 200 };
    const { container, onConnectionDrawn } = renderModel({ elements: [...MODEL.elements, second], connections: [] });
    const handle = container.querySelector('[data-element-id="second"] [data-anchor="e"]')!;
    const over = clientOf(container, TRIGGER.x, TRIGGER.y);

    fireEvent(handle, pointer("pointerdown", { button: 0, ...clientOf(container, 208, TRIGGER.y) }));
    fireEvent(handle, pointer("pointermove", over));
    expect(container.querySelector('[data-element-id="transistor"]')!.classList.contains("library-connect-target")).toBe(false);
    fireEvent(handle, pointer("pointerup", over));

    const strip = container.querySelector('[data-element-id="radio"] .library-edge-strip[data-edge="bottom"][data-region="0"]')!;
    fireEvent(strip, pointer("pointerdown", { button: 0, ...clientOf(container, 210, middleOf(1) + 16) }));
    fireEvent(strip, pointer("pointermove", over));
    expect(container.querySelector('[data-element-id="transistor"]')!.classList.contains("library-connect-target")).toBe(false);
    fireEvent(strip, pointer("pointerup", over));

    expect(onConnectionDrawn).not.toHaveBeenCalled();
  });

  it("hides an influence from it whose target phase is hidden, and draws the one whose phase is shown", () => {
    const { container } = renderModel();

    expect(container.querySelector('[data-connection-id="t-radio"]')).not.toBeNull();
    expect(container.querySelector('[data-connection-id="t-television"]')).toBeNull();
  });
});

describe("a note, drawn", () => {
  it("wraps its text over several lines and has no anchors", () => {
    const { container } = renderModel();
    const element = container.querySelector('[data-element-id="remark"]')!;

    expect(element.querySelectorAll("text.ghg-note-text").length, "the note's text is one unwrapped line").toBeGreaterThan(1);
    expect(element.querySelector("[data-anchor]")).toBeNull();
    expect(element.querySelector(".library-edge-strip")).toBeNull();
  });
});

describe("the tag filter over triggers and notes", () => {
  it("hides a trigger without the chosen tag, keeps every note, and suggests a trigger's tags", () => {
    const { container } = renderModel();
    const field = container.querySelector(".library-filter .tag-input-field")!;

    fireEvent.change(field, { target: { value: "inv" } });
    expect([...container.querySelectorAll(".tag-input-suggestion")].map((option) => option.textContent)).toContain("invention");

    fireEvent.change(field, { target: { value: "radio" } });
    fireEvent.keyDown(field, { key: "Enter" });

    const drawn = [...container.querySelectorAll("[data-element-id]")].map((element) => element.getAttribute("data-element-id"));
    expect(drawn).toContain("radio");
    expect(drawn).toContain("remark");
    expect(drawn).not.toContain("transistor");
    expect(container.querySelector('[data-connection-id="t-radio"]')).toBeNull();
  });
});

describe("the model, folded from deltas", () => {
  const element = (id: string, type: string, payload: Uint8Array, x = 0, y = 0): Element =>
    create(ElementSchema, { id: { value: id }, type: `${GHG_TYPE_PREFIX}${type}`, position: { x, y }, payload: { typeUrl: `type.googleapis.com/${type}`, value: payload } });

  it("upserts and removes triggers and notes by id", () => {
    const trigger = toBinary(GhgTriggerPayloadSchema, create(GhgTriggerPayloadSchema, { name: "Transistor", when: "Dec 1947" }));
    const note = toBinary(GhgNotePayloadSchema, create(GhgNotePayloadSchema, { text: "Hello", width: 160, height: 64 }));
    const renamed = toBinary(GhgTriggerPayloadSchema, create(GhgTriggerPayloadSchema, { name: "Point contact", when: "Dec 1947" }));

    let model = applyDelta(emptyModel, create(DeltaSchema, { action: { case: "add", value: { elements: [element("t", GhgElementTypes.trigger, trigger, 96, 128), element("n", GhgElementTypes.note, note, 600, 300)] } } }));
    expect([model.triggers.get("t")?.payload.name, model.triggers.get("t")?.x, model.notes.get("n")?.payload.text]).toEqual(["Transistor", 96, "Hello"]);

    model = applyDelta(model, create(DeltaSchema, { action: { case: "add", value: { elements: [element("t", GhgElementTypes.trigger, renamed, 96, 128)] } } }));
    expect([model.triggers.size, model.triggers.get("t")?.payload.name]).toEqual([1, "Point contact"]);

    model = applyDelta(model, create(DeltaSchema, { action: { case: "remove", value: { elementIds: [{ value: "t" }, { value: "n" }] } } }));
    expect([model.triggers.size, model.notes.size]).toEqual([0, 0]);
  });
});
