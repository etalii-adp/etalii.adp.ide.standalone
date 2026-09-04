import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";

/** Where a label is, in the canvas's own units, and what it currently reads. */
export interface LabelPlacement {
  x: number;
  y: number;
  width: number;
  height: number;
  /** The text the editor replaces, so it can open with what is on screen. */
  text: string;
}

/** A canvas's answer for one element: where its label is, or null if it cannot place it. */
export type LabelPlacementResolver = (elementId: string) => LabelPlacement | null;

/**
 * Registration and lookup are two contexts on purpose, and collapsing them into one is the
 * mistake to avoid: a single value has to change identity when a canvas mounts, so that readers
 * re-render - and a registering canvas whose effect depends on that same value then unregisters
 * and re-registers on every change, bumping the version again. That is an infinite loop, and it
 * was written once here before being split.
 */
const RegisterContext = createContext<((resolve: LabelPlacementResolver) => () => void) | undefined>(undefined);
const PlacementContext = createContext<LabelPlacementResolver | undefined>(undefined);

/**
 * Connects the shell's prompt host to whichever diagram canvases are mounted, exactly as
 * `DiagramToolboxContext` connects the Toolbox panel to them.
 *
 * The point is that the *same* function answers for both readers. The host asks "can anyone
 * place this?" to decide whether to stand down, and the canvas asks "can I place this?" to
 * decide whether to draw an editor. Two consumers each deciding for themselves would be two
 * chances to disagree, and a disagreement shows as a dialog on top of an editor, or as a click
 * that does nothing at all.
 *
 * Registration is per canvas rather than per prompt, which is what makes the answer available in
 * the first render that sees a prompt: a canvas is mounted long before anyone can press F2 on
 * something in it, so by the time a prompt exists the registry has already answered and no
 * dialog flashes before the editor appears.
 */
export function InlineLabelPlacementProvider({ children }: { children: ReactNode }) {
  const resolvers = useRef(new Set<LabelPlacementResolver>());
  const [version, setVersion] = useState(0);

  // Stable for the provider's whole life, so a registering canvas's effect runs once.
  const register = useCallback((resolve: LabelPlacementResolver) => {
    resolvers.current.add(resolve);
    setVersion((previous) => previous + 1);
    return () => {
      resolvers.current.delete(resolve);
      setVersion((previous) => previous + 1);
    };
  }, []);

  // Rebuilt per version, so a reader holding it re-reads the set after a canvas mounts or
  // unmounts; the set itself is stable, and this identity is what wakes the readers.
  const placementFor = useMemo<LabelPlacementResolver>(
    () => (elementId) => {
      for (const resolve of resolvers.current) {
        const placement = resolve(elementId);
        if (placement !== null) {
          return placement;
        }
      }
      return null;
    },
    [version],
  );

  return (
    <RegisterContext.Provider value={register}>
      <PlacementContext.Provider value={placementFor}>{children}</PlacementContext.Provider>
    </RegisterContext.Provider>
  );
}

/**
 * Registers a mounted canvas's placement resolver, and withdraws it on unmount. Pass a stable
 * function (memoized): a fresh identity per render re-registers on every one.
 */
export function useRegisterInlineLabelPlacement(resolve: LabelPlacementResolver): void {
  const register = useContext(RegisterContext);

  useEffect(() => {
    if (register === undefined) {
      return;
    }
    return register(resolve);
  }, [register, resolve]);
}

/**
 * Asks every mounted canvas whether it can place an element's label. Outside a provider nothing
 * can be placed, which is the right answer rather than an error: a surface with no canvas under
 * it - the explorer, a ribbon - simply keeps its dialog.
 */
export function useInlineLabelPlacement(): LabelPlacementResolver {
  return useContext(PlacementContext) ?? NOTHING_IS_PLACEABLE;
}

const NOTHING_IS_PLACEABLE: LabelPlacementResolver = () => null;
