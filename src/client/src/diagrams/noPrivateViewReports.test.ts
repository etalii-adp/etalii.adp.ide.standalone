import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * No diagram module may build its own view report. There is one implementation, in this folder,
 * and reaching it through `@client/diagrams/viewReport` and `@client/diagrams/useViewReport` is
 * the only permitted way for a canvas to tell the backend what it can see.
 *
 * The rule is not a preference, and nothing else enforces it. This specification began with the
 * report hand-rolled in four modules: the `reportView` bodies differed in a single comment,
 * `shownRectOf` was written three times - two of them character-identical - and the `Viewport`
 * interface was declared four times over. Nobody copies a five-line interface deliberately; they
 * copy the file it lives in, and each copy looks reasonable on its own.
 *
 * It follows the shape of the repository's other convention guards (`DependencyInventoryTests`,
 * `DocumentationLinksTests`, `WorkspaceLockTests`, and `noPrivateScrollbars.test.ts` next door):
 * walk the tree, collect every offender, fail once naming all of them rather than stopping at
 * the first.
 *
 * **Why this does not test for the import.** Every adopting module legitimately imports from
 * this folder, so "imports the shared module" is true both of a module that behaves and of one
 * that imports it and hand-rolls a request anyway. The guard tests for the offending construct
 * itself. That trap was live in the scrollbar guard next door, whose first draft asked whether a
 * file mentioned a scrollbar without importing the shared one - satisfied by any file importing
 * that module for an unrelated reason, so a hand-rolled thumb sitting beside a legitimate
 * geometry import passed a green run.
 *
 * Its limit, stated plainly: it reads text. A module that assembles the request through a
 * variable it named something else goes unseen. It catches the copy, which is how this actually
 * happens, and not the from-scratch reinvention under a private name.
 */

/**
 * Calling the paired `UpdateView` leg directly. `viewReportOf` is the one place that builds
 * this request, so any other call site is a module correlating a watch id and a rectangle by
 * hand - the whole of what Requirement 2.1 calls the mechanism.
 */
const OWN_REQUEST = /\.updateView\s*\(/;

/**
 * Declaring one of the shared names again, rather than importing it.
 *
 * Each alternative is anchored on the token that *completes* a declaration - the `=` of a type
 * alias, the `{` of an interface - rather than on the keyword alone. That is what separates an
 * offence from ordinary use, and the distinction is finer than it looks: `type Viewport` on its
 * own also occurs inside `import { viewReportOf, type Viewport }`, the inline type import every
 * adopting module writes, so a keyword-only pattern indicts the whole compliant tree. The first
 * draft of this guard did exactly that and named six innocent files, which is how the fixture
 * below came to have an entry for it.
 *
 * The other legitimate uses this must stay silent about: `export type { Viewport };` re-exports
 * the shared type, `reportView: (viewport: Viewport) => void` refers to it, and
 * `convert: () => shownRectOf(...)` calls it.
 */
const OWN_DECLARATION = [
  /\binterface\s+Viewport\s*[{<]/,
  /\btype\s+Viewport\s*[=<]/,
  /\benum\s+Viewport\s*\{/,
  /\b(?:function|const|let|var)\s+shownRectOf\b/,
  /\b(?:const|let|var)\s+VIEW_REPORT_DEBOUNCE_MS\s*=/,
];

/**
 * The repository's `src` folder, found by walking up from this file rather than from the cwd.
 *
 * Recognised by holding both `diagrams/` and the tree's single `.editorconfig`, because
 * `diagrams` alone is not unique - this very file sits inside `src/client/src/diagrams`, so a
 * walk anchored on the folder name stops one directory up, finds no modules and reports a clean
 * tree for ever, which is indistinguishable from good news. The scrollbar guard's canary caught
 * exactly that on its first run, which is why the canary below exists too.
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

/** Every `.ts`/`.tsx` file below `directory`, tests and `node_modules` excluded. */
function sourceFilesUnder(directory: string): string[] {
  const files: string[] = [];

  const walk = (current: string) => {
    for (const entry of readdirSync(current, { withFileTypes: true })) {
      const path = join(current, entry.name);
      if (entry.isDirectory()) {
        if (entry.name !== "node_modules") {
          walk(path);
        }
      } else if (/\.tsx?$/.test(entry.name) && !/\.test\.tsx?$/.test(entry.name)) {
        files.push(path);
      }
    }
  };

  walk(directory);
  return files;
}

/**
 * Everything the rule governs: every diagram module's client, and the shell besides - the shell
 * has canvases of its own reach and no more licence to hand-roll a report than a module does.
 *
 * Two exemptions, both by construction rather than by name. `src/client/src/diagrams` is the
 * shared implementation, which is allowed to be the one that calls `updateView` and declares
 * the shared names. `src/client/src/generated` is machine-written from the proto - it is the
 * API being called, not a caller, and rewriting it to satisfy a guard would be meaningless.
 */
function governedFiles(): string[] {
  const root = sourceRoot();
  const diagrams = join(root, "diagrams");
  const shared = join(root, "client", "src", "diagrams");
  const generated = join(root, "client", "src", "generated");

  const files = sourceFilesUnder(join(root, "client", "src")).filter(
    (path) => !path.startsWith(shared + sep) && !path.startsWith(generated + sep),
  );

  for (const module of readdirSync(diagrams, { withFileTypes: true })) {
    const client = join(diagrams, module.name, "client");
    if (module.isDirectory() && statSync(client, { throwIfNoEntry: false })?.isDirectory() === true) {
      files.push(...sourceFilesUnder(client));
    }
  }

  return files;
}

/** What is wrong with this file, or null when it is behaving. */
export function offenceIn(content: string): string | null {
  if (OWN_REQUEST.test(content)) {
    return "builds its own UpdateView request instead of using viewReportOf";
  }

  const redeclared = OWN_DECLARATION.filter((pattern) => pattern.test(content));
  if (redeclared.length > 0) {
    return "declares a shared view-report name of its own rather than importing it";
  }

  return null;
}

describe("no private view reports", () => {
  it("finds every view report coming from the shared implementation", () => {
    // Arrange.
    const root = sourceRoot();
    const offenders: string[] = [];

    // Act.
    for (const file of governedFiles()) {
      const offence = offenceIn(readFileSync(file, "utf8"));
      if (offence !== null) {
        offenders.push(`${relative(root, file).split(sep).join("/")} - ${offence}`);
      }
    }

    // Assert.
    expect(
      offenders,
      "A module reports its view through viewReportOf and useViewReport from @client/diagrams; " +
        "it supplies its own rectangle and its own visibility decision and nothing else. " +
        `Offenders:\n  ${offenders.join("\n  ")}`,
    ).toEqual([]);
  });

  it("still sees the governed files, so a passing result means something", () => {
    // A guard that silently walks nothing passes for ever. This pins that it is looking, and
    // that it is looking at both halves of the tree rather than only the one it found first.
    const files = governedFiles();
    const root = sourceRoot();

    expect(files.length).toBeGreaterThan(20);
    expect(files.some((path) => path.startsWith(join(root, "diagrams") + sep))).toBe(true);
    expect(files.some((path) => path.startsWith(join(root, "client") + sep))).toBe(true);
  });

  it("tells a hand-rolled report from ordinary use of the shared one", () => {
    // Arrange: what every adopting module legitimately writes.
    const importsAndUses = [
      'import { viewReportOf, type Viewport } from "@client/diagrams/viewReport";',
      "export type { Viewport };",
      "  reportView: (viewport: Viewport) => void;",
      "  const reportView = viewReportOf(client, projectId, watchId, path);",
      "  convert: () => shownRectOf(viewRef.current, surfaceRef.current),",
    ].join("\n");

    // ...and the three ways a module stops using it.
    const ownRequest = "void client.updateView({ projectId: { value: projectId } });";
    const throughARef = "void clientRef.current.updateView({ view: { center } });";
    const ownType = "interface Viewport {\n  minX: number;\n}";
    const ownAlias = "type Viewport = { minX: number };";
    const ownFunction = "function shownRectOf(box: ViewBox): Viewport {\n  return box;\n}";
    const ownDebounce = "const VIEW_REPORT_DEBOUNCE_MS = 200;";

    // The one that matters most: a file that imports the shared module for a real reason and
    // hand-rolls the request anyway. An import-presence check calls this compliant, which is
    // the exact failure the scrollbar guard shipped with first.
    const importedButHandRolled = [
      'import { shownRectOf } from "@client/diagrams/viewReport";',
      "void client.updateView({ view: { boundingBox: shownRectOf(box) } });",
    ].join("\n");

    // Act and assert. This is the test that would have to be deleted, not merely edited, to
    // sneak a private report past.
    expect(offenceIn(importsAndUses)).toBeNull();
    // Each line of the compliant fixture on its own, so a future tightening of the patterns
    // cannot pass by accident on some other line of it. The inline type import is the one that
    // caught this guard out.
    for (const line of importsAndUses.split("\n")) {
      expect(offenceIn(line), line).toBeNull();
    }

    expect(offenceIn(ownRequest)).not.toBeNull();
    expect(offenceIn(throughARef)).not.toBeNull();
    expect(offenceIn(ownType)).not.toBeNull();
    expect(offenceIn(ownAlias)).not.toBeNull();
    expect(offenceIn(ownFunction)).not.toBeNull();
    expect(offenceIn(ownDebounce)).not.toBeNull();
    expect(offenceIn(importedButHandRolled)).not.toBeNull();
  });
});
