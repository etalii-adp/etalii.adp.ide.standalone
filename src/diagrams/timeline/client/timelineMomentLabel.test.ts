import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

// A moment's label stands beside its diamond, outside any shape that takes a press; the shared
// `.canvas-node-label` rule switches presses off, so clicking the label selected nothing (etalii.adp
// spec 092, host test T035). jsdom applies no stylesheet, so the rule is read here instead.
const css = readFileSync(join(dirname(fileURLToPath(import.meta.url)), "timeline.css"), "utf8");

describe("a moment's label", () => {
  it("takes the press over the box it fills, more specifically than the shared rule switches it off", () => {
    const rule = /\.canvas-node-label\.timeline-moment-label\s*\{([^}]*)\}/.exec(css);
    expect(rule?.[1]).toMatch(/pointer-events:\s*bounding-box/);
  });
});
