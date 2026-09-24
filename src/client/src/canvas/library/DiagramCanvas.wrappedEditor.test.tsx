import { describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import type { BuiltInShape, DiagramDefinition, LabelDeclaration } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import {
  InlineLabelPlacementProvider,
  useInlineLabelPlacement,
  type LabelPlacement,
} from "@client/shell/panels/InlineLabelPlacementContext";

/**
 * Where a WRAPPED label's editor opens, which is the half of the multiline editor that lives in
 * the canvas rather than in the editor component.
 *
 * A textarea that can hold newlines is worth nothing if nothing ever asks for one, so this asserts
 * the canvas marks a `wrap: true` label's placement as multiline - and that the box it hands over
 * is the shape's TEXT REGION rather than the element's bounding box. The second one is measured by
 * comparing two shapes through the same canvas: a trapezoid's region is narrower than a box's at
 * the same bounds, and a version that opens over the element gives the two the identical box.
 */

const LABEL = "The quick brown fox jumps over the lazy dog and keeps going for a while";

/** A 160x48 element whose centre is (200, 100), so its bounding box is x 120..280, y 76..124. */
const model: DiagramModel = {
  elements: [{ id: "a", type: "card", x: 200, y: 100, width: 160, height: 48, label: LABEL }],
  connections: [],
};

function definitionOf(shape: BuiltInShape, extra: Partial<LabelDeclaration>): DiagramDefinition {
  return {
    elementTypes: [
      {
        id: "card",
        shape,
        anchors: { kind: "edge" },
        sizing: "model",
        labels: [{ text: { path: "element.label" }, editable: true, typography: { fontSize: 10 }, ...extra }],
      },
    ],
    relationTypes: [],
    layout: { modes: ["manual"] },
    dragging: "enabled",
  };
}

/**
 * The placement the shell would be given for element `a`.
 *
 * Read through the registry rather than by reaching into the canvas, because the registry is what
 * the shell reads: a resolver the canvas never registers would fail here exactly as it fails in
 * the app. The probe records every render's answer and the last one is taken, since the canvas
 * registers in an effect and the first render necessarily sees an empty registry.
 */
function placementOf(shape: BuiltInShape, extra: Partial<LabelDeclaration>): LabelPlacement | null {
  const seen: (LabelPlacement | null)[] = [];

  function Probe() {
    const { placementFor } = useInlineLabelPlacement();
    seen.push(placementFor("a"));
    return null;
  }

  render(
    <InlineLabelPlacementProvider>
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvasCore definition={definitionOf(shape, extra)} model={model} events={{}} />
          <Probe />
        </DiagramToolboxProvider>
      </DiagramViewProvider>
    </InlineLabelPlacementProvider>,
  );

  return seen[seen.length - 1] ?? null;
}

describe("a wrapped label's editor opens over its text region", () => {
  it("marks the placement multiline, so the shell opens a textarea rather than an input", () => {
    // Arrange, act.
    const placement = placementOf("trapezoid", { wrap: true });

    // Assert. This is the line that makes the textarea reachable at all; without it the multiline
    // editor exists and nothing in the product ever opens one.
    expect(placement).not.toBeNull();
    expect(placement!.multiline).toBe(true);
  });

  it("gives a trapezoid a narrower box than a rectangle at the same bounds", () => {
    // Arrange, act: one element, one label, two shapes.
    const inTrapezoid = placementOf("trapezoid", { wrap: true })!;
    const inBox = placementOf("box", { wrap: true })!;

    // Assert: the slanted sides take room away, and both stay inside the 160-wide element. A
    // version measuring the bounding box hands over the same box for both, which is the planted
    // defect - and the comparison, rather than either number on its own, is what detects it.
    expect(inTrapezoid.width).toBeLessThan(inBox.width);
    expect(inBox.width).toBeLessThan(160);
    // ...and it is pushed inwards rather than merely trimmed on the right.
    expect(inTrapezoid.x).toBeGreaterThan(inBox.x);
  });

  it("carries the label's own text, newlines and all, so the editor opens on what is drawn", () => {
    // Arrange, act.
    const placement = placementOf("trapezoid", { wrap: true })!;

    // Assert: the WHOLE text, not the first wrapped line. An editor opened on one line of a
    // wrapped block silently deletes the rest the moment it commits. A companion rather than a
    // detector - today's editor opens on the element's label too - kept because the wrap branch
    // is a new place for that to be got wrong.
    expect(placement.text).toBe(LABEL);
  });

  it("leaves an unwrapped editable label opening over the element, as it does today", () => {
    // Arrange, act.
    const placement = placementOf("trapezoid", {})!;

    // Assert. A no-regression companion rather than a detector: it holds in both worlds, because
    // a version with no wrap support at all also opens the element's own box here. It is kept
    // because the wrap branch sits in front of this one, and a branch that swallowed the
    // unwrapped case would move every existing declared label's editor.
    expect(placement.multiline).not.toBe(true);
    expect(placement.width).toBeCloseTo(160, 5);
  });
});
