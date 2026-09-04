import type { Viewport } from "@client/diagrams/viewReport";

/**
 * The RDF family's four readings all drive a pixels-per-unit view rather than an SVG view box,
 * so they convert to a reported viewport by measuring their own surface and dividing. The shared
 * `shownRectOf` answers the view-box question and deliberately knows nothing about pixels; this
 * answers the other one, once, for the four canvases that ask it.
 *
 * Factoring inside one module, not a second shared library: the four readings genuinely want the
 * same arithmetic and nothing outside the family can reach this.
 */

/** What every reading's view has in common - the part this conversion needs. */
export interface RdfViewLike {
  startX: number;
  startY: number;
  pixelsPerUnit: number;
}

/**
 * What the reader can see, in canvas units.
 *
 * Before the first layout - jsdom, or the frame before a measure settles - the surface has no
 * size, and the fallback is the same 1200x600 the family's scroll geometry already assumes. It
 * is a guess either way; using the same guess in both places means the bars and the report never
 * describe different rectangles.
 */
export function viewportOf(view: RdfViewLike, surface: { width: number; height: number } | null): Viewport {
  const widthPx = surface?.width || 1200;
  const heightPx = surface?.height || 600;

  return {
    minX: view.startX,
    minY: view.startY,
    maxX: view.startX + widthPx / view.pixelsPerUnit,
    maxY: view.startY + heightPx / view.pixelsPerUnit,
  };
}
