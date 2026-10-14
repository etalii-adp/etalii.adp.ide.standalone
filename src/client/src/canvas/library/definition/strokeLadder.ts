import { valueAtPath } from "./binding";
import type { StrokeLadder, StrokeRung } from "./diagramDefinition";

/** Whether one rung's test holds for the number, or for its absence. */
function holdsFor(rung: StrokeRung, value: number | undefined): boolean {
  if (rung.test === "absent") {
    return value === undefined;
  }

  if (value === undefined || rung.value === undefined) {
    return false;
  }

  switch (rung.test) {
    case "atLeast":
      return value >= rung.value;
    case "atMost":
      return value <= rung.value;
    case "above":
      return value > rung.value;
    case "below":
      return value < rung.value;
  }
}

/** The width a ladder gives a connection: the first rung that holds, else its `otherwise`. */
export function strokeWidthOf(ladder: StrokeLadder, payload: unknown): number {
  const raw = valueAtPath(ladder.path, { element: { id: "", type: "", x: 0, y: 0 }, payload });
  const value = typeof raw === "number" && Number.isFinite(raw) ? raw : undefined;
  return ladder.rungs.find((rung) => holdsFor(rung, value))?.width ?? ladder.otherwise;
}

/** The class that paints the width a ladder gives a connection, or none where the ladder names none. */
export function strokeClassOf(ladder: StrokeLadder | undefined, payload: unknown): string {
  return ladder === undefined ? "" : (ladder.classNames[String(strokeWidthOf(ladder, payload))] ?? "");
}
