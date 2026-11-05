import { ADD_NOTE_ICON } from "../definition/hierarchySurfaceDefinition";
import type { NoteMark } from "./noteMark";

export interface NoteIconProps {
  /** The mark of the open notes the box stands for, or null when it has none. */
  mark: NoteMark | null;
  /** The node's number, for a screen reader. */
  number: string;
  /** What the add control says: "New assessment". */
  addLabel: string;
  /** Whether a note can be added: false while the document cannot be changed. */
  canAdd: boolean;
  /** Whether the box's list is open. */
  open: boolean;
  onToggle: () => void;
}

/**
 * The icon at a box's top right (FR-015): the kind's icon for one open note, the icon for several with their count, and
 * with none, the way to add the first. It opens the box's list of notes; with nothing to show and nothing to add, it is
 * not there.
 */
export function NoteIcon({ mark, number, addLabel, canAdd, open, onToggle }: NoteIconProps) {
  if (mark === null && !canAdd) {
    return null;
  }
  const label = mark === null ? `${addLabel} on ${number}` : mark.label;
  return (
    <button
      type="button"
      className={`mdi ${mark === null ? `${ADD_NOTE_ICON} hs-note-add` : `${mark.icon} hs-note-mark`}`}
      aria-label={label}
      aria-expanded={open}
      aria-haspopup="dialog"
      title={label}
      onClick={(event) => {
        event.stopPropagation();
        onToggle();
      }}
    >
      {mark !== null && mark.count > 1 && <span className="hs-note-count">{mark.count}</span>}
    </button>
  );
}
