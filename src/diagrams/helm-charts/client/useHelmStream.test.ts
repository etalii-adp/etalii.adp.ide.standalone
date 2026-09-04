import { describe, expect, it } from "vitest";
import { useHelmStream } from "./useHelmStream";

/**
 * The hook's shape rather than its streaming behaviour - the streaming half is the same loop
 * the sibling hooks already cover, and duplicating it here would test connectrpc rather than
 * this module.
 *
 * What is worth pinning is the PRESENCE: this type's one edit is the reposition, so the hook
 * must expose moveElementTo - the member the read-only sibling deliberately lacks - and no
 * viewport report, because the whole chart arrives at open.
 */
describe("useHelmStream", () => {
  it("exposes the one edit this type has: moveElementTo", () => {
    // Arrange, act and assert.
    const source = useHelmStream.toString();
    expect(source).toContain("moveElement");
  });

  it("exposes reportView, so the canvas can close the view-delta loop", () => {
    // Arrange, act and assert.
    // This test asserted the opposite until view-delta-adoption task 3: the chart used to
    // arrive whole at open and the hook deliberately reported nothing. It now narrows to what
    // the reader is looking at, and the hook hands the canvas the report function to do it.
    const source = useHelmStream.toString();
    expect(source).toContain("reportView");
    expect(source).toContain("loading");
    expect(source).toContain("failed");
  });
});
