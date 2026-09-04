import { describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import {
  ContextPropertyEditor,
  ContextPropertySchema,
  type ContextProperty,
} from "../../generated/context_pb";
import { PropertyRow } from "./PropertyRow";

type PropertyOverrides = Omit<Partial<ContextProperty>, "$typeName" | "$unknown"> & {
  id: string;
  label: string;
  value: string;
};

function property(overrides: PropertyOverrides): ContextProperty {
  return create(ContextPropertySchema, {
    editor: ContextPropertyEditor.LINE,
    readOnlyReason: "",
    group: "",
    ...overrides,
  });
}

async function flush() {
  await act(async () => {
    await Promise.resolve();
  });
}

/**
 * Runs `body` with an escaping promise rejection expected rather than fatal.
 *
 * `onBlur: () => void commit()` does not catch, so a rejecting `onCommit` surfaces as an
 * unhandled rejection, and this task deliberately leaves that alone: releasing the in-flight
 * guard is what a `finally` is for, and deciding who reports a transport fault belongs to the
 * channel, which stops rejecting at all in task 2. Until then a test that drives a rejecting
 * commit has to expect the escape - swallowed here with the reason attached, so it cannot fail
 * the run for a cause nobody is looking at.
 */
async function withEscapingRejection(body: () => Promise<void>) {
  const installed = process.listeners("unhandledRejection");
  process.removeAllListeners("unhandledRejection");
  process.on("unhandledRejection", () => {});
  try {
    await body();
  } finally {
    process.removeAllListeners("unhandledRejection");
    for (const listener of installed) {
      process.on("unhandledRejection", listener);
    }
  }
}

describe("PropertyRow", () => {
  it("takes the next edit after a commit that rejected", async () => {
    // Arrange.
    // The first write faults the way a dropped connection faults - by rejecting, not by
    // resolving to an error string. Nothing about the component's own contract stops a caller
    // handing it a function that does this; `onCommit` is a prop.
    const attempts: string[] = [];
    const onCommit = vi.fn(async (value: string) => {
      attempts.push(value);
      if (attempts.length === 1) {
        throw new Error("the transport dropped");
      }
      return "";
    });
    render(<PropertyRow property={property({ id: "mindmap.text", label: "Text", value: "one" })} onCommit={onCommit} />);
    const field = screen.getByLabelText("Text");

    // Act.
    await withEscapingRejection(async () => {
      fireEvent.focus(field);
      fireEvent.change(field, { target: { value: "two" } });
      fireEvent.blur(field);
      await flush();
    });

    fireEvent.focus(field);
    fireEvent.change(field, { target: { value: "three" } });
    fireEvent.blur(field);
    await flush();

    // Assert.
    expect(
      attempts,
      "the row stopped writing after one failed commit: its in-flight guard was latched by a rejection and never released, so every later edit on this row returns early and says nothing",
    ).toEqual(["two", "three"]);
  });

  it("still writes once for an edit that succeeds", async () => {
    // Arrange.
    // The companion the guard needs: releasing it in a `finally` must not make the ordinary path
    // write twice, which is the defect the guard was put there to prevent.
    const onCommit = vi.fn(async () => "");
    render(<PropertyRow property={property({ id: "mindmap.text", label: "Text", value: "one" })} onCommit={onCommit} />);
    const field = screen.getByLabelText("Text");

    // Act.
    fireEvent.focus(field);
    fireEvent.change(field, { target: { value: "two" } });
    fireEvent.blur(field);
    await flush();

    // Assert.
    expect(onCommit.mock.calls).toEqual([["two"]]);
  });
});
