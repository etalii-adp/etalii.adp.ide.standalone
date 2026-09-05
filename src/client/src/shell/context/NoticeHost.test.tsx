import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { NoticeHost } from "./NoticeHost";

const state: { notices: { id: number; text: string }[]; dismiss: (id: number) => void } = {
  notices: [],
  dismiss: vi.fn(),
};

vi.mock("./ContextConnectionProvider", () => ({
  useContextNotices: () => state,
}));

describe("NoticeHost", () => {
  it("shows nothing at all when there is nothing to say", () => {
    state.notices = [];

    const { container } = render(<NoticeHost />);

    // Not an empty container that still occupies the corner: nothing rendered.
    expect(container.innerHTML).toBe("");
  });

  it("shows what the backend said, verbatim", () => {
    // The sentence is the backend's - the client neither rewords it nor decides what it means,
    // because only the backend knows which file could not be written.
    state.notices = [{ id: 1, text: "The new position could not be saved beside tea.owm." }];

    render(<NoticeHost />);

    expect(screen.getByRole("status").textContent).toContain("The new position could not be saved beside tea.owm.");
  });

  it("keeps both when two edits each lose something", () => {
    // A queue rather than one value: the second must not silently replace the first before
    // anyone has read it, which is the whole failure this surface exists to end.
    state.notices = [
      { id: 1, text: "The new position could not be saved beside tea.owm." },
      { id: 2, text: "The element identities beside tea.owm could not be saved." },
    ];

    render(<NoticeHost />);

    expect(screen.getAllByRole("button", { name: "Dismiss this message" })).toHaveLength(2);
  });

  it("dismisses the one that was clicked, by its own id", () => {
    const dismiss = vi.fn();
    state.notices = [
      { id: 7, text: "first" },
      { id: 9, text: "second" },
    ];
    state.dismiss = dismiss;

    render(<NoticeHost />);
    fireEvent.click(screen.getAllByRole("button", { name: "Dismiss this message" })[1]);

    // The id, not the index: two notices can carry the same sentence, and dismissing by
    // position would close whichever happened to be there.
    expect(dismiss).toHaveBeenCalledWith(9);
  });

  it("announces politely, because nothing failed", () => {
    // The edit succeeded. An assertive live region would interrupt a screen reader mid-sentence
    // to report something that is not an error.
    state.notices = [{ id: 1, text: "anything" }];

    render(<NoticeHost />);

    expect(screen.getByRole("status").getAttribute("aria-live")).toBe("polite");
  });
});
