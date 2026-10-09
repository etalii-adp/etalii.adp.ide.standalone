import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook } from "@testing-library/react";
import type { LayoutPlacement, LayoutPositions } from "./layoutAlgorithm";
import { PLACEMENT_MOTION_MS, useAnimatedPlacements } from "./useAnimatedPlacements";

/**
 * A change of layout is drawn as a move, not a jump (agent-activity-diagram Requirement 6.8) -
 * and as a jump where the reader's system asks for reduced motion.
 *
 * The frames are driven by hand here: a stub stands in for `requestAnimationFrame` and for the
 * clock, so the test says which moment it is looking at instead of waiting for one.
 */

const positions = (entries: Record<string, [number, number]>): LayoutPositions =>
  new Map(Object.entries(entries).map(([id, [x, y]]) => [id, { x, y } satisfies LayoutPlacement]));

let frames: FrameRequestCallback[] = [];
let now = 0;

function runFrame(at: number) {
  now = at;
  const pending = frames;
  frames = [];
  act(() => pending.forEach((frame) => frame(at)));
}

function stubMotion(reduced: boolean) {
  vi.stubGlobal("matchMedia", (query: string) => ({ matches: reduced && query.includes("reduce"), media: query }));
  vi.stubGlobal("requestAnimationFrame", (callback: FrameRequestCallback) => frames.push(callback));
  vi.stubGlobal("cancelAnimationFrame", () => {});
  vi.spyOn(performance, "now").mockImplementation(() => now);
}

beforeEach(() => {
  frames = [];
  now = 0;
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

const first = positions({ a: [0, 0], b: [100, 0] });
const second = positions({ a: [200, 100], b: [100, 0], c: [50, 50] });

describe("a change of layout, drawn", () => {
  it("travels from the old placements to the new, and ends exactly on the new", () => {
    // The planted defect this was seen to fail against: the new placements drawn at once.
    stubMotion(false);
    const { result, rerender } = renderHook(({ target }) => useAnimatedPlacements(target, true), { initialProps: { target: first } });
    expect(result.current).toBe(first);

    rerender({ target: second });
    // Not there yet: `a` is still where it was. `c` is new and is drawn where it belongs at once.
    expect(result.current?.get("a")).toEqual({ x: 0, y: 0 });
    expect(result.current?.get("c")).toEqual({ x: 50, y: 50 });

    runFrame(PLACEMENT_MOTION_MS / 2);
    const midway = result.current?.get("a") as LayoutPlacement;
    expect(midway.x).toBeGreaterThan(0);
    expect(midway.x).toBeLessThan(200);
    expect(midway.y).toBeGreaterThan(0);
    expect(midway.y).toBeLessThan(100);
    // An element whose place did not change does not move on the way.
    expect(result.current?.get("b")).toMatchObject({ x: 100, y: 0 });

    runFrame(PLACEMENT_MOTION_MS);
    expect(result.current).toBe(second);
    expect(frames).toHaveLength(0);
  });

  it("jumps where the reader's system asks for reduced motion", () => {
    stubMotion(true);
    const { result, rerender } = renderHook(({ target }) => useAnimatedPlacements(target, true), { initialProps: { target: first } });

    rerender({ target: second });

    expect(result.current).toBe(second);
    expect(frames).toHaveLength(0);
  });

  it("jumps for a layout whose changes are not shown as motion", () => {
    stubMotion(false);
    const { result, rerender } = renderHook(({ target }) => useAnimatedPlacements(target, false), { initialProps: { target: first } });

    rerender({ target: second });

    expect(result.current).toBe(second);
    expect(frames).toHaveLength(0);
  });

  it("places nothing that the new placements do not place", () => {
    stubMotion(false);
    const { result, rerender } = renderHook(({ target }) => useAnimatedPlacements(target, true), { initialProps: { target: second } });

    // `c` is gone from the layout's answer - locked, or removed from the model.
    rerender({ target: first });

    expect(result.current?.has("c")).toBe(false);
    runFrame(PLACEMENT_MOTION_MS);
    expect(result.current).toBe(first);
  });
});
