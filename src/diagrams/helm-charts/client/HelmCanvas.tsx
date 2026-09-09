import { useMemo, useState } from "react";

import { forwardBezierPath } from "@client/canvas/connectors";
import { elementSelectionOf, selectedElementIdOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type {
  CustomRouteRef,
  DiagramDefinition,
  ShapeBounds,
  ShapePoint,
} from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers, DiagramSelection } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { useRegisterDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useViewReport } from "@client/diagrams/useViewReport";
import { ContextSelectionAction } from "@client/generated/context_pb";
import { HelmEdgeKind, HelmElementKind } from "@client/generated/helm-charts_pb";
import { anchorsOf, edgesOf, nodesOf, type HelmElement } from "./helmModel";
import { useHelmStream } from "./useHelmStream";

/** How far an open edge's stub reaches out of its source, in canvas units. */
const STUB_LENGTH = 48;

export interface HelmCanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}

/** An element as the library carries it here: the model element plus this canvas's closures. */
type NodeElement = DiagramModelElement & {
  node: HelmElement;
  activate: () => void;
  contextSelect: () => void;
};

/** An open edge, drawn as a stub element: a line to nothing and the name that failed. */
type StubElement = DiagramModelElement & {
  from: ShapePoint;
  text: string;
  stubClass: string;
};




/**
 * One relationship, exactly as before: out of the source's right side and into the target's
 * left - the bands run left to right, so the edges read the same way.
 */
const helmRoute: CustomRouteRef = {
  customRoute: "helm-forward-bezier",
  path: (from, to, _waypoints, ends) => {
    const a = ends ? { x: ends.source.x + ends.source.width, y: ends.source.y + ends.source.height / 2 } : from;
    const b = ends ? { x: ends.target.x, y: ends.target.y + ends.target.height / 2 } : to;
    return forwardBezierPath(a, b);
  },
};

/**
 * What this diagram allows, stated once: nodes that drag into the layout block and select,
 * stubs and edges that only render. No relation declares a source anchor and no type is
 * deletable - chart content is created by helm tooling, and this canvas's one edit is the
 * drag.
 */
const HELM_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    {
      id: "node",
      shape: "box",
      classNames: [
        { className: "helm-node" },
        { className: { template: "helm-node-{payload.kindClass}" } },
        { className: "helm-node-unreadable", when: { path: "payload.unreadable", is: "true" } },
        { className: "helm-node-selected", when: { path: "state.selected", is: "true" } },
        { className: "helm-node-box", on: "shape" },
      ],
      labels: [
        {
          text: { path: "payload.name" },
          truncate: true,
          className: "helm-node-label",
        },
      ],
      tooltip: { template: "{payload.title}" },
      data: { kind: { path: "payload.kindClass" } },
      accessibility: { role: "button", focusable: true, label: { path: "payload.title" } },
      actions: [
        // What `onDoubleClick` and `onContextMenu` did on the rendered element.
        { id: "helm.activate", invokedBy: [{ kind: "gesture", gesture: "activate" }], appliesTo: [{ kind: "element" }] },
        { id: "helm.context-menu", invokedBy: [{ kind: "gesture", gesture: "context-menu" }], appliesTo: [{ kind: "element" }] },
      ],
      anchors: { kind: "edge" },
      sizing: "model",
      deletable: false,
    },
    {
      id: "stub",
      // AN ELEMENT THAT IS ONLY AN ORNAMENT: a short rule out of the source with the
      // target's own words beside it, for a dependency this document names and does not
      // resolve. `none` is the shape that made this declarable at all.
      shape: "none",
      classNames: [{ className: { path: "payload.stubClass" }, on: "element" }],
      decorations: [
        {
          glyph: "line",
          from: { x: { path: "bounds.left" }, y: { path: "bounds.centreY" } },
          to: { x: { path: "bounds.right" }, y: { path: "bounds.centreY" } },
          className: "helm-edge-line",
        },
      ],
      labels: [
        {
          text: { path: "payload.text" },
          anchorTo: "top",
          offset: { x: 0, y: 0 },
          align: "start",
          insetX: 8,
          className: "helm-edge-label",
        },
      ],
      data: { "edge-id": { path: "element.id" } },
      anchors: { kind: "edge" },
      sizing: "model",
      draggable: false,
      deletable: false,
    },
  ],
  relationTypes: [
    {
      id: "edge",
      route: helmRoute,
      style: { endMarker: "arrow" },
      label: { placement: "midpoint", offset: -6 },
      className: "helm-edge",
      lineClassName: "helm-edge-line",
      endpoints: {
        source: { elementTypes: ["node"], anchors: [] },
        target: { elementTypes: ["node"], anchors: "edge" },
        allowSelf: false,
      },
    },
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
});

/**
 * Renders a helm chart's anatomy and reports what the user selects - drawn through the
 * central canvas library.
 *
 * It holds no document state and offers one edit exactly: dragging a box stores an authored
 * position in the registration's `layout:` block - a view arrangement, never a chart edit,
 * undoable like everything else (Requirement 6.2). Everything else is navigation: activating
 * a file-backed node reveals it, which is most of the value (Requirement 8).
 */
export function HelmCanvas({ projectId, entryId, path }: HelmCanvasProps) {
  const { model, loading, failed, moveElementTo, reportView } = useHelmStream(projectId, path);

  // This type's palette is empty by design - registered here as well as by the library
  // canvas, because the loading/empty states return before the canvas mounts, and the panel
  // must say "offers nothing" rather than "no diagram is open".
  const toolboxItems = useToolboxItems(projectId, path);
  useRegisterDiagramToolbox(toolboxItems);
  const { select, revealPath } = useContextConnection();
  const { selection } = useContextSelection();

  const [rejection, setRejection] = useState<string | null>(null);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const nodes = useMemo(() => nodesOf(model), [model]);
  const edges = useMemo(() => edgesOf(model), [model]);
  const selectedId = selectedElementIdOf(selection);

  const activate = (element: HelmElement) => {
    const segments = element.payload.chartRelativePath;
    if (segments.length > 0) {
      revealPath([...path.slice(0, -1), ...segments]);
    }
  };

  const diagramModel = useMemo<DiagramModel>(() => {
    const elements: DiagramModelElement[] = [];
    const connections = [];

    // Stubs first, so a node their label happens to cross still draws over it.
    for (const edge of edges) {
      const wire = edge.payload.edge;
      if (!wire) {
        continue;
      }

      const anchors = anchorsOf(model, edge);
      if (!anchors) {
        const source = model.elements.get(wire.sourceId);
        if (!source) {
          continue;
        }

        const from = { x: source.x + source.payload.width, y: source.y + source.payload.height / 2 };
        elements.push({
          id: edge.id,
          type: "stub",
          x: from.x + STUB_LENGTH / 2,
          y: from.y,
          width: STUB_LENGTH,
          height: 12,
          from,
          text: `${wire.label}${wire.kind === HelmEdgeKind.RESOLVES ? " (unvendored)" : " (not defined here)"}`,
          stubClass: `helm-edge helm-edge-${edgeClass(wire.kind)} helm-edge-open`,
          payload: {
            text: `${wire.label}${wire.kind === HelmEdgeKind.RESOLVES ? " (unvendored)" : " (not defined here)"}`,
            stubClass: `helm-edge helm-edge-${edgeClass(wire.kind)} helm-edge-open`,
          },
        } as StubElement);
        continue;
      }

      connections.push({
        id: edge.id,
        type: "edge",
        sourceId: anchors.from.id,
        targetId: anchors.to.id,
        label: wire.label || undefined,
        className: `helm-edge-${edgeClass(wire.kind)}`,
      });
    }

    for (const node of nodes) {
      elements.push({
        id: node.id,
        type: "node",
        x: node.x + node.payload.width / 2,
        y: node.y + node.payload.height / 2,
        width: node.payload.width,
        height: node.payload.height,
        label: node.payload.name,
        // What the declaration reads.
        payload: {
          name: node.payload.name,
          kindClass: kindClass(node.payload.kind),
          unreadable: node.payload.unreadable,
          title: `${kindLabel(node.payload.kind)} ${node.payload.name}`,
        },
        node,
        activate: () => activate(node),
        contextSelect: () => select(elementSelectionOf(entryId, path, node.id, ContextSelectionAction.CONTEXT_MENU)),
      } as NodeElement);
    }

    return { elements, connections };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the closures read stable setters
    // and the same model/path the listed dependencies cover.
  }, [model, nodes, edges, entryId, path]);

  /** The backend's push is the selection; the canvas renders it and never decides. */
  const librarySelection = useMemo<DiagramSelection>(() => {
    if (!selectedId || !model.elements.has(selectedId)) {
      return [];
    }
    return [{ kind: "element", id: selectedId }];
  }, [selectedId, model.elements]);

  const events: DiagramEventHandlers = {
    // The two gestures the rendered element used to answer itself. Same behaviour,
    // reached by action id: this module still decides what "activate" means.
    onActionInvoked: ({ actionId, targetId }) => {
      if (targetId === undefined) {
        return;
      }

      const node = model.elements.get(targetId);
      if (actionId === "helm.activate") {
        if (node !== undefined) {
          activate(node);
        }

        return;
      }

      if (actionId === "helm.context-menu") {
        select(elementSelectionOf(entryId, path, targetId, ContextSelectionAction.CONTEXT_MENU));
      }
    },
    // A background press never deselected here, so only an element selection is forwarded.
    onSelectionChanged: ({ selection: next }) => {
      const element = next.find((item) => item.kind === "element");
      if (element !== undefined && model.elements.has(element.id)) {
        select(elementSelectionOf(entryId, path, element.id));
      }
    },
    onElementMoved: ({ elementId, position }) => {
      const node = model.elements.get(elementId);
      if (!node) {
        return;
      }
      // The authored position, raw: the layout block stores what the author placed, and
      // rounding it here would quietly turn the canvas into a grid.
      void (async () => {
        const error = await moveElementTo(elementId, position.x - node.payload.width / 2, position.y - node.payload.height / 2);
        if (error) {
          setRejection(error);
        }
      })();
    },
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

  if (failed) {
    return <div className="helm-canvas-message">This helm chart diagram could not be opened.</div>;
  }

  if (loading) {
    return <div className="helm-canvas-message">Reading the chart…</div>;
  }

  if (nodes.length === 0) {
    return (
      <div className="helm-canvas-message">
        This folder is not a Helm chart: it has no <code>Chart.yaml</code>, so there is nothing to draw.
      </div>
    );
  }

  return (
    <div className="helm-canvas-frame" role="application" aria-label="Helm chart anatomy">
      {rejection ? (
        <div className="helm-canvas-rejection" role="status" onClick={() => setRejection(null)}>
          {rejection}
        </div>
      ) : null}
      <DiagramCanvas
        definition={HELM_DEFINITION}
        model={diagramModel}
        events={events}
        selection={librarySelection}
        toolboxItems={toolboxItems}
        ariaLabel="Helm chart anatomy"
        className="helm-canvas"
        scrollbarsClassName="helm-scrollbars"
      />
    </div>
  );
}

function kindClass(kind: HelmElementKind): string {
  switch (kind) {
    case HelmElementKind.CHART:
      return "chart";
    case HelmElementKind.VALUES:
      return "values";
    case HelmElementKind.SCHEMA:
      return "schema";
    case HelmElementKind.TEMPLATE:
      return "template";
    case HelmElementKind.CRDS:
      return "crds";
    case HelmElementKind.DEPENDENCY:
      return "dependency";
    case HelmElementKind.SUBCHART:
      return "subchart";
    case HelmElementKind.ARCHIVE:
      return "archive";
    case HelmElementKind.LOCK:
      return "lock";
    default:
      return "unknown";
  }
}

function kindLabel(kind: HelmElementKind): string {
  switch (kind) {
    case HelmElementKind.CHART:
      return "Chart";
    case HelmElementKind.VALUES:
      return "Values";
    case HelmElementKind.SCHEMA:
      return "Values schema";
    case HelmElementKind.TEMPLATE:
      return "Template";
    case HelmElementKind.CRDS:
      return "CRDs";
    case HelmElementKind.DEPENDENCY:
      return "Dependency";
    case HelmElementKind.SUBCHART:
      return "Subchart";
    case HelmElementKind.ARCHIVE:
      return "Archive";
    case HelmElementKind.LOCK:
      return "Lock";
    default:
      return "Element";
  }
}

function edgeClass(kind: HelmEdgeKind): string {
  switch (kind) {
    case HelmEdgeKind.DECLARES:
      return "declares";
    case HelmEdgeKind.RESOLVES:
      return "resolves";
    case HelmEdgeKind.OVERRIDES:
      return "overrides";
    case HelmEdgeKind.CONFIGURES:
      return "configures";
    case HelmEdgeKind.INCLUDES:
      return "includes";
    default:
      return "unknown";
  }
}
