import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, fireEvent, waitFor } from "@testing-library/react";
import { create, toBinary } from "@bufbuild/protobuf";
import { ElementSchema } from "@client/generated/elements_pb";
import {
  C4BoundaryPayloadSchema,
  C4ElementPayloadSchema,
  C4RelationshipPayloadSchema,
  C4ViewPayloadSchema,
} from "@client/generated/c4_pb";
import { applyDelta, emptyModel, BOUNDARY_TYPE, NODE_TYPE, RELATIONSHIP_TYPE, VIEW_TYPE, type C4Model } from "./c4Model";
import { ContextPromptSchema } from "@client/generated/context_pb";
import type { ContextPrompt } from "@client/generated/context_pb";
import { act } from "@testing-library/react";
import { elementSelectionOf, selectedElementIdOf } from "@client/canvas/selection";
import type { ContextSelection } from "@client/generated/context_pb";
import { expectLibrarySelection } from "@client/canvas/library/testing/expectLibrarySelection";

const select = vi.fn();
let currentModel: C4Model = emptyModel;
let currentLoading = false;
let currentFailed = false;
let currentReportView: ((viewport: unknown) => void) | null = null;

// The prompt the shell is holding, and the calls the inline editor makes back through it.
let currentPrompt: ContextPrompt | null = null;
const proposeLabel = vi.fn(async (revision: number) => ({ revision, valid: true, reason: "" }));
const submitLabel = vi.fn(async () => ({ completed: true, error: "" }));
const cancelLabel = vi.fn();

vi.mock("./useC4Stream", () => ({
  useC4Stream: () => ({
    model: currentModel,
    loading: currentLoading,
    failed: currentFailed,
    reportView: (v: unknown) => currentReportView?.(v),
    moveElementTo: (elementId: string, x: number, y: number) => {
      moves.push({ elementId, x, y });
      return Promise.resolve(moveOutcome);
    },
  }),
}));

let currentSelection: unknown = null;

// What the backend answers a shortcut with; accepted unless a test says otherwise.
let shortcutOutcome = { accepted: true, error: "" };

// What the backend answers a move with: empty when recorded, its refusal sentence otherwise.
let moveOutcome = "";

vi.mock("@client/shell/context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@client/shell/context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextConnection: () => ({
      watchId: new Uint8Array(16),
      select,
      executeAction: (actionId: string) => {
        executed.push(actionId);
        return Promise.resolve({ accepted: true, error: "" });
      },
      executeShortcut: () => Promise.resolve(shortcutOutcome),
    }),
    useContextSelection: () => ({ selection: currentSelection, actions: [] }),
    useContextPrompt: () => ({ prompt: currentPrompt, onPropose: proposeLabel, onSubmit: submitLabel, onCancel: cancelLabel }),
  };
});

// The palette comes from the backend over its own call; this canvas only registers what it
// is handed, so the tests here need it to be nothing rather than to be real.
vi.mock("@client/shell/panels/useToolboxItems", () => ({ useToolboxItems: () => [] }));
vi.mock("@client/shell/panels/DiagramToolboxContext", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@client/shell/panels/DiagramToolboxContext")>();
  return { ...actual, useRegisterDiagramToolbox: () => {} };
});

/** Action ids the canvas asked the backend to run - what a drop and a menu choice produce. */
const executed: string[] = [];

/** Positions the canvas asked the backend to record - what a completed drag produces. */
const moves: Array<{ elementId: string; x: number; y: number }> = [];

const { C4Canvas } = await import("./C4Canvas");

function element(id: string, type: string, payload: Uint8Array, x = 0, y = 0) {
  return create(ElementSchema, {
    id: { value: id },
    position: { x, y },
    type,
    payload: { typeUrl: `type.googleapis.com/${type}`, value: payload },
  });
}

function node(id: string, name: string, x: number, y: number, extra: Record<string, unknown> = {}) {
  return element(
    id,
    NODE_TYPE,
    toBinary(C4ElementPayloadSchema, create(C4ElementPayloadSchema, {
      name,
      typeLine: "[Software System]",
      description: "A description.",
      width: 160,
      height: 80,
      style: { background: "#1168bd", color: "#ffffff", shape: "RoundedBox" },
      ...extra,
    })),
    x,
    y,
  );
}

function seed(...elements: ReturnType<typeof element>[]): C4Model {
  return applyDelta(emptyModel, { action: { case: "add", value: { elements } } } as never);
}

const props = { projectId: new Uint8Array(16), entryId: new Uint8Array(16).fill(3), path: ["docs", "model.adp"] };

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

describe("C4Canvas", () => {
  beforeEach(() => {
    select.mockClear();
    executed.length = 0;
    moves.length = 0;
    currentLoading = false;
    currentFailed = false;
    currentPrompt = null;
    currentSelection = null;
    proposeLabel.mockClear();
    submitLabel.mockClear();
    cancelLabel.mockClear();
    currentModel = seed(
      node("a", "Alpha", 0, 0),
      node("b", "Beta", 0, 200),
      element("a->b", RELATIONSHIP_TYPE, toBinary(C4RelationshipPayloadSchema, create(C4RelationshipPayloadSchema, {
        sourceId: "a",
        destinationId: "b",
        description: "Uses",
        technology: "HTTPS",
        sourceX: 0, sourceY: 0, sourceWidth: 160, sourceHeight: 80,
        destinationX: 0, destinationY: 200, destinationWidth: 160, destinationHeight: 80,
      }))),
      element("c4:view", VIEW_TYPE, toBinary(C4ViewPayloadSchema, create(C4ViewPayloadSchema, {
        title: "System Context diagram for Alpha",
        viewKind: "SystemContext",
        viewKey: "context",
        legend: [
          { label: "Software System", style: { background: "#1168bd" } },
          { label: "Person", style: { background: "#08427b" } },
        ],
      }))),
    );
  });

  it("renders a box per element, at the size the backend measured", () => {
    // Arrange.
    const { container } = render(<C4Canvas {...props} />);

    // Act and assert, step by step.
    const nodes = container.querySelectorAll(".c4-node");
    expect(nodes).toHaveLength(2);
    const rect = nodes[0].querySelector("rect")!;
    expect(rect.getAttribute("width")).toBe("160");
    expect(rect.getAttribute("height")).toBe("80");
  });

  it("shows the three lines C4 asks for: name, bracketed type, and description", () => {
    // Act.
    const { container } = render(<C4Canvas {...props} />);

    // Assert.
    expect(container.textContent).toContain("Alpha");
    expect(container.textContent).toContain("[Software System]");
    expect(container.textContent).toContain("A description.");
  });

  it("wraps a description onto the lines the backend sized the card for, rather than one line out past its edges", () => {
    // Arrange: a real description, from the industrial-plant example's container view, at the
    // box C4Metrics.Measure gives it - clamped to 240 wide, and four description lines tall
    // (ceil(103 x 7.7 / 216)), so 6 x 19.6 + 20 = 137.6. Drawn as one line it ran ~250 wide
    // at the description's 10px, out of both sides of a 240 card.
    const description = "The only thing that talks to the control network: subscribes to line tags and issues recipe downloads.";
    currentModel = seed(node("opc", "OPC UA Gateway", 0, 0, { typeLine: "[Container: C# and OPC UA SDK]", description, width: 240, height: 137.6 }));

    // Act.
    const { container } = render(<C4Canvas {...props} />);

    // Assert.
    const card = container.querySelector('[data-element-id="opc"]')!;
    const nameY = Number(card.querySelector("text.c4-node-name")!.getAttribute("y"));
    const lines = Array.from(card.querySelectorAll("text.c4-node-description"));
    expect(lines.length).toBeGreaterThan(1);
    // Every word, in order, and nothing else - a wrap that dropped a word would still fit.
    expect(lines.map((line) => line.textContent).join(" ")).toBe(description);
    // Each line fits the content width by the drawn 10px at C4Metrics' 0.55 per character,
    // inside its 12 of padding either side.
    for (const line of lines) {
      expect(line.textContent!.length * 10 * 0.55).toBeLessThanOrEqual(240 - 2 * 12);
    }
    // Stacked downwards, and the last baseline still inside the card (its top is 22 above the
    // name's baseline, as the declaration pins it).
    const ys = lines.map((line) => Number(line.getAttribute("y")));
    expect(ys).toEqual([...ys].sort((a, b) => a - b));
    expect(new Set(ys).size).toBe(ys.length);
    expect(ys[ys.length - 1] - (nameY - 22)).toBeLessThanOrEqual(137.6 - 4);
  });

  it("cuts a type line longer than the card, which the backend clamped to 240 and drew whole past both edges", () => {
    // Arrange: the example's database deployment node - 52 characters, drawn 249 wide in a
    // 240 card.
    const typeLine = "[Deployment Node: PostgreSQL 16 on Ubuntu 24.04 LTS]";
    currentModel = seed(node("db", "Primary", 0, 0, { typeLine, description: "", width: 240, height: 59.2 }));

    // Act.
    const { container } = render(<C4Canvas {...props} />);

    // Assert.
    const drawn = container.querySelector('[data-element-id="db"] text.c4-node-type')!.textContent!;
    expect(drawn.endsWith("…")).toBe(true);
    expect(typeLine.startsWith(drawn.slice(0, -1))).toBe(true);
    expect(drawn.length * 10 * 0.55).toBeLessThanOrEqual(240 - 2 * 12);
  });

  it("leaves a type line that fits exactly as it is", () => {
    // The guard's other side: a cut at the library's 7-per-character estimate would have taken
    // this 39-character line, which the browser draws 194 wide in the same 240 card.
    const typeLine = "[Deployment Node: Kubernetes namespace]";
    currentModel = seed(node("ns", "Namespace", 0, 0, { typeLine, description: "", width: 240, height: 59.2 }));

    const { container } = render(<C4Canvas {...props} />);

    expect(container.querySelector('[data-element-id="ns"] text.c4-node-type')!.textContent).toBe(typeLine);
  });

  it("carries the title C4 requires on every diagram", () => {
    // Act.
    const { getByTestId } = render(<C4Canvas {...props} />);

    // Assert.
    expect(getByTestId("c4-title").textContent).toBe("System Context diagram for Alpha");
  });

  it("carries a key explaining the notation, so the diagram reads without narrative", () => {
    // Arrange.
    const { getByTestId } = render(<C4Canvas {...props} />);

    // Act and assert, step by step.
    const legend = getByTestId("c4-legend");
    expect(legend.textContent).toContain("Software System");
    expect(legend.textContent).toContain("Person");
  });

  it("draws each relationship as one arrow, labelled with its intent and technology", () => {
    // Arrange.
    const { container } = render(<C4Canvas {...props} />);

    // Act and assert, step by step.
    const relationships = container.querySelectorAll(".c4-relationship-group");
    expect(relationships).toHaveLength(1);
    expect(relationships[0].querySelector("path.c4-relationship-line")!.getAttribute("marker-end")).toBe("url(#library-arrow)");
    expect(relationships[0].textContent).toContain("Uses [HTTPS]");
  });

  it("keeps the stylesheet aimed at the element the relationship actually draws", async () => {
    // Arrange.
    // jsdom loads no CSS, so a selector saying `line` while the markup says `path` passes
    // every DOM test and ships invisible relationships - which is exactly what happened when
    // the connection became a shared <path>. The stylesheet's own text is the only witness.
    const { readFileSync } = await import("node:fs");
    const { resolve } = await import("node:path");
    // Resolved from the workspace the runner starts in (src/client); import.meta.url is not
    // a file: URL under the test transform, so the path is spelled out.
    const css = readFileSync(resolve(process.cwd(), "../diagrams/c4/client/c4.css"), "utf8");
    const { container } = render(<C4Canvas {...props} />);

    // Act & assert.
    expect(container.querySelector("path.c4-relationship-line")).not.toBeNull();
    expect(css).toMatch(/\.c4-relationship-line\b/);
    expect(css).not.toMatch(/\.c4-relationship-line line\s*\{/);
  });

  it("anchors a relationship on the boxes' edges, not their centres", () => {
    // Arrange.
    // A line drawn centre-to-centre disappears under the boxes at both ends.
    const { container } = render(<C4Canvas {...props} />);

    // Act.
    // Drawn as a path rather than a line, so the same element can carry a curve if C4 ever
    // wants one - the geometry is shared with the mindmap, only the shape chosen differs.
    // "M x1 y1 L x2 y2" - read positionally rather than by regex, which is one fewer thing to
    // get subtly wrong in a test that exists to catch subtle wrongness.
    const drawn = container.querySelector("path.c4-relationship-line")!.getAttribute("d")!.split(/\s+/);
    expect(drawn[0]).toBe("M");
    expect(drawn[3]).toBe("L");
    const y1 = Number(drawn[2]);
    const y2 = Number(drawn[5]);

    // Assert.
    // Alpha is centred at (0,0) and is 80 tall, so the line leaves at its lower edge.
    expect(y1).toBeCloseTo(40, 5);
    // Beta is centred at (0,200), so the line arrives at its upper edge.
    expect(y2).toBeCloseTo(160, 5);
  });

  it("draws a boundary as a labelled dashed rectangle", () => {
    // Arrange.
    currentModel = seed(
      node("a", "Alpha", 0, 0),
      element("boundary:s", BOUNDARY_TYPE, toBinary(C4BoundaryPayloadSchema, create(C4BoundaryPayloadSchema, {
        name: "Internet Banking",
        kind: "Software System",
        width: 400,
        height: 300,
      }))),
    );

    const { container } = render(<C4Canvas {...props} />);

    // Act and assert, step by step.
    const boundary = container.querySelector(".c4-boundary")!;
    expect(boundary.textContent).toContain("Internet Banking [Software System]");
    expect(boundary.querySelector("rect")!.getAttribute("width")).toBe("400");
  });

  it("draws a person with the person shape and a data store as a cylinder", () => {
    // Arrange.
    currentModel = seed(
      node("p", "Customer", 0, 0, { style: { background: "#08427b", color: "#ffffff", shape: "Person" } }),
      node("d", "Database", 0, 200, { style: { background: "#438dd5", color: "#ffffff", shape: "Cylinder" } }),
    );

    const { container } = render(<C4Canvas {...props} />);

    // Act and assert, step by step.
    const [person, store] = [...container.querySelectorAll(".c4-node")];
    expect(person.querySelector("circle")).not.toBeNull();
    expect(store.querySelectorAll("ellipse")).toHaveLength(2);
  });

  it("uses the palette the backend resolved, so a themed model renders in its own colours", () => {
    // Arrange.
    currentModel = seed(node("a", "Alpha", 0, 0, { style: { background: "#ff0000", color: "#000000", shape: "RoundedBox" } }));

    // Act.
    const { container } = render(<C4Canvas {...props} />);

    // Assert.
    expect(container.querySelector(".c4-node rect")!.getAttribute("fill")).toBe("#ff0000");
  });

  it("reports a nested file->element selection when an element is clicked", () => {
    // Arrange.
    const { container } = render(<C4Canvas {...props} />);

    // Act.
    press(container.querySelectorAll(".c4-node")[0]);

    // Assert.
    expect(select).toHaveBeenCalledTimes(1);
    const selection = select.mock.calls[0][0];
    expect(selection.id.source.value.value).toEqual(props.entryId);
    expect(selection.detail.value.id.source.value.value).toBe("a");
    // Empty asks the backend to fill the path in; a partial one is refused.
    expect(selection.detail.value.path.segments).toEqual([]);
  });

  it("clicking the empty canvas deselects", () => {
    // Arrange.
    const { container } = render(<C4Canvas {...props} />);
    press(container.querySelectorAll(".c4-node")[0]);
    select.mockClear();

    // Act.
    press(container.querySelector(".library-canvas-surface")!);

    // Assert.
    expect(select).toHaveBeenCalledWith(null);
  });

  it.each([
    ["unavailable", () => (currentFailed = true)],
    ["loading", () => (currentLoading = true)],
  ])("while %s, draws no status of its own and no diagram: the library's frame says it", (_state, arrange) => {
    // Arrange: client-centralization Requirement 2.3 - one appearance, drawn by the library around
    // every canvas. This canvas had its own loading and unavailable blocks.
    arrange();
    currentModel = emptyModel;

    // Act.
    const { container } = render(<C4Canvas {...props} />);

    // Assert.
    expect(container.textContent).not.toContain("no longer available");
    expect(container.querySelector('[role="status"], [role="alert"]')).toBeNull();
    expect(container.querySelectorAll(".c4-node")).toHaveLength(0);
  });

  // ---- pan, zoom and the reported viewport ---------------------------------------------

  const viewBoxOf = (container: HTMLElement) =>
    (container.querySelector(".library-canvas-surface")!.getAttribute("viewBox") ?? "").split(" ").map(Number);

  it("zooms in about the pointer on a wheel up, and back out on a wheel down", () => {
    // Arrange.
    const { container } = render(<C4Canvas {...props} />);
    const surface = container.querySelector(".library-canvas-surface")!;
    const [, , wBefore] = viewBoxOf(container);

    // Act and assert, step by step.
    fireEvent.wheel(surface, { deltaY: -100 });
    expect(viewBoxOf(container)[2]).toBeLessThan(wBefore);

    fireEvent.wheel(surface, { deltaY: 100 });
    expect(viewBoxOf(container)[2]).toBeCloseTo(wBefore, 5);
  });

  // ---- scrollbars: each test is named for the defect it catches -------------------------

  const thumbOf = (container: HTMLElement, axis: "horizontal" | "vertical") =>
    container.querySelector(`.canvas-scrollbar-${axis} .canvas-scrollbar-thumb`) as HTMLElement;

  it("pans the view when a thumb is dragged - catches an unwired onPan", () => {
    // Arrange.
    const { container } = render(<C4Canvas {...props} />);
    const [xBefore, yBefore] = viewBoxOf(container);

    // Act.
    fireEvent.mouseDown(thumbOf(container, "horizontal"), { button: 0, clientX: 10, clientY: 0 });
    fireEvent.mouseMove(window, { clientX: 40, clientY: 0 });
    fireEvent.mouseUp(window);

    // Assert.
    // Dragging the thumb to the right moves the view to the right; the magnitude is the
    // component's business, the direction is the wiring's.
    expect(viewBoxOf(container)[0]).toBeGreaterThan(xBefore);

    // Act, vertically.
    fireEvent.mouseDown(thumbOf(container, "vertical"), { button: 0, clientX: 0, clientY: 10 });
    fireEvent.mouseMove(window, { clientX: 0, clientY: 40 });
    fireEvent.mouseUp(window);

    // Assert.
    expect(viewBoxOf(container)[1]).toBeGreaterThan(yBefore);
  });

  it("moves the thumb when the view is panned by other means - catches a stale copy of the view", () => {
    // Arrange.
    // The bars must read the same view state the canvas pans, not a private copy taken once.
    const { container } = render(<C4Canvas {...props} />);
    const surface = container.querySelector(".library-canvas-surface")!;
    const before = thumbOf(container, "horizontal").style.left;

    // Act.
    fireEvent(surface, pointer("pointerdown", { button: 0, clientX: 200, clientY: 100 }));
    fireEvent(surface, pointer("pointermove", { clientX: 60, clientY: 100 }));
    fireEvent(surface, pointer("pointerup", { clientX: 60, clientY: 100 }));

    // Assert.
    expect(thumbOf(container, "horizontal").style.left).not.toBe(before);
  });

  it("resizes the thumb when the view is zoomed - catches a hard-coded viewSpan", () => {
    // Arrange.
    // A thumb that only moves reports position while hiding magnification, which is what a
    // hard-coded span produces.
    const { container } = render(<C4Canvas {...props} />);
    const surface = container.querySelector(".library-canvas-surface")!;
    const before = thumbOf(container, "horizontal").style.width;

    // Act.
    fireEvent.wheel(surface, { deltaY: -100 });

    // Assert.
    expect(thumbOf(container, "horizontal").style.width).not.toBe(before);
  });

  it("selecting after a drag whose pointer left the surface still selects the next element clicked", () => {
    // Arrange: the reported defect. A drag of Alpha released away from where it began used to
    // arm a suppress-the-next-click flag with no consumer, which then swallowed the next
    // legitimate selection. Seen to fail against that flag before the arbiter replaced it.
    const { container } = render(<C4Canvas {...props} />);
    const [alpha, beta] = Array.from(container.querySelectorAll(".c4-node"));

    // Act: press Alpha, drag well past the threshold, release far outside the surface - the
    // capture delivers the off-surface release to Alpha, exactly as a browser would.
    fireEvent(alpha, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
    fireEvent(alpha, pointer("pointermove", { clientX: 60, clientY: 60 }));
    fireEvent(alpha, pointer("pointerup", { clientX: 500, clientY: 500 }));

    // Act, continued: the browser's trailing click, wherever it happens to land, is inert -
    // nothing on this canvas listens to click - and the next press on Beta selects Beta.
    fireEvent.click(beta);
    press(beta, { clientX: 500, clientY: 500 });

    // Assert: one selection, naming "b" - the drag selected nothing, the click undid nothing.
    expect(select).toHaveBeenCalledTimes(1);
    const chain = select.mock.calls[0][0];
    expect(chain.detail.value.id.source.value.value).toBe("b");
  });

  it("pans with a background drag, and the trailing click does not deselect", () => {
    // Arrange.
    const { container } = render(<C4Canvas {...props} />);
    const surface = container.querySelector(".library-canvas-surface")!;
    const [xBefore] = viewBoxOf(container);

    // Act.
    fireEvent(surface, pointer("pointerdown", { button: 0, clientX: 100, clientY: 100 }));
    fireEvent(surface, pointer("pointermove", { clientX: 60, clientY: 100 }));
    fireEvent(surface, pointer("pointerup", { clientX: 60, clientY: 100 }));
    fireEvent.click(surface);

    // Assert.
    expect(viewBoxOf(container)[0]).toBeCloseTo(xBefore + 40, 5);
    expect(select).not.toHaveBeenCalled();
  });

  it("reports the viewBox as the viewport, the contract the reference canvases set", async () => {
    // Recorded unification: the hand-built canvas aspect-corrected its report through
    // shownRectOf; the library raises one view-changed signal carrying the viewBox, and the
    // module reports it verbatim - exactly as the rdf and timeline references do.
    const reportView = vi.fn();
    currentReportView = reportView;
    try {
      const { container } = render(<C4Canvas {...props} />);
      const [boxX, boxY, boxW, boxH] = viewBoxOf(container);

      await waitFor(() => expect(reportView).toHaveBeenCalled(), { timeout: 2000 });

      const viewport = reportView.mock.calls.at(-1)![0];
      expect(viewport.minX).toBeCloseTo(boxX, 5);
      expect(viewport.minY).toBeCloseTo(boxY, 5);
      expect(viewport.maxX).toBeCloseTo(boxX + boxW, 5);
      expect(viewport.maxY).toBeCloseTo(boxY + boxH, 5);
    } finally {
      currentReportView = null;
    }
  });

  // ---- editing: the toolbox and the context menu -----------------------------------------

  /** The drag the Toolbox panel starts, carrying the backend's own action id and nothing else. */
  function toolboxDrag(actionId: string) {
    return {
      dataTransfer: {
        types: ["application/x-adp-toolbox-item"],
        getData: () => actionId,
        dropEffect: "",
      },
    };
  }

  /** A drop on the library surface, at a canvas point, built by hand - jsdom has no DragEvent. */
  function dropAt(container: HTMLElement, actionId: string, canvasX: number, canvasY: number) {
    const surface = container.querySelector("svg.library-canvas-surface")!;
    const box = surface.getAttribute("viewBox")!.split(" ").map(Number);
    Object.defineProperty(surface, "getBoundingClientRect", {
      value: () => ({ x: 0, y: 0, top: 0, left: 0, right: box[2], bottom: box[3], width: box[2], height: box[3], toJSON: () => ({}) }),
      configurable: true,
    });
    const event = new MouseEvent("drop", { bubbles: true, cancelable: true, clientX: canvasX - box[0], clientY: canvasY - box[1] });
    Object.defineProperty(event, "dataTransfer", { value: toolboxDrag(actionId).dataTransfer });
    fireEvent(surface, event);
  }

  it("runs the backend's own action when a toolbox entry is dropped on an element", () => {
    // The panel tells the canvas an action id and nothing more; what it means stays the
    // backend's business. Dropping on an element is how C4 containment gets decided - the
    // element becomes the new one's parent. (The per-element hover highlight during an HTML5
    // drag is a recorded loss with the migration; the drop's target is decided the same way.)
    const { container } = render(<C4Canvas {...props} />);

    // Act: dropped where Alpha sits.
    dropAt(container, "c4.add-container", 0, 0);

    // Assert.
    expect(executed).toEqual(["c4.add-container"]);
  });

  it("runs the action with no element when a toolbox entry is dropped on empty canvas", () => {
    // No parent, so only what stands on its own can land. The backend refuses the rest and
    // says where it should have gone - the canvas does not second-guess it.
    const { container } = render(<C4Canvas {...props} />);

    // Act: dropped far from every box.
    dropAt(container, "c4.add-softwaresystem", -110, -110);

    // Assert.
    expect(executed).toEqual(["c4.add-softwaresystem"]);
  });

  it("ignores a drag that is not from the toolbox", () => {
    // Arrange.
    const { container } = render(<C4Canvas {...props} />);
    const surface = container.querySelector("svg.library-canvas-surface")!;

    // Act.
    const event = new MouseEvent("drop", { bubbles: true, cancelable: true, clientX: 0, clientY: 0 });
    Object.defineProperty(event, "dataTransfer", { value: { types: ["text/plain"], getData: () => "", dropEffect: "" } });
    fireEvent(surface, event);

    // Assert.
    expect(executed).toEqual([]);
  });

  it("selects with the menu gesture on right-click, rather than opening a menu of its own", () => {
    // Arrange.
    // The menu shows the backend's answer: the canvas asks for the selection and waits for the
    // actions to arrive rather than guessing what a C4 element offers.
    const { container } = render(<C4Canvas {...props} />);
    const alpha = container.querySelectorAll(".c4-node")[0];

    // Act.
    fireEvent.contextMenu(alpha);

    // Assert.
    expect(select).toHaveBeenCalled();
    expect(container.querySelector(".context-menu")).toBeNull();
  });


  // ---- dragging an element ----------------------------------------------------------------

  /**
   * jsdom gives every element a zero-sized bounding rect, so the canvas would compute one
   * canvas unit per pixel from nothing. Pinned to a real width instead, which makes the
   * arithmetic in these tests the arithmetic the browser would do.
   */
  function withSurfaceWidth(container: HTMLElement, width: number) {
    const surface = container.querySelector(".library-canvas-surface")!;
    surface.getBoundingClientRect = () => ({ width, height: width, x: 0, y: 0, top: 0, left: 0, right: width, bottom: width, toJSON: () => ({}) });
    return surface;
  }

  it("records where an element was dropped, in canvas units", () => {
    // Arrange.
    // Alpha starts at (0,0). The view is 1000 units wide over 500 pixels, so one pixel is two
    // canvas units and a 50-pixel drag is a 100-unit move.
    const { container } = render(<C4Canvas {...props} />);
    withSurfaceWidth(container, 500);
    const alpha = container.querySelectorAll(".c4-node")[0];

    // Act.
    fireEvent(alpha, pointer("pointerdown", { button: 0, clientX: 100, clientY: 100 }));
    fireEvent(alpha, pointer("pointermove", { clientX: 150, clientY: 100 }));
    fireEvent(alpha, pointer("pointerup", { clientX: 150, clientY: 100 }));

    // Assert.
    const move = moves[0];
    expect(move?.elementId).toBe("a");
    expect(move.x).toBeGreaterThan(0);
    expect(move.y).toBeCloseTo(0, 5);
  });

  it("draws no refusal line of its own for a refused move: the move reports it to the library's", async () => {
    // Arrange: the backend refuses to record the position. The move itself reports the sentence to
    // the one line the library draws around every canvas (client-centralization Requirement 2;
    // useDiagramStream.move.test.ts). Here: this canvas adds none.
    moveOutcome = "An element inside a boundary is placed by its boundary.";
    const { container } = render(<C4Canvas {...props} />);
    withSurfaceWidth(container, 500);
    const alpha = container.querySelectorAll(".c4-node")[0];

    // Act.
    fireEvent(alpha, pointer("pointerdown", { button: 0, clientX: 100, clientY: 100 }));
    fireEvent(alpha, pointer("pointermove", { clientX: 150, clientY: 100 }));
    fireEvent(alpha, pointer("pointerup", { clientX: 150, clientY: 100 }));

    await act(async () => {});

    // Assert.
    expect(moves).toHaveLength(1);
    expect(container.textContent).not.toContain("An element inside a boundary is placed by its boundary.");
    expect(container.querySelector(".canvas-rejection")).toBeNull();

    moveOutcome = "";
  });

  it("writes nothing for a wobbly click", () => {
    // Arrange.
    // A few pixels of movement while clicking is a click. Sending it would put an entry on the
    // project history for having pressed the mouse.
    const { container } = render(<C4Canvas {...props} />);
    withSurfaceWidth(container, 500);
    const alpha = container.querySelectorAll(".c4-node")[0];

    // Act.
    fireEvent(alpha, pointer("pointerdown", { button: 0, clientX: 100, clientY: 100 }));
    fireEvent(alpha, pointer("pointermove", { clientX: 102, clientY: 101 }));
    fireEvent(alpha, pointer("pointerup", { clientX: 102, clientY: 101 }));

    // Assert.
    expect(moves).toEqual([]);
  });

  it("moves the element under the pointer while the button is down", () => {
    // Arrange.
    // The drop's outcome should be visible during the drag, not only after the backend answers.
    const { container } = render(<C4Canvas {...props} />);
    withSurfaceWidth(container, 500);
    const alpha = container.querySelectorAll(".c4-node")[0];
    const before = alpha.getAttribute("transform");

    // Act.
    fireEvent(alpha, pointer("pointerdown", { button: 0, clientX: 100, clientY: 100 }));
    fireEvent(alpha, pointer("pointermove", { clientX: 200, clientY: 160 }));

    // Assert.
    const during = container.querySelectorAll(".c4-node")[0].getAttribute("transform");
    expect(during).not.toBe(before);
    expect(container.querySelector(".c4-node-dragging")).toBeTruthy();
    expect(container.querySelector(".library-element-dragging")).toBeTruthy();
  });

  it("does not treat a right-click as the start of a drag", () => {
    // Arrange.
    // Right-click is the menu's gesture. Starting a drag on it would make every context menu
    // a potential accidental move.
    const { container } = render(<C4Canvas {...props} />);
    withSurfaceWidth(container, 500);
    const alpha = container.querySelectorAll(".c4-node")[0];

    // Act.
    fireEvent(alpha, pointer("pointerdown", { button: 2, clientX: 100, clientY: 100 }));
    fireEvent(alpha, pointer("pointermove", { clientX: 200, clientY: 200 }));
    fireEvent(alpha, pointer("pointerup", { clientX: 200, clientY: 200 }));

    // Assert.
    expect(moves).toEqual([]);
  });

  it("does not re-select the element on the click that trails a drag", () => {
    // Arrange.
    const { container } = render(<C4Canvas {...props} />);
    withSurfaceWidth(container, 500);
    const alpha = container.querySelectorAll(".c4-node")[0];

    // Act.
    fireEvent(alpha, pointer("pointerdown", { button: 0, clientX: 100, clientY: 100 }));
    fireEvent(alpha, pointer("pointermove", { clientX: 200, clientY: 200 }));
    fireEvent(alpha, pointer("pointerup", { clientX: 200, clientY: 200 }));
    select.mockClear();
    fireEvent.click(alpha);

    // Assert.
    // The browser fires a click when a drag ends; it is inert here because nothing on this
    // canvas listens to click - there is no flag to arm, and none to leak.
    expect(select).not.toHaveBeenCalled();
  });

  // ---- inline renaming ---------------------------------------------------------------------

  /** A marked prompt for one element or relationship - the shape the backend now sends. */
  function labelPromptFor(elementId: string, initialValue: string): ContextPrompt {
    return create(ContextPromptSchema, {
      interactionId: { value: new Uint8Array(16).fill(9) },
      prompt: {
        case: "inputDialog",
        value: {
          title: "Rename element",
          icon: "mdi-pencil-outline",
          fieldLabel: "Name",
          initialValue,
          confirmLabel: "Rename",
          inlineLabelEdit: { elementId: { value: elementId } },
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

  it("renames an element in place, over its name line rather than over its whole box", async () => {
    // Arrange.
    currentPrompt = labelPromptFor("a", "Alpha");
    const { container } = render(<C4Canvas {...props} />);

    // Assert, first: the editor covers the name, not the type line and description under it.
    // A box-sized editor would sit over three lines of text to edit one of them.
    const box = editorBox(container);
    expect(box).not.toBeNull();
    expect(Number(box.getAttribute("y"))).toBeCloseTo(0 - 80 / 2 + 6, 5);
    expect(Number(box.getAttribute("height"))).toBeCloseTo(20, 5);

    // Act.
    fireEvent.change(labelField(container), { target: { value: "Alpha Prime" } });
    await act(async () => {
      fireEvent.keyDown(labelField(container), { key: "Enter" });
    });

    // Assert.
    expect(submitLabel).toHaveBeenCalledWith("Alpha Prime");
  });

  it("places a relationship's editor at the line's midpoint, where its label is drawn", () => {
    // Arrange.
    // The fixture's two boxes are centred at (0, 0) and (0, 200) and are 80 tall, so the line
    // runs from (0, 40) to (0, 160) and its midpoint is (0, 100). The label sits six above it.
    currentPrompt = labelPromptFor("a->b", "Uses");

    // Act.
    const { container } = render(<C4Canvas {...props} />);

    // Assert.
    // Centred on the midpoint, using the per-character fallback width - jsdom implements no
    // getBBox, so this is the estimate path, which is exactly the one that runs in a test.
    const box = editorBox(container);
    expect(box).not.toBeNull();
    const x = Number(box.getAttribute("x"));
    const width = Number(box.getAttribute("width"));
    expect(x + width / 2).toBeCloseTo(0, 5);
    expect(Number(box.getAttribute("y"))).toBeCloseTo(100 - 6 - 16, 5);
  });

  it("opens a relationship's editor on the description alone, not on the label drawn with its technology", () => {
    // Arrange.
    // The arrow reads "Uses [HTTPS]". The technology is decoration around one authored value
    // and has an action of its own; typing over the rendered string would put the technology
    // into the description, after which it appears twice.
    currentPrompt = labelPromptFor("a->b", "Uses");

    // Act.
    const { container } = render(<C4Canvas {...props} />);

    // Assert.
    expect(labelField(container).value).toBe("Uses");
    expect(labelField(container).value).not.toContain("HTTPS");
  });

  // ---- selecting a relationship --------------------------------------------------------------

  function relationshipGroup(container: HTMLElement, id: string): SVGGElement {
    return container.querySelector(`[data-connection-id="${id}"]`) as SVGGElement;
  }

  it("selects a relationship when its line is clicked", () => {
    // Arrange.
    // Until this worked, nothing on this canvas could select a relationship at all: the backend
    // offered relabel and set-technology on one, and no gesture could reach either.
    const { container } = render(<C4Canvas {...props} />);

    // Act.
    press(relationshipGroup(container, "a->b"));

    // Assert.
    // A nested selection, the same shape an element reports: the .adp file, then the
    // relationship as its child.
    expect(select).toHaveBeenCalled();
    const selection = select.mock.calls.at(-1)![0] as { detail: { value: { id: { source: { value: { value: string } } } } } };
    expect(selection.detail.value.id.source.value.value).toBe("a->b");
  });

  it("gives a relationship an invisible hit path, because a dashed line is not a target", () => {
    // Arrange, act.
    const { container } = render(<C4Canvas {...props} />);

    // Assert.
    // The shared class, so the grab width is the same on every canvas that draws a line. Its
    // absence is the defect: a 1.5px dashed stroke is unhittable in practice, and a test that
    // clicks the group programmatically would never notice.
    const hit = relationshipGroup(container, "a->b").querySelector("path.canvas-connection-hit");
    expect(hit).not.toBeNull();
    expect(hit!.getAttribute("d")).toMatch(/^M /);
  });

  it("does not let a press on a relationship start a background pan", () => {
    // Arrange.
    const { container } = render(<C4Canvas {...props} />);
    const surface = container.querySelector("svg.library-canvas-surface") as SVGSVGElement;
    const viewBoxBefore = surface.getAttribute("viewBox");

    // Act.
    fireEvent(relationshipGroup(container, "a->b"), pointer("pointerdown", { button: 0, clientX: 200, clientY: 200 }));
    fireEvent(surface, pointer("pointermove", { clientX: 320, clientY: 260 }));
    fireEvent(surface, pointer("pointerup", { clientX: 320, clientY: 260 }));

    // Assert.
    // A user-facing property rather than a guard over one line: pressing a relationship must
    // not also drag the view away from it. What holds it is the arbiter running one gesture
    // at a time - the press's own stopPropagation hands the gesture to the relationship,
    // whose drag deliberately moves nothing - even with the moves bubbling via the surface.
    expect(surface.getAttribute("viewBox")).toBe(viewBoxBefore);
  });

  it("marks the selected relationship, so which one is selected is visible", () => {
    // Arrange: the highlight is the backend's pushed selection, as everywhere on the library.
    currentSelection = {
      id: { source: { case: "entryId", value: { value: props.entryId } } },
      detail: { case: "child", value: { id: { source: { case: "elementId", value: { value: "a->b" } } }, detail: { case: "none" } } },
    };
    const { container } = render(<C4Canvas {...props} />);

    // Assert.
    expect(relationshipGroup(container, "a->b").getAttribute("class")).toContain("canvas-selected");
  });
});

describe("selection, as every canvas has it", () => {
  const idOf = (push: unknown) => (push === null ? null : (selectedElementIdOf(push as ContextSelection) ?? null));

  it("highlights a pushed element and relationship, and clears on a background press (centralized-selection 9.2)", () => {
    expectLibrarySelection({
      mountWith: (id) => {
        currentSelection = id === null ? null : elementSelectionOf(props.entryId, props.path, id);
        return render(<C4Canvas {...props} />);
      },
      pushedIds: () => select.mock.calls.map(([push]) => idOf(push)),
      element: "a",
      connection: "a->b",
    });
  });

  it("never selects a boundary: a pushed one highlights nothing, and a press on one clears (Requirement 2.3)", () => {
    // The inert box, declared `selectable: false` rather than refused by hand in a handler.
    currentModel = seed(
      node("a", "Alpha", 0, 0),
      element("boundary:s", BOUNDARY_TYPE, toBinary(C4BoundaryPayloadSchema, create(C4BoundaryPayloadSchema, {
        name: "Internet Banking",
        kind: "Software System",
        width: 400,
        height: 300,
      }))),
    );
    currentSelection = elementSelectionOf(props.entryId, props.path, "boundary:s");
    const { container } = render(<C4Canvas {...props} />);
    const boundary = container.querySelector('[data-element-id="boundary:s"]')!;
    expect(boundary.classList.contains("canvas-selected")).toBe(false);

    select.mockClear();
    fireEvent(boundary, new MouseEvent("pointerdown", { bubbles: true, cancelable: true, button: 0 }));
    fireEvent(boundary, new MouseEvent("pointerup", { bubbles: true, cancelable: true }));

    expect(select.mock.calls.map(([push]) => idOf(push))).toEqual([null]);
  });
});

describe("a refused action", () => {
  it("draws no refusal line of its own: the library shows a refused keystroke", async () => {
    // Arrange: an element is selected, and the backend refuses what is asked of it. The library
    // sends the declared key and its call reports the refusal to the one line drawn around every
    // canvas (client-centralization Requirement 2; contextConnectionReportsToCanvas.test.tsx holds
    // the report of a keystroke). Here: none.
    currentModel = seed(node("a", "Alpha", 0, 0));
    currentSelection = elementSelectionOf(props.entryId, props.path, "a");
    shortcutOutcome = { accepted: false, error: "Nothing can be inserted inside a person." };
    const { container } = render(<C4Canvas {...props} />);

    // Act.
    fireEvent.keyDown(container.querySelector(".library-canvas-surface")!, { key: "Insert" });
    await act(async () => {});

    // Assert.
    expect(container.textContent).not.toContain("Nothing can be inserted inside a person.");
    expect(container.querySelector(".canvas-rejection")).toBeNull();

    shortcutOutcome = { accepted: true, error: "" };
  });
});
