import { useEffect, useId, useState, type KeyboardEvent } from "react";
import { Dialog } from "../../components/Dialog";
import "../../components/SearchList.css";
import type { LookupDialogPrompt } from "../../generated/context_pb";
import { useDebouncedValue } from "../useDebouncedValue";
import type { ContextLookupResult, ContextPromptSubmission } from "./ContextPromptHost";

/** How long the query must sit still before the backend is asked again. */
const SEARCH_DEBOUNCE_MS = 150;

/** One element the lookup offers: what is submitted when it is picked, and how it is shown. */
export interface ContextLookupCandidate {
  value: string;
  name: string;
  documentName: string;
  folder: string;
}

export interface LookupPromptDialogProps {
  prompt: LookupDialogPrompt;
  onSearch: (query: string) => Promise<ContextLookupResult>;
  onSubmit: (value: string) => Promise<ContextPromptSubmission>;
  onCancel: () => void;
}

/** The query a set of candidates answers, so an answer for text since replaced is never shown. */
interface Answer {
  query: string;
  result: ContextLookupResult;
}

/**
 * The host's lookup over the linkable elements of the workspace (DISL 9.4, etalii.adp spec 092, T021): a search field whose text the
 * backend matches against the elements' names, and the elements it found, each with the document and folder it is in. Enter or a
 * click picks the active one, which submits its value; a refused pick keeps the dialog open with the reason, to pick again.
 *
 * It knows nothing of the diagram it adds to: the backend opened it, searches for it and adds what was picked.
 */
export function LookupPromptDialog({ prompt, onSearch, onSubmit, onCancel }: LookupPromptDialogProps) {
  const [query, setQuery] = useState("");
  const [answer, setAnswer] = useState<Answer | null>(null);
  const [active, setActive] = useState(0);
  const [submitError, setSubmitError] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const listId = useId();

  // The empty query is asked at once, so the dialog opens on what can be added rather than on a blank list.
  const debounced = useDebouncedValue(query, SEARCH_DEBOUNCE_MS);
  useEffect(() => {
    let current = true;
    void onSearch(debounced).then((result) => {
      if (current) {
        setAnswer({ query: debounced, result });
        setActive(0);
      }
    });
    return () => {
      current = false;
    };
  }, [debounced, onSearch]);

  const candidates = answer?.result.candidates ?? [];
  const current = Math.min(active, Math.max(0, candidates.length - 1));
  const searchError = answer?.result.error ?? "";

  const pick = async (candidate: ContextLookupCandidate | undefined) => {
    if (candidate === undefined || submitting) {
      return;
    }
    setSubmitting(true);
    setSubmitError("");
    try {
      const result = await onSubmit(candidate.value);
      if (!result.completed) {
        setSubmitError(result.error);
      }
    } finally {
      setSubmitting(false);
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      setActive(Math.min(current + 1, candidates.length - 1));
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setActive(Math.max(current - 1, 0));
    } else if (event.key === "Enter") {
      event.preventDefault();
      void pick(candidates[current]);
    }
  };

  return (
    <Dialog
      open
      icon={prompt.icon}
      title={prompt.title}
      onClose={onCancel}
      buttons={[
        { key: "cancel", label: "Cancel", color: "neutral", onClick: onCancel },
        { key: "confirm", label: "Add", color: "primary", disabled: candidates.length === 0 || submitting, onClick: () => void pick(candidates[current]) },
      ]}
    >
      <div className="search-list">
        <input
          className="search-list-field"
          type="text"
          role="combobox"
          aria-label={prompt.title}
          aria-expanded="true"
          aria-controls={listId}
          aria-activedescendant={candidates.length > 0 ? `${listId}-${current}` : undefined}
          placeholder={prompt.placeholder}
          value={query}
          data-dialog-autofocus=""
          onChange={(event) => setQuery(event.target.value)}
          onKeyDown={onKeyDown}
        />
        <ul className="search-list-rows" id={listId} role="listbox" aria-label={prompt.title}>
          {candidates.map((candidate, index) => (
            <li
              key={candidate.value}
              id={`${listId}-${index}`}
              className={`search-list-row${index === current ? " search-list-row-active" : ""}`}
              role="option"
              aria-selected={index === current}
              // The field keeps the focus, so the arrow keys go on working after a click elsewhere in the list.
              onMouseDown={(event) => event.preventDefault()}
              onClick={() => void pick(candidate)}
            >
              <span className="mdi mdi-link-variant" aria-hidden="true" />
              <span>{candidate.name}</span>
              <span className="lookup-candidate-where">{candidate.folder === "" ? candidate.documentName : `${candidate.folder}/${candidate.documentName}`}</span>
            </li>
          ))}
          {answer !== null && candidates.length === 0 && searchError === "" && (
            <li className="search-list-empty" role="presentation">
              {prompt.emptyMessage}
            </li>
          )}
        </ul>
      </div>
      {(searchError || submitError) && (
        <p className="context-prompt-error" role="alert">
          {submitError || searchError}
        </p>
      )}
    </Dialog>
  );
}
