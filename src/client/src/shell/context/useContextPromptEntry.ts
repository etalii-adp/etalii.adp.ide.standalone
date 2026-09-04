import { useCallback, useEffect, useState } from "react";
import { useDebouncedValue } from "../useDebouncedValue";
import type { ContextPromptSubmission, ContextPromptVerdict } from "./ContextPromptHost";

/** How long the input must sit still before its validation round trip is worth making. */
export const VALIDATION_DEBOUNCE_MS = 200;

export interface ContextPromptEntryOptions {
  /** The value the prompt opened with; the user has not chosen it, so it is not validated. */
  initialValue: string;
  onPropose: (revision: number, value: string) => Promise<ContextPromptVerdict>;
  onSubmit: (value: string, text?: string) => Promise<ContextPromptSubmission>;
}

export interface ContextPromptEntry {
  value: string;
  /** 0 until the user's first edit; every edit after that bumps it by one. */
  revision: number;
  setValue: (next: string) => void;
  /** True only while the standing verdict judged exactly the text now in the box. */
  verdictIsCurrent: boolean;
  /** The refusal to show, from the backend's verdict or from a submit that did not complete. */
  message: string;
  canSubmit: boolean;
  submitting: boolean;
  /** Submits the current value; a refusal is reported through `message` and nothing closes. */
  submit: () => Promise<ContextPromptSubmission | null>;
}

/**
 * The propose/verdict/submit discipline behind a context input prompt, held apart from any one
 * way of putting it on screen.
 *
 * It exists so the dialog and the in-place editor cannot drift. Both ask the same backend the
 * same way, so a value the module refuses is refused identically whichever presentation asked -
 * and a rule that lives in one component would otherwise be re-derived, subtly differently, by
 * the second one written (inline-rename Requirement 1.4).
 *
 * Two rules here are easy to lose and both are deliberate. A revision travels with the text so
 * a verdict can always be tied back to the exact text it judged, and submission is enabled only
 * for a verdict whose revision matches - so a `valid` verdict for text the user has since edited
 * can never be submitted. And revision 0 is never proposed: it is the value the prompt opened
 * with, which the user has not chosen, and validating it would greet them with a complaint about
 * text they never typed.
 *
 * `ChoicePromptDialog` deliberately does not use this. Its name field is optional, so its
 * submit rule is a different one ("no field, or an unedited non-empty name, or a current valid
 * verdict"), and folding that in would make this hook a switch rather than a discipline.
 */
export function useContextPromptEntry({ initialValue, onPropose, onSubmit }: ContextPromptEntryOptions): ContextPromptEntry {
  // Revision and text move together so a verdict can always be tied back to the exact text it
  // judged; keeping them in one object also keeps the debounce from restarting on re-renders
  // that changed nothing.
  const [entry, setEntry] = useState({ revision: 0, value: initialValue });
  const [verdict, setVerdict] = useState<ContextPromptVerdict | null>(null);
  const [submitError, setSubmitError] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const debounced = useDebouncedValue(entry, VALIDATION_DEBOUNCE_MS);

  useEffect(() => {
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

  const setValue = useCallback((next: string) => {
    setEntry((previous) => ({ revision: previous.revision + 1, value: next }));
  }, []);

  const verdictIsCurrent = verdict !== null && verdict.revision === entry.revision;
  const canSubmit = verdictIsCurrent && verdict.valid && !submitting;

  const submit = useCallback(async () => {
    setSubmitting(true);
    setSubmitError("");
    try {
      const result = await onSubmit(entry.value);
      if (!result.completed) {
        // Left open on purpose, with what the user typed intact, so they can adjust the value
        // or cancel rather than losing it.
        setSubmitError(result.error);
      }
      return result;
    } finally {
      setSubmitting(false);
    }
  }, [entry.value, onSubmit]);

  return {
    value: entry.value,
    revision: entry.revision,
    setValue,
    verdictIsCurrent,
    message: submitError || (verdictIsCurrent && !verdict.valid ? verdict.reason : ""),
    canSubmit,
    submitting,
    submit,
  };
}
