import { describe, expect, it, vi, beforeEach } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import {
  TARGET_CLASS,
  emptyShaclModel,
  type ShaclModel,
  type ShaclShape,
} from "./shaclModel";

let currentModel: ShaclModel = emptyShaclModel;
let currentLoading = false;
let currentFailed = false;
let currentSelectionKey: string | null = null;
let currentActions: unknown[] = [];
let moves: { elementId: string; x: number; y: number }[] = [];
let moveError = "";
let selections: unknown[] = [];
let executed: { actionId: string; source: unknown }[] = [];

vi.mock("./useShaclStream", () => ({
  useShaclStream: () => ({
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

const { ShaclCanvas, CARD_WIDTH } = await import("./ShaclCanvas");

const PERSON = "res:http://example.org/PersonShape";
const ADDRESS = "res:http://example.org/AddressShape";

function shape(id: string, display: string, x: number, y: number, options: Partial<ShaclShape> = {}): ShaclShape {
  return {
    id,
    x,
    y,
    iri: id.startsWith("res:") ? id.slice("res:".length) : "",
    display,
    blank: false,
    deactivated: false,
    severity: "",
    closed: false,
    name: "",
    description: "",
    targets: [],
    rows: [],
    ...options,
  };
}

function modelWith(overrides: Partial<ShaclModel> = {}): ShaclModel {
  return {
    shapes: new Map([
      [PERSON, shape(PERSON, "ex:PersonShape", 0, 0, {
        targets: [
          { kind: TARGET_CLASS, termDisplay: "ex:Person", termIri: "http://example.org/Person", describedInFile: false },
        ],
        rows: [
          { path: "ex:name", name: "", summary: "xsd:string", cardinality: "[1..1]", sparql: false, blank: true, severity: "" },
        ],
      })],
      [ADDRESS, shape(ADDRESS, "ex:AddressShape", 320, 0)],
    ]),
    edges: new Map(),
    truncation: null,
    ...overrides,
  };
}

function renderCanvas() {
  return render(<ShaclCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["shapes.ttl"]} />);
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

describe("ShaclCanvas", () => {
  it("draws a card per shape, with its targets and rows inside it", () => {
    const { container } = renderCanvas();

    // Two cards, and nothing else: the targeted class is not an element, and neither is the
    // blank property shape - both travel inside their card.
    expect(container.querySelectorAll("[data-element-id]")).toHaveLength(2);
    expect(container.textContent).toContain("ex:PersonShape");
    expect(container.textContent).toContain("targets class ex:Person");
    expect(container.textContent).toContain("ex:name");
    expect(container.textContent).toContain("[1..1]");
  });

  it("keeps a row inside its card: the path wears its sized class, and a long summary is cut before the edge", () => {
    // Two defects on one row of the W3C spec examples. The migration put `shacl-row` on the
    // path's text but not `shacl-row-path`, the class shacl.css sizes to 11px, so every path
    // drew at the browser's 16px. And the summary - drawn 110 in, untruncated since before the
    // migration - ran a hundred past the card's right edge.
    const summary = "or({ datatype xsd:string }, { class ex:Address })";
    currentModel = modelWith({
      shapes: new Map([
        [PERSON, shape(PERSON, "ex:PersonAddressShape", 0, 0, {
          rows: [{ path: "ex:address", name: "", summary, cardinality: "[0..*]", sparql: false, blank: true, severity: "" }],
        })],
      ]),
    });

    const { container } = renderCanvas();

    const path = [...container.querySelectorAll("text.shacl-row")].find((text) => text.textContent === "ex:address")!;
    expect(path.classList.contains("shacl-row-path")).toBe(true);
    const drawn = container.querySelector("text.shacl-row-summary")!;
    expect(drawn.textContent!.endsWith("…")).toBe(true);
    expect(summary.startsWith(drawn.textContent!.slice(0, -1))).toBe(true);
    // The path's x is the card's left edge plus its 8 inset; by truncation's own 7-per-character
    // estimate the summary must end eight short of the right edge.
    const left = Number(path.getAttribute("x")) - 8;
    expect(Number(drawn.getAttribute("x")) - left + drawn.textContent!.length * 7).toBeLessThanOrEqual(CARD_WIDTH - 8);
  });

  it("renders an absent target exactly like a present one", () => {
    const { container } = renderCanvas();
    const absent = [...container.querySelectorAll(".shacl-target")].map((node) => node.getAttribute("class"));

    currentModel = modelWith({
      shapes: new Map([
        [PERSON, shape(PERSON, "ex:PersonShape", 0, 0, {
          targets: [
            { kind: TARGET_CLASS, termDisplay: "ex:Person", termIri: "http://example.org/Person", describedInFile: true },
          ],
        })],
      ]),
    });
    const present = [...renderCanvas().container.querySelectorAll(".shacl-target")].map((node) => node.getAttribute("class"));

    // The whole point of Requirement 1.3: a shapes graph aims at data held elsewhere, so a
    // target the file does not describe is the normal case and must not be styled as a problem.
    expect(present).toEqual(absent);
  });

  it("draws a reference between two shapes as a labeled edge", () => {
    currentModel = modelWith({
      edges: new Map([
        ["shacl-edge:a|node|b", {
          id: "shacl-edge:a|node|b",
          fromElementId: PERSON,
          toElementId: ADDRESS,
          kind: "node",
          label: "ex:address",
        }],
      ]),
    });

    const { container } = renderCanvas();

    expect(container.querySelector(".shacl-edge-node")).not.toBeNull();
    expect(container.textContent).toContain("ex:address");
  });

  it("wears the deactivated and closed badges, and dims a deactivated shape", () => {
    currentModel = modelWith({
      shapes: new Map([[PERSON, shape(PERSON, "ex:PersonShape", 0, 0, { deactivated: true, closed: true, severity: "sh:Warning" })]]),
    });

    const { container } = renderCanvas();

    expect(container.querySelector(".shacl-shape-deactivated")).not.toBeNull();
    const badges = container.querySelector(".shacl-badges")?.textContent ?? "";
    expect(badges).toContain("deactivated");
    expect(badges).toContain("closed");
    expect(badges).toContain("sh:Warning");
  });

  it("marks an anonymous shape so it reads as one, and still draws it", () => {
    currentModel = modelWith({
      shapes: new Map([["blank:0", shape("blank:0", "anonymous shape", 0, 0, { blank: true, iri: "" })]]),
    });

    const { container } = renderCanvas();

    expect(container.querySelector(".shacl-shape-blank")).not.toBeNull();
    expect(container.textContent).toContain("anonymous shape");
  });

  it("sends a drag as a layout edit, and shows the backend's refusal rather than deciding it", async () => {
    moveError = "This constraint is written as a blank node…";
    const { container } = renderCanvas();
    const card = container.querySelector(`[data-element-id="${PERSON}"]`)!;

    fireEvent(card, new MouseEvent("pointerdown", { bubbles: true, button: 0, clientX: 10, clientY: 10 }));
    fireEvent(card, new MouseEvent("pointermove", { bubbles: true, clientX: 90, clientY: 60 }));
    fireEvent(card, new MouseEvent("pointerup", { bubbles: true, clientX: 90, clientY: 60 }));

    expect(moves).toHaveLength(1);
    expect(moves[0].elementId).toBe(PERSON);

    // The canvas reports the refusal; it does not decide it - the backend is the authority.
    expect(await screenText(container)).toContain("written as a blank node");
  });

  it("shows the truncation banner with the real totals", () => {
    currentModel = modelWith({ truncation: { shown: 40, total: 137 } });

    const { container } = renderCanvas();

    expect(container.textContent).toContain("Showing 40 of 137 shapes");
    expect(container.textContent).toContain("withheld");
  });

  it("lands a toolbox drop on the card under the pointer, and as a placement otherwise", () => {
    const { container } = renderCanvas();
    const surface = container.querySelector("svg.library-canvas-surface")!;
    const box = surface.getAttribute("viewBox")!.split(" ").map(Number);
    Object.defineProperty(surface, "getBoundingClientRect", {
      value: () => ({ x: 0, y: 0, top: 0, left: 0, right: box[2], bottom: box[3], width: box[2], height: box[3], toJSON: () => ({}) }),
    });
    const dataTransfer = {
      getData: (type: string) => (type === "application/x-adp-toolbox-item" ? "shacl.add-property-row" : ""),
      types: ["application/x-adp-toolbox-item"],
      dropEffect: "",
    };
    const dropAt = (clientX: number, clientY: number) => {
      const event = new MouseEvent("drop", { bubbles: true, cancelable: true, clientX, clientY });
      Object.defineProperty(event, "dataTransfer", { value: dataTransfer });
      fireEvent(surface, event);
    };

    // Inside the Person card, whose authored corner is (0,0): the drop acts on that card.
    dropAt(130 - box[0], 20 - box[1]);
    expect(executed.at(-1)?.actionId).toBe("shacl.add-property-row");
    expect((executed.at(-1)?.source as { source: { value: { value: string } } }).source.value.value).toBe(PERSON);

    // On empty canvas: the same entry lands as a placement instead.
    dropAt(1000 - box[0], 1000 - box[1]);
    expect(executed).toHaveLength(2);
    expect((executed.at(-1)?.source as { source: { value: { value: string } } }).source.value.value).toContain("new:");
  });

  it("says so when the diagram cannot be opened", () => {
    currentFailed = true;

    expect(renderCanvas().container.textContent).toContain("could not be opened");
  });
});

/** The rejection is rendered after the move promise settles. */
async function screenText(container: HTMLElement): Promise<string> {
  await Promise.resolve();
  await Promise.resolve();
  return container.textContent ?? "";
}
