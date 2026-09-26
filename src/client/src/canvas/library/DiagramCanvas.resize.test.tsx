import { describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import type { LibraryEventHandlers } from "./api/diagramEvents";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { pointer } from "@client/canvas/library/testing/canvasHarness";

/**
 * Height resize, and the permission that gates it.
 *
 * <b>The point of the second test is the one that matters.</b> Adding handles is easy; adding them
 * only where a type asked for them is what keeps every existing user-sizable element as it was. A
 * version that renders four handles for every `sizing: "user"` type passes the first test and
 * changes the timeline's spans, which is why `resize` omitted has to mean "width" and be asserted.
 */

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
