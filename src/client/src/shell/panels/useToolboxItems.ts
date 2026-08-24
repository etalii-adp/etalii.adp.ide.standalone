import { useEffect, useRef, useState } from "react";
import { createClient } from "@connectrpc/connect";
import { useAuth } from "../../auth/AuthContext";
import { DiagramService } from "../../generated/diagrams_pb";
import type { ToolboxItem } from "../../generated/diagrams_pb";

const EMPTY: ToolboxItem[] = [];

/**
 * The toolbox entries the backend describes for the diagram at `path` - asked once per
 * opened diagram, since a type's toolbox is static data, not changing state. An empty
 * answer (a type contributing nothing, or a diagram that cannot be resolved) is a valid
 * palette with nothing in it, never an error the canvas has to handle.
 */
export function useToolboxItems(projectId: Uint8Array, path: readonly string[]): ToolboxItem[] {
  const { transport } = useAuth();
  const clientRef = useRef(createClient(DiagramService, transport));
  const [items, setItems] = useState<ToolboxItem[]>(EMPTY);

  const pathKey = path.join("/");

  useEffect(() => {
    let active = true;
    void clientRef.current
      .describeToolbox({ projectId: { value: projectId }, path: { segments: [...path] } })
      .then((response) => {
        if (active) {
          setItems(response.items);
        }
      })
      .catch(() => {
        // Advisory, like a view report: without an answer the palette simply stays empty.
      });
    return () => {
      active = false;
    };
    // path is compared by value through pathKey, not by array identity.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projectId, pathKey]);

  return items;
}
