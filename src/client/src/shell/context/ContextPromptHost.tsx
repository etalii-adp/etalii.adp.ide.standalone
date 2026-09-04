import { useEffect, useId, useState } from "react";
import { Dialog } from "../../components/Dialog";
import { ConfirmDialog } from "../../components/ConfirmDialog";
import { ChoicePromptDialog } from "./ChoicePromptDialog";
import { useContextPromptEntry } from "./useContextPromptEntry";
import type { ContextPrompt } from "../../generated/context_pb";

export interface ContextPromptVerdict {
  /** The revision the backend judged; a reply for older text is recognisable as stale. */
  revision: number;
  valid: boolean;
  reason: string;
}

export interface ContextPromptSubmission {
  completed: boolean;
  error: string;
  /** Project-relative segments of what the interaction created, when it created something. */
  createdPath?: string[];
}

export interface ContextPromptHostProps {
  prompt: ContextPrompt | null;
  onPropose: (revision: number, value: string) => Promise<ContextPromptVerdict>;
  onSubmit: (value: string, text?: string) => Promise<ContextPromptSubmission>;
  onCancel: () => void;
}

/** The prompt kinds this host knows how to put on screen. */
const RENDERABLE_PROMPTS = new Set(["inputDialog", "confirmDialog", "choiceDialog", "closed"]);

/**
 * Renders whichever dialog the backend's current prompt asks for. It is driven purely by
 * prompt data and never asks what the running action is, so an action added later gets its
 * dialog here without a new component or a new branch.
 *
 * A prompt this build does not know - a newer backend asking for a dialog kind added after
 * this client was built, or a client running against a contract it was not generated from -
 * is not silently dropped. Dropping it would leave the user's click doing nothing at all and
 * the backend holding an interaction that is never answered; instead the interaction is
 * cancelled and the user is told, so the action visibly ends rather than vanishing.
 */
export function ContextPromptHost({ prompt, onPropose, onSubmit, onCancel }: ContextPromptHostProps) {
  const promptCase = prompt?.prompt.case;
  const unsupported = prompt !== null && !RENDERABLE_PROMPTS.has(promptCase ?? "");
  const [unsupportedNotice, setUnsupportedNotice] = useState(false);

  useEffect(() => {
    if (unsupported) {
      if (import.meta.env.DEV) {
        console.warn(
          `ContextPromptHost: the backend asked for a '${promptCase ?? "(unset)"}' prompt, which this build cannot render` +
            " - cancelling the interaction. If the contract changed, the generated client stubs are probably stale.",
        );
      }
      setUnsupportedNotice(true);
      onCancel();
      return;
    }

    if (prompt !== null) {
      // A prompt that can be rendered takes over from any notice still on screen.
      setUnsupportedNotice(false);
    }
  }, [onCancel, prompt, promptCase, unsupported]);

  if (prompt === null || unsupported) {
    // Either there is nothing to show, or there was something this build could not show -
    // whose interaction the effect above has already cancelled.
    return unsupportedNotice ? (
      <Dialog compact
        open
        icon="mdi-alert-circle-outline"
        title="Action not supported"
        buttons={[{ key: "close", label: "Close", color: "neutral", autoFocus: true, onClick: () => setUnsupportedNotice(false) }]}
        onClose={() => setUnsupportedNotice(false)}
      >
        <p className="dialog-message">
          This action needs a newer version of the app than the one you are running. Nothing was changed.
        </p>
      </Dialog>
    ) : null;
  }

  switch (prompt.prompt.case) {
    case "inputDialog":
      return (
        <InputPromptDialog
          // Remounting per interaction is what resets the typed value, revision and verdict
          // together; carrying any of them across two interactions would be a bug.
          key={keyOf(prompt)}
          prompt={prompt.prompt.value}
          onPropose={onPropose}
          onSubmit={onSubmit}
          onCancel={onCancel}
        />
      );

    case "confirmDialog": {
      const confirm = prompt.prompt.value;
      return (
        <ConfirmDialog
          open
          icon={confirm.icon}
          title={confirm.title}
          message={confirm.message}
          confirmLabel={confirm.confirmLabel}
          confirmColor={confirm.danger ? "danger" : "primary"}
          onConfirm={() => void onSubmit("")}
          onCancel={onCancel}
        />
      );
    }

    case "choiceDialog":
      return (
        <ChoicePromptDialog
          // Remounted per interaction for the same reason as the input dialog: the chosen
          // option, the expansion state and any error belong to one interaction only.
          key={keyOf(prompt)}
          prompt={prompt.prompt.value}
          onPropose={onPropose}
          onSubmit={onSubmit}
          onCancel={onCancel}
        />
      );

    case "closed":
      return (
        <Dialog compact
          open
          icon="mdi-alert-circle-outline"
          title="Action cancelled"
          buttons={[{ key: "close", label: "Close", color: "neutral", autoFocus: true, onClick: onCancel }]}
          onClose={onCancel}
        >
          <p className="dialog-message">{prompt.prompt.value.message}</p>
        </Dialog>
      );

    default:
      // Unreachable: an unrenderable prompt was handled above.
      return null;
  }
}

function keyOf(prompt: ContextPrompt): string {
  return Array.from(prompt.interactionId?.value ?? [], (byte) => byte.toString(16).padStart(2, "0")).join("");
}

interface InputPromptDialogProps {
  prompt: { title: string; icon: string; fieldLabel: string; initialValue: string; confirmLabel: string };
  onPropose: (revision: number, value: string) => Promise<ContextPromptVerdict>;
  onSubmit: (value: string, text?: string) => Promise<ContextPromptSubmission>;
  onCancel: () => void;
}

function InputPromptDialog({ prompt, onPropose, onSubmit, onCancel }: InputPromptDialogProps) {
  // The revision, the debounce and the verdict live in the shared hook, so this component and
  // the in-place editor ask the backend the same way and cannot drift apart.
  const entry = useContextPromptEntry({ initialValue: prompt.initialValue, onPropose, onSubmit });
  const fieldId = useId();

  return (
    <Dialog compact
      open
      icon={prompt.icon}
      title={prompt.title}
      onClose={onCancel}
      buttons={[
        { key: "cancel", label: "Cancel", color: "neutral", onClick: onCancel },
        { key: "confirm", label: prompt.confirmLabel, color: "primary", disabled: !entry.canSubmit, onClick: () => void entry.submit() },
      ]}
    >
      <label className="context-prompt-field-label" htmlFor={fieldId}>
        {prompt.fieldLabel}
      </label>
      <input
        id={fieldId}
        className="context-prompt-field"
        type="text"
        value={entry.value}
        data-dialog-autofocus=""
        onChange={(event) => entry.setValue(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === "Enter" && entry.canSubmit) {
            event.preventDefault();
            void entry.submit();
          }
        }}
      />
      {entry.message && (
        <p className="context-prompt-error" role="alert">
          {entry.message}
        </p>
      )}
    </Dialog>
  );
}
