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
| ✅ Implemented (prototype) | `c4/context` | System Context | [c4model.com](https://c4model.com/diagrams/system-context) | Everyone-facing overview |
| ✅ Implemented (prototype) | `c4/container` | Container | [c4model.com](https://c4model.com/diagrams/container) | Deployable/runnable units |
| ✅ Implemented (prototype) | `c4/component` | Component | [c4model.com](https://c4model.com/diagrams/component) | Building blocks inside one container |
| 📝 Specified | `c4/code` | Code (optional) | [c4model.com](https://c4model.com/diagrams/code) | Usually IDE-generated |
| ✅ Implemented (prototype) | `c4/system-landscape` | System Landscape (supplementary) | [c4model.com](https://c4model.com/diagrams/system-landscape) | Multiple systems across an org |
| ✅ Implemented (prototype) | `c4/dynamic` | Dynamic (supplementary) | [c4model.com](https://c4model.com/diagrams/dynamic) | One scenario across containers/components |
| ✅ Implemented (prototype) | `c4/deployment` | Deployment (supplementary) | [c4model.com](https://c4model.com/diagrams/deployment) | Containers mapped onto infrastructure |

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
| ✅ Implemented | `generic/timeline` | Timeline diagram (left-to-right, elements spanning begin→end on snap-to rows, bezier connections; `.tml` — Timeline Markup Language); [`timeline-diagram`](../.spec-workflow/specs/timeline-diagram/requirements.md) | Distinct from `mermaid/gantt`: placement is authored on both axes, not computed, and connections are arbitrary rather than scheduling dependencies | — |

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

| State                       | Origin | Diagram | Theory | Example |
|-----------------------------|---|---|---|---|
| 💡 Identified               | `network/topology` | Network topology diagram | [Cisco iconography](https://www.cisco.com/c/en/us/products/downloads.html) | [Lucidchart examples](https://www.lucidchart.com/pages/examples/network-diagram-software) |
| 💡 Identified               | `aws/architecture` | AWS architecture diagram | [AWS Architecture Icons](https://aws.amazon.com/architecture/icons/) | [AWS Architecture Center](https://aws.amazon.com/architecture/) |
| 💡 Identified               | `azure/architecture` | Azure architecture diagram | [Azure Architecture Icons](https://learn.microsoft.com/en-us/azure/architecture/icons/) | [Azure Architecture Center](https://learn.microsoft.com/en-us/azure/architecture/browse/) |
| 💡 Identified               | `gcp/architecture` | GCP architecture diagram | [Google Cloud Architecture Icons](https://cloud.google.com/icons) | [Google Cloud Architecture Center](https://cloud.google.com/architecture) |
| ✅ Implemented (prototype)  | `azure-devops/pipeline` | Azure DevOps pipeline diagram | [Azure Pipelines YAML schema](https://learn.microsoft.com/en-us/azure/devops/pipelines/yaml-schema/) · [Key pipelines concepts](https://learn.microsoft.com/en-us/azure/devops/pipelines/get-started/key-pipelines-concepts) | [Stages, dependsOn and conditions](https://learn.microsoft.com/en-us/azure/devops/pipelines/process/stages) · the pipeline `.yml` a repository already has |
| ✅ Implemented (prototype)  | `ansible/structure` | Ansible project structure (read-only: playbooks, roles, inventories and their include/import/dependency relationships) | [Ansible directory layout](https://charlesreid1.com/wiki/Ansible/Directory_Layout/Details) · [Role dependencies](https://oneuptime.com/blog/post/2026-01-24-ansible-roles-dependencies/view) | [ansible-playbook-grapher](https://github.com/haidaraM/ansible-playbook-grapher) · [ansible-viz](https://github.com/aspiers/ansible-viz) · the folder tree a repository already has |

---

## 9. Strategy & landscape mapping

| State | Origin | Diagram | Theory | Example |
|---|---|---|---|---|
| ✅ Implemented (prototype) | `wardley/map` | Wardley Map | [learnwardleymapping.com](https://learnwardleymapping.com/) · reads and writes the [OnlineWardleyMaps `.owm` DSL](https://onlinewardleymaps.com/), keeping a map another tool wrote byte-for-byte · [`wardley-map`](../.spec-workflow/archive/specs/wardley-map/requirements.md) | [Online Wardley Maps editor](https://onlinewardleymaps.com/) |

---

## 10. Knowledge & informal modeling

Diagram types for capturing and structuring knowledge rather than formal system architecture — no single standards body owns these, so `Origin` names the tool/author most associated with the notation, per `<vendor>/<diagram-type>`.

| State | Origin | Diagram | Theory | Example |
|---|---|---|---|---|
| ✅ Implemented (prototype) | `freeplane/mindmap` | Mind map (radial/hierarchical, single central topic) | [Freeplane](https://www.freeplane.org/) · `.mm` file format (tech.md's diagram-type test fixture) | [Freeplane example maps](https://www.freeplane.org/wiki/index.php/Gallery) |
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
| 📝 Specified | `plantuml/uml` | Full UML set (see section 1) | [plantuml.com](https://plantuml.com/) · [`plantuml-uml`](../.spec-workflow/specs/plantuml-uml/requirements.md) specifies class and sequence first | [Real World PlantUML gallery](https://real-world-plantuml.com/) |
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

---

## 12. Data platforms & pipeline orchestration

Configuration and definition files that data platforms already carry — bundles, DAGs, workflow definitions, lineage events. Every one of them is an established text artifact another tool deploys or executes, which is exactly the "files are the source of truth" shape ADP wants: the diagram renders (and where safe, edits) the file, and never invents a format of its own. Open-source standards are preferred where they exist.

| State | Origin | Diagram | Theory | Example |
|---|---|---|---|---|
| 📝 Specified | `databricks/bundle` | Databricks Asset Bundle topology (targets, resources, overrides) · [`databricks-diagrams`](../.spec-workflow/specs/databricks-diagrams/requirements.md) | [Bundle configuration](https://docs.databricks.com/aws/en/dev-tools/bundles/settings) · [reference](https://docs.databricks.com/aws/en/dev-tools/bundles/reference) | the `databricks.yml` a repository already has |
| 📝 Specified | `databricks/job` | Lakeflow Jobs task DAG (tasks, depends_on, run_if, clusters) · [`databricks-diagrams`](../.spec-workflow/specs/databricks-diagrams/requirements.md) | [Job task types](https://docs.databricks.com/aws/en/dev-tools/bundles/job-task-types) | the job resource `.yml` a bundle already has |
| 📝 Specified | `databricks/pipeline` | Lakeflow Declarative Pipelines settings (sources, pipeline, target catalog) · [`databricks-diagrams`](../.spec-workflow/specs/databricks-diagrams/requirements.md) | [Pipeline properties](https://docs.databricks.com/aws/en/ldp/properties) | the pipeline settings `.json` a workspace exports |
| 💡 Identified | `spark/declarative-pipeline` | Spark Declarative Pipelines (SDP) spec (`spark-pipeline.yml`: name, libraries, storage; flows, streaming tables and materialized views declared in the SQL/Python sources it names). The open-source donation of Databricks DLT, native to Apache Spark since 4.1 — the upstream sibling of `databricks/pipeline` | [SDP programming guide](https://spark.apache.org/docs/latest/declarative-pipelines-programming-guide.html) | the `spark-pipeline.yml` a repository already has |
| 💡 Identified | `airflow/dag` | Apache Airflow DAG (tasks and dependencies; DAG files are Python, so a read-only structural view is the realistic first step) | [airflow.apache.org](https://airflow.apache.org/) · [DAGs concept](https://airflow.apache.org/docs/apache-airflow/stable/core-concepts/dags.html) | [Airflow graph view](https://airflow.apache.org/docs/apache-airflow/stable/ui.html) |
| 💡 Identified | `dbt/lineage` | dbt project lineage (models, sources, `ref()`/`source()` edges from `dbt_project.yml` + `schema.yml` + manifest) | [docs.getdbt.com](https://docs.getdbt.com/) · [About dbt projects](https://docs.getdbt.com/docs/build/projects) | [dbt lineage graph](https://docs.getdbt.com/terms/data-lineage) |
| 💡 Identified | `dagster/assets` | Dagster asset graph (software-defined assets and their dependencies) | [dagster.io](https://dagster.io/) · [Asset definitions](https://docs.dagster.io/guides/build/assets/) | [Dagster asset lineage UI](https://docs.dagster.io/guides/operate/webserver) |
| 💡 Identified | `nifi/flow` | Apache NiFi flow definition (processors, connections, process groups from an exported flow definition JSON) | [nifi.apache.org](https://nifi.apache.org/) | [NiFi flow canvas](https://nifi.apache.org/docs/nifi-docs/html/user-guide.html) |
| 💡 Identified | `argoproj/workflow` | Argo Workflows definition (Kubernetes CRD YAML: steps, DAG templates, artifacts) | [argo-workflows docs](https://argo-workflows.readthedocs.io/) | [DAG template examples](https://argo-workflows.readthedocs.io/en/latest/walk-through/dag/) |
| 💡 Identified | `kubeflow/pipeline` | Kubeflow Pipelines definition (compiled pipeline IR YAML: components, inputs/outputs, DAG) | [kubeflow.org](https://www.kubeflow.org/docs/components/pipelines/) | [KFP pipeline graph](https://www.kubeflow.org/docs/components/pipelines/overview/) |
| 💡 Identified | `commonwl/workflow` | Common Workflow Language workflow (open standard YAML: steps, inputs/outputs, scatter) | [commonwl.org](https://www.commonwl.org/) · [CWL user guide](https://www.commonwl.org/user_guide/) | [CWL Viewer](https://view.commonwl.org/) |
| 💡 Identified | `openwdl/workflow` | Workflow Description Language workflow (open standard `.wdl`: tasks, calls, dataflow) | [openwdl.org](https://openwdl.org/) | [WDL examples](https://github.com/openwdl/wdl) |
| 💡 Identified | `nextflow/workflow` | Nextflow pipeline (`.nf` DSL2 processes and channels; read-only structural view) | [nextflow.io](https://www.nextflow.io/) | [nf-core pipelines](https://nf-co.re/pipelines) |
| 💡 Identified | `snakemake/workflow` | Snakemake workflow (Snakefile rules and their input/output dependency DAG) | [snakemake.readthedocs.io](https://snakemake.readthedocs.io/) | [Snakemake --dag output](https://snakemake.readthedocs.io/en/stable/executing/cli.html) |
| 💡 Identified | `meltano/project` | Meltano ELT project (`meltano.yml`: extractors, loaders, transforms, schedules as a source→target flow) | [meltano.com](https://meltano.com/) · [meltano.yml reference](https://docs.meltano.com/reference/project) | the `meltano.yml` a repository already has |
| 💡 Identified | `openlineage/lineage` | OpenLineage lineage graph (open standard run/job/dataset events, LF AI & Data; read-only graph over captured events) | [openlineage.io](https://openlineage.io/) · [spec on GitHub](https://github.com/OpenLineage/openlineage) | [Marquez](https://marquezproject.ai/) |

---

## 13. Ontologies & semantic web

Open W3C and community standards for knowledge representation. The visualization tradition here is strong (VOWL and its kin), and the files are plain text — Turtle, RDF/XML, YAML — that other tools own outright.

| State | Origin | Diagram | Theory | Example |
|---|---|---|---|---|
| 💡 Identified | `w3c/owl` | OWL 2 ontology (classes, properties, individuals, axioms; Turtle/RDF-XML/functional syntax) | [W3C OWL 2 overview](https://www.w3.org/TR/owl2-overview/) | [WebVOWL](https://service.tib.eu/webvowl/) · [VOWL notation](http://vowl.visualdataweb.org/) |
| 💡 Identified | `w3c/skos` | SKOS concept scheme (concepts, broader/narrower/related, collections) | [W3C SKOS reference](https://www.w3.org/TR/skos-reference/) | [SKOS Play](https://skos-play.sparna.fr/) |
| 💡 Identified | `w3c/shacl` | SHACL shapes graph (node/property shapes, targets, constraints over an RDF data graph) | [W3C SHACL](https://www.w3.org/TR/shacl/) | [SHACL Play](https://shacl-play.sparna.fr/play/) |
| 💡 Identified | `linkml/schema` | LinkML schema (open-source YAML modeling language: classes, slots, enums, inheritance) | [linkml.io](https://linkml.io/) · [schema guide](https://linkml.io/linkml/schemas/) | [LinkML generated ER/UML views](https://linkml.io/linkml/generators/) |

---

## 14. Database queries & results

Query languages whose statements and result sets are themselves worth a picture — the query as a structure (a graph pattern, a stage pipeline) and the result as a graph or document set. Distinct from section 7's schema diagrams: these visualize *asking*, not modeling.

| State | Origin | Diagram | Theory | Example |
|---|---|---|---|---|
| 💡 Identified | `neo4j/cypher` | Cypher query and result graph (MATCH patterns as node/edge structure; results as an interactive graph). Cypher is standardized via [openCypher](https://opencypher.org/) and the ISO [GQL](https://www.gqlstandards.org/) standard | [Cypher manual](https://neo4j.com/docs/cypher-manual/current/) | [Neo4j Browser graph view](https://neo4j.com/docs/browser-manual/current/) |
| 💡 Identified | `mongodb/aggregation-pipeline` | MongoDB aggregation pipeline (MQL stages — `$match`, `$group`, `$lookup`, … — as a left-to-right stage flow, with document shape per stage) | [Aggregation pipeline manual](https://www.mongodb.com/docs/manual/core/aggregation-pipeline/) | [Compass aggregation builder](https://www.mongodb.com/docs/compass/current/create-agg-pipeline/) |
| 💡 Identified | `mongodb/query` | MongoDB find query and result documents (MQL filter/projection as a predicate tree; results as a document/tree view) | [MongoDB CRUD queries](https://www.mongodb.com/docs/manual/tutorial/query-documents/) | [Compass documents view](https://www.mongodb.com/docs/compass/current/documents/) |
