import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvasCore } from "@client/canvas/library/DiagramCanvas";
import { effectiveDefinition } from "@client/canvas/library/api/diagramRuntimeConfig";
import { validateDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramModel } from "@client/canvas/library/api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { pointer } from "@client/canvas/library/testing/canvasHarness";
import { GHG_DEFINITION, ghgDefinitionFor } from "./GhgCanvas";
import { GhgScale, GhgTimeUnits, type GhgTimeUnit } from "./ghgIds";
import { exampleModel } from "./ghgExample";
import { LABEL_FONT_SIZE, widthOf } from "@client/canvas/label/textMetrics";
import { BEFORE_GAP } from "@client/canvas/library/definition/labels";

/**
 * Compact mode over the technology-trends example: the definition's second layout mode, switched on
 * with the Compact toggle, read off what the canvas draws. The placement's own properties are the
 * library's `rowPackedLayout.test.ts`; this is the module's declaration of it, and what an author
 * can and cannot do while it is on.
 */

const COMPACT_WIDTH = 12 * GhgScale.unitsPerMonth;
const RECT = { width: 1000, height: 600 };
let restore: (() => void) | undefined;

beforeEach(() => {
  const original = SVGSVGElement.prototype.getBoundingClientRect;
  SVGSVGElement.prototype.getBoundingClientRect = () =>
    ({ left: 0, top: 0, x: 0, y: 0, width: RECT.width, height: RECT.height, right: RECT.width, bottom: RECT.height, toJSON: () => ({}) }) as DOMRect;
  restore = () => { SVGSVGElement.prototype.getBoundingClientRect = original; };
});

afterEach(() => restore?.());

function renderCompact(model: DiagramModel, events = {}) {
  const result = render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={GHG_DEFINITION} model={model} events={events} selection={[{ kind: "element", id: model.elements[0].id }]} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
  fireEvent.click(result.container.querySelector(".library-layout-toggle")!);
  return result;
}

/** An element's drawn box, from its segments' corners. */
function drawnBox(container: HTMLElement, id: string) {
  const corners = [...container.querySelectorAll<SVGPathElement>(`[data-element-id="${id}"] path.library-segment`)].flatMap((path) =>
    [...path.getAttribute("d")!.matchAll(/(-?[\d.]+) (-?[\d.]+)/g)].map(([, x, y]) => ({ x: Number(x), y: Number(y) })),
  );
  const xs = corners.map((corner) => corner.x);
  const ys = corners.map((corner) => corner.y);
  return { left: Math.min(...xs), right: Math.max(...xs), top: Math.min(...ys), bottom: Math.max(...ys) };
}

/** The client point over a canvas point, through the view the canvas is showing. */
function clientOf(container: HTMLElement, x: number, y: number) {
  const [vx, vy, vw, vh] = container.querySelector("svg")!.getAttribute("viewBox")!.split(/\s+/).map(Number);
  return { clientX: ((x - vx) / vw) * RECT.width, clientY: ((y - vy) / vh) * RECT.height };
}

describe("the hype cycle graph's compact mode, declared", () => {
  it("is a second layout mode behind a toggle captioned Compact, valid for every time unit", () => {
    for (const unit of Object.keys(GhgTimeUnits) as GhgTimeUnit[]) {
      const definition = ghgDefinitionFor(unit);
      expect(definition.layout.modes).toEqual(["manual", "row-packed"]);
      expect(definition.layout.toggle).toEqual({ caption: "Compact", on: "row-packed" });
      expect(definition.layout.rowPacked).toEqual({ width: COMPACT_WIDTH, gap: GhgScale.unitsPerMonth });
      expect(validateDiagramDefinition(definition)).toEqual([]);
    }
  });

  it("switches off moving, resizing, boundary dragging and the time axis, and splits phases evenly", () => {
    const compact = effectiveDefinition(GHG_DEFINITION, undefined, "row-packed");
    const [trend] = compact.elementTypes;

    expect(compact.dragging).toBe("disabled");
    expect(trend.draggable).toBe(false);
    expect(trend.sizing).toBe("model");
    expect(trend.segments?.draggableBoundaries).toBe(false);
    expect(trend.segments?.boundaries).toBeUndefined();
    expect(compact.chrome?.rulers).toEqual([]);
    // Everything else about a trend stays as it is in true-time.
    expect(trend.labels).toEqual(GHG_DEFINITION.elementTypes[0].labels);
    expect(trend.anchors).toEqual(GHG_DEFINITION.elementTypes[0].anchors);
    expect(compact.relationTypes).toBe(GHG_DEFINITION.relationTypes);
  });
});

describe("the technology-trends example in compact mode", () => {
  const model = exampleModel();

  // All 200 trends are mounted and measured: well under a second alone, and several times that when
  // the backend suite runs every client test file at once, so it is given room rather than the default.
  it("draws every trend at one width, in the order they start, never two touching on a row", { timeout: 30_000 }, () => {
    const { container } = renderCompact(model);

    const placed = model.elements.map((element) => ({
      name: element.label ?? "",
      start: element.x - element.width! / 2,
      row: element.y,
      box: drawnBox(container, element.id),
    }));
    for (const entry of placed) {
      expect(entry.box.right - entry.box.left).toBeCloseTo(COMPACT_WIDTH, 5);
    }
    const byStart = [...placed].sort((a, b) => a.start - b.start);
    for (let index = 1; index < byStart.length; index += 1) {
      if (byStart[index].start > byStart[index - 1].start) {
        expect(byStart[index].box.left).toBeGreaterThanOrEqual(byStart[index - 1].box.left);
      }
    }
    const rows = new Map<number, typeof placed>();
    for (const entry of placed) {
      rows.set(entry.row, [...(rows.get(entry.row) ?? []), entry]);
    }
    for (const row of rows.values()) {
      // A trend's name is drawn to its left, so the gap before it holds the name as well.
      const ordered = [...row].sort((a, b) => a.box.left - b.box.left);
      for (let index = 1; index < ordered.length; index += 1) {
        const room = GhgScale.unitsPerMonth + BEFORE_GAP + widthOf(ordered[index].name, LABEL_FONT_SIZE);
        expect(ordered[index].box.left - ordered[index - 1].box.right).toBeGreaterThanOrEqual(room - 1e-6);
      }
    }
  });

  it("draws no ruler, no resize handle and no boundary handle", () => {
    const { container } = renderCompact(exampleModel(["coal", "steam-engine"]));

    expect(container.querySelector(".library-ruler")).toBeNull();
    expect(container.querySelectorAll(".library-resize-handle")).toHaveLength(0);
    expect(container.querySelectorAll(".library-boundary-handle")).toHaveLength(0);
  });

  it("offers an influence where none exists, and refuses a second in the same direction", () => {
    const onConnectionDrawn = vi.fn();
    const { container } = renderCompact(exampleModel(["iron-smelting", "coal", "steam-engine"]), { onConnectionDrawn });

    const draw = (fromId: string, toId: string) => {
      const from = drawnBox(container, fromId);
      const to = drawnBox(container, toId);
      const strip = container.querySelector(`[data-element-id="${fromId}"] .library-edge-strip[data-edge="bottom"][data-region="0"]`)!;
      const over = clientOf(container, (to.left + to.right) / 2 - 8, (to.top + to.bottom) / 2);
      fireEvent(strip, pointer("pointerdown", { button: 0, ...clientOf(container, from.left + 2, from.bottom) }));
      fireEvent(strip, pointer("pointermove", over));
      const offered = container.querySelector(`[data-element-id="${toId}"]`)!.classList.contains("library-connect-target");
      fireEvent(strip, pointer("pointerup", over));
      return offered;
    };

    // Iron smelting already influences the steam engine; nothing joins it to coal.
    expect(draw("iron-smelting", "steam-engine")).toBe(false);
    expect(draw("iron-smelting", "coal")).toBe(true);
    expect(onConnectionDrawn).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({ sourceElementId: "iron-smelting", targetElementId: "coal" }),
    );
  });

  it("starts no drag of a trend", () => {
    const onElementMoved = vi.fn();
    const { container } = renderCompact(exampleModel(["coal"]), { onElementMoved });
    const box = drawnBox(container, "coal");
    const body = container.querySelector('[data-element-id="coal"] .library-segment')!;

    fireEvent(body, pointer("pointerdown", { button: 0, ...clientOf(container, box.left + 20, box.top + 8) }));
    fireEvent(body, pointer("pointermove", clientOf(container, box.left + 200, box.top + 100)));
    fireEvent(body, pointer("pointerup", clientOf(container, box.left + 200, box.top + 100)));

    expect(onElementMoved).not.toHaveBeenCalled();
  });
});
