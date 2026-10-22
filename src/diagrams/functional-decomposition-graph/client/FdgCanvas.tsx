import { useMemo, useState } from "react";

import { elementSourceOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf } from "@client/diagrams/viewReport";
import { useViewReport } from "@client/diagrams/useViewReport";
import disText from "../definition/functional-decomposition-graph.dis?raw";
import { FDG_BINDINGS } from "./fdgBindings";
import {
  FDG_ACTION_IDS,
  FDG_ADD_ACTION_PREFIX,
  FDG_ELEMENT_TYPES,
  FdgActions,
  FdgElementTypes,
  FdgProperties,
  type FdgElementType,
  type FdgRelationType,
} from "./fdgIds";
import { applyDelta, emptyModel } from "./fdgModel";
import { placementId, relationId } from "@client/canvas/gestureIds";

/**
 * What a functional decomposition graph allows and draws, compiled from its bundled specification
 * (`definition/functional-decomposition-graph.dis`), the one the backend runs, with what the library cannot
 * read from it in {@link FDG_BINDINGS}. The canvas refuses under the pointer what the backend would refuse
 * on write, and the backend refuses it anyway, because a request is never trusted to have come from this
 * canvas.
 */
export const FDG_DEFINITION: DiagramDefinition = assertValidDiagramDefinition(compileNotation(parseDisl(disText), FDG_BINDINGS));

/** The declared actions this module forwards; anything else the library raises is not ours. */
const FORWARDED_ACTIONS: ReadonlySet<string> = new Set(FDG_ACTION_IDS);

function isElementType(value: string): value is FdgElementType {
  return (FDG_ELEMENT_TYPES as readonly string[]).includes(value);
}

/**
 * A functional decomposition graph: screens, actions, data and functions, each owned by one parent,
 * drawn through the shared canvas library. Every library event takes the one route that can carry
 * it (the user's chat ruling of 2026-09-25): a move through the stream's `moveElementTo`, a resize
 * through `setProperty`, and everything else through a context action on one target id.
 */
export function FdgCanvas({ projectId, entryId, path }: ToolContentProps) {
  const { watchId, executeAction, setProperty } = useContextConnection();
  const { model, loading, failed, client, moveElementTo } = useDiagramStream(projectId, path, emptyModel, applyDelta);
  const toolboxItems = useToolboxItems(projectId, path);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const diagramModel = useMemo<DiagramModel>(() => {
    const elements = [...model.elements.values()].map((element): DiagramModelElement => ({
      id: element.id,
      type: element.type,
      x: element.x,
      y: element.y,
      width: element.payload.width,
      height: element.payload.height,
      label: element.type === FdgElementTypes.comment ? element.payload.text : element.payload.name,
      payload: { name: element.payload.name, text: element.payload.text },
    }));
    const connections = [...model.connections.values()].flatMap((connection): DiagramModelConnection[] =>
      model.elements.has(connection.payload.fromElementId) && model.elements.has(connection.payload.toElementId)
        ? [{
            id: connection.id,
            type: connection.type,
            sourceId: connection.payload.fromElementId,
            targetId: connection.payload.toElementId,
            label: connection.payload.name || undefined,
          }]
        : [], // an end is not held - off screen, or never declared; a line to nothing is worse than none
    );
    return { elements, connections };
  }, [model]);

  // Every route below - an action, a property, a move - reports its own refusal to the one line the
  // library draws around every canvas, and clears it when sent (client-centralization Requirement 2).
  const runAction = (actionId: string, targetId: string) => {
    void executeAction(actionId, elementSourceOf(targetId, entryId));
  };

  const runProperty = async (propertyId: string, value: number, targetId: string): Promise<boolean> => {
    const outcome = await setProperty(propertyId, String(Math.round(value)), elementSourceOf(targetId, entryId));
    return outcome.accepted;
  };

  // A move takes the stream's one `moveElementTo` (client-centralization task 8). The position passed
  // is the TOP-LEFT, which is what the document holds; a position is what makes this an arrangement
  // rather than a re-parenting. It answers with the backend's refusal, or "" when the move was recorded.
  const moveTo = (elementId: string, left: number, top: number): Promise<string> => moveElementTo(elementId, left, top);

  const events: DiagramEventHandlers = {
    // Rename, delete and disconnect, as the library dispatched them from their declarations.
    onActionInvoked: ({ actionId, targetId }) => {
      if (targetId !== undefined && FORWARDED_ACTIONS.has(actionId)) {
        runAction(actionId, targetId);
      }
    },
    // The library reports the CENTRE it drew the element at; the document holds the top-left.
    onElementMoved: ({ elementId, position }) => {
      const element = model.elements.get(elementId);
      if (element === undefined) {
        return;
      }
      // Returned, so a refusal lets the library put the element back where it was (ruled 2026-10-19).
      return moveTo(elementId, position.x - element.payload.width / 2, position.y - element.payload.height / 2);
    },
    // A resize is a size. Dragging the left or top edge also moves the top-left, because the far
    // edge stays put - and a property carries one value, so that edge is a size and then a move.
    onElementResized: ({ elementId, side, bounds }) => {
      const horizontal = side === "left" || side === "right";
      void (async () => {
        const sized = await runProperty(
          horizontal ? FdgProperties.width : FdgProperties.height,
          horizontal ? bounds.width : bounds.height,
          elementId,
        );
        if (sized && (side === "left" || side === "top")) {
          await moveTo(elementId, bounds.x, bounds.y);
        }
      })();
    },
    // The whole gesture in one stateless call: the relation in the action id, both ends in the target.
    onConnectionDrawn: ({ relationType, sourceElementId, targetElementId }) => {
      runAction(FdgActions.connect(relationType as FdgRelationType), relationId(sourceElementId, targetElementId));
    },
    // A toolbox drop carries the element type; the placement is the drop's centre.
    // The backend's toolbox drops its add action (`fdg.add.<type>`), as every module's does; a toolbox
    // the library derived from the definition drops the bare type. Both mean the same add.
    onElementDropped: ({ elementType, position }) => {
      const type = elementType.startsWith(FDG_ADD_ACTION_PREFIX) ? elementType.slice(FDG_ADD_ACTION_PREFIX.length) : elementType;
      if (!isElementType(type)) {
        return;
      }
      runAction(FdgActions.add(type), placementId(position.x, position.y));
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
    <div className="fdg-canvas canvas-host" role="application" aria-label="Functional decomposition graph">
      <DiagramCanvas
        definition={FDG_DEFINITION}
        model={diagramModel}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        ariaLabel="Functional decomposition graph"
        className="fdg-surface"
      />
    </div>
  );
}
