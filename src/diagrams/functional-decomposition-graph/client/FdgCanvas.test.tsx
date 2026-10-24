import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import {
  FdgConnectionPayloadSchema,
  FdgElementPayloadSchema,
} from "@client/generated/functional-decomposition-graph_pb";
import { ContextActionGroupSchema, ContextActionSchema, type ContextActionGroup } from "@client/generated/context-contract_pb";
import { ContextSelectionAction, type ContextSelection } from "@client/generated/context_pb";
import { selectedElementIdOf } from "@client/canvas/selection";
import { expectLibrarySelection } from "@client/canvas/library/testing/expectLibrarySelection";
import { emptyModel, type FdgModel } from "./fdgModel";
import { FDG_ELEMENT_TYPES, FdgActions } from "./fdgIds";
import { fakeContextConnection, idsPushed, dragAndSettle } from "@client/canvas/library/testing/canvasHarness";

// The moves the canvas sent, and what the backend answers one with: empty when recorded, its refusal otherwise.
const moves: string[] = [];
let moveAnswer = "";

/**
 * The REAL canvas, mounted: what the library draws from this module's definition and model, and
 * the shared selection assertion every canvas's own test runs. The handlers' routes are
 * `fdgHandlers.test.tsx`'s, and the connect gesture over the example is `fdgConnect.test.tsx`'s.
 */

let currentModel: FdgModel = emptyModel;
let currentSelectionKey: string | null = null;
let selections: unknown[] = [];
let currentActions: ContextActionGroup[] = [];

vi.mock("@client/diagrams/useDiagramStream", () => ({
  useDiagramStream: () => ({
    model: currentModel,
    loading: false,
    failed: false,
    client: { moveElement: () => Promise.resolve({ error: "" }) },
    // The stream's one move (client-centralization task 8), which FdgCanvas now takes from here.
    moveElementTo: (elementId: string) => {
      moves.push(elementId);
      return Promise.resolve(moveAnswer);
    },
  }),
}));

vi.mock("@client/diagrams/viewReport", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@client/diagrams/viewReport")>()),
  viewReportOf: () => () => {},
}));

vi.mock("@client/shell/context/ContextConnectionProvider", () => ({
  innermostKey: () => currentSelectionKey,
  useContextPrompt: () => ({ prompt: null, onPropose: vi.fn(), onSubmit: vi.fn(), onCancel: vi.fn() }),
  useContextSelection: () => ({ selection: currentSelectionKey, levels: [], actions: currentActions }),
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
  currentActions = [];
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

  it("snaps an element back when the backend refuses its move, rather than leaving it at the drop", async () => {
    // Arrange: the library holds a drop until the model moves the element, and a refused move never
    // does - so the move's answer has to be handed back for the element to return (ruled 2026-10-19).
    moves.length = 0;
    moveAnswer = "This diagram is read-only.";
    const { container } = renderCanvas();

    // Act.
    const { before, after } = await dragAndSettle(container, "planning");

    // Assert.
    expect(moves, "the move was never sent, so this test cannot say anything").toHaveLength(1);
    expect(after).toBe(before);
    moveAnswer = "";
  });

  it("selects through the library, exactly as every other canvas does", () => {
    expectLibrarySelection({
      mountWith: (id) => {
        currentSelectionKey = id === null ? null : `element:${id}`;
        return renderCanvas();
      },
      pushedIds: () => idsPushed(selections),
      element: "planning",
      connection: "c-1",
    });
  });
});

/**
 * The labels the backend answers a placement with: the specification's canvas menu, in the order of the
 * tool's element types (`FDG_ELEMENT_TYPES`), which is not the palette's.
 */
const ADD_HERE_LABELS = ["Add ui element here", "Add data element here", "Add action here", "Add function here", "Add comment here"];

describe("the menu on empty canvas, which the user ruled is offered (chat ruling of 2026-10-24)", () => {
  it("asks for the menu at the point right-clicked, and offers the five Add … here entries the backend answers", async () => {
    // Arrange.
    const { container, rerender } = renderCanvas();
    selections = [];

    // Act: a right-click on the surface, on no element and no connection.
    fireEvent.contextMenu(container.querySelector("svg.library-canvas-surface")!, { clientX: 320, clientY: 240 });

    // Assert: the canvas asked for the menu of a placement, not of an item.
    expect(selections, "a right-click on empty canvas asked for no menu, so it is not offered").toHaveLength(1);
    const push = selections[0] as ContextSelection;
    const placement = selectedElementIdOf(push);
    expect(placement).toMatch(/^new:-?[\d.]+,-?[\d.]+$/);
    const inner = push.detail.case === "child" ? push.detail.value : push;
    expect(inner.detail.case === "action" ? inner.detail.value : null).toBe(ContextSelectionAction.CONTEXT_MENU);

    // Act: the backend answers for the placement with its add actions.
    currentSelectionKey = `element:${placement}`;
    currentActions = [create(ContextActionGroupSchema, {
      actions: FDG_ELEMENT_TYPES.map((type, index) => create(ContextActionSchema, { id: FdgActions.add(type), label: ADD_HERE_LABELS[index]!, available: true })),
    })];
    rerender(<FdgCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["field-service.fdg"]} />);

    // Assert: the menu opens with exactly those five, in that order, and an entry runs its add action.
    const entries = await screen.findAllByRole("menuitem");
    expect(entries.map((entry) => entry.textContent?.trim())).toEqual(ADD_HERE_LABELS);
    fireEvent.click(entries[0]!);
    expect(connection.executeAction).toHaveBeenCalledWith(FdgActions.add(FDG_ELEMENT_TYPES[0]!));
  });
});
