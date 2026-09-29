// The client's picture of one pipeline: the stages, jobs, steps and dependency edges the backend
// streamed. Pure functions over plain data, so the reducer that applies deltas is testable
// without a canvas or a stream.

import { fromBinary } from "@bufbuild/protobuf";
import type { Delta } from "@client/generated/deltas_pb";
import {
  PipelineElementPayloadSchema,
  type PipelineElementPayload,
} from "@client/generated/azure-pipeline_pb";

/** The element kinds this module puts on the wire; anything else on the stream is ignored. */
export const STAGE_TYPE = "azure-devops/pipeline+stage";
export const JOB_TYPE = "azure-devops/pipeline+job";
export const STEP_TYPE = "azure-devops/pipeline+step";
export const EDGE_TYPE = "azure-devops/pipeline+edge";
export const TEMPLATE_TYPE = "azure-devops/pipeline+template";

/**
 * One box.
 *
 * The position is its **top-left** corner, not its centre - the convention this module's layout
 * uses, because a stage is a container sized from what it holds and a container is naturally
 * placed by its corner. The C4 module puts centres on the wire, so the two are not interchangeable
 * and this comment is here to stop anyone assuming they are.
 */
export interface PipelineNode {
  id: string;
  x: number;
  y: number;
  /** Which of the wire types it arrived as, so the canvas can draw a job unlike a stage. */
  type: string;
  payload: PipelineElementPayload;
}

/** One dependency arrow. It has no position of its own: both ends are boxes the model already has. */
export interface PipelineEdgeLine {
  id: string;
  payload: PipelineElementPayload;
}

export interface PipelineModel {
  nodes: ReadonlyMap<string, PipelineNode>;
  edges: ReadonlyMap<string, PipelineEdgeLine>;
  /**
   * Which stages this connection has expanded. Held here rather than in the canvas because the
   * backend decides it: a `group` delta folds a stage and an `ungroup` opens it, and the model is
   * what remembers which way round each one currently is.
   */
  collapsed: ReadonlySet<string>;
}

export const emptyModel: PipelineModel = {
  nodes: new Map(),
  edges: new Map(),
  collapsed: new Set(),
};

/**
 * Applies one delta, returning a new model.
 *
 * `add` is an upsert keyed on id, which is what makes an edit and a re-delivery the same thing:
 * the backend re-sends an element in its new state and it replaces the old one in place. Never
 * mutates its input.
 */
export function applyDelta(model: PipelineModel, delta: Delta): PipelineModel {
  switch (delta.action.case) {
    case "add": {
      const nodes = new Map(model.nodes);
      const edges = new Map(model.edges);

      for (const raw of delta.action.value.elements) {
        const id = raw.id?.value;
        if (!id || !raw.payload) {
          continue;
        }

        // The type is checked before the payload is decoded, not after. The stream is shared, so
        // an element belonging to another module arrives here too - and decoding its bytes as a
        // pipeline payload does not merely produce nonsense, it throws and takes the rest of the
        // delta with it.
        if (raw.type === EDGE_TYPE) {
          edges.set(id, { id, payload: fromBinary(PipelineElementPayloadSchema, raw.payload.value) });
          continue;
        }

        if (raw.type === STAGE_TYPE || raw.type === JOB_TYPE || raw.type === STEP_TYPE || raw.type === TEMPLATE_TYPE) {
          nodes.set(id, {
            id,
            x: raw.position?.x ?? 0,
            y: raw.position?.y ?? 0,
            type: raw.type,
            payload: fromBinary(PipelineElementPayloadSchema, raw.payload.value),
          });
        }
      }

      return { ...model, nodes, edges };
    }

    case "remove": {
      const nodes = new Map(model.nodes);
      const edges = new Map(model.edges);
      for (const id of delta.action.value.elementIds) {
        nodes.delete(id.value);
        edges.delete(id.value);
      }
      return { ...model, nodes, edges };
    }

    case "group": {
      // Folding a stage: its jobs go away and the stage stands for them. The third independent
      // use of these two actions after the mindmap's, which is why Requirement 8.6 asks for them
      // rather than for a new delta.
      const groupId = delta.action.value.groupElement?.id?.value;
      if (!groupId) {
        return model;
      }

      const nodes = new Map(model.nodes);
      for (const source of delta.action.value.sourceElementIds) {
        nodes.delete(source.value);
      }

      const collapsed = new Set(model.collapsed);
      collapsed.add(groupId);
      return { ...model, nodes, collapsed };
    }

    case "ungroup": {
      const groupId = delta.action.value.groupElementId?.value;
      if (!groupId) {
        return model;
      }

      // Opening a stage: the jobs it stands for arrive with the delta that reveals them.
      const nodes = new Map(model.nodes);
      for (const raw of delta.action.value.elements) {
        const id = raw.id?.value;
        if (id && raw.payload) {
          nodes.set(id, {
            id,
            x: raw.position?.x ?? 0,
            y: raw.position?.y ?? 0,
            type: raw.type,
            payload: fromBinary(PipelineElementPayloadSchema, raw.payload.value),
          });
        }
      }

      const collapsed = new Set(model.collapsed);
      collapsed.delete(groupId);
      return { ...model, nodes, collapsed };
    }

    default:
      return model;
  }
}

/** Every box, which is what fit-to-view measures. */
export function boxesOf(model: PipelineModel): { x: number; y: number; width: number; height: number }[] {
  return [...model.nodes.values()].map((node) => ({
    x: node.x,
    y: node.y,
    width: node.payload.width,
    height: node.payload.height,
  }));
}

/** The jobs inside one stage, in the order the backend sent them. */
export function jobsOf(model: PipelineModel, stageId: string): PipelineNode[] {
  return [...model.nodes.values()].filter(
    (node) => node.type === JOB_TYPE && node.payload.parentId === stageId,
  );
}

/**
 * What to write under a collapsed stage's name: how many jobs it holds.
 *
 * A collapsed stage sends no jobs, so this counts what the file said rather than what arrived -
 * the count is on the stage itself for exactly that reason.
 */
export function jobCountLabel(count: number): string {
  return count === 1 ? "1 job" : `${count} jobs`;
}

/**
 * Whether an element is drawn as uncertain: its presence, or how many of it there will be, is
 * decided by an expression nobody has evaluated yet.
 */
export function isIndeterminate(node: PipelineNode): boolean {
  return node.payload.indeterminate;
}

/** The two boxes an edge joins, or null when either end is not on the canvas. */
export function endpointsOf(
  model: PipelineModel,
  edge: PipelineEdgeLine,
): { from: PipelineNode; to: PipelineNode } | null {
  const from = model.nodes.get(edge.payload.sourceId);
  const to = model.nodes.get(edge.payload.targetId);
  return from && to ? { from, to } : null;
}
