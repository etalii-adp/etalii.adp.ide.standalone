import { describe, expect, it, vi, beforeEach } from "vitest";
import { act, fireEvent, render, waitFor } from "@testing-library/react";
import { create, toBinary } from "@bufbuild/protobuf";
import { ElementSchema } from "../../../generated/elements_pb";
import { MindmapNodePayloadSchema } from "../../../generated/mindmap_pb";
import { applyDelta, emptyModel, type MindmapModel } from "./mindmapModel";
import { DiagramViewProvider, useDiagramViewControls, type DiagramViewControls } from "../DiagramViewContext";

const select = vi.fn();
const executeShortcut = vi.fn<(shortcut: { key: string }, source: { source: { value: { value: string } } }) => Promise<{ accepted: boolean; error: string }>>(async () => ({ accepted: true, error: "" }));
const moveElement = vi.fn(async () => "");
let currentReportView: ((viewport: unknown) => void) | null = null;
let currentModel: MindmapModel = emptyModel;
let currentFailed = false;
let currentSelection: unknown = null;

vi.mock("./useMindmapStream", () => ({
  useMindmapStream: () => ({ model: currentModel, loading: false, failed: currentFailed, reportView: (v: unknown) => currentReportView?.(v), moveElement }),
}));

vi.mock("../../context/ContextConnectionProvider", () => ({
  useContextConnection: () => ({ watchId: new Uint8Array(16), select, executeShortcut }),
  useContextSelection: () => ({ selection: currentSelection }),
}));

// Imported after the mocks so the component picks them up.
const { MindmapCanvas } = await import("./MindmapCanvas");

function node(id: string, text: string, x = 0, y = 0, parentId = "") {
  return create(ElementSchema, {
    id: { value: id },
    position: { x, y },
    type: "freeplane/mindmap+node",
    payload: {
      typeUrl: "type.googleapis.com/etalii.adp.mindmap.MindmapNodePayload",
      value: toBinary(MindmapNodePayloadSchema, create(MindmapNodePayloadSchema, { text, hasChildren: id === "root", parentId })),
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
    moveElement.mockClear();
    executeShortcut.mockClear();
    currentModel = seed(node("root", "Root", 0, 0), node("a", "Alpha", 120, -20, "root"));
    currentSelection = null;
    currentFailed = false;
  });

  it("renders a node per streamed element", () => {
    const { container } = render(<MindmapCanvas {...props} />);

    expect(container.querySelectorAll(".mindmap-node")).toHaveLength(2);
    expect(container.textContent).toContain("Root");
    expect(container.textContent).toContain("Alpha");
  });

  it("gives the surface the keyboard when a node is clicked", () => {
    // Found by the diagram-workspace-tabs manual pass: clicking an SVG child shape does not
    // reliably move DOM focus into the SVG, so every shortcut kept landing in the explorer.
    const { container } = render(<MindmapCanvas {...props} />);

    fireEvent.click(container.querySelectorAll(".mindmap-node")[1]);

    expect(document.activeElement).toBe(container.querySelector(".mindmap-canvas-surface"));
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
    // Empty asks the backend to fill in the full text chain; sending only the node's own
    // text was rejected as a partial path (found by the manual pass).
    expect(selection.detail.value.path.segments).toEqual([]);
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

  it("draws a connector from each child to its parent, under the nodes", () => {
    const { container } = render(<MindmapCanvas {...props} />);

    const edges = container.querySelectorAll(".mindmap-edge");
    expect(edges).toHaveLength(1); // one child, one line; the root has no parent to draw to
    const edge = edges[0];
    expect(edge.getAttribute("x1")).toBe("0"); // Root's centre
    expect(edge.getAttribute("x2")).toBe("120"); // Alpha's centre
  });

  it("clicking the empty canvas deselects", () => {
    const { container } = render(<MindmapCanvas {...props} />);
    fireEvent.click(container.querySelectorAll(".mindmap-node")[1]); // select Alpha first
    select.mockClear();
    moveElement.mockClear();

    fireEvent.click(container.querySelector(".mindmap-canvas-surface")!);

    expect(select).toHaveBeenCalledWith(null);
    expect(container.querySelectorAll(".mindmap-node-focused")).toHaveLength(0);
  });

  it("dragging one node onto another moves it there, and does not also select", () => {
    const { container } = render(<MindmapCanvas {...props} />);
    const [root, alpha] = [...container.querySelectorAll(".mindmap-node")];

    fireEvent.mouseDown(alpha, { clientX: 120, clientY: -20 });
    fireEvent.mouseMove(container.querySelector(".mindmap-canvas-surface")!, { clientX: 40, clientY: 0 });
    fireEvent.mouseUp(root);
    fireEvent.click(alpha); // the click that trails the gesture

    expect(moveElement).toHaveBeenCalledWith("a", "root");
    expect(select).not.toHaveBeenCalled();
  });

  it("a wobbly click stays a click: no move below the drag threshold", () => {
    const { container } = render(<MindmapCanvas {...props} />);
    const [root, alpha] = [...container.querySelectorAll(".mindmap-node")];

    fireEvent.mouseDown(alpha, { clientX: 120, clientY: -20 });
    fireEvent.mouseMove(container.querySelector(".mindmap-canvas-surface")!, { clientX: 121, clientY: -19 });
    fireEvent.mouseUp(root);

    expect(moveElement).not.toHaveBeenCalled();
  });

  it("releasing a drag over empty canvas moves nothing", () => {
    const { container } = render(<MindmapCanvas {...props} />);
    const alpha = container.querySelectorAll(".mindmap-node")[1];

    fireEvent.mouseDown(alpha, { clientX: 120, clientY: -20 });
    fireEvent.mouseMove(container.querySelector(".mindmap-canvas-surface")!, { clientX: 10, clientY: 10 });
    fireEvent.mouseUp(container.querySelector(".mindmap-canvas-surface")!);

    expect(moveElement).not.toHaveBeenCalled();
  });

  // ---- pan, zoom and fit -------------------------------------------------------------

  const viewBoxOf = (container: HTMLElement) =>
    (container.querySelector(".mindmap-canvas-surface")!.getAttribute("viewBox") ?? "").split(" ").map(Number);

  it("zooms in about the pointer on a wheel up, and back out on a wheel down", () => {
    const { container } = render(<MindmapCanvas {...props} />);
    const surface = container.querySelector(".mindmap-canvas-surface")!;
    const [, , wBefore] = viewBoxOf(container);

    fireEvent.wheel(surface, { deltaY: -100 });
    const [, , wIn] = viewBoxOf(container);
    expect(wIn).toBeLessThan(wBefore);

    fireEvent.wheel(surface, { deltaY: 100 });
    const [, , wOut] = viewBoxOf(container);
    expect(wOut).toBeCloseTo(wBefore, 5);
  });

  it("pans with a background drag, and the trailing click does not deselect", () => {
    const { container } = render(<MindmapCanvas {...props} />);
    const surface = container.querySelector(".mindmap-canvas-surface")!;
    const [xBefore, yBefore] = viewBoxOf(container);

    fireEvent.mouseDown(surface, { clientX: 100, clientY: 100 });
    fireEvent.mouseMove(surface, { clientX: 60, clientY: 130 });
    fireEvent.mouseUp(surface);
    fireEvent.click(surface); // the click that trails the pan

    const [xAfter, yAfter] = viewBoxOf(container);
    // jsdom has no layout, so one pixel maps to one canvas unit: the view moved opposite
    // the pointer, and the selection was left alone.
    expect(xAfter).toBeCloseTo(xBefore + 40, 5);
    expect(yAfter).toBeCloseTo(yBefore - 30, 5);
    expect(select).not.toHaveBeenCalled();
  });

  it("a drag that starts on a node never pans the view", () => {
    const { container } = render(<MindmapCanvas {...props} />);
    const surface = container.querySelector(".mindmap-canvas-surface")!;
    const alpha = container.querySelectorAll(".mindmap-node")[1];
    const before = viewBoxOf(container);

    fireEvent.mouseDown(alpha, { clientX: 120, clientY: -20 });
    fireEvent.mouseMove(surface, { clientX: 60, clientY: 40 });
    fireEvent.mouseUp(surface);

    expect(viewBoxOf(container)).toEqual(before);
  });

  it("registers zoom and fit for the ribbon, and Fit to View hands the window back to the content", async () => {
    let controls: DiagramViewControls | null = null;
    function Probe() {
      controls = useDiagramViewControls();
      return null;
    }
    const { container } = render(
      <DiagramViewProvider>
        <MindmapCanvas {...props} />
        <Probe />
      </DiagramViewProvider>,
    );
    const fitted = viewBoxOf(container);
    expect(controls).not.toBeNull();

    act(() => controls!.zoomIn());
    expect(viewBoxOf(container)[2]).toBeLessThan(fitted[2]);

    act(() => controls!.fitToView());
    expect(viewBoxOf(container)).toEqual(fitted);
  });

  it("reports the settled viewport to the backend", async () => {
    const reportView = vi.fn();
    currentReportView = reportView;
    try {
      render(<MindmapCanvas {...props} />);

      await waitFor(() => expect(reportView).toHaveBeenCalled(), { timeout: 2000 });
      const viewport = reportView.mock.calls.at(-1)![0];
      expect(viewport.maxX).toBeGreaterThan(viewport.minX);
      expect(viewport.maxY).toBeGreaterThan(viewport.minY);
    } finally {
      currentReportView = null;
    }
  });
});
