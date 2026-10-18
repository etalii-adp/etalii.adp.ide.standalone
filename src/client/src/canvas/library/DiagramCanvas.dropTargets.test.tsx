import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createEvent, fireEvent, render } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider, TOOLBOX_DRAG_TYPE } from "@client/shell/panels/DiagramToolboxContext";
import { CanvasRefusalContext, type CanvasRefusalReporter } from "./surface/canvasRefusals";

/**
 * A toolbox drop declared to land on elements (`toolbox.drops`, DISL 7.2 `drop.targets`): released
 * on an element a target names - the topmost such element under the point - it raises
 * `element-dropped` naming that element and the target's action; released anywhere else it is
 * refused with the declaration's sentence, and nothing is raised.
 *
 * The surface is given a real size equal to the declared extent - jsdom measures nothing - so a
 * client pixel is a canvas unit and a drop lands where the test says.
 */

const EXTENT = { x: 0, y: 0, width: 800, height: 400 };

const definition: DiagramDefinition = {
  elementTypes: [
    { id: "lane", shape: "box", anchors: { kind: "edge" }, sizing: "model" },
    { id: "card", shape: "box", anchors: { kind: "edge" }, sizing: "model" },
    { id: "note", shape: "box", anchors: { kind: "edge" }, sizing: "model" },
  ],
  relationTypes: [],
  layout: { modes: ["manual"] },
  dragging: "disabled",
  extent: EXTENT,
  toolbox: {
    drops: [
      {
        payload: "add-card",
        targets: [
          { elementTypes: ["card"], actionId: "add-card-after" },
          { elementTypes: ["lane"], actionId: "add-card-into" },
        ],
        refusal: "Drop a card onto a lane or a card.",
      },
      { payload: "quiet", targets: [{ elementTypes: ["lane"], actionId: "quiet-into" }] },
    ],
  },
};

// A lane holding a card, and a note beside them; positions are centres.
const model: DiagramModel = {
  elements: [
    { id: "lane", type: "lane", x: 200, y: 200, width: 300, height: 300 },
    { id: "card", type: "card", x: 200, y: 200, width: 100, height: 40 },
    { id: "note", type: "note", x: 600, y: 200, width: 100, height: 40 },
  ],
  connections: [],
};

describe("a toolbox drop declared to land on elements", () => {
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

  function drop(payload: string, x: number, y: number) {
    const onElementDropped = vi.fn();
    const reporter: CanvasRefusalReporter = { attempted: vi.fn(), refused: vi.fn() };
    const { container } = render(
      <CanvasRefusalContext.Provider value={reporter}>
        <DiagramViewProvider>
          <DiagramToolboxProvider>
            <DiagramCanvasCore definition={definition} model={model} events={{ onElementDropped }} toolboxItems={[]} />
          </DiagramToolboxProvider>
        </DiagramViewProvider>
      </CanvasRefusalContext.Provider>,
    );
    const surface = container.querySelector("svg.library-canvas-surface")!;
    const event = createEvent.drop(surface, { dataTransfer: { types: [TOOLBOX_DRAG_TYPE], getData: () => payload, dropEffect: "" } });
    Object.defineProperty(event, "clientX", { value: x });
    Object.defineProperty(event, "clientY", { value: y });
    fireEvent(surface, event);
    return { onElementDropped, refused: reporter.refused };
  }

  it("names the topmost element a target accepts, and that target's action", () => {
    const onCard = drop("add-card", 200, 200);
    expect(onCard.onElementDropped).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ elementType: "add-card", onto: { elementId: "card", actionId: "add-card-after" } }));
    expect(onCard.refused).not.toHaveBeenCalled();

    // Inside the lane but off the card: the lane's own target.
    const onLane = drop("add-card", 100, 100);
    expect(onLane.onElementDropped).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ onto: { elementId: "lane", actionId: "add-card-into" } }));
  });

  it("refuses with the declaration's sentence a drop on an element no target names, or on the empty canvas, and raises nothing", () => {
    for (const [x, y] of [[600, 200], [780, 380]] as const) {
      const elsewhere = drop("add-card", x, y);
      expect(elsewhere.onElementDropped).not.toHaveBeenCalled();
      expect(elsewhere.refused).toHaveBeenCalledExactlyOnceWith("Drop a card onto a lane or a card.");
    }
  });

  it("ignores, saying nothing, a drop elsewhere whose declaration states no sentence", () => {
    const quiet = drop("quiet", 600, 200);
    expect(quiet.onElementDropped).not.toHaveBeenCalled();
    expect(quiet.refused).not.toHaveBeenCalled();
  });

  it("leaves a payload it declares nothing for as it was: raised with what lies under the point", () => {
    const other = drop("something-else", 200, 200);
    expect(other.onElementDropped).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ elementType: "something-else", elementIds: ["card", "lane"] }));
    expect(other.onElementDropped.mock.calls[0]![0]).not.toHaveProperty("onto");
  });
});
