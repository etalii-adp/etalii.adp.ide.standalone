import { useMemo, useState } from "react";

import disText from "../definition/wardley-map.dis?raw";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { widthOf } from "@client/canvas/label/textMetrics";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useViewReport } from "@client/diagrams/useViewReport";
import { WardleyAttitudeKind, WardleyDecorator, WardleyElementKind } from "@client/generated/wardley-map_pb";
import type { WardleyElement, WardleyModel } from "./wardleyModel";
import { useWardleyStream } from "./useWardleyStream";
import { WARDLEY_BINDINGS } from "./wardleyBindings";

/** The specification the backend runs, read once: the canvas is compiled from it for each pane's shape. */
const SPEC = parseDisl(disText);

/**
 * The canvas's definition for a pane of the given proportions, compiled from the bundled specification.
 *
 * The map's bounds are stretched across to the pane (`fit: "stretch"`): a Wardley map's two axes measure
 * unrelated things, evolution across and value chain up, so the space takes the pane's shape rather than
 * being letterboxed inside it. Without a pane yet, the bounds are as the specification states them.
 */
export function wardleyDefinitionFor(paneAspect: number | null): DiagramDefinition {
  const pane = paneAspect !== null && Number.isFinite(paneAspect) && paneAspect > 0 ? { width: paneAspect, height: 1 } : undefined;
  return assertValidDiagramDefinition(compileNotation(SPEC, WARDLEY_BINDINGS, pane !== undefined ? { pane } : {}));
}

/** The drawn size of a mark, of an annotation's pin and of an accelerator, as the specification fixes them. */
const MARK = 18;
const PIN = 22;
const ACCELERATOR = { width: 32, height: 3 };

/** The font size a note is written at (the specification's style for it), which places its left end. */
const NOTE_FONT_SIZE = 14;

/** Where a mark's name starts when the document gives no offset, and its badges a line below it. */
const DEFAULT_LABEL_OFFSET = { x: 15, y: 4 };
const BADGES_BELOW = 14;

const DECORATORS: Readonly<Partial<Record<WardleyDecorator, string>>> = {
  [WardleyDecorator.MARKET]: "market",
  [WardleyDecorator.ECOSYSTEM]: "ecosystem",
  [WardleyDecorator.BUILD]: "build",
  [WardleyDecorator.BUY]: "buy",
  [WardleyDecorator.OUTSOURCE]: "outsource",
};

function attitudeName(kind: WardleyAttitudeKind): string {
  switch (kind) {
    case WardleyAttitudeKind.SETTLERS:
      return "settlers";
    case WardleyAttitudeKind.TOWN_PLANNERS:
      return "townplanners";
    default:
      return "pioneers";
  }
}

/** The specification's type an element of the stream is: a pipeline's component, or its own kind. */
function markType(element: WardleyElement): string {
  if (element.pipelineParentId.length > 0) {
    return "pipelinecomponent";
  }
  switch (element.kind) {
    case WardleyElementKind.ANCHOR:
      return "anchor";
    case WardleyElementKind.SUBMAP:
      return "submap";
    default:
      return "component";
  }
}

/**
 * The library's model of a map: each element of the stream as the specification's type, at its place on
 * the drawing area, carrying what the specification's CEL computes and the library cannot, under the paths
 * `wardleyBindings.ts` names. `area` is the drawing area, the definition's `dragBounds`; the stream's
 * positions are its 0..1 fractions.
 */
export function wardleyDiagramModelOf(model: WardleyModel, area: { width: number; height: number }): DiagramModel {
  const x = (value: number) => Math.round(value * area.width * 100) / 100;
  const y = (value: number) => Math.round(value * area.height * 100) / 100;

  const marks = [...model.elements.values()].map((element): DiagramModelElement => {
    const offset = element.labelOffset ?? DEFAULT_LABEL_OFFSET;
    const badges = [...element.decorators.map((decorator) => DECORATORS[decorator] ?? ""), ...(element.inertia ? ["inertia"] : [])].filter((name) => name.length > 0);
    return {
      id: element.id,
      type: markType(element),
      x: x(element.x),
      y: y(element.y),
      width: MARK,
      height: MARK,
      label: element.name,
      payload: {
        name: element.name,
        inertia: element.inertia,
        badges: badges.join(" · "),
        labelOffset: [offset.x, offset.y],
        badgesOffset: [offset.x, offset.y + BADGES_BELOW],
      },
    };
  });

  // An evolving element is shown at both positions, its evolution a ring where it is heading.
  const evolutions = [...model.elements.values()].flatMap((element): DiagramModelElement[] =>
    element.evolve === undefined
      ? []
      : [{
          id: `evolve:${element.id}`,
          type: "evolution",
          x: x(element.evolve.maturity),
          y: y(element.y),
          width: MARK,
          height: MARK,
          payload: { renamedTo: element.evolve.overrideName },
        }],
  );

  const notes = [...model.notes.values()].map((note): DiagramModelElement => {
    // Placed by its left end (`anchor: "left"`), so its box is as wide as its text.
    const width = widthOf(note.text, NOTE_FONT_SIZE);
    return { id: note.id, type: "note", x: x(note.x) + width / 2, y: y(note.y), width, height: 0, payload: { text: note.text } };
  });

  // Every place an annotation is pinned, none of them lost.
  const pins = [...model.annotations.values()].flatMap((annotation) =>
    annotation.occurrences.map((occurrence, index): DiagramModelElement => ({
      id: `${annotation.id}#${index}`,
      type: "annotationpin",
      x: x(occurrence.x),
      y: y(occurrence.y),
      width: PIN,
      height: PIN,
      payload: { number: String(annotation.number), text: annotation.text },
    })),
  );

  const accelerators = [...model.accelerators.values()].map((accelerator): DiagramModelElement => ({
    id: accelerator.id,
    type: "accelerator",
    x: x(accelerator.x),
    y: y(accelerator.y),
    width: ACCELERATOR.width,
    height: ACCELERATOR.height,
    payload: { name: accelerator.name, direction: accelerator.isDeaccelerator ? "back" : "forward" },
  }));

  const attitudes = [...model.attitudes.values()].map((attitude): DiagramModelElement => {
    // From one corner to the other (`x`, `x2`, `y`, `y2`), whichever way round the document gives them.
    const left = x(Math.min(attitude.x, attitude.opposite.x));
    const right = x(Math.max(attitude.x, attitude.opposite.x));
    const top = y(Math.min(attitude.y, attitude.opposite.y));
    const bottom = y(Math.max(attitude.y, attitude.opposite.y));
    return {
      id: attitude.id,
      type: "attitude",
      x: (left + right) / 2,
      y: (top + bottom) / 2,
      width: right - left,
      height: bottom - top,
      payload: { attitude: attitudeName(attitude.kind) },
    };
  });

  const links = [...model.links.values()].flatMap((link): DiagramModelConnection[] =>
    model.elements.has(link.sourceId) && model.elements.has(link.targetId)
      ? [{ id: link.id, type: link.isFlow ? "flow" : "dependency", sourceId: link.sourceId, targetId: link.targetId, ...(link.context ? { title: link.context } : {}) }]
      : [], // a link whose end names nothing has nowhere to be drawn to
  );

  // An evolution's line runs from it to the element it belongs to (`Evolution.component`).
  const joins = [...model.elements.values()].flatMap((element): DiagramModelConnection[] =>
    element.evolve === undefined
      ? []
      : [{ id: `evolve-line:${element.id}`, type: "evolution.component", sourceId: `evolve:${element.id}`, targetId: element.id }],
  );

  return {
    // The attitudes first, behind everything; the background layer keeps them beneath the lines too.
    elements: [...attitudes, ...accelerators, ...notes, ...pins, ...marks, ...evolutions],
    connections: [...links, ...joins],
  };
}

export interface WardleyCanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}

/** The definition with nothing drawn behind the elements. */
function withoutBackground(definition: DiagramDefinition): DiagramDefinition {
  const { background: _background, ...rest } = definition;
  return rest;
}

/**
 * Renders one Wardley map through the diagram library, its definition compiled from the specification
 * the backend runs (`definition/wardley-map.dis`): the module folds the stream into the library's model
 * and answers what the library hands it - a drag as the same backend move it always was, and the view it
 * reports. The menus, F2 and Delete go to the backend as the keys their entries write. Every position comes
 * from the document: there is no layout here and none anywhere in this module.
 */
export function WardleyCanvas({ projectId, entryId, path }: WardleyCanvasProps) {
  const { model, loading, failed, moveElementTo, reportView } = useWardleyStream(projectId, path);
  const toolboxItems = useToolboxItems(projectId, path);
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  // The pane's proportions, read off the view the library reports: it shapes the view to the pane before
  // anything sees it, so this is the pane's aspect without measuring it twice.
  const paneAspect = viewport !== null && viewport.height > 0 ? viewport.width / viewport.height : null;
  const compiled = useMemo(() => wardleyDefinitionFor(paneAspect), [paneAspect]);
  // No stages are drawn before the map has arrived, nor for a map that could not be read.
  const definition = useMemo(() => (loading || failed ? withoutBackground(compiled) : compiled), [compiled, loading, failed]);
  const area = definition.dragBounds ?? { x: 0, y: 0, width: 1, height: 1 };

  const diagramModel = useMemo(() => wardleyDiagramModelOf(model, area), [model, area.width, area.height]);

  const events: DiagramEventHandlers = {
    onElementMoved: ({ elementId, position }) => {
      if (!model.elements.has(elementId)) {
        return;
      }
      // A drag is a DOCUMENT EDIT here, not a view change: the backend converts the point back into the
      // document's axes and the new position returns as an ordinary delta. The library already clamped
      // to the map's edge; the division puts it back into 0..1. Returned, so a refusal lets the library
      // put the element back where it was.
      return moveElementTo(elementId, position.x / area.width, position.y / area.height);
    },
    onViewChanged: ({ viewport: next }) => setViewport(next),
  };

  // What the reader can see, reported once it settles, in the map's own 0..1 units.
  useViewReport({
    view: { x: viewport?.x ?? 0, y: viewport?.y ?? 0, w: viewport?.width ?? 0, h: viewport?.height ?? 0 },
    report: reportView,
    convert: () => ({
      minX: (viewport?.x ?? 0) / area.width,
      minY: (viewport?.y ?? 0) / area.height,
      maxX: ((viewport?.x ?? 0) + (viewport?.width ?? 0)) / area.width,
      maxY: ((viewport?.y ?? 0) + (viewport?.height ?? 0)) / area.height,
    }),
    ready: !loading && !failed && viewport !== null,
  });

  const name = model.axis?.title ? `Wardley map: ${model.axis.title}` : "Wardley map";

  // Opening, reconnecting and unavailable are the library's to say, in the frame around this canvas.
  return (
    <div className="wardley-canvas" role="application" aria-label={name}>
      <DiagramCanvas
        definition={definition}
        model={loading ? { elements: [], connections: [] } : diagramModel}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
        className="wardley-surface"
        scrollbarsClassName="wardley-scrollbars"
        ariaLabel={name}
      />
    </div>
  );
}
