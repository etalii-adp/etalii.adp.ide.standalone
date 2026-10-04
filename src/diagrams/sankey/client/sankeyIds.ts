/**
 * Every id this module and its backend agree on, stated ONCE, here.
 *
 * The backend's toolbox, action and property providers answer exactly these strings; a second
 * spelling anywhere in this module is a defect. The words are domain-agnostic on purpose: a
 * Sankey diagram draws money, energy, people or anything else that is conserved as it flows, so
 * what a node stands for is the document's business and the diagram only knows nodes and flows.
 */

/** The diagram's mime type, which is its origin: `<vendor>/<diagram-type>`. */
export const SANKEY_MIME = "etalii/sankey";

/** What the backend prefixes an element or connection type with on the wire. */
export const SANKEY_TYPE_PREFIX = `${SANKEY_MIME}+`;

/** The one element: something a quantity flows into, out of, or through. */
export const NODE_TYPE = "node";

/** The one relation: a quantity moving from one node to another. */
export const FLOW_TYPE = "flow";

/**
 * The palette words a node or a flow may name, in the order the property grid offers them - the
 * backend's `SankeyColors.Palette`, word for word. Each is a theme token in `index.css`, readable
 * in both themes; a document may state a `#rrggbb` instead, which is painted as written.
 */
export const SANKEY_COLORS: readonly string[] = ["grey", "slate", "purple", "blue", "teal", "green", "lime", "yellow", "orange", "red", "pink", "brown"];

/** The word a node that states no colour is drawn in. */
export const DEFAULT_COLOR = "grey";

/** Where a node's labels sit: before the bar in the first column, after it everywhere else. */
export const SankeySides = {
  left: "left",
  right: "right",
} as const;

export const SankeyActions = {
  /** Adds a node at the `new:x,y` target: in the column nearest x, above the first node below y. */
  add: "sankey.add",
  /** Draws a flow for the `rel:from->to` target, carrying on what the source has not passed on. */
  connect: "sankey.connect",
  /** Adds one step to a flow's value. */
  increase: "sankey.increase",
  /** Takes one step off a flow's value, never below zero. */
  decrease: "sankey.decrease",
  /** Asks for a node's new name. */
  rename: "sankey.rename",
  /** Removes a node with its flows, or a flow. */
  remove: "sankey.remove",
  /** Draws every bar and band thicker. */
  thicker: "sankey.thicker",
  /** Draws every bar and band thinner. */
  thinner: "sankey.thinner",
} as const;

/** Every action id the backend must answer. */
export const SANKEY_ACTION_IDS: readonly string[] = Object.values(SankeyActions);

/** The property grid's rows. The canvas sets none of them itself. */
export const SankeyProperties = {
  name: "sankey.name",
  color: "sankey.color",
  customColor: "sankey.custom-color",
  note: "sankey.note",
  format: "sankey.format",
  column: "sankey.column",
  description: "sankey.description",
  value: "sankey.value",
  step: "sankey.step",
} as const;

export const SANKEY_PROPERTY_IDS: readonly string[] = Object.values(SankeyProperties);
