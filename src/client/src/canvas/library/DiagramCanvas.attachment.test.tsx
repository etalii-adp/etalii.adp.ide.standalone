import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import type { BuiltInShape, DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "./api/diagramModel";
import { isInsideOutline, outlineOf } from "./shapes/outline";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { pointer } from "./testing/canvasHarness";

/**
 * Where a connector touches a shape that is not a rectangle.
 *
 * <b>The point is read off the rendered path rather than from the resolver</b>, because the drawn
 * line is what a reader sees and the canvas has exactly one attachment resolver behind it
 * (Requirement 3.4) - so the path is evidence about the resolver, while a direct call on the
 * resolver would not be evidence about the path.
 *
 * The test is written against `isInsideOutline` rather than against the intersection arithmetic. A
 * guard that recomputed the crossing would be a second copy of the implementation and would agree
 * with it however wrong both were; "inside the outline, and half a unit further out is outside it"
 * is a statement about the drawn shape that holds whatever the arithmetic looks like.
 */

/** The trapezoid: 160 by 48 centred on the origin, so its bounding box is x -80..80, y -24..24. */
const BOUNDS = { x: -80, y: -24, width: 160, height: 48 };

/**
 * A partner element down and to the LEFT, at a shallow angle.
 *
 * The angle is chosen so the ray leaves through the trapezoid's slanted left side rather than
 * through its bottom edge: a steeper approach exits the bottom, where the outline and the bounding
 * box agree and the whole question is invisible.
 */
const TOWARDS = { x: -400, y: 60 };

function definitionOf(shape: BuiltInShape): DiagramDefinition {
  return {
    elementTypes: [
      { id: "shaped", shape, anchors: { kind: "edge" }, sizing: "model" },
      { id: "plain", shape: "box", anchors: { kind: "edge" }, sizing: "model" },
    ],
    relationTypes: [
      {
        id: "links",
        route: "straight",
        endpoints: { source: { elementTypes: ["shaped"] }, target: { elementTypes: ["plain"] }, allowSelf: false },
      },
    ],
    layout: { modes: ["manual"] },
    dragging: "enabled",
  };
}

const model: DiagramModel = {
  elements: [
    { id: "t", type: "shaped", x: 0, y: 0, width: 160, height: 48, label: "T" },
    { id: "far", type: "plain", x: TOWARDS.x, y: TOWARDS.y, width: 100, height: 40, label: "Far" },
  ],
  connections: [{ id: "t->far", type: "links", sourceId: "t", targetId: "far" }],
};

/** Where the drawn line starts, which is where it touches the source element. */
function attachmentFor(shape: BuiltInShape): { x: number; y: number } {
  const { container } = render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={definitionOf(shape)} model={model} events={{}} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );

  const d = container.querySelector('[data-connection-id="t->far"] path.canvas-connection-line')!.getAttribute("d")!;
  const [, x, y] = /^M (-?[\d.]+) (-?[\d.]+)/.exec(d)!;

  return { x: Number(x), y: Number(y) };
}

/** The same point pushed half a unit further along the ray, away from the centre. */
function halfAUnitOut(point: { x: number; y: number }): { x: number; y: number } {
  const length = Math.hypot(TOWARDS.x, TOWARDS.y);

  return { x: point.x + (0.5 * TOWARDS.x) / length, y: point.y + (0.5 * TOWARDS.y) / length };
}

describe("a connector meets the outline, not the bounding box", () => {
  it("lands on a trapezoid's slanted side, on the outline and within half a unit of it", () => {
    // Arrange, act.
    const point = attachmentFor("trapezoid");
    const outline = outlineOf("trapezoid", BOUNDS);

    // Assert: on the shape...
    expect(isInsideOutline(point, outline), `${point.x},${point.y} is not on or inside the trapezoid`).toBe(true);
    // ...and no more than half a unit inside it, or a line would stop short of the edge it points at.
    expect(isInsideOutline(halfAUnitOut(point), outline)).toBe(false);
    // ...on the SLANTED side rather than on a bounding-box edge or a corner: strictly inside the
    // box on both axes, which only the slant allows. `x` at -80 is today's answer, and at y 12 the
    // trapezoid's own left boundary is at -62 - so the arrowhead sits beside the shape it points at.
    expect(point.x).toBeGreaterThan(BOUNDS.x);
    expect(point.y).toBeLessThan(BOUNDS.y + BOUNDS.height);
  });

  it("leaves a box's attachment point exactly where it was", () => {
    // Arrange, act: the same element, the same partner, the one shape with no outline of its own.
    const point = attachmentFor("box");

    // Assert: the bounding-box answer, unchanged - the ray meets x = -80 at y = 12. A version that
    // gave `box` an outline would move every rectangle in every diagram in the product.
    expect(point.x).toBeCloseTo(-80, 5);
    expect(point.y).toBeCloseTo(12, 5);
  });

  it("lands on the diamond and the superellipse too, not only on the shape the task named", () => {
    // The change is written against `outlineOf`, so it holds for every shape that has an outline -
    // and a guard naming one shape cannot tell a general implementation from a special case for
    // trapezoids. These two were bounding-box attached before this change as much as the
    // trapezoid was.
    for (const shape of ["diamond", "superellipse"] as const) {
      const point = attachmentFor(shape);
      const outline = outlineOf(shape, BOUNDS);
      expect(isInsideOutline(point, outline), shape).toBe(true);
      expect(isInsideOutline(halfAUnitOut(point), outline), shape).toBe(false);
      expect(point.x, shape).toBeGreaterThan(BOUNDS.x);
    }
  });
});

/**
 * Continuous attachment: a connection end that is a FRACTION of a stretch of edge, recomputed from
 * the element's current bounds and segments on every render.
 *
 * The surface is given a real size here - jsdom measures nothing - equal to the declared extent,
 * so a client pixel is a canvas unit and the pointer lands where the test says: canvas x is client
 * x, canvas y is client y less 100.
 */
describe("a connection attached along an edge", () => {
  const EXTENT = { x: 0, y: -100, width: 800, height: 400 };
  let restore: (() => void) | undefined;

  beforeEach(() => {
    const original = SVGSVGElement.prototype.getBoundingClientRect;
    SVGSVGElement.prototype.getBoundingClientRect = () =>
      ({ left: 0, top: 0, x: 0, y: 0, width: EXTENT.width, height: EXTENT.height, right: EXTENT.width, bottom: EXTENT.height, toJSON: () => ({}) }) as DOMRect;
    restore = () => { SVGSVGElement.prototype.getBoundingClientRect = original; };
  });

  afterEach(() => restore?.());

  function bannerDefinition(route: "straight" | "cubic-bezier" = "straight"): DiagramDefinition {
    return {
      elementTypes: [
        {
          id: "banner",
          shape: "arrow-banner",
          anchors: { kind: "along", edges: ["top", "bottom"], regions: "segments" },
          sizing: "user",
          segments: { count: 4, max: 4, boundaries: "payload.boundaries", divider: "chevron" },
        },
      ],
      relationTypes: [
        { id: "flows", route, endpoints: { source: { elementTypes: ["banner"] }, target: { elementTypes: ["banner"] }, allowSelf: false } },
      ],
      layout: { modes: ["manual"] },
      dragging: "enabled",
      extent: EXTENT,
    };
  }

  /** A banner with its boundaries at quarters; `a` spans x 0..width, y 0..32. */
  const banner = (id: string, x: number, y: number, width = 400): DiagramModelElement =>
    ({ id, type: "banner", x, y, width, height: 32, payload: { boundaries: [0.25, 0.5, 0.75] } });

  function renderModel(elements: DiagramModelElement[], connections: DiagramModelConnection[] = [], onConnectionDrawn = vi.fn(), route: "straight" | "cubic-bezier" = "straight") {
    const result = render(
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvasCore definition={bannerDefinition(route)} model={{ elements, connections }} events={{ onConnectionDrawn }} />
        </DiagramToolboxProvider>
      </DiagramViewProvider>,
    );
    return { ...result, onConnectionDrawn };
  }

  const startOf = (container: HTMLElement) => {
    const d = container.querySelector('[data-connection-id="c"] path.canvas-connection-line')!.getAttribute("d")!;
    const [, x, y] = /^M (-?[\d.]+) (-?[\d.]+)/.exec(d)!;
    return { x: Number(x), y: Number(y) };
  };

  const attached: DiagramModelConnection = {
    id: "c",
    type: "flows",
    sourceId: "a",
    targetId: "b",
    sourceAttachment: { edge: "top", region: 1, at: 0.25 },
    targetAttachment: { edge: "bottom", region: 0, at: 0.5 },
  };

  it("ends at the fraction of its segment's stretch of the edge", () => {
    // Arrange, act: segment 2's top runs from the first chevron's tail at 84 to the second's at 184.
    const { container } = renderModel([banner("a", 200, 16), banner("b", 600, 216)], [attached]);

    // Assert: a quarter of the way along it, on the top edge.
    expect(startOf(container)).toEqual({ x: 109, y: 0 });
  });

  it("stays at that fraction of the NEW segment when the element doubles in width", () => {
    // Arrange, act: the same connection, the banner now 800 wide - segment 2's top runs 184..384.
    const { container } = renderModel([banner("a", 400, 16, 800), banner("b", 600, 216)], [attached]);

    // Assert: a quarter along the new stretch. An end stored as an offset would stay at 109.
    expect(startOf(container)).toEqual({ x: 234, y: 0 });
  });

  it("leaves and arrives square to the edge on a curved route, so the arrowhead meets the border at 90 degrees", () => {
    // Arrange, act: from a's TOP edge to b's BOTTOM edge, b below and to the right of a.
    const { container } = renderModel([banner("a", 200, 16), banner("b", 600, 216)], [attached], vi.fn(), "cubic-bezier");
    const d = container.querySelector('[data-connection-id="c"] path.canvas-connection-line')!.getAttribute("d")!;
    const [sx, sy, c1x, c1y, c2x, c2y, ex, ey] = [...d.matchAll(/-?[\d.]+/g)].map((match) => Number(match[0]));

    // Assert: the first control point is straight above the start (a top edge leaves upward) and
    // the second straight below the end (a bottom edge is arrived at from below), so the tangent
    // at each end - which is the direction the arrowhead points - is the edge's normal.
    expect({ x: c1x - sx, up: c1y < sy }, "the curve leaves the top edge sideways: the arrowhead and the tail cross the border at a slant").toEqual({ x: 0, up: true });
    expect({ x: c2x - ex, below: c2y > ey }, "the curve arrives at the bottom edge sideways").toEqual({ x: 0, below: true });
  });

  describe("an end handle on a selected connection", () => {
    function renderSelected(movableEnds: boolean, onConnectionEndMoved = vi.fn()) {
      const plain = bannerDefinition("cubic-bezier");
      const definition: DiagramDefinition = { ...plain, relationTypes: [{ ...plain.relationTypes[0]!, movableEnds }] };
      const view = (connection: DiagramModelConnection) => (
        <DiagramViewProvider>
          <DiagramToolboxProvider>
            <DiagramCanvasCore
              definition={definition}
              model={{ elements: [banner("a", 200, 16), banner("b", 600, 216)], connections: [connection] }}
              events={{ onConnectionEndMoved }}
              selection={[{ kind: "connection", id: "c" }]}
            />
          </DiagramToolboxProvider>
        </DiagramViewProvider>
      );
      const result = render(view(attached));
      return { ...result, onConnectionEndMoved, rerenderWith: (connection: DiagramModelConnection) => result.rerender(view(connection)) };
    }

    const endOf = (container: HTMLElement) => {
      const d = container.querySelector('[data-connection-id="c"] path.canvas-connection-line')!.getAttribute("d")!;
      const numbers = [...d.matchAll(/-?[\d.]+/g)].map((match) => Number(match[0]));
      return { x: numbers[numbers.length - 2], y: numbers[numbers.length - 1] };
    };

    it("is drawn, round, on each end of a selected connection whose relation declares movable ends", () => {
      // Arrange, act.
      const { container } = renderSelected(true);

      // Assert: at the start (109, 0) and at the end (b's first segment's bottom, halfway).
      const handles = [...container.querySelectorAll("circle.library-end-handle")].map((handle) => `${handle.getAttribute("data-end")} ${handle.getAttribute("cx")},${handle.getAttribute("cy")}`);
      expect(handles, "a selected influence showed no handle on its ends, so an end could not be moved").toEqual(["source 109,0", "target 442,232"]);

      // Assert: drawn after every element, so a trend's edge strip above the line cannot take the
      // press meant for the handle - which it did, in a browser, before the handles had a layer.
      const handle = container.querySelector("circle.library-end-handle")!;
      for (const element of container.querySelectorAll("[data-element-id]")) {
        expect(element.compareDocumentPosition(handle) & Node.DOCUMENT_POSITION_FOLLOWING, `the handle is drawn beneath ${element.getAttribute("data-element-id")}`).toBeTruthy();
      }
    });

    it("is not drawn where the relation does not declare it", () => {
      // Arrange, act.
      const { container } = renderSelected(false);

      // Assert.
      expect(container.querySelector("circle.library-end-handle")).toBeNull();
    });

    it("slides the end along its own edge into another segment, and raises the new attachment on release", () => {
      // Arrange: b's bottom edge; its third segment's bottom runs 584..684 at y 232.
      const { container, onConnectionEndMoved } = renderSelected(true);
      const target = container.querySelector('circle.library-end-handle[data-end="target"]')!;

      // Act: dragged right and up, nearer b's TOP edge than its bottom - it must stay on the bottom.
      fireEvent(target, pointer("pointerdown", { button: 0, clientX: 442, clientY: 332 }));
      fireEvent(target, pointer("pointermove", { clientX: 634, clientY: 305 }));

      // Assert: mid-drag, the line already ends on the edge under the pointer.
      expect(endOf(container)).toEqual({ x: 634, y: 232 });

      // Act.
      fireEvent(target, pointer("pointerup", { clientX: 634, clientY: 305 }));

      // Assert: the same edge, the third segment, halfway along it.
      expect(onConnectionEndMoved).toHaveBeenCalledTimes(1);
      expect(onConnectionEndMoved.mock.calls[0][0]).toEqual({ kind: "connection-end-moved", connectionId: "c", end: "target", attachment: { edge: "bottom", region: 2, at: 0.5 } });

      // Assert: still drawn where it was released while the model has not answered.
      expect(endOf(container), "the end jumped back to where it was until the backend answered").toEqual({ x: 634, y: 232 });
    });
  });

  it("records where a gesture started and ended, as edge, segment and fraction", () => {
    // Arrange.
    const { container, onConnectionDrawn } = renderModel([banner("a", 200, 16), banner("b", 600, 216)]);
    const strip = container.querySelector('[data-element-id="a"] .library-edge-strip[data-edge="bottom"][data-region="0"]')!;
    expect(strip).not.toBeNull();

    // Act: pressed on segment 1's bottom edge at canvas (40, 32), released just inside b's top edge
    // at canvas (600, 201) - in b's third segment, whose top runs 584..684.
    fireEvent(strip, pointer("pointerdown", { button: 0, clientX: 40, clientY: 132 }));
    fireEvent(strip, pointer("pointermove", { clientX: 600, clientY: 301 }));
    fireEvent(strip, pointer("pointerup", { clientX: 600, clientY: 301 }));

    // Assert: both ends as fractions of the stretch they landed on, to two decimals.
    expect(onConnectionDrawn).toHaveBeenCalledTimes(1);
    expect(onConnectionDrawn.mock.calls[0][0]).toMatchObject({
      sourceElementId: "a",
      targetElementId: "b",
      sourceAttachment: { edge: "bottom", region: 0, at: 0.48 },
      targetAttachment: { edge: "top", region: 2, at: 0.16 },
    });
  });

  it("lights up the stretch it would attach to while the drag is in flight", () => {
    // Arrange.
    const { container } = renderModel([banner("a", 200, 16), banner("b", 600, 216)]);
    const strip = container.querySelector('[data-element-id="a"] .library-edge-strip[data-edge="bottom"][data-region="0"]')!;

    // Act: mid-drag over b's third segment.
    fireEvent(strip, pointer("pointerdown", { button: 0, clientX: 40, clientY: 132 }));
    fireEvent(strip, pointer("pointermove", { clientX: 600, clientY: 301 }));

    // Assert.
    expect(container.querySelector('[data-element-id="b"] .library-attachment-highlight')?.getAttribute("data-attachment")).toBe("top:2");
  });
});

