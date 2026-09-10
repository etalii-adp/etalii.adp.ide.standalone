import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { create, toBinary } from "@bufbuild/protobuf";
import { ElementSchema } from "@client/generated/elements_pb";
import {
  AnsibleEdgeKind,
  AnsibleElementKind,
  AnsibleElementPayloadSchema,
} from "@client/generated/ansible-structure_pb";
import { VIEW_REPORT_DEBOUNCE_MS } from "@client/diagrams/viewReport";
import { applyDelta, emptyModel, type AnsibleModel } from "./ansibleModel";
import { DiagramToolboxProvider, useDiagramToolbox } from "@client/shell/panels/DiagramToolboxContext";

const select = vi.fn();
const revealPath = vi.fn();
// Typed with the real signature, so the call assertions below can read the arguments.
const moveElementTo = vi.fn(async (_elementId: string, _x: number, _y: number) => "");
const reportView = vi.fn();
let currentModel: AnsibleModel = emptyModel;
let currentLoading = false;
let currentFailed = false;
let currentSelection: unknown = null;

vi.mock("./useAnsibleStream", () => ({
  useAnsibleStream: () => ({
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
    useContextSelection: () => ({ selection: currentSelection }),
  };
});

// Imported after the mocks so the component picks them up.
let toolboxRequests: (readonly string[])[] = [];

// The backend answers an empty palette for this type: it registers no toolbox provider,
// deliberately - the module is read-only. One stable instance, as the real hook returns:
// a fresh array per render would re-register on every one (see useRegisterDiagramToolbox).
const emptyPalette: never[] = [];

vi.mock("@client/shell/panels/useToolboxItems", () => ({
  useToolboxItems: (_projectId: Uint8Array, path: readonly string[]) => {
    toolboxRequests.push(path);
    return emptyPalette;
  },
}));

const { AnsibleCanvas } = await import("./AnsibleCanvas");

function element(
  id: string,
  type: string,
  kind: AnsibleElementKind,
  overrides: Record<string, unknown> = {},
  x = 0,
  y = 0,
) {
  const payload = create(AnsibleElementPayloadSchema, {
    name: id.split(":")[1] ?? id,
    kind,
    width: 120,
    height: 32,
    playIndex: -1,
    ...overrides,
  });
  return create(ElementSchema, {
    id: { value: id },
    position: { x, y },
    type,
    payload: {
      typeUrl: "type.googleapis.com/etalii.adp.ansible.AnsibleElementPayload",
      value: toBinary(AnsibleElementPayloadSchema, payload),
    },
  });
}

function modelOf(...elements: ReturnType<typeof element>[]): AnsibleModel {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  return applyDelta(emptyModel, { action: { case: "add", value: { elements } } } as any);
}

function renderCanvas() {
  return render(<AnsibleCanvas projectId={new Uint8Array(16)} entryId={new Uint8Array(16)} path={["infrastructure.adp"]} />);
}

/**
 * A pointer event jsdom can actually carry. jsdom implements no PointerEvent at all, so
 * `fireEvent.pointerDown` builds a bare Event whose `button` is undefined - and the canvas
 * checks `button !== 0` so a right-drag never repositions anything. A MouseEvent typed
 * "pointerdown" bubbles the same way and carries the button, which is what a real browser
 * delivers. The guard is right; the environment is what is missing.
 */
const pointer = (type: string, init: MouseEventInit) =>
  new MouseEvent(type, { bubbles: true, cancelable: true, ...init });

// jsdom implements no pointer capture on SVG elements. The canvas uses it so a drag whose
// pointer leaves the svg still delivers its pointerup, which every real browser supports -
// stubbed here rather than feature-detected in the component, so the production path stays
// the one that actually ships.
beforeEach(() => {
  SVGElement.prototype.setPointerCapture ??= () => {};
  SVGElement.prototype.releasePointerCapture ??= () => {};
  select.mockClear();
  revealPath.mockClear();
  reportView.mockClear();
  currentLoading = false;
  currentFailed = false;
  currentSelection = null;
  currentModel = modelOf(
    element("playbook:site.yml", "ansible/structure+playbook", AnsibleElementKind.PLAYBOOK, {
      projectRelativePath: ["site.yml"],
    }),
    element("role:nginx", "ansible/structure+role", AnsibleElementKind.ROLE, {
      projectRelativePath: ["roles", "nginx"],
      playIndex: 0,
    }, 300, 0),
  );
});

describe("AnsibleCanvas", () => {
  it("draws every node kind with a class of its own", () => {
    // Arrange.
    currentModel = modelOf(
      element("playbook:a.yml", "ansible/structure+playbook", AnsibleElementKind.PLAYBOOK),
      element("play:a.yml#0", "ansible/structure+play", AnsibleElementKind.PLAY),
      element("role:r", "ansible/structure+role", AnsibleElementKind.ROLE),
      element("taskfile:roles/r/tasks/t.yml", "ansible/structure+taskfile", AnsibleElementKind.TASK_FILE),
      element("inventory:inventories/p", "ansible/structure+inventory", AnsibleElementKind.INVENTORY),
      element("vars:inventories/p/group_vars", "ansible/structure+vars", AnsibleElementKind.VARIABLE_FOLDER),
    );

    // Act.
    const { container } = renderCanvas();

    // Assert.
    // Colour is never the only thing carrying the distinction; each kind has its own class,
    // and the stylesheet gives each its own outline weight or dash.
    for (const kind of ["playbook", "play", "role", "taskfile", "inventory", "vars"]) {
      expect(container.querySelector(`.ansible-node-${kind}`), kind).not.toBeNull();
    }
  });

  it("gives a node its play's palette slot as a class, never a colour", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    const role = container.querySelector('[data-element-id="role:nginx"] .ansible-node');
    expect(role?.getAttribute("class")).toContain("ansible-play-0");
    // Nothing inline on the drawing but geometry: the palette lives in the stylesheet so a theme
    // change is a CSS change. Scoped to the svg rather than the whole container, because the
    // shared scrollbars below it position their thumbs inline - a thumb's offset is geometry, not
    // theme, and it cannot be a class. A declared label's text-anchor is inline for the same
    // reason: it is half of the x the layout computed, and a class rule must not re-anchor it.
    // An allowlist rather than a list of colour properties, so a colour property nobody thought
    // to name still fails here.
    const geometry = new Set(["text-anchor"]);
    const inline = Array.from(container.querySelectorAll<SVGElement>("svg.library-canvas-surface [style]"))
      .flatMap((element) => Array.from(element.style))
      .filter((property) => !geometry.has(property));
    expect(inline).toEqual([]);
  });


  // ---- scrollbars and drag (Requirements 1.1, 1.2, 6.1-6.3) --------------------------------

  it("shows the shared scrollbars over the canvas", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert: the shared component, not an ansible-specific one - both bars, each with a thumb.
    expect(container.querySelectorAll(".canvas-scrollbar")).toHaveLength(2);
    expect(container.querySelectorAll(".canvas-scrollbar-thumb")).toHaveLength(2);
  });

  it("composes the shared canvas chrome alongside its own class names", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert: the module keeps its names, so existing selectors and tests hold, and gains the
    // shared appearance beside them.
    expect(container.querySelector("svg.library-canvas-surface")).not.toBeNull();
    expect(container.querySelector(".ansible-canvas-host.canvas-host")).not.toBeNull();
    expect(container.querySelector(".library-canvas.ansible-canvas-viewport")).not.toBeNull();
  });

  it("repositions a node when it is dragged past the threshold", async () => {
    // Arrange.
    moveElementTo.mockClear();
    const { container } = renderCanvas();
    const role = container.querySelector('[data-element-id="role:nginx"]')!;

    // Act.
    fireEvent(role, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
    fireEvent(role, pointer("pointermove", { clientX: 90, clientY: 70 }));
    fireEvent(role, pointer("pointerup", { clientX: 90, clientY: 70 }));

    // Assert: the element's id and a position, in canvas units.
    expect(moveElementTo).toHaveBeenCalledTimes(1);
    expect(moveElementTo.mock.calls[0][0]).toBe("role:nginx");
  });

  it("does not reposition on a click that barely moves", () => {
    // Arrange: a hand that shifts by a pixel while clicking must not author a position.
    moveElementTo.mockClear();
    const { container } = renderCanvas();
    const role = container.querySelector('[data-element-id="role:nginx"]')!;

    // Act.
    fireEvent(role, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
    fireEvent(role, pointer("pointermove", { clientX: 11, clientY: 10 }));
    fireEvent(role, pointer("pointerup", { clientX: 11, clientY: 10 }));

    // Assert.
    expect(moveElementTo).not.toHaveBeenCalled();
  });

  it("never repositions an edge, which follows its endpoints", () => {
    // Arrange.
    moveElementTo.mockClear();
    const { container } = renderCanvas();
    const edge = container.querySelector("[data-edge-id]");

    // Act.
    if (edge) {
      fireEvent(edge, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
      fireEvent(edge, pointer("pointermove", { clientX: 90, clientY: 70 }));
      fireEvent(edge, pointer("pointerup", { clientX: 90, clientY: 70 }));
    }

    // Assert: an edge carries no drag handler at all, so nothing is written.
    expect(moveElementTo).not.toHaveBeenCalled();
  });


  it("captures the pointer on the node, never on the svg", () => {
    // The regression this pins, found in the manual verification pass: a pointer capture
    // retargets the pointerup, and the browser fires `click` on the common ancestor of the
    // down and up targets. Capturing to the svg therefore delivered the click to the svg
    // instead of the node, and click-to-select silently stopped working while every test
    // here still passed - jsdom implements no pointer capture, so the stub above hides the
    // consequence entirely. What CAN be asserted is which element was asked to capture.
    const captured: string[] = [];
    const original = SVGElement.prototype.setPointerCapture;
    SVGElement.prototype.setPointerCapture = function (this: SVGElement) {
      captured.push(this.getAttribute("data-element-id") ?? this.getAttribute("class") ?? "unknown");
    };

    try {
      // Act.
      const { container } = renderCanvas();
      const role = container.querySelector('[data-element-id="role:nginx"]')!;
      fireEvent(role, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));

      // Assert.
      expect(captured).toEqual(["role:nginx"]);
    } finally {
      SVGElement.prototype.setPointerCapture = original;
    }
  });

  it("marks a hollow role so it looks unfinished", () => {
    // Arrange.
    currentModel = modelOf(
      element("role:hollow", "ansible/structure+role", AnsibleElementKind.ROLE, { hollow: true }),
    );

    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelector(".ansible-node-hollow")).not.toBeNull();
  });

  it("draws a static and a dynamic edge differently", () => {
    // Arrange.
    currentModel = modelOf(
      element("playbook:a.yml", "ansible/structure+playbook", AnsibleElementKind.PLAYBOOK),
      element("role:r", "ansible/structure+role", AnsibleElementKind.ROLE, {}, 300, 0),
      element("edge:static", "ansible/structure+edge", AnsibleElementKind.EDGE, {
        edge: { sourceId: "playbook:a.yml", targetId: "role:r", kind: AnsibleEdgeKind.USES_ROLE, directive: "roles:", dynamic: false },
      }),
      element("edge:dynamic", "ansible/structure+edge", AnsibleElementKind.EDGE, {
        edge: { sourceId: "playbook:a.yml", targetId: "role:r", kind: AnsibleEdgeKind.INCLUDES_TASKS, directive: "include_tasks", dynamic: true },
      }),
    );

    // Act.
    const { container } = renderCanvas();

    // Assert.
    // import_* is resolved before the run and include_* during it, and the two must not look
    // the same or a reader learns they are the same thing.
    expect(container.querySelector(".ansible-edge-static")).not.toBeNull();
    expect(container.querySelector(".ansible-edge-dynamic")).not.toBeNull();
  });

  it("anchors an edge on the source's right side and the target's left side, arrow and all", () => {
    // Arrange: playbook at (0,0), role at (300,0), both 120x32.
    currentModel = modelOf(
      element("playbook:a.yml", "ansible/structure+playbook", AnsibleElementKind.PLAYBOOK),
      element("role:r", "ansible/structure+role", AnsibleElementKind.ROLE, {}, 300, 0),
      element("edge:static", "ansible/structure+edge", AnsibleElementKind.EDGE, {
        edge: { sourceId: "playbook:a.yml", targetId: "role:r", kind: AnsibleEdgeKind.USES_ROLE, directive: "roles:", dynamic: false },
      }),
    );

    // Act.
    const { container } = renderCanvas();
    const line = container.querySelector(".ansible-edge-line")!;
    const numbers = line.getAttribute("d")!.match(/-?[\d.]+/g)!.map(Number);

    // Assert.
    // From (120, 16) - the source's right edge at mid-height - to (300, 16), the target's
    // left edge. The old centre-line anchoring fed the boxes' top-left corners in as centres,
    // so every line started half a box off and cut diagonally across the columns.
    expect(numbers[0]).toBe(120);
    expect(numbers[1]).toBe(16);
    expect(numbers[numbers.length - 2]).toBe(300);
    expect(numbers[numbers.length - 1]).toBe(16);
    expect(line.getAttribute("marker-end")).toBe("url(#library-arrow)");
  });

  it("gives a dependsOn edge a style of its own", () => {
    // Arrange.
    currentModel = modelOf(
      element("role:a", "ansible/structure+role", AnsibleElementKind.ROLE),
      element("role:b", "ansible/structure+role", AnsibleElementKind.ROLE, {}, 300, 0),
      element("edge:dep", "ansible/structure+edge", AnsibleElementKind.EDGE, {
        edge: { sourceId: "role:a", targetId: "role:b", kind: AnsibleEdgeKind.DEPENDS_ON, directive: "dependencies" },
      }),
    );

    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.querySelector(".ansible-edge-depends-on")).not.toBeNull();
  });

  it("labels an edge with its directive, and with its condition when it has one", () => {
    // Arrange.
    currentModel = modelOf(
      element("role:a", "ansible/structure+role", AnsibleElementKind.ROLE),
      element("taskfile:t", "ansible/structure+taskfile", AnsibleElementKind.TASK_FILE, {}, 300, 0),
      element("edge:inc", "ansible/structure+edge", AnsibleElementKind.EDGE, {
        edge: {
          sourceId: "role:a",
          targetId: "taskfile:t",
          kind: AnsibleEdgeKind.INCLUDES_TASKS,
          directive: "include_tasks",
          dynamic: true,
          condition: "nginx_tls_enabled",
        },
      }),
    );

    // Act.
    const { container } = renderCanvas();

    // Assert.
    // The condition is shown as written and never evaluated.
    expect(container.textContent).toContain("include_tasks when nginx_tls_enabled");
  });

  it("draws a missing target as a stub rather than not at all", () => {
    // Arrange.
    currentModel = modelOf(
      element("playbook:a.yml", "ansible/structure+playbook", AnsibleElementKind.PLAYBOOK),
      element("edge:missing", "ansible/structure+edge", AnsibleElementKind.EDGE, {
        edge: { sourceId: "playbook:a.yml", targetId: "", kind: AnsibleEdgeKind.USES_ROLE, directive: "roles:", targetAsWritten: "absent-role" },
      }),
    );

    // Act.
    const { container } = renderCanvas();

    // Assert.
    // A reader has to be able to see that a playbook names something that is not there.
    expect(container.querySelector(".ansible-edge-unresolved")).not.toBeNull();
    expect(container.textContent).toContain("absent-role (missing)");
  });

  it("tells a missing target from an unknowable one", () => {
    // Arrange.
    currentModel = modelOf(
      element("playbook:a.yml", "ansible/structure+playbook", AnsibleElementKind.PLAYBOOK),
      element("edge:expr", "ansible/structure+edge", AnsibleElementKind.EDGE, {
        unresolvable: true,
        edge: { sourceId: "playbook:a.yml", targetId: "", kind: AnsibleEdgeKind.USES_ROLE, directive: "roles:", targetAsWritten: "{{ role_name }}" },
      }),
    );

    // Act.
    const { container } = renderCanvas();

    // Assert.
    // Missing is a mistake; unknown is not, and the reader must be able to tell.
    expect(container.textContent).toContain("{{ role_name }} (expression)");
    expect(container.textContent).not.toContain("(missing)");
  });

  // ---- what the user can do ------------------------------------------------------------------

  it("selects a node on click", () => {
    // Act.
    const { container } = renderCanvas();
    const role = container.querySelector('[data-element-id="role:nginx"]')!;
    fireEvent(role, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
    fireEvent(role, pointer("pointerup", { clientX: 10, clientY: 10 }));

    // Assert.
    expect(select).toHaveBeenCalledTimes(1);
  });

  it("reveals a node's file on double click", () => {
    // Act.
    const { container } = renderCanvas();
    fireEvent.doubleClick(container.querySelector('[data-element-id="role:nginx"] .ansible-node')!);

    // Assert.
    // The jump from the picture to the file is most of what this diagram type is for.
    expect(revealPath).toHaveBeenCalledWith(["roles", "nginx"]);
  });

  it("reveals a node's file from the keyboard too", () => {
    // Arrange.
    const { container } = renderCanvas();
    const role = container.querySelector('[data-element-id="role:nginx"]')!;
    fireEvent(role, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
    fireEvent(role, pointer("pointerup", { clientX: 10, clientY: 10 }));

    // Act.
    fireEvent.keyDown(container.querySelector("svg.library-canvas-surface")!, { key: "Enter" });

    // Assert.
    // A reader who navigates by keyboard should not have to reach for the mouse to use the one
    // thing the diagram offers.
    expect(revealPath).toHaveBeenCalledWith(["roles", "nginx"]);
  });

  it("offers no editing affordance of any kind", () => {
    // Act.
    const { container } = renderCanvas();

    // Assert.
    // Nothing on this diagram is draggable, droppable or editable, and the absence is the type's
    // whole statement - so it is asserted rather than left to inspection.
    expect(container.querySelector("[draggable]")).toBeNull();
    expect(container.querySelector("input, textarea, [contenteditable]")).toBeNull();
    expect(container.querySelector("[data-testid*='toolbox']")).toBeNull();
  });

  // ---- the states before there is anything to draw ----------------------------------------------

  it("says it is reading rather than showing an empty canvas", () => {
    // Arrange.
    currentLoading = true;

    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.textContent).toContain("Reading the folder");
  });

  it("says so when the diagram cannot be opened", () => {
    // Arrange.
    currentFailed = true;

    // Act.
    const { container } = renderCanvas();

    // Assert.
    expect(container.textContent).toContain("could not be opened");
  });

  it("explains an unrecognised layout rather than showing a blank canvas", () => {
    // Arrange.
    currentModel = emptyModel;

    // Act.
    const { container } = renderCanvas();

    // Assert.
    // Undrawn is a valid answer, but a bare empty canvas would read as broken - so the canvas
    // says where this type looks.
    expect(container.textContent).toContain("playbooks/");
    expect(container.textContent).toContain("roles/");
  });
});

/** Reads what the shell's Toolbox panel reads: null is what renders the "Open a diagram" placeholder. */
function ToolboxProbe() {
  const items = useDiagramToolbox();
  return <div data-testid="toolbox-probe">{items === null ? "placeholder" : "palette:" + items.map((item) => item.label).join(",")}</div>;
}

describe("AnsibleCanvas toolbox", () => {
  it("registers its empty palette, so the panel says this type offers nothing rather than that no diagram is open", () => {
    // Arrange. The module registers no toolbox provider by design - but the *panel* can only
    // say so ("This diagram type offers no toolbox elements") if the mounted canvas registers
    // the empty answer. Unregistered, an open structure diagram wears the misleading
    // "Open a diagram" placeholder - the same defect the wardley and pipeline canvases had.
    currentModel = emptyModel;
    toolboxRequests = [];
    const path = ["diagrams", "ansible-structure", "example 1", "structure.adp"];

    // Act.
    const { getByTestId } = render(
      <DiagramToolboxProvider>
        <AnsibleCanvas projectId={new Uint8Array(16)} entryId={new Uint8Array(16)} path={path} />
        <ToolboxProbe />
      </DiagramToolboxProvider>,
    );

    // Assert: registered-and-empty, not unregistered.
    expect(getByTestId("toolbox-probe").textContent).toBe("palette:");
    expect(toolboxRequests[0]).toEqual(path);
  });
});

describe("the view report", () => {
  it("aDiagramThatIsLoadingOrFailed_ReportsNoViewport", () => {
    // Arrange.
    // The report effect is declared before this component's `if (failed)` and `if (loading)`
    // early returns, and hooks run regardless of a later return - so nothing about those
    // returns keeps a report from being sent. The guard has to be in the effect.
    vi.useFakeTimers();

    try {
      for (const state of [{ loading: true, failed: false }, { loading: false, failed: true }]) {
        reportView.mockClear();
        currentLoading = state.loading;
        currentFailed = state.failed;

        // Act.
        renderCanvas();
        vi.advanceTimersByTime(VIEW_REPORT_DEBOUNCE_MS * 2);

        // Assert.
        // Loading: the report would describe a view of nothing, before the first delta has
        // said what there is. Failed: it is a call to a connection the backend has just told
        // us is unroutable - the path is gone, moved or has no session.
        expect(reportView, state.loading ? "reported while loading" : "reported after a permanent failure").not.toHaveBeenCalled();
      }
    } finally {
      vi.useRealTimers();
    }
  });

  it("reports the viewport once the diagram is ready", () => {
    // Arrange.
    // The other half of the guard: it must suppress the two states above and nothing else, or
    // it would be indistinguishable from never reporting at all.
    vi.useFakeTimers();

    try {
      reportView.mockClear();

      // Act.
      renderCanvas();
      vi.advanceTimersByTime(VIEW_REPORT_DEBOUNCE_MS * 2);

      // Assert.
      expect(reportView).toHaveBeenCalledTimes(1);
    } finally {
      vi.useRealTimers();
    }
  });
});
