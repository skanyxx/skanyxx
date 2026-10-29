#!/usr/bin/env bash
# Day 1 (requirements §5): the admin connects the integrations, through Skanyxx's own APIs where it has them.
#
#   scripts/day1/day1.sh up     [--cluster skx-day0 | --context <ctx>] [--ci runner|emulate|off] [--argo on|off]
#   scripts/day1/day1.sh verify every integration healthy in Skanyxx (also the last step of `up`)
#   scripts/day1/day1.sh down   removes the Gitea and runner containers and Argo CD (the cluster stays: day0 down)
#
# up, idempotent, in order:
#   1. git: a throwaway Gitea container ($GITEA_NAME on 127.0.0.1:$GITEA_PORT) with the one account Skanyxx uses
#      (skanyxx-bot, not an admin; token write:organization+write:repository) and a read-only token for Argo CD.
#   2. Argo CD $ARGO_VERSION in namespace argocd (--argo off skips it): http API, a read-only account `skanyxx`, a
#      kagent.dev/Agent health check, the repo Secret and the Application from docs/templates/agents-repo.
#   3. Skanyxx: one Secret (git, CI and Argo tokens, from files, never argv) and `helm upgrade` with
#      $STATE/day1-values.yaml (Studio settings, egress to Gitea/Argo/Ollama); the reconciler then creates the repo.
#   4. CI: the owner's "Add CI to the agent repo" (POST /api/studio/ci/install), the repo's SKANYXX_URL variable and
#      SKANYXX_CI_TOKEN secret, and (--ci runner, default) a Gitea act_runner container that runs it. --ci emulate:
#      no runner; Day 2 posts the workflow's own check through the same API and git status. --ci off: no CI.
#   5. models through /api/models: the default model (Ollama $OLLAMA_MODEL on this machine) and e2e-test-openai whose
#      key comes from $MODEL_KEY_FILE (default: a generated dummy key in $STATE, mode 0600) — never echoed.
# Env: GITEA_NAME (ws2-ops-gitea), GITEA_PORT (3430), GITEA_IMAGE (gitea/gitea:1.24), RUNNER_NAME (ws2-ops-runner),
#   RUNNER_IMAGE (gitea/act_runner:0.2.13), JOB_IMAGE (node:20-bookworm), ARGO_VERSION (v3.5.6), OLLAMA_MODEL
#   (qwen3-coder:30b), OLLAMA_PORT (11434), MODEL_KEY_FILE.
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
[[ $CI =~ ^(runner|emulate|off)$ ]] || die "--ci runner|emulate|off"
[[ $ARGO =~ ^(on|off)$ ]] || die "--argo on|off"
target_init
GITEA_NAME=${GITEA_NAME:-ws2-ops-gitea}
GITEA_PORT=${GITEA_PORT:-3430}
GITEA_IMAGE=${GITEA_IMAGE:-gitea/gitea:1.24}
RUNNER_NAME=${RUNNER_NAME:-ws2-ops-runner}
RUNNER_IMAGE=${RUNNER_IMAGE:-gitea/act_runner:0.2.13}
JOB_IMAGE=${JOB_IMAGE:-node:20-bookworm}
ARGO_VERSION=${ARGO_VERSION:-v3.5.6}
OLLAMA_MODEL=${OLLAMA_MODEL:-qwen3-coder:30b}
OLLAMA_PORT=${OLLAMA_PORT:-11434}
ARGO_PF_PORT=${ARGO_PF_PORT:-18431}
GITEA_LOCAL=http://127.0.0.1:$GITEA_PORT
for n in "$GITEA_NAME" "$RUNNER_NAME"; do
  case $n in skanyxx-gitea-1 | skanyxx-postgres-1 | skx-integration-pg | kind-registry) die "$n is not ours" ;; esac
done

# How pods and containers reach this machine: host.docker.internal, by address (pods do not all resolve the name).
host_ip() {
  local ip
  ip=$(docker run --rm alpine:latest getent ahostsv4 host.docker.internal 2>/dev/null | awk 'NR==1{print $1}')
  [[ $ip =~ ^[0-9.]+$ ]] || die "host.docker.internal does not resolve in containers (Docker Desktop or OrbStack needed)"
  echo "$ip"
}

gitea_exec() { docker exec -i -u git "$GITEA_NAME" gitea "$@"; }
# Basic auth as the bot, the password from $STATE/gitea-bot-password through a curl config on stdin (never argv).
as_bot() { printf 'user = "skanyxx-bot:%s"\n' "$(cat "$STATE/gitea-bot-password")" | curl -fsS -m 20 -K - -H 'Content-Type: application/json' "$@"; }

# A container by that name must be one this script made (label skanyxx-e2e=day1).
ours() { [[ $(docker inspect "$1" --format '{{index .Config.Labels "skanyxx-e2e"}}' 2>/dev/null) == day1 ]]; }

git_server() {
  if docker inspect "$GITEA_NAME" >/dev/null 2>&1; then
    ours "$GITEA_NAME" || die "a container $GITEA_NAME exists that this script did not create"
  else
    log "Gitea $GITEA_IMAGE as $GITEA_NAME on 127.0.0.1:$GITEA_PORT (Actions on)"
    # ROOT_URL is what CI jobs (containers) clone from, so it is the name containers use for this machine.
    docker run -d --name "$GITEA_NAME" --label skanyxx-e2e=day1 -p "127.0.0.1:$GITEA_PORT:3000" \
      -e GITEA__security__INSTALL_LOCK=true -e GITEA__database__DB_TYPE=sqlite3 \
      -e "GITEA__server__ROOT_URL=http://host.docker.internal:$GITEA_PORT/" -e GITEA__server__HTTP_PORT=3000 \
      -e GITEA__server__DISABLE_SSH=true -e GITEA__service__DISABLE_REGISTRATION=true \
      -e GITEA__service__REQUIRE_SIGNIN_VIEW=true -e GITEA__actions__ENABLED=true \
      "$GITEA_IMAGE" >/dev/null
  fi
  docker start "$GITEA_NAME" >/dev/null
  local _
  for _ in $(seq 1 60); do curl -fsS -m 2 "$GITEA_LOCAL/api/healthz" >/dev/null 2>&1 && break; sleep 2; done
  curl -fsS -m 2 "$GITEA_LOCAL/api/healthz" >/dev/null || die "Gitea did not become healthy"
  if ! gitea_exec admin user list 2>/dev/null | awk 'NR > 1 {print $2}' | grep -qx skanyxx-bot; then
    log "Gitea account skanyxx-bot (not an admin)"
    gitea_exec admin user create --username skanyxx-bot --email skanyxx-bot@e2e.test --must-change-password=false \
      --random-password >/dev/null
  fi
  if [[ ! -s $STATE/gitea-bot-password ]]; then
    random_secret 32 | secret_write "$STATE/gitea-bot-password"
  fi
  # The password reaches the container on stdin only.
  docker exec -i -u git "$GITEA_NAME" sh -c 'read -r p; gitea admin user change-password --username skanyxx-bot --password "$p" --must-change-password=false' \
    <"$STATE/gitea-bot-password" >/dev/null
  if [[ ! -s $STATE/gitea-bot-token ]] || ! curl -fsS -m 5 -K <(printf 'header = "Authorization: token %s"\n' "$(cat "$STATE/gitea-bot-token")") "$GITEA_LOCAL/api/v1/repos/search?limit=1" >/dev/null 2>&1; then
    log "token for Skanyxx (write:organization, write:repository) → $STATE/gitea-bot-token"
    as_bot -X POST "$GITEA_LOCAL/api/v1/users/skanyxx-bot/tokens" \
      -d "{\"name\":\"skanyxx-$(date +%s)\",\"scopes\":[\"write:organization\",\"write:repository\"]}" | jq -jer .sha1 | secret_write "$STATE/gitea-bot-token"
    log "read-only token for Argo CD (read:repository) → $STATE/gitea-read-token"
    as_bot -X POST "$GITEA_LOCAL/api/v1/users/skanyxx-bot/tokens" \
      -d "{\"name\":\"argocd-$(date +%s)\",\"scopes\":[\"read:repository\"]}" | jq -jer .sha1 | secret_write "$STATE/gitea-read-token"
  fi
  [[ -s $STATE/gitea-bot-token && -s $STATE/gitea-read-token ]] || die "Gitea issued no token"
}

argo() {
  [[ $ARGO == on ]] || { log "Argo CD skipped (--argo off): deploy status comes from the reconciler and kagent"; return 0; }
  log "Argo CD $ARGO_VERSION → namespace argocd"
  if "${KUBECTL[@]}" get namespace argocd >/dev/null 2>&1; then
    owned argocd scripts/day1 || die "$CONTEXT already has an argocd namespace this script did not create: not touching it"
  else
    "${KUBECTL[@]}" create namespace argocd >/dev/null
    own argocd scripts/day1
  fi
  "${KUBECTL[@]}" -n argocd apply --server-side --force-conflicts \
    -f "https://raw.githubusercontent.com/argoproj/argo-cd/$ARGO_VERSION/manifests/install.yaml" >/dev/null
  # Plain http inside the cluster (Skanyxx's client takes no self-signed certificate); poll git every 30 s.
  "${KUBECTL[@]}" -n argocd patch configmap argocd-cmd-params-cm --type merge -p '{"data":{"server.insecure":"true"}}' >/dev/null
  local health
  health=$(jq -Rs . <"$ROOT/docs/templates/agents-repo/argocd-agent-health.lua")
  "${KUBECTL[@]}" -n argocd patch configmap argocd-cm --type merge -p "{\"data\":{\"accounts.skanyxx\":\"apiKey\",\"timeout.reconciliation\":\"30s\",\"resource.customizations.health.kagent.dev_Agent\":$health}}" >/dev/null
  "${KUBECTL[@]}" -n argocd patch configmap argocd-rbac-cm --type merge \
    -p '{"data":{"policy.csv":"p, skanyxx, applications, get, skanyxx-agents/skanyxx-agents, allow\n","policy.default":""}}' >/dev/null
  "${KUBECTL[@]}" -n argocd rollout restart deploy/argocd-server statefulset/argocd-application-controller deploy/argocd-repo-server >/dev/null
  "${KUBECTL[@]}" -n argocd rollout status deploy/argocd-server --timeout=300s
  "${KUBECTL[@]}" -n argocd rollout status statefulset/argocd-application-controller --timeout=300s
  "${KUBECTL[@]}" -n argocd rollout status deploy/argocd-repo-server --timeout=300s

  local repo=http://$HOST_IP:$GITEA_PORT/skanyxx/skanyxx-agents.git
  log "Argo repo credentials (read-only token) and Application skanyxx-agents → $repo"
  # The token goes from its file into the Secret (server-side apply: no last-applied copy of it).
  "${KUBECTL[@]}" -n argocd create secret generic skanyxx-agents-repo --from-literal=type=git --from-literal=url="$repo" \
    --from-literal=username=skanyxx-bot --from-file=password="$STATE/gitea-read-token" --dry-run=client -o yaml \
    | "${KUBECTL[@]}" label --local -f - argocd.argoproj.io/secret-type=repository -o yaml \
    | "${KUBECTL[@]}" apply --server-side --force-conflicts --field-manager=skanyxx-day1 -f - >/dev/null
  sed -e "s#REPO_URL#$repo#" -e "s#NAMESPACE#$NAMESPACE#" "$ROOT/docs/templates/agents-repo/argocd-application.yaml" \
    | "${KUBECTL[@]}" apply -f - >/dev/null
  # Skanyxx's read-only Argo token, minted with the initial admin password; both stay in memory and the Secret.
  # A forward from an earlier run points at the argocd-server pod the restart above replaced.
  port_forward_stop argocd
  port_forward argocd "$ARGO_PF_PORT" argocd svc/argocd-server 80
  ARGO_PF_PORT=$ARGO_PF_PORT run_node "$ROOT/scripts/day1/argo-token.mjs"
}

skanyxx() {
  log "Skanyxx Studio settings ($STATE/day1-values.yaml) and Secret skanyxx-studio"
  [[ -s $STATE/ci-token ]] || random_secret 48 | secret_write "$STATE/ci-token"
  local from=(--from-file=Studio__Git__Token="$STATE/gitea-bot-token" --from-file=Studio__Ci__Token="$STATE/ci-token")
  [[ -s $STATE/argo-token ]] && from+=(--from-file=Studio__Argo__Token="$STATE/argo-token")
  "${KUBECTL[@]}" -n "$NAMESPACE" create secret generic skanyxx-studio "${from[@]}" --dry-run=client -o yaml \
    | "${KUBECTL[@]}" apply --server-side --force-conflicts --field-manager=skanyxx-day1 -f - >/dev/null
  local argo_env='' require=false
  [[ $ARGO == on ]] && argo_env="    - {name: Studio__Argo__BaseUrl, value: \"http://argocd-server.argocd.svc\"}"
  [[ $CI == runner || $CI == emulate ]] && require=true
  cat >"$STATE/day1-values.yaml" <<EOF
# Written by scripts/day1/day1.sh (no secrets: they are in the Secret skanyxx-studio).
skanyxx:
  extraEnvFrom:
    - secretRef: {name: skanyxx-studio}
  extraEnv:
    - {name: Studio__Git__BaseUrl, value: "http://$HOST_IP:$GITEA_PORT"}
    - {name: Studio__Namespace, value: "$NAMESPACE"}
    - {name: Studio__MemoryMcpUrl, value: "http://$RELEASE.$NAMESPACE.svc:8081/mcp/memory"}
    - {name: Studio__RequireChecks, value: "$require"}
    - {name: Studio__ReconcileSeconds, value: "20"}
    - {name: Studio__SkillRegistries__0, value: "ghcr.io/agentregistry-dev/skills/"}
$argo_env
networkPolicy:
  skanyxxEgress:
    enabled: true
    internetPorts: [443, 465, 587]
    extra:
      # The Gitea container on this machine.
      - to: [{ipBlock: {cidr: "$HOST_IP/32"}}]
        ports: [{port: $GITEA_PORT}]
      # Argo CD's API (status reads only, D363).
      - to: [{namespaceSelector: {matchLabels: {kubernetes.io/metadata.name: argocd}}, podSelector: {matchLabels: {app.kubernetes.io/name: argocd-server}}}]
        ports: [{port: 8080}]
  modelEgress:
    ports: [443]
    cidrs: ["0.0.0.0/0", "::/0"]
    extra:
      # Ollama on this machine.
      - to: [{ipBlock: {cidr: "$HOST_IP/32"}}]
        ports: [{port: $OLLAMA_PORT}]
EOF
  "${HELM[@]}" upgrade "$RELEASE" "$ROOT/deploy/helm/skanyxx" -n "$NAMESPACE" --reuse-values -f "$STATE/day1-values.yaml" --wait --timeout 10m >/dev/null
  # A changed Secret alone does not roll the pod (the chart's checksum covers its own Secrets).
  "${KUBECTL[@]}" -n "$NAMESPACE" rollout restart deploy/"$RELEASE" >/dev/null
  "${KUBECTL[@]}" -n "$NAMESPACE" rollout status deploy/"$RELEASE" --timeout=300s
}

ci_runner() {
  [[ $CI == runner ]] || return 0
  if docker inspect "$RUNNER_NAME" >/dev/null 2>&1; then
    ours "$RUNNER_NAME" || die "a container $RUNNER_NAME exists that this script did not create"
  else
    log "Gitea act_runner $RUNNER_IMAGE as $RUNNER_NAME (jobs in $JOB_IMAGE)"
    # The registration token reaches the runner as a 0600 file, never argv or env (docker inspect shows env).
    gitea_exec actions generate-runner-token 2>/dev/null | tail -1 | tr -d '\n' | secret_write "$STATE/runner-registration-token"
    [[ -s $STATE/runner-registration-token ]] || die "Gitea issued no runner registration token"
    # The job containers reach Gitea and Skanyxx's Ingress on this machine as host.docker.internal.
    cat >"$STATE/runner-config.yaml" <<EOF
runner:
  capacity: 1
  labels: ["ubuntu-latest:docker://$JOB_IMAGE"]
container:
  options: "--add-host=host.docker.internal:host-gateway"
  # Job containers get no Docker socket: a job is the agent repo's code.
  docker_host: "-"
  force_pull: false
EOF
    docker run -d --name "$RUNNER_NAME" --label skanyxx-e2e=day1 \
      -e "GITEA_INSTANCE_URL=http://host.docker.internal:$GITEA_PORT" -e GITEA_RUNNER_NAME=e2e-runner \
      -e CONFIG_FILE=/config.yaml -v "$STATE/runner-config.yaml:/config.yaml:ro" \
      -v "$STATE/runner-registration-token:/run/registration-token:ro" -e GITEA_RUNNER_REGISTRATION_TOKEN_FILE=/run/registration-token \
      -v /var/run/docker.sock:/var/run/docker.sock "$RUNNER_IMAGE" >/dev/null
  fi
  docker start "$RUNNER_NAME" >/dev/null
}

models_and_ci() {
  if [[ -z ${MODEL_KEY_FILE:-} ]]; then
    MODEL_KEY_FILE=$STATE/dummy-openai-key
    [[ -s $MODEL_KEY_FILE ]] || printf 'sk-e2e-dummy-%s' "$(random_secret 40)" | secret_write "$MODEL_KEY_FILE"
  fi
  [[ -r $MODEL_KEY_FILE ]] || die "MODEL_KEY_FILE $MODEL_KEY_FILE is not readable"
  HOST_IP=$HOST_IP OLLAMA_MODEL=$OLLAMA_MODEL OLLAMA_PORT=$OLLAMA_PORT MODEL_KEY_FILE=$MODEL_KEY_FILE CI=$CI ARGO=$ARGO \
    GITEA_LOCAL=$GITEA_LOCAL GITEA_TOKEN_FILE=$STATE/gitea-bot-token UI_PORT=$UI_PORT run_node "$ROOT/scripts/day1/day1.mjs"
}

down() {
  local n
  for n in "$RUNNER_NAME" "$GITEA_NAME"; do
    if ours "$n"; then
      log "removing container $n"
      docker rm -f "$n" >/dev/null
    fi
  done
  if owned argocd scripts/day1; then
    "${KUBECTL[@]}" -n argocd delete application skanyxx-agents --ignore-not-found >/dev/null || true
    "${KUBECTL[@]}" delete appproject -n argocd skanyxx-agents --ignore-not-found >/dev/null 2>&1 || true
    "${KUBECTL[@]}" delete namespace argocd --wait=false >/dev/null || true
  fi
  rm -f "$STATE"/gitea-* "$STATE"/argo-token "$STATE"/ci-token "$STATE"/runner-config.yaml "$STATE"/day1-*
}

case $cmd in
  up)
    HOST_IP=$(host_ip)
    git_server; argo; skanyxx; ci_runner; models_and_ci ;;
  verify) HOST_IP=$(host_ip); VERIFY_ONLY=1 models_and_ci ;;
  down) down ;;
  *) sed -n '2,24p' "$0" >&2; exit 2 ;;
esac
