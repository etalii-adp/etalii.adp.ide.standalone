/**
 * Which open tabs hold unsaved edits, keyed by their path key. The tab strip consults this on
 * close so an unsaved editor asks before its work vanishes (modular-text-editors R6.5);
 * editors mark themselves here, and diagrams - saved through commands as they are edited -
 * simply never appear.
 */
const dirty = new Set<string>();

export function markTabDirty(key: string, isDirty: boolean) {
  if (isDirty) {
    dirty.add(key);
  } else {
    dirty.delete(key);
  }
}

export function isTabDirty(key: string): boolean {
  return dirty.has(key);
}
