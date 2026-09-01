import { describe, expect, it } from "vitest";
import { elementIdOfKey, elementSelectionOf, elementSourceOf, selectedElementIdOf } from "./selection";

describe("the shared canvas selection builders", () => {
  it("builds the nested file-to-element selection every canvas reports", () => {
    // Act.
    const selection = elementSelectionOf(new Uint8Array([1, 2]), ["docs", "plan.adp"], "aaa");

    // Assert.
    // The outer level names the entry with the full path; the child names the element with an
    // empty path, which asks the backend to fill it in.
    expect(selection.path?.segments).toEqual(["docs", "plan.adp"]);
    expect(selection.detail.case).toBe("child");
    const child = selection.detail.case === "child" ? selection.detail.value : undefined;
    expect(child?.id?.source.case).toBe("elementId");
    expect(child?.id?.source.case === "elementId" ? child.id.source.value.value : "").toBe("aaa");
    expect(child?.path?.segments).toEqual([]);
    expect(child?.detail.case).toBe("none");
  });

  it("carries a gesture when one is given", () => {
    // Act.
    const selection = elementSelectionOf(new Uint8Array([1]), ["plan.adp"], "aaa", 1);

    // Assert.
    const child = selection.detail.case === "child" ? selection.detail.value : undefined;
    expect(child?.detail.case).toBe("action");
  });

  it("walks a selection chain back to its element id", () => {
    // Arrange & act.
    const selection = elementSelectionOf(new Uint8Array([1]), ["plan.adp"], "bbb");

    // Assert.
    expect(selectedElementIdOf(selection)).toBe("bbb");
    expect(selectedElementIdOf(null)).toBeUndefined();
  });

  it("reads the element id out of a selection key", () => {
    expect(elementIdOfKey("element:ccc")).toBe("ccc");
    expect(elementIdOfKey("problems")).toBeNull();
    expect(elementIdOfKey(undefined)).toBeNull();
  });

  it("builds an element source for actions and shortcuts", () => {
    // Act.
    const source = elementSourceOf("ddd");

    // Assert.
    expect(source.source.case).toBe("elementId");
    expect(source.source.case === "elementId" ? source.source.value.value : "").toBe("ddd");
  });
});
