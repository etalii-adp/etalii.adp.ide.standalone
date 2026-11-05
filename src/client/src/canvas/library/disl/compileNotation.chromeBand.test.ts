import { describe, expect, it } from "vitest";
import { compileNotation, type NotationBindings } from "./compileNotation";
import type { DislDocument } from "./disTypes";

/**
 * The canvas's header band and a budget's notice (DISL §6.13 `canvas.header` and notices, §3.2.1): the
 * band's rows bound against the diagram's attributes, each with the module's class and drawn while its
 * `visible` holds, and a budget's sentence shown on the module's condition, since only its backend
 * knows the budget cut what is drawn. A read-only diagram uses both to say what it shows: its model's
 * form above the canvas, and "Showing N of M" over a model larger than it draws.
 */

function specOf(): DislDocument {
  return {
    disl: "0.3",
    language: {
      id: "test",
      version: "1.0.0",
      limits: {
        budgets: {
          drawn: {
            measure: { count: ["Item"] },
            notice: { text: { cel: "'Showing ' + string(budget('drawn').shown) + ' of ' + string(budget('drawn').total)" }, severity: "warning", position: "top" },
          },
        },
      },
    },
    metamodel: {
      diagram: { attributes: { form: { type: "string" }, modifiers: { type: "string" } } },
      types: { Item: { attributes: { name: { type: "string" } } } },
    },
    notation: {
      nodes: { Item: { shape: "rect", labels: [{ id: "name", text: { attribute: "name" } }] } },
      canvas: {
        header: {
          rows: [
            { id: "form", text: { attribute: "form" }, editable: false },
            { id: "modifiers", text: { attribute: "modifiers" }, editable: false, visible: { cel: "self.modifiers != ''" } },
          ],
        },
      },
    },
  };
}

const BINDINGS: NotationBindings = {
  celPaths: { "string(budget('drawn').shown)": "payload.shown", "string(budget('drawn').total)": "payload.total" },
  classNames: () => [],
  celConditions: { "self.modifiers != ''": { path: "payload.modifiers", is: "non-empty" } },
  canvasChrome: {
    header: { className: "t-band", rowClassName: (row) => `t-${row}` },
    notices: { "budget:drawn": { className: "t-notice", when: { path: "payload.cut", is: "true" } } },
  },
};

describe("compileNotation, for the canvas's header band and a budget's notice", () => {
  it("binds the header's rows to the diagram's attributes, each with its class and its visible", () => {
    expect(compileNotation(specOf(), BINDINGS).chrome?.header).toEqual({
      rows: [
        { text: { path: "payload.form" }, className: "t-form" },
        { text: { path: "payload.modifiers" }, when: { path: "payload.modifiers", is: "non-empty" }, className: "t-modifiers" },
      ],
      className: "t-band",
    });
  });

  it("refuses an editable header row, since the library draws a header and edits none", () => {
    const spec = specOf();
    (spec.notation.canvas!.header!.rows[0] as { editable: unknown }).editable = true;

    expect(() => compileNotation(spec, BINDINGS)).toThrow(/header row "form" is editable/);
  });

  it("shows a budget's notice on the module's condition, its sentence a template over the background", () => {
    expect(compileNotation(specOf(), BINDINGS).chrome?.notices).toEqual([
      {
        id: "budget:drawn",
        text: { template: "Showing {payload.shown} of {payload.total}" },
        severity: "warning",
        position: "top",
        when: { path: "payload.cut", is: "true" },
        className: "t-notice",
      },
    ]);
  });

  it("refuses a budget's notice the module gives no condition for", () => {
    expect(() => compileNotation(specOf(), { ...BINDINGS, canvasChrome: { header: BINDINGS.canvasChrome!.header } })).toThrow(/notice "budget:drawn".*canvasChrome\.notices/);
  });

  it("draws a declared notice of the budget's id in place of the budget's own, and none for a budget that does not cut", () => {
    const spec = specOf();
    const canvas = spec.notation.canvas as { notices?: unknown };
    canvas.notices = [{ id: "budget:drawn", text: "Cut", severity: "info", position: "bottom" }];
    expect(compileNotation(spec, BINDINGS).chrome?.notices).toMatchObject([{ id: "budget:drawn", text: { template: "Cut" }, severity: "info", position: "bottom" }]);

    const counted = specOf();
    (counted.language.limits!.budgets!.drawn as { truncate?: boolean }).truncate = false;
    expect(compileNotation(counted, BINDINGS).chrome?.notices).toBeUndefined();
  });

  it("refuses a declared notice that is no budget's", () => {
    const spec = specOf();
    (spec.notation.canvas as { notices?: unknown }).notices = [{ id: "welcome", text: "Hello" }];

    expect(() => compileNotation(spec, BINDINGS)).toThrow(/notice "welcome" is no budget's/);
  });
});
