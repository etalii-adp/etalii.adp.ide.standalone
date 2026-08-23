import { describe, expect, it, vi, beforeEach } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { create, toBinary } from "@bufbuild/protobuf";
import { ElementSchema } from "../../../generated/elements_pb";
import { MindmapNodePayloadSchema } from "../../../generated/mindmap_pb";
import { applyDelta, emptyModel, type MindmapModel } from "./mindmapModel";

const select = vi.fn();
const executeShortcut = vi.fn<(shortcut: { key: string }, source: { source: { value: { value: string } } }) => Promise<{ accepted: boolean; error: string }>>(async () => ({ accepted: true, error: "" }));
let currentModel: MindmapModel = emptyModel;
let currentFailed = false;
let currentSelection: unknown = null;

vi.mock("./useMindmapStream", () => ({
  useMindmapStream: () => ({ model: currentModel, loading: false, failed: currentFailed, reportView: vi.fn() }),
}));

vi.mock("../../context/ContextConnectionProvider", () => ({
  useContextConnection: () => ({ watchId: new Uint8Array(16), select, executeShortcut }),
  useContextSelection: () => ({ selection: currentSelection }),
}));

// Imported after the mocks so the component picks them up.
const { MindmapCanvas } = await import("./MindmapCanvas");

function node(id: string, text: string, x = 0, y = 0) {
  return create(ElementSchema, {
    id: { value: id },
    position: { x, y },
    type: "freeplane/mindmap+node",
    payload: {
      typeUrl: "type.googleapis.com/etalii.adp.mindmap.MindmapNodePayload",
      value: toBinary(MindmapNodePayloadSchema, create(MindmapNodePayloadSchema, { text, hasChildren: id === "root" })),
    },
  });
}

function seed(...elements: ReturnType<typeof node>[]): MindmapModel {
  return applyDelta(emptyModel, { action: { case: "add", value: { elements } } } as never);
}

const props = { projectId: new Uint8Array(16), entryId: new Uint8Array(16).fill(3), path: ["docs", "map.adp"] };

describe("MindmapCanvas", () => {
  beforeEach(() => {
    select.mockClear();
    executeShortcut.mockClear();
    currentModel = seed(node("root", "Root", 0, 0), node("a", "Alpha", 120, -20));
    currentSelection = null;
    currentFailed = false;
  });

  it("renders a node per streamed element", () => {
    const { container } = render(<MindmapCanvas {...props} />);

    expect(container.querySelectorAll(".mindmap-node")).toHaveLength(2);
    expect(container.textContent).toContain("Root");
    expect(container.textContent).toContain("Alpha");
  });

  it("says the diagram is no longer available, naming its path, when the stream failed for good", () => {
    // diagram-workspace-tabs Requirement 5.1: the tab remains and explains itself - never a
    // crash, a spinner, or a silently frozen canvas.
    currentFailed = true;

    const { container } = render(<MindmapCanvas {...props} />);

    expect(container.textContent).toContain("This diagram is no longer available at docs/map.adp.");
    expect(container.querySelectorAll(".mindmap-node")).toHaveLength(0);
  });

  it("reports a nested file->node selection when a node is clicked", () => {
    const { container } = render(<MindmapCanvas {...props} />);

    fireEvent.click(container.querySelectorAll(".mindmap-node")[1]);

    expect(select).toHaveBeenCalledTimes(1);
    const selection = select.mock.calls[0][0];
    expect(selection.id.source.value.value).toEqual(props.entryId); // the file
    expect(selection.detail.case).toBe("child");
    expect(selection.detail.value.id.source.value.value).toBe("a"); // the node
  });

  it("forwards a structural key against the focused node as a shortcut", () => {
    const { container } = render(<MindmapCanvas {...props} />);
    fireEvent.click(container.querySelectorAll(".mindmap-node")[0]); // focus Root

    fireEvent.keyDown(container.querySelector(".mindmap-canvas-surface")!, { key: "Insert" });

    expect(executeShortcut).toHaveBeenCalledTimes(1);
    const [shortcut, source] = executeShortcut.mock.calls[0];
    expect(shortcut.key).toBe("Insert");
    expect(source.source.value.value).toBe("root");
  });

  it("maps Tab to the child action's Insert key, not to an action", () => {
    const { container } = render(<MindmapCanvas {...props} />);
    fireEvent.click(container.querySelectorAll(".mindmap-node")[0]);

    fireEvent.keyDown(container.querySelector(".mindmap-canvas-surface")!, { key: "Tab" });

    expect(executeShortcut.mock.calls[0][0].key).toBe("Insert");
  });

  it("ignores a key that carries no structural meaning", () => {
    const { container } = render(<MindmapCanvas {...props} />);
    fireEvent.click(container.querySelectorAll(".mindmap-node")[0]);

    fireEvent.keyDown(container.querySelector(".mindmap-canvas-surface")!, { key: "x" });

    expect(executeShortcut).not.toHaveBeenCalled();
  });

  it("does nothing on a key when no node is focused", () => {
    const { container } = render(<MindmapCanvas {...props} />);

    fireEvent.keyDown(container.querySelector(".mindmap-canvas-surface")!, { key: "Delete" });

    expect(executeShortcut).not.toHaveBeenCalled();
  });

  it("follows a pushed selection to focus the matching node", () => {
    currentSelection = {
      id: { source: { case: "entryId", value: { value: props.entryId } } },
      detail: { case: "child", value: { id: { source: { case: "elementId", value: { value: "a" } } }, detail: { case: "none" } } },
    };

    const { container } = render(<MindmapCanvas {...props} />);

    const alpha = container.querySelectorAll(".mindmap-node")[1];
    expect(alpha.classList.contains("mindmap-node-focused")).toBe(true);
  });
});
