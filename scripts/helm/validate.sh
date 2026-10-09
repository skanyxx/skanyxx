#!/usr/bin/env bash
# Static checks of the umbrella chart (D5): helm lint + helm template | kubeconform -strict for the default values,
# bring-your-own (ci/byo-values.yaml, ingress on) and AX on (ci/ax-values.yaml), then the chart's refusals. Needs helm
# and kubeconform (brew install kubeconform). Built-in kinds use kubeconform's default schemas; the kagent
# CustomResourceDefinition objects themselves are checked against the apiextensions/v1 schema from yannh's
# (non-standalone) set. No custom resource (Agent, ModelConfig, ...) is rendered, so no CRD-derived schema is needed.
# Dragonfly's optional CRDs (ServiceMonitor, PrometheusRule) are off.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
CHART=$ROOT/deploy/helm/skanyxx
K8S_VERSION=${K8S_VERSION:-1.33.0}
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
die() { echo "FAIL: $*"; exit 1; }
# One rendered NetworkPolicy, by name.
netpol() { awk -v n="$1" 'BEGIN{RS="\n---"} /\nkind: NetworkPolicy/ && $0 ~ "\n  name: " n "\n" {print}' "$2"; }

helm dependency build "$CHART" >/dev/null

for values in "" ci/byo-values.yaml ci/ax-values.yaml; do
  label=$(basename "${values:-default}" .yaml)
  args=(--namespace skanyxx)
  [[ -n $values ]] && args+=(-f "$CHART/$values")
  echo "==> $label"
  helm lint "$CHART" "${args[@]}" --strict >"$WORK/lint.txt" || { cat "$WORK/lint.txt"; exit 1; }
  helm template skanyxx "$CHART" "${args[@]}" >"$WORK/$label.yaml"
  kubeconform -strict -summary -kubernetes-version "$K8S_VERSION" -schema-location default \
    -schema-location 'https://raw.githubusercontent.com/yannh/kubernetes-json-schema/master/{{.NormalizedKubernetesVersion}}/{{.ResourceKind}}{{.KindSuffix}}.json' \
    "$WORK/$label.yaml"
  if grep -qi -- '- path: /mcp' "$WORK/$label.yaml"; then die "an ingress publishes /mcp"; fi
  if grep -q '^kind: ModelConfig' "$WORK/$label.yaml"; then die "the chart renders a ModelConfig (D099: it must not)"; fi
  if grep -qE '^kind: Cluster(Role|RoleBinding)$' "$WORK/$label.yaml"; then die "a ClusterRole/Binding is rendered (D136: kagent's RBAC stays namespaced)"; fi
  # Every Ingress backend is the http Service port: the mcp port (8081) is never published (D138).
  if grep -A4 'service:' "$WORK/$label.yaml" | grep -q 'number: 8081'; then die "an Ingress targets the mcp port"; fi
  secrets=$(grep -c '^kind: Secret$' "$WORK/$label.yaml" || true)
  kept=$(grep -c 'helm.sh/resource-policy: keep' "$WORK/$label.yaml" || true)
  [[ $secrets == "$kept" ]] || die "$label: $secrets Secrets but $kept kept on uninstall (D139)"
  if [[ $label != byo-values ]]; then
    grep -q 'name: skanyxx-default-deny' "$WORK/$label.yaml" || die "no default-deny NetworkPolicy (D137)"
  fi
done
grep -q 'kind: Ingress' "$WORK/byo-values.yaml" || die "the BYO values should render an Ingress"

# N1 (D147): with BYO kagent, networkPolicy.mcpFrom is admitted on the mcp port, and only there.
byo_np=$(netpol skanyxx "$WORK/byo-values.yaml")
grep -A4 'kubernetes.io/metadata.name: kagent$' <<<"$byo_np" | grep -q -- '- port: mcp' \
  || die "BYO: networkPolicy.mcpFrom is not admitted on the mcp port"
# ... and with no peer at all, NOTES says nothing can reach /mcp/memory (client-only dry run: no cluster is read).
KUBECONFIG=/dev/null helm install skanyxx "$CHART" --namespace skanyxx -f "$CHART/ci/byo-values.yaml" --set networkPolicy.mcpFrom=null \
  --dry-run=client >"$WORK/notes.txt" 2>&1 || { cat "$WORK/notes.txt"; exit 1; }
grep -q 'nothing may reach /mcp/memory' "$WORK/notes.txt" || die "BYO with no mcpFrom: NOTES does not warn"
KUBECONFIG=/dev/null helm install skanyxx "$CHART" --namespace skanyxx -f "$CHART/ci/byo-values.yaml" --dry-run=client >"$WORK/notes.txt" 2>&1
if grep -q 'nothing may reach /mcp/memory' "$WORK/notes.txt"; then die "BYO with mcpFrom: NOTES warns anyway"; fi

# N3 (D148): agent pods send to kagent's controller, Skanyxx's mcp port and the model only, never the whole namespace.
agents=$(netpol kagent-agents "$WORK/default.yaml")
[[ -n $agents ]] || die "no kagent-agents NetworkPolicy"
if grep -q 'podSelector: {}' <<<"$agents"; then die "kagent-agents egress admits every pod in the namespace"; fi
grep -A6 'app.kubernetes.io/name: skanyxx' <<<"$agents" | grep -q -- '- port: mcp' || die "agents cannot reach Skanyxx's mcp port"
if grep -q -- '- port: http' <<<"$agents"; then die "agents may reach Skanyxx's UI/API port"; fi

# N4 (D149): the model egress excludes every clusterCidrs entry, including one the operator adds.
helm template skanyxx "$CHART" --namespace skanyxx --set 'networkPolicy.clusterCidrs={10.0.0.0/8,203.0.113.0/24,2600:1f00::/40}' >"$WORK/cidrs.yaml"
agents=$(netpol kagent-agents "$WORK/cidrs.yaml")
grep -A4 'cidr: "0.0.0.0/0"' <<<"$agents" | grep -q '"203.0.113.0/24"' || die "an added clusterCidrs range is not excluded"
grep -A4 'cidr: "::/0"' <<<"$agents" | grep -q '"2600:1f00::/40"' || die "an added IPv6 clusterCidrs range is not excluded"
if sed -n '/cidr: "0.0.0.0\/0"/,/cidr: "::\/0"/p' <<<"$agents" | grep -q '"2600:'; then die "an IPv6 range under an IPv4 ipBlock"; fi
if sed -n '/cidr: "::\/0"/,/ports:/p' <<<"$agents" | grep -q '"203\.'; then die "an IPv4 range under an IPv6 ipBlock"; fi

# Each of these must be refused by the chart (template fail or values.schema.json).
refuse() {
  local why=$1; shift
  if helm template skanyxx "$CHART" --namespace skanyxx "$@" >"$WORK/refused.txt" 2>&1; then die "accepted: $why"; fi
  echo "refused: $why ($(grep -m1 -oE '(execution error|values don.t meet).*' "$WORK/refused.txt" | cut -c1-90))"
}
ING=(--set ingress.enabled=true --set 'ingress.hosts[0]=x.example.com')
refuse "an /MCP ingress path" "${ING[@]}" --set 'ingress.prefixPaths[0]=/MCP'
refuse "/ as an ingress prefix (routes everything, /mcp included)" "${ING[@]}" --set 'ingress.prefixPaths[0]=/'
refuse "a regex ingress path" "${ING[@]}" --set 'ingress.prefixPaths[0]=/.*'
refuse "a %-escaped ingress path" "${ING[@]}" --set 'ingress.prefixPaths[0]=/%6dcp'
refuse "a dot-segment ingress path" "${ING[@]}" --set 'ingress.prefixPaths[0]=/..'
refuse "the nginx use-regex annotation" "${ING[@]}" --set 'ingress.annotations.nginx\.ingress\.kubernetes\.io/use-regex=true'
refuse "an nginx configuration snippet" "${ING[@]}" --set 'ingress.annotations.nginx\.ingress\.kubernetes\.io/server-snippet=x'
refuse "a typo'd key (postgres.enabled)" --set postgres.enabled=false
refuse "a string where a boolean belongs" --set ingress.enabled=yes
refuse "kagent.providers set (Helm would own default-model-config)" --set kagent.providers.openAI.apiKey=x
refuse "kagent RBAC outside the release namespace" --namespace other
refuse "postgresql.existingSecret without the connection-string Secret" --set postgresql.existingSecret=pg
refuse "an image digest that is not sha256" --set skanyxx.image.digest=latest
refuse "service.port == service.mcpPort (one Service port twice)" --set skanyxx.service.port=8081
refuse "service.mcpPort == 8080 (the UI/API container port)" --set skanyxx.service.mcpPort=8080
refuse "modelEgress.ports null (would open every port)" --set networkPolicy.modelEgress.ports=null
refuse "modelEgress.ports empty (would open every port)" --set-json 'networkPolicy.modelEgress.ports=[]'
refuse "a narrower modelEgress cidr (belongs in extra)" --set 'networkPolicy.modelEgress.cidrs={203.0.113.0/24}'
echo "ok: lint + kubeconform -strict for default, byo, ax; /mcp, ModelConfig, RBAC, keep, default-deny, mcpFrom, agent egress and clusterCidrs checks hold; 18 refusals"
