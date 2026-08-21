import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { WorkspaceShell } from "./WorkspaceShell";

describe("WorkspaceShell", () => {
  it("renders the default pane/tab arrangement (Requirement 4.1-4.4)", () => {
    render(<WorkspaceShell projectName="Test Project" onBack={() => {}} />);

    const tabLists = screen.getAllByRole("tablist");
    expect(tabLists).toHaveLength(3);

    const labelsFor = (tabList: HTMLElement) =>
      within(tabList)
        .getAllByRole("tab")
        .map((tab) => tab.textContent);

    const [left, center, bottom] = tabLists;
    expect(labelsFor(left)).toEqual(["Hierarchy", "Toolbox", "Search"]);
    expect(labelsFor(center)).toEqual(["Diagram 1", "Diagram 2"]);
    expect(labelsFor(bottom)).toEqual(["Property Grid", "Errors & Warnings"]);
  });

  it("displays the project name", () => {
    render(<WorkspaceShell projectName="Test Project" onBack={() => {}} />);
    expect(screen.getByText("Test Project")).toBeTruthy();
  });

  it("calls onBack when the back affordance is used", () => {
    const onBack = vi.fn();
    render(<WorkspaceShell projectName="Test Project" onBack={onBack} />);

    fireEvent.click(screen.getByText(/Back to projects/));

    expect(onBack).toHaveBeenCalledTimes(1);
  });
});
