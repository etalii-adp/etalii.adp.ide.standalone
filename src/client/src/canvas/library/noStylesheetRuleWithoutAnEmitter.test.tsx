import { readdirSync, readFileSync, statSync } from "node:fs";
import { basename, dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { beforeAll, describe, expect, it, vi } from "vitest";
import { act, cleanup, render } from "@testing-library/react";
import { fromBinary } from "@bufbuild/protobuf";
import { DeltaSchema, type Delta } from "@client/generated/deltas_pb";
import { CanvasFrame } from "@client/canvas/library/surface/CanvasFrame";
import { fakeContextConnection } from "@client/canvas/library/testing/canvasHarness";
import { sourceFiles } from "@client/sourceFiles";

/**
 * NO STYLESHEET RULE WITHOUT SOMETHING THAT DRAWS IT, AND NOTHING DRAWN WITHOUT A RULE OR A REASON
 * (client-centralization Requirement 11).
 *
 * <b>Mounted, on the shipped examples, because reading cannot answer it.</b> A class is often
 * composed at runtime from a declared template - `sparql-region-{payload.kind}`,
 * `causal-loop-{payload.polarityWord}` - so a text search calls it dead, and the hand-written models
 * the canvas tests build never compose it either. So a backend test
 * (`ShippedExampleModelsTests`) opens every shipped example in the real host and writes the stream
 * each canvas receives to `src/fixtures/cross-tier/example-models/`; this file plays each one into
 * the registered canvas for its type, inside the library's frame, and collects every class in the
 * document. The two template classes above are held as a positive control below: the day the
 * payloads stop reaching the canvas, they read as dead and this fails.
 *
 * <b>Both directions, over the same two lists.</b> A class a canvas stylesheet names that no example
 * draws is dead unless it is listed with why (11.1). A class an example draws that no client
 * stylesheet names anywhere is listed with why (11.4) - the direction `library-canvas-surface`
 * asked for, drawn with no rule, which left the svg `display: inline` and spilled a scrollbar.
 *
 * <b>What the first run removed</b> (11.3), each a rule for a class no source draws at all:
 * `.canvas-anchor`, `.canvas-anchor-hit`, `.canvas-pending-connection`, `.ansible-canvas` (its
 * palette moved to the host that is drawn), `.causal-loop-canvas`, `.c4-relationship-label`,
 * `.rdf-edge-label`, `.timeline-adorner`, `.dependency-graph-adorner`, `.mindmap-canvas-surface`
 * and `.wardley-axis-label`. `timeline`'s `.timeline-selected` and `.timeline-connect-target`, the
 * two the requirement names, had already gone with the centralised highlight (`c5d7e935`).
 *
 * <b>Its limits.</b> It sees a class, never a selector: `.a .b` counts `a` and `b` as ruled whether
 * or not any `.b` ever sits inside an `.a`. And it sees the examples at rest - nothing selected,
 * dragged or edited - which is why most of the first list is state.
 */

interface Listed {
  readonly reason: string;
  readonly classes: readonly string[];
}

/** Rules for classes the shipped examples do not draw at rest - live, and why none shows. */
const RULED_BUT_NOT_DRAWN_AT_REST: readonly Listed[] = [
  {
    reason: "drawn only while a gesture is in progress - a drag, a connect, or an in-place label edit - and a mounted example is at rest",
    classes: [
      "c4-node-dragging",
      "mindmap-node-dragging",
      "wardley-dragging",
      "mindmap-edge-preview",
      "mindmap-node-drop-target-ring",
      "library-connect-forbidden",
      "library-connect-preview",
      "library-connect-preview-invalid",
      "inline-label-editor",
      "inline-label-editor-body",
      "inline-label-editor-error",
      "inline-label-editor-field",
      "library-attachment-highlight",
    ],
  },
  {
    reason: "drawn only on a selected element or connection, and nothing is selected here",
    classes: ["library-resize-handle", "library-adjust-handle", "library-end-handle", "library-span-anchor", "library-span-anchor-hit"],
  },
  {
    reason: "the library frame's refusal and status lines, shown only while a canvas opens, is refused or is unavailable - every example opens",
    classes: ["canvas-rejection", "canvas-status"],
  },
  {
    reason: "a module's own message for a diagram with nothing to draw, and every shipped example has content",
    classes: ["ansible-canvas-message", "canvas-host-message", "causal-loop-message", "dotnet-dependency-canvas-message", "helm-canvas-message"],
  },
  {
    reason: "shown only after the user changes the view: a hidden or re-shown ambient package, a stage the user has shut (stages open expanded)",
    classes: ["dotnet-dependency-canvas-filtered", "pipeline-stage-count"],
  },
  {
    reason: "a banner for a view the sanity bound cut short, and no shipped example is that large",
    classes: ["owl-truncation-banner", "sparql-truncation-banner"],
  },
  {
    reason: "a supply chain's trace marking and its +/- steppers, drawn only while a node or flow is selected, and an exported example has no selection",
    classes: [
      "supply-chain-trace-selected",
      "supply-chain-trace-upstream",
      "supply-chain-trace-downstream",
      "supply-chain-trace-related",
      "supply-chain-trace-dimmed",
      "supply-chain-stepper",
      "supply-chain-stepper-button",
      "supply-chain-stepper-sign",
      "supply-chain-stepper-disabled",
    ],
  },
  {
    reason: "the simulated run's states and banner, drawn only after the user starts a simulation",
    classes: ["databricks-simulation-banner", "databricks-sim-pending", "databricks-sim-running", "databricks-sim-succeeded", "databricks-sim-failed", "databricks-sim-skipped"],
  },
  {
    reason: "composed from a payload value no shipped example has: a pipeline problem's severity, a light or heavy link, a chart's archive or CRDs, a SPARQL anonymous node, subquery, GRAPH, MINUS or SERVICE, an OWL equivalence",
    classes: [
      "pipeline-problem-error",
      "pipeline-problem-warning",
      "pipeline-problem-mark",
      "pipeline-problem-mark-error",
      "pipeline-problem-mark-warning",
      "causal-loop-weight-light",
      "causal-loop-weight-heavy",
      "helm-node-archive",
      "helm-node-crds",
      "sparql-node-anonymous",
      "sparql-node-subquery",
      "sparql-region-graph",
      "sparql-region-minus",
      "sparql-region-service",
      "owl-edge-equivalent",
      "owl-shape-inner",
    ],
  },
  {
    reason: "set by a payload flag no shipped example raises: an unresolved or unreadable target, a hollow node, a broken or implicit edge, a blank or deactivated shape, a SPARQL-constrained row, a malformed axiom, an alternate or fallback label, a language chip, an ordered collection, a mapping",
    classes: [
      "ansible-edge-unresolved",
      "ansible-edge-label",
      "ansible-node-hollow",
      "databricks-node-missing",
      "helm-node-unreadable",
      "pipeline-edge-broken",
      "pipeline-edge-implicit",
      "rdf-node-blank",
      "shacl-row-sparql",
      "shacl-shape-blank",
      "shacl-shape-deactivated",
      "owl-malformed",
      "skos-concept-blank",
      "skos-label-alternate",
      "skos-label-fallback",
      "skos-language-chip",
      "skos-region-kind",
      "skos-edge-mapping",
    ],
  },
];

/** Classes the examples draw that no client stylesheet names - and why none needs to. */
const DRAWN_BUT_UNRULED: readonly Listed[] = [
  {
    reason: "a module's name for its host, surface, viewport or scrollbar container; the layout comes from the shared class beside it (canvas-host, library-canvas, canvas-scrollbar), which is ruled, and the name is what the module's tests find it by",
    classes: [
      "ansible-canvas-viewport",
      "ansible-scrollbars",
      "c4-scrollbars",
      "databricks-canvas",
      "databricks-surface",
      "dependency-graph-canvas",
      "dependency-graph-surface",
      "dotnet-dependency-scrollbars",
      "fdg-canvas",
      "fdg-surface",
      "helm-scrollbars",
      "mindmap-canvas-host",
      "owl-canvas",
      "owl-scrollbars",
      "owl-surface",
      "pipeline-scrollbars",
      "rdf-canvas",
      "rdf-surface",
      "shacl-canvas",
      "shacl-scrollbars",
      "shacl-surface",
      "skos-canvas",
      "skos-scrollbars",
      "skos-surface",
      "supply-chain-surface",
      "sparql-canvas",
      "sparql-scrollbars",
      "sparql-surface",
      "timeline-canvas",
      "timeline-surface",
      "ghg-canvas",
      "ghg-surface",
    ],
  },
  {
    reason: "names what kind of element or connection this is; its paint comes from the ruled classes beside it (canvas-connection, canvas-node, the module's box and line classes) or from the definition's inline style, and the kind name is what tests, data hooks and the selection guards read",
    classes: [
      "ansible-edge",
      "ansible-edge-imports-playbook",
      "ansible-edge-includes-tasks",
      "ansible-edge-uses-role",
      "c4-boundary",
      "causal-loop-delay",
      "causal-loop-link",
      "causal-loop-loop",
      "databricks-edge",
      "databricks-edge-depends",
      "databricks-edge-flow",
      "databricks-frame",
      "databricks-node",
      "databricks-node-bundle",
      "databricks-node-compute",
      "databricks-node-notebook",
      "databricks-node-notifications",
      "databricks-node-pipeline",
      "databricks-node-pipelines",
      "databricks-node-python",
      "databricks-node-source",
      "databricks-node-sql",
      "databricks-node-target",
      "databricks-override",
      "dependency-graph-element",
      "dependency-graph-node",
      "dotnet-dependency-edge",
      "dotnet-dependency-edge-project",
      "dotnet-dependency-element",
      "fdg-element",
      "fdg-owns-action",
      "fdg-owns-data",
      "fdg-owns-function",
      "fdg-relation",
      "fdg-shows",
      "fdg-ui-child",
      "helm-edge",
      "owl-class",
      "owl-datatype",
      "owl-edge",
      "owl-edge-datatype-property",
      "owl-edge-object-property",
      "owl-individual",
      "owl-node",
      "owl-ontology",
      "pipeline-job",
      "pipeline-stage",
      "pipeline-stage-expanded",
      "pipeline-step",
      "rdf-node",
      "shacl-edge",
      "shacl-edge-node",
      "shacl-edge-xone",
      "shacl-shape",
      "skos-concept",
      "skos-edge",
      "skos-edge-hierarchy",
      "skos-region",
      "sparql-annotation-bind",
      "sparql-annotation-filter",
      "sparql-edge",
      "sparql-node",
      "sparql-node-iri",
      "sparql-node-projected",
      "sparql-region",
      "sparql-region-branch",
      "sparql-region-template",
      "sparql-region-union",
      "timeline-connection",
      "timeline-period",
      "wardley-annotation",
      "wardley-kind-component",
      "wardley-kind-submap",
      "ghg-influence",
      "ghg-trigger",
      "ghg-note",
    ],
  },
  {
    reason: "the unmarked member of a ruled family - no play, a normal-weight link - drawn by the family's base rule, with nothing to add",
    classes: ["ansible-play-none", "causal-loop-weight-normal"],
  },
  {
    reason: "a label, line or hit path beside the ruled shared class that paints it (library-element-label, canvas-node-label, canvas-connection-line, canvas-connection-hit)",
    classes: [
      "databricks-label",
      "dependency-graph-label",
      "fdg-label",
      "rdf-label",
      "shacl-row",
      "skos-region-label",
      "sparql-label",
      "timeline-label",
      "dependency-graph-relation-hit",
      "owl-edge-hit",
      "shacl-edge-hit",
      "skos-edge-hit",
      "sparql-edge-hit",
      "timeline-connection-hit",
      "shacl-edge-line",
      "timeline-connection-line",
      "ghg-label",
    ],
  },
  {
    reason: "the library's own structural name, whose paint is the definition's inline style; noUnstyledLibraryClasses.test.ts keeps the library-* inventory and its reasons, and this does not copy them",
    classes: [
      "library-canvas-background",
      "library-connection",
      "library-connection-line",
      "library-decoration",
      "library-decoration-text",
      "library-element",
      "library-frame",
      "library-shape",
      "library-span",
      "library-span-label",
      "canvas-boundary",
    ],
  },
  {
    reason: "a per-item name the library composes for a background band, attitude, accelerator or note; its paint comes from the ruled class on it or on its group (wardley-band, wardley-attitude, wardley-accelerator-forward and -back, wardley-note) or from the definition's typography",
    classes: [
      "wardley-accelerator-back-glyph",
      "wardley-accelerator-back-label",
      "wardley-accelerator-forward-glyph",
      "wardley-accelerator-forward-label",
      "wardley-attitude-pioneers",
      "wardley-attitude-pioneers-label",
      "wardley-attitude-settlers",
      "wardley-attitude-settlers-label",
      "wardley-attitude-townplanners",
      "wardley-attitude-townplanners-label",
      "wardley-band-0",
      "wardley-band-0-label",
      "wardley-band-1-edge",
      "wardley-band-1-label",
      "wardley-band-2-edge",
      "wardley-band-2-label",
      "wardley-band-3-edge",
      "wardley-band-3-label",
      "wardley-chrome",
      "wardley-note-label",
    ],
  },
  {
    reason: "the mind map's corner glyphs, left as they render by the user's ruling (2026-09-26): the muted, smaller rule written for them never applied, and was removed rather than made to",
    classes: ["mindmap-node-indicators"],
  },
];

// ---- a scripted transport: the diagram stream plays one exported example, then parks ----

let openStream: () => AsyncIterable<Delta> = () => parked;
const parked: AsyncIterable<never> = { [Symbol.asyncIterator]: () => ({ next: () => new Promise<never>(() => {}) }) };

function playing(deltas: Delta[]): AsyncIterable<Delta> {
  return {
    [Symbol.asyncIterator]: () => {
      let index = 0;
      return {
        next: () => (index < deltas.length ? Promise.resolve({ value: deltas[index++], done: false }) : new Promise<never>(() => {})),
      };
    },
  };
}

vi.mock("@connectrpc/connect", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@connectrpc/connect")>();
  return {
    ...actual,
    createClient: () =>
      new Proxy(
        {},
        {
          get: (_target, property) => {
            if (property === "watch") {
              return () => parked;
            }
            return () => Promise.resolve({ accepted: true, error: "", items: [] });
          },
        },
      ),
  };
});

vi.mock("@client/auth/AuthContext", () => {
  const transport = {};
  return { useAuth: () => ({ transport }) };
});

const watchId = new Uint8Array(16);
// The deltas ride the tab's one stream: a canvas is handed `openStream()` as its diagram stream. One
// object, as the provider's is memoised: the hook keys its effect on it, so a fresh one per render
// would re-open the stream on every render.
const workspaceStreams = { openDiagramStream: () => openStream(), watchHierarchy: () => parked };
vi.mock("@client/shell/context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@client/shell/context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextConnection: () => connection,
    useWorkspaceStreams: () => workspaceStreams,
    useContextPrompt: () => ({ prompt: null, onPropose: vi.fn(), onSubmit: vi.fn(), onCancel: vi.fn() }),
    useContextSelection: () => ({ selection: null, levels: [], actions: [] }),
    useContextProblems: () => null,
    useProjectActions: () => [],
    useContextNotices: () => ({ notices: [], dismiss: () => {} }),
  };
});

const connection = fakeContextConnection({ watchId });

vi.mock("@client/shell/panels/useToolboxItems", () => ({ useToolboxItems: () => [] }));

const { panelFor, toolPanels } = await import("@client/shell/panels/toolPanels");

// ---- the inputs ----

function sourceRoot(): string {
  let directory = dirname(fileURLToPath(import.meta.url));
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

const root = sourceRoot();
const exportsFolder = join(root, "fixtures", "cross-tier", "example-models");

interface ExportedDiagram {
  readonly file: string;
  readonly example: string;
  readonly mimeType: string;
  readonly deltas: readonly string[];
}

/** Every diagram the backend exported, named by its file and example. */
const exported: readonly ExportedDiagram[] = readdirSync(exportsFolder)
  .filter((file) => file.endsWith(".json"))
  .sort()
  .flatMap((file) => {
    const { diagrams } = JSON.parse(readFileSync(join(exportsFolder, file), "utf8")) as { diagrams: Omit<ExportedDiagram, "file">[] };
    return diagrams.map((diagram) => ({ ...diagram, file: basename(file, ".json") }));
  });

/** The class names a stylesheet's selectors name - comments, declaration blocks and attribute values removed. */
function classesStyledBy(css: string): string[] {
  const selectors = css
    .replace(/\/\*[\s\S]*?\*\//g, " ")
    .replace(/\{[^{}]*\}/g, "{}")
    .replace(/\[[^\]]*\]/g, "[]")
    .replace(/@[a-z-]+[^{;]*[{;]/g, " ");
  return [...selectors.matchAll(/\.(-?[_a-zA-Z][\w-]*)/g)].map((match) => match[1]!);
}

/** The stylesheets that draw on a canvas: every diagram module client's, and the canvas library's own. */
function canvasStylesheets(): string[] {
  const modules = readdirSync(join(root, "diagrams"), { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => join(root, "diagrams", entry.name, "client"))
    .filter((client) => statSync(client, { throwIfNoEntry: false })?.isDirectory() === true)
    .flatMap((client) => sourceFiles(client).filter((path) => path.endsWith(".css")));
  return [...modules, ...sourceFiles(join(root, "client", "src", "canvas")).filter((path) => path.endsWith(".css"))];
}

/** Every stylesheet the client ships, shell included: a class drawn on a canvas may be ruled from anywhere. */
function clientStylesheets(): string[] {
  return [...new Set([...canvasStylesheets(), ...sourceFiles(join(root, "client", "src")).filter((path) => path.endsWith(".css"))])];
}

const settle = () =>
  act(async () => {
    for (let tick = 0; tick < 5; tick++) {
      await new Promise((resolve) => setTimeout(resolve, 0));
    }
  });

const props = { projectId: new Uint8Array([1]), entryId: new Uint8Array([2]), path: ["guard", "example.adp"] };

/** Every class token in the document while the diagram's canvas shows it. */
async function classesDrawnFor(diagram: ExportedDiagram): Promise<string[]> {
  const Canvas = panelFor(diagram.mimeType)?.Panel;
  if (Canvas === undefined) {
    throw new Error(`${diagram.file}: ${diagram.example} is ${diagram.mimeType}, which no canvas is registered for`);
  }
  const deltas = diagram.deltas.map((delta) => fromBinary(DeltaSchema, Uint8Array.from(atob(delta), (character) => character.charCodeAt(0))));
  openStream = () => playing(deltas);
  render(
    <CanvasFrame>
      <Canvas {...props} />
    </CanvasFrame>,
  );
  await settle();
  const classes = [...document.body.querySelectorAll("*")].flatMap((element) => [...element.classList]);
  cleanup();
  openStream = () => parked;
  return classes;
}

function listed(lists: readonly Listed[]): Set<string> {
  return new Set(lists.flatMap((entry) => entry.classes));
}

const where = (path: string) => relative(root, path).replaceAll("\\", "/");

// ---- the walk ----

const drawn = new Set<string>();
const ruledOnACanvas = new Map<string, Set<string>>();
let ruledAnywhere = new Set<string>();

describe("no stylesheet rule without something that draws it, and nothing drawn without a rule or a reason", () => {
  beforeAll(async () => {
    for (const diagram of exported) {
      (await classesDrawnFor(diagram)).forEach((token) => drawn.add(token));
    }
    for (const sheet of canvasStylesheets()) {
      for (const name of classesStyledBy(readFileSync(sheet, "utf8"))) {
        ruledOnACanvas.set(name, (ruledOnACanvas.get(name) ?? new Set()).add(where(sheet)));
      }
    }
    ruledAnywhere = new Set(clientStylesheets().flatMap((sheet) => classesStyledBy(readFileSync(sheet, "utf8"))));
  }, 600_000);

  it("reads the class names a selector names, and nothing else", () => {
    // The positive control on the reader: without it, an empty ruled set would make every drawn
    // class read as unruled and every rule read as fine.
    expect(
      classesStyledBy(`/* .commented */ .a .b:hover, .c > .d::after, .e[data-kind=".f"] { fill: url(#g.h); }
        @media (prefers-color-scheme: dark) { .i.j { stroke: none; } }`),
    ).toEqual(["a", "b", "c", "d", "e", "i", "j"]);
  });

  it("mounts every registered diagram canvas on at least one shipped example", () => {
    // The completeness canary: a registration the exports never reach would make both lists
    // below true of nothing. Editors have no shipped examples and are out of scope.
    const diagramRegistrations = toolPanels.filter(
      (registration) => registration.Panel !== undefined && !registration.matches("editor/markdown") && !registration.matches("editor/plain"),
    );
    expect(diagramRegistrations.length, "fewer diagram canvases are registered than when this was written").toBeGreaterThanOrEqual(19);
    expect(
      diagramRegistrations.filter((registration) => !exported.some((diagram) => registration.matches(diagram.mimeType))).map((registration) => registration.Panel!.name),
      "these canvases are mounted on no exported example",
    ).toEqual([]);
    expect(exported.length).toBeGreaterThanOrEqual(90);
  });

  it("sees a class composed from a real payload - what the exported examples are for", () => {
    // The positive control on the export: both are composed from a declared template and a
    // payload value, so neither is in any source as written.
    expect(drawn).toContain("sparql-region-union");
    expect(drawn).toContain("causal-loop-undecidable");
  });

  it("finds every canvas stylesheet's classes drawn, or listed with why", () => {
    // Act.
    const excused = listed(RULED_BUT_NOT_DRAWN_AT_REST);
    const dead = [...ruledOnACanvas]
      .filter(([name]) => !drawn.has(name) && !excused.has(name))
      .map(([name, sheets]) => `.${name} (${[...sheets].join(", ")})`)
      .sort();

    // Assert: remove the rule, or list the class with the state or payload that draws it.
    expect(dead, "no shipped example draws these, and they are not listed").toEqual([]);
  });

  it("finds every drawn class ruled, or listed with why", () => {
    // Act.
    const excused = listed(DRAWN_BUT_UNRULED);
    const unruled = [...drawn].filter((name) => !ruledAnywhere.has(name) && !excused.has(name)).sort();

    // Assert: give it a rule, or list it with what paints it instead.
    expect(unruled, "a canvas draws these and no client stylesheet names them").toEqual([]);
  });

  it("keeps both lists true: nothing listed that the walk no longer needs excused", () => {
    // Act.
    const notDrawn = listed(RULED_BUT_NOT_DRAWN_AT_REST);
    const unruled = listed(DRAWN_BUT_UNRULED);
    const stale = [
      ...[...notDrawn].filter((name) => drawn.has(name)).map((name) => `${name}: listed as not drawn, and an example draws it`),
      ...[...notDrawn].filter((name) => !ruledOnACanvas.has(name)).map((name) => `${name}: listed as not drawn, and no canvas stylesheet rules it`),
      ...[...unruled].filter((name) => ruledAnywhere.has(name)).map((name) => `${name}: listed as unruled, and a stylesheet rules it`),
      ...[...unruled].filter((name) => !drawn.has(name)).map((name) => `${name}: listed as unruled, and no example draws it`),
    ].sort();
    const every = [...RULED_BUT_NOT_DRAWN_AT_REST, ...DRAWN_BUT_UNRULED].flatMap((entry) => entry.classes);

    // Assert.
    expect(stale, "delete these lines from the list").toEqual([]);
    expect(every.length, "a class is listed twice").toBe(new Set(every).size);
  });
});
