#!/usr/bin/env bash
# The whole customer scenario in one go (requirements §5): Day 0 → Day 1 → Day 2 → Studio CI/CD → cleanup, each day's
# output in $LOGDIR/day{0,1,2}.log, studio-cicd.log and clean.log; the first failing day stops the run (failures
# block release). --down also removes everything at the end (Day 1's containers and Argo, then the kind cluster).
#
#   LOGDIR=/tmp/skx-e2e scripts/e2e/run-all.sh [--cluster skx-day0 | --context <ctx>] [--ci runner|emulate|off] [--argo on|off] [--down]
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
LOGDIR=${LOGDIR:-$PWD/skx-e2e-logs}
mkdir -p "$LOGDIR"
target=() opts=() down=''
while [[ $# -gt 0 ]]; do
  case $1 in
    --cluster | --context) target+=("$1" "$2"); shift 2 ;;
    --ci | --argo) opts+=("$1" "$2"); shift 2 ;;
    --down) down=1; shift ;;
    *) echo "unknown flag $1" >&2; exit 2 ;;
  esac
done
step() {
  local name=$1; shift
  echo "==> $name (log: $LOGDIR/$name.log)"
  if "$@" >"$LOGDIR/$name.log" 2>&1; then
    grep -E '^[a-z0-9-]+: [0-9]+/[0-9]+ passed' "$LOGDIR/$name.log" || true
  else
    grep -E '^FAIL|passed$' "$LOGDIR/$name.log" || tail -5 "$LOGDIR/$name.log"
    echo "==> $name FAILED" >&2
    return 1
  fi
}
rc=0
step day0 "$here/../day0/day0.sh" all ${target[@]+"${target[@]}"} \
  && step day1 "$here/../day1/day1.sh" up ${target[@]+"${target[@]}"} ${opts[@]+"${opts[@]}"} \
  && step day2 "$here/../day2/day2.sh" run ${target[@]+"${target[@]}"} ${opts[@]+"${opts[@]}"} \
  && step studio-cicd "$here/studio-cicd.sh" ${target[@]+"${target[@]}"} ${opts[@]+"${opts[@]}"} || rc=1
step clean "$here/../day2/day2.sh" clean ${target[@]+"${target[@]}"} ${opts[@]+"${opts[@]}"} || rc=1
if [[ -n $down ]]; then
  "$here/../day1/day1.sh" down ${target[@]+"${target[@]}"} >"$LOGDIR/down.log" 2>&1 || rc=1
  "$here/../day0/day0.sh" down ${target[@]+"${target[@]}"} >>"$LOGDIR/down.log" 2>&1 || rc=1
fi
exit $rc
