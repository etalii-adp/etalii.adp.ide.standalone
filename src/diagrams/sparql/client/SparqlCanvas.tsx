import { useMemo, useState } from "react";

import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ShapeBounds, ShapePoint } from "@client/canvas/library/definition/diagramDefinition";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import type { ToolContentProps } from "@client/shell/panels/toolPanelRegistration";
import { useViewReport } from "@client/diagrams/useViewReport";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useSparqlStream } from "./useSparqlStream";
import type { SparqlAnnotation, SparqlNode, SparqlRegion } from "./sparqlModel";
import { SPARQL_BINDINGS } from "./sparqlBindings";
import disText from "../definition/sparql-query.dis?raw";

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

/** The DISL type each kind of node is drawn as: the element type the compiled definition names it by. */
const NODE_TYPE: Readonly<Record<SparqlNode["kind"], string>> = {
  variable: "variable",
  anonymous: "anonymousvariable",
  iri: "iri",
  literal: "literal",
  subquery: "subquery",
};

/**
 * What a query diagram allows, compiled from its bundled specification (`definition/sparql-query.dis`):
 * nodes and regions that drag and select, annotations that select only, and two render-only relations.
 * No relation declares a source anchor, so no connect gesture exists; there is no toolbox and no drop
 * handler, so nothing can be added - the backend offers no mutating action, and the canvas invents none.
 */
export const SPARQL_DEFINITION: DiagramDefinition = assertValidDiagramDefinition(compileNotation(parseDisl(disText), SPARQL_BINDINGS));

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
 * the form and modifiers as a header band - drawn through the central canvas library from the
 * bundled specification (Requirements 3 and 4).
 *
 * <b>Nothing here can edit the query.</b> The backend offers no mutating action and no toolbox,
 * so this canvas offers no palette, no drop target and no connect gesture - there is exactly
 * one gesture, a drag, and it lands in the registration's `layout:` block through
 * `moveElementTo`. Refusals come back from the backend with their own sentences (Requirement 5.4).
 */
export function SparqlCanvas({ projectId, entryId, path }: ToolContentProps) {
  const { model, loading, failed, moveElementTo, reportView } = useSparqlStream(projectId, path);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);
  // The backend's palette for this type, which is empty: handed to the library, it tells the panel
  // the type offers nothing, where the library would otherwise derive an item per element type.
  const toolboxItems = useToolboxItems(projectId, path);

  const diagramModel = useMemo<DiagramModel>(() => {
    // Regions first, so every frame paints behind the patterns it contains.
    const regions = [...model.regions.values()].map((region): RegionElement => ({
      id: region.id,
      type: "scope",
      x: region.x + region.width / 2,
      y: region.y + region.height / 2,
      width: region.width,
      height: region.height,
      label: region.label,
      payload: { label: region.label, kind: region.kind, scopeKind: region.kind },
      region,
    }));
    const nodes = [...model.nodes.values()].map((node): NodeElement => ({
      id: node.id,
      type: NODE_TYPE[node.kind],
      x: node.x + NODE_WIDTH / 2,
      y: node.y + NODE_HEIGHT / 2,
      width: NODE_WIDTH,
      height: NODE_HEIGHT,
      label: node.display,
      // A variable's second line is its defining expression, a literal's its datatype or language: both are the node's annotation.
      payload: { display: node.display, kind: node.kind, projected: node.projected, annotation: node.annotation, definingExpression: node.annotation },
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
      // A subquery's projected variable joins the outer query; every other edge is a triple pattern.
      type: edge.id.startsWith("edge:sub:") ? "projectedjoin" : "triplepattern",
      sourceId: edge.fromElementId,
      targetId: edge.toElementId,
      label: edge.label,
      className: edge.isPath ? "sparql-edge-path" : undefined,
      payload: { predicate: edge.label, isPath: edge.isPath },
    }));
    // The header band and the banner read the background: the specification's diagram attributes,
    // and what only the backend knows, whether its budget cut what is drawn.
    const background = {
      ...(model.header ? { headerForm: model.header.form, modifiers: model.header.modifierRows.join(" · ") } : {}),
      truncated: model.truncation !== null,
      shown: model.truncation?.shown ?? 0,
      total: model.truncation?.total ?? 0,
    };
    return { elements: [...regions, ...nodes, ...annotations], connections, background };
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
      // Returned, so a refusal lets the library put the element back where it was (ruled 2026-10-19).
      return moveElementTo(elementId, position.x - width / 2, position.y - height / 2);
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
      <DiagramCanvas
        definition={SPARQL_DEFINITION}
        model={diagramModel}
        events={events}
        toolboxItems={toolboxItems}
        source={{ entryId, path }}
        ariaLabel="SPARQL query"
        className="sparql-surface"
        scrollbarsClassName="sparql-scrollbars"
      />
    </div>
  );
}
