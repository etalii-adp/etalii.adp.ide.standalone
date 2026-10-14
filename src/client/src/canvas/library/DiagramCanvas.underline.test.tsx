import { afterEach, describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { DiagramCanvas } from "./DiagramCanvas";
import type { DiagramDefinition, LabelDeclaration } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { widthOf } from "../label/textMetrics";

/**
 * A declared label's wavy underline, mounted: the canvas draws the wave the declaration asks for, under
 * the text it belongs to, as wide as that text.
 *
 * Drawn because Chromium ignores `text-decoration-style` on SVG text, so a stylesheet's
 * `underline wavy` came out straight on every canvas label. jsdom applies no CSS and lays out no
 * text, so what is asserted here is the shape the canvas emits and the width it took it from - the
 * browser's own measurement where there is one (stubbed below), the library's estimate where not.
 */

function canvasOf(label: LabelDeclaration, payload: unknown) {
  const definition: DiagramDefinition = {
    elementTypes: [{ id: "node", shape: "box", anchors: { kind: "edge" }, sizing: "model", labels: [label] }],
    relationTypes: [],
    layout: { modes: ["manual"] },
    dragging: "enabled",
  };
  const model: DiagramModel = {
    elements: [{ id: "a", type: "node", x: 0, y: 0, width: 120, height: 40, label: "Alpha", payload }],
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

const flagged: LabelDeclaration = {
  text: { path: "payload.caption" },
  className: "marked-caption",
  underline: { style: "wavy", when: { path: "payload.flagged", is: "true" } },
};

const underlineIn = (container: HTMLElement) => container.querySelector<SVGPathElement>('[data-element-id="a"] path.library-label-underline');

/** How far the path runs from left to right. */
function spanOf(path: SVGPathElement): { left: number; right: number } {
  const xs = (path.getAttribute("d") ?? "")
    .split(/(?=[MQLZ])/)
    .filter((part) => part[0] !== "Z")
    .map((part) => (part.match(/-?\d+(\.\d+)?/g) ?? []).map(Number).at(-2)!);
  return { left: Math.min(...xs), right: Math.max(...xs) };
}

describe("a declared label's wavy underline", () => {
  const prototype = SVGElement.prototype as unknown as { getComputedTextLength?: () => number };
  afterEach(() => {
    delete prototype.getComputedTextLength;
  });

  it("is drawn right after its text, in the label's own classes, where its condition holds", () => {
    const { container } = canvasOf(flagged, { caption: "R1", flagged: true });
    const underline = underlineIn(container);

    expect(underline, "the underline is drawn").not.toBeNull();
    expect(underline!.getAttribute("data-underline")).toBe("wavy");
    // The label's classes, so whatever paints the caption's fill paints its underline.
    expect(underline!.classList.contains("marked-caption")).toBe(true);
    const text = underline!.previousElementSibling;
    expect(text?.tagName.toLowerCase()).toBe("text");
    expect(text?.textContent).toBe("R1");
  });

  it("is not drawn where its condition fails, nor where none is declared", () => {
    expect(underlineIn(canvasOf(flagged, { caption: "R1", flagged: false }).container)).toBeNull();
    expect(underlineIn(canvasOf({ text: { path: "payload.caption" } }, { caption: "R1" }).container)).toBeNull();
  });

  it("is as wide as the text's estimate where the browser measures nothing, centred under it", () => {
    const { container } = canvasOf(flagged, { caption: "Growth", flagged: true });
    const underline = underlineIn(container)!;
    const x = Number(underline.previousElementSibling!.getAttribute("x"));
    const width = widthOf("Growth", 12);

    expect(spanOf(underline).left).toBeCloseTo(x - (width / 2), 1);
    expect(spanOf(underline).right).toBeCloseTo(x + (width / 2), 1);
  });

  it("follows the width the browser measures for the text, which a stylesheet's font decides", () => {
    // A bold caption at the module's own size is wider than the library's estimate; the line under it
    // must be too.
    prototype.getComputedTextLength = () => 80;
    const { container } = canvasOf(flagged, { caption: "Growth", flagged: true });
    const underline = underlineIn(container)!;
    const x = Number(underline.previousElementSibling!.getAttribute("x"));

    expect(spanOf(underline).left).toBeCloseTo(x - 40, 1);
    expect(spanOf(underline).right).toBeCloseTo(x + 40, 1);
  });
});
