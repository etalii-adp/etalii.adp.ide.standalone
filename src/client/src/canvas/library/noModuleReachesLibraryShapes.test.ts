import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { sourceFiles } from "@client/sourceFiles";

/**
 * No module stylesheet selects an SVG element type; a module styles its shapes by the classes it
 * declares on them (client-centralization Requirement 3).
 *
 * <b>The incident.</b> `.mindmap-node rect` was written for the mind map's own box. The library's
 * selection ring was also a `rect` in that group, so the rule matched it too, outranked the ring's
 * single class, and painted an opaque box over the label (`59f1ed3a`). A descendant type selector
 * reaches everything of that type the library ever draws inside the module's element, whatever
 * the library adds later, so the module cannot know what it is styling.
 *
 * <b>The latent one, and why this reads text rather than looking.</b> `wardley.css` said
 * `.wardley-annotation circle`, which also matches any circle the library draws in that group and
 * sets it to the colour it already had - nothing looked wrong, so no eye would ever find it. A
 * structural rule finds it the day it is written. The mounted `highlightSurvivesModuleStyles`
 * stays as the rendered half: it proves the highlight survives whatever rules exist, where this
 * proves no rule can reach a library shape by its type in the first place.
 *
 * <b>Every type, not only as a descendant.</b> Requirement 3.1 names the descendant form because
 * that is what the tree had. A bare `rect { }`, or `path.x` qualified by a class, reaches library
 * shapes just the same, so any SVG element type in a module selector fails - compound, child,
 * sibling or alone.
 *
 * <b>Its limits.</b> It reads selectors, so it cannot see a class a module puts on something the
 * library draws, nor an attribute selector (`[d]`) standing in for a type. The positive controls
 * prove the matcher still sees each selector shape it polices, so a matcher gone blind fails here
 * rather than reporting a clean tree.
 */

/** The SVG element types a library shape, anchor, label or decoration can be drawn as. */
const SVG_TYPES = ["rect", "path", "circle", "ellipse", "polygon", "polyline", "line", "text", "tspan", "g", "use", "image"];

/**
 * A type selector: the name standing as a compound's type, so bounded by the start, whitespace, a
 * combinator, a comma or a parenthesis on the left, and on the right by the end or anything that
 * cannot continue an identifier. `.databricks-frame-rect` and `#text` are not matched.
 */
const TYPE_SELECTOR = new RegExp(`(^|[\\s>+~,(])(${SVG_TYPES.join("|")})(?![\\w-])`);

/** The selectors in a stylesheet, comments and declaration blocks removed, at-rule preludes skipped. */
function selectorsOf(css: string): string[] {
  const text = css.replace(/\/\*[\s\S]*?\*\//g, " ");
  const selectors: string[] = [];
  let prelude = "";
  for (const character of text) {
    if (character === "{") {
      const trimmed = prelude.trim();
      // An at-rule's prelude (`@media (…)`) is not a selector; the rules nested in it are.
      if (trimmed.length > 0 && !trimmed.startsWith("@")) {
        selectors.push(...trimmed.split(",").map((part) => part.trim()).filter((part) => part.length > 0));
      }
      prelude = "";
    } else if (character === "}" || character === ";") {
      // A declaration or a block's end: whatever came before it was not a selector.
      prelude = "";
    } else {
      prelude += character;
    }
  }
  return selectors;
}

/** The selectors in `css` that name an SVG element type. */
function typeSelectorsIn(css: string): string[] {
  // Attribute values and strings could hold a type name as text; they are not selectors.
  return selectorsOf(css).filter((selector) => TYPE_SELECTOR.test(selector.replace(/\[[^\]]*\]/g, "[]")));
}

/** The repository's src folder, found by walking up - the family's shared idiom. */
function sourceRoot(): string {
  let directory = dirname(fileURLToPath(import.meta.url));
  for (let depth = 0; depth < 12; depth++) {
    const hasModules = statSync(join(directory, "diagrams"), { throwIfNoEntry: false })?.isDirectory() === true;
    const hasStyleRules = statSync(join(directory, ".editorconfig"), { throwIfNoEntry: false })?.isFile() === true;
    if (hasModules && hasStyleRules) {
      return directory;
    }
    directory = dirname(directory);
  }
  throw new Error("The src folder was not found above this test file.");
}

/** Every stylesheet in each diagram and editor module's client folder. */
function moduleStylesheets(root: string): string[] {
  return ["diagrams", "editors"].flatMap((family) => {
    const base = join(root, family);
    if (statSync(base, { throwIfNoEntry: false })?.isDirectory() !== true) {
      return [];
    }
    return readdirSync(base, { withFileTypes: true })
      .filter((entry) => entry.isDirectory())
      .map((module) => join(base, module.name, "client"))
      .filter((client) => statSync(client, { throwIfNoEntry: false })?.isDirectory() === true)
      .flatMap((client) => sourceFiles(client).filter((path) => path.endsWith(".css")));
  });
}

describe("no module stylesheet reaches a library shape by its type", () => {
  it.each([
    [".mindmap-node rect { fill: red; }", ".mindmap-node rect"],
    [".wardley-annotation circle, .x { stroke: red; }", ".wardley-annotation circle"],
    [".dependency-graph-relation path.dependency-graph-relation-line { marker-end: none; }", ".dependency-graph-relation path.dependency-graph-relation-line"],
    [".a > text { fill: red; }", ".a > text"],
    ["@media (prefers-color-scheme: dark) { .a line { stroke: red; } }", ".a line"],
    ["rect { fill: red; }", "rect"],
  ])("sees %s - the matcher still recognises each shape it polices", (css, offender) => {
    // Assert: the positive control. Without it, an offender list of [] could mean a matcher gone blind.
    expect(typeSelectorsIn(css)).toEqual([offender]);
  });

  it.each([
    ".mindmap-node-box { fill: red; }",
    ".databricks-frame-rect, .a-text-muted { fill: red; }",
    ".sparql-region-optional .sparql-region-outline { stroke-dasharray: 4 4; }",
    "/* .mindmap-node rect */ .a { fill: red; }",
    ".a[data-kind=\"rect\"] { fill: red; }",
    "@media (max-width: 600px) { .a { fill: red; } }",
  ])("passes %s - a class, a comment or an attribute value is not a type", (css) => {
    expect(typeSelectorsIn(css)).toEqual([]);
  });

  it("walks every module stylesheet, including the ones that used to reach by type", () => {
    // Arrange.
    const root = sourceRoot();

    // Act.
    const walked = moduleStylesheets(root).map((file) => relative(root, file).replaceAll("\\", "/"));

    // Assert: the completeness canary - a floor on the walk, and the seven sheets this requirement
    // rewrote are all in it.
    expect(walked.length, walked.join("\n")).toBeGreaterThanOrEqual(16);
    for (const sheet of [
      "diagrams/c4/client/c4.css",
      "diagrams/causal-loop-diagram/client/causal-loop.css",
      "diagrams/databricks/client/databricks.css",
      "diagrams/dependency-graph/client/dependency-graph.css",
      "diagrams/mindmap/client/mindmap.css",
      "diagrams/sparql/client/sparql.css",
      "diagrams/wardley-map/client/wardley.css",
    ]) {
      expect(walked, sheet).toContain(sheet);
    }
  });

  it("finds no module stylesheet that selects an SVG element type", () => {
    // Arrange.
    const root = sourceRoot();

    // Act.
    const offenders = moduleStylesheets(root).flatMap((file) =>
      typeSelectorsIn(readFileSync(file, "utf8")).map((selector) => `${relative(root, file).replaceAll("\\", "/")}: ${selector}`),
    );

    // Assert.
    expect(offenders, `style these shapes by a class the module declares on them:\n${offenders.join("\n")}`).toEqual([]);
  });
});
