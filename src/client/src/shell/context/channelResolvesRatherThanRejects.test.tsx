import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, render } from "@testing-library/react";
import {
  ContextConnectionProvider,
  useContextConnection,
  useContextPrompt,
} from "./ContextConnectionProvider";

/**
 * The rule this file keeps true, and the reason it iterates rather than listing:
 *
 * **Every value-returning method on the context channel resolves with a failure value when the
 * transport rejects. It never rejects.**
 *
 * Four defects arrived in four files at different times because each caller was written as
 * though the promise it awaited could only resolve - a dropped connection then showed the user
 * nothing while the component's own state said the write had succeeded. Guarding those four
 * callers would leave the fifth to be written the same way, so the guard sits on the channel
 * and on the whole class: a method added later is covered by having been added, because the
 * coverage assertion fails until somebody says which of the two kinds it is.
 */

/** A stream that neither yields nor ends, so the provider's reconnect loop stays parked. */
const parkedStream = {
  [Symbol.asyncIterator]: () => ({ next: () => new Promise<never>(() => {}) }),
};

/**
 * A transport where every call rejects - a proxy rather than a list of stubs, so a call added
 * to the service is a rejecting call here too without this file being edited.
 */
vi.mock("@connectrpc/connect", () => ({
  createClient: () =>
    new Proxy(
      {},
      {
        get: (_target, property) => {
          if (property === "watch") {
            return () => parkedStream;
          }
          return () => Promise.reject(new Error("the transport dropped"));
        },
      },
    ),
}));

vi.mock("../../auth/AuthContext", () => ({
  useAuth: () => ({ transport: {} }),
}));

let connection: ReturnType<typeof useContextConnection> | undefined;
let promptValue: ReturnType<typeof useContextPrompt> | undefined;

function Probe() {
  connection = useContextConnection();
  promptValue = useContextPrompt();
  return null;
}

/** The methods whose caller acts on what comes back. */
const VALUE_RETURNING = [
  "executeAction",
  "executeShortcut",
  "describeProperties",
  "setProperty",
  "onPropose",
  "onSubmit",
];

/**
 * The other half of the rule: a method returning `void` is advisory and swallows its fault.
 * Named here so the coverage assertion can tell "classified as advisory" from "nobody has
 * looked at this one yet".
 */
const ADVISORY = ["select", "clearReveal", "revealPath", "onCancel"];

/**
 * How to call each value-returning method. The arguments are placeholders: what is under test
 * is what comes back from a transport that rejected, and none of these reach a backend to care.
 */
function invokers(): Record<string, () => Promise<unknown>> {
  const channel = connection!;
  const prompt = promptValue!;
  return {
    executeAction: () => channel.executeAction("hierarchy.rename"),
    executeShortcut: () => channel.executeShortcut({ key: "F2" } as Parameters<typeof channel.executeShortcut>[0]),
    describeProperties: () => channel.describeProperties(),
    setProperty: () => channel.setProperty("mindmap.text", "Milestones"),
    onPropose: () => prompt.onPropose(1, "a.txt"),
    onSubmit: () => prompt.onSubmit("a.txt"),
  };
}

/** A failure a caller can act on says why, in whichever field this method's shape provides. */
function statesAFailure(resolved: unknown): boolean {
  if (typeof resolved !== "object" || resolved === null) {
    return false;
  }
  const record = resolved as Record<string, unknown>;
  return (
    (typeof record.error === "string" && record.error.length > 0) ||
    (typeof record.reason === "string" && record.reason.length > 0)
  );
}

describe("the context channel resolves rather than rejects", () => {
  beforeEach(() => {
    connection = undefined;
    promptValue = undefined;
    render(
      <ContextConnectionProvider projectId={new Uint8Array(16).fill(1)}>
        <Probe />
      </ContextConnectionProvider>,
    );
  });

  it("classifies every method the channel exposes", () => {
    // Arrange.
    const exposed = [...Object.entries(connection!), ...Object.entries(promptValue!)]
      .filter(([, member]) => typeof member === "function")
      .map(([name]) => name);
    const classified = new Set([...VALUE_RETURNING, ...ADVISORY]);

    // Assert.
    const unclassified = exposed.filter((name) => !classified.has(name));
    expect(
      unclassified,
      `${unclassified.join(", ")} is on the context channel and this guard does not know which kind it is. ` +
        "Add it to VALUE_RETURNING with a way to call it if a caller acts on what it returns, or to ADVISORY " +
        "if it returns void and swallows its fault.",
    ).toEqual([]);

    // And the names cannot drift from the calls: a name listed with no way to call it would
    // look covered while being skipped, which is the one failure this guard must not have.
    expect(Object.keys(invokers()).sort()).toEqual([...VALUE_RETURNING].sort());
  });

  it.each(VALUE_RETURNING)("%s resolves with a failure value", async (name) => {
    // Act.
    let resolved: unknown;
    let rejection: unknown;
    await act(async () => {
      try {
        resolved = await invokers()[name]();
      } catch (caught) {
        rejection = caught;
      }
    });

    // Assert.
    expect(
      rejection,
      `${name} rejected instead of resolving with a failure value. The rule that every ` +
        "value-returning method on this channel reports a transport fault inside its return value " +
        "has stopped holding, and its callers - React event handlers and effects - turn such a " +
        "rejection into an unhandled rejection that the user is never shown.",
    ).toBeUndefined();
    expect(
      statesAFailure(resolved),
      `${name} resolved, but with nothing a caller could show: a failure value has to say why, in ` +
        `its own error or reason field. It resolved with ${JSON.stringify(resolved)}.`,
    ).toBe(true);
  });
});
