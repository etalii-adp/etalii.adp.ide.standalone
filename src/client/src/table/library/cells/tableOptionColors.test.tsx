import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { afterEach, describe, expect, it } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { contrastRatio, resolveToken, themeTokens } from "../../../themeContrast";
import { applyTableEvent, EMPTY_TABLE } from "../api/tableModel";
import { TableSurface } from "../TableSurface";
import { OPTION_COLORS, optionColor, OptionTag } from "./OptionTag";

/**
 * The ten option colours, held to the theme: each name has a token in both modes, each token
 * is a fill the theme's own text reads on, and each has a rule that paints a tag with it.
 *
 * <b>Why the tokens rather than something rendered.</b> The test renderer applies no stylesheet,
 * so a contrast taken from a mounted tag would measure nothing and pass. Both modes declare
 * literal hex, so the ratio a browser would show is computable from the source - with the one
 * `contrastRatio` the theme's other guards use, against `--color-text` of the same mode.
 */

const here = path.dirname(fileURLToPath(import.meta.url));
const indexCss = readFileSync(path.join(here, "..", "..", "..", "index.css"), "utf8");
const tableCss = readFileSync(path.join(here, "..", "table.css"), "utf8");
const tokens = themeTokens(indexCss);
const PREFIX = "--color-table-option-";

/** WCAG 2.x AA for normal-size text. */
const AA = 4.5;

afterEach(cleanup);

describe("the option colours of the theme", () => {
  it.each(["light", "dark"] as const)("declares exactly the ten names in %s mode", (mode) => {
    // Act: the tokens by their prefix, not from a list written here.
    const declared = [...tokens[mode].keys()].filter((name) => name.startsWith(PREFIX)).map((name) => name.slice(PREFIX.length));

    // Assert: a name missing from one mode is a tag with no colour in that mode.
    expect(declared.sort()).toEqual([...OPTION_COLORS].sort());
  });

  it.each(["light", "dark"] as const)("keeps the theme's text readable on every fill in %s mode", (mode) => {
    // Arrange.
    const text = resolveToken(tokens[mode], "--color-text")!;
    expect(text).toMatch(/^#[0-9a-f]{6}$/i);

    // Act.
    const ratios = OPTION_COLORS.map((name) => {
      const fill = resolveToken(tokens[mode], PREFIX + name);
      return { name, fill, ratio: fill === undefined ? 0 : contrastRatio(fill, text) };
    });

    // Assert: every one named with its number, so a failure says which and by how much.
    expect(ratios.filter((entry) => entry.ratio < AA).map((entry) => `${entry.name} ${entry.fill} is ${entry.ratio.toFixed(2)}:1 in ${mode} mode`)).toEqual([]);
  });

  it("declares each name again in the dark block, rather than inheriting the light fill", () => {
    // The dark mode reads what the dark block does not declare from the light one, so a name left
    // out of it is still "declared" - as a pale fill under light text. This is what sees that.
    const inherited = OPTION_COLORS.filter((name) => resolveToken(tokens.dark, PREFIX + name) === resolveToken(tokens.light, PREFIX + name));
    expect(inherited).toEqual([]);
  });

  it("gives the ten names ten fills in each mode, so two options can be told apart", () => {
    for (const mode of ["light", "dark"] as const) {
      const fills = OPTION_COLORS.map((name) => resolveToken(tokens[mode], PREFIX + name));
      expect(new Set(fills).size, mode).toBe(OPTION_COLORS.length);
    }
  });

  it("has a rule that paints each name's tag with its own token", () => {
    // Assert: the class the tag carries is ruled, and the rule reads that name's token and no other.
    for (const name of OPTION_COLORS) {
      const rule = new RegExp(`\\.table-option-${name}\\s*\\{[^}]*background:\\s*var\\(${PREFIX}${name}\\)`);
      expect(rule.test(tableCss), name).toBe(true);
    }
  });
});

describe("a cell that chooses among options", () => {
  it("shows a tag per option in the option's colour, and keeps one the column no longer has", () => {
    // Arrange.
    const structure = {
      title: "Cities",
      columns: [
        {
          id: "p1",
          name: "Kind",
          kind: "select",
          options: [
            { id: "o1", name: "Capital", color: "blue" },
            { id: "o2", name: "Port", color: "green" },
          ],
          width: 0,
          visible: true,
          isTitle: false,
          wraps: false,
          settings: {},
        },
      ],
      views: [],
      settings: EMPTY_TABLE.settings,
      rowCount: 1,
      readOnlyReason: "",
    };
    const row = { id: "r1", depth: 0, cells: [{ columnId: "p1", values: ["o2", "o1", "gone"], labels: [], pending: false }], isGroup: false, label: "", count: 0, collapsed: false, hasChildren: false, isNewRow: false };
    const model = applyTableEvent(applyTableEvent(EMPTY_TABLE, { kind: "baseline", structure, findings: [] }), { kind: "rows", first: 0, rows: [row], rowCount: 1 });

    // Act.
    render(<TableSurface model={model} definition={{ kinds: { select: { icon: "mdi-tag-multiple-outline", label: "Multiple selection", editor: "options" } } }} />);

    // Assert: in the cell's order, each in its own colour.
    const tags = [...screen.getByRole("gridcell").querySelectorAll(".table-option-tag")];
    expect(tags.map((tag) => `${tag.textContent}:${tag.className.replace("table-option-tag table-option-", "")}`)).toEqual(["Port:green", "Capital:blue", "gone:default"]);
  });
});

describe("OptionTag", () => {
  it("draws the option's name as a tag of its colour", () => {
    // Act.
    render(<OptionTag label="Capital" color="blue" />);

    // Assert.
    const tag = screen.getByText("Capital");
    expect(tag.classList.contains("table-option-tag")).toBe(true);
    expect(tag.classList.contains("table-option-blue")).toBe(true);
  });

  it("draws a colour the theme does not have as the default, never as a value from the file", () => {
    // Act.
    render(<OptionTag label="Port" color="#ff00ff" />);
    render(<OptionTag label="Hub" color="" />);

    // Assert.
    expect(screen.getByText("Port").className).toBe("table-option-tag table-option-default");
    expect(screen.getByText("Hub").className).toBe("table-option-tag table-option-default");
    expect(screen.getByText("Port").getAttribute("style")).toBeNull();
  });

  it("knows the ten names and no other", () => {
    expect(OPTION_COLORS).toHaveLength(10);
    expect(optionColor("purple")).toBe("purple");
    expect(optionColor("Purple")).toBe("default");
    expect(optionColor("teal")).toBe("default");
  });
});
