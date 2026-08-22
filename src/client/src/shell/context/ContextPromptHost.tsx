import { useCallback, useEffect, useId, useState } from "react";
import { Dialog } from "../../components/Dialog";
import { ConfirmDialog } from "../../components/ConfirmDialog";
import { ChoicePromptDialog } from "./ChoicePromptDialog";
import { useDebouncedValue } from "./../useDebouncedValue";
import type { ContextPrompt } from "../../generated/context_pb";

/** How long the input must sit still before its validation round trip is worth making. */
const VALIDATION_DEBOUNCE_MS = 200;

export interface ContextPromptVerdict {
  /** The revision the backend judged; a reply for older text is recognisable as stale. */
  revision: number;
  valid: boolean;
  reason: string;
}

export interface ContextPromptSubmission {
  completed: boolean;
  error: string;
}

export interface ContextPromptHostProps {
  prompt: ContextPrompt | null;
  onPropose: (revision: number, value: string) => Promise<ContextPromptVerdict>;
  onSubmit: (value: string) => Promise<ContextPromptSubmission>;
  onCancel: () => void;
}

/**
 * Renders whichever dialog the backend's current prompt asks for. It is driven purely by
 * prompt data and never asks what the running action is, so an action added later gets its
 * dialog here without a new component or a new branch.
 */
export function ContextPromptHost({ prompt, onPropose, onSubmit, onCancel }: ContextPromptHostProps) {
  switch (prompt?.prompt.case) {
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
          onSubmit={onSubmit}
          onCancel={onCancel}
        />
      );

    case "closed":
      return (
        <Dialog
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
      return null;
  }
}

function keyOf(prompt: ContextPrompt): string {
  return Array.from(prompt.interactionId?.value ?? [], (byte) => byte.toString(16).padStart(2, "0")).join("");
}

interface InputPromptDialogProps {
  prompt: { title: string; icon: string; fieldLabel: string; initialValue: string; confirmLabel: string };
  onPropose: (revision: number, value: string) => Promise<ContextPromptVerdict>;
  onSubmit: (value: string) => Promise<ContextPromptSubmission>;
  onCancel: () => void;
}

function InputPromptDialog({ prompt, onPropose, onSubmit, onCancel }: InputPromptDialogProps) {
  // Revision and text move together so a verdict can always be tied back to the exact text
  // it judged; keeping them in one object also keeps the debounce from restarting on
  // re-renders that changed nothing.
  const [entry, setEntry] = useState({ revision: 0, value: prompt.initialValue });
  const [verdict, setVerdict] = useState<ContextPromptVerdict | null>(null);
  const [submitError, setSubmitError] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const debounced = useDebouncedValue(entry, VALIDATION_DEBOUNCE_MS);
  const fieldId = useId();

  useEffect(() => {
    // Revision 0 is the value the dialog opened with, which the user has not chosen yet -
    // validating it would greet them with a complaint about text they never typed.
    if (debounced.revision === 0) {
      return;
    }

    let abandoned = false;
    void onPropose(debounced.revision, debounced.value).then((next) => {
      if (!abandoned) {
        setVerdict(next);
      }
    });

    return () => {
      abandoned = true;
    };
  }, [debounced, onPropose]);

  // Enabled only for a verdict that judged exactly the text now in the box: through the
  // debounce window and while a call is in flight the revisions differ, so a "valid"
  // verdict for text the user has since edited can never be submitted.
  const verdictIsCurrent = verdict !== null && verdict.revision === entry.revision;
  const canSubmit = verdictIsCurrent && verdict.valid && !submitting;

  const handleSubmit = useCallback(async () => {
    setSubmitting(true);
    setSubmitError("");
    try {
      const result = await onSubmit(entry.value);
      if (!result.completed) {
        // Left open on purpose, with what the user typed intact, so they can adjust the
        // value or cancel rather than losing it.
        setSubmitError(result.error);
      }
    } finally {
      setSubmitting(false);
    }
  }, [entry.value, onSubmit]);

  const message = submitError || (verdictIsCurrent && !verdict.valid ? verdict.reason : "");

  return (
    <Dialog
      open
      icon={prompt.icon}
      title={prompt.title}
      onClose={onCancel}
      buttons={[
        { key: "cancel", label: "Cancel", color: "neutral", onClick: onCancel },
        { key: "confirm", label: prompt.confirmLabel, color: "primary", disabled: !canSubmit, onClick: () => void handleSubmit() },
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
        onChange={(event) => setEntry((previous) => ({ revision: previous.revision + 1, value: event.target.value }))}
        onKeyDown={(event) => {
          if (event.key === "Enter" && canSubmit) {
            event.preventDefault();
            void handleSubmit();
          }
        }}
      />
      {message && (
        <p className="context-prompt-error" role="alert">
          {message}
        </p>
      )}
    </Dialog>
  );
}
