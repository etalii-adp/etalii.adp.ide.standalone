import { describe, expect, it } from "vitest";
import disText from "../definition/mindmap.dis?raw";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition } from "@client/canvas/library/definition/diagramDefinition";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { MINDMAP_DEFINITION } from "./MindmapCanvas";
import { MINDMAP_BINDINGS } from "./mindmapBindings";

/**
 * The mind map's canvas definition, compiled from its bundled DISL specification, is the definition
 * the module stated by hand until the switch-over.
 *
 * <b>The oracle below is that hand-written definition, moved here unchanged</b> from
 * `MindmapCanvas.tsx` (`definitionOf`), but for the action ids: the library dispatches each action by
 * its `backendKey`, and the module listens for none by id, so the compiled actions carry the wire ids
 * of the specification's `x-mindmap` block (`mindmap.add-child`), where the hand-written ones were
 * local names (`add-child`). Nothing a user sees or the backend receives depends on that id.
 */
const HAND_WRITTEN: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    {
      id: "node",
      shape: "centered-box",
      classNames: [
        { className: "mindmap-node", on: "element" },
        { className: "mindmap-node-dragging", on: "element", when: { path: "state.dragging", is: "true" } },
        { className: "mindmap-node-box" },
      ],
      labels: [
        {
          text: { path: "payload.text" },
          editable: true,
          className: "mindmap-node-label",
        },
        {
          text: { path: "payload.indicators" },
          when: { path: "payload.indicators", is: "non-empty" },
          anchorTo: "top",
          offset: { x: 0, y: 12 },
          align: "end",
          insetX: 4,
          className: "mindmap-node-label mindmap-node-indicators",
        },
      ],
      anchors: { kind: "edge", edgeSides: "horizontal" },
      sizing: "model",
    },
  ],
  relationTypes: [
    {
      id: "branch",
      route: "cubic-bezier",
      style: { endMarker: "none" },
      lineClassName: "mindmap-edge",
      selectable: false,
      endpoints: {
        source: { elementTypes: ["node"] },
        target: { elementTypes: ["node"], anchors: "edge" },
        allowSelf: false,
      },
    },
  ],
  actions: [
    {
      id: "mindmap.add-child",
      backendKey: "Insert",
      invokedBy: [
        { kind: "shortcut", key: "Insert" },
        { kind: "shortcut", key: "Tab" },
      ],
      appliesTo: [{ kind: "element" }],
    },
    { id: "mindmap.add-sibling", backendKey: "Enter", invokedBy: [{ kind: "shortcut", key: "Enter" }], appliesTo: [{ kind: "element" }] },
    { id: "mindmap.rename", backendKey: "F2", invokedBy: [{ kind: "shortcut", key: "F2" }], appliesTo: [{ kind: "element" }] },
    { id: "mindmap.toggle-fold", backendKey: " ", invokedBy: [{ kind: "shortcut", key: " " }], appliesTo: [{ kind: "element" }] },
    { id: "mindmap.delete", backendKey: "Delete", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }] },
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
  dropTarget: {
    parentPath: "payload.parentId",
    group: { className: "mindmap-drag-preview", data: { testid: "mindmap-drag-preview" } },
    ring: { className: "mindmap-node-drop-target-ring", data: { testid: "mindmap-drop-ring" } },
    preview: { className: "mindmap-edge mindmap-edge-preview" },
  },
});

/** A definition with its actions in id order: the library matches an action by its key or gesture, never by its place in the list. */
function byActionId(definition: DiagramDefinition): DiagramDefinition {
  return { ...definition, actions: [...(definition.actions ?? [])].sort((a, b) => a.id.localeCompare(b.id)) };
}

describe("the mind map's compiled definition", () => {
  it("is the hand-written definition it replaced, to the last key", () => {
    const compiled = assertValidDiagramDefinition(compileNotation(parseDisl(disText), MINDMAP_BINDINGS));

    expect(byActionId(compiled)).toEqual(byActionId(HAND_WRITTEN));
  });

  it("is what the canvas draws", () => {
    expect(byActionId(MINDMAP_DEFINITION)).toEqual(byActionId(HAND_WRITTEN));
  });
});
