import { ContextPromptHost } from "./ContextPromptHost";
import { useContextPrompt } from "./ContextConnectionProvider";

/**
 * Mounts the prompt host once, at the shell level, on the connection's context stream -
 * so a dialog raised by an action from any surface (explorer, ribbon, a future canvas)
 * shows regardless of which panel triggered it.
 */
export function ShellPromptHost() {
  const { prompt, onPropose, onSubmit, onCancel } = useContextPrompt();
  return <ContextPromptHost prompt={prompt} onPropose={onPropose} onSubmit={onSubmit} onCancel={onCancel} />;
}
