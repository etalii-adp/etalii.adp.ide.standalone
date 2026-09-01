import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { MarkdownOutline, headingsOf } from "./MarkdownEditorPanel";

describe("headingsOf", () => {
  it("finds ATX headings with their level and 1-based line", () => {
    // Arrange and act.
    const headings = headingsOf("# One\ntext\n## Two\n");

    // Assert.
    expect(headings).toEqual([
      { level: 1, title: "One", line: 1 },
      { level: 2, title: "Two", line: 3 },
    ]);
  });

  it("ignores a # inside a code fence", () => {
    // Arrange and act.
    const headings = headingsOf("```\n# not a heading\n```\n# Real\n");

    // Assert.
    expect(headings).toEqual([{ level: 1, title: "Real", line: 4 }]);
  });
});

describe("MarkdownOutline", () => {
  it("navigates to a heading's line when clicked", () => {
    // Arrange.
    const goToLine = vi.fn();
    render(<MarkdownOutline text={"# Intro\n\n## Details\n"} goToLine={goToLine} />);

    // Act.
    fireEvent.click(screen.getByText(/Details/));

    // Assert.
    expect(goToLine).toHaveBeenCalledWith(3);
  });

  it("renders nothing for a document without headings", () => {
    // Arrange and act.
    const rendered = render(<MarkdownOutline text={"plain text\n"} goToLine={() => {}} />);

    // Assert.
    expect(rendered.container.innerHTML).toBe("");
  });
});
