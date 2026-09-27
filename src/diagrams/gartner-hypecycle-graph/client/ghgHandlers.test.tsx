import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, render } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { GhgAttachmentSchema, GhgInfluencePayloadSchema, GhgTrendPayloadSchema } from "@client/generated/gartner-hypecycle-graph_pb";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel } from "@client/canvas/library/api/diagramModel";
import { fakeContextConnection } from "@client/canvas/library/testing/canvasHarness";
import { emptyModel, type GhgModel } from "./ghgModel";

/**
 * Task 20's handler guard: each library event produces its ONE route and nothing else - a move
 * through the stream's `moveElementTo`, a resize or a boundary drag through `setProperty`, and a
 * drop, connection, deletion or rename through a context action.
 *
 * The library is replaced by a stand-in that keeps the handlers it was given, so each event is raised
 * exactly as the library would raise it and what reaches the transports is all that is measured.
 */

let captured: { events: DiagramEventHandlers; model: DiagramModel } | null = null;
let currentModel: GhgModel = emptyModel;
let executed: { actionId: string; targetId: string }[] = [];
let properties: { propertyId: string; value: string; targetId: string }[] = [];
let moves: { elementId: string; x: number; y: number }[] = [];

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
    client: {},
    moveElementTo: (elementId: string, x: number, y: number) => {
      moves.push({ elementId, x, y });
      return Promise.resolve("");
    },
  }),
}));

vi.mock("@client/diagrams/viewReport", () => ({ viewReportOf: () => () => {} }));
vi.mock("@client/diagrams/useViewReport", () => ({ useViewReport: () => undefined }));
vi.mock("@client/shell/panels/useToolboxItems", () => ({ useToolboxItems: () => [] }));

vi.mock("@client/shell/context/ContextConnectionProvider", () => ({
  useContextPrompt: () => ({ prompt: null, onPropose: vi.fn(), onSubmit: vi.fn(), onCancel: vi.fn() }),
  useContextConnection: () => connection,
}));

const connection = fakeContextConnection({
  watchId: new Uint8Array([9]),
  executeAction: (actionId: string, source?: unknown) => {
    executed.push({ actionId, targetId: idOf(source) });
    return Promise.resolve({ accepted: true, error: "" });
  },
  setProperty: (propertyId: string, value: string, source?: unknown) => {
    properties.push({ propertyId, value, targetId: idOf(source) });
    return Promise.resolve({ accepted: true, error: "" });
  },
});

const { GhgCanvas } = await import("./GhgCanvas");

/** Steam engine: 1760-01 to 1860-01 on row 3 - 400 months, 1,600 units wide, its left at x -560. */
function modelWith(): GhgModel {
  return {
    trends: new Map([
      ["steam-engine", {
        id: "steam-engine",
        x: -560 + 800,
        y: 3 * 56 + 16,
        payload: create(GhgTrendPayloadSchema, { name: "Steam engine", phases: 3, boundaries: [0.2, 0.5], tags: ["energy"], width: 1600 }),
      }],
      ["coal", {
        id: "coal",
        x: 0,
        y: 16,
        payload: create(GhgTrendPayloadSchema, { name: "Coal", phases: 4, width: 400 }),
      }],
    ]),
    influences: new Map([
      ["coal--steam-engine", {
        id: "coal--steam-engine",
        payload: create(GhgInfluencePayloadSchema, {
          fromElementId: "coal",
          toElementId: "steam-engine",
          sourceAttachment: create(GhgAttachmentSchema, { edge: "bottom", region: 0, at: 0.4 }),
          targetAttachment: create(GhgAttachmentSchema, { edge: "top", region: 1, at: 0.25 }),
        }),
      }],
    ]),
  };
}

function renderCanvas() {
  render(<GhgCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["technology-trends.ghg"]} />);
  return captured!.events;
}

const settle = () => act(async () => {});

beforeEach(() => {
  captured = null;
  currentModel = modelWith();
  executed = [];
  properties = [];
  moves = [];
});

describe("the hype cycle graph canvas answers each library event through its one route", () => {
  it("a move goes through moveElementTo, as the TOP-LEFT the backend places by", async () => {
    const events = renderCanvas();

    events.onElementMoved!({ kind: "element-moved", elementId: "steam-engine", position: { x: 1000, y: 240 } });
    await settle();

    expect(moves).toEqual([{ elementId: "steam-engine", x: 1000 - 800, y: 240 - 16 }]);
    expect(executed).toEqual([]);
    expect(properties).toEqual([]);
  });

  it("a resize of the right edge is the new stop month, through setProperty, and no move", async () => {
    const events = renderCanvas();

    // The right edge at x 1,080 is the start of 1922-07.
    events.onElementResized!({ kind: "element-resized", elementId: "steam-engine", side: "right", bounds: { x: -560, y: 168, width: 1640, height: 32 } });
    await settle();

    expect(properties).toEqual([{ propertyId: "ghg.stop", value: "1922-07", targetId: "steam-engine" }]);
    expect(moves).toEqual([]);
    expect(executed).toEqual([]);
  });

  it("a resize of the left edge is the new start month", async () => {
    const events = renderCanvas();

    events.onElementResized!({ kind: "element-resized", elementId: "steam-engine", side: "left", bounds: { x: -600, y: 168, width: 1640, height: 32 } });
    await settle();

    expect(properties).toEqual([{ propertyId: "ghg.start", value: "1887-07", targetId: "steam-engine" }]);
  });

  it.each([
    [0, "ghg.peak-end"],
    [1, "ghg.trough-end"],
    [2, "ghg.slope-end"],
  ])("a dragged boundary %i is the month its phase now ends, as %s", async (index, propertyId) => {
    const events = renderCanvas();

    events.onSegmentBoundaryMoved!({ kind: "segment-boundary-moved", elementId: "steam-engine", index, x: 2400 });
    await settle();

    expect(properties).toEqual([{ propertyId, value: "1950-01", targetId: "steam-engine" }]);
    expect(moves).toEqual([]);
    expect(executed).toEqual([]);
  });

  it("a drawn influence is one action carrying both ends and where each attaches", async () => {
    const events = renderCanvas();

    events.onConnectionDrawn!({
      kind: "connection-drawn",
      relationType: "influence",
      sourceElementId: "steam-engine",
      targetElementId: "coal",
      sourceAttachment: { edge: "bottom", region: 2, at: 0.3 },
      targetAttachment: { edge: "top", region: 0, at: 0.125 },
    });
    await settle();

    expect(executed).toEqual([{ actionId: "ghg.connect.influence", targetId: "rel:steam-engine@slope/bottom/0.3->coal@peak/top/0.13" }]);
    expect(properties).toEqual([]);
  });

  it("a drop from the backend's toolbox, or of the bare type, adds a trend where it landed", async () => {
    const events = renderCanvas();

    events.onElementDropped!({ kind: "element-dropped", elementType: "ghg.add.trend", position: { x: 300, y: 700 } });
    events.onElementDropped!({ kind: "element-dropped", elementType: "trend", position: { x: 40, y: 60 } });
    events.onElementDropped!({ kind: "element-dropped", elementType: "fdg.add.comment", position: { x: 1, y: 2 } });
    await settle();

    expect(executed).toEqual([
      { actionId: "ghg.add.trend", targetId: "new:300,700" },
      { actionId: "ghg.add.trend", targetId: "new:40,60" },
    ]);
  });

  it.each([
    ["ghg.rename", "steam-engine"],
    ["ghg.remove", "steam-engine"],
    ["ghg.disconnect", "coal--steam-engine"],
  ])("a declared %s reaches the backend as that action on its target", async (actionId, targetId) => {
    const events = renderCanvas();

    events.onActionInvoked!({ kind: "action-invoked", actionId, targetKind: "element", targetId });
    await settle();

    expect(executed).toEqual([{ actionId, targetId }]);
  });

  it("an action this module does not declare is not forwarded, and a label commit sends nothing of its own", async () => {
    const events = renderCanvas();

    events.onActionInvoked!({ kind: "action-invoked", actionId: "delete", targetKind: "element", targetId: "coal" });
    await settle();

    expect(executed).toEqual([]);
    expect(events.onLabelCommitRequested).toBeUndefined();
  });

  it("hands the library each trend at its centre and width, with its phases, boundaries, tags and attachments", () => {
    renderCanvas();

    const steam = captured!.model.elements.find((element) => element.id === "steam-engine")!;
    expect(steam).toMatchObject({ type: "trend", x: 240, y: 184, width: 1600, height: 32, label: "Steam engine" });
    expect(steam.payload).toEqual({ name: "Steam engine", phases: 3, boundaries: [0.2, 0.5], tags: ["energy"] });
    expect(captured!.model.connections).toEqual([
      expect.objectContaining({
        id: "coal--steam-engine",
        type: "influence",
        sourceId: "coal",
        targetId: "steam-engine",
        sourceAttachment: { edge: "bottom", region: 0, at: 0.4 },
        targetAttachment: { edge: "top", region: 1, at: 0.25 },
      }),
    ]);
  });
});
