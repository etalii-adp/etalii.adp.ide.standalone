import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { AadElementPayloadSchema, AadRelationPayloadSchema, AadRowPayloadSchema } from "@client/generated/agent-activity-diagram_pb";
import { expectLibrarySelection } from "@client/canvas/library/testing/expectLibrarySelection";
import { fakeContextConnection, idsPushed } from "@client/canvas/library/testing/canvasHarness";
import { emptyModel, type AadElement, type AadModel } from "./aadModel";

/**
 * The REAL canvas, mounted: what the library draws from this module's definition and model, the
 * requests its gestures make, and the shared selection assertion every canvas's own test runs.
 */

let currentModel: AadModel = emptyModel;
let currentSelectionKey: string | null = null;
let selections: unknown[] = [];
let actions: { actionId: string; target: string }[] = [];

vi.mock("@client/diagrams/useDiagramStream", () => ({
  useDiagramStream: () => ({
    model: currentModel,
    loading: false,
    failed: false,
    client: { moveElement: () => Promise.resolve({ error: "" }) },
    moveElementTo: () => Promise.resolve(""),
  }),
}));

vi.mock("@client/shell/context/ContextConnectionProvider", () => ({
  innermostKey: () => currentSelectionKey,
  useContextPrompt: () => ({ prompt: null, onPropose: vi.fn(), onSubmit: vi.fn(), onCancel: vi.fn() }),
  useContextSelection: () => ({ selection: currentSelectionKey, levels: [], actions: [] }),
  useContextConnection: () => connection,
}));

vi.mock("@client/canvas/selection", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@client/canvas/selection")>()),
  elementSourceOf: (elementId: string) => elementId,
}));

const connection = fakeContextConnection({
  watchId: new Uint8Array([9]),
  select: (selection: unknown) => selections.push(selection),
  executeAction: (actionId: string, target: unknown) => {
    actions.push({ actionId, target: String(target) });
    return Promise.resolve({ accepted: true, error: "" });
  },
});

vi.mock("@client/shell/panels/DiagramViewContext", () => ({ useRegisterDiagramView: () => undefined }));
vi.mock("@client/shell/panels/InlineLabelPlacementContext", () => ({ useRegisterInlineLabelPlacement: () => undefined }));
vi.mock("@client/shell/panels/DiagramToolboxContext", () => ({
  TOOLBOX_DRAG_TYPE: "application/x-adp-toolbox-item",
  useRegisterDiagramToolbox: () => undefined,
}));
vi.mock("@client/shell/panels/useToolboxItems", () => ({ useToolboxItems: () => [] }));

const { AadCanvas } = await import("./AadCanvas");

function element(id: string, type: string, payload: Parameters<typeof create<typeof AadElementPayloadSchema>>[1]): [string, AadElement] {
  return [id, { id, type, x: 0, y: 0, payload: create(AadElementPayloadSchema, { width: 200, height: 60, ...payload }) }];
}

const task = (id: string, title: string, status: string, updated: string) => create(AadRowPayloadSchema, { id, title, status, updated });

/** One project, one specification with three tasks in two groups, its agent, where it works and on what. */
function modelWith(showArchived = false): AadModel {
  return {
    elements: new Map([
      element("p", "project", { name: "ADP" }),
      element("s", "specification", {
        name: "Agent activity diagram",
        status: "progressing",
        statusLabel: "Progressing",
        link: "https://example.org/specification",
        collapsed: ["pending", "finished"],
        tasks: [
          task("t1", "The older task", "progressing", "2026-10-09T10:00:00+02:00"),
          task("t2", "The newer task", "progressing", "2026-10-09T12:00:00+02:00"),
          task("t3", "A folded task", "pending", ""),
        ],
      }),
      element("a", "agent", { name: "Thread" }),
      element("l", "location", { name: "develop", folder: "Default", collapsed: ["pullRequests"] }),
      element("e", "environment", { name: "Fractal", kindLabel: "Local machine" }),
    ]),
    relations: new Map(
      [["p", "s"], ["s", "a"], ["a", "l"], ["l", "e"]].map(([from, to]) => [
        `${from}--${to}`,
        { id: `${from}--${to}`, payload: create(AadRelationPayloadSchema, { fromElementId: from, toElementId: to }) },
      ]),
    ),
    showArchived,
  };
}

function renderCanvas() {
  return render(<AadCanvas projectId={new Uint8Array([1])} entryId={new Uint8Array([2])} path={["work.aad"]} />);
}

beforeEach(() => {
  currentModel = modelWith();
  currentSelectionKey = null;
  selections = [];
  actions = [];
});

describe("the agent activity diagram canvas, mounted", () => {
  it("draws the five kinds, each in its own class, joined by the four relations", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    for (const kind of ["project", "specification", "agent", "location", "environment"]) {
      expect(container.querySelector(`.aad-${kind}`), kind).not.toBeNull();
    }
    expect(container.querySelectorAll(".aad-relation")).toHaveLength(4);
    expect(container.textContent).toContain("Progressing");
    expect(container.textContent).toContain("Default");
    expect(container.textContent).toContain("Local machine");
  });

  it("gives every label a class this module's stylesheet colours", () => {
    // Found in a browser, in the dark theme: a label with no fill of its own is drawn in SVG's
    // default black, which on this notation's dark fills cannot be read. jsdom computes no style
    // from a stylesheet, so this holds the two halves apart: each label drawn carries one of the
    // module's label classes, and the stylesheet gives each of those classes a fill.
    const { container } = renderCanvas();
    // A label with no text draws nothing, and so has nothing to colour.
    const labels = [...container.querySelectorAll(".library-element-label")].filter((label) => label.textContent !== "");
    const coloured = ["aad-label", "aad-detail", "aad-status"];

    expect(labels.length).toBeGreaterThan(5);
    expect(labels.filter((label) => !coloured.some((name) => label.classList.contains(name))).map((label) => label.textContent)).toEqual([]);

    const css = readFileSync(join(dirname(fileURLToPath(import.meta.url)), "aad.css"), "utf8");
    for (const name of coloured) {
      expect(css, name).toMatch(new RegExp(`\\.${name} \\{[^}]*fill: var\\(--color-text`));
    }
  });

  it("lists an open group's tasks most recently updated first, and none of a folded group's", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert: the newer task above the older; the pending one is folded away.
    const rows = [...container.querySelectorAll('[data-element-id="s"] .library-compartment-row-text')].map((row) => row.textContent);
    expect(rows).toEqual(["The newer task", "The older task"]);
  });

  it("asks the backend to unfold a group, naming the element and the group", () => {
    // Arrange.
    const { container } = renderCanvas();
    const pending = [...container.querySelectorAll('[data-element-id="s"] .library-compartment-heading')].find((heading) => heading.textContent?.includes("Pending"));

    // Act.
    fireEvent.click(pending!);

    // Assert.
    expect(actions).toEqual([{ actionId: "expand-group", target: "s#pending" }]);
  });

  it("shows the archived switch as the document has it, and asks for the other state when pressed", () => {
    // Arrange: off in the document.
    const { container, unmount } = renderCanvas();
    const off = container.querySelector(".library-switch")!;
    expect(off.getAttribute("aria-checked")).toBe("false");

    // Act.
    fireEvent.click(off);

    // Assert.
    expect(actions).toEqual([{ actionId: "show-archived", target: "diagram view" }]);

    // And on, when the document says so.
    unmount();
    currentModel = modelWith(true);
    expect(renderCanvas().container.querySelector(".library-switch")!.getAttribute("aria-checked")).toBe("true");
  });

  it("selects through the library, exactly as every other canvas does", () => {
    expectLibrarySelection({
      mountWith: (id) => {
        currentSelectionKey = id === null ? null : `element:${id}`;
        return renderCanvas();
      },
      pushedIds: () => idsPushed(selections),
      element: "a",
      connection: "s--a",
    });
  });
});
