import type { ActionDeclaration } from "@client/canvas/library/definition/actions";
import type { ClassDeclaration, CustomRouteRef, DiagramDefinition } from "@client/canvas/library/definition/diagramDefinition";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import { compileNotation, type NotationBindings } from "@client/canvas/library/disl/compileNotation";
import { parseDisl, type DislDocument } from "@client/canvas/library/disl/disTypes";

/**
 * What the family's three canvases need beside their bundled definitions (`definition/databricks-bundle.dis`,
 * `databricks-job.dis` and `databricks-pipeline.dis`), because the library cannot read it from there.
 * `compileNotation` derives everything else from each: its node types, their labels and where each sits,
 * their sizes and anchors, which of them may be connected, moved or deleted, the target frames beneath the
 * connections, the relations with their routes, markers and dash, the outcome label, the shortcuts and the
 * delete gesture with their wire ids, the canvas menu and the manual layout.
 *
 * <b>Why each entry is code.</b>
 * - **The CEL paths.** The library has no CEL, so a label the definition computes - a badge strip, a target's
 *   summary, a destination's name - is read from the payload the canvas builds from what the backend sends
 *   (`DatabricksCanvas`'s `payloadOf`), under the name this table gives it.
 * - **The class names.** They tie the drawing to `databricks.css`, which paints the theme's colours, the
 *   condition task's corner, a missing task's stub and the simulated states; the definitions' styles,
 *   variants and conditions say the same in tokens, and the library reads none of them.
 * - **The route.** DISL's `bezier` leaving right and entering left says the curve; its exact shape is the
 *   layered layout's fixed horizontal reach.
 * - **Where a dependency is drawn from.** A job's task offers the gesture from either side anchor, as it always
 *   has; the definition's `anchoring` says only where the line attaches. Every other end starts nothing and
 *   attaches by its edge.
 * - **The rename's editor box.** It covers the whole box rather than the name's own line.
 * - **The keys the backend is sent.** The backend resolves an action by its key where its menu entry writes one.
 * - **The drops.** A toolbox entry is dropped on the canvas, which the canvas answers with a placement id.
 * - **The simulated actions** (`simulatedMenuActions`), standing in for runtime gap A6.
 */

/** FixedBezierConnection's control reach, kept for the family's layered edges. */
const EDGE_REACH = 30;

/**
 * A dependency or flow edge, exactly as FixedBezierConnection drew it: out of the source's right edge, into
 * the target's left, with the fixed control reach the layered layout wants.
 */
export const layeredRoute: CustomRouteRef = {
  customRoute: "databricks-layered",
  path: (from, to, _waypoints, ends) => {
    const a = ends ? { x: ends.source.x + ends.source.width, y: ends.source.y + ends.source.height / 2 } : from;
    const b = ends ? { x: ends.target.x, y: ends.target.y + ends.target.height / 2 } : to;
    return `M ${a.x} ${a.y} C ${a.x + EDGE_REACH} ${a.y}, ${b.x - EDGE_REACH} ${b.y}, ${b.x} ${b.y}`;
  },
};

/**
 * What every box of the three readings wears: its kind, the stub's look for a task no task declares, and the
 * state a simulated run has put it in, which `useSimulatedRun` sets and `databricks.css` paints.
 */
const NODE_CLASSES: readonly ClassDeclaration[] = [
  { className: "databricks-node canvas-element", on: "element" },
  { className: { template: "databricks-node-{payload.kind}" }, on: "element" },
  { className: "databricks-node-missing", on: "element", when: { path: "payload.unresolved", is: "true" } },
  { className: { template: "databricks-sim-{payload.simulated}" }, on: "element", when: { path: "payload.simulated", is: "present" } },
  { className: "databricks-node-box canvas-node", on: "shape" },
];

/** A bundle's target: a dashed frame, reached by the outline's own class rather than a descendant `rect`. */
const TARGET_CLASSES: readonly ClassDeclaration[] = [
  { className: "databricks-frame", on: "element" },
  { className: "databricks-frame-outline" },
];

/** The box's name, which every node draws first; the second line is its badge strip. */
const NAME_LABELS: ReadonlySet<string> = new Set(["key", "name", "recipients"]);

/** A label's class: the target's name above its frame, a box's name, or a badge strip. */
function labelClassOf(type: string, id: string): string {
  if (type === "Target") {
    return id === "name" ? "databricks-frame-label" : "databricks-badges";
  }

  // A library's path is its name; an unknown resource's path is its badge, beneath its key.
  const isName = NAME_LABELS.has(id) || (id === "path" && type === "Library");
  return isName ? "databricks-label canvas-node-label" : "databricks-badges";
}

/** An action sending the key its menu entry writes, which is how the backend resolves it. */
function dispatched(action: ActionDeclaration, entry: { shortcut?: string }): ActionDeclaration {
  return entry.shortcut !== undefined ? { ...action, backendKey: entry.shortcut } : action;
}

/** The node height the rename's editor covers. */
const NODE_HEIGHT = 56;

export const DATABRICKS_BINDINGS: NotationBindings = {
  wireIds: "x-databricks",
  celPaths: {
    // A task's `run_if · cluster` and a job cluster's `version · node type · workers`, joined as the
    // backend joined them, with the parts that do not apply left out.
    "(has(self.runIf) && self.runIf != '' ? self.runIf + ' · ' : '') + (has(self.clusterKey) && self.clusterKey != '' ? self.clusterKey : 'serverless')": "payload.badgeLine",
    "[has(self.sparkVersion) ? self.sparkVersion : '', has(self.nodeType) ? self.nodeType : '', self.workers > 0 ? string(self.workers) + ' workers' : ''].filter(b, b != '').join(' · ')": "payload.badgeLine",
    "[self.serverless ? 'serverless' : '', self.continuous ? 'continuous' : '', self.development ? 'development' : '', has(self.channel) ? self.channel : ''].filter(b, b != '').join(' · ')": "payload.badgeLine",
    // A target's `mode · default · n overrides`.
    "cel.bind(n, self.outgoingOf('Override').size(), [has(self.mode) ? self.mode : '', self.isDefault ? 'default' : '', n == 1 ? '1 override' : (n > 1 ? string(n) + ' overrides' : '')].filter(s, s != '').join(' · '))": "payload.summary",
    // What the pipeline's destination, compute and notifications are called, which the backend works out.
    "diagram.nodesOfType('Pipeline').map(p, has(p.catalog) && p.catalog != '' && has(p.schema) && p.schema != '' ? p.catalog + '.' + p.schema : (has(p.catalog) && p.catalog != '' ? p.catalog : (has(p.schema) ? p.schema : '')))[0]": "payload.name",
    "diagram.nodesOfType('Pipeline').exists(p, p.serverless) ? 'serverless' : 'cluster'": "payload.name",
    "self.entries.map(e, e.recipients.join(', ')).filter(r, r != '').join(', ')": "payload.recipients",
    // A dependency's outcome is the connection's own label.
    "has(self.outcome) ? self.outcome : ''": "label",
  },
  classNames: (type) => (type === "Target" ? TARGET_CLASSES : NODE_CLASSES),
  labelClassName: (type, label) => labelClassOf(type, label.id),
  labelEditorBox: (_type, label) => (label.editable === "inline" ? { top: 0, height: NODE_HEIGHT } : undefined),
  relationClassName: (relation) => (relation === "Override" ? "databricks-override" : `databricks-edge databricks-edge-${relation === "Dependency" ? "depends" : "flow"}`),
  relationLineClassName: (relation) => (relation === "Override" ? "databricks-override-line" : undefined),
  endpointAnchors: (relation, end) => (end === "target" ? "edge" : relation === "Dependency" ? ["left", "right"] : []),
  customRoutes: { Dependency: layeredRoute, Flow: layeredRoute },
  action: dispatched,
  toolboxDrops: "module",
};

/**
 * THE SIMULATED ACTIONS, STANDING IN FOR RUNTIME GAP A6. Each definition states its mock run, deploy or
 * update as an operation whose body is a `simulate`, which the canvas library does not play yet. Until it
 * does, `useSimulatedRun.ts` plays them, and each is declared here as a menu entry the canvas runs itself:
 * the library hands it to the module's handler and sends nothing, so a simulation never reaches a command,
 * the history or a file. The ids are the definition's own wire ids, offered on the types the operation is for.
 */
function simulatedMenuActions(spec: DislDocument): ActionDeclaration[] {
  const wire = (spec as unknown as Record<string, { actions?: Record<string, string> } | undefined>)["x-databricks"]?.actions ?? {};
  return Object.entries(spec.behavior?.operations ?? {}).flatMap(([name, operation]): ActionDeclaration[] => {
    const declared = operation as { simulate?: unknown; for?: string | readonly string[] };
    const id = wire[name];
    if (declared.simulate === undefined || id === undefined) {
      return [];
    }

    const types = (Array.isArray(declared.for) ? declared.for : [declared.for]).map((type) => String(type).toLowerCase());
    return [{ id, invokedBy: [{ kind: "menu" }], appliesTo: [{ kind: "element", elementTypes: types }] }];
  });
}

/** A reading's canvas definition: its bundled definition compiled, with its simulated actions declared beside. */
export function compiledDefinition(text: string): DiagramDefinition {
  const spec = parseDisl(text);
  const compiled = compileNotation(spec, DATABRICKS_BINDINGS);
  return assertValidDiagramDefinition({ ...compiled, actions: [...(compiled.actions ?? []), ...simulatedMenuActions(spec)] });
}
