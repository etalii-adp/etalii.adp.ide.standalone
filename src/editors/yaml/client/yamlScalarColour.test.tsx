import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it, vi } from "vitest";
import { render } from "@testing-library/react";
import { EditorView } from "@codemirror/view";
import { tags, type Tag } from "@lezer/highlight";
import { parseDocument } from "yaml";
import { BaseTextEditor } from "@client/editors/BaseTextEditor";
import { editorHighlight } from "@client/editors/editorHighlight";
import { yamlGrammar } from "./yamlGrammar";
import { scalarTagOf, yamlScalarColour } from "./yamlScalarColour";

/**
 * The colour of YAML through the shared scheme (Requirement 3.1, 3.2 and 3.4). jsdom applies no
 * CSS, so what is asserted is the scheme's own class on each piece of text, never a colour.
 */
function mount(text: string) {
  const rendered = render(<BaseTextEditor content={text} onChange={vi.fn()} extensions={[yamlGrammar, yamlScalarColour]} />);
  EditorView.findFromDOM(rendered.getByTestId("base-text-editor"));
  return rendered;
}

/** The text carrying the scheme's class for `tag`, span by span. */
function coloured(container: HTMLElement, tag: Tag): string[] {
  const style = editorHighlight.style([tag])!;
  expect(style).toBeTruthy();
  return [...container.querySelectorAll(".cm-content ." + style.split(" ")[0])].map((span) => span.textContent ?? "");
}

describe("plain scalars", () => {
  it.each([
    ["yes", tags.string, "string"],
    ["1.10", tags.number, 1.1],
    ["0x1f", tags.number, 31],
    ["~", tags.null, null],
    ["null", tags.null, null],
    ["true", tags.bool, true],
    ["2026-10-12", tags.string, "2026-10-12"],
    ["-12", tags.number, -12],
    [".inf", tags.number, Infinity],
  ])("%s is coloured as the type YAML 1.2 reads it as", (text, tag, read) => {
    // The type the `yaml` parser reads the value as, so the colour and a tool agree.
    const value = parseDocument(`v: ${text}`, { version: "1.2" }).toJS().v;
    expect(read === "string" ? typeof value : value).toEqual(read);
    expect(scalarTagOf(text)).toBe(tag);

    // And the class on the value in the editor.
    const { container } = mount(`v: ${text}\n`);
    expect(coloured(container, tag)).toContain(text);
  });

  it("keeps a key in the key's colour, whatever its text", () => {
    const { container } = mount("80: http\ntrue: yes\n");
    const keys = coloured(container, tags.definition(tags.propertyName));
    expect(keys).toEqual(expect.arrayContaining(["80", "true"]));
    expect(coloured(container, tags.number)).not.toContain("80");
  });
});

describe("each kind of token", () => {
  const text = [
    "%YAML 1.2",
    "---",
    "# a comment",
    "name: web",
    "quoted: 'single'",
    "port: 80",
    "on: true",
    "off: null",
    "defaults: &defaults {a: 1}",
    "copy: *defaults",
    "ref: !Ref other",
    "note: |",
    "  block text",
    "...",
    "",
  ].join("\n");

  it.each([
    ["a key", tags.definition(tags.propertyName), "name"],
    ["a string", tags.string, "'single'"],
    ["a plain string", tags.string, "web"],
    ["a number", tags.number, "80"],
    ["a boolean", tags.bool, "true"],
    ["null", tags.null, "null"],
    ["a comment", tags.lineComment, "# a comment"],
    ["an anchor", tags.attributeName, "&defaults"],
    ["an alias", tags.attributeName, "*defaults"],
    ["a tag", tags.tagName, "!Ref"],
    ["the document start", tags.meta, "---"],
    ["the document end", tags.meta, "..."],
    ["a block scalar's indicator", tags.special(tags.string), "|"],
  ])("%s carries the shared scheme's class", (_kind, tag, piece) => {
    const { container } = mount(text);
    expect(coloured(container, tag).map((span) => span.trim())).toContain(piece);
  });

  it("a block scalar's text carries the string class", () => {
    const { container } = mount(text);
    expect(coloured(container, tags.string).join("")).toContain("block text");
  });

  it("keeps the classes of what can still be read in a document with a mistake", () => {
    // An unclosed flow sequence further down, which the grammar cannot read to its end.
    const { container } = mount("name: web\nport: 80\nlist: [1, 2\nlast: true\n");
    expect(coloured(container, tags.definition(tags.propertyName))).toEqual(expect.arrayContaining(["name", "port"]));
    expect(coloured(container, tags.number)).toContain("80");
  });
});

describe("the module's colour", () => {
  it("declares none: no colour literal and no style of its own in the module's folder", () => {
    // The colours are the shared scheme's tokens (Requirement 3.3); the module only names tags.
    const folder = __dirname;
    const sources = readdirSync(folder).filter((name) => /\.(ts|tsx|css)$/.test(name) && !name.includes(".test."));
    expect(sources.length).toBeGreaterThan(0);
    for (const name of sources) {
      const source = readFileSync(join(folder, name), "utf8");
      expect(source, name).not.toMatch(/#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(|--color-|HighlightStyle\.define|EditorView\.theme/);
    }
  });
});
