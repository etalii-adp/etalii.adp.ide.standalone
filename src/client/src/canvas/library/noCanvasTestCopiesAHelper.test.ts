import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { sourceFiles } from "@client/sourceFiles";

/**
 * No canvas test declares its own copy of a shared helper (client-centralization Requirement 10.5).
 *
 * The pointer-capture stubs were copied into 26 canvas tests, the pointer-event factory into 23, the
 * `pushedIds` adapter into 18 and a partial fake of the context connection into 30. The stubs now
 * live in `test-setup.ts`; the other three in `canvas/library/testing/canvasHarness.ts`. This fails
 * on a canvas test that writes one of them again, naming the file and the helper it should import.
 *
 * <b>What counts as a copy</b> is the helper's shape, not its name, where the shape can be read:
 * the factory is a `MouseEvent` built from a `type` parameter, the adapter is the `null`-or-element-id
 * mapping over `selectedElementIdOf`, and the fake is an object literal returned from
 * `useContextConnection` - so a copy renamed `press` or `ptr` is still found. A test that stubs the
 * capture with plain `=` to watch it, as `AnsibleCanvas.test.tsx` does for one test, is not a copy:
 * only the `??=` stub is.
 *
 * <b>`renderCanvas` is not policed</b>: its props differ per module, so each is that module's own
 * harness rather than a copy (Requirement 10.4).
 *
 * <b>Its limits, stated plainly.</b> It reads text. A from-scratch helper written in a different shape
 * - a pointer event built from a literal type in a loop, say - is not recognised. The positive control
 * proves each pattern still matches the one place allowed to hold it, so a pattern gone blind fails
 * here rather than reporting a clean tree.
 */

interface SharedHelper {
  name: string;
  /** Where a test gets it instead. */
  instead: string;
  pattern: RegExp;
  /** A file, relative to src/, that legitimately holds the shape - the positive control. */
  control: string;
}

const HARNESS = "client/src/canvas/library/testing/canvasHarness.ts";

const SHARED_HELPERS: readonly SharedHelper[] = [
  {
    name: "the pointer-capture stubs",
    instead: "test-setup.ts, which installs them for every test",
    pattern: /PointerCapture\s*\?\?=/,
    control: "client/src/test-setup.ts",
  },
  {
    name: "the pointer-event factory",
    instead: "pointer, from canvasHarness",
    pattern: /new\s+MouseEvent\(\s*type\b/,
    control: HARNESS,
  },
  {
    name: "the pushedIds adapter",
    instead: "idsPushed, from canvasHarness",
    pattern: /\?\s*null\s*:\s*\(?\s*selectedElementIdOf\(/,
    control: HARNESS,
  },
  {
    name: "a fake context connection",
    instead: "fakeContextConnection, from canvasHarness",
    pattern: /useContextConnection\s*:\s*\([^)]*\)\s*=>\s*(\(\s*)?\{/,
    // A shell test, outside the canvas tests this guard walks, still fakes its own.
    control: "client/src/shell/ribbon/RibbonBar.test.tsx",
  },
];

/** This file names every pattern it polices, so it does not walk itself. */
const THIS_GUARD = "client/src/canvas/library/noCanvasTestCopiesAHelper.test.ts";

/**
 * A source with its comments blanked. A comment explaining that a test no longer builds its own
 * pointer event is prose, not a copy - `noModuleSendsAKeystroke.test.ts`'s reasoning, same shape.
 */
function withoutComments(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, " ").replace(/(^|[^:])\/\/[^\n]*/g, "$1 ");
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

/** Every test file under a folder, relative to src/ with forward slashes. */
function testsUnder(root: string, folder: string): string[] {
  if (statSync(join(root, folder), { throwIfNoEntry: false })?.isDirectory() !== true) {
    return [];
  }
  return sourceFiles(join(root, folder))
    .filter((path) => /\.test\.tsx?$/.test(path))
    .map((path) => relative(root, path).replaceAll("\\", "/"));
}

/** The canvas tests: the library's own, and every diagram module's client tests. */
function canvasTests(root: string): string[] {
  const modules = readdirSync(join(root, "diagrams"), { withFileTypes: true }).filter((entry) => entry.isDirectory());
  return [
    ...testsUnder(root, "client/src/canvas"),
    ...modules.flatMap((module) => testsUnder(root, `diagrams/${module.name}/client`)),
  ].filter((file) => file !== THIS_GUARD);
}

describe("no canvas test copies a shared helper", () => {
  it("recognises the one place each helper may live - every pattern still matches what it polices", () => {
    // Arrange.
    const root = sourceRoot();

    // Act: read exactly as the check below reads a test, comments blanked.
    const blind = SHARED_HELPERS.filter((helper) => !helper.pattern.test(withoutComments(readFileSync(join(root, helper.control), "utf8"))));

    // Assert: the control. Without it, an offender list of [] could mean a pattern gone blind.
    expect(blind.map((helper) => `${helper.name} (${helper.control})`)).toEqual([]);
  });

  it("walks the canvas tests that used to carry their own copies", () => {
    // Arrange.
    const root = sourceRoot();

    // Act.
    const walked = canvasTests(root);

    // Assert: the completeness canary - former copy holders from the library and from modules.
    for (const file of [
      "client/src/canvas/library/DiagramCanvas.test.tsx",
      "client/src/canvas/gesture/usePointerGesture.test.tsx",
      "diagrams/c4/client/C4Canvas.test.tsx",
      "diagrams/databricks/client/JobCanvas.test.tsx",
      "diagrams/mindmap/client/MindmapCanvas.test.tsx",
      "diagrams/mindmap/client/useMindmapStream.test.ts",
      "diagrams/rdf/client/RdfCanvas.test.tsx",
    ]) {
      expect(walked, file).toContain(file);
    }
  });

  it("finds no canvas test declaring its own copy of a shared helper", () => {
    // Arrange.
    const root = sourceRoot();

    // Act.
    const offenders = canvasTests(root).flatMap((file) => {
      const source = withoutComments(readFileSync(join(root, file), "utf8"));
      return SHARED_HELPERS.filter((helper) => helper.pattern.test(source)).map((helper) => `${file}: ${helper.name} - use ${helper.instead}`);
    });

    // Assert: every offender named at once, with what to use instead.
    expect(offenders).toEqual([]);
  });
});
