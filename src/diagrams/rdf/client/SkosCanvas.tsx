import { useMemo, useState } from "react";

import { BoxElement } from "@client/canvas/elements/box/BoxElement";
import { edgePointOf } from "@client/canvas/connectors";
import { elementIdOfKey, elementSelectionOf, elementSourceOf } from "@client/canvas/selection";
import { isTextTarget, structuralShortcutFor } from "@client/canvas/interaction";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { CustomShapeRef, CustomShapeState, DiagramDefinition, ShapeBounds, ShapePoint } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers, DiagramSelection } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { innermostKey, useContextConnection, useContextSelection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { type ContextShortcut } from "@client/generated/context-contract_pb";
import { ContextSelectionAction } from "@client/generated/context_pb";
import { useSkosStream } from "./useSkosStream";
import { useViewReport } from "@client/diagrams/useViewReport";
import { ALTERNATE, HIERARCHY, IRI_FALLBACK, MAPPING, type SkosCollection, type SkosConcept, type SkosModel, type SkosScheme } from "./skosModel";

/** A concept's drawn width, in the module's own canvas units - matching the layout's spacing. */
export const CONCEPT_WIDTH = 200;

/** A concept box is one line of label, with room for the notation badge above it. */
const CONCEPT_HEIGHT = 44;

/** A scheme region's header: the title strip the concepts hang beneath. */
const REGION_WIDTH = 260;
const REGION_HEIGHT = 40;

/** An element as the library carries it here: the model element plus what it draws. */
type ConceptElement = DiagramModelElement & { concept: SkosConcept };
type RegionElement = DiagramModelElement & { region: SkosScheme | SkosCollection };

function boxEdgePoint(bounds: ShapeBounds, towards: ShapePoint): ShapePoint {
  const centre = { x: bounds.x + bounds.width / 2, y: bounds.y + bounds.height / 2 };
  return edgePointOf(
    { x: centre.x, y: centre.y, width: bounds.width, height: bounds.height },
    towards.x - centre.x,
    towards.y - centre.y,
  );
}

/**
 * A concept card as a first-class custom shape: label, notation badge, language chip, the
 * blank and label-kind stylings - while hit-testing, anchoring and connecting stay the
 * library's. Anchors carry the gesture's meaning: the TOP anchor files this concept under
 * the one it is dropped on, the SIDE anchor cross-links them.
 */
const conceptShape: CustomShapeRef = {
  customShape: "skos-concept",
  render: (raw, state?: CustomShapeState) => {
    const element = raw as ConceptElement;
    const concept = element.concept;
    const classes = ["skos-concept canvas-element"];
    if (concept.blank) {
      classes.push("skos-concept-blank");
    }
    if (concept.labelKind === ALTERNATE) {
      classes.push("skos-label-alternate");
    }
    if (concept.labelKind === IRI_FALLBACK) {
      classes.push("skos-label-fallback");
    }
    if (state?.selected) {
      classes.push("skos-selected");
    }
    if (state?.connectTarget) {
      classes.push("skos-connect-target canvas-connect-target");
    }

    return (
      <BoxElement
        className={classes.join(" ")}
        x={element.x - CONCEPT_WIDTH / 2}
        y={element.y - CONCEPT_HEIGHT / 2}
        width={CONCEPT_WIDTH}
        height={CONCEPT_HEIGHT}
        label={concept.label}
        boxClassName="skos-concept-box canvas-node"
        labelClassName="skos-concept-label canvas-node-label"
        labelY={CONCEPT_HEIGHT / 2 + 8}
      >
        {concept.notation ? (
          <text className="skos-notation" x={8} y={14}>
            {concept.notation}
          </text>
        ) : null}
        {concept.languageChip ? (
          // A translation gap, visible but quiet: the backend decided this, so the chip
          // cannot disagree with the label beside it.
          <text className="skos-language-chip" x={CONCEPT_WIDTH - 8} y={14}>
            {concept.languageTag}
          </text>
        ) : null}
      </BoxElement>
    );
  },
  edgePoint: boxEdgePoint,
};

/** A scheme's or collection's titled region strip, with the ordered note where it applies. */
const regionShape: CustomShapeRef = {
  customShape: "skos-region",
  render: (raw, state?: CustomShapeState) => {
    const element = raw as RegionElement;
    const region = element.region;
    const ordered = "ordered" in region && region.ordered;
    const classes = ["skos-region canvas-element"];
    if (state?.selected) {
      classes.push("skos-selected");
    }

    return (
      <BoxElement
        className={classes.join(" ")}
        x={element.x - REGION_WIDTH / 2}
        y={element.y - REGION_HEIGHT / 2}
        width={REGION_WIDTH}
        height={REGION_HEIGHT}
        label={"memberCount" in region ? `${region.label} (${region.memberCount})` : region.label}
        boxClassName="skos-region-box canvas-boundary"
        labelClassName="skos-region-label canvas-node-label"
        labelY={REGION_HEIGHT / 2 + 5}
      >
        {ordered ? (
          <text className="skos-region-kind" x={8} y={REGION_HEIGHT - 6}>
            ordered
          </text>
        ) : null}
      </BoxElement>
    );
  },
  edgePoint: boxEdgePoint,
};

/**
 * What a SKOS concept scheme allows, stated once: concepts that drag, select and connect -
 * filing from the top anchor, relating from the side - blank concepts that connect to but
 * never from, regions that drag and select, and the three edge kinds in their own language:
 * hierarchy solid and arrowless (the layering carries the direction), related dashed, mapping
 * dotted with the arrowhead and its property as the label.
 */
const SKOS_DEFINITION: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    {
      id: "concept",
      shape: conceptShape,
      anchors: {
        kind: "sides",
        fractions: [
          { side: "top", at: 0.5, name: "file" },
          { side: "right", at: 0.5, name: "relate" },
        ],
      },
      sizing: "model",
    },
    { id: "blank-concept", shape: conceptShape, anchors: { kind: "edge" }, sizing: "model" },
    { id: "region", shape: regionShape, anchors: { kind: "edge" }, sizing: "model" },
  ],
  relationTypes: [
    {
      id: "hierarchy",
      route: "straight",
      style: { endMarker: "none" },
      className: "skos-edge skos-edge-hierarchy",
      lineClassName: "skos-edge-line",
      hitClassName: "skos-edge-hit",
      endpoints: {
        // The gesture from the TOP anchor: file this concept under the one it lands on.
        source: { elementTypes: ["concept"], anchors: ["file"] },
        target: { elementTypes: ["concept", "blank-concept"], anchors: "edge" },
        allowSelf: false,
      },
    },
    {
      id: "related",
      route: "straight",
      style: { endMarker: "none" },
      className: "skos-edge skos-edge-related",
      lineClassName: "skos-edge-line",
      hitClassName: "skos-edge-hit",
      endpoints: {
        // The gesture from the SIDE anchor: cross-link the two.
        source: { elementTypes: ["concept"], anchors: ["relate"] },
        target: { elementTypes: ["concept", "blank-concept"], anchors: "edge" },
        allowSelf: false,
      },
    },
    {
      id: "mapping",
      route: "straight",
      style: { endMarker: "arrow" },
      label: { placement: "midpoint", offset: -6 },
      className: "skos-edge skos-edge-mapping",
      lineClassName: "skos-edge-line",
      hitClassName: "skos-edge-hit",
      endpoints: {
        // Render-only: mappings come from the document; no anchor starts one by gesture.
        source: { elementTypes: ["concept"], anchors: [] },
        target: { elementTypes: ["concept", "blank-concept"], anchors: "edge" },
        allowSelf: false,
      },
    },
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
});

/**
 * The concept scheme, drawn through the diagram library: schemes as titled regions, concepts
 * under their broader concepts in a layered hierarchy, related and mapping links across it,
 * collections as labeled groups. A drag never writes the SKOS file: `moveElementTo` lands in
 * the registration's `layout:` block as one undoable command, and a connect drag becomes the
 * stateless rel: gesture its starting anchor means.
 */
export function SkosCanvas({ projectId, entryId, path }: DiagramCanvasProps) {
  const { model, loading, failed, reportView, moveElementTo } = useSkosStream(projectId, path);
  const { select, executeAction, executeShortcut } = useContextConnection();
  const { selection, actions } = useContextSelection();
  const toolboxItems = useToolboxItems(projectId, path);
  const [rejection, setRejection] = useState("");
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);

  const selectionKey = innermostKey(selection);
  const selectedId = elementIdOfKey(selectionKey ?? null);

  const diagramModel = useMemo<DiagramModel>(() => {
    const regions = [...model.schemes.values(), ...model.collections.values()].map((region): RegionElement => ({
      id: region.id,
      type: "region",
      x: region.x + REGION_WIDTH / 2,
      y: region.y + REGION_HEIGHT / 2,
      width: REGION_WIDTH,
      height: REGION_HEIGHT,
      label: region.label,
      region,
    }));
    const concepts = [...model.concepts.values()].map((concept): ConceptElement => ({
      id: concept.id,
      type: concept.blank ? "blank-concept" : "concept",
      x: concept.x + CONCEPT_WIDTH / 2,
      y: concept.y + CONCEPT_HEIGHT / 2,
      width: CONCEPT_WIDTH,
      height: CONCEPT_HEIGHT,
      label: concept.label,
      concept,
    }));
    const edges = [...model.edges.values()].map((edge) => ({
      id: edge.id,
      type: edge.kind === HIERARCHY ? "hierarchy" : edge.kind === MAPPING ? "mapping" : "related",
      sourceId: edge.fromElementId,
      targetId: edge.toElementId,
      label: edge.kind === MAPPING ? edge.predicate || undefined : undefined,
    }));
    return { elements: [...regions, ...concepts], connections: edges };
  }, [model]);

  /** The backend's push is the selection; the canvas renders it and never decides. */
  const librarySelection = useMemo<DiagramSelection>(() => {
    if (selectedId === null) {
      return [];
    }
    return [{ kind: model.edges.has(selectedId) ? "connection" : "element", id: selectedId }];
  }, [selectedId, model.edges]);

  const runAction = (actionId: string, sourceId?: string) => {
    void (async () => {
      const outcome = await executeAction(actionId, sourceId ? elementSourceOf(sourceId) : undefined);
      if (!outcome.accepted && outcome.error) {
        setRejection(outcome.error);
      }
    })();
  };

  const runShortcut = (shortcut: ContextShortcut, sourceId: string) => {
    void (async () => {
      const outcome = await executeShortcut(shortcut, elementSourceOf(sourceId));
      if (!outcome.accepted && outcome.error) {
        setRejection(outcome.error);
      }
    })();
  };

  const events: DiagramEventHandlers = {
    onSelectionChanged: ({ selection: next }) =>
      select(next.length > 0 ? elementSelectionOf(entryId, path, next[0].id) : null),
    onElementMoved: ({ elementId, position }) => {
      setRejection("");
      const width = model.concepts.has(elementId) ? CONCEPT_WIDTH : REGION_WIDTH;
      const height = model.concepts.has(elementId) ? CONCEPT_HEIGHT : REGION_HEIGHT;
      // The authored position, raw: the layout block stores what the author placed. A blank
      // node's refusal comes back from the backend with its sentence.
      void (async () => {
        const error = await moveElementTo(elementId, position.x - width / 2, position.y - height / 2);
        if (error) {
          setRejection(error);
        }
      })();
    },
    // Which anchor the drag began at is which gesture it is - one stateless rel: id either
    // way, and the backend refuses ends that are not both asserted concepts.
    onConnectionDrawn: ({ relationType, sourceElementId, targetElementId }) =>
      runAction(relationType === "hierarchy" ? "skos.file-under" : "skos.relate", `rel:${sourceElementId}->${targetElementId}`),
    // A drop names a placement - `new:{x},{y}` under the pointer.
    onElementDropped: ({ elementType, position }) => runAction(elementType, `new:${position.x},${position.y}`),
    onElementDeleted: ({ elementId }) =>
      runShortcut({ key: "Delete", ctrl: false, shift: false, alt: false, meta: false } as ContextShortcut, elementId),
    onConnectionDeleted: ({ connectionId }) =>
      runShortcut({ key: "Delete", ctrl: false, shift: false, alt: false, meta: false } as ContextShortcut, connectionId),
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

  /** F2 travels to the backend as data; Delete is the library's event, handled above. */
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
      <div className="skos-canvas canvas-host skos-canvas-message canvas-host-message">
        <p>This diagram could not be opened.</p>
      </div>
    );
  }

  return (
    <div className="skos-canvas canvas-host" role="application" aria-label="SKOS concept scheme" onKeyDown={onKeyDown}>
      <DiagramCanvas
        definition={SKOS_DEFINITION}
        model={diagramModel}
        events={events}
        selection={librarySelection}
        toolboxItems={toolboxItems}
        context={{
          selectionKey: selectionKey ?? undefined,
          actions,
          selectForMenu: (id) => select(elementSelectionOf(entryId, path, id, ContextSelectionAction.CONTEXT_MENU)),
          executeAction: (actionId) => runAction(actionId, selectedId ?? undefined),
        }}
        ariaLabel="SKOS concept scheme"
        className="skos-surface"
        scrollbarsClassName="skos-scrollbars"
      />
      {model.truncation ? (
        <p className="skos-truncation-banner">
          {`Showing ${model.truncation.shown} of ${model.truncation.total} terms — edits are withheld on this truncated view`}
        </p>
      ) : null}
      {loading ? <p className="skos-status canvas-status">Opening…</p> : null}
      {rejection ? <p className="skos-rejection canvas-rejection">{rejection}</p> : null}
    </div>
  );
}

export type { SkosModel };
