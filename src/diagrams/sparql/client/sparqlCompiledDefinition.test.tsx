import { describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { create, fromJson, toBinary, type DescMessage, type JsonValue } from "@bufbuild/protobuf";
import { ElementSchema } from "@client/generated/elements_pb";
import {
  SparqlAnnotationPayloadSchema,
  SparqlEdgePayloadSchema,
  SparqlHeaderPayloadSchema,
  SparqlRegionPayloadSchema,
  SparqlTermPayloadSchema,
  SparqlTruncationPayloadSchema,
  SparqlVariablePayloadSchema,
} from "@client/generated/sparql_pb";
import type { DiagramDefinition, ShapePoint } from "@client/canvas/library/definition/diagramDefinition";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import type { DiagramModel } from "@client/canvas/library/api/diagramModel";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { fakeContextConnection } from "@client/canvas/library/testing/canvasHarness";
import disText from "../definition/sparql-query.dis?raw";
import transcriptText from "../backend/EtAlii.Adp.Diagram.Sparql.Tests/Parity/sparql.transcript.json?raw";
import { SPARQL_BINDINGS } from "./sparqlBindings";
import { applyDelta, emptyModel, type SparqlAnnotation, type SparqlModel, type SparqlNode, type SparqlRegion } from "./sparqlModel";

/**
 * Requirement 3: the SPARQL query canvas's definition is compiled from its bundled specification, and the
 * hand-written definition it replaced is kept here as the oracle, moved unchanged with the model mapping
 * and the header and banner it drew around the library. Both are drawn by the real `DiagramCanvas` over
 * every document of the backend's parity transcript - the corpus the backend's own reading is held to,
 * with every form, every group construct, property paths, subqueries and a query over the bound - and
 * their markup must be the same.
 *
 * The specification is compiled as it is bundled: nothing corrects it on the way in. What it draws
 * differently on purpose, because it says so, is taken into the oracle below rather than tolerated:
 *  - a region is a rectangle on the background layer (DISL 6.16), drawn beneath the connections, where the
 *    oracle drew the library's frame over them: the library's box, its corners rounded 4 rather than 6;
 *  - a region whose label is empty draws no label (its `visible` is `self.label != ''`), where the oracle
 *    drew an empty one;
 *  - the header's modifiers are one row, joined by " · ", where the oracle drew a span per modifier: a
 *    header row is a label, and DISL has no row repeated over a list;
 *  - the banner is the budget's notice, a status region that says where and how severely it shows;
 *  - an annotation can be dragged, so the move reaches the backend and comes back refused with the
 *    specification's sentence (its `refusals.move`), where the oracle let nothing start the drag. That
 *    draws nothing differently, so it is pinned by its own test below.
 */
let currentModel: SparqlModel = emptyModel;

vi.mock("./useSparqlStream", () => ({
  useSparqlStream: () => ({ model: currentModel, loading: false, failed: false, moveElementTo: vi.fn(), reportView: vi.fn() }),
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

const connection = fakeContextConnection({ select: vi.fn(), revealPath: vi.fn() });

// The backend's palette for a query, which is empty.
const emptyPalette: never[] = [];

vi.mock("@client/shell/panels/useToolboxItems", () => ({
  useToolboxItems: () => emptyPalette,
}));

const { DiagramCanvas } = await import("@client/canvas/library/DiagramCanvas");
const { SparqlCanvas, SPARQL_DEFINITION } = await import("./SparqlCanvas");

const PROJECT = new Uint8Array(16);
const ENTRY = new Uint8Array(16);

const NODE_WIDTH = 170;
const NODE_HEIGHT = 64;
const ANNOTATION_WIDTH = 120;
const ANNOTATION_HEIGHT = 14;

/** The hand-written definition the compiled one replaced, unchanged. */
const HAND_WRITTEN: DiagramDefinition = assertValidDiagramDefinition({
  elementTypes: [
    {
      id: "region",
      shape: "frame",
      classNames: [
        { className: "sparql-region canvas-element", on: "element" },
        { className: "sparql-region-outline" },
        { className: { template: "sparql-region-{payload.kind}" }, on: "element" },
      ],
      labels: [{ text: { path: "payload.label" }, placement: "above", className: "sparql-region-label canvas-hint" }],
      anchors: { kind: "edge" },
      sizing: "model",
    },
    {
      id: "node",
      shape: "box",
      classNames: [
        { className: "sparql-node canvas-element", on: "element" },
        { className: { template: "sparql-node-{payload.kind}" }, on: "element" },
        { className: "sparql-node-projected", on: "element", when: { path: "payload.projected", is: "true" } },
        { className: "sparql-node-box canvas-node", on: "shape" },
      ],
      labels: [
        { text: { path: "payload.display" }, anchorTo: "top", offset: { x: 0, y: NODE_HEIGHT / 2 + 2 }, truncate: true, className: "sparql-label canvas-node-label" },
        {
          text: { path: "payload.annotation" },
          when: { path: "payload.annotation", is: "non-empty" },
          anchorTo: "top",
          offset: { x: 0, y: NODE_HEIGHT - 10 },
          align: "start",
          insetX: 8,
          className: "sparql-node-annotation",
        },
      ],
      decorations: [
        {
          glyph: "marker",
          anchor: "canvas",
          from: { x: { path: "bounds.left", number: { plus: 8 } }, y: { path: "bounds.top", number: { plus: 16 } } },
          text: { template: "→" },
          className: "sparql-projection-mark",
          when: { path: "payload.projected", is: "true" },
        },
      ],
      anchors: { kind: "edge" },
      sizing: "model",
    },
    {
      id: "annotation",
      shape: "none",
      classNames: [
        { className: "sparql-annotation canvas-hint", on: "element" },
        { className: { template: "sparql-annotation-{payload.kind}" }, on: "element" },
      ],
      labels: [{ text: { path: "payload.text" } }],
      anchors: { kind: "edge" },
      sizing: "model",
      draggable: false,
    },
  ],
  relationTypes: [
    {
      id: "pattern",
      route: "straight",
      style: { endMarker: "arrow" },
      label: { placement: "midpoint", offset: -6 },
      className: "sparql-edge",
      lineClassName: "sparql-edge-line",
      hitClassName: "sparql-edge-hit",
      endpoints: { source: { elementTypes: ["node"], anchors: [] }, target: { elementTypes: ["node"], anchors: "edge" }, allowSelf: false },
    },
  ],
  layout: { modes: ["manual"] },
  dragging: "enabled",
});

/** The oracle with the specification's decisions taken in: a region on the background layer, labelled only when it has a label. */
const ORACLE: DiagramDefinition = assertValidDiagramDefinition({
  ...HAND_WRITTEN,
  elementTypes: HAND_WRITTEN.elementTypes.map((type) =>
    type.id === "region"
      ? { ...type, shape: "box", beneathConnections: true, labels: type.labels?.map((label) => ({ ...label, when: { path: "payload.label", is: "non-empty" } as const })) }
      : type.id === "annotation"
        ? { ...type, draggable: undefined }
        : type,
  ),
});

function annotationAnchor(annotation: SparqlAnnotation, nodes: Map<string, SparqlNode>, regions: Map<string, SparqlRegion>): ShapePoint {
  const node = nodes.get(annotation.attachedTo);
  if (node) {
    return { x: node.x, y: node.y - 6 };
  }
  const region = regions.get(annotation.attachedTo);
  if (region) {
    return { x: region.x + 12, y: region.y + 18 };
  }
  return { x: 0, y: -10 };
}

/** The hand-written canvas's model mapping, unchanged. */
function handWrittenModel(model: SparqlModel): DiagramModel {
  const regions = [...model.regions.values()].map((region) => ({
    id: region.id,
    type: "region",
    x: region.x + region.width / 2,
    y: region.y + region.height / 2,
    width: region.width,
    height: region.height,
    label: region.label,
    payload: { label: region.label, kind: region.kind },
  }));
  const nodes = [...model.nodes.values()].map((node) => ({
    id: node.id,
    type: "node",
    x: node.x + NODE_WIDTH / 2,
    y: node.y + NODE_HEIGHT / 2,
    width: NODE_WIDTH,
    height: NODE_HEIGHT,
    label: node.display,
    payload: { display: node.display, kind: node.kind, projected: node.projected, annotation: node.annotation },
  }));
  const annotations = [...model.annotations.values()].map((annotation) => {
    const anchor = annotationAnchor(annotation, model.nodes, model.regions);
    return { id: annotation.id, type: "annotation", x: anchor.x, y: anchor.y, width: ANNOTATION_WIDTH, height: ANNOTATION_HEIGHT, label: annotation.text, payload: { text: annotation.text, kind: annotation.kind } };
  });
  const connections = [...model.edges.values()].map((edge) => ({
    id: edge.id,
    type: "pattern",
    sourceId: edge.fromElementId,
    targetId: edge.toElementId,
    label: edge.label,
    className: edge.isPath ? "sparql-edge-path" : undefined,
  }));
  return { elements: [...regions, ...nodes, ...annotations], connections };
}

const ids = (markup: string) => markup.replace(/«r[0-9a-z]+»|:r[0-9a-z]+:/g, "«id»");

/** The hand-written canvas, its header and banner with the specification's decisions taken in. */
function oracleDrawing(model: SparqlModel): string {
  const modifiers = model.header?.modifierRows.join(" · ") ?? "";
  const view = render(
    <div className="sparql-canvas canvas-host" role="application" aria-label="SPARQL query">
      {model.header ? (
        <header className="sparql-header-band">
          <span className="sparql-header-form">{model.header.form}</span>
          {modifiers !== "" ? <span className="sparql-header-modifier">{modifiers}</span> : null}
        </header>
      ) : null}
      <DiagramCanvas
        definition={ORACLE}
        model={handWrittenModel(model)}
        events={{}}
        source={{ entryId: ENTRY, path: ["query.rq"] }}
        ariaLabel="SPARQL query"
        className="sparql-surface"
        scrollbarsClassName="sparql-scrollbars"
      />
      {model.truncation ? (
        <p className="sparql-truncation-banner" role="status" data-notice-position="top" data-notice-severity="warning">
          {`Showing ${model.truncation.shown} of ${model.truncation.total} elements — this query is larger than the diagram draws`}
        </p>
      ) : null}
    </div>,
  );
  const markup = ids(view.container.innerHTML);
  view.unmount();
  return markup;
}

function compiledDrawing(model: SparqlModel): string {
  currentModel = model;
  const view = render(<SparqlCanvas projectId={PROJECT} entryId={ENTRY} path={["query.rq"]} />);
  const markup = ids(view.container.innerHTML);
  view.unmount();
  return markup;
}

const SCHEMAS: Readonly<Record<string, DescMessage>> = {
  SparqlVariablePayload: SparqlVariablePayloadSchema,
  SparqlTermPayload: SparqlTermPayloadSchema,
  SparqlEdgePayload: SparqlEdgePayloadSchema,
  SparqlRegionPayload: SparqlRegionPayloadSchema,
  SparqlAnnotationPayload: SparqlAnnotationPayloadSchema,
  SparqlHeaderPayload: SparqlHeaderPayloadSchema,
  SparqlTruncationPayload: SparqlTruncationPayloadSchema,
};

interface TranscriptDocument {
  document: string;
  payloads?: string[];
}

const TRANSCRIPT = JSON.parse(transcriptText) as { documents: TranscriptDocument[] };

/** A document's payloads as the stream delivers them: `id | type | x, y | PayloadName {json}` each. */
function modelOf(lines: readonly string[]): SparqlModel {
  const elements = lines.map((line) => {
    const match = /^(.*?) \| (\S+) \| (-?[\d.]+), (-?[\d.]+) \| (\w+) (\{.*\})$/.exec(line);
    expect(match, line).not.toBeNull();
    const [, id, type, x, y, name, json] = match!;
    const schema = SCHEMAS[name!];
    expect(schema, name).toBeDefined();
    const payload = fromJson(schema!, JSON.parse(json!) as JsonValue);
    return create(ElementSchema, {
      id: { value: id! },
      position: { x: Number(x), y: Number(y) },
      type: type!,
      payload: { typeUrl: `type.googleapis.com/${schema!.typeName}`, value: toBinary(schema!, payload) },
    });
  });
  return applyDelta(emptyModel, { action: { case: "add", value: { elements } } } as Parameters<typeof applyDelta>[1]);
}

/** Every document the transcript draws; a document recorded as the same as another is that one. */
const CORPUS = TRANSCRIPT.documents.filter((document) => (document.payloads?.length ?? 0) > 0);

describe("the SPARQL query's compiled definition", () => {
  it.each(CORPUS.map((document) => [document.document, document] as const))("draws %s as the hand-written definition drew it", (_name, document) => {
    const model = modelOf(document.payloads!);

    expect(compiledDrawing(model)).toBe(oracleDrawing(model));
  });

  it("is compared over a corpus that draws every kind of element, edge, header and banner", () => {
    // The cases the comparison would pass vacuously on: a corpus that drew none of them.
    const drawn = CORPUS.map((document) => compiledDrawing(modelOf(document.payloads!))).join("\n");
    for (const kind of ["variable", "anonymous", "iri", "literal", "subquery"]) {
      expect(drawn, kind).toContain(`sparql-node-${kind}`);
    }
    for (const marked of ["sparql-node-projected", "sparql-projection-mark", "sparql-node-annotation", "sparql-edge-path", "sparql-region-optional", "sparql-region-union", "sparql-region-minus", "sparql-annotation-filter", "sparql-header-modifier", "sparql-truncation-banner", "Showing 500 of"]) {
      expect(drawn, marked).toContain(marked);
    }
  });

  it("lets every element be dragged, so a move the specification refuses comes back with its sentence", () => {
    // An annotation's and an anonymous variable's moves are refused by the backend, in the words of the
    // specification's `refusals.move` and the definition's gesture constraint; a canvas that refused to
    // start the drag would show neither.
    expect(SPARQL_DEFINITION.elementTypes.filter((type) => type.draggable === false).map((type) => type.id)).toEqual([]);
    expect(SPARQL_DEFINITION.dragging).toBe("enabled");
  });

  it("is what the canvas draws: its definition is compiled from the bundled specification", () => {
    // The canary for the tests above: were the canvas still holding its own definition, they would
    // compare the oracle with a copy of itself.
    expect(SPARQL_DEFINITION).toEqual(assertValidDiagramDefinition(compileNotation(parseDisl(disText), SPARQL_BINDINGS)));
    expect(SPARQL_DEFINITION.elementTypes.map((type) => type.id)).toEqual(expect.arrayContaining(["iri", "variable", "anonymousvariable", "literal", "subquery", "scope", "annotation"]));
    expect(SPARQL_DEFINITION.relationTypes.map((relation) => relation.id)).toEqual(expect.arrayContaining(["triplepattern", "projectedjoin"]));
  });
});
