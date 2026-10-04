/**
 * Every id this module and its backend agree on, stated ONCE, here. The backend's providers answer
 * exactly these strings; a second spelling anywhere in this module is a defect.
 *
 * The kind ids keep the behavior tree literature's names - `sequence`, `fallback` - while the
 * Markdown and the canvas say what they mean to an agent: "Do in order", "Try in order".
 */

/** The diagram's mime type, which is its origin: `<vendor>/<diagram-type>`. */
export const ABM_MIME = "etalii/agent-behavior-modelling";

/** What the backend prefixes an element or connection type with on the wire. */
export const ABM_TYPE_PREFIX = `${ABM_MIME}+`;

/** The eleven kinds of node, as the wire names them. */
export const AbmNodeKinds = {
  sequence: "sequence",
  fallback: "fallback",
  parallel: "parallel",
  retry: "retry",
  repeat: "repeat",
  guard: "guard",
  approval: "approval",
  check: "check",
  action: "action",
  ask: "ask",
  delegate: "delegate",
} as const;

export type AbmNodeKind = (typeof AbmNodeKinds)[keyof typeof AbmNodeKinds];

export const ABM_NODE_KINDS: readonly AbmNodeKind[] = Object.values(AbmNodeKinds);

/** What each kind may hold beneath it - the family its shape and colour come from. */
export const ABM_CATEGORY: Readonly<Record<AbmNodeKind, "composite" | "decorator" | "leaf">> = {
  sequence: "composite",
  fallback: "composite",
  parallel: "composite",
  retry: "decorator",
  repeat: "decorator",
  guard: "decorator",
  approval: "decorator",
  check: "leaf",
  action: "leaf",
  ask: "leaf",
  delegate: "leaf",
};

/** The one relation: a parent's line to each of its children. */
export const ABM_CHILD_RELATION = "child";

/** What every add action id starts with; the rest is the node kind. */
export const ABM_ADD_ACTION_PREFIX = "abm.add.";

/**
 * The context actions. Each runs against ONE target id: a node id, a placement `new:x,y` (a drop's
 * centre), or a relation gesture `rel:from->to` (a parent line drawn from the parent to the child).
 */
export const AbmActions = {
  /** Adds a node of this kind: a child of the target node, or at the `new:x,y` drop. */
  add: (kind: AbmNodeKind) => `${ABM_ADD_ACTION_PREFIX}${kind}`,
  /** Moves the gesture's child, with its subtree, to be the last child of the gesture's parent. */
  connectChild: "abm.connect.child",
  rename: "abm.rename",
  remove: "abm.remove",
  moveEarlier: "abm.move-earlier",
  moveLater: "abm.move-later",
  editNotes: "abm.edit-notes",
  /** Forgets every dragged position, so the whole tree is drawn tidy again. */
  arrange: "abm.arrange",
} as const;

/** Every action id the backend must answer. */
export const ABM_ACTION_IDS: readonly string[] = [
  ...ABM_NODE_KINDS.map((kind) => AbmActions.add(kind)),
  AbmActions.connectChild,
  AbmActions.rename,
  AbmActions.remove,
  AbmActions.moveEarlier,
  AbmActions.moveLater,
  AbmActions.editNotes,
  AbmActions.arrange,
];

/** The properties of a selected node, set through the property grid. */
export const AbmProperties = {
  kind: "abm.kind",
  label: "abm.label",
  attempts: "abm.attempts",
  notes: "abm.notes",
  place: "abm.place",
} as const;

export const ABM_PROPERTY_IDS: readonly string[] = Object.values(AbmProperties);

/** The keys the declared actions listen for. */
export const AbmShortcuts = {
  rename: "F2",
  moveEarlier: "Alt+Up",
  moveLater: "Alt+Down",
} as const;

/**
 * The least space a dragged row keeps between its parent's bottom and its own top: the backend's
 * `AbmLayout.MinimumGap`, so the canvas shows a drop where the backend will put it.
 */
export const ABM_MINIMUM_GAP = 16;
