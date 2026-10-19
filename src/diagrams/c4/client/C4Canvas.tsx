import { useMemo, useState } from "react";
import { placementId } from "@client/canvas/gestureIds";
import { elementSourceOf } from "@client/canvas/selection";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { C4ElementKindProto } from "@client/generated/c4_pb";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useViewReport } from "@client/diagrams/useViewReport";
import component from "../definition/c4-component.dis?raw";
import container from "../definition/c4-container.dis?raw";
import context from "../definition/c4-context.dis?raw";
import deployment from "../definition/c4-deployment.dis?raw";
import dynamic from "../definition/c4-dynamic.dis?raw";
import systemLandscape from "../definition/c4-system-landscape.dis?raw";
import { BOUNDARY_TYPE, C4_BINDINGS } from "./c4Bindings";
import type { C4Model } from "./c4Model";
import { useC4Stream } from "./useC4Stream";

export interface C4CanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}

/** What the backend begins a boundary's id with (`C4Boundary.IdPrefix`); the id of the element it is drawn for follows. */
const BOUNDARY_ID_PREFIX = "boundary:";

/** The view kinds the backend names (`C4ViewKind`), each with the specification of its diagram type. */
const SPECIFICATIONS: Readonly<Record<string, string>> = {
  SystemLandscape: systemLandscape,
  SystemContext: context,
  Container: container,
  Component: component,
  Dynamic: dynamic,
  Deployment: deployment,
};

/**
 * The canvas definition of each view kind, compiled from its bundled specification at module load (c4-disl-fbl
 * Requirement 9), with the boundary the host draws around a view's scope beside the specification's types.
 */
export const C4_DEFINITIONS: Readonly<Record<string, DiagramDefinition>> = Object.fromEntries(
  Object.entries(SPECIFICATIONS).map(([viewKind, text]) => {
    const compiled = compileNotation(parseDisl(text), C4_BINDINGS);
    return [viewKind, assertValidDiagramDefinition({ ...compiled, elementTypes: [...compiled.elementTypes, BOUNDARY_TYPE] })];
  }),
);

/** The definition a view of `viewKind` is drawn with; a system context's until the view has arrived. */
export function c4DefinitionFor(viewKind: string | undefined): DiagramDefinition {
  return C4_DEFINITIONS[viewKind ?? ""] ?? C4_DEFINITIONS.SystemContext!;
}

/** Each kind of card by its element type in the specifications, which are named for DISL's types. */
const ELEMENT_TYPES: Readonly<Record<number, string>> = {
  [C4ElementKindProto.C4_ELEMENT_KIND_PERSON]: "person",
  [C4ElementKindProto.C4_ELEMENT_KIND_SOFTWARE_SYSTEM]: "softwaresystem",
  [C4ElementKindProto.C4_ELEMENT_KIND_CONTAINER]: "container",
  [C4ElementKindProto.C4_ELEMENT_KIND_COMPONENT]: "component",
  [C4ElementKindProto.C4_ELEMENT_KIND_DEPLOYMENT_NODE]: "deploymentnode",
  [C4ElementKindProto.C4_ELEMENT_KIND_INFRASTRUCTURE_NODE]: "infrastructurenode",
  [C4ElementKindProto.C4_ELEMENT_KIND_CONTAINER_INSTANCE]: "containerinstance",
  [C4ElementKindProto.C4_ELEMENT_KIND_SOFTWARE_SYSTEM_INSTANCE]: "softwaresysteminstance",
};

/**
 * A view as the compiled definition draws it: the boundaries first, so everything they enclose draws on top
 * of them, then the cards and the lines, with the view's title and key on the background.
 *
 * Everything is the backend's: a card's three lines, which the library fits to the card at the sizes the
 * specification's styles draw them, and its palette and silhouette; a line's label, which decorates the one
 * authored value its editor opens with. A line is an interaction where it carries an interaction's number
 * and a relationship otherwise: the wire does not say whether a line is stored or derived, and the two are
 * drawn alike.
 */
export function c4DiagramModel(model: C4Model): DiagramModel {
  const boundaries = [...model.boundaries.values()].map((boundary): DiagramModelElement => ({
    id: boundary.id,
    type: "boundary",
    x: boundary.x,
    y: boundary.y,
    width: boundary.payload.width,
    height: boundary.payload.height,
    label: `${boundary.payload.name} [${boundary.payload.kind}]`,
    payload: { name: boundary.payload.name, kind: boundary.payload.kind },
  }));
  const nodes = [...model.nodes.values()].map((node): DiagramModelElement => {
    const { name, typeLine, description, width, height, style } = node.payload;
    return {
      id: node.id,
      type: ELEMENT_TYPES[node.payload.kind] ?? "softwaresystem",
      x: node.x,
      y: node.y,
      width,
      height,
      label: name,
      payload: {
        name,
        shownName: name !== "" ? name : node.id,
        // An empty line is not drawn at all, rather than drawn empty.
        ...(typeLine !== "" ? { typeLine } : {}),
        ...(description !== "" ? { description } : {}),
        shape: style?.shape ?? "RoundedBox",
        background: style?.background ?? "#1168bd",
        color: style?.color ?? "#ffffff",
      },
    };
  });
  const connections = [...model.relationships.values()].map((relationship): DiagramModelConnection => {
    const p = relationship.payload;
    const label = p.technology ? `${p.description} [${p.technology}]` : p.description;
    return {
      id: relationship.id,
      type: p.interactionOrder ? "interactionline" : "relationship",
      sourceId: p.sourceId,
      targetId: p.destinationId,
      ...(label ? { label: p.interactionOrder ? `${p.interactionOrder}. ${label}` : label } : {}),
      editValue: p.description,
    };
  });
  const background = model.view === null ? undefined : { heading: model.view.title, legend: model.view.legend };
  return { elements: [...boundaries, ...nodes], connections, ...(background !== undefined ? { background } : {}) };
}

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

  const diagramModel = useMemo(() => c4DiagramModel(model), [model]);

  // Every refusal - a move, a drop, a declared keystroke the library sends - is shown on the one
  // line the library draws around every canvas, by the call that got it (client-centralization
  // Requirement 2), and the next gesture clears it.
  const events: DiagramEventHandlers = {
    // Selection is the library's (centralized-selection), and a boundary's inertness is its
    // type's `selectable: false` (`BOUNDARY_TYPE`).
    onElementMoved: ({ elementId, position }) => {
      if (!model.nodes.has(elementId)) {
        return;
      }
      // The library holds the card at the drop until the backend's delta places it. A refused move
      // - a view that declares autoLayout, ruled 2026-10-19 - sends no delta, so the answer is
      // handed back: the card snaps back to where the backend has it, and the sentence is shown on
      // the library's line like every other refusal.
      return moveElementTo(elementId, position.x, position.y);
    },
    onElementDropped: ({ elementType, position }) => {
      // The entry carries the backend's own drop action, and is sent as a drop: the backend
      // resolves it by the entry - the definition's drop targets and refusals - rather than by
      // the menu of what it landed on, which for a C4 element has no add. Dropped on an element,
      // that element is what the entry lands on, which is how C4's containment gets decided by
      // the gesture; inside a boundary and on no box, the element the boundary is drawn for, as
      // the software system a container view is about is drawn. Empty canvas is named by the
      // point, as the background menu names it: with no source the backend would run the action
      // on whatever is selected.
      const inside = (x: number, y: number, width: number, height: number) =>
        Math.abs(position.x - x) <= width / 2 && Math.abs(position.y - y) <= height / 2;
      const target = [...model.nodes.values()].reverse().find((node) => inside(node.x, node.y, node.payload.width, node.payload.height));
      const boundary = [...model.boundaries.values()]
        .filter((each) => inside(each.x, each.y, each.payload.width, each.payload.height))
        .sort((a, b) => a.payload.width * a.payload.height - b.payload.width * b.payload.height)[0];
      const landedOn = target?.id ?? (boundary !== undefined ? boundary.id.slice(BOUNDARY_ID_PREFIX.length) : placementId(position.x, position.y));
      void executeAction(elementType, elementSourceOf(landedOn, entryId), { drop: true });
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


  // Opening, reconnecting and unavailable are the library's to say, in the frame around this canvas. The
  // title C4 asks of every diagram and the key that lets it be read without narrative are the
  // specification's, drawn by the library above and below the drawing surface.
  return (
    <div className="c4-canvas" data-testid="c4-canvas">
      <DiagramCanvas
        definition={c4DefinitionFor(model.view?.viewKind)}
        model={diagramModel}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        className="c4-canvas-host"
        scrollbarsClassName="c4-scrollbars"
        ariaLabel={model.view?.title ?? "C4 diagram"}
      />
    </div>
  );
}

export type { C4Model };
