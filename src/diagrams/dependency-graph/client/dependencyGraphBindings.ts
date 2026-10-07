import {
  facingAnchorsBetween,
  forwardBezierPath,
  horizontalBezierPath,
  sideAnchorOf,
  type ConnectorBox,
} from "@client/canvas/connectors";
import { snapToStep } from "@client/canvas/library/DiagramCanvas";
import type { ActionDeclaration } from "@client/canvas/library/definition/actions";
import type { ClassDeclaration, CustomRouteRef, ShapeBounds, ShapePoint } from "@client/canvas/library/definition/diagramDefinition";
import type { NotationBindings } from "@client/canvas/library/disl/compileNotation";

/**
 * What the dependency graph's canvas needs beside its bundled specification, because the library cannot
 * read it from there. `compileNotation` derives everything else - the node type, its label, its named
 * `left` and `right` anchors, its fixed size and free moves, the relation with its ends, arrowhead, label
 * and empty-canvas release, the row snap, the shortcuts and the background menu - from
 * `definition/dependency-graph.dis`.
 *
 * <b>Why each entry is code.</b>
 * - **The CEL path.** A label is `self.label != '' ? self.label : self.id`: the library has no CEL, so the
 *   canvas computes it into the element's `label` (`DependencyGraphCanvas`), and this table names it.
 * - **The class names.** They tie the drawing to `dependency-graph.css`.
 * - **The span.** DISL draws a node as a `roundedRect`; this module draws it as the library's shared
 *   `span`, as the .NET dependency graph does.
 * - **The route.** The definition names the `dependencyCurve` routing plugin; its geometry is this route.
 * - **A dependency's end at its target.** It attaches on the side facing its source, the library's `edge`
 *   rule, because the notation reads left to right and an edge through the top of a box would read as a
 *   different relation.
 * - **Where a background right-click lands.** The menu's "Add node here" places a node as a drop does, at
 *   the x under the pointer and on the row nearest it, and a row is the backend's unit, not a canvas one.
 * - **The keys the backend is sent.** The backend resolves an action by its key, so each action sends the
 *   key its menu entry writes.
 */

/**
 * The vertical distance between adjacent rows, in the module's own y units.
 *
 * Mirrors `DependencyGraphRows.Height` in the backend, and must stay equal to it: the y this canvas
 * sends with a move is `row × this`, and the backend divides the same constant back out. A mismatch
 * would land every vertical drag on the wrong row.
 */
export const ROW_HEIGHT = 60;

/**
 * The nearest row for a module-space y, by the library's one rounding rule - halves away from zero,
 * never negative zero - which DependencyGraphRows.ToNearestRow shares on the backend. A dragged
 * element's position already arrives snapped by the declaration, so for a move this only turns an
 * exact row back into its index.
 */
export function nearestRow(y: number): number {
  return snapToStep(y, ROW_HEIGHT) / ROW_HEIGHT;
}

/** A corner-based bounds as the centre-based connector geometry wants it. */
function boxOf(bounds: ShapeBounds): ConnectorBox {
  return {
    x: bounds.x + bounds.width / 2,
    y: bounds.y + bounds.height / 2,
    width: bounds.width,
    height: bounds.height,
  };
}

/**
 * The dependency curve, exactly as `InteractiveBezierConnection` drew it: the facing bezier when the
 * dependency sits clear to the right, and the forward loop - out of the dependent's right, back into
 * the dependency's left - when it sits behind. The loop is decided from the endpoint BOUNDS, which the
 * connect preview does not have yet; the preview draws the plain horizontal bezier to the pointer.
 */
export const dependencyRoute: CustomRouteRef = {
  customRoute: "dependency-bezier",
  path: (from, to, _waypoints, ends) => {
    if (!ends) {
      return horizontalBezierPath(from, to);
    }

    const fromBox = boxOf(ends.source);
    const toBox = boxOf(ends.target);
    const loopsBack = ends.target.x < ends.source.x + ends.source.width;
    const [a, b] = loopsBack
      ? [sideAnchorOf(fromBox, "right"), sideAnchorOf(toBox, "left")]
      : facingAnchorsBetween(fromBox, toBox);
    return loopsBack ? forwardBezierPath(a, b) : horizontalBezierPath(a, b);
  },
};

/** Where a background right-click lands: the x under the pointer, on the row nearest it, as a drop places a node. */
const backgroundPlacement = (point: ShapePoint): ShapePoint => ({ x: point.x, y: nearestRow(point.y) });

const NODE_CLASSES: readonly ClassDeclaration[] = [
  { className: "dependency-graph-element canvas-element" },
  { className: "dependency-graph-node canvas-node" },
];

/** An action sending the key its menu entry writes, which is how the backend resolves it. */
function dispatched(action: ActionDeclaration, entry: { shortcut?: string }): ActionDeclaration {
  return entry.shortcut !== undefined ? { ...action, backendKey: entry.shortcut } : action;
}

export const DEPENDENCY_GRAPH_BINDINGS: NotationBindings = {
  wireIds: "x-dependencies",
  celPaths: { "self.label != '' ? self.label : self.id": "element.label" },
  classNames: () => NODE_CLASSES,
  labelClassName: () => "dependency-graph-label canvas-node-label",
  relationClassName: () => "dependency-graph-relation",
  relationLineClassName: () => "dependency-graph-relation-line",
  relationHitClassName: () => "dependency-graph-relation-hit",
  endpointAnchors: (_relation, end) => (end === "target" ? "edge" : undefined),
  builtInShapes: { roundedRect: "span" },
  customRoutes: { DependsOn: dependencyRoute },
  action: dispatched,
  extras: () => ({ backgroundPlacement }),
};
