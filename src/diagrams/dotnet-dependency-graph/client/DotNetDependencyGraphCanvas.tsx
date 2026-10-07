import { useMemo, useState } from "react";

import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useViewReport } from "@client/diagrams/useViewReport";
import { useRegisterDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import { DependencyElementKind } from "@client/generated/dotnet-dependency-graph_pb";
import disText from "../definition/dotnet-dependency-graph.dis?raw";
import { DOTNET_BINDINGS } from "./dotnetBindings";
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

const SPEC = parseDisl(disText);

/** The action a box's activate gesture invokes: the definition's `revealProjectFile`, by its `x-dotnet` wire id. */
const ACTIVATE = (SPEC["x-dotnet"] as { actions: { revealProjectFile: string } }).actions.revealProjectFile;

/**
 * What this diagram allows, compiled from the bundled DISL specification
 * (`definition/dotnet-dependency-graph.dis`) with what the library cannot read from it in
 * `dotnetBindings.ts`: boxes that drag into the layout block and select, edges that only render.
 * Nothing is deletable and no relation can be drawn - the graph's shape belongs to the solution,
 * and this canvas's one edit is the drag.
 */
export const DOTNET_DEPENDENCY_DEFINITION: DiagramDefinition = assertValidDiagramDefinition(compileNotation(SPEC, DOTNET_BINDINGS));

/**
 * Draws a solution's projects, the packages they consume, and every reference between them -
 * through the central canvas library, so that this and the authored `generic/dependencies`
 * graph do not feel like two products.
 *
 * It holds no document state and offers exactly one edit: dragging a box stores an authored
 * position in the registration's `layout:` block - a view arrangement, never a change to the
 * solution, and one undo away like everything else.
 */
export function DotNetDependencyGraphCanvas({ projectId, entryId, path }: ToolContentProps) {
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

      // The payload the DEFINITION reads, composed here because the library has no CEL: the
      // second line is the specification's `self.versions.join(', ')` or
      // `self.targetFrameworks.join(', ')`, and the title its `title(self)` (`dotnetBindings.ts`).
      const element: DiagramModelElement = {
        id: node.id,
        type: isPackage ? "package" : "project",
        x: node.x,
        y: node.y,
        width: NODE_WIDTH,
        height: NODE_HEIGHT,
        label: node.payload.name,
        payload: {
          name: node.payload.name,
          subtitle: isPackage ? versions : node.payload.targetFrameworks.join(", "),
          hasVersionConflict: node.payload.hasVersionConflict,
          title: isPackage
            ? `Package ${node.payload.name}${versions ? ` (${versions})` : ""}${node.payload.hasVersionConflict ? " - referenced at more than one version" : ""}`
            : `Project ${node.payload.name}`,
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

      if (actionId === ACTIVATE) {
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
