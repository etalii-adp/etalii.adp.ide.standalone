import { describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { create, toBinary } from "@bufbuild/protobuf";
import { ElementSchema } from "@client/generated/elements_pb";
import {
  DependencyElementKind,
  DependencyElementPayloadSchema,
} from "@client/generated/dotnet-dependency-graph_pb";
import { applyDelta, emptyModel, type DotNetDependencyGraphModel } from "./dotnetDependencyGraphModel";

const select = vi.fn();
const revealPath = vi.fn();
const moveElementTo = vi.fn(async (_elementId: string, _x: number, _y: number) => "");
const reportView = vi.fn();
let currentModel: DotNetDependencyGraphModel = emptyModel;
let currentLoading = false;
let currentFailed = false;

vi.mock("./useDotNetDependencyGraphStream", () => ({
  useDotNetDependencyGraphStream: () => ({
    model: currentModel,
    loading: currentLoading,
    failed: currentFailed,
    reportView,
    moveElementTo,
  }),
}));

vi.mock("@client/shell/context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@client/shell/context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextConnection: () => ({ watchId: new Uint8Array(16), select, revealPath }),
    useContextSelection: () => ({ selection: null }),
  };
});

const emptyPalette: never[] = [];

vi.mock("@client/shell/panels/useToolboxItems", () => ({
  useToolboxItems: () => emptyPalette,
}));

const { DotNetDependencyGraphCanvas } = await import("./DotNetDependencyGraphCanvas");

function element(id: string, type: string, kind: DependencyElementKind, overrides: Record<string, unknown> = {}) {
  const payload = create(DependencyElementPayloadSchema, {
    name: id.split(":").slice(1).join(":") || id,
    kind,
    ...overrides,
  });
  return create(ElementSchema, {
    id: { value: id },
    position: { x: 0, y: 0 },
    type,
    payload: {
      typeUrl: "type.googleapis.com/etalii.adp.dotnetdependencygraph.DependencyElementPayload",
      value: toBinary(DependencyElementPayloadSchema, payload),
    },
  });
}

/**
 * THE SHOWCASE, ON THE WIRE.
 *
 * These are the sixteen elements the backend actually delivers for
 * `src/examples/diagrams/dotnet-dependency-graph/pipeline-toolkit`, ids and type strings copied
 * verbatim from a run of the real host over gRPC rather than invented here. That is the point:
 * a fixture the client author makes up tests the client against its own idea of the wire, which
 * is exactly the agreement that can be wrong while both sides pass their own tests.
 */
function showcaseModel(): DotNetDependencyGraphModel {
  const project = (path: string) =>
    element(`project:${path}`, "dotnet/dependency-graph+project", DependencyElementKind.PROJECT, {
      projectRelativePath: path.split("/"),
      targetFrameworks: ["net10.0"],
    });
  const pkg = (id: string) =>
    element(`package:${id}`, "dotnet/dependency-graph+package", DependencyElementKind.PACKAGE, { versions: ["1.0.0"] });
  const edge = (from: string, to: string, kind: DependencyElementKind) =>
    element(`depends:${from}->${to}`, "dotnet/dependency-graph+edge", kind);

  const core = "project:src/Pipeline.Core/Pipeline.Core.csproj";
  const storage = "project:src/Pipeline.Storage/Pipeline.Storage.csproj";
  const cli = "project:src/Pipeline.Cli/Pipeline.Cli.csproj";
  const tests = "project:tests/Pipeline.Core.Tests/Pipeline.Core.Tests.csproj";

  return applyDelta(emptyModel, {
    action: {
      case: "add",
      value: {
        elements: [
          project("src/Pipeline.Core/Pipeline.Core.csproj"),
          project("src/Pipeline.Storage/Pipeline.Storage.csproj"),
          project("src/Pipeline.Cli/Pipeline.Cli.csproj"),
          project("tests/Pipeline.Core.Tests/Pipeline.Core.Tests.csproj"),
          pkg("Serilog"),
          pkg("System.Text.Json"),
          pkg("xunit.v3"),
          edge(core, "package:Serilog", DependencyElementKind.PACKAGE_REFERENCE),
          edge(storage, core, DependencyElementKind.PROJECT_REFERENCE),
          edge(storage, "package:System.Text.Json", DependencyElementKind.PACKAGE_REFERENCE),
          edge(cli, core, DependencyElementKind.PROJECT_REFERENCE),
          edge(cli, storage, DependencyElementKind.PROJECT_REFERENCE),
          edge(cli, "package:Serilog", DependencyElementKind.PACKAGE_REFERENCE),
          edge(tests, core, DependencyElementKind.PROJECT_REFERENCE),
          edge(tests, "package:xunit.v3", DependencyElementKind.PACKAGE_REFERENCE),
          edge(tests, "package:Serilog", DependencyElementKind.PACKAGE_REFERENCE),
        ],
      },
    },
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
  } as any);
}

function renderCanvas() {
  return render(
    <DotNetDependencyGraphCanvas
      projectId={new Uint8Array(16)}
      entryId={new Uint8Array(16)}
      path={["PipelineToolkit.adp"]}
    />,
  );
}

describe("DotNetDependencyGraphCanvas", () => {
  it("draws the shipped showcase rather than reporting it empty", () => {
    // THE GUARD FOR THE SHOWCASE BUG. The user's report was this canvas saying "This solution
    // has no projects ADP could resolve" for a solution the backend resolves completely - so
    // the assertion that matters is the absence of that message with the real wire payload in
    // hand, not merely that some node rendered.
    currentModel = showcaseModel();
    currentLoading = false;
    currentFailed = false;

    const { container, queryByText } = renderCanvas();

    expect(queryByText(/no projects ADP could resolve/i)).toBeNull();
    expect(container.querySelectorAll(".dotnet-dependency-node-project")).toHaveLength(4);
    expect(container.querySelectorAll(".dotnet-dependency-node-package").length).toBeGreaterThan(0);
  });

  it("still says the solution is empty when the backend really resolved nothing", () => {
    // The pairing: the message is right for the case it was written for, so the test above is
    // asserting the payload reaches the canvas rather than that the message was deleted.
    currentModel = emptyModel;
    currentLoading = false;
    currentFailed = false;

    const { queryByText } = renderCanvas();

    expect(queryByText(/no projects ADP could resolve/i)).not.toBeNull();
  });
});
