import { isCustomShape, type CustomRouteRef, type CustomShapeRef, type DiagramDefinition, type LayoutMode } from "./diagramDefinition";

/**
 * Checks a diagram definition at construction, before anything renders from it. The shapes of
 * wrongness rejected here are definition BUGS, not runtime states - a canvas that can lay out
 * no way, a relation whose endpoint names an element type nobody defined, a custom shape or
 * route that names itself but supplies no way to draw. Rejecting them here means the canvas
 * never has to carry a defensive branch for them (design, Error Handling 4).
 *
 * Returns every complaint rather than the first, because an author fixing a definition wants
 * the whole list once, not one item per attempt.
 */
export function validateDiagramDefinition(definition: DiagramDefinition): readonly string[] {
  const problems: string[] = [];

  const layout = definition.layout;
  if (layout.modes.length === 0) {
    problems.push("The layout allows zero modes: a canvas that can lay out no way is a definition bug.");
  }
  if (layout.modes.includes("row-packed") && layout.rowPacked === undefined) {
    problems.push("The layout allows row-packed without declaring rowPacked: the mode has no width to draw with.");
  }
  const declaredTypes = new Set(definition.elementTypes.map((type) => type.id));
  for (const type of layout.rowPacked?.types ?? []) {
    if (!declaredTypes.has(type)) {
      problems.push(`The row-packed layout gives its width to element type "${type}", which this definition does not declare.`);
    }
  }
  if (layout.modes.includes("tiered-force") && layout.tiers === undefined) {
    problems.push("The layout allows tiered-force without declaring tiers: the mode has no rings to place on.");
  }
  for (const type of (layout.tiers ?? []).flat()) {
    if (!declaredTypes.has(type)) {
      problems.push(`The tiered-force layout puts element type "${type}" on a ring, which this definition does not declare.`);
    }
  }
  if (layout.toggle !== undefined) {
    if (!layout.modes.includes(layout.toggle.on)) {
      problems.push(`The layout toggle switches to "${layout.toggle.on}", which the layout does not allow.`);
    }
    if (layout.modes.length !== 2) {
      problems.push(`The layout toggle needs exactly two modes to switch between; the layout allows ${layout.modes.length}.`);
    }
    if (definition.filter === undefined) {
      problems.push("The layout toggle is drawn below the filter box's legend, and this definition declares no filter.");
    }
  }

  problems.push(...validateBody(definition));

  // A mode's overrides make a second definition the canvas really draws, so it answers to the
  // same checks: an override naming an element type nobody declares is as much a bug there.
  for (const [mode, overrides] of Object.entries(layout.modeOverrides ?? {}) as [LayoutMode, Partial<DiagramDefinition>][]) {
    problems.push(...validateBody({ ...definition, ...overrides, layout }).map((problem) => `Under layout mode "${mode}": ${problem}`));
  }

  return problems;
}

/** The checks on what a definition draws - its types, routes and rules - as opposed to its layout. */
function validateBody(definition: DiagramDefinition): string[] {
  const problems: string[] = [];

  const knownElementTypes = new Set(definition.elementTypes.map((type) => type.id));

  for (const element of definition.elementTypes) {
    if (isCustomShape(element.shape) && !isRenderableCustomShape(element.shape)) {
      problems.push(
        `Element type "${element.id}" names custom shape "${element.shape.customShape}" without supplying its renderer and edge function.`,
      );
    }
    if (element.anchors.kind === "along" && element.anchors.attachDrawnBy !== undefined) {
      problems.push(`Element type "${element.id}" declares attachDrawnBy on along anchors, which record where an end sits and are never drawn by edge.`);
    }
  }

  for (const relation of definition.relationTypes) {
    for (const [end, constraint] of [
      ["source", relation.endpoints.source],
      ["target", relation.endpoints.target],
    ] as const) {
      for (const elementType of constraint.elementTypes) {
        if (!knownElementTypes.has(elementType)) {
          problems.push(
            `Relation type "${relation.id}" allows ${end} element type "${elementType}", which this definition does not declare.`,
          );
        }
      }
    }

    if (typeof relation.route !== "string" && !isBuildableCustomRoute(relation.route)) {
      problems.push(
        `Relation type "${relation.id}" names custom route "${relation.route.customRoute}" without supplying its path builder.`,
      );
    }
  }

  // A filter scoped to a type nobody declares leaves that type's elements hidden or kept by accident.
  for (const elementType of definition.filter?.elementTypes ?? []) {
    if (!knownElementTypes.has(elementType)) {
      problems.push(`The filter applies to element type "${elementType}", which this definition does not declare.`);
    }
  }

  // An acyclic rule naming a relation nobody declares enforces nothing, and enforces it silently:
  // the walk simply never covers an edge, so the diagram admits the cycle the author forbade. That
  // is a definition bug of exactly the shape this function exists to catch - a typo in an id.
  const knownRelationTypes = new Set(definition.relationTypes.map((relation) => relation.id));
  for (const rule of definition.acyclic ?? []) {
    for (const relationType of rule.relationTypes) {
      if (!knownRelationTypes.has(relationType)) {
        problems.push(
          `An acyclic rule names relation type "${relationType}", which this definition does not declare.`,
        );
      }
    }
  }

  return problems;
}

/**
 * The same check as a gate: a module constructing its definition once at load time calls this
 * so a broken definition fails the build's tests instead of rendering a broken canvas.
 */
export function assertValidDiagramDefinition(definition: DiagramDefinition): DiagramDefinition {
  const problems = validateDiagramDefinition(definition);
  if (problems.length > 0) {
    throw new Error(`The diagram definition is invalid:\n${problems.join("\n")}`);
  }

  return definition;
}

/** A custom shape is renderable when it actually carries the functions its contract names. */
function isRenderableCustomShape(shape: CustomShapeRef): boolean {
  return typeof shape.render === "function" && typeof shape.edgePoint === "function";
}

/** A custom route is buildable when it actually carries its path builder. */
function isBuildableCustomRoute(route: CustomRouteRef): boolean {
  return typeof route.path === "function";
}
