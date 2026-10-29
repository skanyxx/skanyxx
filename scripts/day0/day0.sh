#!/usr/bin/env bash
# Day 0 (requirements §5): install the infrastructure on Kubernetes with the umbrella chart, then an end-to-end smoke.
#
#   scripts/day0/day0.sh up      [--cluster skx-day0 | --context <ctx>] [--rebuild] [--skip-build]
#   scripts/day0/day0.sh smoke   pods ready, UI through the Ingress (or Service), /health, owner bootstrap,
#                                kagent through Skanyxx, an employee refused on admin APIs (403)
#   scripts/day0/day0.sh status
#   scripts/day0/day0.sh down    uninstall; a kind cluster this script created is deleted
#   scripts/day0/day0.sh all     up + smoke
#
# --cluster NAME: a kind cluster of that name (default skx-day0), created when missing, with an ingress-nginx
#   on 127.0.0.1:$UI_PORT. --context CTX: an existing cluster (never the current context by default); Skanyxx is then
#   reached by a port-forward to its Service on 127.0.0.1:$PF_PORT and the image must be pullable there
#   (IMAGE/IMAGE_TAG of a registry you push to yourself).
# Env: NAMESPACE (skanyxx), RELEASE (skanyxx), IMAGE (skanyxx), IMAGE_TAG (e2e-<git short sha>), UI_PORT (18430),
#   PF_PORT (5431), INGRESS_NGINX (controller-v1.15.1),
#   SKX_E2E_STATE (~/.config/skanyxx-e2e). Owner credentials: $STATE/owner.env (0600), never printed.
set -euo pipefail
. "$(dirname "$0")/../e2e/lib.sh"

cmd=${1:-}; shift || true
REBUILD='' SKIP_BUILD=''
while [[ $# -gt 0 ]]; do
  case $1 in
    --cluster) CLUSTER=$2; shift 2 ;;
    --context) CONTEXT=$2; shift 2 ;;
    --rebuild) REBUILD=1; shift ;;
    --skip-build) SKIP_BUILD=1; shift ;;
    *) die "unknown flag $1" ;;
  esac
done
target_init
IMAGE=${IMAGE:-skanyxx}
IMAGE_TAG=${IMAGE_TAG:-e2e-$(git -C "$ROOT" rev-parse --short HEAD)}
INGRESS_NGINX=${INGRESS_NGINX:-controller-v1.15.1}
CHART=$ROOT/deploy/helm/skanyxx

cluster_up() {
  if [[ -n $CLUSTER ]]; then
    if ! kind get clusters 2>/dev/null | grep -qx "$CLUSTER"; then
      log "kind cluster $CLUSTER (ingress on 127.0.0.1:$UI_PORT)"
      kind create cluster --name "$CLUSTER" --wait 120s --config - <<EOF
kind: Cluster
apiVersion: kind.x-k8s.io/v1alpha4
nodes:
  - role: control-plane
    labels:
      ingress-ready: "true"
    extraPortMappings:
      - containerPort: 80
        hostPort: $UI_PORT
        listenAddress: 127.0.0.1
EOF
      # Marks the cluster as ours: `down` deletes only a cluster carrying it. A new cluster starts with new state
      # (an owner of a deleted cluster cannot sign in to this one); its kubeconfig stays.
      own kube-system scripts/day0
      find "$STATE" -mindepth 1 ! -name kubeconfig -delete
    fi
  fi
  "${KUBECTL[@]}" get --raw /readyz >/dev/null || die "context $CONTEXT is not reachable"
}

image() {
  if [[ -n $SKIP_BUILD ]]; then
    log "image build skipped ($IMAGE:$IMAGE_TAG must already be on the nodes or pullable)"
  elif [[ -n $REBUILD ]] || ! docker image inspect "$IMAGE:$IMAGE_TAG" >/dev/null 2>&1; then
    log "image $IMAGE:$IMAGE_TAG (scripts/helm/build-image.sh)"
    IMAGE=$IMAGE TAG=$IMAGE_TAG "$ROOT/scripts/helm/build-image.sh"
  else
    log "image $IMAGE:$IMAGE_TAG exists (--rebuild to build again)"
  fi
  if [[ -n $CLUSTER && -z $SKIP_BUILD ]]; then
    log "kind load docker-image $IMAGE:$IMAGE_TAG → $CLUSTER"
    kind load docker-image "$IMAGE:$IMAGE_TAG" --name "$CLUSTER"
  fi
}

ingress() {
  [[ -n $CLUSTER ]] || return 0
  log "ingress-nginx $INGRESS_NGINX (kind provider manifest)"
  "${KUBECTL[@]}" apply -f "https://raw.githubusercontent.com/kubernetes/ingress-nginx/$INGRESS_NGINX/deploy/static/provider/kind/deploy.yaml" >/dev/null
  "${KUBECTL[@]}" -n ingress-nginx rollout status deploy/ingress-nginx-controller --timeout=300s
  touch "$STATE/ingress"
}

install() {
  log "helm dependency build"
  helm dependency build "$CHART" >/dev/null
  local args=(--namespace "$NAMESPACE" --create-namespace --set-string "skanyxx.image.repository=$IMAGE"
    --set-string "skanyxx.image.tag=$IMAGE_TAG")
  if [[ $NAMESPACE != skanyxx ]]; then args+=(--set "kagent.rbac.namespaces={$NAMESPACE}"); fi
  if [[ -f $STATE/ingress ]]; then
    args+=(-f "$ROOT/deploy/helm/local/day0-values.yaml" --set-string "skanyxx.publicUrl=http://localhost:$UI_PORT")
  else
    args+=(--set-string "skanyxx.publicUrl=http://localhost:$PF_PORT")
  fi
  # An existing release keeps the values day1 added (--reuse-values would drop a changed image tag, so day1's file
  # is passed again when it exists).
  [[ -f $STATE/day1-values.yaml ]] && args+=(-f "$STATE/day1-values.yaml")
  # Never over a release someone else installed (an existing context may run the real one).
  if "${HELM[@]}" -n "$NAMESPACE" status "$RELEASE" >/dev/null 2>&1 && ! owned "$NAMESPACE" "scripts/day0 release $RELEASE"; then
    die "$CONTEXT/$NAMESPACE already has a release $RELEASE that this script did not install"
  fi
  log "helm upgrade --install $RELEASE ($IMAGE:$IMAGE_TAG) → $CONTEXT/$NAMESPACE"
  "${HELM[@]}" upgrade --install "$RELEASE" "$CHART" "${args[@]}" --wait --timeout 15m
  own "$NAMESPACE" "scripts/day0 release $RELEASE"
}

pods_ready() {
  log "pods ready in $NAMESPACE"
  # Per workload, not `wait pod --all`: a pod a rolling update replaces would be waited on until the timeout.
  local w
  for w in $("${KUBECTL[@]}" -n "$NAMESPACE" get deploy,statefulset -o name); do
    "${KUBECTL[@]}" -n "$NAMESPACE" rollout status "$w" --timeout=600s
  done
  "${KUBECTL[@]}" -n "$NAMESPACE" get pods -o wide
}

smoke() {
  pods_ready
  local base
  base=$(skx_base) || die "Skanyxx is not reachable"
  log "smoke against $base"
  run_node "$ROOT/scripts/day0/smoke.mjs"
}

status() {
  "${KUBECTL[@]}" -n "$NAMESPACE" get pods,svc,ingress 2>/dev/null || true
  "${HELM[@]}" -n "$NAMESPACE" status "$RELEASE" 2>/dev/null | head -8 || true
  ls -l "$STATE"
}

down() {
  port_forward_stop_all
  if [[ -n $CLUSTER ]] && kind get clusters 2>/dev/null | grep -qx "$CLUSTER"; then
    owned kube-system scripts/day0 \
      || die "kind cluster $CLUSTER was not created by this script (no kube-system/skanyxx-e2e-owner): not deleting it"
    log "deleting kind cluster $CLUSTER"
    kind delete cluster --name "$CLUSTER"
  elif [[ -z $CLUSTER ]]; then
    owned "$NAMESPACE" "scripts/day0 release $RELEASE" || die "release $RELEASE in $CONTEXT/$NAMESPACE was not installed by this script: not uninstalling it"
    log "helm uninstall $RELEASE from $CONTEXT/$NAMESPACE (kept Secrets and PVCs stay: D139)"
    "${HELM[@]}" -n "$NAMESPACE" uninstall "$RELEASE" --wait || true
  fi
  rm -rf "$STATE"
  log "state $STATE removed"
}

case $cmd in
  up) cluster_up; image; ingress; install; pods_ready ;;
  smoke) smoke ;;
  all) cluster_up; image; ingress; install; smoke ;;
  status) status ;;
  down) down ;;
  *) sed -n '2,20p' "$0" >&2; exit 2 ;;
esac
