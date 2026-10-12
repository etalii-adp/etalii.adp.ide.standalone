import { StateEffect, StateField, type Extension } from "@codemirror/state";
import { EditorView, ViewPlugin, hoverTooltip, keymap, showTooltip, type Tooltip, type ViewUpdate } from "@codemirror/view";
import { forEachDiagnostic, lintGutter, lintKeymap, setDiagnostics, type Diagnostic } from "@codemirror/lint";
import { autocompletion, snippet, type Completion, type CompletionContext, type CompletionResult } from "@codemirror/autocomplete";
import type { EditorAnalysis, EditorCompletion, EditorFinding, EditorFindingCounts, EditorHover } from "./editorAnalysis";

/**
 * Turns a module's {@link EditorAnalysis} into the shared presentation (json-editor Requirement
 * 9.3 and 9.4): findings underlined, marked in the gutter and readable on hover and from the
 * keyboard; a completion list that works from the keyboard alone; hover text. Knows no format.
 *
 * `BaseTextEditor` builds its state once at mount, so these extensions are built once too and
 * read what changes - the analysis itself, the counts callback - through references.
 */
export interface AnalysisReferences {
  readonly analysis: { readonly current: EditorAnalysis | undefined };
  readonly onCounts?: { readonly current: ((counts: EditorFindingCounts) => void) | undefined };
}

export interface AnalysisOptions {
  /** How long typing must pause before the text is analysed (Performance: typing never waits). */
  delay?: number;
}

/** Asks the view to analyse its text again, as when a schema the analysis reads has changed. */
export const reanalyse = StateEffect.define<null>();

export function analysisExtensions(references: AnalysisReferences, options: AnalysisOptions = {}): Extension[] {
  return [
    findingsRunner(references, options.delay ?? 400),
    lintGutter(),
    keymap.of(lintKeymap),
    findingStyles,
    countsReporter(references),
    autocompletion({ override: [completionSource(references)], activateOnTyping: true, icons: false }),
    hoverTooltip(hoverSource(references)),
    caretHover(references),
  ];
}

/** The underline differs by line style per severity, so colour is not the only carrier (Requirement 4.4). */
const findingStyles = EditorView.theme({
  ".cm-lintRange-error": { backgroundImage: "none", textDecoration: "underline wavy var(--color-danger)", textUnderlineOffset: "3px" },
  ".cm-lintRange-warning": { backgroundImage: "none", textDecoration: "underline dashed var(--color-warning)", textUnderlineOffset: "3px" },
  ".cm-lintRange-info": { backgroundImage: "none", textDecoration: "underline dotted var(--color-text-muted)", textUnderlineOffset: "3px" },
  ".cm-tooltip": { backgroundColor: "var(--color-surface)", color: "var(--color-text)", border: "1px solid var(--color-border)" },
  ".cm-editor-hover": { padding: "0.2rem 0.4rem", maxWidth: "40rem", whiteSpace: "pre-wrap" },
});

function toDiagnostic(finding: EditorFinding, length: number): Diagnostic {
  const from = Math.max(0, Math.min(finding.from, length));
  const to = Math.max(from, Math.min(finding.to, length));
  return { from, to, severity: finding.severity, message: finding.message, source: finding.source };
}

/**
 * Runs the analysis after a pause in typing, and again on {@link reanalyse}. An answer is applied
 * only while the view still shows the text it was asked about: typing on while the analysis
 * works must never leave findings for text that is gone on screen (Requirement 4.6).
 */
function findingsRunner({ analysis }: AnalysisReferences, delay: number) {
  return ViewPlugin.fromClass(
    class {
      private timer: ReturnType<typeof setTimeout> | undefined;
      private destroyed = false;

      constructor(private readonly view: EditorView) {
        this.schedule();
      }

      update(update: ViewUpdate) {
        if (update.docChanged || update.transactions.some((transaction) => transaction.effects.some((effect) => effect.is(reanalyse)))) {
          this.schedule();
        }
      }

      destroy() {
        this.destroyed = true;
        clearTimeout(this.timer);
      }

      private schedule() {
        clearTimeout(this.timer);
        this.timer = setTimeout(() => void this.run(), delay);
      }

      private async run() {
        const current = analysis.current;
        const text = this.view.state.doc.toString();
        let found: EditorFinding[] = [];
        if (current) {
          try {
            found = await current.findings(text);
          } catch {
            // An analysis that throws keeps what it showed; saying why is the module's status.
            return;
          }
        }

        if (this.destroyed || this.view.state.doc.toString() !== text) {
          return;
        }

        const length = this.view.state.doc.length;
        this.view.dispatch(setDiagnostics(this.view.state, found.map((finding) => toDiagnostic(finding, length))));
      }
    },
  );
}

function countsOf(view: EditorView): EditorFindingCounts {
  const counts = { errors: 0, warnings: 0 };
  forEachDiagnostic(view.state, (diagnostic) => {
    if (diagnostic.severity === "error") {
      counts.errors++;
    } else if (diagnostic.severity === "warning") {
      counts.warnings++;
    }
  });
  return counts;
}

/** Reports the counts of errors and warnings to the status line whenever they change (Requirement 4.5). */
function countsReporter({ onCounts }: AnalysisReferences) {
  let last = "";
  return EditorView.updateListener.of((update) => {
    const counts = countsOf(update.view);
    const key = `${counts.errors}/${counts.warnings}`;
    if (key !== last) {
      last = key;
      onCounts?.current?.(counts);
    }
  });
}

/** Whether an insert carries tab stops and goes through the snippet engine rather than as plain text. */
const tabStop = /[#$]\{\d/;

function toCompletion(item: EditorCompletion): Completion {
  return {
    label: item.filterText ?? item.label,
    displayLabel: item.label,
    detail: item.detail,
    // A string `info` is rendered by the package as a text node: a schema's description that
    // holds markup stays text (Security).
    info: item.info,
    boost: item.boost,
    apply: (view, completion, from, to) => {
      // The item's own range, stretched to the caret: the list may have been narrowed by typing
      // since the analysis answered, and what was typed belongs to the replaced range.
      const end = Math.max(to, item.to);
      const start = Math.min(from, item.from);
      if (tabStop.test(item.insert)) {
        snippet(item.insert)(view, completion, start, end);
      } else {
        view.dispatch({ changes: { from: start, to: end, insert: item.insert }, selection: { anchor: start + item.insert.length }, userEvent: "input.complete" });
      }
    },
  };
}

function completionSource({ analysis }: AnalysisReferences) {
  return async (context: CompletionContext): Promise<CompletionResult | null> => {
    const current = analysis.current;
    if (!current?.completions) {
      return null;
    }

    const list = await current.completions(context.state.doc.toString(), context.pos, context.explicit);
    if (context.aborted || !list || list.items.length === 0) {
      return null;
    }

    const from = Math.min(...list.items.map((item) => item.from), context.pos);
    return { from, options: list.items.map(toCompletion) };
  };
}

function hoverElement(hover: EditorHover): Tooltip {
  return {
    pos: hover.from,
    end: hover.to,
    above: true,
    create: () => {
      const dom = document.createElement("div");
      dom.className = "cm-editor-hover";
      dom.setAttribute("role", "tooltip");
      // Text, never markup: a schema's description is data (Security).
      dom.textContent = hover.text;
      return { dom };
    },
  };
}

function hoverSource({ analysis }: AnalysisReferences) {
  return async (view: EditorView, pos: number): Promise<Tooltip | null> => {
    const current = analysis.current;
    if (!current?.hover) {
      return null;
    }

    const hover = await current.hover(view.state.doc.toString(), pos);
    return hover ? hoverElement(hover) : null;
  };
}

const setCaretHover = StateEffect.define<Tooltip | null>();

/**
 * The hover text from the keyboard (Requirement 7.6): Ctrl+K Ctrl+I, as in VS Code, shows what
 * the pointer would show at the caret; moving the caret, typing or Escape closes it.
 */
function caretHover(references: AnalysisReferences): Extension {
  const field = StateField.define<Tooltip | null>({
    create: () => null,
    update(value, transaction) {
      for (const effect of transaction.effects) {
        if (effect.is(setCaretHover)) {
          return effect.value;
        }
      }
      return transaction.docChanged || transaction.selection ? null : value;
    },
    provide: (self) => showTooltip.from(self),
  });

  const show = (view: EditorView) => {
    const current = references.analysis.current;
    if (!current?.hover) {
      return false;
    }

    const text = view.state.doc.toString();
    const at = view.state.selection.main.head;
    void current.hover(text, at).then((hover) => {
      if (hover && view.state.doc.toString() === text && view.state.selection.main.head === at) {
        view.dispatch({ effects: setCaretHover.of(hoverElement(hover)) });
      }
    });
    return true;
  };

  const hide = (view: EditorView) => {
    if (!view.state.field(field)) {
      return false;
    }
    view.dispatch({ effects: setCaretHover.of(null) });
    return true;
  };

  return [field, keymap.of([{ key: "Mod-k Mod-i", run: show }, { key: "Escape", run: hide }])];
}
