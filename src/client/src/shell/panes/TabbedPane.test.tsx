import { describe, expect, it } from "vitest";
import { fireEvent, render } from "@testing-library/react";
import { TabbedPane } from "./TabbedPane";

describe("TabbedPane", () => {
  it("shows the first tab's content by default", () => {
    const { getByText, queryByText } = render(
      <TabbedPane
        tabs={[
          { id: "a", label: "A", content: <div>Content A</div> },
          { id: "b", label: "B", content: <div>Content B</div> },
        ]}
      />,
    );

    expect(getByText("Content A")).toBeTruthy();
    expect(queryByText("Content B")).toBeNull();
  });

  it("honors defaultActiveTabId", () => {
    const { getByText, queryByText } = render(
      <TabbedPane
        defaultActiveTabId="b"
        tabs={[
          { id: "a", label: "A", content: <div>Content A</div> },
          { id: "b", label: "B", content: <div>Content B</div> },
        ]}
      />,
    );

    expect(getByText("Content B")).toBeTruthy();
    expect(queryByText("Content A")).toBeNull();
  });

  it("switches content when a tab is clicked", () => {
    const { getByText, queryByText } = render(
      <TabbedPane
        tabs={[
          { id: "a", label: "A", content: <div>Content A</div> },
          { id: "b", label: "B", content: <div>Content B</div> },
        ]}
      />,
    );

    fireEvent.click(getByText("B"));

    expect(getByText("Content B")).toBeTruthy();
    expect(queryByText("Content A")).toBeNull();
  });

  it("does not render a tab strip for a single tab", () => {
    const { queryByRole } = render(<TabbedPane tabs={[{ id: "a", label: "A", content: <div>Content A</div> }]} />);

    expect(queryByRole("tablist")).toBeNull();
  });

  it("keeps two sibling instances' active tabs independent", () => {
    const { getAllByText, queryAllByText } = render(
      <>
        <TabbedPane
          tabs={[
            { id: "a", label: "A", content: <div>Content A1</div> },
            { id: "b", label: "B", content: <div>Content B1</div> },
          ]}
        />
        <TabbedPane
          tabs={[
            { id: "a", label: "A", content: <div>Content A2</div> },
            { id: "b", label: "B", content: <div>Content B2</div> },
          ]}
        />
      </>,
    );

    // Switch only the first instance to tab B.
    fireEvent.click(getAllByText("B")[0]);

    expect(queryAllByText("Content B1")).toHaveLength(1);
    expect(queryAllByText("Content A2")).toHaveLength(1);
    expect(queryAllByText("Content B2")).toHaveLength(0);
  });
});
