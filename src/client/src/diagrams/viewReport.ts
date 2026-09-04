import type { Client } from "@connectrpc/connect";
import type { DiagramService } from "@client/generated/diagrams_pb";

/**
 * The client half of the view-delta loop: what the reader can see goes up the paired
 * `UpdateView` leg, and the backend answers on the `Open` stream with the deltas that bring the
 * connection into line.
 *
 * This was four identical copies before it was one. `reportView`'s bodies differed in a single
 * comment; `shownRectOf` was defined three times, two of them character-identical and the third
 * exactly what those two return when there is no laid-out surface; and the `Viewport` interface
 * below was declared four times, which is how a shape actually spreads - nobody copies a
 * five-line interface deliberately, they copy the file it lives in.
 *
 * What is deliberately NOT here: what a module's elements are, how it decides which of them a
 * viewport intersects, and what its session does with the answer. This file speaks only of
 * rectangles, paths and watch ids (view-delta-adoption Requirement 2.4).
 */

/** A viewport the client reports; the backend answers with what falls inside it. */
export interface Viewport {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

/** An SVG view box, in the canvas's own units. */
export interface ViewBox {
  x: number;
  y: number;
  w: number;
  h: number;
}

/**
 * How long a settled view waits before it is reported - the one place this number is written.
 * A pan produces a view change per frame; without this the backend becomes a participant in
 * the gesture rather than an observer of where it ended.
 */
export const VIEW_REPORT_DEBOUNCE_MS = 200;

/**
 * What the svg actually puts on screen, in canvas units - which is not the viewBox. With the
 * default `preserveAspectRatio` ("xMidYMid meet") the browser scales the box to fit inside the
 * element and centres it, so whichever axis has room left over shows more of the diagram than
 * the box asked for. Reporting the bare viewBox therefore understates the visible area, and the
 * backend culls elements sitting in that margin while the reader is looking straight at them.
 *
 * `surface` is optional, and a canvas that has none passes nothing rather than passing `null`.
 * Without a laid-out surface - no argument, a null ref, jsdom, or before the first measure - the
 * box is the best answer available and is reported unchanged. That branch is not a fallback
 * bolted on for the pixel-less case: it is character-for-character what a canvas driving a bare
 * `viewBox` computed for itself before this was shared, so passing no surface is a complete
 * answer rather than a degraded one.
 */
export function shownRectOf(box: ViewBox, surface?: SVGSVGElement | null): Viewport {
  const rect = surface?.getBoundingClientRect();
  if (rect === undefined || rect.width <= 0 || rect.height <= 0 || box.w <= 0 || box.h <= 0) {
    return { minX: box.x, minY: box.y, maxX: box.x + box.w, maxY: box.y + box.h };
  }

  // "meet" scales by whichever axis is the tighter fit; the other one then spans more units.
  const scale = Math.min(rect.width / box.w, rect.height / box.h);
  const shownWidth = rect.width / scale;
  const shownHeight = rect.height / scale;
  const centerX = box.x + box.w / 2;
  const centerY = box.y + box.h / 2;
  return {
    minX: centerX - shownWidth / 2,
    minY: centerY - shownHeight / 2,
    maxX: centerX + shownWidth / 2,
    maxY: centerY + shownHeight / 2,
  };
}

/**
 * Builds the module's `reportView`: the unary `UpdateView` call, correlated to the open stream
 * by `watchId` and path.
 *
 * A module builds this on the client its stream hook already returned, so adopting the report
 * does not mean adopting {@link useDiagramStream} - three of the eleven modules hand-roll their
 * open loop, and this must not drag them into that migration (Requirement 3.6).
 */
export function viewReportOf(
  client: Client<typeof DiagramService>,
  projectId: Uint8Array,
  watchId: Uint8Array,
  path: readonly string[],
): (viewport: Viewport) => void {
  return (viewport: Viewport) => {
    void client
      .updateView({
        projectId: { value: projectId },
        watchId: { value: watchId },
        path: { segments: [...path] },
        view: {
          center: { x: (viewport.minX + viewport.maxX) / 2, y: (viewport.minY + viewport.maxY) / 2 },
          boundingBox: { min: { x: viewport.minX, y: viewport.minY }, max: { x: viewport.maxX, y: viewport.maxY } },
        },
      })
      .catch(() => {
        // A view report is advisory; if it fails the backend keeps the last one it had.
      });
  };
}
