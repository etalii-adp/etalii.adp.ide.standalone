import { describe, expect, it } from "vitest";
import { create, toBinary } from "@bufbuild/protobuf";
import { ElementSchema } from "@client/generated/elements_pb";
import {
  DependencyElementKind,
  DependencyElementPayloadSchema,
} from "@client/generated/dotnet-dependency-graph_pb";
import {
  applyDelta,
  edgesOf,
  emptyModel,
  endsOf,
  nodesOf,
  withoutAmbientPackages,
} from "./dotnetDependencyGraphModel";

const TYPE_URL = "type.googleapis.com/etalii.adp.dotnetdependencygraph.DependencyElementPayload";

function element(
  id: string,
  type: string,
  kind: DependencyElementKind,
  overrides: Record<string, unknown> = {},
  x = 0,
  y = 0,
) {
  const payload = create(DependencyElementPayloadSchema, { name: id, kind, ...overrides });
  return create(ElementSchema, {
    id: { value: id },
    position: { x, y },
    type,
    payload: { typeUrl: TYPE_URL, value: toBinary(DependencyElementPayloadSchema, payload) },
  });
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const addDelta = (...elements: ReturnType<typeof element>[]) => ({ action: { case: "add", value: { elements } } }) as any;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const removeDelta = (...ids: string[]) => ({ action: { case: "remove", value: { elementIds: ids.map((value) => ({ value })) } } }) as any;

describe("dotnetDependencyGraphModel", () => {
  it("decodes projects, packages and edges from the stream", () => {
    const model = applyDelta(
      emptyModel,
      addDelta(
        element("project:A.csproj", "dotnet/dependency-graph+project", DependencyElementKind.PROJECT, { targetFrameworks: ["net10.0"] }),
        element("package:Serilog", "dotnet/dependency-graph+package", DependencyElementKind.PACKAGE, { versions: ["4.4.0"] }),
      ),
    );

    // By id rather than by index: nodesOf sorts, so an index asserts the sort order as much as
    // the decoding, and a change to either would fail this for the wrong reason.
    const nodes = nodesOf(model);
    expect(nodes).toHaveLength(2);
    expect(nodes.find((node) => node.id === "package:Serilog")?.payload.versions).toEqual(["4.4.0"]);
    expect(nodes.find((node) => node.id === "project:A.csproj")?.payload.targetFrameworks).toEqual(["net10.0"]);
  });

  it("ignores an element type this diagram does not draw", () => {
    // The canvas draws what it understands and never guesses at what it does not - a stray
    // element from another type's stream must not become a box with no meaning.
    const model = applyDelta(
      emptyModel,
      addDelta(element("mystery", "some/other+thing", DependencyElementKind.PROJECT)),
    );

    expect(model.elements.size).toBe(0);
  });

  it("separates nodes from edges", () => {
    const model = applyDelta(
      emptyModel,
      addDelta(
        element("project:A.csproj", "dotnet/dependency-graph+project", DependencyElementKind.PROJECT),
        element("depends:project:A.csproj->package:Serilog", "dotnet/dependency-graph+edge", DependencyElementKind.PACKAGE_REFERENCE),
      ),
    );

    expect(nodesOf(model).map((node) => node.id)).toEqual(["project:A.csproj"]);
    expect(edgesOf(model).map((edge) => edge.id)).toEqual(["depends:project:A.csproj->package:Serilog"]);
  });

  it("reads an edge's two ends out of its own id", () => {
    // The ends are carried by the id rather than duplicated into the payload: one source of
    // truth, so an edge whose ends disagreed with its id is impossible rather than unlikely.
    const model = applyDelta(
      emptyModel,
      addDelta(
        element("project:A.csproj", "dotnet/dependency-graph+project", DependencyElementKind.PROJECT),
        element("package:Serilog", "dotnet/dependency-graph+package", DependencyElementKind.PACKAGE),
        element("depends:project:A.csproj->package:Serilog", "dotnet/dependency-graph+edge", DependencyElementKind.PACKAGE_REFERENCE),
      ),
    );

    const ends = endsOf(model, edgesOf(model)[0]);
    expect(ends?.from.id).toBe("project:A.csproj");
    expect(ends?.to.id).toBe("package:Serilog");
  });

  it("gives no ends for an edge whose partner has not been delivered", () => {
    // What stops a connector being drawn to nothing.
    const model = applyDelta(
      emptyModel,
      addDelta(
        element("project:A.csproj", "dotnet/dependency-graph+project", DependencyElementKind.PROJECT),
        element("depends:project:A.csproj->package:Serilog", "dotnet/dependency-graph+edge", DependencyElementKind.PACKAGE_REFERENCE),
      ),
    );

    expect(endsOf(model, edgesOf(model)[0])).toBeNull();
  });

  it("removes what a remove delta names, and leaves the rest", () => {
    const added = applyDelta(
      emptyModel,
      addDelta(
        element("project:A.csproj", "dotnet/dependency-graph+project", DependencyElementKind.PROJECT),
        element("project:B.csproj", "dotnet/dependency-graph+project", DependencyElementKind.PROJECT),
      ),
    );

    const after = applyDelta(added, removeDelta("project:A.csproj"));

    expect(nodesOf(after).map((node) => node.id)).toEqual(["project:B.csproj"]);
  });

  describe("ambient packages", () => {
    // One project, two packages: xunit ambient, Serilog not, and one edge to each.
    const graph = () =>
      applyDelta(
        emptyModel,
        addDelta(
          element("project:A.csproj", "dotnet/dependency-graph+project", DependencyElementKind.PROJECT),
          element("package:xunit.v3", "dotnet/dependency-graph+package", DependencyElementKind.PACKAGE, {
            isAmbient: true,
            dependentProjectCount: 26,
          }),
          element("package:Serilog", "dotnet/dependency-graph+package", DependencyElementKind.PACKAGE, {
            dependentProjectCount: 3,
          }),
          element("depends:project:A.csproj->package:xunit.v3", "dotnet/dependency-graph+edge", DependencyElementKind.PACKAGE_REFERENCE),
          element("depends:project:A.csproj->package:Serilog", "dotnet/dependency-graph+edge", DependencyElementKind.PACKAGE_REFERENCE),
        ),
      );

    it("hides an ambient package and the edges that reach it", () => {
      // The scale answer applied: four package nodes carry 63% of this repository's package
      // edges and discriminate nothing. An edge left behind would be a connector to nothing,
      // so it goes with the node.
      const { nodes, edges } = withoutAmbientPackages(graph(), false);

      expect(nodes.map((node) => node.id)).toEqual(["package:Serilog", "project:A.csproj"]);
      expect(edges.map((edge) => edge.id)).toEqual(["depends:project:A.csproj->package:Serilog"]);
    });

    it("reports what it hid, so the filtering can be seen", () => {
      // Part of the feature, not a nicety: filtering that cannot be seen is just a wrong
      // diagram. The canvas names them, so the count and the identity both have to survive.
      const { hidden } = withoutAmbientPackages(graph(), false);

      expect(hidden.map((node) => node.payload.name)).toEqual(["package:xunit.v3"]);
    });

    it("puts every node back when asked to show them", () => {
      // Restorable, and from the model rather than by re-deriving: nothing was ever removed.
      const { nodes, edges, hidden } = withoutAmbientPackages(graph(), true);

      expect(nodes).toHaveLength(3);
      expect(edges).toHaveLength(2);
      expect(hidden).toHaveLength(0);
    });

    it("hides nothing when the backend marked nothing", () => {
      // A small solution reaches the canvas with no marking at all, and must draw complete.
      const model = applyDelta(
        emptyModel,
        addDelta(
          element("project:A.csproj", "dotnet/dependency-graph+project", DependencyElementKind.PROJECT),
          element("package:Serilog", "dotnet/dependency-graph+package", DependencyElementKind.PACKAGE),
        ),
      );

      const { nodes, hidden } = withoutAmbientPackages(model, false);

      expect(nodes).toHaveLength(2);
      expect(hidden).toHaveLength(0);
    });
  });

  it("never mutates the model it was given", () => {
    // The reducer is pure, so React sees a new object and a stale render cannot show through.
    const before = applyDelta(emptyModel, addDelta(element("project:A.csproj", "dotnet/dependency-graph+project", DependencyElementKind.PROJECT)));
    const sizeBefore = before.elements.size;

    applyDelta(before, addDelta(element("project:B.csproj", "dotnet/dependency-graph+project", DependencyElementKind.PROJECT)));

    expect(before.elements.size).toBe(sizeBefore);
  });
});
