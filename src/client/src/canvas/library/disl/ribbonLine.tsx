import type { ReactNode } from "react";
import { resolveOneAt, resolveNumberAt, type FieldBinding } from "../definition/binding";
import type { CustomRouteRef, RelationTypeDefinition } from "../definition/diagramDefinition";
import type { DiagramModelConnection } from "../api/diagramModel";
import { HIGHLIGHT_STROKE } from "../highlight";

/**
 * A line drawn as a ribbon: a filled band along its route, as thick as a number each connection
 * carries, MEASURED VERTICALLY - its top and bottom edges are the route itself moved up and down by
 * half the thickness, so it keeps that height at every x however steeply it climbs.
 *
 * <b>Filled rather than stroked, because a stroke is thick across the curve, not down it.</b> A
 * stroked curve is as wide as its value only where it runs level; on a slope it narrows, so a band
 * looks pinched where two cross, and its ends no longer meet the bands stacked beside it at a box's
 * edge. Two parallel curves keep the height everywhere, which is how ribbons stack edge to edge.
 *
 * The route stays the connection's line, which hit-testing and the connect preview read; the ribbon
 * is drawn over it, inside the connection's group, so a press on the band selects the connection.
 */
export interface RibbonDeclaration {
  /** The band's height, read from the connection. */
  thickness: FieldBinding;
  /** Its paint, read from the connection: a custom property (`--name`) or a colour as written. */
  fill?: FieldBinding;
  /** Its class, which the module's stylesheet dresses (its opacity, a hover). */
  className?: string;
  /** The width of the outline drawn in the selection colour while the connection is highlighted. */
  highlightWidth?: number;
}

interface Point {
  x: number;
  y: number;
}

/** One piece of a route: a straight run or a cubic curve, to its end. */
type Segment = { kind: "L"; end: Point } | { kind: "C"; c1: Point; c2: Point; end: Point };

/** The route's start and its pieces, read from an absolute path of `M`, `L` and `C`; anything else is refused. */
function segmentsOf(path: string): { start: Point; segments: Segment[] } {
  const tokens = path.trim().split(/[\s,]+/);
  const numbers = (at: number, count: number): number[] => tokens.slice(at, at + count).map(Number);
  let start: Point | undefined;
  const segments: Segment[] = [];
  for (let at = 0; at < tokens.length;) {
    const command = tokens[at]!;
    if (command === "M" && start === undefined) {
      const [x, y] = numbers(at + 1, 2);
      start = { x: x!, y: y! };
      at += 3;
    } else if (command === "L" && start !== undefined) {
      const [x, y] = numbers(at + 1, 2);
      segments.push({ kind: "L", end: { x: x!, y: y! } });
      at += 3;
    } else if (command === "C" && start !== undefined) {
      const [x1, y1, x2, y2, x, y] = numbers(at + 1, 6);
      segments.push({ kind: "C", c1: { x: x1!, y: y1! }, c2: { x: x2!, y: y2! }, end: { x: x!, y: y! } });
      at += 7;
    } else {
      throw new Error(`ribbonPath: the route "${path}" has "${command}" where a ribbon reads one M and then L or C, absolute.`);
    }
  }

  if (start === undefined) {
    throw new Error(`ribbonPath: the route "${path}" has no start.`);
  }
  return { start, segments };
}

/** The band around a route: along it `thickness / 2` higher, and back along it as much lower. */
export function ribbonPath(centre: string, thickness: number): string {
  const half = thickness / 2;
  const { start, segments } = segmentsOf(centre);
  const at = (point: Point, dy: number) => `${point.x} ${point.y + dy}`;
  const top = segments.map((segment) => (segment.kind === "L" ? `L ${at(segment.end, -half)}` : `C ${at(segment.c1, -half)} ${at(segment.c2, -half)} ${at(segment.end, -half)}`));
  const last = segments.length > 0 ? segments[segments.length - 1]!.end : start;
  // Back along the same pieces in reverse: each ends where the one before it began, its controls swapped.
  const bottom = segments.map((segment, index) => {
    const from = index === 0 ? start : segments[index - 1]!.end;
    return segment.kind === "L" ? `L ${at(from, half)}` : `C ${at(segment.c2, half)} ${at(segment.c1, half)} ${at(from, half)}`;
  }).reverse();
  return [`M ${at(start, -half)}`, ...top, `L ${at(last, half)}`, ...bottom, "Z"].join(" ");
}

/** A custom property as CSS can use it inline; a colour as itself. */
function paintOf(value: string): string {
  return value.startsWith("--") ? `var(${value})` : value;
}

/** The ribbon drawn along a connection's route, for a relation type's `adorn`. */
export function ribbonAdornment(declaration: RibbonDeclaration, route: CustomRouteRef): NonNullable<RelationTypeDefinition["adorn"]> {
  return (drawn, rawConnection): ReactNode => {
    const connection = rawConnection as DiagramModelConnection;
    const root = { payload: connection.payload };
    const thickness = resolveNumberAt(declaration.thickness, root);
    if (thickness === null) {
      return null;
    }

    const fill = declaration.fill === undefined ? null : resolveOneAt(declaration.fill, root);
    const centre = route.path(drawn.from, drawn.to, drawn.waypoints, drawn.ends, connection);
    return (
      <path
        className={declaration.className}
        d={ribbonPath(centre, thickness)}
        style={{
          ...(fill !== null && fill !== "" ? { fill: paintOf(fill) } : {}),
          ...(drawn.highlighted === true ? { stroke: HIGHLIGHT_STROKE, strokeWidth: declaration.highlightWidth ?? 2 } : {}),
        }}
      />
    );
  };
}
