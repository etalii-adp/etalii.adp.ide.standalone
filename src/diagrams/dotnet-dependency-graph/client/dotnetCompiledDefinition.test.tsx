import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import disText from "../definition/dotnet-dependency-graph.dis?raw";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramDefinition } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { fakeContextConnection } from "@client/canvas/library/testing/canvasHarness";
import { DOTNET_BINDINGS, dependencyRoute } from "./dotnetBindings";

vi.mock("@client/shell/context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@client/shell/context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextPrompt: () => ({ prompt: null, onPropose: vi.fn(), onSubmit: vi.fn(), onCancel: vi.fn() }),
    useContextConnection: () => connection,
    useContextSelection: () => ({ selection: null }),
  };
});

const connection = fakeContextConnection({ select: vi.fn(), revealPath: vi.fn() });

const { DiagramCanvas } = await import("@client/canvas/library/DiagramCanvas");
const { DOTNET_DEPENDENCY_DEFINITION } = await import("./DotNetDependencyGraphCanvas");

/**
 * The .NET dependency graph's canvas definition, compiled from its bundled DISL specification, draws
 * what the definition the module stated by hand until the switch-over drew.
 *
 * <b>The oracle below is that hand-written definition, moved here unchanged</b> from
 * `DotNetDependencyGraphCanvas.tsx`, with the payload its model carried. The two are not equal key for
 * key, and cannot be: the specification has a node type per kind where the hand-written definition had
 * one `node` type told apart by a payload class, so the compiled definition states per type what the
 * oracle stated once - the kind's class, the conflict's class on a package only (a project never has
 * one), the activate action on a project only (a package's did nothing), and each reference's end
 * types. The rest differ only by a library default stated or left out: a label's `placement: "inside"`,
 * a label offset measured from the centre, a tooltip given as the one title path rather than as parts
 * joining to the same text, anchors that start no connection where every source end refused to, and
 * one reference per ordered pair, which the readers already guarantee. So the test is the drawing:
 * both definitions render the same model, and the markup must be the same, element for element.
 */
const HAND_WRITTEN: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    {
      id: "node",
      shape: "span",
      classNames: [
        { className: "dotnet-dependency-element canvas-element", on: "element" },
        { className: { template: "dotnet-dependency-element-{payload.kindClass}" }, on: "element" },
        { className: "dotnet-dependency-conflict", on: "element", when: { path: "payload.hasVersionConflict", is: "true" } },
        { className: "dotnet-dependency-node canvas-node", on: "shape" },
      ],
      labels: [
        {
          text: { path: "payload.name" },
          placement: "inside",
          truncate: true,
          className: "dotnet-dependency-node-label canvas-node-label",
        },
        {
          text: { path: "payload.subtitle" },
          offset: { x: 0, y: 14 },
          when: { path: "payload.subtitle", is: "non-empty" },
          className: "dotnet-dependency-node-subtitle",
        },
      ],
      tooltip: {
        parts: [
          { template: "Project {payload.name}", when: { path: "payload.kindClass", equals: "project" } },
          { template: "Package {payload.name}", when: { path: "payload.kindClass", equals: "package" } },
          { template: "({payload.subtitle})", when: { path: "payload.hasVersions", is: "true" } },
          { template: "- referenced at more than one version", when: { path: "payload.hasVersionConflict", is: "true" } },
        ],
        join: " ",
      },
      accessibility: { role: "button", focusable: true, label: { path: "payload.title" } },
      actions: [
        { id: "dotnet-dependency.activate", invokedBy: [{ kind: "gesture", gesture: "activate" }], appliesTo: [{ kind: "element" }] },
      ],
      anchors: { kind: "edge", edgeSides: "horizontal" },
      sizing: "model",
      deletable: false,
    },
  ],
  relationTypes: [
    {
      id: "project-reference",
      route: dependencyRoute,
      style: { endMarker: "arrow" },
      className: "dotnet-dependency-edge dotnet-dependency-edge-project",
      lineClassName: "dotnet-dependency-edge-line",
      endpoints: {
        source: { elementTypes: ["node"], anchors: [] },
        target: { elementTypes: ["node"], anchors: "edge" },
        allowSelf: false,
      },
    },
    {
      id: "package-reference",
      route: dependencyRoute,
      style: { endMarker: "arrow" },
      className: "dotnet-dependency-edge dotnet-dependency-edge-package",
      lineClassName: "dotnet-dependency-edge-line",
      endpoints: {
        source: { elementTypes: ["node"], anchors: [] },
        target: { elementTypes: ["node"], anchors: "edge" },
        allowSelf: false,
      },
    },
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
});

/** A box as the canvas now builds it: typed by its kind, with the payload the compiled definition reads. */
function box(id: string, kind: "project" | "package", x: number, y: number, subtitle: string, conflict = false): DiagramModelElement {
  const name = id.split(":").slice(1).join(":").split("/").at(-1)!.replace(/\.csproj$/, "");
  return {
    id,
    type: kind,
    x,
    y,
    width: 220,
    height: 56,
    label: name,
    payload: {
      name,
      subtitle,
      hasVersionConflict: conflict,
      title: kind === "package"
        ? `Package ${name}${subtitle ? ` (${subtitle})` : ""}${conflict ? " - referenced at more than one version" : ""}`
        : `Project ${name}`,
    },
  };
}

/**
 * Every case the drawing distinguishes: a project with frameworks and one with none, a package with a
 * version, one in conflict and one whose version is not discoverable, references leading back the way
 * they came (every project reference) and forward into the package band.
 */
const MODEL: DiagramModel = {
  elements: [
    box("project:src/Core/Core.csproj", "project", 0, 0, "net10.0"),
    box("project:src/Storage/Storage.csproj", "project", 320, 0, "net10.0, netstandard2.0"),
    box("project:src/Legacy/Legacy.csproj", "project", 320, 90, ""),
    box("package:Serilog", "package", 840, 0, "3.1.1, 4.4.0", true),
    box("package:System.Text.Json", "package", 840, 90, "9.0.0"),
    box("package:Unversioned", "package", 840, 180, ""),
  ],
  connections: [
    { id: "depends:project:src/Storage/Storage.csproj->project:src/Core/Core.csproj", type: "project-reference", sourceId: "project:src/Storage/Storage.csproj", targetId: "project:src/Core/Core.csproj" },
    { id: "depends:project:src/Legacy/Legacy.csproj->project:src/Core/Core.csproj", type: "project-reference", sourceId: "project:src/Legacy/Legacy.csproj", targetId: "project:src/Core/Core.csproj" },
    { id: "depends:project:src/Core/Core.csproj->package:Serilog", type: "package-reference", sourceId: "project:src/Core/Core.csproj", targetId: "package:Serilog" },
    { id: "depends:project:src/Storage/Storage.csproj->package:System.Text.Json", type: "package-reference", sourceId: "project:src/Storage/Storage.csproj", targetId: "package:System.Text.Json" },
    { id: "depends:project:src/Legacy/Legacy.csproj->package:Unversioned", type: "package-reference", sourceId: "project:src/Legacy/Legacy.csproj", targetId: "package:Unversioned" },
  ],
};

/** The same model as the hand-written definition read it: one `node` type, told apart by the payload's kind class. */
const HAND_WRITTEN_MODEL: DiagramModel = {
  ...MODEL,
  elements: MODEL.elements.map((element) => {
    const payload = element.payload as { subtitle: string };
    return { ...element, type: "node", payload: { ...payload, kindClass: element.type, hasVersions: element.type === "package" && payload.subtitle !== "" } };
  }),
};

/** The canvas's markup, with the ids React generates per mount taken out. */
function drawn(definition: DiagramDefinition, model: DiagramModel): string {
  const { container, unmount } = render(
    <DiagramCanvas
      definition={definition}
      model={model}
      events={{}}
      source={{ entryId: new Uint8Array(16), path: ["Solution.adp"] }}
      ariaLabel=".NET dependency graph"
      className="dotnet-dependency-canvas"
      scrollbarsClassName="dotnet-dependency-scrollbars"
    />,
  );
  const markup = container.innerHTML.replace(/«r[0-9a-z]+»|:r[0-9a-z]+:/g, "«id»");
  unmount();
  return markup;
}

describe("the .NET dependency graph's compiled definition", () => {
  it("draws what the hand-written definition drew, element for element", () => {
    const compiled = drawn(DOTNET_DEPENDENCY_DEFINITION, MODEL);

    // The case the comparison would pass vacuously on: a canvas that drew nothing for either.
    expect(compiled).toContain("dotnet-dependency-element-package");
    expect(compiled).toContain("dotnet-dependency-conflict");
    expect(compiled).toContain("dotnet-dependency-edge-line");
    expect(compiled).toBe(drawn(HAND_WRITTEN, HAND_WRITTEN_MODEL));
  });

  it("states per kind what the hand-written definition stated once", () => {
    const [project, pkg] = DOTNET_DEPENDENCY_DEFINITION.elementTypes;
    const [projectReference, packageReference] = DOTNET_DEPENDENCY_DEFINITION.relationTypes;

    expect(DOTNET_DEPENDENCY_DEFINITION.elementTypes.map((type) => type.id)).toEqual(["project", "package"]);
    expect(project?.deletable).toBe(false);
    expect(pkg?.deletable).toBe(false);
    expect(DOTNET_DEPENDENCY_DEFINITION.actions).toEqual([
      { id: "dotnet-dependency.activate", invokedBy: [{ kind: "gesture", gesture: "activate" }], appliesTo: [{ kind: "element", elementTypes: ["project"] }] },
    ]);
    expect(projectReference?.endpoints).toMatchObject({ source: { elementTypes: ["project"], anchors: [] }, target: { elementTypes: ["project"], anchors: "edge" }, allowSelf: false });
    expect(packageReference?.endpoints).toMatchObject({ source: { elementTypes: ["project"], anchors: [] }, target: { elementTypes: ["package"], anchors: "edge" }, allowSelf: false });
    expect(DOTNET_DEPENDENCY_DEFINITION.layout).toEqual(HAND_WRITTEN.layout);
    expect(DOTNET_DEPENDENCY_DEFINITION.dragging).toBe(HAND_WRITTEN.dragging);
  });

  it("is what the canvas draws: the module's definition is compiled from the bundled specification", () => {
    // The canary for the tests above: were the canvas still holding its own definition, they
    // would compare the oracle with a copy of itself.
    expect(DOTNET_DEPENDENCY_DEFINITION).toEqual(assertValidDiagramDefinition(compileNotation(parseDisl(disText), DOTNET_BINDINGS)));
  });
});

/** Where each theme token of the specification is painted: a custom property of the module's stylesheet or of the host's. */
const TOKEN_PROPERTIES: Readonly<Record<string, { sheet: "module" | "host"; property: string }>> = {
  "color.project.fill": { sheet: "module", property: "--dependency-project-fill" },
  "color.project.stroke": { sheet: "module", property: "--dependency-project-stroke" },
  "color.package.fill": { sheet: "module", property: "--dependency-package-fill" },
  "color.package.stroke": { sheet: "module", property: "--dependency-package-stroke" },
  "color.node.label": { sheet: "module", property: "--dependency-node-label" },
  "color.node.sublabel": { sheet: "module", property: "--dependency-node-sublabel" },
  "color.edge.project": { sheet: "host", property: "--color-text-muted" },
  "color.edge.package": { sheet: "host", property: "--color-border" },
  "color.warning": { sheet: "host", property: "--color-warning" },
};

/** The custom properties a CSS block declares, by name. */
function customPropertiesIn(block: string): Record<string, string> {
  return Object.fromEntries([...block.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)].map((match) => [match[1]!, match[2]!.trim().toLowerCase()]));
}

function stylesheet(...path: string[]): string {
  return readFileSync(join(__dirname, ...path), "utf8").replace(/\/\*[\s\S]*?\*\//g, "");
}

/** The light and dark custom properties of the module's stylesheet (`.dotnet-dependency-canvas`) and the host's (`:root`). */
function themes(): Record<"module" | "host", { light: Record<string, string>; dark: Record<string, string> }> {
  const module = stylesheet("dotnet-dependency-graph.css");
  const host = stylesheet("..", "..", "..", "client", "src", "index.css");
  const moduleLight = /^\.dotnet-dependency-canvas\s*\{([^}]*)\}/m.exec(module);
  const moduleDark = /@media \(prefers-color-scheme: dark\)\s*\{\s*\.dotnet-dependency-canvas\s*\{([^}]*)\}/.exec(module);
  const hostLight = /^:root\s*\{([^}]*)\}/m.exec(host);
  const hostDark = /@media \(prefers-color-scheme: dark\)\s*\{\s*:root\s*\{([^}]*)\}/.exec(host);
  if (moduleLight === null || moduleDark === null || hostLight === null || hostDark === null) {
    throw new Error("A stylesheet no longer has the light block and the dark one this test reads.");
  }

  return {
    module: { light: customPropertiesIn(moduleLight[1]!), dark: customPropertiesIn(moduleDark[1]!) },
    host: { light: customPropertiesIn(hostLight[1]!), dark: customPropertiesIn(hostDark[1]!) },
  };
}

describe("the .NET dependency graph's theme tokens", () => {
  const theme = parseDisl(disText).notation.theme!;
  const css = themes();

  it("are each painted by a custom property, but for the font", () => {
    expect(Object.keys(theme.tokens).filter((token) => token !== "font.body").sort()).toEqual(Object.keys(TOKEN_PROPERTIES).sort());
  });

  it("equal the custom properties in the light theme", () => {
    const light = { ...theme.tokens, ...theme.modes?.light };
    for (const [token, { sheet, property }] of Object.entries(TOKEN_PROPERTIES)) {
      expect(css[sheet].light[property], `${token} (${property})`).toBe(light[token]!.toLowerCase());
    }
  });

  it("equal the custom properties in the dark theme", () => {
    const dark = { ...theme.tokens, ...theme.modes?.dark };
    for (const [token, { sheet, property }] of Object.entries(TOKEN_PROPERTIES)) {
      expect(css[sheet].dark[property], `${token} (${property})`).toBe(dark[token]!.toLowerCase());
    }
  });

  it("name the font the canvas inherits from the page", () => {
    const body = /^body\s*\{[^}]*font-family:\s*([^;]+);/m.exec(stylesheet("..", "..", "..", "client", "src", "index.css"));

    expect(body?.[1]?.trim()).toBe(theme.tokens["font.body"]);
  });
});
