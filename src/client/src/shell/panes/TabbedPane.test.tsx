import { afterEach, describe, expect, it, vi } from "vitest";
import { useState } from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { TabbedPane, type TabDef } from "./TabbedPane";

function twoTabs(): TabDef[] {
  return [
    { id: "one", label: "One", content: <span data-testid="content-one">first</span> },
    { id: "two", label: "Two", content: <span data-testid="content-two">second</span>, tooltip: "docs/two.adp" },
  ];
}

function manyTabs(count: number): TabDef[] {
  return Array.from({ length: count }, (_, index) => ({
    id: `t${index + 1}`,
    label: `Tab ${index + 1}`,
    content: <span data-testid={`content-t${index + 1}`}>{index + 1}</span>,
  }));
}

/**
 * jsdom lays nothing out, so overflow tests stub the geometry: the strip is 300px and every
 * tab 100px, which fits three tabs bare or two beside the overflow button.
 */
function stubStripGeometry() {
  return vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(function (this: HTMLElement) {
    const width = this.classList.contains("tab-strip") ? 300 : this.dataset.tabId !== undefined ? 100 : 0;
    return { width, height: 0, x: 0, y: 0, top: 0, left: 0, right: width, bottom: 0, toJSON: () => ({}) } as DOMRect;
  });
}

afterEach(() => vi.restoreAllMocks());

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

  it("moves overflowing tabs behind the dropdown instead of a scrollbar", () => {
    // Arrange: four 100px tabs in a 300px strip - two fit beside the overflow button.
    stubStripGeometry();

    // Act.
    render(<TabbedPane tabs={manyTabs(4)} activeTabId="t1" onSelectTab={vi.fn()} />);

    // Assert.
    expect(screen.getAllByRole("tab")).toHaveLength(2);
    const button = screen.getByRole("button", { name: "2 more tabs" });
    fireEvent.click(button);
    const items = screen.getAllByRole("menuitem");
    expect(items.map((item) => item.textContent)).toEqual(["Tab 3", "Tab 4"]);
  });

  it("shows every tab when they all fit, with no dropdown", () => {
    // Arrange & act: three 100px tabs fit a 300px strip exactly.
    stubStripGeometry();
    render(<TabbedPane tabs={manyTabs(3)} activeTabId="t1" onSelectTab={vi.fn()} />);

    // Assert.
    expect(screen.getAllByRole("tab")).toHaveLength(3);
    expect(screen.queryByRole("button", { name: /more tabs/ })).toBeNull();
  });

  it("promotes a tab selected from the dropdown into the strip, demoting the least recently used", () => {
    // Arrange: t1 and t2 visible, t1 active; focus t2 briefly so t1 is the least recent.
    stubStripGeometry();
    const Harness = () => {
      const [active, setActive] = useState("t1");
      return <TabbedPane tabs={manyTabs(4)} activeTabId={active} onSelectTab={setActive} />;
    };
    render(<Harness />);
    fireEvent.click(screen.getByRole("tab", { name: "Tab 2" }));

    // Act: pick Tab 4 out of the dropdown.
    fireEvent.click(screen.getByRole("button", { name: "2 more tabs" }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Tab 4" }));

    // Assert.
    // Tab 4 sits in the primary list, active; Tab 1 - the least recently used visible tab -
    // took its place in the dropdown. The focus history is what decided who hid.
    const visible = screen.getAllByRole("tab").map((tab) => tab.textContent);
    expect(visible).toEqual(["Tab 4", "Tab 2"]);
    expect(screen.getByRole("tab", { name: "Tab 4" }).getAttribute("aria-selected")).toBe("true");
    fireEvent.click(screen.getByRole("button", { name: "2 more tabs" }));
    expect(screen.getAllByRole("menuitem").map((item) => item.textContent)).toEqual(["Tab 1", "Tab 3"]);
  });

  it("keeps a newly opened tab in the primary list", () => {
    // Arrange: an overflowing strip with the last-opened tab active - the double-click flow.
    stubStripGeometry();
    const { rerender } = render(<TabbedPane tabs={manyTabs(4)} activeTabId="t1" onSelectTab={vi.fn()} />);

    // Act: a fifth tab opens and becomes active.
    rerender(<TabbedPane tabs={manyTabs(5)} activeTabId="t5" onSelectTab={vi.fn()} />);

    // Assert.
    expect(screen.getAllByRole("tab").map((tab) => tab.textContent)).toContain("Tab 5");
    expect(screen.getByRole("tab", { name: "Tab 5" }).getAttribute("aria-selected")).toBe("true");
  });

  it("closes the dropdown on Escape and on a click outside it", () => {
    // Arrange.
    stubStripGeometry();
    render(<TabbedPane tabs={manyTabs(4)} activeTabId="t1" onSelectTab={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: "2 more tabs" }));
    expect(screen.getAllByRole("menuitem").length).toBeGreaterThan(0);

    // Act & assert, step by step.
    fireEvent.keyDown(document, { key: "Escape" });
    expect(screen.queryByRole("menuitem")).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "2 more tabs" }));
    fireEvent.mouseDown(document.body);
    expect(screen.queryByRole("menuitem")).toBeNull();
  });
});
