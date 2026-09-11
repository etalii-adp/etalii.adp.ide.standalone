import { describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvas } from "./DiagramCanvas";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import type { DiagramEventHandlers, DiagramSelection } from "./api/diagramEvents";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";

SVGElement.prototype.setPointerCapture ??= () => {};
SVGElement.prototype.releasePointerCapture ??= () => {};

/**
 * The two looks and how they combine (centralized-selection tasks 4 and 5).
 *
 * *Selected* and *would accept a connection* are each a ring the library draws just outside the
 * element, at different offsets, each read from its own flag alone. What these can see is WHICH
 * ring is drawn and WHERE. What they cannot see is how either looks - jsdom applies no CSS - so
 * legibility on a diagram's fills, and telling the two apart, are the browser pass's to confirm
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

function mount(selection: DiagramSelection, events: DiagramEventHandlers = {}) {
  return render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvas definition={definition} model={model} events={events} selection={selection} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
}

function pointer(type: string, init: MouseEventInit) {
  return new MouseEvent(type, { bubbles: true, cancelable: true, ...init });
}

const elementOn = (container: HTMLElement, id: string) => container.querySelector(`[data-element-id="${id}"]`)!;
const anchorOn = (container: HTMLElement, id: string, name: string) => container.querySelector(`[data-element-id="${id}"] [data-anchor="${name}"]`)!;

/** The ring of this kind on this element, as the box it draws, or null when it is not drawn. */
function ringOf(container: HTMLElement, id: string, kind: "selected" | "accept") {
  const ring = elementOn(container, id).querySelector(`rect.library-${kind}-outline`);
  return ring === null ? null : { x: Number(ring.getAttribute("x")), y: Number(ring.getAttribute("y")), width: Number(ring.getAttribute("width")) };
}

/** Starts a connect from Alpha's east anchor and holds it over Beta - accept, mid-gesture. */
function holdConnectOverBeta(container: HTMLElement) {
  const anchor = anchorOn(container, "a", "e");
  fireEvent(anchor, pointer("pointerdown", { button: 0, clientX: 50, clientY: 0 }));
  fireEvent(anchor, pointer("pointermove", { clientX: 300, clientY: 0 }));
}

describe("the two looks", () => {
  it("a selected element carries the selected ring just outside it, and no accept ring", () => {
    const { container } = mount([{ kind: "element", id: "a" }]);

    expect(ringOf(container, "a", "selected")).toEqual({ x: -54, y: -24, width: 108 });
    expect(ringOf(container, "a", "accept")).toBeNull();
    expect(ringOf(container, "b", "selected")).toBeNull();
  });

  it("a valid drop target carries the accept ring, further out, and no selected ring", () => {
    const { container } = mount([]);

    holdConnectOverBeta(container);

    expect(elementOn(container, "b").classList.contains("library-connect-target"), "the drag never reached Beta").toBe(true);
    expect(ringOf(container, "b", "accept")).toEqual({ x: 241, y: -29, width: 118 });
    expect(ringOf(container, "b", "selected")).toBeNull();
  });
});

describe("the looks combine without reading one another", () => {
  it("selected and a valid drop target shows both rings, one inside the other", () => {
    const { container } = mount([{ kind: "element", id: "b" }]);

    holdConnectOverBeta(container);

    const selected = ringOf(container, "b", "selected");
    const accept = ringOf(container, "b", "accept");
    expect(selected, "the selected ring gave way to the accept ring").not.toBeNull();
    expect(accept, "the accept ring gave way to the selected ring").not.toBeNull();
    expect(accept!.x).toBeLessThan(selected!.x);
    expect(accept!.width).toBeGreaterThan(selected!.width);
  });

  it("dragging a selected element keeps its ring and adds the dragging look", () => {
    const { container } = mount([{ kind: "element", id: "a" }]);
    const target = elementOn(container, "a");

    fireEvent(target, pointer("pointerdown", { button: 0, clientX: 0, clientY: 0 }));
    fireEvent(target, pointer("pointermove", { clientX: 60, clientY: 40 }));

    expect(target.classList.contains("library-element-dragging"), "the drag never started").toBe(true);
    expect(ringOf(container, "a", "selected")).toEqual({ x: 6, y: 16, width: 108 });
  });

  it("dropped, held and selected shows the ring at the held position, with no dragging look", () => {
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
    expect(ringOf(container, "a", "selected")).toEqual({ x: 6, y: 16, width: 108 });
  });

  it("a drag leaves the selection where it was", () => {
    const onSelectionChanged = vi.fn();
    const { container } = mount([{ kind: "element", id: "b" }], { onSelectionChanged });
    const target = elementOn(container, "a");

    fireEvent(target, pointer("pointerdown", { button: 0, clientX: 0, clientY: 0 }));
    fireEvent(target, pointer("pointermove", { clientX: 60, clientY: 40 }));
    fireEvent(target, pointer("pointerup", { clientX: 60, clientY: 40 }));

    expect(onSelectionChanged).not.toHaveBeenCalled();
    expect(ringOf(container, "b", "selected")).not.toBeNull();
    expect(ringOf(container, "a", "selected")).toBeNull();
  });
});
