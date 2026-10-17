import { describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { EditorView } from "@codemirror/view";
import { foldAll, unfoldAll, foldedRanges, forceParsing } from "@codemirror/language";
import { markdown } from "@codemirror/lang-markdown";
import { tags } from "@lezer/highlight";
import { BaseTextEditor } from "./BaseTextEditor";
import { editorHighlight } from "./editorHighlight";

function mount(content: string, extensions = [markdown()]) {
  const onChange = vi.fn();
  const rendered = render(<BaseTextEditor content={content} onChange={onChange} extensions={extensions} />);
  const view = EditorView.findFromDOM(rendered.getByTestId("base-text-editor"))!;
  // CodeMirror parses for a fixed time slice when the view is made and finishes later, when the
  // browser is idle; a fold or a tree query on a slow runner can come first (spec 025, T281, T286),
  // so the parse is completed before anything is asked of the tree.
  forceParsing(view, view.state.doc.length, 10_000);
  return { rendered, view, onChange };
}

describe("the shared editor surface", () => {
  it("colours what the grammar parses through the one shared scheme", () => {
    // Arrange and act.
    const { rendered } = mount("# Title\n\nbody\n");

    // Assert. The class is the scheme's own for a heading, so a module's private style would not pass.
    const headingClass = editorHighlight.style([tags.heading1])!;
    expect(headingClass).toBeTruthy();
    const heading = rendered.container.querySelector(".cm-line")!;
    const coloured = [...heading.querySelectorAll(`.${headingClass.split(" ")[0]}`)].map((span) => span.textContent).join("");
    expect(coloured).toContain("Title");
  });

  it("marks the partner of the bracket beside the caret", () => {
    // Arrange.
    const { rendered, view } = mount("(one [two])", []);

    // Act.
    view.dispatch({ selection: { anchor: 0 } });

    // Assert.
    const matched = [...rendered.container.querySelectorAll(".cm-matchingBracket")].map((element) => element.textContent);
    expect(matched).toEqual(["(", ")"]);
  });

  it("folds and unfolds without changing a character of the text", () => {
    // Arrange.
    const text = "# One\n\nfirst\nsection\n\n# Two\n\nsecond\n";
    const { view, onChange } = mount(text);

    // Act.
    foldAll(view);
    const folded = foldedRanges(view.state).size;
    unfoldAll(view);

    // Assert.
    expect(folded).toBeGreaterThan(0);
    expect(view.state.doc.toString()).toBe(text);
    expect(onChange).not.toHaveBeenCalled();
  });

  it("offers folding from the gutter", () => {
    // Arrange and act.
    const { rendered } = mount("# One\n\nfirst\n\n# Two\n");

    // Assert.
    expect(rendered.container.querySelector(".cm-foldGutter")).not.toBeNull();
  });
});
