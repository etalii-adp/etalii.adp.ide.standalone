import { describe, expect, it } from "vitest";
import { folderPlaceholder } from "./folderPlaceholder";

describe("folderPlaceholder", () => {
  it("shows a drive letter on Windows and a home folder on macOS and Linux", () => {
    expect(folderPlaceholder("Win32")).toBe("C:\\path\\to\\project");
    expect(folderPlaceholder("Windows")).toBe("C:\\path\\to\\project");
    expect(folderPlaceholder("MacIntel")).toBe("/Users/you/project");
    expect(folderPlaceholder("macOS")).toBe("/Users/you/project");
    expect(folderPlaceholder("Linux x86_64")).toBe("/home/you/project");
  });
});
