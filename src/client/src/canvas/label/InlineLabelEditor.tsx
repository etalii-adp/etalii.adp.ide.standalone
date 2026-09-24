import { useEffect, useRef } from "react";
import { useContextPromptEntry } from "../../shell/context/useContextPromptEntry";
import type { ContextPromptSubmission, ContextPromptVerdict } from "../../shell/context/ContextPromptHost";
import type { LabelPlacement } from "../../shell/panels/InlineLabelPlacementContext";
import "./inlineLabelEditor.css";

/** How much taller the box gets while a refusal is showing under the field, in canvas units. */
const MESSAGE_HEIGHT = 18;

export interface InlineLabelEditorProps {
  /** Where the label is and what it currently reads, in the canvas's own units. */
  placement: LabelPlacement;
  onPropose: (revision: number, value: string) => Promise<ContextPromptVerdict>;
  onSubmit: (value: string, text?: string) => Promise<ContextPromptSubmission>;
  onCancel: () => void;
  /** Called when the editor closes, so the canvas can take its keyboard focus back. */
  onReturnFocus?: () => void;
}

/**
 * A textbox drawn in place of a label, inside the canvas's own `svg`.
 *
 * It renders a textbox and reports intent. It does not know what a rename is, cannot decide
 * what is renameable, and imports nothing from any diagram module: the module says which prompts
 * edit a visible label, the canvas says where that label is, and this draws the box.
 *
 * **It is mounted in canvas units, not screen pixels, and that is the design.** A `foreignObject`
 * at the label's own rectangle is carried by the `viewBox` like everything else drawn there, so
 * panning and zooming move the editor with its element without a line of tracking code
 * (inline-rename Requirement 5.1). An editor positioned in screen pixels would need to follow
 * every view change, and would drift on the ones it missed.
 *
 * Four behaviours are easy to get backwards and all four are deliberate:
 *
 * - **Blur commits.** Losing typed text to a stray click is the failure users resent most, and
 *   this is the rule most likely to be "fixed" the other way by a later reader (Requirement 4.4).
 * - **A refusal keeps the editor open, with the text intact** (Requirement 4.5), so the value can
 *   be adjusted rather than retyped.
 * - **An unchanged value dispatches nothing** and records no history entry (Requirement 4.6):
 *   opening an editor and pressing Enter is not an edit.
 * - **Enter commits whatever is typed**, rather than waiting for a current verdict the way the
 *   dialog's confirm button does. See the note at `commit` for why they differ here.
 *
 * <b>A multiline placement renders a textarea instead, and only Enter behaves differently there.</b>
 * Blur still commits, Escape still cancels, a refusal still holds the box open: a second field is
 * the obvious place for one of those to be quietly dropped, so they are shared rather than copied.
 */
export function InlineLabelEditor({ placement, onPropose, onSubmit, onCancel, onReturnFocus }: InlineLabelEditorProps) {
  const entry = useContextPromptEntry({ initialValue: placement.text, onPropose, onSubmit });
  const field = useRef<HTMLInputElement | HTMLTextAreaElement | null>(null);
  const multiline = placement.multiline === true;

  // Set the moment the editor starts closing, so the blur that closing causes is not read as a
  // second commit. Without it, Enter commits and then the unmount's blur commits again.
  const closing = useRef(false);

  useEffect(() => {
    const input = field.current;
    input?.focus();
    // Selected, not just focused: typing replaces the old label, while a click places a caret -
    // which is what in-place editing does everywhere else (Requirement 4.1).
    input?.select();
  }, []);

  useEffect(() => {
    // Reset on every run, and this line is load-bearing. React's StrictMode double-invokes
    // effects in development - mount, clean up, mount again - so the cleanup below fires once
    // while the editor is very much alive. Without this the flag latched true at first paint and
    // `commit` returned immediately for the rest of the editor's life: the box appeared, took
    // focus, accepted typing, and then Enter, blur and every other route to committing did
    // nothing at all, silently. A real unmount leaves it true, which is harmless - the component
    // is gone.
    closing.current = false;

    return () => {
      // Marked BEFORE the focus goes back, and this order is the whole point. Handing focus to
      // the canvas blurs this input, and a blur is a commit - so an editor being torn down would
      // commit itself on the way out. With an unchanged value that commit ends the interaction,
      // which is how a rename came to cancel itself the instant it appeared, silently and with
      // nothing in any log to say why. A blur caused by this component going away is not the
      // user clicking elsewhere, and must not be read as one.
      closing.current = true;
      onReturnFocus?.();
    };
  }, [onReturnFocus]);

  const commit = async () => {
    if (closing.current) {
      return;
    }

    if (entry.value === placement.text) {
      // Nothing to say to the backend, so nothing is said: no command, no history entry.
      closing.current = true;
      onCancel();
      return;
    }

    // A value the module has ALREADY judged and refused is never sent. The refusal is on screen
    // beside the field, so the edit stays open for the user to fix (Requirement 4.5).
    //
    // This was not the first shape, and the first shape was wrong. The editor used to submit
    // whatever was typed, reasoning that nothing could be bypassed because the handler validates
    // its own preconditions. It does not: a module's ValidateAsync runs on propose, and
    // SubmitInteraction does not run it again. So typing an empty name into a C4 element showed
    // "Every C4 element needs a name." and then wrote `container ""` to the document anyway - the
    // dialog never allowed that only because its confirm button is disabled while a verdict is
    // invalid. Found by running the app; the document was restored afterwards.
    if (entry.refused) {
      return;
    }

    // With no current verdict - inside the 200ms debounce - the value IS sent. An inline editor
    // has no button to disable, and a keystroke that silently does nothing is worse than a
    // refusal that comes back through the submission result.
    const result = await entry.submit();
    if (result?.completed === true) {
      closing.current = true;
    }
  };

  const abandon = () => {
    closing.current = true;
    onCancel();
  };

  /**
   * What Enter means, which is the one behaviour the two fields do not share.
   *
   * In a textarea Enter is text the user is typing, so it is neither committed nor prevented - the
   * browser's own default is what inserts the newline, and preventing it is precisely the defect
   * that makes a wrapped label impossible to write. Ctrl or Cmd with Enter is the commit, the
   * gesture every multi-line field in every editor uses for "done".
   */
  const keyDown = (event: { key: string; ctrlKey: boolean; metaKey: boolean; preventDefault: () => void }) => {
    if (event.key === "Escape") {
      event.preventDefault();
      abandon();
      return;
    }
    if (event.key !== "Enter") {
      // Everything else belongs to the textbox. The canvas already declines to act on keys
      // whose target is a text input, so Delete and its kin type rather than delete
      // (Requirement 8.4); stopping propagation here would be a second, divergent copy of
      // that rule.
      return;
    }
    if (multiline && !event.ctrlKey && !event.metaKey) {
      return;
    }
    event.preventDefault();
    void commit();
  };

  const shared = {
    className: "inline-label-editor-field",
    "aria-label": "Label",
    value: entry.value,
    onBlur: () => void commit(),
    onKeyDown: keyDown,
  };

  const height = placement.height + (entry.message ? MESSAGE_HEIGHT : 0);

  return (
    <foreignObject
      className="inline-label-editor"
      x={placement.x}
      y={placement.y}
      width={placement.width}
      height={height}
    >
      <div className="inline-label-editor-body">
        {multiline ? (
          <textarea
            ref={(element) => {
              field.current = element;
            }}
            {...shared}
            onChange={(event) => entry.setValue(event.target.value)}
          />
        ) : (
          <input
            ref={(element) => {
              field.current = element;
            }}
            {...shared}
            type="text"
            onChange={(event) => entry.setValue(event.target.value)}
          />
        )}
        {entry.message && (
          <p className="inline-label-editor-error" role="alert">
            {entry.message}
          </p>
        )}
      </div>
    </foreignObject>
  );
}
