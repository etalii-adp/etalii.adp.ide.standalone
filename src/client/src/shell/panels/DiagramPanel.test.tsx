import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { DiagramPanel, type OpenDiagram } from "./DiagramPanel";
import type { DiagramCanvasRegistration } from "./diagramCanvas";

// The registry, not any particular canvas. This panel names no diagram type any more - it asks
// which module claims a MIME type and renders what that module supplies - so its test names
// none either. What each module actually claims is asserted in that module's own register test.
const registrations: DiagramCanvasRegistration[] = [
  {
    matches: (mimeType) => mimeType === "fixture/drawable",
    Canvas: () => <div data-testid="fixture-canvas" />,
  },
  {
    matches: (mimeType) => mimeType === "fixture/known-but-undrawable",
    unsupported: {
      description: "A module can explain its own gap.",
      futureSpec: "fixture-spec",
    },
  },
];

vi.mock("./diagramCanvases", () => ({
  canvasFor: (mimeType: string) => registrations.find((registration) => registration.matches(mimeType)),
}));

function diagram(mimeType: string): OpenDiagram {
  return {
    projectId: new Uint8Array(16).fill(1),
    entryId: new Uint8Array(16).fill(2),
    path: ["docs", "architecture.adp"],
    mimeType,
  };
}

describe("DiagramPanel", () => {
  it("renders the canvas of whichever module claims the type", () => {
    // Act.
    render(<DiagramPanel diagram={diagram("fixture/drawable")} />);

    // Assert.
    expect(screen.getByTestId("fixture-canvas")).toBeTruthy();
  });

  it("puts every module canvas inside the library's frame, the one place refusals and status show", () => {
    // client-centralization Requirement 2, as the user ruled it: the shell places the frame, so no
    // module declares or wires one. A canvas mounted outside it would show its refusals nowhere.
    // Act.
    const { container } = render(<DiagramPanel diagram={diagram("fixture/drawable")} />);

    // Assert.
    expect(container.querySelector(".canvas-frame [data-testid='fixture-canvas']")).not.toBeNull();
  });

  it("shows a claiming module's own explanation when it has no canvas", async () => {
    // Act.
    // A module that knows a type but cannot draw it explains why itself, rather than falling
    // into the shell's generic wording - the shell does not understand anyone's notation.
    render(<DiagramPanel diagram={diagram("fixture/known-but-undrawable")} />);

    // Assert.
    expect(await screen.findByText("A module can explain its own gap.")).toBeTruthy();
    expect(screen.queryByTestId("fixture-canvas")).toBeNull();
  });

  it("names the type it has no canvas for, rather than showing a blank surface", async () => {
    // Act.
    render(<DiagramPanel diagram={diagram("vendor/unheard-of")} />);

    // Assert.
    expect(await screen.findByText("No canvas can render vendor/unheard-of diagrams yet.")).toBeTruthy();
    expect(await screen.findByText("architecture.adp")).toBeTruthy();
  });

  it("keeps the generic fallback for no diagram at all", async () => {
    // Act.
    render(<DiagramPanel />);

    // Assert.
    expect(await screen.findByText("The diagram canvas for viewing and editing.")).toBeTruthy();
  });

  it("keeps the generic fallback for a diagram with no type", async () => {
    // Act.
    render(<DiagramPanel diagram={diagram("")} />);

    // Assert.
    expect(await screen.findByText("The diagram canvas for viewing and editing.")).toBeTruthy();
  });
});
