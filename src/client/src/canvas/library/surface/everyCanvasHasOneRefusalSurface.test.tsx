import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { fileURLToPath } from "node:url";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, render } from "@testing-library/react";
import { Code, ConnectError } from "@connectrpc/connect";
import { CanvasFrame } from "./CanvasFrame";
import { useCanvasRefusalReporter, type CanvasRefusalReporter } from "./canvasRefusals";

/**
 * EVERY REGISTERED CANVAS SHOWS A REFUSAL AND ITS STATUS IN ONE PLACE, AND IT IS THE LIBRARY'S
 * (client-centralization Requirement 2.4, and task 3's four conditions).
 *
 * Mounted, over every registration the shell's registry holds - diagram modules and editors alike -
 * because what a module renders cannot be read reliably from its source. For each canvas it pushes a
 * refusal and fails if:
 *   1. no message appears;
 *   2. more than one surface shows it - a module ADDING to the library's line;
 *   3. a module declares a rejection or status element of its own;
 *   4. the surface that appears is not the library's - the case a count cannot see, since three
 *      canvases (dotnet-dependency-graph, helm-charts, wardley-map) REPLACED the shared line with a
 *      class of their own, so one surface appeared, the count was right, and it was the wrong one.
 * The same is asserted of the status, opening and unavailable.
 *
 * <b>The source half</b> exists because a mounted guard sees only what renders: a module's banner
 * drawn only once ITS OWN state holds a refusal is absent from the DOM at rest, whatever is pushed
 * through the library. So every module client source is also read for a rejection or status class,
 * with a positive control on the library's own frame and a planted canary.
 */

// ---- a scripted transport: the diagram stream parks or fails; every other call answers at once ----

let openStream: () => AsyncIterable<unknown> = () => parked;
const parked: AsyncIterable<never> = { [Symbol.asyncIterator]: () => ({ next: () => new Promise<never>(() => {}) }) };
const failing = (error: Error): AsyncIterable<never> => ({ [Symbol.asyncIterator]: () => ({ next: () => Promise.reject(error) }) });

vi.mock("@connectrpc/connect", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@connectrpc/connect")>();
  return {
    ...actual,
    createClient: () =>
      new Proxy(
        {},
        {
          get: (_target, property) => {
            if (property === "open") {
              return () => openStream();
            }
            if (property === "watch") {
              return () => parked;
            }
            return () => Promise.resolve({ accepted: true, error: "", items: [] });
          },
        },
      ),
  };
});

vi.mock("@client/auth/AuthContext", () => {
  const transport = {};
  return { useAuth: () => ({ transport }) };
});

const watchId = new Uint8Array(16);
vi.mock("@client/shell/context/ContextConnectionProvider", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@client/shell/context/ContextConnectionProvider")>();
  const accepted = () => Promise.resolve({ accepted: true, error: "" });
  return {
    ...actual,
    useContextConnection: () => ({
      watchId,
      select: () => {},
      executeAction: accepted,
      executeShortcut: accepted,
      setProperty: accepted,
      describeProperties: () => Promise.resolve([]),
      clearReveal: () => {},
      revealPath: () => {},
    }),
    useContextPrompt: () => ({ prompt: null, onPropose: vi.fn(), onSubmit: vi.fn(), onCancel: vi.fn() }),
    useContextSelection: () => ({ selection: null, levels: [], actions: [] }),
    useContextProblems: () => null,
    useProjectActions: () => [],
    useContextNotices: () => ({ notices: [], dismiss: () => {} }),
  };
});

vi.mock("@client/shell/panels/useToolboxItems", () => ({ useToolboxItems: () => [] }));

const { diagramCanvases } = await import("@client/shell/panels/diagramCanvases");

const registered = diagramCanvases
  .filter((registration) => registration.Canvas !== undefined)
  .map((registration, index) => [`${index}: ${registration.Canvas!.name || "anonymous canvas"}`, registration.Canvas!] as const);

// ---- what counts as a surface ----

/** A class a module would use for a rejection or status element of its own. */
const SURFACE_CLASS = /(^|-)(rejection|loading|unavailable)$|^canvas-(status|rejection)$/;

/** Every element wearing a surface class that is NOT one of the library's two lines. */
function moduleSurfaces(container: HTMLElement): string[] {
  return [...container.querySelectorAll("*")]
    .filter((element) => !element.hasAttribute("data-canvas-surface"))
    .flatMap((element) => [...element.classList].filter((token) => SURFACE_CLASS.test(token)));
}

/** Elements whose own text is exactly this sentence - the surfaces showing it. */
function showing(container: HTMLElement, sentence: string): Element[] {
  return [...container.querySelectorAll("*")].filter(
    (element) => element.textContent === sentence && [...element.children].every((child) => child.textContent !== sentence),
  );
}

let push: CanvasRefusalReporter | null = null;
function Pusher() {
  push = useCanvasRefusalReporter();
  return null;
}

const props = { projectId: new Uint8Array([1]), entryId: new Uint8Array([2]), path: ["guard", "example.adp"] };

function mount(Canvas: (typeof registered)[number][1]) {
  return render(
    <CanvasFrame>
      <Canvas {...props} />
      <Pusher />
    </CanvasFrame>,
  );
}

const settle = () => act(async () => {
  await new Promise((resolve) => setTimeout(resolve, 0));
});

describe("every registered canvas has one refusal surface and one status, and both are the library's", () => {
  beforeEach(() => {
    openStream = () => parked;
    push = null;
  });

  afterEach(() => {
    cleanup();
  });

  it("finds the registrations it walks", () => {
    // The completeness floor: an empty walk would pass every check below. Seventeen diagram canvases
    // and two editors are registered at the time of writing - a floor well under that still fails
    // on a registry that stopped resolving.
    expect(registered.length).toBeGreaterThanOrEqual(15);
  });

  it.each(registered)("%s: a pushed refusal appears once, on the library's line, and on no line of its own", async (_name, Canvas) => {
    // Arrange.
    const { container } = mount(Canvas);
    await settle();

    // Act.
    act(() => push!.refused("PLANTED REFUSAL"));

    // Assert.
    const shown = showing(container, "PLANTED REFUSAL");
    expect(shown, "no surface shows the refusal").not.toHaveLength(0);
    expect(shown, "more than one surface shows the refusal").toHaveLength(1);
    expect(shown[0].getAttribute("data-canvas-surface"), "the surface showing it is not the library's").toBe("refusal");
    expect(moduleSurfaces(container), "the module declares a rejection or status element of its own").toEqual([]);
  });

  it.each(registered)("%s: while opening, the library says so once and the module says nothing", async (_name, Canvas) => {
    // Arrange: the diagram stream never answers.
    const { container } = mount(Canvas);

    // Act.
    await settle();

    // Assert.
    const shown = showing(container, "Opening…");
    expect(shown).toHaveLength(1);
    expect(shown[0].getAttribute("data-canvas-surface")).toBe("status");
    expect(moduleSurfaces(container)).toEqual([]);
  });

  it.each(registered)("%s: when unavailable, the library says so once, in the backend's words", async (_name, Canvas) => {
    // Arrange: the backend answers permanently.
    openStream = () => failing(new ConnectError("PLANTED UNAVAILABLE", Code.NotFound));

    // Act.
    const { container } = mount(Canvas);
    await settle();

    // Assert.
    const shown = showing(container, "PLANTED UNAVAILABLE");
    expect(shown).toHaveLength(1);
    expect(shown[0].getAttribute("data-canvas-surface")).toBe("status");
    expect(container.textContent).not.toMatch(/could not be opened|no longer available|cannot be opened as text/);
    expect(moduleSurfaces(container)).toEqual([]);
  });
});

// ---- the source half ----

/**
 * A class token in a string, template or className - the text a module would declare a surface with.
 * A surface class always carries a prefix (`wardley-rejection`, `pipeline-canvas-loading`), so at
 * least one hyphenated segment is required: without it the bare word matched any sentence in a string
 * that happened to say "rejection" or "loading" - which the canary below caught on its first run.
 */
const DECLARES_SURFACE = /["'`\s]([a-z0-9]+-)+(rejection|loading|unavailable)["'`\s]|["'`\s]canvas-(status|rejection)["'`\s]/;

function withoutComments(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, " ").replace(/(^|[^:])\/\/[^\n]*/g, "$1 ");
}

function sourceRoot(): string {
  let directory = dirname(fileURLToPath(import.meta.url));
  for (let depth = 0; depth < 12; depth++) {
    const hasModules = statSync(join(directory, "diagrams"), { throwIfNoEntry: false })?.isDirectory() === true;
    const hasStyleRules = statSync(join(directory, ".editorconfig"), { throwIfNoEntry: false })?.isFile() === true;
    if (hasModules && hasStyleRules) {
      return directory;
    }
    directory = dirname(directory);
  }
  throw new Error("The src folder was not found above this test file.");
}

/** Every non-test source of every module client and of the shell's text editor panels. */
function moduleSources(root: string): string[] {
  const files: string[] = [];
  const walk = (directory: string) => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (entry.isDirectory()) {
        if (entry.name !== "node_modules" && entry.name !== "generated") {
          walk(path);
        }
      } else if (/\.tsx?$/.test(entry.name) && !/\.test\.tsx?$/.test(entry.name)) {
        files.push(path);
      }
    }
  };
  for (const family of ["diagrams", "editors"]) {
    for (const module of readdirSync(join(root, family), { withFileTypes: true }).filter((entry) => entry.isDirectory())) {
      const client = join(root, family, module.name, "client");
      if (statSync(client, { throwIfNoEntry: false })?.isDirectory() === true) {
        walk(client);
      }
    }
  }
  walk(join(root, "client", "src", "editors"));
  return files;
}

describe("no module source declares a rejection or status element", () => {
  it("recognises the library's own - the pattern still sees what it polices", () => {
    // The control, read exactly as the check reads a module: comments blanked.
    const frame = readFileSync(join(sourceRoot(), "client/src/canvas/library/surface/CanvasFrame.tsx"), "utf8");
    expect(DECLARES_SURFACE.test(withoutComments(frame))).toBe(true);
  });

  it("tells a declared surface from a sentence that merely mentions one", () => {
    // The canary: planted text of both kinds.
    expect(DECLARES_SURFACE.test(`<p className="wardley-rejection">`)).toBe(true);
    expect(DECLARES_SURFACE.test(`<div className="pipeline-canvas-loading" role="status">`)).toBe(true);
    expect(DECLARES_SURFACE.test(`{ className: "canvas-status" }`)).toBe(true);
    expect(DECLARES_SURFACE.test(`const message = "The rejection line is the library's.";`)).toBe(false);
  });

  it("finds none in any module client or text editor panel", () => {
    // Arrange.
    const root = sourceRoot();
    const sources = moduleSources(root);
    // 82 files when this was written (seventeen module clients, the two editors and the shell's text
    // editor panels); the floor is set under that, so a walk that stopped resolving fails here.
    expect(sources.length, "the walk found too few files to mean anything").toBeGreaterThanOrEqual(70);

    // Act.
    const offenders = sources
      .filter((file) => DECLARES_SURFACE.test(withoutComments(readFileSync(file, "utf8"))))
      .map((file) => relative(root, file).replaceAll("\\", "/"));

    // Assert: the fix is to delete the element - the library's frame already shows it.
    expect(offenders).toEqual([]);
  });
});
