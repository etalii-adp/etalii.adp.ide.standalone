import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { DeltaSchema } from "@client/generated/deltas_pb";
import { isTabDirty, markTabDirty } from "@client/shell/panels/dirtyTabs";
import { applyEditorDelta, emptyEditorText, type EditorTextModel } from "./useEditorText";
import { TextEditorPanel } from "./TextEditorPanel";
import { CanvasFrame } from "@client/canvas/library/surface/CanvasFrame";

// The panel's logic - dirty state, conflict presentation, save flow - is what is under test;
// CodeMirror's own rendering is not, so the base editor becomes a plain textarea with the
// same contract, plus a button standing in for the Ctrl+S gesture.
const scrollToLine = vi.fn();

vi.mock("./BaseTextEditor", () => ({
  BaseTextEditor: ({ content, onChange, onSave }: { content: string; onChange: (text: string) => void; onSave?: () => void }) => (
    <div>
      <textarea data-testid="base-text-editor" value={content} onChange={(event) => onChange(event.target.value)} />
      <button data-testid="save-gesture" type="button" onClick={() => onSave?.()} />
    </div>
  ),
  scrollToLine: (host: HTMLElement, line: number) => scrollToLine(host, line),
}));

const model = (text: string, revision: number): EditorTextModel => ({ text, revision, loaded: true, contentMime: "editor/plain" });

const streamState: {
  model: EditorTextModel;
  loading: boolean;
  failed: boolean;
  save: (content: string) => Promise<string>;
} = {
  model: model("hello\n", 1),
  loading: false,
  failed: false,
  save: () => Promise.resolve(""),
};

vi.mock("./useEditorText", async (importOriginal) => {
  const actual = await importOriginal<typeof import("./useEditorText")>();
  return { ...actual, useEditorText: () => streamState };
});

describe("TextEditorPanel", () => {
  afterEach(() => {
    streamState.model = model("hello\n", 1);
    streamState.loading = false;
    streamState.failed = false;
    streamState.save = () => Promise.resolve("");
    scrollToLine.mockClear();
  });

  const props = { projectId: new Uint8Array(16), entryId: new Uint8Array(16), path: ["notes.txt"] };

  const editorValue = () => (screen.getByTestId("base-text-editor") as HTMLTextAreaElement).value;

  it("shows the streamed text and a clean status", () => {
    // Arrange and act.
    render(<TextEditorPanel {...props} />);

    // Assert.
    expect(editorValue()).toBe("hello\n");
    expect(screen.getByTestId("dirty-indicator").textContent).toBe("Saved");
  });

  it("opens a CRLF file clean - the editor's line-ending normalisation is not an edit", () => {
    // Arrange. The stream serves the file as it is on disk, CRLF included (this repository's
    // own house style), while CodeMirror holds every document LF-only and echoes the
    // normalised text back through onChange when the doc is first populated. Before the fix,
    // that echo compared LF against CRLF and every CRLF file opened with "Unsaved changes"
    // and a dirty tab - found when a readme screenshot of a freshly opened file showed the
    // dirty flag (documentation spec, task 9).
    streamState.model = model("line one\r\nline two\r\n", 1);
    render(<TextEditorPanel {...props} />);

    // Act. What CodeMirror does on mount: report the document it now holds, LF-only.
    fireEvent.change(screen.getByTestId("base-text-editor"), { target: { value: "line one\nline two\n" } });

    // Assert. Not an edit - nothing the user did, and nothing the save-side buffer (which
    // re-applies each line's own terminator) would write differently.
    expect(screen.getByTestId("dirty-indicator").textContent).toBe("Saved");
    expect(isTabDirty("notes.txt")).toBe(false);

    // Act, continued. A real edit still counts.
    fireEvent.change(screen.getByTestId("base-text-editor"), { target: { value: "line one edited\nline two\n" } });

    // Assert.
    expect(screen.getByTestId("dirty-indicator").textContent).toContain("Unsaved");
  });

  it("marks unsaved edits, in the panel and in the tab registry (R6.5)", () => {
    // Arrange.
    render(<TextEditorPanel {...props} />);

    // Act.
    fireEvent.change(screen.getByTestId("base-text-editor"), { target: { value: "hello edited\n" } });

    // Assert.
    expect(screen.getByTestId("dirty-indicator").textContent).toContain("Unsaved");
    expect(isTabDirty("notes.txt")).toBe(true);
  });

  it("presents an external change as a conflict, never a silent discard (R6.4)", () => {
    // Arrange: an edit in flight...
    const rendered = render(<TextEditorPanel {...props} />);
    fireEvent.change(screen.getByTestId("base-text-editor"), { target: { value: "mine\n" } });

    // Act: ...and the disk changes underneath it.
    streamState.model = model("theirs\n", 2);
    rendered.rerender(<TextEditorPanel {...props} />);

    // Assert: both sides stay real until the user chooses.
    expect(screen.getByRole("alert").textContent).toContain("changed on disk");
    expect(editorValue()).toBe("mine\n");

    // Choosing the disk version replaces the text; nothing was overwritten silently.
    fireEvent.click(screen.getByText("Load the disk version"));
    expect(editorValue()).toBe("theirs\n");
  });

  it("keeps local edits when the user says so", () => {
    // Arrange.
    const rendered = render(<TextEditorPanel {...props} />);
    fireEvent.change(screen.getByTestId("base-text-editor"), { target: { value: "mine\n" } });
    streamState.model = model("theirs\n", 2);
    rendered.rerender(<TextEditorPanel {...props} />);

    // Act.
    fireEvent.click(screen.getByText("Keep my changes"));

    // Assert.
    expect(editorValue()).toBe("mine\n");
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("saves through the provided pipeline and returns to clean", async () => {
    // Arrange.
    const saved: string[] = [];
    const onSave = (content: string) => {
      saved.push(content);
      return Promise.resolve("");
    };
    render(<TextEditorPanel {...props} onSave={onSave} />);
    fireEvent.change(screen.getByTestId("base-text-editor"), { target: { value: "to keep\n" } });
    expect(screen.getByTestId("dirty-indicator").textContent).toContain("Unsaved");

    // Act: the save gesture (Ctrl+S in the real component).
    fireEvent.click(screen.getByTestId("save-gesture"));
    await screen.findByText("Saved");

    // Assert: the content went through the pipeline, and the panel is clean again.
    expect(saved).toEqual(["to keep\n"]);
    expect(isTabDirty("notes.txt")).toBe(false);
  });

  it("keeps the dirty state and shows the reason when a save fails, on the library's one refusal line", async () => {
    // Arrange: client-centralization Requirement 2.5 - a failed save reaches the same surface every
    // other refusal does, the line the library's frame draws around the panel.
    const { container } = render(
      <CanvasFrame>
        <TextEditorPanel {...props} onSave={() => Promise.resolve("disk is full")} />
      </CanvasFrame>,
    );
    fireEvent.change(screen.getByTestId("base-text-editor"), { target: { value: "x" } });

    // Act.
    fireEvent.click(screen.getByTestId("save-gesture"));

    // Assert: shown once, by the library, and the edit is kept.
    const line = await screen.findByText("disk is full");
    expect(line.getAttribute("data-canvas-surface")).toBe("refusal");
    expect(screen.getAllByText("disk is full")).toHaveLength(1);
    expect(container.querySelector(".text-editor-save-error")).toBeNull();
    expect(screen.getByTestId("dirty-indicator").textContent).toContain("Unsaved");
  });

  it("saves through the stream's own pipeline when the module passes no override (R6.2)", async () => {
    // Arrange: the hook's save is the default - the SaveText wire, in production.
    const saved: string[] = [];
    streamState.save = (content) => {
      saved.push(content);
      return Promise.resolve("");
    };
    render(<TextEditorPanel {...props} />);
    fireEvent.change(screen.getByTestId("base-text-editor"), { target: { value: "wired\n" } });

    // Act.
    fireEvent.click(screen.getByTestId("save-gesture"));
    await screen.findByText("Saved");

    // Assert.
    expect(saved).toEqual(["wired\n"]);
  });

  it("scrolls to a problem's line once the text is loaded (R8.1)", () => {
    // Arrange and act: "hello\n" is two editor lines, so line 2 exists.
    render(<TextEditorPanel {...props} initialLine={2} />);

    // Assert.
    expect(scrollToLine).toHaveBeenCalledWith(expect.anything(), 2);
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("says a stale line is gone rather than guessing a nearby one (R8.3)", () => {
    // Arrange and act: the problem named line 9 of a file that now has 2 lines.
    render(<TextEditorPanel {...props} initialLine={9} />);

    // Assert: the file opened, the line did not, and nothing scrolled anywhere.
    expect(screen.getByRole("status").textContent).toContain("Line 9 is not in this file any more");
    expect(scrollToLine).not.toHaveBeenCalled();
    expect(editorValue()).toBe("hello\n");
  });
});

describe("applyEditorDelta", () => {
  it("takes the text of an arriving content element", () => {
    // Arrange.
    const delta = create(DeltaSchema, {
      action: {
        case: "add",
        value: { elements: [{ id: { value: "content" }, type: "editor/markdown", payload: { value: new TextEncoder().encode("abc") } }] },
      },
    });

    // Act.
    const folded = applyEditorDelta(emptyEditorText, delta);

    // Assert: the element's own type names the resolved module - what an "Open as text"
    // tab mounts its canvas from (R5.2).
    expect(folded.text).toBe("abc");
    expect(folded.revision).toBe(1);
    expect(folded.loaded).toBe(true);
    expect(folded.contentMime).toBe("editor/markdown");
  });

  it("ignores the remove half of a replacement pair", () => {
    // Arrange.
    const remove = create(DeltaSchema, { action: { case: "remove", value: { elementIds: [{ value: "content" }] } } });
    const current: EditorTextModel = { text: "abc", revision: 1, loaded: true, contentMime: "editor/plain" };

    // Act and assert.
    expect(applyEditorDelta(current, remove)).toBe(current);
  });
});

describe("dirtyTabs", () => {
  it("tracks and clears dirtiness by key", () => {
    // Arrange, act and assert.
    markTabDirty("a", true);
    expect(isTabDirty("a")).toBe(true);
    markTabDirty("a", false);
    expect(isTabDirty("a")).toBe(false);
  });
});
