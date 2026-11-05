/**
 * The read model of a hierarchy surface (DESL section 7.7), as the library draws it: everything one page of nested
 * boxes shows, sent whole on open and after every change. It knows no designer type: a level, an entry, a label and a
 * note are whatever the designer's declaration names.
 */

/** The role a node plays: a level holds further nodes and is drawn as a box with them to its right; an entry holds none. */
export type HierarchySurfaceRole = "level" | "entry";

/** A number a text names, where it stands in that text. */
export interface HierarchySurfaceReference {
  /** Its offset, in UTF-16 code units. */
  start: number;
  /** The length of the bracketed number, brackets included. */
  length: number;
  /** The node it names, or empty when it names none. */
  targetId: string;
}

/** The values a node carries of one label, in the order of its line. */
export interface HierarchySurfaceLabelValues {
  label: string;
  values: readonly string[];
}

export interface HierarchySurfaceNode {
  /** What a gesture names it by: its number when usable, otherwise `line/<n>`, valid for this model only. */
  id: string;
  /** Its number as written, a placeholder included. */
  number: string;
  role: HierarchySurfaceRole;
  /** The specification's type. */
  type: string;
  /** The level it stands in, or empty at the top. */
  parentId: string;
  title: string;
  description: string;
  labels: readonly HierarchySurfaceLabelValues[];
  references: readonly HierarchySurfaceReference[];
  /** Whether the document keeps it collapsed. */
  collapsed: boolean;
}

export interface HierarchySurfaceOption {
  id: string;
  name: string;
  /** One of the ten colour names. */
  colour: string;
  /** Carried by a node, not held by the list. */
  implicit: boolean;
}

export interface HierarchySurfaceOptionList {
  label: string;
  options: readonly HierarchySurfaceOption[];
}

export interface HierarchySurfaceFilter {
  id: string;
  name: string;
  query: string;
}

export interface HierarchySurfaceNote {
  /** Its place among the notes, valid for this model only. */
  id: string;
  kind: string;
  /** The number it is about, as written. */
  about: string;
  /** The node it is about, or empty when it names none. */
  aboutId: string;
  text: string;
  detail: string;
  resolved: boolean;
  references: readonly HierarchySurfaceReference[];
}

export interface HierarchySurfaceFinding {
  code: string;
  severity: "error" | "warning" | "info";
  message: string;
  /** The node it stands at, or empty. */
  nodeId: string;
  /** The 1-based line it stands at. */
  line: number;
}

export interface HierarchySurfaceModel {
  title: string;
  /** Why nothing can be edited, or empty when the document can be. */
  readOnlyReason: string;
  /** What this model follows: `open`, `edit` or `outside`. */
  cause: string;
  /** Every level and entry, in the file's order. */
  nodes: readonly HierarchySurfaceNode[];
  optionLists: readonly HierarchySurfaceOptionList[];
  filters: readonly HierarchySurfaceFilter[];
  notes: readonly HierarchySurfaceNote[];
  findings: readonly HierarchySurfaceFinding[];
  /** The widths the user set of the columns of levels, by depth; a column not listed has the page's own. */
  widths: readonly HierarchySurfaceColumnWidth[];
}

/** The width the user set of one column of levels (DESL section 7.7.1). */
export interface HierarchySurfaceColumnWidth {
  id: string;
  /** The depth of the levels the column holds, 1 for the top ones. */
  level: number;
  /** In pixels. */
  width: number;
}

/** The page before anything arrived. */
export const EMPTY_HIERARCHY_SURFACE: HierarchySurfaceModel = {
  title: "",
  readOnlyReason: "",
  cause: "open",
  nodes: [],
  optionLists: [],
  filters: [],
  notes: [],
  findings: [],
  widths: [],
};
