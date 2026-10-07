import {
  facingAnchorsBetween,
  forwardBezierPath,
  horizontalBezierPath,
  sideAnchorOf,
  type ConnectorBox,
} from "@client/canvas/connectors";
import type { ClassDeclaration, CustomRouteRef, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import type { NotationBindings } from "@client/canvas/library/disl/compileNotation";

/**
 * What the .NET dependency graph's canvas needs beside its bundled specification, because the
 * library cannot read it from there. `compileNotation` derives everything else - the two node
 * types, their labels and when the second line shows, the tooltip and accessible name, the
 * activate action, the two reference types with their ends and markers, and the manual layout -
 * from `definition/dotnet-dependency-graph.dis`.
 *
 * <b>Why each entry is code.</b>
 * - **The CEL paths and conditions.** The library has no CEL, so the canvas composes what the
 *   specification computes into the element's payload (`DotNetDependencyGraphCanvas`): a box's
 *   second line is `subtitle`, drawn while it has text, and `title(self)` is `title`.
 * - **The class names.** They tie the drawing to `dotnet-dependency-graph.css`, which paints the
 *   theme tokens; a test proves those tokens equal the custom properties.
 * - **The span.** DISL draws a box as a `roundedRect`; this module draws it as the library's shared
 *   `span`, slot for slot what the authored `generic/dependencies` graph draws, so the two
 *   dependency graphs look like one drawing in two colourways.
 * - **The route.** DISL's `bezier` with `backward: "loop"` says the curve's shape; how far its
 *   control points reach, and when it goes the long way round, is this route.
 */

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
export const dependencyRoute: CustomRouteRef = {
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

const ELEMENT_CLASSES: Readonly<Record<string, readonly ClassDeclaration[]>> = {
  Project: [
    { className: "dotnet-dependency-element canvas-element", on: "element" },
    { className: "dotnet-dependency-element-project", on: "element" },
    { className: "dotnet-dependency-node canvas-node", on: "shape" },
  ],
  Package: [
    { className: "dotnet-dependency-element canvas-element", on: "element" },
    { className: "dotnet-dependency-element-package", on: "element" },
    // Collapsing LOUDLY: a package the solution's projects disagree about is marked on the
    // element, which Requirement 3.5 asks for as against the silent collapse it forbids.
    { className: "dotnet-dependency-conflict", on: "element", when: { path: "payload.hasVersionConflict", is: "true" } },
    { className: "dotnet-dependency-node canvas-node", on: "shape" },
  ],
};

const RELATION_CLASSES: Readonly<Record<string, string>> = {
  ProjectReference: "dotnet-dependency-edge dotnet-dependency-edge-project",
  PackageReference: "dotnet-dependency-edge dotnet-dependency-edge-package",
};

export const DOTNET_BINDINGS: NotationBindings = {
  wireIds: "x-dotnet",
  celPaths: {
    "self.targetFrameworks.join(', ')": "payload.subtitle",
    "self.versions.join(', ')": "payload.subtitle",
    "title(self)": "payload.title",
  },
  celConditions: {
    "self.targetFrameworks.size() > 0": { path: "payload.subtitle", is: "non-empty" },
    "self.versions.size() > 0": { path: "payload.subtitle", is: "non-empty" },
  },
  classNames: (type) => ELEMENT_CLASSES[type] ?? [],
  labelClassName: (_type, label) => (label.id === "subtitle" ? "dotnet-dependency-node-subtitle" : "dotnet-dependency-node-label canvas-node-label"),
  relationClassName: (relation) => RELATION_CLASSES[relation],
  relationLineClassName: () => "dotnet-dependency-edge-line",
  // A reference is never drawn by hand: no end starts one, and a target attaches by its edge.
  endpointAnchors: (_relation, end) => (end === "source" ? [] : "edge"),
  builtInShapes: { roundedRect: "span" },
  focusable: true,
  customRoutes: { ProjectReference: dependencyRoute, PackageReference: dependencyRoute },
};
