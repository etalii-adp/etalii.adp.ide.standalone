import { facingAnchorsBetween, forwardBezierPath, horizontalBezierPath, sideAnchorOf, type ConnectorBox } from "../../connectors";

export interface InteractiveBezierConnectionProps {
  /** The box the connection leaves. */
  from: ConnectorBox;
  /** The box it arrives at. */
  to: ConnectorBox;
  /**
   * Whether the target conceptually starts before the source ends - decided by the module in
   * its own coordinates (time, order), not in pixels. When set, the line departs the source
   * forward and loops back into the target, so the direction still reads left to right.
   */
  loopsBack?: boolean;
  selected?: boolean;
  label?: string;
  /** Classes for the pieces; all styling lives with the adopting module's stylesheet. */
  className?: string;
  selectedClassName?: string;
  /** The invisible fat twin of the line that carries the pointer - a thin stroke is no target. */
  hitClassName?: string;
  lineClassName?: string;
  /** Written as `data-connection-id` so release-point hit tests can find the connection. */
  id?: string;
  onSelect?: (event: React.MouseEvent) => void;
  onOpenMenu?: (event: React.MouseEvent) => void;
}

/**
 * A selectable, right-clickable connection drawn as a facing-sides bezier.
 *
 * This is the interactive case the timeline grew: a relation the user can left-click to
 * select and right-click for its menu, clickable along its whole length through an invisible
 * fat hit path painted under the visible line. Mousedown stops propagating so the surface
 * never reads the press as background panning.
 */
export function InteractiveBezierConnection({
  from,
  to,
  loopsBack = false,
  selected = false,
  label,
  className,
  selectedClassName,
  hitClassName,
  lineClassName,
  id,
  onSelect,
  onOpenMenu,
}: InteractiveBezierConnectionProps) {
  const [a, b] = loopsBack
    ? [sideAnchorOf(from, "right"), sideAnchorOf(to, "left")]
    : facingAnchorsBetween(from, to);
  const d = loopsBack ? forwardBezierPath(a, b) : horizontalBezierPath(a, b);
  const classes = selected && selectedClassName ? `${className} ${selectedClassName}` : className;

  return (
    <g
      className={classes}
      data-connection-id={id}
      onClick={onSelect}
      onMouseDown={(event) => event.stopPropagation()}
      onContextMenu={onOpenMenu}
    >
      <path className={hitClassName} d={d} />
      <path className={lineClassName} d={d} />
      {label ? (
        <text x={(a.x + b.x) / 2} y={(a.y + b.y) / 2 - 6}>
          {label}
        </text>
      ) : null}
    </g>
  );
}
