import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { EditorView } from "@codemirror/view";
import { forceParsing } from "@codemirror/language";
import { tags, type Tag } from "@lezer/highlight";
import type { EditorTextResult } from "@client/editors/useEditorText";
import { editorHighlight } from "@client/editors/editorHighlight";
import { JsonEditorPanel } from "./JsonEditorPanel";

/**
 * The JSON editor mounted over a stubbed stream (json-editor Requirements 3 and 5.8): the real
 * CodeMirror view, the real analysis and the real service. Each path's state is set here, and a
 * re-render of the panel is what a delta arriving on the real stream causes. jsdom applies no CSS,
 * so colour is asserted as the highlight class each token carries; the classes are bound to the
 * nine colour tokens in one place, `editorHighlight`.
 */
const stream = new Map<string, Partial<EditorTextResult>>();

vi.mock("@client/editors/useEditorText", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@client/editors/useEditorText")>();
  return {
    ...actual,
    useEditorText: (_projectId: Uint8Array, path: readonly string[]): EditorTextResult => {
      const state = stream.get(path.join("/")) ?? {};
      return {
        model: state.model ?? actual.emptyEditorText,
        loading: state.loading ?? true,
        failed: state.failed ?? false,
        save: () => Promise.resolve(""),
      };
    },
  };
});

const loaded = (text: string, revision = 1): Partial<EditorTextResult> => ({
  model: { text, revision, loaded: true, contentMime: "editor/json" },
  loading: false,
  failed: false,
});

const ids = { projectId: new Uint8Array(16), entryId: new Uint8Array(16) };
const documentPath = ["settings", "app.json"];

function mount(text: string) {
  stream.set(documentPath.join("/"), loaded(text));
  const rendered = render(<JsonEditorPanel {...ids} path={documentPath} />);
  // CodeMirror parses for a fixed time slice when the view is made and finishes later, when the
  // browser is idle. On a cold, slow runner a test can look before the tree exists, as the YAML
  // editor's did (spec 025, T276, T281), so the parse is completed before anything is asserted.
  const view = EditorView.findFromDOM(rendered.getByTestId("base-text-editor"))!;
  forceParsing(view, view.state.doc.length, 10_000);
  return { rendered, rerender: () => rendered.rerender(<JsonEditorPanel {...ids} path={documentPath} />) };
}

/** The status line's counts; the panel leaves them out when there are none. */
const counts = () => screen.queryByTestId("finding-counts")?.textContent ?? "";

/** The highlight class a tag is drawn with. */
const classOf = (tag: Tag) => editorHighlight.style([tag])!.split(" ")[0];

/** The text of every span carrying `className`, in document order. */
function textsWith(container: HTMLElement, className: string): string[] {
  return [...container.querySelectorAll(`.cm-content .${className}`)].map((span) => span.textContent ?? "");
}

describe("JsonEditorPanel: colour", () => {
  afterEach(() => stream.clear());

  it("gives a property name and a string value beside it different classes (3.2)", () => {
    // Arrange and act.
    const { rendered } = mount('{ "name": "orders" }');

    // Assert.
    expect(classOf(tags.propertyName)).not.toBe(classOf(tags.string));
    expect(textsWith(rendered.container, classOf(tags.propertyName))).toEqual(['"name"']);
    expect(textsWith(rendered.container, classOf(tags.string))).toEqual(['"orders"']);
  });

  it("gives every kind of token a class (3.1)", () => {
    // Arrange and act.
    const { rendered } = mount('{ "a": [1, 2.5, true, false, null, "x"] }');
    const container = rendered.container;

    // Assert: names, strings, numbers, the three words and the punctuation.
    expect(textsWith(container, classOf(tags.propertyName))).toEqual(['"a"']);
    expect(textsWith(container, classOf(tags.string))).toEqual(['"x"']);
    expect(textsWith(container, classOf(tags.number))).toEqual(["1", "2.5"]);
    expect(textsWith(container, classOf(tags.bool))).toEqual(expect.arrayContaining(["true", "false", "null"]));
    expect(textsWith(container, classOf(tags.brace)).join("")).toContain("{");
    expect(textsWith(container, classOf(tags.squareBracket)).join("")).toContain("[");
  });

  it("keeps colour on what precedes a missing brace (3.4)", () => {
    // Arrange and act: the inner object is never closed.
    const { rendered } = mount('{ "outer": { "inner": 1, "label": "x", "more": [true] \n "after": 2 }');
    const container = rendered.container;

    // Assert.
    expect(textsWith(container, classOf(tags.propertyName))).toEqual(expect.arrayContaining(['"outer"', '"inner"', '"label"']));
    expect(textsWith(container, classOf(tags.string))).toContain('"x"');
    expect(textsWith(container, classOf(tags.number))).toContain("1");
  });
});

describe("JsonEditorPanel: the schema", () => {
  afterEach(() => stream.clear());

  const text = '{ "$schema": "./app.schema.json", "port": "x" }';

  it("shows findings once the schema arrives, and new ones when the schema changes on disk (5.8)", async () => {
    // Arrange: the document first, its schema still on its way.
    const { rerender } = mount(text);
    await waitFor(() => expect(screen.getByTestId("analysis-status").textContent).toBe("Reading ./app.schema.json"), { timeout: 3000 });
    expect(counts()).toBe("");

    // Act: the schema arrives.
    stream.set("settings/app.schema.json", loaded(JSON.stringify({ title: "Ports", properties: { port: { type: "integer" } } })));
    rerender();

    // Assert.
    await waitFor(() => expect(counts()).toBe("1 error"), { timeout: 3000 });
    expect(screen.getByTestId("analysis-status").textContent).toBe("Ports");

    // Act: the schema is saved elsewhere with a string port; the document is not reopened.
    stream.set("settings/app.schema.json", loaded(JSON.stringify({ title: "Ports", properties: { port: { type: "string" } } }), 2));
    rerender();

    // Assert.
    await waitFor(() => expect(counts()).toBe(""), { timeout: 3000 });
  });

  it("says once on the $schema line that the schema file is not in the project", async () => {
    // Arrange and act.
    stream.set("settings/app.schema.json", { failed: true, loading: false });
    mount(text);

    // Assert.
    await waitFor(() => expect(screen.getByTestId("analysis-status").textContent).toBe("./app.schema.json was not found in the project"), { timeout: 3000 });
    expect(counts()).toBe("");
  });
});
