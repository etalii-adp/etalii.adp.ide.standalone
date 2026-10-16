import { describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import type { DiagramDefinition, RelationTypeDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";

/**
 * A line's dash as its relation type states it (DISL 6.10 `stroke.dash`): one dash for the type, one
 * chosen per connection by a condition on its payload, and an open end's own. Inline, as the library
 * draws a line's stroke, so the definition is what decides it rather than a stylesheet beside it.
 *
 * jsdom applies no stylesheet, so this pins down what the canvas writes; what a browser paints is the
 * module's `tests.md` pass.
 */

function modelOf(): DiagramModel {
  return {
    elements: [
      { id: "a", type: "node", x: 100, y: 50, width: 80, height: 40, label: "A" },
      { id: "b", type: "node", x: 400, y: 50, width: 80, height: 40, label: "B" },
    ],
    connections: [
      { id: "solid", type: "uses", sourceId: "a", targetId: "b", payload: { dynamic: false } },
      { id: "dashed", type: "uses", sourceId: "b", targetId: "a", payload: { dynamic: true } },
      { id: "open", type: "uses", sourceId: "a", targetId: "", payload: { dynamic: true } },
    ],
  };
}

function definitionOf(relation: Partial<RelationTypeDefinition>): DiagramDefinition {
  return {
    elementTypes: [{ id: "node", shape: "box", anchors: { kind: "edge" }, sizing: "model" }],
    relationTypes: [
      {
        id: "uses",
        route: "straight",
        endpoints: { source: { elementTypes: ["node"] }, target: { elementTypes: ["node"] }, allowSelf: false },
        stub: { length: 48, side: "right" },
        ...relation,
      },
    ],
    layout: { modes: ["manual"] },
    dragging: "enabled",
  };
}

function dashes(definition: DiagramDefinition): Record<string, string> {
  const { container, unmount } = render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={definition} model={modelOf()} events={{}} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
  const drawn = Object.fromEntries(
    ["solid", "dashed", "open"].map((id) => [id, (container.querySelector(`[data-connection-id="${id}"] path.canvas-connection-line`) as SVGPathElement | null)?.style.strokeDasharray ?? "(not drawn)"]),
  );
  unmount();
  return drawn;
}

describe("a line's dash", () => {
  it("is its relation type's dash, on every connection of the type and on its stub", () => {
    expect(dashes(definitionOf({ style: { dash: [1, 4] } }))).toEqual({ solid: "1 4", dashed: "1 4", open: "1 4" });
  });

  it("is chosen per connection where the type states a dash for a condition, and the stub keeps the type's", () => {
    expect(dashes(definitionOf({ dashWhen: { when: { path: "payload.dynamic", is: "true" }, dash: [5, 4] } }))).toEqual({ solid: "", dashed: "5 4", open: "" });
    expect(dashes(definitionOf({ style: { dash: [2, 2] }, dashWhen: { when: { path: "payload.dynamic", is: "true" }, dash: [5, 4] } }))).toEqual({ solid: "2 2", dashed: "5 4", open: "2 2" });
  });

  it("is an open end's own where its stub states one", () => {
    expect(dashes(definitionOf({ style: { dash: [1, 4] }, stub: { length: 48, side: "right", dash: [3, 3] } }))).toEqual({ solid: "1 4", dashed: "1 4", open: "3 3" });
  });
});
