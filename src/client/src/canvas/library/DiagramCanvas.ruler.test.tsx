import { describe, expect, it } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";

/**
 * A declared bottom ruler, drawn by the library.
 *
 * jsdom lays nothing out, so "stays at the bottom of the viewport" is asserted as what makes it
 * true: the strip is HTML beside the drawing rather than anything inside it, and nothing about
 * its placement moves when the view does. The CSS that pins it is `.library-ruler`, and the
 * browser pass is where its pixels are checked. What jsdom CAN check exactly is the arithmetic:
 * a tick's position as a share of the strip, against the view the drawing is using.
 */

/** A pointer event jsdom can carry: it implements no PointerEvent (DiagramCanvas.resize.test.tsx's idiom). */
function pointer(type: string, init: MouseEventInit) {
  return new MouseEvent(type, { bubbles: true, cancelable: true, ...init });
}

// jsdom implements no pointer capture on SVG elements; the arbiter uses it.
SVGElement.prototype.setPointerCapture ??= () => {};
SVGElement.prototype.releasePointerCapture ??= () => {};

/** Canvas x of the first of a month on the declared scale: four units a month from 1900-01. */
const x = (year: number, month: number) => ((year * 12) + (month - 1) - (1900 * 12)) * 4;

const definition: DiagramDefinition = {
  elementTypes: [{ id: "bar", shape: "box", anchors: { kind: "edge" }, sizing: "model" }],
  relationTypes: [],
  layout: { modes: ["manual"] },
  dragging: "enabled",
  chrome: {
    rulers: [
      {
        orientation: "horizontal",
        edge: "bottom",
        scale: { unit: "month", unitsPerStep: 4, origin: "1900-01" },
        ladder: [
          { every: { calendar: "month" }, label: "MMM yyyy" },
          { every: { calendar: "quarter" }, label: "MMM yyyy" },
          { every: { calendar: "year" }, label: "yyyy" },
          { every: { calendar: "decade" }, label: "yyyy" },
        ],
        minSpacingPx: 64,
      },
    ],
  },
};

/** Two bars, 1940 to 1960 wide between them, so the fitted view shows both and 1950 between. */
const model: DiagramModel = {
  elements: [
    { id: "a", type: "bar", x: x(1940, 1), y: 0, width: 40, height: 32 },
    { id: "b", type: "bar", x: x(1960, 1), y: 400, width: 40, height: 32 },
  ],
  connections: [],
};

function renderRuler() {
  return render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={definition} model={model} events={{}} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
}

const viewOf = (container: HTMLElement) => {
  const [vx, vy, vw, vh] = container.querySelector("svg")!.getAttribute("viewBox")!.split(" ").map(Number);
  return { x: vx!, y: vy!, w: vw!, h: vh! };
};

/** Where the 1950-01 tick sits along the strip, as a share of it - null when it is not shown. */
function share1950(container: HTMLElement): number | null {
  const tick = container.querySelector<HTMLElement>(`.library-ruler-tick[data-at="${1950 * 12}"]`);
  return tick === null ? null : parseFloat(tick.style.left) / 100;
}

describe("a declared bottom ruler", () => {
  it("is drawn as chrome beside the drawing, never inside it", () => {
    // Arrange, act.
    const { container } = renderRuler();

    // Assert: in the canvas's box and outside the svg, so no viewBox change can carry it away.
    const strip = container.querySelector(".library-ruler")!;
    expect(strip).not.toBeNull();
    expect(strip.closest("svg")).toBeNull();
    expect(strip.parentElement?.classList.contains("library-canvas")).toBe(true);
  });

  it("keeps its place when the view scrolls vertically", () => {
    // Arrange.
    const { container } = renderRuler();
    const before = container.querySelector(".library-ruler")!.getAttribute("style");
    const viewBefore = viewOf(container);

    // Act: the background dragged straight down - a vertical pan.
    const svg = container.querySelector("svg")!;
    fireEvent(svg, pointer("pointerdown", { button: 0, clientX: 100, clientY: 100 }));
    fireEvent(svg, pointer("pointermove", { clientX: 100, clientY: 300 }));
    fireEvent(svg, pointer("pointerup", { clientX: 100, clientY: 300 }));

    // Assert: the view moved, and the strip's own placement did not change with it.
    expect(viewOf(container).y).not.toBe(viewBefore.y);
    expect(container.querySelector(".library-ruler")!.getAttribute("style")).toBe(before);
    expect(container.querySelector(".library-ruler")!.closest("svg")).toBeNull();
  });

  it("keeps the 1950-01 tick over canvas x of 1950-01 through a pan and a zoom", () => {
    // Arrange.
    const { container } = renderRuler();
    const expected = () => {
      const view = viewOf(container);
      return (x(1950, 1) - view.x) / view.w;
    };
    expect(share1950(container)).toBeCloseTo(expected(), 6);

    // Act: panned sideways...
    const svg = container.querySelector("svg")!;
    fireEvent(svg, pointer("pointerdown", { button: 0, clientX: 100, clientY: 100 }));
    fireEvent(svg, pointer("pointermove", { clientX: 160, clientY: 100 }));
    fireEvent(svg, pointer("pointerup", { clientX: 160, clientY: 100 }));

    // Assert: still over it.
    expect(share1950(container)).toBeCloseTo(expected(), 6);

    // Act: ...then zoomed in.
    fireEvent.wheel(svg, { deltaY: -100 });

    // Assert.
    expect(share1950(container)).toBeCloseTo(expected(), 6);
  });
});
