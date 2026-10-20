/**
 * How the drag-cost guard reads a gesture's per-frame cost on a large model against a small one
 * (drag-and-drop-centralization Requirements 3.1 and 3.2): as a ratio, never as a millisecond
 * budget, so a slower machine slows both sides alike.
 */

/** One gesture under measurement: already mounted and pressed by whoever opened it. */
export interface MeasuredGesture {
  /** One pointer frame of the gesture. The caller times it. */
  frame(index: number): void;
  /** Release the gesture and unmount what it was drawn on. */
  end(): void;
}

export interface FrameCostOptions {
  /** Pointer frames per gesture. */
  frames: number;
  /** Gestures per model size, each on a fresh mount. */
  repetitions: number;
  /** The clock; `performance.now` unless a test drives its own. */
  now?: () => number;
}

/**
 * The large model's median single-frame cost over the small model's.
 *
 * Each frame is timed alone and the median taken - `dragCost.test.tsx` says why neither a
 * whole-window minimum nor a per-frame minimum will do.
 *
 * Both sizes are mounted side by side and their frames alternate, so every large frame is timed
 * next to a small one under the same load. A slower machine slows both sides alike, but only when
 * both are measured on the same machine at the same moment: timed one size after the other, a
 * shared runner whose load changed between the two blocks put that change into the ratio. That
 * read 8.92 against a tolerance of 8 in stability run 16 with nothing wrong in the canvas (T312),
 * and the same change in the other direction would hide a cost that does track the model.
 * Which size goes first alternates per frame as well, so neither always runs on a cache the other
 * just warmed.
 */
export function perFrameCostRatio(open: (size: "large" | "small") => MeasuredGesture, { frames, repetitions, now = () => performance.now() }: FrameCostOptions): number {
  const samples = { large: [] as number[], small: [] as number[] };
  const timed = (gesture: MeasuredGesture, frame: number, into: number[]) => {
    const started = now();
    gesture.frame(frame);
    into.push(now() - started);
  };
  for (let repetition = 0; repetition < repetitions; repetition++) {
    const large = open("large");
    const small = open("small");
    for (let frame = 1; frame <= frames; frame++) {
      if (frame % 2 === 0) {
        timed(large, frame, samples.large);
        timed(small, frame, samples.small);
      } else {
        timed(small, frame, samples.small);
        timed(large, frame, samples.large);
      }
    }
    large.end();
    small.end();
  }
  return median(samples.large) / Math.max(median(samples.small), 0.001);
}

function median(samples: number[]): number {
  const sorted = [...samples].sort((a, b) => a - b);
  return sorted[Math.floor(sorted.length / 2)];
}
