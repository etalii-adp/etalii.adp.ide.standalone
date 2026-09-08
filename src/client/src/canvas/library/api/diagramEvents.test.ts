import { describe, expect, it, vi } from "vitest";
import {
  dispatchDiagramEvent,
  type DiagramEvent,
  type DiagramEventHandlers,
  type DiagramSelection,
} from "./diagramEvents";
import { effectiveDefinition, type DiagramRuntimeConfig } from "./diagramRuntimeConfig";
import type { DiagramDefinition } from "../definition/diagramDefinition";

/**
 * One of every event, so the dispatch walk below is a population with named members rather
 * than whatever a loop happened to construct (the canary idiom, Requirement 10.2).
 */
const oneOfEvery: readonly DiagramEvent[] = [
  { kind: "element-dropped", elementType: "node", position: { x: 10, y: 20 } },
  { kind: "element-deleted", elementId: "a" },
  { kind: "element-moved", elementId: "a", position: { x: 30, y: 40 } },
  { kind: "element-resized", elementId: "a", side: "right", bounds: { x: 0, y: 0, width: 120, height: 40 } },
  { kind: "connection-drawn", relationType: "link", sourceElementId: "a", targetElementId: "b", sourceAnchor: "e" },
  { kind: "connection-released-on-empty", relationType: "link", sourceElementId: "a", position: { x: 9, y: 9 } },
  { kind: "connection-deleted", connectionId: "a->b" },
  { kind: "connection-adjusted", connectionId: "a->b", waypoints: [{ x: 1, y: 2 }] },
  { kind: "selection-changed", selection: [{ kind: "element", id: "a" }] },
  { kind: "label-commit-requested", target: { kind: "connection", id: "a->b" }, value: "renamed" },
  { kind: "view-changed", viewport: { x: 0, y: 0, width: 800, height: 600 } },
  { kind: "layout-mode-changed", mode: "tree" },
  { kind: "action-invoked", actionId: "mindmap.add-child", targetKind: "element", targetId: "a" },
];

describe("the diagram event surface", () => {
  it("a full handler map over the union typechecks, and dispatch reaches every member's handler", () => {
    // The task's acceptance in executable form: this map compiling IS the typecheck claim,
    // and dispatching one of every event proves the name mapping (kebab kind -> onPascal
    // handler) holds for each member rather than for the ones somebody remembered.
    const seen: string[] = [];
    const handlers: Required<DiagramEventHandlers> = {
      onElementDropped: (event) => seen.push(event.kind),
      onElementDeleted: (event) => seen.push(event.kind),
      onElementMoved: (event) => seen.push(event.kind),
      onElementResized: (event) => seen.push(event.kind),
      onConnectionDrawn: (event) => seen.push(event.kind),
      onConnectionReleasedOnEmpty: (event) => seen.push(event.kind),
      onConnectionDeleted: (event) => seen.push(event.kind),
      onConnectionAdjusted: (event) => seen.push(event.kind),
      onSelectionChanged: (event) => seen.push(event.kind),
      onLabelCommitRequested: (event) => seen.push(event.kind),
      onViewChanged: (event) => seen.push(event.kind),
      onLayoutModeChanged: (event) => seen.push(event.kind),
      onActionInvoked: (event) => seen.push(event.kind),
    };

    for (const event of oneOfEvery) {
      dispatchDiagramEvent(handlers, event);
    }

    expect(seen).toEqual(oneOfEvery.map((event) => event.kind));
    // The canaries: the members the design names must be in the population.
    expect(seen).toContain("connection-drawn");
    expect(seen).toContain("label-commit-requested");
    // The declarative-modules member: an action reaches its handler by ID, never as a keystroke.
    expect(seen).toContain("action-invoked");
  });

  it("an event nobody handles is ignored, because a request may be declined by silence", () => {
    const handlers: DiagramEventHandlers = {};

    expect(() => dispatchDiagramEvent(handlers, oneOfEvery[0])).not.toThrow();
  });

  it("dispatch routes to the matching handler and no other", () => {
    const onElementMoved = vi.fn();
    const onElementDeleted = vi.fn();

    dispatchDiagramEvent({ onElementMoved, onElementDeleted }, { kind: "element-moved", elementId: "a", position: { x: 1, y: 2 } });

    expect(onElementMoved).toHaveBeenCalledExactlyOnceWith({ kind: "element-moved", elementId: "a", position: { x: 1, y: 2 } });
    expect(onElementDeleted).not.toHaveBeenCalled();
  });

  it("the selection is set-shaped but carries one member until multi-select lands", () => {
    // Requirement 7.4 in a type: a set today with one entry. When multi-select lands, the
    // canvas starts putting more members in and nothing reading this type changes shape.
    const selection: DiagramSelection = [{ kind: "element", id: "a" }];

    expect(selection).toHaveLength(1);
  });
});

describe("the runtime configuration", () => {
  const definition: DiagramDefinition = {
    elementTypes: [{ id: "node", shape: "box", anchors: { kind: "edge" }, sizing: "model" }],
    relationTypes: [],
    layout: { modes: ["manual"] },
    dragging: "enabled",
  };

  it("returns the definition untouched when nothing overrides it", () => {
    expect(effectiveDefinition(definition, undefined)).toBe(definition);
    expect(effectiveDefinition(definition, { activeTool: "link" })).toBe(definition);
  });

  it("a dragging override reconfigures without touching the module's own object", () => {
    const config: DiagramRuntimeConfig = { dragging: "disabled" };

    const effective = effectiveDefinition(definition, config);

    expect(effective.dragging).toBe("disabled");
    expect(definition.dragging).toBe("enabled");
  });

  it("definition overrides merge over the module's statement", () => {
    const config: DiagramRuntimeConfig = { definitionOverrides: { layout: { modes: ["manual", "tree"] } } };

    const effective = effectiveDefinition(definition, config);

    expect(effective.layout.modes).toEqual(["manual", "tree"]);
    expect(effective.elementTypes).toBe(definition.elementTypes);
  });
});
