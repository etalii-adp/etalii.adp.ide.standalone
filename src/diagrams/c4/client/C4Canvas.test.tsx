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

const select = vi.fn();
let currentModel: C4Model = emptyModel;
let currentLoading = false;
let currentFailed = false;
let currentReportView: ((viewport: unknown) => void) | null = null;

vi.mock("./useC4Stream", () => ({
  useC4Stream: () => ({
    model: currentModel,
    loading: currentLoading,
    failed: currentFailed,
    reportView: (v: unknown) => currentReportView?.(v),
    moveElementTo: (elementId: string, x: number, y: number) => {
      moves.push({ elementId, x, y });
      return Promise.resolve("");
    },
  }),
}));

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
      executeShortcut: () => Promise.resolve({ accepted: true, error: "" }),
    }),
    useContextSelection: () => ({ selection: null, actions: [] }),
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

describe("C4Canvas", () => {
  beforeEach(() => {
    select.mockClear();
    executed.length = 0;
    moves.length = 0;
    currentLoading = false;
    currentFailed = false;
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
    const relationships = container.querySelectorAll(".c4-relationship");
    expect(relationships).toHaveLength(1);
    expect(relationships[0].querySelector("path")!.getAttribute("marker-end")).toBe("url(#c4-arrow)");
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
    expect(container.querySelector(".c4-relationship path")).not.toBeNull();
    expect(css).toMatch(/\.c4-relationship path\s*\{/);
    expect(css).not.toMatch(/\.c4-relationship line\s*\{/);
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
    const drawn = container.querySelector(".c4-relationship path")!.getAttribute("d")!.split(/\s+/);
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
    fireEvent.click(container.querySelectorAll(".c4-node")[0]);

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
    fireEvent.click(container.querySelectorAll(".c4-node")[0]);
    select.mockClear();

    // Act.
    fireEvent.click(container.querySelector(".c4-canvas-surface")!);

    // Assert.
    expect(select).toHaveBeenCalledWith(null);
  });

  it("says the diagram is no longer available, naming its path, when the stream failed", () => {
    // Arrange.
    currentFailed = true;

    // Act.
    const { container } = render(<C4Canvas {...props} />);

    // Assert.
    expect(container.textContent).toContain("This diagram is no longer available at docs/model.adp.");
    expect(container.querySelectorAll(".c4-node")).toHaveLength(0);
  });

  it("shows it is loading rather than an empty diagram", () => {
    // Arrange.
    currentLoading = true;

    // Act.
    const { container } = render(<C4Canvas {...props} />);

    // Assert.
    expect(container.querySelector('[role="status"]')).not.toBeNull();
  });

  // ---- pan, zoom and the reported viewport ---------------------------------------------

  const viewBoxOf = (container: HTMLElement) =>
    (container.querySelector(".c4-canvas-surface")!.getAttribute("viewBox") ?? "").split(" ").map(Number);

  it("zooms in about the pointer on a wheel up, and back out on a wheel down", () => {
    // Arrange.
    const { container } = render(<C4Canvas {...props} />);
    const surface = container.querySelector(".c4-canvas-surface")!;
    const [, , wBefore] = viewBoxOf(container);

    // Act and assert, step by step.
    fireEvent.wheel(surface, { deltaY: -100 });
    expect(viewBoxOf(container)[2]).toBeLessThan(wBefore);

    fireEvent.wheel(surface, { deltaY: 100 });
    expect(viewBoxOf(container)[2]).toBeCloseTo(wBefore, 5);
  });

  it("pans with a background drag, and the trailing click does not deselect", () => {
    // Arrange.
    const { container } = render(<C4Canvas {...props} />);
    const surface = container.querySelector(".c4-canvas-surface")!;
    const [xBefore] = viewBoxOf(container);

    // Act.
    fireEvent.mouseDown(surface, { clientX: 100, clientY: 100 });
    fireEvent.mouseMove(surface, { clientX: 60, clientY: 100 });
    fireEvent.mouseUp(surface);
    fireEvent.click(surface);

    // Assert.
    expect(viewBoxOf(container)[0]).toBeCloseTo(xBefore + 40, 5);
    expect(select).not.toHaveBeenCalled();
  });

  it("reports the area the svg actually shows, not the bare viewBox", async () => {
    // Arrange.
    // The svg letterboxes: whichever axis has room to spare displays more of the model than
    // the box asks for, and reporting the box alone would have the backend cull elements the
    // user is looking straight at.
    const reportView = vi.fn();
    currentReportView = reportView;
    const measure = vi.spyOn(Element.prototype, "getBoundingClientRect").mockReturnValue({
      x: 0, y: 0, width: 800, height: 200, top: 0, left: 0, right: 800, bottom: 200, toJSON: () => ({}),
    } as DOMRect);
    try {
      const { container } = render(<C4Canvas {...props} />);
      const [, , boxW, boxH] = viewBoxOf(container);

    // Act and assert, step by step.
      await waitFor(() => expect(reportView).toHaveBeenCalled(), { timeout: 2000 });

      const viewport = reportView.mock.calls.at(-1)![0];
      const scale = Math.min(800 / boxW, 200 / boxH);
      expect(viewport.maxX - viewport.minX).toBeCloseTo(800 / scale, 5);
      expect(viewport.maxY - viewport.minY).toBeCloseTo(200 / scale, 5);
    } finally {
      measure.mockRestore();
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

  it("runs the backend's own action when a toolbox entry is dropped on an element", () => {
    // Arrange.
    // The panel tells the canvas an action id and nothing more; what it means stays the
    // backend's business. Dropping on an element is how C4 containment gets decided - the
    // element becomes the new one's parent.
    const { container } = render(<C4Canvas {...props} />);
    const alpha = container.querySelectorAll(".c4-node")[0];

    // Act.
    fireEvent.drop(alpha, toolboxDrag("c4.add-container"));

    // Assert.
    expect(executed).toEqual(["c4.add-container"]);
  });

  it("runs the action with no element when a toolbox entry is dropped on empty canvas", () => {
    // Arrange.
    // No parent, so only what stands on its own can land. The backend refuses the rest and
    // says where it should have gone - the canvas does not second-guess it.
    const { container } = render(<C4Canvas {...props} />);
    const surface = container.querySelector(".c4-canvas-surface")!;

    // Act.
    fireEvent.drop(surface, toolboxDrag("c4.add-softwaresystem"));

    // Assert.
    expect(executed).toEqual(["c4.add-softwaresystem"]);
  });

  it("highlights the element a toolbox entry is held over, before the drop", () => {
    // Arrange.
    // The outcome of the drop should be visible while the button is still down.
    const { container } = render(<C4Canvas {...props} />);
    const alpha = container.querySelectorAll(".c4-node")[0];

    // Act.
    fireEvent.dragOver(alpha, toolboxDrag("c4.add-container"));

    // Assert.
    expect(container.querySelector(".c4-node-drop-target")).toBeTruthy();
  });

  it("ignores a drag that is not from the toolbox", () => {
    // Arrange.
    const { container } = render(<C4Canvas {...props} />);
    const alpha = container.querySelectorAll(".c4-node")[0];

    // Act.
    fireEvent.dragOver(alpha, { dataTransfer: { types: ["text/plain"], getData: () => "", dropEffect: "" } });

    // Assert.
    expect(container.querySelector(".c4-node-drop-target")).toBeNull();
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
    const surface = container.querySelector(".c4-canvas-surface")!;
    surface.getBoundingClientRect = () => ({ width, height: width, x: 0, y: 0, top: 0, left: 0, right: width, bottom: width, toJSON: () => ({}) });
    return surface;
  }

  it("records where an element was dropped, in canvas units", () => {
    // Arrange.
    // Alpha starts at (0,0). The view is 1000 units wide over 500 pixels, so one pixel is two
    // canvas units and a 50-pixel drag is a 100-unit move.
    const { container } = render(<C4Canvas {...props} />);
    const surface = withSurfaceWidth(container, 500);
    const alpha = container.querySelectorAll(".c4-node")[0];

    // Act.
    fireEvent.mouseDown(alpha, { button: 0, clientX: 100, clientY: 100 });
    fireEvent.mouseMove(surface, { clientX: 150, clientY: 100 });
    fireEvent.mouseUp(surface);

    // Assert.
    const move = moves[0];
    expect(move?.elementId).toBe("a");
    expect(move.x).toBeGreaterThan(0);
    expect(move.y).toBeCloseTo(0, 5);
  });

  it("writes nothing for a wobbly click", () => {
    // Arrange.
    // A few pixels of movement while clicking is a click. Sending it would put an entry on the
    // project history for having pressed the mouse.
    const { container } = render(<C4Canvas {...props} />);
    const surface = withSurfaceWidth(container, 500);
    const alpha = container.querySelectorAll(".c4-node")[0];

    // Act.
    fireEvent.mouseDown(alpha, { button: 0, clientX: 100, clientY: 100 });
    fireEvent.mouseMove(surface, { clientX: 102, clientY: 101 });
    fireEvent.mouseUp(surface);

    // Assert.
    expect(moves).toEqual([]);
  });

  it("moves the element under the pointer while the button is down", () => {
    // Arrange.
    // The drop's outcome should be visible during the drag, not only after the backend answers.
    const { container } = render(<C4Canvas {...props} />);
    const surface = withSurfaceWidth(container, 500);
    const alpha = container.querySelectorAll(".c4-node")[0];
    const before = alpha.getAttribute("transform");

    // Act.
    fireEvent.mouseDown(alpha, { button: 0, clientX: 100, clientY: 100 });
    fireEvent.mouseMove(surface, { clientX: 200, clientY: 160 });

    // Assert.
    const during = container.querySelectorAll(".c4-node")[0].getAttribute("transform");
    expect(during).not.toBe(before);
    expect(container.querySelector(".c4-node-dragging")).toBeTruthy();
  });

  it("does not treat a right-click as the start of a drag", () => {
    // Arrange.
    // Right-click is the menu's gesture. Starting a drag on it would make every context menu
    // a potential accidental move.
    const { container } = render(<C4Canvas {...props} />);
    const surface = withSurfaceWidth(container, 500);
    const alpha = container.querySelectorAll(".c4-node")[0];

    // Act.
    fireEvent.mouseDown(alpha, { button: 2, clientX: 100, clientY: 100 });
    fireEvent.mouseMove(surface, { clientX: 200, clientY: 200 });
    fireEvent.mouseUp(surface);

    // Assert.
    expect(moves).toEqual([]);
  });

  it("does not re-select the element on the click that trails a drag", () => {
    // Arrange.
    const { container } = render(<C4Canvas {...props} />);
    const surface = withSurfaceWidth(container, 500);
    const alpha = container.querySelectorAll(".c4-node")[0];

    // Act.
    fireEvent.mouseDown(alpha, { button: 0, clientX: 100, clientY: 100 });
    fireEvent.mouseMove(surface, { clientX: 200, clientY: 200 });
    fireEvent.mouseUp(surface);
    select.mockClear();
    fireEvent.click(alpha);

    // Assert.
    // A drag ends with a click event; taking it as a selection would fight whatever the drag
    // just did.
    expect(select).not.toHaveBeenCalled();
  });

});
