import type { DiagramDefinition, DraggingPolicy, LayoutMode } from "../definition/diagramDefinition";

/**
 * The dynamic half of the library's API: what a module may change at runtime without a
 * remount (diagram-library Requirement 1.3). A mode that forbids editing, a phase that
 * disables a relation type, a review state that disables dragging - all of them are edits
 * to this object, applied on the next render. A capability reachable only by forking the
 * canvas is a defect in this surface (Requirement 1.5).
 */
export interface DiagramRuntimeConfig {
  /** Overrides the definition's canvas-wide dragging policy (Requirement 5.2). */
  dragging?: DraggingPolicy;
  /** The layout mode in force, from the definition's allowed modes (Requirement 8.2). */
  activeLayoutMode?: LayoutMode;
  /**
   * Which relation type a connect gesture draws where several are legal between the same
   * endpoints (Requirement 5.5) - a definition `relationTypes` id. The definition states how
   * the type is chosen; this is the "active toolbox tool" answer to that question.
   */
  activeTool?: string;
  /**
   * Per-render definition overrides, merged shallowly over the module's definition - the
   * hatch for reconfiguration deeper than the three named knobs, kept explicit so a reader
   * of a module can see the whole runtime surface in one place.
   */
  definitionOverrides?: Partial<DiagramDefinition>;
}

/** The definition as the canvas actually reads it: the module's statement plus the runtime's overrides. */
export function effectiveDefinition(definition: DiagramDefinition, config: DiagramRuntimeConfig | undefined): DiagramDefinition {
  if (config?.definitionOverrides === undefined && config?.dragging === undefined) {
    return definition;
  }

  return {
    ...definition,
    ...config.definitionOverrides,
    ...(config.dragging !== undefined ? { dragging: config.dragging } : {}),
  };
}
