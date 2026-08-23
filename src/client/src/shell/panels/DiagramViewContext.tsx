import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";

/**
 * What a diagram view lets the shell do to it. Pure client-side view state: none of this
 * touches a file or the history, exactly like a fold does not.
 */
export interface DiagramViewControls {
  zoomIn: () => void;
  zoomOut: () => void;
  fitToView: () => void;
}

interface DiagramViewRegistry {
  controls: DiagramViewControls | null;
  register: (controls: DiagramViewControls) => () => void;
}

const DiagramViewContext = createContext<DiagramViewRegistry | undefined>(undefined);

/**
 * Connects the ribbon's View group to whichever diagram canvas is currently mounted. The
 * tab pane mounts only the active tab's content, so at most one canvas is registered at a
 * time; with none open the ribbon's buttons have nothing to act on and disable themselves.
 */
export function DiagramViewProvider({ children }: { children: ReactNode }) {
  const [controls, setControls] = useState<DiagramViewControls | null>(null);

  const register = useCallback((next: DiagramViewControls) => {
    setControls(next);
    return () => setControls((current) => (current === next ? null : current));
  }, []);

  const value = useMemo<DiagramViewRegistry>(() => ({ controls, register }), [controls, register]);

  return <DiagramViewContext.Provider value={value}>{children}</DiagramViewContext.Provider>;
}

/**
 * The active diagram's view controls, or null while no diagram is open - what the ribbon
 * reads. Null outside a provider too, the same as with no diagram: both simply mean there
 * is nothing to zoom.
 */
export function useDiagramViewControls(): DiagramViewControls | null {
  return useContext(DiagramViewContext)?.controls ?? null;
}

/**
 * Registers a mounted canvas's controls for the ribbon, and withdraws them on unmount.
 * Pass a stable object (memoized): a fresh identity per render re-registers on every one.
 */
export function useRegisterDiagramView(controls: DiagramViewControls): void {
  const value = useContext(DiagramViewContext);
  useEffect(() => {
    // Optional on purpose: a canvas rendered outside the shell (a test, say) simply has no
    // ribbon to serve and registers nowhere.
    return value?.register(controls);
  }, [value, controls]);
}
