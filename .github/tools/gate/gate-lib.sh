# gate-lib.sh - the shared gate's testable parts. Sourced by gate.sh, land.sh and gate.test.sh;
# nothing here runs on its own. Why each part is shaped the way it is: processes.md,
# "Committing and merging in the shared main checkout" and "Running the backend tests".

# extract_guard - reads processes.md on stdin, prints the scratch-worktree guard on stdout.
#   0  exactly one fenced sh block containing `canon() {`, carrying every required part
#   2  no such block      3  more than one      4  a required part is missing
#
# A chain that extracts its guard by pattern runs UNGUARDED the moment an edit splits, renames or
# truncates the block - silently, unless the extractor refuses. So it refuses anything but one
# intact block, and "intact" includes the newest check (symbolic-ref, the detached refusal): a
# chain handed an older block aborts rather than running with less.
extract_guard() {
  local blk rc need
  blk=$(tr -d '\r' | awk -v want='canon() {' '
    /^```sh/            { inb = 1; buf = ""; next }
    /^```/ && inb       { inb = 0; if (index(buf, want)) { n++; out = buf }; next }
    inb                 { buf = buf $0 "\n" }
    END                 { if (n == 1) { printf "%s", out; exit 0 }; exit (n == 0 ? 2 : 3) }')
  rc=$?
  [ "$rc" -eq 0 ] || return "$rc"
  for need in 'show-toplevel' '$MAIN_TOP' 'symbolic-ref'; do
    case "$blk" in *"$need"*) ;; *) return 4 ;; esac
  done
  printf '%s\n' "$blk"
}

# gate_canon <path> - one spelling per path, for comparisons: Windows form where cygpath exists,
# lower case, no trailing slash. The same definition as the guard's canon() in processes.md. When
# cygpath is missing the raw path is kept, so on Windows every comparison fails - closed - rather
# than every path becoming the same empty string.
gate_canon() {
  local p
  p=$(cygpath -m "$1" 2> /dev/null) || p=$1
  printf '%s' "$p" | tr '[:upper:]' '[:lower:]' | sed 's:/*$::'
}

# gate_main_checkout <dir> - prints the main checkout of the repository <dir> belongs to: the
# parent of the shared git directory, which is the same answer from the main checkout and from
# every worktree. Fails when there is no such thing.
gate_main_checkout() {
  local common
  common=$(git -C "$1" rev-parse --path-format=absolute --git-common-dir 2>/dev/null) || return 1
  case "$common" in */.git) printf '%s\n' "${common%/.git}" ;; *) return 1 ;; esac
}

# gate_is_develops <main> <dir> <file>... - succeeds only when every named file in <dir> is
# byte-identical to develop's committed copy of .github/tools/gate/<file>. The point of a shared
# gate is that everybody runs the same one: a local copy that has drifted, or a branch's edited
# copy, is refused rather than trusted. A file develop does not have compares as different.
gate_is_develops() {
  local main=$1 dir=$2 f
  shift 2
  for f in "$@"; do
    git -C "$main" show "develop:.github/tools/gate/$f" 2>/dev/null | cmp -s - "$dir/$f" || {
      GATE_DIFFERS=$f
      return 1
    }
  done
}

# gate_tree_matches_head <dir> - 0 when the worktree's tracked CONTENT is its HEAD's, 1 when it
# differs, 2 when it cannot be read. Content, not `git status`: the client's pretest regenerates
# src/client/src/generated/ with LF over a CRLF checkout, and status reports every such file as
# modified with nothing in it changed - the shared gate's first real run, all four gates green,
# refused on exactly that. `git diff` compares after the same normalisation git applies on commit.
# retire.sh's first real use then refused a clean scratch tree the same way. The shape was already
# in the board's shared notes from 2026-09-04 (credited to Developer 1): after `buf generate` those
# files show as modified, "line-ending churn only, not content", and `git diff --numstat` returns
# nothing. It reached neither script because nobody asked them the question that record answers:
# "modified" to git status and "changed" are different claims. Ask this function the second one.
gate_tree_matches_head() {
  git -C "$1" diff --quiet HEAD -- 2> /dev/null
  case $? in
    0) return 0 ;;
    1) return 1 ;;
    *) return 2 ;;
  esac
}

# --- A run's logs. Two requirements that look opposed and are not.
#
# One: no run may read another run's log. A reused scratch tree once served a twelve-minute-old green
# dotnet log as the current run's, which is why the logs used to be deleted at the start of a run.
# Two: a REFUSED run's logs must survive the next run. On 2026-09-12 Developer 3 asked for the log of
# a flake that only appears under the full suite, and the re-gate had already deleted it - the author
# of the deletion losing the evidence it was asked for, hours after writing it.
#
# Both hold once each run writes to its OWN directory, named from the clock and the pid, and a
# refused run's directory is kept under a name a live run cannot produce. Staleness stops being
# something to delete against and becomes impossible: gate_run_logs_dir refuses a directory that
# already holds anything.

# gate_run_logs_dir <parent> <run-id> - creates and prints this run's log directory.
#   1 cannot create      2 not empty (something else is using it - never write there)
gate_run_logs_dir() {
  local dir="$1/$2"
  mkdir -p "$dir" 2> /dev/null || return 1
  [ -z "$(ls -A "$dir" 2> /dev/null)" ] || return 2
  printf '%s\n' "$dir"
}

# gate_keep_logs_red <dir> <why> - keeps a refused run's logs: renames <dir> to <dir>-red, writes a
# note inside saying what it is, points <parent>/last-red-run.txt at it, and prints the new path.
# The note and the pointer exist so somebody who does not know this convention still finds it.
# Idempotent: a directory already kept is left where it is.
#
# A rename that fails - a file in it held open, or a <dir>-red that already exists - keeps the logs
# where they are and still prints a path, because an empty KEPT_LOGS= tells the reader the logs are
# gone when they are not. `mv -T` because plain mv onto an existing directory moves <dir> INSIDE it,
# which reports success and prints a path that holds another run's logs. The note says the rename
# failed, since a directory without the -red name is pruned as a plain run after ten newer ones.
gate_keep_logs_red() {
  local dir=$1 why=${2:-} kept renamed=yes
  case "$dir" in *-red) printf '%s\n' "$dir"; return 0 ;; esac
  [ -d "$dir" ] || return 1
  kept="$dir-red"
  if ! mv -T "$dir" "$kept" 2> /dev/null; then
    kept=$dir
    renamed=no
  fi
  {
    echo "These are the logs of a gate run that was REFUSED, kept on purpose."
    echo "Run:     $(basename "$dir")"
    echo "Verdict: ${why:-refused}"
    echo "Kept at: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
    if [ "$renamed" = no ]; then
      echo
      echo "The rename to $(basename "$dir")-red FAILED, so these logs stayed under a plain run's name."
      echo "Pruning treats them as a plain run and deletes them after ten newer runs: copy them out."
    fi
    echo
    echo "A later gate run writes to its own directory beside this one and never touches this one."
    echo "dotnet-test.log holds the per-assembly stdout, including any server-side exception."
    echo "Delete it once whoever is investigating has what they need."
  } > "$kept/WHAT-THIS-IS.txt" 2> /dev/null
  printf '%s\n' "$kept" > "$(dirname "$dir")/last-red-run.txt" 2> /dev/null
  printf '%s\n' "$kept"
}

# gate_prune_logs <parent> <keep> - keeps the newest <keep> plain run directories and the newest
# <keep> kept-red ones. Anything that is not a run directory is left alone, including the pointer
# file and a directory somebody renamed while investigating.
gate_prune_logs() {
  local parent=$1 keep=$2 pattern
  [ -d "$parent" ] || return 0
  for pattern in 'plain' 'red'; do
    local dir
    ls -1dt "$parent"/[0-9]*T[0-9]*Z-[0-9]* 2> /dev/null | while IFS= read -r dir; do
      dir=${dir%/}
      case "$dir" in
        *-red) [ "$pattern" = red ] || continue ;;
        *) [ "$pattern" = plain ] || continue ;;
      esac
      printf '%s\n' "$dir"
    done | tail -n "+$((keep + 1))" | while IFS= read -r dir; do rm -rf "$dir"; done
  done
}

# gate_remove_legacy_flat_logs <parent> - deletes the six log files the gate wrote directly into
# <parent> before each run had its own directory. They are the last run of that older gate, days
# old, and a reader who opens the parent meets them first. Only those six names are touched.
gate_remove_legacy_flat_logs() {
  local parent=$1 name
  [ -d "$parent" ] || return 0
  for name in merge.log npm-install.log npm-test.log typecheck.log format.log dotnet-test.log; do
    rm -f "$parent/$name"
  done
}

# gate_undeleted_folders <dir> - summarises what one run's test processes reported as test folders
# they could not delete. TestFolder (src/TestSupport) writes one <pid>.log per reporting process into
# the directory named by ADP_UNDELETED_FOLDERS_DIR, one line per folder, and creates nothing when it
# has nothing to report. Prints "none", or "<n> file(s), <m> line(s)".
#
# It never judges. The verdict is the four gates; a report is a finding for whoever reads the run,
# and absence is the expected answer. On a green run it says none: TestFolder's own guards report
# into their own scratch files, so a summary of "1 file, 2 lines" on every run means that exclusion
# has regressed. Lines are counted, never parsed - the format is written to be read - and a last
# line without a newline still counts.
gate_undeleted_folders() {
  local dir=${1:-} files=0 lines=0 f n
  if [ -n "$dir" ] && [ -d "$dir" ]; then
    for f in "$dir"/*.log; do
      [ -f "$f" ] || continue
      n=$(awk 'END { print NR }' "$f")
      files=$((files + 1))
      lines=$((lines + n))
    done
  fi
  if [ "$files" = 0 ]; then
    printf 'none\n'
    return 0
  fi
  printf '%s file%s, %s line%s\n' "$files" "$([ "$files" = 1 ] || echo s)" "$lines" "$([ "$lines" = 1 ] || echo s)"
}

# gate_verdict <dotnet-log> - judges the four gates. Reads NPM_INSTALL_EXIT, NPM_TEST_EXIT,
# TC_EXIT, FMT_EXIT and DT_EXIT; sets VERDICT (green|refused), WHY, DT_TOTAL and DT_FAILED.
#
# It STARTS REFUSED and green is assigned in exactly one place, behind every positive condition,
# because a verdict's dangerous input is missing, not wrong: a wrong value is compared and fails,
# a missing one is often never compared at all. So every status is compared as a string (`[ "" -ne
# 0 ]` is an error, and an error is false inside `if`), and the log must show POSITIVE evidence
# that tests ran - a nonzero `total:` and `failed: 0` - rather than merely lack "Zero tests ran".
# That one form refuses a deleted log, an empty one, one truncated before its summary, `total: 0`
# and `failed: 3` behind an exit code of 0.
#
# dotnet writes CRLF. Git Bash's sed happens to strip CR itself (measured), but `sed -b`, bash's
# `read` and a Linux sed do not - so CR is stripped here and no reader's habits are relied on.
gate_verdict() {
  local log=${1:-}
  VERDICT=refused
  WHY=""
  DT_TOTAL=""
  DT_FAILED=""
  [ "${NPM_INSTALL_EXIT:-}" = 0 ] || WHY="$WHY npm-install=${NPM_INSTALL_EXIT:-missing}"
  [ "${NPM_TEST_EXIT:-}" = 0 ]    || WHY="$WHY npm-test=${NPM_TEST_EXIT:-missing}"
  [ "${TC_EXIT:-}" = 0 ]          || WHY="$WHY typecheck=${TC_EXIT:-missing}"
  [ "${FMT_EXIT:-}" = 0 ]         || WHY="$WHY format=${FMT_EXIT:-missing}"
  [ "${DT_EXIT:-}" = 0 ]          || WHY="$WHY dotnet-test=${DT_EXIT:-missing}"
  if [ -n "$log" ] && [ -s "$log" ]; then
    DT_TOTAL=$(tr -d '\r' < "$log" | sed -n 's/^ *total: *\([0-9][0-9]*\) *$/\1/p' | tail -1)
    DT_FAILED=$(tr -d '\r' < "$log" | sed -n 's/^ *failed: *\([0-9][0-9]*\) *$/\1/p' | tail -1)
    case "$DT_TOTAL" in *[1-9]*) ;; *) WHY="$WHY dotnet-ran-no-tests(total='$DT_TOTAL')" ;; esac
    case "$DT_FAILED" in '' | *[!0]*) WHY="$WHY dotnet-failed='$DT_FAILED'" ;; esac
    if tr -d '\r' < "$log" | grep -q "Zero tests ran"; then WHY="$WHY dotnet-zero-tests-ran"; fi
  else
    WHY="$WHY dotnet-log-missing-or-empty"
  fi
  if [ -z "$WHY" ]; then VERDICT=green; fi
}

# gate_tell_write <lock-dir> <scratch> <branch> <base-or-pending> - writes the run's own line into
# the lock it already holds, so any session can read WHICH TREE is gating WHICH BRANCH ON WHICH BASE
# without asking anybody. A session about to move develop needs that and nothing else.
#
# BEST EFFORT, ALWAYS. It returns 0 whether or not it wrote, and the caller reports rather than
# aborts. A diagnostic that can red a gate has inverted its own value - and the write is a rename
# over a destination a reader may hold open, which is the exact operation measured at 0x80070497
# 185 times in 400 this week.
#
# THE LINE CARRIES ITS OWN EXPIRY, and that is the whole design. A killed gate cleans up nothing, so
# no tell may depend on the writer living to retract it: the reader is told when to stop believing
# the line rather than having to know how long a run plausibly takes. A pid would not do - the one
# recorded here is a Git bash pid no Windows reader can resolve, and the process that matters may be
# an orphaned grandchild the writer never knew about (measured, 2026-09-23).
#
# ONE MACHINE, ONE CLOCK, UTC. The lease arithmetic rests on that and it is written rather than
# assumed: the first thing to break if anything ever gates from another machine.
#
# THE FAILURE DIRECTION IS THE SAFE ONE. A shell whose `date` lacks `-d` cannot compute the expiry,
# and the field then reads `unknown` rather than a timestamp - because the first draft fell back to
# NOW, which birthed every line already expired and so told every reader to disregard a live gate.
# That fallback is reached only in the environment where the primary fails, so it was not an unlikely
# path but the only one that would ever run there. A line whose expiry could not be computed must
# read as LIVE: over-holding is recoverable and expires by itself, an under-hold is neither.
#
# IT WARNS, IT NEVER AUTHORISES. An expired line says a reader may ASK about a stale lock. It never
# licenses removing one, and it says nothing about whose turn the board is - that is a decision
# somebody makes, not a fact a file can hold.
gate_tell_write() {
  local lock=${1:-} scratch=${2:-} branch=${3:-} base=${4:-pending} tmp
  [ -n "$lock" ] && [ -d "$lock" ] || return 0
  tmp="$lock/owner.$$"
  {
    printf '%s gating %s on base %s started %s ignore-after %s\n' \
      "$scratch" "$branch" "$base" \
      "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
      "$(date -u -d '+2 hours' +%Y-%m-%dT%H:%M:%SZ 2> /dev/null || echo unknown)"
    printf 'read it with: bash .github/tools/gate/who-is-gating.sh - it warns, it never authorises;\n'
    printf 'the board decides whose turn it is. Ignore this line after the time above.\n'
  } > "$tmp" 2> /dev/null || { rm -f "$tmp" 2> /dev/null; return 0; }
  mv -f "$tmp" "$lock/owner" 2> /dev/null || rm -f "$tmp" 2> /dev/null
  return 0
}

# gate_who_is_gating <worktrees-dir> - the reader. FOUR outputs, each a different string, because
# the first draft of this used `cat <glob> 2>/dev/null` and that prints the identical nothing for
# "nobody is gating", "the directory never existed", "the writer moved", "the path was mistyped" and
# "the glob did not expand" - four of which mean no information while all five read as clear to
# proceed. That is the gate-verdict shape: make the reassuring answer one that must be REACHED.
#
#   GATING=none                        nobody holds a lock
#   <line>                             a holder, with its branch and base
#   <scratch> gating (owner not written yet)   a holder whose details are not on disk yet or failed
#   TELL_UNREADABLE=<dir>, exit 2      the path this reader expects does not exist
#
# IT GLOBS THE LOCK DIRECTORY, NOT THE OWNER FILE, and that is not a detail. The lock is created
# before its line is written, and the write is deliberately best effort - so a reader globbing
# `owner` would print GATING=none while a gate was genuinely running, which is the under-hold whose
# cost is the unrecoverable one. Existence of the directory answers "is this tree busy", which is
# the only thing the lock has ever truly said; content answers "on what".
# gate_who_is_gating <worktrees-dir> [--require-free]
#
# Default: prints the board and returns 0 for a free board AND for a holder alike, because its
# main caller is printing. That default is fail-open in a chain - `tell && commit` runs the commit
# whatever the board says - which is why the flag exists rather than a change to the default.
#
# --require-free: returns 0 ONLY for a positive GATING=none. A holder, an EXPIRED line, an
# unknown expiry and a lock whose owner is not written yet are all 1; an unreadable board stays 2.
# The state that must be REACHED is what gates, rather than the state that must be avoided being
# what aborts - so a board this reader cannot parse fails instead of passing.
# gate_selftest_report <file>
#
# Prints the self-test's own verdict for the gate's summary, or `absent` - never nothing. The test
# runs inside `dotnet test` and PASSES, so xUnit emits none of its output and the gate log has never
# carried the verdict: the mode a gate ran in was invisible exactly when everything worked.
#
# Absence is reported rather than skipped because a missing report means EITHER the test did not run
# OR the write failed, and both are things a reader must be told. A summary that stayed quiet about it
# would be the same defect this line exists to remove.
gate_selftest_report() {
  local file=${1:-}
  if [ -n "$file" ] && [ -s "$file" ]; then
    sed -n '1p' "$file" | tr -d '\r'
  else
    printf 'absent (the test did not run, or could not write its report)'
  fi
}

gate_who_is_gating() {
  local dir=${1:-} mode=${2:-} lock owner found=0 scratch line ignore now
  if [ -z "$dir" ] || [ ! -d "$dir" ]; then
    printf 'TELL_UNREADABLE=%s\n' "${dir:-<no directory named>}"
    return 2
  fi
  for lock in "$dir"/*/adp-gate.lock; do
    [ -d "$lock" ] || continue
    found=1
    owner="$lock/owner"
    if [ -s "$owner" ]; then
      line=$(sed -n '1p' "$owner" | tr -d '\r')
      ignore=${line##*ignore-after }
      ignore=${ignore%% *}
      now=$(date -u +%Y-%m-%dT%H:%M:%SZ)
      # The script does the arithmetic so a human in a hurry does not. Both labels say ASK, never
      # clear: this warns and never authorises, and a stale lock is a question rather than a licence.
      if [ "$ignore" = unknown ]; then
        printf '%s (expiry unknown - treat as live)\n' "$line"
      elif [ -n "$ignore" ] && [ "$ignore" \< "$now" ]; then
        printf '%s (EXPIRED - ask, do not assume)\n' "$line"
      else
        printf '%s\n' "$line"
      fi
    else
      scratch=${lock%/adp-gate.lock}
      scratch=${scratch##*/}
      printf '%s gating (owner not written yet)\n' "$scratch"
    fi
  done
  if [ "$found" = 0 ]; then
    printf 'GATING=none\n'
    return 0
  fi
  # A holder of any kind reaches here, including an EXPIRED line and one whose owner is not on disk
  # yet. Both already print ASK rather than clear, so both are 'not free': a stale lock is a question
  # to ask and never a licence, and --require-free must not be the thing that converts it into one.
  if [ "$mode" = --require-free ]; then
    return 1
  fi
  return 0
}

# gate_who_owns <main-checkout> <spec-name> - is anybody on this specification?
#
# THREE STATES, NEVER TWO, and each one is a different string:
#
#   OWNER=<identity> EVIDENCE=<worktree|unlanded-branch|recent-authorship>   (return 0)
#   OWNERSHIP=unowned REASON=every-task-marked                               (return 0)
#   OWNERSHIP_UNREADABLE=<what could not be read>                            (return 2)
#
# WHY UNREADABLE IS A STATE RATHER THAN A FAILURE. A Developer was placed on client-centralization
# while its owner was offline; the owner came back with complete, gated work and the carry branch had
# to be deleted rather than abandoned. Nothing readable at the time said that specification had an
# owner - the session list showed an idle roster and the tasks document showed unmarked boxes, and
# NEITHER IS EVIDENCE OF ABSENCE. An instrument that answered `unowned` there would have been
# confidently wrong in exactly the way that caused the loss. So `unowned` is the state that must be
# positively REACHED: the verdict starts unreadable and only a search that ran and found nothing can
# change it. A missing input can therefore never read as a free specification.
#
# WHAT IT READS, strongest first. All of it is on disk, and none of it is a session's self-report:
#
#   worktree           a live worktree whose branch carries unlanded commits touching this
#                      specification's folder, or whose branch NAME carries the specification's
#                      name. Its identity comes from that worktree's own `user.name`, set when the
#                      tree was created - a fact a chat title cannot change.
#   unlanded-branch    the same two links, on a branch with no worktree: work set aside, not absent.
#   recent-authorship  who last committed this specification's own files on develop, and when. This
#                      is the signal that caught the client-centralization error after the fact, and
#                      the only one that survives its owner going offline.
#
# WHAT IT REFUSES TO READ. A session title, because a session renames itself Idle and keeps a
# worktree. And an UNMARKED task, because an unmarked task is not an unstarted one. The tasks
# document is read in ONE direction only: every box marked can END ownership, since a specification's
# owner holds it until every task is done. No box count is ever read as evidence that nobody started.
#
# WHY A MISSING TASKS DOCUMENT IS UNREADABLE RATHER THAN UNOWNED. Without it the completion question
# has no answer, and `I cannot tell` is the honest one. multi-select is exactly this shape - design
# and requirements, no tasks document, last touched three weeks ago - and it is in fact parked by a
# decision that no file in the tree records. An instrument calling it free would be repeating the
# original error on the one specification where the evidence is thinnest.
#
# WHAT IT CANNOT DO, so a clear read is never taken for more than it is:
#   - OWNED MEANS CLAIMED, NEVER ACTIVE. A specification with an open task and a claimant reads as
#     owned however long its owner has been gone, and releasing one is a human decision this reader
#     does not make. That is the exact mirror of the failure it was built for: an offline owner was
#     treated as an absent one, and refusing that inference necessarily also refuses the true case
#     where an owner really has left. Ask the board, not this.
#   - It cannot see a session that is thinking about a specification and has touched nothing. The
#     earliest moment ownership becomes visible is the first worktree or the first commit.
#   - A branch-NAME link is a strong hint and not a proof: a branch may be named for a specification
#     it only touches, and a branch may work one without naming it. Which link fired is printed for
#     that reason, so a reader can weigh it rather than take a verdict on trust.
#   - It reports; it never authorises. Placement is the board's decision.
gate_who_owns() {
  local main=${1:-} spec=${2:-}
  local specdir tasks last line path branch short identity dirty authors link ahead counts logdir logs lastlog logauthor
  local verdict=unreadable reason='' owner='' evidence=''
  local open_tasks=0 marked_tasks=0 searched_trees=0 searched_history=0 contested=0 owner_time=0 committed

  if [ -z "$main" ] || [ ! -d "$main" ]; then
    printf 'OWNERSHIP_UNREADABLE=%s\n' "${main:-<no main checkout named>}"
    return 2
  fi
  case "$spec" in
    '')
      printf 'OWNERSHIP_UNREADABLE=<no specification named>\n'
      return 2
      ;;
    */* | .* | *' '*)
      printf 'OWNERSHIP_UNREADABLE=<refusing a specification name that is a path: %s>\n' "$spec"
      return 2
      ;;
  esac
  specdir="$main/.spec-workflow/specs/$spec"
  if [ ! -d "$specdir" ]; then
    printf 'OWNERSHIP_UNREADABLE=<no specification folder at .spec-workflow/specs/%s>\n' "$spec"
    return 2
  fi
  # Everything below is expressed as `unlanded relative to develop`, so a repository without it can
  # answer nothing rather than answering wrongly.
  if ! git -C "$main" rev-parse --verify --quiet refs/heads/develop > /dev/null 2>&1; then
    printf 'OWNERSHIP_UNREADABLE=<no develop branch, so nothing can be called unlanded>\n'
    return 2
  fi

  printf 'SPEC=%s\n' "$spec"

  # THE TWO BRANCH POPULATIONS ARE COMPUTED ONCE, WHICH IS A CORRECTNESS MATTER AS MUCH AS A SPEED
  # ONE. The first draft asked `does this branch touch the specification` of every branch in turn.
  # This repository has 298 branches, the question costs about 0.7 s each, and --all over eight
  # specifications came to roughly half an hour - so the instrument would simply not have been run,
  # which is the same outcome as not having it. Both lists are single commands and both are space
  # delimited, which is safe because a git branch name cannot contain a space.
  local unlanded pathbranches trees
  unlanded=$(git -C "$main" for-each-ref --format='%(refname:short)' --no-merged refs/heads/develop refs/heads 2> /dev/null) || {
    printf 'OWNERSHIP_UNREADABLE=<could not enumerate branches>\n'
    return 2
  }
  unlanded=" $(printf '%s' "$unlanded" | tr '\n' ' ') "
  pathbranches=" $(gate_who_owns_path_branches "$main" "$specdir" | tr '\n' ' ') "
  if ! trees=$(git -C "$main" worktree list --porcelain 2> /dev/null); then
    printf 'OWNERSHIP_UNREADABLE=<could not enumerate worktrees>\n'
    return 2
  fi
  searched_trees=1

  # The worktrees first: a worktree is the stronger evidence because it is somebody's working copy.
  # It carries an identity written when the tree was made, and it can be dirty.
  local seen_branches=' '
  path=''
  branch=''
  while IFS= read -r line; do
    case "$line" in
      'worktree '*)
        path=${line#worktree }
        branch=''
        ;;
      'branch '*) branch=${line#branch } ;;
      '')
        if [ -n "$path" ] && [ -n "$branch" ]; then
          short=${branch#refs/heads/}
          seen_branches="$seen_branches$short "
          # A worktree's own branch is name-matched whether or not it is still unlanded, because
          # here the WORKTREE is the evidence rather than the branch: a finished agent retires its
          # tree, so one left standing is either somebody working or somebody who did not clean up,
          # and a reader placing work needs to be told about both.
          link=''
          case "$pathbranches" in *" $short "*) link=spec-paths ;; esac
          if [ -z "$link" ]; then
            case "$short" in *"$spec"*) link=branch-name ;; esac
          fi
          if [ -n "$link" ]; then
            identity=$(git -C "$path" config user.name 2> /dev/null) || identity=''
            # --untracked-files=no on purpose: a worktree's untracked files are overwhelmingly build
            # output, and the full walk costs seconds per tree on Windows. A tracked modification is
            # the signal that somebody is mid-edit, and it is the one this keeps.
            dirty=$(git -C "$path" status --porcelain --untracked-files=no 2> /dev/null | wc -l | tr -d ' ')
            authors=$(gate_who_owns_authors "$main" "$short")
            case "$unlanded" in
              *" $short "*) ahead=unlanded ;;
              *) ahead=landed ;;
            esac
            # A WORKTREE IS ONLY DECISIVE WHEN IT SHOWS LIVE WORK - unlanded commits, or a tracked
            # modification. One that is landed and clean is a tree somebody forgot to retire, and
            # this repository keeps several: counting those as owners would have made the
            # instrument report an owner for every finished specification whose author had not
            # cleaned up, which is the noise that gets a check ignored rather than read. The line is
            # still PRINTED, because a reader placing work wants to know a tree is standing there;
            # COUNTS= says whether the verdict leaned on it.
            counts=no
            if [ "$ahead" = unlanded ] || [ "$dirty" != 0 ]; then counts=yes; fi
            printf 'WORKTREE=%s BRANCH=%s IDENTITY=%s DIRTY=%s BRANCH_STATE=%s AUTHORS=%s LINK=%s COUNTS=%s\n' \
              "$path" "$short" "${identity:-<unset>}" "$dirty" "$ahead" "${authors:-<none>}" "$link" "$counts"
            if [ "$counts" = yes ]; then
              # TWO LIVE TREES ON ONE SPECIFICATION IS A REAL STATE, not an error to hide. The first
              # draft named whichever worktree git listed first and got module-client-api-readme
              # wrong on its first run: it named a stale tree of mine over the Developer who owns
              # the specification. So the newest commit wins, and CONTESTED= says how many were in
              # the running - a reader who sees it should read the lines rather than the verdict.
              contested=$((contested + 1))
              committed=$(git -C "$main" log -n 1 --format=%ct "refs/heads/$short" 2> /dev/null)
              case "$committed" in *[!0-9]* | '') committed=0 ;; esac
              if [ -z "$owner" ] || [ "$committed" -gt "$owner_time" ]; then
                owner=${identity:-${authors%%,*}}
                evidence=worktree
                owner_time=$committed
              fi
            fi
          fi
        fi
        path=''
        branch=''
        ;;
    esac
  done << EOF
$trees

EOF

  # Then the branches nobody has a worktree for: work set aside, which is not the same as absent.
  # Only UNLANDED branches are name-matched here - a landed branch is landed, and this repository
  # keeps hundreds of them whose names would otherwise each claim a specification forever.
  local ref
  for ref in $pathbranches $unlanded; do
    [ -n "$ref" ] || continue
    [ "$ref" = develop ] && continue
    case "$seen_branches" in *" $ref "*) continue ;; esac
    seen_branches="$seen_branches$ref "
    link=''
    case "$pathbranches" in *" $ref "*) link=spec-paths ;; esac
    if [ -z "$link" ]; then
      case "$ref" in *"$spec"*) link=branch-name ;; esac
    fi
    [ -n "$link" ] || continue
    authors=$(gate_who_owns_authors "$main" "$ref")
    printf 'BRANCH=%s AUTHORS=%s LINK=%s\n' "$ref" "${authors:-<none>}" "$link"
    if [ -z "$owner" ]; then
      owner=${authors%%,*}
      evidence=unlanded-branch
    fi
  done

  # Who last committed this specification's own files, and when. Printed whatever the verdict, because
  # a reader placing work wants the history even when a worktree already answered.
  last=$(git -C "$main" log -n 1 --format='%an|%ar|%h' -- "$specdir" 2> /dev/null) || last=''
  searched_history=1
  if [ -n "$last" ]; then
    printf 'LAST_AUTHOR=%s WHEN=%s COMMIT=%s\n' "${last%%|*}" "$(printf '%s' "$last" | cut -d'|' -f2)" "${last##*|}"
  else
    printf 'LAST_AUTHOR=<none> WHEN=<never> COMMIT=<none>\n'
  fi

  # THE IMPLEMENTATION LOGS ARE WHAT SEPARATE WRITING A SPECIFICATION FROM IMPLEMENTING IT, and
  # without them this reader named the wrong person in the one window that matters most. Before any
  # Developer touches a specification, the last author of its folder is the ARCHITECT who wrote the
  # documents - so a reader going by authorship alone reports an owner for every freshly approved
  # specification and blocks the very placement it exists to inform. Measured on the real case: at
  # 04b3a933 the last author of client-centralization was its Architect and nothing had been
  # implemented; at 49320309, eight hours later, a Developer had marked a task and become its owner.
  # The answer has to flip exactly there, and a log or a marked box is what makes it flip.
  #
  # `No log AND no marked box` is a sound absence rather than a tuned one: a Developer who had begun
  # without writing a log yet would still have a worktree or an unlanded branch, and those are read
  # above. It is NOT the unmarked-task mistake in another coat - an unmarked box is never read as
  # evidence that nobody started, and a specification with marked boxes and no logs is treated as
  # implemented, which is what two-tab-connection-wedge actually looks like.
  logdir="$specdir/Implementation Logs"
  logs=0
  lastlog=''
  if [ -d "$logdir" ]; then
    logs=$(find "$logdir" -maxdepth 1 -name '*.md' 2> /dev/null | wc -l | tr -d ' ')
    lastlog=$(git -C "$main" log -n 1 --format='%an|%ar|%h' -- "$logdir" 2> /dev/null) || lastlog=''
  fi
  case "$logs" in *[!0-9]* | '') logs=-1 ;; esac
  if [ -n "$lastlog" ]; then logauthor=${lastlog%%|*}; else logauthor='<none>'; fi
  printf 'LOGS=%s LAST_LOG_AUTHOR=%s\n' "$logs" "$logauthor"

  tasks="$specdir/tasks.md"
  if [ -f "$tasks" ]; then
    open_tasks=$(grep -c '^- \[ \]' "$tasks") || open_tasks=0
    marked_tasks=$(grep -c '^- \[x\]' "$tasks") || marked_tasks=0
  fi
  # Compared as digits only after being proved to BE digits. An empty count reaching `-gt` is the
  # shape that made a gate verdict error and print ALL GREEN, so the guard is on the value's form
  # rather than on remembering to set it.
  case "$open_tasks" in *[!0-9]* | '') open_tasks=-1 ;; esac
  case "$marked_tasks" in *[!0-9]* | '') marked_tasks=-1 ;; esac
  printf 'TASKS=%s open, %s marked\n' "$open_tasks" "$marked_tasks"

  if [ -n "$owner" ]; then
    verdict=owned
  elif [ "$searched_trees" != 1 ] || [ "$searched_history" != 1 ]; then
    verdict=unreadable
    reason='a search did not complete'
  elif [ -z "$last" ]; then
    verdict=unreadable
    reason="no commit has ever touched .spec-workflow/specs/$spec, so there is no history to read"
  elif [ ! -f "$tasks" ]; then
    verdict=unreadable
    reason='no tasks document, so whether every task is done has no answer'
  elif [ "$open_tasks" -lt 0 ] || [ "$marked_tasks" -lt 0 ] || [ "$logs" -lt 0 ]; then
    verdict=unreadable
    reason='the task or log counts could not be read as numbers'
  elif [ "$logs" = 0 ] && [ "$marked_tasks" -eq 0 ]; then
    verdict=unowned
    reason=no-implementation-yet
  elif [ "$open_tasks" -gt 0 ]; then
    verdict=owned
    # The last LOG author rather than the last author of the folder: an Architect amending the tasks
    # document is the most recent committer often enough to matter, and naming it would hand the
    # specification to the wrong person while the Developer is mid-work.
    if [ -n "$lastlog" ]; then owner=${lastlog%%|*}; else owner=${last%%|*}; fi
    evidence=recent-authorship
  elif [ "$marked_tasks" -gt 0 ]; then
    verdict=unowned
    reason=every-task-marked
  else
    verdict=unreadable
    reason='a tasks document with no task lines at all'
  fi

  case "$verdict" in
    owned)
      if [ "$evidence" = worktree ] && [ "$contested" -gt 1 ]; then
        printf 'OWNER=%s EVIDENCE=%s CONTESTED=%s\n' "${owner:-<unnamed>}" "$evidence" "$contested"
      else
        printf 'OWNER=%s EVIDENCE=%s\n' "${owner:-<unnamed>}" "$evidence"
      fi
      return 0
      ;;
    unowned)
      printf 'OWNERSHIP=unowned REASON=%s\n' "$reason"
      return 0
      ;;
    *)
      printf 'OWNERSHIP_UNREADABLE=<%s>\n' "$reason"
      return 2
      ;;
  esac
}

# gate_who_owns_path_branches <main> <specdir> - every branch carrying an unlanded commit that
# touches this specification's folder. This is the PROOF link, as against the branch-name hint.
#
# One history walk finds the commits across every branch at once, and only then is each one asked
# which branches contain it - so the cost is one walk plus a lookup per hit, rather than a walk per
# branch. There are usually no hits at all.
gate_who_owns_path_branches() {
  local main=$1 specdir=$2 sha
  git -C "$main" log --format=%H --branches --not refs/heads/develop -- "$specdir" 2> /dev/null |
    while IFS= read -r sha; do
      [ -n "$sha" ] || continue
      git -C "$main" branch --contains "$sha" --format='%(refname:short)' 2> /dev/null
    done | awk 'NF && $0 != "develop" && !seen[$0]++'
}

# gate_who_owns_authors <main> <short-branch> - the per-task identities on a branch's unlanded
# commits, comma-separated, most recent first. These are what a commit records; a session's title is
# not consulted anywhere in this file.
gate_who_owns_authors() {
  local main=$1 branch=$2
  git -C "$main" log --format='%an' "refs/heads/develop..refs/heads/$branch" 2> /dev/null |
    awk '!seen[$0]++' | paste -sd, - 2> /dev/null
}

# gate_who_owns_all <main-checkout> - every live specification, one block each.
#
# ITS OWN LIVENESS CHECK IS THE POINT OF HAVING AN --all AT ALL. A walk that answers `unreadable` for
# every specification is indistinguishable, line by line, from a walk over a repository whose
# specifications are all genuinely ambiguous - and the first is a broken instrument while the second
# is a true report. So the walk counts what it could answer and refuses to end green on nothing: a
# zero from a blind tool looks exactly like a zero from a clean run, and the only defence is a health
# figure printed beside the answer.
gate_who_owns_all() {
  local main=${1:-} dir spec answered=0 total=0 rc
  if [ -z "$main" ] || [ ! -d "$main/.spec-workflow/specs" ]; then
    printf 'OWNERSHIP_UNREADABLE=<no .spec-workflow/specs under %s>\n' "${main:-<no main checkout named>}"
    return 2
  fi
  for dir in "$main"/.spec-workflow/specs/*; do
    [ -d "$dir" ] || continue
    spec=${dir##*/}
    total=$((total + 1))
    gate_who_owns "$main" "$spec"
    rc=$?
    [ "$rc" -eq 0 ] && answered=$((answered + 1))
    printf '\n'
  done
  if [ "$total" = 0 ]; then
    printf 'OWNERSHIP_UNREADABLE=<no specification folders to walk>\n'
    return 2
  fi
  printf 'WALKED=%s answered=%s unreadable=%s\n' "$total" "$answered" "$((total - answered))"
  if [ "$answered" = 0 ]; then
    printf 'OWNERSHIP_UNREADABLE=<%s specifications walked and not one could be answered; treat this reader as blind rather than the board as free>\n' "$total"
    return 2
  fi
  return 0
}
