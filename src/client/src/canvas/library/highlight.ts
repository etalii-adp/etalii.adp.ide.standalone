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
 * <b>Painted INLINE, on the DRAWN SHAPE.</b> A stylesheet rule cannot do this job: a module rule
 * styling its own shapes by descendant - `.mindmap-node rect` - outranks a single library class.
 * Inline paint is what both a browser and jsdom apply over every ordinary rule, so the look is
 * provable; `highlightSurvivesModuleStyles.test.tsx` holds every stylesheet in the tree to it.
 *
 * <b>On the shape, never on a group above it.</b> That was got wrong once and shipped: the paint sat
 * on the element's wrapping `<g>`, where it is INHERITED, and an SVG child that states its own stroke
 * ignores an inherited one - which every node shape does, through `.canvas-node` and again through
 * each module's own rules. Five canvases showed no highlight at all while every value the library
 * computed was correct. Each element component therefore paints the shape it draws.
 *
 * <b>The token paints nothing at rest</b> (Requirement 5.5): `--color-primary` is also timeline's
 * moment fill, ansible's outlines, the owl and rdf badges, skos's notation and sparql's projection
 * mark, so a highlight in that colour read as "selected" on things nobody had selected.
 *
 * <b>The width is a considered value, not a default.</b> An SVG stroke is CENTRED on the path and no
 * property moves it outward - `stroke-alignment` was proposed for SVG2 and no browser implements it -
 * so on a strongly filled shape the inner half sits on the fill and does no work, and the highlight
 * is carried by its outer edge alone. That was measured on `c4`, judged by the user and accepted;
 * `centralized-selection` Requirement 5.2 holds the contrast figures.
 *
 * <b>Widening the stroke so its outer half alone equals the declared width does NOT work</b>, and it
 * is the first idea everyone has, including the author of this comment. It assumes a fill to swallow
 * the inner half. `.c4-boundary rect` is `fill: none`, so widening would deliver a 6px highlight on
 * exactly the shapes Requirement 5.3's fill-only rule governs.
 */
export const HIGHLIGHT_STROKE = "var(--color-selected, #7c3aed)";

/**
 * The floor a highlighted line is drawn at: never thinner than this, and never less than one heavier
 * than the notation's own.
 *
 * <b>A floor, not a fallback, and a width, not a delta</b> - this sentence used to say "how much
 * heavier a highlighted line is drawn, when the notation states no width of its own", which is wrong
 * in both directions at once. Read as a fallback it says the constant does not apply when a width is
 * declared: a declared 1 would highlight at 2, where `Math.max(1 + 1, 3)` is 3. Read as a delta it
 * says a declared 4 highlights at 7, where `Math.max(4 + 1, 3)` is 5. The only width it happened to
 * describe correctly was the one where its two errors cancel.
 */
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
