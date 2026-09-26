import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it, vi } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { DiagramCanvasCore } from "@client/canvas/library/DiagramCanvas";
import type { DiagramModel, DiagramModelConnection, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { FDG_DEFINITION } from "./FdgCanvas";
import { FdgElementTypes } from "./fdgIds";
import { pointer } from "@client/canvas/library/testing/canvasHarness";

/**
 * Task 14's connect guard, over the field-service example itself rather than a toy: a connect
 * gesture highlights an allowed target, refuses a forbidden pair, refuses a second parent, refuses
 * a cycle-closing target, and DOES offer the Shows round trip.
 *
 * Every one of these is the canvas's verdict under the pointer. The backend refuses the same links
 * on write (task 12), because a request is never trusted to have come from this canvas.
 */

/** The height every type but a Comment shares - `FdgGeometry.SharedHeight` on the backend. */
const SHARED_HEIGHT = 48;

interface ExampleEntry {
  [key: string]: string;
}

/**
 * The example's entries, read line by line. The document is flat on purpose - one `- id:` entry per
 * element or connection, scalar keys beneath it, and a Comment's text as a `|-` block - so this
 * reads exactly that shape and the counts below say whether it read all of it.
 */
function readExample(): { elements: ExampleEntry[]; connections: ExampleEntry[] } {
  const text = readFileSync(join(__dirname, "..", "examples", "field-service", "field-service.fdg"), "utf8");
  const sections: Record<string, ExampleEntry[]> = { elements: [], connections: [] };
  let section: ExampleEntry[] | null = null;
  let entry: ExampleEntry | null = null;
  let inBlock = false;

  for (const line of text.split(/\r?\n/)) {
    if (/^(elements|connections):/.test(line)) {
      section = sections[line.slice(0, line.indexOf(":"))];
      inBlock = false;
      continue;
    }
    const start = /^ {2}- id: (.+)$/.exec(line);
    if (start !== null && section !== null) {
      entry = { id: start[1] };
      section.push(entry);
      inBlock = false;
      continue;
    }
    const key = /^ {4}([a-z]+): ?(.*)$/.exec(line);
    if (key !== null && entry !== null) {
      entry[key[1]] = key[2];
      inBlock = key[2] === "|-";
      continue;
    }
    if (!inBlock || !/^ {6}/.test(line)) {
      inBlock = false;
    }
  }

  return { elements: sections.elements, connections: sections.connections };
}

const EXAMPLE = readExample();

/** The example as the library draws it: centres, sizes, and every connection. */
function exampleModel(without: readonly string[] = []): DiagramModel {
  const elements = EXAMPLE.elements.map((entry): DiagramModelElement => {
    const width = Number(entry.width);
    const height = entry.type === FdgElementTypes.comment ? Number(entry.height) : SHARED_HEIGHT;
    return {
      id: entry.id,
      type: entry.type,
      x: Number(entry.x) + width / 2,
      y: Number(entry.y) + height / 2,
      width,
      height,
      label: entry.name ?? "",
      payload: { name: entry.name ?? "", text: "" },
    };
  });
  const connections = EXAMPLE.connections
    .filter((entry) => !without.includes(entry.id))
    .map((entry): DiagramModelConnection => ({ id: entry.id, type: entry.type, sourceId: entry.from, targetId: entry.to }));
  return { elements, connections };
}

function renderExample(model: DiagramModel) {
  const onConnectionDrawn = vi.fn();
  const result = render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={FDG_DEFINITION} model={model} events={{ onConnectionDrawn }} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
  return { ...result, onConnectionDrawn, model };
}

const centreOf = (model: DiagramModel, id: string) => {
  const element = model.elements.find((candidate) => candidate.id === id)!;
  return { x: element.x, y: element.y };
};

/**
 * A right-button draw from one element's centre to another's, stopping mid-gesture so the
 * highlight can be read, and returning the release to finish it. The library maps a pixel delta to
 * canvas units at scale one here, so centres in canvas units are the pixels to press and move to.
 */
function drawBetween(rendered: ReturnType<typeof renderExample>, fromId: string, toId: string) {
  const { container, model } = rendered;
  const source = container.querySelector(`[data-element-id="${fromId}"]`)!;
  const from = centreOf(model, fromId);
  const to = centreOf(model, toId);
  fireEvent(source, pointer("pointerdown", { button: 2, clientX: from.x, clientY: from.y }));
  fireEvent(source, pointer("pointermove", { clientX: to.x, clientY: to.y }));
  const target = container.querySelector(`[data-element-id="${toId}"]`)!;
  return {
    target,
    release: () => fireEvent(source, pointer("pointerup", { button: 2, clientX: to.x, clientY: to.y })),
  };
}

describe("the field-service example, as this definition reads it", () => {
  it("is read in full, so a verdict below is about the whole example", () => {
    // Assert: 19 elements and 18 connections, counted from the document by hand.
    expect(EXAMPLE.elements).toHaveLength(19);
    expect(EXAMPLE.connections).toHaveLength(18);
    expect(EXAMPLE.elements.every((entry) => entry.type !== undefined && entry.x !== undefined && entry.width !== undefined)).toBe(true);
    expect(EXAMPLE.connections.every((entry) => entry.from !== undefined && entry.to !== undefined)).toBe(true);
  });
});

describe("a connect gesture over the field-service example", () => {
  it("highlights an allowed target: Tick step shows nothing yet, so it may show the Step list", () => {
    // Arrange.
    const rendered = renderExample(exampleModel());

    // Act.
    const { target, release } = drawBetween(rendered, "tick-step", "step-list");

    // Assert.
    expect(target.classList.contains("library-connect-target")).toBe(true);
    release();
    expect(rendered.onConnectionDrawn).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({ relationType: "shows", sourceElementId: "tick-step", targetElementId: "step-list" }),
    );
  });

  it("refuses a forbidden pair: no relation runs from an Action to an Action", () => {
    // Arrange.
    const rendered = renderExample(exampleModel());

    // Act.
    const { target, release } = drawBetween(rendered, "open-task", "tick-step");

    // Assert.
    expect(target.classList.contains("library-connect-forbidden")).toBe(true);
    release();
    expect(rendered.onConnectionDrawn).not.toHaveBeenCalled();
  });

  it("refuses a second parent: Task row already has the Task list", () => {
    // Arrange.
    const rendered = renderExample(exampleModel());

    // Act.
    const { target, release } = drawBetween(rendered, "planning", "task-row");

    // Assert.
    expect(target.classList.contains("library-connect-forbidden")).toBe(true);
    release();
    expect(rendered.onConnectionDrawn).not.toHaveBeenCalled();
  });

  it("refuses a cycle-closing target: Planning has no parent, but already owns Task row", () => {
    // Arrange: only the cycle walk can explain this refusal - the type admits UI to UI, and
    // Planning has no parent, so the cardinality check passes.
    const rendered = renderExample(exampleModel());

    // Act.
    const { target, release } = drawBetween(rendered, "task-row", "planning");

    // Assert.
    expect(target.classList.contains("library-connect-forbidden")).toBe(true);
    release();
    expect(rendered.onConnectionDrawn).not.toHaveBeenCalled();
  });

  it("offers the Shows round trip: a loop through Shows is navigation, not ownership", () => {
    // Arrange: the example without Back to list's Shows, so drawing it again closes the round trip
    // Task list -> Task row -> Open task -> Task detail -> Back to list -> Task list.
    const rendered = renderExample(exampleModel(["c-back-shows-list"]));

    // Act.
    const { target, release } = drawBetween(rendered, "back-to-list", "task-list");

    // Assert.
    expect(target.classList.contains("library-connect-target")).toBe(true);
    release();
    expect(rendered.onConnectionDrawn).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({ relationType: "shows", sourceElementId: "back-to-list", targetElementId: "task-list" }),
    );
  });
});
