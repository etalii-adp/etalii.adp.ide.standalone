import { valueAtPath } from "../definition/binding";
import type { ElementTypeDefinition } from "../definition/diagramDefinition";
import type { DiagramModelElement } from "../api/diagramModel";

/**
 * Each element whose type declares {@link ElementTypeDefinition.centreOf} moved to the mean of the
 * centres of the elements its payload lists there; every other element as it is.
 *
 * An element's `x` and `y` are its centre, so the mean of the members' is the point among them.
 * Where the list is empty, is not a list, or names an element the model does not hold - one the
 * viewport has not brought in - the element stays where the model put it: a mean over the members
 * that happen to be held would move it every time the reader scrolls.
 */
export function placedAtCentreOfMembers(
  elements: readonly DiagramModelElement[],
  types: ReadonlyMap<string, ElementTypeDefinition>,
): readonly DiagramModelElement[] {
  if (![...types.values()].some((type) => type.centreOf !== undefined)) {
    return elements;
  }

  const byId = new Map(elements.map((element) => [element.id, element]));
  return elements.map((element) => {
    const path = types.get(element.type)?.centreOf;
    if (path === undefined) {
      return element;
    }

    const ids = valueAtPath(path, { element, payload: element.payload });
    const members = Array.isArray(ids) ? ids.map((id) => (typeof id === "string" ? byId.get(id) : undefined)) : [];
    if (members.length === 0 || members.some((member) => member === undefined)) {
      return element;
    }

    const held = members as DiagramModelElement[];
    return {
      ...element,
      x: held.reduce((sum, member) => sum + member.x, 0) / held.length,
      y: held.reduce((sum, member) => sum + member.y, 0) / held.length,
    };
  });
}
