import { describe, expect, it } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { reportedCall, useCanvasRefusalLine, type CanvasRefusalReporter } from "./canvasRefusals";

/**
 * The one refusal line's rule (client-centralization Requirement 2, cleared as the user ruled on
 * 2026-09-25): a refusal shows in the backend's words, a new gesture clears it, a click dismisses
 * it, and no refusal is dropped for arriving late.
 */

function recorder(): { reporter: CanvasRefusalReporter; told: string[] } {
  const told: string[] = [];
  return {
    told,
    reporter: { attempted: () => told.push("attempted"), refused: (message) => told.push(`refused: ${message}`) },
  };
}

describe("a canvas's refusal line", () => {
  it("shows a refusal in the backend's own words", () => {
    // Arrange.
    const { result } = renderHook(() => useCanvasRefusalLine());

    // Act.
    act(() => result.current.reporter.refused("That connection would make a cycle."));

    // Assert.
    expect(result.current.message).toBe("That connection would make a cycle.");
  });

  it("clears when a new gesture is sent, so it says the LAST attempt failed", () => {
    // Arrange: the defect this ends - a stale refusal stayed through later successful commands.
    const { result } = renderHook(() => useCanvasRefusalLine());
    act(() => result.current.reporter.refused("Refused."));

    // Act.
    act(() => result.current.reporter.attempted());

    // Assert.
    expect(result.current.message).toBeNull();
  });

  it("is dismissed by a click", () => {
    // Arrange.
    const { result } = renderHook(() => useCanvasRefusalLine());
    act(() => result.current.reporter.refused("Refused."));

    // Act.
    act(() => result.current.dismiss());

    // Assert.
    expect(result.current.message).toBeNull();
  });

  it("does not blank a refusal for an empty sentence", () => {
    // Arrange.
    const { result } = renderHook(() => useCanvasRefusalLine());
    act(() => result.current.reporter.refused("Refused."));

    // Act.
    act(() => result.current.reporter.refused(""));

    // Assert.
    expect(result.current.message).toBe("Refused.");
  });

  it("keeps one reporter for the life of the canvas, so calls wrapped with it are not rebuilt", () => {
    // Arrange.
    const { result } = renderHook(() => useCanvasRefusalLine());
    const first = result.current.reporter;

    // Act.
    act(() => result.current.reporter.refused("Refused."));

    // Assert.
    expect(result.current.reporter).toBe(first);
  });
});

describe("a reported call", () => {
  it("tells the line it was attempted before it is sent, and refused after", async () => {
    // Arrange.
    const { reporter, told } = recorder();

    // Act.
    const outcome = await reportedCall(reporter, async () => {
      told.push("sent");
      return { accepted: false, error: "No." };
    });

    // Assert: the order is the rule - clear first, then say what this attempt got.
    expect(told).toEqual(["attempted", "sent", "refused: No."]);
    expect(outcome).toEqual({ accepted: false, error: "No." });
  });

  it("reports only the attempt when the call is accepted", async () => {
    // Arrange.
    const { reporter, told } = recorder();

    // Act.
    await reportedCall(reporter, () => Promise.resolve({ accepted: true, error: "" }));

    // Assert.
    expect(told).toEqual(["attempted"]);
  });

  it("still shows a refusal that lands after a newer attempt was sent", async () => {
    // Arrange: two gestures in flight; the first is refused after the second was sent.
    const { reporter, told } = recorder();
    type Outcome = { accepted: boolean; error: string };
    let refuseFirst: (outcome: Outcome) => void = () => {};
    const first = reportedCall(reporter, () => new Promise<Outcome>((resolve) => (refuseFirst = resolve)));
    const second = reportedCall(reporter, () => Promise.resolve({ accepted: true, error: "" }));
    await second;

    // Act.
    refuseFirst({ accepted: false, error: "The first one was refused." });
    await first;

    // Assert: dropping it would be the silence this channel exists to end.
    expect(told).toEqual(["attempted", "attempted", "refused: The first one was refused."]);
  });

  it("with no canvas in scope, runs the call and reports to nobody", async () => {
    // Act.
    const outcome = await reportedCall(null, () => Promise.resolve({ accepted: false, error: "No." }));

    // Assert.
    expect(outcome).toEqual({ accepted: false, error: "No." });
  });
});
