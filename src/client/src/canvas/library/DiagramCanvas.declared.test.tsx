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
