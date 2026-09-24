import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import mermaid from "mermaid";
import { computeModuleClientApiSurface, SET_B_ROOTS, sourceRoot } from "./diagramModuleClientApi.surface";

/**
 * `docs/diagram-module-client-api.md` is held to the code by this file rather than by review.
 *
 * Six checks, each collecting every offender and failing once naming them all, because a check that
 * stops at the first gives a repair list of length one and is run again for the next.
 *
 * **No check may pass when its input is missing.** An unreadable readme, an unreadable surface file
 * or a Set B root that no longer resolves throws rather than producing a smaller answer - a short set
 * makes every check below it pass by having nothing to check, which is the failure this whole file
 * exists to prevent.
 */

/** The readme, read once. Throws rather than returning empty - see the note above. */
function readme(): string {
  const path = join(sourceRoot(), "..", "docs", "diagram-module-client-api.md");
  if (statSync(path, { throwIfNoEntry: false })?.isFile() !== true) {
    throw new Error(`The readme is missing at '${path}'. Every check below would otherwise pass on an empty document.`);
  }

  return readFileSync(path, "utf8").replace(/\r\n/g, "\n");
}

/**
 * The document with every fenced block removed.
 *
 * **The readme demonstrates its own conventions by showing an example of each, inside a fence.** A
 * parse that reads those examples treats them as real entries - and because they name real
 * declarations, it would PASS for the wrong reason rather than fail. A document that teaches its
 * format teaches the parser a lie unless the parser skips fences.
 */
function withoutFences(text: string): string {
  return text.replace(/```[\s\S]*?```/g, "");
}

/** Every name on a `**Declarations:**` line, outside fences. */
function declaredNames(text: string): Map<string, string> {
  const declared = new Map<string, string>();
  const body = withoutFences(text);
  let section = "(before the first heading)";
  for (const line of body.split("\n")) {
    const heading = /^#{2,3} (.+)$/.exec(line);
    if (heading !== null) {
      section = heading[1];
      continue;
    }

    const declarations = /^\*\*Declarations:\*\* (.+)$/.exec(line);
    if (declarations !== null) {
      for (const name of declarations[1].matchAll(/`([^`]+)`/g)) {
        declared.set(name[1], section);
      }
    }
  }

  return declared;
}

/** The section a heading opens, to its text - for the field-coverage check. */
function sections(text: string): Map<string, string> {
  const found = new Map<string, string>();
  let current: string | null = null;
  let body: string[] = [];
  for (const line of withoutFences(text).split("\n")) {
    const heading = /^#{2,3} (.+)$/.exec(line);
    if (heading !== null) {
      if (current !== null) {
        found.set(current, body.join("\n"));
      }

      current = heading[1];
      body = [];
    } else {
      body.push(line);
    }
  }

  if (current !== null) {
    found.set(current, body.join("\n"));
  }

  return found;
}

/** Every `Source:` line with the fenced block under it - but only a Source: line OUTSIDE a fence. */
function excerpts(text: string): { file: string; body: string }[] {
  // The readme demonstrates this convention by showing a Source: line inside a fence. Today that
  // demonstration has no body under it, so the pattern below would skip it anyway - but that is
  // coincidence, not knowledge, and it breaks the day a demonstration includes an example body.
  // So fences are located first and a Source: line inside one is never an excerpt.
  const insideAFence = fenceSpans(text);
  return [...text.matchAll(/Source: \[`([^`]+)`\]\([^)]+\)\n\n```[a-z]*\n([\s\S]*?)\n```/g)]
    .filter((match) => !insideAFence((match.index ?? 0)))
    .map((match) => ({ file: match[1], body: match[2] }));
}

/** Whether a character offset falls inside a fenced block, fences paired in document order. */
function fenceSpans(text: string): (offset: number) => boolean {
  const spans: [number, number][] = [];
  let open: number | null = null;
  for (const fence of text.matchAll(/^```.*$/gm)) {
    const at = fence.index ?? 0;
    if (open === null) {
      open = at;
    } else {
      spans.push([open, at]);
      open = null;
    }
  }

  return (offset) => spans.some(([start, end]) => offset > start && offset < end);
}

/** The backticked names of the final list. */
function internalList(text: string): string[] {
  const section = sections(text).get("Library-internal exports");
  if (section === undefined) {
    throw new Error("The readme has no 'Library-internal exports' section, so check 3 would compare against nothing.");
  }

  // Only the list itself - the lines that START with a backtick. The sentence above it carries the
  // commit the list was read at, in backticks, and reading the whole section reported that hash as
  // a missing export: the third time this document annotated a convention in a way its own parser
  // misread.
  const listLines = section.split("\n").filter((line) => line.startsWith("`"));
  return listLines.flatMap((line) => [...line.matchAll(/`([^`]+)`/g)].map((match) => match[1]));
}

/**
 * Tests that walk module clients: a client test naming the diagrams folder in a path literal AND
 * reading the filesystem.
 *
 * **Its blind spot, stated because the rule's own author is in it**: this finds a test that walks
 * DIRECTLY, not one that walks through a helper. This file walks through
 * `diagramModuleClientApi.surface.ts`, so the rule does not find this file - which is correct for
 * what the readme's table is for (guards a MODULE is subject to) and would be wrong for any use that
 * claimed to enumerate every walker.
 */
function walkingTests(): string[] {
  const root = sourceRoot();
  const client = join(root, "client", "src");
  const found: string[] = [];
  const walk = (at: string): void => {
    for (const entry of readdirSync(at)) {
      const path = join(at, entry);
      if (statSync(path, { throwIfNoEntry: false })?.isDirectory() === true) {
        if (entry !== "node_modules") {
          walk(path);
        }
      } else if (/\.test\.tsx?$/.test(entry)) {
        const text = readFileSync(path, "utf8");
        const namesTheFolder = /["'`][^"'`]*\bdiagrams\b[^"'`]*["'`]/.test(text);
        const readsTheTree = /readdirSync|statSync|import\.meta\.glob/.test(text);
        if (namesTheFolder && readsTheTree) {
          found.push(path.slice(client.length + 1).replace(/\\/g, "/"));
        }
      }
    }
  };

  walk(client);
  return found.sort();
}

describe("docs/diagram-module-client-api.md is held to the code", () => {
  const surface = computeModuleClientApiSurface();
  const text = readme();
  const declared = declaredNames(text);

  it("is alive: the readers find what was PLANTED for them, and not what was hidden from them", () => {
    // A liveness gate that only asks "did this come back empty" detects blindness solely where
    // blindness is implausible: if the true answer were legitimately empty, a blind reader and a
    // working one agree and the run CONFIRMS the broken instrument. So the readers are given a
    // document written here, with a positive they must find and a decoy they must not.
    const planted = [
      "## A planted section",
      "",
      "**Declarations:** `PlantedDeclaration`",
      "",
      "It names `plantedMember` in its own body.",
      "",
      "Source: [`docs/diagram-module-client-api.md`](../docs/diagram-module-client-api.md)",
      "",
      "```ts",
      "const plantedExcerpt = true;",
      "```",
      "",
      "```",
      "**Declarations:** `DecoyInsideAFence`",
      "```",
      "",
      "## Library-internal exports",
      "",
      "`PlantedInternal`",
      "",
    ].join("\n");

    const plantedNames = declaredNames(planted);
    expect([...plantedNames.keys()], "the Declarations reader cannot see a planted entry, so a clean " +
      "run of check 1 would mean nothing").toContain("PlantedDeclaration");
    expect([...plantedNames.keys()], "THE DECOY WAS READ: a Declarations line inside a fence was " +
      "treated as a real entry. The readme demonstrates its own conventions inside fences, so this " +
      "is the failure that makes the checks pass for the wrong reason.").not.toContain("DecoyInsideAFence");
    expect(excerpts(planted).map((each) => each.body), "the excerpt reader cannot see a planted Source block")
      .toEqual(["const plantedExcerpt = true;"]);
    expect(internalList(planted), "the internal-list reader cannot see a planted list").toEqual(["PlantedInternal"]);
    expect(sections(planted).get("A planted section") ?? "", "the section reader lost a planted body")
      .toContain("`plantedMember`");

    // Only now is the real document's non-emptiness worth asserting: the readers are known to read.
    expect(text.length, "the readme is empty").toBeGreaterThan(2000);
    expect(declared.size, "no Declarations lines were parsed from the real document").toBeGreaterThanOrEqual(10);
    expect(excerpts(text).length, "no Source: excerpts were parsed from the real document").toBeGreaterThanOrEqual(10);
    expect(new Set([...surface.setA.keys(), ...surface.setB.keys()]).size).toBeGreaterThanOrEqual(90);
  });

  it("check 1 - covers every name in the surface, and every member of every covered interface", () => {
    const uncovered = [...new Set([...surface.setA.keys(), ...surface.setB.keys()])]
      .filter((name) => !declared.has(name))
      .sort();
    const missingMembers: string[] = [];
    const bySection = sections(text);
    for (const [name, section] of declared) {
      const members = surface.interfaceMembers.get(name);
      if (members === undefined || members.length === 0) {
        continue;
      }

      const body = bySection.get(section) ?? "";
      const absent = members.filter((member) => !body.includes(`\`${member}\``));
      if (absent.length > 0) {
        missingMembers.push(`${name} (in '${section}'): ${absent.join(", ")}`);
      }
    }

    expect(uncovered, `${uncovered.length} name(s) in the computed surface have no Declarations line. ` +
      "Each is a name a module can import that this document does not describe - add an entry, or stop exporting it.")
      .toEqual([]);
    expect(missingMembers, "an entry declares an interface without naming every member in its own section " +
      "(Requirement 3.3), so a member added later would go undocumented while the entry still looked complete:\n" +
      missingMembers.join("\n")).toEqual([]);
  });

  it("check 3 - every declared and listed name exists, and the internal list is exactly what is left over", () => {
    const unknown = [...declared.keys()]
      .filter((name) => !surface.exportsBySurfaceFile.has(name))
      .sort();
    const listed = internalList(text);
    const computed = [...surface.exportsBySurfaceFile.entries()]
      .filter(([name, file]) => file.includes("canvas/library") && !surface.setA.has(name) && !surface.setB.has(name))
      .map(([name]) => name)
      .sort();
    const listedInASet = listed.filter((name) => surface.setA.has(name) || surface.setB.has(name));

    expect(unknown, `${unknown.length} declared name(s) are exported by no surface file - a renamed or ` +
      "removed declaration whose entry outlived it.").toEqual([]);
    expect(listedInASet, "a name on the internal list is in Set A or Set B, so it is module-facing and " +
      "must have an entry rather than a line on that list.").toEqual([]);
    expect(listed, "the internal list is not exactly the library's exports outside both sets. It is " +
      "generated rather than typed, so a difference means the generator and the document have diverged.")
      .toEqual(computed);
  });

  it("check 4 - every excerpt occurs verbatim in the file it names", () => {
    const wrong: string[] = [];
    for (const { file, body } of excerpts(text)) {
      const path = join(sourceRoot(), "..", file);
      if (statSync(path, { throwIfNoEntry: false })?.isFile() !== true) {
        wrong.push(`${file}: the file does not exist`);
        continue;
      }

      if (!readFileSync(path, "utf8").replace(/\r\n/g, "\n").includes(body)) {
        wrong.push(`${file}: the excerpt no longer occurs there`);
      }
    }

    expect(wrong, "an excerpt has drifted from its source. Excerpts are copied, never retyped, so a " +
      "difference means the source changed and the document did not:\n" + wrong.join("\n")).toEqual([]);
  });

  it("check 5 - diagram identifiers are real, and the guard table is the walking tests", () => {
    const named = new Set<string>();
    for (const block of text.matchAll(/```mermaid\n([\s\S]*?)```/g)) {
      for (const identifier of block[1].matchAll(/\b(use[A-Z]\w+|[A-Z]\w+(?:Definition|Declaration|Ref|Rule|Handlers|Model|Canvas|Registration|Refused|Drawn))\b/g)) {
        named.add(identifier[1]);
      }
    }

    const unreal = [...named]
      .filter((name) => !surface.setA.has(name) && !surface.setB.has(name) && !surface.exportsBySurfaceFile.has(name))
      .sort();
    const walkers = walkingTests();
    const undocumented = walkers.filter((path) => !text.includes(`\`${path.split("/").pop() as string}\``)).sort();

    expect(unreal, "a diagram names something that is not in the surface. A picture reads as " +
      "illustration rather than as a claim, which is why this is checked:\n" + unreal.join("\n")).toEqual([]);
    expect(walkers.length, "the walking-test detection found nothing, so the guard table is unchecked").toBeGreaterThanOrEqual(10);
    expect(undocumented, `${undocumented.length} test(s) walk module clients and are not in the guard ` +
      "table. A module is subject to them whether the document says so or not:\n" + undocumented.join("\n")).toEqual([]);
  });

  it("check 6 - every mermaid block parses, and is the kind the document uses it as", async () => {
    const blocks = [...text.matchAll(/```mermaid\n([\s\S]*?)```/g)].map((match) => match[1]);
    const failures: string[] = [];
    const kinds: string[] = [];
    for (const [index, block] of blocks.entries()) {
      try {
        const parsed = await mermaid.parse(block);
        kinds.push(parsed?.diagramType ?? "unknown");
      } catch (error) {
        // A block that fails to parse degrades to its source text rather than erroring, so this
        // failure is silent in every renderer - which is why it is asserted rather than looked at.
        failures.push(`block ${index + 1}: ${(error as Error).message.split("\n")[0]}`);
      }
    }

    expect(blocks.length, "the readme carries no mermaid blocks, so this check has nothing to parse").toBeGreaterThanOrEqual(5);
    expect(failures, "a mermaid block does not parse. It will render as a code listing rather than as " +
      "a diagram, which no render check reliably catches:\n" + failures.join("\n")).toEqual([]);
    expect(kinds).toEqual(["flowchart-v2", "sequence", "classDiagram", "sequence", "sequence"]);
  });

  it("the paths this document writes resolve, because it is written to make that true", () => {
    // A rule over a document I own, NOT a heuristic over documents I do not. Across the
    // specifications, 61 of 131 backticked path spans legitimately do not resolve - elisions, globs,
    // templates, paths relative to an implied root, and files a design proposes to create. A guard
    // over those would be a second copy of the data. This readme is written to make its own spans
    // resolvable, so here the rule is a rule rather than a guess.
    const spans = [...withoutFences(text).matchAll(/`((?:src|docs)\/[A-Za-z0-9_./-]+)`/g)].map((match) => match[1]);
    const dead = spans.filter((path) => statSync(join(sourceRoot(), "..", path), { throwIfNoEntry: false }) === undefined).sort();

    expect(spans.length, "no repo-relative path spans were found, so this check is vacuous").toBeGreaterThanOrEqual(10);
    expect(dead, "a path this document names does not exist:\n" + dead.join("\n")).toEqual([]);
  });

  it("the conformance-only category is enumerated from the code, not listed in prose", () => {
    // A type describing a module FILE's own export shape cannot be imported by that file: it is
    // satisfied by the file EXISTING at the import.meta.glob boundary. Such a type is in neither set
    // and is NOT library-internal, so it may carry a Declarations line without being in Set A or B.
    expect(surface.conformanceOnly.length,
      "no import.meta.glob type arguments were found, so the third category is unenumerated and " +
      "DiagramClientModule would read as library-internal").toBeGreaterThanOrEqual(1);
    for (const name of surface.conformanceOnly) {
      expect(surface.exportsBySurfaceFile.has(name) ? declared.has(name) || internalList(text).includes(name) : true,
        `'${name}' is a conformance-only type in the surface and appears nowhere in the document`).toBe(true);
    }
  });

  it("sees what a module imports from OUTSIDE the library, in Set A", () => {
    // Kept from task 2: useViewReport lives outside the library and reaches the surface only by
    // being imported, so finding it proves the import parse AND that a non-library file can enter
    // the surface - the half a library-only walk would miss entirely.
    expect([...surface.setA.keys()]).toContain("useViewReport");
    expect(surface.surfaceFiles.filter((file) => !file.includes("canvas/library")).length).toBeGreaterThanOrEqual(5);
  });

  it("the Set B roots are all reached, so the walk did not quietly stop", () => {
    for (const root of SET_B_ROOTS) {
      expect([...surface.setB.keys()], `'${root.name}' is a Set B root and must be reached`).toContain(root.name);
    }
  });
});
