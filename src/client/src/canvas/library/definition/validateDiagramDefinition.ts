import type { CustomRouteRef, CustomShapeRef, DiagramDefinition } from "./diagramDefinition";

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

  if (definition.layout.modes.length === 0) {
    problems.push("The layout allows zero modes: a canvas that can lay out no way is a definition bug.");
  }

  const knownElementTypes = new Set(definition.elementTypes.map((type) => type.id));

  for (const element of definition.elementTypes) {
    if (typeof element.shape !== "string" && !isRenderableCustomShape(element.shape)) {
      problems.push(
        `Element type "${element.id}" names custom shape "${element.shape.customShape}" without supplying its renderer and edge function.`,
      );
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
