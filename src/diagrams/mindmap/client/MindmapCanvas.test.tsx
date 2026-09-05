import { describe, expect, it, vi, beforeEach } from "vitest";
import { act, fireEvent, render, waitFor } from "@testing-library/react";
import { create, toBinary } from "@bufbuild/protobuf";
import { ElementSchema } from "@client/generated/elements_pb";
import { ContextPromptSchema, ContextSelectionAction } from "@client/generated/context_pb";
import type { ContextPrompt } from "@client/generated/context_pb";
import { MindmapNodePayloadSchema } from "@client/generated/mindmap_pb";
import { applyDelta, emptyModel, type MindmapModel } from "./mindmapModel";
import { DiagramViewProvider, useDiagramViewControls, type DiagramViewControls } from "@client/shell/panels/DiagramViewContext";
import { InlineLabelPlacementProvider } from "@client/shell/panels/InlineLabelPlacementContext";
import { ShellPromptHost } from "@client/shell/context/ShellPromptHost";

const select = vi.fn();
const executeShortcut = vi.fn<(shortcut: { key: string }, source: { source: { value: { value: string } } }) => Promise<{ accepted: boolean; error: string }>>(async () => ({ accepted: true, error: "" }));
const moveElement = vi.fn(async () => "");
const executeAction = vi.fn(async () => "");
let currentReportView: ((viewport: unknown) => void) | null = null;
let currentModel: MindmapModel = emptyModel;
let currentFailed = false;
let currentSelection: unknown = null;
let currentActions: unknown[] = [];

// The prompt the shell is holding, and the three calls the inline editor makes back through it.
let currentPrompt: ContextPrompt | null = null;
const proposeLabel = vi.fn(async (revision: number) => ({ revision, valid: true, reason: "" }));
const submitLabel = vi.fn(async () => ({ completed: true, error: "" }));
const cancelLabel = vi.fn();

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
    useContextPrompt: () => ({ prompt: currentPrompt, onPropose: proposeLabel, onSubmit: submitLabel, onCancel: cancelLabel }),
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

/**
 * A pointer event jsdom can actually carry: jsdom implements no PointerEvent, and
 * `fireEvent.pointerDown` builds a bare Event whose `button` is undefined. A MouseEvent typed
 * "pointerdown" bubbles the same way and carries the button - usePointerGesture.test.tsx's
 * idiom, for the same reason.
 */
function pointer(type: string, init: MouseEventInit) {
  return new MouseEvent(type, { bubbles: true, cancelable: true, ...init });
}

/** A click in the pointer vocabulary the canvas listens to: press and release, unmoved. */
function press(target: Element, init: MouseEventInit = {}) {
  fireEvent(target, pointer("pointerdown", { button: 0, ...init }));
  fireEvent(target, pointer("pointerup", { ...init }));
}

// jsdom implements no pointer capture on SVG elements; the arbiter uses it so a release
// outside the surface still ends the gesture.
SVGElement.prototype.setPointerCapture ??= () => {};
SVGElement.prototype.releasePointerCapture ??= () => {};

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
    currentPrompt = null;
    proposeLabel.mockClear();
    submitLabel.mockClear();
    cancelLabel.mockClear();
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
    press(container.querySelectorAll(".mindmap-node")[1]);

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
    press(container.querySelectorAll(".mindmap-node")[1]);

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
    press(container.querySelectorAll(".mindmap-node")[0]); // focus Root

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
    press(container.querySelectorAll(".mindmap-node")[0]);

    // Act.
    fireEvent.keyDown(container.querySelector(".mindmap-canvas-surface")!, { key: "Tab" });

    // Assert.
    expect(executeShortcut.mock.calls[0][0].key).toBe("Insert");
  });

  it("ignores a key that carries no structural meaning", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);
    press(container.querySelectorAll(".mindmap-node")[0]);

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
    press(container.querySelectorAll(".mindmap-node")[1]); // select Alpha first
    select.mockClear();
    moveElement.mockClear();

    // Act.
    press(container.querySelector(".mindmap-canvas-surface")!);

    // Assert.
    expect(select).toHaveBeenCalledWith(null);
    expect(container.querySelectorAll(".mindmap-node-focused")).toHaveLength(0);
  });

  it("dragging one node onto another moves it there, and does not also select", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);
    const [, alpha] = [...container.querySelectorAll(".mindmap-node")];

    // Act.
    fireEvent(alpha, pointer("pointerdown", { button: 0, clientX: 120, clientY: -20 }));
    fireEvent(alpha, pointer("pointermove", { clientX: 40, clientY: 0 }));
    fireEvent(alpha, pointer("pointerup", { clientX: 40, clientY: 0 })); // released over Root's box
    fireEvent.click(alpha); // the click that trails the gesture - inert, nothing listens

    // Assert.
    expect(moveElement).toHaveBeenCalledWith("a", "root");
    expect(select).not.toHaveBeenCalled();
  });

  it("selecting after a reparenting drag still selects the next node clicked", () => {
    // Arrange: the reported defect, face (a). A drag of Alpha dropped on Root reparents it;
    // the reparent's layout moves Alpha out from under the pointer, so the trailing click
    // never consumes the armed flag - and the next legitimate click is swallowed by it.
    const { container } = render(<MindmapCanvas {...props} />);
    const [root, alpha] = [...container.querySelectorAll(".mindmap-node")];

    fireEvent(alpha, pointer("pointerdown", { button: 0, clientX: 120, clientY: -20 }));
    fireEvent(alpha, pointer("pointermove", { clientX: 40, clientY: 0 }));
    fireEvent(alpha, pointer("pointerup", { clientX: 40, clientY: 0 })); // dropped on Root
    // No trailing click arrives: the drop's layout moved Alpha away from the pointer.

    // Act: the next gesture is a plain press on Root.
    press(root);

    // Assert: Root is selected - one selection, naming "root".
    expect(select).toHaveBeenCalledTimes(1);
    expect(select.mock.calls[0][0].detail.value.id.source.value.value).toBe("root");
  });

  it("a drag released over empty canvas leaves the selection alone", () => {
    // Arrange: face (b). Alpha is selected; dragging it and releasing over empty canvas ends
    // the drag, and the trailing background click must not throw the selection away.
    const { container } = render(<MindmapCanvas {...props} />);
    const surface = container.querySelector(".mindmap-canvas-surface")!;
    const alpha = container.querySelectorAll(".mindmap-node")[1];
    press(alpha); // select Alpha first
    select.mockClear();

    // Act.
    fireEvent(alpha, pointer("pointerdown", { button: 0, clientX: 120, clientY: -20 }));
    fireEvent(alpha, pointer("pointermove", { clientX: 400, clientY: 300 })); // over empty canvas
    fireEvent(alpha, pointer("pointerup", { clientX: 400, clientY: 300 }));
    fireEvent.click(surface); // the click that trails the drag - inert, nothing listens

    // Assert: a completed drag over empty canvas neither selects nor deselects.
    expect(select).not.toHaveBeenCalled();
  });

  it("a wobbly click stays a click: no move below the drag threshold", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);
    const [, alpha] = [...container.querySelectorAll(".mindmap-node")];

    // Act.
    fireEvent(alpha, pointer("pointerdown", { button: 0, clientX: 120, clientY: -20 }));
    fireEvent(alpha, pointer("pointermove", { clientX: 121, clientY: -19 }));
    fireEvent(alpha, pointer("pointerup", { clientX: 121, clientY: -19 }));

    // Assert.
    expect(moveElement).not.toHaveBeenCalled();
  });

  it("releasing a drag over empty canvas moves nothing", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);
    const alpha = container.querySelectorAll(".mindmap-node")[1];

    // Act.
    fireEvent(alpha, pointer("pointerdown", { button: 0, clientX: 120, clientY: -20 }));
    fireEvent(alpha, pointer("pointermove", { clientX: 400, clientY: 300 })); // off every node's box
    fireEvent(alpha, pointer("pointerup", { clientX: 400, clientY: 300 }));

    // Assert.
    expect(moveElement).not.toHaveBeenCalled();
  });

  it("highlights the node a drag is held over, until the drop lands", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);
    const [root, alpha] = [...container.querySelectorAll(".mindmap-node")];

    // Act and assert, step by step. The highlight rides the drag's own movement: holding
    // the ghost over Root's box is what marks Root, no hover event needed.
    fireEvent(alpha, pointer("pointerdown", { button: 0, clientX: 120, clientY: -20 }));
    fireEvent(alpha, pointer("pointermove", { clientX: 40, clientY: 0 }));
    expect(root.classList.contains("mindmap-node-drop-target")).toBe(true);

    fireEvent(alpha, pointer("pointerup", { clientX: 40, clientY: 0 }));
    expect(root.classList.contains("mindmap-node-drop-target")).toBe(false);
    expect(moveElement).toHaveBeenCalledWith("a", "root");
  });

  it("never marks the dragged node itself, and an idle hover marks nothing", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);
    const [root, alpha] = [...container.querySelectorAll(".mindmap-node")];

    // Act and assert, step by step.
    fireEvent(root, pointer("pointermove", { clientX: 0, clientY: 0 })); // no gesture in flight
    expect(root.classList.contains("mindmap-node-drop-target")).toBe(false);

    fireEvent(alpha, pointer("pointerdown", { button: 0, clientX: 120, clientY: -20 }));
    fireEvent(alpha, pointer("pointermove", { clientX: 100, clientY: -10 })); // held back over itself
    expect(alpha.classList.contains("mindmap-node-drop-target")).toBe(false);
  });

  it("shows the drag's outcome mid-drag: a ghost at the pointer, a preview connector to the candidate parent", () => {
    // Arrange.
    const { container } = render(<MindmapCanvas {...props} />);
    const [, alpha] = [...container.querySelectorAll(".mindmap-node")];

    // Act and assert, step by step.
    fireEvent(alpha, pointer("pointerdown", { button: 0, clientX: 120, clientY: -20 }));
    expect(container.querySelector("[data-testid=mindmap-drag-preview]")).toBeNull(); // nothing until it moves

    fireEvent(alpha, pointer("pointermove", { clientX: 400, clientY: 300 }));
    // Moved over empty canvas: the ghost travels, the original dims, but with no candidate
    // parent no connector yet.
    expect(container.querySelector("[data-testid=mindmap-drag-preview]")).not.toBeNull();
    expect(container.querySelector(".mindmap-node-ghost")?.textContent).toContain("Alpha");
    expect(alpha.classList.contains("mindmap-node-dragging")).toBe(true);
    expect(container.querySelector(".mindmap-edge-preview")).toBeNull();

    fireEvent(alpha, pointer("pointermove", { clientX: 40, clientY: 0 }));
    // Held over the root: the connector the drop would create is on screen before the release.
    expect(container.querySelector(".mindmap-edge-preview")).not.toBeNull();

    fireEvent(alpha, pointer("pointerup", { clientX: 40, clientY: 0 }));
    expect(container.querySelector("[data-testid=mindmap-drag-preview]")).toBeNull();
    expect(alpha.classList.contains("mindmap-node-dragging")).toBe(false);
  });

  it("does not offer a node inside the dragged branch as a drop target", () => {
    // Arrange.
    currentModel = seed(node("root", "Root", 0, 0), node("a", "Alpha", 120, -20, "root"), node("b", "Beta", 240, -20, "a"));
    const { container } = render(<MindmapCanvas {...props} />);
    const beta = [...container.querySelectorAll(".mindmap-node")][2];

    // Act and assert, step by step.
    const alpha = container.querySelectorAll(".mindmap-node")[1];
    fireEvent(alpha, pointer("pointerdown", { button: 0, clientX: 120, clientY: -20 }));
    // Held over Beta's box - but Beta sits inside Alpha's branch: the move would be refused.
    fireEvent(alpha, pointer("pointermove", { clientX: 240, clientY: -20 }));
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

    fireEvent(surface, pointer("pointerdown", { button: 0, clientX: 100, clientY: 100 }));
    fireEvent(surface, pointer("pointermove", { clientX: 60, clientY: 130 }));
    fireEvent(surface, pointer("pointerup", { clientX: 60, clientY: 130 }));
    fireEvent.click(surface); // the click that trails the pan - inert, nothing listens

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
    fireEvent(alpha, pointer("pointerdown", { button: 0, clientX: 120, clientY: -20 }));
    fireEvent(surface, pointer("pointermove", { clientX: 60, clientY: 40 }));
    fireEvent(surface, pointer("pointerup", { clientX: 60, clientY: 40 }));

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

  // ---- inline renaming ---------------------------------------------------------------------

  /** A rename prompt for one node - the shape the backend sends once the module marks it. */
  function renamePromptFor(nodeId: string, text: string): ContextPrompt {
    return create(ContextPromptSchema, {
      interactionId: { value: new Uint8Array(16).fill(7) },
      prompt: {
        case: "inputDialog",
        value: {
          title: "Rename node",
          icon: "mdi-pencil-outline",
          fieldLabel: "Text",
          initialValue: text,
          confirmLabel: "Rename",
          inlineLabelEdit: { elementId: { value: nodeId } },
        },
      },
    });
  }

  function labelField(container: HTMLElement): HTMLInputElement {
    return container.querySelector("input.inline-label-editor-field") as HTMLInputElement;
  }

  function editorBox(container: HTMLElement): SVGForeignObjectElement {
    return container.querySelector("foreignObject.inline-label-editor") as SVGForeignObjectElement;
  }

  it("draws an editor over the node a marked rename prompt names, and submits what is typed", async () => {
    // Arrange.
    currentModel = seed(node("root", "Root", 0, 0), node("a", "Alpha", 120, -20, "root", 80, 24));
    currentPrompt = renamePromptFor("a", "Alpha");
    const { container } = render(<MindmapCanvas {...props} />);

    // Assert, first: the box is over that node, in canvas units, positioned by its corner.
    const box = editorBox(container);
    expect(box).not.toBeNull();
    expect(Number(box.getAttribute("x"))).toBeCloseTo(120 - 40, 5);
    expect(Number(box.getAttribute("y"))).toBeCloseTo(-20 - 12, 5);

    // Act.
    fireEvent.change(labelField(container), { target: { value: "Beta" } });
    await act(async () => {
      fireEvent.keyDown(labelField(container), { key: "Enter" });
    });

    // Assert.
    expect(submitLabel).toHaveBeenCalledWith("Beta");
  });

  it("keeps the editor on its node, with the typing intact, when the canvas is panned mid-edit", () => {
    // Arrange.
    currentModel = seed(node("root", "Root", 0, 0), node("a", "Alpha", 120, -20, "root", 80, 24));
    currentPrompt = renamePromptFor("a", "Alpha");
    const { container } = render(<MindmapCanvas {...props} />);
    fireEvent.change(labelField(container), { target: { value: "Half typed" } });
    const surface = container.querySelector("svg.mindmap-canvas-surface") as SVGSVGElement;
    const viewBoxBefore = surface.getAttribute("viewBox");

    // Act.
    // A pan of the view, by the canvas's own empty-surface drag.
    fireEvent(surface, pointer("pointerdown", { button: 0, clientX: 200, clientY: 200 }));
    fireEvent(surface, pointer("pointermove", { clientX: 260, clientY: 240 }));
    fireEvent(surface, pointer("pointerup", { clientX: 260, clientY: 240 }));

    // Assert.
    // The editor is mounted in canvas units, so the view moving underneath it does not move it
    // off its node: the box's own coordinates are unchanged while the viewBox is not. An editor
    // positioned in screen pixels would have drifted here, which is the defect this catches.
    expect(surface.getAttribute("viewBox")).not.toBe(viewBoxBefore);
    const box = editorBox(container);
    expect(Number(box.getAttribute("x"))).toBeCloseTo(120 - 40, 5);
    expect(Number(box.getAttribute("y"))).toBeCloseTo(-20 - 12, 5);
    expect(labelField(container).value).toBe("Half typed");
  });

  it("commits an open editor before a gesture that moves the canvas begins", async () => {
    // Arrange.
    currentModel = seed(node("root", "Root", 0, 0), node("a", "Alpha", 120, -20, "root", 80, 24));
    currentPrompt = renamePromptFor("a", "Alpha");
    const { container } = render(<MindmapCanvas {...props} />);
    fireEvent.change(labelField(container), { target: { value: "Beta" } });
    const surface = container.querySelector("svg.mindmap-canvas-surface") as SVGSVGElement;

    // Act.
    // Pressing on the empty canvas starts a pan; pressing a node starts a drag. Either way the
    // gesture must not begin over an editor still holding unsaved text.
    await act(async () => {
      fireEvent(surface, pointer("pointerdown", { button: 0, clientX: 200, clientY: 200 }));
    });

    // Assert.
    expect(submitLabel).toHaveBeenCalledWith("Beta");
  });

  it("leaves the selection exactly as it was when an inline edit commits", async () => {
    // Arrange.
    currentModel = seed(node("root", "Root", 0, 0), node("a", "Alpha", 120, -20, "root", 80, 24));
    currentPrompt = renamePromptFor("a", "Alpha");
    const { container } = render(<MindmapCanvas {...props} />);
    select.mockClear();

    // Act.
    fireEvent.change(labelField(container), { target: { value: "Beta" } });
    await act(async () => {
      fireEvent.keyDown(labelField(container), { key: "Enter" });
    });

    // Assert.
    // A commit that re-selects would quietly lose the user's place, which is only noticeable
    // when renaming several things in a row - and by then it looks like the canvas misbehaving
    // rather than like the rename doing it.
    expect(select).not.toHaveBeenCalled();
  });

  // ---- the canvas and the shell together ---------------------------------------------------

  /**
   * The real placement registry, the real canvas and the real shell host in one tree - no stub
   * resolver anywhere.
   *
   * This is the shape the unit tests could not express and a manual pass found the hard way:
   * every rename was cancelled the instant it opened, while both components' own tests passed.
   * A stubbed canvas registers once and answers for ever; a real one re-registers whenever its
   * model changes, and the shell was reading the gap in between as the element having gone.
   */
  function renderCanvasAndShell(places: string[] = []) {
    void places;
    return render(
      <InlineLabelPlacementProvider>
        <MindmapCanvas {...props} />
        <ShellPromptHost />
      </InlineLabelPlacementProvider>,
    );
  }

  it("opens the editor rather than cancelling, with the real registry between the canvas and the shell", async () => {
    // Arrange.
    currentModel = seed(node("root", "Root", 0, 0), node("a", "Alpha", 120, -20, "root", 80, 24));
    currentPrompt = renamePromptFor("a", "Alpha");

    // Act.
    const { container } = renderCanvasAndShell();
    await act(async () => {
      await Promise.resolve();
    });

    // Assert.
    // An editor on the node, no dialog over it, and above all no cancel: the interaction the
    // backend just opened must still be open.
    expect(container.querySelector("foreignObject.inline-label-editor")).not.toBeNull();
    expect(cancelLabel).not.toHaveBeenCalled();
  });

  it("keeps the editor open when the model changes under it, which re-registers the canvas", async () => {
    // Arrange.
    currentModel = seed(node("root", "Root", 0, 0), node("a", "Alpha", 120, -20, "root", 80, 24));
    currentPrompt = renamePromptFor("a", "Alpha");
    const { container, rerender } = renderCanvasAndShell();
    await act(async () => {
      await Promise.resolve();
    });
    expect(container.querySelector("foreignObject.inline-label-editor")).not.toBeNull();

    // Act.
    // A delta arrives while the editor is open - a sibling appears. The canvas's resolver is
    // memoized on the model, so this is a new identity and a re-registration.
    currentModel = seed(
      node("root", "Root", 0, 0),
      node("a", "Alpha", 120, -20, "root", 80, 24),
      node("b", "Beta", 120, 40, "root", 80, 24),
    );
    await act(async () => {
      rerender(
        <InlineLabelPlacementProvider>
          <MindmapCanvas {...props} />
          <ShellPromptHost />
        </InlineLabelPlacementProvider>,
      );
      await Promise.resolve();
    });

    // Assert.
    expect(cancelLabel).not.toHaveBeenCalled();
    expect(container.querySelector("foreignObject.inline-label-editor")).not.toBeNull();
  });
});
