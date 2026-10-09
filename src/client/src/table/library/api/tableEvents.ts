import type { RowWindow } from "../rows/windowing";

/**
 * What the author did, in the table's own words. The library raises a gesture and knows nothing
 * of what it does to a document; the designer's backend session gives each kind its meaning, and
 * the host only carries it.
 */
export interface TableGesture {
  /** What was done, e.g. `setCell`. */
  kind: string;
  rowId?: string;
  columnId?: string;
  viewId?: string;
  /** The value or values given, in their written form. */
  values?: readonly string[];
  /** A position, for a gesture that places something. */
  index?: number;
  /** A second thing it names: what something is placed beside. */
  targetId?: string;
  /** Named settings of the gesture. */
  settings?: Readonly<Record<string, string>>;
}

/** What the table surface raises. A handler that is not given is a thing the table does not offer. */
export interface TableEvents {
  /** The rows in sight changed, margin included: which lines the surface now wants. */
  onWindow?: (window: RowWindow) => void;
  /** The author chose another view. */
  onView?: (viewId: string) => void;
  /**
   * The author did something that changes the table. Resolves to the backend's refusal as a
   * sentence, or to `""` when the edit was accepted; an editor shows a refusal and stays open.
   */
  onGesture?: (gesture: TableGesture) => Promise<string>;
}
