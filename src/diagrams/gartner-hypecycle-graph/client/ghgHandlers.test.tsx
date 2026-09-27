import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, render } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { GhgAttachmentSchema, GhgInfluencePayloadSchema, GhgNotePayloadSchema, GhgTrendPayloadSchema, GhgTriggerPayloadSchema } from "@client/generated/gartner-hypecycle-graph_pb";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel } from "@client/canvas/library/api/diagramModel";
import type { DiagramDefinition } from "@client/canvas/library/definition/diagramDefinition";
import { fakeContextConnection } from "@client/canvas/library/testing/canvasHarness";
import { emptyModel, type GhgModel } from "./ghgModel";
import { clearPropertyPreview, usePropertyPreview } from "@client/shell/panels/propertyPreview";

/**
 * Task 20's handler guard: each library event produces its ONE route and nothing else - a move
 * through the stream's `moveElementTo`, a resize or a boundary drag through `setProperty`, and a
 * drop, connection, deletion or rename through a context action.
 *
 * The library is replaced by a stand-in that keeps the handlers it was given, so each event is raised
 * exactly as the library would raise it and what reaches the transports is all that is measured.
 */

let captured: { events: DiagramEventHandlers; model: DiagramModel; definition: DiagramDefinition } | null = null;
let currentModel: GhgModel = emptyModel;
let executed: { actionId: string; targetId: string }[] = [];
let properties: { propertyId: string; value: string; targetId: string }[] = [];
let moves: { elementId: string; x: number; y: number }[] = [];

type Source = { source: { value: { value: string } } };
const idOf = (source: unknown) => (source as Source).source.value.value;

vi.mock("@client/canvas/library/DiagramCanvas", () => ({
  DiagramCanvas: (props: { events: DiagramEventHandlers; model: DiagramModel; definition: DiagramDefinition }) => {
    captured = { events: props.events, model: props.model, definition: props.definition };
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

const { GhgCanvas, GHG_DEFINITION, ghgDefinitionFor } = await import("./GhgCanvas");

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
    triggers: new Map(),
    notes: new Map(),
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

  it("a dragged influence end is its new attachment, through setProperty on the end it was", async () => {
    const events = renderCanvas();

    events.onConnectionEndMoved!({ kind: "connection-end-moved", connectionId: "coal--steam-engine", end: "target", attachment: { edge: "top", region: 2, at: 0.5 } });
    events.onConnectionEndMoved!({ kind: "connection-end-moved", connectionId: "coal--steam-engine", end: "source", attachment: { edge: "bottom", region: 3, at: 0.25 } });
    await settle();

    expect(properties).toEqual([
      { propertyId: "ghg.to-attachment", value: "slope/top/0.5", targetId: "coal--steam-engine" },
      { propertyId: "ghg.from-attachment", value: "plateau/bottom/0.25", targetId: "coal--steam-engine" },
    ]);
    expect(executed).toEqual([]);
  });

  it("a drag in flight is shown as the dates it would write, and writes nothing", async () => {
    const events = renderCanvas();
    let shown: ReturnType<typeof usePropertyPreview> = null;
    function Probe() {
      shown = usePropertyPreview();
      return null;
    }
    render(<Probe />);

    // Resized to span x -560 to 1,080 (1888-05 to 1922-07), its chevrons drawn at 2,400 and 800.
    act(() => events.onElementPreviewed!({ kind: "element-previewed", elementId: "steam-engine", bounds: { x: -560, y: 168, width: 1640, height: 32 }, boundaries: [2400, 800] }));

    expect(shown).toEqual({
      elementId: "steam-engine",
      values: { "ghg.start": "1888-05", "ghg.stop": "1922-07", "ghg.peak-end": "1950-01", "ghg.trough-end": "1916-09" },
    });
    expect(properties).toEqual([]);

    // Abandoned: nothing was written, so nothing is kept.
    act(() => events.onElementPreviewed!({ kind: "element-previewed", elementId: "steam-engine", bounds: null }));
    expect(shown).toBeNull();
    clearPropertyPreview();
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

  it("draws a diagram whose trends name no unit in months", () => {
    renderCanvas();

    expect(captured!.definition).toBe(GHG_DEFINITION);
  });

  it("draws a diagram of decades with the decade's definition, and writes the decade a dragged edge snaps to", async () => {
    // Every trend carries the diagram's unit, as the backend sends it.
    for (const trend of currentModel.trends.values()) {
      trend.payload.unit = "decade";
    }
    const events = renderCanvas();

    // x 40 is 2000-01 in decades (four units a decade from 1900), and x 41 snaps back to it.
    events.onElementResized!({ kind: "element-resized", elementId: "steam-engine", side: "right", bounds: { x: -560, y: 168, width: 601, height: 32 } });
    events.onSegmentBoundaryMoved!({ kind: "segment-boundary-moved", elementId: "steam-engine", index: 0, x: -2159 });
    await settle();

    expect(captured!.definition).toBe(ghgDefinitionFor("decade"));
    expect(properties).toEqual([
      { propertyId: "ghg.stop", value: "2000-01", targetId: "steam-engine" },
      { propertyId: "ghg.peak-end", value: "-3500-01", targetId: "steam-engine" },
    ]);
  });

  it("hands the library each trend at its centre and width, with its phases, boundaries, tags and attachments", () => {
    renderCanvas();

    const steam = captured!.model.elements.find((element) => element.id === "steam-engine")!;
    expect(steam).toMatchObject({ type: "trend", x: 240, y: 184, width: 1600, height: 32, label: "Steam engine" });
    expect(steam.payload).toEqual({ name: "Steam engine", phases: 3, boundaries: [0.2, 0.5], tags: ["energy"], snapX: 0, snapY: 0 });
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

/** The same graph with a trigger on a step line at row 2's middle, and a note whose top-left is on row 4. */
function withTriggerAndNote(): GhgModel {
  const base = modelWith();
  return {
    ...base,
    triggers: new Map([["transistor", {
      id: "transistor",
      x: 96,
      y: 2 * 56 + 16,
      payload: create(GhgTriggerPayloadSchema, { name: "Transistor", when: "Dec 1947", snapX: -8, snapY: 8 }),
    }]]),
    notes: new Map([["remark", {
      id: "remark",
      x: 280,
      y: 4 * 56 + 32,
      payload: create(GhgNotePayloadSchema, { text: "A remark", width: 160, height: 64 }),
    }]]),
  };
}

describe("triggers and notes, answered through their routes", () => {
  it("a drop of a Trigger or a Note, from the backend's toolbox or as the bare type, adds one where it landed", async () => {
    const events = renderCanvas();

    events.onElementDropped!({ kind: "element-dropped", elementType: "ghg.add.trigger", position: { x: 300, y: 700 } });
    events.onElementDropped!({ kind: "element-dropped", elementType: "trigger", position: { x: 4, y: 8 } });
    events.onElementDropped!({ kind: "element-dropped", elementType: "ghg.add.note", position: { x: 40, y: 60 } });
    events.onElementDropped!({ kind: "element-dropped", elementType: "note", position: { x: 12, y: 16 } });
    await settle();

    expect(executed).toEqual([
      { actionId: "ghg.add.trigger", targetId: "new:300,700" },
      { actionId: "ghg.add.trigger", targetId: "new:4,8" },
      { actionId: "ghg.add.note", targetId: "new:40,60" },
      { actionId: "ghg.add.note", targetId: "new:12,16" },
    ]);
  });

  it("a move takes each element's OWN half-size off its centre: a trigger's 8, a note's half its box", async () => {
    currentModel = withTriggerAndNote();
    const events = renderCanvas();

    events.onElementMoved!({ kind: "element-moved", elementId: "transistor", position: { x: 100, y: 184 } });
    events.onElementMoved!({ kind: "element-moved", elementId: "remark", position: { x: 500, y: 300 } });
    await settle();

    expect(moves).toEqual([
      { elementId: "transistor", x: 100 - 8, y: 184 - 8 },
      { elementId: "remark", x: 500 - 80, y: 300 - 32 },
    ]);
  });

  it("a note's resize is its size and the top-left it now has, through setProperty, and no move", async () => {
    currentModel = withTriggerAndNote();
    const events = renderCanvas();

    // The left edge at x 200 is the start of 1904-03; the top at 168 is row 3.
    events.onElementResized!({ kind: "element-resized", elementId: "remark", side: "top", bounds: { x: 200, y: 168, width: 240.5, height: 120 } });
    await settle();

    expect(properties).toEqual([{ propertyId: "ghg.size", value: "240.5 x 120 at 1904-03 row 3", targetId: "remark" }]);
    expect(moves).toEqual([]);
  });

  it("hands the library a trigger 16 across with its name as its label, and a note at its own size", () => {
    currentModel = withTriggerAndNote();
    renderCanvas();

    const trigger = captured!.model.elements.find((element) => element.id === "transistor")!;
    expect(trigger).toMatchObject({ type: "trigger", width: 16, height: 16, label: "Transistor" });
    expect(trigger.payload).toMatchObject({ name: "Transistor", when: "Dec 1947", snapX: -8, snapY: 8 });
    const note = captured!.model.elements.find((element) => element.id === "remark")!;
    expect(note).toMatchObject({ type: "note", width: 160, height: 64, label: "A remark", payload: { text: "A remark" } });
  });
});
