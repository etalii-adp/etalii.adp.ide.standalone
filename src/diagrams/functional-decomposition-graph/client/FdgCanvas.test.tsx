import { beforeEach, describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { selectedElementIdOf } from "@client/canvas/selection";
import type { ContextSelection } from "@client/generated/context_pb";
import {
  FdgConnectionPayloadSchema,
  FdgElementPayloadSchema,
} from "@client/generated/functional-decomposition-graph_pb";
import { expectLibrarySelection } from "@client/canvas/library/testing/expectLibrarySelection";
import { emptyModel, type FdgModel } from "./fdgModel";

/**
 * The REAL canvas, mounted: what the library draws from this module's definition and model, and
 * the shared selection assertion every canvas's own test runs. The handlers' routes are
 * `fdgHandlers.test.tsx`'s, and the connect gesture over the example is `fdgConnect.test.tsx`'s.
 */

let currentModel: FdgModel = emptyModel;
let currentSelectionKey: string | null = null;
let selections: unknown[] = [];

vi.mock("@client/diagrams/useDiagramStream", () => ({
  useDiagramStream: () => ({
    model: currentModel,
    loading: false,
    failed: false,
    client: { moveElement: () => Promise.resolve({ error: "" }) },
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
  useContextConnection: () => ({
    watchId: new Uint8Array([9]),
    select: (selection: unknown) => selections.push(selection),
    executeAction: () => Promise.resolve({ accepted: true, error: "" }),
    setProperty: () => Promise.resolve({ accepted: true, error: "" }),
  }),
}));

vi.mock("@client/shell/panels/DiagramViewContext", () => ({ useRegisterDiagramView: () => undefined }));
vi.mock("@client/shell/panels/InlineLabelPlacementContext", () => ({ useRegisterInlineLabelPlacement: () => undefined }));
vi.mock("@client/shell/panels/DiagramToolboxContext", () => ({
  TOOLBOX_DRAG_TYPE: "application/x-adp-toolbox-item",
  useRegisterDiagramToolbox: () => undefined,
}));
vi.mock("@client/shell/panels/useToolboxItems", () => ({ useToolboxItems: () => [] }));

const { FdgCanvas } = await import("./FdgCanvas");

/** Planning owning the Task list, and a Comment beside them - centres, as the backend sends them. */
function modelWith(): FdgModel {
  const element = (name: string, text: string, width: number, height: number) =>
    create(FdgElementPayloadSchema, { name, text, width, height });
  return {
    elements: new Map([
      ["planning", { id: "planning", type: "ui-element", x: 670, y: 64, payload: element("Planning", "", 140, 48) }],
      ["task-list", { id: "task-list", type: "ui-element", x: 350, y: 184, payload: element("Task list", "", 140, 48) }],
      ["note", { id: "note", type: "comment", x: 150, y: 548, payload: element("", "Offline first.", 260, 96) }],
    ]),
    connections: new Map([
      ["c-1", { id: "c-1", type: "ui-child", payload: create(FdgConnectionPayloadSchema, { fromElementId: "planning", toElementId: "task-list", name: "" }) }],
    ]),
  };
}

function renderCanvas() {
  return render(<FdgCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["field-service.fdg"]} />);
}

beforeEach(() => {
  currentModel = modelWith();
  currentSelectionKey = null;
  selections = [];
});

describe("the functional decomposition graph canvas, mounted", () => {
  it("draws each element in its type's fill class, on the shared node class", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelector('[data-element-id="planning"] .canvas-node.fdg-ui-element')).not.toBeNull();
    expect(container.querySelector('[data-element-id="note"] .canvas-node.fdg-comment')).not.toBeNull();
    expect(container.textContent).toContain("Planning");
  });

  it("selects through the library, exactly as every other canvas does", () => {
    expectLibrarySelection({
      mountWith: (id) => {
        currentSelectionKey = id === null ? null : `element:${id}`;
        return renderCanvas();
      },
      pushedIds: () => selections.map((push) => (push === null ? null : (selectedElementIdOf(push as ContextSelection) ?? null))),
      element: "planning",
      connection: "c-1",
    });
  });
});
