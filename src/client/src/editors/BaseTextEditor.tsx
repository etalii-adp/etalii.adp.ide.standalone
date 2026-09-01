import { useEffect, useRef } from "react";
import { EditorState, type Extension } from "@codemirror/state";
import { EditorView, keymap, lineNumbers } from "@codemirror/view";
import { defaultKeymap, history, historyKeymap } from "@codemirror/commands";

/**
 * The editor surface in the application's own colors. Without this, CodeMirror runs its
 * built-in LIGHT base theme whatever the app shows: a #f5f5f5 gutter with gray numbers, and -
 * the invisible half of the bug - `caret-color: black`, which on the dark surface makes the
 * blinking caret disappear entirely while the text stays perfectly editable. Bound to the
 * shell's CSS variables rather than literal colors, so the same extension is right in both
 * light and dark mode and follows a theme switch live.
 */
const appTheme = EditorView.theme({
  "&": {
    backgroundColor: "var(--color-surface)",
    color: "var(--color-text)",
  },
  ".cm-content": {
    caretColor: "var(--color-text)",
  },
  // The drawn cursor some extensions add; kept in step so a module adding drawSelection
  // does not resurface the invisible-caret bug.
  ".cm-cursor, .cm-dropCursor": {
    borderLeftColor: "var(--color-text)",
  },
  ".cm-gutters": {
    backgroundColor: "var(--color-surface)",
    color: "var(--color-text-muted)",
    borderRight: "1px solid var(--color-border)",
  },
});

/**
 * The one text-editing surface every editor module wraps (modular-text-editors task 5.1):
 * CodeMirror 6, chosen over Monaco because its modular per-language packages match the
 * plugin-per-module shape and it needs no bespoke worker/Vite configuration touching shared
 * build files. Deliberately minimal - language extensions, previews and navigation belong to
 * each module's own client code, exactly as a diagram type's drawing belongs to its canvas.
 */
export interface BaseTextEditorProps {
  /** The text to show. Applied from outside only when it differs from the view's own state. */
  content: string;
  /** Called with the full new text on every user edit. */
  onChange: (content: string) => void;
  /** Module-supplied CodeMirror extensions - a language mode, a theme. */
  extensions?: Extension[];
  /** Called with (line) when the caret moves to a new line; heading navigation reads this back. */
  onSave?: () => void;
}

export function BaseTextEditor({ content, onChange, extensions = [], onSave }: BaseTextEditorProps) {
  const hostRef = useRef<HTMLDivElement | null>(null);
  const viewRef = useRef<EditorView | null>(null);
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;
  const onSaveRef = useRef(onSave);
  onSaveRef.current = onSave;

  useEffect(() => {
    if (!hostRef.current) {
      return;
    }

    const view = new EditorView({
      state: EditorState.create({
        doc: content,
        extensions: [
          appTheme,
          lineNumbers(),
          history(),
          keymap.of([
            // Ctrl+S dispatches the module's save rather than the browser's dialog.
            {
              key: "Mod-s",
              run: () => {
                onSaveRef.current?.();
                return true;
              },
            },
            ...defaultKeymap,
            ...historyKeymap,
          ]),
          EditorView.updateListener.of((update) => {
            if (update.docChanged) {
              onChangeRef.current(update.state.doc.toString());
            }
          }),
          ...extensions,
        ],
      }),
      parent: hostRef.current,
    });
    viewRef.current = view;
    return () => {
      view.destroy();
      viewRef.current = null;
    };
    // The view owns its own state after mount; content flows in through the effect below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    const view = viewRef.current;
    if (!view) {
      return;
    }

    // Outside content (a reload after an external change) replaces the doc only when it
    // genuinely differs, so the user's own keystrokes never bounce through this path.
    const current = view.state.doc.toString();
    if (current !== content) {
      view.dispatch({ changes: { from: 0, to: current.length, insert: content } });
    }
  }, [content]);

  return <div ref={hostRef} className="base-text-editor" data-testid="base-text-editor" />;
}

/** Scrolls the editor to a 1-based line - the go-to mechanism heading navigation uses. */
export function scrollToLine(host: HTMLElement, line: number) {
  const view = EditorView.findFromDOM(host);
  if (!view) {
    return;
  }

  const clamped = Math.max(1, Math.min(line, view.state.doc.lines));
  const position = view.state.doc.line(clamped).from;
  view.dispatch({ selection: { anchor: position }, scrollIntoView: true });
  view.focus();
}
