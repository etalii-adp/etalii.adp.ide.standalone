import { describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import type { DiagramDefinition } from "../definition/diagramDefinition";
import { EmptyCanvas } from "./EmptyCanvas";

/**
 * The canvas's own sentence for a document that holds nothing to draw (DISL 6.13 `canvas.empty`), drawn
 * in the canvas's place by the library from the definition. When to draw it is the module's: its stream
 * knows whether the document has been read.
 */

function definitionOf(empty?: string): DiagramDefinition {
  return {
    elementTypes: [],
    relationTypes: [],
    layout: { modes: ["manual"] },
    dragging: "enabled",
    ...(empty !== undefined ? { chrome: { empty: { text: { template: empty } } } } : {}),
  };
}

describe("EmptyCanvas", () => {
  it("says the definition's sentence, in the host's message frame and the module's class", () => {
    const { container } = render(<EmptyCanvas definition={definitionOf("Nothing here to draw.")} className="t-message" />);

    const message = container.firstElementChild;
    expect(message?.textContent).toBe("Nothing here to draw.");
    expect(message?.getAttribute("class")).toBe("t-message canvas-host canvas-host-message");
  });

  it("draws nothing for a definition that states no sentence", () => {
    const { container } = render(<EmptyCanvas definition={definitionOf()} className="t-message" />);

    expect(container.innerHTML).toBe("");
  });
});
