import type { RowWindow } from "../rows/windowing";

/**
 * What the author did, in the table's own words. The library raises a gesture and knows nothing
 * of what it does to a document; the designer's backend session gives each kind its meaning, and
 * the host only carries it.
 */
/**
 * What an answer to a gesture starts with when it is a question and not a refusal: nothing was
 * done, and the same gesture made again with the setting `confirmed` set to `true` does it.
 */
export const CONFIRM_PREFIX = "confirm:";

/**
 * What an answer to a gesture starts with when it is a choice to make and not a refusal: nothing
 * was done, and what follows is the list to choose from, as JSON - each with its `id` and its
 * `label`. The same gesture made again with the chosen id in the setting it lacked does it.
 */
export const CHOOSE_PREFIX = "choose:";

/** Whether an answer to a gesture is a question to its author - to confirm, or to choose - and so not a refusal to report. */
export function isQuestion(answer: string): boolean {
  return answer.startsWith(CONFIRM_PREFIX) || answer.startsWith(CHOOSE_PREFIX);
}

/** The things an answer offers to choose from; none when it is not such an answer, or does not read as one. */
export function choicesOf(answer: string): { id: string; label: string }[] {
  if (!answer.startsWith(CHOOSE_PREFIX)) {
    return [];
  }
  try {
    const parsed: unknown = JSON.parse(answer.slice(CHOOSE_PREFIX.length));
    return Array.isArray(parsed)
      ? parsed.filter((choice): choice is { id: string; label: string } => typeof choice === "object" && choice !== null && typeof (choice as { id: unknown }).id === "string" && typeof (choice as { label: unknown }).label === "string")
      : [];
  } catch {
    return [];
  }
}

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
