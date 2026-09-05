# Findings

Recorded after this specification's tasks were complete, for a future amendment. Each entry states what was found, what it would cost, and why it is here rather than in another specification.

## Agent-facing documents have no link guard

**Requirement 3.5 mandates a link that nothing checks.** It says a how-to-use step duplicating a CLAUDE.md rule "SHALL summarise in one line and link, not restate", because *"CLAUDE.md is written for agents working on the repository; the readme is written for people using it — the two SHALL reference rather than fork each other."* So this specification already recognises the agent-facing class and already requires cross-references into it. No guard checks that those references resolve.

`DocumentationLinks.Tests.cs` covers **exactly five delivered documents** — `readme.md`, `docs/dependencies.md`, the two module walkthroughs and `docs/screenshots/readme.md` — and says so deliberately: *"The scope is exactly the delivered list, not a repository-wide crawl… Widening the net is a later decision, deliberately not smuggled into this guard."* CLAUDE.md and the steering documents are outside it. This entry is that later decision, raised where the guard reserved it.

### What happened, because the justification is the incident and not the principle

On 2026-09-04 a rule was added to CLAUDE.md requiring each agent to set a per-task git identity. As written it prescribed `git config user.name` inside a worktree — which writes to the **shared** `.git/config` that every worktree and the main checkout have in common, so each agent that followed it renamed every other agent. The key was written by four sessions within two hours; two commits carry the wrong agent's name and remain in history, because rewriting shared history for a name is worse than the name.

The rule linked to its full reasoning at `processes.md#git-identity`. **That section did not exist.** So an agent following the instruction to its reasoning found nothing, and applied a one-line rule whose *why* was unreachable — which is how the original wording went wrong in the first place.

**An instruction whose reasoning is unreachable is one an agent applies without understanding.** That is the argument for guarding these documents, and it does not transfer to a readme: a broken link in `readme.md` fails a *reader* — cosmetic, embarrassing, eventually noticed by a human who can route around it. A broken link in CLAUDE.md fails an *agent*, silently, in the direction of acting on a rule it cannot check.

### Why a sibling guard rather than a wider list

The delivered list draws a distinction worth keeping. Its documents are what a reader of the repository is handed; CLAUDE.md and the steering documents are instructions to agents. The list already excludes `docs/diagrams.md` and the module readmes for a stated reason, so its boundary is considered rather than accidental, and stretching "delivered" to cover project instructions would blur the one distinction it draws. The two classes also fail differently, as above. **A second guard beside the first, not a longer array in it.**

### What it would cost, measured

| | Existing guard | Proposed sibling |
| --- | --- | --- |
| Documents | 5 delivered | 5 agent-facing — `CLAUDE.md` and the four under `.spec-workflow/steering/` |
| Relative links | 89 | **15** |
| Broken today | 0 | **0** |

One test file of roughly forty lines: a different `Documents` array over the same regex, the same resolution rule, the same extraction floor. **The sibling file is the design** — it exists in the tree to copy, so no design phase is needed.

It would go in **green**, repairing nothing, which is the cheapest moment to add a guard. The broken link that prompted this was fixed at `226bfc06` before it was measured; the guard would have caught it within one gate run.

### Scope notes for whoever implements it

- **Keep the extraction floor.** The parent guard's floor exists because a pattern that stopped matching would check nothing and pass; at 15 links the floor should be low enough to tolerate documents shrinking and high enough to catch total extraction failure.
- **Steering documents move.** `processes.md` is absorbing material from CLAUDE.md, so the two will cross-reference heavily; that traffic is the reason the guard earns its place rather than an argument against pinning the list.
- **A deliberately unlinked reference is not a broken one.** CLAUDE.md currently names `processes.md` in prose without linking it, precisely because the target section does not exist yet. The guard checks links, so this passes — and that is correct behaviour, not a gap to close.
