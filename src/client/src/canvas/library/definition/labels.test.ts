import { describe, expect, it } from "vitest";
import type { DiagramModelElement } from "../api/diagramModel";
import type { BindingSource } from "./binding";
import type { LabelDeclaration } from "./diagramDefinition";
import { layoutLabels } from "./labels";

const element: DiagramModelElement = { id: "e1", type: "card", x: 0, y: 0 };
const bounds = { x: -100, y: -40, width: 200, height: 80 };
const source = (payload?: unknown): BindingSource => ({ element, payload });

describe("labels — the single-label case, which is most modules", () => {
  it("draws one centred line from a field binding", () => {
    const declarations: LabelDeclaration[] = [{ text: { path: "payload.name" } }];
    const out = layoutLabels(declarations, source({ name: "Pipeline.Core" }), bounds);

    expect(out).toHaveLength(1);
    expect(out[0]!.text).toBe("Pipeline.Core");
    expect(out[0]!.x).toBe(0);
    expect(out[0]!.anchor).toBe("middle");
  });

  it("draws nothing at all when its binding resolves to nothing", () => {
    // Not an empty string at the right place - no line. A module that declared a label for a
    // field some elements lack must not leave a blank row reserving space.
    expect(layoutLabels([{ text: { path: "payload.missing" } }], source({}), bounds)).toEqual([]);
  });

  it("places above, below and beside relative to the shape", () => {
    const at = (placement: LabelDeclaration["placement"]) =>
      layoutLabels([{ text: { path: "payload.n" }, placement }], source({ n: "x" }), bounds)[0]!;

    expect(at("above").y).toBeLessThan(bounds.y);
    expect(at("below").y).toBeGreaterThan(bounds.y + bounds.height);
    expect(at("beside").anchor).toBe("start");
    expect(at("beside").x).toBeGreaterThan(bounds.x + bounds.width);
  });

  it("truncates to the box only when asked", () => {
    const long = "a".repeat(200);
    const plain = layoutLabels([{ text: { path: "payload.n" } }], source({ n: long }), bounds)[0]!;
    const cut = layoutLabels([{ text: { path: "payload.n" }, truncate: true }], source({ n: long }), bounds)[0]!;

    expect(plain.text).toHaveLength(200);
    expect(cut.text.endsWith("…")).toBe(true);
    expect(cut.text.length).toBeLessThan(40);
  });
});

describe("labels — owl-card, which is the acceptance", () => {
  // THE SHAPE THIS ADDITION WAS SPECIFIED AGAINST, and the reason it binds to a collection
  // rather than enumerating slots. A design that passes with three fixed slots - which
  // `styled-box` already is - would serve c4 and leave the rdf family exactly where it is.
  const card: LabelDeclaration[] = [
    { text: { path: "payload.name" }, slot: "header", typography: { fontWeight: "bold" }, editable: true },
    {
      text: { template: "{payload.badgeCount} badges" },
      slot: "header",
      offset: undefined,
      when: { path: "payload.badges", is: "non-empty" },
    },
    {
      text: { path: "payload.rows", each: { template: "{predicate}: {value} {annotation}" } },
      slot: "body",
      stack: { lineHeight: 14, start: 6 },
      truncate: true,
      tooltip: { path: "payload.name" },
    },
  ];

  const rows = [
    { predicate: "born", value: "1867", annotation: "(Warsaw)" },
    { predicate: "died", value: "1934" },
    { predicate: "field", value: "physics" },
  ];

  it("draws a header, a conditional badge line and one line per collection entry", () => {
    const out = layoutLabels(card, source({ name: "Marie Curie", badges: ["nobel"], badgeCount: 1, rows }), bounds);

    expect(out.map((l) => l.text)).toEqual([
      "Marie Curie",
      "1 badges",
      "born: 1867 (Warsaw)",
      "died: 1934",
      "field: physics",
    ]);
  });

  it("omits the badge line when the badge list is empty, and keeps the rest", () => {
    // Replaces `badges.length > 0 ? … : null`, which is a ternary in the renderer today.
    const out = layoutLabels(card, source({ name: "Marie Curie", badges: [], badgeCount: 0, rows }), bounds);

    expect(out.map((l) => l.text)).toEqual(["Marie Curie", "born: 1867 (Warsaw)", "died: 1934", "field: physics"]);
  });

  it("stacks the collection lines at the declared line height", () => {
    const out = layoutLabels(card, source({ name: "n", badges: [], rows }), bounds);
    const body = out.filter((l) => l.declarationIndex === 2);

    expect(body).toHaveLength(3);
    expect(body[1]!.y - body[0]!.y).toBe(14);
    expect(body[2]!.y - body[1]!.y).toBe(14);
  });

  it("draws no body lines at all when the collection is empty", () => {
    const out = layoutLabels(card, source({ name: "n", badges: [], rows: [] }), bounds);
    expect(out.map((l) => l.text)).toEqual(["n"]);
  });

  it("carries the tooltip and the typography each line declared", () => {
    const out = layoutLabels(card, source({ name: "Marie Curie", badges: [], rows }), bounds);

    expect(out[0]!.typography).toEqual({ fontWeight: "bold" });
    expect(out[1]!.tooltip).toBe("Marie Curie");
  });

  it("refuses to mark a collection line editable, however the declaration is written", () => {
    // An editor over a computed line would commit to nothing: there is no single authored
    // field beneath `predicate: value annotation`. The declaration cannot opt into it, because
    // an author who tried would get an editor that silently discarded what they typed.
    const editableCollection: LabelDeclaration[] = [
      { text: { path: "payload.rows", each: { path: "predicate" } }, editable: true },
    ];
    const out = layoutLabels(editableCollection, source({ rows }), bounds);

    expect(out).toHaveLength(3);
    expect(out.every((line) => !line.editable)).toBe(true);
  });

  it("keeps a single-value label editable, so the existing behaviour survives", () => {
    // The pairing for the rule above: `editable` must still mean what `LabelRule.editable`
    // means today, or every migrated module loses inline rename.
    const out = layoutLabels(card, source({ name: "n", badges: [], rows: [] }), bounds);
    expect(out[0]!.editable).toBe(true);
  });
});

describe("labels — the empty cases", () => {
  it("returns nothing for no declarations, and for an empty list", () => {
    expect(layoutLabels(undefined, source({}), bounds)).toEqual([]);
    expect(layoutLabels([], source({}), bounds)).toEqual([]);
  });
});

/**
 * COLUMNS AND ALIGNMENT — sufficiency rows 20 and 13, and nothing else.
 *
 * A shacl constraint row is a path on the left, a summary in the middle and a cardinality on
 * the right, once per constraint. That is one line with three columns, not three collections.
 */
describe("labels — the row-with-columns case the rdf family needs", () => {
  it("aligns a label to the box's right edge, which a vertical slot cannot say (G15)", () => {
    const out = layoutLabels([{ text: { path: "payload.severity" }, align: "end" }], source({ severity: "Violation" }), bounds);

    expect(out[0]!.anchor).toBe("end");
    // At the right edge less the standard inset, rather than at the centre the slot would give.
    expect(out[0]!.x).toBe(bounds.x + bounds.width - 8);
  });

  it("draws a column on the SAME line, reading the SAME item", () => {
    const declaration: LabelDeclaration = {
      text: { path: "payload.rows", each: { path: "path" } },
      stack: { lineHeight: 16 },
      columns: [{ text: { path: "cardinality" }, insetX: 8, align: "end" }],
    };
    const out = layoutLabels(declaration ? [declaration] : [], source({ rows: [{ path: "sh:name", cardinality: "1..1" }, { path: "sh:age", cardinality: "0..1" }] }), bounds);

    const cardinalities = out.filter((line) => line.anchor === "end");
    expect(cardinalities.map((line) => line.text)).toEqual(["1..1", "0..1"]);
    // ON THE SAME LINE as the path it belongs to - the assertion that makes this a column
    // rather than a second label that happens to draw nearby.
    const paths = out.filter((line) => line.anchor !== "end");
    expect(cardinalities.map((line) => line.y)).toEqual(paths.map((line) => line.y));
  });

  it("keeps a column paired with its own row when an earlier row resolves to nothing", () => {
    // THE DRIFT THIS SHAPE EXISTS TO PREVENT, and the reason columns are not three separate
    // collections: a row whose text is absent contributes no line, so a column resolved
    // independently would slide up and pair row 2's cardinality with row 3's path. Here they
    // come from the same entry and cannot.
    const declaration: LabelDeclaration = {
      text: { path: "payload.rows", each: { path: "path" } },
      stack: { lineHeight: 16 },
      columns: [{ text: { path: "cardinality" }, insetX: 8, align: "end" }],
    };
    const rows = [{ path: "sh:name", cardinality: "1..1" }, { cardinality: "0..*" }, { path: "sh:age", cardinality: "0..1" }];
    const out = layoutLabels([declaration], source({ rows }), bounds);

    const pairs = out.reduce<Record<number, string[]>>((acc, line) => {
      (acc[line.y] ??= []).push(line.text);
      return acc;
    }, {});

    // Sorted, because what is being claimed is the PAIRING, not the order the two are emitted
    // in: a column drawn before its line paints the same picture.
    expect(Object.values(pairs).map((texts) => [...texts].sort())).toEqual([
      ["1..1", "sh:name"],
      ["0..1", "sh:age"],
    ]);
  });

  it("never marks a column editable, whatever the declaration says", () => {
    const out = layoutLabels(
      [{ text: { path: "payload.name" }, editable: true, columns: [{ text: { path: "payload.badge" }, insetX: 8 }] }],
      source({ name: "Shape", badge: "closed" }),
      bounds,
    );

    const badge = out.find((line) => line.text === "closed")!;
    expect(badge.editable).toBe(false);
    // The line itself stays editable: a column is a second value on somebody else's line, and
    // that is the only thing being denied here.
    expect(out.find((line) => line.text === "Shape")!.editable).toBe(true);
  });
});
