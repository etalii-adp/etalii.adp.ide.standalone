import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { VIEW_REPORT_DEBOUNCE_MS, type Viewport } from "@client/diagrams/viewReport";
import { emptyModel, type DatabricksModel } from "./databricksModel";
import { fakeContextConnection } from "@client/canvas/library/testing/canvasHarness";

let currentModel: DatabricksModel = emptyModel;
let currentLoading = false;
let currentFailed = false;
let reports: Viewport[] = [];

vi.mock("./useDatabricksStream", () => ({
  useDatabricksStream: () => ({
    model: currentModel,
    loading: currentLoading,
    failed: currentFailed,
    moveElementTo: () => Promise.resolve(""),
    reportView: (viewport: Viewport) => reports.push(viewport),
  }),
}));

vi.mock("@client/shell/context/ContextConnectionProvider", () => ({
  innermostKey: () => null,
  useContextPrompt: () => ({ prompt: null, onPropose: vi.fn(), onSubmit: vi.fn(), onCancel: vi.fn() }),
  useContextSelection: () => ({ selection: null, levels: [], actions: [] }),
  useContextConnection: () => connection,
}));

const connection = fakeContextConnection();

vi.mock("@client/shell/panels/InlineLabelPlacementContext", () => ({
  useRegisterInlineLabelPlacement: () => undefined,
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

const { JobCanvas } = await import("./JobCanvas");

function draw() {
  return render(<JobCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["job.adp"]} />);
}

describe("the databricks view-delta loop, client half", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    reports = [];
    currentModel = emptyModel;
    currentLoading = false;
    currentFailed = false;
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("reports the settled view once the diagram has loaded", () => {
    // Act.
    draw();
    vi.advanceTimersByTime(VIEW_REPORT_DEBOUNCE_MS);

    // Assert: a rectangle, not a point - the backend culls against it.
    expect(reports).toHaveLength(1);
    expect(reports[0].maxX).toBeGreaterThan(reports[0].minX);
    expect(reports[0].maxY).toBeGreaterThan(reports[0].minY);
  });

  it("reports again when the view changes, which is what makes a pan bring content in", () => {
    // Arrange: the settled first report.
    const { container } = draw();
    vi.advanceTimersByTime(VIEW_REPORT_DEBOUNCE_MS);
    const first = reports.length;

    // Act: a wheel zoom, which changes the view without changing the model.
    const surface = container.querySelector("svg.library-canvas-surface")!;
    fireEvent.wheel(surface, { deltaY: -100 });
    vi.advanceTimersByTime(VIEW_REPORT_DEBOUNCE_MS);

    // Assert. Requirement 1.3 is behavioural: reporting once at open is the first frame of the
    // loop, not the loop. The backend half of this pairing is asserted in
    // DatabricksSessionTests, because a report the session answers with `[]` is no adoption at
    // all and nothing here would notice.
    expect(reports.length).toBeGreaterThan(first);
    expect(reports[reports.length - 1]).not.toEqual(reports[first - 1]);
  });

  it("says nothing while the diagram is still loading", () => {
    // Arrange & act.
    currentLoading = true;
    draw();
    vi.advanceTimersByTime(VIEW_REPORT_DEBOUNCE_MS * 4);

    // Assert: a report before the first delta describes a view of nothing.
    expect(reports).toHaveLength(0);
  });

  it("says nothing once the backend has said the path is gone", () => {
    // Arrange & act.
    currentFailed = true;
    draw();
    vi.advanceTimersByTime(VIEW_REPORT_DEBOUNCE_MS * 4);

    // Assert: a report on a connection that has just been told its path is gone.
    expect(reports).toHaveLength(0);
  });
});
