/**
 * Every id this module and its backend agree on, stated ONCE, here.
 *
 * The design says the action, property and shortcut ids are "defined once, in the client module,
 * and tasks 12 and 13 answer exactly those". This file is that once: the canvas reads these
 * constants and nothing else, and the backend's providers answer these strings. A second spelling
 * anywhere in this module is a defect.
 *
 * Element and relation ids are the document's own vocabulary - `ui-element`, `owns-action` - so a
 * `.fdg` file, the backend's `FdgElementTypes`/`FdgConnectionTypes` and the definition below all
 * read the same words.
 */

/** The diagram's mime type, which is its origin: `<vendor>/<diagram-type>`. */
export const FDG_MIME = "etalii/functional-decomposition-graph";

/** What the backend prefixes an element or connection type with on the wire. */
export const FDG_TYPE_PREFIX = `${FDG_MIME}+`;

/** The five element types, as the document names them. */
export const FdgElementTypes = {
  uiElement: "ui-element",
  dataElement: "data-element",
  action: "action",
  function: "function",
  comment: "comment",
} as const;

export type FdgElementType = (typeof FdgElementTypes)[keyof typeof FdgElementTypes];

export const FDG_ELEMENT_TYPES: readonly FdgElementType[] = Object.values(FdgElementTypes);

/** The five relation types, as the document names them. */
export const FdgRelationTypes = {
  uiChild: "ui-child",
  ownsAction: "owns-action",
  ownsData: "owns-data",
  ownsFunction: "owns-function",
  shows: "shows",
} as const;

export type FdgRelationType = (typeof FdgRelationTypes)[keyof typeof FdgRelationTypes];

export const FDG_RELATION_TYPES: readonly FdgRelationType[] = Object.values(FdgRelationTypes);

/**
 * The four relations a decomposition is made of, among which no directed cycle may be drawn.
 * `shows` is deliberately absent: it is navigation, and a navigation loop is legal (Requirement 5.4).
 */
export const FDG_OWNERSHIP_RELATIONS: readonly FdgRelationType[] = [
  FdgRelationTypes.uiChild,
  FdgRelationTypes.ownsAction,
  FdgRelationTypes.ownsData,
  FdgRelationTypes.ownsFunction,
];

/**
 * The context actions. Each runs against ONE target id: an element or connection id, a placement
 * `new:x,y` (the drop's centre), or a relation gesture `rel:from->to`.
 *
 * Add and connect are one id per type rather than one id with the type in the target, so the
 * target keeps the two shapes the resolver already reads and the type is never parsed out of it.
 */
export const FdgActions = {
  /** Adds an element of this type centred on the `new:x,y` target. */
  add: (type: FdgElementType) => `fdg.add.${type}`,
  /** Draws this relation for the `rel:from->to` target. */
  connect: (relation: FdgRelationType) => `fdg.connect.${relation}`,
  /** Removes the target element and every connection to or from it. */
  remove: "fdg.remove",
  /** Removes the target connection. */
  disconnect: "fdg.disconnect",
  /** Opens the inline editor on the target element's Name, or a Comment's text. */
  rename: "fdg.rename",
  /** Opens the inline editor on the target connection's name. */
  renameConnection: "fdg.rename-connection",
} as const;

/** Every action id the backend must answer - the list task 13's provider is checked against. */
export const FDG_ACTION_IDS: readonly string[] = [
  ...FDG_ELEMENT_TYPES.map((type) => FdgActions.add(type)),
  ...FDG_RELATION_TYPES.map((relation) => FdgActions.connect(relation)),
  FdgActions.remove,
  FdgActions.disconnect,
  FdgActions.rename,
  FdgActions.renameConnection,
];

/**
 * The properties, set on the selected element or connection through `setProperty`. A resize is
 * the only one the canvas sets itself; the rest are the property grid's.
 */
export const FdgProperties = {
  name: "fdg.name",
  text: "fdg.text",
  description: "fdg.description",
  width: "fdg.width",
  height: "fdg.height",
  connectionName: "fdg.connection-name",
} as const;

export const FDG_PROPERTY_IDS: readonly string[] = Object.values(FdgProperties);

/** The keys the declared actions listen for. The library derives its key set from these. */
export const FdgShortcuts = {
  rename: "F2",
} as const;
