import { useMemo, useState } from "react";

import { elementSourceOf } from "@client/canvas/selection";
import { placementId, relationId } from "@client/canvas/gestureIds";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import type { ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import { useDiagramStream } from "@client/diagrams/useDiagramStream";
import { viewReportOf } from "@client/diagrams/viewReport";
import { useViewReport } from "@client/diagrams/useViewReport";
import { AAD_DEFINITION, AAD_RELATION_BY_ENDS } from "./aadDefinition";
import { AAD_SHOW_ARCHIVED_SWITCH, AadActions, AadElementTypes, AadViewId } from "./aadIds";
import { applyDelta, emptyModel, type AadElement, type AadModel } from "./aadModel";

/** What a toolbox drop adds, by the element type dropped. */
const DROPPED_ACTIONS: ReadonlyMap<string, string> = new Map([
  [AadElementTypes.project, AadActions.addProject],
  [AadElementTypes.specification, AadActions.addSpecification],
  [AadElementTypes.agent, AadActions.addAgent],
  [AadElementTypes.location, AadActions.addLocation],
  [AadElementTypes.environment, AadActions.addEnvironment],
]);

function drawn(element: AadElement): DiagramModelElement {
  const payload = element.payload;
  return {
    id: element.id,
    type: element.type,
    x: element.x,
    y: element.y,
    width: payload.width,
    height: payload.height,
    label: payload.name,
    payload: {
      name: payload.name,
      // A location is named by its branch, and its folder is the one line of a list of its own.
      branch: payload.name,
      folderRows: element.type === AadElementTypes.location ? [{ id: "", title: payload.folder, link: payload.folderLink }] : [],
      status: payload.status,
      statusLabel: payload.statusLabel,
      link: payload.link,
      folder: payload.folder,
      branchLink: payload.branchLink,
      folderLink: payload.folderLink,
      kindLabel: payload.kindLabel,
      pinned: payload.pinned,
      collapsed: [...payload.collapsed],
      tasks: payload.tasks.map((row) => ({ id: row.id, title: row.title, status: row.status, updated: row.updated, link: row.link })),
      pullRequests: payload.pullRequests.map((row) => ({ id: row.id, title: row.title, updated: row.updated, link: row.link })),
    },
  };
}

/** The stream's model as the library draws one. Exported for the tests that draw a model directly. */
export function diagramModelOf(model: AadModel): DiagramModel {
  const elements = [...model.elements.values()].map(drawn);
  const connections = [...model.relations.values()].flatMap((relation): DiagramModelConnection[] => {
    const from = model.elements.get(relation.payload.fromElementId);
    const to = model.elements.get(relation.payload.toElementId);
    const type = from === undefined || to === undefined ? undefined : AAD_RELATION_BY_ENDS.get(`${from.type}>${to.type}`);
    // An end that is not drawn, or a pair no relation joins: a line to nothing is worse than none.
    return type === undefined ? [] : [{ id: relation.id, type, sourceId: relation.payload.fromElementId, targetId: relation.payload.toElementId }];
  });
  return { elements, connections, background: { showArchived: model.showArchived } };
}

/**
 * An agent activity diagram: projects in the middle, their specifications around them, then the
 * agents working on each, where each agent works, and the systems that work happens on.
 *
 * The canvas holds no state of the document's: a locked position, a folded group and the archived
 * switch are each drawn from what the stream says and changed by asking the backend, which writes
 * the file. Until the file says otherwise, the drawing stays as it is.
 */
export function AadCanvas({ projectId, entryId, path }: ToolContentProps) {
  const { watchId, executeAction } = useContextConnection();
  const { model, loading, failed, client, moveElementTo } = useDiagramStream(projectId, path, emptyModel, applyDelta);
  const toolboxItems = useToolboxItems(projectId, path);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);
  const diagramModel = useMemo(() => diagramModelOf(model), [model]);

  const runAction = (actionId: string, targetId: string) => {
    void executeAction(actionId, elementSourceOf(targetId, entryId));
  };

  const events: DiagramEventHandlers = {
    onActionInvoked: ({ actionId, targetId }) => {
      if (targetId !== undefined) {
        runAction(actionId, targetId);
      }
    },
    // A drag locks the element where it was let go. The library reports the centre it drew the
    // element at, and that is what the file keeps: a locked position is a centre.
    onElementMoved: ({ elementId, position }) => {
      void moveElementTo(elementId, position.x, position.y);
    },
    onConnectionDrawn: ({ sourceElementId, targetElementId }) => {
      runAction(AadActions.connect, relationId(sourceElementId, targetElementId));
    },
    onElementDropped: ({ elementType, position }) => {
      const action = DROPPED_ACTIONS.get(elementType) ?? (elementType.startsWith("add-") ? elementType : undefined);
      if (action !== undefined) {
        runAction(action, placementId(position.x, position.y));
      }
    },
    // A fold is asked of the backend with the element and the group in one target id.
    onCompartmentToggled: ({ elementId, key, collapsed }) => {
      runAction(collapsed ? AadActions.collapseGroup : AadActions.expandGroup, `${elementId}#${key}`);
    },
    onSwitchToggled: ({ id, on }) => {
      if (id === AAD_SHOW_ARCHIVED_SWITCH) {
        runAction(on ? AadActions.showArchived : AadActions.hideArchived, AadViewId);
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
    <div className="aad-canvas canvas-host" role="application" aria-label="Agent activity diagram">
      <DiagramCanvas
        definition={AAD_DEFINITION}
        model={diagramModel}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        ariaLabel="Agent activity diagram"
        className="aad-surface"
      />
    </div>
  );
}
