import { useMemo, useState } from "react";

import { straightPath } from "@client/canvas/connectors";
import { SymbolElement } from "@client/canvas/elements/symbol/SymbolElement";
import { edgePointOf } from "@client/canvas/connectors";
import { elementIdOfKey, elementSelectionOf, elementSourceOf } from "@client/canvas/selection";
import { isTextTarget, structuralShortcutFor } from "@client/canvas/interaction";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { CustomShapeRef, CustomShapeState, DiagramDefinition, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers, DiagramSelection } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { innermostKey, useContextConnection, useContextPrompt, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { ContextSelectionAction } from "@client/generated/context_pb";
import { inlineLabelElementIdOf } from "@client/shell/context/inlineLabelPrompt";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useViewReport } from "@client/diagrams/useViewReport";
import type { ContextShortcut } from "@client/generated/context_pb";
import {
  WardleyAttitudeKind,
  WardleyDecorator,
  WardleyElementKind,
} from "@client/generated/wardley-map_pb";
import type { WardleyAxis, WardleyElement, WardleyModel } from "./wardleyModel";
import { useWardleyStream } from "./useWardleyStream";

/**
 * The map's own space is 0..1 on both axes, drawn into a box of canvas units so labels and
 * stroke widths have a sensible scale - the intrinsic space the definition declares as its
 * extent, which is why Fit shows the whole map rather than a box derived from its contents.
 *
 * The box is only square when the pane is. A Wardley map's two axes measure unrelated things -
 * evolution across, value chain up - so the space is stretched to the shape of the pane rather
 * than letterboxed inside it. This is the height; the width comes from {@link spaceWidthFor}.
 * Both stay canvas units in one uniform system, so a mark's dot stays round and its label stays
 * unstretched: what varies is where the space's right-hand edge falls.
 */
const SPACE = 1000;

/** Room outside the space for the axis labels, which sit beyond the plotted area. */
const MARGIN = 90;

/** A floor for the plotted width, so a very tall narrow pane still leaves a map to read. */
const MIN_SPACE_WIDTH = 300;

/** The radius a component is drawn at, and the box a connector anchors on. */
const DOT = 9;

export interface WardleyCanvasProps {
  projectId: Uint8Array;
  entryId: Uint8Array;
  path: readonly string[];
}

/**
 * The plotted width that makes the whole space - plus both margins - sit at the pane's own
 * proportions, so the map fills the canvas and the axes land on its left and bottom.
 *
 * A square space on a wide pane was drawn as a square in the middle with dead margins either
 * side: on a 469x285 pane, 92px of nothing on each side, with the value-chain axis floating
 * 114px in from the left edge instead of sitting at it.
 *
 * The aspect is taken from the reported viewport rather than measured again here, because the
 * library already guarantees the view carries the pane's proportions - it grows the view to
 * them before anything reads it. Nothing circular follows: the view's SHAPE is the pane's
 * whatever this returns, so the width settles on the first report and stays put under pan and
 * zoom.
 */
function spaceWidthFor(paneAspect: number | null): number {
  if (paneAspect === null || !Number.isFinite(paneAspect) || paneAspect <= 0) {
    return SPACE;
  }

  return Math.max(MIN_SPACE_WIDTH, paneAspect * (SPACE + MARGIN * 2) - MARGIN * 2);
}

/**
 * The map's 0..1 space as canvas units, one function per axis.
 *
 * Rounded far finer than a pixel, to keep the DOM readable and a snapshot diff legible.
 */
export interface MapScale {
  /** A 0..1 evolution coordinate as canvas units. */
  x: (value: number) => number;
  /** A 0..1 value-chain coordinate as canvas units. */
  y: (value: number) => number;
  /** The plotted space's own size in canvas units. */
  width: number;
  height: number;
}

function mapScaleOf(spaceWidth: number): MapScale {
  return {
    x: (value) => Math.round(value * spaceWidth * 100) / 100,
    y: (value) => Math.round(value * SPACE * 100) / 100,
    width: spaceWidth,
    height: SPACE,
  };
}

/** An element as the library carries it here: the model element plus the mark it draws. */
type MarkElement = DiagramModelElement & { mark: WardleyElement };

/** An evolve target as the library carries it: the destination dot of a `evolve` statement. */
type EvolveTargetElement = DiagramModelElement & { overrideName?: string };

function kindName(kind: WardleyElementKind): string {
  switch (kind) {
    case WardleyElementKind.ANCHOR:
      return "anchor";
    case WardleyElementKind.SUBMAP:
      return "submap";
    default:
      return "component";
  }
}

function decoratorName(decorator: WardleyDecorator): string {
  switch (decorator) {
    case WardleyDecorator.MARKET:
      return "market";
    case WardleyDecorator.ECOSYSTEM:
      return "ecosystem";
    case WardleyDecorator.BUILD:
      return "build";
    case WardleyDecorator.BUY:
      return "buy";
    case WardleyDecorator.OUTSOURCE:
      return "outsource";
    default:
      return "";
  }
}

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

/** A circle's edge, for the connector geometry - the same box edge the old canvas anchored on. */
function circleEdgePoint(bounds: ShapeBounds, towards: { x: number; y: number }) {
  const centre = { x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 };
  return edgePointOf(
    { x: centre.x, y: centre.y, width: bounds.width, height: bounds.height },
    towards.x - centre.x,
    towards.y - centre.y,
  );
}

/**
 * A component, anchor or submap as a first-class custom shape: the shared symbol mark with
 * this notation's decorations - variant by kind, decorator badges, inertia, the label at its
 * authored pixel offset - while hit-testing, edge attachment and dragging stay the library's.
 */
const markShape: CustomShapeRef = {
  customShape: "wardley-mark",
  render: (raw, state?: CustomShapeState) => {
    const element = raw as MarkElement;
    const mark = element.mark;
    const x = element.x;
    const y = element.y;

    // The label offset is in PIXELS rather than map coordinates - a property of the format,
    // which ADP reproduces rather than corrects.
    const labelX = x + (mark.labelOffset?.x ?? DOT + 6);
    const labelY = y + (mark.labelOffset?.y ?? 4);

    const decorations = mark.decorators.map(decoratorName).filter((name) => name.length > 0);
    const badges = [...decorations, ...(mark.inertia ? ["inertia"] : [])];

    const variant =
      mark.kind === WardleyElementKind.ANCHOR
        ? ("square" as const)
        : mark.kind === WardleyElementKind.SUBMAP
          ? ("double-circle" as const)
          : ("circle" as const);

    return (
      <SymbolElement
        className={`wardley-element-group wardley-kind-${kindName(mark.kind)}${state?.dragging ? " wardley-dragging" : ""}${state?.selected ? " wardley-selected" : ""}`}
        x={x}
        y={y}
        radius={DOT}
        variant={variant}
        label={mark.name}
        labelX={labelX}
        labelY={labelY}
        badges={badges}
        inertia={mark.inertia}
        markClassName="wardley-element"
        outerClassName="wardley-element-outer"
        labelClassName="wardley-element-label"
        badgesClassName="wardley-element-badges"
        inertiaClassName="wardley-inertia"
      />
    );
  },
  edgePoint: circleEdgePoint,
};

/** The destination of an evolve statement: the hollow dot, with the override name where one is given. */
const evolveTargetShape: CustomShapeRef = {
  customShape: "wardley-evolve-target",
  render: (raw) => {
    const element = raw as EvolveTargetElement;
    return (
      <g>
        <circle className="wardley-evolve-target" cx={element.x} cy={element.y} r={DOT} />
        {element.overrideName ? (
          <text className="wardley-element-label" x={element.x + DOT + 6} y={element.y + 4}>
            {element.overrideName}
          </text>
        ) : null}
      </g>
    );
  },
  edgePoint: circleEdgePoint,
};

/**
 * What a Wardley map allows, stated once: marks that drag inside the intrinsic 0..1 space and
 * edit their names in place, links and evolve indicators drawn between them, and nothing else
 * interactive - the toolbox palette comes from the backend, drops stay unanswered exactly as
 * the hand-built canvas left them, and no connect gesture exists because no mark renders an
 * anchor (edge attachment draws the lines; the gesture starts from anchors, and there are
 * none). The axis chrome, attitudes, accelerators, notes and annotations are the background:
 * inert before the migration, inert after it.
 */
function definitionOf(model: WardleyModel, scale: MapScale): DiagramDefinition {
  return assertValidDiagramDefinition({
    elementTypes: [
      { id: "component", shape: markShape, anchors: { kind: "edge" }, sizing: "model", label: { placement: "beside", editable: true } },
      { id: "anchor", shape: markShape, anchors: { kind: "edge" }, sizing: "model", label: { placement: "beside", editable: true } },
      { id: "submap", shape: markShape, anchors: { kind: "edge" }, sizing: "model", label: { placement: "beside", editable: true } },
      { id: "evolve-target", shape: evolveTargetShape, anchors: { kind: "edge" }, sizing: "model", draggable: false },
    ],
    relationTypes: [
      {
        id: "link",
        route: "straight",
        style: { endMarker: "none" },
        lineClassName: "wardley-link",
        endpoints: {
          source: { elementTypes: ["component", "anchor", "submap"] },
          target: { elementTypes: ["component", "anchor", "submap"], anchors: "edge" },
          allowSelf: false,
        },
      },
      {
        id: "flow-link",
        route: "straight",
        style: { endMarker: "none" },
        lineClassName: "wardley-link wardley-link-flow",
        endpoints: {
          source: { elementTypes: ["component", "anchor", "submap"] },
          target: { elementTypes: ["component", "anchor", "submap"], anchors: "edge" },
          allowSelf: false,
        },
      },
      {
        id: "evolve",
        route: "straight",
        style: { endMarker: "none" },
        lineClassName: "wardley-evolve",
        endpoints: {
          source: { elementTypes: ["component", "anchor", "submap"] },
          target: { elementTypes: ["evolve-target"], anchors: "edge" },
          allowSelf: false,
        },
      },
    ],
    layout: { modes: ["manual"] },
    dragging: "enabled",
    // The intrinsic space: Fit shows exactly this, margins included, because the map's plane
    // is the 0..1 space itself rather than whatever its contents span.
    extent: { x: -MARGIN, y: -MARGIN, width: scale.width + MARGIN * 2, height: scale.height + MARGIN * 2 },
    // And its hard edge: a mark must not be draggable off the map while the pointer is down.
    dragBounds: { x: 0, y: 0, width: scale.width, height: scale.height },
    background: {
      background: "wardley-chrome",
      render: (view: ShapeBounds) => <WardleyBackground model={model} view={view} scale={scale} />,
    },
  });
}

/**
 * Renders one Wardley map, through the diagram library: the module supplies the definition
 * above, folds the stream into the library's model, and answers events - a drag as the same
 * backend move it always was, the menu and F2 through the context channel, the label editor
 * opening where the drawn text begins. The axes are still drawn by this module, now as the
 * definition's declared background, and every position still comes from the document: there
 * is no layout here and none anywhere in this module.
 */
export function WardleyCanvas({ projectId, entryId, path }: WardleyCanvasProps) {
  const { model, loading, failed, moveElementTo, reportView } = useWardleyStream(projectId, path);
  const { select, executeAction, executeShortcut } = useContextConnection();
  const { selection, actions } = useContextSelection();
  const toolboxItems = useToolboxItems(projectId, path);
  const [rejection, setRejection] = useState("");
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const selectionKey = innermostKey(selection);
  const selectedId = elementIdOfKey(selectionKey ?? null);

  const { prompt, onPropose: onProposeLabel, onSubmit: onSubmitLabel, onCancel: onCancelLabel } = useContextPrompt();
  const editingId = inlineLabelElementIdOf(prompt);

  // The pane's proportions, read off the view the library reports: it shapes the view to the
  // pane before anything sees it, so this is the pane's aspect without measuring it twice.
  const paneAspect = viewport !== null && viewport.height > 0 ? viewport.width / viewport.height : null;
  const scale = useMemo(() => mapScaleOf(spaceWidthFor(paneAspect)), [paneAspect]);

  // The definition closes over the model for its background - read every render, so the axis
  // and the inert furniture follow the document without a remount.
  const definition = useMemo(() => definitionOf(model, scale), [model, scale]);

  const diagramModel = useMemo<DiagramModel>(() => {
    const marks = [...model.elements.values()].map((element): MarkElement => ({
      id: element.id,
      type: kindName(element.kind),
      x: scale.x(element.x),
      y: scale.y(element.y),
      width: DOT * 2,
      height: DOT * 2,
      label: element.name,
      // Where the drawn label begins - the author's own offset, left of the mark included -
      // so the editor opens over the text it replaces.
      labelAt: {
        x: scale.x(element.x) + (element.labelOffset?.x ?? DOT + 6),
        y: scale.y(element.y) + (element.labelOffset?.y ?? 4) - 4,
      },
      mark: element,
    }));

    // An evolving mark is shown at BOTH positions joined by a movement indicator, because the
    // pair is the point of the statement: the destination is an element (a line needs two
    // ends) that ignores every gesture, and the indicator is an ordinary connection.
    const evolveTargets = [...model.elements.values()]
      .filter((element) => element.evolve)
      .map((element): EvolveTargetElement => ({
        id: `evolve:${element.id}`,
        type: "evolve-target",
        x: scale.x(element.evolve!.maturity),
        y: scale.y(element.y),
        width: DOT * 2,
        height: DOT * 2,
        overrideName: element.evolve!.overrideName || undefined,
      }));

    const links = [...model.links.values()].flatMap((link) =>
      model.elements.has(link.sourceId) && model.elements.has(link.targetId)
        ? [{
            id: link.id,
            type: link.isFlow ? "flow-link" : "link",
            sourceId: link.sourceId,
            targetId: link.targetId,
            title: link.context || undefined,
          }]
        : [], // a dangling link has nowhere to be drawn to, exactly as before
    );

    const evolves = [...model.elements.values()]
      .filter((element) => element.evolve)
      .map((element) => ({
        id: `evolve-line:${element.id}`,
        type: "evolve",
        sourceId: element.id,
        targetId: `evolve:${element.id}`,
      }));

    return { elements: [...marks, ...evolveTargets], connections: [...links, ...evolves] };
  }, [model]);

  /** The backend's push is the selection; the canvas renders it and never decides. */
  const librarySelection = useMemo<DiagramSelection>(
    () => (selectedId === null || !model.elements.has(selectedId) ? [] : [{ kind: "element", id: selectedId }]),
    [selectedId, model.elements],
  );

  const runShortcut = (shortcut: ContextShortcut, sourceId: string) => {
    void (async () => {
      const outcome = await executeShortcut(shortcut, elementSourceOf(sourceId));
      if (!outcome.accepted && outcome.error) {
        setRejection(outcome.error);
      }
    })();
  };

  const events: DiagramEventHandlers = {
    onSelectionChanged: ({ selection: next }) => {
      // An evolve target ignores every gesture: it exists so the indicator has two ends, and
      // a press on it neither selects nor deselects - the closest the library offers to the
      // inert dot it replaced.
      if (next.length > 0 && next[0].id.startsWith("evolve:")) {
        return;
      }
      select(next.length > 0 ? elementSelectionOf(entryId, path, next[0].id) : null);
    },
    onElementMoved: ({ elementId, position }) => {
      if (!model.elements.has(elementId)) {
        return;
      }
      setRejection("");
      // A drag is a DOCUMENT EDIT here, not a view change: the backend converts the point
      // back into the document's axes and the new position returns as an ordinary delta. The
      // library already clamped to the map's edge; the division puts it back into 0..1.
      void (async () => {
        const error = await moveElementTo(elementId, position.x / scale.width, position.y / scale.height);
        if (error) {
          setRejection(error);
        }
      })();
    },
    onViewChanged: ({ viewport: next }) => setViewport(next),
    // Deliberately unanswered: element-dropped (the hand-built canvas never wired toolbox
    // drops, and a migration adds nothing), element-deleted and connection-deleted (Delete
    // was never a wardley key; removal lives in the menu the backend pushes).
  };

  // What the reader can see, reported once it settles, in the map's own 0..1 units.
  useViewReport({
    view: { x: viewport?.x ?? 0, y: viewport?.y ?? 0, w: viewport?.width ?? 0, h: viewport?.height ?? 0 },
    report: reportView,
    convert: () => ({
      minX: (viewport?.x ?? 0) / scale.width,
      minY: (viewport?.y ?? 0) / scale.height,
      maxX: ((viewport?.x ?? 0) + (viewport?.width ?? 0)) / scale.width,
      maxY: ((viewport?.y ?? 0) + (viewport?.height ?? 0)) / scale.height,
    }),
    ready: !loading && !failed && viewport !== null,
  });

  /** F2 travels to the backend as data - the backend owns the key-to-action map. */
  const onKeyDown = (event: React.KeyboardEvent) => {
    if (!selectedId || isTextTarget(event.target)) {
      return;
    }
    const shortcut = structuralShortcutFor(event, ["F2"]);
    if (!shortcut) {
      return;
    }
    event.preventDefault();
    runShortcut(shortcut, selectedId);
  };

  if (failed) {
    return (
      <div className="wardley-canvas wardley-canvas-message">
        <p>This map could not be opened.</p>
      </div>
    );
  }

  return (
    <div className="wardley-canvas" role="application" aria-label={model.axis?.title ? `Wardley map: ${model.axis.title}` : "Wardley map"} onKeyDown={onKeyDown}>
      <DiagramCanvas
        definition={definition}
        model={loading ? { elements: [], connections: [] } : diagramModel}
        events={events}
        selection={librarySelection}
        toolboxItems={toolboxItems}
        context={{
          selectionKey: selectionKey ?? undefined,
          actions,
          selectForMenu: (id) => select(elementSelectionOf(entryId, path, id, ContextSelectionAction.CONTEXT_MENU)),
          executeAction: (actionId) => {
            void (async () => {
              const outcome = await executeAction(actionId);
              if (!outcome.accepted && outcome.error) {
                setRejection(outcome.error);
              }
            })();
          },
        }}
        editing={{ editingId, onPropose: onProposeLabel, onSubmit: onSubmitLabel, onCancel: onCancelLabel }}
        className="wardley-surface"
        scrollbarsClassName="wardley-scrollbars"
        ariaLabel={model.axis?.title ? `Wardley map: ${model.axis.title}` : "Wardley map"}
      />
      {rejection ? <p className="wardley-rejection">{rejection}</p> : null}
    </div>
  );
}

/**
 * Everything inert, drawn behind the elements: the bands, axes and their labels, the attitude
 * regions, the accelerators, the notes and the numbered annotations. All of it ignored every
 * gesture before the migration, and the background layer is what keeps it that way - at the
 * recorded cost that a note overlapping a link now draws beneath it rather than above.
 *
 * The stage boundaries come from the model's axis, never from a constant here: the backend
 * holds the one copy and this draws what it is told.
 */
function WardleyBackground({ model, view, scale }: { model: WardleyModel; view: ShapeBounds; scale: MapScale }) {
  // Stage and axis labels hold a readable size as the map is zoomed; the boundaries they name
  // do not, because a position is only meaningful against its own axes.
  const scaleFactor = view.width / (scale.width + MARGIN * 2);
  const labelSize = 20 * Math.max(0.35, Math.min(2.5, scaleFactor));

  return (
    <g className="wardley-chrome" aria-hidden="true">
      <WardleyAxes axis={model.axis} labelSize={labelSize} scale={scale} />

      {[...model.attitudes.values()].map((attitude) => (
        <g key={attitude.id}>
          <rect
            className={`wardley-attitude wardley-attitude-${attitudeName(attitude.kind)}`}
            x={scale.x(Math.min(attitude.x, attitude.opposite.x))}
            y={scale.y(Math.min(attitude.y, attitude.opposite.y))}
            width={scale.x(Math.abs(attitude.opposite.x - attitude.x))}
            height={scale.y(Math.abs(attitude.opposite.y - attitude.y))}
          />
          <text
            className="wardley-attitude-label"
            x={scale.x(Math.min(attitude.x, attitude.opposite.x)) + 8}
            y={scale.y(Math.min(attitude.y, attitude.opposite.y)) + 22}
          >
            {attitudeName(attitude.kind)}
          </text>
        </g>
      ))}

      {[...model.accelerators.values()].map((accelerator) => (
        <g key={accelerator.id} className="wardley-accelerator">
          <path
            className={accelerator.isDeaccelerator ? "wardley-accelerator-back" : "wardley-accelerator-forward"}
            d={straightPath(
              { x: scale.x(accelerator.x) - 16, y: scale.y(accelerator.y) },
              { x: scale.x(accelerator.x) + 16, y: scale.y(accelerator.y) },
            )}
          />
          <text className="wardley-element-label" x={scale.x(accelerator.x) + 22} y={scale.y(accelerator.y) + 4}>
            {accelerator.name}
          </text>
        </g>
      ))}

      {[...model.notes.values()].map((note) => (
        <text key={note.id} className="wardley-note" x={scale.x(note.x)} y={scale.y(note.y)}>
          {note.text}
        </text>
      ))}

      {/* Every occurrence, not just the first: one numbered annotation may be pinned in
          several places, and none of them may be lost. */}
      {[...model.annotations.values()].flatMap((annotation) =>
        annotation.occurrences.map((occurrence, index) => (
          <g key={`${annotation.id}-${index}`} className="wardley-annotation">
            <circle cx={scale.x(occurrence.x)} cy={scale.y(occurrence.y)} r={11} />
            <text x={scale.x(occurrence.x)} y={scale.y(occurrence.y) + 4} textAnchor="middle">
              {annotation.number}
            </text>
            <title>{annotation.text}</title>
          </g>
        )),
      )}
    </g>
  );
}

/** The bands, the two axes and their labels - what an empty map shows on its own. */
function WardleyAxes({ axis, labelSize, scale }: { axis?: WardleyAxis; labelSize: number; scale: MapScale }) {
  return (
    <>
      {(axis?.stages ?? []).map((stage, index) => (
        <g key={stage.label}>
          <rect
            className={`wardley-band wardley-band-${index}`}
            x={scale.x(stage.start)}
            y={0}
            width={scale.x(stage.end - stage.start)}
            height={scale.height}
          />
          {index > 0 ? (
            <line
              className="wardley-band-edge"
              x1={scale.x(stage.start)}
              y1={0}
              x2={scale.x(stage.start)}
              y2={scale.height}
            />
          ) : null}
          <text
            className="wardley-band-label"
            x={scale.x((stage.start + stage.end) / 2)}
            y={scale.height + 34}
            fontSize={labelSize}
            textAnchor="middle"
          >
            {stage.label}
          </text>
        </g>
      ))}

      {/* The value chain: the user need at the top, invisible at the bottom. */}
      <line className="wardley-axis" x1={0} y1={0} x2={0} y2={scale.height} />
      {/* Evolution: genesis at the left, commodity at the right. */}
      <line className="wardley-axis" x1={0} y1={scale.height} x2={scale.width} y2={scale.height} />

      <text className="wardley-axis-label" transform={`translate(${-34} ${scale.height / 2}) rotate(-90)`} fontSize={labelSize} textAnchor="middle">
        Value chain
      </text>
      <text className="wardley-axis-end" x={-14} y={12} fontSize={labelSize * 0.8} textAnchor="end">
        Visible
      </text>
      <text className="wardley-axis-end" x={-14} y={scale.height} fontSize={labelSize * 0.8} textAnchor="end">
        Invisible
      </text>
      <text className="wardley-axis-label" x={scale.width / 2} y={scale.height + 66} fontSize={labelSize} textAnchor="middle">
        Evolution
      </text>
    </>
  );
}
