import { forwardBezierPath } from "@client/canvas/connectors";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { CustomRouteRef, DiagramDefinition, ShapePoint } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { AnsibleEdgeKind, AnsibleElementKind } from "@client/generated/ansible-structure_pb";
import { anchorsOf, edgesOf, nodesOf, paletteSlotOf, type AnsibleModel } from "./ansibleModel";

/**
 * THE ORACLE, AND TEST CODE ONLY: the canvas definition `AnsibleCanvas.tsx` stated by hand until it
 * was compiled from the bundled specification, moved here unchanged with the model it built, so
 * `ansibleCompiledDefinition.test.tsx` can hold the compiled drawing to it. Nothing in the product
 * imports this file.
 *
 * It had one `node` type told apart by a payload class, a `stub` element type the canvas built for
 * every unresolved target, and one `edge` relation told apart by classes on each connection - its
 * kind, and `ansible-edge-static` or `ansible-edge-dynamic` - which `ansible-structure.css` turned into
 * the lines' dashes. Those dashes are `ORACLE_DASHES` below, as the stylesheet stated them at the
 * switch-over, because the compiled definition paints them itself.
 */

/** How many play colours the stylesheet defines. The palette itself is CSS's; this is its size. */
const PALETTE_SLOTS = 6;

/** How far an unresolved edge's stub reaches out of its source, in canvas units. */
const STUB_LENGTH = 48;

/**
 * The dash `ansible-structure.css` gave each line the oracle drew, by the classes on it, in the
 * stylesheet's own order of precedence: an unresolved target over a dependency over a dynamic
 * relation. A static relation had `none`, which is no dash.
 */
export const ORACLE_DASHES: readonly { readonly className: string; readonly dash: string }[] = [
  { className: "ansible-edge-unresolved", dash: "3 3" },
  { className: "ansible-edge-depends-on", dash: "1 4" },
  { className: "ansible-edge-dynamic", dash: "5 4" },
];

/** An unresolved edge, drawn as a stub element: a line to nothing and the name that failed. */
type StubElement = DiagramModelElement & {
  from: ShapePoint;
  text: string;
  stubClass: string;
};

/**
 * One relationship, exactly as before: out of the source's right side, into the target's left
 * side, curving horizontally through the corridor between the columns. The forward bezier
 * loops around for the rare backward edge.
 */
const ansibleRoute: CustomRouteRef = {
  customRoute: "ansible-forward-bezier",
  path: (from, to, _waypoints, ends) => {
    const a = ends ? { x: ends.source.x + ends.source.width, y: ends.source.y + ends.source.height / 2 } : from;
    const b = ends ? { x: ends.target.x, y: ends.target.y + ends.target.height / 2 } : to;
    return forwardBezierPath(a, b);
  },
};

export const HAND_WRITTEN_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    {
      id: "node",
      shape: "box",
      classNames: [
        // The whole element, labels and all: the group the library makes focusable and hovers, which
        // the hover and focus rules anchor on. Every other class here lands on the box itself.
        { className: "ansible-node", on: "element" },
        { className: { template: "ansible-node-{payload.kindClass}" } },
        { className: { template: "ansible-play-{payload.playSlot}" }, when: { path: "payload.playSlot", is: "present" } },
        { className: "ansible-play-none", when: { path: "payload.playSlot", is: "absent" } },
        { className: "ansible-node-hollow", when: { path: "payload.hollow", is: "true" } },
        { className: "ansible-node-box", on: "shape" },
      ],
      labels: [
        {
          text: { path: "payload.name" },
          truncate: true,
          className: "ansible-node-label",
        },
      ],
      tooltip: { template: "{payload.title}" },
      data: { kind: { path: "payload.kindClass" } },
      accessibility: { role: "button", focusable: true, label: { path: "payload.title" } },
      actions: [
        {
          id: "ansible.activate",
          invokedBy: [
            { kind: "gesture", gesture: "activate" },
            { kind: "shortcut", key: "Enter" },
            { kind: "shortcut", key: " " },
          ],
          appliesTo: [{ kind: "element" }],
        },
      ],
      anchors: { kind: "edge" },
      sizing: "model",
      deletable: false,
    },
    {
      id: "stub",
      shape: "none",
      classNames: [{ className: { path: "payload.stubClass" }, on: "element" }],
      decorations: [
        {
          glyph: "line",
          anchor: "canvas",
          from: { x: { path: "bounds.left" }, y: { path: "bounds.centreY" } },
          to: { x: { path: "bounds.right" }, y: { path: "bounds.centreY" } },
          className: "ansible-edge-line",
        },
      ],
      labels: [
        {
          text: { path: "payload.text" },
          anchorTo: "top",
          offset: { x: 0, y: 0 },
          align: "start",
          insetX: 8,
          className: "ansible-edge-label",
        },
      ],
      data: { "edge-id": { path: "element.id" } },
      anchors: { kind: "edge" },
      sizing: "model",
      draggable: false,
      deletable: false,
    },
  ],
  relationTypes: [
    {
      id: "edge",
      route: ansibleRoute,
      style: { endMarker: "arrow" },
      label: { placement: "midpoint", offset: -6 },
      className: "ansible-edge",
      lineClassName: "ansible-edge-line",
      endpoints: {
        source: { elementTypes: ["node"], anchors: [] },
        target: { elementTypes: ["node"], anchors: "edge" },
        allowSelf: false,
      },
    },
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
});

/** The model the canvas built for the hand-written definition, from the same streamed model. */
export function handWrittenModel(model: AnsibleModel): DiagramModel {
  const elements: DiagramModelElement[] = [];
  const connections = [];

  // Stubs first, so a node its label happens to cross still draws over it.
  for (const edge of edgesOf(model)) {
    const wire = edge.payload.edge;
    if (!wire) {
      continue;
    }

    const anchors = anchorsOf(model, edge);
    if (!anchors) {
      const source = model.elements.get(wire.sourceId);
      if (!source) {
        continue;
      }

      const from = { x: source.x + source.payload.width, y: source.y + source.payload.height / 2 };
      const text = `${wire.targetAsWritten}${edge.payload.unresolvable ? " (expression)" : " (missing)"}`;
      const stubClass = `ansible-edge ansible-edge-${edgeClass(wire.kind)} ansible-edge-unresolved`;
      elements.push({
        id: edge.id,
        type: "stub",
        x: from.x + STUB_LENGTH / 2,
        y: from.y,
        width: STUB_LENGTH,
        height: 12,
        from,
        text,
        stubClass,
        payload: { text, stubClass },
      } as StubElement);
      continue;
    }

    connections.push({
      id: edge.id,
      type: "edge",
      sourceId: anchors.from.id,
      targetId: anchors.to.id,
      label: wire.condition ? `${wire.directive} when ${wire.condition}` : wire.directive,
      className: `ansible-edge-${edgeClass(wire.kind)} ${wire.dynamic ? "ansible-edge-dynamic" : "ansible-edge-static"}`,
    });
  }

  for (const node of nodesOf(model)) {
    elements.push({
      id: node.id,
      type: "node",
      x: node.x + node.payload.width / 2,
      y: node.y + node.payload.height / 2,
      width: node.payload.width,
      height: node.payload.height,
      label: node.payload.name,
      payload: {
        name: node.payload.name,
        kindClass: kindClass(node.payload.kind),
        hollow: node.payload.hollow,
        title: node.payload.hosts
          ? `${kindLabel(node.payload.kind)} ${node.payload.name} — hosts: ${node.payload.hosts}`
          : `${kindLabel(node.payload.kind)} ${node.payload.name}`,
        ...(paletteSlotOf(node, PALETTE_SLOTS) >= 0 ? { playSlot: paletteSlotOf(node, PALETTE_SLOTS) } : {}),
      },
    });
  }

  return { elements, connections };
}

function kindClass(kind: AnsibleElementKind): string {
  switch (kind) {
    case AnsibleElementKind.PLAYBOOK:
      return "playbook";
    case AnsibleElementKind.PLAY:
      return "play";
    case AnsibleElementKind.ROLE:
      return "role";
    case AnsibleElementKind.TASK_FILE:
      return "taskfile";
    case AnsibleElementKind.INVENTORY:
      return "inventory";
    case AnsibleElementKind.VARIABLE_FOLDER:
      return "vars";
    default:
      return "unknown";
  }
}

function kindLabel(kind: AnsibleElementKind): string {
  switch (kind) {
    case AnsibleElementKind.PLAYBOOK:
      return "Playbook";
    case AnsibleElementKind.PLAY:
      return "Play";
    case AnsibleElementKind.ROLE:
      return "Role";
    case AnsibleElementKind.TASK_FILE:
      return "Task file";
    case AnsibleElementKind.INVENTORY:
      return "Inventory";
    case AnsibleElementKind.VARIABLE_FOLDER:
      return "Variables";
    default:
      return "Element";
  }
}

function edgeClass(kind: AnsibleEdgeKind): string {
  switch (kind) {
    case AnsibleEdgeKind.USES_ROLE:
      return "uses-role";
    case AnsibleEdgeKind.IMPORTS_PLAYBOOK:
      return "imports-playbook";
    case AnsibleEdgeKind.INCLUDES_TASKS:
      return "includes-tasks";
    case AnsibleEdgeKind.DEPENDS_ON:
      return "depends-on";
    case AnsibleEdgeKind.TARGETS:
      return "targets";
    default:
      return "unknown";
  }
}
