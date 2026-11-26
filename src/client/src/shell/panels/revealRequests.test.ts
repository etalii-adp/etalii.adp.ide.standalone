import { describe, expect, it } from "vitest";
import { onElementRevealRequested, onRevealRequested, requestReveal, takeElementReveal, type RevealRequest } from "./revealRequests";

const reveal = (elementId: string): RevealRequest => ({ entryId: new Uint8Array(16).fill(3), path: ["specs", "features.yaml"], mimeType: "generic/knowledge", elementId });

describe("revealRequests", () => {
  it("tells the tab strip and the canvases, and leaves the element for the canvas of that document, once", () => {
    // Arrange.
    const heard: RevealRequest[] = [];
    let canvases = 0;
    const stopTabs = onRevealRequested((request) => heard.push(request));
    const stopCanvases = onElementRevealRequested(() => (canvases += 1));

    // Act.
    requestReveal(reveal("checkout"));
    const elsewhere = takeElementReveal(["other.mm"], () => true);
    const first = takeElementReveal(["specs", "features.yaml"], () => true);
    const second = takeElementReveal(["specs", "features.yaml"], () => true);
    stopTabs();
    stopCanvases();

    // Assert.
    expect(heard.map((request) => request.elementId)).toEqual(["checkout"]);
    expect(canvases).toBe(1);
    expect([elsewhere, first, second]).toEqual([null, "checkout", null]);
  });

  it("keeps the element waiting while the canvas does not draw it yet", () => {
    // Arrange.
    requestReveal(reveal("checkout"));

    // Act.
    const early = takeElementReveal(["specs", "features.yaml"], () => false);
    const loaded = takeElementReveal(["specs", "features.yaml"], (id) => id === "checkout");

    // Assert.
    expect([early, loaded]).toEqual([null, "checkout"]);
  });
});
