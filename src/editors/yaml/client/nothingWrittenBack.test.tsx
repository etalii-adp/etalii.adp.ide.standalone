import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { EditorView, runScopeHandlers } from "@codemirror/view";
import type { EditorTextResult } from "@client/editors/useEditorText";
import { YamlEditorPanel } from "./YamlEditorPanel";

/**
 * Nothing the module parses is written back (yaml-editor Requirement 8.2). Every example is opened
 * in the panel with its schema, analysed in full, and saved: the text in the editor is still the
 * file's, the tab is still clean, and the save gesture writes nothing. The backend's own example
 * tests hold the save path itself byte for byte.
 */
const examples = join(__dirname, "..", "examples");
const stream = new Map<string, Partial<EditorTextResult>>();
const save = vi.fn((_content: string) => Promise.resolve(""));

vi.mock("@client/editors/useEditorText", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@client/editors/useEditorText")>();
  return {
    ...actual,
    useEditorText: (_projectId: Uint8Array, path: readonly string[]): EditorTextResult => {
      const state = stream.get(path.join("/")) ?? {};
      return { model: state.model ?? actual.emptyEditorText, loading: state.loading ?? true, failed: state.failed ?? false, save };
    },
  };
});

const emptyRects = () => Object.assign([], { item: () => null }) as unknown as DOMRectList;
Range.prototype.getClientRects ??= emptyRects;
Range.prototype.getBoundingClientRect ??= () => new DOMRect();

function serve(name: string) {
  const text = readFileSync(join(examples, name), "utf8");
  stream.set(`examples/${name}`, { model: { text, revision: 1, loaded: true, contentMime: "editor/yaml" }, loading: false, failed: false });
  return text;
}

const yamlExamples = readdirSync(examples).filter((name) => name.endsWith(".yaml"));

describe("nothing the module parses is written back", { timeout: 30_000 }, () => {
  afterEach(() => {
    stream.clear();
    save.mockClear();
  });

  it("has the examples it walks", () => {
    expect(yamlExamples.length).toBeGreaterThanOrEqual(6);
  });

  it.each(yamlExamples)("leaves %s exactly as it is after a full analysis and a save", async (name) => {
    // Arrange.
    const text = serve(name);
    serve("service.schema.json");
    const rendered = render(<YamlEditorPanel projectId={new Uint8Array(16)} entryId={new Uint8Array(16)} path={["examples", name]} />);
    const view = EditorView.findFromDOM(rendered.container.querySelector<HTMLElement>(".cm-editor")!)!;

    // Act: the analysis has answered once its status is settled, then the save gesture.
    await waitFor(() => expect(screen.getByTestId("analysis-status").textContent).toMatch(/^(No schema|Service)$/), { timeout: 20_000 });
    runScopeHandlers(view, new KeyboardEvent("keydown", { key: "s", ctrlKey: true }), "editor");

    // Assert: the editor holds the file's text as CodeMirror holds every text, LF-only.
    expect(view.state.doc.toString()).toBe(text.replace(/\r\n/g, "\n"));
    expect(screen.getByTestId("dirty-indicator").textContent).toBe("Saved");
    expect(save).not.toHaveBeenCalled();
  });
});

describe("what the module hands the editor", () => {
  it("is a completion's insert and nothing else: no export returns a text to put in the document", async () => {
    // Every export of the module, by what it gives. A new export fails here until its line is
    // added, with what it returns: the point is that none of them is a rewritten document.
    const exported = {
      "./yamlAnalysis": await import("./yamlAnalysis"),
      "./splitDocuments": await import("./splitDocuments"),
      "./completionWindow": await import("./completionWindow"),
      "./yamlReading": await import("./yamlReading"),
      "./yamlKeys": await import("./yamlKeys"),
      "./yamlScalarColour": await import("./yamlScalarColour"),
      "./yamlGrammar": await import("./yamlGrammar"),
      "./YamlEditorPanel": await import("./YamlEditorPanel"),
      "./register": await import("./register"),
    };
    const gives: Record<string, string> = {
      "./yamlAnalysis.largestChecked": "a number",
      "./yamlAnalysis.loadYamlEngine": "the service, loaded",
      "./yamlAnalysis.createYamlAnalysis": "findings, completions (whose insert is the one text), hover text and a status",
      "./yamlAnalysis.markdownToText": "hover and description text, shown beside the document",
      "./splitDocuments.splitDocuments": "offsets",
      "./completionWindow.windowAbove": "a number",
      "./completionWindow.completionWindow": "lines of the document for the service to read, never for the editor",
      "./yamlReading.maxAliasCount": "a number",
      "./yamlReading.readPart": "findings and offsets",
      "./yamlReading.readSchema": "a schema as data, handed to the service",
      "./yamlKeys.indentationWidthOf": "a number",
      "./yamlKeys.indentationAfter": "a number",
      "./yamlKeys.yamlKeys": "an extension: spaces typed with Tab and Enter",
      "./yamlScalarColour.scalarTagOf": "a highlight tag",
      "./yamlScalarColour.yamlScalarColour": "an extension: classes",
      "./yamlGrammar.yamlGrammar": "an extension: the grammar",
      "./YamlEditorPanel.YamlEditorPanel": "the panel",
      "./register.registrations": "the panel's registration",
    };
    const names = Object.entries(exported).flatMap(([file, module]) => Object.keys(module).map((name) => `${file}.${name}`));
    expect(names.sort()).toEqual(Object.keys(gives).sort());
  });
});
