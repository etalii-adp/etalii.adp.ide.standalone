import { useMemo, useState } from "react";

import { elementSourceOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useViewReport } from "@client/diagrams/useViewReport";
import type { C4Model, C4Node, C4BoundaryBox } from "./c4Model";
import { useC4Stream } from "./useC4Stream";

/**
 * Where an element's name sits inside its box, in canvas units: `StyledBoxElement` draws the
 * name's baseline 22 below the box's top, so a 20-tall editor starting 6 below the top covers
 * that line and nothing else. The inset label rule carries these as definition data.
 */

export interface C4CanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}

/** An element as the library carries it here: the model element plus the node it draws. */
type C4NodeElement = DiagramModelElement & { node: C4Node };
type C4BoundaryElement = DiagramModelElement & { boundary: C4BoundaryBox };



/**
 * How many characters of the card's two small lines fit across it. The type line and the
 * description are drawn at 10px (c4.css); `C4Metrics` in the module's backend estimates 0.55 of
 * the font size per character and pads 12 either side. The browser draws these lines nearer
 * 0.48, so the estimate errs towards fitting. It sizes cards at 14px, not 10, so a description
 * wrapped here always takes at most the lines the backend made the card tall for.
 */
function charactersAcross(width: number): number {
  return Math.max(1, Math.floor((width - 2 * 12) / (10 * 0.55)));
}

/**
 * The type line as drawn: whole when it fits, cut with an ellipsis when it does not. The
 * backend clamps a card at 240 wide however long the technology is, and reserves one line.
 */
function typeLineFitted(typeLine: string, width: number): string {
  const across = charactersAcross(width);
  return typeLine.length <= across ? typeLine : `${typeLine.slice(0, across - 1)}…`;
}

/**
 * A description broken into lines at spaces, each no wider than {@link charactersAcross}. A
 * single word longer than a line is cut, since nothing else keeps it inside the card.
 */
function descriptionLines(description: string, width: number): string[] {
  const perLine = charactersAcross(width);
  const lines: string[] = [];
  let current = "";
  for (let word of description.split(/\s+/).filter((part) => part.length > 0)) {
    if (current.length > 0 && current.length + 1 + word.length <= perLine) {
      current = `${current} ${word}`;
      continue;
    }
    if (current.length > 0) {
      lines.push(current);
    }
    while (word.length > perLine) {
      lines.push(word.slice(0, perLine));
      word = word.slice(perLine);
    }
    current = word;
  }
  if (current.length > 0) {
    lines.push(current);
  }
  return lines;
}

/**
 * What a C4 view allows, stated once: element cards that drag, rename their NAME line in
 * place and speak F2/Delete/Insert; boundaries that enclose and ignore every gesture; one
 * dashed, arrowed, labelled relationship whose editor opens with the authored description
 * rather than the drawn "description [technology]" string. Everything drawn was decided by
 * the backend - the canvas stays a renderer, not a second opinion about what C4 is.
 */
const C4_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    {
      id: "element",
      // The shared styled box, with the silhouette and the palette the DOCUMENT names: a C4
      // author sets an element's shape and colours, so both are bound rather than fixed on the
      // type. That is the distinction `boundStyle` exists for.
      shape: "styled-box",
      boundStyle: {
        silhouette: { path: "payload.shape" },
        fill: { path: "payload.background" },
        labelColor: { path: "payload.color" },
      },
      classNames: [
        { className: "c4-node" },
        { className: "c4-node-dragging", when: { path: "state.dragging", is: "true" } },
      ],
      labels: [
        {
          // The three lines the card draws, pinned to its TOP so they stay put however tall it
          // is - which is what `anchorTo` exists for, and what a centre offset cannot say.
          text: { path: "payload.name" },
          anchorTo: "top",
              offset: { x: 0, y: 22 },
          editable: true,
          className: "c4-node-name",
        },
        {
          text: { path: "payload.typeLine" },
          anchorTo: "top",
          offset: { x: 0, y: 38 },
          when: { path: "payload.typeLine", is: "non-empty" },
          className: "c4-node-type",
        },
        {
          // The description, on the lines the backend made the card tall enough for. It was
          // one line, so anything longer than the card ran out of both sides - the backend
          // sized for a wrap nobody drew. 14 apart: the description's 10px at C4Metrics' 1.4.
          text: { path: "payload.descriptionLines", each: { path: "text" } },
          anchorTo: "top",
          offset: { x: 0, y: 58 },
          stack: { lineHeight: 14 },
          when: { path: "payload.descriptionLines", is: "non-empty" },
          className: "c4-node-description",
        },
      ],
      accessibility: { role: "button", label: { path: "payload.name" } },
      anchors: { kind: "edge" },
      sizing: "model",
    },
    {
      id: "boundary",
      // The dashed rectangle around a system's containers - a frame with one composed label.
      shape: "frame",
      // On the ELEMENT rather than the shape: a boundary is its frame AND its label, and the
      // test that reads its text reads the whole thing. A card is the other way round, because
      // what a drag moves is the styled box's own group.
      classNames: [
        { className: "c4-boundary", on: "element" },
        // The dashed outline's own class, so the stylesheet reaches the frame and nothing else
        // the library draws in the boundary's group (client-centralization Requirement 3.1).
        { className: "c4-boundary-outline" },
      ],
      labels: [{ text: { template: "{payload.name} [{payload.kind}]" }, placement: "above", className: "c4-boundary-label" }],
      anchors: { kind: "edge" },
      sizing: "model",
      draggable: false,
      // A boundary is read, never picked: a press on it is a press on the background, as it
      // always was here - the frame never had a hit surface of its own. Declared rather than
      // checked by hand in a selection handler (centralized-selection Requirement 2.3).
      selectable: false,
    },
  ],
  relationTypes: [
    {
      id: "relationship",
      route: "straight",
      style: { endMarker: "arrow" },
      label: { placement: "midpoint", offset: -6, editable: true },
      className: "c4-relationship-group",
      lineClassName: "c4-relationship-line",
      endpoints: {
        source: { elementTypes: ["element"] },
        target: { elementTypes: ["element"], anchors: "edge" },
        allowSelf: false,
      },
    },
  ],
  // WHAT THIS TYPE OFFERS, AND WHAT INVOKES IT. The key list was hand-written in this canvas
  // and the delete was a keystroke it built to describe a gesture the library had already
  // handed it. Declared, the library derives the key set and dispatches an action id.
  actions: [
    { id: "rename", backendKey: "F2", invokedBy: [{ kind: "shortcut", key: "F2" }], appliesTo: [{ kind: "element" }] },
    { id: "insert", backendKey: "Insert", invokedBy: [{ kind: "shortcut", key: "Insert" }], appliesTo: [{ kind: "element" }] },
    { id: "delete", backendKey: "Delete", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }, { kind: "connection" }] },
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
});

/**
 * Renders one C4 view, through the diagram library. Everything it draws was decided by the
 * backend - the boxes at the sizes it measured, the palette it resolved, the title and the
 * key it composed - and every gesture answers with the same backend calls the hand-built
 * canvas made: a drag as `moveElementTo`, a drop as the entry's own action with the target
 * element as parent, the keys as data against the selection.
 */
export function C4Canvas({ projectId, entryId, path }: C4CanvasProps) {
  const { model, loading, failed, reportView, moveElementTo } = useC4Stream(projectId, path);
  const { executeAction } = useContextConnection();
  const toolboxItems = useToolboxItems(projectId, path);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const diagramModel = useMemo<DiagramModel>(() => {
    const boundaries = [...model.boundaries.values()].map((boundary): C4BoundaryElement => ({
      id: boundary.id,
      type: "boundary",
      x: boundary.x,
      y: boundary.y,
      width: boundary.payload.width,
      height: boundary.payload.height,
      label: `${boundary.payload.name} [${boundary.payload.kind}]`,
      payload: { name: boundary.payload.name, kind: boundary.payload.kind },
      boundary,
    }));
    const nodes = [...model.nodes.values()].map((node): C4NodeElement => ({
      id: node.id,
      type: "element",
      x: node.x,
      y: node.y,
      width: node.payload.width,
      height: node.payload.height,
      label: node.payload.name,
      // What the declaration reads: the document's own palette and the card's three lines.
      payload: {
        name: node.payload.name,
        typeLine: typeLineFitted(node.payload.typeLine, node.payload.width),
        descriptionLines: descriptionLines(node.payload.description, node.payload.width).map((text) => ({ text })),
        shape: node.payload.style?.shape ?? "RoundedBox",
        background: node.payload.style?.background ?? "#1168bd",
        color: node.payload.style?.color ?? "#ffffff",
      },
      node,
    }));
    const relationships = [...model.relationships.values()].map((relationship) => {
      const p = relationship.payload;
      const label = p.technology ? `${p.description} [${p.technology}]` : p.description;
      return {
        id: relationship.id,
        type: "relationship",
        sourceId: p.sourceId,
        targetId: p.destinationId,
        // The drawn string decorates the one authored value; the editor opens with the value.
        label: label ? (p.interactionOrder ? `${p.interactionOrder}. ${label}` : label) : undefined,
        editValue: p.description,
      };
    });
    // Boundaries first, so everything they enclose draws on top of them.
    return { elements: [...boundaries, ...nodes], connections: relationships };
  }, [model]);

  // Every refusal - a move, a drop, a declared keystroke the library sends - is shown on the one
  // line the library draws around every canvas, by the call that got it (client-centralization
  // Requirement 2), and the next gesture clears it.
  const events: DiagramEventHandlers = {
    // Selection is the library's (centralized-selection), and a boundary's inertness is its
    // type's `selectable: false` above.
    onElementMoved: ({ elementId, position }) => {
      if (!model.nodes.has(elementId)) {
        return;
      }
      // Nothing optimistic: the element stays where it was until the backend's delta says
      // otherwise, so what is drawn is always what was recorded. A refused move answers with
      // its sentence, shown like every other refusal rather than as a silent snap-back.
      void moveElementTo(elementId, position.x, position.y);
    },
    onElementDropped: ({ elementType, position }) => {
      // The entry carries the backend's own action id. Dropped on an element, that element
      // becomes the new one's parent - which is how C4's containment gets decided by the
      // gesture; on empty canvas only what stands alone can land, and the backend refuses
      // the rest with a sentence.
      const target = [...model.nodes.values()].reverse().find((node) => {
        const { width, height } = node.payload;
        return Math.abs(position.x - node.x) <= width / 2 && Math.abs(position.y - node.y) <= height / 2;
      });
      void executeAction(elementType, target !== undefined ? elementSourceOf(target.id) : undefined);
    },
    // Delete travels as the backend shortcut it always was, raised by the library's key path.
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


  // Opening, reconnecting and unavailable are the library's to say, in the frame around this canvas.
  return (
    <div className="c4-canvas" data-testid="c4-canvas">
      {/* C4 requires every diagram to carry a title describing its type and scope. */}
      {model.view && (
        <div className="c4-canvas-title" data-testid="c4-title">
          {model.view.title}
        </div>
      )}
      <DiagramCanvas
        definition={C4_DEFINITION}
        model={diagramModel}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        className="c4-canvas-host"
        scrollbarsClassName="c4-scrollbars"
        ariaLabel={model.view?.title ?? "C4 diagram"}
      />

      {/* C4 requires a key explaining every shape and colour the diagram uses, so it can be
          read without accompanying narrative. Built from what the backend actually drew. */}
      {model.view && model.view.legend.length > 0 && (
        <div className="c4-canvas-legend" data-testid="c4-legend">
          <span className="c4-legend-title">Key</span>
          {model.view.legend.map((entry) => (
            <span className="c4-legend-entry" key={entry.label}>
              <span
                className="c4-legend-swatch"
                style={{ background: entry.style?.background, borderColor: entry.style?.background }}
              />
              {entry.label}
            </span>
          ))}
        </div>
      )}
    </div>
  );
}

export type { C4Model };
