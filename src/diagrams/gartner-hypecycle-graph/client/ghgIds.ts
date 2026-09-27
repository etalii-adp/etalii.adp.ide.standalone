/**
 * Every id this module and its backend agree on, stated ONCE, here - and the time scale, which is
 * the other thing both tiers must state identically.
 *
 * The backend answers these strings (`GhgContextActionProvider`, `GhgContextPropertyProvider`,
 * `GhgElementMapper`); the canvas reads these constants and nothing else. A second spelling anywhere
 * in this module is a defect.
 */

/** The diagram's mime type, which is its origin: `<vendor>/<diagram-type>`. */
export const GHG_MIME = "gartner/hypecycle-graph";

/** What the backend prefixes an element or connection type with on the wire. */
export const GHG_TYPE_PREFIX = `${GHG_MIME}+`;

export const GhgElementTypes = { trend: "trend" } as const;
export const GhgRelationTypes = { influence: "influence" } as const;

/** The four phases, in order: the segment index is the index here. */
export const GHG_PHASES = ["peak", "trough", "slope", "plateau"] as const;

/** The phases as a reader names them, in the order of {@link GHG_PHASES} - as the property grid titles them. */
export const GHG_PHASE_TITLES = ["Peak", "Trough", "Slope", "Plateau"] as const;

/** The full Gartner names, which the segments show as tooltips. */
export const GHG_PHASE_TOOLTIPS = [
  "Peak of Inflated Expectations",
  "Trough of Disillusionment",
  "Slope of Enlightenment",
  "Plateau of Productivity",
] as const;

/** The actions. Each runs against ONE target id: a trend or influence id, `new:x,y`, or `rel:...`. */
export const GhgActions = {
  /** Adds a trend a year long from the month containing the drop's x, on the row nearest its y. */
  addTrend: "ghg.add.trend",
  /** Draws an influence for a `rel:{from}@{phase}/{edge}/{at}->{to}@{phase}/{edge}/{at}` target. */
  connect: "ghg.connect.influence",
  rename: "ghg.rename",
  remove: "ghg.remove",
  evenPhases: "ghg.even-phases",
  disconnect: "ghg.disconnect",
} as const;

export const GHG_ACTION_IDS: readonly string[] = Object.values(GhgActions);

/** The properties the canvas sets itself: a resize is a span, a boundary drag is a boundary. */
export const GhgProperties = {
  start: "ghg.start",
  stop: "ghg.stop",
  /** The month each inner boundary ends its phase at, indexed as the library indexes boundaries. */
  boundaries: ["ghg.peak-end", "ghg.trough-end", "ghg.slope-end"],
  /** Where each end of an influence attaches, as `phase/edge/at`: a dragged end handle. */
  fromAttachment: "ghg.from-attachment",
  toAttachment: "ghg.to-attachment",
} as const;

export const GhgShortcuts = { rename: "F2" } as const;

/**
 * The time scale: four canvas units a month from 1900-01, so x is a pure function of the date.
 * `scale-fixture.json` states the same numbers, and both tiers are asserted against it.
 */
export const GhgScale = {
  unitsPerMonth: 4,
  origin: "1900-01",
  originMonth: 1900 * 12,
  trendHeight: 32,
  rowStep: 56,
} as const;

/** The month index a snapped canvas x starts, or ends, at. */
export function monthAt(x: number): number {
  return GhgScale.originMonth + Math.round(x / GhgScale.unitsPerMonth);
}

/** The canvas x of the start of a month index. */
export function xOfMonth(monthIndex: number): number {
  return (monthIndex - GhgScale.originMonth) * GhgScale.unitsPerMonth;
}

/** A month index as the document writes it: `YYYY-MM`, or `-YYYY-MM` before year 0 (ISO 8601's astronomical years). */
export function formatMonth(monthIndex: number): string {
  const year = Math.floor(monthIndex / 12);
  const month = monthIndex - year * 12 + 1;
  return `${year < 0 ? "-" : ""}${String(Math.abs(year)).padStart(4, "0")}-${String(month).padStart(2, "0")}`;
}
