import { beforeEach, describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { AbmChildPayloadSchema, AbmNodePayloadSchema } from "@client/generated/agent-behavior-modelling_pb";
import { expectLibrarySelection } from "@client/canvas/library/testing/expectLibrarySelection";
import { emptyModel, type AbmModel } from "./abmModel";
import type { AbmNodeKind } from "./abmIds";
import { fakeContextConnection, idsPushed } from "@client/canvas/library/testing/canvasHarness";

/**
 * The REAL canvas, mounted: what the library draws from this module's definition and model, and
 * the shared selection assertion every canvas's own test runs.
 */

let currentModel: AbmModel = emptyModel;
let currentSelectionKey: string | null = null;
let selections: unknown[] = [];

vi.mock("@client/diagrams/useDiagramStream", () => ({
  useDiagramStream: () => ({
    model: currentModel,
    loading: false,
    failed: false,
    client: { moveElement: () => Promise.resolve({ error: "" }) },
    moveElementTo: () => Promise.resolve(""),
  }),
}));

vi.mock("@client/diagrams/viewReport", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@client/diagrams/viewReport")>()),
  viewReportOf: () => () => {},
}));

vi.mock("@client/shell/context/ContextConnectionProvider", () => ({
  innermostKey: () => currentSelectionKey,
  useContextPrompt: () => ({ prompt: null, onPropose: vi.fn(), onSubmit: vi.fn(), onCancel: vi.fn() }),
  useContextSelection: () => ({ selection: currentSelectionKey, levels: [], actions: [] }),
  useContextConnection: () => connection,
}));

const connection = fakeContextConnection({
  watchId: new Uint8Array([9]),
  select: (selection: unknown) => selections.push(selection),
});

vi.mock("@client/shell/panels/DiagramViewContext", () => ({ useRegisterDiagramView: () => undefined }));
vi.mock("@client/shell/panels/InlineLabelPlacementContext", () => ({ useRegisterInlineLabelPlacement: () => undefined }));
vi.mock("@client/shell/panels/DiagramToolboxContext", () => ({
  TOOLBOX_DRAG_TYPE: "application/x-adp-toolbox-item",
  useRegisterDiagramToolbox: () => undefined,
}));
vi.mock("@client/shell/panels/useToolboxItems", () => ({ useToolboxItems: () => [] }));

const { AbmCanvas } = await import("./AbmCanvas");

/** A sequence over a check and a step the Markdown gave no keyword - centres, as the backend sends them. */
function modelWith(): AbmModel {
  const node = (id: string, kind: AbmNodeKind, keyword: string, label: string, x: number, y: number, implicit = false) =>
    [id, { id, kind, x, y, payload: create(AbmNodePayloadSchema, { keyword, label, width: 200, height: 60, implicit, place: id }) }] as const;
  return {
    nodes: new Map([
      node("1", "sequence", "Do in order", "Handle the request", 214, 30),
      node("1.1", "check", "Check", "The request is clear", 100, 146),
      node("1.2", "action", "Do", "Answer it", 328, 146, true),
    ]),
    lines: new Map([
      ["child:1.1", { id: "child:1.1", payload: create(AbmChildPayloadSchema, { fromElementId: "1", toElementId: "1.1", index: 0 }) }],
      ["child:1.2", { id: "child:1.2", payload: create(AbmChildPayloadSchema, { fromElementId: "1", toElementId: "1.2", index: 1 }) }],
    ]),
  };
}

function renderCanvas() {
  return render(<AbmCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["support.md"]} />);
}

beforeEach(() => {
  currentModel = modelWith();
  currentSelectionKey = null;
  selections = [];
});

describe("the agent behavior modelling canvas, mounted", () => {
  it("draws each node in its family's fill class, with its keyword above its label", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelector('[data-element-id="1"] .canvas-node.abm-composite')).not.toBeNull();
    expect(container.querySelector('[data-element-id="1.1"] .canvas-node.abm-check')).not.toBeNull();
    expect(container.textContent).toContain("Do in order");
    expect(container.textContent).toContain("Handle the request");
  });

  it("marks only the node the Markdown gave no keyword as implicit", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelector('[data-element-id="1.2"].abm-implicit, [data-element-id="1.2"] .abm-implicit')).not.toBeNull();
    expect(container.querySelector('[data-element-id="1.1"].abm-implicit, [data-element-id="1.1"] .abm-implicit')).toBeNull();
  });

  it("selects through the library, exactly as every other canvas does", () => {
    expectLibrarySelection({
      mountWith: (id) => {
        currentSelectionKey = id === null ? null : `element:${id}`;
        return renderCanvas();
      },
      pushedIds: () => idsPushed(selections),
      element: "1.1",
      connection: "child:1.1",
    });
  });
});
