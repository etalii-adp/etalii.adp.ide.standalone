import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, render } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { CanvasRefusalContext, type CanvasRefusalReporter } from "../../canvas/library/surface/canvasRefusals";
import { ContextShortcutSchema } from "../../generated/context-contract_pb";
import { ContextConnectionProvider, useContextConnection } from "./ContextConnectionProvider";

/**
 * Inside a canvas, the context connection's gesture calls report to that canvas's refusal line
 * (client-centralization Requirement 2). This is the half that makes a module's OWN call reach the
 * library: before it, `executeAction`, `executeShortcut` and `setProperty` resolved inside whichever
 * module sent them, and a module with no banner of its own showed the user nothing.
 *
 * Outside a canvas nothing changes, and that is asserted rather than assumed: the ribbon, the
 * explorer and the property grid use the same hook.
 */

/** A stream that neither yields nor ends, so the provider's reconnect loop stays parked. */
const parkedStream = {
  [Symbol.asyncIterator]: () => ({ next: () => new Promise<never>(() => {}) }),
};

let answer: { accepted: boolean; error: string } = { accepted: true, error: "" };

vi.mock("@connectrpc/connect", () => ({
  createClient: () =>
    new Proxy(
      {},
      {
        get: (_target, property) => {
          if (property === "watch") {
            return () => parkedStream;
          }
          return () => Promise.resolve(answer);
        },
      },
    ),
}));

vi.mock("../../auth/AuthContext", () => {
  const transport = {};
  return { useAuth: () => ({ transport }) };
});

let inside: ReturnType<typeof useContextConnection> | undefined;
let outside: ReturnType<typeof useContextConnection> | undefined;
let told: string[] = [];

const reporter: CanvasRefusalReporter = {
  attempted: () => told.push("attempted"),
  refused: (message) => told.push(`refused: ${message}`),
};

function Inside() {
  inside = useContextConnection();
  return null;
}

function Outside() {
  outside = useContextConnection();
  return null;
}

function mount() {
  render(
    <ContextConnectionProvider projectId={new Uint8Array([1])}>
      <Outside />
      <CanvasRefusalContext.Provider value={reporter}>
        <Inside />
      </CanvasRefusalContext.Provider>
    </ContextConnectionProvider>,
  );
}

const shortcut = create(ContextShortcutSchema, { key: "F2" });

const GESTURE_CALLS: [string, () => Promise<unknown>][] = [
  ["executeAction", () => inside!.executeAction("rename")],
  ["executeShortcut", () => inside!.executeShortcut(shortcut)],
  ["setProperty", () => inside!.setProperty("name", "Planning")],
];

describe("the context connection inside a canvas", () => {
  beforeEach(() => {
    told = [];
    answer = { accepted: true, error: "" };
  });

  it.each(GESTURE_CALLS)("%s reports a refusal to the canvas's line", async (_name, call) => {
    // Arrange.
    mount();
    answer = { accepted: false, error: "That name is taken." };

    // Act.
    await act(async () => {
      await call();
    });

    // Assert.
    expect(told).toEqual(["attempted", "refused: That name is taken."]);
  });

  it.each(GESTURE_CALLS)("%s clears the line when it is sent, whatever comes back", async (_name, call) => {
    // Arrange.
    mount();

    // Act.
    await act(async () => {
      await call();
    });

    // Assert.
    expect(told).toEqual(["attempted"]);
  });

  it("does not treat a selection as a gesture: it neither clears nor fills the line", async () => {
    // Arrange: the user's ruling names a move, a command, a shortcut and a drop - not a press.
    mount();

    // Act.
    await act(async () => {
      inside!.select(null);
    });

    // Assert.
    expect(told).toEqual([]);
  });

  it("outside a canvas reports nothing, and still hands the caller its refusal", async () => {
    // Arrange.
    mount();
    answer = { accepted: false, error: "Refused." };

    // Act.
    let outcome: unknown;
    await act(async () => {
      outcome = await outside!.executeAction("rename");
    });

    // Assert.
    expect(told).toEqual([]);
    expect(outcome).toEqual({ accepted: false, error: "Refused." });
  });

  it("inside a canvas still hands the caller its refusal, so a module that branches on it can", async () => {
    // Arrange.
    mount();
    answer = { accepted: false, error: "Refused." };

    // Act.
    let outcome: unknown;
    await act(async () => {
      outcome = await inside!.executeAction("rename");
    });

    // Assert.
    expect(outcome).toEqual({ accepted: false, error: "Refused." });
  });
});
