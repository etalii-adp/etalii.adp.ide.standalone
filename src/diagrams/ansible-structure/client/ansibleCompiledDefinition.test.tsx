import { describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { create, toBinary } from "@bufbuild/protobuf";
import { ElementSchema } from "@client/generated/elements_pb";
import { AnsibleEdgeKind, AnsibleElementKind, AnsibleElementPayloadSchema } from "@client/generated/ansible-structure_pb";
import disText from "../definition/ansible-structure.dis?raw";
import { assertValidDiagramDefinition } from "@client/canvas/library/definition/validateDiagramDefinition";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { fakeContextConnection } from "@client/canvas/library/testing/canvasHarness";
import { applyDelta, emptyModel, type AnsibleModel } from "./ansibleModel";
import { ANSIBLE_BINDINGS } from "./ansibleBindings";
import { HAND_WRITTEN_DEFINITION, ORACLE_DASHES, handWrittenModel } from "./handWrittenDefinition";

let currentModel: AnsibleModel = emptyModel;

vi.mock("./useAnsibleStream", () => ({
  useAnsibleStream: () => ({ model: currentModel, loading: false, failed: false, moveElementTo: vi.fn(), reportView: vi.fn() }),
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
const { AnsibleCanvas, ANSIBLE_DEFINITION } = await import("./AnsibleCanvas");

/**
 * The Ansible structure diagram's canvas definition, compiled from its bundled DISL specification,
 * draws what the definition the module stated by hand until the switch-over drew.
 *
 * <b>The oracle is that hand-written definition, kept in `handWrittenDefinition.ts`</b> with the model
 * the canvas built for it. The compiled definition has a node type per kind where the oracle had one
 * `node` type told apart by a payload class, a relation type per relation where it had one `edge` told
 * apart by classes on each connection, and an unresolved target drawn by the library as its relation's
 * stub where it was a `stub` element the canvas built. So the test is the drawing: the real canvas
 * renders the corpus below, the oracle renders the same corpus through the model the canvas used to
 * build, and the markup must be the same, element for element and connection for connection.
 *
 * <b>What is compared apart, and the differences it states.</b>
 * - **The dashes.** The oracle's were the stylesheet's, by the classes `ansible-edge-static`,
 *   `ansible-edge-dynamic`, `ansible-edge-depends-on` and `ansible-edge-unresolved`; the compiled
 *   definition paints the specification's on each line. So the oracle's drawing is given the dash its
 *   stylesheet gave each line (`ORACLE_DASHES`), its two static/dynamic classes are taken off, and the
 *   two must then be the same - a dash changed in the specification fails here.
 * - **An unresolved target** is a connection now and was an element, so its markup cannot be the same:
 *   each is compared by what it draws instead - its line's two ends and dash, its classes, its text,
 *   where the text stands and how it is anchored.
 * - **Class order.** The words of every class attribute are compared as a set, because the compiled
 *   type puts the kind's and the relation's class in another order; and `ansible-play-none`, which the
 *   oracle wrote on every node of no play and no stylesheet rule reads, is gone.
 * - **`data-kind`** was written on every node and is read by no stylesheet and no test.
 * - **A node's accessible name** is DISL's default (6.15), its type's label and its name, where the
 *   oracle used the tooltip: a playbook or play with hosts no longer says them in its name, though its
 *   tooltip still does, and a variable folder is named by its type's label "Variable folder" where the
 *   tooltip says "Variables". Each name is compared as the oracle's with exactly those two changes.
 * - **The empty sentence** is the specification's `canvas.empty`, a plain text, where the oracle set the
 *   three folder names in a monospace font, which the definition's own doc says a Message cannot state.
 */

const TYPE_URL = "type.googleapis.com/etalii.adp.ansible.AnsibleElementPayload";

function node(id: string, type: string, kind: AnsibleElementKind, x: number, y: number, width: number, extra: Record<string, unknown> = {}) {
  const payload = create(AnsibleElementPayloadSchema, { name: id.split(":")[1] ?? id, kind, width, height: 32, playIndex: -1, projectRelativePath: [id], ...extra });
  return create(ElementSchema, {
    id: { value: id },
    position: { x, y },
    type: `ansible/structure+${type}`,
    payload: { typeUrl: TYPE_URL, value: toBinary(AnsibleElementPayloadSchema, payload) },
  });
}

function edge(id: string, kind: AnsibleEdgeKind, sourceId: string, targetId: string, directive: string, extra: Record<string, unknown> = {}, unresolvable = false) {
  const payload = create(AnsibleElementPayloadSchema, {
    name: id,
    kind: AnsibleElementKind.EDGE,
    playIndex: -1,
    unresolvable,
    edge: { sourceId, targetId, kind, directive, targetAsWritten: targetId === "" ? (extra.targetAsWritten as string) : targetId, ...extra },
  });
  return create(ElementSchema, {
    id: { value: id },
    position: { x: 0, y: 0 },
    type: "ansible/structure+edge",
    payload: { typeUrl: TYPE_URL, value: toBinary(AnsibleElementPayloadSchema, payload) },
  });
}

/**
 * Every case the drawing distinguishes: each node kind and a hollow role; plays of two playbooks, one
 * with hosts, their indices running past the six slots; the five relations, static and dynamic, with
 * and without a condition; and a missing target, an expression and an unresolved dependency.
 */
const CORPUS: AnsibleModel = applyDelta(emptyModel, {
  action: {
    case: "add",
    value: {
      elements: [
        node("playbook:site.yml", "playbook", AnsibleElementKind.PLAYBOOK, 0, 0, 120, { hosts: "all" }),
        node("playbook:db.yml", "playbook", AnsibleElementKind.PLAYBOOK, 0, 200, 110),
        node("play:site.yml#0", "play", AnsibleElementKind.PLAY, 200, 0, 140, { playIndex: 0, hosts: "webservers" }),
        node("play:site.yml#1", "play", AnsibleElementKind.PLAY, 200, 50, 140, { playIndex: 1 }),
        node("play:db.yml#0", "play", AnsibleElementKind.PLAY, 200, 200, 140, { playIndex: 7 }),
        node("role:nginx", "role", AnsibleElementKind.ROLE, 440, 0, 100),
        node("role:common", "role", AnsibleElementKind.ROLE, 440, 60, 100),
        node("role:empty", "role", AnsibleElementKind.ROLE, 440, 120, 100, { hollow: true }),
        node("taskfile:roles/nginx/tasks/tls.yml", "taskfile", AnsibleElementKind.TASK_FILE, 640, 0, 110),
        node("inventory:inventories/production", "inventory", AnsibleElementKind.INVENTORY, 440, 300, 130),
        node("vars:inventories/production/group_vars", "vars", AnsibleElementKind.VARIABLE_FOLDER, 640, 300, 130),
        edge("edge:imports", AnsibleEdgeKind.IMPORTS_PLAYBOOK, "playbook:site.yml", "playbook:db.yml", "import_playbook"),
        edge("edge:uses", AnsibleEdgeKind.USES_ROLE, "play:site.yml#0", "role:nginx", "roles:"),
        edge("edge:uses-when", AnsibleEdgeKind.USES_ROLE, "play:site.yml#1", "role:common", "include_role", { dynamic: true, condition: "ansible_os_family == 'Debian'" }),
        edge("edge:includes", AnsibleEdgeKind.INCLUDES_TASKS, "role:nginx", "taskfile:roles/nginx/tasks/tls.yml", "include_tasks", { dynamic: true, condition: "nginx_tls_enabled" }),
        edge("edge:depends", AnsibleEdgeKind.DEPENDS_ON, "role:nginx", "role:common", "dependencies"),
        edge("edge:targets", AnsibleEdgeKind.TARGETS, "play:db.yml#0", "inventory:inventories/production", "hosts:"),
        edge("edge:missing", AnsibleEdgeKind.USES_ROLE, "play:db.yml#0", "", "roles:", { targetAsWritten: "absent-role", missing: true }),
        edge("edge:expression", AnsibleEdgeKind.INCLUDES_TASKS, "role:common", "", "include_tasks", { targetAsWritten: "{{ task_file }}", dynamic: true }, true),
        edge("edge:missing-dependency", AnsibleEdgeKind.DEPENDS_ON, "role:empty", "", "dependencies", { targetAsWritten: "geerlingguy.java", missing: true }),
      ],
    },
  },
} as never);

/** What an unresolved target draws, read off either drawing. */
interface DrawnStub {
  id: string;
  classes: string[];
  line: number[];
  lineClasses: string[];
  dash: string;
  text: string;
  textAt: number[];
  anchor: string;
  textClasses: string[];
}

const words = (value: string | null | undefined, without: readonly string[] = []) =>
  (value ?? "").split(/\s+/).filter((word) => word !== "" && !without.includes(word)).sort();

/** The dash the oracle's stylesheet gave a line, by the classes of the group it is drawn in. */
function oracleDash(group: Element): string {
  const classes = words(group.getAttribute("class"));
  return ORACLE_DASHES.find((rule) => classes.includes(rule.className))?.dash ?? "";
}

/** Takes the oracle's stub elements out of its drawing, reading what each drew. */
function oracleStubs(container: HTMLElement): DrawnStub[] {
  return [...container.querySelectorAll("g.library-element.ansible-edge-unresolved")].map((group) => {
    const line = group.querySelector("line")!;
    const text = group.querySelector("text")!;
    const stub: DrawnStub = {
      id: group.getAttribute("data-element-id")!,
      classes: words(group.getAttribute("class"), ["library-element"]),
      line: ["x1", "y1", "x2", "y2"].map((name) => Number(line.getAttribute(name))),
      lineClasses: words(line.getAttribute("class")),
      dash: oracleDash(group),
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
    const line = group.querySelector<SVGPathElement>("path.canvas-connection-line")!;
    const text = group.querySelector("text")!;
    const stub: DrawnStub = {
      id: group.getAttribute("data-connection-id")!,
      classes: words(group.getAttribute("class"), ["canvas-connection", "library-connection", "library-connection-stub"]),
      line: (line.getAttribute("d") ?? "").replace(/[ML]/g, " ").trim().split(/\s+/).map(Number),
      lineClasses: words(line.getAttribute("class"), ["canvas-connection-line", "library-connection-line"]),
      dash: line.style.strokeDasharray,
      text: text.textContent ?? "",
      textAt: [Number(text.getAttribute("x")), Number(text.getAttribute("y"))],
      anchor: (text as SVGTextElement).style.textAnchor,
      textClasses: words(text.getAttribute("class"), ["library-connection-label"]),
    };
    group.remove();
    return stub;
  });
}

/** Takes the accessible names off the drawing's nodes, by node id. */
function names(container: HTMLElement): Record<string, string> {
  return Object.fromEntries(
    [...container.querySelectorAll("g.library-element[aria-label]")].map((group) => {
      const name = group.getAttribute("aria-label")!;
      group.removeAttribute("aria-label");
      return [group.getAttribute("data-element-id")!, name];
    }),
  );
}

/** The oracle's name as DISL's default says it: no hosts, and a variable folder by its type's label. */
const asDefault = (name: string) => name.replace(/ — hosts: .*$/, "").replace(/^Variables /, "Variable folder ");

/** Every class attribute's words in one order, and the generated ids made alike. */
function normalised(container: HTMLElement, without: readonly string[] = []): string {
  container.querySelectorAll("[class]").forEach((element) => element.setAttribute("class", words(element.getAttribute("class"), without).join(" ")));
  return container.innerHTML.replace(/«r[0-9a-z]+»|:r[0-9a-z]+:/g, "«id»");
}

/** The oracle's drawing of the corpus: the hand-written definition, in the frame the canvas draws it in. */
function oracleDrawing(): { container: HTMLElement; unmount: () => void } {
  return render(
    <div className="ansible-canvas-host canvas-host" role="application" aria-label="Ansible project structure">
      <DiagramCanvas
        definition={HAND_WRITTEN_DEFINITION}
        model={handWrittenModel(CORPUS)}
        events={{}}
        source={{ entryId: new Uint8Array(16), path: ["infrastructure.adp"] }}
        toolboxItems={emptyPalette}
        ariaLabel="Ansible project structure"
        className="ansible-canvas-viewport"
        scrollbarsClassName="ansible-scrollbars"
      />
    </div>,
  );
}

/** The canvas's own drawing of the corpus. */
function compiledDrawing(): { container: HTMLElement; unmount: () => void } {
  currentModel = CORPUS;
  return render(<AnsibleCanvas projectId={new Uint8Array(16)} entryId={new Uint8Array(16)} path={["infrastructure.adp"]} />);
}

describe("the Ansible structure diagram's compiled definition", () => {
  it("draws what the hand-written definition drew, element for element", () => {
    const oracle = oracleDrawing();
    const oracleUnresolved = oracleStubs(oracle.container);
    oracle.container.querySelectorAll("[data-kind]").forEach((element) => element.removeAttribute("data-kind"));
    oracle.container.querySelectorAll<SVGPathElement>("g.library-connection").forEach((group) => {
      const dash = oracleDash(group);
      if (dash !== "") {
        group.querySelector<SVGPathElement>("path.ansible-edge-line")!.style.strokeDasharray = dash;
      }
    });
    const oracleNames = names(oracle.container);
    const expected = normalised(oracle.container, ["ansible-play-none", "ansible-edge-static", "ansible-edge-dynamic"]);
    oracle.unmount();

    const compiled = compiledDrawing();
    const compiledUnresolved = compiledStubs(compiled.container);
    const compiledNames = names(compiled.container);
    const actual = normalised(compiled.container);
    compiled.unmount();

    // The cases the comparison would pass vacuously on: a canvas that drew nothing for either.
    for (const kind of ["playbook", "play", "role", "taskfile", "inventory", "vars", "hollow"]) {
      expect(actual, kind).toContain(`ansible-node-${kind}`);
    }
    for (const slot of [0, 1]) {
      expect(actual, `slot ${slot}`).toContain(`ansible-play-${slot}`);
    }
    for (const relation of ["uses-role", "imports-playbook", "includes-tasks", "depends-on", "targets"]) {
      expect(actual, relation).toContain(`ansible-edge-${relation}`);
    }
    // The oracle's dashes are drawn, so a dash the specification changes cannot pass unseen.
    for (const dash of ["5 4", "1 4"]) {
      expect(expected, dash).toContain(`stroke-dasharray: ${dash}`);
    }
    expect(Object.keys(oracleNames)).toHaveLength(11);
    expect(compiledNames).toEqual(Object.fromEntries(Object.entries(oracleNames).map(([id, name]) => [id, asDefault(name)])));
    expect(oracleUnresolved).toHaveLength(3);
    expect(compiledUnresolved).toEqual(oracleUnresolved);
    expect(actual).toBe(expected);
  });

  it("states per type what the hand-written definition stated once", () => {
    const types = ANSIBLE_DEFINITION.elementTypes;

    expect(types.map((type) => type.id)).toEqual(["playbook", "play", "role", "taskfile", "inventory", "variablefolder"]);
    expect(types.every((type) => type.deletable === false && type.sizing === "model" && type.accessibility?.focusable === true)).toBe(true);
    expect(ANSIBLE_DEFINITION.relationTypes.map((relation) => relation.id)).toEqual(["usesrole", "importsplaybook", "includestasks", "dependson", "targets"]);
    expect(ANSIBLE_DEFINITION.relationTypes.every((relation) => relation.stub !== undefined && Array.isArray(relation.endpoints.source.anchors) && relation.endpoints.source.anchors.length === 0)).toBe(true);
    // The reveal, on every node: Enter (the operation's shortcut), a double-click (the node's
    // `doubleClick`) and Space (gap 9).
    expect(ANSIBLE_DEFINITION.actions).toEqual([
      {
        id: "revealFile",
        invokedBy: [
          { kind: "shortcut", key: "Enter" },
          { kind: "gesture", gesture: "activate" },
          { kind: "shortcut", key: " " },
        ],
        appliesTo: [{ kind: "element" }],
      },
    ]);
    expect(ANSIBLE_DEFINITION.layout).toEqual(HAND_WRITTEN_DEFINITION.layout);
    expect(ANSIBLE_DEFINITION.dragging).toBe(HAND_WRITTEN_DEFINITION.dragging);
  });

  it("paints the play hues the specification states, mixed as it mixes them", async () => {
    // The stylesheet still paints a play, by the class the library chooses by its index; its hues
    // must be the specification's tokens in both modes, and its 18 % the specification's mix of 0.82.
    const { readFileSync } = await import("node:fs");
    const { resolve } = await import("node:path");
    const css = readFileSync(resolve(process.cwd(), "../diagrams/ansible-structure/client/ansible-structure.css"), "utf8").replace(/\/\*[\s\S]*?\*\//g, " ");
    const theme = (JSON.parse(disText) as { notation: { theme: { tokens: Record<string, string>; modes: { dark: Record<string, string> } } } }).notation.theme;
    const hues = (block: string) => Object.fromEntries([...block.matchAll(/--ansible-play-hue-(\d):\s*(#[0-9a-fA-F]{6})/g)].map((match) => [`color.play.hue${match[1]}`, match[2]!.toUpperCase()]));
    const [light, dark] = css.split("@media (prefers-color-scheme: dark)");
    const stated = (tokens: Record<string, string>) => Object.fromEntries(Object.entries(tokens).filter(([name]) => name.startsWith("color.play.hue")));

    expect(Object.keys(stated(theme.tokens))).toHaveLength(6);
    expect(hues(light!)).toEqual(stated(theme.tokens));
    expect(hues(dark!)).toEqual(stated(theme.modes.dark));
    const mixes = [...css.matchAll(/color-mix\(in srgb, var\(--ansible-play-hue-\d\) (\d+)%, var\(--color-surface-raised\)\)/g)].map((match) => Number(match[1]));
    expect(mixes).toEqual([18, 18, 18, 18, 18, 18]);
    expect(disText).toContain("mix(token('color.surface.raised'), 0.82)");
  });

  it("is what the canvas draws: the module's definition is compiled from the bundled specification", () => {
    // The canary for the tests above: were the canvas still holding its own definition, they
    // would compare the oracle with a copy of itself.
    expect(ANSIBLE_DEFINITION).toEqual(assertValidDiagramDefinition(compileNotation(parseDisl(disText), ANSIBLE_BINDINGS)));
  });
});
