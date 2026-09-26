import { canvasPositionOf, resolveTicks, rulerRangeOf, type RulerDeclaration } from "../definition/chrome";
import type { BindingSource } from "../definition/binding";

/** The strip's width in pixels when nothing has been measured - jsdom, or the first paint. */
const UNMEASURED_WIDTH_PX = 800;

export interface RulerStripProps {
  declaration: RulerDeclaration;
  source: BindingSource;
  /** The visible rectangle's left edge and width, in canvas units. */
  view: { x: number; w: number };
  /** The surface's measured width in pixels, or null before it has one. */
  widthPx: number | null;
}

/**
 * A declared ruler pinned to the BOTTOM of the viewport: chrome, not diagram.
 *
 * It is HTML over the drawing rather than SVG inside it, positioned against the canvas's own box,
 * so a vertical scroll never carries it out of sight - which is the whole of why a ruler is chrome
 * and not a background. Its ticks are placed as a percentage of the strip from the same view the
 * drawing uses, so a tick sits over the canvas x it names at every pan and zoom, and the ladder
 * is walked for however many labels the strip's width can hold.
 */
export function RulerStrip({ declaration, source, view, widthPx }: RulerStripProps) {
  if (!(view.w > 0)) {
    return null;
  }

  const sizePx = widthPx !== null && widthPx > 0 ? widthPx : UNMEASURED_WIDTH_PX;
  const range = rulerRangeOf(declaration, source, { start: view.x, size: view.w });
  const ticks = resolveTicks(declaration, source, { from: range.from, to: range.to, sizePx });

  return (
    <div className={["library-ruler", declaration.className].filter(Boolean).join(" ")} data-testid="library-ruler" aria-hidden="true">
      {ticks.map((tick) => (
        <span
          key={tick.at}
          className="library-ruler-tick"
          data-at={tick.at}
          style={{ left: `${((canvasPositionOf(declaration, source, tick.at) - view.x) / view.w) * 100}%` }}
        >
          {tick.label}
        </span>
      ))}
    </div>
  );
}
