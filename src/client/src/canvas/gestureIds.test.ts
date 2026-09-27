import { readFileSync, statSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { sourceFiles } from "../sourceFiles";
import { placementId, relationId, withoutPrefix } from "./gestureIds";

/**
 * One builder per gesture id grammar (client-centralization Requirement 9).
 *
 * The grammar is the backend's; its golden fixture (backend-centralization task 21) is where
 * the forms below are pinned to what the backend parses. This file holds the client's half: the
 * builders, the prefix check, and a guard that no canvas writes either form by hand or removes a
 * prefix it has not checked.
 */

describe("the gesture ids", () => {
  it("builds a placement and a relation", () => {
    expect(placementId(120, -3)).toBe("new:120,-3");
    expect(relationId("variable:a", "variable:b")).toBe("rel:variable:a->variable:b");
  });

  it("removes a prefix only when the id carries it and something follows", () => {
    expect(withoutPrefix("variable:growth", "variable:")).toBe("growth");
    // The two ids causal-loop turned into a wrong end by stripping by length.
    expect(withoutPrefix("variable:", "variable:")).toBeNull();
    expect(withoutPrefix("loop:R1", "variable:")).toBeNull();
    expect(withoutPrefix("", "variable:")).toBeNull();
  });
});

/** One valid case of the backend's golden fixture: the id, and the components it is built from. */
interface ValidCase {
  id: string;
  x?: number;
  y?: number;
  row?: number;
  from?: string;
  to?: string;
}

/**
 * The backend's golden fixture for the grammar (backend-centralization task 21), which its own
 * suite parses. Every valid id there is one both tiers build identically, so the client's
 * builders must give each back from its components.
 */
describe("the gesture ids, against the backend's grammar fixture", () => {
  const fixture = JSON.parse(readFileSync(join(sourceRoot(), "fixtures", "cross-tier", "gesture-ids.json"), "utf8")) as {
    placements: { xy: { valid: ValidCase[] }; row: { valid: ValidCase[] } };
    relations: { valid: ValidCase[]; invalid: { id: string }[] };
  };

  it("builds every valid placement from its components", () => {
    const cases = [...fixture.placements.xy.valid, ...fixture.placements.row.valid];
    expect(cases.length).toBeGreaterThan(5);
    for (const placement of cases) {
      expect(placementId(placement.x!, placement.y ?? placement.row!), placement.id).toBe(placement.id);
    }
  });

  it("builds every valid relation from its ends", () => {
    expect(fixture.relations.valid.length).toBeGreaterThan(2);
    for (const relation of fixture.relations.valid) {
      expect(relationId(relation.from!, relation.to!), relation.id).toBe(relation.id);
    }
  });

  it("takes no end out of an id the grammar calls empty or unprefixed", () => {
    // The fixture's invalid relations with an empty end or no prefix, read as element ids a
    // canvas might strip: none yields an end.
    expect(withoutPrefix("rel:", "rel:")).toBeNull();
    for (const invalid of fixture.relations.invalid.filter((relation) => !relation.id.startsWith("rel:"))) {
      expect(withoutPrefix(invalid.id, "rel:"), invalid.id).toBeNull();
    }
  });
});

/** Code, with its comments blanked so prose about an id is not mistaken for building one. */
function codeOf(source: string): string {
  return source
    .replace(/\/\*[\s\S]*?\*\//g, (comment) => comment.replace(/[^\n]/g, " "))
    .replace(/(^|[^:"'`])\/\/.*$/gm, "$1");
}

/**
 * What building an id by hand, or stripping a prefix unchecked, reads like. `before` is the line
 * and the few above it, where a check that returns early - `if (!id.startsWith(p)) return` -
 * sits.
 */
const OFFENCES: readonly { name: string; reads: (line: string, before?: string) => boolean }[] = [
  { name: "a placement id written by hand", reads: (line) => /`new:\$\{/.test(line) },
  { name: "a relation id written by hand", reads: (line) => /`rel:\$\{/.test(line) },
  {
    name: "a prefix removed without a check",
    reads: (line, before = line) => {
      const stripped = /\.slice\(\s*"([^"]+)"\.length\s*\)/.exec(line);
      return stripped !== null && !before.includes(`startsWith("${stripped[1]}")`);
    },
  },
];

function offencesIn(files: readonly string[], root: string): string[] {
  const found: string[] = [];
  for (const file of files) {
    const lines = codeOf(readFileSync(file, "utf8")).split("\n");
    lines.forEach((line, index) => {
      const before = lines.slice(Math.max(0, index - 6), index + 1).join("\n");
      for (const offence of OFFENCES) {
        if (offence.reads(line, before)) {
          found.push(`${relative(root, file).split(sep).join("/")}:${index + 1}  ${offence.name}`);
        }
      }
    });
  }
  return found;
}

/** The repository's `src`, found from this file: the folder holding both `diagrams/` and `.editorconfig`. */
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

describe("no gesture id built or taken apart by hand", () => {
  it("reads each form it exists to catch", () => {
    const planted = [
      "      runAction(elementType, `new:${position.x},${position.y}`);",
      "    sourceAnchor === \"left\" ? `rel:${landing}->${sourceId}` : `rel:${sourceId}->${landing}`;",
      "      const from = sourceElementId.slice(\"variable:\".length);",
    ];
    for (const line of planted) {
      expect(OFFENCES.some((offence) => offence.reads(line)), line).toBe(true);
    }
    // A strip guarded on the same line is the shape the rest of the client already uses.
    expect(OFFENCES.some((offence) => offence.reads("  return key?.startsWith(\"element:\") ? key.slice(\"element:\".length) : null;"))).toBe(false);
  });

  it("finds none in the client or any module's client", () => {
    const root = sourceRoot();
    const files = [...sourceFiles(join(root, "client", "src")), ...sourceFiles(join(root, "diagrams"))]
      .filter((file) => /\.tsx?$/.test(file) && !/\.test\.tsx?$/.test(file))
      .filter((file) => !file.split(sep).includes("generated") && !file.split(sep).includes("backend"))
      .filter((file) => !file.endsWith(`${sep}gestureIds.ts`));

    // Enough files that a walk from the wrong folder cannot pass by finding nothing.
    expect(files.length).toBeGreaterThan(150);
    expect(offencesIn(files, root)).toEqual([]);
  });
});
