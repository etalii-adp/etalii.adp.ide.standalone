import { useCallback, useEffect, useRef, useState } from "react";
import { ContextPromptHost } from "./ContextPromptHost";
import { useContextPrompt } from "./ContextConnectionProvider";
import { inlineLabelElementIdOf } from "./inlineLabelPrompt";
import { useInlineLabelPlacement } from "../panels/InlineLabelPlacementContext";

/** What the user is told when the thing they were renaming stopped existing mid-edit. */
const VANISHED = "That element is no longer there, so the edit was abandoned. Nothing was changed.";

/**
 * Mounts the prompt host once, at the shell level, on the connection's context stream -
 * so a dialog raised by an action from any surface (explorer, ribbon, a canvas) shows
 * regardless of which panel triggered it.
 *
 * It also decides, for a prompt the backend marked as editing a visible label, whether to stand
 * down and let a canvas render it in place. The decision is not made here: it asks the placement
 * registry, which is the same function the canvas itself asks, so the two cannot answer
 * differently (inline-rename Requirements 2.3, 2.4).
 *
 * The fallback is silent and always available. An unmarked prompt, a marked prompt for something
 * no mounted canvas draws, a rename invoked from the explorer where there is no canvas at all -
 * every one of those renders exactly the dialog it rendered before this feature existed.
 */
export function ShellPromptHost() {
  const { prompt, onPropose, onSubmit, onCancel } = useContextPrompt();
  const { placementFor, anyCanvasRegistered } = useInlineLabelPlacement();
  const [notice, setNotice] = useState("");
  // The interaction is over as far as this client is concerned, but the backend's own
  // acknowledgement of the cancel arrives over the stream a moment later. Without this the
  // dialog would appear in that gap - asking the user to rename something that is no longer
  // on the canvas, which is precisely the outcome the notice exists to replace.
  const [abandoned, setAbandoned] = useState(false);

  const inlineElementId = inlineLabelElementIdOf(prompt);
  const placedInline = inlineElementId !== null && placementFor(inlineElementId) !== null;

  /**
   * A marked prompt while no canvas is registered at all: the answer is not in yet, not "no".
   * Nothing is rendered for that instant, and in particular NOT the dialog.
   *
   * This is not tidiness, it is the fix for a defect that killed every rename outright. A canvas
   * registers in an effect, so a prompt arriving in the same commit is unplaceable for exactly
   * one render - the dialog would open and close again within two frames. Nobody sees the flash,
   * but `Dialog`'s cleanup restores focus to whatever had it before, which lands *after* the
   * inline editor has mounted and focused itself. The editor is blurred by that restore, blur
   * commits, the value is unchanged, and an unchanged value ends the interaction - so the editor
   * cancelled itself the instant it appeared, and the rename died with no error anywhere.
   *
   * A marked prompt always comes from a module whose canvas is on screen, so this state is
   * transient by construction. An unmarked prompt - the explorer's rename, and every other input
   * site - never reaches here and is unaffected.
   */
  const awaitingCanvas = inlineElementId !== null && !anyCanvasRegistered;

  // Whether the interaction still on screen was claimed by a canvas. Kept so that a placement
  // turning from answered to null can be told apart from a prompt that was never inline: the
  // first means the element went away mid-edit, and falling back to a dialog there would ask
  // the user to rename something that is no longer on the canvas.
  const claimedInline = useRef(false);

  useEffect(() => {
    if (placedInline) {
      claimedInline.current = true;
      return;
    }

    // Only a canvas that is mounted and cannot find the element is evidence the element is gone.
    // An empty registry means no canvas is mounted at all - a panel between mounts, a tab being
    // switched - and reading that as a disappearance abandons an edit that nothing happened to.
    if (inlineElementId !== null && claimedInline.current && anyCanvasRegistered) {
      claimedInline.current = false;
      setNotice(VANISHED);
      setAbandoned(true);
      onCancel();
      return;
    }

    if (prompt === null) {
      claimedInline.current = false;
      setAbandoned(false);
    }
  }, [anyCanvasRegistered, inlineElementId, onCancel, placedInline, prompt]);

  const dismissNotice = useCallback(() => setNotice(""), []);

  return (
    <ContextPromptHost
      prompt={placedInline || abandoned || awaitingCanvas ? null : prompt}
      onPropose={onPropose}
      onSubmit={onSubmit}
      onCancel={onCancel}
      notice={notice}
      onDismissNotice={dismissNotice}
    />
  );
}
