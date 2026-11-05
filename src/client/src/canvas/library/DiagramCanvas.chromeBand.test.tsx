import { describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";

/**
 * The header band and the notices, which the canvas draws around its drawing surface from the model's
 * background (DISL §6.13 `canvas.header` and notices): the band between the title and the surface, one
 * span per row that says something, and each notice below everything else as a status region.
 *
 * jsdom applies no stylesheet, so this pins down what the canvas writes and where; what a browser paints
 * is a module's `tests.md` pass.
 */

const definition: DiagramDefinition = {
  elementTypes: [{ id: "node", shape: "box", anchors: { kind: "edge" }, sizing: "model" }],
  relationTypes: [],
  layout: { modes: ["manual"] },
  dragging: "enabled",
  chrome: {
    title: { text: { path: "payload.heading" }, className: "view-title" },
    header: {
      rows: [
        { text: { path: "payload.form" }, className: "band-form" },
        { text: { path: "payload.modifiers" }, className: "band-modifiers", when: { path: "payload.modifiers", is: "non-empty" } },
      ],
      className: "band",
      data: { testid: "band" },
    },
    notices: [
      { id: "budget:drawn", text: { template: "Showing {payload.shown} of {payload.total}" }, severity: "warning", position: "top", when: { path: "payload.cut", is: "true" }, className: "cut" },
    ],
  },
};

function drawn(background: unknown): HTMLElement {
  const model: DiagramModel = { elements: [{ id: "a", type: "node", x: 100, y: 50, width: 80, height: 40, label: "A" }], connections: [], background };
  const { container } = render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={definition} model={model} events={{}} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
  return container;
}

describe("the canvas's header band and notices", () => {
  it("draws the band after the title and before the surface, and a notice after everything, from the background", () => {
    const container = drawn({ heading: "Query", form: "SELECT", modifiers: "LIMIT 10", cut: true, shown: 500, total: 505 });
    const order = Array.from(container.children).map((child) => child.className);

    expect(order).toEqual(["view-title", "band", expect.stringContaining("library-canvas"), "cut"]);
    const band = container.querySelector('[data-testid="band"]') as HTMLElement;
    expect(band.tagName).toBe("HEADER");
    expect(Array.from(band.children).map((row) => [row.className, row.textContent])).toEqual([["band-form", "SELECT"], ["band-modifiers", "LIMIT 10"]]);

    const notice = container.querySelector(".cut") as HTMLElement;
    expect(notice.textContent).toBe("Showing 500 of 505");
    expect(notice.getAttribute("role")).toBe("status");
    expect(notice.dataset.noticePosition).toBe("top");
    expect(notice.dataset.noticeSeverity).toBe("warning");
  });

  it("draws a row only while it says something, and neither the band nor the notice while the background says nothing for them", () => {
    const formOnly = drawn({ form: "ASK", modifiers: "", cut: false });
    expect(Array.from(formOnly.querySelector('[data-testid="band"]')!.children).map((row) => row.textContent)).toEqual(["ASK"]);
    expect(formOnly.querySelector(".cut")).toBeNull();

    const nothing = drawn({});
    expect(nothing.querySelector('[data-testid="band"]')).toBeNull();
    expect(nothing.children).toHaveLength(1);
  });
});
