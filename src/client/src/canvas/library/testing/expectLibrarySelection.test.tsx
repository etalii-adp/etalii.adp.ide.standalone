import { describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { elementSelectionOf, selectedElementIdOf } from "@client/canvas/selection";
import type { ContextSelection } from "@client/generated/context_pb";
import { DiagramCanvas, DiagramCanvasCore } from "../DiagramCanvas";
import type { DiagramDefinition } from "../definition/diagramDefinition";
import type { DiagramModel } from "../api/diagramModel";
import type { DiagramSelection } from "../api/diagramEvents";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { expectLibrarySelection, type LibrarySelectionHarness } from "./expectLibrarySelection";

/**
 * The shared assertion is itself a guard, so it is held to the same rule: seen red against each
 * defect it names before it is trusted (centralized-selection Requirement 9.3). Each broken canvas
 * below is one of the variants the four broken modules actually shipped.
 */

const channel = vi.hoisted(() => ({ pushed: null as unknown, pushes: [] as unknown[] }));

vi.mock("@client/shell/context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@client/shell/context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextConnection: () => ({ select: (selection: unknown) => channel.pushes.push(selection), executeAction: () => Promise.resolve({ accepted: true, error: "" }) }),
    useContextSelection: () => ({ selection: channel.pushed, levels: [], actions: [] }),
  };
});

SVGElement.prototype.setPointerCapture ??= () => {};
SVGElement.prototype.releasePointerCapture ??= () => {};

const ENTRY = new Uint8Array([7]);
const PATH = ["system.adp"];

const definition: DiagramDefinition = {
  elementTypes: [{ id: "service", shape: "box", anchors: { kind: "edge" }, sizing: "model" }],
  relationTypes: [
    { id: "calls", route: "straight", endpoints: { source: { elementTypes: ["service"] }, target: { elementTypes: ["service"] }, allowSelf: false } },
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
};

const model: DiagramModel = {
  elements: [
    { id: "a", type: "service", x: 0, y: 0, width: 100, height: 40 },
    { id: "b", type: "service", x: 300, y: 0, width: 100, height: 40 },
  ],
  connections: [{ id: "a->b", type: "calls", sourceId: "a", targetId: "b" }],
};

const idOf = (push: unknown) => (push === null ? null : (selectedElementIdOf(push as ContextSelection) ?? null));

function wrap(canvas: React.ReactElement) {
  return render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>{canvas}</DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
}

/** A canvas whose selection the library owns: `source`, and nothing else. */
function ownedHarness(overrides: Partial<LibrarySelectionHarness> = {}): LibrarySelectionHarness {
  channel.pushes = [];
  return {
    mountWith: (id) => {
      channel.pushed = id === null ? null : elementSelectionOf(ENTRY, PATH, id);
      return wrap(<DiagramCanvas definition={definition} model={model} events={{}} source={{ entryId: ENTRY, path: PATH }} />);
    },
    pushedIds: () => channel.pushes.map(idOf),
    element: "a",
    connection: "a->b",
    ...overrides,
  };
}

/**
 * A canvas wiring selection by hand, broken the way a module was: `toSelection` is its inbound
 * mapping, `pushesClear` whether its handler answers an empty selection at all. Mounted on the
 * canvas core, because since centralized-selection task 21 no module can wire selection by hand
 * through DiagramCanvas - these reproduce the four broken variants the assertion must catch.
 */
function handWiredHarness(toSelection: (id: string | null) => DiagramSelection, pushesClear: boolean): LibrarySelectionHarness {
  const pushes: (string | null)[] = [];
  return {
    mountWith: (id) =>
      wrap(
        <DiagramCanvasCore
          definition={definition}
          model={model}
          selection={toSelection(id)}
          events={{
            onSelectionChanged: ({ selection }) => {
              if (selection.length > 0) {
                pushes.push(selection[0].id);
              } else if (pushesClear) {
                pushes.push(null);
              }
            },
          }}
        />,
      ),
    pushedIds: () => pushes,
    element: "a",
    connection: "a->b",
  };
}

const working = (id: string | null): DiagramSelection =>
  id === null ? [] : [{ kind: model.connections.some((c) => c.id === id) ? "connection" : "element", id }];

describe("expectLibrarySelection", () => {
  it("passes a canvas whose selection the library owns", () => {
    expect(() => expectLibrarySelection(ownedHarness())).not.toThrow();
  });

  it("passes a hand-wired canvas that gets all three right - it asserts behaviour, not mechanism", () => {
    expect(() => expectLibrarySelection(handWiredHarness(working, true))).not.toThrow();
  });

  it("fails a canvas whose pushed element selection is not highlighted", () => {
    expect(() => expectLibrarySelection(handWiredHarness(() => [], true))).toThrow(/element "a" does not highlight it/);
  });

  it("fails a canvas that highlights no connection - helm's, ansible's and azure-pipeline's defect", () => {
    const elementsOnly = (id: string | null): DiagramSelection => (id === null ? [] : [{ kind: "element", id }]);

    expect(() => expectLibrarySelection(handWiredHarness(elementsOnly, true))).toThrow(/connection "a->b" does not highlight it/);
  });

  it("fails a canvas whose background press does not clear - helm's and ansible's defect", () => {
    expect(() => expectLibrarySelection(handWiredHarness(working, false))).toThrow(/background does not clear/);
  });

  it("cannot pass vacuously: an element that is not on the canvas is a failure, not a skip", () => {
    expect(() => expectLibrarySelection(ownedHarness({ element: "not-drawn" }))).toThrow(/not on the canvas/);
    expect(() => expectLibrarySelection(ownedHarness({ connection: "not-drawn" }))).toThrow(/not on the canvas/);
  });
});
