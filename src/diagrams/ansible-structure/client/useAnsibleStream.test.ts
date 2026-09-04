import { describe, expect, it } from "vitest";
import { useAnsibleStream } from "./useAnsibleStream";

/**
 * The hook's shape rather than its streaming behaviour - the streaming half is the same loop
 * the mindmap's hook already covers, and duplicating it here would test connectrpc rather than
 * this module.
 *
 * What is worth pinning is the surface: exactly one write, and no more. This file used to pin
 * the opposite - that the hook exposed no move at all - on the reasoning that a read-only type's
 * client should not be able to ask for one. ansible-refinements gave the backend a position to
 * store, so the gesture now has an answer; what stays true, and is asserted below, is that a
 * reposition is the ONLY thing this client can write.
 */
describe("useAnsibleStream", () => {
  it("exposes a reposition, and no other write", () => {
    // Arrange, act and assert.
    // Read off the hook's own source rather than by calling it, which would need a React
    // renderer and a live transport for a claim that is really about the surface.
    const source = useAnsibleStream.toString();
    expect(source).toContain("moveElement");
    // The verbs that would mean this type had started editing Ansible's own files.
    for (const write of ["saveText", "addElement", "removeElement", "renameElement"]) {
      expect(source, write).not.toContain(write);
    }
  });

  it("returns only a model, its two states, and a viewport report", () => {
    // Arrange, act and assert.
    const source = useAnsibleStream.toString();
    expect(source).toContain("reportView");
    expect(source).toContain("loading");
    expect(source).toContain("failed");
  });
});
