import { useMemo, useState } from "react";

import {
  facingAnchorsBetween,
  forwardBezierPath,
  horizontalBezierPath,
  sideAnchorOf,
  type ConnectorBox,
} from "@client/canvas/connectors";
import { elementIdOfKey, elementSelectionOf, elementSourceOf } from "@client/canvas/selection";
import { isTextTarget, structuralShortcutFor } from "@client/canvas/interaction";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { CustomRouteRef, DiagramDefinition, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers, DiagramSelection } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { inlineLabelElementIdOf } from "@client/shell/context/inlineLabelPrompt";
import { innermostKey, useContextConnection, useContextPrompt, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { type ContextShortcut } from "@client/generated/context-contract_pb";
import { ContextSelectionAction } from "@client/generated/context_pb";
import { useViewReport } from "@client/diagrams/useViewReport";
import { useDependencyGraphStream } from "./useDependencyGraphStream";
import type { DependencyGraphElement } from "./dependencyGraphModel";

/**
 * The vertical distance between adjacent rows, in the module's own y units.
 *
 * Mirrors `DependencyGraphRows.Height` in the backend, and must stay equal to it: the y this
 * canvas sends with a move is `row × this`, and the backend divides the same constant back out.
 * A mismatch would land every vertical drag on the wrong row.
 */
const ROW_HEIGHT = 60;

/** How tall a node's box is drawn, leaving a gutter between rows. */
const NODE_HEIGHT = 36;

/** How wide a node's box is, in canvas units - the module's own rendering constant. */
const NODE_WIDTH = 160;

/** An element as the library carries it here: the model element plus what it draws. */
type NodeElement = DiagramModelElement & { node: DependencyGraphElement };

/** A corner-based bounds as the centre-based connector geometry wants it. */
function boxOf(bounds: ShapeBounds): ConnectorBox {
  return {
    x: bounds.x + bounds.width / 2,
    y: bounds.y + bounds.height / 2,
    width: bounds.width,
    height: bounds.height,
  };
}

/**
 * The dependency curve, exactly as `InteractiveBezierConnection` drew it: the facing bezier
 * when the dependency sits clear to the right, and the forward loop - out of the dependent's
 * right, back into the dependency's left - when it sits behind. The loop is decided from the
 * endpoint BOUNDS, which the connect preview does not have yet; the preview draws the plain
 * horizontal bezier to the pointer.
 */
const dependencyRoute: CustomRouteRef = {
  customRoute: "dependency-bezier",
  path: (from, to, _waypoints, ends) => {
    if (!ends) {
      return horizontalBezierPath(from, to);
    }

    const fromBox = boxOf(ends.source);
    const toBox = boxOf(ends.target);
    const loopsBack = ends.target.x < ends.source.x + ends.source.width;
    const [a, b] = loopsBack
      ? [sideAnchorOf(fromBox, "right"), sideAnchorOf(toBox, "left")]
      : facingAnchorsBetween(fromBox, toBox);
    return loopsBack ? forwardBezierPath(a, b) : horizontalBezierPath(a, b);
  },
};

/**
 * What a dependency graph allows, stated once: nodes that drag freely on x and land on rows,
 * with the two side anchors a dependency gesture lifts from - which side decides the edge's
 * direction on release - and one directed depends-on relation whose release over empty
 * canvas is the create-and-relate gesture. Labels edit inline on nodes and relations alike.
 */
const DEPENDENCY_GRAPH_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    {
      id: "node",
      // THE SHAPE THIS MODULE USED TO DRAW ITSELF. `span` is the same shared component the
      // custom renderer wrapped - the renderer existed to add three classes and an edge rule,
      // both of which are declarations now.
      shape: "span",
      classNames: [
        { className: "dependency-graph-element canvas-element" },
        { className: "dependency-graph-node canvas-node" },
        { className: "dependency-graph-selected", when: { path: "state.selected", is: "true" } },
        { className: "dependency-graph-connect-target canvas-connect-target", when: { path: "state.connectTarget", is: "true" } },
      ],
      labels: [
        {
          // `label || id`, as the renderer wrote it: the element's own label, falling back to
          // its id when the document names none.
          text: { template: "{element.label}" },
          placement: "inside",
          truncate: true,
          editable: true,
          className: "dependency-graph-label canvas-node-label",
        },
      ],
      anchors: {
        kind: "sides",
        fractions: [
          { side: "left", at: 0.5, name: "left" },
          { side: "right", at: 0.5, name: "right" },
        ],
        // The other half of what `nodeShape.edgePoint` did: a target end attaches on the side
        // facing its source rather than by true edge intersection.
        edgeSides: "horizontal",
      },
      sizing: "model",
    },
  ],
  relationTypes: [
    {
      id: "depends",
      route: dependencyRoute,
      style: { endMarker: "arrow" },
      label: { placement: "midpoint", offset: -6, editable: true },
      className: "dependency-graph-relation",
      lineClassName: "dependency-graph-relation-line",
      hitClassName: "dependency-graph-relation-hit",
      endpoints: {
        source: { elementTypes: ["node"], anchors: ["left", "right"] },
        // `anchors: "edge"` with the type's own horizontal constraint: a connector reaches the
        // side facing its other end, whatever the angle, because this notation reads
        // left-to-right and an edge through the top of a box reads as a different relation.
        target: { elementTypes: ["node"], anchors: "edge" },
        allowSelf: false,
      },
      emptyRelease: "complete",
    },
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
});

/** The nearest row for a module-space y, matching DependencyGraphRows.ToNearestRow's away-from-zero midpoint. */
function nearestRow(y: number): number {
  const exact = y / ROW_HEIGHT;
  return exact >= 0 ? Math.floor(exact + 0.5) : -Math.floor(-exact + 0.5);
}

/** The placement id a gesture carries when it lands on empty canvas: `new:{x},{row}`. */
function newPlacementId(x: number, row: number): string {
  return `new:${x},${row}`;
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
export function DependencyGraphCanvas({ projectId, entryId, path }: DiagramCanvasProps) {
  const { model, loading, failed, moveElementTo, reportView } = useDependencyGraphStream(projectId, path);
  const { select, executeAction, executeShortcut } = useContextConnection();
  const { selection, actions } = useContextSelection();
  const toolboxItems = useToolboxItems(projectId, path);
  const [rejection, setRejection] = useState("");
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const selectionKey = innermostKey(selection);
  const selectedId = elementIdOfKey(selectionKey ?? null);

  const { prompt, onPropose: onProposeLabel, onSubmit: onSubmitLabel, onCancel: onCancelLabel } = useContextPrompt();
  const editingId = inlineLabelElementIdOf(prompt);

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
      type: "depends",
      sourceId: relation.fromElementId,
      targetId: relation.toElementId,
      label: relation.label || undefined,
    }));
    return { elements, connections };
  }, [model]);

  /** The backend's push is the selection; the canvas renders it and never decides. */
  const librarySelection = useMemo<DiagramSelection>(() => {
    if (selectedId === null) {
      return [];
    }
    return [{ kind: model.relations.has(selectedId) ? "connection" : "element", id: selectedId }];
  }, [selectedId, model.relations]);

  const runAction = (actionId: string, sourceId?: string) => {
    void (async () => {
      const outcome = await executeAction(actionId, sourceId ? elementSourceOf(sourceId) : undefined);
      if (!outcome.accepted && outcome.error) {
        setRejection(outcome.error);
      }
    })();
  };

  const runShortcut = (shortcut: ContextShortcut, sourceId: string) => {
    void (async () => {
      const outcome = await executeShortcut(shortcut, elementSourceOf(sourceId));
      if (!outcome.accepted && outcome.error) {
        setRejection(outcome.error);
      }
    })();
  };

  /**
   * The gesture's direction, decided by the anchor it lifted from: from the right, the
   * dragged node depends on the landing; from the left it arrives reversed - the landing
   * depends on the dragged node - because what depends on a node arrives at it.
   */
  const dependencyGesture = (sourceId: string, landing: string, sourceAnchor?: string): string =>
    sourceAnchor === "left" ? `rel:${landing}->${sourceId}` : `rel:${sourceId}->${landing}`;

  const events: DiagramEventHandlers = {
    onSelectionChanged: ({ selection: next }) =>
      select(next.length > 0 ? elementSelectionOf(entryId, path, next[0].id) : null),
    onElementMoved: ({ elementId, position }) => {
      setRejection("");
      // The x is free; the y lands on the nearest row, matching DependencyGraphRows. The
      // conversion subtracts the centre offset first, so the snapped row is the authored one.
      const x = position.x - NODE_WIDTH / 2;
      const row = nearestRow(position.y - NODE_HEIGHT / 2);
      void (async () => {
        const error = await moveElementTo(elementId, x, row * ROW_HEIGHT);
        if (error) {
          setRejection(error);
        }
      })();
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
    onElementDeleted: ({ elementId }) =>
      runShortcut({ key: "Delete", ctrl: false, shift: false, alt: false, meta: false } as ContextShortcut, elementId),
    onConnectionDeleted: ({ connectionId }) =>
      runShortcut({ key: "Delete", ctrl: false, shift: false, alt: false, meta: false } as ContextShortcut, connectionId),
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

  /**
   * Structural keys travel to the backend as data - the backend holds the key-to-action
   * table. Tab and Enter are prevented from their browser defaults when a node is selected,
   * because here they mean "add to the right" and "add below". Delete is the library's own
   * event, handled above.
   */
  const onKeyDown = (event: React.KeyboardEvent) => {
    if (!selectedId || isTextTarget(event.target)) {
      return;
    }

    const shortcut = structuralShortcutFor(event, ["F2", "Insert", "Tab", "Enter"]);
    if (!shortcut) {
      return;
    }

    event.preventDefault();
    runShortcut(shortcut, selectedId);
  };

  if (failed) {
    return (
      <div className="dependency-graph-canvas canvas-host dependency-graph-canvas-message canvas-host-message">
        <p>This dependency graph could not be opened.</p>
      </div>
    );
  }

  return (
    <div
      className="dependency-graph-canvas canvas-host"
      role="application"
      aria-label="Dependency graph"
      onKeyDown={onKeyDown}
    >
      <DiagramCanvas
        definition={DEPENDENCY_GRAPH_DEFINITION}
        model={diagramModel}
        events={events}
        selection={librarySelection}
        toolboxItems={toolboxItems}
        editing={{ editingId, onPropose: onProposeLabel, onSubmit: onSubmitLabel, onCancel: onCancelLabel }}
        context={{
          selectionKey: selectionKey ?? undefined,
          actions,
          selectForMenu: (id) => select(elementSelectionOf(entryId, path, id, ContextSelectionAction.CONTEXT_MENU)),
          executeAction: (actionId) => runAction(actionId, selectedId ?? undefined),
        }}
        ariaLabel="Dependency graph"
        className="dependency-graph-surface"
        scrollbarsClassName="dependency-graph-scrollbars"
      />
      {loading ? <p className="dependency-graph-status canvas-status">Opening…</p> : null}
      {rejection ? <p className="dependency-graph-rejection canvas-rejection">{rejection}</p> : null}
    </div>
  );
}
