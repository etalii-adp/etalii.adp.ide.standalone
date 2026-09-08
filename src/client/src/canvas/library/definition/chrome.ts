import { holds, resolveMany, resolveOne, type Binding, type BindingSource, type Condition } from "./binding";
import type { LabelTypography } from "./diagramDefinition";

/**
 * `chrome` — what a canvas shows around the diagram, declared instead of hand-written.
 *
 * Loading, unavailable, a title, a legend — and, since 2026-09-08, <b>a view-fixed ruler</b>.
 * Eleven percent of every module client is chrome JSX today, and two modules showing the same
 * state show it differently, which is what Requirement 6.3 is about.
 *
 * <b>Absence is as explicit as presence.</b> A module wanting no title declares none, and that
 * is a statement rather than an omission — the distinction Requirement 6.2 asks for and the same
 * one `actions` draws between "has no rename" and "nobody wired one".
 *
 * <b>The ruler landed here after its mechanism was read rather than its purpose.</b> It was
 * task 4's second acceptance instance, on the reading that a ruler is a backdrop. It is not:
 * `TimelineRuler` is HTML rather than SVG, absolutely positioned over the scrolling surface so
 * it never scrolls out of sight, `aria-hidden`, with tick positions in viewport pixels and a
 * tick SET that changes with zoom. A canvas-space background cannot express that, and stretching
 * one until it could would make it able to express anything — the property this design refuses
 * in writing. So it is chrome, and chrome needs a kind that background does not have.
 */

/** One line of chrome: a message, a title, a legend entry. */
export interface ChromeTextDeclaration {
  text: Binding;
  when?: Condition;
  typography?: LabelTypography;
  className?: string;
}

/** A legend: one entry per model collection entry, each with a swatch class and a caption. */
export interface ChromeLegendDeclaration {
  /** A collection binding: one entry per item, its paths rooted at the item. */
  entries: Binding;
  /** The class naming the swatch's colour, bound per entry so a notation keeps its own palette. */
  swatchClass?: Binding;
  when?: Condition;
  className?: string;
}

/**
 * One rung of a ruler's ladder: how far apart its ticks are, and how each is labelled.
 *
 * <b>The ladder is data; choosing a rung and walking it is mechanism.</b> That split is what
 * makes a ruler declarable at all — the alternative is a module supplying a function, which is
 * the escape hatch this specification exists to close.
 */
export interface RulerRung {
  /**
   * A fixed step in the ruler's own unit — seconds, for a timeline — or a calendar step, which
   * is not a fixed number of anything. A month is not 2,629,746 seconds and a reader expects a
   * label on "the 1st", so the calendar rungs are named rather than approximated.
   */
  every: number | { calendar: "month" | "quarter" | "year"; count?: number };
  /**
   * How a tick at this rung is labelled. A format the library understands, not a function:
   * `"yyyy"`, `"MMM"`, `"d MMM"`, `"HH:mm"`, or `"number"` for a plain count.
   */
  label: "yyyy" | "MMM" | "MMM yyyy" | "d MMM" | "HH:mm" | "HH:mm:ss" | "number";
}

/** A ruler pinned to the view rather than to the diagram. */
export interface RulerDeclaration {
  orientation: "horizontal" | "vertical";
  /**
   * How many of the ruler's own units one canvas unit covers - a timeline's seconds per unit.
   * Bound, because it is the module's scale and the library has no opinion about it.
   */
  unitsPerCanvasUnit: Binding | number;
  /** Where the ruler's own zero sits, in its units. */
  origin?: Binding | number;
  /** The rungs, coarsest chosen that still fits. Order does not matter; the library sorts. */
  ladder: readonly RulerRung[];
  /** A labelled tick needs roughly this many pixels or the labels collide. */
  minSpacingPx?: number;
  className?: string;
  when?: Condition;
}

export interface ChromeDeclaration {
  /** Shown until the module's model has arrived. */
  loading?: ChromeTextDeclaration;
  /** Shown when the diagram cannot be opened at this path. */
  unavailable?: ChromeTextDeclaration;
  title?: ChromeTextDeclaration;
  legend?: ChromeLegendDeclaration;
  rulers?: readonly RulerDeclaration[];
}

/** One resolved chrome line. */
export interface ResolvedChromeText {
  text: string;
  typography?: LabelTypography;
  className?: string;
}

export interface ResolvedLegendEntry {
  caption: string;
  swatchClass?: string;
}

export interface ResolvedTick {
  /** Position along the ruler, in the ruler's own units. */
  at: number;
  label: string;
}

const SECOND = 1;
const MINUTE = 60;
const HOUR = 3600;
const DAY = 86400;

function numberOf(value: Binding | number | undefined, source: BindingSource, fallback: number): number {
  if (value === undefined) {
    return fallback;
  }

  if (typeof value === "number") {
    return value;
  }

  const text = resolveOne(value, source);
  const parsed = text === null ? Number.NaN : Number(text);
  return Number.isFinite(parsed) ? parsed : fallback;
}

/** One resolved line, or null when its condition fails or its binding yields nothing. */
export function resolveChromeText(
  declaration: ChromeTextDeclaration | undefined,
  source: BindingSource,
): ResolvedChromeText | null {
  if (!declaration || !holds(declaration.when, source)) {
    return null;
  }

  const text = resolveOne(declaration.text, source);
  return text === null ? null : { text, typography: declaration.typography, className: declaration.className };
}

/** A legend's entries, one per model collection entry. */
export function resolveLegend(
  declaration: ChromeLegendDeclaration | undefined,
  source: BindingSource,
): readonly ResolvedLegendEntry[] {
  if (!declaration || !holds(declaration.when, source)) {
    return [];
  }

  const captions = resolveMany(declaration.entries, source);
  const swatches = declaration.swatchClass ? resolveMany(declaration.swatchClass, source) : [];
  return captions.map((caption, index) => ({ caption, swatchClass: swatches[index] }));
}

/** How many of the ruler's own units one rung spans, for choosing between rungs. */
function approximateSpan(rung: RulerRung): number {
  if (typeof rung.every === "number") {
    return rung.every;
  }

  const count = rung.every.count ?? 1;
  switch (rung.every.calendar) {
    case "month":
      return count * 30 * DAY;
    case "quarter":
      return count * 91 * DAY;
    case "year":
      return count * 365 * DAY;
  }
}

function pad(value: number): string {
  return value < 10 ? `0${value}` : String(value);
}

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/**
 * A tick's label.
 *
 * <b>UTC throughout, deliberately.</b> Timeline's own ruler takes times at face value with no
 * timezone, so that the same document shows the same ruler on every machine; a formatter using
 * local time would shift every label by the viewer's offset and the defect would be invisible to
 * whoever wrote it.
 */
function labelOf(at: number, format: RulerRung["label"]): string {
  if (format === "number") {
    return String(at);
  }

  const date = new Date(at * 1000);
  switch (format) {
    case "yyyy":
      return String(date.getUTCFullYear());
    case "MMM":
      return MONTHS[date.getUTCMonth()]!;
    case "MMM yyyy":
      return `${MONTHS[date.getUTCMonth()]!} ${date.getUTCFullYear()}`;
    case "d MMM":
      return `${date.getUTCDate()} ${MONTHS[date.getUTCMonth()]!}`;
    case "HH:mm":
      return `${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())}`;
    case "HH:mm:ss":
      return `${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())}:${pad(date.getUTCSeconds())}`;
  }
}

function fixedTicks(from: number, to: number, step: number, format: RulerRung["label"]): ResolvedTick[] {
  const ticks: ResolvedTick[] = [];
  // Round boundaries, so a label falls on the hour rather than wherever the view happens to start.
  for (let at = Math.ceil(from / step) * step; at <= to; at += step) {
    ticks.push({ at, label: labelOf(at, format) });
  }

  return ticks;
}

function calendarTicks(
  from: number,
  to: number,
  unit: "month" | "quarter" | "year",
  count: number,
  format: RulerRung["label"],
): ResolvedTick[] {
  const ticks: ResolvedTick[] = [];
  const start = new Date(from * 1000);
  const months = unit === "year" ? 12 * count : unit === "quarter" ? 3 * count : count;

  let year = start.getUTCFullYear();
  let month = unit === "year" ? 0 : Math.floor(start.getUTCMonth() / months) * months;
  for (let guard = 0; guard < 4096; guard++) {
    const at = Date.UTC(year, month, 1) / 1000;
    if (at > to) {
      break;
    }

    if (at >= from) {
      ticks.push({ at, label: labelOf(at, format) });
    }

    month += months;
    while (month >= 12) {
      month -= 12;
      year += 1;
    }
  }

  return ticks;
}

/**
 * The ticks a ruler shows for the visible range.
 *
 * <b>The coarsest rung that still fits</b>, so labels neither crowd nor vanish — the rule
 * `ticksFor` applies today, moved out of the module and driven by the declared ladder rather
 * than by a hard-coded list of steps.
 */
export function resolveTicks(
  declaration: RulerDeclaration,
  source: BindingSource,
  view: { from: number; to: number; sizePx: number },
): readonly ResolvedTick[] {
  if (!holds(declaration.when, source) || !(view.to > view.from) || !(view.sizePx > 0)) {
    return [];
  }

  const span = view.to - view.from;
  const maxTicks = Math.max(1, Math.floor(view.sizePx / (declaration.minSpacingPx ?? 80)));
  const coarsest = span / maxTicks;

  const rungs = [...declaration.ladder].sort((left, right) => approximateSpan(left) - approximateSpan(right));
  const chosen = rungs.find((rung) => approximateSpan(rung) >= coarsest) ?? rungs[rungs.length - 1];
  if (chosen === undefined) {
    return [];
  }

  return typeof chosen.every === "number"
    ? fixedTicks(view.from, view.to, chosen.every, chosen.label)
    : calendarTicks(view.from, view.to, chosen.every.calendar, chosen.every.count ?? 1, chosen.label);
}

/** The ruler's own range for a viewport, in its units. */
export function rulerRangeOf(
  declaration: RulerDeclaration,
  source: BindingSource,
  viewport: { start: number; size: number },
): { from: number; to: number } {
  const perUnit = numberOf(declaration.unitsPerCanvasUnit, source, 1);
  const origin = numberOf(declaration.origin, source, 0);
  return { from: origin + viewport.start * perUnit, to: origin + (viewport.start + viewport.size) * perUnit };
}

export { SECOND, MINUTE, HOUR, DAY };
