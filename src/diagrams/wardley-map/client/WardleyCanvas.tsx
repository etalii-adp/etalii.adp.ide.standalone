import { useMemo, useState } from "react";

import { elementIdOfKey, elementSelectionOf, elementSourceOf } from "@client/canvas/selection";
import { contextShortcutOf } from "@client/canvas/interaction";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers, DiagramSelection } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { innermostKey, useContextConnection, useContextPrompt, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { ContextSelectionAction } from "@client/generated/context_pb";
import { inlineLabelElementIdOf } from "@client/shell/context/inlineLabelPrompt";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import { useViewReport } from "@client/diagrams/useViewReport";
import type { ContextShortcut } from "@client/generated/context-contract_pb";
import {
  WardleyAttitudeKind,
  WardleyDecorator,
  WardleyElementKind,
} from "@client/generated/wardley-map_pb";
import type { WardleyElement, WardleyModel } from "./wardleyModel";
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




/**
 * Which key the backend knows each declared action by.
 *
 * The library dispatches an id; the backend's context table is keyed by keystroke. One map,
 * in one place, rather than a keystroke built at each call site.
 */
const BACKEND_KEYS: Readonly<Record<string, string>> = { "rename": "F2" };

/**
 * What a Wardley map allows, stated once: marks that drag inside the intrinsic 0..1 space and
 * edit their names in place, links and evolve indicators drawn between them, and nothing else
 * interactive - the toolbox palette comes from the backend, drops stay unanswered exactly as
 * the hand-built canvas left them, and no connect gesture exists because no mark renders an
 * anchor (edge attachment draws the lines; the gesture starts from anchors, and there are
 * none). The axis chrome, attitudes, accelerators, notes and annotations are the background:
 * inert before the migration, inert after it.
 */
function definitionOf(scale: MapScale): DiagramDefinition {
  return assertValidDiagramDefinition({
    elementTypes: [
      {
        id: "component",
        // The mark the notation draws, chosen per element from the document: a component is a
        // circle, an anchor a square, a submap a double ring.
        shape: "symbol",
        boundStyle: { silhouette: { path: "payload.variant" } },
        classNames: [
          { className: "wardley-element-group", on: "element" },
          { className: { template: "wardley-kind-{payload.kind}" }, on: "element" },
          { className: "wardley-dragging", on: "element", when: { path: "state.dragging", is: "true" } },
          { className: "wardley-selected", on: "element", when: { path: "state.selected", is: "true" } },
          { className: "wardley-element", on: "shape" },
          { className: "wardley-element-outer", on: "shape-inner" },
        ],
        labels: [
          {
            // AT THE DOCUMENT'S OWN OFFSET. A Wardley map stores each mark's label offset in
            // pixels - a property of the format, which ADP reproduces rather than corrects -
            // and `beside` now draws where the element says, which is where its editor opens.
            text: { path: "payload.name" },
            placement: "beside",
            editable: true,
            className: "wardley-element-label",
          },
          {
            // What the author claimed about this element, and the wall it is pushed against.
            text: { path: "payload.badges", each: { path: "text" }, join: " · " },
            when: { path: "payload.badges", is: "non-empty" },
            placement: "beside",
            offset: { x: 0, y: 14 },
            className: "wardley-element-badges",
          },
        ],
        decorations: [
          {
            // The inertia bar: the wall movement would meet, drawn where it stands.
            glyph: "line",
            // Canvas units: these read `bounds`, which resolves to a position rather than an offset.
            anchor: "canvas",
            from: { x: { path: "bounds.right", number: { plus: 4 } }, y: { path: "bounds.top", number: { plus: -6 } } },
            to: { x: { path: "bounds.right", number: { plus: 4 } }, y: { path: "bounds.bottom", number: { plus: 6 } } },
            className: "wardley-inertia",
            when: { path: "payload.inertia", is: "true" },
          },
        ],
        anchors: { kind: "edge" },
        sizing: "model",
      },
      {
        id: "anchor",
        // The mark the notation draws, chosen per element from the document: a component is a
        // circle, an anchor a square, a submap a double ring.
        shape: "symbol",
        boundStyle: { silhouette: { path: "payload.variant" } },
        classNames: [
          { className: "wardley-element-group", on: "element" },
          { className: { template: "wardley-kind-{payload.kind}" }, on: "element" },
          { className: "wardley-dragging", on: "element", when: { path: "state.dragging", is: "true" } },
          { className: "wardley-selected", on: "element", when: { path: "state.selected", is: "true" } },
          { className: "wardley-element", on: "shape" },
          { className: "wardley-element-outer", on: "shape-inner" },
        ],
        labels: [
          {
            // AT THE DOCUMENT'S OWN OFFSET. A Wardley map stores each mark's label offset in
            // pixels - a property of the format, which ADP reproduces rather than corrects -
            // and `beside` now draws where the element says, which is where its editor opens.
            text: { path: "payload.name" },
            placement: "beside",
            editable: true,
            className: "wardley-element-label",
          },
          {
            // What the author claimed about this element, and the wall it is pushed against.
            text: { path: "payload.badges", each: { path: "text" }, join: " · " },
            when: { path: "payload.badges", is: "non-empty" },
            placement: "beside",
            offset: { x: 0, y: 14 },
            className: "wardley-element-badges",
          },
        ],
        decorations: [
          {
            // The inertia bar: the wall movement would meet, drawn where it stands.
            glyph: "line",
            // Canvas units: these read `bounds`, which resolves to a position rather than an offset.
            anchor: "canvas",
            from: { x: { path: "bounds.right", number: { plus: 4 } }, y: { path: "bounds.top", number: { plus: -6 } } },
            to: { x: { path: "bounds.right", number: { plus: 4 } }, y: { path: "bounds.bottom", number: { plus: 6 } } },
            className: "wardley-inertia",
            when: { path: "payload.inertia", is: "true" },
          },
        ],
        anchors: { kind: "edge" },
        sizing: "model",
      },
      {
        id: "submap",
        // The mark the notation draws, chosen per element from the document: a component is a
        // circle, an anchor a square, a submap a double ring.
        shape: "symbol",
        boundStyle: { silhouette: { path: "payload.variant" } },
        classNames: [
          { className: "wardley-element-group", on: "element" },
          { className: { template: "wardley-kind-{payload.kind}" }, on: "element" },
          { className: "wardley-dragging", on: "element", when: { path: "state.dragging", is: "true" } },
          { className: "wardley-selected", on: "element", when: { path: "state.selected", is: "true" } },
          { className: "wardley-element", on: "shape" },
          { className: "wardley-element-outer", on: "shape-inner" },
        ],
        labels: [
          {
            // AT THE DOCUMENT'S OWN OFFSET. A Wardley map stores each mark's label offset in
            // pixels - a property of the format, which ADP reproduces rather than corrects -
            // and `beside` now draws where the element says, which is where its editor opens.
            text: { path: "payload.name" },
            placement: "beside",
            editable: true,
            className: "wardley-element-label",
          },
          {
            // What the author claimed about this element, and the wall it is pushed against.
            text: { path: "payload.badges", each: { path: "text" }, join: " · " },
            when: { path: "payload.badges", is: "non-empty" },
            placement: "beside",
            offset: { x: 0, y: 14 },
            className: "wardley-element-badges",
          },
        ],
        decorations: [
          {
            // The inertia bar: the wall movement would meet, drawn where it stands.
            glyph: "line",
            // Canvas units: these read `bounds`, which resolves to a position rather than an offset.
            anchor: "canvas",
            from: { x: { path: "bounds.right", number: { plus: 4 } }, y: { path: "bounds.top", number: { plus: -6 } } },
            to: { x: { path: "bounds.right", number: { plus: 4 } }, y: { path: "bounds.bottom", number: { plus: 6 } } },
            className: "wardley-inertia",
            when: { path: "payload.inertia", is: "true" },
          },
        ],
        anchors: { kind: "edge" },
        sizing: "model",
      },
      {
        id: "evolve-target",
        // The destination dot of an `evolve` statement: a mark and, where the statement renames
        // it, the name it lands under.
        shape: "symbol",
        classNames: [{ className: "wardley-evolve-target", on: "shape" }],
        labels: [
          {
            text: { path: "payload.overrideName" },
            when: { path: "payload.overrideName", is: "non-empty" },
            placement: "beside",
            className: "wardley-element-label",
          },
        ],
        anchors: { kind: "edge" },
        sizing: "model",
        draggable: false,
      },
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
    // WHAT THIS TYPE OFFERS, AND WHAT INVOKES IT. The key list was hand-written in this canvas
    // and the delete was a keystroke it built to describe a gesture the library had already
    // handed it. Declared, the library derives the key set and dispatches an action id.
    actions: [
      { id: "rename", invokedBy: [{ kind: "shortcut", key: "F2" }], appliesTo: [{ kind: "element" }] },
    ],
    layout: { modes: ["manual"] },
    dragging: "enabled",
    // The intrinsic space: Fit shows exactly this, margins included, because the map's plane
    // is the 0..1 space itself rather than whatever its contents span.
    extent: { x: -MARGIN, y: -MARGIN, width: scale.width + MARGIN * 2, height: scale.height + MARGIN * 2 },
    // And its hard edge: a mark must not be draggable off the map while the pointer is down.
    dragBounds: { x: 0, y: 0, width: scale.width, height: scale.height },
    // THE BACKDROP, DECLARED - and with it the third escape hatch closes. Twenty-one of the
    // twenty-four raw-SVG lines this module drew were these: evolution bands with their
    // boundaries and names, two axes with a rotated title and end labels, attitude regions, and
    // the map's own furniture. Every position below is a fraction of the declared extent, which
    // the fold converts from the map's 0..1 space - the module's arithmetic, not the library's.
    background: {
      className: "wardley-chrome",
      bands: [
        {
          each: { path: "payload.stages" },
          orientation: "vertical",
          start: { path: "start" },
          end: { path: "end" },
          label: { path: "label" },
          // A boundary BETWEEN stages: the library skips the first, because a rule at the map's
          // own left edge would be the axis rather than a boundary.
          edge: true,
          className: { path: "className" },
          typography: { fontSize: 20, scaleWithView: { min: 0.35, max: 2.5 } },
        },
      ],
      axes: [
        {
          orientation: "vertical",
          title: { template: "Value chain" },
          startLabel: { template: "Visible" },
          endLabel: { template: "Invisible" },
          className: "wardley-axis",
          typography: { fontSize: 20, scaleWithView: { min: 0.35, max: 2.5 } },
        },
        {
          orientation: "horizontal",
          title: { template: "Evolution" },
          className: "wardley-axis",
          typography: { fontSize: 20, scaleWithView: { min: 0.35, max: 2.5 } },
        },
      ],
      regions: [
        {
          each: { path: "payload.attitudes" },
          x: { path: "x" },
          y: { path: "y" },
          width: { path: "width" },
          height: { path: "height" },
          label: { path: "label" },
          className: { path: "className" },
        },
      ],
      marks: [
        {
          // An accelerator: a short rule with the name it carries.
          each: { path: "payload.accelerators" },
          x: { path: "x" },
          y: { path: "y" },
          glyph: "rule",
          width: 16,
          label: { path: "name" },
          labelOffset: { x: 22, y: 4 },
          className: { path: "className" },
        },
        {
          // A note: free text where its author pinned it, and no glyph at all.
          each: { path: "payload.notes" },
          x: { path: "x" },
          y: { path: "y" },
          glyph: "none",
          label: { path: "text" },
          className: "wardley-note",
        },
        {
          // EVERY OCCURRENCE, not just the first: one numbered annotation may be pinned in
          // several places and none of them may be lost - which is why the fold hands over a
          // FLAT list, and why nested collections are refused rather than quietly half-drawn.
          each: { path: "payload.annotations" },
          x: { path: "x" },
          y: { path: "y" },
          glyph: "circle",
          radius: 11,
          label: { path: "number" },
          labelOffset: { x: 0, y: 4 },
          labelAnchor: "middle",
          tooltip: { path: "text" },
          className: "wardley-annotation",
        },
      ],
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
  const definition = useMemo(() => definitionOf(scale), [model, scale]);

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
      // What the declaration reads: the mark's kind, the badges the author claimed, and the
      // wall it is pushed against.
      payload: {
        name: element.name,
        kind: kindName(element.kind),
        variant:
          element.kind === WardleyElementKind.ANCHOR
            ? "square"
            : element.kind === WardleyElementKind.SUBMAP
              ? "double-circle"
              : "circle",
        inertia: element.inertia,
        badges: [...element.decorators.map(decoratorName).filter((name) => name.length > 0), ...(element.inertia ? ["inertia"] : [])].map(
          (text) => ({ text }),
        ),
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
        payload: { overrideName: element.evolve!.overrideName },
        labelAt: { x: scale.x(element.evolve!.maturity) + DOT + 6, y: scale.y(element.y) + 4 },
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

    return {
      elements: [...marks, ...evolveTargets],
      connections: [...links, ...evolves],
      // THE BACKDROP AS DATA. Every position is a fraction of the declared extent, which is the
      // map's 0..1 space plus its margin - the conversion is this module's arithmetic, because
      // the margin is this notation's and the library has no opinion about it.
      background: backgroundOf(model, scale),
    };
  }, [model, scale]);

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
    // The declared action, answered as the shortcut the backend has always known it by.
    onActionInvoked: ({ actionId, targetId }) => {
      const key = BACKEND_KEYS[actionId];
      if (key !== undefined && targetId !== undefined) {
        runShortcut(contextShortcutOf(key), targetId);
      }
    },
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


  if (failed) {
    return (
      <div className="wardley-canvas wardley-canvas-message">
        <p>This map could not be opened.</p>
      </div>
    );
  }

  return (
    <div className="wardley-canvas" role="application" aria-label={model.axis?.title ? `Wardley map: ${model.axis.title}` : "Wardley map"}>
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
 * The map's chrome as data: bands, attitudes, accelerators, notes and numbered annotations,
 * every one of them positioned as a fraction of the declared extent.
 *
 * <b>This is where the third escape hatch closes.</b> `DiagramBackgroundRef` was a function the
 * library called to draw a backdrop; what it drew was this, and it is a value now.
 *
 * <b>Annotations are FLATTENED here</b>, one entry per occurrence: a numbered annotation may be
 * pinned in several places, and a background's `each` reads one level. Doing it in the fold is
 * the module saying what its own model means, which is the half that stays module code.
 */
function backgroundOf(model: WardleyModel, scale: MapScale): unknown {
  // A map coordinate as a fraction of the extent: the plotted space sits inside a margin on
  // every side, so 0 on the map is not 0 on the extent.
  const fx = (value: number) => (scale.x(value) + MARGIN) / (scale.width + MARGIN * 2);
  const fy = (value: number) => (scale.y(value) + MARGIN) / (scale.height + MARGIN * 2);

  return {
    stages: (model.axis?.stages ?? []).map((stage, index) => ({
      start: fx(stage.start),
      end: fx(stage.end),
      label: stage.label,
      className: `wardley-band wardley-band-${index}`,
    })),
    attitudes: [...model.attitudes.values()].map((attitude) => ({
      x: fx(Math.min(attitude.x, attitude.opposite.x)),
      y: fy(Math.min(attitude.y, attitude.opposite.y)),
      width: fx(Math.abs(attitude.opposite.x - attitude.x)) - fx(0),
      height: fy(Math.abs(attitude.opposite.y - attitude.y)) - fy(0),
      label: attitudeName(attitude.kind),
      className: `wardley-attitude wardley-attitude-${attitudeName(attitude.kind)}`,
    })),
    accelerators: [...model.accelerators.values()].map((accelerator) => ({
      x: fx(accelerator.x),
      y: fy(accelerator.y),
      name: accelerator.name,
      className: accelerator.isDeaccelerator ? "wardley-accelerator-back" : "wardley-accelerator-forward",
    })),
    notes: [...model.notes.values()].map((note) => ({ x: fx(note.x), y: fy(note.y), text: note.text })),
    annotations: [...model.annotations.values()].flatMap((annotation) =>
      annotation.occurrences.map((occurrence) => ({
        x: fx(occurrence.x),
        y: fy(occurrence.y),
        number: String(annotation.number),
        text: annotation.text,
      })),
    ),
  };
}

