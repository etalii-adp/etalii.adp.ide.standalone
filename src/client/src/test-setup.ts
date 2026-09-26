import { afterEach } from "vitest";
import { cleanup } from "@testing-library/react";

afterEach(() => {
  cleanup();
});

// jsdom implements no pointer capture on SVG elements. The canvas's gesture arbiter uses it so a
// release outside the surface still ends the gesture, which every real browser supports - stubbed
// here, once, rather than feature-detected in the component, so the production path stays the one
// that ships (client-centralization Requirement 10.1). `??=` leaves a real implementation alone,
// and a test that needs to watch a capture spies on the prototype as before.
SVGElement.prototype.setPointerCapture ??= () => {};
SVGElement.prototype.releasePointerCapture ??= () => {};
