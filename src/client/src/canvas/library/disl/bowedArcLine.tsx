import type { ReactNode } from "react";
import { holds, resolveOneAt, type Condition, type FieldBinding, type PartsBinding, type TemplateBinding } from "../definition/binding";
import type { CustomRouteRef, RelationTypeDefinition, RouteEnds, ShapeBounds } from "../definition/diagramDefinition";
import type { DiagramModelConnection } from "../api/diagramModel";
import { HIGHLIGHT_STROKE, highlightedText } from "../highlight";
import { arcBetween, normalAlong, pointAlong, SelfLoopRadius, type ArcBox, type BowedArc } from "./bowedArc";

/**
 * A line drawn as a {@link arcBetween bowed arc}, and what rides along it: bars across it and text
 * marks beside it. DISL's `curved` routing with its `curvature`, its `variants` that bow the other
 * way, its `selfLoop`, its `midMarkers` of type `bar` and its edge labels placed at a fraction.
 *
 * <b>Why the route and its adornment are built together.</b> Both read the same arc: a bar or a
 * mark placed against an arc bowed the other way would sit beside the line rather than on it. So
 * the route and the adornment are made from one declaration and choose the bow by the same rule.
 */
export interface BowedArcDeclaration {
  /** How far the arc bows, as a fraction of its chord; positive bows to the left of travel. */
  bow: number;
  /** The bow where a condition on the connection's payload holds, the first that does. */
  variants?: readonly { when: Condition; bow: number }[];
  /** The radius of the loop a line from a box to itself is drawn as. */
  selfLoopRadius?: number;
  /** Bars across the line. */
  bars?: readonly ArcBarsDeclaration[];
  /** Text beside the line. */
  marks?: readonly ArcMarkDeclaration[];
}

/**
 * Bars drawn across the line at fractions along it, as one group: the conventional delay mark.
 *
 * <b>Square to the arc at its apex, so the bars of one group are parallel.</b> Bars placed close
 * together either side of the middle read as one mark, and two bars each square to the curve at its
 * own point would splay apart.
 */
export interface ArcBarsDeclaration {
  at: readonly number[];
  /** Each bar's length, across the line. */
  length: number;
  /** Drawn only while this holds. */
  when?: Condition;
  /** The group's class, and each bar's. */
  className?: string;
  barClassName?: string;
}

/** Text beside the line at a fraction along it, at a distance to the left or right of travel. */
export interface ArcMarkDeclaration {
  text: FieldBinding | TemplateBinding | PartsBinding;
  at: number;
  side: "left" | "right";
  distance: number;
  className?: string;
}

/** What a declaration reads on a connection: its payload, as a condition or a text names it. */
function rootOf(connection: DiagramModelConnection | undefined): unknown {
  return { payload: connection?.payload };
}

function toBox(bounds: ShapeBounds): ArcBox {
  return { x: bounds.x, y: bounds.y, width: bounds.width, height: bounds.height };
}

/** The bow this connection is drawn with: the first variant whose condition holds, else the declaration's own. */
function bowOf(declaration: BowedArcDeclaration, connection: DiagramModelConnection | undefined): number {
  const root = rootOf(connection);
  return declaration.variants?.find((variant) => holds(variant.when, root))?.bow ?? declaration.bow;
}

function arcOf(declaration: BowedArcDeclaration, ends: RouteEnds, connection: DiagramModelConnection | undefined): BowedArc {
  return arcBetween(toBox(ends.source), toBox(ends.target), bowOf(declaration, connection), declaration.selfLoopRadius ?? SelfLoopRadius);
}

/**
 * The route: the arc between the two end boxes. Without them - the connect gesture's preview, which
 * has no target box yet - a straight line from the gesture's start to the pointer.
 */
export function bowedArcRoute(declaration: BowedArcDeclaration): CustomRouteRef {
  return {
    customRoute: "bowed-arc",
    path: (from, to, _waypoints, ends, connection) =>
      ends !== undefined ? arcOf(declaration, ends, connection).path : `M ${from.x} ${from.y} L ${to.x} ${to.y}`,
  };
}

/**
 * The adornment: each group of bars while its condition holds, then each mark that has text. Drawn in
 * the connection's group, and painted in the selected colour with the line while it is highlighted.
 */
export function bowedArcAdornment(declaration: BowedArcDeclaration): NonNullable<RelationTypeDefinition["adorn"]> {
  return (route, rawConnection): ReactNode => {
    if (route.ends === undefined) {
      return null;
    }

    const connection = rawConnection as DiagramModelConnection;
    const root = rootOf(connection);
    const arc = arcOf(declaration, route.ends, connection);
    const highlighted = route.highlighted === true;

    return (
      <>
        {(declaration.bars ?? []).map((bars, index) =>
          holds(bars.when, root) ? (
            <g key={`bars-${index}`} className={bars.className} style={highlighted ? { stroke: HIGHLIGHT_STROKE } : undefined}>
              {bars.at.map((t, bar) => {
                const at = pointAlong(arc, t);
                const half = bars.length / 2;
                return (
                  <line
                    key={bar}
                    className={bars.barClassName}
                    x1={at.x - arc.apexNormal.x * half}
                    y1={at.y - arc.apexNormal.y * half}
                    x2={at.x + arc.apexNormal.x * half}
                    y2={at.y + arc.apexNormal.y * half}
                  />
                );
              })}
            </g>
          ) : null,
        )}
        {(declaration.marks ?? []).map((mark, index) => {
          const text = resolveOneAt(mark.text, root);
          if (text === null || text === "") {
            return null;
          }

          const at = pointAlong(arc, mark.at);
          const normal = normalAlong(arc, mark.at);
          const distance = mark.side === "left" ? mark.distance : -mark.distance;
          return (
            <text key={`mark-${index}`} className={mark.className} style={highlightedText(highlighted)} x={at.x + normal.x * distance} y={at.y + normal.y * distance} textAnchor="middle">
              {text}
            </text>
          );
        })}
      </>
    );
  };
}
