import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createEvent, fireEvent, render } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel, DiagramModelElement } from "./api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider, TOOLBOX_DRAG_TYPE } from "@client/shell/panels/DiagramToolboxContext";
import { pointer } from "./testing/canvasHarness";

/**
 * A toolbox drop of a type declaring `editOnDrop`: the element the model brings back at the drop
 * point is selected and its editor opened through the type's `activate` gesture - and nothing else
 * is, however near in time or place.
 *
 * The surface is given a real size equal to the declared extent - jsdom measures nothing - so a
 * client pixel is a canvas unit and a drop lands where the test says.
 */

const EXTENT = { x: 0, y: 0, width: 800, height: 400 };

const definition: DiagramDefinition = {
  elementTypes: [
    { id: "remark", shape: "box", anchors: { kind: "edge", enabled: false }, sizing: "model", editOnDrop: true },
    { id: "plain", shape: "box", anchors: { kind: "edge" }, sizing: "model" },
  ],
  relationTypes: [],
  actions: [{ id: "rename", invokedBy: [{ kind: "gesture", gesture: "activate" }], appliesTo: [{ kind: "element" }] }],
  layout: { modes: ["manual"] },
  dragging: "enabled",
  extent: EXTENT,
};

const existing: DiagramModelElement = { id: "old", type: "plain", x: 600, y: 60, width: 100, height: 40 };

/** A 160 by 64 element centred on (cx, cy). */
const made = (id: string, type: string, cx: number, cy: number): DiagramModelElement => ({ id, type, x: cx, y: cy, width: 160, height: 64 });

describe("edit on drop", () => {
  let restore: (() => void) | undefined;

  beforeEach(() => {
    const original = SVGSVGElement.prototype.getBoundingClientRect;
    SVGSVGElement.prototype.getBoundingClientRect = () =>
      ({ left: 0, top: 0, x: 0, y: 0, width: EXTENT.width, height: EXTENT.height, right: EXTENT.width, bottom: EXTENT.height, toJSON: () => ({}) }) as DOMRect;
    restore = () => { SVGSVGElement.prototype.getBoundingClientRect = original; };
  });

  afterEach(() => {
    restore?.();
    vi.restoreAllMocks();
  });

  function renderCanvas() {
    const onActionInvoked = vi.fn();
    const onSelectionChanged = vi.fn();
    const view = (model: DiagramModel) => (
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvasCore definition={definition} model={model} events={{ onActionInvoked, onSelectionChanged }} />
        </DiagramToolboxProvider>
      </DiagramViewProvider>
    );
    const result = render(view({ elements: [existing], connections: [] }));
    const surface = result.container.querySelector("svg.library-canvas-surface")!;
    return {
      ...result,
      surface,
      onActionInvoked,
      onSelectionChanged,
      answer: (...elements: DiagramModelElement[]) => result.rerender(view({ elements: [existing, ...elements], connections: [] })),
    };
  }

  /** A toolbox drop of `type` at canvas (x, y). jsdom drops a drag event's coordinates from its init, so they are set on the event. */
  const dropAt = (surface: Element, type: string, x: number, y: number) => {
    const event = createEvent.drop(surface, { dataTransfer: { types: [TOOLBOX_DRAG_TYPE], getData: () => type, dropEffect: "" } });
    Object.defineProperty(event, "clientX", { value: x });
    Object.defineProperty(event, "clientY", { value: y });
    fireEvent(surface, event);
  };

  const renamed = (onActionInvoked: ReturnType<typeof vi.fn>) =>
    onActionInvoked.mock.calls.map((call) => call[0]).filter((event) => event.actionId === "rename").map((event) => event.targetId);

  it("selects the new element at the drop point and opens its editor", () => {
    // Arrange.
    const { surface, onActionInvoked, onSelectionChanged, answer } = renderCanvas();

    // Act: dropped at (100, 100); the model brings a remark covering it.
    dropAt(surface, "remark", 100, 100);
    answer(made("new", "remark", 150, 120));

    // Assert.
    expect(renamed(onActionInvoked), "no editor opened on the dropped element").toEqual(["new"]);
    expect(onSelectionChanged).toHaveBeenLastCalledWith({ kind: "selection-changed", selection: [{ kind: "element", id: "new" }] });
  });

  it("opens nothing on a new element somewhere else", () => {
    const { surface, onActionInvoked, answer } = renderCanvas();

    dropAt(surface, "remark", 100, 100);
    answer(made("new", "remark", 500, 300));

    expect(renamed(onActionInvoked)).toEqual([]);
  });

  it("opens nothing on a new element of a type not declaring it", () => {
    const { surface, onActionInvoked, answer } = renderCanvas();

    dropAt(surface, "plain", 100, 100);
    answer(made("new", "plain", 150, 120));

    expect(renamed(onActionInvoked)).toEqual([]);
  });

  it("opens nothing when the model arrives more than five seconds after the drop", () => {
    // Arrange: the clock stands still for the drop, then moves on.
    const { surface, onActionInvoked, answer } = renderCanvas();
    const now = vi.spyOn(Date, "now").mockReturnValue(1_000_000);
    dropAt(surface, "remark", 100, 100);

    // Act.
    now.mockReturnValue(1_000_000 + 5_001);
    answer(made("new", "remark", 150, 120));

    // Assert.
    expect(renamed(onActionInvoked)).toEqual([]);
  });

  it("opens nothing when another gesture came between the drop and the model", () => {
    const { container, surface, onActionInvoked, answer } = renderCanvas();

    dropAt(surface, "remark", 100, 100);
    fireEvent(container.querySelector('[data-element-id="old"]')!, pointer("pointerdown", { button: 0, clientX: 600, clientY: 60 }));
    answer(made("new", "remark", 150, 120));

    expect(renamed(onActionInvoked)).toEqual([]);
  });

  it("opens the editor once, not again on the next model", () => {
    const { surface, onActionInvoked, answer } = renderCanvas();

    dropAt(surface, "remark", 100, 100);
    answer(made("new", "remark", 150, 120));
    answer(made("new", "remark", 150, 120), made("newer", "remark", 150, 120));

    expect(renamed(onActionInvoked)).toEqual(["new"]);
  });
});
