import { describe, expect, it, vi, beforeEach } from "vitest";
import { fireEvent, render, act } from "@testing-library/react";
import { VIEW_REPORT_DEBOUNCE_MS } from "@client/diagrams/viewReport";
import { expectLibrarySelection } from "@client/canvas/library/testing/expectLibrarySelection";
import {
  emptyModel,
  type SparqlAnnotation,
  type SparqlDiagramEdge,
  type SparqlModel,
  type SparqlNode,
  type SparqlRegion,
} from "./sparqlModel";
import { fakeContextConnection, idsPushed } from "@client/canvas/library/testing/canvasHarness";

let currentModel: SparqlModel = emptyModel;
let currentLoading = false;
let currentFailed = false;
let currentSelectionKey: string | null = null;
let currentActions: unknown[] = [];
let moves: { elementId: string; x: number; y: number }[] = [];
let selections: unknown[] = [];
let moveError = "";

const reportView = vi.fn();

vi.mock("./useSparqlStream", () => ({
  useSparqlStream: () => ({
    model: currentModel,
    loading: currentLoading,
    failed: currentFailed,
    moveElementTo: (elementId: string, x: number, y: number) => {
      moves.push({ elementId, x, y });
      return Promise.resolve(moveError);
    },
    reportView,
  }),
}));

vi.mock("@client/shell/context/ContextConnectionProvider", () => ({
  // The library reads the inline-edit prompt itself where it owns the canvas (client-centralization
  // task 7), so a sourced canvas needs one here even though this module never renames inline.
  useContextPrompt: () => ({ prompt: null, onPropose: vi.fn(), onSubmit: vi.fn(), onCancel: vi.fn() }),
  innermostKey: () => currentSelectionKey,
  useContextSelection: () => ({ selection: currentSelectionKey, levels: [], actions: currentActions }),
  useContextConnection: () => connection,
}));

const connection = fakeContextConnection({ select: (selection: unknown) => selections.push(selection) });

vi.mock("@client/shell/panels/DiagramViewContext", () => ({
  useRegisterDiagramView: () => undefined,
}));

vi.mock("@client/shell/panels/DiagramToolboxContext", () => ({
  TOOLBOX_DRAG_TYPE: "application/x-adp-toolbox-item",
  useRegisterDiagramToolbox: () => undefined,
}));

vi.mock("@client/shell/panels/useToolboxItems", () => ({
  useToolboxItems: () => [],
}));

const { SparqlCanvas } = await import("./SparqlCanvas");

function node(id: string, display: string, kind: SparqlNode["kind"], options: Partial<SparqlNode> = {}): SparqlNode {
  return {
    id,
    x: 0,
    y: 0,
    display,
    kind,
    projected: false,
    joinCount: 0,
    annotation: "",
    full: "",
    ...options,
  };
}

function region(id: string, kind: string, label: string, options: Partial<SparqlRegion> = {}): SparqlRegion {
  return { id, x: 0, y: 0, width: 300, height: 200, kind, label, parentRegionId: "", ...options };
}

function edge(id: string, from: string, to: string, label: string, isPath = false): SparqlDiagramEdge {
  return { id, fromElementId: from, toElementId: to, label, isPath };
}

function annotation(id: string, kind: string, text: string, attachedTo: string): SparqlAnnotation {
  return { id, kind, text, attachedTo };
}

/** A query with one of everything the canvas draws. */
function modelWith(): SparqlModel {
  return {
    nodes: new Map([
      ["var:person", node("var:person", "?person", "variable", { projected: true, joinCount: 3, x: 0, y: 0 })],
      ["var:friend", node("var:friend", "?friend", "variable", { joinCount: 1, x: 240, y: 0 })],
      ["anon:0", node("anon:0", "[]", "anonymous", { x: 480, y: 0 })],
      ["iri:http://xmlns.com/foaf/0.1/Person", node("iri:http://xmlns.com/foaf/0.1/Person", "foaf:Person", "iri", { x: 0, y: 120 })],
      ["lit:42", node("lit:42", "42", "literal", { annotation: "integer", x: 240, y: 120 })],
      ["sub:where.0", node("sub:where.0", "SELECT ?person (COUNT(?p) AS ?n)", "subquery", { x: 480, y: 120 })],
    ]),
    edges: new Map([
      ["edge:a", edge("edge:a", "var:person", "iri:http://xmlns.com/foaf/0.1/Person", "a")],
      ["edge:knows", edge("edge:knows", "var:person", "var:friend", "foaf:knows+", true)],
    ]),
    regions: new Map([
      ["region:where/optional.0", region("region:where/optional.0", "optional", "OPTIONAL", { x: 200, y: 200 })],
    ]),
    annotations: new Map([
      ["note:where/filter.0", annotation("note:where/filter.0", "filter", "FILTER(?friend != ?person)", "var:friend")],
    ]),
    header: { form: "SELECT DISTINCT", modifierRows: ["ORDER BY ?person", "LIMIT 10"] },
    truncation: null,
  };
}

function renderCanvas() {
  return render(
    <SparqlCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["people.rq"]} />,
  );
}

describe("SparqlCanvas", () => {
  beforeEach(() => {
    currentModel = modelWith();
    currentLoading = false;
    currentFailed = false;
    currentSelectionKey = null;
    currentActions = [];
    moves = [];
    selections = [];
    moveError = "";
  });

  it("draws one node per variable, styled apart from the concrete terms", () => {
    // Arrange & act.
    const { container } = renderCanvas();

    // Assert: a variable is dashed and a concrete term is not - what is being asked for, told
    // apart from what is pinned down, at a glance.
    expect(container.querySelectorAll(".sparql-node-variable")).toHaveLength(2);
    expect(container.querySelectorAll(".sparql-node-anonymous")).toHaveLength(1);
    expect(container.querySelectorAll(".sparql-node-iri")).toHaveLength(1);
    expect(container.querySelectorAll(".sparql-node-literal")).toHaveLength(1);
    expect(container.querySelectorAll(".sparql-node-subquery")).toHaveLength(1);
    expect(container.textContent).toContain("?person");
  });

  it("marks the projected variable, so what leaves the query reads without the header", () => {
    // Arrange & act.
    const { container } = renderCanvas();

    // Assert.
    const projected = container.querySelectorAll(".sparql-node-projected");
    expect(projected).toHaveLength(1);
    expect(projected[0].closest("[data-element-id]")!.getAttribute("data-element-id")).toBe("var:person");
    expect(container.querySelectorAll(".sparql-projection-mark")).toHaveLength(1);
  });

  it("draws a region as a labelled frame behind its contents", () => {
    // Arrange & act.
    const { container } = renderCanvas();

    // Assert: the frame is in the document before any node, so it paints underneath.
    const frame = container.querySelector(".sparql-region-optional");
    expect(frame).not.toBeNull();
    expect(frame!.textContent).toContain("OPTIONAL");

    const drawn = [...container.querySelectorAll("[data-element-id]")].map((element) =>
      element.getAttribute("data-element-id"),
    );
    expect(drawn.indexOf("region:where/optional.0")).toBeLessThan(drawn.indexOf("var:person"));
  });

  it("labels an edge with its predicate, and marks a property path as the multi-step ask it is", () => {
    // Arrange & act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.textContent).toContain("foaf:knows+");
    expect(container.querySelectorAll(".sparql-edge-path")).toHaveLength(1);
  });

  it("shows an annotation's text exactly as written", () => {
    // Arrange & act.
    const { container } = renderCanvas();

    // Assert: the expression as the author wrote it, never a parsed reconstruction.
    const badge = container.querySelector(".sparql-annotation-filter");
    expect(badge?.textContent).toBe("FILTER(?friend != ?person)");
  });

  it("states the form and modifiers in the header band rather than on the canvas", () => {
    // Arrange & act.
    const { container } = renderCanvas();

    // Assert.
    const band = container.querySelector(".sparql-header-band");
    expect(band?.textContent).toContain("SELECT DISTINCT");
    expect(band?.textContent).toContain("LIMIT 10");
    // The header is a band, not a drawn element - nothing on the canvas carries its id.
    expect(container.querySelector('[data-element-id="header:query"]')).toBeNull();
  });

  it("sends a drag as a layout move and nothing else", () => {
    // Arrange.
    const { container } = renderCanvas();
    const target = container.querySelector('[data-element-id="var:person"]')!;

    // Act.
    fireEvent(target, new MouseEvent("pointerdown", { bubbles: true, button: 0, clientX: 10, clientY: 10 }));
    fireEvent(target, new MouseEvent("pointermove", { bubbles: true, clientX: 90, clientY: 60 }));
    fireEvent(target, new MouseEvent("pointerup", { bubbles: true, clientX: 90, clientY: 60 }));

    // Assert.
    expect(moves).toHaveLength(1);
    expect(moves[0].elementId).toBe("var:person");
    expect(moves[0].x).toBeGreaterThan(0);
  });

  it("leaves the backend's refusal of a move to the library's line", async () => {
    // Arrange: the move itself reports the refusal to the one line the library draws around every
    // canvas (client-centralization Requirement 2; useDiagramStream.move.test.ts).
    moveError = "That is an anonymous variable - it takes its computed place.";
    const { container } = renderCanvas();
    const target = container.querySelector('[data-element-id="anon:0"]')!;

    // Act.
    fireEvent(target, new MouseEvent("pointerdown", { bubbles: true, button: 0, clientX: 10, clientY: 10 }));
    fireEvent(target, new MouseEvent("pointermove", { bubbles: true, clientX: 90, clientY: 60 }));
    fireEvent(target, new MouseEvent("pointerup", { bubbles: true, clientX: 90, clientY: 60 }));

    await act(async () => {});

    // Assert: the move was sent, and this canvas drew no line of its own.
    expect(moves).toHaveLength(1);
    expect(container.textContent).not.toContain("anonymous variable - it takes");
    expect(container.querySelector(".canvas-rejection")).toBeNull();
  });

  it("treats a press without movement as a selection", () => {
    // Arrange.
    const { container } = renderCanvas();
    const target = container.querySelector('[data-element-id="var:friend"]')!;

    // Act.
    fireEvent(target, new MouseEvent("pointerdown", { bubbles: true, button: 0, clientX: 10, clientY: 10 }));
    fireEvent(target, new MouseEvent("pointerup", { bubbles: true, clientX: 10, clientY: 10 }));

    // Assert.
    expect(moves).toHaveLength(0);
    expect(selections).toHaveLength(1);
  });

  it("offers no editing affordance at all", () => {
    // Arrange & act: the backend registers no toolbox and no mutating action, so the canvas
    // must invent none - no drop target, no connect anchors, no pending edge.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelectorAll(".canvas-anchor")).toHaveLength(0);
    expect(container.querySelectorAll(".canvas-anchor-hit")).toHaveLength(0);
    expect(container.querySelectorAll(".canvas-pending-connection")).toHaveLength(0);
    expect(container.querySelector("svg.library-canvas-surface")?.getAttribute("ondrop")).toBeNull();
  });

  it("shows the showing-N-of-M banner when the sanity bound cut the query", () => {
    // Arrange.
    currentModel = { ...modelWith(), truncation: { shown: 500, total: 1200 } };

    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelector(".sparql-truncation-banner")?.textContent).toContain("Showing 500 of 1200");
  });

  it("reports the changed view once, carrying the new rectangle", () => {
    // Arrange.
    // The client half of the view-delta loop (view-delta-adoption Requirements 1.1, 1.2). This
    // is a pixels-per-unit canvas, so it converts to a rectangle at its own call site; jsdom
    // measures nothing, so the fallback span is what the conversion uses and the expected
    // rectangle is exact rather than approximate.
    vi.useFakeTimers();

    try {
      const { container } = renderCanvas();
      vi.advanceTimersByTime(VIEW_REPORT_DEBOUNCE_MS * 2);
      reportView.mockClear();

      // Act: a wheel zoom, which changes the visible rectangle.
      const surface = container.querySelector("svg.library-canvas-surface")!;
      fireEvent.wheel(surface, { deltaY: -1, clientX: 10, clientY: 10 });
      vi.advanceTimersByTime(VIEW_REPORT_DEBOUNCE_MS * 2);

      // Assert: exactly one report, describing a rectangle that actually changed.
      expect(reportView).toHaveBeenCalledTimes(1);
      const reported = reportView.mock.calls.at(-1)![0] as {
        minX: number; minY: number; maxX: number; maxY: number;
      };
      expect(reported.maxX - reported.minX).toBeGreaterThan(0);
      expect(reported.maxY - reported.minY).toBeGreaterThan(0);
    } finally {
      vi.useRealTimers();
    }
  });

  it("leaves the unavailable state to the library's frame rather than saying it itself", () => {
    // Arrange: client-centralization Requirement 2.3 - one appearance, drawn by the library.
    currentFailed = true;

    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.textContent).not.toContain("could not be opened");
  });
});

describe("selection, as every canvas has it", () => {
  it("highlights a pushed variable and triple pattern, and clears on a background press (centralized-selection 9.2)", () => {
    expectLibrarySelection({
      mountWith: (id) => {
        currentModel = modelWith();
        currentLoading = false;
        currentFailed = false;
        currentSelectionKey = id === null ? null : `element:${id}`;
        return renderCanvas();
      },
      pushedIds: () => idsPushed(selections),
      element: "var:person",
      connection: "edge:knows",
    });
  });
});
