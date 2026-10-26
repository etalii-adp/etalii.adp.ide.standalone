import type { ReactNode } from "react";

import type { CustomRouteRef, RouteEnds, ShapePoint } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramModelConnection } from "@client/canvas/library/api/diagramModel";
import { widthOf } from "@client/canvas/label/textMetrics";
import { formatAmount, type SupplyChainFlow } from "./supplyChainModel";

/**
 * How a flow is drawn where the notation cannot say it (finding A10, `supply-chain.md`, *Notation DISL
 * cannot state*): its S through the lanes the layout reserved for it, and what rides on it - the goods
 * moving along the band, the arrowhead square to the consumer's side and the volume pill under the
 * product's name. `supplyChainBindings.ts` hands both to `compileNotation`.
 */

/** Where a flow leaves its supplier and reaches its consumer: the middle of the facing sides. */
function flowEndsOf(ends: RouteEnds): { start: ShapePoint; end: ShapePoint } {
  const forward = ends.target.x + ends.target.width / 2 >= ends.source.x + ends.source.width / 2;
  return {
    start: { x: forward ? ends.source.x + ends.source.width : ends.source.x, y: ends.source.y + ends.source.height / 2 },
    end: { x: forward ? ends.target.x : ends.target.x + ends.target.width, y: ends.target.y + ends.target.height / 2 },
  };
}

/** Half a card's width: how far a lane runs straight either side of its slot's middle. */
const LANE_HALF = 96;

/**
 * Where a flow goes: its two ends, and between them, for a flow the layout gave lanes, straight
 * through the middle of each slot it reserved - in at the slot's left, out at its right.
 */
function flowStopsOf(from: ShapePoint, to: ShapePoint, ends: RouteEnds | undefined, lanes: readonly ShapePoint[]): ShapePoint[] {
  const { start, end } = ends !== undefined ? flowEndsOf(ends) : { start: from, end: to };
  const half = ends !== undefined ? ends.source.width / 2 : LANE_HALF;
  return [start, ...lanes.flatMap((lane) => [{ x: lane.x - half, y: lane.y }, { x: lane.x + half, y: lane.y }]), end];
}

/**
 * The flow's curve: a horizontal S from one side's middle to the other's, so goods read left to
 * right the way the layout lays the chain out - and a flow that runs backwards still leaves and
 * arrives square to the side it uses. A long flow bends the same way into and out of each lane the
 * layout reserved for it, so it runs between the cards of the stages it skips instead of through them.
 */
export function flowPath(from: ShapePoint, to: ShapePoint, ends?: RouteEnds, lanes: readonly ShapePoint[] = []): string {
  const stops = flowStopsOf(from, to, ends, lanes);
  const segments = stops.slice(1).map((end, i) => {
    const start = stops[i];
    const direction = end.x >= start.x ? 1 : -1;
    const pull = Math.max(48, Math.abs(end.x - start.x) / 2);
    return `C ${start.x + direction * pull} ${start.y} ${end.x - direction * pull} ${end.y} ${end.x} ${end.y}`;
  });
  return `M ${stops[0].x} ${stops[0].y} ${segments.join(" ")}`;
}

/** Where a flow's name and volume sit: the middle lane of a long flow, the middle of a short one. */
export function flowMiddleOf(start: ShapePoint, end: ShapePoint, lanes: readonly ShapePoint[]): ShapePoint {
  return lanes.length > 0 ? lanes[Math.floor(lanes.length / 2)] : { x: (start.x + end.x) / 2, y: (start.y + end.y) / 2 };
}

/** What a flow connection carries beside the library's own fields, for the adornment to read. */
export interface FlowConnection extends DiagramModelConnection {
  flow: SupplyChainFlow;
}

/** How much broader than a trickle the heaviest band is. */
const MAX_BAND_GROWTH = 15;

/** The band's width for a flow's weight: thin for a trickle, broad for the heaviest in its unit. */
export function bandWidthOf(weight: number): number {
  return 3 + Math.round(Math.max(0, Math.min(1, weight)) * MAX_BAND_GROWTH);
}

/** The volume pill's font size, as `supply-chain.css` sets it; the pill is measured at it. */
const VOLUME_FONT_SIZE = 10;

/**
 * What rides on a flow: the goods moving along it, an arrowhead square to the consumer's side, and
 * the volume in a pill under the product's name. Drawn inside the connection's group, so it
 * dims, lights and selects with the line it describes.
 */
export function flowAdornment(route: { from: ShapePoint; to: ShapePoint; waypoints?: readonly ShapePoint[]; ends?: RouteEnds; highlighted?: boolean }, raw: unknown): ReactNode {
  const connection = raw as FlowConnection;
  const payload = connection.flow.payload;
  const lanes = route.waypoints ?? [];
  const d = flowPath(route.from, route.to, route.ends, lanes);
  const stops = flowStopsOf(route.from, route.to, route.ends, lanes);
  const [start, end] = [stops[0], stops[stops.length - 1]];
  // The arrowhead points the way the last bend arrives, square to the consumer's side.
  const direction = end.x >= stops[stops.length - 2].x ? 1 : -1;
  const head = 7 + bandWidthOf(payload.weight) / 3;
  const mid = flowMiddleOf(start, end, lanes);
  const volume = payload.hasVolume ? `${formatAmount(payload.volume)}${payload.unit ? ` ${payload.unit}` : ""}` : "";
  const pillWidth = Math.max(36, widthOf(volume, VOLUME_FONT_SIZE) + 16);
  // Beneath the band, clear of it however broad it is; the product's name sits as far above it.
  const pillTop = mid.y + bandWidthOf(payload.weight) / 2 + 3;

  return (
    <>
      <path className="supply-chain-flow-goods" d={d} style={{ strokeWidth: Math.max(2, bandWidthOf(payload.weight) * 0.4) }} />
      <path
        className="supply-chain-flow-head"
        d={`M ${end.x} ${end.y} L ${end.x - direction * head} ${end.y - head * 0.7} L ${end.x - direction * head} ${end.y + head * 0.7} Z`}
      />
      {volume !== "" && (
        <g className="supply-chain-flow-volume">
          <rect className="supply-chain-flow-volume-pill" x={mid.x - pillWidth / 2} y={pillTop} width={pillWidth} height={18} rx={9} />
          <text className="supply-chain-flow-volume-text" x={mid.x} y={pillTop + 13} textAnchor="middle">
            {volume}
          </text>
        </g>
      )}
    </>
  );
}

/** The flow's route: the S through its lanes, which DISL's bezier states only in outline. */
export const supplyChainFlowRoute: CustomRouteRef = {
  customRoute: "supply-chain-flow",
  path: (from, to, waypoints, ends) => flowPath(from, to, ends, waypoints),
};
