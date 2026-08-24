import { describe, expect, it, vi, beforeEach } from "vitest";
import { render, fireEvent, waitFor } from "@testing-library/react";
import { create, toBinary } from "@bufbuild/protobuf";
import { ElementSchema } from "../../../generated/elements_pb";
import {
  C4BoundaryPayloadSchema,
  C4ElementPayloadSchema,
  C4RelationshipPayloadSchema,
  C4ViewPayloadSchema,
} from "../../../generated/c4_pb";
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
  }),
}));

vi.mock("../../context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../../context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextConnection: () => ({ watchId: new Uint8Array(16), select }),
    useContextSelection: () => ({ selection: null, actions: [] }),
  };
});

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
    const { container } = render(<C4Canvas {...props} />);

    const nodes = container.querySelectorAll(".c4-node");
    expect(nodes).toHaveLength(2);
    const rect = nodes[0].querySelector("rect")!;
    expect(rect.getAttribute("width")).toBe("160");
    expect(rect.getAttribute("height")).toBe("80");
  });

  it("shows the three lines C4 asks for: name, bracketed type, and description", () => {
    const { container } = render(<C4Canvas {...props} />);

    expect(container.textContent).toContain("Alpha");
    expect(container.textContent).toContain("[Software System]");
    expect(container.textContent).toContain("A description.");
  });

  it("carries the title C4 requires on every diagram", () => {
    const { getByTestId } = render(<C4Canvas {...props} />);

    expect(getByTestId("c4-title").textContent).toBe("System Context diagram for Alpha");
  });

  it("carries a key explaining the notation, so the diagram reads without narrative", () => {
    const { getByTestId } = render(<C4Canvas {...props} />);

    const legend = getByTestId("c4-legend");
    expect(legend.textContent).toContain("Software System");
    expect(legend.textContent).toContain("Person");
  });

  it("draws each relationship as one arrow, labelled with its intent and technology", () => {
    const { container } = render(<C4Canvas {...props} />);

    const relationships = container.querySelectorAll(".c4-relationship");
    expect(relationships).toHaveLength(1);
    expect(relationships[0].querySelector("line")!.getAttribute("marker-end")).toBe("url(#c4-arrow)");
    expect(relationships[0].textContent).toContain("Uses [HTTPS]");
  });

  it("anchors a relationship on the boxes' edges, not their centres", () => {
    // A line drawn centre-to-centre disappears under the boxes at both ends.
    const { container } = render(<C4Canvas {...props} />);

    const line = container.querySelector(".c4-relationship line")!;
    // Alpha is centred at (0,0) and is 80 tall, so the line leaves at its lower edge.
    expect(Number(line.getAttribute("y1"))).toBeCloseTo(40, 5);
    // Beta is centred at (0,200), so the line arrives at its upper edge.
    expect(Number(line.getAttribute("y2"))).toBeCloseTo(160, 5);
  });

  it("draws a boundary as a labelled dashed rectangle", () => {
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

    const boundary = container.querySelector(".c4-boundary")!;
    expect(boundary.textContent).toContain("Internet Banking [Software System]");
    expect(boundary.querySelector("rect")!.getAttribute("width")).toBe("400");
  });

  it("draws a person with the person shape and a data store as a cylinder", () => {
    currentModel = seed(
      node("p", "Customer", 0, 0, { style: { background: "#08427b", color: "#ffffff", shape: "Person" } }),
      node("d", "Database", 0, 200, { style: { background: "#438dd5", color: "#ffffff", shape: "Cylinder" } }),
    );

    const { container } = render(<C4Canvas {...props} />);

    const [person, store] = [...container.querySelectorAll(".c4-node")];
    expect(person.querySelector("circle")).not.toBeNull();
    expect(store.querySelectorAll("ellipse")).toHaveLength(2);
  });

  it("uses the palette the backend resolved, so a themed model renders in its own colours", () => {
    currentModel = seed(node("a", "Alpha", 0, 0, { style: { background: "#ff0000", color: "#000000", shape: "RoundedBox" } }));

    const { container } = render(<C4Canvas {...props} />);

    expect(container.querySelector(".c4-node rect")!.getAttribute("fill")).toBe("#ff0000");
  });

  it("reports a nested file->element selection when an element is clicked", () => {
    const { container } = render(<C4Canvas {...props} />);

    fireEvent.click(container.querySelectorAll(".c4-node")[0]);

    expect(select).toHaveBeenCalledTimes(1);
    const selection = select.mock.calls[0][0];
    expect(selection.id.source.value.value).toEqual(props.entryId);
    expect(selection.detail.value.id.source.value.value).toBe("a");
    // Empty asks the backend to fill the path in; a partial one is refused.
    expect(selection.detail.value.path.segments).toEqual([]);
  });

  it("clicking the empty canvas deselects", () => {
    const { container } = render(<C4Canvas {...props} />);
    fireEvent.click(container.querySelectorAll(".c4-node")[0]);
    select.mockClear();

    fireEvent.click(container.querySelector(".c4-canvas-surface")!);

    expect(select).toHaveBeenCalledWith(null);
  });

  it("says the diagram is no longer available, naming its path, when the stream failed", () => {
    currentFailed = true;

    const { container } = render(<C4Canvas {...props} />);

    expect(container.textContent).toContain("This diagram is no longer available at docs/model.adp.");
    expect(container.querySelectorAll(".c4-node")).toHaveLength(0);
  });

  it("shows it is loading rather than an empty diagram", () => {
    currentLoading = true;

    const { container } = render(<C4Canvas {...props} />);

    expect(container.querySelector('[role="status"]')).not.toBeNull();
  });

  // ---- pan, zoom and the reported viewport ---------------------------------------------

  const viewBoxOf = (container: HTMLElement) =>
    (container.querySelector(".c4-canvas-surface")!.getAttribute("viewBox") ?? "").split(" ").map(Number);

  it("zooms in about the pointer on a wheel up, and back out on a wheel down", () => {
    const { container } = render(<C4Canvas {...props} />);
    const surface = container.querySelector(".c4-canvas-surface")!;
    const [, , wBefore] = viewBoxOf(container);

    fireEvent.wheel(surface, { deltaY: -100 });
    expect(viewBoxOf(container)[2]).toBeLessThan(wBefore);

    fireEvent.wheel(surface, { deltaY: 100 });
    expect(viewBoxOf(container)[2]).toBeCloseTo(wBefore, 5);
  });

  it("pans with a background drag, and the trailing click does not deselect", () => {
    const { container } = render(<C4Canvas {...props} />);
    const surface = container.querySelector(".c4-canvas-surface")!;
    const [xBefore] = viewBoxOf(container);

    fireEvent.mouseDown(surface, { clientX: 100, clientY: 100 });
    fireEvent.mouseMove(surface, { clientX: 60, clientY: 100 });
    fireEvent.mouseUp(surface);
    fireEvent.click(surface);

    expect(viewBoxOf(container)[0]).toBeCloseTo(xBefore + 40, 5);
    expect(select).not.toHaveBeenCalled();
  });

  it("reports the area the svg actually shows, not the bare viewBox", async () => {
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
});
