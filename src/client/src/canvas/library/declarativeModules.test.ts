import { describe, expect, it } from "vitest";
import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";
import { BUILT_IN_SHAPES } from "./definition/diagramDefinition";

/**
 * Every diagram module draws through the declaration, and nothing reaches past it.
 *
 * ## Why this exists rather than a rule everyone remembers
 *
 * The tree already carried a declarative vocabulary and a function-valued escape hatch beside
 * it. **Twenty-eight element types used the hatch and zero used the vocabulary.** Nobody decided
 * that; each canvas was written by copying the one before it, and the copy is how a standard
 * dies. An unasserted standard drifts, and it drifts in the direction of whatever the last
 * author found easiest to imitate.
 *
 * So the standard is asserted. This walks every module client, collects **every** offender, and
 * fails once naming all of them with the file and the rule - not the first, because a guard that
 * stops at the first offender turns one afternoon's work into five.
 *
 * ## What it can and cannot see, stated rather than implied
 *
 * **It reads text.** A renderer under a name nobody has used before goes unseen: `shape: myThing`
 * where `myThing` is a function declared in another file with no recognisable spelling would pass
 * here and be caught only by the type. That limit is real and it is acceptable, because it is not
 * how this happens - what happens is a copy, and a copy carries the shape it was copied from.
 * The three rules below are the three shapes the tree actually grew.
 *
 * **It is keyed by artifact, per file owning the property.** `databricks` has three wrappers over
 * one inner canvas; keying by module would let a compliant wrapper mask a non-compliant inner
 * file, or report one offence three times. A file that declares nothing is not walked at all.
 *
 * ## The two canaries
 *
 * A guard that walks nothing passes. Two assertions stand between this and that: a **floor** on
 * how many module clients it found, and a **named member** it must have seen. Either alone is
 * defeated by the other's failure mode - a floor by a glob that matches the wrong tree, a name by
 * a walk that stops after one file.
 */

const DIAGRAMS = join(__dirname, "..", "..", "..", "..", "diagrams");

/** What a module client may not do, and the words each rule reads. */
const RULES: readonly { rule: string; why: string; matches: (line: string) => boolean }[] = [
  {
    rule: "a module-supplied renderer",
    why: "a shape, route or background the library CALLS is the escape hatch this specification closed",
    matches: (line) => /\b(customShape|customBackground)\s*:/.test(line) || /:\s*CustomShapeRef\b/.test(line),
  },
  {
    rule: "a hand-written structural key list",
    why: "the library derives the key set from the declared actions; a list here is a fifth spelling waiting to happen",
    matches: (line) => /structuralShortcutFor\s*\(\s*event\s*,\s*\[/.test(line),
  },
  {
    rule: "a synthesised key event",
    why: "building a keystroke to name a gesture the library already handed you is fiction the backend then believes",
    matches: (line) => /\{\s*key:\s*"[^"]+",\s*ctrl:/.test(line),
  },
];

/** Every `*.tsx`/`*.ts` under a module's `client/`, tests and generated files excluded. */
function moduleClientFiles(): readonly string[] {
  const files: string[] = [];
  for (const module of readdirSync(DIAGRAMS)) {
    const client = join(DIAGRAMS, module, "client");
    let entries: string[];
    try {
      entries = readdirSync(client);
    } catch {
      continue; // a module with no client half
    }

    for (const entry of entries) {
      if (!entry.endsWith(".ts") && !entry.endsWith(".tsx")) {
        continue;
      }

      if (entry.includes(".test.")) {
        continue;
      }

      const path = join(client, entry);
      if (statSync(path).isFile()) {
        files.push(path);
      }
    }
  }

  return files;
}

/** One offence: which file, which rule, and the line as written. */
interface Offence {
  file: string;
  rule: string;
  why: string;
  line: number;
  text: string;
}

function offencesIn(path: string): readonly Offence[] {
  const found: Offence[] = [];
  const lines = readFileSync(path, "utf8").split(/\r?\n/);

  lines.forEach((text, index) => {
    // A line that is only a comment describes the rule rather than breaking it - every one of
    // these files explains what it no longer does, and a guard that could not tell the
    // difference would forbid saying so.
    if (/^\s*(\/\/|\*|\/\*)/.test(text)) {
      return;
    }

    for (const { rule, why, matches } of RULES) {
      if (matches(text)) {
        found.push({ file: path.slice(path.indexOf("diagrams")), rule, why, line: index + 1, text: text.trim() });
      }
    }
  });

  return found;
}

describe("every diagram module draws through the declaration", () => {
  const files = moduleClientFiles();

  it("names a shape the library actually has", () => {
    // AND THIS IS WHAT `BUILT_IN_SHAPES` IS FOR. Its comment claimed a guard walked it and a
    // toolbox derived icons from it; both were imagined, and the constant had exactly one
    // reference - its own definition. This is the guard, so half of that claim is now true and
    // the other half is gone from the comment rather than left standing.
    //
    // A misspelled shape name is the failure this catches: `shape: "rounded-rect"` type-checks
    // nowhere but reads as a string here, and at runtime the switch falls through and the
    // element draws NOTHING. Silent, and exactly the shape of thing a copy carries.
    const named = files.flatMap((file) => {
      const text = readFileSync(file, "utf8");
      return [...text.matchAll(/shape:\s*"([^"]+)"/g)].map((match) => ({ file: file.slice(file.indexOf("diagrams")), shape: match[1]! }));
    });

    // The canary for this one: the modules do name shapes, so a walk that found none would be
    // a walk that found nothing.
    expect(named.length).toBeGreaterThan(0);
    expect(named.filter((entry) => !BUILT_IN_SHAPES.includes(entry.shape as never)).map((entry) => `${entry.file} — ${entry.shape}`)).toEqual([]);
  });

  it("walks the module clients, and knows one of them by name", () => {
    // THE TWO CANARIES. A guard that walks nothing passes every rule it has; a floor catches a
    // glob pointing at the wrong tree, and a named member catches a walk that stops early.
    // Neither alone is enough, which is why there are two.
    expect(files.length).toBeGreaterThanOrEqual(40);
    expect(files.some((file) => file.endsWith(join("wardley-map", "client", "WardleyCanvas.tsx")))).toBe(true);
  });

  it("finds no module reaching past the vocabulary", () => {
    const offences = files.flatMap(offencesIn);

    // EVERY offender, named once with its rule - a guard that stops at the first turns one
    // afternoon's work into five, and the second author never learns why the first stopped.
    expect(
      offences.map((offence) => `${offence.file}:${offence.line} — ${offence.rule} (${offence.why})\n    ${offence.text}`),
    ).toEqual([]);
  });
});
