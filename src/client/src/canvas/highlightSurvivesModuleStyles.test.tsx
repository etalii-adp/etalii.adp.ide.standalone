import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { afterEach, describe, expect, it } from "vitest";
import { cleanup, render } from "@testing-library/react";
import { DiagramCanvasCore } from "./library/DiagramCanvas";
import type { DiagramDefinition } from "./library/definition/diagramDefinition";
import type { DiagramModel } from "./library/api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";

SVGElement.prototype.setPointerCapture ??= () => {};
SVGElement.prototype.releasePointerCapture ??= () => {};

/**
 * No stylesheet in the application can repaint the library's two rings.
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
 * jsdom's cascade is source order alone - no specificity, no `!important` - which is why the fix
 * it guards is inline paint: the one thing both jsdom and a browser apply over every ordinary
 * rule. So a module rule marked `!important`, which would beat inline paint in a browser, is not
 * seen here. Nor is a rule reaching the ring through an attribute selector or a pseudo-class jsdom
 * does not match (`:hover`): the wrapper carries classes only.
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
  const found: string[] = [];
  const walk = (directory: string) => {
    for (const child of readdirSync(directory, { withFileTypes: true })) {
      const path = join(directory, child.name);
      if (child.isDirectory()) {
        if (!["node_modules", "bin", "obj", "dist"].includes(child.name)) {
          walk(path);
        }
      } else if (child.name.endsWith(".css")) {
        found.push(path);
      }
    }
  };
  walk(join(root, "client", "src"));
  walk(join(root, "diagrams"));
  return found;
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
  shape: PAINT,
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
  const shape = [...element.querySelectorAll<SVGElement>("*")].find((node) => node.style?.stroke !== "" && !node.hasAttribute("data-anchor"));
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
});
