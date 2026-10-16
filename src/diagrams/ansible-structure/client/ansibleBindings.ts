import { forwardBezierPath } from "@client/canvas/connectors";
import type { ActionDeclaration, ActionInvocation } from "@client/canvas/library/definition/actions";
import type { ClassDeclaration, CustomRouteRef } from "@client/canvas/library/definition/diagramDefinition";
import type { NotationBindings } from "@client/canvas/library/disl/compileNotation";

/**
 * What the Ansible structure diagram's canvas needs beside its bundled specification, because the
 * library cannot read it from there. `compileNotation` derives everything else from
 * `definition/ansible-structure.dis`: the six node types with the notation they inherit from
 * `AnsibleNode`, their labels, tooltips and accessible names, the five relations with what they
 * inherit from `AnsibleRelation`, their ends, markers, labels and dashes, the stub an unresolved
 * target is drawn as, the play's colour chosen by its index, the reveal a double-click and Enter run,
 * the empty-canvas sentence and the manual layout.
 *
 * <b>Why each entry is code.</b>
 * - **The CEL paths.** The library has no CEL, so what the specification computes - a node's tooltip,
 *   a relation's directive and condition, a stub's words - is composed by the canvas into the payload
 *   (`title`, `text`) or the connection's label.
 * - **The class names.** They tie the drawing to `ansible-structure.css`, which paints each kind's
 *   outline and fill, the hollow role and the six play hues mixed into the raised surface; a test
 *   proves the stylesheet's hues equal the specification's tokens.
 * - **The route.** DISL's `bezier` leaving right and entering left says the curve's directions; how
 *   far it reaches, and its way round for a backward relation, is the shared forward Bezier.
 * - **Space**, which reveals a file as Enter does: an operation's one `shortcut` cannot state a
 *   second key (`ansible-structure.md`, gap 9).
 * - **The role and the tab order**, which DISL leaves to the runtime (6.15).
 */

/**
 * One relationship: out of the source's right side, into the target's left side, curving horizontally
 * through the corridor between the columns. The forward bezier loops around for the rare backward edge.
 */
export const ansibleRoute: CustomRouteRef = {
  customRoute: "ansible-forward-bezier",
  path: (from, to, _waypoints, ends) => {
    const a = ends ? { x: ends.source.x + ends.source.width, y: ends.source.y + ends.source.height / 2 } : from;
    const b = ends ? { x: ends.target.x, y: ends.target.y + ends.target.height / 2 } : to;
    return forwardBezierPath(a, b);
  },
};

/** The stylesheet's class for each node type's kind. */
const KIND_CLASSES: Readonly<Record<string, string>> = {
  Playbook: "playbook",
  Play: "play",
  Role: "role",
  TaskFile: "taskfile",
  Inventory: "inventory",
  VariableFolder: "vars",
};

/** The stylesheet's class for each relation. */
const RELATION_CLASSES: Readonly<Record<string, string>> = {
  UsesRole: "uses-role",
  ImportsPlaybook: "imports-playbook",
  IncludesTasks: "includes-tasks",
  DependsOn: "depends-on",
  Targets: "targets",
};

/** The condition the specification marks a role with nothing Ansible would run by, and what the payload carries for it. */
const HOLLOW = "self.hollow";

function classNamesOf(type: string, node: { conditions?: readonly { when: string }[] }): readonly ClassDeclaration[] {
  const kind = KIND_CLASSES[type];
  if (kind === undefined) {
    throw new Error(`ansibleBindings: the node type "${type}" has no class in ansible-structure.css.`);
  }

  const hollow = (node.conditions ?? []).some((condition) => condition.when === HOLLOW);
  return [
    // The whole element, labels and all: the group the library makes focusable and hovers, which the
    // hover and focus rules anchor on. Every other class lands on the box itself.
    { className: "ansible-node", on: "element" },
    { className: `ansible-node-${kind}` },
    ...(hollow ? [{ className: "ansible-node-hollow", when: { path: "payload.hollow", is: "true" } } as const] : []),
    { className: "ansible-node-box", on: "shape" },
  ];
}

function relationClassOf(relation: string): string {
  const kind = RELATION_CLASSES[relation];
  if (kind === undefined) {
    throw new Error(`ansibleBindings: the relation "${relation}" has no class in ansible-structure.css.`);
  }
  return `ansible-edge ansible-edge-${kind}`;
}

/** Space reveals a file as Enter does, which an operation's one shortcut cannot state (gap 9). */
const SPACE: ActionInvocation = { kind: "shortcut", key: " " };

function withSpace(action: ActionDeclaration): ActionDeclaration {
  const enter = action.invokedBy.some((invocation) => invocation.kind === "shortcut" && invocation.key === "Enter");
  return enter ? { ...action, invokedBy: [...action.invokedBy, SPACE] } : action;
}

const TITLE = "payload.title";

export const ANSIBLE_BINDINGS: NotationBindings = {
  celPaths: {
    "self.hosts != '' ? 'Playbook ' + self.name + ' — hosts: ' + self.hosts : 'Playbook ' + self.name": TITLE,
    "self.hosts != '' ? 'Play ' + self.name + ' — hosts: ' + self.hosts : 'Play ' + self.name": TITLE,
    "'Role ' + self.name": TITLE,
    "'Task file ' + self.name": TITLE,
    "'Inventory ' + self.name": TITLE,
    "'Variables ' + self.name": TITLE,
    // The directive and its condition, composed into the connection's own label.
    "self.condition != '' ? enumLabel('Directive', self.directive) + ' when ' + self.condition : enumLabel('Directive', self.directive)": "label",
    "self.targetAsWritten + (self.resolution == 'expression' ? ' (expression)' : ' (missing)')": "payload.text",
  },
  classNames: classNamesOf,
  labelClassName: () => "ansible-node-label",
  relationClassName: relationClassOf,
  relationLineClassName: () => "ansible-edge-line",
  // A relation is never drawn by hand: no end starts one, and a target attaches by its edge.
  endpointAnchors: (_relation, end) => (end === "source" ? [] : "edge"),
  builtInShapes: { roundedRect: "box" },
  customRoutes: { UsesRole: ansibleRoute, ImportsPlaybook: ansibleRoute, IncludesTasks: ansibleRoute, DependsOn: ansibleRoute, Targets: ansibleRoute },
  // A target that is missing or an expression: drawn in the warning colour, its words above it.
  stubClassName: () => "ansible-edge-unresolved",
  edgeLabelClassName: () => "ansible-edge-label",
  // A play's hue and tint are one of six slots, wrapped by its index.
  indexedColourClassName: (_type, token) => (token === "color.play.hue" ? "ansible-play-" : undefined),
  action: withSpace,
  focusable: true,
  accessibleRole: "button",
};
