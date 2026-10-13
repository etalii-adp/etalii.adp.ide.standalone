import { afterEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, waitFor } from "@testing-library/react";
import { EditorView } from "@codemirror/view";
import { forEachDiagnostic } from "@codemirror/lint";
import type { EditorTextResult } from "@client/editors/useEditorText";
import { YamlEditorPanel } from "./YamlEditorPanel";

/**
 * The panel over the real editor and the real analysis, with the stream stubbed per path: the
 * document's own and each schema's (Requirement 5). A re-render is what a delta arriving on the
 * real stream causes.
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

const emptyRects = () => Object.assign([], { item: () => null }) as unknown as DOMRectList;
Range.prototype.getClientRects ??= emptyRects;
Range.prototype.getBoundingClientRect ??= () => new DOMRect();

let revision = 0;
function serve(path: string, text: string) {
  stream.set(path, { model: { text, revision: ++revision, loaded: true, contentMime: "editor/yaml" }, loading: false, failed: false });
}

const schema = (title: string, required: string[]) => JSON.stringify({ title, type: "object", required, properties: Object.fromEntries(required.map((key) => [key, { type: "string" }])) });

const text = [
  "# yaml-language-server: $schema=web.schema.json",
  "image: nginx",
  "---",
  "# yaml-language-server: $schema=worker.schema.json",
  "image: worker",
  "",
].join("\n");

const props = { projectId: new Uint8Array(16), entryId: new Uint8Array(16), path: ["app", "services.yaml"] };

function view(container: HTMLElement): EditorView {
  return EditorView.findFromDOM(container.querySelector<HTMLElement>(".cm-editor")!)!;
}

/** Each finding as its line and the key its message names. */
function findings(container: HTMLElement): string[] {
  const state = view(container).state;
  const found: string[] = [];
  forEachDiagnostic(state, (diagnostic) => found.push(`${state.doc.lineAt(diagnostic.from).number} ${diagnostic.severity} ${/"(\w+)"/.exec(diagnostic.message)?.[1] ?? diagnostic.message}`));
  return found.sort();
}

const patience = { timeout: 10_000 };

// The first analysis loads the service, which takes a while in a cold test run.
describe("YamlEditorPanel", { timeout: 30_000 }, () => {
  afterEach(() => stream.clear());

  it("validates each document against its own schema, once the schema arrives after the document", async () => {
    // Arrange: the document is there, its schemas are not yet.
    serve("app/services.yaml", text);
    const rendered = render(<YamlEditorPanel {...props} />);
    await waitFor(() => expect(screen.getByTestId("analysis-status").textContent).toBe("Reading the schema"), patience);
    expect(findings(rendered.container)).toEqual([]);

    // Act: both arrive.
    serve("app/web.schema.json", schema("Web", ["image", "port"]));
    serve("app/worker.schema.json", schema("Worker", ["image", "queue"]));
    rendered.rerender(<YamlEditorPanel {...props} />);

    // Assert: each document is missing its own schema's key, on its own line.
    await waitFor(() => expect(findings(rendered.container)).toEqual(["2 error port", "5 error queue"]), patience);
    expect(screen.getByTestId("analysis-status").textContent).toBe("Web, Worker");
  });

  it("validates again when a schema changes on disk, with no reopening", async () => {
    // Arrange.
    serve("app/services.yaml", text);
    serve("app/web.schema.json", schema("Web", ["image", "port"]));
    serve("app/worker.schema.json", schema("Worker", ["image", "queue"]));
    const rendered = render(<YamlEditorPanel {...props} />);
    await waitFor(() => expect(findings(rendered.container)).toEqual(["2 error port", "5 error queue"]), patience);

    // Act: the web schema no longer asks for a port.
    serve("app/web.schema.json", schema("Web", ["image"]));
    await act(async () => {
      rendered.rerender(<YamlEditorPanel {...props} />);
    });

    // Assert.
    await waitFor(() => expect(findings(rendered.container)).toEqual(["5 error queue"]), patience);
  });

  it("says No schema for a document that names none", async () => {
    serve("app/services.yaml", "image: nginx\n");
    render(<YamlEditorPanel {...props} />);
    await waitFor(() => expect(screen.getByTestId("analysis-status").textContent).toBe("No schema"), patience);
  });
});
