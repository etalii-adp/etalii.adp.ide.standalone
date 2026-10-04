/**
 * Every id this module and its backend agree on, stated ONCE, here.
 *
 * The backend's toolbox, action and property providers answer exactly these strings; a second
 * spelling anywhere in this module is a defect. Stage and type words are the document's own, so a
 * `.supply` file, the backend's `SupplyChainNodeTypes` and the definition read the same words.
 */

/** The diagram's mime type, which is its origin: `<vendor>/<diagram-type>`. */
export const SUPPLY_CHAIN_MIME = "etalii/supply-chain";

/** What the backend prefixes an element or connection type with on the wire. */
export const SUPPLY_CHAIN_TYPE_PREFIX = `${SUPPLY_CHAIN_MIME}+`;

/**
 * The seven stages a node may be, in the order goods move through them. They name roles, not
 * industries; the backend reads a document's older words (`raw-material`, `supplier`, …) as these,
 * so only these ever reach the client.
 */
export const SupplyChainStages = {
  source: "source",
  processor: "processor",
  producer: "producer",
  integrator: "integrator",
  hub: "hub",
  outlet: "outlet",
  consumer: "consumer",
} as const;

export type SupplyChainStage = (typeof SupplyChainStages)[keyof typeof SupplyChainStages];

export const SUPPLY_CHAIN_STAGES: readonly SupplyChainStage[] = Object.values(SupplyChainStages);

/** The frame a group is drawn as. */
export const GROUP_TYPE = "group";

/** The one relation: goods moving from a supplier to a consumer. */
export const FLOW_TYPE = "flow";

/**
 * The two client-only element types: the + and - beside a selected node's quantity or a selected
 * flow's volume. The backend never sends them; the canvas derives them from the trace it does send.
 */
export const StepperTypes = {
  up: "step-up",
  down: "step-down",
} as const;

/** What every add action id starts with; the rest is the stage. */
export const SUPPLY_CHAIN_ADD_ACTION_PREFIX = "supply-chain.add.";

export const SupplyChainActions = {
  /** Adds a node of this stage centred on the `new:x,y` target. */
  add: (stage: SupplyChainStage) => `${SUPPLY_CHAIN_ADD_ACTION_PREFIX}${stage}`,
  /** Adds an empty group whose frame is centred on the `new:x,y` target. */
  addGroup: "supply-chain.add-group",
  /** Draws a flow for the `rel:from->to` target. */
  connect: "supply-chain.connect",
  /** Adds one step to a node's quantity or a flow's volume. */
  increase: "supply-chain.increase",
  /** Takes one step off a node's quantity or a flow's volume, never below zero. */
  decrease: "supply-chain.decrease",
  /** Opens the inline editor on a node's or group's name, or a flow's product. */
  rename: "supply-chain.rename",
  /** Asks for a new group's name and puts the node in it. */
  group: "supply-chain.group",
  /** Removes a node with its flows, a flow, or a group (its members stay). */
  remove: "supply-chain.remove",
  /** Lays the whole diagram out again, left to right. */
  arrange: "supply-chain.arrange",
} as const;

/** Every action id the backend must answer. */
export const SUPPLY_CHAIN_ACTION_IDS: readonly string[] = [
  ...SUPPLY_CHAIN_STAGES.map((stage) => SupplyChainActions.add(stage)),
  SupplyChainActions.addGroup,
  SupplyChainActions.connect,
  SupplyChainActions.increase,
  SupplyChainActions.decrease,
  SupplyChainActions.rename,
  SupplyChainActions.group,
  SupplyChainActions.remove,
  SupplyChainActions.arrange,
];

/** The property grid's rows. The canvas sets none of them itself. */
export const SupplyChainProperties = {
  name: "supply-chain.name",
  stage: "supply-chain.stage",
  group: "supply-chain.group",
  quantity: "supply-chain.quantity",
  product: "supply-chain.product",
  volume: "supply-chain.volume",
  unit: "supply-chain.unit",
  step: "supply-chain.step",
  description: "supply-chain.description",
} as const;

export const SUPPLY_CHAIN_PROPERTY_IDS: readonly string[] = Object.values(SupplyChainProperties);

/** The markings the backend's trace gives everything while something is selected. */
export const SupplyChainTraces = {
  selected: "selected",
  upstream: "upstream",
  downstream: "downstream",
  related: "related",
  dimmed: "dimmed",
} as const;
