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

# gate_blocking_paths <changed-by-the-merge> <locally-changed> - the paths in BOTH newline-separated
# lists, in the first list's order. These are the only paths a fast-forward can refuse over.
#
# GIT REFUSES ON A COLLISION, NOT ON A DIRTY INDEX, which is the distinction the whole diagnosis
# rests on: a local change to a file the fast-forward does not touch lets it through, and a local
# change to one it does touch stops it with "Your local changes would be overwritten". Measured
# 2026-09-24 in a scratch repository, all three shapes - untouched path, touched path, staged
# deletion of a touched path - and in every case the local work was left intact.
#
# It is a function rather than two greps at the call site because this is the sentence a refused
# landing prints as its cause, and a confident wrong cause is worse than no cause at all: it is what
# sends the reader at the wrong remedy.
gate_blocking_paths() {
  local changed=${1:-} locally=${2:-} path
  [ -n "$changed" ] && [ -n "$locally" ] || return 0
  printf '%s\n' "$changed" | while IFS= read -r path; do
    [ -n "$path" ] || continue
    case "
$locally
" in
      *"
$path
"*) printf '%s\n' "$path" ;;
    esac
  done
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

# gate_awaiting_land_write <gitdir> <scratch> <base> <merged> - a GREEN gate's claim on develop, written
# before the gate's lock is released and cleared by land.sh. 1 when it could not be written.
#
# THE LOCK ENDS WHEN THE PROCESS DOES, AND DEVELOP'S PROMISE DOES NOT. gate.sh's finish() removes the
# lock on every exit, green included, so the moment a green gate's process ended the tell read
# GATING=none while develop was still promised to it until land.sh fast-forwarded. At 11:58:50Z on
# 2026-09-25 a spec commit landed ten seconds after Developer 3's green gate: the tell read free, the
# commit went ahead, and the gate lost its landing. The board's rule for that window was prose, which
# depends on vigilance at the moment nobody is looking; this makes it a state the tell can read.
#
# One line, in the scratch tree's own git directory beside the lock, so a temp clean-up cannot take it
# and each scratch tree holds at most one claim. The line ends in the FULL merged SHA, because that is
# what land.sh is handed and matches exactly. Its ignore-after is an hour from the verdict: a landing
# takes minutes, and a claim nobody landed within the hour is a slot that died with its session, whose
# base will have moved - see the expiry note in gate_who_is_gating for why it is dropped, not flagged.
gate_awaiting_land_write() {
  local gitdir=${1:-} scratch=${2:-} base=${3:-} merged=${4:-} tmp
  [ -n "$gitdir" ] && [ -d "$gitdir" ] && [ -n "$merged" ] || return 1
  tmp="$gitdir/adp-gate.awaiting-land.$$"
  printf '%s green, awaiting land on base %s since %s ignore-after %s merged %s\n' \
    "$scratch" "$(printf '%s' "$base" | cut -c1-8)" \
    "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
    "$(date -u -d '+1 hour' +%Y-%m-%dT%H:%M:%SZ 2> /dev/null || echo unknown)" \
    "$merged" > "$tmp" 2> /dev/null || {
    rm -f "$tmp" 2> /dev/null
    return 1
  }
  mv -f "$tmp" "$gitdir/adp-gate.awaiting-land" 2> /dev/null || {
    rm -f "$tmp" 2> /dev/null
    return 1
  }
}

# gate_awaiting_land_clear <worktrees-dir> <merged> - removes every claim for EXACTLY this merged commit
# and prints how many it removed. A claim for any other commit is left alone: it is somebody else's
# green gate, and releasing it would open the board under their landing.
gate_awaiting_land_clear() {
  local dir=${1:-} merged=${2:-} marker n=0
  if [ -z "$dir" ] || [ ! -d "$dir" ] || [ -z "$merged" ]; then
    printf '0\n'
    return 1
  fi
  for marker in "$dir"/*/adp-gate.awaiting-land; do
    [ -f "$marker" ] || continue
    case "$(sed -n '1p' "$marker" | tr -d '\r')" in
      *" merged $merged") rm -f "$marker" && n=$((n + 1)) ;;
    esac
  done
  printf '%s\n' "$n"
}

gate_who_is_gating() {
  local dir=${1:-} mode=${2:-} lock owner found=0 scratch line ignore now marker
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
  # A GREEN gate's claim on develop, from its verdict until land.sh lands it or refuses it for good (see
  # gate_awaiting_land_write). It holds the board exactly as a running gate does.
  #
  # UNLIKE A LOCK, A CLAIM PAST ITS IGNORE-AFTER IS DROPPED RATHER THAN LABELLED ASK, and the difference
  # is deliberate. An EXPIRED lock may be a gate that is still running, which nobody can see, so it stays
  # a question. An expired claim is a green verdict that nobody landed within the hour: its base will
  # have moved, and land.sh already refuses a moved base on its own. Holding the board for it would trade
  # a refusal that already exists for a board nobody can clear. An unknown expiry is held, as with a lock:
  # the direction that cannot be recovered is releasing a slot somebody still owns.
  for marker in "$dir"/*/adp-gate.awaiting-land; do
    [ -f "$marker" ] || continue
    line=$(sed -n '1p' "$marker" | tr -d '\r')
    [ -n "$line" ] || continue
    ignore=${line##*ignore-after }
    ignore=${ignore%% *}
    now=$(date -u +%Y-%m-%dT%H:%M:%SZ)
    if [ "$ignore" != unknown ] && [ -n "$ignore" ] && [ "$ignore" \< "$now" ]; then
      continue
    fi
    found=1
    if [ "$ignore" = unknown ]; then
      printf '%s (expiry unknown - treat as live)\n' "$line"
    else
      printf '%s\n' "$line"
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
