import { describe, expect, it } from "vitest";
import { splitDocuments } from "./splitDocuments";

/** The text of each part, so a case reads as the documents it expects. */
function texts(text: string): string[] {
  return splitDocuments(text).map((part) => text.slice(part.from, part.to));
}

describe("splitDocuments", () => {
  it("answers one part covering the whole text for one document, and for none", () => {
    expect(texts("name: web\nport: 80\n")).toEqual(["name: web\nport: 80\n"]);
    expect(texts("")).toEqual([""]);
    expect(texts("# only a comment\n")).toEqual(["# only a comment\n"]);
  });

  it("splits two and three documents at their markers, contiguously", () => {
    expect(texts("a: 1\n---\nb: 2\n")).toEqual(["a: 1\n", "---\nb: 2\n"]);
    expect(texts("---\na: 1\n---\nb: 2\n---\nc: 3\n")).toEqual(["---\na: 1\n", "---\nb: 2\n", "---\nc: 3\n"]);

    const text = "a: 1\n...\n---\nb: 2\n";
    const parts = splitDocuments(text);
    expect(texts(text)).toEqual(["a: 1\n...\n", "---\nb: 2\n"]);
    expect(parts[0].to).toBe(parts[1].from);
  });

  it("does not split at a marker inside a block scalar or a quoted string", () => {
    expect(texts("note: |\n  ---\n  more\nnext: 1\n")).toHaveLength(1);
    expect(texts('note: "a\n  ---\n  b"\n')).toHaveLength(1);
    expect(texts("note: '\n  ...\n  '\n")).toHaveLength(1);
  });

  it("reads the schema line before each document's first content, and only there", () => {
    const text = [
      "# yaml-language-server: $schema=web.schema.json",
      "name: web",
      "# yaml-language-server: $schema=late.schema.json",
      "---",
      "# yaml-language-server: $schema=worker.schema.json",
      "name: worker",
      "---",
      "name: none",
      "",
    ].join("\n");
    const parts = splitDocuments(text);
    expect(parts.map((part) => part.schemaReference)).toEqual(["web.schema.json", "worker.schema.json", undefined]);

    const line = parts[1].schemaLine!;
    expect(text.slice(line.from, line.to)).toBe("# yaml-language-server: $schema=worker.schema.json");
  });

  it("takes a schema line after a document end for the document that follows it", () => {
    const parts = splitDocuments("a: 1\n...\n# yaml-language-server: $schema=next.json\n---\nb: 2\n");
    expect(parts.map((part) => part.schemaReference)).toEqual([undefined, "next.json"]);
  });

  it("works on the panel's LF text of a file that was CRLF", () => {
    // The panel hands every analysis the editor's LF-only text; this is what a CRLF file becomes.
    const crlf = "# yaml-language-server: $schema=a.json\r\na: 1\r\n---\r\nb: 2\r\n";
    const lf = crlf.replace(/\r\n/g, "\n");
    expect(texts(lf)).toEqual(["# yaml-language-server: $schema=a.json\na: 1\n", "---\nb: 2\n"]);
    expect(splitDocuments(lf)[0].schemaReference).toBe("a.json");
  });
});
