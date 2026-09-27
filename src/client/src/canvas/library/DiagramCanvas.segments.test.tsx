import { describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import type { DiagramDefinition, SegmentDeclaration } from "./definition/diagramDefinition";
import type { DiagramModel, DiagramModelElement } from "./api/diagramModel";
import type { LibraryEventHandlers } from "./api/diagramEvents";
import { isInsideOutline, outlineOf } from "./shapes/outline";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { pointer } from "./testing/canvasHarness";

/**
 * An arrow banner cut into segments, read off what the canvas DRAWS.
 *
 * The segments are counted as rendered paths rather than asked of the geometry module, because a
 * drawing that ignored the layout's count would pass every test of the layout. The four segment
 * names below are placeholders: the library knows no notation, and neither does its test.
 */

/** A banner 400 by 32 centred on (200, 16), so its bounds are x 0..400, y 0..32. */
const BOUNDS = { x: 0, y: 0, width: 400, height: 32 };

const SEGMENTS: SegmentDeclaration = {
  count: { path: "payload.count" },
  max: 4,
  boundaries: "payload.boundaries",
  classNames: ["seg-a", "seg-b", "seg-c", "seg-d"],
  tooltips: ["First stretch", "Second stretch", "Third stretch", "Fourth stretch"],
  divider: "chevron",
};

function definitionOf(segments: SegmentDeclaration = SEGMENTS): DiagramDefinition {
  return {
    elementTypes: [{ id: "banner", shape: "arrow-banner", anchors: { kind: "edge" }, sizing: "user", segments }],
    relationTypes: [],
    layout: { modes: ["manual"] },
    dragging: "enabled",
  };
}

function bannerOf(count: number, boundaries?: readonly number[]): DiagramModelElement {
  return { id: "b", type: "banner", x: 200, y: 16, width: 400, height: 32, payload: { count, boundaries } };
}

function renderBanner(element: DiagramModelElement, definition: DiagramDefinition = definitionOf(), events: LibraryEventHandlers = {}) {
  const model: DiagramModel = { elements: [element], connections: [] };
  return render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={definition} model={model} events={events} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
}

const segmentsOf = (container: HTMLElement) => [...container.querySelectorAll<SVGPathElement>("path.library-segment")];
const dividersOf = (container: HTMLElement) => [...container.querySelectorAll("polyline.library-segment-divider")];

/** The corners a segment's `d` visits, in order. */
function cornersOfPath(path: SVGPathElement): { x: number; y: number }[] {
  return [...path.getAttribute("d")!.matchAll(/(-?[\d.]+) (-?[\d.]+)/g)].map(([, x, y]) => ({ x: Number(x), y: Number(y) }));
}

describe("an arrow banner draws its segments", () => {
  it("draws four filled segments and three chevrons at a count of four", () => {
    // Arrange, act.
    const { container } = renderBanner(bannerOf(4, [0.2, 0.45, 0.7]));

    // Assert: one path per drawn segment, each with its declared class, and a divider between each pair.
    const segments = segmentsOf(container);
    expect(segments).toHaveLength(4);
    expect(segments.map((segment) => segment.getAttribute("class"))).toEqual([
      "library-segment seg-a",
      "library-segment seg-b",
      "library-segment seg-c",
      "library-segment seg-d",
    ]);
    expect(dividersOf(container)).toHaveLength(3);
    // A chevron is three points - a V - not a straight line's two.
    for (const divider of dividersOf(container)) {
      expect(divider.getAttribute("points")!.split(" ")).toHaveLength(3);
    }
  });

  it("draws two segments and one chevron at a count of two, whatever the maximum", () => {
    // Arrange, act: the maximum is four and the model says two.
    const { container } = renderBanner(bannerOf(2, [0.5]));

    // Assert: the count decides, not the maximum.
    expect(segmentsOf(container)).toHaveLength(2);
    expect(dividersOf(container)).toHaveLength(1);
  });

  it("keeps every segment's corners inside the banner's outline", () => {
    // Arrange: boundaries close together and close to the point, where a chevron's tails would
    // poke out of the shape if nothing pulled them in.
    for (const boundaries of [[0.2, 0.45, 0.7], [0.05, 0.1, 0.97], [0.3, 0.31, 0.32]]) {
      const { container, unmount } = renderBanner(bannerOf(4, boundaries));
      const outline = outlineOf("arrow-banner", BOUNDS);

      // Act, assert.
      for (const segment of segmentsOf(container)) {
        for (const corner of cornersOfPath(segment)) {
          expect(isInsideOutline(corner, outline), `${boundaries}: ${corner.x},${corner.y}`).toBe(true);
        }
      }
      unmount();
    }
  });

  it("ends the last DRAWN segment in the point, so a one-segment banner is the whole outline", () => {
    // Arrange, act.
    const { container } = renderBanner(bannerOf(1));

    // Assert: the single segment reaches the tip at the right edge's middle.
    const [only] = segmentsOf(container);
    expect(cornersOfPath(only!)).toContainEqual({ x: 400, y: 16 });
  });

  it("spreads the drawn segments evenly when the model gives no usable boundaries", () => {
    // Arrange, act: three segments, and a boundary list meant for four.
    const { container } = renderBanner(bannerOf(3, [0.2, 0.45, 0.7]));

    // Assert: the chevron tips sit at thirds of the width.
    const tips = dividersOf(container).map((divider) => Number(divider.getAttribute("points")!.split(" ")[1]!.split(",")[0]));
    expect(tips[0]).toBeCloseTo(400 / 3, 5);
    expect(tips[1]).toBeCloseTo(800 / 3, 5);
  });
});

describe("a segment's tooltip", () => {
  it("is the one its index declares", () => {
    // Arrange, act.
    const { container } = renderBanner(bannerOf(4, [0.2, 0.45, 0.7]));

    // Assert: resting on segment 2 (index 1) names the second stretch - a <title> is what the
    // browser shows on hover, so it is the tooltip.
    const second = container.querySelector('path.library-segment[data-segment="1"]')!;
    expect(second.querySelector("title")?.textContent).toBe("Second stretch");
  });

  it("over the point is the last DRAWN segment's", () => {
    // Arrange, act: two of four segments drawn.
    const { container } = renderBanner(bannerOf(2, [0.5]));

    // Assert: the path that holds the tip is segment 2's, and names the second stretch - not the
    // fourth, which is the maximum's last.
    const holdingTip = segmentsOf(container).find((segment) => cornersOfPath(segment).some((corner) => corner.x === 400 && corner.y === 16))!;
    expect(holdingTip.getAttribute("data-segment")).toBe("1");
    expect(holdingTip.querySelector("title")?.textContent).toBe("Second stretch");
  });
});

describe("a banner without segments", () => {
  it("draws the outline alone", () => {
    // Arrange, act.
    const plain: DiagramDefinition = {
      ...definitionOf(),
      elementTypes: [{ id: "banner", shape: "arrow-banner", anchors: { kind: "edge" }, sizing: "user" }],
    };
    const { container } = renderBanner(bannerOf(4), plain);

    // Assert.
    expect(segmentsOf(container)).toHaveLength(0);
    expect(container.querySelector("polygon.library-shape")).not.toBeNull();
  });
});

/** Draggable boundaries on a lattice of 4, the way a time axis snaps whole months. */
function draggableDefinition(): DiagramDefinition {
  return { ...definitionOf({ ...SEGMENTS, draggableBoundaries: true }), snap: { x: { step: 4, origin: 0 } } };
}

/** Boundary `index` of the banner, dragged from `fromX` to `toX` - one pixel is one unit in jsdom. */
function dragBoundary(container: HTMLElement, index: number, fromX: number, toX: number, release = true) {
  const handle = container.querySelector(`[data-boundary="${index}"]`)!;
  fireEvent(handle, pointer("pointerdown", { button: 0, clientX: fromX, clientY: 16 }));
  fireEvent(handle, pointer("pointermove", { clientX: toX, clientY: 16 }));
  if (release) {
    fireEvent(handle, pointer("pointerup", { clientX: toX, clientY: 16 }));
  }
}

describe("a draggable segment boundary", () => {
  it("raises segment-boundary-moved with the position snapped to the declared step", () => {
    // Arrange: boundaries at 100, 200 and 300.
    const onSegmentBoundaryMoved = vi.fn();
    const { container } = renderBanner(bannerOf(4, [0.25, 0.5, 0.75]), draggableDefinition(), { onSegmentBoundaryMoved });

    // Act: boundary 1 is dragged 11 units right, to 211 - between the steps at 208 and 212.
    dragBoundary(container, 1, 200, 211);

    // Assert: the nearer step, never the raw pointer position.
    expect(onSegmentBoundaryMoved).toHaveBeenCalledTimes(1);
    expect(onSegmentBoundaryMoved.mock.calls[0][0]).toEqual({ kind: "segment-boundary-moved", elementId: "b", index: 1, x: 212 });
  });

  it("stops one step short of the next boundary when dragged past it", () => {
    // Arrange.
    const onSegmentBoundaryMoved = vi.fn();
    const { container } = renderBanner(bannerOf(4, [0.25, 0.5, 0.75]), draggableDefinition(), { onSegmentBoundaryMoved });

    // Act: boundary 1 dragged far beyond boundary 2 at 300.
    dragBoundary(container, 1, 200, 390);

    // Assert: 296 - the segment between them keeps one step of width.
    expect(onSegmentBoundaryMoved.mock.calls[0][0]).toMatchObject({ index: 1, x: 296 });
  });

  it("redraws the banner with the boundary under the pointer while the drag is in flight", () => {
    // Arrange.
    const { container } = renderBanner(bannerOf(4, [0.25, 0.5, 0.75]), draggableDefinition());

    // Act: mid-drag, nothing released.
    dragBoundary(container, 1, 200, 211, false);

    // Assert: the chevron's tip already sits at the snapped landing.
    const tip = dividersOf(container)[1]!.getAttribute("points")!.split(" ")[1]!;
    expect(tip).toBe("212,16");
  });

  it("has no handle where the declaration does not ask for one", () => {
    // Arrange, act.
    const { container } = renderBanner(bannerOf(4, [0.25, 0.5, 0.75]));

    // Assert.
    expect(container.querySelector("[data-boundary]")).toBeNull();
  });
});
