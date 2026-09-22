# Findings — the publish race, 2026-09-20 to 2026-09-22

**THREE occurrences of `0x80070497` ("Unable to remove the file to be replaced") that nobody has explained, the measurements that narrowed them, and how to read the next one.** Recorded here because the evidence itself cannot be: the logs are about 6 MB each, too large to commit, and an excerpt loses the interleaving that makes them readable.

**Where the evidence is:** `C:\Users\vrenk\AppData\Local\adp-evidence\2026-09-20-publish-race\` — `2026-09-20-wardley-tea-owm-0x80070497.log`, `2026-09-22-editor-resolution-deadline.log`, and a `README.md` carrying the measurement table. Developer 3 moved the surviving copies there because a gate's own log directory keeps only ten runs. **That location is durable against sessions, not against the machine**: it is one disk, outside the repository, and nothing backs it up. The third occurrence's log IS copied there, as `2026-09-22-timeline-plan-tml-0x80070497.log`, taken from the gate's kept `-red` directory before pruning could reach it. **If these ever matter beyond this week they need somewhere better than one unbacked disk.**

## The sentence that must survive

**"No process was holding it when asked" is an ANSWER, not a failed query.** A completed delete or a finished publish holds nothing by the time anyone asks, and the Restart Manager can only name a holder that still exists. So on the next occurrence **the destination's state is what speaks**: present or GONE, and whether another publisher's scratch or backup file sits beside it. A reader who treats the empty holder list as "the instrument failed" will go looking for a better instrument instead of reading the answer it gave.

## What is measured, and by whom

Developer 3's measurements, each independently checkable from the code paths named:

| Arrangement | Result |
| --- | --- |
| Two concurrent `File.Replace` calls on one destination | `0x80070497` **29 times in 800**, plus `0x20`, `0x498`, `0x499` and `FileNotFound` |
| A concurrent DELETE-AND-RECREATE of the destination, 400 replaces (mixed-actor harness) | `0x80070497` **185**, `0x80070020` **65**, `FileNotFound` **150**, and **0 successes** |
| A concurrent deleter, 400 replaces, with and without the per-destination turn (turn harness) | without it **305 failures** - `0x800700B7` 148, `FileNotFound` 76, `0x80070497` **75**, `0x80070005` 5, `0x80070020` 1. With it **400 of 400 succeed** and no code appears at all |
| A reader sharing only `Read` (a raw `File.ReadAllText`) | `0x80070020` every time, **never** `0x497` |
| A reader sharing `ReadWrite \| Delete` (`SharedDocumentReader`) | the replace succeeds |
| A single writer, no other actor | **0 of 400** |

**The delete arrangement is TWO rows because it was two harnesses, and this record said `0x80070497` "305
of 400" until 2026-09-22.** 305 was the turn harness's total across every code it produced; `0x497` was 75 of
them, and 185 of 400 in the other harness. **Quote 305 only for the fix - 305 failures against 0 - and 185 or
75 for the error code.** Developer 3 measured both and caught the conflation; by then it had reached this
table, a steering clause on its way to the gate, and two working notes - a propagation lesson queued for
`processes.md` rather than landed there yet: correcting the source does not correct the copies, so the sweep
is *where else does this number appear*. **Two harnesses, two distributions, one
staged actor - which is the evidence FOR reading a staged rate as mechanism rather than frequency, not an
inconsistency in it.**

**So `0x497` means a second party that REPLACED or DELETED the destination.** Readers are excluded by measurement in both directions, which is what makes the diagnosis narrower than "a file was busy".

**Landed from this:** deletes of a published path now take the same per-destination turn a save takes, and the failure record names the holders it can see, our own process id, and the destination's state.

## A third occurrence, 2026-09-22, with the new record in place

It happened again during the gate of this very document - a documentation-only branch, so nothing in the change
could be the cause. `TimelineFlowTests.RemovingAConnectedElement_AsksWithTheCount_AndTheWholeRemovalIsOneUndo`
failed with *"plan.tml could not be written. The change is still here to try again."* **The failure record,**
**which did not exist for the first two occurrences, said more than either of them could:**

- `IOException 0x80070497 Unable to remove the file to be replaced.`
- `this process is pid 953272`
- `holders: holders could not be determined: the query did not answer within 2 seconds`
- `destination: present, 132 bytes`

**Two things follow, and the second is a gap in the INSTRUMENT rather than in the diagnosis.**

1. **The destination was PRESENT, not gone.** So this occurrence is not the already-released-deleter shape the
   measurements made dominant: the replace failed while the destination existed and had content.
2. **"The query did not answer within 2 seconds" is not the same as "nobody was holding it".** The first is a
   MISSING measurement, the second is an answer - and **the two-second budget is most likely to expire exactly
   when the failure is most likely, under gate load**, so the instrument is weakest precisely where it is
   needed. **Both halves of that were answered on 2026-09-22, and not the way this record proposed**: the
   elapsed time is now logged beside every answered query, the two-second number was deliberately LEFT ALONE,
   and the defect turned out to be the ABANDONMENT rather than the budget. The subsection below says how.

**So the reading procedure gains a third case: holders NAMED, holders EMPTY, or holders UNKNOWN - and only the
first two are answers.** The remedy below turns the third into two cases of its own, one of which is an answer.

## The instrument's remedy, and the fourth reading it introduces

**The budget was not raised. The query is no longer ABANDONED when it expires** (Developer 3, landed in
`5efb359f`). It runs on its own thread, the failing save carries on at once, and the query logs its own line
when it answers - repeating the path and the pid of the first, so the two join without comparing timestamps. A
longer budget would have delayed every failing save, under exactly the load that makes the query slow, to buy
an answer on a few.

| What the failure record says | What it means |
| --- | --- |
| `holders: <names> (answered in <n> ms)` | an answer, with the cost of getting it |
| `holders: no process was holding it when asked` | an answer: nobody the query could see, the already-released shape |
| `holders could not be determined: <exception>` | no measurement - the query itself failed |
| `holders not yet known after 2s; a later line for this path says what the query found, or none does and it never answered` | no measurement YET |

**For the last one, look further down the log for a line naming the same path and pid.** Present, the service
was merely slow and the holders are named; absent, it never answered at all. **Slow and never are different
facts about the machine at the moment of failure, and no budget can tell them apart** - which is why the third
occurrence's `did not answer within 2 seconds` can never be resolved either way now.

**Read "not yet known" as a timestamp, not a verdict.** It says when the query had not answered, and nothing
at all about who was holding the file.

## What is still open

1. **The second actor is unnamed.** In the 2026-09-20 wardley occurrence there was **no "Waited for another save" warning anywhere in the log**, so no second writer inside this process's lock was involved — the actor was outside it. Nothing in the repository writes a diagram body outside `AdpFileWriter`, fixture folders are per-fixture GUIDs, and the lock's dictionary ignores case on Windows, all checked. **It remains unexplained.** And the staged figures cannot narrow it: they say a replace and a delete both PRODUCE this error, never which one happened when nobody was staging anything. The third occurrence's present-with-content destination is the only fact anyone has about the wild actor rather than about a perturbation, which is why it is the counter-example the steering clause is anchored on.
2. **Architect 1's wardley red is unexplained, and the delete fix does not close it.** `WardleyIdentities` deletes the SIDECAR, never the body, so no in-repository deleter touches `tea.owm`. Developer 3 said so in its own landing rather than letting the fix imply a cause.
3. **A wrong inference to avoid repeating.** Architect 1 argued from the missing "Waited for another save" line that the recurrence was not the racing-writers case. That was wrong: the warning only fires for a writer inside this process's lock, so its absence says the second writer was OUTSIDE the lock — the more interesting answer, not the negative one. The general form is in `processes.md`, *Measure the thing, not something adjacent to it*.

## How to read the next occurrence

1. **Copy the run's log out of the gate's log directory immediately** — pruning keeps ten runs, and a red run's directory is kept only under its `-red` name.
2. **Read the failure record first**, not the stack: it carries the holders the query could see, this process's id, and the destination's state at the moment of failure. **An empty holder list with a GONE destination is the signature of an actor that has already released** — which the measurements say is the dominant case.
   **A holder list that says the query did not answer is NOT an empty list**: it is no measurement at all,
   and the third occurrence above is one, so the destination's state is the only evidence in that case.
3. **Check for a "Waited for another save" warning.** Present, the second writer was in-process and the lock serialised them. Absent, the actor was outside this process's lock, which is the open question above.
4. **Do not attribute a bare `Exit code:` line to the assembly below it**; it belongs to the one above, and with assemblies running in parallel adjacency means nothing at all.

**This file is the record; the rule each of its lessons generalises to is in `processes.md`.** A finding without its general form is an anecdote; a general form without its finding is an assertion.
