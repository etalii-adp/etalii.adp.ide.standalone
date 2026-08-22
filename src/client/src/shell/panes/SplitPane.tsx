import { useCallback, useRef, useState, type ReactNode } from "react";

export interface SplitPaneProps {
  direction: "horizontal" | "vertical";
  initialSplit: number;
  minSize: number;
  first: ReactNode;
  second: ReactNode;
}

export function SplitPane({ direction, initialSplit, minSize, first, second }: SplitPaneProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [split, setSplit] = useState(initialSplit);
  const draggingRef = useRef(false);
  const isHorizontal = direction === "horizontal";

  const clampSplit = useCallback(
    (ratio: number, containerSize: number) => {
      const minRatio = minSize / containerSize;
      const maxRatio = 1 - minRatio;
      return Math.min(Math.max(ratio, minRatio), maxRatio);
    },
    [minSize],
  );

  const handlePointerMove = useCallback(
    (event: PointerEvent) => {
      if (!draggingRef.current || !containerRef.current) {
        return;
      }
      const rect = containerRef.current.getBoundingClientRect();
      const containerSize = isHorizontal ? rect.width : rect.height;
      if (containerSize <= 0) {
        return;
      }
      const offset = isHorizontal ? event.clientX - rect.left : event.clientY - rect.top;
      setSplit(clampSplit(offset / containerSize, containerSize));
    },
    [isHorizontal, clampSplit],
  );

  const stopDragging = useCallback(() => {
    draggingRef.current = false;
    document.removeEventListener("pointermove", handlePointerMove);
    document.removeEventListener("pointerup", stopDragging);
  }, [handlePointerMove]);

  const startDragging = useCallback(() => {
    draggingRef.current = true;
    document.addEventListener("pointermove", handlePointerMove);
    document.addEventListener("pointerup", stopDragging);
  }, [handlePointerMove, stopDragging]);

  const firstStyle = isHorizontal ? { width: `${split * 100}%` } : { height: `${split * 100}%` };
  const secondStyle = isHorizontal ? { width: `${(1 - split) * 100}%` } : { height: `${(1 - split) * 100}%` };

  return (
    <div
      ref={containerRef}
      className={`split-pane ${isHorizontal ? "split-pane-horizontal" : "split-pane-vertical"}`}
    >
      <div className="split-pane-first" style={firstStyle}>
        {first}
      </div>
      <div
        className="split-pane-divider"
        role="separator"
        aria-orientation={isHorizontal ? "vertical" : "horizontal"}
        onPointerDown={startDragging}
      />
      <div className="split-pane-second" style={secondStyle}>
        {second}
      </div>
    </div>
  );
}
