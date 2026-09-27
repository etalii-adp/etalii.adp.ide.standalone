import { readFileSync } from "node:fs";
import { join } from "node:path";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { GHG_PHASES, GhgElementTypes, GhgRelationTypes, GhgScale, xOfMonth } from "./ghgIds";

/**
 * The technology-trends example, read line by line for the tests. The document is flat on purpose -
 * one `- id:` entry per trend or influence with scalar keys beneath it - so this reads exactly that
 * shape, and the counts asserted beside it say whether it read all of it.
 */
export interface ExampleEntry {
  [key: string]: string;
}

export function readExample(): { trends: ExampleEntry[]; influences: ExampleEntry[] } {
  const text = readFileSync(join(__dirname, "..", "examples", "technology-trends", "technology-trends.ghg"), "utf8");
  const sections: Record<string, ExampleEntry[]> = { trends: [], influences: [] };
  let section: ExampleEntry[] | null = null;
  let entry: ExampleEntry | null = null;

  for (const line of text.split(/\r?\n/)) {
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

  return { trends: sections.trends, influences: sections.influences };
}

const monthOf = (text: string) => {
  const [year, month] = text.split("-").map(Number);
  return year * 12 + month - 1;
};

/** Whether an influence attaches to a phase its trend does not show - which hides it. */
export function isHidden(influence: ExampleEntry, trends: readonly ExampleEntry[]): boolean {
  const phasesOf = (id: string) => Number(trends.find((trend) => trend.id === id)!.phases);
  return GHG_PHASES.indexOf(influence["from-phase"] as never) >= phasesOf(influence.from)
    || GHG_PHASES.indexOf(influence["to-phase"] as never) >= phasesOf(influence.to);
}

/** The example as the canvas is handed it, limited to `ids` when given: centres, widths, attachments. */
export function exampleModel(ids?: readonly string[]): DiagramModel {
  const { trends, influences } = readExample();
  const kept = ids === undefined ? trends : trends.filter((trend) => ids.includes(trend.id));
  const elements = kept.map((trend): DiagramModelElement => {
    const width = (monthOf(trend.stop) - monthOf(trend.start)) * GhgScale.unitsPerMonth;
    const tags = /^\[(.*)\]$/.exec(trend.tags ?? "")?.[1].split(",").map((tag) => tag.trim()) ?? [];
    return {
      id: trend.id,
      type: GhgElementTypes.trend,
      x: xOfMonth(monthOf(trend.start)) + width / 2,
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
