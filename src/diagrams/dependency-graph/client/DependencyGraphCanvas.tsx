import { useMemo, useState } from "react";

import { elementSourceOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import { useViewReport } from "@client/diagrams/useViewReport";
import { useDependencyGraphStream } from "./useDependencyGraphStream";
import type { DependencyGraphElement } from "./dependencyGraphModel";
import { placementId, relationId } from "@client/canvas/gestureIds";
import disText from "../definition/dependency-graph.dis?raw";
import { DEPENDENCY_GRAPH_BINDINGS, ROW_HEIGHT, nearestRow } from "./dependencyGraphBindings";

export { nearestRow };

/** How tall a node's box is drawn, leaving a gutter between rows. */
const NODE_HEIGHT = 36;

/** How wide a node's box is, in canvas units - the module's own rendering constant. */
const NODE_WIDTH = 160;

/** An element as the library carries it here: the model element plus what it draws. */
type NodeElement = DiagramModelElement & { node: DependencyGraphElement };

/**
 * What a dependency graph allows, compiled from the bundled DISL specification
 * (`definition/dependency-graph.dis`) with what the library cannot read from it in
 * `dependencyGraphBindings.ts`: nodes that drag freely on x and land on rows, with the two side anchors
 * a dependency gesture lifts from - which side decides the edge's direction on release - and one
 * directed depends-on relation whose release over empty canvas is the create-and-relate gesture. Labels
 * edit inline on nodes and relations alike, and a right-click on empty canvas offers "Add node here".
 */
export const DEPENDENCY_GRAPH_DEFINITION: DiagramDefinition = assertValidDiagramDefinition(compileNotation(parseDisl(disText), DEPENDENCY_GRAPH_BINDINGS));

/** The relation type the compiled definition draws a dependency as. */
const DEPENDENCY = DEPENDENCY_GRAPH_DEFINITION.relationTypes[0]!.id;

/** The placement id a gesture carries when it lands on empty canvas: `new:{x},{row}`. */
function newPlacementId(x: number, row: number): string {
  return placementId(x, row);
}

/**
 * The dependency graph: named nodes on rows, joined by directed depends-on edges, at the
 * coordinates their author gave them - drawn through the central canvas library.
 *
 * Zoom and pan are the library's viewBox geometry and never reach the backend: what crosses
 * the wire is always a point in the module's own coordinate space, and the y a move sends is
 * snapped to `row × ROW_HEIGHT` in the mapping's coordinate conversion. The backend's
 * span-viewport culling is untouched: this canvas reports what it can see and draws what it
 * is sent, exactly as before.
 */
export function DependencyGraphCanvas({ projectId, entryId, path }: ToolContentProps) {
  const { model, loading, failed, moveElementTo, reportView } = useDependencyGraphStream(projectId, path);
  const { executeAction } = useContextConnection();
  const toolboxItems = useToolboxItems(projectId, path);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const diagramModel = useMemo<DiagramModel>(() => {
    const elements = [...model.elements.values()].map((node): NodeElement => ({
      id: node.id,
      type: "node",
      x: node.x + NODE_WIDTH / 2,
      y: node.y + NODE_HEIGHT / 2,
      width: NODE_WIDTH,
      height: NODE_HEIGHT,
      label: node.label || node.id,
      node,
    }));
    const connections = [...model.relations.values()].map((relation) => ({
      id: relation.id,
      type: DEPENDENCY,
      sourceId: relation.fromElementId,
      targetId: relation.toElementId,
      label: relation.label || undefined,
    }));
    return { elements, connections };
  }, [model]);

  // A refusal needs nothing here: the call reports it to the library's refusal line.
  const runAction = (actionId: string, sourceId?: string) => {
    void executeAction(actionId, sourceId ? elementSourceOf(sourceId, entryId) : undefined);
  };

  /**
   * The gesture's direction, decided by the anchor it lifted from: from the right, the
   * dragged node depends on the landing; from the left it arrives reversed - the landing
   * depends on the dragged node - because what depends on a node arrives at it.
   */
  const dependencyGesture = (sourceId: string, landing: string, sourceAnchor?: string): string =>
    sourceAnchor === "left" ? relationId(landing, sourceId) : relationId(sourceId, landing);

  // Selection is the library's (centralized-selection), and so are sending a declared action and
  // showing a refusal: every call below reports its own to the library's one refusal line.
  const events: DiagramEventHandlers = {
    onElementMoved: ({ elementId, position }) => {
      // The x is free; the y lands on the nearest row, matching DependencyGraphRows. The
      // conversion subtracts the centre offset first, so the snapped row is the authored one.
      const x = position.x - NODE_WIDTH / 2;
      const row = nearestRow(position.y - NODE_HEIGHT / 2);
      void moveElementTo(elementId, x, row * ROW_HEIGHT);
    },
    // The whole gesture in one call - source and landing together in a rel: id. Deliberately
    // stateless: the two-call protocol this replaces kept an armed source in the backend
    // between calls, and a stale arm related the wrong pair.
    onConnectionDrawn: ({ sourceElementId, targetElementId, sourceAnchor }) =>
      runAction("dependencies.connect", dependencyGesture(sourceElementId, targetElementId, sourceAnchor)),
    // Released over empty canvas: the create-and-relate gesture - the landing is a placement.
    onConnectionReleasedOnEmpty: ({ sourceElementId, sourceAnchor, position }) =>
      runAction(
        "dependencies.connect",
        dependencyGesture(sourceElementId, newPlacementId(position.x, nearestRow(position.y)), sourceAnchor),
      ),
    // A toolbox drop names a placement: the coordinate and row under the pointer.
    onElementDropped: ({ elementType, position }) =>
      runAction(elementType, newPlacementId(position.x, nearestRow(position.y))),
    onViewChanged: ({ viewport: next }) => setViewport(next),
  };

  useViewReport({
    view: { x: viewport?.x ?? 0, y: viewport?.y ?? 0, w: viewport?.width ?? 0, h: viewport?.height ?? 0 },
    report: reportView,
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
    <div
      className="dependency-graph-canvas canvas-host"
      role="application"
      aria-label="Dependency graph"
    >
      <DiagramCanvas
        definition={DEPENDENCY_GRAPH_DEFINITION}
        model={diagramModel}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        ariaLabel="Dependency graph"
        className="dependency-graph-surface"
        scrollbarsClassName="dependency-graph-scrollbars"
      />
    </div>
  );
}
