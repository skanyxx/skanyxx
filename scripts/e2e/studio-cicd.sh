#!/usr/bin/env bash
# Studio CI/CD end to end (owner decision 2026-10-28): template → agent repo → pull request → CI checks → supervisor
# merge → the reconciler → kagent, reported by Argo CD (D546; --argo off: by kagent) → "Deployed" on /Studio; then the
# agent is removed again.
#
#   scripts/e2e/studio-cicd.sh [--cluster skx-day0 | --context <ctx>] [--ci runner|emulate|off] [--argo on|off]
#
# Needs day0 and day1 on the same target. Agent: $AGENT (e2e-test-cicd), no skill (SKILL_REF= to add one). Results:
# $STATE/studio-cicd-result.json and the cleanup's day2-clean-result.json.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
export AGENT=${AGENT:-e2e-test-cicd} STAGES=studio STEP_NAME=studio-cicd SKILL_REF=${SKILL_REF-}
rc=0
"$here/../day2/day2.sh" run "$@" || rc=$?
AGENTS=$AGENT "$here/../day2/day2.sh" clean "$@"
exit $rc
