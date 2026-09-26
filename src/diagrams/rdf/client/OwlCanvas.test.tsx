import { describe, expect, it, vi, beforeEach } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { emptyOwlModel, type OwlModel, type OwlNode, type OwlNodeKind } from "./owlModel";
import { expectLibrarySelection } from "@client/canvas/library/testing/expectLibrarySelection";
import { fakeContextConnection, idsPushed } from "@client/canvas/library/testing/canvasHarness";

let currentModel: OwlModel = emptyOwlModel;
let currentLoading = false;
let currentFailed = false;
let currentSelectionKey: string | null = null;
let currentActions: unknown[] = [];
let moves: { elementId: string; x: number; y: number }[] = [];
let moveError = "";
let selections: unknown[] = [];
let executed: { actionId: string; source: unknown }[] = [];

vi.mock("./useOwlStream", () => ({
  useOwlStream: () => ({
    model: currentModel,
    loading: currentLoading,
    failed: currentFailed,
    reportView: () => {},
    moveElementTo: (elementId: string, x: number, y: number) => {
      moves.push({ elementId, x, y });
      return Promise.resolve(moveError);
    },
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

const connection = fakeContextConnection({
  select: (selection: unknown) => selections.push(selection),
  executeAction: (actionId: string, source?: unknown) => {
    executed.push({ actionId, source });
    return Promise.resolve({ accepted: true, error: "" });
  },
});

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

const { OwlCanvas, nodeSizeOf, labelFor, fit } = await import("./OwlCanvas");

const NS = "http://example.org/o#";

function node(id: string, kind: OwlNodeKind, display: string, x: number, y: number, options: Partial<OwlNode> = {}): OwlNode {
  return {
    id,
    x,
    y,
    kind,
    iri: id.startsWith("res:") ? id.slice("res:".length) : "",
    display,
    badges: [],
    rows: [],
    deprecated: false,
    external: false,
    elided: false,
    malformed: false,
    ownerElementId: "",
    ...options,
  };
}

const EXPRESSION_ID = `expr:${NS}Vegetarian|http://www.w3.org/2000/01/rdf-schema#subClassOf|1`;

function modelWith(): OwlModel {
  return {
    nodes: new Map([
      [`res:${NS}Pizza`, node(`res:${NS}Pizza`, "class", "Pizza", 0, 0)],
      [`res:${NS}Vegetarian`, node(`res:${NS}Vegetarian`, "class", "Vegetarian", 300, 0)],
      [`res:${NS}Old`, node(`res:${NS}Old`, "class", "Old", 300, 200, { deprecated: true })],
      ["res:http://example.org/ext#Base", node("res:http://example.org/ext#Base", "class", "ext:Base", 600, 200, { external: true })],
      [
        "res:http://www.w3.org/2001/XMLSchema#integer",
        node("res:http://www.w3.org/2001/XMLSchema#integer", "datatype", "xsd:integer", 600, 0),
      ],
      [`thing:${NS}toppingOf|domain`, node(`thing:${NS}toppingOf|domain`, "thing", "Thing", 0, 400)],
      [
        EXPRESSION_ID,
        node(EXPRESSION_ID, "restriction", "∃ :hasTopping.(:B ∪ …)", 470, 70, {
          elided: true,
          ownerElementId: `res:${NS}Vegetarian`,
        }),
      ],
      [
        `res:${NS}Margherita`,
        node(`res:${NS}Margherita`, "individual", "Margherita", 0, 700, {
          badges: ["Pizza"],
          rows: [{ predicate: ":hasCalories", value: "850", annotation: "" }],
        }),
      ],
    ]),
    edges: new Map([
      [
        "edge:subclass",
        {
          id: "edge:subclass",
          kind: "subclass" as const,
          fromElementId: `res:${NS}Vegetarian`,
          toElementId: `res:${NS}Pizza`,
          label: "",
          propertyIri: "http://www.w3.org/2000/01/rdf-schema#subClassOf",
        },
      ],
      [
        "edge:property",
        {
          id: "edge:property",
          kind: "object-property" as const,
          fromElementId: `res:${NS}Pizza`,
          toElementId: `res:${NS}Vegetarian`,
          label: "hasTopping (functional)",
          propertyIri: `${NS}hasTopping`,
        },
      ],
      [
        "edge:equivalent",
        {
          id: "edge:equivalent",
          kind: "equivalent" as const,
          fromElementId: `res:${NS}Pizza`,
          toElementId: `res:${NS}Old`,
          label: "",
          propertyIri: "http://www.w3.org/2002/07/owl#equivalentClass",
        },
      ],
      [
        "edge:expression",
        {
          id: "edge:expression",
          kind: "expression" as const,
          fromElementId: `res:${NS}Vegetarian`,
          toElementId: EXPRESSION_ID,
          label: "",
          propertyIri: "",
        },
      ],
    ]),
    truncation: null,
  };
}

/** Finds an element by its id attribute - the ids carry characters a CSS selector cannot. */
function elementWithId(container: HTMLElement, id: string): Element {
  return [...container.querySelectorAll("[data-element-id]")].find(
    (element) => element.getAttribute("data-element-id") === id,
  )!;
}

function renderCanvas() {
  return render(<OwlCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["ontology.adp"]} />);
}

beforeEach(() => {
  currentModel = modelWith();
  currentLoading = false;
  currentFailed = false;
  currentSelectionKey = null;
  currentActions = [];
  moves = [];
  moveError = "";
  selections = [];
  executed = [];
});

describe("the owl canvas", () => {
  it("draws each kind in its own shape, dimming the deprecated and the borrowed", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert: classes and Thing anchors are round, datatypes are rectangles, individuals are
    // cards - the adopted vocabulary (Requirement 1).
    const pizza = container.querySelector(`[data-element-id="res:${NS}Pizza"]`)!;
    expect(pizza.querySelector("ellipse.owl-shape")).not.toBeNull();
    expect(pizza.querySelector("text")!.textContent).toBe("Pizza");

    const datatype = container.querySelector('[data-element-id="res:http://www.w3.org/2001/XMLSchema#integer"]')!;
    expect(datatype.querySelector("rect.owl-datatype-box")).not.toBeNull();

    const thing = elementWithId(container, `thing:${NS}toppingOf|domain`);
    expect(thing.querySelector(".owl-thing")).not.toBeNull();
    expect(thing.querySelector("ellipse")).not.toBeNull();

    const individual = container.querySelector(`[data-element-id="res:${NS}Margherita"]`)!;
    expect(individual.querySelector("rect.owl-card-box")).not.toBeNull();
    expect(individual.querySelector("text.owl-badges")!.textContent).toBe("Pizza");
    expect(individual.querySelector("text.owl-row")!.textContent).toBe(":hasCalories: 850");

    // Dimming rides classes, so a theme decides how dim (Requirement 1.6).
    expect(container.querySelector(`[data-element-id="res:${NS}Old"] .owl-deprecated`)).not.toBeNull();
    expect(
      container.querySelector('[data-element-id="res:http://example.org/ext#Base"]')!.querySelector(".owl-external"),
    ).not.toBeNull();
  });

  it("marks an equivalent class with the doubled outline the notation uses", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert: Pizza carries an equivalence edge, so its outline doubles; Vegetarian does not.
    const pizza = container.querySelector(`[data-element-id="res:${NS}Pizza"]`)!;
    expect(pizza.querySelectorAll("ellipse").length).toBe(2);
    expect(pizza.querySelector("ellipse.owl-shape-inner")).not.toBeNull();
    expect(container.querySelector(`[data-element-id="res:${NS}Vegetarian"]`)!.querySelectorAll("ellipse").length).toBe(1);
  });

  it("draws an expression node with its Manchester label and the elision marker", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert: the depth-capped label, the visible marker, and no anchors - an expression has no
    // identity an edit could key off (Requirements 3.1, 3.2, 3.4).
    const expression = elementWithId(container, EXPRESSION_ID);
    expect(expression.querySelector(".owl-restriction")).not.toBeNull();
    expect(expression.querySelector(".owl-elided")).not.toBeNull();
    expect(expression.querySelector("text")!.textContent).toBe("∃ :hasTopping.(:B ∪ …) …");
  });

  it("styles each axiom edge by what it states, and points only what points", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert (Requirement 2).
    const subclass = container.querySelector('[data-connection-id="edge:subclass"]')!;
    expect(subclass.classList.contains("owl-edge-subclass")).toBe(true);
    const property = container.querySelector('[data-connection-id="edge:property"]')!;
    expect(property.classList.contains("owl-edge-object-property")).toBe(true);
    // The property's characteristic words ride its label (Requirement 2.3).
    expect(property.querySelector("text.library-connection-label")!.textContent).toBe("hasTopping (functional)");
    // A symmetric axiom carries no arrowhead; a directed one does.
    expect(container.querySelector('[data-connection-id="edge:equivalent"] path[marker-end]')).toBeNull();
    expect(container.querySelector('[data-connection-id="edge:property"] path[marker-end]')).not.toBeNull();
    // A pure axiom edge has no label to draw.
    expect(container.querySelector('[data-connection-id="edge:subclass"] text.library-connection-label')).toBeNull();
  });

  it("repositions a class through the layout path, and shows the refusal an expression drag earns", async () => {
    // Arrange.
    const { container } = renderCanvas();
    const pizza = elementWithId(container, `res:${NS}Pizza`);

    // Act: press, move past the threshold, release - the pointer vocabulary the library hears.
    fireEvent(pizza, new MouseEvent("pointerdown", { bubbles: true, button: 0, clientX: 10, clientY: 10 }));
    fireEvent(pizza, new MouseEvent("pointermove", { bubbles: true, clientX: 90, clientY: 60 }));
    fireEvent(pizza, new MouseEvent("pointerup", { bubbles: true, clientX: 90, clientY: 60 }));

    // Assert: one move, carrying the authored corner - the library reports the dragged
    // CENTRE, and the module converts back before the layout path sees it. Pizza's corner is
    // (0,0) and jsdom's zero-size rect makes one pixel one unit, so the delta lands verbatim.
    expect(moves).toHaveLength(1);
    expect(moves[0].elementId).toBe(`res:${NS}Pizza`);
    expect(moves[0].x).toBe(80);
    expect(moves[0].y).toBe(50);

    // And a refusal from the backend is shown rather than swallowed (Requirement 3.2) - by the
    // move itself, on the one line the library draws around every canvas (client-centralization
    // Requirement 2; useDiagramStream.move.test.ts). This canvas draws no line of its own.
    moveError = "That is an anonymous class expression, whose identity does not survive an edit to the file.";
    const expression = elementWithId(container, EXPRESSION_ID);
    fireEvent(expression, new MouseEvent("pointerdown", { bubbles: true, button: 0, clientX: 10, clientY: 10 }));
    fireEvent(expression, new MouseEvent("pointermove", { bubbles: true, clientX: 90, clientY: 60 }));
    fireEvent(expression, new MouseEvent("pointerup", { bubbles: true, clientX: 90, clientY: 60 }));
    await vi.waitFor(() => expect(moves).toHaveLength(2));
    expect(container.textContent).not.toContain("does not survive an edit");
    expect(container.querySelector(".canvas-rejection")).toBeNull();
  });

  it("sends a toolbox drop as a placement and a gesture as one rel: call", () => {
    // Arrange.
    const { container } = renderCanvas();
    const surface = container.querySelector("svg.library-canvas-surface")!;
    const box = surface.getAttribute("viewBox")!.split(" ").map(Number);
    Object.defineProperty(surface, "getBoundingClientRect", {
      value: () => ({ x: 0, y: 0, top: 0, left: 0, right: box[2], bottom: box[3], width: box[2], height: box[3], toJSON: () => ({}) }),
    });

    // Act: a drop names the place it landed (Requirement 6.2), built by hand because
    // fireEvent.drop loses clientX/clientY in jsdom.
    const event = new MouseEvent("drop", { bubbles: true, cancelable: true, clientX: 40, clientY: 50 });
    Object.defineProperty(event, "dataTransfer", {
      value: { getData: () => "owl.add-class", types: ["application/x-adp-toolbox-item"] },
    });
    fireEvent(surface, event);

    // Assert.
    expect(executed[0].actionId).toBe("owl.add-class");
    expect((executed[0].source as { source: { value: { value: string } } }).source.value.value).toContain("new:");
  });

  it("shows the truncation banner and the counts it carries", () => {
    // Arrange.
    currentModel = { ...modelWith(), truncation: { shown: 4, total: 10 } };

    // Act.
    const { container } = renderCanvas();

    // Assert (Requirement 8.3).
    expect(container.querySelector(".owl-truncation-banner")!.textContent).toContain("Showing 4 of 10");
  });

  it("keeps a label inside its shape, with the full name still reachable", () => {
    // The guard behind "this looks chaotic": local names and Manchester expressions run long,
    // and drawn whole they smear across their neighbours.
    const long = node("res:x", "class", "TemporalReferenceSystemUsedForDurationDescription", 0, 0);
    expect(labelFor(long, 190).length).toBeLessThan(long.display.length);
    expect(labelFor(long, 190).endsWith("…")).toBe(true);

    // A short one is left exactly alone.
    expect(labelFor(node("res:y", "class", "Pizza", 0, 0), 190)).toBe("Pizza");

    // Card rows are sentences - a comment, a contributor's address - and are cut the same way.
    expect(fit("dct:contributor: mailto:chris.little@metoffice.gov.uk", 220).endsWith("…")).toBe(true);

    // The shape carries the whole name as a title, so nothing is lost by the trim.
    const { container } = renderCanvas();
    const pizza = container.querySelector(`[data-element-id="res:${NS}Pizza"]`)!;
    expect(pizza.querySelector("title")!.textContent).toBe("Pizza");
  });

  it("sizes cards by their content and shapes by their kind", () => {
    // Act & assert: a card grows with badges and rows; an anchor is smaller than a class.
    const plainCard = nodeSizeOf(node("res:a", "individual", "A", 0, 0));
    const fullCard = nodeSizeOf(
      node("res:b", "individual", "B", 0, 0, { badges: ["T"], rows: [{ predicate: "p", value: "v", annotation: "" }] }),
    );
    expect(fullCard.height).toBeGreaterThan(plainCard.height);
    expect(nodeSizeOf(node("thing:x", "thing", "Thing", 0, 0)).width).toBeLessThan(
      nodeSizeOf(node("res:c", "class", "C", 0, 0)).width,
    );
  });
});

describe("selection, as every canvas has it", () => {
  it("highlights a pushed class and subclass edge, and clears on a background press (centralized-selection 9.2)", () => {
    expectLibrarySelection({
      mountWith: (id) => {
        currentModel = modelWith();
        currentLoading = false;
        currentFailed = false;
        currentSelectionKey = id === null ? null : `element:${id}`;
        return renderCanvas();
      },
      pushedIds: () => idsPushed(selections),
      element: `res:${NS}Pizza`,
      connection: "edge:subclass",
    });
  });
});
