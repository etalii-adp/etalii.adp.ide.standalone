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
    const { container, queryByText } = render(
      <PanelPlaceholder title="Hierarchy" description="Project file tree" futureSpec="project-root-folder-explorer" />,
    );

    expect(container.querySelector(".panel-placeholder-shim")).toBeTruthy();
    expect(queryByText("Hierarchy")).toBeNull();
  });

  it("shows the static placeholder after the simulated delay", () => {
    const { container, getByText } = render(
      <PanelPlaceholder title="Hierarchy" description="Project file tree" futureSpec="project-root-folder-explorer" />,
    );

    act(() => {
      vi.runAllTimers();
    });

    expect(getByText("Hierarchy")).toBeTruthy();
    expect(getByText("Project file tree")).toBeTruthy();
    expect(container.querySelector(".panel-placeholder-shim")).toBeNull();
  });

  it("never makes a network call in either state", () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch");

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
