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

/**
 * A population mixing types: `trend`s take the declared width, while `dot`s and `note`s keep their
 * own - the notes two rows tall. Rows are 56 apart, and an element covers every row line its span
 * crosses, so the properties above are re-proven per ROW LINE, not per centre.
 */
describe("row-packed placement with a width per type and elements across rows", () => {
  const ROW = 56;
  const mixed: LayoutDefinition = { modes: ["manual", "row-packed"], rowPacked: { width: WIDTH, gap: GAP, types: ["trend"], rowStep: ROW } };

  /** An element by type, manual left edge, width, top and height; the layout reads centres. */
  const shaped = (id: string, type: string, left: number, width: number, top: number, height: number): LayoutElement =>
    ({ id, type, x: left + width / 2, y: top + height / 2, width, height });

  const trendAt = (id: string, left: number, span: number, row: number) => shaped(id, "trend", left, span, row * ROW, 32);
  const dotAt = (id: string, left: number, row: number) => shaped(id, "dot", left - 8, 16, row * ROW + 8, 16);
  const noteAt = (id: string, left: number, row: number) => shaped(id, "note", left, 100, row * ROW, 100);

  const MIXED: LayoutElement[] = Array.from({ length: 45 }, (_, index) => {
    const left = (index * 37) % 400 - (((index * 37) % 400) % 8);
    const row = index % 5;
    return index % 3 === 0 ? trendAt(`e${index}`, left, 4 + ((index * 53) % 900), row) : index % 3 === 1 ? dotAt(`e${index}`, left, row) : noteAt(`e${index}`, left, row);
  });

  const place = (elements: LayoutElement[], declaration = mixed) =>
    rowPackedLayout.place(inputOf(elements), declaration) as Map<string, LayoutPlacement>;

  /** The placed left edge and width of an element. */
  const span = (positions: Map<string, LayoutPlacement>, element: LayoutElement) => {
    const at = positions.get(element.id)!;
    const width = at.width ?? element.width;
    return { left: at.x - width / 2, right: at.x + width / 2 };
  };

  /** Every row line an element's span crosses. */
  const rowLines = (element: LayoutElement) => {
    const first = Math.floor((element.y - element.height / 2) / ROW);
    const last = Math.floor((element.y + element.height / 2 - 1) / ROW);
    return Array.from({ length: last - first + 1 }, (_, index) => first + index);
  };

  it("keeps time order across types", () => {
    const positions = place(shuffled(MIXED, 5));
    const manualLeft = (element: LayoutElement) => element.x - element.width / 2;

    for (const a of MIXED) {
      for (const b of MIXED) {
        if (manualLeft(a) < manualLeft(b)) {
          expect(span(positions, a).left).toBeLessThanOrEqual(span(positions, b).left);
        }
      }
    }
  });

  it("never lets two elements covering one row line come closer than the gap", () => {
    const positions = place(MIXED);

    const lines = new Map<number, { left: number; right: number }[]>();
    for (const element of MIXED) {
      for (const line of rowLines(element)) {
        lines.set(line, [...(lines.get(line) ?? []), span(positions, element)]);
      }
    }
    for (const spans of lines.values()) {
      const sorted = [...spans].sort((a, b) => a.left - b.left);
      for (let index = 1; index < sorted.length; index += 1) {
        expect(sorted[index].left - sorted[index - 1].right).toBeGreaterThanOrEqual(GAP);
      }
    }
  });

  it("keeps nothing on the first row of a two-row note from overlapping it", () => {
    // The note covers rows 0 and 1, its centre on row line 0's far side at y 50; a trend on row 0
    // starts after it and must clear it. Keyed by the centre alone, row 0 would look empty.
    const note = noteAt("note", 0, 0);
    const later = trendAt("later", 10, 40, 0);
    const below = trendAt("below", 20, 40, 1);

    const positions = place([note, later, below]);

    expect(span(positions, later).left, "the trend on the note's first row overlaps it").toBeGreaterThanOrEqual(100 + GAP);
    expect(span(positions, below).left, "the trend on the note's second row overlaps it").toBeGreaterThanOrEqual(100 + GAP);
  });

  it("places each element as far left as time order and its rows allow", () => {
    const positions = place([trendAt("a", 0, 400, 0), dotAt("d", 100, 0), trendAt("c", 50, 8, 1)]);

    expect(span(positions, trendAt("a", 0, 400, 0)).left).toBe(0);
    expect(span(positions, trendAt("c", 50, 8, 1)).left).toBe(0);
    expect(span(positions, dotAt("d", 100, 0)).left).toBe(WIDTH + GAP);
  });

  it("gives the same placement for every order the elements arrive in", () => {
    const reference = place(MIXED);

    for (const seed of [1, 2, 3, 99]) {
      expect(place(shuffled(MIXED, seed))).toEqual(reference);
    }
  });

  it("draws the listed type at the declared width and leaves every other its own", () => {
    const positions = place(MIXED);

    for (const element of MIXED) {
      expect(positions.get(element.id)!.width, element.id).toBe(element.type === "trend" ? WIDTH : undefined);
      expect(positions.get(element.id)!.y).toBe(element.y);
    }
  });

  it("gives every element the declared width when no types are listed", () => {
    const positions = place(MIXED, { ...mixed, rowPacked: { width: WIDTH, gap: GAP } });

    for (const element of MIXED) {
      expect(positions.get(element.id)!.width).toBe(WIDTH);
    }
  });

  it("inverts a drop between a listed and an unlisted element to a manual x between theirs", () => {
    const pair = [trendAt("early", 0, 40, 0), dotAt("late", 400, 0)];
    const positions = place(pair);
    const early = span(positions, pair[0]).left;
    const late = span(positions, pair[1]).left;

    const manual = rowPackedLayout.inverse!({ x: (early + late) / 2, y: 3 * ROW + 16 }, inputOf(pair), mixed);

    expect(manual.x).toBeGreaterThan(0);
    expect(manual.x).toBeLessThan(400 - 8);
  });
});

describe("row-packed placement with a width per element and connections followed", () => {
  const ROW = 56;
  const base: LayoutDefinition = { modes: ["manual", "row-packed"], rowPacked: { width: { path: "payload.w" }, gap: GAP, rowStep: ROW } };
  const at = (id: string, left: number, row: number, packedWidth?: number): LayoutElement =>
    ({ id, x: left + 20, y: row * ROW + 16, width: 40, height: 32, ...(packedWidth !== undefined ? { packedWidth } : {}) });
  const leftOf = (positions: Map<string, LayoutPlacement>, element: LayoutElement) => {
    const placed = positions.get(element.id)!;
    return placed.x - (placed.width ?? element.width) / 2;
  };

  it("draws each element at its own resolved width, and at its own width where the binding gave none", () => {
    const wide = at("wide", 0, 0, 96);
    const half = at("half", 100, 1, 48);
    const unbound = at("unbound", 200, 2);

    const positions = rowPackedLayout.place({ elements: [wide, half, unbound], connections: [] }, base) as Map<string, LayoutPlacement>;

    expect([positions.get("wide")!.width, positions.get("half")!.width, positions.get("unbound")!.width]).toEqual([96, 48, 40]);
  });

  it("starts an effect after the middle of its earlier cause, on another row, only when connections are followed", () => {
    const cause = at("cause", 0, 0, 96);
    const effect = at("effect", 10, 1, 96);
    const input = { elements: [cause, effect], connections: [{ sourceId: "cause", targetId: "effect" }] };

    const followed = rowPackedLayout.place(input, { ...base, rowPacked: { ...base.rowPacked!, followConnections: true } }) as Map<string, LayoutPlacement>;
    const ignored = rowPackedLayout.place(input, base) as Map<string, LayoutPlacement>;

    expect(leftOf(followed, effect)).toBe(48 + GAP);
    expect(leftOf(ignored, effect)).toBe(0);
  });
});
