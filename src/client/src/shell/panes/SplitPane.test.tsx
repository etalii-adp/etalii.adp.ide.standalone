import { describe, expect, it, vi } from "vitest";
import { act, fireEvent, render } from "@testing-library/react";
import { SplitPane } from "./SplitPane";

// jsdom has no PointerEvent implementation, so @testing-library/dom's
// fireEvent falls back to a plain Event whose constructor silently drops
// clientX/clientY (they aren't part of EventInit). Dispatch manually with
// those properties actually attached, so the component under test - which
// reads real PointerEvent.clientX/clientY in a real browser - sees them here too.
// Wrapped in act() since fireEvent's automatic act() wrapping doesn't apply
// to a manually constructed dispatchEvent call.
function firePointerMove(clientX: number, clientY: number) {
  const event = new Event("pointermove", { bubbles: true }) as unknown as PointerEvent;
  Object.assign(event, { clientX, clientY });
  act(() => {
    document.dispatchEvent(event);
  });
}

function mockRect(element: HTMLElement, rect: Partial<DOMRect>) {
  vi.spyOn(element, "getBoundingClientRect").mockReturnValue({
    x: 0,
    y: 0,
    top: 0,
    left: 0,
    right: 0,
    bottom: 0,
    width: 0,
    height: 0,
    toJSON: () => undefined,
    ...rect,
  } as DOMRect);
}

describe("SplitPane", () => {
  it("renders both panes sized by the initial split ratio", () => {
    // Arrange and act.
    const { getByText } = render(
      <SplitPane
        direction="horizontal"
        initialSplit={0.3}
        minSize={50}
        first={<div>First</div>}
        second={<div>Second</div>}
      />,
    );

    // Assert.
    expect(getByText("First").parentElement).toHaveProperty("style.width", "30%");
    expect(getByText("Second").parentElement).toHaveProperty("style.width", "70%");
  });

  it("updates the split ratio when the divider is dragged (horizontal)", () => {
    // Arrange.
    const { container, getByText, getByRole } = render(
      <SplitPane
        direction="horizontal"
        initialSplit={0.5}
        minSize={50}
        first={<div>First</div>}
        second={<div>Second</div>}
      />,
    );
    const root = container.querySelector(".split-pane") as HTMLElement;
    mockRect(root, { width: 1000, height: 500 });

    // Act.
    fireEvent.pointerDown(getByRole("separator"));
    firePointerMove(250, 0);

    // Assert.
    expect(getByText("First").parentElement).toHaveProperty("style.width", "25%");
  });

  it("clamps the split ratio at minSize on both ends", () => {
    // Arrange.
    const { container, getByText, getByRole } = render(
      <SplitPane
        direction="horizontal"
        initialSplit={0.5}
        minSize={100}
        first={<div>First</div>}
        second={<div>Second</div>}
      />,
    );
    const root = container.querySelector(".split-pane") as HTMLElement;
    mockRect(root, { width: 1000, height: 500 });

    fireEvent.pointerDown(getByRole("separator"));

    // Act and assert, step by step.
    firePointerMove(-500, 0);
    expect(getByText("First").parentElement).toHaveProperty("style.width", "10%");

    firePointerMove(5000, 0);
    expect(getByText("First").parentElement).toHaveProperty("style.width", "90%");
  });

  it("supports vertical orientation", () => {
    // Arrange.
    const { container, getByText, getByRole } = render(
      <SplitPane
        direction="vertical"
        initialSplit={0.5}
        minSize={50}
        first={<div>First</div>}
        second={<div>Second</div>}
      />,
    );
    const root = container.querySelector(".split-pane") as HTMLElement;
    mockRect(root, { width: 500, height: 1000 });

    // Act.
    fireEvent.pointerDown(getByRole("separator"));
    firePointerMove(0, 250);

    // Assert.
    expect(getByText("First").parentElement).toHaveProperty("style.height", "25%");
  });
});
