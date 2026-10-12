import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { tags } from "@lezer/highlight";
import { editorHighlight } from "@client/editors/editorHighlight";
import { MarkdownEditorPanel, MarkdownOutline, headingsOf } from "./MarkdownEditorPanel";

// The panel reads its file through the shared stream; here the stream is a fixed text, so the
// real CodeMirror view mounts over it.
vi.mock("@client/editors/useEditorText", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@client/editors/useEditorText")>();
  return {
    ...actual,
    useEditorText: () => ({
      model: { text: "# Title\n\nbody\n", revision: 1, loaded: true, contentMime: "editor/markdown" },
      loading: false,
      failed: false,
      save: () => Promise.resolve(""),
    }),
  };
});

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

describe("MarkdownEditorPanel", () => {
  it("colours a heading through the family's one colour scheme (json-editor Q5)", () => {
    // Arrange and act.
    const rendered = render(<MarkdownEditorPanel projectId={new Uint8Array(16)} entryId={new Uint8Array(16)} path={["notes.md"]} />);

    // Assert.
    const headingClass = editorHighlight.style([tags.heading1])!.split(" ")[0];
    const coloured = [...rendered.container.querySelectorAll(`.cm-content .${headingClass}`)].map((span) => span.textContent).join("");
    expect(coloured).toContain("Title");
  });
});
