import type { Element } from "@client/generated/elements_pb";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "./diagramModel";

/**
 * The structural half of a module's model, built from the wire rather than by the module.
 *
 * <b>This is what removes the 665 lines.</b> Ten percent of every module client is mapping its
 * own folded model into `DiagramModel` — and the mapping is the same five facts every time:
 * identity, type, position, size, and an edge's two ends. Every module writes it differently,
 * and no two agree: `dotnet-dependency-graph` parses an edge's ends out of the element id,
 * others read them from their payload, and each hard-codes a size constant and the definition's
 * element-type id in its canvas.
 *
 * So the backend sends those shaped and this turns them into a `DiagramModel` with no module
 * code at all.
 *
 * <b>The semantic half is deliberately absent.</b> Which payload field is the header, which
 * collection are the rows — that stays a binding in the module's declaration, because putting it
 * on the wire would mean a `.proto` deciding presentation, which inverts the dependency
 * `structure.md` sets. What crosses here is only what is the same concept in every module.
 *
 * <b>Inert until a module fills the fields.</b> Every structural field is optional; an element
 * that sets none maps to what the module would have built by hand anyway, so the twelve
 * unmigrated modules are unaffected.
 */

/**
 * Whether a wire element is a connection.
 *
 * <b>Its endpoints are the discriminator</b>, rather than a flag or a naming convention: an
 * element that names two ends is an edge and one that does not is a node, in every notation.
 * That is what lets the split happen here instead of in thirteen canvases with thirteen rules.
 */
export function isConnectionElement(element: Element): boolean {
  return element.sourceId !== undefined && element.targetId !== undefined;
}

/** One wire element as the canvas's element model, carrying its payload for bindings to read. */
export function elementOf(element: Element, payload?: unknown): DiagramModelElement {
  return {
    id: element.id?.value ?? "",
    // The definition's own id where the backend sends one; otherwise the mime-style type, which
    // is what an unmigrated module's canvas passes today.
    type: element.elementType !== "" ? element.elementType : element.type,
    x: element.position?.x ?? 0,
    y: element.position?.y ?? 0,
    ...(element.size ? { width: element.size.width, height: element.size.height } : {}),
    payload,
  };
}

/** One wire element as a connection. Callers check {@link isConnectionElement} first. */
export function connectionOf(element: Element): DiagramModelConnection {
  return {
    id: element.id?.value ?? "",
    type: element.elementType !== "" ? element.elementType : element.type,
    sourceId: element.sourceId?.value ?? "",
    targetId: element.targetId?.value ?? "",
    ...(element.sourceAnchor !== "" ? { sourceAnchor: element.sourceAnchor } : {}),
    ...(element.targetAnchor !== "" ? { targetAnchor: element.targetAnchor } : {}),
  };
}

/**
 * A whole `DiagramModel` from wire elements, split into nodes and connections by their ends.
 *
 * `payloadOf` is the one thing a module still supplies, and it decodes rather than maps: the
 * module knows how to read its own `Any`, and nothing else here does. A connection whose ends
 * have not both been delivered is still emitted — the canvas already declines to draw a
 * connector to an element it does not hold, and dropping it here would hide a delivery order
 * from the module that might legitimately resolve on the next delta.
 */
export function structuralModelOf(
  elements: Iterable<Element>,
  payloadOf?: (element: Element) => unknown,
): DiagramModel {
  const nodes: DiagramModelElement[] = [];
  const connections: DiagramModelConnection[] = [];

  for (const element of elements) {
    if (isConnectionElement(element)) {
      connections.push(connectionOf(element));
    } else {
      nodes.push(elementOf(element, payloadOf?.(element)));
    }
  }

  return { elements: nodes, connections };
}
