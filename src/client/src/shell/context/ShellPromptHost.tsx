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
  const placementFor = useInlineLabelPlacement();
  const [notice, setNotice] = useState("");
  // The interaction is over as far as this client is concerned, but the backend's own
  // acknowledgement of the cancel arrives over the stream a moment later. Without this the
  // dialog would appear in that gap - asking the user to rename something that is no longer
  // on the canvas, which is precisely the outcome the notice exists to replace.
  const [abandoned, setAbandoned] = useState(false);

  const inlineElementId = inlineLabelElementIdOf(prompt);
  const placedInline = inlineElementId !== null && placementFor(inlineElementId) !== null;

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

    if (inlineElementId !== null && claimedInline.current) {
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
  }, [inlineElementId, onCancel, placedInline, prompt]);

  const dismissNotice = useCallback(() => setNotice(""), []);

  return (
    <ContextPromptHost
      prompt={placedInline || abandoned ? null : prompt}
      onPropose={onPropose}
      onSubmit={onSubmit}
      onCancel={onCancel}
      notice={notice}
      onDismissNotice={dismissNotice}
    />
  );
}
