import { describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";

/**
 * The title band and the key, which the canvas draws around its drawing surface from the model's
 * background (DISL 6.13 `canvas.title` and `canvas.legend`): the title before the surface and the key
 * after it, each only once it says something.
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
    title: { text: { path: "payload.heading" }, className: "view-title", data: { testid: "title" } },
    legend: {
      entries: { path: "payload.legend", each: { path: "label" } },
      swatchColour: { path: "style.background" },
      title: "Key",
      className: "view-key",
      titleClassName: "view-key-title",
      entryClassName: "view-key-entry",
      swatchClassName: "view-key-swatch",
      data: { testid: "key" },
    },
  },
};

function modelOf(background: unknown): DiagramModel {
  return { elements: [{ id: "a", type: "node", x: 100, y: 50, width: 80, height: 40, label: "A" }], connections: [], background };
}

function drawn(background: unknown): HTMLElement {
  const { container } = render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={definition} model={modelOf(background)} events={{}} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
  return container;
}

describe("the canvas's title band and key", () => {
  it("draws the title before the drawing surface and the key after it, from the model's background", () => {
    const container = drawn({ heading: "Containers of the bank", legend: [{ label: "Person", style: { background: "#08427b" } }, { label: "Plain", style: {} }] });
    const order = Array.from(container.children).map((child) => child.className);

    expect(order).toEqual(["view-title", expect.stringContaining("library-canvas"), "view-key"]);
    expect(container.querySelector('[data-testid="title"]')?.textContent).toBe("Containers of the bank");

    const key = container.querySelector('[data-testid="key"]') as HTMLElement;
    expect(key.querySelector(".view-key-title")?.textContent).toBe("Key");
    const entries = Array.from(key.querySelectorAll(".view-key-entry"));
    expect(entries.map((entry) => entry.textContent)).toEqual(["Person", "Plain"]);
    const swatches = entries.map((entry) => entry.querySelector(".view-key-swatch") as HTMLElement);
    expect(swatches[0].style.background).toBe("rgb(8, 66, 123)");
    expect(swatches[0].style.borderColor).toBe("rgb(8, 66, 123)");
    expect(swatches[1].getAttribute("style")).toBeNull();
  });

  it("draws neither while the background says nothing for them", () => {
    const container = drawn({ legend: [] });

    expect(container.querySelector('[data-testid="title"]')).toBeNull();
    expect(container.querySelector('[data-testid="key"]')).toBeNull();
    expect(container.children).toHaveLength(1);
  });
});
