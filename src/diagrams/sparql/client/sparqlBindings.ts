import type { ClassDeclaration } from "@client/canvas/library/definition/diagramDefinition";
import type { NotationBindings } from "@client/canvas/library/disl/compileNotation";

/**
 * What the SPARQL query canvas needs beside its bundled specification, because the library cannot
 * read it from there. `compileNotation` derives everything else - the node types and their labels,
 * when the second line and the projection mark show, the frames behind their contents, the two edge
 * types with their markers and dashes, the header band and the banner - from
 * `definition/sparql-query.dis`.
 *
 * <b>Why each entry is code.</b>
 * - **The CEL paths and conditions.** The library has no CEL, so the canvas composes what the
 *   specification computes into the element's payload (`SparqlCanvas`).
 * - **The class names.** They tie the drawing to `sparql.css`, which paints the theme tokens.
 */

/** A node's classes, the same for every kind of term: its kind is a class, so the stylesheet tells a variable from an IRI. */
const NODE_CLASSES: readonly ClassDeclaration[] = [
  { className: "sparql-node canvas-element", on: "element" },
  { className: { template: "sparql-node-{payload.kind}" }, on: "element" },
  { className: "sparql-node-projected", on: "element", when: { path: "payload.projected", is: "true" } },
  { className: "sparql-node-box canvas-node", on: "shape" },
];

const ELEMENT_CLASSES: Readonly<Record<string, readonly ClassDeclaration[]>> = {
  Term: NODE_CLASSES,
  Iri: NODE_CLASSES,
  Variable: NODE_CLASSES,
  AnonymousVariable: NODE_CLASSES,
  Literal: NODE_CLASSES,
  SubQuery: NODE_CLASSES,
  Scope: [
    { className: "sparql-region canvas-element", on: "element" },
    // The frame's own class: the stylesheet reaches the outline by it, never by a descendant `rect`.
    { className: "sparql-region-outline" },
    { className: { template: "sparql-region-{payload.kind}" }, on: "element" },
  ],
  Annotation: [
    { className: "sparql-annotation canvas-hint", on: "element" },
    { className: { template: "sparql-annotation-{payload.kind}" }, on: "element" },
  ],
};

export const SPARQL_BINDINGS: NotationBindings = {
  celPaths: {
    "'?' + self.name": "payload.predicate",
    "string(budget('drawn').shown)": "payload.shown",
    "string(budget('drawn').total)": "payload.total",
  },
  celConditions: {
    "self.projected": { path: "payload.projected", is: "true" },
    "self.definingExpression != ''": { path: "payload.definingExpression", is: "non-empty" },
    "self.annotation != ''": { path: "payload.annotation", is: "non-empty" },
    "self.label != ''": { path: "payload.label", is: "non-empty" },
    "self.isPath": { path: "payload.isPath", is: "true" },
  },
  classNames: (type) => ELEMENT_CLASSES[type] ?? [],
  labelClassName: (type, label) => {
    if (type === "Scope") {
      return "sparql-region-label canvas-hint";
    }
    if (type === "Annotation") {
      return undefined;
    }
    return label.id === "display" ? "sparql-label canvas-node-label" : "sparql-node-annotation";
  },
  badgeClassName: () => "sparql-projection-mark",
  relationClassName: () => "sparql-edge",
  relationLineClassName: () => "sparql-edge-line",
  relationHitClassName: () => "sparql-edge-hit",
  canvasChrome: {
    header: { className: "sparql-header-band", rowClassName: (row) => (row === "form" ? "sparql-header-form" : "sparql-header-modifier") },
    // The banner shows while the backend's budget cut what is drawn, which the background says.
    notices: { "budget:drawn": { className: "sparql-truncation-banner", when: { path: "payload.truncated", is: "true" } } },
  },
  // A pattern is never drawn by hand: no end starts one, and a target attaches by its edge.
  endpointAnchors: (_relation, end) => (end === "source" ? [] : "edge"),
};
