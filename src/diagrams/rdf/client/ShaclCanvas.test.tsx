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

const { ShaclCanvas } = await import("./ShaclCanvas");

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

    fireEvent.mouseDown(card, { clientX: 10, clientY: 10 });
    fireEvent.mouseMove(container.querySelector(".shacl-surface")!, { clientX: 90, clientY: 60 });
    fireEvent.mouseUp(container.querySelector(".shacl-surface")!);

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
    const surface = container.querySelector(".shacl-surface")!;
    const data = new Map([["application/x-adp-toolbox-item", "shacl.add-property-row"]]);
    const dataTransfer = {
      getData: (type: string) => data.get(type) ?? "",
      types: ["application/x-adp-toolbox-item"],
      dropEffect: "",
    };

    fireEvent.drop(container.querySelector(`[data-element-id="${PERSON}"]`)!, { dataTransfer });
    expect(executed.at(-1)?.actionId).toBe("shacl.add-property-row");

    fireEvent.drop(surface, { dataTransfer });
    expect(executed).toHaveLength(2);
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
