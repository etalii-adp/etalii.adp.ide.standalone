import { describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { CanvasScrollbars } from "./CanvasScrollbars";
import type { ScrollAxis } from "./scrollGeometry";

/** An axis by its four numbers. */
function axis(viewStart: number, viewSpan: number, extentStart: number, extentEnd: number): ScrollAxis {
  return { viewStart, viewSpan, extentStart, extentEnd };
}

/**
 * jsdom lays nothing out, so a track measures zero wide unless told otherwise. The drag
 * arithmetic divides the extent by the track length, which is the one number these tests
 * need to be real.
 */
function sizeTrack(track: Element, width: number, height: number) {
  Object.defineProperty(track, "getBoundingClientRect", {
    value: () => ({ x: 0, y: 0, top: 0, left: 0, right: width, bottom: height, width, height, toJSON: () => ({}) }),
  });
}

function renderBars(onPan = vi.fn(), className?: string) {
  const horizontal = axis(25, 50, 0, 100);
  const vertical = axis(200, 100, 0, 400);
  const view = render(<CanvasScrollbars horizontal={horizontal} vertical={vertical} onPan={onPan} className={className} />);
  const horizontalBar = view.container.querySelector(".canvas-scrollbar-horizontal")!;
  const verticalBar = view.container.querySelector(".canvas-scrollbar-vertical")!;
  return { ...view, onPan, horizontalBar, verticalBar };
}

describe("CanvasScrollbars", () => {
  it("renders both bars, hidden from assistive technology", () => {
    // Act.
    const { horizontalBar, verticalBar } = renderBars();

    // Assert.
    expect(horizontalBar).not.toBeNull();
    expect(verticalBar).not.toBeNull();
    expect(horizontalBar.getAttribute("aria-hidden")).toBe("true");
    expect(verticalBar.getAttribute("aria-hidden")).toBe("true");
  });

  it("draws each thumb's geometry as percentages of its track", () => {
    // Act.
    const { horizontalBar, verticalBar } = renderBars();
    const horizontalThumb = horizontalBar.querySelector<HTMLElement>(".canvas-scrollbar-thumb")!;
    const verticalThumb = verticalBar.querySelector<HTMLElement>(".canvas-scrollbar-thumb")!;

    // Assert.
    // 25 into a 100-wide extent, spanning 50: a quarter in, half wide. 200 into 400, spanning
    // 100: half down, a quarter tall.
    expect(horizontalThumb.style.left).toBe("25%");
    expect(horizontalThumb.style.width).toBe("50%");
    expect(verticalThumb.style.top).toBe("50%");
    expect(verticalThumb.style.height).toBe("25%");
  });

  it("appends the placement class to both bars and nothing else", () => {
    // Act.
    const { horizontalBar, verticalBar } = renderBars(vi.fn(), "with-ruler");

    // Assert.
    expect(horizontalBar.classList.contains("with-ruler")).toBe(true);
    expect(verticalBar.classList.contains("with-ruler")).toBe(true);
    expect(horizontalBar.classList.contains("canvas-scrollbar")).toBe(true);
  });

  it("pans the horizontal axis on a horizontal drag and leaves the vertical start alone", () => {
    // Arrange.
    // A 200px track over a 100-unit extent: one thumb pixel is half a unit.
    const { onPan, horizontalBar } = renderBars();
    sizeTrack(horizontalBar, 200, 10);
    const thumb = horizontalBar.querySelector(".canvas-scrollbar-thumb")!;

    // Act.
    fireEvent.mouseDown(thumb, { clientX: 100, clientY: 5 });
    fireEvent.mouseMove(window, { clientX: 140, clientY: 5 });

    // Assert.
    // 40 thumb pixels at half a unit each moves the view 20 units on from 25; the vertical
    // start is reported unchanged, so a caller holding one view object writes one update.
    expect(onPan).toHaveBeenLastCalledWith(45, 200);
  });

  it("pans the vertical axis on a vertical drag and leaves the horizontal start alone", () => {
    // Arrange.
    // A 100px track over a 400-unit extent: one thumb pixel is four units.
    const { onPan, verticalBar } = renderBars();
    sizeTrack(verticalBar, 10, 100);
    const thumb = verticalBar.querySelector(".canvas-scrollbar-thumb")!;

    // Act.
    fireEvent.mouseDown(thumb, { clientX: 5, clientY: 50 });
    fireEvent.mouseMove(window, { clientX: 5, clientY: 60 });

    // Assert.
    expect(onPan).toHaveBeenLastCalledWith(25, 240);
  });

  it("stops reporting once the mouse is released", () => {
    // Arrange.
    const { onPan, horizontalBar } = renderBars();
    sizeTrack(horizontalBar, 200, 10);
    const thumb = horizontalBar.querySelector(".canvas-scrollbar-thumb")!;

    // Act.
    fireEvent.mouseDown(thumb, { clientX: 100, clientY: 5 });
    fireEvent.mouseMove(window, { clientX: 120, clientY: 5 });
    fireEvent.mouseUp(window);
    const callsAtRelease = onPan.mock.calls.length;
    fireEvent.mouseMove(window, { clientX: 300, clientY: 5 });

    // Assert.
    expect(callsAtRelease).toBe(1);
    expect(onPan.mock.calls.length).toBe(callsAtRelease);
  });

  it("does not divide by zero on a track that has no layout", () => {
    // Arrange.
    // The jsdom default, and also a bar rendered before the browser has laid it out.
    const { onPan, horizontalBar } = renderBars();
    const thumb = horizontalBar.querySelector(".canvas-scrollbar-thumb")!;

    // Act.
    fireEvent.mouseDown(thumb, { clientX: 0, clientY: 0 });
    fireEvent.mouseMove(window, { clientX: 1, clientY: 0 });

    // Assert.
    const [horizontalStart, verticalStart] = onPan.mock.calls.at(-1)!;
    expect(Number.isFinite(horizontalStart)).toBe(true);
    expect(verticalStart).toBe(200);
  });
});
