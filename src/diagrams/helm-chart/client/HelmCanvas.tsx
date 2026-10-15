import { useMemo, useState } from "react";

import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useRegisterDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useViewReport } from "@client/diagrams/useViewReport";
import { HelmEdgeKind } from "@client/generated/helm-charts_pb";
import disText from "../definition/helm-chart.dis?raw";
import { HELM_BINDINGS } from "./helmBindings";
import { edgesOf, nodesOf, type HelmElement } from "./helmModel";
import { useHelmStream } from "./useHelmStream";

export interface HelmCanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}

/** An element as the library carries it here: the model element plus this canvas's closures. */
type NodeElement = DiagramModelElement & {
  node: HelmElement;
  activate: () => void;
};

/**
 * What this diagram allows, compiled from the bundled specification the backend runs: nodes that
 * drag into the layout block and select, relations that only render, and an open end drawn as its
 * relation's stub. No relation declares a source anchor and no type is deletable - chart content is
 * created by helm tooling, and this canvas's one edit is the drag. What the library cannot read
 * from the specification is `helmBindings.ts`.
 */
export const HELM_DEFINITION: DiagramDefinition = assertValidDiagramDefinition(compileNotation(parseDisl(disText), HELM_BINDINGS));

/** The operation a double-click runs on a file-backed node (`behavior.operations` of the specification). */
const REVEAL = "openArtifact";

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
  // canvas, because the empty state returns before the canvas mounts, and the panel
  // must say "offers nothing" rather than "no diagram is open".
  const toolboxItems = useToolboxItems(projectId, path);
  useRegisterDiagramToolbox(toolboxItems);
  const { revealPath } = useContextConnection();

  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const nodes = useMemo(() => nodesOf(model), [model]);
  const edges = useMemo(() => edgesOf(model), [model]);

  const activate = (element: HelmElement) => {
    const segments = element.payload.chartRelativePath;
    if (segments.length > 0) {
      revealPath([...path.slice(0, -1), ...segments]);
    }
  };

  const diagramModel = useMemo<DiagramModel>(() => {
    const connections: DiagramModelConnection[] = [];
    for (const edge of edges) {
      const wire = edge.payload.edge;
      if (!wire) {
        continue;
      }

      connections.push({
        id: edge.id,
        type: relationTypeOf(wire.kind),
        sourceId: wire.sourceId,
        // An open end has no target: the library draws it as the relation's stub.
        targetId: wire.openEnd ? "" : wire.targetId,
        label: wire.label || undefined,
        // What the stub's label reads (`self.label`).
        payload: { label: wire.label },
      });
    }

    const elements = nodes.map((node): NodeElement => ({
      id: node.id,
      type: node.type,
      x: node.x + node.payload.width / 2,
      y: node.y + node.payload.height / 2,
      width: node.payload.width,
      height: node.payload.height,
      label: node.payload.name,
      // What the compiled declaration reads.
      payload: { name: node.payload.name, unreadable: node.payload.unreadable },
      node,
      activate: () => activate(node),
    }));

    return { elements, connections };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the closures read stable setters
    // and the same model/path the listed dependencies cover.
  }, [model, nodes, edges, path]);

  const events: DiagramEventHandlers = {
    // The two gestures the rendered element used to answer itself. Same behaviour,
    // reached by action id: this module still decides what "activate" means.
    onActionInvoked: ({ actionId, targetId }) => {
      if (targetId === undefined) {
        return;
      }

      const node = model.elements.get(targetId);
      if (actionId === REVEAL && node !== undefined) {
        activate(node);
      }
    },
    // Selection is the library's (centralized-selection): a node and an edge alike, and a
    // background press clears - the two things this canvas never did. A refused menu action or
    // move is shown on the one line the library draws around every canvas, by the call that got it
    // (client-centralization Requirement 2).
    onElementMoved: ({ elementId, position }) => {
      const node = model.elements.get(elementId);
      if (!node) {
        return;
      }
      // The authored position, raw: the layout block stores what the author placed, and
      // rounding it here would quietly turn the canvas into a grid.
      void moveElementTo(elementId, position.x - node.payload.width / 2, position.y - node.payload.height / 2);
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

  // Opening, reconnecting and unavailable are the library's to say, in the frame around this canvas;
  // the empty explanation is this module's own, and only true once the chart has been read.
  if (!loading && !failed && nodes.length === 0) {
    return (
      <div className="helm-canvas-message">
        This folder is not a Helm chart: it has no <code>Chart.yaml</code>, so there is nothing to draw.
      </div>
    );
  }

  return (
    <div className="helm-canvas-frame" role="application" aria-label="Helm chart anatomy">
      <DiagramCanvas
        definition={HELM_DEFINITION}
        model={diagramModel}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        ariaLabel="Helm chart anatomy"
        className="helm-canvas"
        scrollbarsClassName="helm-scrollbars"
      />
    </div>
  );
}

/** The relation type an edge of the wire is: the specification's relation name in lower case. */
function relationTypeOf(kind: HelmEdgeKind): string {
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
