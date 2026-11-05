import { readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { DiagramCanvasCore } from "@client/canvas/library/DiagramCanvas";
import type { DiagramDefinition } from "@client/canvas/library/definition/diagramDefinition";
import type { DiagramModel, DiagramModelElement } from "@client/canvas/library/api/diagramModel";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { FDG_DEFINITION } from "./FdgCanvas";
import { HAND_WRITTEN_FDG_DEFINITION } from "./handWrittenDefinition";
import { FdgElementTypes } from "./fdgIds";

/**
 * The functional decomposition graph's canvas definition, compiled from its bundled DISL specification,
 * is the definition the module stated by hand until the switch-over (functional-decomposition-graph-disl-fbl
 * task 8, Requirement 7.2): the same pairs, limits and cycle rule, every other key the same, and the same
 * markup for the field-service example.
 *
 * <b>Two differences are named, not hidden.</b> The actions are declared in the order the definition's
 * menus list them, which changes nothing the canvas does with them. The compiled canvas opens the
 * definition's "Add … here" menu on a right-click on empty canvas (`backgroundMenu`), which the hand-written
 * one did not; the user ruled that it is offered (chat ruling of 2026-10-24), and it is the one key compared apart.
 */

/** The height every type but a Comment shares - `FdgGeometry.SharedHeight` on the backend. */
const SHARED_HEIGHT = 48;

/** A definition with its actions in one order, for comparing what they are rather than how they are listed. */
function comparable(definition: DiagramDefinition): DiagramDefinition {
  const { backgroundMenu: _backgroundMenu, ...rest } = definition;
  return { ...rest, actions: [...(definition.actions ?? [])].sort((one, other) => one.id.localeCompare(other.id)) };
}

/** The field-service example as the library draws it: centres, sizes, labels, a Comment's text, and every connection. */
function exampleModel(): DiagramModel {
  const text = readFileSync(join(__dirname, "..", "examples", "field-service", "field-service.fdg"), "utf8");
  const entries: { section: string; keys: Record<string, string> }[] = [];
  let section = "";
  let block: { keys: Record<string, string>; key: string } | null = null;
  for (const line of text.split(/\r?\n/)) {
    const header = /^(elements|connections):/.exec(line);
    if (header !== null) {
      section = header[1]!;
      block = null;
      continue;
    }
    const start = /^ {2}- id: (.+)$/.exec(line);
    if (start !== null) {
      entries.push({ section, keys: { id: start[1]! } });
      block = null;
      continue;
    }
    const current = entries[entries.length - 1];
    if (block !== null && /^ {6}/.test(line)) {
      block.keys[block.key] = block.keys[block.key] === "" ? line.slice(6) : `${block.keys[block.key]}\n${line.slice(6)}`;
      continue;
    }
    const key = /^ {4}([a-z]+): ?(.*)$/.exec(line);
    if (key !== null && current !== undefined) {
      current.keys[key[1]!] = key[2] === "|-" ? "" : key[2]!;
      block = key[2] === "|-" ? { keys: current.keys, key: key[1]! } : null;
    }
  }

  const elements = entries.filter((entry) => entry.section === "elements").map(({ keys }): DiagramModelElement => {
    const width = Number(keys.width);
    const comment = keys.type === FdgElementTypes.comment;
    const height = comment ? Number(keys.height) : SHARED_HEIGHT;
    return {
      id: keys.id!,
      type: keys.type!,
      x: Number(keys.x) + width / 2,
      y: Number(keys.y) + height / 2,
      width,
      height,
      label: comment ? (keys.text ?? "") : (keys.name ?? ""),
      payload: { name: keys.name ?? "", text: keys.text ?? "" },
    };
  });
  const connections = entries.filter((entry) => entry.section === "connections")
    .map(({ keys }) => ({ id: keys.id!, type: keys.type!, sourceId: keys.from!, targetId: keys.to!, label: keys.name || undefined }));
  return { elements, connections };
}

function markupOf(definition: DiagramDefinition): string {
  const { container, unmount } = render(
    <DiagramViewProvider>
      <DiagramToolboxProvider>
        <DiagramCanvasCore definition={definition} model={exampleModel()} events={{}} />
      </DiagramToolboxProvider>
    </DiagramViewProvider>,
  );
  const markup = container.innerHTML;
  unmount();
  return markup;
}

describe("the compiled functional decomposition graph definition", () => {
  it("allows the same pairs, the same limits and the same cycle rule as the hand-written one", () => {
    // Assert.
    expect(FDG_DEFINITION.relationTypes.map((relation) => [relation.id, relation.endpoints]))
      .toEqual(HAND_WRITTEN_FDG_DEFINITION.relationTypes.map((relation) => [relation.id, relation.endpoints]));
    expect(FDG_DEFINITION.acyclic).toEqual(HAND_WRITTEN_FDG_DEFINITION.acyclic);
  });

  it("is the hand-written one in every other key, the order of its actions aside", () => {
    // Assert.
    expect(comparable(FDG_DEFINITION)).toEqual(comparable(HAND_WRITTEN_FDG_DEFINITION));
  });

  it("opens the definition's menu on empty canvas, which the hand-written one did not", () => {
    // Assert.
    expect(FDG_DEFINITION.backgroundMenu).toBe(true);
    expect(HAND_WRITTEN_FDG_DEFINITION.backgroundMenu).toBeUndefined();
  });

  it("draws the same markup for the field-service example", () => {
    // Arrange.
    const model = exampleModel();
    expect(model.elements).toHaveLength(19);
    expect(model.elements.filter((element) => element.type === FdgElementTypes.comment).every((element) => element.label !== "")).toBe(true);

    // Act & assert.
    expect(markupOf(FDG_DEFINITION)).toBe(markupOf(HAND_WRITTEN_FDG_DEFINITION));
  });
});
