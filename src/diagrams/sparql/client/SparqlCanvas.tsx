import { useMemo, useState } from "react";

import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type {
  DiagramDefinition,
  ShapeBounds,
  ShapePoint,
} from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { useViewReport } from "@client/diagrams/useViewReport";
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





/**
 * What a query diagram allows, stated once: nodes and regions that drag and select,
 * annotation badges that select only, and one render-only triple-pattern relation. No
 * relation declares a source anchor, so no connect gesture exists; there is no toolbox and
 * no drop handler, so nothing can be added - the backend offers no mutating action, and the
 * canvas must invent none.
 */
const SPARQL_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    {
      id: "region",
      shape: "frame",
      classNames: [
        { className: "sparql-region canvas-element", on: "element" },
        { className: { template: "sparql-region-{payload.kind}" }, on: "element" },
      ],
      labels: [{ text: { path: "payload.label" }, placement: "above", className: "sparql-region-label canvas-hint" }],
      anchors: { kind: "edge" },
      sizing: "model",
    },
    {
      id: "node",
      shape: "box",
      classNames: [
        { className: "sparql-node canvas-element", on: "element" },
        { className: { template: "sparql-node-{payload.kind}" }, on: "element" },
        { className: "sparql-node-projected", on: "element", when: { path: "payload.projected", is: "true" } },
        { className: "sparql-node-box canvas-node", on: "shape" },
      ],
      labels: [
        {
          text: { path: "payload.display" },
          anchorTo: "top",
          offset: { x: 0, y: NODE_HEIGHT / 2 + 2 },
          truncate: true,
          className: "sparql-label canvas-node-label",
        },
        {
          text: { path: "payload.annotation" },
          when: { path: "payload.annotation", is: "non-empty" },
          anchorTo: "top",
          offset: { x: 0, y: NODE_HEIGHT - 10 },
          align: "start",
          insetX: 8,
          className: "sparql-node-annotation",
        },
      ],
      decorations: [
        {
          // The mark that says "this leaves the query", without consulting the header.
          glyph: "marker",
          // Canvas units: these read `bounds`, which resolves to a position rather than an offset.
          anchor: "canvas",
          from: { x: { path: "bounds.left", number: { plus: 8 } }, y: { path: "bounds.top", number: { plus: 16 } } },
          text: { template: "→" },
          className: "sparql-projection-mark",
          when: { path: "payload.projected", is: "true" },
        },
      ],
      anchors: { kind: "edge" },
      sizing: "model",
    },
    {
      id: "annotation",
      // Text and nothing else - a note beside what it annotates.
      shape: "none",
      classNames: [
        { className: "sparql-annotation canvas-hint", on: "element" },
        { className: { template: "sparql-annotation-{payload.kind}" }, on: "element" },
      ],
      labels: [{ text: { path: "payload.text" } }],
      anchors: { kind: "edge" },
      sizing: "model",
      draggable: false,
    },
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
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);


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
      payload: { label: region.label, kind: region.kind },
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
      payload: { display: node.display, kind: node.kind, projected: node.projected, annotation: node.annotation },
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
        payload: { text: annotation.text, kind: annotation.kind },
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

  const events: DiagramEventHandlers = {
    // Selection is the library's (centralized-selection), and so is the refusal line: every call
    // here, and every menu action the library runs, reports its own refusal there
    // (client-centralization Requirement 2).
    onElementMoved: ({ elementId, position }) => {
      const region = model.regions.get(elementId);
      const width = region ? region.width : NODE_WIDTH;
      const height = region ? region.height : NODE_HEIGHT;
      // The authored position, raw - and the only thing this canvas can send. An anonymous
      // variable's refusal comes back from the backend with its sentence (Requirement 5.4).
      void moveElementTo(elementId, position.x - width / 2, position.y - height / 2);
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

  // Opening, reconnecting and unavailable are the library's to say, in the frame around this canvas.
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
        source={{ entryId, path }}
        ariaLabel="SPARQL query"
        className="sparql-surface"
        scrollbarsClassName="sparql-scrollbars"
      />
      {model.truncation ? (
        <p className="sparql-truncation-banner">
          {`Showing ${model.truncation.shown} of ${model.truncation.total} elements — this query is larger than the diagram draws`}
        </p>
      ) : null}
    </div>
  );
}
