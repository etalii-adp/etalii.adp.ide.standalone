import { describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { EditorSelection } from "@codemirror/state";
import { EditorView, runScopeHandlers } from "@codemirror/view";
import { foldAll, foldedRanges, unfoldAll } from "@codemirror/language";
import { parseDocument } from "yaml";
import { BaseTextEditor } from "@client/editors/BaseTextEditor";
import { indentationAfter, indentationWidthOf, yamlKeys } from "./yamlKeys";
import { yamlGrammar } from "./yamlGrammar";
import { Text } from "@codemirror/state";

/**
 * The keyboard over the real editor, with the shared editor's own keymap beneath it: what is
 * asserted is the text a key leaves, on documents indented by two and by four (Requirement 3.5,
 * 3.6 and 3.7).
 */
function mount(text: string, caret: number) {
  const onChange = vi.fn();
  const rendered = render(<BaseTextEditor content={text} onChange={onChange} extensions={[yamlGrammar, yamlKeys]} />);
  const view = EditorView.findFromDOM(rendered.container.querySelector<HTMLElement>("[data-testid=base-text-editor]")!)!;
  view.dispatch({ selection: EditorSelection.cursor(caret) });
  return { view, onChange };
}

function press(view: EditorView, key: string, shiftKey = false) {
  return runScopeHandlers(view, new KeyboardEvent("keydown", { key, shiftKey }), "editor");
}

const byTwo = "service:\n  name: web\n  ports:\n    - port: 80\n";
const byFour = "service:\n    name: web\n    ports:\n        - port: 80\n";

describe("the indentation width", () => {
  it("is read from the document, and two when it cannot be", () => {
    expect(indentationWidthOf(Text.of(byTwo.split("\n")))).toBe(2);
    expect(indentationWidthOf(Text.of(byFour.split("\n")))).toBe(4);
    expect(indentationWidthOf(Text.of(["name: web", "port: 80"]))).toBe(2);
    // A comment indented by one is not the document's indentation.
    expect(indentationWidthOf(Text.of(["a:", " # note", "    b: 1"]))).toBe(4);
  });
});

describe("Tab and Shift+Tab", () => {
  it.each([
    ["by two", byTwo, "  "],
    ["by four", byFour, "    "],
  ])("insert spaces to the next stop, never a tab character, in a document indented %s", (_name, text, unit) => {
    // Arrange: the caret at the start of an empty last line.
    const { view } = mount(text, text.length);

    // Act.
    expect(press(view, "Tab")).toBe(true);

    // Assert.
    expect(view.state.doc.toString()).toBe(text + unit);
    expect(view.state.doc.toString()).not.toContain("\t");
  });

  it("goes to the next stop from the middle of a level, not a whole level further", () => {
    const { view } = mount("a:\n    b: 1\n \n", "a:\n    b: 1\n ".length);
    press(view, "Tab");
    expect(view.state.doc.line(3).text).toBe("    ");
  });

  it("indents and outdents selected lines by the document's width", () => {
    // Arrange.
    const { view } = mount(byFour, 0);
    view.dispatch({ selection: EditorSelection.range(byFour.indexOf("name"), byFour.indexOf("web")) });

    // Act and assert.
    press(view, "Tab");
    expect(view.state.doc.line(2).text).toBe("        name: web");
    press(view, "Tab", true);
    press(view, "Tab", true);
    expect(view.state.doc.line(2).text).toBe("name: web");
    expect(view.state.doc.toString()).not.toContain("\t");
  });
});

describe("Enter", () => {
  it.each([
    ["by two", byTwo, 2],
    ["by four", byFour, 4],
  ])("continues at the line's indentation, and one level deeper after a key that opens a block, %s", (_name, text, width) => {
    // After "name: web" the next key is a sibling.
    const sibling = mount(text, text.indexOf("web") + 3);
    press(sibling.view, "Enter");
    expect(sibling.view.state.doc.line(3).text).toBe(" ".repeat(width));

    // After "ports:" the next line is inside it.
    const ports = text.indexOf("ports:") + "ports:".length;
    const deeper = mount(text, ports);
    press(deeper.view, "Enter");
    expect(deeper.view.state.doc.line(4).text).toBe(" ".repeat(width * 2));
  });

  it("aligns under an item's first key, and goes deeper after a lone dash or a block scalar's header", () => {
    expect(indentationAfter("  - port: 80", 2)).toBe(4);
    expect(indentationAfter("  - spec:", 2)).toBe(6);
    expect(indentationAfter("  -", 2)).toBe(4);
    expect(indentationAfter("note: |", 2)).toBe(2);
    expect(indentationAfter("  note: >-  # folded", 2)).toBe(4);
    expect(indentationAfter("url: http://x  # a colon inside is not an opening", 2)).toBe(0);
  });

  it("leaves YAML that still reads as the same structure plus a new key", () => {
    // Arrange.
    const text = "service:\n  ports:\n";
    const { view } = mount(text, "service:\n  ports:".length);

    // Act.
    press(view, "Enter");
    view.dispatch(view.state.replaceSelection("- 80"));

    // Assert.
    expect(parseDocument(view.state.doc.toString()).toJS()).toEqual({ service: { ports: [80] } });
  });
});

describe("folding", () => {
  it("folds a mapping, a sequence and a block scalar, and changes nothing in the text", () => {
    // Arrange.
    const text = "map:\n  a: 1\n  b: 2\nlist:\n  - 1\n  - 2\nnote: |\n  one\n  two\n";
    const { view, onChange } = mount(text, 0);

    // Act.
    foldAll(view);
    const folded = foldedRanges(view.state).size;
    unfoldAll(view);

    // Assert.
    expect(folded).toBe(3);
    expect(view.state.doc.toString()).toBe(text);
    expect(onChange).not.toHaveBeenCalled();
  });
});
