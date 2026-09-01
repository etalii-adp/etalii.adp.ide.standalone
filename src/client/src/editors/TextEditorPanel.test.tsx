import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { DeltaSchema } from "@client/generated/deltas_pb";
import { isTabDirty, markTabDirty } from "@client/shell/panels/dirtyTabs";
import { applyEditorDelta, emptyEditorText, type EditorTextModel } from "./useEditorText";
import { TextEditorPanel } from "./TextEditorPanel";

// The panel's logic - dirty state, conflict presentation, save flow - is what is under test;
// CodeMirror's own rendering is not, so the base editor becomes a plain textarea with the
// same contract, plus a button standing in for the Ctrl+S gesture.
vi.mock("./BaseTextEditor", () => ({
  BaseTextEditor: ({ content, onChange, onSave }: { content: string; onChange: (text: string) => void; onSave?: () => void }) => (
    <div>
      <textarea data-testid="base-text-editor" value={content} onChange={(event) => onChange(event.target.value)} />
      <button data-testid="save-gesture" type="button" onClick={() => onSave?.()} />
    </div>
  ),
  scrollToLine: () => {},
}));

const streamState: { model: EditorTextModel; loading: boolean; failed: boolean } = {
  model: { text: "hello\n", revision: 1, loaded: true },
  loading: false,
  failed: false,
};

vi.mock("./useEditorText", async (importOriginal) => {
  const actual = await importOriginal<typeof import("./useEditorText")>();
  return { ...actual, useEditorText: () => streamState };
});

describe("TextEditorPanel", () => {
  afterEach(() => {
    streamState.model = { text: "hello\n", revision: 1, loaded: true };
    streamState.loading = false;
    streamState.failed = false;
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
    streamState.model = { text: "theirs\n", revision: 2, loaded: true };
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
    streamState.model = { text: "theirs\n", revision: 2, loaded: true };
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

  it("keeps the dirty state and shows the reason when a save fails", async () => {
    // Arrange.
    render(<TextEditorPanel {...props} onSave={() => Promise.resolve("disk is full")} />);
    fireEvent.change(screen.getByTestId("base-text-editor"), { target: { value: "x" } });

    // Act.
    fireEvent.click(screen.getByTestId("save-gesture"));

    // Assert.
    await screen.findByText("disk is full");
    expect(screen.getByTestId("dirty-indicator").textContent).toContain("Unsaved");
  });
});

describe("applyEditorDelta", () => {
  it("takes the text of an arriving content element", () => {
    // Arrange.
    const delta = create(DeltaSchema, {
      action: {
        case: "add",
        value: { elements: [{ id: { value: "content" }, payload: { value: new TextEncoder().encode("abc") } }] },
      },
    });

    // Act.
    const model = applyEditorDelta(emptyEditorText, delta);

    // Assert.
    expect(model.text).toBe("abc");
    expect(model.revision).toBe(1);
    expect(model.loaded).toBe(true);
  });

  it("ignores the remove half of a replacement pair", () => {
    // Arrange.
    const remove = create(DeltaSchema, { action: { case: "remove", value: { elementIds: [{ value: "content" }] } } });
    const current: EditorTextModel = { text: "abc", revision: 1, loaded: true };

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
