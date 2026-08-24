import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { DiagramPanel, type OpenDiagram } from "./DiagramPanel";

// The canvas opens a stream on mount; here only the routing decision is under test.
vi.mock("./mindmap/MindmapCanvas", () => ({
  MindmapCanvas: () => <div data-testid="mindmap-canvas" />,
}));

vi.mock("./c4/C4Canvas", () => ({
  C4Canvas: () => <div data-testid="c4-canvas" />,
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

  it.each([
    "c4/context",
    "c4/container",
    "c4/component",
    "c4/system-landscape",
    "c4/dynamic",
    "c4/deployment",
  ])("routes %s to the shared C4 canvas", (mimeType) => {
    // Six views of one notation, so one canvas - not six.
    render(<DiagramPanel diagram={diagram(mimeType)} />);

    expect(screen.getByTestId("c4-canvas")).toBeTruthy();
  });

  it("sends c4/code to its own notice rather than the C4 canvas", async () => {
    // C4 specifies UML class or ER notation for the code level and advises generating it
    // rather than drawing it, so this type waits on a class diagram type.
    render(<DiagramPanel diagram={diagram("c4/code")} />);

    expect(screen.queryByTestId("c4-canvas")).toBeNull();
    expect(await screen.findByText(/UML class or entity-relationship notation/)).toBeTruthy();
  });

  it("keeps the generic fallback for no diagram at all", async () => {
    render(<DiagramPanel />);

    expect(await screen.findByText("The diagram canvas for viewing and editing.")).toBeTruthy();
  });
});
