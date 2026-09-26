import { describe, expect, it } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvas } from "./DiagramCanvas";
import type { DiagramDefinition, ElementTypeDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";

/**
 * THE ELEMENT-LEVEL GAPS THE SUFFICIENCY TABLE FOUND, mounted.
 *
 * Each of these covers one register entry, and each names the rows that justified it. The unit
 * tests beside them prove the resolvers; these prove the CANVAS actually draws what a resolver
 * returns, which is a different claim - `binding.test.ts` would pass unchanged if the canvas
 * never called any of it.
 */

// jsdom implements no pointer capture on SVG elements; the gesture arbiter uses it.
SVGElement.prototype.setPointerCapture ??= () => {};
SVGElement.prototype.releasePointerCapture ??= () => {};

function canvasOf(type: Partial<ElementTypeDefinition>, payload?: unknown, model?: DiagramModel) {
  const definition: DiagramDefinition = {
    elementTypes: [
      {
        id: "node",
        shape: "box",
        anchors: { kind: "edge" },
        sizing: "model",
        ...type,
      } as ElementTypeDefinition,
    ],
    relationTypes: [],
    layout: { modes: ["manual"] },
    dragging: "enabled",
  };

  const diagram: DiagramModel = model ?? {
    elements: [{ id: "a", type: "node", x: 0, y: 0, width: 120, height: 40, label: "Alpha", payload }],
    connections: [],
  };

  return render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvas definition={definition} model={diagram} events={{}} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
}

const shapeIn = (container: HTMLElement) => container.querySelector('[data-element-id="a"] .library-shape')!;

describe("declared classes — the gap every one of the 28 rows had (G1)", () => {
  it("puts a stated class on the built-in shape, beside the library's own", () => {
    // Without this a migrated element draws the right geometry in the LIBRARY's colours: no
    // kind colour, no unresolved dash, no selection ring. Twenty-eight renderers depend on a
    // class for their entire visual identity, so this is the difference between a vocabulary
    // that can replace a renderer and one that cannot.
    const { container } = canvasOf({ classNames: [{ className: "helm-node" }] });

    expect(shapeIn(container).getAttribute("class")).toContain("helm-node");
    expect(shapeIn(container).getAttribute("class")).toContain("library-shape");
  });

  it("binds a class to a model field, which is how a kind colour arrives", () => {
    // `helm-node-{payload.kind}` - the library knows no module's kinds and must not have to.
    const { container } = canvasOf(
      { classNames: [{ className: { template: "helm-node-{payload.kind}" } }] },
      { kind: "chart" },
    );

    expect(shapeIn(container).getAttribute("class")).toContain("helm-node-chart");
  });

  it("leaves out a class whose condition fails, rather than emitting an empty one", () => {
    const { container } = canvasOf(
      { classNames: [{ className: "unreadable", when: { path: "payload.unreadable", is: "true" } }] },
      { unreadable: false },
    );

    const classes = shapeIn(container).getAttribute("class")!;
    expect(classes).not.toContain("unreadable");
    // AND NO STRAY SPACES: a joined-with-blanks class list is how `class="library-shape  "`
    // arrives, which is harmless until a selector matches on the exact attribute.
    expect(classes).toBe(classes.trim());
    expect(classes).not.toContain("  ");
  });

  it("reads the canvas's own selection, which 23 of the 28 rows style on (G2)", () => {
    // THE ASSERTION THAT MATTERS: a declared class conditioned on `state.selected` must be
    // absent before the click and present after it. A test that only checked the selected case
    // would pass against a class emitted unconditionally.
    const { container } = canvasOf({ classNames: [{ className: "node-selected", when: { path: "state.selected", is: "true" } }] });

    expect(shapeIn(container).getAttribute("class")).not.toContain("node-selected");

    fireEvent(shapeIn(container), new MouseEvent("pointerdown", { bubbles: true, cancelable: true, button: 0 }));
    fireEvent(shapeIn(container), new MouseEvent("pointerup", { bubbles: true, cancelable: true }));

    expect(shapeIn(container).getAttribute("class")).toContain("node-selected");
  });
});

describe("the shapes and the paint a row could not otherwise reach", () => {
  it("chooses the shape per element from the model (G10, G14)", () => {
    // A wardley mark is a square when it is an anchor and a circle otherwise; a c4 element
    // carries its shape name in the document. One type drawing itself differently per element -
    // not four types, which would fragment the notation's own vocabulary.
    const selection = { path: "payload.kind", cases: { anchor: "diamond", submap: "double-ellipse" }, fallback: "ellipse" } as const;
    const anchor = canvasOf({ shape: selection }, { kind: "anchor" });
    expect(anchor.container.querySelector('[data-element-id="a"] polygon')).not.toBeNull();

    const submap = canvasOf({ shape: selection }, { kind: "submap" });
    expect(submap.container.querySelectorAll('[data-element-id="a"] ellipse')).toHaveLength(2);

    // An unlisted value takes the fallback rather than drawing nothing: an element always draws.
    const plain = canvasOf({ shape: selection }, { kind: "component" });
    expect(plain.container.querySelectorAll('[data-element-id="a"] ellipse')).toHaveLength(1);
  });

  it("draws no body for a shape of `none`, and still draws its labels (G8)", () => {
    // Rows 2, 8, 14 and 25: a connector stub, a loop badge, a bare annotation. Before this,
    // `shape` was required and every built-in drew something.
    const { container } = canvasOf({ shape: "none", labels: [{ text: { path: "payload.text" } }] }, { text: "when: never" });

    expect(container.querySelector('[data-element-id="a"] rect.library-shape')).toBeNull();
    expect(container.textContent).toContain("when: never");
  });

  it("takes its paint from the model where the document sets it (G10)", () => {
    // c4 stores background and text colour per element, because the notation lets an author
    // set them. `style` names tokens chosen once for the type and cannot say that.
    const { container } = canvasOf({ shape: "box", boundStyle: { fill: { path: "payload.background" } } }, { background: "#1168bd" });

    // The paint lands on the shape's own group as an inline style, which is where every other
    // built-in's `style={paint}` goes - asserted on the subtree rather than on one node, so a
    // change of which node carries it does not silently pass.
    expect(container.querySelector('[data-element-id="a"] [style*="1168bd"]')).not.toBeNull();
  });

  it("draws the element's own tooltip, which five renderers write by hand (G7)", () => {
    const { container } = canvasOf(
      { tooltip: { parts: [{ template: "Play {payload.name}" }, { template: "hosts: {payload.hosts}", when: { path: "payload.hosts", is: "present" } }], join: " — " } },
      { name: "web", hosts: "all" },
    );

    expect(container.querySelector('[data-element-id="a"] title')!.textContent).toBe("Play web — hosts: all");
  });

  it("drops the tooltip's absent tail with its separator, not leaving a dangling dash", () => {
    const { container } = canvasOf(
      { tooltip: { parts: [{ template: "Play {payload.name}" }, { template: "hosts: {payload.hosts}", when: { path: "payload.hosts", is: "present" } }], join: " — " } },
      { name: "web" },
    );

    expect(container.querySelector('[data-element-id="a"] title')!.textContent).toBe("Play web");
  });
});

/**
 * EDGE ATTACHMENT ON ONE AXIS — register entry G20, and the guard exists because a SABOTAGE
 * found the module's own suite blind to it.
 *
 * Three canvases attach on the left or right side facing the other end, whatever the angle,
 * because the notation reads left-to-right and a connector leaving through the top of a box
 * reads as a different relation. Each spells that in a custom shape's `edgePoint` today.
 *
 * The reference migration declared it and every one of dependency-graph's forty-five tests
 * still passed when it was removed again - that module routes with its own path builder, which
 * takes the end BOUNDS and picks its own anchors, so the attachment point never reaches the
 * drawn line there. Rows 12 and 26 do not have that shelter, and this is what stands in for
 * the coverage their suites will need.
 */
describe("edge attachment constrained to an axis (G20)", () => {
  function twoNodes(edgeSides?: "all" | "horizontal" | "vertical") {
    const definition: DiagramDefinition = {
      elementTypes: [{ id: "node", shape: "box", anchors: { kind: "edge", ...(edgeSides ? { edgeSides } : {}) }, sizing: "model" }],
      relationTypes: [{ id: "to", route: "straight", endpoints: { source: { elementTypes: ["node"] }, target: { elementTypes: ["node"] }, allowSelf: false } }],
      layout: { modes: ["manual"] },
      dragging: "enabled",
    };

    // Beta sits well BELOW and a little to the right: a true edge intersection leaves through
    // the bottom, and a horizontal constraint leaves through the right side.
    const model: DiagramModel = {
      elements: [
        { id: "a", type: "node", x: 0, y: 0, width: 100, height: 40 },
        { id: "b", type: "node", x: 40, y: 400, width: 100, height: 40 },
      ],
      connections: [{ id: "a->b", type: "to", sourceId: "a", targetId: "b" }],
    };

    const { container } = render(
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvas definition={definition} model={model} events={{}} />
        </DiagramToolboxProvider>
      </DiagramViewProvider>,
    );

    return container.querySelector('[data-connection-id="a->b"] path.canvas-connection-line')!.getAttribute("d")!;
  }

  it("leaves through the bottom by default, and through the side when the type says horizontal", () => {
    // Alpha is 100x40 centred on the origin. Unconstrained, a line toward a box far below
    // leaves at the bottom edge, y = 20. Constrained, it leaves at the right edge, x = 50.
    expect(twoNodes()).toMatch(/^M [\d.-]+ 20 /);
    expect(twoNodes("horizontal")).toMatch(/^M 50 0 /);
  });

  it("leaves through the top or bottom when the type says vertical", () => {
    expect(twoNodes("vertical")).toMatch(/^M 0 20 /);
  });
});

describe("a declared label's anchor reaches the screen - it is part of the geometry, not a style choice", () => {
  // The layout computes a label's x FOR an anchor: `start` at the left inset, `middle` at the
  // centre, `end` at the right inset. Written as an SVG presentation attribute, that anchor LOST
  // to every stylesheet rule - an attribute sits below any author rule in the cascade, even a
  // single class - so the library's own `.library-element-label { text-anchor: middle }` centred
  // every start- and end-aligned line on its inset, and a module rule re-anchored the centred
  // ones. Found in a browser on three diagram types the user reported (databricks, SHACL) and one
  // nobody had (azure-pipeline's stage name, whose earlier fix never took visible effect). Every
  // test before these read the ATTRIBUTE, which is exactly the half that cannot see it.
  const anchorOf = (text: Element) => (text as SVGTextElement).style.getPropertyValue("text-anchor");

  it.each(["start", "middle", "end"] as const)("carries a declared `%s` as an inline style, which no stylesheet rule outranks", (align) => {
    const { container } = canvasOf({ labels: [{ text: { path: "payload.text" }, align, className: "a-line" }] }, { text: "Alpha" });

    expect(anchorOf(container.querySelector('[data-element-id="a"] text.a-line')!)).toBe(align);
  });

  it("carries the default `middle` too, so a module rule cannot re-anchor a centred line", () => {
    // The databricks name: centred by the layout, left-anchored by `.databricks-label`, so it
    // STARTED at the centre and ran off the right edge. Leaving `middle` to CSS is what let it.
    const { container } = canvasOf({ labels: [{ text: { path: "payload.text" }, className: "a-line" }] }, { text: "Alpha" });

    expect(anchorOf(container.querySelector('[data-element-id="a"] text.a-line')!)).toBe("middle");
  });

  it("keeps the declared anchor against the library's own rule and a module rule that disagrees", () => {
    // The cascade itself, as the browser resolves it: the library's rule and a module's, both
    // present, and the computed anchor asked for - not the markup.
    const sheet = document.createElement("style");
    sheet.textContent = ".library-element-label { text-anchor: middle; } .a-module-label { text-anchor: start; }";
    document.head.appendChild(sheet);
    try {
      const { container } = canvasOf(
        {
          labels: [
            { text: { path: "payload.right" }, align: "end", className: "a-right" },
            { text: { path: "payload.centre" }, className: "a-module-label" },
          ],
        },
        { right: "Right", centre: "Centre" },
      );

      expect(getComputedStyle(container.querySelector("text.a-right")!).getPropertyValue("text-anchor")).toBe("end");
      expect(getComputedStyle(container.querySelector("text.a-module-label")!).getPropertyValue("text-anchor")).toBe("middle");
    } finally {
      sheet.remove();
    }
  });
});

/**
 * `visible: false` on a type's anchors: no dot at rest or on hover, and a connection can still be
 * drawn from and to it - the valid target still lights up, so the gesture is not blind.
 */
describe("invisible anchors", () => {
  function twoNodes(visible: boolean | undefined) {
    const definition: DiagramDefinition = {
      elementTypes: [
        {
          id: "node",
          shape: "box",
          anchors: { kind: "compass", positions: ["e", "w"], ...(visible === undefined ? {} : { visible }) },
          sizing: "model",
        },
      ],
      relationTypes: [
        { id: "link", route: "straight", endpoints: { source: { elementTypes: ["node"] }, target: { elementTypes: ["node"] }, allowSelf: false } },
      ],
      layout: { modes: ["manual"] },
      dragging: "enabled",
    };
    const model: DiagramModel = {
      elements: [
        { id: "a", type: "node", x: 0, y: 0, width: 100, height: 40, label: "A" },
        { id: "b", type: "node", x: 300, y: 0, width: 100, height: 40, label: "B" },
      ],
      connections: [],
    };
    return render(
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvas definition={definition} model={model} events={{}} />
        </DiagramToolboxProvider>
      </DiagramViewProvider>,
    );
  }

  const pointer = (type: string, init: MouseEventInit) => new MouseEvent(type, { bubbles: true, cancelable: true, ...init });

  it("draws no anchor dot, at rest or while the pointer rests on the element", () => {
    // Arrange, act.
    const { container } = twoNodes(false);
    fireEvent.pointerEnter(container.querySelector('[data-element-id="a"]')!);

    // Assert: no dot anywhere - and the visible default still draws them, so the check is live.
    expect(container.querySelectorAll(".library-anchor")).toHaveLength(0);
    expect(twoNodes(undefined).container.querySelectorAll(".library-anchor")).toHaveLength(4);
  });

  it("still starts a connection and still highlights the valid target during the drag", () => {
    // Arrange.
    const { container } = twoNodes(false);
    const east = container.querySelector('[data-element-id="a"] [data-anchor="e"]')!;
    expect(east).not.toBeNull();

    // Act: A's east anchor (50) dragged over B (300).
    fireEvent(east, pointer("pointerdown", { button: 0, clientX: 50, clientY: 0 }));
    fireEvent(east, pointer("pointermove", { clientX: 300, clientY: 0 }));

    // Assert.
    expect(container.querySelector('[data-element-id="b"]')!.classList.contains("library-connect-target")).toBe(true);
  });
});
