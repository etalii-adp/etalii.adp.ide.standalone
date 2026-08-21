import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { AppHeader } from "./AppHeader";

describe("AppHeader", () => {
  it("links Peter Vrenken to the project's git repository", () => {
    render(<AppHeader />);

    const link = screen.getByRole("link", { name: "Peter Vrenken" });
    expect(link.getAttribute("href")).toBe("https://github.com/vrenken/EtAlii.Adp");
    expect(link.getAttribute("target")).toBe("_blank");
    expect(link.getAttribute("rel")).toBe("noopener noreferrer");
  });
});
