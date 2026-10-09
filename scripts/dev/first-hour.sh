#!/usr/bin/env bash
# The local three-plane run of the first hour (docs/design/first-hour.md, todo.md slice 1):
#   knowledge = Postgres (docker compose) · runtime = kagent 0.10.2 on any Kubernetes · workspace = the Host (dotnet run).
# Every step is idempotent: re-running `up` changes nothing that is already in place.
#
#   scripts/dev/first-hour.sh up            Postgres, kagent (sample agents off) on $KUBE_CONTEXT, deploy/kagent/memory/,
#                                            controller port-forward on :8083
#   scripts/dev/first-hour.sh host          the Host on $HOST_BIND:$PORT (foreground; env config only)
#   OWNER_EMAIL=… OWNER_PASSWORD=… scripts/dev/first-hour.sh seed-secret
#                                            after /Setup: issue the seed's memory secret (actsForUsers: true) and put
#                                            it in the Secret its RemoteMCPServer reads; rerun to rotate
#   scripts/dev/first-hour.sh git           the bundled git (Gitea, compose service gitea on 127.0.0.1:3300) and the one
#                                            account Skanyxx uses; its token goes to $GIT_TOKEN_FILE (0600, outside the repo),
#                                            which `host` reads into Studio:Git:Token. Rerun to mint a new token.
#   scripts/dev/first-hour.sh status        what is running
#
# Settings (env):
#   KUBE_CONTEXT    kubectl/helm context (default: the current one). A kind-<name> context gets its kind cluster created
#                   when missing; any other context must already exist (OrbStack, k3d, Docker Desktop, a real cluster).
#   HOST_FROM_PODS  how pods reach this machine (host.docker.internal: Docker Desktop, kind, OrbStack; host.internal: OrbStack)
#   HOST_BIND       the Host's listen address (127.0.0.1). Docker Desktop and OrbStack route host.docker.internal to this
#                   machine's loopback, so pods reach a 127.0.0.1-bound Host (verified on Docker Desktop + kind). Plain Linux
#                   Docker does not (not verified here): bind the docker bridge address (e.g. 172.17.0.1), not 0.0.0.0 —
#                   a Development Host (Swagger, /Setup) must stay off the LAN. The URLs the browser and seed-secret use
#                   follow it (localhost for a loopback bind; IPv6 in brackets).
#   PORT (5287), DB (skanyxx; [a-z_][a-z0-9_]*), KAGENT_VERSION (0.10.2)
#   OLLAMA_MODEL (qwen3-coder:30b), OLLAMA_HOST (http://$HOST_FROM_PODS:11434, as pods see it),
#   OLLAMA_NUM_CTX (32768), OLLAMA_LOCAL (http://localhost:11434, as this machine sees it; only checked)
#   GIT_TOKEN_FILE  (${XDG_CONFIG_HOME:-~/.config}/skanyxx/gitea-token) the studio's git token; `host` enables the studio
#                   only when it exists
set -euo pipefail

KUBE_CONTEXT=${KUBE_CONTEXT:-$(kubectl config current-context)}
HOST_FROM_PODS=${HOST_FROM_PODS:-host.docker.internal}
HOST_BIND=${HOST_BIND:-127.0.0.1}
PORT=${PORT:-5287}
DB=${DB:-skanyxx}
KAGENT_VERSION=${KAGENT_VERSION:-0.10.2}
OLLAMA_MODEL=${OLLAMA_MODEL:-qwen3-coder:30b}
OLLAMA_HOST=${OLLAMA_HOST:-http://$HOST_FROM_PODS:11434}
OLLAMA_NUM_CTX=${OLLAMA_NUM_CTX:-32768}
OLLAMA_LOCAL=${OLLAMA_LOCAL:-http://localhost:11434}
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
# The same compose project as a plain `docker compose up` in the main checkout, so a worktree reuses its Postgres.
COMPOSE=(docker compose -p skanyxx -f "$ROOT/docker-compose.yml")
KUBECTL=(kubectl --context "$KUBE_CONTEXT")
HELM=(helm --kube-context "$KUBE_CONTEXT")
MCP_URL=http://$HOST_FROM_PODS:$PORT/mcp/memory
GIT_TOKEN_FILE=${GIT_TOKEN_FILE:-${XDG_CONFIG_HOME:-$HOME/.config}/skanyxx/gitea-token}
GIT_URL=http://localhost:3300

log() { printf '==> %s\n' "$*" >&2; }
die() { log "$*"; exit 1; }

# The context of the port-forward supervisor `up` starts, when pid $1 is one; empty otherwise. The match is the WHOLE
# command line `up` runs, so a pid that was reused, or a process that merely mentions it (a `ps | grep`), never counts
# and is never stopped (D176). -ww: a long command line is never cut.
supervisor_context() {
  local re='^([^ ]*/)?bash -c while true; do "\$@" >/dev/null 2>&1; sleep 2; done port-forward kubectl --context ([^ ]+) -n kagent port-forward svc/kagent-controller 8083:8083$' cmd
  cmd=$(ps -ww -o command= -p "$1" 2>/dev/null || true)
  if [[ $cmd =~ $re ]]; then printf '%s' "${BASH_REMATCH[2]}"; fi
}
# Every loop `up` started, as "pid context" lines. A loop's own child is skipped: between fork and exec it carries the
# loop's command line.
supervisors() {
  local p ctx
  for p in $(pgrep -f 'while true; do .* port-forward kubectl --context ' || true); do
    ctx=$(supervisor_context "$p")
    [[ -n $ctx ]] || continue
    [[ -z $(supervisor_context "$(ps -o ppid= -p "$p" 2>/dev/null | tr -d ' ' || true)") ]] || continue
    echo "$p $ctx"
  done
}
# $1's descendants, deepest last (collected before anything is stopped: an orphan is re-parented to init).
descendants() {
  local c
  for c in $(pgrep -P "$1" || true); do
    echo "$c"
    descendants "$c"
  done
}
# Every process listening on :8083, IPv4 and IPv6 alike (localhost tries ::1 first).
listeners_8083() { lsof -nP -t -iTCP:8083 -sTCP:LISTEN 2>/dev/null | sort -u || true; }
# The supervisor a listener runs under — its parent, or up to three levels up (a kubectl wrapper may not exec) — or nothing.
owner_of() {
  local pid=$1 _
  for _ in 1 2 3 4; do
    if [[ -n $(supervisor_context "$pid") ]]; then printf '%s' "$pid"; return 0; fi
    pid=$(ps -o ppid= -p "$pid" 2>/dev/null | tr -d ' ' || true)
    [[ $pid =~ ^[0-9]+$ && $pid -gt 1 ]] || return 0
  done
}

# They end up in SQL, a sed expression and URLs.
[[ $DB =~ ^[a-z_][a-z0-9_]*$ ]] || die "DB must match [a-z_][a-z0-9_]* (got '$DB')"
[[ $HOST_FROM_PODS =~ ^[A-Za-z0-9.-]+$ ]] || die "HOST_FROM_PODS must be a host name or IP (got '$HOST_FROM_PODS')"
[[ $HOST_BIND =~ ^[0-9a-fA-F.:]+$ ]] || die "HOST_BIND must be an IP address (got '$HOST_BIND')"
[[ $PORT =~ ^[0-9]+$ ]] || die "PORT must be a number (got '$PORT')"

# An IPv6 address goes in brackets in a URL. The browser (and seed-secret) open BASE: loopback and wildcard binds as
# localhost, any other address as itself. BASE is also Identity:PublicBaseUrl, whose origin the Host allows on /api/chat.
case $HOST_BIND in
  *:*) BIND_HOST="[$HOST_BIND]" ;;
  *) BIND_HOST=$HOST_BIND ;;
esac
case $HOST_BIND in
  127.0.0.1 | ::1 | 0.0.0.0 | ::) BASE=http://localhost:$PORT ;;
  *) BASE=http://$BIND_HOST:$PORT ;;
esac

up() {
  log "Postgres (compose project skanyxx, 127.0.0.1:55432)"
  "${COMPOSE[@]}" up -d --wait postgres

  if [[ $KUBE_CONTEXT == kind-* ]] && ! kind get clusters 2>/dev/null | grep -qx "${KUBE_CONTEXT#kind-}"; then
    log "kind cluster ${KUBE_CONTEXT#kind-}"
    kind create cluster --name "${KUBE_CONTEXT#kind-}"
  fi
  "${KUBECTL[@]}" get --raw /readyz >/dev/null || { log "context $KUBE_CONTEXT is not reachable"; exit 1; }

  # Install once. A later `helm upgrade` would put the chart's default-model-config back over what the owner saved in
  # the model step, so an existing release is left alone.
  local release
  release=$("${HELM[@]}" -n kagent status kagent -o json 2>/dev/null | jq -r .info.status || true)
  if [[ -n $release && $release != deployed ]]; then
    die "the kagent release is '$release', not deployed: fix it (helm -n kagent rollback kagent, or uninstall it) and re-run"
  fi
  if [[ -z $release ]]; then
    log "kagent $KAGENT_VERSION on $KUBE_CONTEXT (sample agents off, D019; model $OLLAMA_MODEL at $OLLAMA_HOST)"
    "${HELM[@]}" upgrade --install kagent-crds oci://ghcr.io/kagent-dev/kagent/helm/kagent-crds \
      --version "$KAGENT_VERSION" -n kagent --create-namespace --wait
    "${HELM[@]}" upgrade --install kagent oci://ghcr.io/kagent-dev/kagent/helm/kagent \
      --version "$KAGENT_VERSION" -n kagent -f "$ROOT/scripts/dev/kagent-values.yaml" \
      --set-string providers.ollama.model="$OLLAMA_MODEL" \
      --set-string providers.ollama.config.host="$OLLAMA_HOST" \
      --set-string providers.ollama.config.options.num_ctx="$OLLAMA_NUM_CTX" \
      --wait --timeout 10m
  fi

  # Only deploy/kagent/memory/ (D019); its in-cluster Skanyxx URL is swapped for this machine's Host.
  log "deploy/kagent/memory/ → $MCP_URL"
  # A YAML change that renames the URL would otherwise make the sed match nothing, silently pointing kagent at a
  # Service that does not exist here: refuse instead.
  local manifests in_cluster=http://skanyxx.skanyxx.svc:8081/mcp/memory
  manifests=$("${KUBECTL[@]}" kustomize "$ROOT/deploy/kagent/memory")
  grep -qF "$in_cluster" <<<"$manifests" || die "deploy/kagent/memory no longer contains $in_cluster: update the URL swap in $0"
  sed "s#$in_cluster#$MCP_URL#" <<<"$manifests" | "${KUBECTL[@]}" apply -f -

  # :8083 must be served only by this script's supervised forward to $KUBE_CONTEXT. Anything else answers /health just
  # as well, and /Model and Chat would then talk to another cluster's kagent while every check here passes (D176).
  command -v lsof >/dev/null && command -v pgrep >/dev/null || die "up needs lsof and pgrep"
  # Every supervisor loop of ours, found by its command line: one for another context (or a duplicate), even holding
  # no port right now, would win :8083 the next time our kubectl restarts.
  local sup ctx ours='' listener foreign stopped=''
  while read -r sup ctx; do
    if [[ $ctx == "$KUBE_CONTEXT" && -z $ours ]]; then
      ours=$sup
    else
      [[ $ctx == "$KUBE_CONTEXT" ]] && log "stopping a second port-forward supervisor $sup for $ctx" \
        || log "stopping the port-forward supervisor $sup (context $ctx, not $KUBE_CONTEXT)"
      # Paused while its processes are listed and stopped, or it could fork a new kubectl in between.
      kill -STOP "$sup" 2>/dev/null || true
      # shellcheck disable=SC2046 # one word per pid
      kill "$sup" $(descendants "$sup") 2>/dev/null || true
      kill -CONT "$sup" 2>/dev/null || true
      stopped=1
    fi
  done < <(supervisors)
  # Whatever still listens must run under ours (a stopped loop's port is given a few seconds to close).
  for _ in $(seq 1 "$([[ -n $stopped ]] && echo 10 || echo 1)"); do
    foreign=''
    for listener in $(listeners_8083); do
      [[ -n $ours && $(owner_of "$listener") == "$ours" ]] || foreign=$listener
    done
    [[ -z $foreign ]] && break
    sleep 1
  done
  [[ -z $foreign ]] || die "localhost:8083 is held by pid $foreign, not by this script's port-forward to $KUBE_CONTEXT (lsof -nP -iTCP:8083 -sTCP:LISTEN): stop it, then re-run"

  # Supervised: a port-forward dies with the pod it points at (a controller restart), so it is restarted in a loop.
  if [[ -z $ours ]]; then
    log "port-forward kagent-controller → localhost:8083"
    nohup bash -c 'while true; do "$@" >/dev/null 2>&1; sleep 2; done' port-forward \
      "${KUBECTL[@]}" -n kagent port-forward svc/kagent-controller 8083:8083 >/dev/null 2>&1 &
    ours=$!
  fi
  for _ in $(seq 1 20); do curl -fsS -m 2 http://localhost:8083/health >/dev/null 2>&1 && break; sleep 1; done
  foreign=''
  for listener in $(listeners_8083); do
    [[ $(owner_of "$listener") == "$ours" ]] || foreign=$listener
  done
  [[ -z $foreign ]] || die "localhost:8083 is also held by pid $foreign (lsof -nP -iTCP:8083 -sTCP:LISTEN): stop it, then re-run"
  if [[ -z $(listeners_8083) ]] || ! curl -fsS -m 2 http://localhost:8083/health >/dev/null 2>&1; then
    die "kagent's API is not reachable through this script's port-forward on localhost:8083 (kubectl --context $KUBE_CONTEXT -n kagent get pods)"
  fi

  if ! curl -fsS -m 3 "$OLLAMA_LOCAL/api/tags" 2>/dev/null | jq -e --arg m "$OLLAMA_MODEL" '.models[] | select(.name == $m)' >/dev/null; then
    log "WARNING: Ollama on this machine does not serve $OLLAMA_MODEL (ollama pull $OLLAMA_MODEL), or pick another model in /Model"
  fi
  log "next: scripts/dev/first-hour.sh host, open $BASE/Setup, then seed-secret"
}

host() {
  if ! "${COMPOSE[@]}" exec -T postgres psql -U skanyxx -d skanyxx -v ON_ERROR_STOP=1 -tAc "SELECT 1 FROM pg_database WHERE datname = '$DB'" | grep -qx 1; then
    log "database $DB"
    "${COMPOSE[@]}" exec -T postgres createdb -U skanyxx "$DB"
  fi
  local cs="Host=localhost;Port=55432;Database=$DB;Username=skanyxx;Password=skanyxx;Timeout=10;Command Timeout=15"
  export ASPNETCORE_ENVIRONMENT=Development
  [[ $HOST_BIND == 0.0.0.0 || $HOST_BIND == :: ]] && log "WARNING: HOST_BIND=$HOST_BIND exposes a Development Host (Swagger, /Setup) to the network"
  export ASPNETCORE_URLS=http://$BIND_HOST:$PORT
  # The seed's memory MCP calls come from the cluster under $HOST_FROM_PODS.
  export AllowedHosts="localhost;127.0.0.1;[::1];$BIND_HOST;$HOST_FROM_PODS"
  # The Chat page's fetch to the guarded /api/chat carries its Origin: $BASE's is allowed as the PublicBaseUrl, and the
  # bind address itself (127.0.0.1 when that is typed instead of localhost) is listed.
  export Identity__PublicBaseUrl=$BASE Skanyxx__AllowedOrigins__0=http://$BIND_HOST:$PORT
  export ConnectionStrings__Memory=$cs ConnectionStrings__Identity=$cs ConnectionStrings__Tickets=$cs
  export KAgent__BaseUrl=localhost KAgent__Port=8083
  # A local model with tool calls can take minutes on one answer (the default is 300 s too; control calls get 10 s).
  export KAgent__ChatTimeoutSeconds=300
  export Tickets__Source=local Tickets__LocalPath=$ROOT/deploy/tickets/sample-tickets.json
  # The studio (slice 3): proposals are pull requests in the bundled git; studio agents reach memory like the seed does.
  if [[ -s $GIT_TOKEN_FILE ]]; then
    export Studio__Git__BaseUrl=$GIT_URL Studio__MemoryMcpUrl=$MCP_URL
    Studio__Git__Token=$(<"$GIT_TOKEN_FILE")
    export Studio__Git__Token
  else
    log "no $GIT_TOKEN_FILE: the studio is off (scripts/dev/first-hour.sh git)"
  fi
  log "Host on http://$BIND_HOST:$PORT, open $BASE (database $DB)"
  exec dotnet run --project "$ROOT/src/Skanyxx.Host" --no-launch-profile
}

# D2 / D084: the owner issues the seed's secret with actsForUsers: true, so the seed searches and writes each person's
# personal memory as that person (kagent forwards the chat user as X-User-Id). The secret goes from the response
# straight into the Secret — never into a file, argv or the log — and the label change makes kagent re-render the agent.
seed_secret() {
  : "${OWNER_EMAIL:?set OWNER_EMAIL}" "${OWNER_PASSWORD:?set OWNER_PASSWORD}"
  local base=$BASE token secret
  token=$(jq -n '{email: env.OWNER_EMAIL, password: env.OWNER_PASSWORD}' \
    | curl -fsS -X POST -H 'Content-Type: application/json' --data @- "$base/api/identity/sign-in" \
    | jq -er .tokens.accessToken)
  secret=$(curl -fsS -X POST -H @<(printf 'Authorization: Bearer %s' "$token") -H 'Content-Type: application/json' \
    -d '{"actsForUsers": true}' "$base/api/memory/agents/seed/secret" | jq -er .secret)
  # Server-side apply: a client-side apply would copy the whole Secret, token included, into the
  # last-applied-configuration annotation, where metadata readers and `kubectl describe` see it.
  printf 'Bearer %s' "$secret" \
    | "${KUBECTL[@]}" -n kagent create secret generic skanyxx-memory-seed --from-file=token=/dev/stdin \
        --dry-run=client -o yaml | "${KUBECTL[@]}" apply --server-side --force-conflicts --field-manager=skanyxx-first-hour -f -
  unset secret token
  # A Secret written by an earlier client-side apply still carries that annotation (with the old token).
  "${KUBECTL[@]}" -n kagent annotate secret skanyxx-memory-seed kubectl.kubernetes.io/last-applied-configuration- >/dev/null 2>&1 || true
  "${KUBECTL[@]}" -n kagent label agent seed skanyxx.dev/memory-secret="$(date +%s)" --overwrite
  log "seed secret issued (actsForUsers: true) and stored in kagent/skanyxx-memory-seed"
}

# D023 locally: Gitea from the compose file, one account Skanyxx uses (skanyxx-bot, a plain user — not a site admin —
# that owns the org skanyxx holding the repo), with a random password nobody keeps, and a token scoped to what the
# studio calls (orgs and repos; D123). The token goes from the container straight into $GIT_TOKEN_FILE (0600) — never
# argv, the log or the repo. A rerun revokes the account's earlier skanyxx-* tokens.
git_server() {
  log "Gitea (compose project skanyxx, $GIT_URL)"
  "${COMPOSE[@]}" up -d --wait gitea
  local exec=("${COMPOSE[@]}" exec -T -u git gitea gitea admin user)
  if ! "${exec[@]}" list 2>/dev/null | awk 'NR > 1 {print $2}' | grep -qx skanyxx-bot; then
    log "Gitea account skanyxx-bot (not an admin)"
    "${exec[@]}" create --username skanyxx-bot --email skanyxx-bot@skanyxx.local --must-change-password=false \
      --random-password >/dev/null
  fi

  # A one-off password for this run's basic-auth calls (Gitea manages tokens only with a password). It reaches the
  # container through stdin and curl through a config on stdin, so it is in no argv on the host.
  local password
  password=$(head -c 32 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c 32)
  printf '%s' "$password" | "${COMPOSE[@]}" exec -T -u git gitea sh -c \
    'read -r p; gitea admin user change-password --username skanyxx-bot --password "$p" --must-change-password=false' >/dev/null
  as_bot() { printf 'user = "skanyxx-bot:%s"\n' "$password" | curl -fsS -m 10 -K - -H 'Content-Type: application/json' "$@"; }

  # An earlier run made the bot a site admin: an admin token can lift branch protection, so main would only be as
  # trusted as the token. Demote it (the bot still owns its org, which is all the studio needs). Gitea refuses to
  # demote its last admin, so a separate admin (gitea-admin, random password nobody keeps) takes that role.
  if as_bot "$GIT_URL/api/v1/user" | grep -q '"is_admin":true'; then
    log "skanyxx-bot is a site admin from an earlier run; demoting it"
    "${exec[@]}" list --admin 2>/dev/null | awk 'NR > 1 {print $2}' | grep -qx gitea-admin \
      || "${exec[@]}" create --admin --username gitea-admin --email gitea-admin@skanyxx.local --must-change-password=false \
        --random-password >/dev/null
    as_bot -X PATCH "$GIT_URL/api/v1/admin/users/skanyxx-bot" \
      -d '{"login_name":"skanyxx-bot","source_id":0,"admin":false}' >/dev/null
  fi
  local id
  for id in $(as_bot "$GIT_URL/api/v1/users/skanyxx-bot/tokens" \
      | python3 -c 'import json,sys; print(" ".join(str(t["id"]) for t in json.load(sys.stdin) if t["name"].startswith("skanyxx-")))'); do
    as_bot -X DELETE "$GIT_URL/api/v1/users/skanyxx-bot/tokens/$id" >/dev/null
  done

  mkdir -p "$(dirname "$GIT_TOKEN_FILE")"
  (umask 077 && as_bot -X POST "$GIT_URL/api/v1/users/skanyxx-bot/tokens" \
      -d "{\"name\":\"skanyxx-$(date +%s)\",\"scopes\":[\"write:organization\",\"write:repository\"]}" \
    | python3 -c 'import json,sys; print(json.load(sys.stdin)["sha1"], end="")' >"$GIT_TOKEN_FILE")
  chmod 600 "$GIT_TOKEN_FILE" # umask does not tighten a file that already existed
  unset password
  [[ -s $GIT_TOKEN_FILE ]] || die "Gitea issued no token"
  log "token for Skanyxx in $GIT_TOKEN_FILE (scopes write:organization, write:repository); restart the host (scripts/dev/first-hour.sh host) to use it"
}

status() {
  "${COMPOSE[@]}" ps postgres
  "${KUBECTL[@]}" -n kagent get agents,remotemcpservers,modelconfigs
  curl -fsS -m 3 http://localhost:8083/health >/dev/null && echo "kagent API: localhost:8083 up" || echo "kagent API: localhost:8083 down"
  local sup ctx listener owner found=''
  while read -r sup ctx; do echo "port-forward supervisor: pid $sup, context $ctx"; done < <(supervisors)
  for listener in $(listeners_8083); do
    owner=$(owner_of "$listener")
    if [[ -n $owner ]]; then
      echo "localhost:8083: pid $listener under port-forward supervisor $owner, context $(supervisor_context "$owner")"
    else
      echo "localhost:8083: pid $listener is NOT this script's port-forward"
    fi
    found=1
  done
  if ! command -v lsof >/dev/null; then
    echo "localhost:8083: owner unknown (lsof missing)"
  elif [[ -z $found ]]; then
    echo "localhost:8083: nothing listens"
  fi
  curl -fsS -m 3 "$BASE/health" >/dev/null && echo "Host: $BASE up" || echo "Host: $BASE down"
  curl -fsS -m 3 "$GIT_URL/api/healthz" >/dev/null && echo "Gitea: $GIT_URL up" || echo "Gitea: $GIT_URL down (scripts/dev/first-hour.sh git)"
}

case "${1:-}" in
  up) up ;;
  host) host ;;
  seed-secret) seed_secret ;;
  git) git_server ;;
  status) status ;;
  *) sed -n '2,30p' "$0" >&2; exit 2 ;;
esac
