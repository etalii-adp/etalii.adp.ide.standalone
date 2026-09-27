import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createEvent, fireEvent, render } from "@testing-library/react";
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
      expect(definition.layout.rowPacked).toEqual({ width: COMPACT_WIDTH, gap: GhgScale.unitsPerMonth, types: ["trend"], rowStep: GhgScale.rowStep });
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

/**
 * ghg-triggers-and-notes task 8: triggers and notes in the compact placement, among the trends, at
 * their own size, and fixed in place - read off what the canvas draws and raises.
 */
describe("triggers and notes in compact mode", () => {
  const middleOf = (row: number) => row * GhgScale.rowStep + GhgScale.trendHeight / 2;
  const trend = (id: string, left: number, row: number) => ({
    id, type: "trend", x: left + 200, y: middleOf(row), width: 400, height: GhgScale.trendHeight, label: id,
    payload: { name: id, phases: 4, boundaries: [], tags: [], snapX: 0, snapY: 0 },
  });

  /** Early and late on row 1, a trigger dated between them, and a note two rows tall starting after early. */
  const MODEL: DiagramModel = {
    elements: [
      trend("early", 0, 1),
      trend("late", 400, 1),
      trend("below", 150, 2),
      { id: "spark", type: "trigger", x: 200, y: middleOf(1), width: 16, height: 16, label: "Spark", payload: { name: "Spark", when: "1904", whenLong: "1904", tags: [], snapX: -8, snapY: 8 } },
      { id: "remark", type: "note", x: 100 + 80, y: GhgScale.rowStep + 32, width: 160, height: 64, label: "A remark", payload: { text: "A remark", snapX: 0, snapY: 0 } },
    ],
    connections: [],
  };

  function renderWith(events: Record<string, unknown> = {}, selected = "early") {
    const result = render(
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvasCore definition={GHG_DEFINITION} model={MODEL} events={events} selection={[{ kind: "element", id: selected }]} />
        </DiagramToolboxProvider>
      </DiagramViewProvider>,
    );
    fireEvent.click(result.container.querySelector(".library-layout-toggle")!);
    return result;
  }

  /** Where a shape's own group is translated to: shapes draw about their centre inside it. */
  const translationOf = (shape: Element) => {
    const [, x, y] = /translate\((-?[\d.]+)[ ,]+(-?[\d.]+)\)/.exec(shape.parentElement!.getAttribute("transform") ?? "") ?? [, "0", "0"];
    return { x: Number(x), y: Number(y) };
  };

  /** The circle's drawn centre and radius. */
  const circleOf = (container: HTMLElement, id: string) => {
    const circle = container.querySelector(`[data-element-id="${id}"] ellipse`)!;
    const at = translationOf(circle);
    return { cx: at.x + Number(circle.getAttribute("cx") ?? 0), cy: at.y + Number(circle.getAttribute("cy") ?? 0), r: Number(circle.getAttribute("rx")) };
  };

  /** The note's drawn box. */
  const noteBox = (container: HTMLElement) => {
    const rect = container.querySelector('[data-element-id="remark"] rect.ghg-note-box')!;
    const at = translationOf(rect);
    const left = at.x + Number(rect.getAttribute("x"));
    const top = at.y + Number(rect.getAttribute("y"));
    return { left, top, right: left + Number(rect.getAttribute("width")), bottom: top + Number(rect.getAttribute("height")) };
  };

  it("places a trigger between the trends starting before and after its date, on its row, at its own size", () => {
    const { container } = renderWith();
    const spark = circleOf(container, "spark");

    expect(spark.r).toBe(8);
    expect(spark.cy).toBe(middleOf(1));
    expect(spark.cx - spark.r).toBeGreaterThan(drawnBox(container, "early").right);
    expect(spark.cx + spark.r).toBeLessThan(drawnBox(container, "late").left);
  });

  it("keeps a note's size, and nothing overlaps it on either row it covers", () => {
    const { container } = renderWith();
    const note = noteBox(container);

    expect([note.right - note.left, note.bottom - note.top]).toEqual([160, 64]);
    for (const id of ["early", "late", "below"]) {
      const box = drawnBox(container, id);
      const sharesARow = box.top < note.bottom && box.bottom > note.top;
      const overlaps = sharesARow && box.left < note.right && box.right > note.left;
      expect(overlaps, `${id} overlaps the note`).toBe(false);
    }
  });

  it("drags neither, and offers the note no resize", () => {
    const onElementMoved = vi.fn();
    const { container } = renderWith({ onElementMoved }, "remark");
    const spark = circleOf(container, "spark");
    const circle = container.querySelector('[data-element-id="spark"] ellipse')!;

    fireEvent(circle, pointer("pointerdown", { button: 0, ...clientOf(container, spark.cx, spark.cy) }));
    fireEvent(circle, pointer("pointermove", clientOf(container, spark.cx + 100, spark.cy + 100)));
    fireEvent(circle, pointer("pointerup", clientOf(container, spark.cx + 100, spark.cy + 100)));

    expect(onElementMoved).not.toHaveBeenCalled();
    expect(container.querySelectorAll('[data-element-id="remark"] .library-resize-handle')).toHaveLength(0);
  });

  it("still draws an influence from a trigger", () => {
    const onConnectionDrawn = vi.fn();
    const { container } = renderWith({ onConnectionDrawn });
    const spark = circleOf(container, "spark");
    const late = drawnBox(container, "late");
    const handle = container.querySelector('[data-element-id="spark"] [data-anchor="e"]')!;
    const over = clientOf(container, late.left + 4, late.top + 2);

    fireEvent(handle, pointer("pointerdown", { button: 0, ...clientOf(container, spark.cx + 8, spark.cy) }));
    fireEvent(handle, pointer("pointermove", over));
    fireEvent(handle, pointer("pointerup", over));

    expect(onConnectionDrawn).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ sourceElementId: "spark", targetElementId: "late" }));
  });

  it("dates a trigger dropped between two trends between theirs", () => {
    const onElementDropped = vi.fn();
    const { container } = renderWith({ onElementDropped });
    const early = drawnBox(container, "early");
    const late = drawnBox(container, "late");
    const surface = container.querySelector("svg.library-canvas-surface")!;
    const at = clientOf(container, (early.left + late.left) / 2, middleOf(4));
    const drop = createEvent.drop(surface, { dataTransfer: { types: ["application/x-adp-toolbox-item"], getData: () => "trigger", dropEffect: "" } });
    Object.defineProperty(drop, "clientX", { value: at.clientX });
    Object.defineProperty(drop, "clientY", { value: at.clientY });

    fireEvent(surface, drop);

    expect(onElementDropped).toHaveBeenCalledTimes(1);
    const { x } = onElementDropped.mock.calls[0][0].position;
    expect(x).toBeGreaterThan(0);
    expect(x).toBeLessThan(400);
  });

  it("switches on and off without a single edit raised", () => {
    const edits = { onElementMoved: vi.fn(), onElementResized: vi.fn(), onElementDropped: vi.fn(), onConnectionDrawn: vi.fn(), onElementDeleted: vi.fn(), onActionInvoked: vi.fn() };
    const { container } = renderWith(edits);

    fireEvent.click(container.querySelector(".library-layout-toggle")!);
    fireEvent.click(container.querySelector(".library-layout-toggle")!);

    for (const [name, handler] of Object.entries(edits)) {
      expect(handler, name).not.toHaveBeenCalled();
    }
  });
});
