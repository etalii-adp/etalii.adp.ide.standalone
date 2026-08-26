# Diagram Types

A catalog of diagram types EtAlii.Adp could support, adapted from a broader reference overview of software/IT architecture diagram styles, grouped by the methodology/notation family they belong to (where one exists). Each entry tracks how far along ADP's own support for it is, alongside the theory and a visual example for the notation itself.

**This document is a living catalog — see the note in [CLAUDE.md](../CLAUDE.md) about keeping it updated whenever a new diagram type is identified or its state changes.**

## Legend

**State** — how far ADP's own support for the diagram type has progressed:

| Icon | State | Meaning |
|---|---|---|
| 💡 | Identified | Recognized as a candidate diagram type; no spec yet |
| 📝 | Specified | A requirements/design spec exists (`.spec-workflow/specs/`) |
| ⏸️ | To-do | Spec approved and queued for implementation, not yet started |
| 🛠️ | Work-in-progress | Actively being implemented |
| ✅ | Implemented | Shipped and usable in ADP |

**Origin** — a MIME-type-style tag identifying where the notation comes from, in the form `<architecture-or-vendor>/<diagram-type>` (e.g. `uml/class`, `c4/context`, `archimate/business`). Diagram types with no single owning standards body use the tool/author most associated with them as the vendor (e.g. `freeplane/mindmap`). This tag is what a diagram-type spec's `hierarchy.proto`-style `type` field (mime-style, per `elements.proto`) is expected to use once implemented.

---

## 1. UML — Unified Modeling Language

A standardized general-purpose visual modeling language (OMG standard) defining 14 diagram types across two families.

- Theory: [OMG UML specification](https://www.omg.org/spec/UML/) · [uml-diagrams.org](https://www.uml-diagrams.org/)

### 1a. Structural diagrams

| State | Origin | Diagram | Theory | Example |
|---|---|---|---|---|
| 💡 Identified | `uml/class` | Class diagram | [uml-diagrams.org](https://www.uml-diagrams.org/class-diagrams-overview.html) | [Example](https://www.uml-diagrams.org/class-reference-uml-example.png) |
| 💡 Identified | `uml/object` | Object diagram | [uml-diagrams.org](https://www.uml-diagrams.org/object-diagrams.html) | [Example](https://www.uml-diagrams.org/examples/object-diagram-example.html) |
| 💡 Identified | `uml/component` | Component diagram | [uml-diagrams.org](https://www.uml-diagrams.org/component-diagrams.html) | [Example](https://www.uml-diagrams.org/examples/component-diagram-example.html) |
| 💡 Identified | `uml/composite-structure` | Composite structure diagram | [uml-diagrams.org](https://www.uml-diagrams.org/composite-structure-diagrams.html) | [Example](https://www.uml-diagrams.org/composite-structure-diagrams.html) |
| 💡 Identified | `uml/deployment` | Deployment diagram | [uml-diagrams.org](https://www.uml-diagrams.org/deployment-diagrams.html) | [Example](https://www.uml-diagrams.org/examples/deployment-diagram-example.html) |
| 💡 Identified | `uml/package` | Package diagram | [uml-diagrams.org](https://www.uml-diagrams.org/package-diagrams.html) | [Example](https://www.uml-diagrams.org/package-diagrams.html) |
| 💡 Identified | `uml/profile` | Profile diagram | [uml-diagrams.org](https://www.uml-diagrams.org/profile-diagrams.html) | [Example](https://www.uml-diagrams.org/profile-diagrams.html) |

### 1b. Behavioral diagrams

| State | Origin | Diagram | Theory | Example |
|---|---|---|---|---|
| 💡 Identified | `uml/use-case` | Use case diagram | [uml-diagrams.org](https://www.uml-diagrams.org/use-case-diagrams.html) | [Example](https://www.uml-diagrams.org/examples/use-case-diagram-example.html) |
| 💡 Identified | `uml/activity` | Activity diagram | [uml-diagrams.org](https://www.uml-diagrams.org/activity-diagrams.html) | [Example](https://www.uml-diagrams.org/examples/activity-diagram-example.html) |
| 💡 Identified | `uml/state-machine` | State machine diagram | [uml-diagrams.org](https://www.uml-diagrams.org/state-machine-diagrams.html) | [Example](https://www.uml-diagrams.org/examples/state-machine-diagram-example.html) |

### 1c. Interaction diagrams

| State | Origin | Diagram | Theory | Example |
|---|---|---|---|---|
| 💡 Identified | `uml/sequence` | Sequence diagram | [uml-diagrams.org](https://www.uml-diagrams.org/sequence-diagrams.html) | [Example](https://www.uml-diagrams.org/examples/sequence-diagram-example.html) |
| 💡 Identified | `uml/communication` | Communication diagram | [uml-diagrams.org](https://www.uml-diagrams.org/communication-diagrams.html) | [Example](https://www.uml-diagrams.org/communication-diagrams.html) |
| 💡 Identified | `uml/interaction-overview` | Interaction overview diagram | [uml-diagrams.org](https://www.uml-diagrams.org/interaction-overview-diagrams.html) | [Example](https://www.uml-diagrams.org/interaction-overview-diagrams.html) |
| 💡 Identified | `uml/timing` | Timing diagram | [uml-diagrams.org](https://www.uml-diagrams.org/timing-diagrams.html) | [Example](https://www.uml-diagrams.org/timing-diagrams.html) |

---

## 2. The C4 Model

A lightweight, notation-independent hierarchy of zoom levels (Simon Brown, 2006–2018) — Context + Container covers most teams' needs.

- Theory: [c4model.com](https://c4model.com/) · [Diagrams overview](https://c4model.com/diagrams)

| State | Origin | Diagram | Theory | Example |
|---|---|---|---|---|
| ✅ Implemented | `c4/context` | System Context | [c4model.com](https://c4model.com/diagrams/system-context) | Everyone-facing overview |
| ✅ Implemented | `c4/container` | Container | [c4model.com](https://c4model.com/diagrams/container) | Deployable/runnable units |
| ✅ Implemented | `c4/component` | Component | [c4model.com](https://c4model.com/diagrams/component) | Building blocks inside one container |
| 📝 Specified | `c4/code` | Code (optional) | [c4model.com](https://c4model.com/diagrams/code) | Usually IDE-generated |
| ✅ Implemented | `c4/system-landscape` | System Landscape (supplementary) | [c4model.com](https://c4model.com/diagrams/system-landscape) | Multiple systems across an org |
| ✅ Implemented | `c4/dynamic` | Dynamic (supplementary) | [c4model.com](https://c4model.com/diagrams/dynamic) | One scenario across containers/components |
| ✅ Implemented | `c4/deployment` | Deployment (supplementary) | [c4model.com](https://c4model.com/diagrams/deployment) | Containers mapped onto infrastructure |

Tooling: [Structurizr](https://structurizr.com/) · [C4-PlantUML](https://github.com/plantuml-stdlib/C4-PlantUML) · [IcePanel](https://icepanel.io/).

---

## 3. The 4+1 Architectural View Model (Kruchten)

Not a separate notation — a way of *organizing* 5 concurrent stakeholder views, each typically expressed using the UML diagrams already listed in section 1 (Logical → class/state, Process → activity/sequence, Development → component/package, Physical → deployment, Scenarios → use case). No dedicated `Origin` rows here since it introduces no renderable diagram type of its own.

- Theory: [Kruchten's original paper (PDF)](https://www.cs.ubc.ca/~gregor/teaching/papers/4+1view-architecture.pdf) · [Wikipedia](https://en.wikipedia.org/wiki/4+1_architectural_view_model)

---

## 4. Enterprise Architecture frameworks

Model how business, application, and technology fit together across a whole organization — one level up from software architecture.

| State | Origin | Diagram | Theory | Example |
|---|---|---|---|---|
| 💡 Identified | `archimate/motivation` | ArchiMate — Motivation layer | [ArchiMate spec](https://pubs.opengroup.org/architecture/archimate3-doc/) | [Archi example models](https://www.archimatetool.com/examples/) |
| 💡 Identified | `archimate/strategy` | ArchiMate — Strategy layer | [ArchiMate spec](https://pubs.opengroup.org/architecture/archimate3-doc/) | [Archi example models](https://www.archimatetool.com/examples/) |
| 💡 Identified | `archimate/business` | ArchiMate — Business layer | [ArchiMate spec](https://pubs.opengroup.org/architecture/archimate3-doc/) | [Archi example models](https://www.archimatetool.com/examples/) |
| 💡 Identified | `archimate/application` | ArchiMate — Application layer | [ArchiMate spec](https://pubs.opengroup.org/architecture/archimate3-doc/) | [Archi example models](https://www.archimatetool.com/examples/) |
| 💡 Identified | `archimate/technology` | ArchiMate — Technology layer | [ArchiMate spec](https://pubs.opengroup.org/architecture/archimate3-doc/) | [Archi example models](https://www.archimatetool.com/examples/) |
| 💡 Identified | `archimate/implementation-migration` | ArchiMate — Implementation & Migration layer | [ArchiMate spec](https://pubs.opengroup.org/architecture/archimate3-doc/) | [Archi example models](https://www.archimatetool.com/examples/) |
| 💡 Identified | `togaf/adm` | TOGAF ADM cycle diagram | [TOGAF Standard](https://www.opengroup.org/togaf) | [GitHub: TOGAF 10 diagram set](https://github.com/nelsambrose/togaf-diagrams) |
| 💡 Identified | `zachman/matrix` | Zachman Framework matrix | [Zachman International](https://www.zachman.com/about-the-zachman-framework) | [Visual Paradigm comparison](https://www.visual-paradigm.com/guide/togaf/togaf-vs-zachman-framework/) |

Layers can be diagrammed individually or combined into a cross-layer viewpoint; TOGAF is a process methodology often paired with ArchiMate as its notation, not a notation itself.

---

## 5. Business process & workflow notation

| State | Origin | Diagram | Theory | Example |
|---|---|---|---|---|
| 💡 Identified | `bpmn/process` | BPMN process diagram | [bpmn.org](https://www.bpmn.org/) · [OMG BPMN spec](https://www.omg.org/bpmn/) | [Camunda BPMN examples](https://camunda.com/bpmn/examples/) |
| 💡 Identified | `iso/flowchart` | Flowchart | [ISO 5807 overview](https://en.wikipedia.org/wiki/Flowchart) | [Lucidchart examples](https://www.lucidchart.com/pages/examples/flowchart-symbols-and-meaning) |
| 💡 Identified | `generic/swimlane` | Swimlane diagram | [Lucidchart guide](https://www.lucidchart.com/pages/tutorial/swim-lane-diagram) | [Lucidchart examples](https://www.lucidchart.com/pages/examples/flowchart-symbols-and-meaning) |

---

## 6. Domain-Driven Design collaborative modeling

Workshop techniques from Eric Evans' DDD tradition that produce a diagram as a byproduct of a group activity.

| State | Origin | Diagram | Theory | Example |
|---|---|---|---|---|
| 💡 Identified | `ddd/event-storming` | Event Storming | [eventstorming.com](https://www.eventstorming.com/) | [Miro template](https://miro.com/templates/event-storming/) |
| 💡 Identified | `ddd/context-map` | Bounded Context / Context Map | [Context Mapping reference](https://www.domainlanguage.com/ddd/context-mapping/) | [Context Mapper example](https://contextmapper.org/docs/context-map/) |

---

## 7. Data-oriented diagrams

| State | Origin | Diagram | Theory | Example |
|---|---|---|---|---|
| 💡 Identified | `erd/entity-relationship` | Entity-Relationship Diagram (Chen or Crow's Foot notation) | [Chen's original paper](https://dl.acm.org/doi/10.1145/320434.320440) | [Lucidchart ERD examples](https://www.lucidchart.com/pages/examples/entity-relationship-diagram-tool) |
| 💡 Identified | `dfd/data-flow` | Data Flow Diagram (Yourdon/DeMarco or Gane–Sarson notation) | [DeMarco's foundational text](https://en.wikipedia.org/wiki/Data_flow_diagram) | [Lucidchart DFD examples](https://www.lucidchart.com/pages/examples/data-flow-diagram) |

---

## 8. Infrastructure, network & cloud diagrams

Not tied to one methodology — each cloud vendor's official icon set functions as a de-facto shared notation.

| State | Origin | Diagram | Theory | Example |
|---|---|---|---|---|
| 💡 Identified | `network/topology` | Network topology diagram | [Cisco iconography](https://www.cisco.com/c/en/us/products/downloads.html) | [Lucidchart examples](https://www.lucidchart.com/pages/examples/network-diagram-software) |
| 💡 Identified | `aws/architecture` | AWS architecture diagram | [AWS Architecture Icons](https://aws.amazon.com/architecture/icons/) | [AWS Architecture Center](https://aws.amazon.com/architecture/) |
| 💡 Identified | `azure/architecture` | Azure architecture diagram | [Azure Architecture Icons](https://learn.microsoft.com/en-us/azure/architecture/icons/) | [Azure Architecture Center](https://learn.microsoft.com/en-us/azure/architecture/browse/) |
| 💡 Identified | `gcp/architecture` | GCP architecture diagram | [Google Cloud Architecture Icons](https://cloud.google.com/icons) | [Google Cloud Architecture Center](https://cloud.google.com/architecture) |
| ✅ Implemented | `azure-devops/pipeline` | Azure DevOps pipeline diagram | [Azure Pipelines YAML schema](https://learn.microsoft.com/en-us/azure/devops/pipelines/yaml-schema/) · [Key pipelines concepts](https://learn.microsoft.com/en-us/azure/devops/pipelines/get-started/key-pipelines-concepts) | [Stages, dependsOn and conditions](https://learn.microsoft.com/en-us/azure/devops/pipelines/process/stages) · the pipeline `.yml` a repository already has |
| 🛠️ Work-in-progress | `ansible/structure` | Ansible project structure (read-only: playbooks, roles, inventories and their include/import/dependency relationships) | [Ansible directory layout](https://charlesreid1.com/wiki/Ansible/Directory_Layout/Details) · [Role dependencies](https://oneuptime.com/blog/post/2026-01-24-ansible-roles-dependencies/view) | [ansible-playbook-grapher](https://github.com/haidaraM/ansible-playbook-grapher) · [ansible-viz](https://github.com/aspiers/ansible-viz) · the folder tree a repository already has |

---

## 9. Strategy & landscape mapping

| State | Origin | Diagram | Theory | Example |
|---|---|---|---|---|
| 🛠️ Work-in-progress | `wardley/map` | Wardley Map | [learnwardleymapping.com](https://learnwardleymapping.com/) · [`wardley-map`](../.spec-workflow/specs/wardley-map/requirements.md) specifies the OnlineWardleyMaps `.owm` DSL | [Online Wardley Maps editor](https://onlinewardleymaps.com/) |

---

## 10. Knowledge & informal modeling

Diagram types for capturing and structuring knowledge rather than formal system architecture — no single standards body owns these, so `Origin` names the tool/author most associated with the notation, per `<vendor>/<diagram-type>`.

| State | Origin | Diagram | Theory | Example |
|---|---|---|---|---|
| ✅ Implemented | `freeplane/mindmap` | Mind map (radial/hierarchical, single central topic) | [Freeplane](https://www.freeplane.org/) · `.mm` file format (tech.md's diagram-type test fixture) | [Freeplane example maps](https://www.freeplane.org/wiki/index.php/Gallery) |
| 💡 Identified | `cmap/concept-map` | Concept map (free-form network of concepts with labeled relationships) | [Novak & Cañas, "The Theory Underlying Concept Maps"](https://cmap.ihmc.us/docs/theory-of-concept-maps) | [CmapTools example maps](https://cmap.ihmc.us/) |

Other mind-mapping tools (XMind, MindMeister, Coggle, FreeMind) could each get their own `<vendor>/mindmap` row if ADP ever needs to read/write their specific file formats; `mindmap-diagram`'s spec settled on Freeplane's `.mm` format specifically (see tech.md).

---

## 11. "Diagrams as code" tooling

Text-based tools that render many of the notations above from plain text, well suited to version control and to generation by Claude Code. Each one is a distinct *rendering path* for one or more diagram types already cataloged above — a code-authored `uml/class` diagram is still fundamentally a class diagram, but rendering it via Mermaid vs. ADP's own native editor is a different `Origin`, so each tool/diagram-type pair gets its own row here rather than being folded into the sections above.

### 11a. Mermaid

| State | Origin | Diagram | Theory | Example |
|---|---|---|---|---|
| 💡 Identified | `mermaid/flowchart` | Flowchart | [mermaid.js.org](https://mermaid.js.org/) | [Mermaid Live Editor](https://mermaid.live/) |
| 💡 Identified | `mermaid/sequence` | Sequence diagram | [mermaid.js.org](https://mermaid.js.org/) | [Mermaid Live Editor](https://mermaid.live/) |
| 💡 Identified | `mermaid/class` | Class diagram | [mermaid.js.org](https://mermaid.js.org/) | [Mermaid Live Editor](https://mermaid.live/) |
| 💡 Identified | `mermaid/state` | State diagram | [mermaid.js.org](https://mermaid.js.org/) | [Mermaid Live Editor](https://mermaid.live/) |
| 💡 Identified | `mermaid/er` | Entity-Relationship diagram | [mermaid.js.org](https://mermaid.js.org/) | [Mermaid Live Editor](https://mermaid.live/) |
| 💡 Identified | `mermaid/gantt` | Gantt chart | [mermaid.js.org](https://mermaid.js.org/) | [Mermaid Live Editor](https://mermaid.live/) |
| 💡 Identified | `mermaid/c4` | C4 diagram (subset) | [mermaid.js.org](https://mermaid.js.org/) | [Mermaid Live Editor](https://mermaid.live/) |
| 💡 Identified | `mermaid/architecture` | Architecture diagram | [mermaid.js.org](https://mermaid.js.org/) | [Mermaid Live Editor](https://mermaid.live/) |

### 11b. PlantUML

| State | Origin | Diagram | Theory | Example |
|---|---|---|---|---|
| 📝 Specified | `plantuml/uml` | Full UML set (see section 1); [`plantuml-uml`](../.spec-workflow/specs/plantuml-uml/requirements.md) specifies class and sequence first | [plantuml.com](https://plantuml.com/) | [Real World PlantUML gallery](https://real-world-plantuml.com/) |
| 💡 Identified | `plantuml/c4` | C4 diagram, via C4-PlantUML | [C4-PlantUML](https://github.com/plantuml-stdlib/C4-PlantUML) | [Real World PlantUML gallery](https://real-world-plantuml.com/) |

### 11c. Structurizr

| State | Origin | Diagram | Theory | Example |
|---|---|---|---|---|
| 💡 Identified | `structurizr/c4` | C4 model ("model once, view many") | [structurizr.com](https://structurizr.com/) | [structurizr.com/help/examples](https://structurizr.com/help/examples) |

### 11d. D2

| State | Origin | Diagram | Theory | Example |
|---|---|---|---|---|
| 💡 Identified | `d2/diagram` | General-purpose declarative diagram | [d2lang.com](https://d2lang.com/) | [D2 Playground](https://play.d2lang.com/) |

### 11e. Diagrams (Python)

| State | Origin | Diagram | Theory | Example |
|---|---|---|---|---|
| 💡 Identified | `diagrams-python/cloud-infrastructure` | Cloud/infrastructure diagram with official-style vendor icons | [diagrams.mingrammer.com](https://diagrams.mingrammer.com/) | [Diagrams gallery](https://diagrams.mingrammer.com/docs/getting-started/examples) |

### 11f. Context Mapper

| State | Origin | Diagram | Theory | Example |
|---|---|---|---|---|
| 💡 Identified | `contextmapper/context-map` | DDD Context Map, plus generated PlantUML/BPMN sketches | [contextmapper.org](https://contextmapper.org/) | [Context Mapper example](https://contextmapper.org/docs/context-map/) |
