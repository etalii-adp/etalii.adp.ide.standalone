import { describe, expect, it, vi, beforeEach } from "vitest";
import { act, fireEvent, render, waitFor } from "@testing-library/react";
import { create, toBinary } from "@bufbuild/protobuf";
import { ElementSchema } from "@client/generated/elements_pb";
import { ContextSelectionAction } from "@client/generated/context_pb";
import { MindmapNodePayloadSchema } from "@client/generated/mindmap_pb";
import { applyDelta, emptyModel, type MindmapModel } from "./mindmapModel";
import { DiagramViewProvider, useDiagramViewControls, type DiagramViewControls } from "@client/shell/panels/DiagramViewContext";

const select = vi.fn();
const executeShortcut = vi.fn<(shortcut: { key: string }, source: { source: { value: { value: string } } }) => Promise<{ accepted: boolean; error: string }>>(async () => ({ accepted: true, error: "" }));
const moveElement = vi.fn(async () => "");
const executeAction = vi.fn(async () => "");
let currentReportView: ((viewport: unknown) => void) | null = null;
let currentModel: MindmapModel = emptyModel;
let currentFailed = false;
let currentSelection: unknown = null;
let currentActions: unknown[] = [];

vi.mock("./useMindmapStream", () => ({
  useMindmapStream: () => ({ model: currentModel, loading: false, failed: currentFailed, reportView: (v: unknown) => currentReportView?.(v), moveElement }),
}));

// The palette fetch talks gRPC through useAuth; the canvas under test gets its answer here.
let currentToolboxItems: unknown[] = [];
vi.mock("@client/shell/panels/useToolboxItems", () => ({
  useToolboxItems: () => currentToolboxItems,
}));

vi.mock("@client/shell/context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@client/shell/context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextConnection: () => ({ watchId: new Uint8Array(16), select, executeAction, executeShortcut }),
    useContextSelection: () => ({ selection: currentSelection, actions: currentActions }),
  };
});

// Imported after the mocks so the component picks them up.
const { MindmapCanvas } = await import("./MindmapCanvas");

function node(id: string, text: string, x = 0, y = 0, parentId = "", width = 0, height = 0) {
  return create(ElementSchema, {
    id: { value: id },
    position: { x, y },
    type: "freeplane/mindmap+node",
    payload: {
      typeUrl: "type.googleapis.com/etalii.adp.mindmap.MindmapNodePayload",
      value: toBinary(MindmapNodePayloadSchema, create(MindmapNodePayloadSchema, { text, hasChildren: id === "root", parentId, width, height })),
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
    executeAction.mockClear();
    currentModel = seed(node("root", "Root", 0, 0), node("a", "Alpha", 120, -20, "root"));
    currentSelection = null;
    currentActions = [];
    currentFailed = false;
  });

  it("renders a node per streamed element", () => {
    // Act.
    const { container } = render(<MindmapCanvas {...props} />);

    // Assert.
    expect(container.querySelectorAll(".mindmap-node")).toHaveLength(2);
    expect(container.textContent).toContain("Root");
    expect(container.textContent).toContain("Alpha");
  });

  it("renders both shared scrollbars over the map", () => {
    // Act.
    const { container } = render(<MindmapCanvas {...props} />);

    // Assert.
    expect(container.querySelector(".canvas-scrollbar-horizontal")).not.toBeNull();
    expect(container.querySelector(".canvas-scrollbar-vertical")).not.toBeNull();
  });

  it("pans from the fitted state when a scrollbar thumb is dragged", () => {
    // Arrange.
    // The one interaction the mindmap has that the timeline does not: no pan or zoom has
    // happened yet, so the bars describe the fitted box - and a thumb drag must take over
    // from it the same way dragging the canvas does. jsdom lays nothing out, so the track
    // is given a real width by hand.
    const { container } = render(<MindmapCanvas {...props} />);
    const horizontalBar = container.querySelector(".canvas-scrollbar-horizontal")!;
    Object.defineProperty(horizontalBar, "getBoundingClientRect", {
      value: () => ({ x: 0, y: 0, top: 0, left: 0, right: 200, bottom: 10, width: 200, height: 10, toJSON: () => ({}) }),
    });
    const thumb = horizontalBar.querySelector(".canvas-scrollbar-thumb")!;
    const surface = container.querySelector(".mindmap-canvas-surface")!;
    const fitted = surface.getAttribute("viewBox");

    // Act.
    fireEvent.mouseDown(thumb, { clientX: 100, clientY: 5 });
    fireEvent.mouseMove(window, { clientX: 140, clientY: 5 });
    fireEvent.mouseUp(window);

    // Assert.
    // Fitted box: x -80, w 280 (two nodes at their fallback sizes plus the 20-unit margin).
    // The extent pads that by half its span to 560 units, so the 200px track maps the
    // 40-pixel drag to 112 units - and only x moves, never the zoom.
    expect(fitted).toBe("-80 -56 280 92");
    expect(surface.getAttribute("viewBox")).toBe("32 -56 280 92");
  });

  it("gives the surface the keyboard when a node is clicked", () => {
    // Arrange.
    // Found by the diagram-workspace-tabs manual pass: clicking an SVG child shape does not
    // reliably move DOM focus into the SVG, so every shortcut kept landing in the explorer.
    const { container } = render(<MindmapCanvas {...props} />);

    // Act.
    fireEvent.click(container.querySelectorAll(".mindmap-node")[1]);

    // Assert.
    expect(document.activeElement).toBe(container.querySelector(".mindmap-canvas-surface"));
  });

  it("says the diagram is no longer available, naming its path, when the stream failed for good", () => {
    // Arrange.
    // diagram-workspace-tabs Requirement 5.1: the tab remains and explains itself - never a
    // crash, a spinner, or a silently frozen canvas.
    currentFailed = true;

    // Act.
    const { container } = render(<MindmapCanvas {...props} />);

    // Assert.
    expect(container.textContent).toContain("This diagram is no longer available at docs/map.adp.");
    expect(container.querySelectorAll(".mindmap-node")).toHaveLength(0);
  });

  it("reports a nested file->node selection when a node is clicked", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);

    // Act.
    fireEvent.click(container.querySelectorAll(".mindmap-node")[1]);

    // Assert.
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
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);
    fireEvent.click(container.querySelectorAll(".mindmap-node")[0]); // focus Root

    // Act.
    fireEvent.keyDown(container.querySelector(".mindmap-canvas-surface")!, { key: "Insert" });

    // Assert.
    expect(executeShortcut).toHaveBeenCalledTimes(1);
    const [shortcut, source] = executeShortcut.mock.calls[0];
    expect(shortcut.key).toBe("Insert");
    expect(source.source.value.value).toBe("root");
  });

  it("maps Tab to the child action's Insert key, not to an action", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);
    fireEvent.click(container.querySelectorAll(".mindmap-node")[0]);

    // Act.
    fireEvent.keyDown(container.querySelector(".mindmap-canvas-surface")!, { key: "Tab" });

    // Assert.
    expect(executeShortcut.mock.calls[0][0].key).toBe("Insert");
  });

  it("ignores a key that carries no structural meaning", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);
    fireEvent.click(container.querySelectorAll(".mindmap-node")[0]);

    // Act.
    fireEvent.keyDown(container.querySelector(".mindmap-canvas-surface")!, { key: "x" });

    // Assert.
    expect(executeShortcut).not.toHaveBeenCalled();
  });

  it("does nothing on a key when no node is focused", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);

    // Act.
    fireEvent.keyDown(container.querySelector(".mindmap-canvas-surface")!, { key: "Delete" });

    // Assert.
    expect(executeShortcut).not.toHaveBeenCalled();
  });

  it("follows a pushed selection to focus the matching node", () => {
    // Arrange.
    currentSelection = {
      id: { source: { case: "entryId", value: { value: props.entryId } } },
      detail: { case: "child", value: { id: { source: { case: "elementId", value: { value: "a" } } }, detail: { case: "none" } } },
    };

    const { container } = render(<MindmapCanvas {...props} />);

    // Act and assert, step by step.
    const alpha = container.querySelectorAll(".mindmap-node")[1];
    expect(alpha.classList.contains("mindmap-node-focused")).toBe(true);
  });

  it("draws a bezier from the parent's near edge, vertically centred, to the child's near edge", () => {
    // Arrange.
    // Sizes come from the payload, so the anchors sit on the measured edges - never a
    // centre-to-centre line cutting through the boxes.
    currentModel = seed(
      node("root", "Root", 0, 0, "", 100, 40),
      node("a", "Alpha", 200, -20, "root", 160, 48),
      node("b", "Beta", -200, 30, "root", 160, 48),
    );

    const { container } = render(<MindmapCanvas {...props} />);

    // Act and assert, step by step.
    const edges = [...container.querySelectorAll(".mindmap-edge")];
    expect(edges).toHaveLength(2); // the root has no parent to draw to
    expect(edges.every((edge) => edge.tagName === "path")).toBe(true);
    // Alpha sits right: out of the root's right edge (x=50) at the root's middle (y=0), into
    // Alpha's left edge (x=120) at Alpha's middle, with horizontal control points halfway.
    expect(edges[0].getAttribute("d")).toBe("M 50 0 C 85 0, 85 -20, 120 -20");
    // Beta sits left: out of the root's left edge, into Beta's right edge.
    expect(edges[1].getAttribute("d")).toBe("M -50 0 C -85 0, -85 30, -120 30");
  });

  it("draws each node box at the size the backend measured", () => {
    // Arrange.
    currentModel = seed(node("root", "Root", 0, 0, "", 100, 40), node("a", "Alpha", 200, -20, "root", 160, 48));

    const { container } = render(<MindmapCanvas {...props} />);

    // Act and assert, step by step.
    const rect = container.querySelectorAll(".mindmap-node")[1].querySelector("rect")!;
    expect(rect.getAttribute("x")).toBe("-80");
    expect(rect.getAttribute("y")).toBe("-24");
    expect(rect.getAttribute("width")).toBe("160");
    expect(rect.getAttribute("height")).toBe("48");
  });

  it("clicking the empty canvas deselects", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);
    fireEvent.click(container.querySelectorAll(".mindmap-node")[1]); // select Alpha first
    select.mockClear();
    moveElement.mockClear();

    // Act.
    fireEvent.click(container.querySelector(".mindmap-canvas-surface")!);

    // Assert.
    expect(select).toHaveBeenCalledWith(null);
    expect(container.querySelectorAll(".mindmap-node-focused")).toHaveLength(0);
  });

  it("dragging one node onto another moves it there, and does not also select", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);
    const [root, alpha] = [...container.querySelectorAll(".mindmap-node")];

    // Act.
    fireEvent.mouseDown(alpha, { clientX: 120, clientY: -20 });
    fireEvent.mouseMove(container.querySelector(".mindmap-canvas-surface")!, { clientX: 40, clientY: 0 });
    fireEvent.mouseUp(root);
    fireEvent.click(alpha); // the click that trails the gesture

    // Assert.
    expect(moveElement).toHaveBeenCalledWith("a", "root");
    expect(select).not.toHaveBeenCalled();
  });

  it("a wobbly click stays a click: no move below the drag threshold", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);
    const [root, alpha] = [...container.querySelectorAll(".mindmap-node")];

    // Act.
    fireEvent.mouseDown(alpha, { clientX: 120, clientY: -20 });
    fireEvent.mouseMove(container.querySelector(".mindmap-canvas-surface")!, { clientX: 121, clientY: -19 });
    fireEvent.mouseUp(root);

    // Assert.
    expect(moveElement).not.toHaveBeenCalled();
  });

  it("releasing a drag over empty canvas moves nothing", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);
    const alpha = container.querySelectorAll(".mindmap-node")[1];

    // Act.
    fireEvent.mouseDown(alpha, { clientX: 120, clientY: -20 });
    fireEvent.mouseMove(container.querySelector(".mindmap-canvas-surface")!, { clientX: 10, clientY: 10 });
    fireEvent.mouseUp(container.querySelector(".mindmap-canvas-surface")!);

    // Assert.
    expect(moveElement).not.toHaveBeenCalled();
  });

  it("highlights the node a drag is held over, until the drop lands", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);
    const [root, alpha] = [...container.querySelectorAll(".mindmap-node")];

    // Act and assert, step by step.
    fireEvent.mouseDown(alpha, { clientX: 120, clientY: -20 });
    fireEvent.mouseMove(container.querySelector(".mindmap-canvas-surface")!, { clientX: 40, clientY: 0 });
    fireEvent.mouseOver(root);
    expect(root.classList.contains("mindmap-node-drop-target")).toBe(true);

    fireEvent.mouseUp(root);
    expect(root.classList.contains("mindmap-node-drop-target")).toBe(false);
    expect(moveElement).toHaveBeenCalledWith("a", "root");
  });

  it("never marks the dragged node itself, and an idle hover marks nothing", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);
    const [root, alpha] = [...container.querySelectorAll(".mindmap-node")];

    // Act and assert, step by step.
    fireEvent.mouseOver(root); // no drag in flight
    expect(root.classList.contains("mindmap-node-drop-target")).toBe(false);

    fireEvent.mouseDown(alpha, { clientX: 120, clientY: -20 });
    fireEvent.mouseMove(container.querySelector(".mindmap-canvas-surface")!, { clientX: 40, clientY: 0 });
    fireEvent.mouseOver(alpha); // held back over itself
    expect(alpha.classList.contains("mindmap-node-drop-target")).toBe(false);
  });

  it("shows the drag's outcome mid-drag: a ghost at the pointer, a preview connector to the candidate parent", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);
    const [root, alpha] = [...container.querySelectorAll(".mindmap-node")];

    // Act and assert, step by step.
    fireEvent.mouseDown(alpha, { clientX: 120, clientY: -20 });
    expect(container.querySelector("[data-testid=mindmap-drag-preview]")).toBeNull(); // nothing until it moves

    fireEvent.mouseMove(container.querySelector(".mindmap-canvas-surface")!, { clientX: 40, clientY: 0 });
    // Moved: the ghost travels, the original dims, but with no candidate parent no connector yet.
    expect(container.querySelector("[data-testid=mindmap-drag-preview]")).not.toBeNull();
    expect(container.querySelector(".mindmap-node-ghost")?.textContent).toContain("Alpha");
    expect(alpha.classList.contains("mindmap-node-dragging")).toBe(true);
    expect(container.querySelector(".mindmap-edge-preview")).toBeNull();

    fireEvent.mouseOver(root);
    // Held over the root: the connector the drop would create is on screen before the release.
    expect(container.querySelector(".mindmap-edge-preview")).not.toBeNull();

    fireEvent.mouseUp(root);
    expect(container.querySelector("[data-testid=mindmap-drag-preview]")).toBeNull();
    expect(alpha.classList.contains("mindmap-node-dragging")).toBe(false);
  });

  it("does not offer a node inside the dragged branch as a drop target", () => {
    // Arrange.
    currentModel = seed(node("root", "Root", 0, 0), node("a", "Alpha", 120, -20, "root"), node("b", "Beta", 240, -20, "a"));
    const { container } = render(<MindmapCanvas {...props} />);
    const beta = [...container.querySelectorAll(".mindmap-node")][2];

    // Act and assert, step by step.
    fireEvent.mouseDown(container.querySelectorAll(".mindmap-node")[1], { clientX: 120, clientY: -20 });
    fireEvent.mouseMove(container.querySelector(".mindmap-canvas-surface")!, { clientX: 40, clientY: 0 });
    fireEvent.mouseOver(beta); // Beta sits inside Alpha's branch: the move would be refused
    expect(beta.classList.contains("mindmap-node-drop-target")).toBe(false);
    expect(container.querySelector(".mindmap-edge-preview")).toBeNull();
  });

  it("executes a toolbox entry's action against the node it is dropped on", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);
    const root = container.querySelectorAll(".mindmap-node")[0];
    const dataTransfer = {
      types: ["application/x-adp-toolbox-item"],
      dropEffect: "",
      getData: (type: string) => (type === "application/x-adp-toolbox-item" ? "mindmap.add-child" : ""),
    };

    // Act and assert, step by step.
    fireEvent.dragOver(root, { dataTransfer });
    expect(root.classList.contains("mindmap-node-drop-target")).toBe(true);

    fireEvent.drop(root, { dataTransfer });
    expect(root.classList.contains("mindmap-node-drop-target")).toBe(false);
    expect(executeAction).toHaveBeenCalledTimes(1);
    const [actionId, source] = (executeAction as ReturnType<typeof vi.fn>).mock.calls[0] as [string, { source: { case: string; value: { value: string } } }];
    expect(actionId).toBe("mindmap.add-child");
    expect(source.source.case).toBe("elementId");
    expect(source.source.value.value).toBe("root");
  });

  it("ignores a drag that is not a toolbox entry", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);
    const root = container.querySelectorAll(".mindmap-node")[0];
    const dataTransfer = { types: ["text/plain"], dropEffect: "", getData: () => "" };

    // Act and assert, step by step.
    fireEvent.dragOver(root, { dataTransfer });
    expect(root.classList.contains("mindmap-node-drop-target")).toBe(false);

    fireEvent.drop(root, { dataTransfer });
    expect(executeAction).not.toHaveBeenCalled();
  });

  it("right-clicking a node selects it with the menu gesture, and the menu opens on the backend's answer", () => {
    // Arrange.
    const { container, rerender } = render(<MindmapCanvas {...props} />);
    const alpha = container.querySelectorAll(".mindmap-node")[1];

    fireEvent.contextMenu(alpha);

    // Act and assert, step by step.
    // The selection went out carrying the context-menu gesture...
    expect(select).toHaveBeenCalledTimes(1);
    const inner = select.mock.calls[0][0].detail.value;
    expect(inner.id.source.value.value).toBe("a");
    expect(inner.detail.case).toBe("action");
    expect(inner.detail.value).toBe(ContextSelectionAction.CONTEXT_MENU);
    // ...and no menu is open yet: it shows the backend's actions, never a client-side guess.
    expect(container.querySelector(".context-menu")).toBeNull();

    // The push comes back naming the node, with its actions.
    currentSelection = {
      id: { source: { case: "entryId", value: { value: props.entryId } } },
      detail: { case: "child", value: { id: { source: { case: "elementId", value: { value: "a" } } }, detail: { case: "none" } } },
    };
    currentActions = [{ actions: [{ id: "mindmap.rename", label: "Rename", icon: "", available: true, unavailableReason: "", items: [] }] }];
    rerender(<MindmapCanvas {...props} />);

    expect(container.querySelector(".context-menu")).not.toBeNull();
    expect(container.textContent).toContain("Rename");

    // Choosing the entry executes the pushed action and the menu closes.
    fireEvent.click(container.querySelector(".context-menu-item")!);
    expect(executeAction).toHaveBeenCalledWith("mindmap.rename");
    expect(container.querySelector(".context-menu")).toBeNull();
  });

  // ---- pan, zoom and fit -------------------------------------------------------------

  const viewBoxOf = (container: HTMLElement) =>
    (container.querySelector(".mindmap-canvas-surface")!.getAttribute("viewBox") ?? "").split(" ").map(Number);

  it("zooms in about the pointer on a wheel up, and back out on a wheel down", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);
    const surface = container.querySelector(".mindmap-canvas-surface")!;
    const [, , wBefore] = viewBoxOf(container);

    // Act and assert, step by step.
    fireEvent.wheel(surface, { deltaY: -100 });
    const [, , wIn] = viewBoxOf(container);
    expect(wIn).toBeLessThan(wBefore);

    fireEvent.wheel(surface, { deltaY: 100 });
    const [, , wOut] = viewBoxOf(container);
    expect(wOut).toBeCloseTo(wBefore, 5);
  });

  it("pans with a background drag, and the trailing click does not deselect", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);
    const surface = container.querySelector(".mindmap-canvas-surface")!;
    const [xBefore, yBefore] = viewBoxOf(container);

    fireEvent.mouseDown(surface, { clientX: 100, clientY: 100 });
    fireEvent.mouseMove(surface, { clientX: 60, clientY: 130 });
    fireEvent.mouseUp(surface);
    fireEvent.click(surface); // the click that trails the pan

    // Act and assert, step by step.
    const [xAfter, yAfter] = viewBoxOf(container);
    // jsdom has no layout, so one pixel maps to one canvas unit: the view moved opposite
    // the pointer, and the selection was left alone.
    expect(xAfter).toBeCloseTo(xBefore + 40, 5);
    expect(yAfter).toBeCloseTo(yBefore - 30, 5);
    expect(select).not.toHaveBeenCalled();
  });

  it("a drag that starts on a node never pans the view", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);
    const surface = container.querySelector(".mindmap-canvas-surface")!;
    const alpha = container.querySelectorAll(".mindmap-node")[1];
    const before = viewBoxOf(container);

    // Act.
    fireEvent.mouseDown(alpha, { clientX: 120, clientY: -20 });
    fireEvent.mouseMove(surface, { clientX: 60, clientY: 40 });
    fireEvent.mouseUp(surface);

    // Assert.
    expect(viewBoxOf(container)).toEqual(before);
  });

  it("registers zoom and fit for the ribbon, and Fit to View hands the window back to the content", async () => {
    // Act and assert, step by step.
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
    // Arrange and act.
    const reportView = vi.fn();
    currentReportView = reportView;
    try {
      render(<MindmapCanvas {...props} />);

    // Assert.
      await waitFor(() => expect(reportView).toHaveBeenCalled(), { timeout: 2000 });
      const viewport = reportView.mock.calls.at(-1)![0];
      expect(viewport.maxX).toBeGreaterThan(viewport.minX);
      expect(viewport.maxY).toBeGreaterThan(viewport.minY);
    } finally {
      currentReportView = null;
    }
  });

  it("reports the area the svg actually shows, not the bare viewBox", async () => {
    // Arrange.
    // The svg letterboxes: with the default preserveAspectRatio the viewBox is fitted inside
    // the element and centred, so the axis with room to spare displays more of the map than
    // the box asks for. Reporting the box alone had the backend cull nodes that were on screen
    // in that margin. jsdom reports no layout, so the surface is measured by hand here.
    const reportView = vi.fn();
    currentReportView = reportView;
    const surfaceWidth = 800;
    const surfaceHeight = 200; // deliberately a different aspect ratio than the content
    const measure = vi
      .spyOn(Element.prototype, "getBoundingClientRect")
      .mockReturnValue({ x: 0, y: 0, width: surfaceWidth, height: surfaceHeight, top: 0, left: 0, right: surfaceWidth, bottom: surfaceHeight, toJSON: () => ({}) } as DOMRect);
    try {
      const { container } = render(<MindmapCanvas {...props} />);
      const [boxX, boxY, boxW, boxH] = viewBoxOf(container);

    // Act and assert, step by step.
      await waitFor(() => expect(reportView).toHaveBeenCalled(), { timeout: 2000 });

      const viewport = reportView.mock.calls.at(-1)![0];
      const scale = Math.min(surfaceWidth / boxW, surfaceHeight / boxH);
      // The reported rectangle stays centred on the viewBox but spans what the element covers.
      expect(viewport.maxX - viewport.minX).toBeCloseTo(surfaceWidth / scale, 5);
      expect(viewport.maxY - viewport.minY).toBeCloseTo(surfaceHeight / scale, 5);
      expect((viewport.minX + viewport.maxX) / 2).toBeCloseTo(boxX + boxW / 2, 5);
      expect((viewport.minY + viewport.maxY) / 2).toBeCloseTo(boxY + boxH / 2, 5);
      // And it never reports less than the box itself, on either axis.
      expect(viewport.minX).toBeLessThanOrEqual(boxX + 1e-9);
      expect(viewport.maxY).toBeGreaterThanOrEqual(boxY + boxH - 1e-9);
    } finally {
      measure.mockRestore();
      currentReportView = null;
    }
  });
});
