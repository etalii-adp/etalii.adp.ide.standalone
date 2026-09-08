import { holds, resolveEntries, resolveMany, resolveOneAt, type BindingSource } from "./binding";
import type { LabelDeclaration, LabelSlot, LabelTypography, ShapeBounds } from "./diagramDefinition";

/**
 * `labels` — what an element says, declared rather than drawn.
 *
 * <b>This is the addition that closes the measured gap.</b> Every built-in shape carries
 * exactly one label; only `styled-box` carries more, and its three are fixed as name, type-line
 * and description. So a module drawing a card with a second line has had no declarative way to
 * say so, and ten of the twenty-eight renderers hand-roll raw `<text>` for exactly that. This
 * replaces the hand-rolling rather than adding a second way to do it.
 *
 * <b>The collection case is the acceptance, not a variant of it.</b> A fixed set of named slots
 * — which `styled-box` already is — would serve c4 and leave the rdf family exactly where it
 * is. `owl-card` is the shape this was specified against: a header, a badge line present only
 * when its list is non-empty, and <em>one line per entry of a model collection</em>, each
 * composed as `predicate: value annotation` and truncated to the box. A design that passes with
 * three fixed slots has not satisfied this.
 *
 * Layout here is <b>pure</b> — declarations and a source in, positioned lines out — so the
 * acceptance can be asserted without a canvas, and so the same function serves the renderer, a
 * test and a future measurement.
 */

/** One line, positioned and ready to draw. The renderer adds no geometry of its own. */
export interface LaidOutLabel {
  text: string;
  x: number;
  y: number;
  anchor: "start" | "middle" | "end";
  typography?: LabelTypography;
  editable: boolean;
  className?: string;
  tooltip?: string;
  /** Which declaration produced it, and which line of it - what an editor and a test address. */
  declarationIndex: number;
  lineIndex: number;
}

/**
 * The per-character estimate the span element already uses. Shared here rather than re-derived
 * so a truncated label is trimmed identically wherever it is drawn - the alternative is two
 * capacities that disagree by a character and a diagram that looks different in two places.
 */
const CHAR_WIDTH = 7;

/** Vertical offsets for the named slots, as fractions of the box height. */
const SLOT_FRACTION: Record<LabelSlot, number> = { header: 0.28, body: 0.5, footer: 0.78 };

function trimmedToWidth(text: string, width: number): string {
  const capacity = Math.floor(Math.max(width - 8, 0) / CHAR_WIDTH);
  if (text.length <= capacity) {
    return text;
  }

  return capacity <= 1 ? "…" : `${text.slice(0, capacity - 1)}…`;
}

function baselineOf(declaration: LabelDeclaration, bounds: ShapeBounds): { x: number; y: number; anchor: "start" | "middle" | "end" } {
  const placed = placementOf(declaration, bounds);
  // A declared alignment overrides the placement's own, and moves the x to the edge it names:
  // an `end` label sits at the box's right edge, which is what shacl's badges and cardinalities
  // do (sufficiency row 20) and what a vertical slot fraction cannot say.
  if (declaration.align === undefined || declaration.offset !== undefined) {
    return placed;
  }

  return { x: alignedX(declaration.align, bounds, declaration.insetX), y: placed.y, anchor: declaration.align };
}

/** Where a column or an aligned label sits horizontally, for the alignment it declares. */
function alignedX(align: "start" | "middle" | "end", bounds: ShapeBounds, insetX: number = LABEL_INSET): number {
  switch (align) {
    case "start":
      return bounds.x + insetX;
    case "end":
      return bounds.x + bounds.width - insetX;
    case "middle":
      return bounds.x + bounds.width / 2;
  }
}

/** How far from an edge an aligned label or column sits, when it does not say. */
const LABEL_INSET = 8;

function placementOf(declaration: LabelDeclaration, bounds: ShapeBounds): { x: number; y: number; anchor: "start" | "middle" | "end" } {
  const centreX = bounds.x + bounds.width / 2;
  const centreY = bounds.y + bounds.height / 2;

  if (declaration.offset) {
    // Measured from the edge the declaration names, so a line pinned below a card's top stays
    // there however tall the card is.
    const originY =
      declaration.anchorTo === "top" ? bounds.y : declaration.anchorTo === "bottom" ? bounds.y + bounds.height : centreY;
    return { x: centreX + declaration.offset.x, y: originY + declaration.offset.y, anchor: "middle" };
  }

  switch (declaration.placement ?? "inside") {
    case "above":
      return { x: centreX, y: bounds.y - 6, anchor: "middle" };
    case "below":
      return { x: centreX, y: bounds.y + bounds.height + 14, anchor: "middle" };
    case "beside":
      return { x: bounds.x + bounds.width + 6, y: centreY + 4, anchor: "start" };
    case "inset":
    case "inside":
    default: {
      const fraction = SLOT_FRACTION[declaration.slot ?? "body"];
      return { x: centreX, y: bounds.y + bounds.height * fraction + 4, anchor: "middle" };
    }
  }
}

/**
 * Every line a set of declarations draws, positioned.
 *
 * <b>Nothing here throws and nothing here is skipped silently for the wrong reason.</b> A
 * declaration whose `when` fails contributes no lines; a collection over an empty list
 * contributes none; a path that does not resolve contributes none. All three are the binding
 * resolver's rules, not new ones — this function adds position and nothing else, which is what
 * keeps the two testable apart.
 */
/**
 * Typography with a view-dependent size resolved, if it declares one.
 *
 * <b>Sufficiency row 27.</b> A Wardley map's stage and axis labels hold a readable size as the
 * map zooms while the boundaries they name do not, because a position is only meaningful
 * against its own axes - so the text has to grow in canvas units exactly as the view does.
 * The module computes that today from the view width; the library is what knows the view.
 *
 * The clamp is the declaration's, not a default: a label that grew without bound would swallow
 * the map at the far end of a zoom, and one that shrank without bound would vanish.
 */
export function scaledTypography(typography: LabelTypography | undefined, viewScale: number): LabelTypography | undefined {
  const scale = typography?.scaleWithView;
  if (typography === undefined || scale === undefined) {
    return typography;
  }

  const factor = Math.max(scale.min, Math.min(scale.max, viewScale));
  return { ...typography, fontSize: (typography.fontSize ?? 12) * factor };
}

export function layoutLabels(
  declarations: readonly LabelDeclaration[] | undefined,
  source: BindingSource,
  bounds: ShapeBounds,
  /** The view's width over the definition's own extent - 1 when the diagram declares none. */
  viewScale = 1,
): readonly LaidOutLabel[] {
  if (!declarations || declarations.length === 0) {
    return [];
  }

  const laidOut: LaidOutLabel[] = [];

  declarations.forEach((declaration, declarationIndex) => {
    if (!holds(declaration.when, source)) {
      return;
    }

    const entries = resolveEntries(declaration.text, source);
    if (entries.length === 0) {
      return;
    }

    const lines = entries.map((entry) => entry.text);

    const base = baselineOf(declaration, bounds);
    const tooltip = declaration.tooltip ? (resolveMany(declaration.tooltip, source)[0] ?? undefined) : undefined;
    const stack = declaration.stack;

    lines.forEach((line, lineIndex) => {
      const y = stack ? base.y + (stack.start ?? 0) + lineIndex * stack.lineHeight : base.y;
      for (const column of declaration.columns ?? []) {
        // The entry's OWN root: for a collection, the item this line came from.
        const text = resolveOneAt(column.text, entries[lineIndex]!.root);
        if (text === null) {
          continue;
        }

        const align = column.align ?? "start";
        laidOut.push({
          text: column.truncate ? trimmedToWidth(text, bounds.width) : text,
          x: alignedX(align, bounds, column.insetX),
          y,
          anchor: align,
          typography: scaledTypography(declaration.typography, viewScale),
          // Never editable: a column is a second value on somebody else's line, and an editor
          // over it would commit to a field the line does not name.
          editable: false,
          className: column.className,
          declarationIndex,
          lineIndex,
        });
      }

      laidOut.push({
        text: declaration.truncate ? trimmedToWidth(line, bounds.width) : line,
        x: base.x,
        y,
        anchor: base.anchor,
        typography: scaledTypography(declaration.typography, viewScale),
        // A collection line has no single authored value to write back to, so it is never
        // editable however the declaration is written. Stated here rather than trusted to the
        // author, because an editor over a computed line would commit to nothing.
        editable: (declaration.editable ?? false) && lines.length === 1,
        className: declaration.className,
        tooltip,
        declarationIndex,
        lineIndex,
      });
    });
  });

  return laidOut;
}
