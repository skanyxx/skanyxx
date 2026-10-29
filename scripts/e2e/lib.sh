# Shared by scripts/day0, day1, day2 and e2e (sourced, not run). Bash 3.2+ (macOS) and Linux.
#
# State: every run keeps what it must remember (owner credentials, tokens, pids) under $STATE (0700); secret files are
# 0600 and their contents are never printed, logged or passed in argv.

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)

log() { printf '==> %s\n' "$*" >&2; }
die() { log "FAIL: $*"; exit 1; }

# The target: a kind cluster the scripts create (CLUSTER) or an existing context (CONTEXT), never the current context.
# Both come from flags or env; day0.sh writes the choice to $STATE/target so day1/day2 follow it.
target_init() {
  CLUSTER=${CLUSTER:-}
  CONTEXT=${CONTEXT:-}
  if [[ -z $CLUSTER && -z $CONTEXT ]]; then CLUSTER=skx-day0; fi
  if [[ -n $CLUSTER && -n $CONTEXT ]]; then die "give --cluster or --context, not both"; fi
  [[ $CONTEXT != kind-ate ]] || die "the kind cluster 'ate' is not ours"
  if [[ -n $CLUSTER ]]; then
    [[ $CLUSTER =~ ^[a-z0-9][a-z0-9-]*$ ]] || die "cluster name must be a DNS label (got '$CLUSTER')"
    [[ $CLUSTER != ate ]] || die "the kind cluster 'ate' is not ours"
    CONTEXT=kind-$CLUSTER
    NAME=$CLUSTER
  else
    NAME=ctx-$(printf '%s' "$CONTEXT" | tr -c 'a-zA-Z0-9-' '-')
  fi
  NAMESPACE=${NAMESPACE:-skanyxx}
  RELEASE=${RELEASE:-skanyxx}
  [[ $NAMESPACE =~ ^[a-z0-9-]+$ && $RELEASE =~ ^[a-z0-9-]+$ ]] || die "NAMESPACE and RELEASE must be DNS labels"
  STATE=${SKX_E2E_STATE:-${XDG_CONFIG_HOME:-$HOME/.config}/skanyxx-e2e}/$NAME
  (umask 077 && mkdir -p "$STATE")
  chmod 700 "$STATE"
  if [[ -n $CLUSTER ]]; then
    # A kubeconfig of its own: kind would otherwise add the cluster to ~/.kube/config and make it the current context,
    # under every other tool that follows the current context.
    export KUBECONFIG=$STATE/kubeconfig
    if [[ ! -s $KUBECONFIG ]] && kind get clusters 2>/dev/null | grep -qx "$CLUSTER"; then
      kind get kubeconfig --name "$CLUSTER" | secret_write "$KUBECONFIG"
    fi
  fi
  KUBECTL=(kubectl --context "$CONTEXT")
  HELM=(helm --kube-context "$CONTEXT")
  # Where this machine reaches Skanyxx: the ingress on kind, else a port-forward to the Service.
  UI_PORT=${UI_PORT:-18430}
  PF_PORT=${PF_PORT:-5431}
}

# Marks what the scripts created, so `down` removes only that: a ConfigMap skanyxx-e2e-owner (createdBy=<what>) in $1.
owned() { [[ $("${KUBECTL[@]}" -n "$1" get configmap skanyxx-e2e-owner -o jsonpath='{.data.createdBy}' 2>/dev/null) == "$2" ]]; }
own() { "${KUBECTL[@]}" -n "$1" create configmap skanyxx-e2e-owner --from-literal=createdBy="$2" --dry-run=client -o yaml | "${KUBECTL[@]}" apply -f - >/dev/null; }

# A secret file: written from stdin, 0600 from the first byte.
secret_write() { (umask 077 && cat >"$1"); chmod 600 "$1"; }

# A random secret of $1 characters from [A-Za-z0-9] (never printed by the callers).
# A bounded read first: `tr </dev/urandom | head` dies of SIGPIPE, which pipefail turns into a failure.
random_secret() { head -c 1024 /dev/urandom | LC_ALL=C tr -dc 'A-Za-z0-9' | head -c "${1:-32}"; }

# `kubectl port-forward` kept in the background, its pid in $STATE/pf-<name>.pid; restarted when dead.
#   port_forward <name> <local-port> <namespace> <target> <remote-port>
port_forward() {
  local name=$1 lport=$2 ns=$3 target=$4 rport=$5 pidf=$STATE/pf-$1.pid
  if [[ -f $pidf ]] && kill -0 "$(cat "$pidf")" 2>/dev/null && nc -z 127.0.0.1 "$lport" 2>/dev/null; then return 0; fi
  if nc -z 127.0.0.1 "$lport" 2>/dev/null; then die "127.0.0.1:$lport is held by another process (lsof -nP -iTCP:$lport)"; fi
  "${KUBECTL[@]}" -n "$ns" port-forward --address 127.0.0.1 "$target" "$lport:$rport" >"$STATE/pf-$name.log" 2>&1 &
  echo $! >"$pidf"
  local _
  for _ in $(seq 1 30); do nc -z 127.0.0.1 "$lport" 2>/dev/null && return 0; sleep 1; done
  die "port-forward $name ($target → 127.0.0.1:$lport) did not come up: $(tail -2 "$STATE/pf-$name.log")"
}

# Stops the port-forward <name> this tooling started (only a pid from its pid file, and only a port-forward).
port_forward_stop() {
  local f=$STATE/pf-$1.pid pid
  [[ -f $f ]] || return 0
  pid=$(cat "$f")
  if ps -o command= -p "$pid" 2>/dev/null | grep -q 'port-forward'; then kill "$pid" 2>/dev/null || true; fi
  rm -f "$f"
  sleep 1
}

# Stops every port-forward this tooling started for the target (only pids from its own pid files, and only kubectl).
port_forward_stop_all() {
  local f n
  for f in "$STATE"/pf-*.pid; do
    [[ -f $f ]] || continue
    n=${f##*/pf-}
    port_forward_stop "${n%.pid}"
  done
}

# The Skanyxx base URL this machine uses (ingress when day0 installed one, else the port-forward).
skx_base() {
  if [[ -f $STATE/ingress ]]; then
    echo "http://localhost:$UI_PORT"
  else
    port_forward skanyxx "$PF_PORT" "$NAMESPACE" "svc/$RELEASE" 8080
    echo "http://localhost:$PF_PORT"
  fi
}

# node with the shared helper module on the path (scripts/e2e/skx.mjs); SKX_* env carries the target.
run_node() {
  local base
  base=$(skx_base) || die "Skanyxx is not reachable"
  SKX_STATE=$STATE SKX_BASE=$base SKX_CONTEXT=$CONTEXT SKX_NAMESPACE=$NAMESPACE SKX_RELEASE=$RELEASE node "$@"
}
