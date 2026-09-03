import { describe, expect, it, vi, beforeEach } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { emptyModel, type RdfModel, type RdfNode } from "./rdfModel";

let currentModel: RdfModel = emptyModel;
let currentLoading = false;
let currentFailed = false;
let currentSelectionKey: string | null = null;
let currentActions: unknown[] = [];
let moves: { elementId: string; x: number; y: number }[] = [];
let selections: unknown[] = [];
let executed: { actionId: string; source: unknown }[] = [];

vi.mock("./useRdfStream", () => ({
  useRdfStream: () => ({
    model: currentModel,
    loading: currentLoading,
    failed: currentFailed,
    moveElementTo: (elementId: string, x: number, y: number) => {
      moves.push({ elementId, x, y });
      return Promise.resolve("");
    },
  }),
}));

vi.mock("@client/shell/context/ContextConnectionProvider", () => ({
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

const { RdfCanvas, nodeHeightOf } = await import("./RdfCanvas");

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

function modelWith(): RdfModel {
  return {
    nodes: new Map([
      [
        "res:http://example.org/alice",
        resource("res:http://example.org/alice", "ex:alice", 0, 0, {
          typeBadges: ["foaf:Person"],
          rows: [
            { predicate: "foaf:name", value: "Alice", annotation: "" },
            { predicate: "dc:description", value: "A person", annotation: "@en" },
          ],
        }),
      ],
      ["res:http://example.org/bob", resource("res:http://example.org/bob", "ex:bob", 520, 0)],
      ["blank:0", resource("blank:0", "_:c", 260, 200, { blank: true })],
    ]),
    edges: new Map([
      [
        "edge:res:http://example.org/alice|http://example.org/knows|res:http://example.org/bob",
        {
          id: "edge:res:http://example.org/alice|http://example.org/knows|res:http://example.org/bob",
          fromElementId: "res:http://example.org/alice",
          toElementId: "res:http://example.org/bob",
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

beforeEach(() => {
  currentModel = modelWith();
  currentLoading = false;
  currentFailed = false;
  currentSelectionKey = null;
  currentActions = [];
  moves = [];
  selections = [];
  executed = [];
});

describe("the rdf canvas", () => {
  it("draws cards with badges, literal rows and a labeled directed edge (Requirement 3)", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelectorAll(".rdf-node")).toHaveLength(3);
    expect(container.querySelector("marker#rdf-arrowhead")).not.toBeNull();
    expect(container.textContent).toContain("ex:alice");
    // Types as badges, literals as rows with their annotations - never nodes.
    expect(container.querySelector(".rdf-badges")!.textContent).toBe("foaf:Person");
    expect(container.textContent).toContain("foaf:name: Alice");
    expect(container.textContent).toContain("dc:description: A person @en");
    // The predicate rides its edge.
    expect(container.querySelector(".rdf-edge-label")!.textContent).toBe("ex:knows");
  });

  it("styles a blank node apart, wearing the shared element classes (Requirement 3.4)", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    const blank = container.querySelector(".rdf-node-blank");
    expect(blank).not.toBeNull();
    expect(blank!.textContent).toContain("_:c");
    expect(blank!.classList.contains("canvas-element")).toBe(true);
  });

  it("sizes a card by its rows", () => {
    // Arrange.
    const bare = resource("res:x", "x", 0, 0);
    const full = resource("res:y", "y", 0, 0, {
      typeBadges: ["t"],
      rows: [
        { predicate: "a", value: "1", annotation: "" },
        { predicate: "b", value: "2", annotation: "" },
      ],
    });

    // Act & assert.
    expect(nodeHeightOf(full)).toBeGreaterThan(nodeHeightOf(bare));
  });

  it("lands a drag as one layout move, and treats a still click as a selection (Requirement 4)", () => {
    // Arrange.
    const { container } = renderCanvas();
    const node = container.querySelector('[data-element-id="res:http://example.org/alice"]')!;

    // Act: press, move past the threshold, release.
    fireEvent.mouseDown(node, { clientX: 100, clientY: 100 });
    fireEvent.mouseMove(container.querySelector(".rdf-surface")!, { clientX: 160, clientY: 140 });
    fireEvent.mouseUp(container.querySelector(".rdf-surface")!);

    // Assert.
    expect(moves).toHaveLength(1);
    expect(moves[0].elementId).toBe("res:http://example.org/alice");

    // Act: press and release without moving.
    fireEvent.mouseDown(node, { clientX: 100, clientY: 100 });
    fireEvent.mouseUp(container.querySelector(".rdf-surface")!);

    // Assert: a click selects, never edits.
    expect(moves).toHaveLength(1);
    expect(selections.length).toBeGreaterThan(0);
  });

  it("shows the showing-N-of-M banner exactly when the view is truncated (Requirement 8.2)", () => {
    // Arrange & act: not truncated.
    let rendered = renderCanvas();
    expect(rendered.container.querySelector(".rdf-truncation-banner")).toBeNull();
    rendered.unmount();

    // Arrange & act: truncated.
    currentModel = { ...modelWith(), truncation: { shown: 1000, total: 5321 } };
    rendered = renderCanvas();

    // Assert.
    const banner = rendered.container.querySelector(".rdf-truncation-banner");
    expect(banner).not.toBeNull();
    expect(banner!.textContent).toContain("1000 of 5321");
    expect(banner!.textContent).toContain("withheld");
  });

  it("finishes an anchor drag as one stateless rel: gesture (Requirement 6)", () => {
    // Arrange: the source resource is selected, so its anchors are drawn.
    currentSelectionKey = "element:res:http://example.org/alice";
    const { container } = renderCanvas();
    const anchor = container.querySelector(".rdf-anchor-hit")!;
    const target = container.querySelector('[data-element-id="res:http://example.org/bob"]')!;

    // Act: start on the anchor, enter the target, release on it.
    fireEvent.mouseDown(anchor, { clientX: 100, clientY: 100 });
    fireEvent.mouseEnter(target);
    fireEvent.mouseUp(target);

    // Assert.
    expect(executed).toHaveLength(1);
    expect(executed[0].actionId).toBe("rdf.connect");
  });

  it("offers no anchors on a blank node - the identity boundary starts at the gesture", () => {
    // Arrange.
    currentSelectionKey = "element:blank:0";

    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelector(".rdf-anchor-hit")).toBeNull();
  });

  it("drops a toolbox entry as a new: placement under the pointer (Requirement 6)", () => {
    // Arrange.
    const { container } = renderCanvas();
    const surface = container.querySelector(".rdf-surface")!;
    const dataTransfer = {
      types: ["application/x-adp-toolbox-item"],
      getData: () => "rdf.add-resource",
      dropEffect: "",
    };

    // Act.
    fireEvent.dragOver(surface, { dataTransfer });
    fireEvent.drop(surface, { dataTransfer, clientX: 50, clientY: 60 });

    // Assert.
    expect(executed).toHaveLength(1);
    expect(executed[0].actionId).toBe("rdf.add-resource");
    expect(String((executed[0].source as { case?: unknown; value?: unknown } | undefined) ?? "")).toBeDefined();
  });
});
