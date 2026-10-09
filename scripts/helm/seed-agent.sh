#!/usr/bin/env bash
# The seed agent on a Helm install (D4 of slice 4; D019: only deploy/kagent/memory/, never kagent's sample pack).
# Run once after the owner exists (/Setup); run again to rotate the seed's memory secret.
#
#   kubectl -n skanyxx port-forward svc/skanyxx 8080:8080 &      # or BASE=https://skanyxx.example.com
#   OWNER_EMAIL=… OWNER_PASSWORD=… scripts/helm/seed-agent.sh [--insecure]
#
# 0. checks kubectl can do steps 2-3 and finds the release's Skanyxx Service (by its labels, so any release name works)
#    and its `mcp` port; 1. as the owner, issues the seed's secret with actsForUsers: true (D084) straight into the
#    Secret the RemoteMCPServer reads (never a file, argv or the log). Issuing REVOKES the previous secret at once, so
#    if step 1's apply then fails the seed is locked out until this script runs again (it says so); 2. applies
#    deploy/kagent/memory/ (seed Agent + its RemoteMCPServer) into the release namespace, pointed at Skanyxx's
#    in-cluster /mcp/memory port — after the Secret, so the first tool discovery already has it; 3. bumps the Agent's
#    label so kagent re-renders it (rotation). Not a Helm hook: the Agent kind only exists once the release's CRDs are
#    installed, and the secret needs an owner, who exists only after /Setup.
#
# Settings (env): RELEASE (skanyxx), NAMESPACE (skanyxx), BASE (http://localhost:8080), KUBE_CONTEXT (current).
# BASE must be https:// or a loopback http:// (the owner's password goes there); --insecure allows any http://.
set -euo pipefail
: "${OWNER_EMAIL:?set OWNER_EMAIL}" "${OWNER_PASSWORD:?set OWNER_PASSWORD}"
RELEASE=${RELEASE:-skanyxx}
NAMESPACE=${NAMESPACE:-skanyxx}
BASE=${BASE:-http://localhost:8080}
KUBE_CONTEXT=${KUBE_CONTEXT:-$(kubectl config current-context)}
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
KUBECTL=(kubectl --context "$KUBE_CONTEXT" -n "$NAMESPACE")
log() { printf '==> %s\n' "$*" >&2; }
die() { log "$*"; exit 1; }

[[ $NAMESPACE =~ ^[a-z0-9-]+$ && $RELEASE =~ ^[a-z0-9-]+$ ]] || die "NAMESPACE and RELEASE must be DNS labels"
if [[ $BASE == http://* && ${1:-} != --insecure ]] && ! [[ $BASE =~ ^http://(localhost|127\.0\.0\.1|\[::1\])(:[0-9]+)?/?$ ]]; then
  die "BASE=$BASE would send the owner's password in clear text: use https:// or a port-forward (or pass --insecure)"
fi

# 0. Everything kubectl must do, before anything is rotated.
for check in "create secrets" "patch secrets" "create agents.kagent.dev" "patch agents.kagent.dev" "create remotemcpservers.kagent.dev"; do
  [[ $("${KUBECTL[@]}" auth can-i $check 2>/dev/null) == yes ]] || die "kubectl ($KUBE_CONTEXT) may not: $check in $NAMESPACE"
done
SERVICE=$("${KUBECTL[@]}" get svc -l "app.kubernetes.io/name=skanyxx,app.kubernetes.io/instance=$RELEASE" -o jsonpath='{.items[0].metadata.name}' 2>/dev/null || true)
MCP_PORT=$("${KUBECTL[@]}" get svc "$SERVICE" -o jsonpath='{.spec.ports[?(@.name=="mcp")].port}' 2>/dev/null || true)
[[ -n $SERVICE && -n $MCP_PORT ]] || die "no Skanyxx Service with an mcp port for release $RELEASE in $NAMESPACE"
MCP_URL=http://$SERVICE.$NAMESPACE.svc:$MCP_PORT/mcp/memory

# 1. Issue (this revokes the previous secret) and store it.
token=$(jq -n '{email: env.OWNER_EMAIL, password: env.OWNER_PASSWORD}' \
  | curl -fsS -X POST -H 'Content-Type: application/json' --data @- "$BASE/api/identity/sign-in" \
  | jq -er .tokens.accessToken)
secret=$(curl -fsS -X POST -H @<(printf 'Authorization: Bearer %s' "$token") -H 'Content-Type: application/json' \
  -d '{"actsForUsers": true}' "$BASE/api/memory/agents/seed/secret" | jq -er .secret)
unset token
# Server-side apply: a client-side apply would copy the token into the last-applied-configuration annotation.
if ! printf 'Bearer %s' "$secret" \
  | "${KUBECTL[@]}" create secret generic skanyxx-memory-seed --from-file=token=/dev/stdin --dry-run=client -o yaml \
  | "${KUBECTL[@]}" apply --server-side --force-conflicts --field-manager=skanyxx-seed -f - >/dev/null; then
  unset secret
  die "the new seed secret was issued (the old one is revoked) but could not be stored in $NAMESPACE/skanyxx-memory-seed: the seed cannot reach memory until this script runs again successfully"
fi
unset secret

# 2. The seed Agent + RemoteMCPServer, pointed at this release's mcp port.
log "deploy/kagent/memory/ → namespace $NAMESPACE, $MCP_URL"
# Each swap must match: a YAML change would otherwise leave the default URL/namespace in place without a word.
IN_CLUSTER=http://skanyxx.skanyxx.svc:8081/mcp/memory
manifests=$(kubectl --context "$KUBE_CONTEXT" kustomize "$ROOT/deploy/kagent/memory")
grep -qF "$IN_CLUSTER" <<<"$manifests" || die "deploy/kagent/memory no longer contains $IN_CLUSTER: update the URL swap in $0"
grep -q '^  namespace: kagent$' <<<"$manifests" || die "deploy/kagent/memory no longer sets namespace: kagent: update the namespace swap in $0"
sed -e "s#$IN_CLUSTER#$MCP_URL#" -e "s#^  namespace: kagent\$#  namespace: $NAMESPACE#" <<<"$manifests" \
  | "${KUBECTL[@]}" apply -f -
# 3. Re-render the Agent so it picks the new secret up.
"${KUBECTL[@]}" label agent seed skanyxx.dev/memory-secret="$(date +%s)" --overwrite >/dev/null
log "seed secret issued (actsForUsers: true) into $NAMESPACE/skanyxx-memory-seed; Agent seed re-rendered"
