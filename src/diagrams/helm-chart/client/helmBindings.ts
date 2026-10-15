import { forwardBezierPath } from "@client/canvas/connectors";
import type { ClassDeclaration, CustomRouteRef } from "@client/canvas/library/definition/diagramDefinition";
import type { NotationBindings } from "@client/canvas/library/disl/compileNotation";

/**
 * What the helm chart diagram's canvas needs beside its bundled specification, because the library
 * cannot read it from there. `compileNotation` derives everything else from
 * `definition/helm-chart.dis`: the ten node types and their labels, tooltips and accessible names,
 * the five relations with their ends, markers and labels, the stub an open end is drawn as, the
 * reveal a double-click runs, and the manual layout.
 *
 * <b>Why each entry is code.</b>
 * - **The CEL paths.** The library has no CEL, so every label the specification computes from a
 *   node - a chart's name or `(chart)`, a file's name, a dependency's effective name, an entry's
 *   name - is the one `name` the backend measured the box for and sends in the payload.
 * - **The class names.** They tie the drawing to `helm-charts.css`, which turns each kind into one
 *   of the theme's `--color-diagram-helm-*` hues; the specification's styles say the same in tokens.
 *   A partial is drawn as any template, as the specification's shared `template` style says.
 * - **The route.** DISL's `bezier` leaving right and entering left says the curve's directions; how
 *   far it reaches is the shared forward Bezier every left-to-right diagram draws.
 * - **The role and the tab order**, which DISL leaves to the runtime (6.15).
 */

/** A relation out of its source's right side and into its target's left: the bands run left to right. */
export const helmRoute: CustomRouteRef = {
  customRoute: "helm-forward-bezier",
  path: (from, to, _waypoints, ends) => {
    const a = ends ? { x: ends.source.x + ends.source.width, y: ends.source.y + ends.source.height / 2 } : from;
    const b = ends ? { x: ends.target.x, y: ends.target.y + ends.target.height / 2 } : to;
    return forwardBezierPath(a, b);
  },
};

/** The stylesheet's class for each node type's kind. */
const KIND_CLASSES: Readonly<Record<string, string>> = {
  Chart: "chart",
  Values: "values",
  Schema: "schema",
  Template: "template",
  Partial: "template",
  Crds: "crds",
  Dependency: "dependency",
  Subchart: "subchart",
  Archive: "archive",
  Lock: "lock",
};

/** The condition the specification marks a file that did not parse by, and what the payload carries for it. */
const UNREADABLE = "has(self.failure)";

function classNamesOf(type: string, node: { conditions?: readonly { when: string }[] }): readonly ClassDeclaration[] {
  const kind = KIND_CLASSES[type];
  if (kind === undefined) {
    throw new Error(`helmBindings: the node type "${type}" has no class in helm-charts.css.`);
  }

  const unreadable = (node.conditions ?? []).some((condition) => condition.when === UNREADABLE);
  return [
    { className: "helm-node" },
    { className: `helm-node-${kind}` },
    ...(unreadable ? [{ className: "helm-node-unreadable", when: { path: "payload.unreadable", is: "true" } } as const] : []),
    { className: "helm-node-box", on: "shape" },
  ];
}

const NAME = "payload.name";

export const HELM_BINDINGS: NotationBindings = {
  celPaths: {
    "has(self.name) ? self.name : '(chart)'": NAME,
    "(has(self.name) ? self.name : '(chart)')": NAME,
    "self.path": NAME,
    "fileName(self.path)": NAME,
    "effectiveName(self)": NAME,
    "self.entryName": NAME,
  },
  classNames: classNamesOf,
  labelClassName: () => "helm-node-label",
  relationClassName: (relation) => `helm-edge helm-edge-${relation.toLowerCase()}`,
  relationLineClassName: () => "helm-edge-line",
  // A relation is never drawn by hand: no end starts one, and a target attaches by its edge.
  endpointAnchors: (_relation, end) => (end === "source" ? [] : "edge"),
  customRoutes: { Declares: helmRoute, Resolves: helmRoute, Overrides: helmRoute, Configures: helmRoute, Includes: helmRoute },
  // An open end: something names what is not there - visible, not an error shout.
  stubClassName: () => "helm-edge-open",
  edgeLabelClassName: () => "helm-edge-label",
  focusable: true,
  accessibleRole: "button",
};
