import { describe, expect, it, vi, beforeEach } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { VIEW_REPORT_DEBOUNCE_MS } from "@client/diagrams/viewReport";
import { emptyModel, type RdfModel, type RdfNode } from "./rdfModel";
import { selectedElementIdOf } from "@client/canvas/selection";
import type { ContextSelection } from "@client/generated/context_pb";
import { expectLibrarySelection } from "@client/canvas/library/testing/expectLibrarySelection";

let currentModel: RdfModel = emptyModel;
let currentLoading = false;
let currentFailed = false;
let currentSelectionKey: string | null = null;
let currentActions: unknown[] = [];
let moves: { elementId: string; x: number; y: number }[] = [];
let selections: unknown[] = [];
let executed: { actionId: string; source: unknown }[] = [];
const reportView = vi.fn();

vi.mock("./useRdfStream", () => ({
  useRdfStream: () => ({
    model: currentModel,
    loading: currentLoading,
    failed: currentFailed,
    reportView,
    moveElementTo: (elementId: string, x: number, y: number) => {
      moves.push({ elementId, x, y });
      return Promise.resolve("");
    },
  }),
}));

vi.mock("@client/shell/context/ContextConnectionProvider", () => ({
  // The library reads the inline-edit prompt itself where it owns the canvas (client-centralization
  // task 7), so a sourced canvas needs one here even though this module never renames inline.
  useContextPrompt: () => ({ prompt: null, onPropose: vi.fn(), onSubmit: vi.fn(), onCancel: vi.fn() }),
  innermostKey: () => currentSelectionKey,
  useContextSelection: () => ({ selection: currentSelectionKey, levels: [], actions: currentActions }),
  useContextConnection: () => ({
    select: (selection: unknown) => selections.push(selection),
    executeAction: (actionId: string, source?: unknown) => {
      executed.push({ actionId, source });
      return Promise.resolve({ accepted: true, error: "" });
    },
    executeShortcut: () => Promise.resolve({ accepted: true, error: "" }),
    setProperty: () => Promise.resolve({ accepted: true, error: "" }),
  }),
}));

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

const { RdfCanvas, nodeHeightOf, NODE_WIDTH } = await import("./RdfCanvas");

// jsdom implements no pointer capture on SVG elements; the library's arbiter uses it.
SVGElement.prototype.setPointerCapture ??= () => {};
SVGElement.prototype.releasePointerCapture ??= () => {};

/** A pointer event jsdom can carry - usePointerGesture.test.tsx's idiom, for the same reason. */
function pointer(type: string, init: MouseEventInit) {
  return new MouseEvent(type, { bubbles: true, cancelable: true, ...init });
}

function press(target: Element, init: MouseEventInit = {}) {
  fireEvent(target, pointer("pointerdown", { button: 0, ...init }));
  fireEvent(target, pointer("pointerup", { ...init }));
}

function drag(target: Element, fromX: number, fromY: number, toX: number, toY: number) {
  fireEvent(target, pointer("pointerdown", { button: 0, clientX: fromX, clientY: fromY }));
  fireEvent(target, pointer("pointermove", { clientX: toX, clientY: toY }));
  fireEvent(target, pointer("pointerup", { clientX: toX, clientY: toY }));
}

function resource(id: string, display: string, x: number, y: number, options: Partial<RdfNode> = {}): RdfNode {
  return {
    id,
    x,
    y,
    iri: id.startsWith("res:") ? id.slice("res:".length) : "",
    display,
    typeBadges: [],
    rows: [],
    blank: false,
    ...options,
  };
}

const ALICE = "res:http://example.org/alice";
const BOB = "res:http://example.org/bob";
const EDGE = "edge:res:http://example.org/alice|http://example.org/knows|res:http://example.org/bob";

function modelWith(): RdfModel {
  return {
    nodes: new Map([
      [
        ALICE,
        resource(ALICE, "ex:alice", 0, 0, {
          typeBadges: ["foaf:Person"],
          rows: [
            { predicate: "foaf:name", value: "Alice", annotation: "" },
            { predicate: "dc:description", value: "A person", annotation: "@en" },
          ],
        }),
      ],
      [BOB, resource(BOB, "ex:bob", 520, 0)],
      ["blank:0", resource("blank:0", "_:c", 260, 200, { blank: true })],
    ]),
    edges: new Map([
      [
        EDGE,
        {
          id: EDGE,
          fromElementId: ALICE,
          toElementId: BOB,
          predicate: "ex:knows",
          predicateIri: "http://example.org/knows",
        },
      ],
    ]),
    truncation: null,
  };
}

function renderCanvas() {
  return render(<RdfCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["graph.adp"]} />);
}

const cardOf = (container: HTMLElement, id: string) => container.querySelector(`[data-element-id="${id}"]`)!;

beforeEach(() => {
  currentModel = modelWith();
  currentLoading = false;
  currentFailed = false;
  currentSelectionKey = null;
  currentActions = [];
  moves = [];
  selections = [];
  executed = [];
  reportView.mockClear();
});

describe("the rdf canvas, on the library", () => {
  it("draws cards with badges, literal rows and a labeled directed edge (Requirement 3)", () => {
    const { container } = renderCanvas();

    expect(container.textContent).toContain("ex:alice");
    expect(container.textContent).toContain("foaf:Person");
    expect(container.textContent).toContain("foaf:name: Alice");
    expect(container.textContent).toContain("dc:description: A person @en");
    expect(container.textContent).toContain("ex:knows");
    expect(container.querySelector(`[data-connection-id="${EDGE}"] path.canvas-connection-line`)).not.toBeNull();
  });

  it("styles a blank node apart, wearing the shared element classes (Requirement 3.4)", () => {
    const { container } = renderCanvas();

    const blank = cardOf(container, "blank:0");
    expect(blank.querySelector(".rdf-node-blank")).not.toBeNull();
    expect(blank.querySelector(".canvas-element")).not.toBeNull();
  });

  it("sizes a card by its rows", () => {
    const bare = resource("res:x", "x", 0, 0);
    const tall = resource("res:y", "y", 0, 0, {
      typeBadges: ["t"],
      rows: [
        { predicate: "a", value: "1", annotation: "" },
        { predicate: "b", value: "2", annotation: "" },
      ],
    });

    expect(nodeHeightOf(tall)).toBeGreaterThan(nodeHeightOf(bare));
  });

  it("lands a drag as one layout move, and treats a still click as a selection (Requirement 4)", () => {
    const { container } = renderCanvas();
    const alice = cardOf(container, ALICE);

    // A drag: the move lands in the module's own top-left coordinates, exactly as the
    // layout block stores them. jsdom's zero rect makes one pixel one canvas unit.
    drag(alice, 10, 10, 60, 40);
    expect(moves).toHaveLength(1);
    expect(moves[0].elementId).toBe(ALICE);
    expect(moves[0].x).toBeCloseTo(50, 5);
    expect(moves[0].y).toBeCloseTo(30, 5);

    // A still press: a selection, never an edit - and never a move.
    press(cardOf(container, BOB), { clientX: 10, clientY: 10 });
    expect(moves).toHaveLength(1);
    expect(selections).toHaveLength(1);
  });

  it("shows the showing-N-of-M banner exactly when the view is truncated (Requirement 8.2)", () => {
    currentModel = { ...modelWith(), truncation: { shown: 3, total: 12 } };
    const { container } = renderCanvas();

    expect(container.textContent).toContain("Showing 3 of 12 resources");
  });

  it("finishes an anchor drag as one stateless rel: gesture (Requirement 6)", () => {
    const { container } = renderCanvas();

    // Alice's east anchor sits at her right edge's midpoint (220, 44); Bob's card spans
    // x 520..740, y 0..40 - so a 340-right, 10-up drag lands the preview inside his box.
    const anchor = container.querySelector(`[data-element-id="${ALICE}"] [data-anchor="e"]`)!;
    drag(anchor, NODE_WIDTH, 30, 560, 20);

    expect(executed).toHaveLength(1);
    expect(executed[0].actionId).toBe("rdf.connect");
    const source = executed[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toBe(`rel:${ALICE}->${BOB}`);
  });

  it("a connect released over nothing, or back on its own source, is a never-mind", () => {
    const { container } = renderCanvas();
    const anchor = container.querySelector(`[data-element-id="${ALICE}"] [data-anchor="e"]`)!;

    // Over empty canvas: no event, no action.
    drag(anchor, NODE_WIDTH, 30, 400, 400);
    // Back over Alice herself: the definition forbids self-connections.
    drag(anchor, NODE_WIDTH, 30, 100, 40);

    expect(executed).toHaveLength(0);
  });

  it("selects an edge on press, so its removal is reachable (found in the manual pass)", () => {
    const { container } = renderCanvas();

    press(container.querySelector(`[data-connection-id="${EDGE}"] path.canvas-connection-hit`)!, {
      clientX: 300,
      clientY: 30,
    });

    expect(selections).toHaveLength(1);
  });

  it("offers no anchors on a blank node - the identity boundary starts at the gesture", () => {
    const { container } = renderCanvas();

    expect(cardOf(container, "blank:0").querySelectorAll("[data-anchor]")).toHaveLength(0);
    expect(cardOf(container, ALICE).querySelectorAll("[data-anchor]")).toHaveLength(2);
  });

  it("drops a toolbox entry as a new: placement under the pointer (Requirement 6)", () => {
    const { container } = renderCanvas();
    const surface = container.querySelector("svg.library-canvas-surface")!;

    fireEvent.drop(surface, {
      clientX: 200,
      clientY: 100,
      dataTransfer: { types: ["application/x-adp-toolbox-item"], getData: () => "rdf.addResource", dropEffect: "" },
    });

    expect(executed).toHaveLength(1);
    expect(executed[0].actionId).toBe("rdf.addResource");
    const source = executed[0].source as { source: { value: { value: string } } };
    expect(source.source.value.value).toMatch(/^new:/);
  });

  it("an empty drop payload refuses: nothing executes", () => {
    const { container } = renderCanvas();
    const surface = container.querySelector("svg.library-canvas-surface")!;

    fireEvent.drop(surface, {
      dataTransfer: { types: ["application/x-adp-toolbox-item"], getData: () => "", dropEffect: "" },
    });

    expect(executed).toHaveLength(0);
  });

  it("reports the viewport once the view settles, and again when it changes", async () => {
    vi.useFakeTimers();
    try {
      const { container } = renderCanvas();

      await vi.advanceTimersByTimeAsync(VIEW_REPORT_DEBOUNCE_MS * 2);
      expect(reportView).toHaveBeenCalledTimes(1);
      const first = reportView.mock.calls[0][0] as { minX: number; maxX: number };
      expect(first.maxX).toBeGreaterThan(first.minX);

      // A zoom changes the view; once it settles the backend hears the new rectangle.
      fireEvent.wheel(container.querySelector("svg.library-canvas-surface")!, { deltaY: -100 });
      await vi.advanceTimersByTimeAsync(VIEW_REPORT_DEBOUNCE_MS * 2);
      expect(reportView).toHaveBeenCalledTimes(2);
    } finally {
      vi.useRealTimers();
    }
  });

  it("reports nothing while the diagram is loading or has failed", async () => {
    vi.useFakeTimers();
    try {
      currentLoading = true;
      renderCanvas();
      await vi.advanceTimersByTimeAsync(VIEW_REPORT_DEBOUNCE_MS * 3);
      expect(reportView).not.toHaveBeenCalled();
    } finally {
      vi.useRealTimers();
    }
  });
});

describe("selection, as every canvas has it", () => {
  it("highlights a pushed resource and triple, and clears on a background press (centralized-selection 9.2)", () => {
    expectLibrarySelection({
      mountWith: (id) => {
        currentModel = modelWith();
        currentLoading = false;
        currentFailed = false;
        currentSelectionKey = id === null ? null : `element:${id}`;
        return renderCanvas();
      },
      pushedIds: () => selections.map((push) => (push === null ? null : (selectedElementIdOf(push as ContextSelection) ?? null))),
      element: ALICE,
      connection: EDGE,
    });
  });
});
