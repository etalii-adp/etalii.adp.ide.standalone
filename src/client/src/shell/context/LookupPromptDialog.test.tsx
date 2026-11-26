import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { create } from "@bufbuild/protobuf";
import { ContextPromptSchema, LookupDialogPromptSchema, type LookupDialogPrompt } from "../../generated/context_pb";
import { ContextPromptHost, type ContextLookupResult } from "./ContextPromptHost";
import { LookupPromptDialog, type ContextLookupCandidate } from "./LookupPromptDialog";

const checkout: ContextLookupCandidate = { value: "$k7q2m9xa@features.yaml#checkout=Checkout", name: "Checkout", documentName: "features.yaml", folder: "specs" };
const launch: ContextLookupCandidate = { value: "$r6d3w8ne@roadmap.tml#launch=Launch", name: "Launch", documentName: "roadmap.tml", folder: "" };

function lookupPrompt(): LookupDialogPrompt {
  return create(LookupDialogPromptSchema, {
    title: "Add from another document",
    icon: "mdi-link-plus",
    placeholder: "Search by name",
    emptyMessage: "No element of the workspace has a name like that.",
  });
}

/** A search over the two candidates above, by name, as the backend's would answer. */
function searchOf(found: ContextLookupCandidate[] = [checkout, launch]) {
  return vi.fn(async (query: string): Promise<ContextLookupResult> => ({
    candidates: found.filter((candidate) => candidate.name.toLowerCase().includes(query.toLowerCase())),
    error: "",
  }));
}

function renderDialog(onSearch = searchOf(), submitResult = { completed: true, error: "" }) {
  const onSubmit = vi.fn(async (_value: string) => submitResult);
  const onCancel = vi.fn();
  render(<LookupPromptDialog prompt={lookupPrompt()} onSearch={onSearch} onSubmit={onSubmit} onCancel={onCancel} />);
  return { onSearch, onSubmit, onCancel };
}

/** Lets the debounce run out and the search it starts answer. */
async function settle() {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(200);
  });
}

const field = () => screen.getByRole("combobox", { name: "Add from another document" }) as HTMLInputElement;
const options = () => screen.queryAllByRole("option").map((option) => option.textContent);

describe("LookupPromptDialog", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("opens on what can be added, each with the document and folder it is in", async () => {
    // Act.
    const { onSearch } = renderDialog();
    await settle();

    // Assert.
    expect(onSearch).toHaveBeenCalledWith("");
    expect(options()).toEqual(["Checkoutspecs/features.yaml", "Launchroadmap.tml"]);
    expect(field().placeholder).toBe("Search by name");
  });

  it("asks the backend again for what is typed, once the typing stops", async () => {
    // Arrange.
    const { onSearch } = renderDialog();
    await settle();

    // Act.
    fireEvent.change(field(), { target: { value: "lau" } });
    await settle();

    // Assert.
    expect(onSearch).toHaveBeenLastCalledWith("lau");
    expect(options()).toEqual(["Launchroadmap.tml"]);
  });

  it("picks from what was typed, not from the list of the text before it, when Enter comes before the answer", async () => {
    // Arrange: the list answers the empty query, Checkout first.
    const { onSubmit } = renderDialog();
    await settle();

    // Act: "lau" typed and Enter pressed at once, before the debounce has run out.
    fireEvent.change(field(), { target: { value: "lau" } });
    await act(async () => {
      fireEvent.keyDown(field(), { key: "Enter" });
    });
    await settle();

    // Assert: Launch, the one "lau" names, and never Checkout.
    expect(onSubmit).toHaveBeenCalledTimes(1);
    expect(onSubmit).toHaveBeenCalledWith(launch.value);
  });

  it("says when more match than are shown, so that typing more narrows them", async () => {
    // Arrange: a search that finds more than it gives.
    const search = vi.fn(async (_query: string): Promise<ContextLookupResult> => ({ candidates: [checkout], error: "", more: true }));

    // Act.
    renderDialog(search);
    await settle();

    // Assert.
    expect(screen.getByText("More match. Type more of a name to narrow them.")).toBeTruthy();
  });

  it("says so when nothing has a name like that", async () => {
    // Arrange.
    renderDialog();
    await settle();

    // Act.
    fireEvent.change(field(), { target: { value: "zebra" } });
    await settle();

    // Assert.
    expect(options()).toEqual([]);
    expect(screen.getByText("No element of the workspace has a name like that.")).toBeTruthy();
    expect((screen.getByRole("button", { name: "Add" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("submits the link of the candidate the arrow keys are on with Enter", async () => {
    // Arrange.
    const { onSubmit } = renderDialog();
    await settle();

    // Act.
    fireEvent.keyDown(field(), { key: "ArrowDown" });
    await act(async () => {
      fireEvent.keyDown(field(), { key: "Enter" });
    });

    // Assert.
    expect(onSubmit).toHaveBeenCalledExactlyOnceWith(launch.value);
  });

  it("submits a candidate that is clicked", async () => {
    // Arrange.
    const { onSubmit } = renderDialog();
    await settle();

    // Act.
    await act(async () => {
      fireEvent.click(screen.getByText("Checkout"));
    });

    // Assert.
    expect(onSubmit).toHaveBeenCalledExactlyOnceWith(checkout.value);
  });

  it("stays open with the reason when the pick is refused", async () => {
    // Arrange.
    renderDialog(searchOf(), { completed: false, error: "That element is no longer in the workspace." });
    await settle();

    // Act.
    await act(async () => {
      fireEvent.click(screen.getByText("Checkout"));
    });

    // Assert.
    expect(screen.getByRole("alert").textContent).toBe("That element is no longer in the workspace.");
    expect(options()).toEqual(["Checkoutspecs/features.yaml", "Launchroadmap.tml"]);
  });
});

describe("ContextPromptHost", () => {
  it("renders a lookup prompt with the host's search rather than cancelling it", async () => {
    // Arrange.
    vi.useFakeTimers();
    const onCancel = vi.fn();
    const onSearch = searchOf();
    const prompt = create(ContextPromptSchema, { prompt: { case: "lookupDialog", value: lookupPrompt() } });

    // Act.
    render(
      <ContextPromptHost
        prompt={prompt}
        onPropose={vi.fn()}
        onSearch={onSearch}
        onSubmit={vi.fn(async () => ({ completed: true, error: "" }))}
        onCancel={onCancel}
      />,
    );
    await settle();
    vi.useRealTimers();

    // Assert.
    expect(onCancel).not.toHaveBeenCalled();
    expect(onSearch).toHaveBeenCalledWith("");
    expect(options()).toEqual(["Checkoutspecs/features.yaml", "Launchroadmap.tml"]);
  });
});
