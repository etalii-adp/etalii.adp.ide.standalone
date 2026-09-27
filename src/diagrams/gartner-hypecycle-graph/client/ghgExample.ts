import { readFileSync } from "node:fs";
import { join } from "node:path";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { GHG_PHASES, GhgElementTypes, GhgRelationTypes, GhgScale, timeUnitOf, xOfMonth, type GhgTimeUnit } from "./ghgIds";

/**
 * An example document - technology-trends unless another is named - read line by line for the tests. The document is flat on purpose -
 * one `- id:` entry per trend or influence with scalar keys beneath it - so this reads exactly that
 * shape, and the counts asserted beside it say whether it read all of it.
 */
export interface ExampleEntry {
  [key: string]: string;
}

export function readExample(name = "technology-trends"): { trends: ExampleEntry[]; influences: ExampleEntry[]; unit: GhgTimeUnit } {
  const text = readFileSync(join(__dirname, "..", "examples", name, `${name}.ghg`), "utf8");
  const sections: Record<string, ExampleEntry[]> = { trends: [], influences: [] };
  let section: ExampleEntry[] | null = null;
  let entry: ExampleEntry | null = null;
  let unit: GhgTimeUnit = "month";

  for (const line of text.split(/\r?\n/)) {
    const named = /^unit: (\S+)$/.exec(line);
    if (named !== null) {
      unit = timeUnitOf(named[1]);
      continue;
    }
    const heading = /^(trends|influences):/.exec(line);
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
    }
  }

  return { trends: sections.trends, influences: sections.influences, unit };
}

// The year may be signed, as ISO 8601 writes a year before 1: `-3200-01`.
const monthOf = (text: string) => {
  const [, year, month] = /^(-?\d+)-(\d{2})$/.exec(text)!;
  return Number(year) * 12 + Number(month) - 1;
};

/** Whether an influence attaches to a phase its trend does not show - which hides it. */
export function isHidden(influence: ExampleEntry, trends: readonly ExampleEntry[]): boolean {
  const phasesOf = (id: string) => Number(trends.find((trend) => trend.id === id)!.phases);
  return GHG_PHASES.indexOf(influence["from-phase"] as never) >= phasesOf(influence.from)
    || GHG_PHASES.indexOf(influence["to-phase"] as never) >= phasesOf(influence.to);
}

/** The example as the canvas is handed it, limited to `ids` when given: centres, widths, attachments. */
export function exampleModel(ids?: readonly string[], name = "technology-trends"): DiagramModel {
  const { trends, influences, unit } = readExample(name);
  const kept = ids === undefined ? trends : trends.filter((trend) => ids.includes(trend.id));
  const elements = kept.map((trend): DiagramModelElement => {
    const width = xOfMonth(monthOf(trend.stop), unit) - xOfMonth(monthOf(trend.start), unit);
    const tags = /^\[(.*)\]$/.exec(trend.tags ?? "")?.[1].split(",").map((tag) => tag.trim()) ?? [];
    return {
      id: trend.id,
      type: GhgElementTypes.trend,
      x: xOfMonth(monthOf(trend.start), unit) + width / 2,
      y: Number(trend.row) * GhgScale.rowStep + GhgScale.trendHeight / 2,
      width,
      height: GhgScale.trendHeight,
      label: trend.name,
      payload: { name: trend.name, phases: Number(trend.phases), tags },
    };
  });
  const held = new Set(elements.map((element) => element.id));
  const connections = influences
    .filter((influence) => held.has(influence.from) && held.has(influence.to))
    .map((influence): DiagramModelConnection => ({
      id: influence.id,
      type: GhgRelationTypes.influence,
      sourceId: influence.from,
      targetId: influence.to,
      sourceAttachment: { edge: influence["from-edge"] as "top" | "bottom", region: GHG_PHASES.indexOf(influence["from-phase"] as never), at: Number(influence["from-at"]) },
      targetAttachment: { edge: influence["to-edge"] as "top" | "bottom", region: GHG_PHASES.indexOf(influence["to-phase"] as never), at: Number(influence["to-at"]) },
    }));
  return { elements, connections };
}
