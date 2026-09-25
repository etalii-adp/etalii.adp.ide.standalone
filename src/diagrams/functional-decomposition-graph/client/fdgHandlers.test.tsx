import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, render } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { FdgElementPayloadSchema } from "@client/generated/functional-decomposition-graph_pb";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel } from "@client/canvas/library/api/diagramModel";
import { emptyModel, type FdgModel } from "./fdgModel";

/**
 * Task 14's handler guard: each library event produces its ONE route and nothing else - a move
 * through the stream's `moveElement`, a resize through `setProperty`, and a drop, connection,
 * deletion or rename through a context action (the user's chat ruling of 2026-09-25).
 *
 * The library itself is replaced by a stand-in that keeps the handlers it was given, so each event
 * is raised exactly as the library would raise it and what reaches the transports is all that is
 * measured. The gestures themselves are the library's own suites' concern, and the connect gesture
 * over the example is `fdgConnect.test.tsx`'s.
 */

let captured: { events: DiagramEventHandlers; model: DiagramModel } | null = null;
let currentModel: FdgModel = emptyModel;
let executed: { actionId: string; targetId: string }[] = [];
let properties: { propertyId: string; value: string; targetId: string }[] = [];
let moves: { elementId: string; x: number; y: number }[] = [];
let refuse = "";

type Source = { source: { value: { value: string } } };
const idOf = (source: unknown) => (source as Source).source.value.value;

vi.mock("@client/canvas/library/DiagramCanvas", () => ({
  DiagramCanvas: (props: { events: DiagramEventHandlers; model: DiagramModel }) => {
    captured = { events: props.events, model: props.model };
    return null;
  },
}));

vi.mock("@client/diagrams/useDiagramStream", () => ({
  useDiagramStream: () => ({
    model: currentModel,
    loading: false,
    failed: false,
    client: {
      moveElement: (request: { elementId: string; position: { x: number; y: number } }) => {
        moves.push({ elementId: request.elementId, x: request.position.x, y: request.position.y });
        return Promise.resolve({ error: "" });
      },
    },
  }),
}));

vi.mock("@client/diagrams/viewReport", () => ({ viewReportOf: () => () => {} }));
vi.mock("@client/diagrams/useViewReport", () => ({ useViewReport: () => undefined }));
vi.mock("@client/shell/panels/useToolboxItems", () => ({ useToolboxItems: () => [] }));

vi.mock("@client/shell/context/ContextConnectionProvider", () => ({
  useContextPrompt: () => ({ prompt: null, onPropose: vi.fn(), onSubmit: vi.fn(), onCancel: vi.fn() }),
  useContextConnection: () => ({
    watchId: new Uint8Array([9]),
    executeAction: (actionId: string, source?: unknown) => {
      executed.push({ actionId, targetId: idOf(source) });
      return Promise.resolve(refuse ? { accepted: false, error: refuse } : { accepted: true, error: "" });
    },
    setProperty: (propertyId: string, value: string, source?: unknown) => {
      properties.push({ propertyId, value, targetId: idOf(source) });
      return Promise.resolve({ accepted: true, error: "" });
    },
  }),
}));

const { FdgCanvas } = await import("./FdgCanvas");

/** Planning at top-left (600, 40), 140 wide and the shared 48 tall - so its centre is (670, 64). */
function modelWith(): FdgModel {
  const payload = (name: string, text: string, width: number, height: number) =>
    create(FdgElementPayloadSchema, { name, text, width, height });
  return {
    elements: new Map([
      ["planning", { id: "planning", type: "ui-element", x: 670, y: 64, payload: payload("Planning", "", 140, 48) }],
      ["note", { id: "note", type: "comment", x: 150, y: 548, payload: payload("", "Offline first.", 260, 96) }],
    ]),
    connections: new Map(),
  };
}

function renderCanvas() {
  render(<FdgCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["field-service.fdg"]} />);
  return captured!.events;
}

/** Lets the handler's own promise chain run to the end. */
const settle = () => act(async () => {});

beforeEach(() => {
  captured = null;
  currentModel = modelWith();
  executed = [];
  properties = [];
  moves = [];
  refuse = "";
});

describe("the functional decomposition graph canvas answers each library event through its one route", () => {
  it("a move goes through moveElement, as the TOP-LEFT the document holds", async () => {
    // Arrange.
    const events = renderCanvas();

    // Act: the library reports the centre it drew Planning at.
    events.onElementMoved!({ kind: "element-moved", elementId: "planning", position: { x: 100, y: 200 } });
    await settle();

    // Assert.
    expect(moves).toEqual([{ elementId: "planning", x: 100 - 70, y: 200 - 24 }]);
    expect(executed).toEqual([]);
    expect(properties).toEqual([]);
  });

  it("a resize of the right edge is one width, through setProperty, and no move", async () => {
    // Arrange.
    const events = renderCanvas();

    // Act.
    events.onElementResized!({ kind: "element-resized", elementId: "planning", side: "right", bounds: { x: 600, y: 40, width: 180.4, height: 48 } });
    await settle();

    // Assert.
    expect(properties).toEqual([{ propertyId: "fdg.width", value: "180", targetId: "planning" }]);
    expect(moves).toEqual([]);
    expect(executed).toEqual([]);
  });

  it("a Comment's bottom edge is its height", async () => {
    // Arrange.
    const events = renderCanvas();

    // Act.
    events.onElementResized!({ kind: "element-resized", elementId: "note", side: "bottom", bounds: { x: 20, y: 500, width: 260, height: 130 } });
    await settle();

    // Assert.
    expect(properties).toEqual([{ propertyId: "fdg.height", value: "130", targetId: "note" }]);
    expect(moves).toEqual([]);
  });

  it("the left edge is a width and then the new top-left, because the far edge stays put", async () => {
    // Arrange.
    const events = renderCanvas();

    // Act.
    events.onElementResized!({ kind: "element-resized", elementId: "planning", side: "left", bounds: { x: 560, y: 40, width: 180, height: 48 } });
    await settle();

    // Assert.
    expect(properties).toEqual([{ propertyId: "fdg.width", value: "180", targetId: "planning" }]);
    expect(moves).toEqual([{ elementId: "planning", x: 560, y: 40 }]);
  });

  it("a drawn connection is one stateless action: the relation in the id, both ends in the target", async () => {
    // Arrange.
    const events = renderCanvas();

    // Act.
    events.onConnectionDrawn!({ kind: "connection-drawn", relationType: "shows", sourceElementId: "tick-step", targetElementId: "step-list" });
    await settle();

    // Assert.
    expect(executed).toEqual([{ actionId: "fdg.connect.shows", targetId: "rel:tick-step->step-list" }]);
    expect(moves).toEqual([]);
  });

  it("a toolbox drop adds its type centred where it landed", async () => {
    // Arrange.
    const events = renderCanvas();

    // Act.
    events.onElementDropped!({ kind: "element-dropped", elementType: "action", position: { x: 300, y: 700 } });
    await settle();

    // Assert.
    expect(executed).toEqual([{ actionId: "fdg.add.action", targetId: "new:300,700" }]);
  });

  it("a drop from the backend's toolbox, which carries its add action, adds that type", async () => {
    // The backend's toolbox item names `fdg.add.<type>` as its DropActionId, and that is the drop's
    // payload - as for every module. Ignoring it would make every palette drop do nothing.
    const events = renderCanvas();

    // Act.
    events.onElementDropped!({ kind: "element-dropped", elementType: "fdg.add.comment", position: { x: 40, y: 60 } });
    events.onElementDropped!({ kind: "element-dropped", elementType: "fdg.add.nonsense", position: { x: 1, y: 2 } });
    await settle();

    // Assert.
    expect(executed).toEqual([{ actionId: "fdg.add.comment", targetId: "new:40,60" }]);
  });

  it("a drop of something that is not one of the five sends nothing", async () => {
    // Arrange.
    const events = renderCanvas();

    // Act.
    events.onElementDropped!({ kind: "element-dropped", elementType: "timeline.add-period", position: { x: 1, y: 2 } });
    await settle();

    // Assert.
    expect(executed).toEqual([]);
  });

  it.each([
    ["fdg.rename", "planning"],
    ["fdg.remove", "planning"],
    ["fdg.disconnect", "c-back-shows-list"],
    ["fdg.rename-connection", "c-back-shows-list"],
  ])("a declared %s reaches the backend as that action on its target", async (actionId, targetId) => {
    // Arrange.
    const events = renderCanvas();

    // Act.
    events.onActionInvoked!({ kind: "action-invoked", actionId, targetKind: "element", targetId });
    await settle();

    // Assert.
    expect(executed).toEqual([{ actionId, targetId }]);
  });

  it("an action this module does not declare is not forwarded", async () => {
    // Arrange.
    const events = renderCanvas();

    // Act.
    events.onActionInvoked!({ kind: "action-invoked", actionId: "delete", targetKind: "element", targetId: "planning" });
    await settle();

    // Assert.
    expect(executed).toEqual([]);
  });

  it("sends nothing of its own for a label commit: the backend's prompt already carries it", () => {
    // A rename opens the shared editor from the backend's prompt, and the library hands the commit
    // to that prompt's own submit. A second send from here would rename twice.
    const events = renderCanvas();

    // Assert.
    expect(events.onLabelCommitRequested).toBeUndefined();
  });

  it("hands the library each element at its centre and size, with its Name or a Comment's text", () => {
    // Act.
    renderCanvas();

    // Assert.
    const planning = captured!.model.elements.find((element) => element.id === "planning")!;
    const note = captured!.model.elements.find((element) => element.id === "note")!;
    expect(planning).toMatchObject({ type: "ui-element", x: 670, y: 64, width: 140, height: 48, label: "Planning" });
    expect(note).toMatchObject({ type: "comment", width: 260, height: 96, label: "Offline first." });
  });
});
