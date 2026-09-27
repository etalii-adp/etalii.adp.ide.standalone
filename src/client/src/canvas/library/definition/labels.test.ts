import { describe, expect, it } from "vitest";
import type { DiagramModelElement } from "../api/diagramModel";
import type { BindingSource } from "./binding";
import type { BuiltInShape, LabelDeclaration } from "./diagramDefinition";
import { layoutLabels } from "./labels";
import { LABEL_FONT_SIZE, widthOf } from "../../label/textMetrics";
import { textRegionOf } from "../shapes/outline";

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

  it("ends a before label 8 units left of the element, centred on it and end-anchored", () => {
    // Arrange, act: the anchor and the x together decide where the text's RIGHT edge is, so both
    // are asserted - a `before` that kept `beside`'s start anchor would run the name INTO the
    // element from 8 units out, with the x exactly right.
    const line = layoutLabels([{ text: { path: "payload.n" }, placement: "before" }], source({ n: "Steam engine" }), bounds)[0]!;

    // Assert.
    expect(line.anchor).toBe("end");
    expect(line.x).toBe(bounds.x - 8);
    // The same baseline offset every placement here adds to its centre line.
    expect(line.y).toBe(bounds.y + bounds.height / 2 + 4);
  });

  it("truncates to the box only when asked", () => {
    const long = "a".repeat(200);
    const plain = layoutLabels([{ text: { path: "payload.n" } }], source({ n: long }), bounds)[0]!;
    const cut = layoutLabels([{ text: { path: "payload.n" }, truncate: true }], source({ n: long }), bounds)[0]!;

    expect(plain.text).toHaveLength(200);
    expect(cut.text.endsWith("…")).toBe(true);
    expect(cut.text.length).toBeLessThan(40);
  });

  it("truncates a side-aligned line or column to the room between its inset and the far edge, not to the whole box", () => {
    // SHACL's summary column starts 110 into a 260 card. Trimmed to the whole box it kept 36
    // characters - 252 wide by the estimate then - and ran a hundred past the card's right edge.
    // By the same metric truncation uses, every cut line must end inside the box, eight short of
    // the edge it runs towards.
    const card = { x: 0, y: 0, width: 260, height: 80 };
    const long = "or({ datatype xsd:string }, { class ex:Address }) and then some";
    const rows = [{ path: long, summary: long, cardinality: long }];
    const out = layoutLabels(
      [
        {
          text: { path: "payload.rows", each: { path: "path" } },
          align: "start",
          insetX: 60,
          truncate: true,
          className: "path",
          columns: [
            { text: { path: "summary" }, insetX: 110, align: "start", truncate: true, className: "summary" },
            { text: { path: "cardinality" }, insetX: 90, align: "end", truncate: true, className: "cardinality" },
          ],
        },
      ],
      source({ rows }),
      card,
    );
    const byClass = (name: string) => out.find((line) => line.className === name)!;

    for (const name of ["path", "summary"]) {
      const line = byClass(name);
      expect(line.text.endsWith("…")).toBe(true);
      expect(line.x - card.x + widthOf(line.text, LABEL_FONT_SIZE)).toBeLessThanOrEqual(card.width - 8);
    }
    const end = byClass("cardinality");
    expect(end.text.endsWith("…")).toBe(true);
    expect(end.x - widthOf(end.text, LABEL_FONT_SIZE)).toBeGreaterThanOrEqual(card.x + 8);
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

describe("labels — an offset measured from an edge (sufficiency row 5)", () => {
  it("keeps a line the same distance below the box's TOP however tall the box is", () => {
    // A C4 card's three lines are pinned 22, 38 and 58 below the top, so they stay put as the
    // card grows. Measured from the CENTRE - the default - the same declaration drifts down by
    // half the extra height, which is what the sabotage that found this hole did.
    const declaration: LabelDeclaration = { text: { path: "payload.name" }, anchorTo: "top", offset: { x: 0, y: 22 } };
    const short = layoutLabels([declaration], source({ name: "Customer" }), { x: -100, y: -40, width: 200, height: 80 });
    const tall = layoutLabels([declaration], source({ name: "Customer" }), { x: -100, y: -90, width: 200, height: 180 });

    expect(short[0]!.y).toBe(-40 + 22);
    expect(tall[0]!.y).toBe(-90 + 22);
  });

  it("measures from the centre when nothing says otherwise, which every other module wants", () => {
    const declaration: LabelDeclaration = { text: { path: "payload.name" }, offset: { x: 0, y: 22 } };
    const out = layoutLabels([declaration], source({ name: "Customer" }), { x: -100, y: -40, width: 200, height: 80 });

    expect(out[0]!.y).toBe(22);
  });

  it("measures from the bottom when asked, so a footer line rides the lower edge", () => {
    const declaration: LabelDeclaration = { text: { path: "payload.name" }, anchorTo: "bottom", offset: { x: 0, y: -6 } };
    const out = layoutLabels([declaration], source({ name: "Customer" }), { x: -100, y: -40, width: 200, height: 80 });

    expect(out[0]!.y).toBe(40 - 6);
  });
});

describe("labels — a stack that starts where the card says (rows 16, 19, 20)", () => {
  it("begins the rows below the badge line on a card that has one, and at the header on a card that does not", () => {
    // THE RDF FAMILY'S CARD, and the reason a stack's start is bindable. An owl card's rows
    // begin below its badges when it wears any; a fixed start puts every badged card one row
    // out - and silently, because the rows are still there, just overlapping the badges.
    //
    // A sabotage found this hole: `start: 0` left all forty-one rdf tests green.
    const declaration: LabelDeclaration = {
      text: { path: "payload.rows", each: { path: "text" } },
      anchorTo: "top",
      offset: { x: 0, y: 0 },
      stack: { lineHeight: 16, start: { path: "payload.rowsStart" } },
    };
    const bounds = { x: -110, y: -50, width: 220, height: 100 };
    const rows = [{ text: "a: 1" }, { text: "b: 2" }];

    const badged = layoutLabels([declaration], source({ rows, rowsStart: 42 }), bounds);
    const bare = layoutLabels([declaration], source({ rows, rowsStart: 26 }), bounds);

    expect(badged.map((line) => line.y)).toEqual([-50 + 42, -50 + 42 + 16]);
    expect(bare.map((line) => line.y)).toEqual([-50 + 26, -50 + 26 + 16]);
  });

  it("falls back to no offset when the card names no start, rather than dropping the rows", () => {
    const declaration: LabelDeclaration = {
      text: { path: "payload.rows", each: { path: "text" } },
      anchorTo: "top",
      offset: { x: 0, y: 0 },
      stack: { lineHeight: 16, start: { path: "payload.missing" } },
    };
    const bounds = { x: -110, y: -50, width: 220, height: 100 };

    const out = layoutLabels([declaration], source({ rows: [{ text: "a" }] }), bounds);

    expect(out.map((line) => line.y)).toEqual([-50]);
  });
});

/**
 * ALIGNMENT AND OFFSET ARE ORTHOGONAL, and this is the guard that says so.
 *
 * They were conflated: an `offset` suppressed the `align` entirely, so a declaration stating
 * both got its y and lost its x. Azure-pipeline's stage name states both, and drew CENTRED over
 * a card whose name has sat at the top left since the module was written. Sixty-eight of that
 * module's tests passed against it, because not one of them asks where the name is - which is
 * what made this a browser finding rather than a red suite.
 */
describe("labels - an offset moves the line down, it does not un-align it", () => {
  it("keeps a declared alignment when the same declaration also states an offset", () => {
    const declaration: LabelDeclaration = {
      text: { path: "payload.name" },
      align: "start",
      insetX: 12,
      anchorTo: "top",
      offset: { x: 0, y: 18 },
    };
    const out = layoutLabels([declaration], source({ name: "Build" }), bounds)[0]!;

    // x from the ALIGNMENT - the left edge plus its inset, not the centre the offset gives.
    expect(out.x).toBe(bounds.x + 12);
    expect(out.anchor).toBe("start");
    // y from the OFFSET, measured from the top edge as `anchorTo` says. Both, not either.
    expect(out.y).toBe(bounds.y + 18);
  });

  it("still centres a line that states an offset and no alignment", () => {
    // The other half of the claim: nothing changes for a declaration that never asked to be
    // aligned, which is every offset label written before `align` existed.
    const out = layoutLabels([{ text: { path: "payload.name" }, anchorTo: "top", offset: { x: 0, y: 18 } }], source({ name: "Build" }), bounds)[0]!;

    expect(out.x).toBe(0);
    expect(out.anchor).toBe("middle");
    expect(out.y).toBe(bounds.y + 18);
  });
});

describe("a wrapped label is laid out inside the shape, not inside its bounding box", () => {
  /**
   * The widths here are asserted against `widthOf`, the metric the wrapper itself measures with,
   * at the declaration's font size, rather than against a literal per-character width. A guard
   * naming the literal would pass while disagreeing with the code, and would have to be edited the
   * day the metric changes - a second copy of the data rather than a check on it.
   */
  const wrapped = (extra: Partial<LabelDeclaration> = {}): LabelDeclaration => ({
    text: { path: "payload.note" },
    wrap: true,
    typography: { fontSize: 10 },
    ...extra,
  });

  /** The label's text arrives through the binding, as every other declaration's does. */
  const withText = (text: string) => source({ note: text });

  /** A 160-wide box, the width the task names, at the notation's shared element height. */
  const box = { x: -80, y: -24, width: 160, height: 48 };

  it("breaks a long label onto more than one line, none wider than the region", () => {
    // Arrange: sixty characters, which cannot fit one 160-wide line at the library's estimate.
    const label = "The quick brown fox jumps over the lazy dog and keeps going";
    expect(label).toHaveLength(59);

    // Act. A box tall enough for the wrap, so this test is about breaking rather than about
    // truncation - the 48-tall box has room for two lines, and cutting is the next test's subject.
    const tall = { ...box, height: 120 };
    const out = layoutLabels([wrapped()], withText(label), tall, 1, "box");

    // Assert: more than one line, and every line fits the room the shape gives.
    expect(out.length).toBeGreaterThan(1);
    const region = textRegionOf("box", tall, out.length * 14);
    for (const line of out) {
      expect(widthOf(line.text, 10), line.text).toBeLessThanOrEqual(region.width);
    }
    // Every word survives the break, in order: a wrap that dropped one would still fit.
    expect(out.map((line) => line.text).join(" ").split(/\s+/)).toEqual(label.split(" "));
  });

  it("starts a new line at an explicit newline, whether or not the line was full", () => {
    // Act.
    const out = layoutLabels([wrapped()], withText("Short\nSecond"), box, 1, "box");

    // Assert.
    expect(out.map((line) => line.text)).toEqual(["Short", "Second"]);
  });

  it("ends the last visible line with an ellipsis, and keeps the whole text as the tooltip", () => {
    // Arrange: far more text than a 48-tall box has lines for.
    const label = Array.from({ length: 40 }, (_, index) => `word${index}`).join(" ");

    // Act.
    const out = layoutLabels([wrapped()], withText(label), box, 1, "box");

    // Assert: it says it was cut rather than being drawn outside the shape or silently dropped.
    expect(out[out.length - 1]!.text.endsWith("…")).toBe(true);
    expect(out[out.length - 1]!.tooltip).toBe(label);
  });

  it("measures a trapezoid against its slanted sides, so its text is narrower than a box's", () => {
    // Arrange: the same label and the same bounds, in two shapes. A trapezoid's bottom edge runs
    // from 0.15 to 0.85 of the width, so the room for text is narrower than the bounding box -
    // which is the whole reason this is measured against the outline.
    const label = "The quick brown fox jumps over the lazy dog and keeps going";

    // Act.
    const inBox = layoutLabels([wrapped()], withText(label), box, 1, "box");
    const inTrapezoid = layoutLabels([wrapped()], withText(label), box, 1, "trapezoid");

    // Assert: the trapezoid takes at least as many lines, and no line of it exceeds the narrower
    // region. Measuring against the bounding box is the planted defect this catches.
    expect(inTrapezoid.length).toBeGreaterThanOrEqual(inBox.length);
    const region = textRegionOf("trapezoid", box, inTrapezoid.length * 14);
    for (const line of inTrapezoid) {
      expect(widthOf(line.text, 10), line.text).toBeLessThanOrEqual(region.width);
    }
    // And the two really are different layouts, or this test would pass on a box-measured wrap.
    expect(inTrapezoid.map((line) => line.text)).not.toEqual(inBox.map((line) => line.text));
  });

  it("leaves an unwrapped label exactly as it was: one line, untouched", () => {
    // The capability is opt-in; a declaration that says nothing keeps today's single line.
    const label = "The quick brown fox jumps over the lazy dog and keeps going";
    const out = layoutLabels([{ text: { path: "payload.note" } }], withText(label), box, 1, "trapezoid");
    expect(out).toHaveLength(1);
    expect(out[0]!.text).toBe(label);
  });
});

describe("a truncated line inside an outlined shape is fitted to the shape, not to its bounding box", () => {
  // Found by Developer 1 in the functional decomposition graph's browser pass: on a parallelogram
  // 80 wide, a truncated name's ink came within about 1.3 units of the slanted edge - the box's
  // corner sits past it - where a wrapped label in the same shape keeps the region's padding.
  // Trimmed to the BOUNDING BOX, a single line cannot know the shape is narrower at its band.
  const small = { x: 0, y: 0, width: 80, height: 40 };
  const long = "Reconcile the ledger";
  const lineHeight = Math.round(12 * 1.4);

  const truncatedIn = (shape: BuiltInShape) =>
    layoutLabels([{ text: { path: "payload.n" }, truncate: true }], source({ n: long }), small, 1, shape)[0]!;

  it.each(["parallelogram", "trapezoid", "diamond", "hexagon", "superellipse", "diode"] as const)(
    "keeps a %s's truncated line inside its text region",
    (shape) => {
      const region = textRegionOf(shape, small, lineHeight);
      const line = truncatedIn(shape);
      const half = widthOf(line.text, LABEL_FONT_SIZE) / 2;

      // The arrangement, asserted: a line that was never cut proves nothing about cutting.
      expect(line.text.endsWith("…"), "the line was not long enough to be truncated").toBe(true);
      expect(line.x - half, "the ink starts left of the text region").toBeGreaterThanOrEqual(region.x);
      expect(line.x + half, "the ink runs past the text region").toBeLessThanOrEqual(region.x + region.width);
    },
  );

  it("leaves a box's truncated line exactly as it was", () => {
    // The must-not-catch half. A box has no outline, so its region is only the box less padding,
    // and fitting it there would narrow every truncated label every module draws today. Pinned to
    // the value the bounding-box trim has always given.
    const line = truncatedIn("box");

    expect(line.text).toBe("Reconcile…");
    expect(line.x).toBe(40);
  });
});
