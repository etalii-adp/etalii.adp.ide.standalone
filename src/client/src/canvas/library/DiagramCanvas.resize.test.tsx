import { describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import type { LibraryEventHandlers } from "./api/diagramEvents";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";

/**
 * Height resize, and the permission that gates it.
 *
 * <b>The point of the second test is the one that matters.</b> Adding handles is easy; adding them
 * only where a type asked for them is what keeps every existing user-sizable element as it was. A
 * version that renders four handles for every `sizing: "user"` type passes the first test and
 * changes the timeline's spans, which is why `resize` omitted has to mean "width" and be asserted.
 */

/** A pointer event jsdom can carry: it implements no PointerEvent, and fireEvent.pointerDown
 * builds one whose `button` is undefined - usePointerGesture.test.tsx's idiom, same reason. */
function pointer(type: string, init: MouseEventInit) {
  return new MouseEvent(type, { bubbles: true, cancelable: true, ...init });
}

// jsdom implements no pointer capture on SVG elements; the arbiter uses it.
SVGElement.prototype.setPointerCapture ??= () => {};
SVGElement.prototype.releasePointerCapture ??= () => {};

function definitionOf(resize?: "width" | "both"): DiagramDefinition {
  return {
    elementTypes: [
      { id: "card", shape: "box", anchors: { kind: "edge" }, sizing: "user", ...(resize === undefined ? {} : { resize }) },
    ],
    relationTypes: [],
    layout: { modes: ["manual"] },
    dragging: "enabled",
  };
}

const model: DiagramModel = {
  elements: [{ id: "a", type: "card", x: 200, y: 100, width: 160, height: 48, label: "Alpha" }],
  connections: [],
};

function canvasOf(resize: "width" | "both" | undefined, events: LibraryEventHandlers = {}) {
  return render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={definitionOf(resize)} model={model} events={events} selection={[{ kind: "element", id: "a" }]} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
}

const handle = (container: HTMLElement, side: string) =>
  container.querySelector(`[data-element-id="a"] [data-resize="${side}"]`);

describe("height resize, where a type declares it", () => {
  it('raises element-resized with the bottom edge and the new height for a "both" type', () => {
    // Arrange: the element is 48 tall; its bottom edge is dragged down.
    const onElementResized = vi.fn();
    const { container } = canvasOf("both", { onElementResized });
    const bottom = handle(container, "bottom")!;
    expect(bottom).not.toBeNull();

    // Act. The surface has no layout in jsdom, so one pixel is one canvas unit.
    fireEvent(bottom, pointer("pointerdown", { button: 0, clientX: 200, clientY: 124 }));
    fireEvent(bottom, pointer("pointermove", { clientX: 200, clientY: 164 }));
    fireEvent(bottom, pointer("pointerup", { clientX: 200, clientY: 164 }));

    // Assert: the edge that moved is named, and the height grew by the drag.
    expect(onElementResized).toHaveBeenCalledTimes(1);
    const raised = onElementResized.mock.calls[0][0] as { side: string; bounds: { height: number; width: number } };
    expect(raised.side).toBe("bottom");
    expect(raised.bounds.height).toBeCloseTo(88, 5);
    // The other axis is untouched: a height drag is not a resize of everything.
    expect(raised.bounds.width).toBeCloseTo(160, 5);
  });

  it("holds the far edge still, rather than inverting the box, when a top drag passes it", () => {
    // Arrange: dragging the top edge far below the bottom one.
    const onElementResized = vi.fn();
    const { container } = canvasOf("both", { onElementResized });
    const top = handle(container, "top")!;

    // Act.
    fireEvent(top, pointer("pointerdown", { button: 0, clientX: 200, clientY: 76 }));
    fireEvent(top, pointer("pointermove", { clientX: 200, clientY: 400 }));
    fireEvent(top, pointer("pointerup", { clientX: 200, clientY: 400 }));

    // Assert: one unit tall, and never negative - the same rule the horizontal edges follow.
    // The model's y is the element's CENTRE, so its top edge is at 100 - 24 = 76 and the far
    // (bottom) edge at 124; a top drag past that stops one unit short of it, at 123.
    const raised = onElementResized.mock.calls[0][0] as { side: string; bounds: { height: number; y: number } };
    expect(raised.side).toBe("top");
    expect(raised.bounds.height).toBeCloseTo(1, 5);
    expect(raised.bounds.y).toBeCloseTo(123, 5);
  });
});

describe("a type that declares no resize keeps exactly the handles it had", () => {
  it("renders the two side handles and no top or bottom handle", () => {
    // Arrange & act: `resize` omitted, which means "width" - today's behaviour for every
    // user-sizable type. This is the assertion a four-handles-for-everyone version fails.
    const { container } = canvasOf(undefined);

    // Assert.
    expect(handle(container, "left")).not.toBeNull();
    expect(handle(container, "right")).not.toBeNull();
    expect(handle(container, "top")).toBeNull();
    expect(handle(container, "bottom")).toBeNull();
  });

  it('renders no top or bottom handle for an explicit "width" either', () => {
    const { container } = canvasOf("width");
    expect(handle(container, "top")).toBeNull();
    expect(handle(container, "bottom")).toBeNull();
  });

  it('renders all four for "both", so the permission is the only difference', () => {
    const { container } = canvasOf("both");
    for (const side of ["left", "right", "top", "bottom"]) {
      expect(handle(container, side), side).not.toBeNull();
    }
  });
});

/**
 * A resize lands its moving edge on the declared `snap.x` lattice - during the gesture as well as
 * on release, so the edge the reader watches is the one the module is told about.
 */
describe("a width resize snaps to the declared step", () => {
  function snappedCanvas(events: LibraryEventHandlers = {}) {
    const definition: DiagramDefinition = { ...definitionOf(), snap: { x: { step: 10, origin: 0 } } };
    return render(
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvasCore definition={definition} model={model} events={events} selection={[{ kind: "element", id: "a" }]} />
        </DiagramToolboxProvider>
      </DiagramViewProvider>,
    );
  }

  it("lands a right edge dragged between two steps on the nearer one, and leaves the left edge where it was", () => {
    // Arrange: the card spans 120..280; its right edge is dragged 13 right, to 293.
    const onElementResized = vi.fn();
    const { container } = snappedCanvas({ onElementResized });
    const right = handle(container, "right")!;

    // Act.
    fireEvent(right, pointer("pointerdown", { button: 0, clientX: 280, clientY: 100 }));
    fireEvent(right, pointer("pointermove", { clientX: 293, clientY: 100 }));
    fireEvent(right, pointer("pointerup", { clientX: 293, clientY: 100 }));

    // Assert: 290, not 293 - and the far edge untouched.
    const raised = onElementResized.mock.calls[0][0] as { bounds: { x: number; width: number } };
    expect(raised.bounds.x).toBe(120);
    expect(raised.bounds.x + raised.bounds.width).toBe(290);
  });

  it("shows the snapped edge while the drag is still in flight", () => {
    // Arrange.
    const { container } = snappedCanvas();
    const right = handle(container, "right")!;

    // Act: mid-drag, nothing released.
    fireEvent(right, pointer("pointerdown", { button: 0, clientX: 280, clientY: 100 }));
    fireEvent(right, pointer("pointermove", { clientX: 293, clientY: 100 }));

    // Assert: the handle rides the drawn edge, 3 either side of it - at 290, not at the pointer.
    expect(Number(handle(container, "right")!.getAttribute("x")) + 3).toBe(290);
  });

  it("stops at one step wide rather than crossing the far edge", () => {
    // Arrange.
    const onElementResized = vi.fn();
    const { container } = snappedCanvas({ onElementResized });
    const right = handle(container, "right")!;

    // Act: the right edge dragged far past the left one.
    fireEvent(right, pointer("pointerdown", { button: 0, clientX: 280, clientY: 100 }));
    fireEvent(right, pointer("pointermove", { clientX: -400, clientY: 100 }));
    fireEvent(right, pointer("pointerup", { clientX: -400, clientY: 100 }));

    // Assert.
    const raised = onElementResized.mock.calls[0][0] as { bounds: { x: number; width: number } };
    expect(raised.bounds.x).toBe(120);
    expect(raised.bounds.width).toBe(10);
  });
});
