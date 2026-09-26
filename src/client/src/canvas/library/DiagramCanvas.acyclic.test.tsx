import { describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel, DiagramModelConnection } from "./api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { pointer } from "@client/canvas/library/testing/canvasHarness";

/**
 * A declared cycle rule, refused under the pointer rather than after the drop.
 *
 * <b>The rule is a THIRD check beside the type check and the cardinality one, and the tests here
 * are shaped around that word.</b> Three checks that each refuse on their own is not the same code
 * as three checks consulted in order until one passes, and the difference is invisible on every
 * example where two of them agree. So the last test drives one refusal that only cardinality can
 * explain and one that only the walk can, in a single definition that declares both.
 */

function drag(target: Element, fromX: number, fromY: number, toX: number, toY: number) {
  fireEvent(target, pointer("pointerdown", { button: 0, clientX: fromX, clientY: fromY }));
  fireEvent(target, pointer("pointermove", { clientX: toX, clientY: toY }));
  fireEvent(target, pointer("pointerup", { clientX: toX, clientY: toY }));
}

/**
 * Four nodes in a row, at centres 0, 300, 600 and 900, each 100 wide - so every east anchor is 50
 * to the right of its centre and every west anchor 50 to the left, and no release point is nearer
 * the wrong element's anchor than the right one's.
 */
function modelOf(connections: readonly DiagramModelConnection[]): DiagramModel {
  return {
    elements: [
      { id: "a", type: "node", x: 0, y: 0, width: 100, height: 40, label: "A" },
      { id: "b", type: "node", x: 300, y: 0, width: 100, height: 40, label: "B" },
      { id: "c", type: "node", x: 600, y: 0, width: 100, height: 40, label: "C" },
      { id: "d", type: "node", x: 900, y: 0, width: 100, height: 40, label: "D" },
    ],
    connections: [...connections],
  };
}

const flows = (id: string, sourceId: string, targetId: string): DiagramModelConnection =>
  ({ id, type: "flows", sourceId, targetId });
const notes = (id: string, sourceId: string, targetId: string): DiagramModelConnection =>
  ({ id, type: "notes", sourceId, targetId });

/**
 * Two relations over one element type, and `flows` declared first so it is the one a drag from an
 * anchor draws: the library takes the first relation whose source constraint admits the type.
 */
function definitionOf(overrides: Partial<DiagramDefinition> = {}, maxFromSource?: number): DiagramDefinition {
  const endpoints = {
    source: { elementTypes: ["node"] },
    target: { elementTypes: ["node"] },
    allowSelf: false,
    ...(maxFromSource === undefined ? {} : { cardinality: { maxFromSource } }),
  };

  return {
    elementTypes: [{ id: "node", shape: "box", anchors: { kind: "compass", positions: ["e", "w"] }, sizing: "model" }],
    relationTypes: [
      { id: "flows", route: "straight", endpoints },
      { id: "notes", route: "straight", endpoints: { source: { elementTypes: ["node"] }, target: { elementTypes: ["node"] }, allowSelf: false } },
    ],
    layout: { modes: ["manual"] },
    dragging: "enabled",
    ...overrides,
  };
}

function renderCanvas(definition: DiagramDefinition, model: DiagramModel) {
  const onConnectionDrawn = vi.fn();
  const result = render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={definition} model={model} events={{ onConnectionDrawn }} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );

  return { ...result, onConnectionDrawn };
}

const anchorOn = (container: HTMLElement, id: string, name: string) =>
  container.querySelector(`[data-element-id="${id}"] [data-anchor="${name}"]`)!;
const elementOn = (container: HTMLElement, id: string) => container.querySelector(`[data-element-id="${id}"]`)!;

/** C's west anchor, dragged into another element's box. C is at 600, its west anchor at 550. */
function dragFromC(container: HTMLElement, toX: number) {
  drag(anchorOn(container, "c", "w"), 550, 0, toX, 0);
}

const ACYCLIC = { acyclic: [{ relationTypes: ["flows"] }] } as const;

describe("a relation in an acyclic set cannot close a cycle", () => {
  it("refuses C to A where A already reaches C, and says so under the pointer", () => {
    // Arrange: A -> B -> C, all `flows`, which the definition declares acyclic.
    const { container, onConnectionDrawn } = renderCanvas(definitionOf(ACYCLIC), modelOf([flows("1", "a", "b"), flows("2", "b", "c")]));

    // Act: mid-drag first, because a rule taught after the drop is a rule taught too late.
    const anchor = anchorOn(container, "c", "w");
    fireEvent(anchor, pointer("pointerdown", { button: 0, clientX: 550, clientY: 0 }));
    fireEvent(anchor, pointer("pointermove", { clientX: 40, clientY: 0 }));

    // Assert: forbidden under the pointer, then refused on release.
    //
    // The criterion says such a target shall be neither offered nor highlighted, and it is worth
    // saying why that is ONE assertion here rather than two. `library-connect-target` and
    // `library-connect-forbidden` are painted from a single `connectHighlight` value, so they are
    // mutually exclusive by construction: an added `expect(...contains("library-connect-target"))
    // .toBe(false)` cannot fail while this line passes. It was written, planted against, and
    // removed - it never evaluated, because the forbidden assertion fails first. A check that
    // cannot fail reads as a second guarantee and is none.
    expect(elementOn(container, "a").classList.contains("library-connect-forbidden")).toBe(true);
    fireEvent(anchor, pointer("pointerup", { clientX: 40, clientY: 0 }));
    expect(onConnectionDrawn).not.toHaveBeenCalled();
  });

  it("allows the same drag where the definition declares no acyclic set", () => {
    // Arrange: the identical chain, with the rule removed - the only difference.
    const { container, onConnectionDrawn } = renderCanvas(definitionOf(), modelOf([flows("1", "a", "b"), flows("2", "b", "c")]));

    // Act.
    dragFromC(container, 40);

    // Assert: this is what makes the test above evidence about the RULE rather than about the
    // geometry of a drag from C to A, which is the way this pair could both pass for free.
    expect(onConnectionDrawn).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({ relationType: "flows", sourceElementId: "c", targetElementId: "a" }),
    );
  });

  it("allows it where the existing path runs through a relation outside the set", () => {
    // Arrange: A -> B is `flows` and B -> C is `notes`, which the rule does not cover. There is
    // no path from A to C through covered relations alone, so C -> A closes no cycle in the set.
    const { container, onConnectionDrawn } = renderCanvas(definitionOf(ACYCLIC), modelOf([flows("1", "a", "b"), notes("2", "b", "c")]));

    // Act.
    dragFromC(container, 40);

    // Assert. A walk that ignored the types would refuse this, which is the shape of a rule that
    // has quietly become "no cycles anywhere" - a different rule, and not the declared one.
    expect(onConnectionDrawn).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({ relationType: "flows", sourceElementId: "c", targetElementId: "a" }),
    );
  });

  it("terminates on a model that already holds a cycle, rather than walking it forever", () => {
    // Arrange: a document can hold a cycle whatever the definition says - it may predate the
    // rule, or have been written by hand. B -> C -> B is one, and a walk bounded by a depth
    // guess rather than by the visited set hangs the canvas on it.
    const { container, onConnectionDrawn } = renderCanvas(
      definitionOf(ACYCLIC),
      modelOf([flows("1", "a", "b"), flows("2", "b", "c"), flows("3", "c", "b")]),
    );

    // Act.
    dragFromC(container, 40);

    // Assert: it answered at all, and it answered "no".
    expect(onConnectionDrawn).not.toHaveBeenCalled();
  });
});

describe("the three checks each refuse on their own", () => {
  /**
   * One definition declaring both a cardinality cap and the acyclic set, driven twice.
   *
   * The plant this detects is a verdict that returns early once cardinality is satisfied - or
   * once the walk is - which is indistinguishable from the correct code on any example where both
   * rules agree. So each drag here violates exactly one of them.
   *
   * <b>Do not remove this as redundant. The other four tests in this file cannot see that defect
   * by construction</b>, because every one of them declares no cardinality at all. Measured: with
   * `if (cardinality !== undefined) return true;` planted ahead of the walk, this test was the only
   * failure in the file and the other four were green.
   */
  it("refuses a cycle while cardinality passes, and refuses over cardinality while the walk passes", () => {
    // Arrange (cycle only): A -> B -> C, cap of five, so C has four outgoing to spare.
    const cycleOnly = renderCanvas(definitionOf(ACYCLIC, 5), modelOf([flows("1", "a", "b"), flows("2", "b", "c")]));

    // Act.
    dragFromC(cycleOnly.container, 40);

    // Assert: the walk refused on its own.
    expect(cycleOnly.onConnectionDrawn).not.toHaveBeenCalled();

    // Arrange (cardinality only): C -> D exists and the cap is one. C -> B closes no cycle,
    // because nothing runs from B back to C.
    const capOnly = renderCanvas(definitionOf(ACYCLIC, 1), modelOf([flows("1", "c", "d")]));

    // Act: into B's box, released nearer its west anchor than its east one.
    dragFromC(capOnly.container, 260);

    // Assert: cardinality refused on its own, with the walk saying yes.
    expect(capOnly.onConnectionDrawn).not.toHaveBeenCalled();

    // ...and the same drag is allowed once the cap is lifted, so the refusal above is the CAP
    // rather than anything else about that pair.
    const uncapped = renderCanvas(definitionOf(ACYCLIC), modelOf([flows("1", "c", "d")]));
    dragFromC(uncapped.container, 260);
    expect(uncapped.onConnectionDrawn).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({ sourceElementId: "c", targetElementId: "b" }),
    );
  });
});
