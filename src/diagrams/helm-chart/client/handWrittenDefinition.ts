import { forwardBezierPath } from "@client/canvas/connectors";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { CustomRouteRef, DiagramDefinition, ShapePoint } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { HelmEdgeKind, HelmElementKind } from "@client/generated/helm-charts_pb";
import { edgesOf, nodesOf, type HelmElement, type HelmModel } from "./helmModel";

/**
 * THE ORACLE, AND TEST CODE ONLY: the canvas definition `HelmCanvas.tsx` stated by hand until it was
 * compiled from the bundled specification, moved here unchanged with the model it built, so
 * `helmCompiledDefinition.test.tsx` can hold the compiled drawing to it. Nothing in the product
 * imports this file.
 *
 * It had one `node` type told apart by a payload class, a `stub` element type the canvas built
 * for every open end, and one `edge` relation told apart by a class on each connection.
 */

/** How far an open edge's stub reaches out of its source, in canvas units. */
const STUB_LENGTH = 48;

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

export const HAND_WRITTEN_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    {
      id: "node",
      shape: "box",
      classNames: [
        { className: "helm-node" },
        { className: { template: "helm-node-{payload.kindClass}" } },
        { className: "helm-node-unreadable", when: { path: "payload.unreadable", is: "true" } },
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
        // What `onDoubleClick` did on the rendered element. The right-click that used to sit
        // beside it pushed a context-menu selection by hand - selection glue, now the library's
        // shared menu (centralized-selection Requirement 1.3).
        { id: "helm.activate", invokedBy: [{ kind: "gesture", gesture: "activate" }], appliesTo: [{ kind: "element" }] },
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
          // Canvas units: these read `bounds`, which resolves to a position rather than an offset.
          anchor: "canvas",
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
 * Where an edge should be drawn from and to, or null when either end is not delivered - which
 * includes every open end, whose stub the canvas draws from the source instead.
 */
function anchorsOf(model: HelmModel, edge: HelmElement): { from: HelmElement; to: HelmElement } | null {
  const wire = edge.payload.edge;
  if (!wire) {
    return null;
  }

  const from = model.elements.get(wire.sourceId);
  const to = wire.targetId ? model.elements.get(wire.targetId) : undefined;
  return from && to ? { from, to } : null;
}

/** The model the hand-written definition was handed, built as `HelmCanvas` built it. */
export function handWrittenModel(model: HelmModel): DiagramModel {
  const nodes = nodesOf(model);
  const edges = edgesOf(model);
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
      payload: {
        name: node.payload.name,
        kindClass: kindClass(node.payload.kind),
        unreadable: node.payload.unreadable,
        title: `${kindLabel(node.payload.kind)} ${node.payload.name}`,
      },
    });
  }

  return { elements, connections };
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
