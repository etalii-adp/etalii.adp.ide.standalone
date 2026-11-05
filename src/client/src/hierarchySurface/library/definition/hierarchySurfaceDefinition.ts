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
  /**
   * How its values look, so labels side by side tell apart: a rounded tag written with `#`, a square badge, or a rounded
   * status with a dot. Absent, a label of many values is a tag and one of one value a badge.
   */
  look?: HierarchySurfaceLabelLook;
  /** The types that carry it. */
  on: readonly string[];
  /** How its values and its list are changed on the page (FR-011), for a label of many values; absent for one only shown. */
  edit?: HierarchySurfaceLabelEdit;
  /** How its value and its list are changed on the page, for a label of one value; absent for one only shown. */
  pick?: HierarchySurfaceLabelPick;
}

/** How a label's values look on the page. */
export type HierarchySurfaceLabelLook = "tag" | "badge" | "status";

/**
 * How a label of many values is changed: a field below the title that searches the label's list, picks and creates, and
 * the list's editor, where a member is renamed, recoloured, put in its place and deleted. Each gesture sends one
 * operation of the specification; an add or a remove names the node, the others the member of the list.
 */
export interface HierarchySurfaceLabelEdit {
  operations: {
    /** A value the node carries from now on, picked or created by its name; its param is the name. */
    add: string;
    /** A value the node no longer carries; its param is the name. */
    remove: string;
    /** A member renamed, and every node that carries it with it; its param is the name. */
    rename: string;
    /** A member's colour; its param is the colour. */
    recolour: string;
    /** A member put after another, or first without one; its param is the member it goes after. */
    move: string;
    /** A member deleted, and taken from every node that carries it, asked first as `deletion` says. */
    delete: string;
  };
  /** The names of the params: a value's name, a member's colour, and the member a moved one goes after. */
  params: { name: string; colour: string; after: string };
  /** What the field and the list say: the list's name, and the words of an add and a remove. */
  words: { list: string; add: string; remove: string };
  /** When deleting a member asks first, and in which words: the specification's `confirm` of its delete. */
  deletion: { threshold: number; message: (name: string, count: number) => string };
}

/**
 * How a label of one value is changed: its value, or the way to give one, opens the label's list, where a member is
 * picked or the value cleared, and from where the list is edited in the options editor, a new member added there too.
 * A pick and a clear name the node; an add names the document; the others the member of the list.
 */
export interface HierarchySurfaceLabelPick {
  operations: {
    /** The node's value from now on, a member picked by its name; its param is `params.value`. */
    set: string;
    /** The node's value taken off. */
    clear: string;
    /** A new member of the list; its params are the name and the colour. */
    add: string;
    rename: string;
    recolour: string;
    move: string;
    /** A member deleted, and cleared from every node that carries it, asked first as `deletion` says. */
    delete: string;
  };
  /** The names of the params: the picked value, a member's name and colour, and the member a moved one goes after. */
  params: { value: string; name: string; colour: string; after: string };
  /** What the control and the list say: the list's name, the value's own name, and the words of a clear. */
  words: { list: string; value: string; clear: string };
  deletion: { threshold: number; message: (name: string, count: number) => string };
}

/**
 * What the page's filters are stored, renamed and removed by (FR-048): a store names the document, the others the stored
 * filter; and the keys a sort offers besides the labels of one value, in the query's notation (DESL section 7.7.3).
 */
export interface HierarchySurfaceFilters {
  operations: { store: string; rename: string; remove: string };
  params: { name: string; query: string };
  /** The keys of `sort:` that are not labels, with what the menu calls them: `number`, `title`. */
  sortKeys: readonly { key: string; label: string }[];
  /** What the bar's count calls the entries: `requirements`. */
  entries: string;
}

/**
 * How the widths of the columns of levels are kept (DESL section 7.7.1): the operation that gives a column its first
 * width, with the column's depth and the width as params, the one that changes a kept width and the one that drops it,
 * both sent to the column's id, and the range a width is held to.
 */
export interface HierarchySurfaceWidths {
  operations: { add: string; set: string; reset: string };
  params: { level: string; width: string };
  min: number;
  max: number;
}

/** A control that adds a node: the operation it sends, with no params, what the control says and the icon it shows. */
export interface HierarchySurfaceAdd {
  operation: string;
  label: string;
  /** The control is this icon alone, its label the tooltip. */
  icon: string;
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

/**
 * What the notes of a node are made, changed and taken away by: the operation each gesture of the list sends, the
 * params they carry, and the words of the list. An add names the node, the others the note.
 */
export interface HierarchySurfaceNotes {
  operations: {
    /** A new open note about the node; its params are the kind and the text. */
    add: string;
    /** A note's kind, text and detail written anew. */
    change: string;
    /** A note resolved, or reopened when it is resolved. */
    resolve: string;
    remove: string;
  };
  /** The names of the params: the kind, as the model names it, the one-line text and the lines below it. */
  params: { kind: string; text: string; detail: string };
  /** What the list and its controls say. */
  words: { list: string; add: string; change: string; resolve: string; reopen: string; remove: string };
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
  /** The notes' list, opened from the icon at a box's top right (FR-015, FR-016). */
  notes: HierarchySurfaceNotes;
  moveParams: HierarchySurfaceMoveParams;
  deletion: HierarchySurfaceDeletion;
  /** The filter bar's stored filters and sort keys; absent, the page offers no filter bar. */
  filters?: HierarchySurfaceFilters;
  /** How the columns' widths are kept; absent when the page's own widths are not changed. */
  widths?: HierarchySurfaceWidths;
  /**
   * The keys a selected node sends to the backend as a shortcut, with Ctrl or Cmd and the other modifiers held: which
   * action a chord runs is the backend's, so the page holds no table of them.
   */
  shortcutKeys: readonly string[];
}

/** The icon of a level whose deeper levels do not fit the page (spec 029 R10). */
export const HIDDEN_LEVELS_ICON = "mdi-chevron-double-right";

/** The icon at a box's top right while no open note is about it: the way to add the first. */
export const ADD_NOTE_ICON = "mdi-comment-plus-outline";

/** The icon of the control that makes a level the left edge of the page. */
export const FOCUS_ICON = "mdi-target";

/** The icon of the control at a selected box's bottom right that deletes its node. */
export const DELETE_ICON = "mdi-delete-outline";
