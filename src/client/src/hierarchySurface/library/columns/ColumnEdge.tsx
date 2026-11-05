import { type KeyboardEvent, type PointerEvent, useRef } from "react";
import type { ColumnWidths } from "./useColumnWidths";

/** How far an arrow key moves a column's edge, in pixels. */
const STEP = 16;

export interface ColumnEdgeProps {
  /** The depth of the column whose right edge it is, 1 for the top levels. */
  level: number;
  columns: ColumnWidths;
}

/**
 * The right edge of a column of levels, dragged to change the column's width (DESL section 7.7.1): every box of that
 * depth follows the pointer, and the width is kept when the pointer is let go. A double click gives the column the
 * page's own width again; the arrow keys move the edge by a step, Home gives the page's own width.
 */
export function ColumnEdge({ level, columns }: ColumnEdgeProps) {
  const start = useRef<{ x: number; width: number } | null>(null);
  const width = columns.widthOf(level);

  const onPointerDown = (event: PointerEvent<HTMLSpanElement>) => {
    if (event.button !== 0) {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    event.currentTarget.setPointerCapture?.(event.pointerId);
    start.current = { x: event.clientX, width };
  };
  const onPointerMove = (event: PointerEvent<HTMLSpanElement>) => {
    if (start.current !== null) {
      columns.preview(level, start.current.width + event.clientX - start.current.x);
    }
  };
  const onPointerUp = (event: PointerEvent<HTMLSpanElement>) => {
    if (start.current !== null) {
      columns.commit(level, start.current.width + event.clientX - start.current.x);
      start.current = null;
    }
  };
  const onKeyDown = (event: KeyboardEvent<HTMLSpanElement>) => {
    const by = event.key === "ArrowRight" ? STEP : event.key === "ArrowLeft" ? -STEP : 0;
    if (by !== 0) {
      event.preventDefault();
      event.stopPropagation();
      columns.commit(level, width + by);
    } else if (event.key === "Home") {
      event.preventDefault();
      event.stopPropagation();
      columns.reset(level);
    }
  };

  return (
    <span
      className="hs-column-edge"
      role="separator"
      aria-orientation="vertical"
      aria-label={`Width of column ${level}`}
      aria-valuenow={width}
      aria-valuemin={columns.min}
      aria-valuemax={columns.max}
      title="Drag to change the column's width; double-click for the default"
      tabIndex={0}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={onPointerUp}
      onPointerCancel={() => {
        start.current = null;
      }}
      onClick={(event) => event.stopPropagation()}
      onDoubleClick={(event) => {
        event.stopPropagation();
        columns.reset(level);
      }}
      onKeyDown={onKeyDown}
    />
  );
}
