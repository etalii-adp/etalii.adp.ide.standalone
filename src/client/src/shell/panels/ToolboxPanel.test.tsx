import { describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { ToolboxItemSchema, type ToolboxItem } from "../../generated/diagrams_pb";
import { ToolboxPanel } from "./ToolboxPanel";
import { DiagramToolboxProvider, TOOLBOX_DRAG_TYPE, useRegisterDiagramToolbox } from "./DiagramToolboxContext";

/** Plays the role of a mounted canvas: registers what its diagram's type contributes. */
function Registrar({ items }: { items: ToolboxItem[] }) {
  useRegisterDiagramToolbox(items);
  return null;
}

const nodeItem = create(ToolboxItemSchema, {
  id: "mindmap.toolbox.node",
  label: "Node",
  icon: "mdi-card-plus-outline",
  description: "Drop on a node to add a child under it.",
  dropActionId: "mindmap.add-child",
});

describe("ToolboxPanel", () => {
  it("shows the open-a-diagram placeholder while no canvas is registered", () => {
    // Arrange and act.
    const { container } = render(
      <DiagramToolboxProvider>
        <ToolboxPanel />
      </DiagramToolboxProvider>,
    );

    // Assert.
    // The placeholder opens with its loading shim; its label is the immediate, stable part.
    expect(container.querySelector("[aria-label='Loading Toolbox']")).not.toBeNull();
    expect(container.querySelector(".toolbox-panel-item")).toBeNull();
  });

  it("renders the entries the backend described, without interpreting them", () => {
    // Arrange.
    const { container } = render(
      <DiagramToolboxProvider>
        <Registrar items={[nodeItem]} />
        <ToolboxPanel />
      </DiagramToolboxProvider>,
    );

    // Act and assert, step by step.
    const entry = container.querySelector(".toolbox-panel-item")!;
    expect(entry.textContent).toContain("Node");
    expect(entry.getAttribute("title")).toContain("add a child");
    expect(entry.getAttribute("draggable")).toBe("true");
  });

  it("says so when the registered diagram type contributes nothing", () => {
    // Arrange and act.
    const { container } = render(
      <DiagramToolboxProvider>
        <Registrar items={[]} />
        <ToolboxPanel />
      </DiagramToolboxProvider>,
    );

    // Assert.
    expect(container.textContent).toContain("no toolbox elements");
  });

  it("a drag carries the backend's drop action id and nothing else", () => {
    // Arrange.
    const { container } = render(
      <DiagramToolboxProvider>
        <Registrar items={[nodeItem]} />
        <ToolboxPanel />
      </DiagramToolboxProvider>,
    );

    // Act.
    const setData = vi.fn();
    fireEvent.dragStart(container.querySelector(".toolbox-panel-item")!, {
      dataTransfer: { setData, effectAllowed: "" },
    });

    // Assert.
    expect(setData).toHaveBeenCalledWith(TOOLBOX_DRAG_TYPE, "mindmap.add-child");
    expect(setData).toHaveBeenCalledTimes(1);
  });

  it("the placeholder returns when the registering canvas unmounts", () => {
    // Act and assert, step by step.
    const { container, rerender } = render(
      <DiagramToolboxProvider>
        <Registrar items={[nodeItem]} />
        <ToolboxPanel />
      </DiagramToolboxProvider>,
    );
    expect(container.querySelector(".toolbox-panel-item")).not.toBeNull();

    rerender(
      <DiagramToolboxProvider>
        <ToolboxPanel />
      </DiagramToolboxProvider>,
    );
    expect(container.querySelector("[aria-label='Loading Toolbox']")).not.toBeNull();
    expect(container.querySelector(".toolbox-panel-item")).toBeNull();
  });
});
