import { ticksFor } from "./timelineTicks";

export interface TimelineRulerProps {
  /** The time at the view's left edge, in seconds since the epoch. */
  startSeconds: number;
  /** How many seconds one pixel covers - the canvas's own zoom. */
  secondsPerPixel: number;
  /** The view's width in pixels. */
  widthPx: number;
}

/**
 * The time ruler: chrome fixed to the view, not to the diagram (Requirement 5).
 *
 * It sits at the bottom of the canvas's own component - absolutely positioned over the
 * scrolling surface, so it never scrolls vertically out of sight - and its labels slide with
 * the content because their x positions are computed from the same view transform the elements
 * use. The label ladder lives in `timelineTicks.ts` as a pure function; nothing here decides
 * anything.
 */
export function TimelineRuler({ startSeconds, secondsPerPixel, widthPx }: TimelineRulerProps) {
  const endSeconds = startSeconds + widthPx * secondsPerPixel;
  const ticks = ticksFor(startSeconds, endSeconds, widthPx);

  return (
    <div className="timeline-ruler" aria-hidden="true">
      {ticks.map((tick) => (
        <span
          key={tick.seconds}
          className="timeline-ruler-tick"
          style={{ left: `${(tick.seconds - startSeconds) / secondsPerPixel}px` }}
        >
          {tick.label}
        </span>
      ))}
    </div>
  );
}
