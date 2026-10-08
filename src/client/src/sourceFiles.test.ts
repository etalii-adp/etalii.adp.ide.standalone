import { describe, expect, it } from "vitest";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { basename, join, relative, resolve } from "node:path";
import ts from "typescript";
import { sourceFiles } from "./sourceFiles";

/**
 * The shared source walk, and the rule that tree-scale guards use it.
 *
 * Two guards timed out in gates on 2026-09-26 for the same reason: each walked the modules' root
 * with its own walk and entered every backend's build output. A third, `fileUrlPaths.test.ts`, was
 * walking the same 9,294 entries and had not yet crossed the line. The fix is one walk that never
 * enters build output, and a rule that a test does not write its own.
 */
describe("sourceFiles", () => {
  const src = resolve(__dirname, "..", "..");

  /**
   * **Checked on a tree built here rather than on the repository**, because a timing budget cannot
   * tell a pruned walk from an unpruned one on a quiet machine, and a fresh checkout has no `bin/`
   * or `obj/` at all - a check reading the real tree would pass on an unpruned walk wherever
   * nothing had been built.
   */
  it("never walks into build output or installed packages", () => {
    // Arrange: one real source, and one source inside each folder the walk must not enter.
    const root = mkdtempSync(join(tmpdir(), "source-walk-"));
    try {
      for (const folder of ["client", "backend/bin/Debug", "backend/obj", "node_modules/some-package", "dist"]) {
        mkdirSync(join(root, folder), { recursive: true });
        writeFileSync(join(root, folder, "source.ts"), "export {};\n");
      }
      // The premise: started INSIDE a skipped folder the walk does find its file, so an empty
      // answer below is the folder being skipped, not the walk being blind.
      expect(sourceFiles(join(root, "backend", "bin")), "the walk cannot read the fixture").toHaveLength(1);

      // Act.
      const found = sourceFiles(root);

      // Assert.
      expect(found, "the walk entered build output or installed packages").toEqual([join(root, "client", "source.ts")]);
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });

  /**
   * Test files that still walk a tree with their own recursion, each for a reason: every one of
   * them walks only the modules' `client/` folders or the client's own `src/`, a few hundred
   * entries, where no build output lives. They predate the shared walk and are listed rather than
   * converted because converting them buys no time - see the note on the rule below.
   *
   * `themeTokens.test.ts` is here because its owner is fixing its walk on its own branch; it can
   * move onto `sourceFiles` then, and leave this list.
   *
   * Keyed by file name, which is unique among the test files and checked to be.
   */
  const ownWalkAllowed = new Set([
    "diagramModuleClientApi.test.ts",
    "diagramStreamOpensOnlyInHook.test.ts",
    "everyCanvasHasOneRefusalSurface.test.tsx",
    "noModuleSelection.test.ts",
    "noModuleSendsAKeystroke.test.ts",
    "noPrivateGestures.test.ts",
    "noPrivateLabelEditors.test.ts",
    "noPrivateScrollbars.test.ts",
    "noPrivateViewReports.test.ts",
    "themeTokens.test.ts",
  ]);

  /**
   * Every test file under `src` that defines a function calling `readdirSync` and calling itself:
   * a recursive walk of its own. Parsed rather than pattern-matched, so a comment or a string
   * naming the function - this file has both - is not a walk.
   */
  function testsWithTheirOwnWalk(): string[] {
    const found: string[] = [];
    for (const path of sourceFiles(src).filter((file) => /\.test\.tsx?$/.test(file))) {
      const text = readFileSync(path, "utf8");
      if (!text.includes("readdirSync")) {
        continue;
      }

      const parsed = ts.createSourceFile(path, text, ts.ScriptTarget.Latest, false, ts.ScriptKind.TSX);
      const calls = (body: ts.Node, name: string): boolean => {
        let seen = false;
        const visit = (node: ts.Node): void => {
          if (ts.isCallExpression(node)) {
            const callee = node.expression;
            const called = ts.isIdentifier(callee) ? callee.text : ts.isPropertyAccessExpression(callee) ? callee.name.text : "";
            if (called === name) seen = true;
          }
          if (!seen) ts.forEachChild(node, visit);
        };
        visit(body);
        return seen;
      };

      let walks = false;
      const visit = (node: ts.Node): void => {
        let name: string | undefined;
        let body: ts.Node | undefined;
        if (ts.isFunctionDeclaration(node) && node.name !== undefined) {
          name = node.name.text;
          body = node.body;
        } else if (ts.isVariableDeclaration(node) && ts.isIdentifier(node.name) && node.initializer !== undefined
          && (ts.isArrowFunction(node.initializer) || ts.isFunctionExpression(node.initializer))) {
          name = node.name.text;
          body = node.initializer.body;
        }
        if (name !== undefined && body !== undefined && calls(body, "readdirSync") && calls(body, name)) {
          walks = true;
        }
        if (!walks) ts.forEachChild(node, visit);
      };
      visit(parsed);

      if (walks) found.push(path);
    }

    return found;
  }

  /**
   * **A test that walks a tree uses `sourceFiles`.** Seen red before this rule's first fix, on
   * the three files that walked the modules' root with their own recursion - `fileUrlPaths`,
   * `highlightSurvivesModuleStyles` and `noUnstyledLibraryClasses`.
   *
   * The rule is about the walk a test writes, not about how far it goes, because how far it goes
   * is decided by the argument at the call site, which a parse of the walk cannot see. Of the
   * sixteen test files that read the filesystem when this was written, three walked a tree that
   * holds build output and are converted; the rest are listed above.
   */
  it("is the only recursive walk a new test writes", () => {
    // Act.
    const walkers = testsWithTheirOwnWalk();
    const names = walkers.map((path) => basename(path));

    // Assert: the detection is alive - two listed walkers are seen, by name.
    for (const name of ["noPrivateGestures.test.ts", "diagramModuleClientApi.test.ts"]) {
      expect(names, `the walk detection can no longer see ${name}`).toContain(name);
    }
    expect(new Set(names).size, "two walking test files share a name, so the list below is ambiguous").toBe(names.length);

    const unlisted = walkers.filter((path) => !ownWalkAllowed.has(basename(path))).map((path) => relative(src, path).replace(/\\/g, "/")).sort();
    expect(
      unlisted,
      "A test walks a tree with its own recursion. Use sourceFiles from @client/sourceFiles instead: it never "
        + "enters bin, obj or node_modules, and a module's backend build output is nine in ten of the entries a "
        + `walk of the module folders would otherwise visit: ${unlisted.join(", ")}`,
    ).toEqual([]);
    // Room, not a budget: this takes 150 ms alone and passed three seconds on Windows when the
    // whole backend suite ran beside it. What it guards is the list above, not how long it took.
  }, 30_000);
});
