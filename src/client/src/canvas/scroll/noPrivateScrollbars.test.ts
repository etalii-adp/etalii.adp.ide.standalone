import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * No diagram module may build its own scrollbar. There is one implementation, in this folder,
 * and importing it from `@client/canvas/scroll` is the only permitted way for a canvas to have
 * bars.
 *
 * The rule is not a preference, and nothing else enforces it. The drag-and-drop survey measured
 * what a convention without a guard costs: the same pan and drag state hand-rolled across ten
 * canvases, each copied from whichever came before. The scrollbars were five copies away from
 * that same outcome when this specification started. The decay is invisible in review, because
 * each individual copy looks reasonable on its own.
 *
 * It follows the shape of the repository's other convention guards (`DependencyInventoryTests`,
 * `DocumentationLinksTests`, `WorkspaceLockTests`): walk the tree, collect every offender, fail
 * once naming all of them, rather than stopping at the first.
 *
 * Its limit, stated plainly: it reads text, so a module that hand-builds a bar and calls it
 * nothing resembling a scrollbar goes unseen. It catches the copy, which is how this actually
 * happens - somebody starts from a canvas that already has bars - and not the from-scratch
 * reinvention under a private name.
 */

/** Anything that reads as a scrollbar of one's own. */
const SCROLLBAR = /scroll(bar|-thumb|-track)/i;

/** The one permitted source. */
const SHARED_IMPORT = /from\s+["']@client\/canvas\/scroll\//;

/**
 * The shared component's own internal class names. A module has no reason to write these: it
 * passes a `className` for the bar and the component builds the thumb and track itself. Checked
 * separately from the import, because importing `scrollGeometry` for the axes and then
 * hand-rolling the markup would otherwise read as compliance.
 */
const SHARED_INTERNALS = /className=\s*["'`{][^"'`]*scroll(bar-thumb|-thumb|-track)/;

/**
 * The one thing a module stylesheet may say about the shared bars: where to put them. The
 * component takes a `className` for exactly this, so a compound of the module's own class with
 * a shared one is placement, not a second implementation. `.canvas-scrollbar-vertical { … }`
 * on its own, or any `.something-scroll-thumb`, is a module redefining the bars.
 */
const PLACEMENT_SELECTOR = /^\.[\w-]+\.canvas-scrollbar-(horizontal|vertical)$/;

/**
 * The repository's `src` folder, found by walking up from this file rather than from the cwd.
 *
 * It is recognised by holding both `diagrams/` and the tree's single `.editorconfig`, because
 * `diagrams` alone is not unique: `src/client/src/diagrams` sits between this file and the real
 * root, and a walk that stops there finds no modules and reports a clean tree for ever. The
 * canary below caught exactly that on this guard's first run.
 */
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

/** Every source file under each module's `client/` folder, tests excluded. */
function moduleClientFiles(): string[] {
  const diagrams = join(sourceRoot(), "diagrams");
  const files: string[] = [];

  const walk = (directory: string) => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (entry.isDirectory()) {
        if (entry.name !== "node_modules") {
          walk(path);
        }
      } else if (/\.(ts|tsx|css)$/.test(entry.name) && !/\.test\.tsx?$/.test(entry.name)) {
        files.push(path);
      }
    }
  };

  for (const module of readdirSync(diagrams, { withFileTypes: true })) {
    const client = join(diagrams, module.name, "client");
    if (module.isDirectory() && statSync(client, { throwIfNoEntry: false })?.isDirectory() === true) {
      walk(client);
    }
  }

  return files;
}

/**
 * A stylesheet's selectors, one per returned entry, taken from every line that opens or
 * continues a selector list. Crude by intention: it needs to find class names, not parse CSS.
 */
function selectorsIn(css: string): string[] {
  return css
    .split(/\r?\n/)
    .filter((line) => /[,{]\s*$/.test(line) && !/^\s*\*/.test(line))
    .flatMap((line) => line.replace(/[{,]\s*$/, "").split(","))
    .map((selector) => selector.trim())
    .filter((selector) => selector.length > 0);
}

/** What is wrong with this file, or null when it is behaving. */
function offenceIn(path: string, content: string): string | null {
  if (path.endsWith(".css")) {
    const own = selectorsIn(content).filter((selector) => SCROLLBAR.test(selector) && !PLACEMENT_SELECTOR.test(selector));
    return own.length > 0 ? `defines scrollbar styling of its own (${own.join(", ")})` : null;
  }
  // A component may name the bars all it likes as long as they are the shared ones.
  if (SHARED_INTERNALS.test(content)) {
    return "builds a scrollbar's thumb or track itself";
  }
  if (SCROLLBAR.test(content) && !SHARED_IMPORT.test(content)) {
    return "names a scrollbar without importing the shared one";
  }
  return null;
}

describe("no private scrollbars", () => {
  it("finds every scrollbar in a diagram module coming from the shared component", () => {
    // Arrange.
    const root = sourceRoot();
    const offenders: string[] = [];

    // Act.
    for (const file of moduleClientFiles()) {
      const offence = offenceIn(file, readFileSync(file, "utf8"));
      if (offence !== null) {
        offenders.push(`${relative(root, file).split(sep).join("/")} - ${offence}`);
      }
    }

    // Assert.
    expect(
      offenders,
      "A diagram module must import the shared scrollbars from @client/canvas/scroll and may " +
        `only position them; it may not build its own. Offenders:\n  ${offenders.join("\n  ")}`,
    ).toEqual([]);
  });

  it("still sees the module client files, so a passing result means something", () => {
    // Arrange, act and assert.
    // A guard that silently walks nothing passes for ever. This pins that it is looking.
    expect(moduleClientFiles().length).toBeGreaterThan(20);
  });

  it("calls a hand-built bar an offence, wherever it is written", () => {
    // Arrange.
    const placement = ".databricks-scrollbars.canvas-scrollbar-horizontal {\n  left: 56px;\n}\n";
    const ownStyling = ".my-scroll-thumb {\n  background: red;\n}\n";
    const redefinition = ".canvas-scrollbar-vertical {\n  width: 20px;\n}\n";
    const shared = 'import { CanvasScrollbars } from "@client/canvas/scroll/CanvasScrollbars";\n<CanvasScrollbars />';
    const handRolled = 'const Bars = () => <div className="scroll-track"><div className="scroll-thumb" /></div>;';
    // The one that slipped through first: hand-rolled markup in a file that does import the
    // shared geometry helper, so the import alone says nothing about where the bars came from.
    const importedButHandRolled = [
      'import { scrollExtentOf } from "@client/canvas/scroll/scrollGeometry";',
      'const Bars = () => <div className="canvas-scrollbar-thumb" />;',
    ].join("\n");

    // Act and assert.
    // This is the test that would have to be deleted, not merely edited, to sneak a bar past.
    expect(offenceIn("a.css", placement)).toBeNull();
    expect(offenceIn("a.css", ownStyling)).not.toBeNull();
    expect(offenceIn("a.css", redefinition)).not.toBeNull();
    expect(offenceIn("a.tsx", shared)).toBeNull();
    expect(offenceIn("a.tsx", handRolled)).not.toBeNull();
    expect(offenceIn("a.tsx", importedButHandRolled)).not.toBeNull();
  });
});
