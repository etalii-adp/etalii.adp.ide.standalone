import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderHook } from "@testing-library/react";
import { useDiagramStream } from "./useDiagramStream";

/**
 * A move that the backend refuses says WHY, in the backend's own words (client-centralization
 * Requirement 7, the specification's one permitted visible change besides the theme).
 *
 * Fourteen modules each built `moveElementTo`; thirteen bodies were identical and one had drifted.
 * `ansible-structure`'s caught the failure and returned a fixed "The position could not be saved."
 * - so a transport that said exactly what went wrong was replaced by a sentence that says nothing,
 * on the one canvas nobody was comparing against the others. The move now lives once, in
 * `useDiagramStream`, and this is the behavioural half of keeping it honest; the source-level half,
 * that no module builds its own, is in `diagramStreamOpensOnlyInHook.test.ts`.
 *
 * <b>The thrown case is the one that matters</b>, because it is the only one the drifted copy got
 * wrong: a returned refusal travelled through both bodies alike. It has to be seen failing against
 * the fixed-sentence catch before it is believed.
 */

const moveElement = vi.fn();

// The stream is not under test here, so `open` never yields: the loop parks on its first read and
// the reconnect path never runs, which keeps a move test from racing a reconnect.
const open = vi.fn(() => ({
  [Symbol.asyncIterator]: () => ({ next: () => new Promise<never>(() => {}) }),
}));

vi.mock("@connectrpc/connect", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@connectrpc/connect")>();
  return { ...actual, createClient: () => ({ open, moveElement, updateView: vi.fn() }) };
});

vi.mock("@client/auth/AuthContext", () => {
  // One transport identity, as AuthContext guarantees - a fresh one per render churns the effect.
  const transport = {};
  return { useAuth: () => ({ transport }) };
});

const watchId = new Uint8Array(16);
vi.mock("@client/shell/context/ContextConnectionProvider", () => ({
  useContextConnection: () => ({ watchId }),
}));

const projectId = new Uint8Array([7]);
const path = ["diagrams", "example.adp"] as const;

function mountStream() {
  return renderHook(() => useDiagramStream(projectId, path, { empty: true }, (current) => current));
}

describe("useDiagramStream's move", () => {
  beforeEach(() => {
    moveElement.mockReset();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it("reports the backend's refusal in the backend's own words", async () => {
    // Arrange.
    moveElement.mockResolvedValue({ error: "That element is locked by an open review." });
    const { result } = mountStream();

    // Act.
    const refusal = await result.current.moveElementTo("element:a", 10, 20);

    // Assert.
    expect(refusal).toBe("That element is locked by an open review.");
  });

  it("reports nothing when the move is accepted", async () => {
    // Arrange.
    moveElement.mockResolvedValue({ error: "" });
    const { result } = mountStream();

    // Act.
    const refusal = await result.current.moveElementTo("element:a", 10, 20);

    // Assert: an empty string is the contract for "accepted" - callers clear their surface on it.
    expect(refusal).toBe("");
  });

  it("reports a failed call by what failed, not by a sentence that could mean anything", async () => {
    // Arrange: the transport itself fails, which is the case the drifted copy swallowed.
    moveElement.mockRejectedValue(new Error("The server closed the connection."));
    const { result } = mountStream();

    // Act.
    const refusal = await result.current.moveElementTo("element:a", 10, 20);

    // Assert.
    expect(refusal).toBe("The server closed the connection.");
    expect(refusal).not.toBe("The position could not be saved.");
  });

  it("asks for the move at the position given, on this diagram, for this watcher", async () => {
    // Arrange.
    moveElement.mockResolvedValue({ error: "" });
    const { result } = mountStream();

    // Act.
    await result.current.moveElementTo("element:a", 10, 20);

    // Assert: the request is the one the fourteen wrappers built - position present, which is
    // what makes the backend treat it as an arrangement rather than a re-parenting.
    expect(moveElement).toHaveBeenCalledWith({
      projectId: { value: projectId },
      watchId: { value: watchId },
      path: { segments: [...path] },
      elementId: "element:a",
      position: { x: 10, y: 20 },
    });
  });
});
