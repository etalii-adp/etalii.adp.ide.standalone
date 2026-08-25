import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, render } from "@testing-library/react";
import { PanelPlaceholder } from "./PanelPlaceholder";

describe("PanelPlaceholder", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("shows the loading shim immediately on mount", () => {
    // Arrange and act.
    const { container, queryByText } = render(
      <PanelPlaceholder title="Hierarchy" description="Project file tree" futureSpec="project-root-folder-explorer" />,
    );

    // Assert.
    expect(container.querySelector(".panel-placeholder-shim")).toBeTruthy();
    expect(queryByText("Hierarchy")).toBeNull();
  });

  it("shows the static placeholder after the simulated delay", () => {
    // Arrange.
    const { container, getByText } = render(
      <PanelPlaceholder title="Hierarchy" description="Project file tree" futureSpec="project-root-folder-explorer" />,
    );

    // Act.
    act(() => {
      vi.runAllTimers();
    });

    // Assert.
    expect(getByText("Hierarchy")).toBeTruthy();
    expect(getByText("Project file tree")).toBeTruthy();
    expect(container.querySelector(".panel-placeholder-shim")).toBeNull();
  });

  it("never makes a network call in either state", () => {
    // Arrange.
    const fetchSpy = vi.spyOn(globalThis, "fetch");

    // Act and assert, step by step.
    render(
      <PanelPlaceholder title="Hierarchy" description="Project file tree" futureSpec="project-root-folder-explorer" />,
    );
    expect(fetchSpy).not.toHaveBeenCalled();

    act(() => {
      vi.runAllTimers();
    });
    expect(fetchSpy).not.toHaveBeenCalled();

    fetchSpy.mockRestore();
  });
});
