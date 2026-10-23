import type { HierarchySurfaceNote } from "./api/hierarchySurfaceModel";
import type { HierarchySurfaceDefinition } from "./definition/hierarchySurfaceDefinition";

/** The icon a node shows for its open notes, and what its tooltip says; null when it has none. */
export interface NoteMark {
  icon: string;
  count: number;
  label: string;
}

/**
 * The mark of the open notes about `ids`: the kind's own icon for one, the declared icon for several, nothing for none.
 * A resolved note is not counted; it has been answered.
 */
export function noteMarkOf(notes: readonly HierarchySurfaceNote[], ids: ReadonlySet<string>, definition: HierarchySurfaceDefinition): NoteMark | null {
  const open = notes.filter((note) => !note.resolved && ids.has(note.aboutId));
  if (open.length === 0) {
    return null;
  }
  if (open.length === 1) {
    const only = open[0]!;
    return { icon: definition.noteIcons[only.kind] ?? definition.severalNotesIcon, count: 1, label: `${only.kind}: ${only.text}` };
  }
  return { icon: definition.severalNotesIcon, count: open.length, label: `${open.length} open notes` };
}
