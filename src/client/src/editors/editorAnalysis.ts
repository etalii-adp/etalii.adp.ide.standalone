/**
 * What an editor module's analysis answers about a text, in the one shape the shared panel
 * presents (json-editor Requirement 9.3 and 9.4). A module hands an {@link EditorAnalysis} to
 * `TextEditorPanel`; it never draws an underline, a list or a tooltip of its own. Nothing here
 * names a format: JSON, YAML and XML each answer through the same interface.
 *
 * Offsets are into the editor's text as CodeMirror holds it, which is LF-only (see
 * `TextEditorPanel`'s `normalizeLineEndings`), not into the file's bytes.
 */

/** How serious a finding is. Carried by a line style and a gutter mark as well as by colour. */
export type EditorFindingSeverity = "error" | "warning" | "info";

/** One thing an analysis found in a text. */
export interface EditorFinding {
  from: number;
  to: number;
  severity: EditorFindingSeverity;
  /** The sentence shown on hover and from the keyboard. Always set as text, never as markup. */
  message: string;
  /** Who found it: "json", "json-schema", ... */
  source: string;
}

/** One item a completion list offers. */
export interface EditorCompletion {
  /** What the list shows. */
  label: string;
  /**
   * What typing is matched against, when it differs from the label: a property offered as
   * `name` replaces a range that starts at its opening quote, so the text typed so far is
   * `"na` and only `"name"` matches it. Defaults to the label.
   */
  filterText?: string;
  /** Shown beside the label. */
  detail?: string;
  /** A longer description, shown beside the list as text. */
  info?: string;
  /** The range the insert replaces. */
  from: number;
  to: number;
  /** What is inserted. `${1}`, `${2:default}` and `${0}` are tab stops, applied as a snippet. */
  insert: string;
  /** Sorts an item up the list: required properties first. */
  boost?: number;
}

export interface EditorCompletionList {
  items: EditorCompletion[];
}

/** Text describing what is at a range, shown on hover and from the keyboard. */
export interface EditorHover {
  from: number;
  to: number;
  /** Plain text. Set as a text node, never as markup. */
  text: string;
}

/**
 * Everything a module knows about its format, behind one asynchronous interface so that moving
 * the work to a worker later changes the module and nothing shared.
 */
export interface EditorAnalysis {
  /** The findings for this text. An answer for a text that is no longer shown is dropped. */
  findings(text: string): Promise<EditorFinding[]>;
  /** What may be written at `offset`; `explicit` when the user asked with Ctrl+Space. */
  completions?(text: string, offset: number, explicit: boolean): Promise<EditorCompletionList | null>;
  /** What is at `offset`, described. */
  hover?(text: string, offset: number): Promise<EditorHover | null>;
  /** What the status line says after the counts: a schema's title, "No schema", or why nothing is checked. */
  status?(text: string): string;
}

/** The counts the status line shows. */
export interface EditorFindingCounts {
  errors: number;
  warnings: number;
}

/** "2 errors, 1 warning", or "" when there are none (json-editor Requirement 4.5). */
export function describeFindingCounts({ errors, warnings }: EditorFindingCounts): string {
  const parts: string[] = [];
  if (errors > 0) {
    parts.push(`${errors} ${errors === 1 ? "error" : "errors"}`);
  }
  if (warnings > 0) {
    parts.push(`${warnings} ${warnings === 1 ? "warning" : "warnings"}`);
  }
  return parts.join(", ");
}
