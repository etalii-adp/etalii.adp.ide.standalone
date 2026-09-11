import { afterEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { ContextActionGroupSchema, ContextActionSchema, type ContextActionGroup } from "@client/generated/context-contract_pb";
import { ContextSelectionAction, type ContextSelection } from "@client/generated/context_pb";
import { elementSelectionOf, selectedElementIdOf } from "@client/canvas/selection";
import { DiagramCanvas } from "./DiagramCanvas";
import { resolveSelection } from "./librarySelection";
import type { DiagramDefinition } from "./definition/diagramDefinition";
import type { ActionDeclaration } from "./definition/actions";
import type { DiagramModel } from "./api/diagramModel";
import type { DiagramEventHandlers } from "./api/diagramEvents";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";

/**
 * The selection a canvas owns once it passes `source` (centralized-selection tasks 2 and 3).
 *
 * The context channel is faked the way the module tests fake it, with one difference that is the
 * point: `innermostKey` is the REAL one and the pushed selection is a REAL chain built by
 * `elementSelectionOf`, so resolution runs exactly as it will against the backend.
 */

const channel = vi.hoisted(() => ({
  pushed: null as unknown,
  actions: [] as unknown[],
  pushes: [] as unknown[],
  executed: [] as { actionId: string; source?: unknown }[],
  outcome: { accepted: true, error: "" },
}));

vi.mock("@client/shell/context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@client/shell/context/ContextConnectionProvider")>();
  return {
    ...actual,
    useContextConnection: () => ({
      select: (selection: unknown) => channel.pushes.push(selection),
      executeAction: (actionId: string, source?: unknown) => {
        channel.executed.push({ actionId, source });
        return Promise.resolve(channel.outcome);
      },
    }),
    useContextSelection: () => ({ selection: channel.pushed, levels: [], actions: channel.actions }),
  };
});

SVGElement.prototype.setPointerCapture ??= () => {};
SVGElement.prototype.releasePointerCapture ??= () => {};

const ENTRY = new Uint8Array([1, 2, 3]);
const PATH = ["diagrams", "system.adp"];

afterEach(() => {
  channel.pushed = null;
  channel.actions = [];
  channel.pushes = [];
  channel.executed = [];
  channel.outcome = { accepted: true, error: "" };
});

function definitionOf(overrides: { boundarySelectable?: boolean; noteSelectable?: boolean; actions?: ActionDeclaration[]; backgroundMenu?: boolean } = {}): DiagramDefinition {
  return {
    elementTypes: [
      { id: "service", shape: "box", anchors: { kind: "compass", positions: ["e", "w"] }, sizing: "model" },
      { id: "boundary", shape: "box", anchors: { kind: "edge" }, sizing: "model", selectable: overrides.boundarySelectable },
    ],
    relationTypes: [
      { id: "calls", route: "straight", endpoints: { source: { elementTypes: ["service"] }, target: { elementTypes: ["service"] }, allowSelf: false } },
      {
        id: "note",
        route: "straight",
        endpoints: { source: { elementTypes: ["service"] }, target: { elementTypes: ["service"] }, allowSelf: false },
        selectable: overrides.noteSelectable,
      },
    ],
    layout: { modes: ["manual"] },
    dragging: "enabled",
    actions: overrides.actions,
    backgroundMenu: overrides.backgroundMenu,
  };
}

function modelOf(withoutB = false): DiagramModel {
  const elements = [
    { id: "a", type: "service", x: 0, y: 0, width: 100, height: 40, label: "Alpha" },
    { id: "b", type: "service", x: 300, y: 0, width: 100, height: 40, label: "Beta" },
    { id: "zone", type: "boundary", x: 150, y: 300, width: 100, height: 60, label: "Zone" },
  ];
  return {
    elements: withoutB ? elements.filter((element) => element.id !== "b") : elements,
    connections: withoutB
      ? []
      : [
          { id: "a->b", type: "calls", sourceId: "a", targetId: "b" },
          { id: "b~a", type: "note", sourceId: "b", targetId: "a" },
        ],
  };
}

function mount(events: DiagramEventHandlers = {}, definition = definitionOf(), model = modelOf(), extra: Partial<React.ComponentProps<typeof DiagramCanvas>> = {}) {
  const tree = (m: DiagramModel) => (
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvas definition={definition} model={m} events={events} source={{ entryId: ENTRY, path: PATH }} {...extra} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>
  );
  const view = render(tree(model));
  return { ...view, redraw: (m: DiagramModel) => view.rerender(tree(m)) };
}

function pointer(type: string, init: MouseEventInit) {
  return new MouseEvent(type, { bubbles: true, cancelable: true, ...init });
}

function press(target: Element, init: MouseEventInit = {}) {
  fireEvent(target, pointer("pointerdown", { button: 0, ...init }));
  fireEvent(target, pointer("pointerup", { ...init }));
}

const elementOn = (container: HTMLElement, id: string) => container.querySelector(`[data-element-id="${id}"]`)!;
const connectionOn = (container: HTMLElement, id: string) => container.querySelector(`[data-connection-id="${id}"]`)!;
const surfaceOf = (container: HTMLElement) => container.querySelector("svg.library-canvas-surface")!;
const highlighted = (container: HTMLElement) =>
  [...container.querySelectorAll(".canvas-selected")].map((node) => node.getAttribute("data-element-id") ?? node.getAttribute("data-connection-id"));

/** What a push asked for: the id it names (or null for a clear) and whether it asked for the menu. */
function asked(push: unknown): { id: string | null; menu: boolean } {
  if (push === null) {
    return { id: null, menu: false };
  }
  const chain = push as ContextSelection;
  const inner = chain.detail.case === "child" ? chain.detail.value : chain;
  return { id: selectedElementIdOf(chain) ?? null, menu: inner.detail.case === "action" && inner.detail.value === ContextSelectionAction.CONTEXT_MENU };
}

describe("a pushed selection is resolved by the library", () => {
  it("highlights the element it names", () => {
    channel.pushed = elementSelectionOf(ENTRY, PATH, "a");
    const { container } = mount();

    expect(highlighted(container)).toEqual(["a"]);
  });

  it("highlights the connection it names, as a connection", () => {
    // The rule every working module wrote by hand: an id naming a connection is a connection.
    channel.pushed = elementSelectionOf(ENTRY, PATH, "a->b");
    const { container } = mount();

    expect(highlighted(container)).toEqual(["a->b"]);
    expect(connectionOn(container, "a->b").classList.contains("canvas-selected")).toBe(true);
  });

  it("resolves an id this model does not have to nothing", () => {
    // Asked of the resolver, not of the drawing: an unknown id can never be DRAWN highlighted,
    // so a canvas-level check here would pass whatever the resolver said. What a wrong answer
    // would break is everything that reads the selection - a delete, a declared shortcut.
    expect(resolveSelection("somewhere-else", modelOf(), definitionOf())).toEqual([]);
    expect(resolveSelection(null, modelOf(), definitionOf())).toEqual([]);
  });

  it("highlights nothing for an item whose type is declared unselectable", () => {
    channel.pushed = elementSelectionOf(ENTRY, PATH, "zone");
    const first = mount({}, definitionOf({ boundarySelectable: false }));
    expect(highlighted(first.container)).toEqual([]);
    first.unmount();

    channel.pushed = elementSelectionOf(ENTRY, PATH, "b~a");
    const second = mount({}, definitionOf({ noteSelectable: false }));
    expect(highlighted(second.container)).toEqual([]);
  });

  it("highlights a type that says nothing about selectability - omitted means selectable", () => {
    channel.pushed = elementSelectionOf(ENTRY, PATH, "zone");
    const { container } = mount();

    expect(highlighted(container)).toEqual(["zone"]);
  });
});

describe("a press is pushed by the library", () => {
  it("pushes an element, a connection under its own id, and null for the background", () => {
    const { container } = mount();

    press(elementOn(container, "a"), { clientX: 10, clientY: 10 });
    press(connectionOn(container, "a->b"));
    press(surfaceOf(container), { clientX: 400, clientY: 500 });

    expect(channel.pushes.map(asked)).toEqual([
      { id: "a", menu: false },
      { id: "a->b", menu: false },
      { id: null, menu: false },
    ]);
  });

  it("pushes a clear for a press on an unselectable element or connection, as the background does", () => {
    const { container } = mount({}, definitionOf({ boundarySelectable: false, noteSelectable: false }));

    press(elementOn(container, "zone"));
    press(connectionOn(container, "b~a"));

    expect(channel.pushes.map(asked)).toEqual([
      { id: null, menu: false },
      { id: null, menu: false },
    ]);
  });

  it("pushes nothing for a drag - moving an element does not select it", () => {
    const onElementMoved = vi.fn();
    const { container } = mount({ onElementMoved });
    const target = elementOn(container, "a");

    fireEvent(target, pointer("pointerdown", { button: 0, clientX: 0, clientY: 0 }));
    fireEvent(target, pointer("pointermove", { clientX: 60, clientY: 40 }));
    fireEvent(target, pointer("pointerup", { clientX: 60, clientY: 40 }));

    expect(onElementMoved, "the drag never happened, so this test cannot say anything").toHaveBeenCalledOnce();
    expect(channel.pushes).toEqual([]);
  });

  it("still hands the module every other event it handles", () => {
    const onElementMoved = vi.fn();
    const { container } = mount({ onElementMoved });
    const target = elementOn(container, "b");

    fireEvent(target, pointer("pointerdown", { button: 0, clientX: 0, clientY: 0 }));
    fireEvent(target, pointer("pointermove", { clientX: 30, clientY: 0 }));
    fireEvent(target, pointer("pointerup", { clientX: 30, clientY: 0 }));

    expect(onElementMoved).toHaveBeenCalledWith(expect.objectContaining({ kind: "element-moved", elementId: "b" }));
  });
});

describe("a selection that vanishes", () => {
  it("clears once when the selected item leaves the model", () => {
    channel.pushed = elementSelectionOf(ENTRY, PATH, "b");
    const { container, redraw } = mount();
    expect(highlighted(container)).toEqual(["b"]);

    redraw(modelOf(true)); // an edit removed b; the backend still names it
    redraw(modelOf(true));
    expect(channel.pushes.map(asked)).toEqual([{ id: null, menu: false }]);

    // The clear is spent once pushed. The backend answers it; later another canvas selects a
    // "b" of its own - which is not this canvas's to clear.
    channel.pushed = null;
    redraw(modelOf(true));
    channel.pushed = elementSelectionOf(ENTRY, PATH, "b");
    redraw(modelOf(true));

    expect(channel.pushes.map(asked)).toEqual([{ id: null, menu: false }]);
  });

  it("is not cleared when this canvas never had it - it is another canvas's, or not here yet", () => {
    channel.pushed = elementSelectionOf(ENTRY, PATH, "on-another-tab");
    const { redraw } = mount();

    redraw(modelOf(true));
    redraw(modelOf());

    expect(channel.pushes).toEqual([]);
  });
});

describe("a canvas still wiring selection itself", () => {
  it("behaves exactly as before: the module hears the press and the library pushes nothing", () => {
    const onSelectionChanged = vi.fn();
    const { container } = mount({ onSelectionChanged });

    press(elementOn(container, "a"), { clientX: 10, clientY: 10 });

    expect(onSelectionChanged).toHaveBeenCalledWith({ kind: "selection-changed", selection: [{ kind: "element", id: "a" }] });
    expect(channel.pushes).toEqual([]);
  });
});

function actionsOf(...ids: string[]): ContextActionGroup[] {
  return [create(ContextActionGroupSchema, { actions: ids.map((id) => create(ContextActionSchema, { id, label: `Do ${id}`, available: true })) })];
}

describe("the shared context menu, owned by the library", () => {
  it("asks for an element's menu with the context-menu gesture", () => {
    const { container } = mount();

    fireEvent.contextMenu(elementOn(container, "a"), { clientX: 10, clientY: 10 });

    expect(channel.pushes.map(asked)).toEqual([{ id: "a", menu: true }]);
  });

  it("shows the pushed actions and runs one against the backend's current selection", async () => {
    channel.pushed = elementSelectionOf(ENTRY, PATH, "a");
    channel.actions = actionsOf("service.restart");
    const { container } = mount();

    fireEvent.contextMenu(elementOn(container, "a"), { clientX: 10, clientY: 10 });
    fireEvent.click(await screen.findByRole("menuitem", { name: /Do service.restart/ }));

    // No source of its own: the menu opened only once the backend's selection was "a", and the
    // backend runs a sourceless action against exactly that selection (ContextService.Actions,
    // TryResolveTargetAsync). A source naming "a" again would add nothing.
    expect(channel.executed).toEqual([{ actionId: "service.restart", source: undefined }]);
  });

  it("offers no entry until the backend's selection names the item the menu was opened on", async () => {
    // The property that lets an entry run with no source of its own: "the current selection"
    // is, by the time any entry can be chosen, the item the menu was opened on. Actions pushed
    // for some other selection are on hand, and still nothing is offered for "a" until the
    // backend's selection names it.
    channel.pushed = elementSelectionOf(ENTRY, PATH, "b");
    channel.actions = actionsOf("service.restart");
    const { container, redraw } = mount();

    fireEvent.contextMenu(elementOn(container, "a"), { clientX: 10, clientY: 10 });
    expect(screen.queryByRole("menuitem"), "an entry was offered before the backend answered for this item").toBeNull();
    expect(channel.pushes.map(asked)).toEqual([{ id: "a", menu: true }]);

    channel.pushed = elementSelectionOf(ENTRY, PATH, "a"); // the backend's answer to that push
    redraw(modelOf());
    fireEvent.click(await screen.findByRole("menuitem", { name: /Do service.restart/ }));

    expect(channel.executed).toEqual([{ actionId: "service.restart", source: undefined }]);
  });

  it("hands a refused action to the module as action-refused, with the backend's message", async () => {
    channel.pushed = elementSelectionOf(ENTRY, PATH, "a");
    channel.actions = actionsOf("service.restart");
    channel.outcome = { accepted: false, error: "The service is read-only." };
    const onActionRefused = vi.fn();
    const { container } = mount({ onActionRefused });

    fireEvent.contextMenu(elementOn(container, "a"), { clientX: 10, clientY: 10 });
    fireEvent.click(await screen.findByRole("menuitem", { name: /Do service.restart/ }));

    await vi.waitFor(() =>
      expect(onActionRefused).toHaveBeenCalledWith({ kind: "action-refused", actionId: "service.restart", message: "The service is read-only." }),
    );
  });
});

describe("a menu entry the module runs itself", () => {
  const simulate: ActionDeclaration = { id: "service.simulate", invokedBy: [{ kind: "menu" }], appliesTo: [{ kind: "element" }] };

  async function choose(container: HTMLElement, label: RegExp) {
    fireEvent.contextMenu(elementOn(container, "a"), { clientX: 10, clientY: 10 });
    fireEvent.click(await screen.findByRole("menuitem", { name: label }));
  }

  it("is raised to the module as action-invoked and never sent to the backend", async () => {
    // databricks' simulated runs: the backend offers the entry, and must never run it.
    channel.pushed = elementSelectionOf(ENTRY, PATH, "a");
    channel.actions = actionsOf("service.simulate");
    const onActionInvoked = vi.fn();
    const { container } = mount({ onActionInvoked }, definitionOf({ actions: [simulate] }));

    await choose(container, /Do service.simulate/);

    expect(onActionInvoked).toHaveBeenCalledWith({ kind: "action-invoked", actionId: "service.simulate", targetKind: "element", targetId: "a" });
    expect(channel.executed).toEqual([]);
  });

  it("leaves every entry the definition does not claim to the backend", async () => {
    channel.pushed = elementSelectionOf(ENTRY, PATH, "a");
    channel.actions = actionsOf("service.restart");
    const onActionInvoked = vi.fn();
    const { container } = mount({ onActionInvoked }, definitionOf({ actions: [simulate] }));

    await choose(container, /Do service.restart/);

    expect(onActionInvoked).not.toHaveBeenCalled();
    expect(channel.executed).toEqual([{ actionId: "service.restart", source: undefined }]);
  });

  it("claims an entry only while its declaration is enabled", async () => {
    channel.pushed = elementSelectionOf(ENTRY, PATH, "a");
    channel.actions = actionsOf("service.simulate");
    const onActionInvoked = vi.fn();
    const { container } = mount({ onActionInvoked }, definitionOf({ actions: [{ ...simulate, enabled: false }] }));

    await choose(container, /Do service.simulate/);

    expect(onActionInvoked).not.toHaveBeenCalled();
    expect(channel.executed).toEqual([{ actionId: "service.simulate", source: undefined }]);
  });

  it("is claimed only by a menu invocation - the same id bound to a key alone stays the backend's", async () => {
    // Several modules declare a `rename` for F2; a backend menu entry of the same id must still
    // run on the backend, or every such menu would silently stop working.
    channel.pushed = elementSelectionOf(ENTRY, PATH, "a");
    channel.actions = actionsOf("service.simulate");
    const onActionInvoked = vi.fn();
    const { container } = mount({ onActionInvoked }, definitionOf({ actions: [{ ...simulate, invokedBy: [{ kind: "shortcut", key: "F2" }] }] }));

    await choose(container, /Do service.simulate/);

    expect(onActionInvoked).not.toHaveBeenCalled();
    expect(channel.executed).toEqual([{ actionId: "service.simulate", source: undefined }]);
  });

  it("does not second-guess where the backend offered the entry - appliesTo is not checked again", async () => {
    // The backend's list already decided the entry belongs here; a declaration aimed at
    // connections still claims it when the backend offers it on an element.
    channel.pushed = elementSelectionOf(ENTRY, PATH, "a");
    channel.actions = actionsOf("service.simulate");
    const onActionInvoked = vi.fn();
    const { container } = mount({ onActionInvoked }, definitionOf({ actions: [{ ...simulate, appliesTo: [{ kind: "connection" }] }] }));

    await choose(container, /Do service.simulate/);

    expect(onActionInvoked).toHaveBeenCalledOnce();
    expect(channel.executed).toEqual([]);
  });
});

describe("the background menu, where a definition declares one", () => {
  it("asks for the menu at the point clicked, opens on the backend's answer, and runs the entry there", async () => {
    // causal-loop's diagram menu, hand-built until now: the placement is new:x,y in canvas
    // coordinates, the convention drops already use.
    channel.actions = actionsOf("diagram.arrange");
    const { container, redraw } = mount({}, definitionOf({ backgroundMenu: true }));

    fireEvent.contextMenu(surfaceOf(container), { clientX: 400, clientY: 500 });
    const pushed = channel.pushes.map(asked);
    expect(pushed).toHaveLength(1);
    expect(pushed[0].menu).toBe(true);
    expect(pushed[0].id).toMatch(/^new:-?[\d.]+,-?[\d.]+$/);
    expect(screen.queryByRole("menuitem"), "the menu opened before the backend answered for the point").toBeNull();

    channel.pushed = elementSelectionOf(ENTRY, PATH, pushed[0].id!);
    redraw(modelOf());
    fireEvent.click(await screen.findByRole("menuitem", { name: /Do diagram.arrange/ }));

    expect(channel.executed).toEqual([{ actionId: "diagram.arrange", source: undefined }]);
  });

  it("is not there on a canvas that does not declare it - a background right-click does nothing", () => {
    channel.actions = actionsOf("diagram.arrange");
    const { container } = mount();

    fireEvent.contextMenu(surfaceOf(container), { clientX: 400, clientY: 500 });

    expect(channel.pushes).toEqual([]);
    expect(screen.queryByRole("menuitem")).toBeNull();
  });

  it("never turns a right-click on an item into a background placement, even one the item does not answer with a menu", () => {
    // An item whose right-click is a declared gesture only prevents the browser menu; the event
    // still bubbles to the surface, and must not be taken for a click on empty canvas there.
    const inspect: ActionDeclaration = { id: "service.inspect", invokedBy: [{ kind: "gesture", gesture: "context-menu" }], appliesTo: [{ kind: "element" }] };
    const onActionInvoked = vi.fn();
    const { container } = mount({ onActionInvoked }, definitionOf({ backgroundMenu: true, actions: [inspect] }));

    fireEvent.contextMenu(elementOn(container, "a"), { clientX: 10, clientY: 10 });

    expect(onActionInvoked, "the declared gesture never ran, so this test cannot say anything").toHaveBeenCalledOnce();
    expect(channel.pushes).toEqual([]);
  });

  it("leaves a right-click on an item to that item's own menu", () => {
    const { container } = mount({}, definitionOf({ backgroundMenu: true }));

    fireEvent.contextMenu(elementOn(container, "a"), { clientX: 10, clientY: 10 });

    expect(channel.pushes.map(asked)).toEqual([{ id: "a", menu: true }]);
  });
});
