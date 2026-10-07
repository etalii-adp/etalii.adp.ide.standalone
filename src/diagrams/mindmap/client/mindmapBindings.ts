import type { ActionDeclaration } from "@client/canvas/library/definition/actions";
import type { ClassDeclaration } from "@client/canvas/library/definition/diagramDefinition";
import type { NotationBindings } from "@client/canvas/library/disl/compileNotation";

/**
 * What the mind map's canvas needs beside its bundled specification, because the library cannot read
 * it from there. `compileNotation` derives everything else - the node's shape, labels, anchors and
 * size, the branch's route and ends, the shortcuts and the manual layout - from
 * `definition/mindmap.dis`.
 *
 * <b>Why each entry is code.</b>
 * - **The CEL path.** The corner glyphs are `indicators(self)`: the library has no CEL, so the canvas
 *   computes them into the payload as `indicators` (`MindmapCanvas`), as the definition's function
 *   does, and this table names that path.
 * - **The class names.** They tie the drawing to `mindmap.css`.
 * - **The topic box.** `topicBox` is drawn as the library's `centered-box`, which
 *   `mindmapShapeGeometry.test.ts` proves is the same geometry.
 * - **A branch's end at the child.** It leaves the child's facing side, which is the library's `edge`
 *   anchor rule; DISL's derived relation states no anchoring for it.
 * - **The drag preview.** What a drop onto another node means is the module's (`onElementMoved`); the
 *   library draws the candidate parent from the parent link this names, which DISL has no construct for.
 * - **The keys the backend is sent, and Tab.** The backend resolves a mind map action by its key, so
 *   each action sends the key its menu entry writes; and Tab, the XMind convention, adds a child as
 *   Insert does - a second key DISL's one `shortcut` per entry cannot state.
 */

const NODE_CLASSES: readonly ClassDeclaration[] = [
  { className: "mindmap-node", on: "element" },
  { className: "mindmap-node-dragging", on: "element", when: { path: "state.dragging", is: "true" } },
  // The box's own class, so the stylesheet reaches the box and nothing else the library draws in the
  // node's group (59f1ed3a; client-centralization Requirement 3.2).
  { className: "mindmap-node-box" },
];

const LABEL_CLASSES: Readonly<Record<string, string>> = {
  text: "mindmap-node-label",
  indicators: "mindmap-node-label mindmap-node-indicators",
};

/** The add-child action's second key. */
const ADD_CHILD = "mindmap.add-child";

/** An action sending the key its menu entry writes, with Tab added to add-child. */
function dispatched(action: ActionDeclaration, entry: { shortcut?: string }): ActionDeclaration {
  const withKey = entry.shortcut !== undefined ? { ...action, backendKey: entry.shortcut } : action;
  if (action.id !== ADD_CHILD) {
    return withKey;
  }

  return { ...withKey, invokedBy: [...withKey.invokedBy, { kind: "shortcut", key: "Tab" }] };
}

export const MINDMAP_BINDINGS: NotationBindings = {
  wireIds: "x-mindmap",
  celPaths: { "indicators(self)": "payload.indicators" },
  classNames: () => NODE_CLASSES,
  labelClassName: (_type, label) => LABEL_CLASSES[label.id],
  relationLineClassName: () => "mindmap-edge",
  endpointAnchors: (_relation, end) => (end === "target" ? "edge" : undefined),
  customShapes: { topicBox: { shape: "centered-box" } },
  action: dispatched,
  extras: () => ({
    dropTarget: {
      parentPath: "payload.parentId",
      group: { className: "mindmap-drag-preview", data: { testid: "mindmap-drag-preview" } },
      ring: { className: "mindmap-node-drop-target-ring", data: { testid: "mindmap-drop-ring" } },
      preview: { className: "mindmap-edge mindmap-edge-preview" },
    },
  }),
};
