import type { ShapeBounds } from "../definition/diagramDefinition";
import type { DislAxis, DislCanvas, DislDocument } from "./disTypes";

/**
 * A canvas of fixed bounds (DISL §6.13 `canvas.bounds`, `fit`, `margin`) and where its coordinate
 * system's points fall on it.
 *
 * <b>Stretched across, never up.</b> With `fit: "stretch"` DISL scales x and y independently so that the
 * bounds fill the pane less the margin. The canvas is fitted to its extent at a uniform zoom, so the same
 * drawing is had by keeping the height and widening the bounds until bounds and margin together have the
 * pane's proportions: node sizes, strokes, fonts and label offsets stay in canvas units, as DISL says, and
 * only where things stand moves. Without a pane, or without a stretch, the bounds are as stated.
 *
 * <b>An axis with a `min` and a `max` spans the bounds along it</b>, from its minimum to its maximum, the
 * other way round where it is `reversed`, and a y axis the other way round again in a `y-up` system. An
 * axis without both is measured in canvas units, widened with the bounds where it runs across.
 */
export interface DrawingArea {
  /** The bounds, widened to the pane where the canvas stretches. */
  area: ShapeBounds;
  /** The area with its margin round it: what Fit shows. */
  extent: ShapeBounds;
  /** A point of the coordinate system as canvas units, to the hundredth. */
  toCanvas(x: number, y: number): { x: number; y: number };
  /** A canvas point as a point of the coordinate system. */
  toDomain(x: number, y: number): { x: number; y: number };
}

/** The margin's four sides (DISL §6.13 Insets). */
function marginOf(margin: DislCanvas["margin"]): { top: number; right: number; bottom: number; left: number } {
  if (typeof margin === "number") {
    return { top: margin, right: margin, bottom: margin, left: margin };
  }

  return { top: margin?.top ?? 0, right: margin?.right ?? 0, bottom: margin?.bottom ?? 0, left: margin?.left ?? 0 };
}

/** One axis's fraction of the span for a value, and back: flipped where the axis runs the other way. */
function spanOf(axis: DislAxis | undefined, flipped: boolean): { fraction(value: number): number; value(fraction: number): number } | undefined {
  if (axis?.min === undefined || axis.max === undefined || axis.max === axis.min) {
    return undefined;
  }

  const { min, max } = axis;
  return {
    fraction: (value) => {
      const fraction = (value - min) / (max - min);
      return flipped ? 1 - fraction : fraction;
    },
    value: (fraction) => min + (flipped ? 1 - fraction : fraction) * (max - min),
  };
}

const hundredth = (value: number) => Math.round(value * 100) / 100;

/**
 * The drawing area of `spec`'s coordinate system `system` (its default where none is named), for a pane of
 * the given size; none for a canvas that states no bounds. A stretch with no bounds to stretch is refused.
 */
export function drawingArea(spec: DislDocument, pane?: { width: number; height: number }, system?: string): DrawingArea | undefined {
  const canvas = spec.notation.canvas;
  const bounds = canvas?.bounds;
  if (bounds === undefined) {
    if (canvas?.fit === "stretch") {
      throw new Error("notation.canvas stretches the canvas to the pane and states no bounds to stretch (DISL §6.13).");
    }
    return undefined;
  }

  const margin = marginOf(canvas?.margin);
  const outerHeight = bounds.h + margin.top + margin.bottom;
  const stretched =
    canvas?.fit === "stretch" && pane !== undefined && pane.width > 0 && pane.height > 0 && Number.isFinite(pane.width / pane.height)
      ? (pane.width / pane.height) * outerHeight - margin.left - margin.right
      : bounds.w;
  // A pane too narrow for the margin alone leaves no area; the bounds as stated are the better answer there.
  const width = stretched > 0 ? stretched : bounds.w;
  const area = { x: bounds.x, y: bounds.y, width, height: bounds.h };
  const extent = { x: bounds.x - margin.left, y: bounds.y - margin.top, width: width + margin.left + margin.right, height: outerHeight };

  const coordinates = spec.coordinates;
  const named = coordinates?.systems[system ?? coordinates.default ?? ""];
  const axisOf = (axis: string | DislAxis | undefined) => (typeof axis === "string" ? coordinates?.axes?.[axis] : axis);
  const xAxis = axisOf(named?.x);
  const yAxis = axisOf(named?.y);
  const across = spanOf(xAxis, xAxis?.reversed === true);
  const up = spanOf(yAxis, (yAxis?.reversed === true) !== (named?.orientation === "y-up"));
  const widened = width / bounds.w;

  return {
    area,
    extent,
    toCanvas: (x, y) => ({
      x: hundredth(across === undefined ? bounds.x + (x - bounds.x) * widened : area.x + across.fraction(x) * area.width),
      y: hundredth(up === undefined ? y : area.y + up.fraction(y) * area.height),
    }),
    toDomain: (x, y) => ({
      x: across === undefined ? bounds.x + (x - bounds.x) / widened : across.value((x - area.x) / area.width),
      y: up === undefined ? y : up.value((y - area.y) / area.height),
    }),
  };
}
