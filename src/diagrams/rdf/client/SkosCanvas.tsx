import { useMemo, useState } from "react";

import { elementSourceOf } from "@client/canvas/selection";
import { contextShortcutOf } from "@client/canvas/interaction";
import { DiagramCanvas } from "@client/canvas/library/DiagramCanvas";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition, ShapeBounds } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramEventHandlers } from "@client/canvas/library/api/diagramEvents";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { useContextConnection } from "@client/shell/context/ContextConnectionProvider";
import { useToolboxItems } from "@client/shell/panels/useToolboxItems";
import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { type ContextShortcut } from "@client/generated/context-contract_pb";
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

/**
 * Which key the backend knows each declared action by.
 *
 * The library dispatches an id; the backend's context table is keyed by keystroke. One map,
 * in one place, rather than a keystroke built at each call site.
 */
const BACKEND_KEYS: Readonly<Record<string, string>> = { rename: "F2", delete: "Delete" };

/** An element as the library carries it here: the model element plus what it draws. */
type ConceptElement = DiagramModelElement & { concept: SkosConcept };
type RegionElement = DiagramModelElement & { region: SkosScheme | SkosCollection };




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
      shape: "box",
      classNames: [
        { className: "skos-concept canvas-element" },
        { className: "skos-concept-blank", when: { path: "payload.blank", is: "true" } },
        { className: "skos-label-alternate", when: { path: "payload.labelKindAlternate", is: "true" } },
        { className: "skos-label-fallback", when: { path: "payload.labelKindFallback", is: "true" } },
        { className: "skos-concept-box canvas-node", on: "shape" },
      ],
      labels: [
        {
          text: { path: "payload.label" },
          anchorTo: "top",
          offset: { x: 0, y: CONCEPT_HEIGHT / 2 + 8 },
          truncate: true,
          className: "skos-concept-label canvas-node-label",
        },
        {
          text: { path: "payload.notation" },
          when: { path: "payload.notation", is: "non-empty" },
          anchorTo: "top",
          offset: { x: 0, y: 14 },
          align: "start",
          insetX: 8,
          className: "skos-notation",
        },
        {
          // A translation gap, visible but quiet: the backend decided this, so the chip cannot
          // disagree with the label beside it.
          text: { path: "payload.languageTag" },
          when: { path: "payload.languageChip", is: "true" },
          anchorTo: "top",
          offset: { x: 0, y: 14 },
          align: "end",
          insetX: 8,
          className: "skos-language-chip",
        },
      ],
      anchors: {
        kind: "sides",
        fractions: [
          { side: "top", at: 0.5, name: "file" },
          { side: "right", at: 0.5, name: "relate" },
        ],
      },
      sizing: "model",
    },
    {
      id: "blank-concept",
      shape: "box",
      classNames: [
        { className: "skos-concept canvas-element" },
        { className: "skos-concept-blank", when: { path: "payload.blank", is: "true" } },
        { className: "skos-label-alternate", when: { path: "payload.labelKindAlternate", is: "true" } },
        { className: "skos-label-fallback", when: { path: "payload.labelKindFallback", is: "true" } },
        { className: "skos-concept-box canvas-node", on: "shape" },
      ],
      labels: [
        {
          text: { path: "payload.label" },
          anchorTo: "top",
          offset: { x: 0, y: CONCEPT_HEIGHT / 2 + 8 },
          truncate: true,
          className: "skos-concept-label canvas-node-label",
        },
        {
          text: { path: "payload.notation" },
          when: { path: "payload.notation", is: "non-empty" },
          anchorTo: "top",
          offset: { x: 0, y: 14 },
          align: "start",
          insetX: 8,
          className: "skos-notation",
        },
        {
          // A translation gap, visible but quiet: the backend decided this, so the chip cannot
          // disagree with the label beside it.
          text: { path: "payload.languageTag" },
          when: { path: "payload.languageChip", is: "true" },
          anchorTo: "top",
          offset: { x: 0, y: 14 },
          align: "end",
          insetX: 8,
          className: "skos-language-chip",
        },
      ],
      anchors: { kind: "edge" },
      sizing: "model",
    },
    {
      id: "region",
      shape: "box",
      classNames: [
        { className: "skos-region canvas-element" },
        { className: "skos-region-box canvas-boundary", on: "shape" },
      ],
      labels: [
        {
          // `label (memberCount)` for a collection, the label alone for a scheme - one line
          // with a conditional tail, which is what parts are for.
          text: {
            parts: [
              { path: "payload.label" },
              { template: "({payload.memberCount})", when: { path: "payload.memberCount", is: "present" } },
            ],
            join: " ",
          },
          anchorTo: "top",
          offset: { x: 0, y: REGION_HEIGHT / 2 + 5 },
          truncate: true,
          className: "skos-region-label canvas-node-label",
        },
        {
          text: { template: "ordered" },
          when: { path: "payload.ordered", is: "true" },
          anchorTo: "top",
          offset: { x: 0, y: REGION_HEIGHT - 6 },
          align: "start",
          insetX: 8,
          className: "skos-region-kind",
        },
      ],
      anchors: { kind: "edge" },
      sizing: "model",
    },
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
  // WHAT THIS READING OFFERS, AND WHAT INVOKES IT. F2 was a hand-written key list in this
  // canvas and the delete was a keystroke it built by hand to describe a gesture the library
  // had already handed it. Declared, the library derives the key set and dispatches an action
  // id; the backend still holds the key-to-action table, which is why the handler says which
  // shortcut each action travels as.
  actions: [
    { id: "rename", invokedBy: [{ kind: "shortcut", key: "F2" }], appliesTo: [{ kind: "element" }] },
    { id: "delete", invokedBy: [{ kind: "gesture", gesture: "delete" }], appliesTo: [{ kind: "element" }, { kind: "connection" }] },
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
  const { executeAction, executeShortcut } = useContextConnection();
  const toolboxItems = useToolboxItems(projectId, path);
  const [rejection, setRejection] = useState("");
  const [viewport, setViewport] = useState<ShapeBounds | null>(null);


  const diagramModel = useMemo<DiagramModel>(() => {
    const regions = [...model.schemes.values(), ...model.collections.values()].map((region): RegionElement => ({
      id: region.id,
      type: "region",
      x: region.x + REGION_WIDTH / 2,
      y: region.y + REGION_HEIGHT / 2,
      width: REGION_WIDTH,
      height: REGION_HEIGHT,
      label: region.label,
      payload: {
        label: region.label,
        ...("memberCount" in region ? { memberCount: region.memberCount } : {}),
        ordered: "ordered" in region && region.ordered,
      },
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
      payload: {
        label: concept.label,
        blank: concept.blank,
        notation: concept.notation,
        languageChip: concept.languageChip,
        languageTag: concept.languageTag,
        labelKindAlternate: concept.labelKind === ALTERNATE,
        labelKindFallback: concept.labelKind === IRI_FALLBACK,
      },
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
    // The declared actions, answered as the shortcuts the backend has always known them by.
    onActionInvoked: ({ actionId, targetId }) => {
      const key = BACKEND_KEYS[actionId];
      if (key !== undefined && targetId !== undefined) {
        runShortcut(contextShortcutOf(key), targetId);
      }
    },
    // Selection is the library's (centralized-selection); a menu action it ran and the backend
    // refused comes back here, for the same rejection line every other refusal uses.
    onActionRefused: ({ message }) => setRejection(message),
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


  if (failed) {
    return (
      <div className="skos-canvas canvas-host skos-canvas-message canvas-host-message">
        <p>This diagram could not be opened.</p>
      </div>
    );
  }

  return (
    <div className="skos-canvas canvas-host" role="application" aria-label="SKOS concept scheme">
      <DiagramCanvas
        definition={SKOS_DEFINITION}
        model={diagramModel}
        events={events}
        source={{ entryId, path }}
        toolboxItems={toolboxItems}
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
