import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import ts from "typescript";

/**
 * The module-facing client API, computed rather than listed.
 *
 * Two sets, both parsed with the TypeScript compiler's `createSourceFile` and never by pattern
 * match, because a regular expression over source text cannot tell an import from the word
 * "import" in a comment, and the readme this feeds is checked against the answer.
 *
 * **Set A is what modules import** - every name a module's client reaches for from an
 * `@client/...` specifier. **Set B is what modules supply by value** - the by-value closure from
 * the four contracts a module hands the canvas. A name in neither is library-internal, which is
 * the third thing the readme states and the only one that cannot be computed from a single set.
 *
 * **No type checker.** `createSourceFile` parses one file at a time with no program, no module
 * resolution and no `node_modules`, so this runs in milliseconds and cannot be broken by a
 * half-installed tree. The cost is that the walk is syntactic: it follows names, not symbols, so
 * two different types with one name are one node here. That is a deliberate trade and the reason
 * the canaries below assert named members rather than only counts.
 */

/** The shared wire contracts. Everything else under `generated/` is a module's own payload. */
const CORE_CONTRACTS = new Set([
  "deltas_pb",
  "elements_pb",
  "context_pb",
  "context-contract_pb",
  "diagrams_pb",
  "problems_pb",
]);

/** The one library folder whose helpers a module's TEST files may import (the 2026-09-12 amendment). */
const TEST_ONLY_PREFIX = "@client/canvas/library/testing/";

/**
 * The four roots of Set B: what a module hands the canvas by value, each QUALIFIED BY THE FILE
 * that declares it.
 *
 * **A bare name is not enough, and one of these proved it.** `DiagramCanvasProps` was declared
 * twice - in the shell's registration file as what a canvas COMPONENT receives (since spec 002's
 * naming alignment `ToolContentProps` in `shell/panels/toolPanelRegistration.ts`), and in
 * `canvas/library/DiagramCanvas.tsx` as what the LIBRARY canvas takes. Both were module-facing and
 * neither was wrong. Walking by bare name picked whichever file sorted first, which happened to be
 * the right one and would have silently become the wrong one after a folder rename.
 *
 * Set A never had this problem: one of its entries is a name paired with a SPECIFIER, so the two
 * are already distinct there. The asymmetry is the defect, not the duplicate name - so the roots
 * carry their file and a rename makes a root stop resolving, which is loud.
 */
export const SET_B_ROOTS: readonly SetBRoot[] = [
  { name: "DiagramDefinition", file: "client/src/canvas/library/definition/diagramDefinition.ts" },
  { name: "DiagramCanvasProps", file: "client/src/canvas/library/DiagramCanvas.tsx" },
  { name: "DiagramEventHandlers", file: "client/src/canvas/library/api/diagramEvents.ts" },
  { name: "DiagramModel", file: "client/src/canvas/library/api/diagramModel.ts" },
];

/** A Set B root: the declaration's name, and the repo-relative file that must declare it. */
export interface SetBRoot {
  name: string;
  file: string;
}

/** Where a name came from, so a failure can name the module rather than only the name. */
export interface NameOrigin {
  name: string;
  /** Repo-relative files that import it, for Set A; declaring file for Set B. */
  files: string[];
}

export interface ModuleClientApiSurface {
  /** Names modules import from `@client/...`, with the module files that import them. */
  setA: Map<string, string[]>;
  /** Names reachable by value from the four roots, with the surface file declaring each. */
  setB: Map<string, string>;
  /** Every file the surface was computed from, repo-relative, in a stable order. */
  surfaceFiles: string[];
  /** Module client files read for Set A, repo-relative. */
  moduleFiles: string[];
  /** Exported declarations of the surface files, name to declaring file - Set B's candidates. */
  exportsBySurfaceFile: Map<string, string>;
  /** Each exported interface's own members, for the field-coverage check. */
  interfaceMembers: Map<string, string[]>;
  /**
   * The third category: types satisfied by a file EXISTING rather than by any reference.
   *
   * A type describing a module file's own export shape cannot appear in Set A, because the file it
   * describes does not import it - conformance is checked by assignment at the `import.meta.glob`
   * boundary. Enumerated from the globs themselves, so it moves with the code: today it is one
   * member, and the second one joins it without anybody editing a list.
   */
  conformanceOnly: string[];
}

/** The repository's `src` folder, found by walking up - the family's shared idiom. */
export function sourceRoot(): string {
  let directory = dirname(fileURLToPath(import.meta.url));
  for (let depth = 0; depth < 12; depth++) {
    const hasModules = statSync(join(directory, "diagrams"), { throwIfNoEntry: false })?.isDirectory() === true;
    const hasStyleRules = statSync(join(directory, ".editorconfig"), { throwIfNoEntry: false })?.isFile() === true;
    if (hasModules && hasStyleRules) {
      return directory;
    }

    directory = dirname(directory);
  }

  throw new Error("The src folder (diagrams beside .editorconfig) was not found above this test.");
}

function readSource(absolute: string, root: string): ts.SourceFile {
  let text: string;
  try {
    text = readFileSync(absolute, "utf8");
  } catch (error) {
    // Loudly, per the task: a surface file that cannot be read makes every set below it wrong,
    // and a set that is quietly short is exactly what this computation exists to prevent.
    throw new Error(`The surface file '${relative(root, absolute)}' could not be read: ${String(error)}`);
  }

  return ts.createSourceFile(absolute, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
}

function filesUnder(directory: string, accept: (path: string) => boolean): string[] {
  const found: string[] = [];
  const walk = (at: string): void => {
    for (const entry of readdirSync(at, { withFileTypes: true })) {
      const path = join(at, entry.name);
      if (entry.isDirectory()) {
        if (entry.name !== "node_modules") {
          walk(path);
        }
      } else if (accept(path)) {
        found.push(path);
      }
    }
  };

  if (statSync(directory, { throwIfNoEntry: false })?.isDirectory() === true) {
    walk(directory);
  }

  return found.sort();
}

const isTestFile = (path: string): boolean => /\.test\.tsx?$/.test(path);

/** Every `import.meta.glob<T>` type argument in the client - the conformance-only category. */
function conformanceOnlyNames(root: string): string[] {
  const found = new Set<string>();
  for (const file of filesUnder(join(root, "client", "src"), (path) => isSource(path) && !isTestFile(path))) {
    const source = readSource(file, root);
    const visit = (node: ts.Node): void => {
      if (ts.isCallExpression(node) && node.typeArguments !== undefined && node.typeArguments.length > 0 &&
          node.expression.getText().endsWith("import.meta.glob")) {
        for (const argument of node.typeArguments) {
          if (ts.isTypeReferenceNode(argument)) {
            found.add(argument.typeName.getText());
          }
        }
      }

      node.forEachChild(visit);
    };

    visit(source);
  }

  return [...found].sort();
}
const isSource = (path: string): boolean => /\.tsx?$/.test(path) && !path.endsWith(".d.ts");

/** `@client/canvas/library/api/diagramEvents` becomes the absolute path of that file, or undefined. */
function resolveClientSpecifier(specifier: string, root: string): string | undefined {
  if (!specifier.startsWith("@client/")) {
    return undefined;
  }

  const withoutAlias = specifier.slice("@client/".length);
  for (const candidate of [`${withoutAlias}.ts`, `${withoutAlias}.tsx`, join(withoutAlias, "index.ts")]) {
    const absolute = join(root, "client", "src", candidate);
    if (statSync(absolute, { throwIfNoEntry: false })?.isFile() === true) {
      return absolute;
    }
  }

  return undefined;
}

/** A module's own generated payload - excluded, while the shared contracts stay. */
function isModuleOwnPayload(specifier: string): boolean {
  const match = /^@client\/generated\/(.+)$/.exec(specifier);
  return match !== null && !CORE_CONTRACTS.has(match[1]);
}

/** Every name an import statement binds, default and namespace forms included. */
function importedNames(declaration: ts.ImportDeclaration): string[] {
  const clause = declaration.importClause;
  if (clause === undefined) {
    return [];
  }

  const names: string[] = [];
  if (clause.name !== undefined) {
    names.push(clause.name.text);
  }

  if (clause.namedBindings !== undefined) {
    if (ts.isNamedImports(clause.namedBindings)) {
      for (const element of clause.namedBindings.elements) {
        // `import { a as b }` - `a` is the library's name and the one the readme documents.
        names.push((element.propertyName ?? element.name).text);
      }
    } else {
      names.push(clause.namedBindings.name.text);
    }
  }

  return names;
}

/** Set A: what every module client imports from the shell surface. */
function computeSetA(root: string): { setA: Map<string, string[]>; moduleFiles: string[]; importedFrom: Set<string> } {
  const setA = new Map<string, string[]>();
  const importedFrom = new Set<string>();
  const moduleFiles: string[] = [];

  const clientFolders = [join(root, "diagrams"), join(root, "editors")]
    .flatMap((family) =>
      (statSync(family, { throwIfNoEntry: false })?.isDirectory() === true ? readdirSync(family) : [])
        .map((name) => join(family, name, "client")),
    );

  for (const folder of clientFolders) {
    for (const file of filesUnder(folder, isSource)) {
      const repoRelative = relative(root, file).replace(/\\/g, "/");
      moduleFiles.push(repoRelative);
      const source = readSource(file, root);
      const testFile = isTestFile(file);

      for (const statement of source.statements) {
        if (!ts.isImportDeclaration(statement) || !ts.isStringLiteral(statement.moduleSpecifier)) {
          continue;
        }

        const specifier = statement.moduleSpecifier.text;
        if (!specifier.startsWith("@client/") || isModuleOwnPayload(specifier)) {
          continue;
        }

        // A test file counts for exactly one folder: the library's shared testing helpers. Its
        // other imports are the module testing itself, which is not the module-facing API.
        if (testFile && !specifier.startsWith(TEST_ONLY_PREFIX)) {
          continue;
        }

        const resolved = resolveClientSpecifier(specifier, root);
        if (resolved !== undefined) {
          importedFrom.add(resolved);
        }

        for (const name of importedNames(statement)) {
          setA.set(name, [...(setA.get(name) ?? []), repoRelative]);
        }
      }
    }
  }

  return { setA, moduleFiles, importedFrom };
}

/** Every exported declaration of a file, by the name it exports. */
function exportedDeclarations(source: ts.SourceFile): Map<string, ts.Node> {
  const exported = new Map<string, ts.Node>();
  for (const statement of source.statements) {
    const modifiers = ts.canHaveModifiers(statement) ? (ts.getModifiers(statement) ?? []) : [];
    if (!modifiers.some((modifier) => modifier.kind === ts.SyntaxKind.ExportKeyword)) {
      continue;
    }

    if (ts.isInterfaceDeclaration(statement) || ts.isTypeAliasDeclaration(statement) ||
        ts.isClassDeclaration(statement) || ts.isEnumDeclaration(statement) ||
        ts.isFunctionDeclaration(statement)) {
      if (statement.name !== undefined) {
        exported.set(statement.name.text, statement);
      }
    } else if (ts.isVariableStatement(statement)) {
      for (const declaration of statement.declarationList.declarations) {
        if (ts.isIdentifier(declaration.name)) {
          exported.set(declaration.name.text, declaration);
        }
      }
    }
  }

  return exported;
}

/**
 * Every type name a declaration mentions BY VALUE. For a function or a constant only the
 * signature counts - parameters, return and declared type - because a module supplies arguments
 * and reads results, and never the body's locals.
 */
function referencedTypeNames(node: ts.Node): string[] {
  const names: string[] = [];
  const collect = (at: ts.Node): void => {
    if (ts.isTypeReferenceNode(at)) {
      names.push(ts.isQualifiedName(at.typeName) ? at.typeName.right.text : at.typeName.text);
    } else if (ts.isExpressionWithTypeArguments(at) && ts.isIdentifier(at.expression)) {
      names.push(at.expression.text); // an `extends` clause
    } else if (ts.isTypeQueryNode(at)) {
      names.push(ts.isQualifiedName(at.exprName) ? at.exprName.right.text : at.exprName.text); // `typeof X`
    }

    at.forEachChild(collect);
  };

  if (ts.isFunctionDeclaration(node)) {
    node.parameters.forEach(collect);
    if (node.type !== undefined) {
      collect(node.type);
    }
  } else if (ts.isVariableDeclaration(node)) {
    if (node.type !== undefined) {
      collect(node.type);
    }
  } else {
    collect(node);
  }

  return names;
}

/**
 * Set B: the by-value closure from the four roots, walked syntactically.
 *
 * The roots are resolved by file and name; everything they reach is followed by name, which is
 * sound because the ambiguity is at the roots - a referenced name is read in the context of the
 * declaration that names it, and a second collision would surface as a root failing to resolve
 * rather than as a quiet substitution.
 */
function computeSetB(
  exportsByName: Map<string, { file: string; node: ts.Node }>,
  exportsByFileAndName: Map<string, { file: string; node: ts.Node }>,
): Map<string, string> {
  const setB = new Map<string, string>();
  const queue: string[] = [];

  for (const root of SET_B_ROOTS) {
    const declaration = exportsByFileAndName.get(`${root.file}#${root.name}`);
    if (declaration === undefined) {
      // Loudly: a root that no longer resolves means the surface moved under this computation,
      // and every set below it would otherwise be quietly short.
      throw new Error(
        `Set B root '${root.name}' is not exported by '${root.file}'. If the declaration moved, ` +
          "move it in SET_B_ROOTS too - the roots are file-qualified precisely so this fails rather " +
          "than silently resolving to another declaration of the same name.",
      );
    }

    setB.set(root.name, declaration.file);
    queue.push(...referencedTypeNames(declaration.node));
  }

  while (queue.length > 0) {
    const name = queue.shift() as string;
    if (setB.has(name)) {
      continue;
    }

    const declaration = exportsByName.get(name);
    if (declaration === undefined) {
      continue; // a built-in, a React type, or something no surface file declares
    }

    setB.set(name, declaration.file);
    queue.push(...referencedTypeNames(declaration.node));
  }

  return setB;
}

/**
 * The whole computation, run on every call - never cached to a file, so a module that starts
 * importing a new name changes the answer on the next test run rather than on the next time
 * somebody remembers to regenerate something.
 */
export function computeModuleClientApiSurface(): ModuleClientApiSurface {
  const root = sourceRoot();
  const library = join(root, "client", "src", "canvas", "library");

  const { setA, moduleFiles, importedFrom } = computeSetA(root);

  // The library's own surface: its three declaration folders and the canvas itself. Plus every
  // file outside the library a module was seen importing from - computed, not listed, so a file
  // enters the surface by being imported rather than by anyone maintaining a list.
  const libraryFiles = [
    ...filesUnder(join(library, "definition"), (path) => isSource(path) && !isTestFile(path)),
    ...filesUnder(join(library, "api"), (path) => isSource(path) && !isTestFile(path)),
    ...filesUnder(join(library, "layout"), (path) => isSource(path) && !isTestFile(path)),
    join(library, "DiagramCanvas.tsx"),
  ];

  const allSurfaceFiles = [...new Set([...libraryFiles, ...importedFrom])].sort();

  const exportsByName = new Map<string, { file: string; node: ts.Node }>();
  const interfaceMembers = new Map<string, string[]>();
  const exportsByFileAndName = new Map<string, { file: string; node: ts.Node }>();
  const exportsBySurfaceFile = new Map<string, string>();
  for (const file of allSurfaceFiles) {
    const repoRelative = relative(root, file).replace(/\\/g, "/");
    const source = readSource(file, root); // throws, loudly, if a named surface file is unreadable
    for (const [name, node] of exportedDeclarations(source)) {
      if (ts.isInterfaceDeclaration(node) && !interfaceMembers.has(name)) {
        // The interface's OWN members. Taken from the AST, so a nested object type's members stay
        // in the nested type rather than being hoisted here.
        interfaceMembers.set(
          name,
          node.members.map((member) => member.name?.getText()).filter((each): each is string => each !== undefined),
        );
      }

      exportsByFileAndName.set(`${repoRelative}#${name}`, { file: repoRelative, node });
      if (!exportsByName.has(name)) {
        exportsByName.set(name, { file: repoRelative, node });
        exportsBySurfaceFile.set(name, repoRelative);
      }
    }
  }

  return {
    setA,
    setB: computeSetB(exportsByName, exportsByFileAndName),
    surfaceFiles: allSurfaceFiles.map((file) => relative(root, file).replace(/\\/g, "/")),
    interfaceMembers,
    conformanceOnly: conformanceOnlyNames(root),
    moduleFiles,
    exportsBySurfaceFile,
  };
}
