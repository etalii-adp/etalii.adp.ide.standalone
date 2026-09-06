import { useMemo, useState } from "react";

import { BoxElement } from "@client/canvas/elements/box/BoxElement";
import { FrameElement } from "@client/canvas/elements/frame/FrameElement";
import { edgePointOf } from "@client/canvas/connectors";
import { elementIdOfKey, elementSelectionOf } from "@client/canvas/selection";
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
import { innermostKey, useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { useViewReport } from "@client/diagrams/useViewReport";
import { ContextSelectionAction } from "@client/generated/context_pb";
import { useSparqlStream } from "./useSparqlStream";
import type { SparqlAnnotation, SparqlNode, SparqlRegion } from "./sparqlModel";

/** A node's drawn size, in the module's own canvas units - matching the backend layout's spacing. */
export const NODE_WIDTH = 170;
export const NODE_HEIGHT = 64;

/** An annotation badge's nominal bounds - a text line the library only needs to hit-test. */
const ANNOTATION_WIDTH = 120;
const ANNOTATION_HEIGHT = 14;

/** An element as the library carries it here: the model element plus what it draws. */
type NodeElement = DiagramModelElement & { node: SparqlNode };
type RegionElement = DiagramModelElement & { region: SparqlRegion };
type AnnotationElement = DiagramModelElement & { annotation: SparqlAnnotation };

function boxEdgePoint(bounds: ShapeBounds, towards: ShapePoint): ShapePoint {
  const centre = { x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 };
  return edgePointOf(
    { x: centre.x, y: centre.y, width: bounds.width, height: bounds.height },
    towards.x - centre.x,
    towards.y - centre.y,
  );
}

/** A pattern node: the box with its kind class, the projection mark, and the type annotation. */
const nodeShape: CustomShapeRef = {
  customShape: "sparql-node",
  render: (raw, state?: CustomShapeState) => {
    const element = raw as NodeElement;
    const node = element.node;
    const classes = ["sparql-node canvas-element", `sparql-node-${node.kind}`];
    if (node.projected) {
      classes.push("sparql-node-projected");
    }

    if (state?.selected) {
      classes.push("sparql-selected");
    }

    return (
      <BoxElement
        className={classes.join(" ")}
        x={element.x - NODE_WIDTH / 2}
        y={element.y - NODE_HEIGHT / 2}
        width={NODE_WIDTH}
        height={NODE_HEIGHT}
        label={node.display}
        boxClassName="sparql-node-box canvas-node"
        labelClassName="sparql-label canvas-node-label"
        labelY={NODE_HEIGHT / 2 + 2}
      >
        {node.projected ? (
          // The mark that says "this leaves the query", without consulting the header.
          <text className="sparql-projection-mark" x={8} y={16}>
            →
          </text>
        ) : null}
        {node.annotation ? (
          <text className="sparql-node-annotation" x={8} y={NODE_HEIGHT - 10}>
            {node.annotation}
          </text>
        ) : null}
      </BoxElement>
    );
  },
  edgePoint: boxEdgePoint,
};

/** A group construct as a labelled containment frame, styled by its kind. */
const regionShape: CustomShapeRef = {
  customShape: "sparql-region",
  render: (raw, state?: CustomShapeState) => {
    const element = raw as RegionElement;
    const region = element.region;
    const classes = ["sparql-region", `sparql-region-${region.kind}`, "canvas-element"];
    if (state?.selected) {
      classes.push("sparql-selected");
    }

    return (
      <FrameElement
        className={classes.join(" ")}
        x={element.x}
        y={element.y}
        width={region.width}
        height={region.height}
        label={region.label}
        labelClassName="sparql-region-label canvas-hint"
      />
    );
  },
  edgePoint: boxEdgePoint,
};

/** A FILTER/BIND/VALUES badge: its text exactly as written, floating by its anchor. */
const annotationShape: CustomShapeRef = {
  customShape: "sparql-annotation",
  render: (raw, state?: CustomShapeState) => {
    const element = raw as AnnotationElement;
    const annotation = element.annotation;
    const classes = ["sparql-annotation", `sparql-annotation-${annotation.kind}`, "canvas-hint"];
    if (state?.selected) {
      classes.push("sparql-selected");
    }

    return (
      <text className={classes.join(" ")} x={element.x} y={element.y}>
        {annotation.text}
      </text>
    );
  },
  edgePoint: boxEdgePoint,
};

/**
 * What a query diagram allows, stated once: nodes and regions that drag and select,
 * annotation badges that select only, and one render-only triple-pattern relation. No
 * relation declares a source anchor, so no connect gesture exists; there is no toolbox and
 * no drop handler, so nothing can be added - the backend offers no mutating action, and the
 * canvas must invent none.
 */
const SPARQL_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    { id: "region", shape: regionShape, anchors: { kind: "edge" }, sizing: "model" },
    { id: "node", shape: nodeShape, anchors: { kind: "edge" }, sizing: "model" },
    { id: "annotation", shape: annotationShape, anchors: { kind: "edge" }, sizing: "model", draggable: false },
  ],
  relationTypes: [
    {
      id: "pattern",
      route: "straight",
      style: { endMarker: "arrow" },
      label: { placement: "midpoint", offset: -6 },
      className: "sparql-edge",
      lineClassName: "sparql-edge-line",
      hitClassName: "sparql-edge-hit",
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

/** Where a badge sits, in canvas units: on its anchor element, or floating at the origin. */
function annotationAnchor(
  annotation: SparqlAnnotation,
  nodes: Map<string, SparqlNode>,
  regions: Map<string, SparqlRegion>,
): ShapePoint {
  const node = nodes.get(annotation.attachedTo);
  if (node) {
    return { x: node.x, y: node.y - 6 };
  }

  const region = regions.get(annotation.attachedTo);
  if (region) {
    return { x: region.x + 12, y: region.y + 18 };
  }

  // A root-scope filter constrains the whole query and floats above its patterns.
  return { x: 0, y: -10 };
}

/**
 * The query as the joins it is made of: one node per variable however many patterns mention it,
 * concrete terms and literals told apart from what is being asked for, group constructs as
 * labeled containment frames, FILTER/BIND/VALUES as badges showing their text as written, and
 * the form and modifiers as a header band - drawn through the central canvas library
 * (Requirements 3 and 4).
 *
 * <b>Nothing here can edit the query.</b> The backend offers no mutating action and no toolbox,
 * so this canvas registers no toolbox, no drop target and no connect gesture - there is exactly
 * one gesture, a drag, and it lands in the registration's `layout:` block through
 * `moveElementTo`. Refusals come back from the backend with their own sentences (Requirement 5.4).
 */
export function SparqlCanvas({ projectId, entryId, path }: DiagramCanvasProps) {
  const { model, loading, failed, moveElementTo, reportView } = useSparqlStream(projectId, path);
  const { select } = useContextConnection();
  const { selection, actions } = useContextSelection();
  const [rejection, setRejection] = useState("");
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const selectionKey = innermostKey(selection);
  const selectedId = elementIdOfKey(selectionKey ?? null);

  const diagramModel = useMemo<DiagramModel>(() => {
    // Regions first, so every frame paints behind the patterns it contains.
    const regions = [...model.regions.values()].map((region): RegionElement => ({
      id: region.id,
      type: "region",
      x: region.x + region.width / 2,
      y: region.y + region.height / 2,
      width: region.width,
      height: region.height,
      label: region.label,
      region,
    }));
    const nodes = [...model.nodes.values()].map((node): NodeElement => ({
      id: node.id,
      type: "node",
      x: node.x + NODE_WIDTH / 2,
      y: node.y + NODE_HEIGHT / 2,
      width: NODE_WIDTH,
      height: NODE_HEIGHT,
      label: node.display,
      node,
    }));
    // Badges last, so an annotation is never painted over by what it annotates.
    const annotations = [...model.annotations.values()].map((annotation): AnnotationElement => {
      const anchor = annotationAnchor(annotation, model.nodes, model.regions);
      return {
        id: annotation.id,
        type: "annotation",
        x: anchor.x,
        y: anchor.y,
        width: ANNOTATION_WIDTH,
        height: ANNOTATION_HEIGHT,
        label: annotation.text,
        annotation,
      };
    });
    const connections = [...model.edges.values()].map((edge) => ({
      id: edge.id,
      type: "pattern",
      sourceId: edge.fromElementId,
      targetId: edge.toElementId,
      label: edge.label,
      className: edge.isPath ? "sparql-edge-path" : undefined,
    }));
    return { elements: [...regions, ...nodes, ...annotations], connections };
  }, [model]);

  /** The backend's push is the selection; the canvas renders it and never decides. */
  const librarySelection = useMemo<DiagramSelection>(() => {
    if (selectedId === null) {
      return [];
    }
    return [{ kind: model.edges.has(selectedId) ? "connection" : "element", id: selectedId }];
  }, [selectedId, model.edges]);

  const events: DiagramEventHandlers = {
    onSelectionChanged: ({ selection: next }) =>
      select(next.length > 0 ? elementSelectionOf(entryId, path, next[0].id) : null),
    onElementMoved: ({ elementId, position }) => {
      setRejection("");
      const region = model.regions.get(elementId);
      const width = region ? region.width : NODE_WIDTH;
      const height = region ? region.height : NODE_HEIGHT;
      // The authored position, raw - and the only thing this canvas can send. An anonymous
      // variable's refusal comes back from the backend with its sentence (Requirement 5.4).
      void (async () => {
        const error = await moveElementTo(elementId, position.x - width / 2, position.y - height / 2);
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
    return (
      <div className="sparql-canvas canvas-host sparql-canvas-message canvas-host-message">
        <p>This query could not be opened.</p>
      </div>
    );
  }

  return (
    <div className="sparql-canvas canvas-host" role="application" aria-label="SPARQL query">
      {model.header ? (
        <header className="sparql-header-band">
          <span className="sparql-header-form">{model.header.form}</span>
          {model.header.modifierRows.map((row) => (
            <span key={row} className="sparql-header-modifier">
              {row}
            </span>
          ))}
        </header>
      ) : null}
      <DiagramCanvas
        definition={SPARQL_DEFINITION}
        model={diagramModel}
        events={events}
        selection={librarySelection}
        context={{
          selectionKey: selectionKey ?? undefined,
          actions,
          selectForMenu: (id) => select(elementSelectionOf(entryId, path, id, ContextSelectionAction.CONTEXT_MENU)),
          // The backend offers no mutating action on a query, so the menu's entries only
          // ever closed the menu - executing stays a no-op rather than inventing an edit.
          executeAction: () => {},
        }}
        ariaLabel="SPARQL query"
        className="sparql-surface"
        scrollbarsClassName="sparql-scrollbars"
      />
      {model.truncation ? (
        <p className="sparql-truncation-banner">
          {`Showing ${model.truncation.shown} of ${model.truncation.total} elements — this query is larger than the diagram draws`}
        </p>
      ) : null}
      {loading ? <p className="sparql-status canvas-status">Opening…</p> : null}
      {rejection ? <p className="sparql-rejection canvas-rejection">{rejection}</p> : null}
    </div>
  );
}
