import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { ContextMenu, computeClampedPosition, type ContextMenuGroup } from "./ContextMenu";

describe("computeClampedPosition", () => {
  it("returns the preferred position unchanged when it already fits", () => {
    // Arrange, act and assert.
    expect(
      computeClampedPosition({ x: 10, y: 10 }, { width: 100, height: 50 }, { width: 500, height: 500 }),
    ).toEqual({ x: 10, y: 10 });
  });

  it("flips to the opposite edge when the preferred position overflows the right", () => {
    // Arrange, act and assert.
    expect(
      computeClampedPosition({ x: 450, y: 10 }, { width: 100, height: 50 }, { width: 500, height: 500 }),
    ).toEqual({ x: 350, y: 10 });
  });

  it("flips to the opposite edge when the preferred position overflows the bottom", () => {
    // Arrange, act and assert.
    expect(
      computeClampedPosition({ x: 10, y: 470 }, { width: 100, height: 50 }, { width: 500, height: 500 }),
    ).toEqual({ x: 10, y: 420 });
  });

  it("flips each axis independently", () => {
    // Arrange, act and assert.
    expect(
      computeClampedPosition({ x: 450, y: 10 }, { width: 100, height: 50 }, { width: 500, height: 500 }),
    ).toEqual({ x: 350, y: 10 });
    expect(
      computeClampedPosition({ x: 10, y: 470 }, { width: 100, height: 50 }, { width: 500, height: 500 }),
    ).toEqual({ x: 10, y: 420 });
  });

  it("clamps to 0 rather than going negative when the menu is larger than the viewport", () => {
    // Arrange, act and assert.
    expect(
      computeClampedPosition({ x: 400, y: 400 }, { width: 600, height: 600 }, { width: 500, height: 500 }),
    ).toEqual({ x: 0, y: 0 });
  });
});

function sampleGroups(overrides?: { deleteDisabled?: boolean }): { groups: ContextMenuGroup[]; onRename: ReturnType<typeof vi.fn>; onDelete: ReturnType<typeof vi.fn>; onConvertA: ReturnType<typeof vi.fn> } {
  const onRename = vi.fn();
  const onDelete = vi.fn();
  const onConvertA = vi.fn();

  const coreGroup: ContextMenuGroup = [
    { id: "rename", label: "Rename", icon: "mdi-pencil-outline", onSelect: onRename },
    {
      id: "delete",
      label: "Delete",
      disabled: overrides?.deleteDisabled ?? true,
      disabledReason: "This file is read-only",
      onSelect: onDelete,
    },
  ];

  const diagramTypeGroup: ContextMenuGroup = [
    {
      id: "convert",
      label: "Convert to",
      items: [
        [
          { id: "convert-a", label: "Mindmap", onSelect: onConvertA },
          { id: "convert-b", label: "Flowchart", onSelect: vi.fn() },
        ],
      ],
    },
  ];

  return { groups: [coreGroup, diagramTypeGroup], onRename, onDelete, onConvertA };
}

describe("ContextMenu", () => {
  it("renders nothing when closed", () => {
    // Arrange and act.
    const { groups } = sampleGroups();
    const { container } = render(
      <ContextMenu open={false} groups={groups} position={{ x: 0, y: 0 }} onClose={() => {}} />,
    );
    // Assert.
    expect(container.firstChild).toBeNull();
  });

  it("renders nothing and warns in dev when every group is empty", () => {
    // Arrange.
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});

    // Act and assert, step by step.
    const noGroups = render(<ContextMenu open groups={[]} position={{ x: 0, y: 0 }} onClose={() => {}} />);
    expect(noGroups.container.firstChild).toBeNull();
    expect(warn).toHaveBeenCalled();
    noGroups.unmount();

    warn.mockClear();
    const emptyGroups = render(
      <ContextMenu open groups={[[], []]} position={{ x: 0, y: 0 }} onClose={() => {}} />,
    );
    expect(emptyGroups.container.firstChild).toBeNull();
    expect(warn).toHaveBeenCalled();

    warn.mockRestore();
  });

  it("renders a separator between groups and none within a single group", () => {
    // Arrange and act.
    const { groups } = sampleGroups();
    render(<ContextMenu open groups={groups} position={{ x: 0, y: 0 }} onClose={() => {}} />);

    // Assert.
    expect(screen.getAllByRole("separator")).toHaveLength(1);
  });

  it("aligns icon-less items via a reserved icon space", () => {
    // Arrange.
    const { groups } = sampleGroups();
    render(<ContextMenu open groups={groups} position={{ x: 0, y: 0 }} onClose={() => {}} />);

    // Act and assert, step by step.
    const deleteItem = screen.getByRole("menuitem", { name: "Delete" });
    expect(deleteItem.querySelector(".context-menu-item-icon")).not.toBeNull();
  });

  it("invokes the item's onSelect then closes when a non-disabled item is clicked", () => {
    // Arrange.
    const { groups, onRename } = sampleGroups();
    const onClose = vi.fn();
    render(<ContextMenu open groups={groups} position={{ x: 0, y: 0 }} onClose={onClose} />);

    // Act.
    fireEvent.click(screen.getByRole("menuitem", { name: "Rename" }));

    // Assert.
    expect(onRename).toHaveBeenCalledTimes(1);
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("does not invoke onSelect or close when a disabled item is clicked", () => {
    // Arrange.
    const { groups, onDelete } = sampleGroups();
    const onClose = vi.fn();
    render(<ContextMenu open groups={groups} position={{ x: 0, y: 0 }} onClose={onClose} />);

    // Act.
    fireEvent.click(screen.getByRole("menuitem", { name: "Delete" }));

    // Assert.
    expect(onDelete).not.toHaveBeenCalled();
    expect(onClose).not.toHaveBeenCalled();
  });

  it("surfaces a disabled item's reason as a title tooltip", () => {
    // Arrange and act.
    const { groups } = sampleGroups();
    render(<ContextMenu open groups={groups} position={{ x: 0, y: 0 }} onClose={() => {}} />);

    // Assert.
    expect(screen.getByRole("menuitem", { name: "Delete" }).getAttribute("title")).toBe("This file is read-only");
  });

  it("opens a submenu on click and renders its own grouped items", () => {
    // Arrange.
    const { groups } = sampleGroups();
    render(<ContextMenu open groups={groups} position={{ x: 0, y: 0 }} onClose={() => {}} />);

    // Act.
    fireEvent.click(screen.getByRole("menuitem", { name: "Convert to" }));

    // Assert.
    expect(screen.getByRole("menuitem", { name: "Mindmap" })).not.toBeNull();
    expect(screen.getByRole("menuitem", { name: "Flowchart" })).not.toBeNull();
  });

  it("still opens a disabled item's submenu, so nested actions remain discoverable", () => {
    // Arrange.
    const onConvert = vi.fn();
    const groups: ContextMenuGroup[] = [
      [
        {
          id: "convert",
          label: "Convert to",
          disabled: true,
          items: [[{ id: "convert-a", label: "Mindmap", onSelect: onConvert }]],
        },
      ],
    ];
    render(<ContextMenu open groups={groups} position={{ x: 0, y: 0 }} onClose={() => {}} />);

    // Act.
    fireEvent.click(screen.getByRole("menuitem", { name: "Convert to" }));

    // Assert.
    expect(screen.getByRole("menuitem", { name: "Mindmap" })).not.toBeNull();
  });

  it("navigates with ArrowDown/ArrowUp, skipping the disabled item and wrapping across the group boundary", () => {
    // Arrange.
    const { groups } = sampleGroups();
    render(<ContextMenu open groups={groups} position={{ x: 0, y: 0 }} onClose={() => {}} />);

    const menu = screen.getByRole("menu");
    const rename = screen.getByRole("menuitem", { name: "Rename" });
    const convert = screen.getByRole("menuitem", { name: "Convert to" });

    // Act and assert, step by step.
    expect(document.activeElement).toBe(rename);

    fireEvent.keyDown(menu, { key: "ArrowDown" });
    expect(document.activeElement).toBe(convert);

    fireEvent.keyDown(menu, { key: "ArrowDown" });
    expect(document.activeElement).toBe(rename);

    fireEvent.keyDown(menu, { key: "ArrowUp" });
    expect(document.activeElement).toBe(convert);
  });

  it("opens a submenu and moves focus in with ArrowRight, then closes it and returns focus with ArrowLeft", () => {
    // Arrange.
    const { groups } = sampleGroups();
    render(<ContextMenu open groups={groups} position={{ x: 0, y: 0 }} onClose={() => {}} />);

    // Arrange, continued.
    const rootMenu = screen.getByRole("menu");
    fireEvent.keyDown(rootMenu, { key: "ArrowDown" }); // focus "Convert to"
    const convert = screen.getByRole("menuitem", { name: "Convert to" });
    expect(document.activeElement).toBe(convert);

    // Arrange, continued.
    fireEvent.keyDown(rootMenu, { key: "ArrowRight" });
    const mindmap = screen.getByRole("menuitem", { name: "Mindmap" });
    expect(document.activeElement).toBe(mindmap);

    // Act.
    const submenu = mindmap.closest("ul") as HTMLElement;
    fireEvent.keyDown(submenu, { key: "ArrowLeft" });

    // Assert.
    expect(screen.queryByRole("menuitem", { name: "Mindmap" })).toBeNull();
    expect(document.activeElement).toBe(convert);
  });

  it("closes only the inner submenu when it is left, leaving an ancestor submenu open", () => {
    // Arrange.
    const groups: ContextMenuGroup[] = [
      [
        {
          id: "a",
          label: "A",
          items: [
            [
              {
                id: "b",
                label: "B",
                items: [[{ id: "c", label: "C", onSelect: vi.fn() }]],
              },
            ],
          ],
        },
      ],
    ];
    render(<ContextMenu open groups={groups} position={{ x: 0, y: 0 }} onClose={() => {}} />);

    // Arrange, continued.
    fireEvent.click(screen.getByRole("menuitem", { name: "A" }));
    fireEvent.click(screen.getByRole("menuitem", { name: "B" }));
    expect(screen.getByRole("menuitem", { name: "C" })).not.toBeNull();

    // Act.
    const cLevel = screen.getByRole("menuitem", { name: "C" }).closest("ul") as HTMLElement;
    fireEvent.keyDown(cLevel, { key: "ArrowLeft" });

    // Assert.
    expect(screen.queryByRole("menuitem", { name: "C" })).toBeNull();
    expect(screen.getByRole("menuitem", { name: "B" })).not.toBeNull();
  });

  it("closes an open submenu when the mouse leaves it, without closing the top-level menu", () => {
    // Arrange.
    const { groups } = sampleGroups();
    render(<ContextMenu open groups={groups} position={{ x: 0, y: 0 }} onClose={() => {}} />);

    // Arrange, continued.
    fireEvent.click(screen.getByRole("menuitem", { name: "Convert to" }));
    expect(screen.getByRole("menuitem", { name: "Mindmap" })).not.toBeNull();

    // Act.
    const convertItemWrapper = screen.getByRole("menuitem", { name: "Convert to" }).closest("li") as HTMLElement;
    fireEvent.mouseLeave(convertItemWrapper);

    // Assert.
    expect(screen.queryByRole("menuitem", { name: "Mindmap" })).toBeNull();
    expect(screen.getByRole("menuitem", { name: "Convert to" })).not.toBeNull();
  });

  it("closes and calls onClose exactly once when clicking outside the menu", () => {
    // Arrange.
    const { groups } = sampleGroups();
    const onClose = vi.fn();
    const { container } = render(
      <ContextMenu open groups={groups} position={{ x: 0, y: 0 }} onClose={onClose} />,
    );

    // Act.
    fireEvent.mouseDown(container.querySelector(".context-menu-catcher") as HTMLElement);

    // Assert.
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("does not close when clicking inside the menu itself", () => {
    // Arrange.
    const { groups } = sampleGroups();
    const onClose = vi.fn();
    render(<ContextMenu open groups={groups} position={{ x: 0, y: 0 }} onClose={onClose} />);

    // Act.
    fireEvent.mouseDown(screen.getByRole("menu"));

    // Assert.
    expect(onClose).not.toHaveBeenCalled();
  });

  it("closes and calls onClose exactly once when Escape is pressed", () => {
    // Arrange.
    const { groups } = sampleGroups();
    const onClose = vi.fn();
    render(<ContextMenu open groups={groups} position={{ x: 0, y: 0 }} onClose={onClose} />);

    // Act.
    fireEvent.keyDown(document, { key: "Escape" });

    // Assert.
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("moves focus into the menu on open and restores it to the trigger on close", () => {
    // Arrange.
    const trigger = document.createElement("button");
    trigger.textContent = "Open menu";
    document.body.appendChild(trigger);
    trigger.focus();

    // Arrange, continued.
    const { groups } = sampleGroups();
    const { rerender } = render(
      <ContextMenu open groups={groups} position={{ x: 0, y: 0 }} onClose={() => {}} />,
    );

    // Arrange, continued.
    expect(within(screen.getByRole("menu")).getByRole("menuitem", { name: "Rename" })).toBe(document.activeElement);

    // Act.
    rerender(<ContextMenu open={false} groups={groups} position={{ x: 0, y: 0 }} onClose={() => {}} />);

    // Assert.
    expect(document.activeElement).toBe(trigger);
    trigger.remove();
  });
});
