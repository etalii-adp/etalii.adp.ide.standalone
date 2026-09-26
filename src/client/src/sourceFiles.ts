import { readdirSync } from "node:fs";
import { join, resolve } from "node:path";

/**
 * Folders no source lives in: build output, installed packages and tool caches.
 *
 * `bin` and `obj` are the ones that matter. Every diagram module has a backend beside its client,
 * and after a build its `bin/` and `obj/` are nine in ten of all the entries under the modules'
 * root: 9,294 entries to find 74 sources, measured on 2026-09-26. A guard walking into them timed
 * out at 5.5 s in a gate while the same walk took under half a second on a quiet machine, because
 * build output is largest and busiest exactly while a gate runs.
 */
export const notSources: ReadonlySet<string> = new Set(["bin", "obj", "node_modules", "dist", "coverage", ".vite", ".turbo"]);

const walked = new Map<string, readonly string[]>();

/**
 * Every file under `root`, in the order the directories read, never entering a folder in
 * `notSources`. Callers filter by extension themselves.
 *
 * **One walk per root per test file.** The answer is memoised, so a file whose tests ask for the
 * same tree several times pays once. Vitest isolates module state per test file, so the memo does
 * not carry across files - each file still walks once. The array is shared: filter or copy it,
 * never change it.
 *
 * The directory entry says what it is, so nothing is statted.
 */
export function sourceFiles(root: string): readonly string[] {
  const key = resolve(root);
  let files = walked.get(key);
  if (files === undefined) {
    const found: string[] = [];
    const walk = (directory: string): void => {
      for (const entry of readdirSync(directory, { withFileTypes: true })) {
        const path = join(directory, entry.name);
        if (entry.isDirectory()) {
          if (!notSources.has(entry.name)) walk(path);
        } else {
          found.push(path);
        }
      }
    };
    walk(key);
    files = found;
    walked.set(key, files);
  }

  return files;
}
