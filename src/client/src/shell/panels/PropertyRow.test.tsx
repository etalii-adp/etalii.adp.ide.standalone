import { describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { ContextPropertyEditor } from "../../generated/context-contract_pb";
import { ContextPropertySchema, type ContextProperty } from "../../generated/context_pb";
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

describe("PropertyRow as a slider", () => {
  const phases = () =>
    property({
      id: "phases",
      label: "Phases",
      value: "Trough",
      editor: ContextPropertyEditor.SLIDER,
      candidates: ["Peak", "Trough", "Slope", "Plateau"],
    });

  it("renders one stop per candidate and names the current one", () => {
    // Arrange, act.
    const { container } = render(<PropertyRow property={phases()} onCommit={vi.fn(async () => "")} />);

    // Assert.
    const slider = screen.getByLabelText("Phases") as HTMLInputElement;
    expect(slider.type).toBe("range");
    expect(slider.max).toBe("3");
    expect(slider.value).toBe("1");
    expect(container.querySelectorAll("datalist option")).toHaveLength(4);
    expect(container.querySelector(".property-grid-slider-value")?.textContent).toBe("Trough");
  });

  it("commits the chosen CANDIDATE, never its stop index", async () => {
    // Arrange.
    const onCommit = vi.fn(async () => "");
    render(<PropertyRow property={phases()} onCommit={onCommit} />);

    // Act: the fourth stop.
    fireEvent.change(screen.getByLabelText("Phases"), { target: { value: "3" } });
    await flush();

    // Assert.
    expect(onCommit).toHaveBeenCalledTimes(1);
    expect(onCommit).toHaveBeenCalledWith("Plateau");
  });

  it("keeps the dragged stop while an older value echoes back, and writes only the latest", async () => {
    // Arrange: every write stays in flight until the test settles it, as over a real connection.
    const settle: (() => void)[] = [];
    const onCommit = vi.fn((_value: string) => new Promise<string>((resolve) => settle.push(() => resolve(""))));
    const start = property({ id: "phases", label: "Phases", value: "Peak", editor: ContextPropertyEditor.SLIDER, candidates: ["Peak", "Trough", "Slope", "Plateau"] });
    const { rerender } = render(<PropertyRow property={start} onCommit={onCommit} />);
    const slider = screen.getByLabelText("Phases") as HTMLInputElement;

    // Act: a drag passes Trough and Slope and rests on Plateau while the first write is in
    // flight, and the backend pushes the value that first write produced.
    fireEvent.change(slider, { target: { value: "1" } });
    fireEvent.change(slider, { target: { value: "2" } });
    fireEvent.change(slider, { target: { value: "3" } });
    rerender(<PropertyRow property={{ ...start, value: "Trough" }} onCommit={onCommit} />);

    // Assert: the thumb stays where the pointer is; an echo is not the user's answer.
    expect(slider.value, "a pushed echo of an earlier write moved the thumb back under the pointer, which is the flicker, and the next pointer move writes again").toBe("3");

    // Act: the first write lands, then the second, and the backend echoes the last.
    settle.shift()!();
    await flush();
    settle.shift()?.();
    await flush();
    rerender(<PropertyRow property={{ ...start, value: "Plateau" }} onCommit={onCommit} />);
    await flush();

    // Assert: the stops in between were never written; the one the drag ended on was.
    expect(onCommit.mock.calls.map(([value]) => value)).toEqual(["Trough", "Plateau"]);
    expect(slider.value).toBe("3");
  });

  it("still shows an editor number this client does not know as read-only", () => {
    // Arrange, act: an editor from a backend newer than this client.
    const { container } = render(
      <PropertyRow property={property({ id: "x", label: "Future", value: "v", editor: 99 as ContextPropertyEditor })} onCommit={vi.fn(async () => "")} />,
    );

    // Assert: the value, and no field of any kind.
    expect(container.querySelector(".property-grid-value")?.textContent).toBe("v");
    expect(container.querySelector("input, select, textarea")).toBeNull();
  });
});

describe("PropertyRow", () => {
  it("shows a read-only value of several lines one line a row", () => {
    // Arrange, act: a trend phase's influences, as the GHG provider sends them.
    const { container } = render(
      <PropertyRow
        property={property({ id: "ghg.peak-influences", label: "Influence", value: "Coal power · Peak\nRailways · Slope", editor: ContextPropertyEditor.TEXT, readOnlyReason: "Draw, reattach or delete an influence on the canvas." })}
        onCommit={vi.fn(async () => "")}
      />,
    );

    // Assert: each entry is its own line, not run together into one.
    const lines = [...container.querySelectorAll(".property-grid-value .property-grid-value-line")].map((line) => line.textContent);
    expect(lines, "the entries were drawn as one run-on line").toEqual(["Coal power · Peak", "Railways · Slope"]);
  });

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
