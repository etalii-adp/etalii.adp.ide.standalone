import { describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import { HIGHLIGHT_STROKE, HIGHLIGHT_STROKE_WIDTH } from "./highlight";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import type { DiagramSelection, LibraryEventHandlers } from "./api/diagramEvents";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";

SVGElement.prototype.setPointerCapture ??= () => {};
SVGElement.prototype.releasePointerCapture ??= () => {};

/**
 * The one look, and what carries it (centralized-selection tasks 5 and 28).
 *
 * <b>Selected and "would accept a connection" are the SAME look</b> since the user's ruling of
 * 2026-09-22: colour plus a heavier line, on the element's outline and its anchors, with the fill
 * untouched (Requirements 5.3, 5.4, 6.1, 6.2). There is no ring; an outline drawn outside the shape
 * was the previous answer, and an element that is both selected and a drop target shows that one
 * look once - the accepted cost being that "would accept" is not separately visible on it.
 *
 * <b>These read the INLINE paint</b>, because that is where the look now lives: a stylesheet rule
 * lost to any module rule styling its own shapes, and jsdom applies no module stylesheet anyway.
 * What the browser pass still owns is whether the colour READS on a diagram's own fills
 * (Requirement 10.3).
 */

const definition: DiagramDefinition = {
  elementTypes: [{ id: "service", shape: "box", anchors: { kind: "compass", positions: ["e", "w"] }, sizing: "model" }],
  relationTypes: [
    { id: "calls", route: "straight", endpoints: { source: { elementTypes: ["service"] }, target: { elementTypes: ["service"] }, allowSelf: false } },
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
};

// Alpha's box spans x -50..50, y -20..20; Beta's 250..350.
const model: DiagramModel = {
  elements: [
    { id: "a", type: "service", x: 0, y: 0, width: 100, height: 40, label: "Alpha" },
    { id: "b", type: "service", x: 300, y: 0, width: 100, height: 40, label: "Beta" },
  ],
  connections: [],
};

/** The canvas core with a controlled selection - the looks are the core's, whoever resolves the selection. */
function mount(selection: DiagramSelection, events: LibraryEventHandlers = {}) {
  return render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={definition} model={model} events={events} selection={selection} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
}

function pointer(type: string, init: MouseEventInit) {
  return new MouseEvent(type, { bubbles: true, cancelable: true, ...init });
}

const elementOn = (container: HTMLElement, id: string) => container.querySelector(`[data-element-id="${id}"]`)!;
const anchorOn = (container: HTMLElement, id: string, name: string) => container.querySelector(`[data-element-id="${id}"] [data-anchor="${name}"]`)!;

/**
 * What the element's shape is painted with. The paint is inline, but WHICH node carries it depends
 * on the built-in: a `box` spreads it onto the group it draws (SVG stroke inherits to the rect
 * inside), while an `ellipse` or a polygon carries it on the shape itself. So this reads the first
 * node in the element that states a stroke inline, which is the paint either way.
 */
function shapePaint(container: HTMLElement, id: string) {
  const element = elementOn(container, id);
  // The DRAWN shape, never the group above it: a paint on the group is inherited, and an SVG child
  // that states its own stroke - every node class does - ignores it. Reading the group would call
  // such an element highlighted while nothing about it had changed on screen (task 28).
  const painted = [...element.querySelectorAll<SVGElement>("rect, ellipse, path, polygon")].find(
    (node) => !node.hasAttribute("data-anchor") && !(node.getAttribute("class") ?? "").includes("library-resize-handle"),
  );
  return { stroke: painted?.style.stroke ?? "", strokeWidth: painted?.style.strokeWidth ?? "", fill: painted?.style.fill ?? "" };
}

/** What an anchor of that element is painted with. */
function anchorPaint(container: HTMLElement, id: string, name: string) {
  const anchor = anchorOn(container, id, name) as SVGElement;
  return { stroke: anchor.style.stroke, strokeWidth: anchor.style.strokeWidth };
}

/** Whether this element wears the highlight at all. */
const isHighlighted = (container: HTMLElement, id: string) => shapePaint(container, id).stroke === HIGHLIGHT_STROKE;

/** Starts a connect from Alpha's east anchor and holds it over Beta - accept, mid-gesture. */
function holdConnectOverBeta(container: HTMLElement) {
  const anchor = anchorOn(container, "a", "e");
  fireEvent(anchor, pointer("pointerdown", { button: 0, clientX: 50, clientY: 0 }));
  fireEvent(anchor, pointer("pointermove", { clientX: 300, clientY: 0 }));
}

describe("the one look", () => {
  it("a selected element is painted with the highlight, outline and anchors alike, and its fill is untouched", () => {
    const { container } = mount([{ kind: "element", id: "a" }]);

    const paint = shapePaint(container, "a");
    expect(paint.stroke).toBe(HIGHLIGHT_STROKE);
    expect(Number(paint.strokeWidth)).toBeGreaterThanOrEqual(HIGHLIGHT_STROKE_WIDTH);
    // The fill is the notation's: what a thing IS still reads while the highlight says what is selected.
    expect(paint.fill).toBe(shapePaint(container, "b").fill);
    expect(anchorPaint(container, "a", "e").stroke).toBe(HIGHLIGHT_STROKE);
    expect(isHighlighted(container, "b")).toBe(false);
  });

  it("a valid drop target is painted with the SAME look, by the user's ruling that accept wins in the selection colour", () => {
    const { container } = mount([]);

    holdConnectOverBeta(container);

    expect(elementOn(container, "b").classList.contains("library-connect-target"), "the drag never reached Beta").toBe(true);
    expect(shapePaint(container, "b").stroke).toBe(HIGHLIGHT_STROKE);
    expect(anchorPaint(container, "b", "w").stroke).toBe(HIGHLIGHT_STROKE);
  });
});

describe("one look, shown once", () => {
  it("an element both selected and a valid drop target shows that one look, and says so in both classes", () => {
    // The reversal of the 2026-09-11 amendment, recorded: there is no second look to combine, so
    // "would accept" is not separately visible on an element that is already selected. The two
    // CLASSES still distinguish the states for tests and modules; the paint does not.
    const { container } = mount([{ kind: "element", id: "b" }]);

    holdConnectOverBeta(container);

    const group = elementOn(container, "b");
    expect(group.classList.contains("canvas-selected")).toBe(true);
    expect(group.classList.contains("library-connect-target")).toBe(true);
    expect(shapePaint(container, "b").stroke).toBe(HIGHLIGHT_STROKE);
  });

  it("dragging a selected element keeps its highlight and adds the dragging look", () => {
    const { container } = mount([{ kind: "element", id: "a" }]);
    const target = elementOn(container, "a");

    fireEvent(target, pointer("pointerdown", { button: 0, clientX: 0, clientY: 0 }));
    fireEvent(target, pointer("pointermove", { clientX: 60, clientY: 40 }));

    expect(target.classList.contains("library-element-dragging"), "the drag never started").toBe(true);
    expect(isHighlighted(container, "a")).toBe(true);
  });

  it("dropped, held and selected keeps the highlight, with no dragging look", () => {
    // The drop is held at the landing until the model answers (c077af58), and this model never
    // does - exactly the window a backend round trip opens.
    const onElementMoved = vi.fn();
    const { container } = mount([{ kind: "element", id: "a" }], { onElementMoved });
    const target = elementOn(container, "a");

    fireEvent(target, pointer("pointerdown", { button: 0, clientX: 0, clientY: 0 }));
    fireEvent(target, pointer("pointermove", { clientX: 60, clientY: 40 }));
    fireEvent(target, pointer("pointerup", { clientX: 60, clientY: 40 }));

    expect(onElementMoved, "the drop never happened").toHaveBeenCalledOnce();
    expect(target.classList.contains("library-element-dragging")).toBe(false);
    expect(target.classList.contains("canvas-selected")).toBe(true);
    expect(isHighlighted(container, "a")).toBe(true);
  });

  it("a drag leaves the selection where it was", () => {
    const onSelectionChanged = vi.fn();
    const { container } = mount([{ kind: "element", id: "b" }], { onSelectionChanged });
    const target = elementOn(container, "a");

    fireEvent(target, pointer("pointerdown", { button: 0, clientX: 0, clientY: 0 }));
    fireEvent(target, pointer("pointermove", { clientX: 60, clientY: 40 }));
    fireEvent(target, pointer("pointerup", { clientX: 60, clientY: 40 }));

    expect(onSelectionChanged).not.toHaveBeenCalled();
    expect(isHighlighted(container, "b")).toBe(true);
    expect(isHighlighted(container, "a")).toBe(false);
  });
});
