import { useMemo } from "react";
import { create } from "@bufbuild/protobuf";
import { ChoiceDialogPromptSchema, type FileDialogPrompt } from "../../generated/context_pb";
import { ChoicePromptDialog } from "./ChoicePromptDialog";
import type { ContextPromptSubmission, ContextPromptVerdict } from "./ContextPromptHost";

export interface FilePromptDialogProps {
  prompt: FileDialogPrompt;
  onPropose: (revision: number, value: string) => Promise<ContextPromptVerdict>;
  onSubmit: (value: string, text?: string) => Promise<ContextPromptSubmission>;
  onCancel: () => void;
}

/**
 * A dialog for picking one file of the workspace. It shows the pinned entries first and the
 * workspace's tree under them, and answers with the chosen file's project-relative path.
 *
 * It decides nothing about which files are there: the backend has already limited the tree to
 * what the asking provider accepts, and checks the answer against what it offered. Filtering
 * here as well would be a second copy of a rule this build cannot see.
 *
 * A file tree is a tree of options of which the folders cannot be chosen, which is exactly what
 * the choice dialog draws - so this is that dialog with no name field, and gets its keyboard
 * paths, its roles and its refusal handling from it rather than repeating them.
 */
export function FilePromptDialog({ prompt, onPropose, onSubmit, onCancel }: FilePromptDialogProps) {
  const choice = useMemo(
    () =>
      create(ChoiceDialogPromptSchema, {
        title: prompt.title,
        icon: prompt.icon,
        confirmLabel: prompt.confirmLabel,
        emptyMessage: prompt.emptyMessage,
        options: [...prompt.pinned, ...prompt.files],
      }),
    [prompt],
  );

  return <ChoicePromptDialog prompt={choice} onPropose={onPropose} onSubmit={onSubmit} onCancel={onCancel} />;
}
