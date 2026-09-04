import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { useMemo } from "react";
import { ContextPromptSchema } from "../../generated/context_pb";
import type { ContextPrompt } from "../../generated/context_pb";
import { ShellPromptHost } from "./ShellPromptHost";
import {
  InlineLabelPlacementProvider,
  useRegisterInlineLabelPlacement,
  type LabelPlacement,
} from "../panels/InlineLabelPlacementContext";

const onCancel = vi.fn();
let currentPrompt: ContextPrompt | null = null;

vi.mock("./ContextConnectionProvider", () => ({
  useContextPrompt: () => ({
    prompt: currentPrompt,
    onPropose: vi.fn(),
    onSubmit: vi.fn(),
    onCancel,
  }),
}));

/**
 * A rename prompt. `elementId` empty is what the backend sends for every prompt that is not a
 * label edit - the explorer's rename among them, and the forty-odd asking for run-if values,
 * cluster keys, prefix declarations and predicate IRIs.
 */
function renamePrompt(elementId = ""): ContextPrompt {
  return create(ContextPromptSchema, {
    interactionId: { value: new Uint8Array(16).fill(1) },
    prompt: {
      case: "inputDialog",
      value: {
        title: "Rename node",
        icon: "mdi-pencil-outline",
        fieldLabel: "New name",
        initialValue: "before",
        confirmLabel: "Apply",
        ...(elementId.length > 0 ? { inlineLabelEdit: { elementId: { value: elementId } } } : {}),
      },
    },
  });
}

const somewhere: LabelPlacement = { x: 10, y: 20, width: 80, height: 16, text: "before" };

/** A canvas that can place exactly the elements it is told about, and nothing else. */
function StubCanvas({ places }: { places: readonly string[] }) {
  const resolve = useMemo(
    () => (elementId: string) => (places.includes(elementId) ? somewhere : null),
    [places],
  );
  useRegisterInlineLabelPlacement(resolve);
  return null;
}

function renderHost(places: readonly string[] = []) {
  return render(
    <InlineLabelPlacementProvider>
      <StubCanvas places={places} />
      <ShellPromptHost />
    </InlineLabelPlacementProvider>,
  );
}

function dialogTitle() {
  return screen.queryByText("Rename node");
}

describe("ShellPromptHost inline deferral", () => {
  beforeEach(() => {
    currentPrompt = null;
    onCancel.mockClear();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("renders the dialog for an unmarked prompt, which is what the explorer and the other input sites send", () => {
    // Arrange.
    currentPrompt = renamePrompt();

    // Act.
    renderHost(["node-1"]);

    // Assert.
    // The defect this catches is a host that stands down for any input prompt: the majority of
    // prompts are not label edits, and every one of them would stop opening at all.
    expect(dialogTitle()).not.toBeNull();
  });

  it("renders the dialog for a marked prompt no mounted canvas can place", () => {
    // Arrange.
    currentPrompt = renamePrompt("node-9");

    // Act.
    renderHost(["node-1"]);

    // Assert.
    // The defect this catches is a fallback that never fires - the user's click would do
    // nothing visible at all, with an interaction left open on the backend.
    expect(dialogTitle()).not.toBeNull();
  });

  it("renders neither a dialog nor a notice when a canvas can place the marked prompt", () => {
    // Arrange.
    currentPrompt = renamePrompt("node-1");

    // Act.
    renderHost(["node-1"]);

    // Assert.
    // The defect this catches is a host that renders both, putting a modal on top of the
    // editor the canvas just drew.
    expect(dialogTitle()).toBeNull();
    expect(screen.queryByText("Action cancelled")).toBeNull();
  });

  it("never flashes a dialog for a prompt that arrives while a canvas is already mounted", () => {
    // Arrange.
    // The real sequence, and the only one the app produces: the canvas is mounted first - a
    // diagram has to be open before anything in it can be selected - and the prompt arrives
    // afterwards. Registration is per canvas rather than per prompt precisely so the registry
    // has already answered by then.
    const { rerender, container } = renderHost(["node-1"]);

    // Act.
    currentPrompt = renamePrompt("node-1");
    act(() => {
      rerender(
        <InlineLabelPlacementProvider>
          <StubCanvas places={["node-1"]} />
          <ShellPromptHost />
        </InlineLabelPlacementProvider>,
      );
    });

    // Assert.
    // No dialog at any point, not merely none once the effects have settled. The defect is a
    // registry the host learns about one commit late, which a user sees as a modal appearing
    // and vanishing and a test looking only at the end state never sees at all.
    expect(container.querySelector("dialog")).toBeNull();
    expect(dialogTitle()).toBeNull();
  });

  it("does not mistake a canvas going away for the element disappearing", () => {
    // Arrange.
    // The defect the manual pass found: every mindmap rename was cancelled the instant it
    // opened. A canvas leaves the registry whenever it unmounts or re-registers, and the shell
    // read an empty registry as "the thing being edited has gone" - so an edit was abandoned
    // because no canvas was mounted, which says nothing at all about the element.
    currentPrompt = renamePrompt("node-1");
    const { rerender } = renderHost(["node-1"]);
    expect(onCancel).not.toHaveBeenCalled();

    // Act.
    act(() => {
      rerender(
        <InlineLabelPlacementProvider>
          <ShellPromptHost />
        </InlineLabelPlacementProvider>,
      );
    });

    // Assert.
    expect(onCancel).not.toHaveBeenCalled();
    expect(screen.queryByText("Action cancelled")).toBeNull();
  });

  it("does not mistake a canvas re-registering for the element disappearing", () => {
    // Arrange.
    // A canvas re-registers whenever its model changes, which during an edit is often: every
    // delta from the backend produces a new resolver. This is the defect the manual pass found
    // and no unit test could: registering the resolver directly ran cleanup-then-body, so the
    // registry was momentarily EMPTY, the shell read that as the element being gone, and it
    // cancelled the interaction. Every mindmap rename died the instant it opened.
    currentPrompt = renamePrompt("node-1");
    const { rerender } = renderHost(["node-1"]);
    expect(onCancel).not.toHaveBeenCalled();

    // Act.
    // A new array identity is a new resolver identity, which is exactly what a model delta does.
    act(() => {
      rerender(
        <InlineLabelPlacementProvider>
          <StubCanvas places={["node-1"]} />
          <ShellPromptHost />
        </InlineLabelPlacementProvider>,
      );
    });

    // Assert.
    expect(onCancel).not.toHaveBeenCalled();
    expect(screen.queryByText("Action cancelled")).toBeNull();
    expect(dialogTitle()).toBeNull();
  });

  it("cancels and explains when the element being edited disappears mid-edit", () => {
    // Arrange.
    currentPrompt = renamePrompt("node-1");
    const { rerender } = renderHost(["node-1"]);
    expect(dialogTitle()).toBeNull();

    // Act.
    // The element leaves the canvas while the prompt is still open - a delta removed it, or a
    // reload dropped it.
    act(() => {
      rerender(
        <InlineLabelPlacementProvider>
          <StubCanvas places={[]} />
          <ShellPromptHost />
        </InlineLabelPlacementProvider>,
      );
    });

    // Assert.
    // The defect this catches is a silent abandon - and the near miss is falling back to the
    // dialog, which would ask the user to rename something that is no longer there.
    expect(onCancel).toHaveBeenCalled();
    expect(screen.queryByText("Action cancelled")).not.toBeNull();
    expect(dialogTitle()).toBeNull();
  });
});
