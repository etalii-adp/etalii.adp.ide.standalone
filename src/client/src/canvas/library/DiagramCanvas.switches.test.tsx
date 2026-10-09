import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { DiagramCanvasCore } from "./DiagramCanvas";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import type { DiagramModel } from "./api/diagramModel";
import type { LibraryEventHandlers } from "./api/diagramEvents";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";

/**
 * A switch on the canvas whose value is the document's (agent-activity-diagram Requirement 4.6).
 *
 * The one toggle the library drew before this lived inside the filter block and held its own
 * state. This one is drawn without a filter and holds none: it shows what the model says.
 */

const definition: DiagramDefinition = {
  elementTypes: [{ id: "card", shape: "box", anchors: { kind: "edge" }, sizing: "model" }],
  relationTypes: [],
  layout: { modes: ["manual"] },
  dragging: "enabled",
  chrome: { switches: [{ id: "show-archived", caption: "Show archived", on: "payload.showArchived" }] },
};

function canvasOf(background: unknown, events: LibraryEventHandlers = {}) {
  const model: DiagramModel = { elements: [{ id: "a", type: "card", x: 0, y: 0, width: 120, height: 40 }], connections: [], background };
  return render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={definition} model={model} events={events} selection={[]} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
}

describe("a declared canvas switch", () => {
  it("is drawn although the definition declares no filter", () => {
    // The planted defect this was seen to fail against: the switches drawn only inside the
    // filter block, where the library's one toggle has always lived.
    canvasOf({ showArchived: false });

    expect(screen.queryByTestId("library-filter")).toBeNull();
    expect(screen.getByRole("switch", { name: "Show archived" })).toBeTruthy();
  });

  it("shows the document's value, and off for anything that is not true", () => {
    expect(canvasOf({ showArchived: true }).getByRole("switch").getAttribute("aria-checked")).toBe("true");
    expect(canvasOf({ showArchived: false }).getAllByRole("switch").at(-1)!.getAttribute("aria-checked")).toBe("false");
    expect(canvasOf(undefined).getAllByRole("switch").at(-1)!.getAttribute("aria-checked")).toBe("false");
    expect(canvasOf({ showArchived: "true" }).getAllByRole("switch").at(-1)!.getAttribute("aria-checked")).toBe("false");
  });

  it("asks for the other state when pressed, and stays as it is until the model changes", () => {
    const onSwitchToggled = vi.fn();
    const { getByRole } = canvasOf({ showArchived: false }, { onSwitchToggled });

    fireEvent.click(getByRole("switch"));

    expect(onSwitchToggled).toHaveBeenCalledExactlyOnceWith({ kind: "switch-toggled", id: "show-archived", on: true });
    expect(getByRole("switch").getAttribute("aria-checked")).toBe("false");
  });
});
