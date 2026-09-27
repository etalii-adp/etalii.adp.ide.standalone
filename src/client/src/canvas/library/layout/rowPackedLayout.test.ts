import { describe, expect, it } from "vitest";
import { rowPackedLayout } from "./rowPackedLayout";
import type { LayoutDefinition } from "../definition/diagramDefinition";
import type { LayoutElement, LayoutInput, LayoutPlacement } from "./layoutAlgorithm";

const WIDTH = 48;
const GAP = 4;
const definition: LayoutDefinition = { modes: ["manual", "row-packed"], rowPacked: { width: WIDTH, gap: GAP } };

/** An element by its manual left edge and span, on a row; the layout reads centres, as the canvas does. */
const trend = (id: string, left: number, span: number, row: number): LayoutElement => ({
  id,
  x: left + span / 2,
  y: row * 56 + 16,
  width: span,
  height: 32,
});

const inputOf = (elements: LayoutElement[]): LayoutInput => ({ elements, connections: [] });

function placed(elements: LayoutElement[]): Map<string, LayoutPlacement> {
  return rowPackedLayout.place(inputOf(elements), definition) as Map<string, LayoutPlacement>;
}

const leftOf = (placement: LayoutPlacement) => placement.x - WIDTH / 2;

/** A deterministic shuffle, so a failure reproduces. */
function shuffled<T>(items: readonly T[], seed: number): T[] {
  const copy = [...items];
  let state = seed;
  for (let index = copy.length - 1; index > 0; index -= 1) {
    state = (state * 1103515245 + 12345) % 2147483648;
    const other = state % (index + 1);
    [copy[index], copy[other]] = [copy[other], copy[index]];
  }
  return copy;
}

/** Fifty trends over five rows, with repeated starts and wildly different spans. */
const FIFTY: LayoutElement[] = Array.from({ length: 50 }, (_, index) =>
  trend(`t${String(index).padStart(2, "0")}`, (index * 37) % 400 - ((index * 37) % 400) % 8, 4 + ((index * 53) % 900), index % 5),
);

describe("row-packed placement", () => {
  it("keeps time order: an earlier start is never placed right of a later one", () => {
    const positions = placed(shuffled(FIFTY, 7));

    for (const a of FIFTY) {
      for (const b of FIFTY) {
        if (a.x - a.width / 2 < b.x - b.width / 2) {
          expect(leftOf(positions.get(a.id)!)).toBeLessThanOrEqual(leftOf(positions.get(b.id)!));
        }
      }
    }
  });

  it("never lets two elements on one row come closer than the gap", () => {
    const positions = placed(FIFTY);

    const rows = new Map<number, number[]>();
    for (const element of FIFTY) {
      rows.set(element.y, [...(rows.get(element.y) ?? []), leftOf(positions.get(element.id)!)]);
    }
    for (const lefts of rows.values()) {
      const sorted = [...lefts].sort((a, b) => a - b);
      for (let index = 1; index < sorted.length; index += 1) {
        expect(sorted[index] - sorted[index - 1]).toBeGreaterThanOrEqual(WIDTH + GAP);
      }
    }
  });

  it("places each element as far left as time order and its row allow", () => {
    // a and b share a row; c starts after a on another row, so only a's position bounds it.
    const positions = placed([trend("a", 0, 400, 0), trend("b", 100, 8, 0), trend("c", 50, 8, 1)]);

    expect(leftOf(positions.get("a")!)).toBe(0);
    expect(leftOf(positions.get("c")!)).toBe(0);
    expect(leftOf(positions.get("b")!)).toBe(WIDTH + GAP);
  });

  it("gives the same placement for every order the elements arrive in", () => {
    const reference = placed(FIFTY);

    for (const seed of [1, 2, 3, 99]) {
      expect(placed(shuffled(FIFTY, seed))).toEqual(reference);
    }
  });

  it("lets equal starts on different rows share an x, whichever way their ids sort", () => {
    // z is pushed by its crowded row; a starts with it on an empty row and must still share its x.
    const elements = [trend("z", 20, 8, 0), trend("a", 20, 8, 1), trend("crowd", 0, 8, 0)];

    const positions = placed(elements);

    expect(leftOf(positions.get("a")!)).toBe(leftOf(positions.get("z")!));
  });

  it("draws every element at the declared width, on the row it already sits on", () => {
    const positions = placed(FIFTY);

    for (const element of FIFTY) {
      expect(positions.get(element.id)!.width).toBe(WIDTH);
      expect(positions.get(element.id)!.y).toBe(element.y);
    }
  });

  it("keeps the room an element needs before it clear of the element before it on its row", () => {
    const named = { ...trend("b", 100, 8, 0), leading: 100 };
    const positions = placed([trend("a", 0, 8, 0), named]);

    expect(leftOf(positions.get("b")!)).toBe(WIDTH + GAP + 100);
  });

  it("lays out as manual where the definition declares no rowPacked", () => {
    expect(rowPackedLayout.place(inputOf(FIFTY), { modes: ["row-packed"] })).toBeNull();
  });
});

describe("row-packed inverse", () => {
  // One row, so the two are placed apart and a point can fall between them.
  const pair = [trend("early", 0, 40, 0), trend("late", 400, 40, 0)];

  it("interpolates between the two placed neighbours of a point", () => {
    const positions = placed(pair);
    const early = leftOf(positions.get("early")!);
    const late = leftOf(positions.get("late")!);

    const manual = rowPackedLayout.inverse!({ x: (early + late) / 2, y: 100 }, inputOf(pair), definition);

    expect(manual).toEqual({ x: 200, y: 100 });
  });

  it("extrapolates beyond either end at the same distance from the nearest element", () => {
    const positions = placed(pair);
    const late = leftOf(positions.get("late")!);

    expect(rowPackedLayout.inverse!({ x: -30, y: 0 }, inputOf(pair), definition).x).toBe(-30);
    expect(rowPackedLayout.inverse!({ x: late + 10, y: 0 }, inputOf(pair), definition).x).toBe(410);
  });

  it("leaves the point unchanged with nothing placed", () => {
    expect(rowPackedLayout.inverse!({ x: 123, y: 45 }, inputOf([]), definition)).toEqual({ x: 123, y: 45 });
  });

  it("maps a drop between two trends on different rows to a start that is placed between them", () => {
    const positions = placed(pair);
    const early = leftOf(positions.get("early")!);
    const late = leftOf(positions.get("late")!);
    const drop = { x: (early + late) / 2, y: 2 * 56 + 16 };

    const manual = rowPackedLayout.inverse!(drop, inputOf(pair), definition);
    const dropped = trend("new", manual.x, 40, 2);
    const after = placed([...pair, dropped]);

    expect(manual.x).toBeGreaterThan(0);
    expect(manual.x).toBeLessThan(400);
    expect(leftOf(after.get("new")!)).toBeGreaterThanOrEqual(leftOf(after.get("early")!));
    expect(leftOf(after.get("new")!)).toBeLessThanOrEqual(leftOf(after.get("late")!));
  });
});
