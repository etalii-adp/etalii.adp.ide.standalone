# Technology trends

About two hundred large technological and societal trends of roughly the past 250 years, from the Industrial Revolution and the steam engine through electricity, railways, the telephone, radio, the automobile, aviation, computing, the Internet and mobile phones to machine learning and large language models, drawn as a Gartner hype cycle graph and related by the influences they had on one another.

## Why this diagram

The Gartner Hype Cycle Graph places socio-technological **Trends** on a shared time axis, shows how far each has travelled through the phases of the Gartner hype cycle, and draws the **Influences** trends have had on one another. Its purpose is insight into how trends of any kind relate: which earlier trend made a later one possible, which one pulled another out of its trough, and where a cluster of mutually reinforcing trends sits in time.

The classic hype cycle chart shows one curve and plots each technology as a dot on it, which says where a technology stands today and nothing about how it got there or what moved it. Plotting trends against real time, each spanning the years its phases took, and connecting them with influences turns the chart into a causal history. A reader can follow electricity into telegraphy, telegraphy into telephony, telephony into mobile phones, and mobile phones into the smartphone economy, and see that each influence lands on a specific phase: the steam engine influenced the railways during its plateau, when it had become dependable, not during its peak. That is the value-add: the diagram makes visible *when in its life* a trend influenced another, which neither a timeline nor a plain dependency graph can say. It applies equally to trends that are not technological at all, such as social movements, regulation, or market shifts.

## Hand-authored, and why

**This example was written for ADP, and it is exempt from the published-data rule.** The rule is that diagram modules are tested against real published example data rather than hand-written toys. It cannot apply here: the hype cycle graph's notation - trends spanning dated phases, with influences anchored to a phase of each - is ADP's own, so no published corpus of it exists.

**Its dates and influences are illustrative, not a historical claim.** Every start and stop date is plausible to the year, and every influence is one a history of technology would recognise, but the phase boundaries are mostly spread evenly rather than researched, and the phase an influence attaches to is chosen by when it acted rather than argued from sources. Read it as a demonstration of what the diagram can say, not as a history.

## What it shows

| Part | In this example |
| --- | --- |
| Trends | 200, in six clusters of rows: industry, energy, transport, communication, computing, and society, with an empty row between every two rows of trends |
| Influences | 263, most of them between clusters, each attached to the phase of each trend in which it acted: the two phases overlap in time, or, where one trend ended before the other began, it acts from its last phase on the other's Peak |
| Phase counts | Every count from 1 to 4: trends of the past show all four phases, recent ones such as AI agents, fusion or small modular reactors only a Peak |
| Edges | Influences attach to the top and the bottom edge of every one of the four phases |
| A hidden influence | Autonomous vehicles to urban air mobility is attached to the Slope of a trend that shows only two phases, so it is hidden, and kept in the file |
| Both directions | The steam engine influenced coal, and coal the steam engine: one influence per direction is allowed |
| Dragged boundaries | The steam engine, nuclear power, the dot-com bubble, railway mania, symbolic AI, solar photovoltaics, large language models and virtual reality have phase boundaries placed by hand; every other trend's phases are even |
| Descriptions | On trends and on influences, kept in the document and never drawn |

**Tags make at least three meaningful filters**: `energy` alone; `communication` and `computing` with the filter on All; and `transport` and `energy` on Any. Each matches some trends and hides the rest. The other tags are `industry`, `society`, `science`, `health` and `finance`.

## What it does not demonstrate

- **A document that breaks the rules.** Every rule holds here, and the validator reports nothing for it. The broken documents are the test fixtures under `../../backend/EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests/Fixtures/`, one per rule.
- **Base36 ids.** Every id here is readable, like `steam-engine` or `steam-engine--railways`.
- **Trends that are not technological or societal**, such as a regulation, a market or an organisation's own initiatives, though the diagram applies to them equally.
- **A trend whose phases are researched.** Most boundaries are even, as the diagram draws them until one is dragged.
- **Influences that are uncertain or disputed.** The notation has no way to say so, and this example does not try.
