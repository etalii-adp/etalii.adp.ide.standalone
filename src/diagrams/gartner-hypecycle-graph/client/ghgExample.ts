import { readFileSync } from "node:fs";
import { join } from "node:path";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { compactWidthOf } from "./GhgCanvas";
import { GHG_PHASES, GhgElementTypes, GhgRelationTypes, GhgScale, timeUnitOf, xOfMonth, type GhgTimeUnit } from "./ghgIds";

/**
 * An example document - technology-trends unless another is named - read line by line for the tests. The document is flat on purpose -
 * one `- id:` entry per trend or influence with scalar keys beneath it - so this reads exactly that
 * shape, and the counts asserted beside it say whether it read all of it.
 */
export interface ExampleEntry {
  [key: string]: string;
}

export interface Example {
  trends: ExampleEntry[];
  triggers: ExampleEntry[];
  notes: ExampleEntry[];
  influences: ExampleEntry[];
  unit: GhgTimeUnit;
}

export function readExample(name = "technology-trends"): Example {
  const text = readFileSync(join(__dirname, "..", "examples", name, `${name}.ghg`), "utf8");
  const sections: Record<string, ExampleEntry[]> = { trends: [], triggers: [], notes: [], influences: [] };
  let section: ExampleEntry[] | null = null;
  let entry: ExampleEntry | null = null;
  let unit: GhgTimeUnit = "month";

  for (const line of text.split(/\r?\n/)) {
    const named = /^unit: (\S+)$/.exec(line);
    if (named !== null) {
      unit = timeUnitOf(named[1]);
      continue;
    }
    const heading = /^(trends|triggers|notes|influences):/.exec(line);
    if (heading !== null) {
      section = sections[heading[1]];
      continue;
    }
    const start = /^ {2}- id: (.+)$/.exec(line);
    if (start !== null && section !== null) {
      entry = { id: start[1] };
      section.push(entry);
      continue;
    }
    const key = /^ {4}([a-z-]+): ?(.*)$/.exec(line);
    if (key !== null && entry !== null) {
      entry[key[1]] = key[2];
      continue;
    }
    // A note's text written as a literal block: its lines, joined back with their breaks.
    const block = /^ {6}(.*)$/.exec(line);
    if (block !== null && entry !== null && entry.text !== undefined && /^\|/.test(entry.text)) {
      entry.body = entry.body === undefined ? block[1] : `${entry.body}\n${block[1]}`;
    }
  }

  return { ...sections, unit } as Example;
}

// The year may be signed, as ISO 8601 writes a year before 1: `-3200-01`.
const monthOf = (text: string) => {
  const [, year, month] = /^(-?\d+)-(\d{2})$/.exec(text)!;
  return Number(year) * 12 + Number(month) - 1;
};

/** Whether an influence attaches to a phase its trend does not show - which hides it. A trigger's end has no phase to hide. */
export function isHidden(influence: ExampleEntry, trends: readonly ExampleEntry[]): boolean {
  const phasesOf = (id: string) => Number(trends.find((trend) => trend.id === id)?.phases ?? Infinity);
  return GHG_PHASES.indexOf(influence["from-phase"] as never) >= phasesOf(influence.from)
    || GHG_PHASES.indexOf(influence["to-phase"] as never) >= phasesOf(influence.to);
}

/** A trigger's date as its label writes it in the diagram's unit, as the backend formats it. */
const MONTH_NAMES = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
function whenOf(month: number, unit: GhgTimeUnit): string {
  const year = Math.floor(month / 12);
  return unit === "month" ? `${MONTH_NAMES[month - year * 12]} ${year}` : String(year);
}

const tagsOf = (entry: ExampleEntry) => /^\[(.*)\]$/.exec(entry.tags ?? "")?.[1].split(",").map((tag) => tag.trim()) ?? [];

/**
 * The example as the canvas is handed it, limited to `ids` when given: trends, triggers and notes at
 * their centres and sizes, and the influences between the ones kept, with their attachments.
 */
export function exampleModel(ids?: readonly string[], name = "technology-trends"): DiagramModel {
  const { trends, triggers, notes, influences, unit } = readExample(name);
  const keep = (entry: ExampleEntry) => ids === undefined || ids.includes(entry.id);
  const trendElements = trends.filter(keep).map((trend): DiagramModelElement => {
    const width = xOfMonth(monthOf(trend.stop), unit) - xOfMonth(monthOf(trend.start), unit);
    return {
      id: trend.id,
      type: GhgElementTypes.trend,
      x: xOfMonth(monthOf(trend.start), unit) + width / 2,
      y: Number(trend.row) * GhgScale.rowStep + GhgScale.trendHeight / 2,
      width,
      height: GhgScale.trendHeight,
      label: trend.name,
      payload: { name: trend.name, phases: Number(trend.phases), tags: tagsOf(trend), snapX: 0, snapY: 0, compactWidth: compactWidthOf(Number(trend.phases)) },
    };
  });
  const triggerElements = triggers.filter(keep).map((trigger): DiagramModelElement => ({
    id: trigger.id,
    type: GhgElementTypes.trigger,
    x: xOfMonth(monthOf(trigger.date), unit),
    y: Number(trigger.row) * GhgScale.rowStep + GhgScale.trendHeight / 2,
    width: GhgScale.triggerSize,
    height: GhgScale.triggerSize,
    label: trigger.name,
    payload: {
      name: trigger.name,
      when: whenOf(monthOf(trigger.date), unit),
      whenLong: whenOf(monthOf(trigger.date), unit),
      tags: tagsOf(trigger),
      snapX: -GhgScale.triggerSize / 2,
      snapY: (GhgScale.trendHeight - GhgScale.triggerSize) / 2,
    },
  }));
  const noteElements = notes.filter(keep).map((note): DiagramModelElement => {
    const width = Number(note.width);
    const height = Number(note.height);
    const text = note.body ?? note.text;
    return {
      id: note.id,
      type: GhgElementTypes.note,
      x: xOfMonth(monthOf(note.at), unit) + width / 2,
      y: Number(note.row) * GhgScale.rowStep + height / 2,
      width,
      height,
      label: text,
      payload: { text, snapX: 0, snapY: 0 },
    };
  });
  const elements = [...trendElements, ...triggerElements, ...noteElements];
  const held = new Set(elements.map((element) => element.id));
  const attachment = (influence: ExampleEntry, side: "from" | "to") =>
    influence[`${side}-phase`] === undefined
      ? undefined
      : { edge: influence[`${side}-edge`] as "top" | "bottom", region: GHG_PHASES.indexOf(influence[`${side}-phase`] as never), at: Number(influence[`${side}-at`]) };
  const connections = influences
    .filter((influence) => held.has(influence.from) && held.has(influence.to))
    .map((influence): DiagramModelConnection => ({
      id: influence.id,
      type: GhgRelationTypes.influence,
      sourceId: influence.from,
      targetId: influence.to,
      ...(attachment(influence, "from") !== undefined ? { sourceAttachment: attachment(influence, "from") } : {}),
      targetAttachment: attachment(influence, "to"),
    }));
  return { elements, connections };
}
