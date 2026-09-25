import { useMemo, useState } from "react";

import { forwardBezierPath } from "@client/canvas/connectors";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type {
  CustomRouteRef,
  DiagramDefinition,
  ShapeBounds,
  ShapePoint,
} from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useRegisterDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useViewReport } from "@client/diagrams/useViewReport";
import { AnsibleEdgeKind, AnsibleElementKind } from "@client/generated/ansible-structure_pb";
import { anchorsOf, edgesOf, nodesOf, paletteSlotOf, type AnsibleElement } from "./ansibleModel";
import { useAnsibleStream } from "./useAnsibleStream";

/** How many play colours the stylesheet defines. The palette itself is CSS's; this is its size. */
const PALETTE_SLOTS = 6;

/** How far an unresolved edge's stub reaches out of its source, in canvas units. */
const STUB_LENGTH = 48;

export interface AnsibleCanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}

/** An element as the library carries it here: the model element plus this canvas's closures. */
type NodeElement = DiagramModelElement & {
  node: AnsibleElement;
  activate: () => void;
};

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

/**
 * What this diagram allows, stated once: nodes that drag into the layout block and select,
 * stubs and edges that only render. No relation declares a source anchor and no type is
 * deletable, because this diagram edits nothing - its one edit is the drag, and its value is
 * the jump from a node to its file.
 */
const ANSIBLE_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    {
      id: "node",
      shape: "box",
      classNames: [
        { className: "ansible-node" },
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
        // The jump from a node to its file: a double-click, or Enter or Space on the SELECTED
        // node. The keys used to read a private `focusedId` the canvas kept beside the backend's
        // selection; declared here, they act on the library's selection, which is the only one
        // (centralized-selection Requirement 4.1). The right-click that sat beside them pushed a
        // context-menu selection by hand - selection glue, now the library's shared menu.
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
      // AN ELEMENT THAT IS ONLY AN ORNAMENT: a short rule out of the source with the
      // target's own words beside it, for a dependency this document names and does not
      // resolve. `none` is the shape that made this declarable at all.
      shape: "none",
      classNames: [{ className: { path: "payload.stubClass" }, on: "element" }],
      decorations: [
        {
          glyph: "line",
          // Canvas units: these read `bounds`, which resolves to a position rather than an offset.
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

/**
 * Renders an Ansible project's structure and reports what the user selects - drawn through
 * the central canvas library.
 *
 * It holds no document state and offers no edit: there is no toolbox drop, no context menu of
 * commands and no keyboard mutation, because this diagram type has none of those to offer.
 * What it does have is the jump from a node to its file, which is most of the value
 * (Requirement 8.1), bound to the double-click and the keyboard alike.
 */
export function AnsibleCanvas({ projectId, entryId, path }: AnsibleCanvasProps) {
  const { model, loading, failed, reportView, moveElementTo } = useAnsibleStream(projectId, path);

  // This type's palette is empty by design - the module registers no toolbox provider,
  // because it edits nothing. Registering the backend's empty answer makes the panel say
  // exactly that, instead of claiming no diagram is open - registered HERE as well as by
  // the library canvas, because the empty state returns before the canvas mounts.
  const toolboxItems = useToolboxItems(projectId, path);
  useRegisterDiagramToolbox(toolboxItems);
  const { revealPath } = useContextConnection();

  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const nodes = useMemo(() => nodesOf(model), [model]);
  const edges = useMemo(() => edgesOf(model), [model]);

  const activate = (element: AnsibleElement) => {
    const segments = element.payload.projectRelativePath;
    if (segments.length > 0) {
      revealPath([...path.slice(0, -1), ...segments]);
    }
  };

  const diagramModel = useMemo<DiagramModel>(() => {
    const elements: DiagramModelElement[] = [];
    const connections = [];

    // Stubs first, so a node its label happens to cross still draws over it.
    for (const edge of edges) {
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
        elements.push({
          id: edge.id,
          type: "stub",
          x: from.x + STUB_LENGTH / 2,
          y: from.y,
          width: STUB_LENGTH,
          height: 12,
          from,
          text: `${wire.targetAsWritten}${edge.payload.unresolvable ? " (expression)" : " (missing)"}`,
          stubClass: `ansible-edge ansible-edge-${edgeClass(wire.kind)} ansible-edge-unresolved`,
          payload: {
            text: `${wire.targetAsWritten}${edge.payload.unresolvable ? " (expression)" : " (missing)"}`,
            stubClass: `ansible-edge ansible-edge-${edgeClass(wire.kind)} ansible-edge-unresolved`,
          },
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

    for (const node of nodes) {
      elements.push({
        id: node.id,
        type: "node",
        x: node.x + node.payload.width / 2,
        y: node.y + node.payload.height / 2,
        width: node.payload.width,
        height: node.payload.height,
        label: node.payload.name,
        // What the declaration reads. The palette slot is `playIndex % PALETTE_SLOTS`, which a
        // binding now computes; it is carried here as the index the module already holds.
        payload: {
          name: node.payload.name,
          kindClass: kindClass(node.payload.kind),
          hollow: node.payload.hollow,
          title: node.payload.hosts
            ? `${kindLabel(node.payload.kind)} ${node.payload.name} — hosts: ${node.payload.hosts}`
            : `${kindLabel(node.payload.kind)} ${node.payload.name}`,
          ...(paletteSlotOf(node, PALETTE_SLOTS) >= 0 ? { playSlot: paletteSlotOf(node, PALETTE_SLOTS) } : {}),
        },
        node,
        activate: () => activate(node),
      } as NodeElement);
    }

    return { elements, connections };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the closures read stable setters
    // and the same model/path the listed dependencies cover.
  }, [model, nodes, edges, path]);

  const events: DiagramEventHandlers = {
    // The jump, reached by action id from the double-click and the keyboard alike: this module
    // still decides what "activate" means. Selection is the library's (centralized-selection).
    onActionInvoked: ({ actionId, targetId }) => {
      const node = targetId !== undefined ? model.elements.get(targetId) : undefined;
      if (actionId === "ansible.activate" && node !== undefined) {
        activate(node);
      }
    },
    onElementMoved: ({ elementId, position }) => {
      const node = model.elements.get(elementId);
      if (!node) {
        return;
      }
      // The write goes to the .adp's layout block; the change comes back through the folder's
      // own watcher, so nothing is echoed locally. A refusal is the backend's sentence, which the
      // move reports to the library's refusal line itself.
      void moveElementTo(elementId, position.x - node.payload.width / 2, position.y - node.payload.height / 2);
    },
    onViewChanged: ({ viewport: next }) => setViewport(next),
  };

  useViewReport({
    view: { x: viewport?.x ?? 0, y: viewport?.y ?? 0, w: viewport?.width ?? 0, h: viewport?.height ?? 0 },
    report: reportView,
    convert: () => ({
      minX: viewport?.x ?? 0,
      minY: viewport?.y ?? 0,
      maxX: (viewport?.x ?? 0) + (viewport?.width ?? 0),
      maxY: (viewport?.y ?? 0) + (viewport?.height ?? 0),
    }),
    ready: !loading && !failed && viewport !== null,
  });

  // Opening, reconnecting and unavailable are the library's to say, in the frame around this canvas;
  // the empty explanation is this module's own, and only true once the folder has been read.
  if (!loading && !failed && nodes.length === 0) {
    return (
      <div className="ansible-canvas-message canvas-host canvas-host-message">
        Nothing here is laid out the way Ansible expects, so there is nothing to draw. Playbooks at the folder root or
        under <code>playbooks/</code>, roles under <code>roles/</code>, inventories under <code>inventories/</code>.
      </div>
    );
  }

  return (
    <div className="ansible-canvas-host canvas-host" role="application" aria-label="Ansible project structure">
      <DiagramCanvas
        definition={ANSIBLE_DEFINITION}
        model={diagramModel}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        ariaLabel="Ansible project structure"
        className="ansible-canvas-viewport"
        scrollbarsClassName="ansible-scrollbars"
      />
    </div>
  );
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
