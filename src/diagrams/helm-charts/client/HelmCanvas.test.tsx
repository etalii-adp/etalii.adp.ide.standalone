import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { create, toBinary } from "@bufbuild/protobuf";
import { ElementSchema } from "@client/generated/elements_pb";
import {
  HelmEdgeKind,
  HelmElementKind,
  HelmElementPayloadSchema,
} from "@client/generated/helm-charts_pb";
import { VIEW_REPORT_DEBOUNCE_MS } from "@client/diagrams/viewReport";
import { applyDelta, emptyModel, type HelmModel } from "./helmModel";

const select = vi.fn();
const revealPath = vi.fn();
const moveElementTo = vi.fn(() => Promise.resolve(""));
const reportView = vi.fn();
let currentModel: HelmModel = emptyModel;
let currentLoading = false;
let currentFailed = false;
let currentSelection: unknown = null;

vi.mock("./useHelmStream", () => ({
  useHelmStream: () => ({
    model: currentModel,
    loading: currentLoading,
    failed: currentFailed,
    moveElementTo,
    reportView,
  }),
}));

vi.mock("@client/shell/context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@client/shell/context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextConnection: () => ({ watchId: new Uint8Array(16), select, revealPath }),
    useContextSelection: () => ({ selection: currentSelection }),
  };
});

const emptyPalette: never[] = [];

vi.mock("@client/shell/panels/useToolboxItems", () => ({
  useToolboxItems: () => emptyPalette,
}));

const { HelmCanvas } = await import("./HelmCanvas");

const TYPE_URL = "type.googleapis.com/etalii.adp.helm.HelmElementPayload";

function element(
  id: string,
  type: string,
  kind: HelmElementKind,
  overrides: Record<string, unknown> = {},
  x = 0,
  y = 0,
) {
  const payload = create(HelmElementPayloadSchema, {
    name: id.split(":").pop() ?? id,
    kind,
    width: 200,
    height: 60,
    ...overrides,
  });
  return create(ElementSchema, {
    id: { value: id },
    position: { x, y },
    type,
    payload: { typeUrl: TYPE_URL, value: toBinary(HelmElementPayloadSchema, payload) },
  });
}

function modelOf(...elements: ReturnType<typeof element>[]): HelmModel {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  return applyDelta(emptyModel, { action: { case: "add", value: { elements } } } as any);
}

function renderCanvas() {
  return render(<HelmCanvas projectId={new Uint8Array(16)} entryId={new Uint8Array(16)} path={["helm-chart.adp"]} />);
}

beforeEach(() => {
  select.mockClear();
  revealPath.mockClear();
  moveElementTo.mockClear();
  currentLoading = false;
  currentFailed = false;
  currentSelection = null;
  currentModel = modelOf(
    element("chart", "helm/chart+chart", HelmElementKind.CHART, { chartRelativePath: ["Chart.yaml"] }),
    element(
      "values:values.yaml",
      "helm/chart+values",
      HelmElementKind.VALUES,
      { chartRelativePath: ["values.yaml"] },
      0,
      120,
    ),
    element("dep:redis", "helm/chart+dependency", HelmElementKind.DEPENDENCY, {}, 400, 0),
  );
});

describe("HelmCanvas", () => {
  it("draws every node kind with a class of its own", () => {
    // Arrange.
    currentModel = modelOf(
      element("chart", "helm/chart+chart", HelmElementKind.CHART),
      element("values:values.yaml", "helm/chart+values", HelmElementKind.VALUES),
      element("schema:values.schema.json", "helm/chart+schema", HelmElementKind.SCHEMA),
      element("tpl:templates/d.yaml", "helm/chart+template", HelmElementKind.TEMPLATE),
      element("crds", "helm/chart+crds", HelmElementKind.CRDS),
      element("dep:redis", "helm/chart+dependency", HelmElementKind.DEPENDENCY),
      element("sub:charts/redis", "helm/chart+subchart", HelmElementKind.SUBCHART),
      element("tgz:charts/x.tgz", "helm/chart+archive", HelmElementKind.ARCHIVE),
      element("lock:Chart.lock", "helm/chart+lock", HelmElementKind.LOCK),
    );

    // Act.
    const { container } = renderCanvas();

    // Assert.
    for (const kind of ["chart", "values", "schema", "template", "crds", "dependency", "subchart", "archive", "lock"]) {
      expect(container.querySelector(`.helm-node-${kind}`), kind).not.toBeNull();
    }
  });

  it("draws an edge between delivered ends and a stub for an open end", () => {
    // Arrange.
    const resolved = create(HelmElementPayloadSchema, {
      name: "resolves",
      kind: HelmElementKind.EDGE,
      edge: { sourceId: "chart", targetId: "dep:redis", kind: HelmEdgeKind.DECLARES, label: "1.0.0", openEnd: false },
    });
    const open = create(HelmElementPayloadSchema, {
      name: "open",
      kind: HelmElementKind.EDGE,
      edge: { sourceId: "dep:redis", targetId: "", kind: HelmEdgeKind.RESOLVES, label: "redis", openEnd: true },
    });
    currentModel = modelOf(
      element("chart", "helm/chart+chart", HelmElementKind.CHART),
      element("dep:redis", "helm/chart+dependency", HelmElementKind.DEPENDENCY, {}, 400, 0),
      create(ElementSchema, {
        id: { value: "e1" },
        position: { x: 0, y: 0 },
        type: "helm/chart+edge",
        payload: { typeUrl: TYPE_URL, value: toBinary(HelmElementPayloadSchema, resolved) },
      }),
      create(ElementSchema, {
        id: { value: "e2" },
        position: { x: 0, y: 0 },
        type: "helm/chart+edge",
        payload: { typeUrl: TYPE_URL, value: toBinary(HelmElementPayloadSchema, open) },
      }),
    );

    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelector(".helm-edge-declares")).not.toBeNull();
    const openEdge = container.querySelector(".helm-edge-open");
    expect(openEdge).not.toBeNull();
    expect(openEdge?.textContent).toContain("unvendored");
  });

  it("selects on click and reveals the artifact on double-click", () => {
    // Arrange.
    const { container } = renderCanvas();
    const chart = container.querySelector('[data-element-id="chart"]');
    expect(chart).not.toBeNull();

    // Act.
    fireEvent.click(chart!);
    fireEvent.doubleClick(chart!);

    // Assert.
    expect(select).toHaveBeenCalled();
    expect(revealPath).toHaveBeenCalledWith(["Chart.yaml"]);
  });

  it("does not reveal for a node with no backing artifact", () => {
    // Arrange.
    // A dependency is a fact of Chart.yaml, not a file of its own (Requirement 8.3).
    const { container } = renderCanvas();
    const dependency = container.querySelector('[data-element-id="dep:redis"]');

    // Act.
    fireEvent.doubleClick(dependency!);

    // Assert.
    expect(revealPath).not.toHaveBeenCalled();
  });

  it("dispatches a move when a node is dragged, and only then", () => {
    // Arrange.
    const { container } = renderCanvas();
    const chart = container.querySelector('[data-element-id="chart"]');
    expect(chart).not.toBeNull();

    // Act.
    fireEvent.mouseDown(chart!, { button: 0, clientX: 10, clientY: 10 });
    fireEvent.mouseMove(container.querySelector("svg")!, { clientX: 60, clientY: 40 });
    fireEvent.mouseUp(container.querySelector("svg")!);

    // Assert.
    expect(moveElementTo).toHaveBeenCalledTimes(1);
    const [id] = moveElementTo.mock.calls[0] as unknown as [string, number, number];
    expect(id).toBe("chart");
  });

  it("a press without movement is a click, never a move", () => {
    // Arrange.
    const { container } = renderCanvas();
    const chart = container.querySelector('[data-element-id="chart"]');

    // Act.
    fireEvent.mouseDown(chart!, { button: 0, clientX: 10, clientY: 10 });
    fireEvent.mouseUp(container.querySelector("svg")!);

    // Assert.
    expect(moveElementTo).not.toHaveBeenCalled();
  });

  // ---- scrollbars: each test is named for the defect it catches -------------------------

  const viewBoxOf = (container: HTMLElement) =>
    (container.querySelector("svg.helm-canvas")!.getAttribute("viewBox") ?? "").split(" ").map(Number);

  const thumbOf = (container: HTMLElement, axis: "horizontal" | "vertical") =>
    container.querySelector(`.canvas-scrollbar-${axis} .canvas-scrollbar-thumb`) as HTMLElement;

  it("reports the changed view once, carrying the new rectangle", () => {
    // Arrange.
    // The client half of the view-delta loop (view-delta-adoption Requirements 1.1, 1.2): a
    // view CHANGE must reach the backend. jsdom lays nothing out, so shownRectOf takes its
    // no-surface branch and the reported rectangle is the viewBox itself - which is what
    // makes this assertable rather than approximate.
    vi.useFakeTimers();

    try {
      const { container } = renderCanvas();
      vi.advanceTimersByTime(VIEW_REPORT_DEBOUNCE_MS * 2);
      reportView.mockClear();

      // Act: pan by dragging a scrollbar thumb - one of several ways the view moves, none of
      // which the report is wired to individually.
      fireEvent.mouseDown(thumbOf(container, "horizontal"), { button: 0, clientX: 10, clientY: 0 });
      fireEvent.mouseMove(window, { clientX: 40, clientY: 0 });
      fireEvent.mouseUp(window);
      vi.advanceTimersByTime(VIEW_REPORT_DEBOUNCE_MS * 2);

      // Assert: exactly one report, and it describes where the view actually ended up.
      expect(reportView).toHaveBeenCalledTimes(1);
      const [x, y, w, h] = viewBoxOf(container);
      expect(reportView).toHaveBeenCalledWith({ minX: x, minY: y, maxX: x + w, maxY: y + h });
    } finally {
      vi.useRealTimers();
    }
  });

  it("pans the view when a thumb is dragged - catches an unwired onPan", () => {
    // Arrange.
    const { container } = renderCanvas();
    const [xBefore] = viewBoxOf(container);

    // Act.
    fireEvent.mouseDown(thumbOf(container, "horizontal"), { button: 0, clientX: 10, clientY: 0 });
    fireEvent.mouseMove(window, { clientX: 40, clientY: 0 });
    fireEvent.mouseUp(window);

    // Assert.
    expect(viewBoxOf(container)[0]).toBeGreaterThan(xBefore);
  });

  it("moves the thumb when the view is panned by other means - catches a stale copy of the view", () => {
    // Arrange.
    const { container } = renderCanvas();
    const svg = container.querySelector("svg.helm-canvas")!;
    const before = thumbOf(container, "horizontal").style.left;

    // Act.
    fireEvent.mouseDown(svg, { button: 0, clientX: 200, clientY: 100 });
    fireEvent.mouseMove(svg, { clientX: 60, clientY: 100 });
    fireEvent.mouseUp(svg);

    // Assert.
    expect(thumbOf(container, "horizontal").style.left).not.toBe(before);
  });

  it("resizes the thumb when the view is zoomed - catches a hard-coded viewSpan", () => {
    // Arrange.
    const { container } = renderCanvas();
    const svg = container.querySelector("svg.helm-canvas")!;
    const before = thumbOf(container, "horizontal").style.width;

    // Act.
    fireEvent.wheel(svg, { deltaY: -100 });

    // Assert.
    expect(thumbOf(container, "horizontal").style.width).not.toBe(before);
  });

  it("does not take a thumb drag for an element reposition - this canvas has both", () => {
    // Arrange.
    // The only canvas in scope with an element drag of its own: dragging a thumb must pan the
    // view and must never write an authored position into the .adp layout block.
    const { container } = renderCanvas();

    // Act.
    fireEvent.mouseDown(thumbOf(container, "horizontal"), { button: 0, clientX: 10, clientY: 0 });
    fireEvent.mouseMove(window, { clientX: 40, clientY: 0 });
    fireEvent.mouseUp(window);

    // Assert.
    expect(moveElementTo).not.toHaveBeenCalled();
  });

  it("shows the not-a-chart message for an empty model", () => {
    // Arrange.
    currentModel = emptyModel;

    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.textContent).toContain("not a Helm chart");
  });

  it("shows loading and failure states", () => {
    // Arrange & act & assert.
    currentLoading = true;
    expect(renderCanvas().container.textContent).toContain("Reading the chart");
    currentLoading = false;
    currentFailed = true;
    expect(renderCanvas().container.textContent).toContain("could not be opened");
  });
});
