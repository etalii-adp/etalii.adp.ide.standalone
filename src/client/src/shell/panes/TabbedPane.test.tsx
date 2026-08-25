import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { TabbedPane, type TabDef } from "./TabbedPane";

function twoTabs(): TabDef[] {
  return [
    { id: "one", label: "One", content: <span data-testid="content-one">first</span> },
    { id: "two", label: "Two", content: <span data-testid="content-two">second</span>, tooltip: "docs/two.adp" },
  ];
}

describe("TabbedPane", () => {
  it("keeps its uncontrolled default: first tab active, click switches, single tab hides the strip", () => {
    // Act and assert, step by step.
    const { rerender } = render(<TabbedPane tabs={twoTabs()} />);
    expect(screen.getByTestId("content-one")).toBeTruthy();

    fireEvent.click(screen.getByRole("tab", { name: "Two" }));
    expect(screen.getByTestId("content-two")).toBeTruthy();

    rerender(<TabbedPane tabs={twoTabs().slice(0, 1)} />);
    expect(screen.queryByRole("tablist")).toBeNull();
  });

  it("renders the tooltip on the tab", () => {
    // Act.
    render(<TabbedPane tabs={twoTabs()} />);

    // Assert.
    expect(screen.getByRole("tab", { name: "Two" }).title).toBe("docs/two.adp");
  });

  it("follows activeTabId in controlled mode and reports selection without switching itself", () => {
    // Arrange.
    const onSelectTab = vi.fn();
    const { rerender } = render(<TabbedPane tabs={twoTabs()} activeTabId="two" onSelectTab={onSelectTab} />);
    expect(screen.getByTestId("content-two")).toBeTruthy();

    // Act.
    fireEvent.click(screen.getByRole("tab", { name: "One" }));

    // Assert.
    expect(onSelectTab).toHaveBeenCalledWith("one");
    // Controlled: the shown tab only moves when the owner moves it.
    expect(screen.getByTestId("content-two")).toBeTruthy();
    rerender(<TabbedPane tabs={twoTabs()} activeTabId="one" onSelectTab={onSelectTab} />);
    expect(screen.getByTestId("content-one")).toBeTruthy();
  });

  it("shows a close control per tab that closes without selecting", () => {
    // Arrange.
    const onSelectTab = vi.fn();
    const onCloseTab = vi.fn();
    render(<TabbedPane tabs={twoTabs()} activeTabId="one" onSelectTab={onSelectTab} onCloseTab={onCloseTab} />);

    // Act.
    fireEvent.click(screen.getByRole("button", { name: "Close Two" }));

    // Assert.
    expect(onCloseTab).toHaveBeenCalledWith("two");
    expect(onSelectTab).not.toHaveBeenCalled();
  });

  it("closes on middle-click", () => {
    // Arrange.
    const onCloseTab = vi.fn();
    render(<TabbedPane tabs={twoTabs()} onCloseTab={onCloseTab} />);

    // Act.
    fireEvent(screen.getByRole("tab", { name: /Two/ }), new MouseEvent("auxclick", { button: 1, bubbles: true, cancelable: true }));

    // Assert.
    expect(onCloseTab).toHaveBeenCalledWith("two");
  });

  it("keeps the strip visible for a single closable tab", () => {
    // Act.
    render(<TabbedPane tabs={twoTabs().slice(0, 1)} onCloseTab={vi.fn()} />);

    // Assert.
    expect(screen.getByRole("tablist")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Close One" })).toBeTruthy();
  });

  it("renders the empty state when there are no tabs", () => {
    // Act.
    render(<TabbedPane tabs={[]} emptyState={<span data-testid="empty">nothing open</span>} />);

    // Assert.
    expect(screen.getByTestId("empty")).toBeTruthy();
    expect(screen.queryByRole("tablist")).toBeNull();
  });
});
