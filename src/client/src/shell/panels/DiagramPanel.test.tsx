import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { DiagramPanel, type OpenDiagram } from "./DiagramPanel";

// The canvas opens a stream on mount; here only the routing decision is under test.
vi.mock("./mindmap/MindmapCanvas", () => ({
  MindmapCanvas: () => <div data-testid="mindmap-canvas" />,
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
  it("routes a mindmap to its canvas", () => {
    render(<DiagramPanel diagram={diagram("freeplane/mindmap")} />);

    expect(screen.getByTestId("mindmap-canvas")).toBeTruthy();
  });

  it("names the type it has no canvas for, rather than showing a blank surface", async () => {
    render(<DiagramPanel diagram={diagram("vendor/unheard-of")} />);

    expect(await screen.findByText("No canvas can render vendor/unheard-of diagrams yet.")).toBeTruthy();
    expect(await screen.findByText("architecture.adp")).toBeTruthy();
  });

  it("keeps the generic fallback for no diagram at all", async () => {
    render(<DiagramPanel />);

    expect(await screen.findByText("The diagram canvas for viewing and editing.")).toBeTruthy();
  });
});
