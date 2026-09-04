import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * No diagram module may build its own in-place editor. There is one implementation, in this
 * folder, and importing it from `@client/canvas/label` is the only permitted way for a canvas
 * to have editing on the surface.
 *
 * The rule is not a preference, and nothing else enforces it. The drag-and-drop survey measured
 * what a convention without a guard costs: the same pan and drag state hand-rolled across ten
 * canvases, each copied from whichever came before. An editable field is a smaller thing to
 * copy and therefore an easier one - and the four rules the shared editor keeps (blur commits,
 * a refusal stays open, an unchanged value dispatches nothing, focus goes back to the canvas)
 * are exactly the kind that a copy quietly drops.
 *
 * It follows the shape of this repository's other convention guards - `noPrivateScrollbars`,
 * `DependencyInventoryTests`, `DocumentationLinksTests` - walking the tree, collecting every
 * offender, and failing once naming all of them rather than stopping at the first.
 *
 * **Its limit, stated plainly**: it reads text. A module that hand-builds an editor out of
 * something that does not read as a text field goes unseen. It catches the copy, which is how
 * this actually happens - somebody starts from a canvas that already edits in place - and not
 * the from-scratch reinvention under a private name.
 */

/** Anything that reads as a text field of one's own. */
const TEXT_FIELD = /<input\b|<textarea\b|contentEditable|contenteditable/;

/**
 * The shared component's own class names. A module has no reason to write these: it passes a
 * placement and the component builds its own markup. Checked separately from the import,
 * because importing the editor and then hand-rolling a second field beside it would otherwise
 * read as compliance - the mistake `noPrivateScrollbars` had to be corrected for.
 */
const SHARED_INTERNALS = /inline-label-editor(-body|-field|-error)/;

/**
 * The repository's `src` folder, found by walking up from this file rather than from the cwd.
 *
 * Recognised by holding both `diagrams/` and the tree's single `.editorconfig`, because
 * `diagrams` alone is not unique: `src/client/src/diagrams` sits between this file and the real
 * root, and a walk that stops there finds no modules and reports a clean tree for ever.
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

/** What is wrong with this file, or null when it is behaving. */
function offenceIn(path: string, content: string): string | null {
  if (path.endsWith(".css")) {
    return SHARED_INTERNALS.test(content) ? "restyles the shared inline editor's own internals" : null;
  }
  if (SHARED_INTERNALS.test(content)) {
    return "builds the inline editor's markup itself";
  }
  // No escape for a file that also imports the shared editor. Importing it and hand-rolling a
  // second field beside it is exactly the shape that slipped past the scrollbar guard on its
  // first version, and a module has no need of a raw text field either way: the shared editor
  // renders its own, and no module client in the tree has one. A module that turns out to need
  // a field for something else should raise it rather than route around this.
  if (TEXT_FIELD.test(content)) {
    return "renders a text field of its own instead of the shared inline editor";
  }
  return null;
}

describe("no private label editors", () => {
  it("finds every editable field in a diagram module coming from the shared editor", () => {
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
      "A diagram module must render in-place editing through the shared editor from " +
        `@client/canvas/label; it may not build its own. Offenders:\n  ${offenders.join("\n  ")}`,
    ).toEqual([]);
  });

  it("still sees the module client files, so a passing result means something", () => {
    // Arrange, act and assert.
    // A guard that silently walks nothing passes for ever. This pins that it is looking.
    expect(moduleClientFiles().length).toBeGreaterThan(20);
  });

  it("calls a hand-built field an offence, wherever it is written", () => {
    // Arrange.
    const shared = [
      'import { InlineLabelEditor } from "@client/canvas/label/InlineLabelEditor";',
      "<InlineLabelEditor placement={placement} />",
    ].join("\n");
    const handRolled = '<foreignObject><input className="my-node-editor" /></foreignObject>';
    const contentEditable = '<text contentEditable suppressContentEditableWarning>{label}</text>';
    // The one the scrollbar guard had to be corrected for: importing the shared thing and then
    // building a second field anyway. Here the import buys nothing at all, which is why the
    // rule above has no import escape.
    const importedButHandRolled = [
      'import { InlineLabelEditor } from "@client/canvas/label/InlineLabelEditor";',
      '<div className="inline-label-editor-body"><input /></div>',
    ].join("\n");
    const restyling = ".inline-label-editor-field {\n  border: none;\n}\n";
    const ordinaryStyling = ".mindmap-node rect {\n  stroke: black;\n}\n";

    // Act and assert.
    // This is the test that would have to be deleted, not merely edited, to sneak an editor past.
    expect(offenceIn("a.tsx", shared)).toBeNull();
    expect(offenceIn("a.tsx", handRolled)).not.toBeNull();
    expect(offenceIn("a.tsx", contentEditable)).not.toBeNull();
    expect(offenceIn("a.tsx", importedButHandRolled)).not.toBeNull();
    expect(offenceIn("a.css", restyling)).not.toBeNull();
    expect(offenceIn("a.css", ordinaryStyling)).toBeNull();
  });
});
