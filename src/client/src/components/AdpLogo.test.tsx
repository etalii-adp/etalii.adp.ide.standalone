import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { AdpLogo } from "./AdpLogo";

describe("AdpLogo", () => {
  it("renders the product name as a single readable string", () => {
    // Act.
    const { container } = render(<AdpLogo />);

    // Assert.
    // The name is split across two spans to colour ".Adp" differently, so assert on
    // the joined text to catch a split that would break screen readers or copy/paste.
    expect(container.querySelector(".adp-logo-name")?.textContent).toBe("EtAlii.Adp");
    expect(screen.getByText(".Adp")).toHaveProperty("className", "adp-logo-suffix");
  });

  it("hides the decorative mark from assistive technology", () => {
    // Arrange.
    const { container } = render(<AdpLogo />);

    // Act and assert, step by step.
    const mark = container.querySelector("svg");
    expect(mark?.getAttribute("aria-hidden")).toBe("true");
    expect(mark?.getAttribute("viewBox")).toBe("0 0 32 32");
  });

  it("takes the highlight from a class so --color-primary resolves", () => {
    // Arrange.
    const { container } = render(<AdpLogo />);

    // Act and assert, step by step.
    // A var() in an SVG presentation attribute never resolves; the accent has to
    // stay a class the stylesheet targets.
    const accent = container.querySelector(".adp-mark-accent");
    expect(accent).not.toBeNull();
    expect(accent?.getAttribute("stroke")).toBeNull();
  });
});
