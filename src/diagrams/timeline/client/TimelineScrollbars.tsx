import { useEffect, useRef, useState } from "react";
import type { TimelineModel } from "./timelineModel";

const ROW_HEIGHT = 60;
const DAY = 86400;

/** How much empty room the scrollable extent keeps around the content, in its own units. */
const TIME_MARGIN_FACTOR = 0.5;
const ROW_MARGIN = 2 * ROW_HEIGHT;

interface TimelineViewWindow {
  startSeconds: number;
  secondsPerPixel: number;
  panY: number;
  /** Pixels per module y unit - the vertical zoom, which shrinks the y window as it grows. */
  verticalScale: number;
}

export interface TimelineScrollbarsProps {
  model: TimelineModel;
  view: TimelineViewWindow;
  widthPx: number;
  onPan: (startSeconds: number, panY: number) => void;
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
export function TimelineScrollbars({ model, view, widthPx, onPan }: TimelineScrollbarsProps) {
  const [dragging, setDragging] = useState<"horizontal" | "vertical" | null>(null);
  const dragRef = useRef<{
    axis: "horizontal" | "vertical";
    clientX: number;
    clientY: number;
    startSeconds: number;
    panY: number;
    secondsPerThumbPixel: number;
    unitsPerThumbPixel: number;
  } | null>(null);

  const elements = [...model.elements.values()];
  const heightPx = 400; // The track lengths only shape the thumb ratio; exact height is read per event.

  // The content's extent, padded so the user can always drag a little past the edges.
  const minSeconds = elements.length > 0 ? Math.min(...elements.map((element) => element.x)) : view.startSeconds;
  const maxSeconds = elements.length > 0
    ? Math.max(...elements.map((element) => element.x + DAY))
    : view.startSeconds + widthPx * view.secondsPerPixel;
  const timeSpan = Math.max(maxSeconds - minSeconds, DAY);
  const extentStart = minSeconds - timeSpan * TIME_MARGIN_FACTOR;
  const extentEnd = maxSeconds + timeSpan * TIME_MARGIN_FACTOR;

  const minY = elements.length > 0 ? Math.min(...elements.map((element) => element.y)) : view.panY;
  const maxY = elements.length > 0 ? Math.max(...elements.map((element) => element.y + ROW_HEIGHT)) : view.panY + heightPx;
  const yExtentStart = minY - ROW_MARGIN;
  const yExtentEnd = maxY + ROW_MARGIN;

  const viewSpanSeconds = widthPx * view.secondsPerPixel;
  const horizontal = thumbOf(view.startSeconds, viewSpanSeconds, extentStart, extentEnd);
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
        onPan(drag.startSeconds + (event.clientX - drag.clientX) * drag.secondsPerThumbPixel, drag.panY);
      } else {
        onPan(drag.startSeconds, drag.panY + (event.clientY - drag.clientY) * drag.unitsPerThumbPixel);
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
      startSeconds: view.startSeconds,
      panY: view.panY,
      // One thumb pixel stands for this much content: the whole extent over the whole track.
      secondsPerThumbPixel: (extentEnd - extentStart) / Math.max(track.width, 1),
      unitsPerThumbPixel: (yExtentEnd - yExtentStart) / Math.max(track.height, 1),
    };
    setDragging(axis);
  };

  return (
    <>
      <div className="timeline-scrollbar timeline-scrollbar-horizontal" aria-hidden="true">
        <div
          className="timeline-scrollbar-thumb"
          style={{ left: `${horizontal.offset * 100}%`, width: `${horizontal.size * 100}%` }}
          onMouseDown={beginDrag("horizontal")}
        />
      </div>
      <div className="timeline-scrollbar timeline-scrollbar-vertical" aria-hidden="true">
        <div
          className="timeline-scrollbar-thumb"
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
