import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import type { EditorTextModel } from "./useEditorText";
import { ResolvedTextEditorPanel } from "./ResolvedTextEditorPanel";

// What is under test is the dispatch: which canvas an "Open as text" tab mounts once the
// stream names the editor. The stream, the shared panel and the registry are all stand-ins.
const streamState: { model: EditorTextModel; loading: boolean; failed: boolean; save: () => Promise<string> } = {
  model: { text: "", revision: 0, loaded: false, contentMime: "" },
  loading: true,
  failed: false,
  save: () => Promise.resolve(""),
};

vi.mock("./useEditorText", async (importOriginal) => {
  const actual = await importOriginal<typeof import("./useEditorText")>();
  return { ...actual, useEditorText: () => streamState };
});

vi.mock("./TextEditorPanel", () => ({
  TextEditorPanel: ({ editorId }: { editorId?: string }) => <div data-testid="shared-panel">{editorId}</div>,
}));

vi.mock("@client/shell/panels/diagramCanvases", () => ({
  canvasFor: (mimeType: string) =>
    mimeType === "editor/markdown"
      ? {
          matches: () => true,
          Canvas: ({ editorId }: { editorId?: string }) => <div data-testid="markdown-canvas">{editorId}</div>,
        }
      : undefined,
}));

const props = { projectId: new Uint8Array(16), entryId: new Uint8Array(0), path: ["notes.md"] };

describe("ResolvedTextEditorPanel", () => {
  it("shows the shared panel while the stream has not yet named the editor", () => {
    // Arrange and act.
    streamState.model = { text: "", revision: 0, loaded: false, contentMime: "" };
    render(<ResolvedTextEditorPanel {...props} />);

    // Assert: the forced resolution is already on the stream it would open.
    expect(screen.getByTestId("shared-panel").textContent).toBe("*");
  });

  it("mounts the named module's own canvas once the stream answers (R5.2)", () => {
    // Arrange: the content element's type names markdown - the backend's resolution.
    streamState.model = { text: "# hi\n", revision: 1, loaded: true, contentMime: "editor/markdown" };

    // Act.
    render(<ResolvedTextEditorPanel {...props} />);

    // Assert: markdown's canvas, chrome and all - not a lowest-common-denominator view -
    // and the forced resolution carried onto the canvas's own stream.
    expect(screen.getByTestId("markdown-canvas").textContent).toBe("*");
    expect(screen.queryByTestId("shared-panel")).toBeNull();
  });

  it("falls back to the shared panel for a module with no client half", () => {
    // Arrange: a backend-only editor module - resolvable, streamable, not drawable.
    streamState.model = { text: "x", revision: 1, loaded: true, contentMime: "editor/exotic" };

    // Act.
    render(<ResolvedTextEditorPanel {...props} />);

    // Assert: the text still opens; only the module chrome is missing.
    expect(screen.getByTestId("shared-panel")).toBeTruthy();
  });
});
