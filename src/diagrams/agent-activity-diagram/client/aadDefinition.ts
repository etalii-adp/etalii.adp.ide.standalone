import type { DiagramDefinition } from "@client/canvas/library/definition/diagramDefinition";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import { compileNotation, type NotationBindings } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import disText from "../definition/agent-activity-diagram.dis?raw";

/**
 * What the agent activity diagram allows: compiled from the bundled DISL specification
 * (`definition/agent-activity-diagram.dis`), the same bytes the backend loads and every other host
 * reads. Nothing about the notation is restated here; what follows is only what the library cannot
 * read from a specification, which is {@link AAD_BINDINGS}.
 */

/** The bundled specification, read by Vite as text. */
const AAD_SPEC = parseDisl(disText);

/** The CEL a node's label binds, as the specification writes it - so no expression is copied here. */
function labelCel(node: string, label: string): string {
  const text = AAD_SPEC.notation.nodes[node]?.labels?.find((candidate) => candidate.id === label)?.text;
  if (typeof text !== "object" || text === null || !("cel" in text)) {
    throw new Error(`The agent activity specification's ${node} has no CEL label "${label}".`);
  }

  return text.cel;
}

/**
 * What the library cannot read from the specification.
 *
 * <b>The payload paths of what the backend computes.</b> The library has no CEL: a status in words,
 * an environment's kind in words and a location's folder as its one-line list each arrive on the
 * payload, and the expression that asks for one is mapped to where it is.
 *
 * <b>The classes the stylesheet paints</b>, by kind of element and by a label's style.
 *
 * <b>Two shapes the library's DISL catalog does not draw</b>: `folder`, which stands in as a
 * superellipse, the one shape no other kind has, and `roundedRect`, which the library draws under
 * its own name. `aadDefinition.test.ts` names both as debts and fails when the catalog draws them.
 *
 * <b>The connect gesture</b>, which the specification leaves to the host: a right drag from an
 * element's body, as this host's other diagrams with unmarked anchors draw a relation.
 */
export const AAD_BINDINGS: NotationBindings = {
  celPaths: {
    [labelCel("Specification", "status")]: "payload.statusLabel",
    [labelCel("Environment", "environmentKind")]: "payload.kindLabel",
    "[self.folder]": "payload.folderRows",
  },
  rowPaths: { item: "title", "self.folderLink": "link" },
  celConditions: { "self.view.pinned": { path: "payload.pinned", is: "true" } },
  classNames: (type) => [{ className: `aad-${type.toLowerCase()}` }],
  // Every label names its colour by a class: an SVG text with none is black, whatever the theme.
  labelClassName: (_type, label) =>
    label.style === "status" ? { template: "aad-status aad-status-{payload.status}" } : label.style === "muted" ? "aad-detail" : "aad-label",
  badgeClassName: () => "aad-lock",
  relationClassName: () => "aad-relation",
  builtInShapes: { folder: "superellipse", roundedRect: "rounded-rectangle" },
  extras: () => ({ connectOnRightDrag: true }),
};

export const AAD_DEFINITION: DiagramDefinition = assertValidDiagramDefinition(compileNotation(AAD_SPEC, AAD_BINDINGS));

/**
 * Which relation joins two kinds of element, keyed `<source>><target>` by the kind at each end: read
 * from the specification's relations, each of which joins one pair of types. On the wire a relation
 * carries its two ends and no type, so the canvas names it from its ends.
 */
export const AAD_RELATION_BY_ENDS: ReadonlyMap<string, string> = new Map(
  Object.entries(AAD_SPEC.metamodel.relations ?? {}).map(([name, relation]): [string, string] =>
    [`${String(relation.source).toLowerCase()}>${String(relation.target).toLowerCase()}`, name.toLowerCase()]),
);
