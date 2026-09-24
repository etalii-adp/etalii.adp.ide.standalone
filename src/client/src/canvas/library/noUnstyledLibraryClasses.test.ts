import { describe, expect, it } from "vitest";
import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";
import ts from "typescript";

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
 *
 * ## WHAT THIS GUARD DOES NOT DO, and it has already cost a visible defect
 *
 * **THIS INVENTORY ACCOUNTS FOR THE EXISTENCE OF A RULE, NEVER ITS ADEQUACY.** A class can be
 * ruled, or truthfully listed below, and still be wrong on a property no rule mentions.
 *
 * That is not hypothetical. A second vertical scrollbar appeared in the diagram pane because the
 * library's `<svg>` was left `display: inline` and reserved four pixels of descender space.
 * `library-canvas-surface` was on the list below throughout, **with a reason, and the reason was
 * TRUE** — the class carries no fill, and none should be given to it. The defect was `display`,
 * and `canvas-drawing` beside it in the same attribute does have a rule that is silent about
 * `display`. So this guard was green for the whole life of that defect, and a specification
 * written to strengthen it was archived once that was measured: **strengthening the wrong
 * question does not make it the right one.**
 *
 * Do not motivate work from a layout defect by pointing at this file. It cannot see one.
 *
 * ## The `library-` prefix is a namespace the canvas library owns
 *
 * A class named `library-*` is the library's, wherever it is written. So:
 *
 * - the library's own sources are the emitted set, and every name in it is ruled or listed;
 * - **a diagram module emitting a `library-*` class FAILS this guard** rather than joining the
 *   inventory. The alternative — absorbing it — lets a module grow the library's contract
 *   surface without touching the library, while the reasons on the list below are written by
 *   somebody who cannot see the module;
 * - any other class a module emits is out of scope here, in both directions.
 *
 * The rule follows from the prefix, not from which folder the class is written in, so a module
 * author meets it at the moment it applies to them rather than after a review.
 */
describe("no unstyled library classes", () => {
  const libraryDir = join(__dirname);
  const cssRoot = join(__dirname, "..", "..");
  const modulesRoot = join(__dirname, "..", "..", "..", "..", "diagrams");

  /**
   * Classes painted by an inline `style` the library passes through, so a CSS rule would be a
   * second source for the same colours rather than a fix.
   *
   * `library-shape` is the module's own paint — every call site passes `style={paint}`. The
   * others are structural or text, carrying no fill of their own.
   *
   * **An entry here answers "where is this painted", and NOTHING ELSE.** It does not say the
   * class is correct, and `library-canvas-surface` is the standing proof — see the note about
   * adequacy above.
   */
  const paintedElsewhere = new Set([
    "library-shape",
    "library-frame",
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
    // The group a declared ornament is drawn in: structure, not paint. Its colour comes from
    // the module's own class, which now rides the drawn glyph rather than this wrapper.
    "library-decoration",
    // The inner ring of a `double-ellipse`, painted by the SAME inline `style={paint}` as the
    // outer one it sits inside - the `library-shape` case exactly, and listed for that reason
    // rather than because nobody got round to a rule.
    "library-shape-inner",
    // The highlight: painted by an inline style from highlight.ts, because a stylesheet rule for
    // it lost to any module rule styling its own shapes by descendant (`.mindmap-node rect`), and
    // highlightSurvivesModuleStyles.test.tsx holds it to that. The class stays as the NAME of the
    // state - what tests and modules read - rather than as a look.
    "library-connect-target",
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
    // `library-custom-` ALSO USED TO SIT HERE, and it was never a class at all. It is the leading
    // fragment of an SVG marker reference - `url(#library-custom-${kind.customMarker})` - so it was
    // never in the emitted set and its exemption never did anything. Measured rather than spotted:
    // listed, and not emitted. A specification proposing a walk for exactly this - every entry still
    // emitted - was archived for unrelated reasons, and this entry is the one thing that walk would
    // have found. That is a point in its favour and not enough to revive it: one dead line in one
    // list, against a guard that answers a question this file has since documented it cannot answer.
    // `library-connect-target` and `library-connect-forbidden` USED TO SIT HERE, and they were
    // the only two entries on this list with no reason written beside them - which is what an
    // exemption looks like when it is really an unfixed defect. Neither was painted anywhere:
    // the library computed the connect verdict every frame, put the class on, and nothing
    // showed. They are styled now, so the list is shorter and this guard covers them like any
    // other class. An entry here has to be able to say WHERE it is painted; if it cannot, it
    // belongs in the stylesheet rather than on this list.
    "library-layout-switcher",
    "library-layout-active",
  ]);

  const isClassName = /^library-[a-z-]+$/;

  /**
   * Class names as the library actually applies them, taken from the SYNTAX TREE rather than
   * from the file's text.
   *
   * ## Why a parse rather than a pattern, measured rather than argued
   *
   * Scanning raw source matched `diagram-library-adoption` inside a comment and reported it as
   * an unstyled class. Quoting the pattern fixed that case and introduced two others, and both
   * were invisible because each failure is a class quietly missing from a set:
   *
   * - **`library-connect-preview` and `library-connect-preview-invalid` were not collected at
   *   all.** They are emitted from a template literal carrying a `${...}`, and a quoted-literal
   *   pattern stops at the first delimiter inside it. Two live classes, absent from the
   *   inventory, with the guard green.
   * - **A comment can still supply a name.** `DiagramCanvas.tsx` contains a backticked
   *   `.library-element-label { text-anchor: middle }` inside a `//` comment, which a
   *   quoted-literal scan matches. Today it is masked, because that class is genuinely emitted
   *   twelve lines away; delete the emission and the guard would keep insisting it exists.
   *
   * **Comments are not nodes in a syntax tree**, which is what this parse buys — not the
   * following of attributes. Collecting only what sits inside a `className` attribute was
   * measured too, and it loses **twenty-four** of the thirty-five names, because most reach the
   * attribute through an array or a ternary. It would report a smaller, cleaner, wronger
   * inventory.
   *
   * Template heads, middles and tails are collected alongside plain string literals, and that is
   * load-bearing rather than thorough: without them `library-canvas`, `library-connect-preview`,
   * `library-connection` and `library-connection-line` all vanish.
   */
  function emittedClasses(paths: string[]): Set<string> {
    const names = new Set<string>();
    for (const path of paths) {
      const parsed = ts.createSourceFile(
        path,
        readFileSync(path, "utf8"),
        ts.ScriptTarget.Latest,
        false,
        ts.ScriptKind.TSX,
      );
      const visit = (node: ts.Node): void => {
        const literal = ts.isStringLiteralLike(node)
          || node.kind === ts.SyntaxKind.TemplateHead
          || node.kind === ts.SyntaxKind.TemplateMiddle
          || node.kind === ts.SyntaxKind.TemplateTail;
        if (literal) {
          for (const name of (node as ts.LiteralLikeNode).text.split(/\s+/)) {
            if (isClassName.test(name)) names.add(name);
          }
        }
        ts.forEachChild(node, visit);
      };
      visit(parsed);
    }
    return names;
  }

  /** Every source the library ships, minus its tests. */
  function sources(dir: string, extensions: string[]): string[] {
    const paths: string[] = [];
    for (const entry of readdirSync(dir)) {
      const path = join(dir, entry);
      if (statSync(path).isDirectory()) {
        paths.push(...sources(path, extensions));
      } else if (extensions.some((extension) => entry.endsWith(extension)) && !entry.includes(".test.")) {
        paths.push(path);
      }
    }
    return paths;
  }

  function read(dir: string, extension: string): string {
    let text = "";
    for (const path of sources(dir, [extension])) text += readFileSync(path, "utf8");
    return text;
  }

  /**
   * The stylesheets with their comments removed, so that a class named in a CSS comment does not
   * count as ruled. Nothing is ruled only by a comment today; the strip is here so the first one
   * does not pass silently.
   */
  function css(): string {
    return read(cssRoot, ".css").replace(/\/\*[\s\S]*?\*\//g, " ");
  }

  function ruledClasses(): Set<string> {
    return new Set([...css().matchAll(/\.(library-[a-z-]+)/g)].map((match) => match[1]));
  }

  /**
   * Both extractors prove they are alive before any inventory is believed, because an extractor
   * that finds nothing reports a clean inventory and a clean inventory is what a healthy tree
   * looks like.
   *
   * **Named classes rather than a count.** A floor - this test used to require more than twenty
   * emitted names - survives an extractor that has lost a whole file, and says nothing about
   * WHICH names it can still see. The four below are chosen for what each one would catch:
   *
   * - `library-anchor` is an ordinary attribute literal, so it fails if the walk stops walking;
   * - `library-element` reaches `className` through an array, so it fails if collection is
   *   narrowed to attributes;
   * - `library-canvas-surface` is second in its attribute, so it fails if only the first name in
   *   an attribute is taken - the pattern that once reported a live class as a dead rule;
   * - `library-connect-preview` comes from a template head, so it fails if template parts are
   *   dropped, which is the regression that hid it from this guard until now.
   */
  it("proves both extractors are alive", () => {
    // Act.
    const emitted = emittedClasses(sources(libraryDir, [".ts", ".tsx"]));
    const ruled = ruledClasses();

    // Assert.
    for (const name of ["library-anchor", "library-element", "library-canvas-surface", "library-connect-preview"]) {
      expect([...emitted], `the emitted extractor can no longer see ${name}`).toContain(name);
    }
    for (const name of ["library-anchor", "library-connect-preview"]) {
      expect([...ruled], `the ruled extractor can no longer see ${name}`).toContain(name);
    }
  });

  it("styles every class the library paints with, or says why not", () => {
    // Arrange.
    const emitted = emittedClasses(sources(libraryDir, [".ts", ".tsx"]));
    const styled = ruledClasses();

    // Assert.
    const unstyled = [...emitted].filter((c) => !styled.has(c) && !paintedElsewhere.has(c)).sort();
    expect(
      unstyled,
      `Emitted by the library and styled nowhere, so the browser paints them black. Give them a `
        + `rule in canvas.css, or add them to paintedElsewhere with a reason saying WHERE each is `
        + `painted. Note that being on that list says nothing about whether the class is correct - `
        + `this guard checks that a rule EXISTS, never that it is adequate: ${unstyled.join(", ")}`,
    ).toEqual([]);
  });

  /**
   * The `library-` prefix stays the library's.
   *
   * Green on arrival and expected to stay that way - it exists so that the first module author to
   * reach for the prefix meets a message rather than a reviewer.
   */
  it("keeps the library- prefix to the library", () => {
    // Act.
    const trespassing = [...emittedClasses(sources(modulesRoot, [".ts", ".tsx"]))].sort();

    // Assert.
    expect(
      trespassing,
      `Emitted by a diagram module, in the canvas library's namespace. The library-* prefix is the `
        + `library's, wherever the class is written: absorbing these into the library's inventory `
        + `would let a module grow the library's contract surface from outside it. Name them for `
        + `the module instead: ${trespassing.join(", ")}`,
    ).toEqual([]);
  });

  /**
   * The walk above is only worth its run because it reads a tree rather than text, and here is
   * the proof on live code rather than on a fixture.
   *
   * Two module canvases contain the word `library-internal` in prose, describing why a helper was
   * copied rather than imported. A raw-text scan reports that as a module emitting a class called
   * `library-internal`, and the namespace guard would fail on arrival, on a comment. The parse
   * does not see it, because a comment is not a node.
   */
  it("does not mistake prose in a module comment for an emitted class", () => {
    // Arrange: the premise - the word really is in those files, or this test proves nothing.
    const moduleText = read(modulesRoot, ".tsx");
    expect(moduleText, "no module comment mentions library-internal any more").toContain("library-internal");

    // Act.
    const emitted = emittedClasses(sources(modulesRoot, [".ts", ".tsx"]));

    // Assert.
    expect([...emitted], "a comment was read as an emitted class").not.toContain("library-internal");
  });

  /**
   * The specific rule that stops the wedge. A curve with a fill paints the region between itself
   * and its chord, so this is not a colour preference — it is the difference between a line and
   * a shape.
   */
  it("draws the connect preview as a line rather than a filled region", () => {
    // Act.
    const rule = /\.library-connect-preview\s*\{([^}]*)\}/.exec(css());

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
    const source = read(libraryDir, ".tsx");

    // Assert.
    // The premise: the press handler really is on the anchor circle itself.
    expect(source).toMatch(/className="library-anchor"[\s\S]{0,200}anchorPress/);

    const rule = /\.library-anchor\s*\{([^}]*)\}/.exec(css());
    expect(rule, "the library's own anchors have no rule").not.toBeNull();
    expect(
      rule![1],
      "pointer-events: none on .library-anchor makes the connect gesture unreachable",
    ).not.toMatch(/pointer-events:\s*none/);
  });

  /** An anchor the library draws and one a module draws are the same dot. */
  it("gives the library's anchors the same fill as the shared ones", () => {
    // Arrange.
    const stylesheets = css();

    // Act.
    const shared = /\.canvas-anchor\s*\{([^}]*)\}/.exec(stylesheets);
    const library = /\.library-anchor[^{]*\{([^}]*)\}/.exec(stylesheets);

    // Assert.
    expect(shared).not.toBeNull();
    expect(library, "the library's own anchors have no rule").not.toBeNull();
    for (const property of ["fill", "stroke"]) {
      const of = (block: string) => new RegExp(`${property}:\\s*([^;]+)`).exec(block)?.[1].trim();
      expect(of(library![1]), `${property} differs from the shared anchor`).toEqual(of(shared![1]));
    }
  });
});
