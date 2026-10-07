import { describe, expect, it } from "vitest";
import disText from "../definition/timeline.dis?raw";
import { ELEMENT_HEIGHT, MOMENT_RADIUS, ROW_HEIGHT, TIMELINE_DEFINITION } from "./TimelineCanvas";

/**
 * The canvas definition is stated by hand (`TIMELINE_DEFINITION`), because what it draws - the span,
 * the named begin and end anchors, the loop-back route, the drag hint, the day snap carried in each
 * payload - is not in what `compileNotation` reads. These checks hold it to what the bundled DISL
 * specification (`definition/timeline.dis`) states, so the two cannot drift apart unnoticed.
 */

interface MenuTool {
  shortcut?: string;
}

interface Dis {
  coordinates: { axes: { rows: { scale: number } } };
  metamodel: { types: Record<string, { abstract?: boolean }>; relations: Record<string, { allowSelfLoops?: boolean }> };
  notation: {
    nodes: Record<string, { size: { default?: (number | null)[]; fixed?: number[] } }>;
    edges: Record<string, { labels: { distance: number }[] }>;
  };
  toolbox: { contextMenus: { tools: MenuTool[] }[] };
}

const dis = JSON.parse(disText) as Dis;

describe("the timeline's canvas definition, against its bundled specification", () => {
  it("draws the specification's two node types and one relation type", () => {
    // Arrange.
    const nodeTypes = Object.entries(dis.metamodel.types).filter(([, type]) => type.abstract !== true).map(([name]) => name.toLowerCase());

    // Assert.
    expect(TIMELINE_DEFINITION.elementTypes.map((type) => type.id).sort()).toEqual(nodeTypes.sort());
    expect(TIMELINE_DEFINITION.relationTypes).toHaveLength(Object.keys(dis.metamodel.relations).length);
  });

  it("refuses a relation from an element to itself, as the specification does", () => {
    // Assert.
    expect(dis.metamodel.relations.Connection.allowSelfLoops).toBe(false);
    expect(TIMELINE_DEFINITION.relationTypes[0].endpoints?.allowSelf).toBe(false);
  });

  it("sends every key the specification's menus bind to the backend", () => {
    // Arrange.
    const specified = new Set(dis.toolbox.contextMenus.flatMap((menu) => menu.tools).flatMap((tool) => (tool.shortcut ? [tool.shortcut] : [])));
    const sent = new Set((TIMELINE_DEFINITION.actions ?? []).flatMap((action) => (action.backendKey ? [action.backendKey] : [])));

    // Assert.
    expect([...specified].filter((key) => !sent.has(key))).toEqual([]);
  });

  it("uses the specification's row height, element height, moment size and relation label distance", () => {
    // Assert.
    expect(ROW_HEIGHT).toBe(dis.coordinates.axes.rows.scale);
    expect(ELEMENT_HEIGHT).toBe(dis.notation.nodes.Period.size.default?.[1]);
    expect([MOMENT_RADIUS * 2, MOMENT_RADIUS * 2]).toEqual(dis.notation.nodes.Moment.size.fixed);
    expect(TIMELINE_DEFINITION.relationTypes[0].label?.offset).toBe(-dis.notation.edges.Connection.labels[0].distance);
  });
});
