import { describe, expect, it, vi } from "vitest";
import { create } from "@bufbuild/protobuf";
import { ContextActionGroupSchema } from "../../generated/context_pb";
import { toMenuGroups } from "./toMenuGroups";

describe("toMenuGroups", () => {
  it("maps an unavailable action onto a disabled item carrying its reason", () => {
    // Arrange.
    const groups = [
      create(ContextActionGroupSchema, {
        actions: [
          { id: "hierarchy.delete", label: "Delete", icon: "mdi-trash-can-outline", available: false, unavailableReason: "No parent folder." },
        ],
      }),
    ];

    // Act.
    const menuGroups = toMenuGroups(groups, () => {});

    // Assert.
    expect(menuGroups[0]?.[0]).toMatchObject({
      id: "hierarchy.delete",
      label: "Delete",
      icon: "mdi-trash-can-outline",
      disabled: true,
      disabledReason: "No parent folder.",
    });
  });

  it("keeps one backend group as one menu group, in the order they were reported", () => {
    // Arrange and act.
    const groups = [
      create(ContextActionGroupSchema, { actions: [{ id: "a", label: "A", icon: "", available: true }] }),
      create(ContextActionGroupSchema, { actions: [{ id: "b", label: "B", icon: "", available: true }] }),
    ];

    // Assert.
    expect(toMenuGroups(groups, () => {}).map((group) => group.map((item) => item.id))).toEqual([["a"], ["b"]]);
  });

  it("calls back with the action a selected item stands for", () => {
    // Arrange.
    const onSelect = vi.fn();
    const groups = [
      create(ContextActionGroupSchema, { actions: [{ id: "hierarchy.rename", label: "Rename…", icon: "", available: true }] }),
    ];

    // Act.
    const item = toMenuGroups(groups, onSelect)[0]?.[0];
    (item as { onSelect: () => void }).onSelect();

    // Assert.
    expect(onSelect.mock.calls[0]?.[0]?.id).toBe("hierarchy.rename");
  });

  it("turns an action with items into a submenu item with the same grouping inside", () => {
    // Arrange.
    const groups = [
      create(ContextActionGroupSchema, {
        actions: [
          {
            id: "convert",
            label: "Convert to…",
            icon: "",
            available: true,
            items: [{ actions: [{ id: "convert.mindmap", label: "Mindmap", icon: "", available: true }] }],
          },
        ],
      }),
    ];

    // Act.
    const item = toMenuGroups(groups, () => {})[0]?.[0] as { items: unknown[][] };

    // Assert.
    expect(item.items[0]?.map((child) => (child as { id: string }).id)).toEqual(["convert.mindmap"]);
  });
});
