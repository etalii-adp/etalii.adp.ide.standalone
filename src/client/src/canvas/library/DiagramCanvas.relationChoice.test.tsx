import { describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import type { DiagramDefinition, RelationTypeDefinition } from "./definition/diagramDefinition";
import type { DiagramRuntimeConfig } from "./api/diagramRuntimeConfig";
import type { DiagramModel, DiagramModelConnection } from "./api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";

/**
 * Which relation a connect gesture draws, when the press alone cannot say.
 *
 * <b>The press sees only the source.</b> A notation whose relations are told apart by their TARGET
 * - a screen owns a child screen through one relation and an action through another - could draw
 * only whichever came first from any given element, because the relation was fixed at the press and
 * the target never consulted. These drive a drag from a screen onto an action, which the FIRST
 * relation from a screen does not admit and the second does (the user's ruling of 2026-09-25).
 */

function pointer(type: string, init: MouseEventInit) {
  return new MouseEvent(type, { bubbles: true, cancelable: true, ...init });
}

// jsdom implements no pointer capture on SVG elements; the arbiter uses it.
SVGElement.prototype.setPointerCapture ??= () => {};
SVGElement.prototype.releasePointerCapture ??= () => {};

/**
 * Two screens and an action in a row, at centres 0, 300 and 600, each 100 wide - so a screen's east
 * anchor is 50 to the right of its centre and no release point is nearer the wrong element.
 */
function modelOf(connections: readonly DiagramModelConnection[] = []): DiagramModel {
  return {
    elements: [
      { id: "home", type: "screen", x: 0, y: 0, width: 100, height: 40, label: "Home" },
      { id: "detail", type: "screen", x: 300, y: 0, width: 100, height: 40, label: "Detail" },
      { id: "open", type: "act", x: 600, y: 0, width: 100, height: 40, label: "Open" },
    ],
    connections: [...connections],
  };
}

const child: RelationTypeDefinition = {
  id: "child",
  route: "straight",
  endpoints: { source: { elementTypes: ["screen"] }, target: { elementTypes: ["screen"] }, allowSelf: false },
};

/** Declared SECOND, so a press on a screen never chooses it by itself. */
function owns(overrides: Partial<RelationTypeDefinition["endpoints"]> = {}): RelationTypeDefinition {
  return {
    id: "owns",
    route: "straight",
    endpoints: { source: { elementTypes: ["screen"] }, target: { elementTypes: ["act"] }, allowSelf: false, ...overrides },
  };
}

function definitionOf(relations: readonly RelationTypeDefinition[] = [child, owns()]): DiagramDefinition {
  return {
    elementTypes: [
      { id: "screen", shape: "box", anchors: { kind: "compass", positions: ["e", "s"] }, sizing: "model" },
      { id: "act", shape: "box", anchors: { kind: "compass", positions: ["w"] }, sizing: "model" },
    ],
    relationTypes: relations,
    layout: { modes: ["manual"] },
    dragging: "enabled",
  };
}

function renderCanvas(definition: DiagramDefinition, model: DiagramModel, config?: DiagramRuntimeConfig) {
  const onConnectionDrawn = vi.fn();
  const result = render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={definition} model={model} config={config} events={{ onConnectionDrawn }} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
  return { ...result, onConnectionDrawn };
}

/** Detail's east anchor (350, 0), dragged over Open (centre 600), and held there. */
function dragDetailOverOpen(container: HTMLElement, anchor = "e") {
  const handle = container.querySelector(`[data-element-id="detail"] [data-anchor="${anchor}"]`)!;
  const from = anchor === "e" ? { x: 350, y: 0 } : { x: 300, y: 20 };
  fireEvent(handle, pointer("pointerdown", { button: 0, clientX: from.x, clientY: from.y }));
  fireEvent(handle, pointer("pointermove", { clientX: 590, clientY: 0 }));
  const open = container.querySelector('[data-element-id="open"]')!;
  return { open, release: () => fireEvent(handle, pointer("pointerup", { clientX: 590, clientY: 0 })) };
}

describe("a connect gesture draws the relation that can arrive at its target", () => {
  it("draws the second relation where the first one cannot reach the target", () => {
    // Arrange.
    const { container, onConnectionDrawn } = renderCanvas(definitionOf(), modelOf());

    // Act.
    const { open, release } = dragDetailOverOpen(container);

    // Assert: offered under the pointer, and drawn as the relation that admits a screen-to-action link.
    expect(open.classList.contains("library-connect-target")).toBe(true);
    release();
    expect(onConnectionDrawn).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({ relationType: "owns", sourceElementId: "detail", targetElementId: "open" }),
    );
  });

  it("keeps the pressed relation wherever it already admits the target", () => {
    // Arrange: Detail's east anchor dragged back onto Home, a screen - `child` admits it.
    const { container, onConnectionDrawn } = renderCanvas(definitionOf(), modelOf());
    const handle = container.querySelector('[data-element-id="detail"] [data-anchor="e"]')!;

    // Act.
    fireEvent(handle, pointer("pointerdown", { button: 0, clientX: 350, clientY: 0 }));
    fireEvent(handle, pointer("pointermove", { clientX: 10, clientY: 0 }));
    fireEvent(handle, pointer("pointerup", { clientX: 10, clientY: 0 }));

    // Assert.
    expect(onConnectionDrawn).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ relationType: "child", targetElementId: "home" }));
  });

  it("runs the verdict on the relation it chose: that relation's limit refuses", () => {
    // Arrange: Open already has an owner, and `owns` allows one.
    const definition = definitionOf([child, owns({ cardinality: { maxIntoTarget: 1 } })]);
    const { container, onConnectionDrawn } = renderCanvas(definition, modelOf([{ id: "1", type: "owns", sourceId: "home", targetId: "open" }]));

    // Act.
    const { open, release } = dragDetailOverOpen(container);

    // Assert.
    expect(open.classList.contains("library-connect-forbidden")).toBe(true);
    release();
    expect(onConnectionDrawn).not.toHaveBeenCalled();
  });

  it("respects the anchor the drag began on: a relation that leaves only from the bottom is not chosen from the side", () => {
    // Arrange.
    const definition = definitionOf([child, owns({ source: { elementTypes: ["screen"], anchors: ["s"] } })]);
    const { container, onConnectionDrawn } = renderCanvas(definition, modelOf());

    // Act.
    const { open, release } = dragDetailOverOpen(container, "e");

    // Assert.
    expect(open.classList.contains("library-connect-forbidden")).toBe(true);
    release();
    expect(onConnectionDrawn).not.toHaveBeenCalled();
  });

  it("lets a named tool win: the runtime asked for `child`, so a target it cannot reach is refused", () => {
    // Arrange.
    const { container, onConnectionDrawn } = renderCanvas(definitionOf(), modelOf(), { activeTool: "child" });

    // Act.
    const { open, release } = dragDetailOverOpen(container);

    // Assert.
    expect(open.classList.contains("library-connect-forbidden")).toBe(true);
    release();
    expect(onConnectionDrawn).not.toHaveBeenCalled();
  });

  it("chooses the same way for a right-button draw from the body", () => {
    // Arrange.
    const { container, onConnectionDrawn } = renderCanvas({ ...definitionOf(), connectOnRightDrag: true }, modelOf());
    const detail = container.querySelector('[data-element-id="detail"]')!;

    // Act.
    fireEvent(detail, pointer("pointerdown", { button: 2, clientX: 300, clientY: 0 }));
    fireEvent(detail, pointer("pointermove", { clientX: 600, clientY: 0 }));
    const offered = container.querySelector('[data-element-id="open"]')!.classList.contains("library-connect-target");
    fireEvent(detail, pointer("pointerup", { button: 2, clientX: 600, clientY: 0 }));

    // Assert.
    expect(offered).toBe(true);
    expect(onConnectionDrawn).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ relationType: "owns", targetElementId: "open" }));
  });
});
