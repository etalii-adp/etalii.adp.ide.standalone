/**
 * The colours an option may have. A table's file names one by its word and never by a value; the
 * theme gives each word a fill in both modes (`--color-table-option-<name>` in `index.css`).
 */
export const OPTION_COLORS = ["default", "gray", "brown", "orange", "yellow", "green", "blue", "purple", "pink", "red"] as const;

export type OptionColor = (typeof OPTION_COLORS)[number];

/** The colour a name stands for: itself when it is one of the ten, and the default for any other word. */
export function optionColor(name: string): OptionColor {
  return (OPTION_COLORS as readonly string[]).includes(name) ? (name as OptionColor) : "default";
}

export interface OptionTagProps {
  label: string;
  /** The colour's name as the model gives it. A name the theme does not have draws as the default. */
  color: string;
}

/**
 * An option, drawn as a rounded tag in its colour. The colour is a class, not a value: the
 * stylesheet maps each of the ten names to its theme token, so a tag follows the theme and a
 * file can never smuggle a colour of its own in.
 */
export function OptionTag({ label, color }: OptionTagProps) {
  return <span className={`table-option-tag table-option-${optionColor(color)}`}>{label}</span>;
}
