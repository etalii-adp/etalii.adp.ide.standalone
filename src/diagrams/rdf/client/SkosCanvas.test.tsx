import { describe, expect, it, vi, beforeEach } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import {
  ALTERNATE,
  HIERARCHY,
  IRI_FALLBACK,
  MAPPING,
  RELATED,
  emptySkosModel,
  type SkosConcept,
  type SkosModel,
} from "./skosModel";

let currentModel: SkosModel = emptySkosModel;
let currentLoading = false;
let currentFailed = false;
let currentSelectionKey: string | null = null;
let currentActions: unknown[] = [];
let moves: { elementId: string; x: number; y: number }[] = [];
let selections: unknown[] = [];
let executed: { actionId: string; source: unknown }[] = [];

vi.mock("./useSkosStream", () => ({
  useSkosStream: () => ({
    model: currentModel,
    loading: currentLoading,
    failed: currentFailed,
    reportView: () => {},
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

const { SkosCanvas } = await import("./SkosCanvas");

function concept(id: string, label: string, x: number, y: number, options: Partial<SkosConcept> = {}): SkosConcept {
  return {
    id,
    x,
    y,
    iri: id.startsWith("res:") ? id.slice("res:".length) : "",
    label,
    languageTag: "en",
    labelKind: 0,
    notation: "",
    schemeIris: [],
    blank: false,
    languageChip: false,
    ...options,
  };
}

const TEA = "res:http://example.org/tea";
const MILK = "res:http://example.org/milk";

function modelWith(overrides: Partial<SkosModel> = {}): SkosModel {
  return {
    concepts: new Map([
      [TEA, concept(TEA, "Tea", 0, 100)],
      [MILK, concept(MILK, "Milk", 240, 100)],
    ]),
    schemes: new Map([
      ["res:http://example.org/scheme", {
        id: "res:http://example.org/scheme",
        x: 0,
        y: 0,
        iri: "http://example.org/scheme",
        label: "Drinks",
        languageTag: "en",
        labelKind: 0,
        memberCount: 2,
      }],
    ]),
    collections: new Map(),
    edges: new Map(),
    truncation: null,
    ...overrides,
  };
}

function renderCanvas() {
  return render(<SkosCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["scheme.ttl"]} />);
}

describe("SkosCanvas", () => {
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

  it("draws concepts and their scheme region", () => {
    const { container } = renderCanvas();

    expect(container.querySelectorAll(".skos-concept").length).toBe(2);
    expect(container.querySelector(".skos-region")).not.toBeNull();
    // The region names its member count, so a reader sees the scheme's size without counting.
    expect(container.textContent).toContain("Drinks (2)");
  });

  it("shows the language chip only where the backend said to", () => {
    // The comparison against the session's display language is the backend's: the canvas
    // renders what it was told, so the chip cannot disagree with the label beside it.
    currentModel = modelWith({
      concepts: new Map([
        [TEA, concept(TEA, "Thee", 0, 100, { languageTag: "nl", languageChip: true })],
        [MILK, concept(MILK, "Milk", 240, 100, { languageTag: "en", languageChip: false })],
      ]),
    });
    const { container } = renderCanvas();

    const chips = [...container.querySelectorAll(".skos-language-chip")].map((chip) => chip.textContent);
    expect(chips).toEqual(["nl"]);
  });

  it("marks a stand-in label and dims an IRI fallback", () => {
    currentModel = modelWith({
      concepts: new Map([
        [TEA, concept(TEA, "Cuppa", 0, 100, { labelKind: ALTERNATE })],
        [MILK, concept(MILK, "milk", 240, 100, { labelKind: IRI_FALLBACK })],
      ]),
    });
    const { container } = renderCanvas();

    expect(container.querySelector(".skos-label-alternate")).not.toBeNull();
    expect(container.querySelector(".skos-label-fallback")).not.toBeNull();
  });

  it("badges a notation before the label", () => {
    currentModel = modelWith({
      concepts: new Map([[TEA, concept(TEA, "Tea", 0, 100, { notation: "T1" })]]),
    });
    const { container } = renderCanvas();

    expect(container.querySelector(".skos-notation")?.textContent).toBe("T1");
  });

  it("draws each edge language in its own style", () => {
    currentModel = modelWith({
      edges: new Map([
        ["h", { id: "h", fromElementId: TEA, toElementId: MILK, kind: HIERARCHY, predicate: "", assertedBothWays: false }],
        ["r", { id: "r", fromElementId: MILK, toElementId: TEA, kind: RELATED, predicate: "", assertedBothWays: false }],
        ["m", { id: "m", fromElementId: TEA, toElementId: MILK, kind: MAPPING, predicate: "skos:exactMatch", assertedBothWays: false }],
      ]),
    });
    const { container } = renderCanvas();

    expect(container.querySelector(".skos-edge-hierarchy")).not.toBeNull();
    expect(container.querySelector(".skos-edge-related")).not.toBeNull();
    expect(container.querySelector(".skos-edge-mapping")).not.toBeNull();
    // Only a mapping carries a label - the hierarchy's direction is its layering.
    expect([...container.querySelectorAll(".skos-edge-label")].map((label) => label.textContent)).toEqual(["skos:exactMatch"]);
  });

  it("dispatches file-under from the top anchor and relate from the side anchor", () => {
    currentSelectionKey = `element:${TEA}`;
    const { container } = renderCanvas();

    const anchors = container.querySelectorAll(".skos-anchor-hit");
    expect(anchors.length).toBe(2);
    const target = container.querySelector(`[data-element-id="${MILK}"]`)!;

    // Top anchor: the dragged concept is filed under the one it is released on.
    fireEvent.mouseDown(anchors[0], { clientX: 100, clientY: 100 });
    fireEvent.mouseEnter(target);
    fireEvent.mouseUp(target);
    expect(executed.at(-1)?.actionId).toBe("skos.file-under");

    // Side anchor: the same gesture, cross-linking instead.
    fireEvent.mouseDown(anchors[1], { clientX: 100, clientY: 100 });
    fireEvent.mouseEnter(target);
    fireEvent.mouseUp(target);
    expect(executed.at(-1)?.actionId).toBe("skos.relate");
  });

  it("sends a drag to the layout path, never to the SKOS file", () => {
    const { container } = renderCanvas();
    const box = container.querySelector(`[data-element-id="${TEA}"]`)!;

    fireEvent.mouseDown(box, { clientX: 10, clientY: 10 });
    fireEvent.mouseMove(container.querySelector(".skos-surface")!, { clientX: 60, clientY: 40 });
    fireEvent.mouseUp(container.querySelector(".skos-surface")!);

    // One move, carrying the authored position - the backend turns it into the layout: command.
    expect(moves.length).toBe(1);
    expect(moves[0].elementId).toBe(TEA);
  });

  it("drops a toolbox entry at a placement id under the pointer", () => {
    const { container } = renderCanvas();

    fireEvent.drop(container.querySelector(".skos-surface")!, {
      dataTransfer: { getData: () => "skos.add-concept", types: ["application/x-adp-toolbox-item"] },
      clientX: 120,
      clientY: 80,
    });

    expect(executed.at(-1)?.actionId).toBe("skos.add-concept");
  });

  it("says how much of a truncated vocabulary it is showing", () => {
    currentModel = modelWith({ truncation: { shown: 1000, total: 75736 } });
    const { container } = renderCanvas();

    expect(container.querySelector(".skos-truncation-banner")?.textContent).toContain("Showing 1000 of 75736 terms");
  });

  it("says so when the diagram cannot be opened", () => {
    currentFailed = true;
    const { container } = renderCanvas();

    expect(container.textContent).toContain("could not be opened");
  });
});
