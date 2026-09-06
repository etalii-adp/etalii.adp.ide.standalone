/**
 * Where diagram-library adoption stands, stated once and imported by every guard that
 * shrinks with it (`noPrivateGestures`, the per-module registration check in
 * `libraryGuards`). One list, so the guards cannot disagree about who is migrated.
 *
 * **These sets only shrink.** Each migration task deletes its module's entry in the same
 * change that lands the migration; a name added back is a migration walked backwards, and
 * review treats any addition as a defect. When both sets are empty the exclusion mechanism
 * is deleted with them and the guards cover every module unconditionally
 * (diagram-library-adoption Requirements 3.2, 5.2).
 */

/** Modules adoption has not reached yet. */
export const NOT_YET_MIGRATED: ReadonlySet<string> = new Set([
  "ansible-structure",
  "azure-pipeline",
  "c4",
  "causal-loop",
  "databricks",
  "dependency-graph",
  "helm-charts",
  "sparql",
]);

/**
 * rdf is a split module: `RdfCanvas` migrated as the library's graph reference while its three
 * sibling readings wait for adoption task 6. Until then the guards skip these named files
 * rather than the whole module, so the migrated reference stays guarded.
 */
export const NOT_YET_MIGRATED_FILES: ReadonlySet<string> = new Set([
  "OwlCanvas.tsx",
  "SkosCanvas.tsx",
  "ShaclCanvas.tsx",
]);
