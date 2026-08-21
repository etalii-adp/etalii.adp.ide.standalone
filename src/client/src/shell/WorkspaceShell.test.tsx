import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import type { ComponentProps } from "react";
import { AuthProvider } from "../auth/AuthContext";
import { WorkspaceShell } from "./WorkspaceShell";

// HierarchyPanel now renders ExplorerTreePanel, which reads AuthContext for its
// gRPC transport (useAuth) - every render below needs an AuthProvider ancestor.
// Its ListEntries/WatchHierarchy calls will fail with no real backend behind
// this test's transport; ExplorerTreePanel catches that into its own error
// state rather than throwing, so it doesn't affect these structural assertions.
function renderShell(props: Partial<ComponentProps<typeof WorkspaceShell>> = {}) {
  return render(
    <AuthProvider>
      <WorkspaceShell projectId={new Uint8Array(16)} projectName="Test Project" onBack={() => {}} {...props} />
    </AuthProvider>,
  );
}

describe("WorkspaceShell", () => {
  it("renders the default pane/tab arrangement, with Toolbox and Properties in the rightmost column", () => {
    renderShell();

    const tabLists = screen.getAllByRole("tablist");
    expect(tabLists).toHaveLength(3);

    const labelsFor = (tabList: HTMLElement) =>
      within(tabList)
        .getAllByRole("tab")
        .map((tab) => tab.textContent);

    const [left, center, right] = tabLists;
    expect(labelsFor(left)).toEqual(["Hierarchy", "Search"]);
    expect(labelsFor(center)).toEqual(["Diagram 1", "Diagram 2"]);
    expect(labelsFor(right)).toEqual(["Toolbox", "Properties"]);

    // Errors & Warnings is now alone in its pane, so TabbedPane renders no
    // tab strip for it (only shown when a pane hosts more than one tab) -
    // only the 3 tablists above remain.
  });

  it("displays the project name", () => {
    renderShell();
    expect(screen.getByText("Test Project")).toBeTruthy();
  });

  it("calls onBack when the back affordance is used", () => {
    const onBack = vi.fn();
    renderShell({ onBack });

    fireEvent.click(screen.getByText(/Back to projects/));

    expect(onBack).toHaveBeenCalledTimes(1);
  });
});
