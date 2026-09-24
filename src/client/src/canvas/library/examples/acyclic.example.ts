import type { AcyclicRule } from "../definition/diagramDefinition";

/**
 * An acyclicity rule, for the readme's constraints entry.
 *
 * **No shipped module declares `acyclic` today**, measured by parsing every module's
 * `DiagramDefinition` rather than by searching for the word. A rule names the relation types a
 * cycle may not be formed from, so a diagram whose edges mean "depends on" can refuse a cycle while
 * leaving its other relation types free to form one.
 */
export const ACYCLIC_EXAMPLE: readonly AcyclicRule[] = [{ relationTypes: ["depends-on"] }];
