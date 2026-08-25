import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { RibbonBar } from "./RibbonBar";
import { DiagramViewProvider, useRegisterDiagramView, type DiagramViewControls } from "../panels/DiagramViewContext";

const executeAction = vi.fn(async () => ({ accepted: true, error: "" }));

vi.mock("../context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextConnection: () => ({ watchId: new Uint8Array(16), select: vi.fn(), executeAction, executeShortcut: vi.fn() }),
    useContextSelection: () => ({ selection: null, levels: [], actions: [], preview: null, pendingReveal: null, connected: true }),
    useProjectActions: () => [],
    useContextPrompt: () => ({ prompt: null, onPropose: vi.fn(), onSubmit: vi.fn(), onCancel: vi.fn() }),
  };
});

const zoomIn = vi.fn();
const zoomOut = vi.fn();
const fitToView = vi.fn();

// One stable object, the way the real canvas memoizes its controls: a fresh identity per
// render would re-register forever.
const fakeControls: DiagramViewControls = { zoomIn, zoomOut, fitToView };

/** Stands in for a mounted canvas: registers its view controls the way MindmapCanvas does. */
function FakeCanvas() {
  useRegisterDiagramView(fakeControls);
  return null;
}

describe("RibbonBar view group", () => {
  beforeEach(() => {
    zoomIn.mockClear();
    zoomOut.mockClear();
    fitToView.mockClear();
  });

  it("disables zoom and fit while no diagram is open, and says why", () => {
    // Arrange.
    render(
      <DiagramViewProvider>
        <RibbonBar />
      </DiagramViewProvider>,
    );

    // Act and assert, step by step.
    for (const label of ["Zoom In", "Zoom Out", "Fit to View"]) {
      const button = screen.getByRole("button", { name: label }) as HTMLButtonElement;
      expect(button.disabled).toBe(true);
      expect(button.title).toBe("Open a diagram to use this.");
    }
  });

  it("drives the mounted canvas's own view controls", () => {
    // Arrange.
    render(
      <DiagramViewProvider>
        <RibbonBar />
        <FakeCanvas />
      </DiagramViewProvider>,
    );

    // Act.
    fireEvent.click(screen.getByRole("button", { name: "Zoom In" }));
    fireEvent.click(screen.getByRole("button", { name: "Zoom Out" }));
    fireEvent.click(screen.getByRole("button", { name: "Fit to View" }));

    // Assert.
    expect(zoomIn).toHaveBeenCalledTimes(1);
    expect(zoomOut).toHaveBeenCalledTimes(1);
    expect(fitToView).toHaveBeenCalledTimes(1);
  });

  it("disables the group again when the canvas unmounts", () => {
    // Arrange.
    const { rerender } = render(
      <DiagramViewProvider>
        <RibbonBar />
        <FakeCanvas />
      </DiagramViewProvider>,
    );
    expect((screen.getByRole("button", { name: "Zoom In" }) as HTMLButtonElement).disabled).toBe(false);

    // Act.
    rerender(
      <DiagramViewProvider>
        <RibbonBar />
      </DiagramViewProvider>,
    );

    // Assert.
    expect((screen.getByRole("button", { name: "Zoom In" }) as HTMLButtonElement).disabled).toBe(true);
  });
});
