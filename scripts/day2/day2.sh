#!/usr/bin/env bash
# Day 2 (requirements §5): a user creates an agent, gives it instructions, a skill and a knowledge base, and talks to it.
#
#   scripts/day2/day2.sh run    [--cluster skx-day0 | --context <ctx>] [--ci runner|emulate|off] [--argo on|off]
#   scripts/day2/day2.sh clean  every e2e-test-* object goes (agents, conversations, cards → stale, models, people)
#   scripts/day2/day2.sh all    run + clean
#
# run: people (supervisor, builder, employee) → company cards (the knowledge base) → the builder's agent from the
# "support" template as a pull request (instructions, $SKILL_REF, a company search grant) → CI checks → supervisor
# merge → reconciler → kagent, reported by Argo CD → "Deployed" on /Studio → the employee chats (answers only the cards hold) and
# finds the chat in their history; RBAC refusals on the way; screenshots with Playwright when PLAYWRIGHT_MODULE is set.
# Needs day0 and day1 (same --cluster/--context, same --ci/--argo as day1).
# Env: AGENT (e2e-test-support), SKILL_REF (ghcr.io/agentregistry-dev/skills/brand-guidelines pinned by digest; empty =
#   none), GITEA_PORT (3430), SHOTS (screenshot folder, default $STATE/screenshots), PLAYWRIGHT_MODULE.
set -euo pipefail
. "$(dirname "$0")/../e2e/lib.sh"

cmd=${1:-}; shift || true
CI=runner ARGO=on
while [[ $# -gt 0 ]]; do
  case $1 in
    --cluster) CLUSTER=$2; shift 2 ;;
    --context) CONTEXT=$2; shift 2 ;;
    --ci) CI=$2; shift 2 ;;
    --argo) ARGO=$2; shift 2 ;;
    *) die "unknown flag $1" ;;
  esac
done
target_init
GITEA_PORT=${GITEA_PORT:-3430}
SKILL_REF=${SKILL_REF-ghcr.io/agentregistry-dev/skills/brand-guidelines:latest@sha256:74545eed7641f52bd94635746d83d0338fa0676b33ab7177d25f170a9c79978a}
export GITEA_LOCAL=http://127.0.0.1:$GITEA_PORT GITEA_TOKEN_FILE=$STATE/gitea-bot-token
[[ -s $GITEA_TOKEN_FILE ]] || die "no $GITEA_TOKEN_FILE: run scripts/day1/day1.sh up first"

run() {
  [[ $ARGO == on ]] && port_forward argocd "${ARGO_PF_PORT:-18431}" argocd svc/argocd-server 80
  ARGO_URL=http://127.0.0.1:${ARGO_PF_PORT:-18431} CI=$CI ARGO=$ARGO SKILL_REF=$SKILL_REF SHOTS=${SHOTS:-$STATE/screenshots} PLAYWRIGHT_MODULE=${PLAYWRIGHT_MODULE:-} \
    AGENT=${AGENT:-e2e-test-support} STAGES=${STAGES:-kb,studio,chat,ui} STEP_NAME=${STEP_NAME:-day2} \
    run_node "$ROOT/scripts/day2/day2.mjs"
}
clean() { AGENTS=${AGENTS:-} run_node "$ROOT/scripts/day2/clean.mjs"; }

case $cmd in
  run) run ;;
  clean) clean ;;
  all) rc=0; run || rc=$?; clean; exit $rc ;;
  *) sed -n '2,16p' "$0" >&2; exit 2 ;;
esac
