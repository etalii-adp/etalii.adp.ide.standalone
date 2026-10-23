import type { HierarchySurfaceNode } from "../api/hierarchySurfaceModel";

/** Which text of a node a field is open on. */
export type EntryTextAttribute = "title" | "description";

/** The field open on the page: the node, which of its texts, and what has been typed so far. */
export interface OpenField {
  nodeId: string;
  attribute: EntryTextAttribute;
  text: string;
  /** What the field is, for a screen reader: "Title of REQ-0004". Kept with the field, which can outlive its box. */
  label: string;
}

/** What a box needs to offer its texts for typing, and to show the field when one of them is open. */
export interface EntryEditing {
  /** The field open on this box, or null. */
  field: OpenField | null;
  open: (attribute: EntryTextAttribute) => void;
  change: (text: string) => void;
  commit: () => void;
  cancel: () => void;
  /** What a reference typed in a description can name: the document's nodes that have a number. */
  references: readonly HierarchySurfaceNode[];
}
