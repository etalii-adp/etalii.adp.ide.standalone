/**
 * What a designer declares for the hierarchy surface to draw it (DESL section 7.7, spec 029 R9), as a table designer
 * declares its table: the labels its nodes carry, the icons of its notes, and which operation of its specification each
 * gesture sends. The library names none of these itself.
 */

/** One label: an attribute its nodes carry, drawn as rounded tags below the title. */
export interface HierarchySurfaceLabelDefinition {
  /** The attribute's name in the specification. */
  label: string;
  /** The word that introduces it in a query: `#`, `category:`. */
  prefix: string;
  /** Whether a node holds many values of it, or one. */
  many: boolean;
  /** The types that carry it. */
  on: readonly string[];
}

/** A control that adds a node: the operation it sends, with no params, and what the control says. */
export interface HierarchySurfaceAdd {
  operation: string;
  label: string;
}

/** Which operation of the specification each gesture sends. */
export interface HierarchySurfaceOperations {
  /** The control to the left of a title. */
  toggleCollapsed: string;
  /** Committing a title typed in place; its param is `title`. */
  setTitle: string;
  /** Committing a description typed in place; its param is `description`. */
  setDescription: string;
  /** The control at the end of the page, and the first level of an empty document: a level at the top. */
  addTopLevel: HierarchySurfaceAdd;
  /** The controls at the end of a level's content: an entry, and a level inside it. */
  addEntry: HierarchySurfaceAdd;
  addLevel: HierarchySurfaceAdd;
  /** Dropping a dragged level, with everything in it, or a dragged entry: its params are named by `moveParams`. */
  moveLevel: string;
  moveEntry: string;
  /** Delete on a selected node, with everything in it, asked first as `deletion` says. */
  delete: string;
}

/** The params of a move: the level it goes into (empty for the top), and the sibling it goes after (empty for none). */
export interface HierarchySurfaceMoveParams {
  into: string;
  after: string;
}

/** When a delete asks first, and in which words: the specification's `confirm` of its delete. */
export interface HierarchySurfaceDeletion {
  /** It asks when this many nodes, notes or texts concern the node, or more. */
  threshold: number;
  /** What it asks, for the node's number and that count. */
  message: (number: string, count: number) => string;
}

export interface HierarchySurfaceDefinition {
  /** The type the surface draws as boxes holding further nodes. */
  levelType: string;
  /** The type the surface draws inside the box of its level. */
  entryType: string;
  labels: readonly HierarchySurfaceLabelDefinition[];
  /** The icon of each note kind, by the kind as the model names it. */
  noteIcons: Readonly<Record<string, string>>;
  /** The icon that stands for several open notes. */
  severalNotesIcon: string;
  operations: HierarchySurfaceOperations;
  moveParams: HierarchySurfaceMoveParams;
  deletion: HierarchySurfaceDeletion;
  /**
   * The keys a selected node sends to the backend as a shortcut, with Ctrl or Cmd and the other modifiers held: which
   * action a chord runs is the backend's, so the page holds no table of them.
   */
  shortcutKeys: readonly string[];
}

/** The icon of a level whose deeper levels do not fit the page (spec 029 R10). */
export const HIDDEN_LEVELS_ICON = "mdi-chevron-double-right";

/** The icon of the control that makes a level the left edge of the page. */
export const FOCUS_ICON = "mdi-target";
