import { describe, expect, it, vi } from "vitest";
import { classifyLink, openLink } from "./openLink";

const document = ["planning", "activity", "team.adp"];

describe("what a link is", () => {
  it("takes an http or https address as a web page", () => {
    expect(classifyLink("https://github.com/etalii-adp/etalii.adp/pull/103", document)).toEqual({
      kind: "web",
      href: "https://github.com/etalii-adp/etalii.adp/pull/103",
    });
    expect(classifyLink("  HTTP://example.org/a b  ", document).kind).toBe("web");
  });

  it("refuses every other scheme, by not listing it", () => {
    // The planted defect this was seen to fail against: the scheme check removed, so a
    // `javascript:` value is read as a relative path and handed on.
    for (const link of ["javascript:alert(1)", "JAVASCRIPT:alert(1)", "data:text/html,<p>x</p>", "file:///etc/passwd", "vbscript:x", "ftp://example.org/x", ""]) {
      expect(classifyLink(link, document).kind, link).toBe("refused");
    }
  });

  it("takes a relative path as relative to the document's folder, inside the project", () => {
    expect(classifyLink("../specs/knowledge/requirements.md", document)).toEqual({
      kind: "project",
      segments: ["planning", "specs", "knowledge", "requirements.md"],
    });
    expect(classifyLink("notes\\today.md", document)).toEqual({ kind: "project", segments: ["planning", "activity", "notes", "today.md"] });
    expect(classifyLink("./a/./b", document)).toEqual({ kind: "project", segments: ["planning", "activity", "a", "b"] });
  });

  it("takes a path that leaves the project, or is absolute, as somewhere this page cannot open", () => {
    expect(classifyLink("../../../outside.md", document).kind).toBe("elsewhere");
    expect(classifyLink("C:\\git\\etalii.adp\\.claude\\worktrees\\kd", document)).toEqual({
      kind: "elsewhere",
      location: "C:\\git\\etalii.adp\\.claude\\worktrees\\kd",
    });
    expect(classifyLink("/home/agent/work", document).kind).toBe("elsewhere");
    expect(classifyLink("\\\\fractal\\share\\work", document).kind).toBe("elsewhere");
  });
});

describe("opening a link", () => {
  const opening = () => ({ revealPath: vi.fn(), openWindow: vi.fn(), notify: vi.fn() });

  it("opens a web address in a new tab and nothing else", () => {
    const page = opening();

    openLink("https://example.org/x", document, page);

    expect(page.openWindow).toHaveBeenCalledExactlyOnceWith("https://example.org/x");
    expect(page.revealPath).not.toHaveBeenCalled();
    expect(page.notify).not.toHaveBeenCalled();
  });

  it("opens a new tab that gets no handle on this page", () => {
    const open = vi.spyOn(window, "open").mockImplementation(() => null);
    try {
      openLink("https://example.org/x", document, { revealPath: vi.fn() });

      expect(open).toHaveBeenCalledExactlyOnceWith("https://example.org/x", "_blank", "noopener,noreferrer");
    } finally {
      open.mockRestore();
    }
  });

  it("reveals a path inside the project in the workspace tree", () => {
    const page = opening();

    openLink("../specs/knowledge/requirements.md", document, page);

    expect(page.revealPath).toHaveBeenCalledExactlyOnceWith(["planning", "specs", "knowledge", "requirements.md"]);
    expect(page.openWindow).not.toHaveBeenCalled();
  });

  it("shows a location it cannot open, says why, and offers it for copying", () => {
    const page = opening();

    openLink("C:\\work\\kd", document, page);

    expect(page.notify).toHaveBeenCalledExactlyOnceWith("C:\\work\\kd is outside this project, so it cannot be opened from here.", "C:\\work\\kd");
    expect(page.openWindow).not.toHaveBeenCalled();
    expect(page.revealPath).not.toHaveBeenCalled();
  });

  it("never hands a refused link to the browser", () => {
    const page = opening();

    openLink("javascript:alert(1)", document, page);

    expect(page.openWindow).not.toHaveBeenCalled();
    expect(page.revealPath).not.toHaveBeenCalled();
    expect(page.notify).toHaveBeenCalledTimes(1);
    expect(page.notify.mock.calls[0][0]).toContain("was not opened");
  });
});
