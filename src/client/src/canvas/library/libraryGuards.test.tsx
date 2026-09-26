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

import { readFileSync, readdirSync, statSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { pointer } from "@client/canvas/library/testing/canvasHarness";

/**
 * The library's three standing guards (diagram-library Requirement 10.1), each mounting
 * rather than grepping - a grep over canvas sources has already reported three correct
 * databricks canvases as offenders here - and each carrying a named-member canary so its
 * population can never quietly go empty (Requirement 10.2).
 */

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

  it("every migrated module's registered canvases render through the library", () => {
    // The per-module half of the pair claim (diagram-library-adoption Requirement 5.2): the
    // mounted case above proves DiagramCanvas registers view controls and toolbox as a pair
    // by construction, so what remains per module is that its REGISTERED canvases actually
    // go through DiagramCanvas - then the pair follows for every real registration. That
    // linkage is read from the sources (a central mount of every module would drag every
    // module's stream mocks into this file; the modules' own mounted tests carry those), so
    // its limit is the family's: it reads text. Adoption is complete and the exclusion
    // mechanism is retired, so every module with a registration is walked; a canvas may
    // satisfy the check through a same-module wrapper (databricks' three thin readings over
    // one inner canvas).
    let root = dirname(fileURLToPath(import.meta.url));
    for (let depth = 0; depth < 12 && !(statSync(join(root, "diagrams"), { throwIfNoEntry: false })?.isDirectory() === true && statSync(join(root, ".editorconfig"), { throwIfNoEntry: false })?.isFile() === true); depth++) {
      root = dirname(root);
    }

    const offenders: string[] = [];
    const diagrams = join(root, "diagrams");
    const migrated = readdirSync(diagrams, { withFileTypes: true })
      .filter((entry) => entry.isDirectory())
      .filter((entry) => statSync(join(diagrams, entry.name, "client", "register.ts"), { throwIfNoEntry: false })?.isFile() === true)
      .map((entry) => entry.name);

    // The canary: the two reference migrations are walked members, by name.
    expect(migrated).toContain("rdf");
    expect(migrated).toContain("timeline");

    for (const module of migrated) {
      const client = join(diagrams, module, "client");
      const register = readFileSync(join(client, "register.ts"), "utf-8");
      for (const [, , imported] of register.matchAll(/import\s+\{\s*(\w+Canvas)\s*\}\s+from\s+"\.\/(\w+)"/g)) {
        const file = `${imported}.tsx`;
        const source = readFileSync(join(client, file), "utf-8");
        const onLibrary = source.includes("@client/canvas/library") || /from\s+"\.\/\w*Canvas"/.test(source);
        if (!onLibrary) {
          offenders.push(`${module}/client/${file}: registered but does not render through the library`);
        }
      }
    }

    expect(offenders, offenders.join("\n")).toEqual([]);
  });
});
