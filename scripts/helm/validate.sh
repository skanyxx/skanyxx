#!/usr/bin/env bash
# Static checks of the umbrella chart (D5): helm lint + helm template | kubeconform -strict for the default values,
# bring-your-own (ci/byo-values.yaml, ingress on), AX on (ci/ax-values.yaml: an AX you run) and AX installed by the chart
# (ci/ax-install-values.yaml, D186), then the chart's refusals. Needs helm
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

kc() {
  kubeconform -strict -summary -kubernetes-version "$K8S_VERSION" -schema-location default \
    -schema-location 'https://raw.githubusercontent.com/yannh/kubernetes-json-schema/master/{{.NormalizedKubernetesVersion}}/{{.ResourceKind}}{{.KindSuffix}}.json' \
    "$1"
}

helm dependency build "$CHART" >/dev/null

for values in "" ci/byo-values.yaml ci/ax-values.yaml ci/ax-install-values.yaml; do
  label=$(basename "${values:-default}" .yaml)
  args=(--namespace skanyxx)
  [[ -n $values ]] && args+=(-f "$CHART/$values")
  echo "==> $label"
  helm lint "$CHART" "${args[@]}" --strict >"$WORK/lint.txt" || { cat "$WORK/lint.txt"; exit 1; }
  helm template skanyxx "$CHART" "${args[@]}" >"$WORK/$label.yaml"
  kc "$WORK/$label.yaml"
  if grep -qi -- '- path: /mcp' "$WORK/$label.yaml"; then die "an ingress publishes /mcp"; fi
  if grep -q '^kind: ModelConfig' "$WORK/$label.yaml"; then die "the chart renders a ModelConfig (D099: it must not)"; fi
  if grep -qE '^kind: Cluster(Role|RoleBinding)$' "$WORK/$label.yaml"; then die "a ClusterRole/Binding is rendered (D136: kagent's RBAC stays namespaced)"; fi
  # Every Ingress backend is the http Service port: the mcp port (8081) is never published (D138).
  if grep -A4 'service:' "$WORK/$label.yaml" | grep -q 'number: 8081'; then die "an Ingress targets the mcp port"; fi
  secrets=$(grep -c '^kind: Secret$' "$WORK/$label.yaml" || true)
  kept=$(awk 'BEGIN{RS="\n---"} /\nkind: Secret\n/ && /helm.sh\/resource-policy: keep/ {n++} END{print n+0}' "$WORK/$label.yaml")
  [[ $secrets == "$kept" ]] || die "$label: $secrets Secrets but $kept kept on uninstall (D139)"
  if [[ $label != byo-values ]]; then
    grep -q 'name: skanyxx-default-deny' "$WORK/$label.yaml" || die "no default-deny NetworkPolicy (D137)"
  fi
done
grep -q 'kind: Ingress' "$WORK/byo-values.yaml" || die "the BYO values should render an Ingress"

# D195-D197: the appliance's values plus what deploy/appliance/install.sh sets on the machine.
APP=(--namespace skanyxx -f "$ROOT/deploy/appliance/values.yaml" --set-string 'ingress.hosts[0]=skanyxx.example.com'
  --set-string 'ingress.tls[0].secretName=skanyxx-tls' --set-string 'ingress.tls[0].hosts[0]=skanyxx.example.com'
  --set-string skanyxx.image.tag=202610111200-abc1234)
echo "==> appliance"
helm lint "$CHART" "${APP[@]}" --strict >"$WORK/lint.txt" || { cat "$WORK/lint.txt"; exit 1; }
helm template skanyxx "$CHART" "${APP[@]}" >"$WORK/appliance.yaml"
kc "$WORK/appliance.yaml"
A=$WORK/appliance.yaml
if grep -qE 'name: skanyxx-(ax-|sandbox-)|Sandboxes__Address' "$A"; then die "appliance: AX is rendered (D069)"; fi
grep -A1 'name: Sandboxes__Enabled' "$A" | grep -q 'value: "false"' || die "appliance: the sandboxes module is on (D069)"
if grep -qi -- '- path: /mcp' "$A"; then die "appliance: an ingress publishes /mcp"; fi
grep -q 'ingressClassName: traefik' "$A" || die "appliance: the Ingress is not k3s's Traefik"
grep -A1 -- '- path: /health' "$A" | grep -q 'pathType: Exact' || die "appliance: /health is not published (exact)"
grep -q 'router.middlewares: skanyxx-https-redirect@kubernetescrd,skanyxx-hsts@kubernetescrd' "$A" || die "appliance: no https redirect + HSTS middlewares"
grep -q 'secretName: skanyxx-tls' "$A" || die "appliance: the Ingress has no TLS"
grep -q 'value: "https://skanyxx.example.com"' "$A" || die "appliance: publicUrl is not https://<host>"
netpol skanyxx "$A" | grep -q 'kubernetes.io/metadata.name: kube-system' || die "appliance: Traefik (kube-system) is not admitted"
# Within Secret documents only: the fence NetworkPolicies carry the same annotation and must not mask an unkept Secret.
[[ $(grep -c '^kind: Secret$' "$A") == $(awk 'BEGIN{RS="\n---"} /\nkind: Secret\n/ && /helm.sh\/resource-policy: keep/ {n++} END{print n+0}' "$A") ]] \
  || die "appliance: a Secret is not kept (D139)"

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

# D180-D184: memory's card bodies go to the bundled MinIO (its Secret) or the BYO bucket (its Secret); with neither,
# no object store is configured and bodies stay in Postgres.
grep -A1 'Memory__ObjectStore__Endpoint' "$WORK/default.yaml" | grep -q '"http://skanyxx-minio.skanyxx.svc:9000"' \
  || die "default: Memory__ObjectStore__Endpoint is not the bundled MinIO"
grep -A1 'Memory__ObjectStore__SecretKey' "$WORK/default.yaml" | grep -q 'name: skanyxx-minio, key: secret-key' \
  || die "default: the object store's secret key does not come from the MinIO Secret"
grep -A1 'Memory__ObjectStore__Endpoint' "$WORK/byo-values.yaml" | grep -q '"https://s3.eu-west-1.amazonaws.com"' \
  || die "BYO: Memory__ObjectStore__Endpoint is not external.objectStore.endpoint"
grep -A1 'Memory__ObjectStore__AccessKey' "$WORK/byo-values.yaml" | grep -q 'name: skanyxx-s3, key: access-key' \
  || die "BYO: the object store's access key does not come from external.objectStore.existingSecret"
helm template skanyxx "$CHART" --namespace skanyxx --set minio.enabled=false >"$WORK/nostore.yaml"
if grep -q 'Memory__ObjectStore' "$WORK/nostore.yaml"; then die "no object store, yet Memory__ObjectStore is set"; fi

# A1 (D069/D186): AX is off by default and the chart installs it only with ax.install.
if grep -qE 'name: skanyxx-(ax-|sandbox-)' "$WORK/default.yaml"; then die "the default values render AX"; fi
if grep -qE 'name: skanyxx-(ax-|sandbox-)' "$WORK/ax-values.yaml"; then die "ax.enabled without ax.install renders AX or sandbox policies"; fi
AXI=$WORK/ax-install-values.yaml
grep -q 'value: "http://skanyxx-ax-server.skanyxx.svc:8080"' "$AXI" || die "ax-install: Sandboxes__Address is not the in-chart ax-server"
grep -q -- '--redis-addr=dragonfly.skanyxx.svc:6379' "$AXI" || die "ax-install: AX does not use the bundled Dragonfly (D185)"
[[ $(grep -c 'name: REDIS_PASSWORD' "$AXI") == 2 ]] || die "ax-install: ax-server and ax-controller must both get the Dragonfly password"
grep -q 'value: "gs://acme-ax-snapshots/ax/"' "$AXI" || die "ax-install: AX_SNAPSHOTS_BUCKET is not the value (D187)"
if grep -q 'dberkov' "$AXI"; then die "ax-install: upstream's personal snapshot bucket is rendered"; fi
# A2 (D072): ax-server answers Skanyxx only and sends only to Dragonfly; ax-controller answers nobody.
axs=$(netpol skanyxx-ax-server "$AXI")
[[ -n $axs ]] || die "ax-install: no ax-server NetworkPolicy"
[[ $(sed -n '/ingress:/,/egress:/p' <<<"$axs" | grep -c 'app.kubernetes.io/name:') == 1 ]] || die "ax-server admits more than one peer"
sed -n '/ingress:/,/egress:/p' <<<"$axs" | grep -q 'app.kubernetes.io/name: skanyxx' || die "ax-server does not admit Skanyxx"
if grep -q 'podSelector: {}\|namespaceSelector' <<<"$axs"; then die "ax-server admits a whole namespace"; fi
sed -n '/egress:/,$p' <<<"$axs" | grep -q 'app.kubernetes.io/name: dragonfly' || die "ax-server egress is not Dragonfly only"
axc=$(netpol skanyxx-ax-controller "$AXI")
if grep -q 'ingress:' <<<"$axc"; then die "ax-controller admits ingress"; fi
grep -A2 'namespaceSelector' <<<"$axc" | grep -q 'kubernetes.io/metadata.name: ate-system' || die "ax-controller cannot reach Substrate's namespace"
netpol dragonfly "$AXI" | grep -q 'app.kubernetes.io/name: ax-controller' || die "Dragonfly does not admit ax-controller"
# A3 (D077, D178): the sandbox fence lands in the WorkerPool's namespace.
deny=$(netpol skanyxx-sandbox-egress-deny "$AXI")
grep -q 'namespace: ax-sandboxes' <<<"$deny" || die "sandbox-egress-deny is not in the WorkerPool's namespace"
[[ $(grep -c -- '- to:' <<<"$deny") == 1 ]] && grep -q 'k8s-app: "kube-dns"' <<<"$deny" || die "sandbox-egress-deny allows more than DNS"
netpol skanyxx-sandbox-egress-internet "$AXI" | grep -A8 'cidr: "0.0.0.0/0"' | grep -q '"169.254.0.0/16"' \
  || die "sandbox internet egress does not exclude cloud metadata"
# Both fence policies outlive `helm uninstall`: sandboxes may still run, and must not get open egress.
for fence in skanyxx-sandbox-egress-deny skanyxx-sandbox-egress-internet; do
  netpol $fence "$AXI" | grep -q 'helm.sh/resource-policy: keep' || die "$fence is not kept on uninstall"
done
# The DNS peer is a value (ax.sandboxes.dnsPeer): OpenShift's DNS pods, and still only them.
helm template skanyxx "$CHART" --namespace skanyxx -f "$CHART/ci/ax-install-values.yaml" --set ax.sandboxes.dnsPeer.namespace=openshift-dns \
  --set 'ax.sandboxes.dnsPeer.podLabels={dns.operator.openshift.io/daemonset-dns=default}' >"$WORK/ax-dns.yaml"
deny=$(netpol skanyxx-sandbox-egress-deny "$WORK/ax-dns.yaml")
grep -q 'kubernetes.io/metadata.name: openshift-dns' <<<"$deny" && grep -q 'dns.operator.openshift.io/daemonset-dns: "default"' <<<"$deny" \
  || die "ax.sandboxes.dnsPeer is not the sandbox DNS peer"
if grep -q 'kube-dns' <<<"$deny"; then die "a custom dnsPeer still admits kube-dns"; fi

# S1 (D077, D190-D194): sandbox memory = a listener of its own (Service + container port 8082, Memory:SandboxMcpPort,
# a signing key from a kept Secret), Sandboxes:MemoryMcpUrl pointing at it, the WorkerPool's namespace admitted to
# that port only, and the sandboxes allowed to send to it and nothing else in the cluster.
svc=$(awk 'BEGIN{RS="\n---"} /\nkind: Service\n/ && /\n  name: skanyxx-sandbox-mcp\n/ {print}' "$AXI")
[[ -n $svc ]] || die "ax-install: no sandbox-mcp Service"
[[ $(grep -c -- '- name:' <<<"$svc") == 1 ]] && grep -q 'port: 8082' <<<"$svc" && grep -q 'targetPort: sandbox-mcp' <<<"$svc" \
  || die "ax-install: the sandbox-mcp Service exposes more than the sandbox listener"
grep -A1 'name: ASPNETCORE_HTTP_PORTS' "$AXI" | grep -q '"8080;8081;8082"' || die "ax-install: Skanyxx does not listen on the sandbox port"
grep -A1 'name: Memory__SandboxMcpPort' "$AXI" | grep -q '"8082"' || die "ax-install: Memory__SandboxMcpPort is not 8082"
grep -A1 'name: Memory__SandboxTokenKey' "$AXI" | grep -q 'name: skanyxx-sandbox-token, key: token-key' \
  || die "ax-install: the sandbox token key does not come from its Secret"
grep -A1 'name: Sandboxes__MemoryMcpUrl' "$AXI" | grep -q '"http://skanyxx-sandbox-mcp.skanyxx.svc:8082/mcp/memory"' \
  || die "ax-install: Sandboxes__MemoryMcpUrl is not the sandbox listener"
grep -A1 'name: AllowedHosts' "$AXI" | grep -q 'skanyxx-sandbox-mcp.skanyxx.svc' || die "ax-install: AllowedHosts misses the sandbox-mcp Service"
sk=$(netpol skanyxx "$AXI")
grep -A6 'kubernetes.io/metadata.name: ax-sandboxes' <<<"$sk" | grep -q -- '- port: sandbox-mcp' \
  || die "ax-install: Skanyxx does not admit the WorkerPool's namespace on the sandbox port"
if grep -A6 'kubernetes.io/metadata.name: ax-sandboxes' <<<"$sk" | grep -qE -- '- port: (http|mcp)$'; then die "ax-install: sandboxes are admitted to another Skanyxx port"; fi
mem=$(netpol skanyxx-sandbox-egress-memory "$AXI")
grep -q 'namespace: ax-sandboxes' <<<"$mem" || die "sandbox-egress-memory is not in the WorkerPool's namespace"
[[ $(grep -c -- '- to:' <<<"$mem") == 1 && $(grep -c 'port:' <<<"$mem") == 1 ]] && grep -q 'port: 8082}' <<<"$mem" \
  && grep -q 'app.kubernetes.io/name: skanyxx$' <<<"$mem" || die "sandbox-egress-memory allows more than Skanyxx's sandbox port"
if grep -A4 'service:' "$AXI" | grep -q 'number: 8082'; then die "an Ingress targets the sandbox port"; fi
if grep -qE 'sandbox-mcp|Memory__Sandbox|Sandboxes__MemoryMcpUrl|sandbox-egress-memory' "$WORK/ax-values.yaml"; then
  die "ax without sandboxes.memory renders the sandbox listener"
fi

# A4 (D189): with Dragonfly off AX uses external.redis, else its own Redis, fenced to ax-server + ax-controller.
AXV=(-f "$CHART/ci/ax-install-values.yaml")
helm template skanyxx "$CHART" --namespace skanyxx "${AXV[@]}" --set dragonfly.enabled=false >"$WORK/ax-redis.yaml"
kc "$WORK/ax-redis.yaml"
[[ $(grep -c -- '--redis-addr=skanyxx-ax-redis.skanyxx.svc:6379' "$WORK/ax-redis.yaml") == 2 ]] || die "ax-redis: AX does not use its own Redis"
[[ $(grep -A3 'name: REDIS_PASSWORD' "$WORK/ax-redis.yaml" | grep -c 'name: skanyxx-ax-redis$') == 3 ]] \
  || die "ax-redis: ax-server, ax-controller and Redis itself must all take the generated password from skanyxx-ax-redis"
grep -q -- '--requirepass, "$(REDIS_PASSWORD)"' "$WORK/ax-redis.yaml" || die "ax-redis: Redis does not require its password"
grep -q -- '--maxmemory, "409mb"' "$WORK/ax-redis.yaml" || die "ax-redis: maxmemory is not 80% of the 512Mi limit"
awk 'BEGIN{RS="\n---"} /\nkind: Secret\n/ && /\n  name: skanyxx-ax-redis\n/' "$WORK/ax-redis.yaml" | grep -q 'helm.sh/resource-policy: keep' \
  || die "ax-redis: its password Secret is not kept"
grep -q 'redis:7.4-alpine@sha256:' "$WORK/ax-redis.yaml" || die "ax-redis: the Redis image is not pinned by digest"
axr=$(netpol skanyxx-ax-redis "$WORK/ax-redis.yaml")
[[ $(grep -c 'app.kubernetes.io/name:' <<<"$axr") == 3 ]] && grep -q 'name: ax-controller' <<<"$axr" && grep -q 'name: ax-server' <<<"$axr" \
  || die "ax-redis admits more than ax-server and ax-controller"
netpol skanyxx-ax-server "$WORK/ax-redis.yaml" | sed -n '/egress:/,$p' | grep -q 'app.kubernetes.io/name: ax-redis' || die "ax-server egress is not ax-redis"
helm template skanyxx "$CHART" --namespace skanyxx "${AXV[@]}" --set dragonfly.enabled=false \
  --set external.redis.address=redis.example.com:6380 --set external.redis.existingSecret=ax-redis-auth >"$WORK/ax-ext.yaml"
if grep -q 'skanyxx-ax-redis' "$WORK/ax-ext.yaml"; then die "external.redis set, yet ax-redis is rendered"; fi
[[ $(grep -c -- '--redis-addr=redis.example.com:6380' "$WORK/ax-ext.yaml") == 2 && $(grep -c 'name: ax-redis-auth' "$WORK/ax-ext.yaml") == 2 ]] \
  || die "ax-ext: AX does not use external.redis with its password"
netpol skanyxx-ax-controller "$WORK/ax-ext.yaml" | grep -A3 -- '- ports:' | grep -q 'port: 6380' || die "ax-controller egress misses the external Redis port"
# Each of these must be refused by the chart (template fail or values.schema.json), FOR the expected reason: the
# second argument is a substring of the error (a schema path or the fail text), so a case refused by something
# unrelated (a typo in the ci values, another check) fails the script instead of counting.
refusals=0
refuse() {
  local why=$1 expect=$2; shift 2
  if helm template skanyxx "$CHART" --namespace skanyxx "$@" >"$WORK/refused.txt" 2>&1; then die "accepted: $why"; fi
  grep -qF -- "$expect" "$WORK/refused.txt" || { cat "$WORK/refused.txt"; die "refused, but not with '$expect': $why"; }
  refusals=$((refusals + 1))
  echo "refused: $why ($expect)"
}
ING=(--set ingress.enabled=true --set 'ingress.hosts[0]=x.example.com')
refuse "an /MCP ingress path" "ingress path /MCP would publish" "${ING[@]}" --set 'ingress.prefixPaths[0]=/MCP'
refuse "/ as an ingress prefix (routes everything, /mcp included)" 'ingress prefix path "/":' "${ING[@]}" --set 'ingress.prefixPaths[0]=/'
refuse "a regex ingress path" 'ingress prefix path "/.*"' "${ING[@]}" --set 'ingress.prefixPaths[0]=/.*'
refuse "a %-escaped ingress path" 'ingress prefix path "/%6dcp"' "${ING[@]}" --set 'ingress.prefixPaths[0]=/%6dcp'
refuse "a dot-segment ingress path" 'ingress prefix path "/.."' "${ING[@]}" --set 'ingress.prefixPaths[0]=/..'
refuse "the nginx use-regex annotation" "ingress annotation nginx.ingress.kubernetes.io/use-regex" "${ING[@]}" --set 'ingress.annotations.nginx\.ingress\.kubernetes\.io/use-regex=true'
refuse "an nginx configuration snippet" "ingress annotation nginx.ingress.kubernetes.io/server-snippet" "${ING[@]}" --set 'ingress.annotations.nginx\.ingress\.kubernetes\.io/server-snippet=x'
refuse "a typo'd key (postgres.enabled)" "additional properties 'postgres' not allowed" --set postgres.enabled=false
refuse "a string where a boolean belongs" "at '/ingress/enabled': got string, want boolean" --set ingress.enabled=yes
refuse "kagent.providers set (Helm would own default-model-config)" "at '/kagent/providers'" --set kagent.providers.openAI.apiKey=x
refuse "kagent RBAC outside the release namespace" 'does not include the install namespace "other"' --namespace other
refuse "postgresql.existingSecret without the connection-string Secret" "needs skanyxx.database.existingSecret" --set postgresql.existingSecret=pg
refuse "an image digest that is not sha256" "at '/skanyxx/image/digest'" --set skanyxx.image.digest=latest
refuse "service.port == service.mcpPort (one Service port twice)" "skanyxx.service.port and skanyxx.service.mcpPort must differ" --set skanyxx.service.port=8081
refuse "service.mcpPort == 8080 (the UI/API container port)" "skanyxx.service.mcpPort must not be 8080" --set skanyxx.service.mcpPort=8080
refuse "modelEgress.ports null (would open every port)" "at '/networkPolicy/modelEgress': missing property 'ports'" --set networkPolicy.modelEgress.ports=null
refuse "modelEgress.ports empty (would open every port)" "at '/networkPolicy/modelEgress/ports'" --set-json 'networkPolicy.modelEgress.ports=[]'
refuse "a narrower modelEgress cidr (belongs in extra)" "at '/networkPolicy/modelEgress/cidrs/0'" --set 'networkPolicy.modelEgress.cidrs={203.0.113.0/24}'
refuse "a BYO object store endpoint without a bucket" "external.objectStore.endpoint needs external.objectStore.bucket" --set minio.enabled=false --set external.objectStore.endpoint=https://s3.example.com --set external.objectStore.existingSecret=s3
refuse "a BYO object store endpoint without a Secret" "external.objectStore.endpoint needs external.objectStore.bucket" --set minio.enabled=false --set external.objectStore.endpoint=https://s3.example.com --set external.objectStore.bucket=cards
refuse "a BYO object store endpoint without http(s)://" "at '/external/objectStore/endpoint'" --set minio.enabled=false --set external.objectStore.endpoint=s3.example.com --set external.objectStore.bucket=cards --set external.objectStore.existingSecret=s3
refuse "a BYO object store endpoint while the bundled MinIO is on" "external.objectStore.endpoint is set while minio.enabled=true" --set external.objectStore.endpoint=https://s3.example.com --set external.objectStore.bucket=cards --set external.objectStore.existingSecret=s3
refuse "an invalid bucket name" "at '/minio/bucket'" --set minio.bucket=Not_A_Bucket
refuse "a body threshold above the Postgres column (20000)" "at '/skanyxx/memoryBodies/thresholdBytes'" --set skanyxx.memoryBodies.thresholdBytes=20001
refuse "maxBodyBytes not above thresholdBytes" "skanyxx.memoryBodies.maxBodyBytes must be above thresholdBytes" --set skanyxx.memoryBodies.maxBodyBytes=4096
refuse "maxBodyBytes above 8 MiB (the upsert buffers ~6x it per request)" "at '/skanyxx/memoryBodies/maxBodyBytes'" --set skanyxx.memoryBodies.maxBodyBytes=8388609
refuse "ax.install without ax.enabled" "at '/ax/enabled': value must be true" "${AXV[@]}" --set ax.enabled=false
refuse "ax.install without a Substrate endpoint" "at '/ax/substrate/endpoint'" "${AXV[@]}" --set ax.substrate.endpoint=
refuse "ax.install without an ax-server image" "at '/ax/server/image/repository'" "${AXV[@]}" --set ax.server.image.repository=
refuse "ax.install without an ax-controller image" "at '/ax/controller/image/repository'" "${AXV[@]}" --set ax.controller.image.repository=
refuse "ax.install with an ax-server image by tag only (D075: pin by digest)" "at '/ax/server/image/digest'" "${AXV[@]}" --set ax.server.image.tag=v0.3.1 --set ax.server.image.digest=
refuse "ax.install with an ax-controller image by tag only" "at '/ax/controller/image/digest'" "${AXV[@]}" --set ax.controller.image.tag=v0.3.1 --set ax.controller.image.digest=
refuse "AX's own Redis image with neither tag nor digest" "ax.redis.image needs a digest (or at least a tag)" "${AXV[@]}" --set dragonfly.enabled=false --set ax.redis.image.tag= --set ax.redis.image.digest=
refuse "an AX image digest that is not sha256" "at '/ax/controller/image/digest'" "${AXV[@]}" --set ax.controller.image.digest=latest
refuse "AX's own Redis without a memory limit in Mi/Gi (no maxmemory)" "ax.redis.resources.limits.memory must be set in Mi or Gi" "${AXV[@]}" --set dragonfly.enabled=false --set ax.redis.resources.limits.memory=1G
refuse "external.redis.address without a port (would render port: 0)" "at '/external/redis/address'" "${AXV[@]}" --set dragonfly.enabled=false --set external.redis.address=redis.example.com
refuse "ax.install without a snapshot bucket" "at '/ax/snapshotsBucket'" "${AXV[@]}" --set ax.snapshotsBucket=
refuse "upstream's personal snapshot bucket" "at '/ax/snapshotsBucket': 'not' failed" "${AXV[@]}" --set ax.snapshotsBucket=gs://dberkov-gke-dev3/ate-env/
refuse "ax.install with ax.address (the module would point elsewhere)" "at '/ax/address'" "${AXV[@]}" --set ax.address=http://ax.example:8080
refuse "ax.install with networkPolicy.enabled=false (ax-server has no auth)" "at '/networkPolicy/enabled'" "${AXV[@]}" --set networkPolicy.enabled=false
refuse "a sandbox DNS peer with no pod labels (would admit every pod there)" "at '/ax/sandboxes/dnsPeer/podLabels'" "${AXV[@]}" --set-json 'ax.sandboxes.dnsPeer.podLabels=[]'
refuse "networkIsolationConfirmed without the sandbox policies" "at '/ax/sandboxes/namespace'" -f "$CHART/ci/ax-values.yaml" --set ax.networkIsolationConfirmed=true
refuse "networkIsolationConfirmed with networkPolicy.enabled=false" "at '/networkPolicy/enabled'" -f "$CHART/ci/ax-values.yaml" --set ax.networkIsolationConfirmed=true \
  --set ax.sandboxes.namespace=ax-sandboxes --set networkPolicy.enabled=false
refuse "ax.sandboxes.namespace = the release namespace" "ax.sandboxes.namespace must be the Substrate WorkerPool" "${AXV[@]}" --set ax.sandboxes.namespace=skanyxx
refuse "sandbox memory without ax.enabled" "/ax/enabled': value must be true" --set ax.sandboxes.memory.enabled=true --set ax.sandboxes.namespace=ax-sandboxes
refuse "sandbox memory without the WorkerPool's namespace" "/ax/sandboxes/namespace': minLength" -f "$CHART/ci/ax-values.yaml" --set ax.sandboxes.memory.enabled=true
refuse "sandbox memory with networkPolicy.enabled=false" "/networkPolicy/enabled': value must be true" -f "$CHART/ci/ax-values.yaml" --set ax.sandboxes.memory.enabled=true \
  --set ax.sandboxes.namespace=ax-sandboxes --set networkPolicy.enabled=false
refuse "the sandbox port = 8080 (the UI/API)" "ax.sandboxes.memory.port must differ from 8080" "${AXV[@]}" --set ax.sandboxes.memory.port=8080
refuse "the sandbox port = the agents' mcp port" "ax.sandboxes.memory.port must differ from 8080" "${AXV[@]}" --set ax.sandboxes.memory.port=8081
refuse "a sandbox token lifetime over 720 hours" "tokenHours': maximum: got 721, want 720" "${AXV[@]}" --set ax.sandboxes.memory.tokenHours=721
echo "ok: lint + kubeconform -strict for default, byo, ax, ax-install, appliance; /mcp, ModelConfig, RBAC, keep, default-deny, mcpFrom, agent egress, clusterCidrs, object store and AX (address, Dragonfly/external/own Redis with password + maxmemory, ax-server/controller policies, sandbox fence kept + DNS peer, sandbox memory listener) checks hold; $refusals refusals, each for its expected reason"
