import { anchorsBetween, midpointOf, straightPath, type ConnectorBox } from "../../connectors";

export interface StraightConnectionProps {
  /** The box the connection leaves. */
  from: ConnectorBox;
  /** The box it arrives at. */
  to: ConnectorBox;
  /** Class for the wrapping `<g>`. */
  className?: string;
  /** Class for the line itself. */
  pathClassName?: string;
  /** An SVG marker reference (e.g. `url(#arrow)`) for the arriving end. */
  markerEnd?: string;
  /** Text at the line's midpoint; omitted, no text element renders. */
  label?: string;
  labelClassName?: string;
  /** Vertical offset of the label from the midpoint; negative is above. */
  labelDy?: number;
  labelTextAnchor?: "start" | "middle" | "end";
  /** A tooltip for the whole connection. */
  title?: string;
  ["data-testid"]?: string;
}

/**
 * A straight connection between two boxes, anchored on each box's edge along the line between
 * their centres - which is what stops an arrowhead disappearing under the box it points at.
 *
 * Straight, deliberately: this is the graph case, where any element may connect to any other
 * in any direction, so there is no corridor for a curve to stay inside. C4 relationships,
 * Wardley links and Ansible edges all read this way.
 */
export function StraightConnection({
  from,
  to,
  className,
  pathClassName,
  markerEnd,
  label,
  labelClassName,
  labelDy = -6,
  labelTextAnchor,
  title,
  "data-testid": dataTestId,
}: StraightConnectionProps) {
  const [start, end] = anchorsBetween(from, to);
  const middle = midpointOf(start, end);

  return (
    <g className={className} data-testid={dataTestId}>
      <path className={pathClassName} d={straightPath(start, end)} markerEnd={markerEnd} />
      {label ? (
        <text className={labelClassName} x={middle.x} y={middle.y + labelDy} textAnchor={labelTextAnchor}>
          {label}
        </text>
      ) : null}
      {title ? <title>{title}</title> : null}
    </g>
  );
}
