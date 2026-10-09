import { formatEpochSeconds, holds, resolveMany, resolveOne, type Binding, type BindingSource, type Condition, type TemporalFormat, type BindingPath } from "./binding";
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
  every: number | { calendar: CalendarStep; count?: number };
  /**
   * How a tick at this rung is labelled. A format the library understands, not a function:
   * `"yyyy"`, `"MMM"`, `"d MMM"`, `"HH:mm"`, or `"number"` for a plain count.
   *
   * The same closed set a binding's `number.format` uses, and the SAME formatter behind it: a
   * ruler's tick and a label bound to the same instant must read identically.
   */
  label: TemporalFormat;
}

/** The calendar steps a rung may take. A decade is ten years, starting on a year ending in 0. */
export type CalendarStep = "month" | "quarter" | "year" | "decade";

/**
 * A time axis that is uniform in MONTHS rather than in seconds: every month the same width, so a
 * month is a fixed step on the canvas and snapping to one needs no calendar arithmetic.
 *
 * Canvas x is `(monthIndex(date) - monthIndex(origin)) * unitsPerStep`, where
 * `monthIndex(y-m) = y * 12 + (m - 1)`. The ruler's own unit is then the month index, and the
 * module's backend can state the same scale and convert identically, because nothing is fitted.
 */
export interface MonthScale {
  unit: "month";
  /** Canvas units per month. */
  unitsPerStep: number;
  /** The month at canvas x 0, as `YYYY-MM`. */
  origin: string;
}

/** A ruler pinned to the view rather than to the diagram. */
export interface RulerDeclaration {
  orientation: "horizontal" | "vertical";
  /**
   * Which side of the viewport the canvas pins the ruler to, where the LIBRARY draws it. Only
   * `bottom` is drawn today: a strip in screen coordinates, so it never scrolls out of sight,
   * whose ticks follow horizontal pan and zoom. Omitted, the library draws nothing and a module
   * may render the ticks itself, as it could before.
   */
  edge?: "bottom";
  /**
   * The scale, stated once where the axis is month-uniform. Given, it replaces
   * {@link unitsPerCanvasUnit} and {@link origin}: the ruler's unit becomes the month index.
   */
  scale?: MonthScale;
  /**
   * How many of the ruler's own units one canvas unit covers - a timeline's seconds per unit.
   * Bound, because it is the module's scale and the library has no opinion about it. Ignored
   * under a {@link scale}.
   */
  unitsPerCanvasUnit?: Binding | number;
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
  /**
   * Switches drawn over the canvas, each showing a value the document holds. See
   * {@link SwitchDeclaration}.
   */
  switches?: readonly SwitchDeclaration[];
}

/**
 * A two-state switch over the canvas whose state is the DOCUMENT's, not the view's.
 *
 * The filter box and the layout toggle are view state: held by the canvas, sent nowhere, gone
 * when the diagram closes. This is the other kind - "show archived specifications" is something
 * the reader sets and finds set again tomorrow, on another machine - so the canvas holds nothing:
 * it draws what the model's background says and raises a request when the switch is pressed.
 */
export interface SwitchDeclaration {
  /** Names the switch in the event it raises. */
  id: string;
  caption: string;
  /** A path into the model's background to the switch's value. Anything but `true` reads as off. */
  on: BindingPath;
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

/** How many months a calendar step spans. */
function monthsOf(step: CalendarStep, count: number): number {
  switch (step) {
    case "month":
      return count;
    case "quarter":
      return 3 * count;
    case "year":
      return 12 * count;
    case "decade":
      return 120 * count;
  }
}

/**
 * How many of the ruler's own units one rung spans, for choosing between rungs: seconds on a
 * seconds scale, months on a {@link MonthScale}.
 */
function approximateSpan(rung: RulerRung, monthly = false): number {
  if (typeof rung.every === "number") {
    return rung.every;
  }

  const months = monthsOf(rung.every.calendar, rung.every.count ?? 1);
  if (monthly) {
    return months;
  }

  // A month as 30 days, a quarter as 91 and a year as 365, as the ladder always approximated.
  return rung.every.calendar === "quarter" ? (months / 3) * 91 * DAY : rung.every.calendar === "month" ? months * 30 * DAY : (months / 12) * 365 * DAY;
}

function fixedTicks(from: number, to: number, step: number, format: TemporalFormat): ResolvedTick[] {
  const ticks: ResolvedTick[] = [];
  // Round boundaries, so a label falls on the hour rather than wherever the view happens to start.
  for (let at = Math.ceil(from / step) * step; at <= to; at += step) {
    ticks.push({ at, label: formatEpochSeconds(at, format) });
  }

  return ticks;
}

function calendarTicks(
  from: number,
  to: number,
  unit: CalendarStep,
  count: number,
  format: TemporalFormat,
): ResolvedTick[] {
  const ticks: ResolvedTick[] = [];
  const start = new Date(from * 1000);
  const months = monthsOf(unit, count);

  let year = start.getUTCFullYear();
  if (unit === "decade") {
    year = Math.floor(year / (10 * count)) * 10 * count;
  }
  let month = unit === "year" || unit === "decade" ? 0 : Math.floor(start.getUTCMonth() / months) * months;
  for (let guard = 0; guard < 4096; guard++) {
    const at = Date.UTC(year, month, 1) / 1000;
    if (at > to) {
      break;
    }

    if (at >= from) {
      ticks.push({ at, label: formatEpochSeconds(at, format) });
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

  const monthly = declaration.scale !== undefined;
  const span = view.to - view.from;
  const maxTicks = Math.max(1, Math.floor(view.sizePx / (declaration.minSpacingPx ?? 80)));
  const coarsest = span / maxTicks;

  const rungs = [...declaration.ladder].sort((left, right) => approximateSpan(left, monthly) - approximateSpan(right, monthly));
  const chosen = rungs.find((rung) => approximateSpan(rung, monthly) >= coarsest) ?? rungs[rungs.length - 1];
  if (chosen === undefined) {
    return [];
  }

  if (monthly) {
    return monthTicks(view.from, view.to, chosen);
  }

  return typeof chosen.every === "number"
    ? fixedTicks(view.from, view.to, chosen.every, chosen.label)
    : calendarTicks(view.from, view.to, chosen.every.calendar, chosen.every.count ?? 1, chosen.label);
}

/** `YYYY-MM` as a month index, `y * 12 + (m - 1)`; null for anything else. */
export function monthIndexOf(text: string): number | null {
  const match = /^(-?\d{1,6})-(\d{2})$/.exec(text.trim());
  if (match === null) {
    return null;
  }

  const month = Number(match[2]);
  return month >= 1 && month <= 12 ? (Number(match[1]) * 12) + (month - 1) : null;
}

/**
 * Ticks on a month-uniform scale, at month indices. A calendar rung steps whole months from a
 * boundary of its own size - quarters on January, April, July and October, decades on years
 * ending in 0 - and a fixed rung steps that many months.
 */
function monthTicks(from: number, to: number, rung: RulerRung): ResolvedTick[] {
  const months = typeof rung.every === "number" ? Math.max(1, Math.round(rung.every)) : monthsOf(rung.every.calendar, rung.every.count ?? 1);
  const ticks: ResolvedTick[] = [];
  for (let at = Math.ceil(from / months) * months, guard = 0; at <= to && guard < 4096; at += months, guard++) {
    ticks.push({ at, label: formatEpochSeconds(epochSecondsOfMonth(at), rung.label) });
  }

  return ticks;
}

/** The first instant of a month index, in epoch seconds - so the one formatter labels it. */
function epochSecondsOfMonth(index: number): number {
  const year = Math.floor(index / 12);
  const month = index - (year * 12);
  const date = new Date(Date.UTC(2000, month, 1));
  // Set separately: `Date.UTC` reads a year 0..99 as 1900..1999.
  date.setUTCFullYear(year);
  return date.getTime() / 1000;
}

/** The ruler's own range for a viewport, in its units. */
export function rulerRangeOf(
  declaration: RulerDeclaration,
  source: BindingSource,
  viewport: { start: number; size: number },
): { from: number; to: number } {
  if (declaration.scale !== undefined) {
    const origin = monthIndexOf(declaration.scale.origin) ?? 0;
    const per = declaration.scale.unitsPerStep > 0 ? declaration.scale.unitsPerStep : 1;
    return { from: origin + (viewport.start / per), to: origin + ((viewport.start + viewport.size) / per) };
  }

  const perUnit = numberOf(declaration.unitsPerCanvasUnit, source, 1);
  const origin = numberOf(declaration.origin, source, 0);
  return { from: origin + viewport.start * perUnit, to: origin + (viewport.start + viewport.size) * perUnit };
}

/** Where a tick in the ruler's own units sits on the canvas - the inverse of {@link rulerRangeOf}. */
export function canvasPositionOf(declaration: RulerDeclaration, source: BindingSource, at: number): number {
  if (declaration.scale !== undefined) {
    const origin = monthIndexOf(declaration.scale.origin) ?? 0;
    const per = declaration.scale.unitsPerStep > 0 ? declaration.scale.unitsPerStep : 1;
    return (at - origin) * per;
  }

  const perUnit = numberOf(declaration.unitsPerCanvasUnit, source, 1);
  const origin = numberOf(declaration.origin, source, 0);
  return perUnit !== 0 ? (at - origin) / perUnit : 0;
}

export { SECOND, MINUTE, HOUR, DAY };
