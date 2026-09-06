/**
 * Where diagram-library adoption stands, stated once and imported by every guard that
 * shrinks with it (`noPrivateGestures`, the per-module registration check in
 * `libraryGuards`). One list, so the guards cannot disagree about who is migrated.
 *
 * **This set only shrinks.** Each migration task deletes its module's entry in the same
 * change that lands the migration; a name added back is a migration walked backwards, and
 * review treats any addition as a defect. When the set is empty the exclusion mechanism
 * is deleted with it and the guards cover every module unconditionally
 * (diagram-library-adoption Requirements 3.2, 5.2).
 */

/** Modules adoption has not reached yet. */
export const NOT_YET_MIGRATED: ReadonlySet<string> = new Set([
]);
