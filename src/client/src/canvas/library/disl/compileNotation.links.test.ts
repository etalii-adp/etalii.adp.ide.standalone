import { describe, expect, it } from "vitest";
import { holds, resolveOne, type BindingSource } from "../definition/binding";
import { compileNotation, type NotationBindings } from "./compileNotation";
import type { DislDocument } from "./disTypes";

/**
 * A link between tools is drawn without module code (DISL 4.12, etalii.adp spec 092, R5): `has(self.link)` and
 * `has(self.linked)` ask whether the payload carries the member, which the backend sends for a linked element of
 * every tool type, and the link mark chooses its text by the same question.
 */

const BINDINGS: NotationBindings = { celPaths: {}, classNames: () => [] };

const SPEC = {
  disl: "0.12",
  language: { id: "test", version: "1.0.0" },
  metamodel: { types: { Box: { attributes: { label: { type: "string" }, link: { type: "link" } } } } },
  notation: {
    nodes: {
      Box: {
        shape: "rect",
        labels: [
          { id: "label", text: { attribute: "label" } },
          { id: "link", text: { cel: "has(self.linked) ? '↪' : '↪⚠'" }, visible: { cel: "has(self.link)" } },
        ],
      },
    },
  },
} as unknown as DislDocument;

function linkLabel() {
  return linkLabelOf(SPEC);
}

/** The second label, the link mark: the compiled labels keep the specification's order. */
function linkLabelOf(spec: DislDocument) {
  const label = compileNotation(spec, BINDINGS).elementTypes[0]!.labels?.[1];
  expect(label).toBeDefined();
  return label!;
}

function source(payload: unknown): BindingSource {
  return { element: { id: "n", type: "box", x: 0, y: 0 }, payload };
}

describe("compileNotation, links between tools", () => {
  it("shows the link mark only on an element whose payload carries a link", () => {
    const label = linkLabel();
    expect(holds(label.when, source({ label: "API", link: "$a@b.tml#c.label=API" }))).toBe(true);
    expect(holds(label.when, source({ label: "API" }))).toBe(false);
  });

  it("draws the mark of a target that resolves, and the broken mark of one that does not", () => {
    const label = linkLabel();
    expect(resolveOne(label.text, source({ link: "$a@b.tml#c", linked: {} }))).toBe("↪");
    expect(resolveOne(label.text, source({ link: "$a@b.tml#c" }))).toBe("↪⚠");
  });

  it("reads a negated has as the member's absence", () => {
    const spec = structuredClone(SPEC) as unknown as { notation: { nodes: { Box: { labels: { visible?: unknown }[] } } } };
    spec.notation.nodes.Box.labels[1]!.visible = { cel: "!has(self.link)" };
    const label = linkLabelOf(spec as unknown as DislDocument);
    expect(holds(label.when, source({}))).toBe(true);
    expect(holds(label.when, source({ link: "$a@b.tml" }))).toBe(false);
  });
});
