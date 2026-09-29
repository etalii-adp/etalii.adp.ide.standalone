import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { ToolPanel, type OpenTool } from "./ToolPanel";
import type { ToolPanelRegistration } from "./toolPanelRegistration";

// The registry, not any particular canvas. This panel names no tool type any more - it asks
// which module claims a MIME type and renders what that module supplies - so its test names
// none either. What each module actually claims is asserted in that module's own register test.
const registrations: ToolPanelRegistration[] = [
  {
    matches: (mimeType) => mimeType === "fixture/drawable",
    Panel: () => <div data-testid="fixture-canvas" />,
  },
  {
    matches: (mimeType) => mimeType === "fixture/known-but-undrawable",
    unsupported: {
      description: "A module can explain its own gap.",
      futureSpec: "fixture-spec",
    },
  },
];

vi.mock("./toolPanels", () => ({
  panelFor: (mimeType: string) => registrations.find((registration) => registration.matches(mimeType)),
}));

function openTool(mimeType: string): OpenTool {
  return {
    projectId: new Uint8Array(16).fill(1),
    entryId: new Uint8Array(16).fill(2),
    path: ["docs", "architecture.adp"],
    mimeType,
  };
}

describe("ToolPanel", () => {
  it("renders the panel of whichever module claims the type", () => {
    // Act.
    render(<ToolPanel tool={openTool("fixture/drawable")} />);

    // Assert.
    expect(screen.getByTestId("fixture-canvas")).toBeTruthy();
  });

  it("puts every module canvas inside the library's frame, the one place refusals and status show", () => {
    // client-centralization Requirement 2, as the user ruled it: the shell places the frame, so no
    // module declares or wires one. A canvas mounted outside it would show its refusals nowhere.
    // Act.
    const { container } = render(<ToolPanel tool={openTool("fixture/drawable")} />);

    // Assert.
    expect(container.querySelector(".canvas-frame [data-testid='fixture-canvas']")).not.toBeNull();
  });

  it("shows a claiming module's own explanation when it has no panel", async () => {
    // Act.
    // A module that knows a type but cannot draw it explains why itself, rather than falling
    // into the shell's generic wording - the shell does not understand anyone's notation.
    render(<ToolPanel tool={openTool("fixture/known-but-undrawable")} />);

    // Assert.
    expect(await screen.findByText("A module can explain its own gap.")).toBeTruthy();
    expect(screen.queryByTestId("fixture-canvas")).toBeNull();
  });

  it("names the type it has no panel for, rather than showing a blank surface", async () => {
    // Act.
    render(<ToolPanel tool={openTool("vendor/unheard-of")} />);

    // Assert.
    expect(await screen.findByText("No module can show vendor/unheard-of yet.")).toBeTruthy();
    expect(await screen.findByText("architecture.adp")).toBeTruthy();
  });

  it("keeps the generic fallback for no document at all", async () => {
    // Act.
    render(<ToolPanel />);

    // Assert.
    expect(await screen.findByText("The tool panel, where a diagram, designer or editor opens for viewing and editing.")).toBeTruthy();
  });

  it("keeps the generic fallback for a document with no type", async () => {
    // Act.
    render(<ToolPanel tool={openTool("")} />);

    // Assert.
    expect(await screen.findByText("The tool panel, where a diagram, designer or editor opens for viewing and editing.")).toBeTruthy();
  });
});
