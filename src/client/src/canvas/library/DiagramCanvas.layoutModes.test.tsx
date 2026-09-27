import { afterEach, describe, expect, it, vi } from "vitest";
import { createEvent, fireEvent, render } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import type { DiagramDefinition, ElementTypeDefinition } from "./definition/diagramDefinition";
import type { DiagramModel, DiagramModelElement } from "./api/diagramModel";
import type { DiagramSelection, LibraryEventHandlers } from "./api/diagramEvents";
import type { DiagramRuntimeConfig } from "./api/diagramRuntimeConfig";
import { rowPackedLayout } from "./layout/rowPackedLayout";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider, TOOLBOX_DRAG_TYPE } from "@client/shell/panels/DiagramToolboxContext";
import { pointer } from "./testing/canvasHarness";

/**
 * A second layout mode switched on through a declared toggle, and what the mode's overrides take
 * away while it is on. Everything is read off what the canvas DRAWS and RAISES; the placement's
 * own arithmetic is `rowPackedLayout.test.ts`'s.
 */

const WIDTH = 48;

const bannerType: ElementTypeDefinition = {
  id: "banner",
  shape: "arrow-banner",
  anchors: { kind: "along", edges: ["top", "bottom"], regions: "segments", visible: false },
  sizing: "user",
  segments: {
    count: { path: "payload.count" },
    max: 4,
    boundaries: "payload.boundaries",
    classNames: ["seg-a", "seg-b", "seg-c", "seg-d"],
    divider: "chevron",
    draggableBoundaries: true,
  },
};

/** The same type as the packed mode draws it: sized by the model, not dragged, its segments even. */
const packedBannerType: ElementTypeDefinition = {
  ...bannerType,
  sizing: "model",
  draggable: false,
  segments: { ...bannerType.segments!, boundaries: undefined, draggableBoundaries: false },
};

const definition: DiagramDefinition = {
  elementTypes: [bannerType],
  relationTypes: [
    {
      id: "link",
      route: "cubic-bezier",
      endpoints: { source: { elementTypes: ["banner"] }, target: { elementTypes: ["banner"] } },
    },
  ],
  layout: {
    modes: ["manual", "row-packed"],
    toggle: { caption: "Packed", on: "row-packed" },
    rowPacked: { width: WIDTH, gap: 4 },
    modeOverrides: { "row-packed": { elementTypes: [packedBannerType], dragging: "disabled", chrome: { rulers: [] } } },
  },
  dragging: "enabled",
  snap: { x: { step: 4, origin: 0 } },
  filter: { field: "payload.tags", label: "Filter by tags", legend: [{ caption: "First", swatchClass: "seg-a" }] },
  chrome: {
    rulers: [
      {
        orientation: "horizontal",
        edge: "bottom",
        scale: { unit: "month", unitsPerStep: 4, origin: "1900-01" },
        ladder: [{ every: { calendar: "year" }, label: "yyyy" }],
        minSpacingPx: 64,
      },
    ],
  },
};

/** A banner by its manual left edge and span, on a row, with uneven stored boundaries. */
const banner = (id: string, left: number, span: number, row: number, tags: string[] = []): DiagramModelElement => ({
  id,
  type: "banner",
  x: left + span / 2,
  y: row * 56 + 16,
  width: span,
  height: 32,
  payload: { count: 4, boundaries: [0.1, 0.2, 0.3], tags },
});

/** Three on one row, far apart in time; the packing puts them side by side. */
const model: DiagramModel = {
  elements: [banner("a", 0, 400, 0, ["first"]), banner("b", 800, 40, 0, ["keep"]), banner("c", 1600, 200, 0, ["keep"])],
  connections: [],
};

function mount(options: { events?: LibraryEventHandlers; selection?: DiagramSelection; config?: DiagramRuntimeConfig; model?: DiagramModel } = {}) {
  const canvas = (next: DiagramModel) => (
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore
          definition={definition}
          model={next}
          events={options.events ?? {}}
          selection={options.selection}
          config={options.config}
        />
      </DiagramToolboxProvider>
    </DiagramViewProvider>
  );
  const rendered = render(canvas(options.model ?? model));
  return { ...rendered, update: (next: DiagramModel) => rendered.rerender(canvas(next)) };
}

const toggleOf = (container: HTMLElement) => container.querySelector<HTMLButtonElement>(".library-layout-toggle");
const pressToggle = (container: HTMLElement) => fireEvent.click(toggleOf(container)!);

/** The x extent of an element as drawn: the span of its segments' corners. */
function drawnExtent(container: HTMLElement, id: string): { left: number; width: number } {
  const xs = [...container.querySelectorAll<SVGPathElement>(`[data-element-id="${id}"] path.library-segment`)].flatMap((path) =>
    [...path.getAttribute("d")!.matchAll(/(-?[\d.]+) (-?[\d.]+)/g)].map(([, x]) => Number(x)),
  );
  return { left: Math.min(...xs), width: Math.max(...xs) - Math.min(...xs) };
}

/** Where each chevron between two segments points, left to right: the boundaries as drawn. */
function dividerTips(container: HTMLElement, id: string): number[] {
  return [...container.querySelectorAll(`[data-element-id="${id}"] polyline.library-segment-divider`)].map((divider) =>
    Math.max(...divider.getAttribute("points")!.trim().split(/[\s,]+/).filter((_, index) => index % 2 === 0).map(Number)),
  );
}

describe("a declared layout toggle", () => {
  it("is drawn directly after the filter box's legend, with its caption, unpressed on a fresh mount", () => {
    const { container } = mount();

    const toggle = toggleOf(container);
    expect(toggle, "no toggle drawn").not.toBeNull();
    expect(toggle!.previousElementSibling?.classList.contains("library-filter-legend")).toBe(true);
    expect(toggle!.textContent).toBe("Packed");
    expect(toggle!.getAttribute("aria-pressed")).toBe("false");
    // The toggle replaces the switcher row: both would switch the same state.
    expect(container.querySelector("[data-testid='layout-switcher']")).toBeNull();
  });

  it("switches the layout on a click, shows it pressed, and raises layout-mode-changed once", () => {
    const onLayoutModeChanged = vi.fn();
    const { container } = mount({ events: { onLayoutModeChanged } });
    expect(drawnExtent(container, "a").width).toBeCloseTo(400, 0);

    pressToggle(container);

    expect(toggleOf(container)!.getAttribute("aria-pressed")).toBe("true");
    expect(onLayoutModeChanged).toHaveBeenCalledExactlyOnceWith({ kind: "layout-mode-changed", mode: "row-packed" });
    // Every element is drawn at the declared width, side by side on the row.
    for (const id of ["a", "b", "c"]) {
      expect(drawnExtent(container, id).width).toBeCloseTo(WIDTH, 0);
    }
    expect(drawnExtent(container, "b").left).toBeCloseTo(WIDTH + 4, 0);

    pressToggle(container);
    expect(toggleOf(container)!.getAttribute("aria-pressed")).toBe("false");
    expect(drawnExtent(container, "a").width).toBeCloseTo(400, 0);
  });

  it("keeps the selection and the filter across a switch", () => {
    const { container } = mount({ selection: [{ kind: "element", id: "b" }] });
    const field = container.querySelector(".library-filter .tag-input-field")!;
    fireEvent.change(field, { target: { value: "keep" } });
    fireEvent.keyDown(field, { key: "Enter" });
    const selectedBefore = container.querySelector('[data-element-id="b"]')!.outerHTML.includes("selected");

    pressToggle(container);

    expect([...container.querySelectorAll(".library-filter .tag-input-chip-text")].map((chip) => chip.textContent)).toEqual(["keep"]);
    expect(container.querySelector('[data-element-id="a"]')).toBeNull();
    expect(container.querySelector('[data-element-id="b"]')!.outerHTML.includes("selected")).toBe(selectedBefore);
  });

  it("leaves the switcher row to a definition that declares no toggle, and a switcher click now switches", () => {
    const untoggled: DiagramDefinition = { ...definition, layout: { ...definition.layout, toggle: undefined } };
    const { container } = render(
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvasCore definition={untoggled} model={model} events={{}} />
        </DiagramToolboxProvider>
      </DiagramViewProvider>,
    );
    expect(toggleOf(container)).toBeNull();

    fireEvent.click(container.querySelectorAll("[data-testid='layout-switcher'] button")[1]);

    expect(drawnExtent(container, "a").width).toBeCloseTo(WIDTH, 0);
  });

  it("lets a host that sets the mode keep the last word", () => {
    const { container } = mount({ config: { activeLayoutMode: "manual" } });

    pressToggle(container);

    expect(drawnExtent(container, "a").width).toBeCloseTo(400, 0);
  });
});

describe("a layout mode's overrides", () => {
  it("draws even segments and no boundary handle, resize handle or ruler while the mode is on, and restores them after", () => {
    const { container } = mount({ selection: [{ kind: "element", id: "a" }] });
    expect(container.querySelectorAll(".library-boundary-handle").length).toBeGreaterThan(0);
    expect(container.querySelectorAll(".library-resize-handle").length).toBeGreaterThan(0);
    expect(container.querySelector(".library-ruler")).not.toBeNull();

    pressToggle(container);

    expect(container.querySelectorAll(".library-boundary-handle")).toHaveLength(0);
    expect(container.querySelectorAll(".library-resize-handle")).toHaveLength(0);
    expect(container.querySelector(".library-ruler")).toBeNull();
    // Even: three chevrons a quarter of the width apart, whatever boundaries the model stores.
    const tips = dividerTips(container, "a");
    expect(tips).toHaveLength(3);
    expect(tips[1] - tips[0]).toBeCloseTo(WIDTH / 4, 5);
    expect(tips[2] - tips[1]).toBeCloseTo(WIDTH / 4, 5);

    pressToggle(container);

    expect(container.querySelectorAll(".library-boundary-handle").length).toBeGreaterThan(0);
    expect(container.querySelector(".library-ruler")).not.toBeNull();
  });

  it("starts no drag while the mode is on", () => {
    const onElementMoved = vi.fn();
    const { container } = mount({ events: { onElementMoved } });
    pressToggle(container);

    const element = container.querySelector('[data-element-id="b"]')!;
    fireEvent(element, pointer("pointerdown", { button: 0, clientX: 70, clientY: 16 }));
    fireEvent(element, pointer("pointermove", { clientX: 170, clientY: 80 }));
    fireEvent(element, pointer("pointerup", { clientX: 170, clientY: 80 }));

    expect(onElementMoved).not.toHaveBeenCalled();
  });

  it("lays out over every element, so the filter never moves what it leaves", () => {
    // Hiding a, the first on the row, would pull b into its place if only the drawn were laid out.
    const { container } = mount();
    pressToggle(container);
    const before = drawnExtent(container, "b").left;

    const field = container.querySelector(".library-filter .tag-input-field")!;
    fireEvent.change(field, { target: { value: "keep" } });
    fireEvent.keyDown(field, { key: "Enter" });

    expect(container.querySelector('[data-element-id="a"]')).toBeNull();
    expect(drawnExtent(container, "b").left).toBe(before);
  });

  it("re-places an element when the model moves it", () => {
    const { container, update } = mount();
    pressToggle(container);
    expect(drawnExtent(container, "b").left).toBeCloseTo(WIDTH + 4, 0);

    // b now starts before a: it takes the row's first place, and a follows it.
    update({ ...model, elements: [model.elements[0], banner("b", -400, 40, 0, ["keep"]), model.elements[2]] });

    expect(drawnExtent(container, "b").left).toBeLessThan(drawnExtent(container, "a").left);
  });
});

describe("a drop under a layout with an inverse", () => {
  const rect = { left: 0, top: 0, width: 1000, height: 1000, right: 1000, bottom: 1000, x: 0, y: 0, toJSON: () => ({}) };

  afterEach(() => vi.restoreAllMocks());

  /** Where the canvas maps a client point to, read back from the view it is drawing. */
  function canvasPointOf(container: HTMLElement, clientX: number, clientY: number) {
    const [x, y, w, h] = container.querySelector("svg.library-canvas-surface")!.getAttribute("viewBox")!.split(/\s+/).map(Number);
    return { x: x + (clientX / 1000) * w, y: y + (clientY / 1000) * h };
  }

  /** jsdom's drag events drop the pointer's coordinates from their init, so they are set on the event. */
  function drop(container: HTMLElement, clientX: number, clientY: number) {
    const surface = container.querySelector("svg.library-canvas-surface")!;
    const event = createEvent.drop(surface, {
      dataTransfer: { types: [TOOLBOX_DRAG_TYPE], getData: () => "banner", dropEffect: "" },
    });
    Object.defineProperty(event, "clientX", { value: clientX });
    Object.defineProperty(event, "clientY", { value: clientY });
    fireEvent(surface, event);
  }

  it("raises the drop in the model's own space: between the two elements it fell between", () => {
    vi.spyOn(SVGElement.prototype, "getBoundingClientRect").mockReturnValue(rect as DOMRect);
    const onElementDropped = vi.fn();
    const { container } = mount({ events: { onElementDropped } });
    pressToggle(container);

    // Between a (placed at 0) and b (placed at 52), in client pixels.
    const [x, , w] = container.querySelector("svg.library-canvas-surface")!.getAttribute("viewBox")!.split(/\s+/).map(Number);
    const clientX = ((26 - x) / w) * 1000;
    const pointerAt = canvasPointOf(container, clientX, 500);
    drop(container, clientX, 500);

    const raised = onElementDropped.mock.calls[0][0].position;
    expect(raised.x).toBeGreaterThan(0);
    expect(raised.x).toBeLessThan(800);
    const input = {
      elements: model.elements.map((element) => ({ id: element.id, x: element.x, y: element.y, width: element.width!, height: 32 })),
      connections: [],
    };
    expect(raised).toEqual(rowPackedLayout.inverse!(pointerAt, input, definition.layout));
  });

  it("raises the pointer's own position under the manual layout", () => {
    vi.spyOn(SVGElement.prototype, "getBoundingClientRect").mockReturnValue(rect as DOMRect);
    const onElementDropped = vi.fn();
    const { container } = mount({ events: { onElementDropped } });

    const pointerAt = canvasPointOf(container, 300, 500);
    drop(container, 300, 500);

    expect(onElementDropped.mock.calls[0][0].position).toEqual(pointerAt);
  });
});
