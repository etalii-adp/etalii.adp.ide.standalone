import type { CSSProperties } from "react";

/**
 * <b>ONE HIGHLIGHT, ONE COLOUR, ONE PLACE.</b> What a selected item looks like, and what an item a
 * dragged connection would land on looks like - which since the user's ruling of 2026-09-22 are the
 * same look (centralized-selection Requirements 5.3, 5.4, 6.1, 6.2).
 *
 * <b>Colour and a heavier line, nothing else.</b> The fill never changes, so a notation's own colours
 * still say what a thing IS while the highlight says what is selected. There is no ring: an outline
 * drawn outside the shape was the previous answer and the user replaced it.
 *
 * <b>Painted INLINE, by {@link highlighted}.</b> A stylesheet rule cannot do this job: a module rule
 * styling its own shapes by descendant - `.mindmap-node rect` - outranks a single library class, and
 * jsdom's cascade is source order alone, so no unit test could tell a working rule from a losing one.
 * Inline paint is what both a browser and jsdom apply over every ordinary rule, so the look is
 * provable. `ringsSurviveModuleStyles.test.tsx` holds every stylesheet in the tree to it.
 *
 * <b>The token paints nothing at rest</b> (Requirement 5.5): `--color-primary` is also timeline's
 * moment fill, ansible's outlines, the owl and rdf badges, skos's notation and sparql's projection
 * mark, so a highlight in that colour read as "selected" on things nobody had selected.
 */
export const HIGHLIGHT_STROKE = "var(--color-selected, #7c3aed)";

/** How much heavier a highlighted line is drawn, when the notation states no width of its own. */
export const HIGHLIGHT_STROKE_WIDTH = 3;

/**
 * `paint` as it is drawn when the item is highlighted: its own colours, with the outline recoloured
 * and thickened. Not highlighted, the paint is returned untouched - a notation that declares nothing
 * still draws exactly what it declared.
 */
export function highlighted(paint: CSSProperties, isHighlighted: boolean): CSSProperties {
  if (!isHighlighted) {
    return paint;
  }

  const declared = Number(paint.strokeWidth);
  return {
    ...paint,
    stroke: HIGHLIGHT_STROKE,
    // Heavier than whatever the notation asked for, never lighter: a type drawing a 4-wide edge
    // would otherwise look THINNER when selected.
    strokeWidth: Number.isFinite(declared) ? Math.max(declared + 1, HIGHLIGHT_STROKE_WIDTH) : HIGHLIGHT_STROKE_WIDTH,
  };
}

/** A highlighted item's text - a connection's label, and any adorner that draws words. */
export function highlightedText(isHighlighted: boolean): CSSProperties | undefined {
  return isHighlighted ? { fill: HIGHLIGHT_STROKE } : undefined;
}
