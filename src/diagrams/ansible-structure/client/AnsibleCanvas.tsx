import { useMemo, useState } from "react";

import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { EmptyCanvas } from "@client/canvas/library/surface/EmptyCanvas";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useRegisterDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useViewReport } from "@client/diagrams/useViewReport";
import { AnsibleEdgeKind, AnsibleElementKind } from "@client/generated/ansible-structure_pb";
import disText from "../definition/ansible-structure.dis?raw";
import { ANSIBLE_BINDINGS } from "./ansibleBindings";
import { anchorsOf, edgesOf, nodesOf, type AnsibleElement } from "./ansibleModel";
import { useAnsibleStream } from "./useAnsibleStream";

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

/**
 * What this diagram allows, compiled from the bundled specification the backend runs: nodes that
 * drag into the layout block and select, relations that only render, and an unresolved target drawn
 * as its relation's stub. No relation declares a source anchor and no type is deletable, because this
 * diagram edits nothing - its one edit is the drag, and its value is the jump from a node to its file.
 * What the library cannot read from the specification is `ansibleBindings.ts`.
 */
export const ANSIBLE_DEFINITION: DiagramDefinition = assertValidDiagramDefinition(compileNotation(parseDisl(disText), ANSIBLE_BINDINGS));

/** The operation a double-click, Enter and Space run on a node (`behavior.operations` of the specification). */
const REVEAL = "revealFile";

/**
 * Renders an Ansible project's structure and reports what the user selects - drawn through
 * the central canvas library.
 *
 * It holds no document state and offers no edit: there is no toolbox drop, no context menu of
 * commands and no keyboard mutation, because this diagram type has none of those to offer.
 * What it does have is the jump from a node to its file, which is most of the value
 * (Requirement 8.1), bound to the double-click and the keyboard alike. The handler that makes
 * the jump stays here, standing in for gap 9 of `ansible-structure.md`.
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
    const connections: DiagramModelConnection[] = [];
    for (const edge of edges) {
      const wire = edge.payload.edge;
      if (!wire || !model.elements.has(wire.sourceId)) {
        continue;
      }

      const anchors = anchorsOf(model, edge);
      connections.push({
        id: edge.id,
        type: relationTypeOf(wire.kind),
        sourceId: wire.sourceId,
        // A target that is missing, an expression or not in view has no end: the library draws
        // it as the relation's stub.
        targetId: anchors ? anchors.to.id : "",
        // The directive and its condition (the label's `cel`, composed here).
        label: wire.condition ? `${wire.directive} when ${wire.condition}` : wire.directive,
        // What the line's dash reads (`self.dynamic`), and the stub's words.
        payload: {
          dynamic: wire.dynamic,
          text: `${wire.targetAsWritten}${edge.payload.unresolvable ? " (expression)" : " (missing)"}`,
        },
      });
    }

    const elements = nodes.map((node): NodeElement => {
      const kind = kindOf(node.payload.kind);
      return {
        id: node.id,
        type: kind.type,
        x: node.x + node.payload.width / 2,
        y: node.y + node.payload.height / 2,
        width: node.payload.width,
        height: node.payload.height,
        label: node.payload.name,
        // What the compiled declaration reads: the tooltip's `cel`, composed here, and the play
        // index the colour is chosen by (`self.playIndex % 6`), which a play of no playbook lacks.
        payload: {
          name: node.payload.name,
          hollow: node.payload.hollow,
          title: node.payload.hosts ? `${kind.label} ${node.payload.name} — hosts: ${node.payload.hosts}` : `${kind.label} ${node.payload.name}`,
          ...(node.payload.playIndex >= 0 ? { playIndex: node.payload.playIndex } : {}),
        },
        node,
        activate: () => activate(node),
      };
    });

    return { elements, connections };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the closures read stable setters
    // and the same model/path the listed dependencies cover.
  }, [model, nodes, edges, path]);

  const events: DiagramEventHandlers = {
    // The jump, reached by action id from the double-click and the keyboard alike: this module
    // still decides what "reveal" means. Selection is the library's (centralized-selection).
    onActionInvoked: ({ actionId, targetId }) => {
      const node = targetId !== undefined ? model.elements.get(targetId) : undefined;
      if (actionId === REVEAL && node !== undefined) {
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
      // Returned, so a refusal lets the library put the element back where it was (ruled 2026-10-19).
      return moveElementTo(elementId, position.x - node.payload.width / 2, position.y - node.payload.height / 2);
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
  // the empty sentence is the specification's (`canvas.empty`), and only true once the folder has been read.
  if (!loading && !failed && nodes.length === 0) {
    return <EmptyCanvas definition={ANSIBLE_DEFINITION} className="ansible-canvas-message" />;
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

/** The node type an element of the wire is - the specification's type name in lower case - and how its tooltip names it. */
function kindOf(kind: AnsibleElementKind): { type: string; label: string } {
  switch (kind) {
    case AnsibleElementKind.PLAYBOOK:
      return { type: "playbook", label: "Playbook" };
    case AnsibleElementKind.PLAY:
      return { type: "play", label: "Play" };
    case AnsibleElementKind.ROLE:
      return { type: "role", label: "Role" };
    case AnsibleElementKind.TASK_FILE:
      return { type: "taskfile", label: "Task file" };
    case AnsibleElementKind.INVENTORY:
      return { type: "inventory", label: "Inventory" };
    case AnsibleElementKind.VARIABLE_FOLDER:
      return { type: "variablefolder", label: "Variables" };
    default:
      return { type: "unknown", label: "Element" };
  }
}

/** The relation type an edge of the wire is: the specification's relation name in lower case. */
function relationTypeOf(kind: AnsibleEdgeKind): string {
  switch (kind) {
    case AnsibleEdgeKind.USES_ROLE:
      return "usesrole";
    case AnsibleEdgeKind.IMPORTS_PLAYBOOK:
      return "importsplaybook";
    case AnsibleEdgeKind.INCLUDES_TASKS:
      return "includestasks";
    case AnsibleEdgeKind.DEPENDS_ON:
      return "dependson";
    case AnsibleEdgeKind.TARGETS:
      return "targets";
    default:
      return "unknown";
  }
}
