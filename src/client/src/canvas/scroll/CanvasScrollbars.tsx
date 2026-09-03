import { useEffect, useRef, useState } from "react";
import { thumbOf, type ScrollAxis } from "./scrollGeometry";
import "./scrollView.css";

type Axis = "horizontal" | "vertical";

export interface CanvasScrollbarsProps {
  horizontal: ScrollAxis;
  vertical: ScrollAxis;
  /**
   * Both axis starts, every time - the axis not being dragged reports its current `viewStart`
   * unchanged - so a caller holding one view object writes one update.
   */
  onPan: (horizontalStart: number, verticalStart: number) => void;
  /** An extra class on both bars, for placement only: a canvas with a ruler insets its bars. */
  className?: string;
}

/**
 * Two thin scrollbars over a canvas's edges, each drawing a thumb that describes where the
 * view sits inside the content's extent, and each draggable to pan there.
 *
 * The canvas is an unbounded plane, so these are windows onto an extent (the content plus a
 * margin, see `scrollExtentOf`) rather than native scrollbars onto a sized element - a native
 * bar cannot describe a surface whose size is a transform. Zoom stays on the wheel and the
 * ribbon; dragging a thumb only pans.
 *
 * This component knows nothing about seconds, rows, nodes or view boxes. It takes two axes of
 * plain numbers and reports two new starts; each canvas converts at the call site.
 */
export function CanvasScrollbars({ horizontal, vertical, onPan, className }: CanvasScrollbarsProps) {
  const [dragging, setDragging] = useState<Axis | null>(null);
  const horizontalTrack = useRef<HTMLDivElement>(null);
  const verticalTrack = useRef<HTMLDivElement>(null);
  const dragRef = useRef<{
    axis: Axis;
    clientX: number;
    clientY: number;
    horizontalStart: number;
    verticalStart: number;
    unitsPerThumbPixel: number;
  } | null>(null);

  const horizontalThumb = thumbOf(horizontal);
  const verticalThumb = thumbOf(vertical);

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
        onPan(drag.horizontalStart + (event.clientX - drag.clientX) * drag.unitsPerThumbPixel, drag.verticalStart);
      } else {
        onPan(drag.horizontalStart, drag.verticalStart + (event.clientY - drag.clientY) * drag.unitsPerThumbPixel);
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

  const beginDrag = (axis: Axis) => (event: React.MouseEvent) => {
    event.preventDefault();
    event.stopPropagation();

    // The track is measured from its own ref rather than by walking to the thumb's parent:
    // behaviour-identical, one fewer assumption about the DOM shape.
    const track = (axis === "horizontal" ? horizontalTrack : verticalTrack).current?.getBoundingClientRect();
    const trackLength = axis === "horizontal" ? track?.width ?? 0 : track?.height ?? 0;
    const extent = axis === "horizontal"
      ? horizontal.extentEnd - horizontal.extentStart
      : vertical.extentEnd - vertical.extentStart;

    dragRef.current = {
      axis,
      clientX: event.clientX,
      clientY: event.clientY,
      horizontalStart: horizontal.viewStart,
      verticalStart: vertical.viewStart,
      // One thumb pixel stands for this much content: the whole extent over the whole track.
      unitsPerThumbPixel: extent / Math.max(trackLength, 1),
    };
    setDragging(axis);
  };

  const barClass = (axis: Axis) => `canvas-scrollbar canvas-scrollbar-${axis}${className ? ` ${className}` : ""}`;

  // Both bars are hidden from assistive technology: they duplicate a pan that is already
  // available by dragging the canvas, and announcing them adds noise rather than access.
  return (
    <>
      <div ref={horizontalTrack} className={barClass("horizontal")} aria-hidden="true">
        <div
          className="canvas-scrollbar-thumb"
          style={{ left: `${horizontalThumb.offset * 100}%`, width: `${horizontalThumb.size * 100}%` }}
          onMouseDown={beginDrag("horizontal")}
        />
      </div>
      <div ref={verticalTrack} className={barClass("vertical")} aria-hidden="true">
        <div
          className="canvas-scrollbar-thumb"
          style={{ top: `${verticalThumb.offset * 100}%`, height: `${verticalThumb.size * 100}%` }}
          onMouseDown={beginDrag("vertical")}
        />
      </div>
    </>
  );
}
