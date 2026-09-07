import { useMemo, useState } from "react";

import { edgePointOf } from "@client/canvas/connectors";
import { BoxElement } from "@client/canvas/elements/box/BoxElement";
import { elementSelectionOf, selectedElementIdOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type {
  CustomShapeRef,
  CustomShapeState,
  DiagramDefinition,
  ShapeBounds,
  ShapePoint,
} from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers, DiagramSelection } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { useViewReport } from "@client/diagrams/useViewReport";
import { ContextSelectionAction } from "@client/generated/context_pb";
import { useRegisterDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { DependencyElementKind } from "@client/generated/dotnet-dependency-graph_pb";
import { useDotNetDependencyGraphStream } from "./useDotNetDependencyGraphStream";
import {
  endsOf,
  nodesOf,
  withoutAmbientPackages,
  type DependencyElement,
} from "./dotnetDependencyGraphModel";

/** How wide and tall a box is drawn. The backend sends no size: every node here is one shape. */
const NODE_WIDTH = 220;
const NODE_HEIGHT = 56;

/** A node as the library hands it back to the shape renderer. */
type NodeElement = DiagramModelElement & {
  node: DependencyElement;
  activate: () => void;
  contextSelect: () => void;
};

function boxEdgePoint(bounds: ShapeBounds, towards: ShapePoint): ShapePoint {
  const centre = { x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 };
  return edgePointOf(
    { x: centre.x, y: centre.y, width: bounds.width, height: bounds.height },
    towards.x - centre.x,
    towards.y - centre.y,
  );
}

/**
 * One box, project or package.
 *
 * The two kinds are told apart by a CSS class rather than an inline style, so every colour
 * stays in the stylesheet (tech.md's centralised-styling rule) - and the requirement that the
 * two be "visually distinguishable" (3.1) is met by the sheet rather than by this file.
 */
const nodeShape: CustomShapeRef = {
  customShape: "dotnet-dependency-node",
  render: (raw, state?: CustomShapeState) => {
    const element = raw as NodeElement;
    const node = element.node;
    const isPackage = node.payload.kind === DependencyElementKind.PACKAGE;
    const kind = isPackage ? "package" : "project";
    const classes = [
      "dotnet-dependency-node",
      `dotnet-dependency-node-${kind}`,
      state?.selected ? "dotnet-dependency-node-selected" : "",
      // Collapsing LOUDLY: a package the solution's projects disagree about is marked on the
      // element, which Requirement 3.5 asks for as against the silent collapse it forbids.
      node.payload.hasVersionConflict ? "dotnet-dependency-node-conflict" : "",
    ]
      .filter(Boolean)
      .join(" ");

    const versions = node.payload.versions.join(", ");
    const subtitle = isPackage ? versions : node.payload.targetFrameworks.join(", ");
    const title = isPackage
      ? `Package ${node.payload.name}${versions ? ` (${versions})` : ""}${node.payload.hasVersionConflict ? " - referenced at more than one version" : ""}`
      : `Project ${node.payload.name}`;

    return (
      <BoxElement
        className={classes}
        data-kind={kind}
        x={element.x - NODE_WIDTH / 2}
        y={element.y - NODE_HEIGHT / 2}
        width={NODE_WIDTH}
        height={NODE_HEIGHT}
        label={node.payload.name}
        boxClassName="dotnet-dependency-node-box"
        labelClassName="dotnet-dependency-node-label"
        role="button"
        tabIndex={0}
        aria-label={title}
        onDoubleClick={element.activate}
        onContextMenu={(event) => {
          event.preventDefault();
          element.contextSelect();
        }}
      >
        <title>{title}</title>
        {subtitle ? (
          <text className="dotnet-dependency-node-subtitle" x={element.x} y={element.y + 16} textAnchor="middle">
            {subtitle}
          </text>
        ) : null}
      </BoxElement>
    );
  },
  edgePoint: boxEdgePoint,
};

/**
 * What this diagram allows, stated once: boxes that drag into the layout block and select,
 * edges that only render. Nothing is deletable and no relation can be drawn - the graph's
 * shape belongs to the solution, and this canvas's one edit is the drag.
 */
const DOTNET_DEPENDENCY_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    { id: "node", shape: nodeShape, anchors: { kind: "edge" }, sizing: "model", deletable: false },
  ],
  relationTypes: [
    {
      id: "project-reference",
      route: "straight",
      style: { endMarker: "arrow" },
      className: "dotnet-dependency-edge dotnet-dependency-edge-project",
      lineClassName: "dotnet-dependency-edge-line",
      endpoints: {
        source: { elementTypes: ["node"], anchors: [] },
        target: { elementTypes: ["node"], anchors: "edge" },
        allowSelf: false,
      },
    },
    {
      id: "package-reference",
      route: "straight",
      style: { endMarker: "arrow" },
      className: "dotnet-dependency-edge dotnet-dependency-edge-package",
      lineClassName: "dotnet-dependency-edge-line",
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
 * Draws a solution's projects, the packages they consume, and every reference between them -
 * through the central canvas library, so that this and the authored `generic/dependencies`
 * graph do not feel like two products.
 *
 * It holds no document state and offers exactly one edit: dragging a box stores an authored
 * position in the registration's `layout:` block - a view arrangement, never a change to the
 * solution, and one undo away like everything else.
 */
export function DotNetDependencyGraphCanvas({ projectId, entryId, path }: DiagramCanvasProps) {
  const { model, loading, failed, moveElementTo, reportView } = useDotNetDependencyGraphStream(projectId, path);

  // Registered here as well as by the library canvas, because the loading and empty states
  // return before the canvas mounts, and the panel must say "offers nothing" rather than
  // "no diagram is open".
  const toolboxItems = useToolboxItems(projectId, path);
  useRegisterDiagramToolbox(toolboxItems);
  const { select, revealPath } = useContextConnection();
  const { selection } = useContextSelection();

  const [rejection, setRejection] = useState<string | null>(null);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  // Ambient packages are hidden by DEFAULT - the scale answer is on unless the reader turns it
  // off, because a graph whose readability depends on the reader finding a control is not
  // readable. The state is the canvas's own: it is a way of looking at the solution, not a fact
  // about it, so it is never written to the registration.
  const [showAmbient, setShowAmbient] = useState(false);

  const everyNode = useMemo(() => nodesOf(model), [model]);
  const { nodes, edges, hidden } = useMemo(
    () => withoutAmbientPackages(model, showAmbient),
    [model, showAmbient],
  );
  const selectedId = selectedElementIdOf(selection);

  /** Reveals the project file a node stands for. A package is not a file here, so it reveals nothing. */
  const activate = (element: DependencyElement) => {
    const segments = element.payload.projectRelativePath;
    if (segments.length > 0) {
      revealPath([...path.slice(0, -1), ...segments]);
    }
  };

  const diagramModel = useMemo<DiagramModel>(() => {
    const elements: DiagramModelElement[] = nodes.map((node) => {
      const element: NodeElement = {
        id: node.id,
        type: "node",
        x: node.x,
        y: node.y,
        width: NODE_WIDTH,
        height: NODE_HEIGHT,
        label: node.payload.name,
        node,
        activate: () => activate(node),
        contextSelect: () => select(elementSelectionOf(entryId, path, node.id, ContextSelectionAction.CONTEXT_MENU)),
      };
      return element;
    });

    const connections = edges
      .map((edge) => {
        const ends = endsOf(model, edge);
        if (!ends) {
          // Both ends must be delivered before a connector can be drawn between them.
          return null;
        }

        return {
          id: edge.id,
          type: edge.payload.kind === DependencyElementKind.PACKAGE_REFERENCE ? "package-reference" : "project-reference",
          sourceId: ends.from.id,
          targetId: ends.to.id,
        };
      })
      .filter((connection) => connection !== null);

    return { elements, connections };
    // `activate` and `contextSelect` close over stable callbacks; the model rebuilds when the
    // graph does, which is what the canvas needs.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [model, nodes, edges, entryId]);

  const librarySelection = useMemo<DiagramSelection>(
    () => (selectedId ? [{ kind: "element", id: selectedId }] : []),
    [selectedId],
  );

  const events: DiagramEventHandlers = {
    onSelectionChanged: ({ selection: next }) => {
      const first = next[0];
      select(first ? elementSelectionOf(entryId, path, first.id) : null);
    },
    onElementMoved: ({ elementId, position }) => {
      void moveElementTo(elementId, position.x, position.y).then((error) => {
        if (error) {
          setRejection(error);
        }
      });
    },
    onViewChanged: ({ viewport: next }) => setViewport(next),
  };

  // The viewport the reader is looking at, reported so the backend can answer with what falls
  // inside it - the same loop every other module runs.
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
    return (
      <div className="dotnet-dependency-canvas-message">
        This diagram could not be opened. Its registration names a solution ADP cannot read.
      </div>
    );
  }

  if (loading) {
    return <div className="dotnet-dependency-canvas-message">Reading the solution…</div>;
  }

  // Emptiness is judged on the whole graph, never on what the filter left: a solution whose
  // every node was ambient would otherwise report itself as having no projects at all.
  if (everyNode.length === 0) {
    return (
      <div className="dotnet-dependency-canvas-message">
        This solution has no projects ADP could resolve. Any reason is reported in the problems panel.
      </div>
    );
  }

  return (
    <div className="dotnet-dependency-canvas-frame" role="application" aria-label=".NET dependency graph">
      {rejection ? (
        <div className="dotnet-dependency-canvas-rejection" role="status" onClick={() => setRejection(null)}>
          {rejection}
        </div>
      ) : null}
      {/*
        Filtering that cannot be seen is just a wrong diagram, so the notice is part of the
        feature rather than a nicety: it says how many were hidden, names them, says on what
        grounds, and puts them back in one click.
      */}
      {hidden.length > 0 ? (
        <div className="dotnet-dependency-canvas-filtered" role="status">
          <span>
            {hidden.length} {hidden.length === 1 ? "package is" : "packages are"} hidden as ambient
            — referenced by so many of this solution&apos;s projects that the edge tells you nothing:{" "}
            {hidden.map((node) => node.payload.name).join(", ")}.
          </span>
          <button type="button" onClick={() => setShowAmbient(true)}>
            Show them
          </button>
        </div>
      ) : null}
      {showAmbient ? (
        <div className="dotnet-dependency-canvas-filtered" role="status">
          <span>Every package is drawn, including the ambient ones.</span>
          <button type="button" onClick={() => setShowAmbient(false)}>
            Hide ambient packages
          </button>
        </div>
      ) : null}
      <DiagramCanvas
        definition={DOTNET_DEPENDENCY_DEFINITION}
        model={diagramModel}
        events={events}
        selection={librarySelection}
        toolboxItems={toolboxItems}
        ariaLabel=".NET dependency graph"
        className="dotnet-dependency-canvas"
        scrollbarsClassName="dotnet-dependency-scrollbars"
      />
    </div>
  );
}
