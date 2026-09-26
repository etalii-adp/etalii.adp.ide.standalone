import { readFileSync, statSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import { cleanup, render } from "@testing-library/react";
import { DiagramCanvasCore } from "./library/DiagramCanvas";
import { BUILT_IN_SHAPES } from "./library/definition/diagramDefinition";
import type { DiagramDefinition } from "./library/definition/diagramDefinition";
import type { DiagramModel } from "./library/api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { sourceFiles } from "@client/sourceFiles";

SVGElement.prototype.setPointerCapture ??= () => {};
SVGElement.prototype.releasePointerCapture ??= () => {};

/**
 * No stylesheet in the application can repaint the library's two rings.
 *
 * ## Its name, since task 28's amendment names the old one
 *
 * This was `ringsSurviveModuleStyles.test.tsx`, and centralized-selection task 28 names it by that
 * name in its list of expected test changes. The ring it guarded no longer exists - the user
 * replaced it with one inline highlight on 2026-09-22 - and a file named for a thing that is gone
 * is a lie a later reader would act on, so the name moved with the subject.
 *
 * ## The defect
 *
 * The selected and accept looks are rings the library draws INSIDE the element's group, just
 * outside its shape (centralized-selection Requirement 5.2). A module stylesheet that styles its
 * element's shapes by descendant - `.mindmap-node rect { fill: var(--color-surface); ... }` -
 * matches the ring too, because the ring is a rect in that group, and `.mindmap-node rect`
 * outranks the ring's own single-class rule. So on the mind map the selected ring was an opaque
 * box in the surface colour, drawn after the label and on top of it, stroked in the border colour:
 * **the selection did not highlight, and the text could not be read**. c4, databricks and sparql
 * repainted the rings the same way. Nothing in a module's own tests could see it, because jsdom
 * applies no module stylesheet unless asked.
 *
 * ## What this asks, and why it reads the stylesheets rather than a list of selectors
 *
 * It renders the two rings as the library draws them - a canvas with an element both selected and
 * under a held connect - so it asks about the rings the application shows, not a copy made here.
 * Then it finds EVERY stylesheet in the tree. For each, it loads the client's sheets and that one -
 * the way the application loads a module - wraps each ring in groups carrying every class that
 * sheet names, and asks the cascade for the ring's paint. That must equal the paint of the ring
 * wrapped in nothing, and each sheet that changes it is named with what it changed. A rule of
 * any shape that reaches the ring through a class - descendant, child, compound - therefore
 * changes the answer, whichever module adds it and whatever it is called, and nothing here names
 * a module or a selector that has to be kept in step.
 *
 * ## Its limits
 *
 * <b>MEASURED against this repository's jsdom (25.0.1) on 2026-09-23, not assumed</b> - the claim had
 * been repeated by several of us and nobody had run it:
 * <list type="bullet">
 * <item><description><b>Specificity: not implemented.</b> `.outer .inner` written first loses to
 * `.inner` written last, and `.mindmap-node rect` loses to `.library-shape`. jsdom takes the LAST
 * matching rule, so a browser's answer and this file's can differ whenever two rules compete on
 * specificity alone.</description></item>
 * <item><description><b>`!important`: not implemented.</b> A stylesheet's `!important` loses to an
 * inline style here and would WIN in a browser - which is why `noModuleSelection` forbids
 * `!important` on stroke or fill in a module sheet by reading the text instead.</description></item>
 * <item><description><b>Inheritance: implemented correctly.</b> A child's own rule beats a stroke
 * inherited from its parent, exactly as a browser does - and that is the one this file depends on,
 * since the defect it now catches was a paint left on the wrapping group.</description></item>
 * </list>
 * A rule reaching the shape through an attribute selector or a pseudo-class jsdom does not match
 * (`:hover`) is still not seen: the wrapper carries classes only.
 */

/** The repository's src folder, found by walking up - the family's shared idiom. */
function sourceRoot(): string {
  let directory = __dirname;
  for (let depth = 0; depth < 12; depth++) {
    const hasModules = statSync(join(directory, "diagrams"), { throwIfNoEntry: false })?.isDirectory() === true;
    const hasStyleRules = statSync(join(directory, ".editorconfig"), { throwIfNoEntry: false })?.isFile() === true;
    if (hasModules && hasStyleRules) {
      return directory;
    }
    directory = dirname(directory);
  }
  throw new Error("The src folder was not found above this test file.");
}

/** Every .css file under the client and the modules, node_modules and build output excluded. */
function stylesheets(root: string): string[] {
  return [join(root, "client", "src"), join(root, "diagrams")]
    .flatMap((directory) => sourceFiles(directory).filter((path) => path.endsWith(".css")));
}

/** What wears the highlight: the element's own shape, and its anchors. */
const HIGHLIGHTED = ["shape", "anchor"] as const;
type Highlighted = (typeof HIGHLIGHTED)[number];

const PAINT = ["fill", "stroke", "stroke-width", "stroke-dasharray", "stroke-opacity", "fill-opacity", "opacity", "visibility", "display"] as const;

/**
 * What is compared for each part. <b>An anchor's FILL is deliberately not the highlight's business</b>:
 * the highlight states an anchor's stroke, and a module may legitimately dress the dots its own
 * notation draws - wardley's `.wardley-annotation circle` sets the same surface colour the anchor
 * already has, written without its fallback. Its stroke, and whether it is shown at all, are still
 * compared, because those are what a module could take away.
 */
const COMPARED: Record<string, readonly string[]> = {
  // The highlight states a STROKE and a WIDTH; the notation keeps everything else, and a module may
  // legitimately dress its own shapes' fill and dash - c4's boundaries are drawn dashed and unfilled
  // by `.c4-boundary rect`, which must keep working while the element is highlighted. What no module
  // may do is change the colour of the highlight, or hide what wears it.
  shape: ["stroke", "stroke-width", "stroke-opacity", "opacity", "visibility", "display"],
  anchor: ["stroke", "stroke-width", "stroke-dasharray", "stroke-opacity", "opacity", "visibility", "display"],
};

const definition: DiagramDefinition = {
  elementTypes: [{ id: "service", shape: "box", anchors: { kind: "compass", positions: ["e", "w"] }, sizing: "model" }],
  relationTypes: [
    { id: "calls", route: "straight", endpoints: { source: { elementTypes: ["service"] }, target: { elementTypes: ["service"] }, allowSelf: false } },
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
};

// Alpha's box spans x -50..50, y -20..20; Beta's 250..350.
const model: DiagramModel = {
  elements: [
    { id: "a", type: "service", x: 0, y: 0, width: 100, height: 40, label: "Alpha" },
    { id: "b", type: "service", x: 300, y: 0, width: 100, height: 40, label: "Beta" },
  ],
  connections: [],
};

/** The highlighted shape and anchor as the library renders them, on a selected element. */
function renderedHighlight(): Record<Highlighted, Element> {
  const { container } = render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={definition} model={model} events={{}} selection={[{ kind: "element", id: "b" }]} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
  const element = container.querySelector('[data-element-id="b"]')!;
  // THE DRAWN SHAPE, not "the first node with a stroke": the bug this file missed put the paint on
  // the wrapping group, where a search for a stroke finds it and calls the element painted while the
  // shape underneath is untouched. A rect, ellipse, path or polygon is what a reader actually sees.
  const shape = [...element.querySelectorAll<SVGElement>("rect, ellipse, path, polygon")].find(
    (node) => !node.hasAttribute("data-anchor") && !(node.getAttribute("class") ?? "").includes("library-resize-handle"),
  );
  const anchor = element.querySelector<SVGElement>("[data-anchor]");
  expect(shape, "the library painted no highlight on a selected element").not.toBeUndefined();
  expect(anchor, "the selected element drew no anchor to paint").not.toBeNull();
  const parts = { shape: shape!.cloneNode(true) as Element, anchor: anchor!.cloneNode(true) as Element };
  cleanup();
  return parts;
}

describe("the library's highlight survives every stylesheet", () => {
  const root = sourceRoot();
  const files = stylesheets(root);
  const classesOf = (text: string) =>
    [...new Set([...text.replace(/\/\*[\s\S]*?\*\//g, "").matchAll(/\.(-?[_a-zA-Z][\w-]*)/g)].map((match) => match[1]))].filter(
      (name) => !name.startsWith("library-"),
    );
  // The client's own sheets are always loaded - the application always has them - and each
  // module's sheet is tried beside them, the way the application loads a module.
  const clientCss = files
    .filter((path) => !relative(root, path).startsWith("diagrams"))
    .map((path) => readFileSync(path, "utf-8"))
    .join("\n");

  afterEach(() => {
    cleanup();
    document.head.innerHTML = "";
    document.body.innerHTML = "";
  });

  /** The node's paint under `css`, inside a div, an svg and two groups each carrying `wrapperClasses`. */
  function paintOf(css: string, painted: Element, wrapperClasses: string): Record<string, string> {
    const style = document.createElement("style");
    style.textContent = css;
    document.head.appendChild(style);
    const ns = "http://www.w3.org/2000/svg";
    const host = document.createElement("div");
    host.setAttribute("class", wrapperClasses);
    const svg = document.createElementNS(ns, "svg");
    svg.setAttribute("class", wrapperClasses);
    const outer = document.createElementNS(ns, "g");
    outer.setAttribute("class", wrapperClasses);
    const inner = document.createElementNS(ns, "g");
    inner.setAttribute("class", wrapperClasses);
    const rect = painted.cloneNode(true) as Element;
    inner.appendChild(rect);
    outer.appendChild(inner);
    svg.appendChild(outer);
    host.appendChild(svg);
    document.body.appendChild(host);
    const computed = getComputedStyle(rect);
    const paint = Object.fromEntries(PAINT.map((property) => [property, computed.getPropertyValue(property)]));
    document.head.innerHTML = "";
    document.body.innerHTML = "";
    return paint;
  }

  it("reads the whole tree - a floor on what it loaded, and a member it must hold", () => {
    expect(files.length, files.map((path) => relative(root, path)).join("\n")).toBeGreaterThanOrEqual(16);
    expect(files.map((path) => relative(root, path).replaceAll("\\", "/"))).toContain("diagrams/mindmap/client/mindmap.css");
  });

  it("puts the highlight on the DRAWN SHAPE, so a module rule that states a stroke cannot ignore it", () => {
    // WHAT THIS FILE MISSED ONCE, and the reason it is worth a case of its own. The library computed
    // the right paint and put it on the element's wrapping <g>, where it was INHERITED - and an SVG
    // child that states its own stroke ignores an inherited one. Every node shape states one
    // (`.canvas-node`, and module rules like `.mindmap-node rect`), so a selected element showed no
    // colour change at all while every value this file compared was correct.
    //
    // So this asserts the COMPUTED stroke of the drawn shape, with a module stylesheet loaded -
    // mindmap's, because its `.mindmap-node rect` rule is exactly the shape of rule that won.
    const shape = renderedHighlight().shape;
    const mindmap = readFileSync(join(sourceRoot(), "diagrams", "mindmap", "client", "mindmap.css"), "utf-8");

    const style = document.createElement("style");
    style.textContent = `${clientCss}
${mindmap}`;
    document.head.appendChild(style);
    const ns = "http://www.w3.org/2000/svg";
    const svg = document.createElementNS(ns, "svg");
    const group = document.createElementNS(ns, "g");
    group.setAttribute("class", "mindmap-node");
    const drawn = shape.cloneNode(true) as Element;
    drawn.setAttribute("class", `${drawn.getAttribute("class") ?? ""} canvas-node`.trim());
    group.appendChild(drawn);
    svg.appendChild(group);
    document.body.appendChild(svg);

    expect(getComputedStyle(drawn).stroke, "the drawn shape does not compute to the highlight, so something else won").toMatch(/^var\(--color-selected/);
  });

  it.each(HIGHLIGHTED)("paints a highlighted %s the same inside any element, whichever stylesheet is loaded, naming each that repaints it", (part) => {
    const painted = renderedHighlight()[part];
    const bare = paintOf(clientCss, painted, "");
    // The highlight must actually be in force, or "unchanged" would compare two defaults.
    expect(bare.stroke, `the highlighted ${part} states no stroke of its own`).toMatch(/^var\(--color-selected/);

    const offenders: string[] = [];
    for (const path of files) {
      const own = readFileSync(path, "utf-8");
      const inside = paintOf(`${clientCss}\n${own}`, painted, classesOf(own).join(" "));
      const changed = COMPARED[part].filter((property) => inside[property] !== bare[property]);
      if (changed.length > 0) {
        offenders.push(`${relative(root, path)}: ${changed.map((property) => `${property} ${bare[property]} -> ${inside[property]}`).join(", ")}`);
      }
    }

    expect(offenders, offenders.join("\n")).toEqual([]);
  });

  // EVERY SHAPE THE LIBRARY CAN DRAW, not the shapes some module happens to declare today. The
  // case above renders ONE element type, `shape: "box"`, so it could only ever see the paint reach
  // a box - and `styled-box` shipped with no highlight at all, because its call in
  // `renderShapeBody` was the one that omitted `style={paint}`. c4 is the only module declaring
  // it, so c4 alone was unpainted; `diamond`, `hexagon` and `parallelogram` would have been the
  // same defect the day a module first declared one.
  //
  // So this walks the library's own `BUILT_IN_SHAPES` and fails for each shape whose drawing does
  // not receive the highlight. A shape added to that list and not to the switch fails here on the
  // day it is added, rather than on the day a module first uses it.
  it.each(BUILT_IN_SHAPES.filter((shape) => shape !== "none"))("paints a highlighted %s, whichever module does or does not declare it", (shape) => {
    // `none` is the one exemption and it is a real one: it draws no body at all - the element is
    // its labels and its decorations - so there is nothing to paint. Every other member must draw
    // something that wears the highlight.
    const { container } = render(
      <DiagramViewProvider>
        <DiagramToolboxProvider>
          <DiagramCanvasCore
            definition={{ ...definition, elementTypes: [{ ...definition.elementTypes[0], shape }] }}
            model={model}
            events={{}}
            selection={[{ kind: "element", id: "b" }]}
          />
        </DiagramToolboxProvider>
      </DiagramViewProvider>,
    );

    const element = container.querySelector('[data-element-id="b"]');
    expect(element, `a selected ${shape} rendered no element at all`).not.toBeNull();
    const drawn = [...element!.querySelectorAll<SVGElement>("rect, ellipse, path, polygon, circle")].filter(
      (node) => !node.hasAttribute("data-anchor") && !(node.getAttribute("class") ?? "").includes("library-resize-handle"),
    );
    expect(drawn.length, `a selected ${shape} drew no shape to paint`).toBeGreaterThan(0);

    // AT LEAST ONE, deliberately, and the reason is worth stating: several shapes draw more than
    // one node and not all of them are the outline - a cylinder's body and its two ellipses, a
    // styled box's silhouette and its badge. The defect this catches is a whole case forgetting
    // the paint, which takes every node with it, so "at least one wears it" separates the two
    // states cleanly. It would NOT catch one node of a multi-part shape being missed.
    const wearing = drawn.filter((node) => (node.style.stroke ?? "").startsWith("var(--color-selected"));
    expect(
      wearing.length,
      `a selected ${shape} draws ${drawn.length} shape(s) and not one carries the highlight - its case in renderShapeBody is missing style={paint}`,
    ).toBeGreaterThan(0);
    cleanup();
  });
});
