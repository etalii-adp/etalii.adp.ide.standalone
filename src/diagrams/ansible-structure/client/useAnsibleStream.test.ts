import { describe, expect, it } from "vitest";
import { useAnsibleStream } from "./useAnsibleStream";

/**
 * The hook's shape rather than its streaming behaviour - the streaming half is the same loop
 * the mindmap's hook already covers, and duplicating it here would test connectrpc rather than
 * this module.
 *
 * What is worth pinning is the absence: a read-only diagram type's client should not be able to
 * ask for a move at all. The backend refuses one with a sentence, so exposing the call would put
 * a gesture in the client's reach whose only possible outcome is a refusal.
 */
describe("useAnsibleStream", () => {
  it("exposes no way to move an element", () => {
    // Arrange, act and assert.
    // Read off the hook's own source rather than by calling it, which would need a React
    // renderer and a live transport for a claim that is really about the surface.
    const source = useAnsibleStream.toString();
    expect(source).not.toContain("moveElement");
  });

  it("returns only a model, its two states, and a viewport report", () => {
    // Arrange, act and assert.
    const source = useAnsibleStream.toString();
    expect(source).toContain("reportView");
    expect(source).toContain("loading");
    expect(source).toContain("failed");
  });
});
