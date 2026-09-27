import { useMemo, useState } from "react";

import { elementSourceOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, EdgeAttachment, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import type { GhgAttachment } from "@client/generated/gartner-hypecycle-graph_pb";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf } from "@client/diagrams/viewReport";
import { useViewReport } from "@client/diagrams/useViewReport";
import {
  GHG_ACTION_IDS,
  GHG_PHASES,
  GHG_PHASE_TOOLTIPS,
  GhgActions,
  GhgElementTypes,
  GhgProperties,
  GhgRelationTypes,
  GhgScale,
  GhgShortcuts,
  formatMonth,
  monthAt,
} from "./ghgIds";
import { applyDelta, emptyModel } from "./ghgModel";

/**
 * What a hype cycle graph is, stated once. Every piece of it is a library declaration: the phased
 * banner, the attachments anywhere along a phase's edge, one influence per direction, the month
 * ruler and the tag filter. The backend states the same rules in `GhgRuleSet` and refuses what the
 * canvas refuses anyway, because a request is never trusted to have come from this canvas.
 */
export const GHG_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    {
      id: GhgElementTypes.trend,
      shape: "arrow-banner",
      classNames: [{ className: "canvas-element ghg-trend", on: "element" }],
      segments: {
        count: { path: "payload.phases" },
        max: GHG_PHASES.length,
        boundaries: "payload.boundaries",
        classNames: GHG_PHASES.map((phase) => `ghg-${phase}`),
        tooltips: GHG_PHASE_TOOLTIPS,
        divider: "chevron",
        draggableBoundaries: true,
      },
      labels: [{ text: { path: "payload.name" }, placement: "before", editable: true, className: "canvas-node-label ghg-label" }],
      // An influence attaches anywhere along a phase's top or bottom edge; no dot is drawn, because
      // the whole edge is the handle.
      anchors: { kind: "along", edges: ["top", "bottom"], regions: "segments", visible: false },
      sizing: "user",
    },
  ],
  relationTypes: [
    {
      id: GhgRelationTypes.influence,
      route: "cubic-bezier",
      style: { endMarker: "arrow" },
      className: "ghg-influence",
      hideWhenAttachmentHidden: true,
      endpoints: {
        source: { elementTypes: [GhgElementTypes.trend] },
        target: { elementTypes: [GhgElementTypes.trend] },
        allowSelf: false,
        cardinality: { perPair: "ordered" },
      },
    },
  ],
  actions: [
    {
      id: GhgActions.rename,
      invokedBy: [{ kind: "shortcut", key: GhgShortcuts.rename }, { kind: "gesture", gesture: "activate" }],
      appliesTo: [{ kind: "element" }],
    },
    { id: GhgActions.remove, invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }] },
    { id: GhgActions.disconnect, invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "connection" }] },
  ],
  // A month is four units wide, so a step of four lands every edge on the start of a month; a row is
  // 56, and the snap rests the trend's top on it, which puts its middle on the row.
  snap: { x: { step: GhgScale.unitsPerMonth, origin: 0 }, y: { step: GhgScale.rowStep } },
  chrome: {
    rulers: [
      {
        orientation: "horizontal",
        edge: "bottom",
        scale: { unit: "month", unitsPerStep: GhgScale.unitsPerMonth, origin: GhgScale.origin },
        ladder: [
          { every: { calendar: "month" }, label: "MMM yyyy" },
          { every: { calendar: "quarter" }, label: "MMM yyyy" },
          { every: { calendar: "year" }, label: "yyyy" },
          { every: { calendar: "decade" }, label: "yyyy" },
        ],
        minSpacingPx: 64,
      },
    ],
  },
  filter: { field: "payload.tags", label: "Filter by tags, e.g. energy and (transport or industry)" },
  layout: { modes: ["manual"] },
  dragging: "enabled",
});

/** The declared actions this module forwards; anything else the library raises is not ours. */
const FORWARDED_ACTIONS: ReadonlySet<string> = new Set(GHG_ACTION_IDS);

/** The wire attachment as the library reads one, or nothing for an end the document could not state. */
function attachmentOf(attachment: GhgAttachment | undefined): EdgeAttachment | undefined {
  return attachment === undefined || (attachment.edge !== "top" && attachment.edge !== "bottom")
    ? undefined
    : { edge: attachment.edge, region: attachment.region, at: attachment.at };
}

/** One end of a connect gesture: the trend id, and where on it the influence attaches. */
function gestureEnd(elementId: string, attachment: EdgeAttachment | undefined): string {
  if (attachment === undefined) {
    return elementId;
  }

  const phase = GHG_PHASES[attachment.region ?? 0] ?? GHG_PHASES[0];
  return `${elementId}@${phase}/${attachment.edge}/${Math.round(attachment.at * 100) / 100}`;
}

/**
 * A Gartner hype cycle graph: trends on a month axis, each an arrow banner in the phases it has
 * reached, joined by influences. Every library event takes the one route that can carry it: a move
 * through the stream's `moveElementTo`, a resize or a boundary drag through `setProperty`, and
 * everything else through a context action on one target id.
 */
export function GhgCanvas({ projectId, entryId, path }: DiagramCanvasProps) {
  const { watchId, executeAction, setProperty } = useContextConnection();
  const { model, loading, failed, client, moveElementTo } = useDiagramStream(projectId, path, emptyModel, applyDelta);
  const toolboxItems = useToolboxItems(projectId, path);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const diagramModel = useMemo<DiagramModel>(() => {
    const elements = [...model.trends.values()].map((trend): DiagramModelElement => ({
      id: trend.id,
      type: GhgElementTypes.trend,
      x: trend.x,
      y: trend.y,
      width: trend.payload.width,
      height: GhgScale.trendHeight,
      label: trend.payload.name,
      payload: {
        name: trend.payload.name,
        phases: trend.payload.phases,
        boundaries: [...trend.payload.boundaries],
        tags: [...trend.payload.tags],
      },
    }));
    const connections = [...model.influences.values()].flatMap((influence): DiagramModelConnection[] =>
      model.trends.has(influence.payload.fromElementId) && model.trends.has(influence.payload.toElementId)
        ? [{
            id: influence.id,
            type: GhgRelationTypes.influence,
            sourceId: influence.payload.fromElementId,
            targetId: influence.payload.toElementId,
            sourceAttachment: attachmentOf(influence.payload.sourceAttachment),
            targetAttachment: attachmentOf(influence.payload.targetAttachment),
          }]
        : [], // an end is not held - off screen; a line to nothing is worse than none
    );
    return { elements, connections };
  }, [model]);

  // Every route below reports its own refusal to the one line the library draws around the canvas.
  const runAction = (actionId: string, targetId: string) => {
    void executeAction(actionId, elementSourceOf(targetId));
  };

  const runProperty = (propertyId: string, value: string, targetId: string) => {
    void setProperty(propertyId, value, elementSourceOf(targetId));
  };

  const events: DiagramEventHandlers = {
    // Rename, delete and disconnect, as the library dispatched them from their declarations.
    onActionInvoked: ({ actionId, targetId }) => {
      if (targetId !== undefined && FORWARDED_ACTIONS.has(actionId)) {
        runAction(actionId, targetId);
      }
    },
    // The library reports the CENTRE it drew the trend at; the backend places by the top-left.
    onElementMoved: ({ elementId, position }) => {
      const trend = model.trends.get(elementId);
      if (trend !== undefined) {
        void moveElementTo(elementId, position.x - trend.payload.width / 2, position.y - GhgScale.trendHeight / 2);
      }
    },
    // A resize is a span: the dragged edge's month is the new start or stop.
    onElementResized: ({ elementId, side, bounds }) => {
      if (side === "left") {
        runProperty(GhgProperties.start, formatMonth(monthAt(bounds.x)), elementId);
      } else if (side === "right") {
        runProperty(GhgProperties.stop, formatMonth(monthAt(bounds.x + bounds.width)), elementId);
      }
    },
    // A dragged boundary is the month its phase now ends at.
    onSegmentBoundaryMoved: ({ elementId, index, x }) => {
      const property = GhgProperties.boundaries[index];
      if (property !== undefined) {
        runProperty(property, formatMonth(monthAt(x)), elementId);
      }
    },
    // The whole gesture in one call: both ends, and where on each the influence attaches.
    onConnectionDrawn: ({ sourceElementId, targetElementId, sourceAttachment, targetAttachment }) => {
      runAction(
        GhgActions.connect,
        `rel:${gestureEnd(sourceElementId, sourceAttachment)}->${gestureEnd(targetElementId, targetAttachment)}`,
      );
    },
    // The backend's toolbox drops its add action; a toolbox derived from the definition drops the type.
    onElementDropped: ({ elementType, position }) => {
      if (elementType === GhgActions.addTrend || elementType === GhgElementTypes.trend) {
        runAction(GhgActions.addTrend, `new:${position.x},${position.y}`);
      }
    },
    onViewChanged: ({ viewport: next }) => setViewport(next),
  };

  useViewReport({
    view: { x: viewport?.x ?? 0, y: viewport?.y ?? 0, w: viewport?.width ?? 0, h: viewport?.height ?? 0 },
    report: viewReportOf(client, projectId, watchId, path),
    convert: () => ({
      minX: viewport?.x ?? 0,
      minY: viewport?.y ?? 0,
      maxX: (viewport?.x ?? 0) + (viewport?.width ?? 0),
      maxY: (viewport?.y ?? 0) + (viewport?.height ?? 0),
    }),
    ready: !loading && !failed && viewport !== null,
  });

  // Opening, reconnecting and unavailable are the library's to say, in the frame around this canvas.
  return (
    <div className="ghg-canvas canvas-host" role="application" aria-label="Gartner hype cycle graph">
      <DiagramCanvas
        definition={GHG_DEFINITION}
        model={diagramModel}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        ariaLabel="Gartner hype cycle graph"
        className="ghg-surface"
      />
    </div>
  );
}
