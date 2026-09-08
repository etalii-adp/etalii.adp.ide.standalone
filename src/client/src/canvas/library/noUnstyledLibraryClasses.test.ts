import { describe, expect, it } from "vitest";
import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";

/**
 * Every visual class the library emits has a rule behind it.
 *
 * ## The defect this was written for
 *
 * The library names the things it draws itself — the anchors it derives from a type's `anchors`
 * declaration, the preview path a connect gesture drags, the resize and adjust handles — with
 * `library-*` classes. **Thirty-one of its thirty-two classes had no CSS anywhere**, so each fell
 * back to the SVG defaults: `fill: black`, `stroke: none`.
 *
 * Two were reported from the running app:
 *
 * - every anchor on a timeline or dependency-graph element drew as a black dot;
 * - starting a connect drag swept a **solid black wedge** across the diagram, because filling a
 *   curve paints the region between the curve and its chord. The colour was not the bug — the
 *   bug was that a line was being filled at all.
 *
 * ## Why a test rather than care
 *
 * Nothing could see it. There is no render that fails, no exception, no type error: the element
 * appears, carries its class, and is painted black by the browser's defaults. `.library-canvas`
 * in `canvas.css` already documents an earlier instance of the same failure — a module passing a
 * class name that had no rule behind it, which there produced a runaway canvas height.
 *
 * So the guard is structural: collect the class names the library emits, collect the selectors
 * the stylesheets define, and require that every emitted name is either styled or explicitly
 * listed below as painted another way.
 */
describe("no unstyled library classes", () => {
  const libraryDir = join(__dirname);
  const cssRoot = join(__dirname, "..", "..");

  /**
   * Classes painted by an inline `style` the library passes through, so a CSS rule would be a
   * second source for the same colours rather than a fix.
   *
   * `library-shape` is the module's own paint — every call site passes `style={paint}`. The
   * others are structural or text, carrying no fill of their own.
   */
  const paintedElsewhere = new Set([
    "library-shape",
    "library-frame",
    "library-custom-",
    "library-fallback",
    "library-canvas-background",
    "library-canvas-surface",
    "library-element",
    "library-element-dragging",
    "library-element-label",
    // A declared decoration's text, and the same category as `library-element-label` beside it:
    // text carrying no fill of its own. Its colour comes from the declaration's own
    // `typography.color`, applied inline, or from the module's `className` on the surrounding
    // group - which is where a decoration's colour belongs, since the notation owns it.
    "library-decoration-text",
    "library-connection",
    "library-connection-label",
    "library-connection-line",
    "library-span",
    "library-span-label",
    "library-span-moment",
    "library-span-adorner",
    "library-span-hint",
    "library-arrow",
    "library-open-arrow",
    "library-circle",
    "library-diamond",
    "library-connect-target",
    "library-connect-forbidden",
    "library-layout-switcher",
    "library-layout-active",
  ]);

  /**
   * Class names as the library actually applies them, taken from string literals rather than
   * from the file's text.
   *
   * Scanning the raw source matched `diagram-library-adoption` inside a comment and reported it
   * as an unstyled class - a guard reading prose, which is the failure this repository has a
   * rule against. Quoted strings are where a class name can only be a class name.
   */
  function emittedClasses(source: string): Set<string> {
    const names = new Set<string>();
    for (const literal of source.matchAll(/["'`]([^"'`\n]*?)["'`]/g)) {
      for (const name of literal[1].split(/\s+/)) {
        if (/^library-[a-z-]+$/.test(name)) names.add(name);
      }
    }
    return names;
  }

  function read(dir: string, extension: string): string {
    let text = "";
    for (const entry of readdirSync(dir)) {
      const path = join(dir, entry);
      if (statSync(path).isDirectory()) {
        text += read(path, extension);
      } else if (entry.endsWith(extension) && !entry.includes(".test.")) {
        text += readFileSync(path, "utf8");
      }
    }
    return text;
  }

  it("styles every class the library paints with, or says why not", () => {
    // Arrange.
    const source = read(libraryDir, ".tsx");
    const css = read(cssRoot, ".css");

    const emitted = emittedClasses(source);
    const styled = new Set([...css.matchAll(/\.(library-[a-z-]+)/g)].map((m) => m[1]));

    // Assert.
    // Vacuously true of a library that emits nothing, so the floor comes first.
    expect(emitted.size).toBeGreaterThan(20);

    const unstyled = [...emitted].filter((c) => !styled.has(c) && !paintedElsewhere.has(c)).sort();
    expect(
      unstyled,
      `Emitted by the library and styled nowhere, so the browser paints them black. Give them a `
        + `rule in canvas.css, or add them to paintedElsewhere with a reason: ${unstyled.join(", ")}`,
    ).toEqual([]);
  });

  /**
   * The specific rule that stops the wedge. A curve with a fill paints the region between itself
   * and its chord, so this is not a colour preference — it is the difference between a line and
   * a shape.
   */
  it("draws the connect preview as a line rather than a filled region", () => {
    // Arrange.
    const css = read(cssRoot, ".css");

    // Act.
    const rule = /\.library-connect-preview\s*\{([^}]*)\}/.exec(css);

    // Assert.
    expect(rule, "the connect preview has no rule at all").not.toBeNull();
    expect(rule![1]).toMatch(/fill:\s*none/);
    expect(rule![1]).toMatch(/stroke:/);
  });

  /**
   * The library's own anchors stay pressable.
   *
   * They carry the connect gesture themselves — `anchorPress` is spread onto the circle — unlike
   * the shared `.canvas-anchor`, which can be inert because the modules using it draw a separate
   * hit circle behind it. Copying that rule wholesale, which is exactly what the first version of
   * this fix did, leaves the anchors looking correct and impossible to drag from: a regression
   * that no test would see and that looks like nothing at all on screen.
   */
  it("leaves the library's own anchors pressable", () => {
    // Arrange.
    const css = read(cssRoot, ".css");
    const source = read(libraryDir, ".tsx");

    // Assert.
    // The premise: the press handler really is on the anchor circle itself.
    expect(source).toMatch(/className="library-anchor"[\s\S]{0,200}anchorPress/);

    const rule = /\.library-anchor\s*\{([^}]*)\}/.exec(css);
    expect(rule, "the library's own anchors have no rule").not.toBeNull();
    expect(
      rule![1],
      "pointer-events: none on .library-anchor makes the connect gesture unreachable",
    ).not.toMatch(/pointer-events:\s*none/);
  });

  /** An anchor the library draws and one a module draws are the same dot. */
  it("gives the library's anchors the same fill as the shared ones", () => {
    // Arrange.
    const css = read(cssRoot, ".css");

    // Act.
    const shared = /\.canvas-anchor\s*\{([^}]*)\}/.exec(css);
    const library = /\.library-anchor[^{]*\{([^}]*)\}/.exec(css);

    // Assert.
    expect(shared).not.toBeNull();
    expect(library, "the library's own anchors have no rule").not.toBeNull();
    for (const property of ["fill", "stroke"]) {
      const of = (block: string) => new RegExp(`${property}:\\s*([^;]+)`).exec(block)?.[1].trim();
      expect(of(library![1]), `${property} differs from the shared anchor`).toEqual(of(shared![1]));
    }
  });
});
