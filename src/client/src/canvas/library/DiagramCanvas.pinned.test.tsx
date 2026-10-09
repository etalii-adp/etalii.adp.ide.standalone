import { describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import type { LibraryEventHandlers } from "./api/diagramEvents";
import { validateDiagramDefinition } from "./definition/validateDiagramDefinition";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { pointer } from "@client/canvas/library/testing/canvasHarness";

/**
 * Locked elements under an automatic layout (agent-activity-diagram Requirements 6.1 and 6.6).
 *
 * The layout places what the reader has not placed, and leaves alone what they have. Before this,
 * a layout's position replaced every element's, so a diagram was either all the reader's or all
 * the layout's.
 */

const definition: DiagramDefinition = {
  elementTypes: [
    { id: "project", shape: "box", anchors: { kind: "edge" }, sizing: "model" },
    { id: "specification", shape: "box", anchors: { kind: "edge" }, sizing: "model" },
  ],
  relationTypes: [
    {
      id: "has",
      route: "straight",
      endpoints: { source: { elementTypes: ["project"] }, target: { elementTypes: ["specification"] }, allowSelf: false },
    },
  ],
  layout: { modes: ["tiered-force"], tiers: [["project"], ["specification"]], pinned: "payload.pinned" },
  dragging: "enabled",
};

function modelOf(pinned: boolean): DiagramModel {
  return {
    elements: [
      { id: "p", type: "project", x: 0, y: 0, width: 160, height: 48 },
      { id: "locked", type: "specification", x: 700, y: -450, width: 200, height: 80, payload: { pinned } },
      { id: "free", type: "specification", x: 0, y: 0, width: 200, height: 80, payload: { pinned: false } },
    ],
    connections: [
      { id: "c1", type: "has", sourceId: "p", targetId: "locked" },
      { id: "c2", type: "has", sourceId: "p", targetId: "free" },
    ],
  };
}

function canvasOf(model: DiagramModel, events: LibraryEventHandlers = {}) {
  return render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={definition} model={model} events={events} selection={[]} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
}

/** The centre of an element's drawn box. */
function centreOf(container: HTMLElement, id: string): { x: number; y: number } {
  const rect = container.querySelector(`[data-element-id="${id}"] rect.library-shape`)!;
  const number = (name: string) => Number(rect.getAttribute(name));
  // A box is drawn at the origin of a group translated to its top-left corner.
  const [left, top] = (rect.parentElement?.getAttribute("transform") ?? "translate(0 0)").replace(/[^0-9. -]/g, "").trim().split(/\s+/).map(Number);
  return { x: left + number("x") + number("width") / 2, y: top + number("y") + number("height") / 2 };
}

describe("a locked element under an automatic layout", () => {
  it("is drawn where the model has it, while the layout places the others", () => {
    // The planted defect this was seen to fail against: the pinned path not read, so the layout
    // places every element, as it did before.
    const { container } = canvasOf(modelOf(true));

    expect(centreOf(container, "locked")).toEqual({ x: 700, y: -450 });
    // The free one was at 0,0 with the project; the layout moved it out onto its ring.
    const free = centreOf(container, "free");
    expect(Math.hypot(free.x, free.y)).toBeGreaterThan(100);
  });

  it("is placed by the layout once it is no longer locked", () => {
    const { container } = canvasOf(modelOf(false));

    expect(centreOf(container, "locked")).not.toEqual({ x: 700, y: -450 });
  });

  it("raises one move, for the dragged element alone, when an unlocked element is dragged", () => {
    const onElementMoved = vi.fn();
    const { container } = canvasOf(modelOf(true), { onElementMoved });
    const free = container.querySelector('[data-element-id="free"]')!;
    const from = centreOf(container, "free");

    fireEvent(free, pointer("pointerdown", { button: 0, clientX: from.x, clientY: from.y }));
    fireEvent(free, pointer("pointermove", { clientX: from.x + 40, clientY: from.y + 30 }));
    fireEvent(free, pointer("pointerup", { clientX: from.x + 40, clientY: from.y + 30 }));

    expect(onElementMoved).toHaveBeenCalledTimes(1);
    expect(onElementMoved.mock.calls[0][0]).toMatchObject({ kind: "element-moved", elementId: "free" });
  });

  it("keeps a settled picture exactly when the model changes in a way that places nothing differently", () => {
    const first = canvasOf(modelOf(true));
    const before = centreOf(first.container, "free");
    const relabelled = modelOf(true);
    const changed: DiagramModel = { ...relabelled, elements: relabelled.elements.map((element) => ({ ...element, label: "renamed" })) };

    first.rerender(
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvasCore definition={definition} model={changed} events={{}} selection={[]} />
        </DiagramToolboxProvider>
      </DiagramViewProvider>,
    );

    expect(centreOf(first.container, "free")).toEqual(before);
  });
});

describe("a definition that allows the radiating layout", () => {
  it("must say which element types are on which ring", () => {
    const { tiers: _tiers, ...withoutTiers } = definition.layout;
    void _tiers;

    expect(validateDiagramDefinition({ ...definition, layout: withoutTiers })).toContain(
      "The layout allows tiered-force without declaring tiers: the mode has no rings to place on.",
    );
    expect(validateDiagramDefinition(definition)).toEqual([]);
  });

  it("must name only element types it declares", () => {
    expect(validateDiagramDefinition({ ...definition, layout: { ...definition.layout, tiers: [["project"], ["agent"]] } })).toContain(
      'The tiered-force layout puts element type "agent" on a ring, which this definition does not declare.',
    );
  });
});
