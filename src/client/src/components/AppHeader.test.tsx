import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { AppHeader } from "./AppHeader";

describe("AppHeader", () => {
  it("links Peter Vrenken to the project's git repository", () => {
    // Arrange.
    render(<AppHeader />);

    // Act and assert, step by step.
    const link = screen.getByRole("link", { name: "Peter Vrenken" });
    expect(link.getAttribute("href")).toBe("https://github.com/vrenken/EtAlii.Adp");
    expect(link.getAttribute("target")).toBe("_blank");
    expect(link.getAttribute("rel")).toBe("noopener noreferrer");
  });

  it("leads with the ADP mark, hidden from assistive technology", () => {
    // Arrange.
    const { container } = render(<AppHeader />);

    // Act and assert, step by step.
    // The mark is decorative here: the credit line already names the product.
    const mark = container.querySelector(".app-header-mark");
    expect(mark?.getAttribute("aria-hidden")).toBe("true");
    expect(container.firstElementChild?.firstElementChild).toBe(mark);
  });
});
