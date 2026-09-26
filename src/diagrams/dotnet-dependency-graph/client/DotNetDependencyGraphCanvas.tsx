import { useMemo, useState } from "react";

import {
  facingAnchorsBetween,
  forwardBezierPath,
  horizontalBezierPath,
  sideAnchorOf,
  type ConnectorBox,
} from "@client/canvas/connectors";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { CustomRouteRef, DiagramDefinition, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useViewReport } from "@client/diagrams/useViewReport";
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

/** A node's box in connector terms - centre-anchored, which is how the span is positioned. */
const boxOf = (bounds: ShapeBounds): ConnectorBox => ({
  x: bounds.x + bounds.width / 2,
  y: bounds.y + bounds.height / 2,
  width: bounds.width,
  height: bounds.height,
});

/**
 * The connector, curved between facing side anchors.
 *
 * <b>This follows from the edge attachment rather than being a separate choice.</b> Side
 * anchors with straight lines draw a connector that leaves horizontally and then cuts diagonally
 * across the canvas - a half-match that would look worse than either whole. The authored graph
 * pairs the two, and so does this.
 */
const dependencyRoute: CustomRouteRef = {
  customRoute: "dotnet-dependency-bezier",
  path: (from, to, _waypoints, ends) => {
    if (!ends) {
      return horizontalBezierPath(from, to);
    }

    const fromBox = boxOf(ends.source);
    const toBox = boxOf(ends.target);
    // A dependency pointing back the way it came needs the long way round, or the curve
    // doubles back through its own source.
    const loopsBack = ends.target.x < ends.source.x + ends.source.width;
    const [a, b] = loopsBack
      ? [sideAnchorOf(fromBox, "right"), sideAnchorOf(toBox, "left")]
      : facingAnchorsBetween(fromBox, toBox);
    return loopsBack ? forwardBezierPath(a, b) : horizontalBezierPath(a, b);
  },
};

/**
 * What this diagram allows, stated once: boxes that drag into the layout block and select,
 * edges that only render. Nothing is deletable and no relation can be drawn - the graph's
 * shape belongs to the solution, and this canvas's one edit is the drag.
 */
const DOTNET_DEPENDENCY_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    {
      id: "node",
      // The same shared span the renderer wrapped, and the same slot contract underneath: a
      // body rect and a label text carrying this module's classes, which is what its test
      // pins and what makes the two dependency graphs one drawing with different colours.
      shape: "span",
      classNames: [
        { className: "dotnet-dependency-element canvas-element", on: "element" },
        { className: { template: "dotnet-dependency-element-{payload.kindClass}" }, on: "element" },
        // Collapsing LOUDLY: a package the solution's projects disagree about is marked on the
        // element, which Requirement 3.5 asks for as against the silent collapse it forbids.
        { className: "dotnet-dependency-conflict", on: "element", when: { path: "payload.hasVersionConflict", is: "true" } },
        { className: "dotnet-dependency-node canvas-node", on: "shape" },
      ],
      labels: [
        {
          text: { path: "payload.name" },
          placement: "inside",
          truncate: true,
          className: "dotnet-dependency-node-label canvas-node-label",
        },
        {
          // The one thing this graph shows that the authored one has no equivalent of: a
          // project's target frameworks, a package's versions - below the label's centre line.
          text: { path: "payload.subtitle" },
          offset: { x: 0, y: 14 },
          when: { path: "payload.subtitle", is: "non-empty" },
          className: "dotnet-dependency-node-subtitle",
        },
      ],
      tooltip: {
        parts: [
          { template: "Project {payload.name}", when: { path: "payload.kindClass", equals: "project" } },
          { template: "Package {payload.name}", when: { path: "payload.kindClass", equals: "package" } },
          { template: "({payload.subtitle})", when: { path: "payload.hasVersions", is: "true" } },
          { template: "- referenced at more than one version", when: { path: "payload.hasVersionConflict", is: "true" } },
        ],
        join: " ",
      },
      accessibility: { role: "button", focusable: true, label: { path: "payload.title" } },
      actions: [
        // What `onDoubleClick` did on the rendered element. The module still decides what it
        // means. The right-click beside it pushed a context-menu selection by hand - selection
        // glue, now the library's shared menu (centralized-selection Requirement 1.3).
        { id: "dotnet-dependency.activate", invokedBy: [{ kind: "gesture", gesture: "activate" }], appliesTo: [{ kind: "element" }] },
      ],
      anchors: { kind: "edge", edgeSides: "horizontal" },
      sizing: "model",
      deletable: false,
    },
  ],
  relationTypes: [
    {
      id: "project-reference",
      route: dependencyRoute,
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
      route: dependencyRoute,
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

  // Registered here as well as by the library canvas, because the empty state returns before
  // the canvas mounts, and the panel must say "offers nothing" rather than
  // "no diagram is open".
  const toolboxItems = useToolboxItems(projectId, path);
  useRegisterDiagramToolbox(toolboxItems);
  const { revealPath } = useContextConnection();

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

  /** Reveals the project file a node stands for. A package is not a file here, so it reveals nothing. */
  const activate = (element: DependencyElement) => {
    const segments = element.payload.projectRelativePath;
    if (segments.length > 0) {
      revealPath([...path.slice(0, -1), ...segments]);
    }
  };

  const diagramModel = useMemo<DiagramModel>(() => {
    const elements: DiagramModelElement[] = nodes.map((node) => {
      const isPackage = node.payload.kind === DependencyElementKind.PACKAGE;
      const versions = node.payload.versions.join(", ");
      const subtitle = isPackage ? versions : node.payload.targetFrameworks.join(", ");
      const kindClass = isPackage ? "package" : "project";
      const title = isPackage
        ? `Package ${node.payload.name}${versions ? ` (${versions})` : ""}${node.payload.hasVersionConflict ? " - referenced at more than one version" : ""}`
        : `Project ${node.payload.name}`;

      // The payload the DECLARATION reads. Composing it here rather than in a renderer is the
      // point of the migration: what a subtitle is made of is this module's knowledge, and
      // where it is drawn is not.
      const element: DiagramModelElement = {
        id: node.id,
        type: "node",
        x: node.x,
        y: node.y,
        width: NODE_WIDTH,
        height: NODE_HEIGHT,
        label: node.payload.name,
        payload: {
          name: node.payload.name,
          kindClass,
          subtitle,
          hasVersions: isPackage && versions.length > 0,
          hasVersionConflict: node.payload.hasVersionConflict,
          title,
        },
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
    // The model rebuilds when the graph does, which is what the canvas needs.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [model, nodes, edges]);

  const events: DiagramEventHandlers = {
    // Selection is the library's (centralized-selection). A pushed reference used to be drawn as
    // if it named an element, so it highlighted nothing; the library looks it up in the model.
    // A refused menu action or move is shown on the one line the library draws around every
    // canvas, by the call that got it (client-centralization Requirement 2).
    onElementMoved: ({ elementId, position }) => {
      void moveElementTo(elementId, position.x, position.y);
    },
    // The two gestures the rendered element used to answer itself. Same behaviour, reached by
    // action id: the module still decides what "activate" means, and no longer needs a shape
    // to be told that it happened.
    onActionInvoked: ({ actionId, targetId }) => {
      if (targetId === undefined) {
        return;
      }

      if (actionId === "dotnet-dependency.activate") {
        const node = nodes.find((candidate) => candidate.id === targetId);
        if (node !== undefined) {
          activate(node);
        }
        return;
      }
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

  // Opening, reconnecting and unavailable are the library's to say, in the frame around this canvas.
  // Emptiness is judged on the whole graph, never on what the filter left: a solution whose
  // every node was ambient would otherwise report itself as having no projects at all - and only
  // once the solution has been read.
  if (!loading && !failed && everyNode.length === 0) {
    return (
      <div className="dotnet-dependency-canvas-message">
        This solution has no projects ADP could resolve. Any reason is reported in the problems panel.
      </div>
    );
  }

  return (
    <div className="dotnet-dependency-canvas-frame" role="application" aria-label=".NET dependency graph">
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
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        ariaLabel=".NET dependency graph"
        className="dotnet-dependency-canvas"
        scrollbarsClassName="dotnet-dependency-scrollbars"
      />
    </div>
  );
}
