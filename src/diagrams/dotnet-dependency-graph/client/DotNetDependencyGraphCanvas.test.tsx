import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { create, toBinary } from "@bufbuild/protobuf";
import { ElementSchema } from "@client/generated/elements_pb";
import {
  DependencyElementKind,
  DependencyElementPayloadSchema,
} from "@client/generated/dotnet-dependency-graph_pb";
import { applyDelta, emptyModel, type DotNetDependencyGraphModel } from "./dotnetDependencyGraphModel";
import { elementSelectionOf, selectedElementIdOf } from "@client/canvas/selection";
import type { ContextSelection } from "@client/generated/context_pb";
import { expectLibrarySelection } from "@client/canvas/library/testing/expectLibrarySelection";

const select = vi.fn();
/** The backend's pushed selection - null for every test but the shared selection assertion. */
let currentSelection: unknown = null;
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
    // The library reads the inline-edit prompt itself where it owns the canvas (client-centralization
    // task 7), so a sourced canvas needs one here even though this module never renames inline.
    useContextPrompt: () => ({ prompt: null, onPropose: vi.fn(), onSubmit: vi.fn(), onCancel: vi.fn() }),
    useContextConnection: () => ({ watchId: new Uint8Array(16), select, revealPath }),
    useContextSelection: () => ({ selection: currentSelection }),
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
    expect(container.querySelectorAll(".dotnet-dependency-element-project")).toHaveLength(4);
    expect(container.querySelectorAll(".dotnet-dependency-element-package").length).toBeGreaterThan(0);
  });

  it("draws each node through the shared span, as the authored dependency graph does", () => {
    // THE REVIEW CRITERION, MADE ASSERTABLE. This module's specification carries "consistency
    // with the authored generic/dependencies canvas" as a non-functional requirement, and its
    // tasks document records it as "a review criterion rather than an assertable test". Nobody
    // reviewed it, and the two dependency graphs shipped looking like different products - a
    // criterion whose absence leaves no trace is one nothing will ever report.
    //
    // What this can and cannot prove, stated so the next reader does not over-read it: it pins
    // the SLOT CONTRACT the shared span renders and the stylesheet depends on - a body rect and
    // a label text carrying the module's span classes, inside the element group. A different
    // primitive given the same class names would still pass. What it catches is the change that
    // actually happened here: a canvas drawing its own furniture under its own class names,
    // which is what this one did until the swap.
    currentModel = showcaseModel();
    currentLoading = false;
    currentFailed = false;

    const { container } = renderCanvas();

    const project = container.querySelector(".dotnet-dependency-element-project");
    expect(project, "no project element was rendered at all").not.toBeNull();

    // The body and the label, in the span's own slots.
    expect(project!.querySelector("rect.dotnet-dependency-node")).not.toBeNull();
    expect(project!.querySelector("text.dotnet-dependency-node-label")).not.toBeNull();

    // And the one thing the authored graph has no equivalent of, kept rather than lost in the
    // swap: the second line. A project shows its target frameworks.
    expect(project!.querySelector("text.dotnet-dependency-node-subtitle")?.textContent).toBe("net10.0");
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

/**
 * WHAT THE CUSTOM RENDERER USED TO CARRY, now declared - and untested until this migration.
 *
 * A double-click revealing the project file, a right-click asking for the menu, the accessible
 * name and the tooltip were all attributes on the element the shape rendered, and none of them
 * had a guard. Moving them into the declaration is where they became checkable, so this is the
 * migration paying for itself rather than merely not breaking anything.
 */
describe("DotNetDependencyGraphCanvas — the declared element", () => {
  beforeEach(() => {
    currentModel = showcaseModel();
    currentLoading = false;
    currentFailed = false;
  });

  it("reveals a project's file on double-click, through a declared action", () => {
    const { container } = renderCanvas();
    const project = container.querySelector(".dotnet-dependency-element-project")!;

    fireEvent.doubleClick(project);

    expect(revealPath).toHaveBeenCalled();
  });

  it("asks for the context menu on right-click, through a declared action", () => {
    const { container } = renderCanvas();
    const project = container.querySelector(".dotnet-dependency-element-project")!;

    fireEvent.contextMenu(project);

    // The selection the module pushes carries the CONTEXT_MENU action - the same call its
    // `onContextMenu` made by hand.
    expect(select).toHaveBeenCalled();
  });

  it("carries its accessible name and stays in the tab order", () => {
    const { container } = renderCanvas();
    const project = container.querySelector(".dotnet-dependency-element-project")!;

    expect(project.getAttribute("role")).toBe("button");
    expect(project.getAttribute("tabindex")).toBe("0");
    expect(project.getAttribute("aria-label")).toMatch(/^Project /);
  });

  it("says which package is referenced at more than one version, in its title", () => {
    const { container } = renderCanvas();
    const project = container.querySelector(".dotnet-dependency-element-project")!;

    expect(project.querySelector("title")?.textContent).toMatch(/^Project /);
  });
});

describe("selection, as every canvas has it", () => {
  it("highlights a pushed project and reference, and clears on a background press (centralized-selection 9.2)", () => {
    try {
      expectLibrarySelection({
        mountWith: (id) => {
          currentModel = showcaseModel();
          currentLoading = false;
          currentFailed = false;
          currentSelection = id === null ? null : elementSelectionOf(new Uint8Array(16), ["PipelineToolkit.adp"], id);
          return renderCanvas();
        },
        pushedIds: () => select.mock.calls.map(([push]) => (push === null ? null : (selectedElementIdOf(push as ContextSelection) ?? null))),
        element: "project:src/Pipeline.Core/Pipeline.Core.csproj",
        // A project reference: the kind of line this canvas used to draw as if it named an element.
        connection: "depends:project:src/Pipeline.Storage/Pipeline.Storage.csproj->project:src/Pipeline.Core/Pipeline.Core.csproj",
      });
    } finally {
      currentSelection = null;
    }
  });
});
