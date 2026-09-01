import { markdown } from "@codemirror/lang-markdown";
import { marked } from "marked";
import type { DiagramCanvasProps } from "@client/shell/panels/diagramCanvas";
import { TextEditorPanel } from "@client/editors/TextEditorPanel";

/**
 * The markdown editor: the shared text panel plus what makes markdown worth its own module
 * (modular-text-editors R10.2) - a rendered preview beside the text, and heading-aware
 * navigation above it.
 */
export function MarkdownEditorPanel(props: DiagramCanvasProps) {
  return (
    <TextEditorPanel
      {...props}
      extensions={[markdown()]}
      header={(text, goToLine) => <MarkdownOutline text={text} goToLine={goToLine} />}
      aside={(text) => (
        <div
          className="markdown-preview"
          data-testid="markdown-preview"
          // marked renders this module's own document for its author; the preview is the
          // module's whole reason to exist, and the content is the user's own file.
          dangerouslySetInnerHTML={{ __html: marked.parse(text, { async: false }) }}
        />
      )}
    />
  );
}

/** Every heading, clickable: the outline scrolls the editor to the heading's line. */
export function MarkdownOutline({ text, goToLine }: { text: string; goToLine: (line: number) => void }) {
  const headings = headingsOf(text);
  if (headings.length === 0) {
    return null;
  }

  return (
    <nav className="markdown-outline" aria-label="Headings">
      {headings.map((heading) => (
        <button key={heading.line} type="button" onClick={() => goToLine(heading.line)}>
          {"#".repeat(heading.level)} {heading.title}
        </button>
      ))}
    </nav>
  );
}

export interface MarkdownHeading {
  level: number;
  title: string;
  /** 1-based, the editor's own line numbering. */
  line: number;
}

/** ATX headings only - the form this repository's own documents use throughout. */
export function headingsOf(text: string): MarkdownHeading[] {
  const headings: MarkdownHeading[] = [];
  const lines = text.split("\n");
  let inFence = false;
  for (let index = 0; index < lines.length; index++) {
    const line = lines[index];
    if (line.trimStart().startsWith("```")) {
      inFence = !inFence;
      continue;
    }
    if (inFence) {
      continue; // a # inside a code fence is a comment, not a heading
    }
    const match = /^(#{1,6})\s+(.+?)\s*#*\s*$/.exec(line);
    if (match) {
      headings.push({ level: match[1].length, title: match[2], line: index + 1 });
    }
  }

  return headings;
}
