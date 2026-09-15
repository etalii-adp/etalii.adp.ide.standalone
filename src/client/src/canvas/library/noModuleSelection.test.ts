import { readdirSync, readFileSync, statSync } from "node:fs";
import { basename, dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

/**
 * No module client handles selection. The library reads the backend's selection, pushes a
 * press's, clears on a background press, and wires the shared menu; a module passes `source`
 * and declares which types are `selectable: false`, and writes nothing else about selection
 * (centralized-selection Requirements 1, 4, 5, 6 and 9.1).
 *
 * ## What it fails on, naming each offender by file
 *
 * - a `DiagramSelection` in a module client - the inbound mapping, derived by hand;
 * - the backend's selection read by hand - `useContextSelection`, `innermostKey`,
 *   `selectedElementIdOf`, `elementIdOfKey` - which is where every inbound mapping started;
 * - an `onSelectionChanged` handler - the outbound push, written by hand;
 * - a selection pushed by hand - `elementSelectionOf` - which is where every outbound push, and
 *   causal-loop's hand-built background menu, ended;
 * - a `selection=` or `context=` prop - the old contract, wired by hand;
 * - the library's own wiring reached past the contract - `DiagramCanvasCore`,
 *   `LibraryEventHandlers`, `LibraryContextIntegration`, `useLibrarySelection` - which task 21
 *   left exported for the library's tests, not for modules;
 * - a declared class bound to `state.selected` or `state.connectTarget` - a private look where
 *   the library's `canvas-selected` and `library-connect-target` are the only ones;
 * - module-held selection or focus state - a `focusedId`, or a `useState` pair named for a
 *   focused or selected item: a second answer to "what is selected";
 * - a canvas whose test does not call `expectLibrarySelection` - which is what makes the mounted
 *   assertion cover every canvas rather than the ones somebody remembered.
 *
 * It is keyed per CANVAS FILE - a client source that renders `<DiagramCanvas` - so databricks'
 * three wrappers around its one canvas are one member, not three, and cannot misreport.
 *
 * ## Its limits, stated plainly
 *
 * It reads text. It catches the glue as it has actually been written - the shapes the sixteen
 * canvases carried - and not selection glue rebuilt under names it does not recognise: a hand
 * derivation typed as `readonly SelectedItem[]`, a push through an alias of `select`. **The
 * mounted assertion closes that**: `expectLibrarySelection` drives each canvas and asserts what
 * selection DOES, whatever the code calls it, and this guard's last rule makes it run for every
 * canvas.
 */

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

/** Every module client's .ts/.tsx files, split into sources and tests. */
function moduleClients(root: string): { module: string; sources: string[]; tests: string[] }[] {
  const diagrams = join(root, "diagrams");
  return readdirSync(diagrams, { withFileTypes: true })
    .filter((entry) => entry.isDirectory() && statSync(join(diagrams, entry.name, "client"), { throwIfNoEntry: false })?.isDirectory() === true)
    .map((entry) => {
      const sources: string[] = [];
      const tests: string[] = [];
      const walk = (directory: string) => {
        for (const child of readdirSync(directory, { withFileTypes: true })) {
          const path = join(directory, child.name);
          if (child.isDirectory()) {
            if (child.name !== "node_modules") {
              walk(path);
            }
          } else if (/\.test\.tsx?$/.test(child.name)) {
            tests.push(path);
          } else if (/\.tsx?$/.test(child.name)) {
            sources.push(path);
          }
        }
      };
      walk(join(diagrams, entry.name, "client"));
      return { module: entry.name, sources, tests };
    });
}

/** The shapes selection glue has been written in, each with what an offender is told. */
const GLUE: readonly { pattern: RegExp; says: string }[] = [
  { pattern: /\bDiagramSelection\b/, says: "derives a DiagramSelection by hand - the library resolves the pushed selection" },
  {
    pattern: /\b(useContextSelection|innermostKey|selectedElementIdOf|elementIdOfKey)\b/,
    says: "reads the backend's selection by hand - the library resolves it",
  },
  { pattern: /\bonSelectionChanged\b/, says: "handles onSelectionChanged - the library pushes a press's selection" },
  { pattern: /\belementSelectionOf\b/, says: "pushes a selection by hand - the library pushes a press's, and the menu's" },
  { pattern: /\bselection=\{/, says: "passes selection= - pass source instead" },
  { pattern: /\bcontext=\{/, says: "passes context= - the library wires the shared menu" },
  {
    pattern: /path:\s*"state\.(selected|connectTarget)"/,
    says: "declares a class bound to state.selected or state.connectTarget - canvas-selected and library-connect-target are the only looks",
  },
  {
    pattern: /\b(DiagramCanvasCore|LibraryEventHandlers|LibraryContextIntegration|useLibrarySelection)\b/,
    says: "reaches past the module contract into the library's own selection wiring - mount DiagramCanvas with source",
  },
  { pattern: /\bfocusedId\b/, says: "holds a focusedId - the library's selection is the only one" },
  {
    pattern: /const\s*\[\s*(focused|selected)\w*\s*,\s*set\w+\s*\]\s*=\s*useState/,
    says: "holds selection or focus in React state - the library's selection is the only one",
  },
];

/**
 * The source with its comments removed. Glue is code: a comment explaining why a `focusedId`
 * was deleted must not read as one, or this guard would be a check on prose - edited whenever
 * the explanation is, and failing the modules that explain themselves best. A `//` counts as a
 * comment only after whitespace or at a line's start, so the one in `https://` survives.
 */
function codeOf(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, "").replace(/(^|\s)\/\/.*$/gm, "$1");
}

/** A canvas file: a module client source that renders the library's canvas itself. */
const RENDERS_CANVAS = /<DiagramCanvas\b/;

const CALLS_ASSERTION = /\bexpectLibrarySelection\(/;

describe("no module handles selection", () => {
  const root = sourceRoot();
  const clients = moduleClients(root);
  const canvases = clients.flatMap(({ module, sources, tests }) =>
    sources
      .filter((path) => RENDERS_CANVAS.test(readFileSync(path, "utf-8")))
      .map((path) => ({ module, path, tests })),
  );

  it("walks every canvas in the tree - a floor on what it read, and a member it must hold", () => {
    // Structure, not content: sixteen canvas files render the library today. A walk that found
    // fewer has stopped reading the tree, and would pass by reading nothing.
    expect(canvases.length, canvases.map((canvas) => relative(root, canvas.path)).join("\n")).toBeGreaterThanOrEqual(16);
    // The canary: the reference migration is a member by name.
    expect(canvases.map((canvas) => relative(root, canvas.path).replaceAll("\\", "/"))).toContain("diagrams/timeline/client/TimelineCanvas.tsx");
  });

  it("finds no selection glue in any module client, naming each file that has some", () => {
    const offenders: string[] = [];

    for (const { sources } of clients) {
      for (const path of sources) {
        const content = codeOf(readFileSync(path, "utf-8"));
        for (const { pattern, says } of GLUE) {
          if (pattern.test(content)) {
            offenders.push(`${relative(root, path)}: ${says}`);
          }
        }
      }
    }

    expect(offenders, offenders.join("\n")).toEqual([]);
  });

  it("finds every canvas's test calling expectLibrarySelection, so the mounted assertion covers them all", () => {
    // A canvas with a test of its own (OwlCanvas.test.tsx beside OwlCanvas.tsx) must call it
    // there; a canvas tested only through wrappers - databricks' - must have it called by one of
    // its module's tests. Either way, no canvas escapes the mounted assertion by omission.
    const missing: string[] = [];

    for (const { path, tests } of canvases) {
      const own = join(dirname(path), `${basename(path).replace(/\.tsx?$/, "")}.test.tsx`);
      const candidates = tests.includes(own) ? [own] : tests;
      if (!candidates.some((test) => CALLS_ASSERTION.test(readFileSync(test, "utf-8")))) {
        missing.push(`${relative(root, path)}: no ${tests.includes(own) ? basename(own) : "test in its module"} calls expectLibrarySelection`);
      }
    }

    expect(missing, missing.join("\n")).toEqual([]);
  });
});
