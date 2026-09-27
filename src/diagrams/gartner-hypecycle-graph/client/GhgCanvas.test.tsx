import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { GhgAttachmentSchema, GhgInfluencePayloadSchema, GhgTrendPayloadSchema } from "@client/generated/gartner-hypecycle-graph_pb";
import { expectLibrarySelection } from "@client/canvas/library/testing/expectLibrarySelection";
import { emptyModel, type GhgModel } from "./ghgModel";
import { fakeContextConnection, idsPushed } from "@client/canvas/library/testing/canvasHarness";

/**
 * The REAL canvas, mounted: what the library draws from this module's definition and model, and
 * the shared selection assertion every canvas's own test runs. The handlers' routes are
 * `ghgHandlers.test.tsx`'s, and the connect gesture over the example is `ghgConnect.test.tsx`'s.
 */

let currentModel: GhgModel = emptyModel;
let currentSelectionKey: string | null = null;
let selections: unknown[] = [];
let reports: { minX: number; maxX: number }[] = [];

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
  viewReportOf: () => (viewport: { minX: number; maxX: number }) => reports.push(viewport),
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

const { GhgCanvas } = await import("./GhgCanvas");

/** Coal, then the steam engine a row below, and coal's influence on it - centres, as the backend sends them. */
function modelWith(): GhgModel {
  const trend = (name: string, phases: number, width: number) => create(GhgTrendPayloadSchema, { name, phases, width });
  const end = (edge: string, region: number, at: number) => create(GhgAttachmentSchema, { edge, region, at });
  return {
    trends: new Map([
      ["coal", { id: "coal", x: 200, y: 16, payload: trend("Coal", 4, 400) }],
      ["steam-engine", { id: "steam-engine", x: 400, y: 72, payload: trend("Steam engine", 3, 400) }],
    ]),
    triggers: new Map(),
    notes: new Map(),
    influences: new Map([
      ["coal--steam-engine", {
        id: "coal--steam-engine",
        payload: create(GhgInfluencePayloadSchema, {
          fromElementId: "coal",
          toElementId: "steam-engine",
          sourceAttachment: end("bottom", 0, 0.5),
          targetAttachment: end("top", 0, 0.5),
        }),
      }],
    ]),
  };
}

function renderCanvas() {
  return render(<GhgCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["technology-trends.ghg"]} />);
}

beforeEach(() => {
  currentModel = modelWith();
  currentSelectionKey = null;
  selections = [];
  reports = [];
});

describe("the hype cycle graph canvas, mounted", () => {
  it("draws each trend in the phases it has reached, each in its phase's class, its name beside it", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert: coal shows all four phases, the steam engine three.
    const phasesOf = (id: string) => [...container.querySelectorAll(`[data-element-id="${id}"] .library-segment`)].map((segment) => segment.getAttribute("class"));
    expect(phasesOf("coal")).toEqual(["library-segment ghg-peak", "library-segment ghg-trough", "library-segment ghg-slope", "library-segment ghg-plateau"]);
    expect(phasesOf("steam-engine")).toHaveLength(3);
    expect(container.textContent).toContain("Steam engine");
  });

  it("selects through the library, exactly as every other canvas does", () => {
    expectLibrarySelection({
      mountWith: (id) => {
        currentSelectionKey = id === null ? null : `element:${id}`;
        return renderCanvas();
      },
      pushedIds: () => idsPushed(selections),
      element: "coal",
      connection: "coal--steam-engine",
    });
  });

  it("reports the whole canvas as its view while Compact is on, and the screen's again after", async () => {
    // Compact places each trend by where every other starts, so it needs the backend to send them all.
    vi.useFakeTimers();
    try {
      const { container } = renderCanvas();
      await act(async () => { vi.advanceTimersByTime(1000); });
      const before = reports.at(-1);

      fireEvent.click(container.querySelector(".library-layout-toggle")!);
      await act(async () => { vi.advanceTimersByTime(1000); });
      expect(reports.at(-1)!.minX).toBeLessThan(-1e8);
      expect(reports.at(-1)!.maxX).toBeGreaterThan(1e8);

      fireEvent.click(container.querySelector(".library-layout-toggle")!);
      await act(async () => { vi.advanceTimersByTime(1000); });
      expect(reports.at(-1)).toEqual(before);
    } finally {
      vi.useRealTimers();
    }
  });
});
