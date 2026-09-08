import { describe, expect, it } from "vitest";
import type { DiagramModelElement } from "../api/diagramModel";
import type { BindingSource } from "./binding";
import type { DecorationDeclaration } from "./diagramDefinition";
import { resolveDecorations } from "./decorations";

const element: DiagramModelElement = { id: "e1", type: "n", x: 0, y: 0 };
const bounds = { x: -50, y: -20, width: 100, height: 40 };
const source = (payload?: unknown): BindingSource => ({ element, payload });

/**
 * THE FOUR SHAPES THE FIVE COMPONENT-LESS RENDERERS DRAW, each written as its module writes it
 * today. This is the acceptance for the addition: if one of these cannot be expressed, the
 * vocabulary is wrong and the procedure is to extend it centrally - never to leave the module
 * imperative.
 */
describe("decorations — the stub (ansible, helm)", () => {
  // A line from the element's right edge to nowhere, with a label above it. Today: a <path>
  // through straightPath() plus a <text> at from.x + 8, from.y - 6.
  const stub: DecorationDeclaration[] = [
    {
      glyph: "line",
      from: { x: 50, y: 0 },
      to: { x: 40, y: 0 },
      text: { path: "payload.missing" },
      textAt: { x: 8, y: -6 },
      className: "ansible-edge-line",
    },
  ];

  it("draws a line from a point to a fixed length, with its label offset from the start", () => {
    const out = resolveDecorations(stub, source({ missing: "db_servers" }), bounds);

    expect(out).toHaveLength(1);
    expect(out[0]!.from).toEqual({ x: 50, y: 0 });
    // `to` is relative to `from`, which is what makes "a fixed length onward" declarable
    // without the author computing an absolute point.
    expect(out[0]!.to).toEqual({ x: 90, y: 0 });
    expect(out[0]!.text).toBe("db_servers");
    expect(out[0]!.textAt).toEqual({ x: 58, y: -6 });
  });
});

describe("decorations — the badge (causal-loop)", () => {
  // A polarity arc plus a centred caption. The arc is computed today by
  // loopMarkerPath(x, y, r, clockwise) inside the renderer; as a declaration the same path
  // arrives as DATA through a binding, which is what keeps it unable to call anything.
  const badge: DecorationDeclaration[] = [
    { glyph: "path", d: { path: "payload.markerPath" }, className: "causal-loop-marker" },
    { glyph: "marker", marker: "arrow", text: { path: "payload.caption" }, textAnchor: "middle" },
  ];

  it("takes a computed path as data rather than as a function", () => {
    const out = resolveDecorations(badge, source({ markerPath: "M0 0 A 13 13 0 1 1 1 0", caption: "R1" }), bounds);

    expect(out[0]!.d).toBe("M0 0 A 13 13 0 1 1 1 0");
    expect(out[1]!.text).toBe("R1");
    expect(out[1]!.textAnchor).toBe("middle");
  });

  it("omits a decoration whose condition fails, which is how one polarity draws and not the other", () => {
    const conditional: DecorationDeclaration[] = [
      { glyph: "path", d: { path: "payload.markerPath" }, when: { path: "payload.reinforcing", is: "true" } },
    ];

    expect(resolveDecorations(conditional, source({ markerPath: "M0 0", reinforcing: true }), bounds)).toHaveLength(1);
    expect(resolveDecorations(conditional, source({ markerPath: "M0 0", reinforcing: false }), bounds)).toHaveLength(0);
  });
});

describe("decorations — the annotation (sparql) and the target (wardley)", () => {
  it("draws bare positioned text with no glyph geometry at all", () => {
    const annotation: DecorationDeclaration[] = [
      { glyph: "marker", text: { path: "payload.note" }, textAt: { x: 0, y: 24 }, typography: { fontStyle: "italic" } },
    ];
    const out = resolveDecorations(annotation, source({ note: "OPTIONAL" }), bounds);

    expect(out[0]!.text).toBe("OPTIONAL");
    expect(out[0]!.textAt).toEqual({ x: 0, y: 24 });
    expect(out[0]!.typography).toEqual({ fontStyle: "italic" });
  });

  it("draws a circle with a bound radius and a label", () => {
    const target: DecorationDeclaration[] = [
      { glyph: "circle", radius: { path: "payload.r" }, text: { path: "payload.name" }, textAt: { x: 0, y: -12 } },
    ];
    const out = resolveDecorations(target, source({ r: 6, name: "target" }), bounds);

    expect(out[0]!.radius).toBe(6);
    expect(out[0]!.text).toBe("target");
  });
});

describe("decorations — resolution rules", () => {
  it("falls back rather than vanishing when a bound number does not resolve", () => {
    // A decoration that disappeared on a typo would be indistinguishable from one nobody
    // declared. It draws at the fallback instead, which is visible and therefore reportable.
    const out = resolveDecorations([{ glyph: "circle", radius: { path: "payload.nope" } }], source({}), bounds);

    expect(out).toHaveLength(1);
    expect(out[0]!.radius).toBe(0);
  });

  it("takes a literal number as readily as a binding, so authored geometry stays authored", () => {
    const out = resolveDecorations([{ glyph: "circle", radius: 9 }], source({}), bounds);
    expect(out[0]!.radius).toBe(9);
  });

  it("positions everything relative to the element's centre, not the canvas origin", () => {
    const shifted = { x: 200, y: 100, width: 100, height: 40 };
    const out = resolveDecorations([{ glyph: "line", from: { x: 0, y: 0 } }], source({}), shifted);

    expect(out[0]!.from).toEqual({ x: 250, y: 120 });
  });

  it("returns nothing for no declarations", () => {
    expect(resolveDecorations(undefined, source({}), bounds)).toEqual([]);
    expect(resolveDecorations([], source({}), bounds)).toEqual([]);
  });
});

/**
 * THE PROBLEM MARK AND THE POLARITY ARC — register entries G6 and G11.
 *
 * Both were unfillable rows: a severity class the library cannot know, and an arc that reads
 * as a stray curve without its arrowhead.
 */
describe("decorations — what rows 3, 4 and 8 needed", () => {
  it("binds a decoration's class, so a severity the library never heard of can colour it (G6)", () => {
    const declaration: DecorationDeclaration = {
      glyph: "marker",
      marker: "warning",
      className: { template: "pipeline-problem-mark-{payload.problem.severity}" },
      text: { path: "payload.problem.glyph" },
      tooltip: { path: "payload.problem.title" },
      when: { path: "payload.problem", is: "present" },
    };

    const resolved = resolveDecorations([declaration], source({ problem: { severity: "error", glyph: "✖", title: "Stage has no jobs" } }), bounds);

    expect(resolved).toHaveLength(1);
    expect(resolved[0]!.className).toBe("pipeline-problem-mark-error");
    expect(resolved[0]!.tooltip).toBe("Stage has no jobs");
    // And an element with no problem draws no mark at all - the condition, not an empty glyph.
    expect(resolveDecorations([declaration], source({}), bounds)).toEqual([]);
  });

  it("puts an arrowhead on the polarity arc, without which it reads as a stray curve (G11)", () => {
    // causal-loop's sweep: reinforcing clockwise, balancing anticlockwise, each a bound path
    // with an end marker, chosen by a condition rather than by a function argument.
    const declarations: DecorationDeclaration[] = [
      { glyph: "path", d: { path: "payload.clockwisePath" }, markerEnd: "arrow", when: { path: "payload.reinforcing", is: "true" } },
      { glyph: "path", d: { path: "payload.anticlockwisePath" }, markerEnd: "arrow", when: { path: "payload.reinforcing", is: "false" } },
    ];

    const reinforcing = resolveDecorations(declarations, source({ reinforcing: true, clockwisePath: "M 0 0 A 1 1", anticlockwisePath: "M 1 1 A 0 0" }), bounds);
    expect(reinforcing).toHaveLength(1);
    expect(reinforcing[0]!.d).toBe("M 0 0 A 1 1");
    expect(reinforcing[0]!.markerEnd).toBe("arrow");

    const balancing = resolveDecorations(declarations, source({ reinforcing: false, clockwisePath: "M 0 0 A 1 1", anticlockwisePath: "M 1 1 A 0 0" }), bounds);
    expect(balancing.map((decoration) => decoration.d)).toEqual(["M 1 1 A 0 0"]);
  });
});

/**
 * ONE GLYPH PER THING THE MODEL HAS — register entry G27, and four rows need it: a pipeline
 * stage's status indicators, a job's, mindmap's note/link/folded marks, wardley's decorator
 * badges. A fixed list of declarations cannot say "as many as there are".
 */
describe("decorations — the collection form", () => {
  const indicators = [
    { key: "disabled", glyph: "⊘", title: "Disabled: this will not run.", className: "pipeline-indicator-disabled" },
    { key: "manual", glyph: "▶", title: "Manual trigger: this waits to be started by a person." },
    { key: "condition", glyph: "?", title: "Runs only when: succeeded()" },
  ];

  it("draws one entry per item, stepped along from the declaration's origin", () => {
    const resolved = resolveDecorations(
      [
        {
          glyph: "marker",
          each: { path: "payload.indicators" },
          from: { x: 40, y: -10 },
          step: { x: -14, y: 0 },
          text: { path: "glyph" },
          tooltip: { path: "title" },
          className: { path: "className" },
          data: { testid: { template: "indicator-{key}" } },
        },
      ],
      source({ indicators }),
      bounds,
    );

    expect(resolved.map((decoration) => decoration.text)).toEqual(["⊘", "▶", "?"]);
    expect(resolved.map((decoration) => decoration.from.x)).toEqual([40, 26, 12]);
    // Rooted at the ITEM, like every other `each` in this vocabulary.
    expect(resolved[0]!.tooltip).toBe("Disabled: this will not run.");
    expect(resolved[0]!.className).toBe("pipeline-indicator-disabled");
    // A class the entry does not carry is absent rather than the string "undefined".
    expect(resolved[1]!.className).toBeUndefined();
    // And the test ids the module's own suite reads, one per entry, rooted at the entry.
    expect(resolved.map((decoration) => decoration.data?.testid)).toEqual(["indicator-disabled", "indicator-manual", "indicator-condition"]);
  });

  it("draws nothing at all when the collection is empty, rather than one empty glyph", () => {
    const resolved = resolveDecorations(
      [{ glyph: "marker", each: { path: "payload.indicators" }, text: { path: "glyph" } }],
      source({ indicators: [] }),
      bounds,
    );

    expect(resolved).toEqual([]);
  });

  it("gives each entry its own key, so a re-render does not reuse one glyph for another", () => {
    const resolved = resolveDecorations(
      [
        { glyph: "circle", each: { path: "payload.indicators" }, radius: 4 },
        { glyph: "rect", width: 10, height: 10 },
      ],
      source({ indicators }),
      bounds,
    );

    expect(new Set(resolved.map((decoration) => decoration.index)).size).toBe(resolved.length);
  });
});
