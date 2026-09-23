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
      "$(date -u -d '+2 hours' +%Y-%m-%dT%H:%M:%SZ 2> /dev/null || date -u +%Y-%m-%dT%H:%M:%SZ)"
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
gate_who_is_gating() {
  local dir=${1:-} lock owner found=0 scratch
  if [ -z "$dir" ] || [ ! -d "$dir" ]; then
    printf 'TELL_UNREADABLE=%s\n' "${dir:-<no directory named>}"
    return 2
  fi
  for lock in "$dir"/*/adp-gate.lock; do
    [ -d "$lock" ] || continue
    found=1
    owner="$lock/owner"
    if [ -s "$owner" ]; then
      sed -n '1p' "$owner" | tr -d '\r'
    else
      scratch=${lock%/adp-gate.lock}
      scratch=${scratch##*/}
      printf '%s gating (owner not written yet)\n' "$scratch"
    fi
  done
  if [ "$found" = 0 ]; then
    printf 'GATING=none\n'
  fi
  return 0
}
