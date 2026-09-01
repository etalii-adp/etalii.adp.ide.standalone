import { branchAnchorsBetween, horizontalBezierPath, type ConnectorBox } from "../../connectors";

export interface BezierConnectionProps {
  /** The box the connection leaves - the parent, in a tree. */
  from: ConnectorBox;
  /** The box it arrives at - the child. */
  to: ConnectorBox;
  /** Class for the path; all styling lives with the adopting module's stylesheet. */
  className?: string;
}

/**
 * A horizontal cubic bezier between a parent's side and the child's facing side.
 *
 * This is the tree case: the direction is known before the geometry is, so the connector can
 * leave sideways and stay in the corridor between two columns instead of cutting across
 * whatever sits between. The mindmap's branches read this way.
 */
export function BezierConnection({ from, to, className }: BezierConnectionProps) {
  const [start, end] = branchAnchorsBetween(from, to);
  return <path className={className} d={horizontalBezierPath(start, end)} />;
}
