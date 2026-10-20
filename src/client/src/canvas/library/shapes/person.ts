import type { ShapeBounds, ShapePoint } from "../definition/diagramDefinition";

/**
 * The person, as DISL 0.5 §6.7 states it, once: what the library's `person` draws, what a styled box
 * of the `Person` silhouette draws, and what a press, a drop and the fitted view count as the person.
 *
 * - The **body** is a rounded rectangle exactly over the bounds. Its corner is the smaller of the
 *   radius (8, Appendix B.2) and half the smaller side, so it never turns inside out.
 * - The **head** is a circle of radius 10 centred on the bounds' vertical centre line, 8 above the top
 *   edge. Its size and place are fixed: neither the bounds nor the radius move it.
 * - The head is painted first, so the body covers its bottom. Relations meet the body alone - the
 *   library's edge point is the box's, which `outlineOf` leaves to the rectangle - while the head is
 *   part of the node for everything that points at it.
 */

/** The body's corner radius, DISL's default and the only one the library draws (`shapeCatalog.ts`). */
export const PERSON_CORNER_RADIUS = 8;

/** The head's radius. */
export const PERSON_HEAD_RADIUS = 10;

/** How far above the top edge the head's centre is. */
export const PERSON_HEAD_RISE = 8;

export interface PersonParts {
  head: { cx: number; cy: number; r: number };
  body: { x: number; y: number; width: number; height: number; rx: number };
}

/** The head and the body of a person whose bounds are `bounds`, in the bounds' own coordinates. */
export function personParts(bounds: ShapeBounds): PersonParts {
  return {
    head: { cx: bounds.x + bounds.width / 2, cy: bounds.y - PERSON_HEAD_RISE, r: PERSON_HEAD_RADIUS },
    body: {
      x: bounds.x,
      y: bounds.y,
      width: bounds.width,
      height: bounds.height,
      rx: Math.min(PERSON_CORNER_RADIUS, bounds.width / 2, bounds.height / 2),
    },
  };
}

/** The box around the head, which stands outside the bounds: what the fitted view adds for a person. */
export function personHeadBox(bounds: ShapeBounds): ShapeBounds {
  const { head } = personParts(bounds);
  return { x: head.cx - head.r, y: head.cy - head.r, width: 2 * head.r, height: 2 * head.r };
}

/** Whether a point is on the head - which, with the bounds, is where a person is hit. */
export function onPersonHead(bounds: ShapeBounds, point: ShapePoint): boolean {
  const { head } = personParts(bounds);
  return Math.hypot(point.x - head.cx, point.y - head.cy) <= head.r;
}
