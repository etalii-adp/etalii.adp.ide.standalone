import { readFileSync } from "node:fs";
import { join } from "node:path";
import { afterEach, describe, expect, it } from "vitest";

/**
 * The "Opening…" status sits in the middle of the canvas, where somebody looking at a blank one
 * will see it.
 *
 * ## The defect, and why it was reported as something else entirely
 *
 * A large SKOS vocabulary takes about twenty seconds to arrive on a cold backend. Measured on
 * 2026-09-24: `business-economics` (17,683 triples) drew nothing until ~20 s and then 1,151 nodes,
 * against `geographic-names` (5,083 triples) at ~2.5 s. Through the whole of that window the canvas
 * drew nothing and this element said "Opening…" — at 12px, in the bottom-left, occupying
 * **0.9% of the canvas area**. It was present, it computed to `visibility: visible` and `opacity: 1`,
 * and a careful session looked straight at it and filed the document as one that renders an empty
 * canvas. Telling somebody where nobody looks is not telling them.
 *
 * ## Why this is not a skos change
 *
 * `.canvas-status` is declared once, in `canvas.css`, and SEVEN canvases render it: databricks,
 * dependency-graph, owl, rdf, skos, sparql and timeline. Every one uses the identical
 * `{loading ? … : null}`, and `useDiagramStream` empties the model whenever `loading` becomes true —
 * on the first open and again before each reconnect — so the status never covers a drawn diagram
 * and all seven can be centred safely. Fixing skos alone would have left the same corner behind the
 * other six and the next large document would have filed the same report.
 *
 * ## What this test can and cannot see
 *
 * It asserts the COMPUTED value, never a class name or an attribute: a class says nothing about
 * whether a rule matched, and a presentation attribute loses to any CSS rule — a distinction that
 * has already caused a board-wide defect here.
 *
 * **It cannot assert a rendered size.** jsdom performs no layout, so every `getBoundingClientRect`
 * is zero whatever the stylesheet says, and an assertion on width or height would be false for the
 * fixed code as well as the broken code. jsdom also applies stylesheets in source order and
 * implements neither specificity nor `!important`. So this proves the rule is DECLARED and REACHED;
 * what a real cascade resolves, and what the thing actually looks like, is the browser check.
 */
describe("the canvas status", () => {
  const withCanvasCss = (run: (host: HTMLElement) => void) => {
    const style = document.createElement("style");
    style.textContent = readFileSync(join(__dirname, "canvas.css"), "utf-8");
    document.head.appendChild(style);
    const host = document.createElement("div");
    host.className = "canvas-host";
    document.body.appendChild(host);
    try {
      run(host);
    } finally {
      host.remove();
      style.remove();
    }
  };

  afterEach(() => {
    document.body.innerHTML = "";
  });

  it("is centred on the canvas, not parked in a corner", () => {
    withCanvasCss((host) => {
      const status = document.createElement("p");
      status.className = "skos-status canvas-status";
      status.textContent = "Opening…";
      host.appendChild(status);

      const computed = getComputedStyle(status);

      // The centring, asserted as the two properties that produce it. Against the cornered rule
      // these are "" and "", so this is red before the change and green after it.
      expect(computed.top, "the status is not pulled to the middle of the canvas").toBe("50%");
      expect(computed.left, "the status is not pulled to the middle of the canvas").toBe("50%");
      expect(
        computed.transform,
        "the status is placed by its corner rather than its centre, so it hangs off the middle",
      ).toContain("translate(-50%, -50%)");
    });
  });

  it("reaches shacl too, whose status was outside the shared rule entirely", () => {
    // THE EIGHTH CANVAS. Seven render `<module>-status canvas-status`; shacl rendered its own
    // `shacl-loading canvas-hint` saying "Loading...". canvas-hint is an SVG TEXT rule - fill and
    // text-anchor - so on an HTML paragraph it did nothing, and the centring above could not reach
    // an element that was not using the shared class. One cause, two symptoms.
    //
    // The class is READ FROM THE COMPONENT rather than written here, so this cannot drift into
    // asserting about a string the source no longer renders - and the extraction asserts itself
    // first, because a regex that matches nothing would otherwise make the whole case vacuous.
    const source = readFileSync(
      join(__dirname, "..", "..", "..", "diagrams", "rdf", "client", "ShaclCanvas.tsx"),
      "utf-8",
    );
    const rendered = /loading && model\.shapes\.size === 0 \? <p className="([^"]+)"/.exec(source);
    expect(rendered, "shacl's loading branch was not found, so this case would prove nothing").not.toBeNull();

    withCanvasCss((host) => {
      const status = document.createElement("p");
      status.className = rendered![1];
      host.appendChild(status);

      const computed = getComputedStyle(status);
      expect(
        computed.top,
        `shacl renders class "${rendered![1]}", which resolves to no status placement - it is outside the shared rule`,
      ).toBe("50%");
      expect(computed.transform).toContain("translate(-50%, -50%)");
    });
  });
  it("leaves the rejection in its corner, because that one answers a gesture over a drawn diagram", () => {
    // THE CONTROL. Without it, moving every absolutely-positioned message to the middle would pass
    // the test above just as well - and a rejection centred over a diagram somebody is editing is a
    // worse defect than the one being fixed. This says the split was deliberate.
    withCanvasCss((host) => {
      const rejection = document.createElement("p");
      rejection.className = "skos-rejection canvas-rejection";
      rejection.textContent = "That edit was refused.";
      host.appendChild(rejection);

      const computed = getComputedStyle(rejection);

      expect(computed.bottom, "the rejection left the corner it belongs in").toBe("32px");
      expect(computed.left, "the rejection left the corner it belongs in").toBe("12px");
    });
  });
});
