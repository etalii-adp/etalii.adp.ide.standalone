import { describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { create, toBinary } from "@bufbuild/protobuf";
import { ElementSchema } from "@client/generated/elements_pb";
import { HelmEdgeKind, HelmElementKind, HelmElementPayloadSchema } from "@client/generated/helm-charts_pb";
import disText from "../definition/helm-chart.dis?raw";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { fakeContextConnection } from "@client/canvas/library/testing/canvasHarness";
import { applyDelta, emptyModel, type HelmModel } from "./helmModel";
import { HELM_BINDINGS } from "./helmBindings";
import { HAND_WRITTEN_DEFINITION, handWrittenModel } from "./handWrittenDefinition";

let currentModel: HelmModel = emptyModel;

vi.mock("./useHelmStream", () => ({
  useHelmStream: () => ({ model: currentModel, loading: false, failed: false, moveElementTo: vi.fn(), reportView: vi.fn() }),
}));

vi.mock("@client/shell/context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@client/shell/context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextPrompt: () => ({ prompt: null, onPropose: vi.fn(), onSubmit: vi.fn(), onCancel: vi.fn() }),
    useContextConnection: () => connection,
    useContextSelection: () => ({ selection: null }),
  };
});

const emptyPalette: never[] = [];

vi.mock("@client/shell/panels/useToolboxItems", () => ({
  useToolboxItems: () => emptyPalette,
}));

const connection = fakeContextConnection({ select: vi.fn(), revealPath: vi.fn() });

const { DiagramCanvas } = await import("@client/canvas/library/DiagramCanvas");
const { HelmCanvas, HELM_DEFINITION } = await import("./HelmCanvas");

/**
 * The helm chart diagram's canvas definition, compiled from its bundled DISL specification, draws
 * what the definition the module stated by hand until the switch-over drew.
 *
 * <b>The oracle is that hand-written definition, kept in `handWrittenDefinition.ts`</b> with the model
 * the canvas built for it. The compiled definition has a node type per kind where the oracle had one
 * `node` type told apart by a payload class, a relation type per relation where it had one `edge` told
 * apart by a class on each connection, and an open end drawn by the library as its relation's stub
 * where it was a `stub` element the canvas built. So the test is the drawing: the real canvas renders
 * the corpus below, the oracle renders the same corpus through the model the canvas used to build,
 * and the markup must be the same, element for element and connection for connection.
 *
 * <b>What is compared apart, and the four differences it states.</b>
 * - **An open end** is a connection now and was an element, so its markup cannot be the same: each
 *   is compared by what it draws instead - its line's two ends, its classes, its text, where the text
 *   stands and how it is anchored.
 * - **`data-kind`** was written on every node and is read by no stylesheet and no test.
 * - **A partial's accessible name** is DISL's default (6.15), its type's label "Partial", where the
 *   oracle said "Template"; its tooltip still says "Template", as the specification's does.
 * - **The arrowhead.** The specification's `targetMarker: "arrow"` is DISL's open V (Appendix B.3),
 *   where the oracle drew the library's solid triangle. That is a defect of the bundled definition,
 *   recorded rather than tuned away: the module draws what the specification says.
 */

const TYPE_URL = "type.googleapis.com/etalii.adp.helm.HelmElementPayload";

function node(id: string, type: string, kind: HelmElementKind, name: string, x: number, y: number, width: number, height: number, unreadable = false) {
  const payload = create(HelmElementPayloadSchema, { name, kind, width, height, unreadable, chartRelativePath: [name] });
  return create(ElementSchema, {
    id: { value: id },
    position: { x, y },
    type: `helm/chart+${type}`,
    payload: { typeUrl: TYPE_URL, value: toBinary(HelmElementPayloadSchema, payload) },
  });
}

function edge(id: string, kind: HelmEdgeKind, sourceId: string, targetId: string, label: string) {
  const payload = create(HelmElementPayloadSchema, {
    name: id,
    kind: HelmElementKind.EDGE,
    edge: { sourceId, targetId, kind, label, openEnd: targetId === "" },
  });
  return create(ElementSchema, {
    id: { value: id },
    position: { x: 0, y: 0 },
    type: "helm/chart+edge",
    payload: { typeUrl: TYPE_URL, value: toBinary(HelmElementPayloadSchema, payload) },
  });
}

/**
 * Every case the drawing distinguishes, in the five bands at the specification's sizes: each of the
 * ten node types, two unreadable files, every relation with and without a label, and both open ends.
 */
const CORPUS: HelmModel = applyDelta(emptyModel, {
  action: {
    case: "add",
    value: {
      elements: [
        node("chart", "chart", HelmElementKind.CHART, "nginx", 0, 0, 220, 110),
        node("lock:Chart.lock", "lock", HelmElementKind.LOCK, "Chart.lock", 0, 140, 200, 56, true),
        node("schema:values.schema.json", "schema", HelmElementKind.SCHEMA, "values.schema.json", 0, 220, 200, 48),
        node("crds", "crds", HelmElementKind.CRDS, "crds", 0, 290, 200, 48),
        node("values:values.yaml", "values", HelmElementKind.VALUES, "values.yaml", 280, 0, 200, 72),
        node("values:values-prod.yaml", "values", HelmElementKind.VALUES, "values-prod.yaml", 280, 100, 200, 72, true),
        node("tpl:templates/deployment.yaml", "template", HelmElementKind.TEMPLATE, "deployment.yaml", 540, 0, 240, 60),
        node("tpl:templates/_helpers.tpl", "partial", HelmElementKind.TEMPLATE, "_helpers.tpl", 540, 90, 240, 60),
        node("dep:redis", "dependency", HelmElementKind.DEPENDENCY, "redis", 840, 0, 210, 96),
        node("dep:postgresql", "dependency", HelmElementKind.DEPENDENCY, "postgresql", 840, 120, 210, 96),
        node("sub:charts/redis", "subchart", HelmElementKind.SUBCHART, "redis", 1120, 0, 210, 84),
        node("tgz:charts/common-2.0.0.tgz", "archive", HelmElementKind.ARCHIVE, "common-2.0.0.tgz", 1120, 110, 210, 52),
        edge("edge:chart|Declares|dep:redis|^17.0.0", HelmEdgeKind.DECLARES, "chart", "dep:redis", "^17.0.0"),
        edge("edge:chart|Declares|dep:postgresql|", HelmEdgeKind.DECLARES, "chart", "dep:postgresql", ""),
        edge("edge:dep:redis|Resolves|sub:charts/redis|redis", HelmEdgeKind.RESOLVES, "dep:redis", "sub:charts/redis", "redis"),
        edge("edge:dep:postgresql|Resolves||postgresql", HelmEdgeKind.RESOLVES, "dep:postgresql", "", "postgresql"),
        edge("edge:values:values-prod.yaml|Overrides|values:values.yaml|", HelmEdgeKind.OVERRIDES, "values:values-prod.yaml", "values:values.yaml", ""),
        edge("edge:values:values.yaml|Configures|dep:redis|redis", HelmEdgeKind.CONFIGURES, "values:values.yaml", "dep:redis", "redis"),
        edge("edge:tpl:templates/deployment.yaml|Includes|tpl:templates/_helpers.tpl|nginx.fullname", HelmEdgeKind.INCLUDES, "tpl:templates/deployment.yaml", "tpl:templates/_helpers.tpl", "nginx.fullname"),
        edge("edge:tpl:templates/deployment.yaml|Includes||common.labels", HelmEdgeKind.INCLUDES, "tpl:templates/deployment.yaml", "", "common.labels"),
      ],
    },
  },
} as never);

/** What an open end draws, read off either drawing. */
interface DrawnStub {
  id: string;
  classes: string[];
  line: number[];
  lineClasses: string[];
  text: string;
  textAt: number[];
  anchor: string;
  textClasses: string[];
}

const words = (value: string | null | undefined, without: readonly string[] = []) =>
  (value ?? "").split(/\s+/).filter((word) => word !== "" && !without.includes(word)).sort();

/** Takes the oracle's stub elements out of its drawing, reading what each drew. */
function oracleStubs(container: HTMLElement): DrawnStub[] {
  return [...container.querySelectorAll("g.library-element.helm-edge-open")].map((group) => {
    const line = group.querySelector("line")!;
    const text = group.querySelector("text")!;
    const stub: DrawnStub = {
      id: group.getAttribute("data-element-id")!,
      classes: words(group.getAttribute("class"), ["library-element"]),
      line: ["x1", "y1", "x2", "y2"].map((name) => Number(line.getAttribute(name))),
      lineClasses: words(line.getAttribute("class")),
      text: text.textContent ?? "",
      textAt: [Number(text.getAttribute("x")), Number(text.getAttribute("y"))],
      anchor: (text as SVGTextElement).style.textAnchor,
      textClasses: words(text.getAttribute("class"), ["library-element-label"]),
    };
    group.remove();
    return stub;
  });
}

/** Takes the library's stubs out of the compiled drawing, reading what each drew. */
function compiledStubs(container: HTMLElement): DrawnStub[] {
  return [...container.querySelectorAll("g.library-connection-stub")].map((group) => {
    const line = group.querySelector("path.canvas-connection-line")!;
    const text = group.querySelector("text")!;
    const stub: DrawnStub = {
      id: group.getAttribute("data-connection-id")!,
      classes: words(group.getAttribute("class"), ["canvas-connection", "library-connection", "library-connection-stub"]),
      line: (line.getAttribute("d") ?? "").replace(/[ML]/g, " ").trim().split(/\s+/).map(Number),
      lineClasses: words(line.getAttribute("class"), ["canvas-connection-line", "library-connection-line"]),
      text: text.textContent ?? "",
      textAt: [Number(text.getAttribute("x")), Number(text.getAttribute("y"))],
      anchor: (text as SVGTextElement).style.textAnchor,
      textClasses: words(text.getAttribute("class"), ["library-connection-label"]),
    };
    group.remove();
    return stub;
  });
}

const ids = (markup: string) => markup.replace(/«r[0-9a-z]+»|:r[0-9a-z]+:/g, "«id»");

/** The oracle's drawing of the corpus: the hand-written definition, in the frame the canvas draws it in. */
function oracleDrawing(): { container: HTMLElement; unmount: () => void } {
  return render(
    <div className="helm-canvas-frame" role="application" aria-label="Helm chart anatomy">
      <DiagramCanvas
        definition={HAND_WRITTEN_DEFINITION}
        model={handWrittenModel(CORPUS)}
        events={{}}
        source={{ entryId: new Uint8Array(16), path: ["helm-chart.adp"] }}
        toolboxItems={emptyPalette}
        ariaLabel="Helm chart anatomy"
        className="helm-canvas"
        scrollbarsClassName="helm-scrollbars"
      />
    </div>,
  );
}

/** The canvas's own drawing of the corpus. */
function compiledDrawing(): { container: HTMLElement; unmount: () => void } {
  currentModel = CORPUS;
  return render(<HelmCanvas projectId={new Uint8Array(16)} entryId={new Uint8Array(16)} path={["helm-chart.adp"]} />);
}

describe("the helm chart diagram's compiled definition", () => {
  it("draws what the hand-written definition drew, element for element", () => {
    const oracle = oracleDrawing();
    const oracleOpen = oracleStubs(oracle.container);
    oracle.container.querySelectorAll("[data-kind]").forEach((element) => element.removeAttribute("data-kind"));
    const expected = ids(oracle.container.innerHTML);
    oracle.unmount();

    const compiled = compiledDrawing();
    const compiledOpen = compiledStubs(compiled.container);
    const actual = ids(compiled.container.innerHTML)
      .replace('aria-label="Partial _helpers.tpl"', 'aria-label="Template _helpers.tpl"')
      .replaceAll('marker-end="url(#library-open-arrow)"', 'marker-end="url(#library-arrow)"');
    compiled.unmount();

    // The cases the comparison would pass vacuously on: a canvas that drew nothing for either.
    for (const kind of ["chart", "values", "schema", "template", "crds", "dependency", "subchart", "archive", "lock"]) {
      expect(actual, kind).toContain(`helm-node-${kind}`);
    }
    expect(actual).toContain("helm-node-unreadable");
    for (const relation of ["declares", "resolves", "overrides", "configures", "includes"]) {
      expect(actual, relation).toContain(`helm-edge-${relation}`);
    }
    expect(oracleOpen).toHaveLength(2);
    expect(compiledOpen).toEqual(oracleOpen);
    expect(actual).toBe(expected);
  });

  it("states per type what the hand-written definition stated once", () => {
    const types = HELM_DEFINITION.elementTypes;

    expect(types.map((type) => type.id)).toEqual(["chart", "values", "schema", "template", "partial", "crds", "dependency", "subchart", "archive", "lock"]);
    expect(types.every((type) => type.deletable === false && type.sizing === "model")).toBe(true);
    expect(HELM_DEFINITION.relationTypes.map((relation) => relation.id)).toEqual(["declares", "resolves", "overrides", "configures", "includes"]);
    expect(HELM_DEFINITION.relationTypes.filter((relation) => relation.stub !== undefined).map((relation) => relation.id)).toEqual(["resolves", "includes"]);
    // A dependency has no file of its own, so its double-click reveals nothing (its `doubleClick` is `none`).
    expect(HELM_DEFINITION.actions).toEqual([
      {
        id: "openArtifact",
        invokedBy: [{ kind: "gesture", gesture: "activate" }],
        appliesTo: [{ kind: "element", elementTypes: ["chart", "values", "schema", "template", "partial", "crds", "subchart", "archive", "lock"] }],
      },
    ]);
    expect(HELM_DEFINITION.layout).toEqual(HAND_WRITTEN_DEFINITION.layout);
    expect(HELM_DEFINITION.dragging).toBe(HAND_WRITTEN_DEFINITION.dragging);
  });

  it("is what the canvas draws: the module's definition is compiled from the bundled specification", () => {
    // The canary for the tests above: were the canvas still holding its own definition, they
    // would compare the oracle with a copy of itself.
    expect(HELM_DEFINITION).toEqual(assertValidDiagramDefinition(compileNotation(parseDisl(disText), HELM_BINDINGS)));
  });
});
