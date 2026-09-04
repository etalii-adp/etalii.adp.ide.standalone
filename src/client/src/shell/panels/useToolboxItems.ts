import { useEffect, useMemo, useState } from "react";
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
  // One shape for acquiring a service client, `useMemo` on the transport - the same at every
  // site that needs one. The `useRef` this replaced was safe, and it is worth saying why so
  // the next reader does not have to re-derive it: `transport` is memoised on a `[]`-stable
  // callback and reads its token through a ref, so it never changes identity. What it cost was
  // small and real - `createClient` ran on every render and the result was thrown away, and
  // this hook backs every open diagram tab.
  const client = useMemo(() => createClient(DiagramService, transport), [transport]);
  const [items, setItems] = useState<ToolboxItem[]>(EMPTY);

  const pathKey = path.join("/");

  useEffect(() => {
    let active = true;
    void client
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
  }, [client, projectId, pathKey]);

  return items;
}
