import { describe, expect, it, vi } from "vitest";
import { onTextTabRequested, requestTextTab } from "./textTabRequests";

describe("textTabRequests", () => {
  it("delivers a request to every listener, and unsubscribing stops it", () => {
    // Arrange.
    const heard = vi.fn();
    const unsubscribe = onTextTabRequested(heard);

    // Act and assert, step by step.
    requestTextTab({ path: ["a.txt"], editorId: "*" });
    expect(heard).toHaveBeenCalledWith({ path: ["a.txt"], editorId: "*" });

    unsubscribe();
    requestTextTab({ path: ["b.txt"], editorId: "plain", line: 3 });
    expect(heard).toHaveBeenCalledTimes(1);
  });

  it("is safe to call with nobody listening", () => {
    // Act and assert: no tab strip mounted is an ordinary state, not an error.
    expect(() => requestTextTab({ path: ["a.txt"], editorId: "*" })).not.toThrow();
  });
});
