import { describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvas, routePath } from "./DiagramCanvas";
import {
  BUILT_IN_ROUTES,
  type DiagramDefinition,
  type RelationTypeDefinition,
} from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import {
  arcPath,
  horizontalBezierPath,
  orthogonalPath,
  polylinePath,
  quadraticBezierPath,
  splinePath,
  straightPath,
} from "../connectors";
import { DiagramViewProvider, useDiagramViewControls } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider, useDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";

/**
 * The library's three standing guards (diagram-library Requirement 10.1), each mounting
 * rather than grepping - a grep over canvas sources has already reported three correct
 * databricks canvases as offenders here - and each carrying a named-member canary so its
 * population can never quietly go empty (Requirement 10.2).
 */

function pointer(type: string, init: MouseEventInit) {
  return new MouseEvent(type, { bubbles: true, cancelable: true, ...init });
}

SVGElement.prototype.setPointerCapture ??= () => {};
SVGElement.prototype.releasePointerCapture ??= () => {};

function definitionOf(): DiagramDefinition {
  return {
    elementTypes: [
      { id: "service", shape: "box", anchors: { kind: "compass", positions: ["e", "w"] }, sizing: "model" },
      { id: "store", shape: "cylinder", anchors: { kind: "edge" }, sizing: "model" },
    ],
    relationTypes: [
      {
        id: "calls",
        route: "straight",
        // The forbidden pair the first guard drives at: a store may be the target of nothing.
        endpoints: { source: { elementTypes: ["service"] }, target: { elementTypes: ["service"] }, allowSelf: false },
      },
    ],
    layout: { modes: ["manual"] },
    dragging: "enabled",
  };
}

function modelOf(): DiagramModel {
  return {
    elements: [
      { id: "a", type: "service", x: 0, y: 0, width: 100, height: 40, label: "Alpha" },
      { id: "s", type: "store", x: 150, y: 200, width: 100, height: 60, label: "Store" },
    ],
    connections: [],
  };
}

describe("the library's standing guards", () => {
  it("connection rules are enforced at gesture time: a forbidden connect cannot complete, mounted", () => {
    // The canary: the forbidden pair is a NAMED pair, so an edited definition that quietly
    // stopped declaring the store would fail here rather than hollowing the guard.
    const definition = definitionOf();
    expect(definition.elementTypes.map((type) => type.id)).toContain("store");
    expect(definition.relationTypes[0].endpoints.target.elementTypes).not.toContain("store");

    const onConnectionDrawn = vi.fn();
    const { container } = render(
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvas definition={definition} model={modelOf()} events={{ onConnectionDrawn }} />
        </DiagramToolboxProvider>
      </DiagramViewProvider>,
    );

    // A full connect gesture from the service's east anchor onto the store's body.
    const anchor = container.querySelector('[data-element-id="a"] [data-anchor="e"]')!;
    fireEvent(anchor, pointer("pointerdown", { button: 0, clientX: 50, clientY: 0 }));
    fireEvent(anchor, pointer("pointermove", { clientX: 150, clientY: 200 }));
    fireEvent(anchor, pointer("pointerup", { clientX: 150, clientY: 200 }));

    expect(onConnectionDrawn).not.toHaveBeenCalled();
  });

  it("every built-in route renders through the shared geometry: the drawn path IS the connectors.ts path", () => {
    // The canary: the walked population is the exported list, pinned non-empty by name.
    expect(BUILT_IN_ROUTES).toContain("straight");
    expect(BUILT_IN_ROUTES).toContain("spline");

    const from = { x: 50, y: 0 };
    const to = { x: 250, y: 100 };
    const waypoints = [{ x: 120, y: 80 }];
    const expected: Record<string, string> = {
      straight: straightPath(from, to),
      polyline: polylinePath(from, to, waypoints),
      orthogonal: orthogonalPath(from, to, 0),
      arc: arcPath(from, to),
      "quadratic-bezier": quadraticBezierPath(from, to),
      "cubic-bezier": horizontalBezierPath(from, to),
      spline: splinePath(from, to, waypoints),
    };

    for (const route of BUILT_IN_ROUTES) {
      const relation: RelationTypeDefinition = {
        id: "r",
        route,
        endpoints: { source: { elementTypes: ["service"] }, target: { elementTypes: ["service"] }, allowSelf: false },
      };
      // The route table is the single seam every drawn connection passes through; a builder
      // constructed anywhere else would disagree with the shared geometry right here.
      expect(routePath(relation, from, to, waypoints, 0), route).toBe(expected[route]);
    }
  });

  it("a library canvas registers view controls and toolbox as a pair, by mounting alone", () => {
    let controls: unknown = null;
    let items: { id: string }[] | null = null;
    function Probe() {
      controls = useDiagramViewControls();
      items = useDiagramToolbox() as { id: string }[] | null;
      return null;
    }

    render(
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvas definition={definitionOf()} model={modelOf()} events={{}} />
          <Probe />
        </DiagramToolboxProvider>
      </DiagramViewProvider>,
    );

    // Both, or the mount itself is broken - the half-registration class cannot occur. The
    // canary names a member: the toolbox derived from the definition must offer the service.
    expect(controls).not.toBeNull();
    expect(items!.map((item) => item.id)).toContain("service");
  });
});
