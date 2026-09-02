import { useEffect, useRef, useState } from "react";
import type { DependencyGraphModel } from "./dependencyGraphModel";

/*
 * TEMPORARY FORK.
 *
 * `small-refinements` Requirement 1.5 centralizes this scroll view as a shared component, and
 * this module's design says to consume it. That extraction has not landed at the time this
 * module was written - the timeline still owns the only copy - so this is a fork of
 * `src/diagrams/timeline/client/TimelineScrollbars.tsx` with its seconds turned into plain
 * canvas units. When the shared component arrives, delete this file and consume it: the
 * extraction task then covers three consumers rather than two.
 */

const ROW_HEIGHT = 60;

/** How much empty room the scrollable extent keeps around the content, in its own units. */
const X_MARGIN_FACTOR = 0.5;
const ROW_MARGIN = 2 * ROW_HEIGHT;

/** How wide a node is drawn, in canvas units - the extent has to include the whole box. */
const NODE_WIDTH = 160;

interface DependencyGraphViewWindow {
  startX: number;
  /** How many pixels one canvas unit covers - the horizontal zoom. */
  pixelsPerUnit: number;
  panY: number;
  /** Pixels per module y unit - the vertical zoom, which shrinks the y window as it grows. */
  verticalScale: number;
}

export interface DependencyGraphScrollbarsProps {
  model: DependencyGraphModel;
  view: DependencyGraphViewWindow;
  widthPx: number;
  onPan: (startX: number, panY: number) => void;
}

/**
 * Two thin scrollbars over the canvas edges, showing where the view sits inside the content and
 * draggable to pan there.
 *
 * The canvas is an unbounded plane, so these are windows onto the content's extent (plus a
 * margin) rather than native scrollbars onto a sized element - a native bar cannot describe a
 * surface whose size is a transform. Zoom stays on the wheel and the ribbon; dragging a thumb
 * only pans.
 */
export function DependencyGraphScrollbars({ model, view, widthPx, onPan }: DependencyGraphScrollbarsProps) {
  const [dragging, setDragging] = useState<"horizontal" | "vertical" | null>(null);
  const dragRef = useRef<{
    axis: "horizontal" | "vertical";
    clientX: number;
    clientY: number;
    startX: number;
    panY: number;
    unitsPerThumbPixel: number;
    yUnitsPerThumbPixel: number;
  } | null>(null);

  const elements = [...model.elements.values()];
  const heightPx = 400; // The track lengths only shape the thumb ratio; exact height is read per event.

  // The content's extent, padded so the user can always drag a little past the edges.
  const minX = elements.length > 0 ? Math.min(...elements.map((element) => element.x)) : view.startX;
  const maxX = elements.length > 0
    ? Math.max(...elements.map((element) => element.x + NODE_WIDTH))
    : view.startX + widthPx / view.pixelsPerUnit;
  const span = Math.max(maxX - minX, NODE_WIDTH);
  const extentStart = minX - span * X_MARGIN_FACTOR;
  const extentEnd = maxX + span * X_MARGIN_FACTOR;

  const minY = elements.length > 0 ? Math.min(...elements.map((element) => element.y)) : view.panY;
  const maxY = elements.length > 0 ? Math.max(...elements.map((element) => element.y + ROW_HEIGHT)) : view.panY + heightPx;
  const yExtentStart = minY - ROW_MARGIN;
  const yExtentEnd = maxY + ROW_MARGIN;

  const viewSpan = widthPx / view.pixelsPerUnit;
  const horizontal = thumbOf(view.startX, viewSpan, extentStart, extentEnd);
  const vertical = thumbOf(view.panY, heightPx / view.verticalScale, yExtentStart, yExtentEnd);

  useEffect(() => {
    if (!dragging) {
      return;
    }

    const onMove = (event: MouseEvent) => {
      const drag = dragRef.current;
      if (!drag) {
        return;
      }

      if (drag.axis === "horizontal") {
        onPan(drag.startX + (event.clientX - drag.clientX) * drag.unitsPerThumbPixel, drag.panY);
      } else {
        onPan(drag.startX, drag.panY + (event.clientY - drag.clientY) * drag.yUnitsPerThumbPixel);
      }
    };

    const onUp = () => {
      dragRef.current = null;
      setDragging(null);
    };

    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup", onUp);
    return () => {
      window.removeEventListener("mousemove", onMove);
      window.removeEventListener("mouseup", onUp);
    };
  }, [dragging, onPan]);

  const beginDrag = (axis: "horizontal" | "vertical") => (event: React.MouseEvent) => {
    event.preventDefault();
    event.stopPropagation();
    const track = (event.currentTarget as HTMLElement).parentElement!.getBoundingClientRect();
    dragRef.current = {
      axis,
      clientX: event.clientX,
      clientY: event.clientY,
      startX: view.startX,
      panY: view.panY,
      // One thumb pixel stands for this much content: the whole extent over the whole track.
      unitsPerThumbPixel: (extentEnd - extentStart) / Math.max(track.width, 1),
      yUnitsPerThumbPixel: (yExtentEnd - yExtentStart) / Math.max(track.height, 1),
    };
    setDragging(axis);
  };

  return (
    <>
      <div className="dependency-graph-scrollbar dependency-graph-scrollbar-horizontal" aria-hidden="true">
        <div
          className="dependency-graph-scrollbar-thumb"
          style={{ left: `${horizontal.offset * 100}%`, width: `${horizontal.size * 100}%` }}
          onMouseDown={beginDrag("horizontal")}
        />
      </div>
      <div className="dependency-graph-scrollbar dependency-graph-scrollbar-vertical" aria-hidden="true">
        <div
          className="dependency-graph-scrollbar-thumb"
          style={{ top: `${vertical.offset * 100}%`, height: `${vertical.size * 100}%` }}
          onMouseDown={beginDrag("vertical")}
        />
      </div>
    </>
  );
}

/** Where the view's window sits inside an extent, both as fractions of the whole. */
function thumbOf(viewStart: number, viewSpan: number, extentStart: number, extentEnd: number) {
  const extent = Math.max(extentEnd - extentStart, 1);
  const size = Math.min(Math.max(viewSpan / extent, 0.05), 1);
  const offset = Math.min(Math.max((viewStart - extentStart) / extent, 0), 1 - size);
  return { size, offset };
}
