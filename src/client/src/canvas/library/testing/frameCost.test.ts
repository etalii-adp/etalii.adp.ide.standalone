import { describe, expect, it } from "vitest";
import { perFrameCostRatio, type MeasuredGesture } from "./frameCost";

/**
 * The guard on the drag-cost ratio's own measurement (T312). Stability run 16 failed the drag-cost
 * guard at a reposition ratio of 8.92 against a tolerance of 8 with nothing wrong in the canvas:
 * the large model's frames were all timed first and the small model's all after, so a change in
 * the runner's load between the two blocks went straight into the ratio. Here the machine is a
 * clock this test drives, its load changes once halfway through, and a ratio taken across that
 * change must still say what the code costs.
 */

const FRAMES = 30;
const REPETITIONS = 3;
const LOADED = 4;

/**
 * A clock whose every frame costs the gesture's own work times the machine's load at that moment.
 * The load is `LOADED` for the half of all frames that `loadedHalf` names and 1 for the other.
 */
function machine(costOf: (size: "large" | "small") => number, loadedHalf: "first" | "second") {
  const half = FRAMES * REPETITIONS;
  let time = 0;
  let driven = 0;
  const open = (size: "large" | "small"): MeasuredGesture => ({
    frame: () => {
      const loaded = loadedHalf === "first" ? driven < half : driven >= half;
      time += costOf(size) * (loaded ? LOADED : 1);
      driven++;
    },
    end: () => {},
  });
  return { open, now: () => time };
}

describe("per-frame cost ratio", () => {
  it("a load that changes mid-measurement leaves a flat cost reading flat", () => {
    const { open, now } = machine(() => 1, "first");

    const ratio = perFrameCostRatio(open, { frames: FRAMES, repetitions: REPETITIONS, now });

    expect(ratio).toBe(1);
  });

  it("a load that changes mid-measurement does not hide a cost that tracks the model", () => {
    // Ten times the work on the large model: what a per-element render per frame looks like.
    const { open, now } = machine((size) => (size === "large" ? 10 : 1), "second");

    const ratio = perFrameCostRatio(open, { frames: FRAMES, repetitions: REPETITIONS, now });

    expect(ratio).toBe(10);
  });

  it("drives every frame of every repetition of both sizes", () => {
    const frames = { large: 0, small: 0 };
    const ended = { large: 0, small: 0 };
    const open = (size: "large" | "small"): MeasuredGesture => ({
      frame: () => {
        frames[size]++;
      },
      end: () => {
        ended[size]++;
      },
    });

    perFrameCostRatio(open, { frames: FRAMES, repetitions: REPETITIONS, now: () => 0 });

    expect(frames).toEqual({ large: FRAMES * REPETITIONS, small: FRAMES * REPETITIONS });
    expect(ended).toEqual({ large: REPETITIONS, small: REPETITIONS });
  });
});
