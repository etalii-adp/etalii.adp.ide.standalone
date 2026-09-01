import type { Point } from "../../connectors";

export interface FixedBezierConnectionProps {
  /** The precomputed departure point - typically the middle of a box's right edge. */
  from: Point;
  /** The precomputed arrival point - typically the middle of the next box's left edge. */
  to: Point;
  /** How far the control points reach horizontally past each end. */
  reach?: number;
  className?: string;
  /** An SVG marker reference (e.g. `url(#arrow)`) for the arriving end. */
  markerEnd?: string;
  ["data-testid"]?: string;
}

/**
 * A horizontal cubic between two precomputed points with a fixed control reach.
 *
 * This is the column-layout case: rows of boxes connect right edge to left edge, close enough
 * together that a midpoint-based curve would flatten out - a constant reach keeps every
 * connector leaving and arriving horizontally at the same crispness. The Azure pipeline's
 * "waits for" arrows read this way.
 */
export function FixedBezierConnection({
  from,
  to,
  reach = 30,
  className,
  markerEnd,
  "data-testid": dataTestId,
}: FixedBezierConnectionProps) {
  return (
    <path
      className={className}
      data-testid={dataTestId}
      d={`M ${from.x} ${from.y} C ${from.x + reach} ${from.y}, ${to.x - reach} ${to.y}, ${to.x} ${to.y}`}
      markerEnd={markerEnd}
      fill="none"
    />
  );
}
