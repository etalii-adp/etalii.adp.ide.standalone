import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import type { ToolboxItem } from "../../generated/diagrams_pb";

/** The MIME type a toolbox drag carries; the canvas accepts drops of exactly this. */
export const TOOLBOX_DRAG_TYPE = "application/x-adp-toolbox-item";

interface DiagramToolboxRegistry {
  /** The active diagram's entries, or null while no diagram is open. */
  items: ToolboxItem[] | null;
  register: (items: ToolboxItem[]) => () => void;
}

const DiagramToolboxContext = createContext<DiagramToolboxRegistry | undefined>(undefined);

/**
 * Connects the Toolbox panel to whichever diagram canvas is currently mounted, exactly as
 * `DiagramViewContext` connects the ribbon's View group. The entries come from the backend
 * (`DiagramService.DescribeToolbox`) - the panel renders a palette it does not understand,
 * and dropping an entry executes the backend-named action, so the client holds no knowledge
 * of what any entry means.
 */
export function DiagramToolboxProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<ToolboxItem[] | null>(null);

  const register = useCallback((next: ToolboxItem[]) => {
    setItems(next);
    return () => setItems((current) => (current === next ? null : current));
  }, []);

  const value = useMemo<DiagramToolboxRegistry>(() => ({ items, register }), [items, register]);

  return <DiagramToolboxContext.Provider value={value}>{children}</DiagramToolboxContext.Provider>;
}

/** The active diagram's toolbox entries, or null while no diagram is open - what the panel reads. */
export function useDiagramToolbox(): ToolboxItem[] | null {
  return useContext(DiagramToolboxContext)?.items ?? null;
}

/**
 * Registers a mounted canvas's toolbox entries, and withdraws them on unmount. Pass a stable
 * array (memoized): a fresh identity per render re-registers on every one.
 */
export function useRegisterDiagramToolbox(items: ToolboxItem[]): void {
  const value = useContext(DiagramToolboxContext);
  useEffect(() => {
    // Optional on purpose: a canvas rendered outside the shell (a test, say) has no panel
    // to serve and registers nowhere.
    return value?.register(items);
  }, [value, items]);
}
