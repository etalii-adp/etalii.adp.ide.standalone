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

/** Which operation of the specification each gesture sends. */
export interface HierarchySurfaceOperations {
  /** The control to the left of a title. */
  toggleCollapsed: string;
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
}

/** The icon of a level whose deeper levels do not fit the page (spec 029 R10). */
export const HIDDEN_LEVELS_ICON = "mdi-chevron-double-right";

/** The icon of the control that makes a level the left edge of the page. */
export const FOCUS_ICON = "mdi-target";
