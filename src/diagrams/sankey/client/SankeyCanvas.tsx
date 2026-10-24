import { useEffect, useMemo, useState } from "react";

import { elementSourceOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf } from "@client/diagrams/viewReport";
import { useViewReport } from "@client/diagrams/useViewReport";
import { placementId, relationId } from "@client/canvas/gestureIds";
import disText from "../definition/sankey.dis?raw";
import { SANKEY_BINDINGS } from "./sankeyBindings";
import { DEFAULT_COLOR, FLOW_TYPE, NODE_TYPE, SankeyActions } from "./sankeyIds";
import { applyDelta, columnPlaceOf, emptyModel, type SankeyModel } from "./sankeyModel";

/** A palette word as its theme token, or a `#rrggbb` as itself. */
export function paintOf(word: string, custom: string): string {
  return custom !== "" ? custom : `--color-diagram-sankey-${word !== "" ? word : DEFAULT_COLOR}`;
}

/**
 * What a Sankey diagram draws, and what each gesture on it means: compiled from the bundled
 * specification the backend runs, with what the library cannot read from it in `SANKEY_BINDINGS`.
 */
export const SANKEY_DEFINITION: DiagramDefinition = assertValidDiagramDefinition(compileNotation(parseDisl(disText), SANKEY_BINDINGS));

/** The declared actions this module forwards; anything else the library raises is not ours. */
const FORWARDED_ACTIONS: ReadonlySet<string> = new Set([SankeyActions.increase, SankeyActions.decrease, SankeyActions.rename, SankeyActions.remove]);

/** Where nodes are drawn other than where the model has them: room made during a drag, and a drop awaiting its answer. */
export interface Rearrangement {
  /** How far each node moves down (or up, negative) to make room. */
  shifts: ReadonlyMap<string, number>;
  /** The dropped node, drawn at its slot until the model places it. */
  settled?: { id: string; y: number };
}

/** The library's elements and connections for a model, with any rearrangement a drag has made. */
export function diagramModelOf(model: SankeyModel, rearrangement?: Rearrangement): DiagramModel {
  const elements = [...model.nodes.values()].map((node): DiagramModelElement => {
    const { width, height, name, displayValue, note, side, color, customColor } = node.payload;
    const y = rearrangement?.settled?.id === node.id ? rearrangement.settled.y : node.y + (rearrangement?.shifts.get(node.id) ?? 0);
    return {
      id: node.id,
      type: NODE_TYPE,
      x: node.x,
      y,
      width,
      height,
      label: name,
      payload: {
        name,
        displayValue,
        note,
        side,
        noteSide: note !== "" ? side : "",
        paint: paintOf(color, customColor),
        tooltip: `${name}: ${displayValue}${note !== "" ? ` (${note})` : ""}`,
      },
    };
  });

  const connections = [...model.flows.values()].flatMap((flow): DiagramModelConnection[] => {
    const from = model.nodes.get(flow.payload.fromElementId);
    const to = model.nodes.get(flow.payload.toElementId);
    if (from === undefined || to === undefined) {
      return []; // an end is not held - off screen, or never declared; a band to nothing is worse than none
    }

    return [{
      id: flow.id,
      type: FLOW_TYPE,
      sourceId: from.id,
      targetId: to.id,
      sourceAttachment: { edge: "right", at: flow.payload.sourceAt },
      targetAttachment: { edge: "left", at: flow.payload.targetAt },
      title: `${from.payload.name} → ${to.payload.name}: ${flow.payload.displayValue}`,
      payload: { paint: paintOf(flow.payload.color, flow.payload.customColor), thickness: flow.payload.thickness },
    }];
  });

  return { elements, connections };
}

/**
 * A Sankey diagram: quantities flowing between nodes, each band as thick as what it carries and
 * each bar as tall as what passes through it - money through an income statement, energy through
 * a country, people through their outcomes. Nothing here knows which: the document names the
 * nodes, and the backend lays them out.
 *
 * <b>Dragging a node reorders its column, and only that.</b> While it moves, the nodes it passes
 * step aside to show where it will land; on release the backend moves its entry and lays the
 * column out again, and until that answer arrives the node is drawn in the slot it was given.
 */
export function SankeyCanvas({ projectId, entryId, path }: ToolContentProps) {
  const { watchId, executeAction } = useContextConnection();
  const { model, loading, failed, client, moveElementTo } = useDiagramStream(projectId, path, emptyModel, applyDelta);
  const toolboxItems = useToolboxItems(projectId, path);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);
  const [rearrangement, setRearrangement] = useState<Rearrangement | null>(null);

  // The model's answer replaces whatever the drag drew: it is where everything really is now.
  useEffect(() => setRearrangement(null), [model]);

  const diagram = useMemo(() => diagramModelOf(model, rearrangement ?? undefined), [model, rearrangement]);

  // Every refusal reaches the one line the library draws around every canvas, and clears when sent.
  const runAction = (actionId: string, targetId: string) => {
    void executeAction(actionId, elementSourceOf(targetId, entryId));
  };

  const events: DiagramEventHandlers = {
    onActionInvoked: ({ actionId, targetId }) => {
      if (targetId !== undefined && FORWARDED_ACTIONS.has(actionId)) {
        runAction(actionId, targetId);
      }
    },
    // While a node is dragged, the nodes it passes make room; nothing is written until the release.
    onElementPreviewed: ({ elementId, bounds }) => {
      if (bounds === null) {
        // The gesture is over. A drop has already settled the node; an abandoned drag puts everything back.
        setRearrangement((current) => (current?.settled !== undefined ? current : null));
        return;
      }
      const place = columnPlaceOf(model, elementId, bounds.y + bounds.height / 2);
      if (place !== null) {
        setRearrangement((current) => (current !== null && sameShifts(current.shifts, place.shifts) ? current : { shifts: place.shifts }));
      }
    },
    // The library reports the CENTRE it drew the node at; the backend places by the top-left.
    onElementMoved: ({ elementId, position }) => {
      const node = model.nodes.get(elementId);
      const place = columnPlaceOf(model, elementId, position.y);
      if (node === undefined || place === null) {
        return;
      }
      // Drawn in its slot at once - a hair off its old place when it stays, so the library lets go of the drop.
      setRearrangement({ shifts: place.shifts, settled: { id: elementId, y: place.moved ? place.slotY : node.y + 0.01 } });
      void moveElementTo(elementId, position.x - node.payload.width / 2, position.y - node.payload.height / 2).then((refusal) => {
        if (refusal !== "" || !place.moved) {
          setRearrangement(null);
        }
      });
    },
    onConnectionDrawn: ({ sourceElementId, targetElementId }) => {
      runAction(SankeyActions.connect, relationId(sourceElementId, targetElementId));
    },
    // The backend's toolbox drops its add action; a toolbox derived from the definition drops the bare type.
    onElementDropped: ({ elementType, position }) => {
      if (elementType === SankeyActions.add || elementType === NODE_TYPE) {
        runAction(SankeyActions.add, placementId(position.x, position.y));
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

  return (
    <div className="sankey-canvas canvas-host" role="application" aria-label="Sankey diagram">
      <DiagramCanvas
        definition={SANKEY_DEFINITION}
        model={diagram}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        ariaLabel="Sankey diagram"
        className="sankey-surface"
      />
    </div>
  );
}

function sameShifts(one: ReadonlyMap<string, number>, other: ReadonlyMap<string, number>): boolean {
  return one.size === other.size && [...one].every(([id, shift]) => other.get(id) === shift);
}
