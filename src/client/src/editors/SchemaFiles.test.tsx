import { afterEach, describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { CanvasStatusContext, useCanvasStatusReporter, type CanvasStatusReporter } from "@client/canvas/library/surface/canvasStatus";
import type { EditorTextResult } from "./useEditorText";
import { SchemaFiles, type SchemaFile } from "./SchemaFiles";

/**
 * The stream is stubbed: each path's state is set here, and a re-render of the parent is what a
 * delta arriving on the real stream causes.
 */
const stream = new Map<string, Partial<EditorTextResult>>();
const opened: string[] = [];
const reportersSeen: (CanvasStatusReporter | null)[] = [];

vi.mock("./useEditorText", async (importOriginal) => {
  const actual = await importOriginal<typeof import("./useEditorText")>();
  return {
    ...actual,
    useEditorText: (_projectId: Uint8Array, path: readonly string[]): EditorTextResult => {
      const key = path.join("/");
      opened.push(key);
      reportersSeen.push(useCanvasStatusReporter());
      const state = stream.get(key) ?? {};
      return {
        model: state.model ?? actual.emptyEditorText,
        loading: state.loading ?? true,
        failed: state.failed ?? false,
        save: () => Promise.resolve(""),
      };
    },
  };
});

const loaded = (text: string, revision: number): Partial<EditorTextResult> => ({
  model: { text, revision, loaded: true, contentMime: "editor/plain" },
  loading: false,
  failed: false,
});

const projectId = new Uint8Array(16);

function mount(paths: string[][]) {
  const onChange = vi.fn<(files: ReadonlyMap<string, SchemaFile>) => void>();
  const rendered = render(<SchemaFiles projectId={projectId} paths={paths} onChange={onChange} />);
  const latest = () => Object.fromEntries(onChange.mock.lastCall?.[0] ?? new Map());
  const rerender = (next: string[][]) => rendered.rerender(<SchemaFiles projectId={projectId} paths={next} onChange={onChange} />);
  return { rendered, onChange, latest, rerender };
}

describe("SchemaFiles", () => {
  afterEach(() => {
    stream.clear();
    opened.length = 0;
    reportersSeen.length = 0;
  });

  it("reports a file as loading, then with its text when it arrives", () => {
    // Arrange.
    const { latest, rerender } = mount([["schemas", "app.schema.json"]]);
    expect(latest()).toEqual({ "schemas/app.schema.json": { state: "loading" } });

    // Act.
    stream.set("schemas/app.schema.json", loaded("{}", 1));
    rerender([["schemas", "app.schema.json"]]);

    // Assert.
    expect(latest()).toEqual({ "schemas/app.schema.json": { state: "loaded", text: "{}", revision: 1 } });
  });

  it("reports a new revision when the file changes on disk", () => {
    // Arrange.
    stream.set("app.schema.json", loaded("{}", 1));
    const { latest, rerender } = mount([["app.schema.json"]]);

    // Act.
    stream.set("app.schema.json", loaded('{"type":"object"}', 2));
    rerender([["app.schema.json"]]);

    // Assert.
    expect(latest()).toEqual({ "app.schema.json": { state: "loaded", text: '{"type":"object"}', revision: 2 } });
  });

  it("reports a file that cannot be opened with the reason", () => {
    // Arrange and act.
    stream.set("missing.schema.json", { failed: true, loading: false });
    const { latest } = mount([["missing.schema.json"]]);

    // Assert.
    expect(latest()).toEqual({ "missing.schema.json": { state: "failed", reason: "was not found in the project" } });
  });

  it("opens a path added later, keeping the ones it has", () => {
    // Arrange.
    stream.set("a.json", loaded("{}", 1));
    stream.set("b.json", loaded("[]", 1));
    const { latest, rerender } = mount([["a.json"]]);

    // Act.
    rerender([["a.json"], ["b.json"]]);

    // Assert.
    expect(latest()).toEqual({
      "a.json": { state: "loaded", text: "{}", revision: 1 },
      "b.json": { state: "loaded", text: "[]", revision: 1 },
    });
  });

  it("forgets a path that is removed", () => {
    // Arrange.
    stream.set("a.json", loaded("{}", 1));
    stream.set("b.json", loaded("[]", 1));
    const { latest, rerender } = mount([["a.json"], ["b.json"]]);

    // Act.
    rerender([["b.json"]]);

    // Assert.
    expect(latest()).toEqual({ "b.json": { state: "loaded", text: "[]", revision: 1 } });
  });

  it("opens a path named twice once", () => {
    // Arrange and act.
    mount([["a.json"], ["a.json"]]);

    // Assert.
    expect(new Set(opened)).toEqual(new Set(["a.json"]));
    expect(opened.length).toBe(1);
  });

  it("never lets a schema's stream speak for the tab it sits in", () => {
    // Arrange.
    const reporter: CanvasStatusReporter = { report: vi.fn(), forget: vi.fn() };

    // Act.
    render(
      <CanvasStatusContext.Provider value={reporter}>
        <SchemaFiles projectId={projectId} paths={[["a.json"]]} onChange={() => {}} />
      </CanvasStatusContext.Provider>,
    );

    // Assert.
    expect(reportersSeen).toEqual([null]);
  });
});
