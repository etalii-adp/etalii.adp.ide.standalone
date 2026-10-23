import type { MouseEvent } from "react";

/** The bar's class, by which the field knows the focus left for the bar rather than the page. */
export const CONFLICT_BAR = "hs-conflict";

export interface ConflictBarProps {
  /** Closes the open field and drops what was typed in it. */
  onShowFile: () => void;
  /** Leaves the field open; committing it is a new edit of the file as it is now. */
  onKeepText: () => void;
}

/**
 * The bar over the page when another program changed the file while a title or description was being typed (spec 029
 * R10, R11, FR-023), in the shape of the text editors' conflict bar. It offers both sides and never drops the text by
 * itself.
 */
export function ConflictBar({ onShowFile, onKeepText }: ConflictBarProps) {
  // A press keeps the focus in the field: leaving it commits the text, which is what the buttons decide on.
  const keepFocus = (event: MouseEvent) => event.preventDefault();
  return (
    <div className={CONFLICT_BAR} role="alert">
      <span>This file was changed outside the designer.</span>
      <button type="button" onMouseDown={keepFocus} onClick={onShowFile}>
        Show the file
      </button>
      <button type="button" onMouseDown={keepFocus} onClick={onKeepText}>
        Keep my text
      </button>
    </div>
  );
}
