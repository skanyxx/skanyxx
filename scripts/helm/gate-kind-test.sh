#!/usr/bin/env bash
# D520-D523 on a live cluster: creates a throwaway kind cluster (kindnet enforces NetworkPolicy), applies the chart's
# NetworkPolicies, kagent gate and Agent admission policy (D528, with kagent's CRDs) as rendered, with echo pods standing in for kagent's controller, two agents and
# Skanyxx (same labels), and walks the who-may-call-whom matrix. Deletes the cluster at the end. Needs kind, kubectl,
# helm, docker, python3. The cluster's kubeconfig is a temp file: the user's kubeconfig and current context are untouched.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
CHART=$ROOT/deploy/helm/skanyxx
CLUSTER=${CLUSTER:-skx-sec}
NS=skanyxx
PY=python:3.13-alpine
WORK=$(mktemp -d)
kind get clusters | grep -qx "$CLUSTER" && { echo "cluster $CLUSTER already exists; delete it or set CLUSTER"; exit 1; }
cleanup() { kind delete cluster --name "$CLUSTER" --kubeconfig "$WORK/kubeconfig" >/dev/null 2>&1 || true; rm -rf "$WORK"; }
trap cleanup EXIT
die() { echo "FAIL: $*"; exit 1; }
export KUBECONFIG=$WORK/kubeconfig
k() { kubectl -n "$NS" "$@"; }

helm dependency build "$CHART" >/dev/null
helm template skanyxx "$CHART" --namespace "$NS" \
  --set-json 'networkPolicy.a2aPeers=[{"from":"agent-a","to":["agent-b"]}]' >"$WORK/render.yaml"
# The chart's policies and gate objects only: no store, no Skanyxx image, no kagent.
awk 'BEGIN{RS="\n---"} /\nkind: (NetworkPolicy|CustomResourceDefinition|ValidatingAdmissionPolicy|ValidatingAdmissionPolicyBinding)\n/ || (/\n  name: skanyxx-kagent-gate\n/ && /\nkind: (ConfigMap|Secret|Deployment|Service)\n/) {sub(/^---/, ""); print "---" $0}' \
  "$WORK/render.yaml" >"$WORK/apply.yaml"
GATE_IMAGE=$(awk 'BEGIN{RS="\n---"} /\nkind: Deployment\n/ && /\n  name: skanyxx-kagent-gate\n/ {print}' "$WORK/render.yaml" | awk '/image: /{gsub(/"/, "", $2); print $2; exit}')
awk 'BEGIN{RS="\n---"} /\nkind: Secret\n/ && /\n  name: skanyxx-kagent-gate\n/ {print}' "$WORK/render.yaml" | awk '/^  token-key: /{print $2}' | base64 -d >"$WORK/token-key"
chmod 600 "$WORK/token-key"

kind create cluster --name "$CLUSTER" --kubeconfig "$WORK/kubeconfig" --wait 120s >/dev/null 2>&1
# The node pulls both images itself (kind load fails on multi-platform indexes).
kubectl create namespace "$NS" >/dev/null

# Each stand-in answers any request on its port with the headers it got (the "controller" on 8083, agents on 8080).
cat >"$WORK/echo.py" <<'EOF'
import json, http.server, sys
class Echo(http.server.BaseHTTPRequestHandler):
    def answer(self):
        body = json.dumps({"path": self.path, "x-user-id": self.headers.get("x-user-id"), "authorization": self.headers.get("authorization")}).encode()
        self.send_response(200); self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)
    do_GET = do_POST = answer
    def log_message(self, *a): pass
http.server.ThreadingHTTPServer(("0.0.0.0", int(sys.argv[1])), Echo).serve_forever()
EOF
k create configmap echo --from-file=echo.py="$WORK/echo.py" >/dev/null
pod() { # pod <name> <port> <labels yaml>
  cat <<EOF
---
apiVersion: v1
kind: Pod
metadata:
  name: $1
  labels: {$3}
spec:
  automountServiceAccountToken: false
  containers:
    - name: echo
      image: $PY
      imagePullPolicy: IfNotPresent
      command: [python, /echo/echo.py, "$2"]
      volumeMounts: [{name: echo, mountPath: /echo}]
  volumes: [{name: echo, configMap: {name: echo}}]
---
apiVersion: v1
kind: Service
metadata:
  name: $1
spec:
  selector: {stand-in: $1}
  ports: [{port: $2}]
EOF
}
{
  pod kagent-controller 8083 '"stand-in": kagent-controller, app.kubernetes.io/name: kagent, app.kubernetes.io/component: controller'
  pod skanyxx 8080 '"stand-in": skanyxx, app.kubernetes.io/name: skanyxx, app.kubernetes.io/instance: skanyxx'
  pod agent-a 8080 '"stand-in": agent-a, app: kagent, kagent: agent-a'
  pod agent-b 8080 '"stand-in": agent-b, app: kagent, kagent: agent-b'
  pod agent-c 8080 '"stand-in": agent-c, app: kagent, kagent: agent-c'
  pod intruder 8080 '"stand-in": intruder'
} >"$WORK/pods.yaml"
k apply -f "$WORK/pods.yaml" >/dev/null
k apply --server-side -f "$WORK/apply.yaml" >/dev/null
k wait --for=condition=Ready pod --all --timeout=180s >/dev/null
k rollout status deployment/skanyxx-kagent-gate --timeout=120s >/dev/null

TOKEN=$(python3 - "$WORK/token-key" <<'EOF'
import base64, hashlib, hmac, json, sys, time
key = open(sys.argv[1], "rb").read(); now = int(time.time())
b = lambda d: base64.urlsafe_b64encode(json.dumps(d, separators=(",", ":")).encode()).rstrip(b"=").decode()
s = b({"alg": "HS256", "typ": "JWT"}) + "." + b({"iss": "skanyxx", "aud": "kagent-gate", "sub": "ana@acme.example", "iat": now, "exp": now + 240})
print(s + "." + base64.urlsafe_b64encode(hmac.new(key, s.encode(), hashlib.sha256).digest()).rstrip(b"=").decode())
EOF
)
# call <from-pod> <url> [bearer] -> the HTTP status, or "blocked" when the connection does not complete in 4 s.
call() {
  k exec "$1" -- python -c '
import sys, urllib.request, urllib.error
req = urllib.request.Request(sys.argv[1], headers={"Authorization": "Bearer " + sys.argv[2]} if sys.argv[2] else {})
try: print(urllib.request.urlopen(req, timeout=4).status)
except urllib.error.HTTPError as e: print(e.code)
except Exception: print("blocked")' "$2" "${3:-}"
}
checks=0
expect() { # expect <want> <why> <from> <url> [bearer]
  local got; got=$(call "$3" "$4" "${5:-}")
  [[ $got == "$1" ]] || die "$2: $got, want $1"
  checks=$((checks + 1)); echo "ok: $2 ($got)"
}
G=http://skanyxx-kagent-gate.$NS.svc
C=http://kagent-controller.$NS.svc:8083
expect 401 "Skanyxx -> gate without a token" skanyxx "$G:8084/api/agents"
expect 200 "Skanyxx -> gate with a token" skanyxx "$G:8084/api/agents" "$TOKEN"
expect blocked "Skanyxx -> controller, past the gate" skanyxx "$C/api/agents"
expect blocked "Skanyxx -> the agents' gate port" skanyxx "$G:8083/api/sessions"
expect 200 "agent -> gate, session storage" agent-a "$G:8083/api/sessions"
expect 403 "agent -> gate, agent CRUD" agent-a "$G:8083/api/agents"
expect blocked "agent -> Skanyxx's gate port" agent-a "$G:8084/api/agents" "$TOKEN"
expect blocked "agent -> controller, past the gate" agent-a "$C/api/agents"
expect 200 "agent-a -> agent-b (a declared a2aPeer)" agent-a "http://agent-b.$NS.svc:8080/"
expect blocked "agent-a -> agent-c (not declared)" agent-a "http://agent-c.$NS.svc:8080/"
expect blocked "agent-b -> agent-a (peers are one-way)" agent-b "http://agent-a.$NS.svc:8080/"
expect 200 "controller -> an agent's A2A port" kagent-controller "http://agent-c.$NS.svc:8080/"
for target in "$G:8084/api/agents" "$G:8083/api/sessions" "$C/api/agents" "http://agent-b.$NS.svc:8080/"; do
  expect blocked "intruder -> $target" intruder "$target" "$TOKEN"
done
k exec skanyxx -- python -c '
import sys, json, urllib.request
r = json.load(urllib.request.urlopen(urllib.request.Request(sys.argv[1], headers={"Authorization": "Bearer " + sys.argv[2], "X-User-Id": "mallory"}), timeout=4))
assert r["x-user-id"] == "ana@acme.example" and not r["authorization"], r' "$G:8084/api/sessions" "$TOKEN" \
  || die "the controller did not see the token's user, or saw the token"
echo "ok: the controller sees the token's sub as X-User-Id and no token"

# D528: the Agent admission policy, by server-side dry run (nothing is created).
agent() { # agent <name> [deployment block, indented 6] [byo]
  local kind=${3:-declarative}
  printf 'apiVersion: kagent.dev/v1alpha2\nkind: Agent\nmetadata: {name: %s, namespace: %s}\nspec:\n' "$1" "$NS"
  if [[ $kind == byo ]]; then printf '  type: BYO\n  byo:\n    deployment:\n      image: example.com/agent:1\n'
  else printf '  type: Declarative\n  description: e2e-test\n  declarative:\n    modelConfig: default-model-config\n    systemMessage: hi\n'
    [[ -n ${2:-} ]] && printf '    deployment:\n'; fi
  [[ -n ${2:-} ]] && printf '%s\n' "$2"
  return 0
}
admit() { # admit <allowed|denied> <why> <yaml>
  local out got
  if out=$(printf '%s' "$3" | k apply --dry-run=server -f - 2>&1); then got=allowed; else got=denied; fi
  [[ $got == "$1" ]] || die "$2: $got, want $1 ($out)"
  [[ $got == allowed ]] || grep -q 'ValidatingAdmissionPolicy' <<<"$out" || die "$2: refused, but not by the policy ($out)"
  checks=$((checks + 1)); echo "ok: agent guard: $2 ($got)"
}
for _ in $(seq 1 30); do
  printf '%s' "$(agent kagent-probe)" | k apply --dry-run=server -f - 2>&1 | grep -q ValidatingAdmissionPolicy && break; sleep 2
done
admit allowed "a plain Agent" "$(agent e2e-test-ok)"
admit allowed "its own labels and the agents' ServiceAccount" "$(agent e2e-test-ok '      labels: {team: payments}
      serviceAccountName: kagent-agent')"
admit denied "named like the gate (review H1)" "$(agent skanyxx-kagent-gate)"
admit denied "named like kagent's controller" "$(agent kagent-controller)"
admit denied "named like Skanyxx" "$(agent skanyxx)"
admit denied "wearing the gate's pod label" "$(agent e2e-test-x '      labels: {app.kubernetes.io/name: skanyxx-kagent-gate}')"
admit denied "wearing Skanyxx's pod label" "$(agent e2e-test-x '      labels: {app.kubernetes.io/name: skanyxx, app.kubernetes.io/instance: skanyxx}')"
admit denied "claiming another agent's peer label" "$(agent e2e-test-x '      labels: {kagent: agent-a}')"
admit denied "running as kagent's controller ServiceAccount" "$(agent e2e-test-x '      serviceAccountName: kagent-controller')"
admit denied "mounting the gate's key" "$(agent e2e-test-x '      volumes: [{name: k, secret: {secretName: skanyxx-kagent-gate}}]')"
admit denied "reading Skanyxx's database Secret" "$(agent e2e-test-x '      env: [{name: DB, valueFrom: {secretKeyRef: {name: skanyxx-skanyxx-db, key: x}}}]')"
admit denied "envFrom the bootstrap Secret" "$(agent e2e-test-x '      envFrom: [{secretRef: {name: skanyxx}}]')"
admit denied "a BYO agent wearing the gate's label" "$(agent e2e-test-x '      labels: {app.kubernetes.io/name: skanyxx-kagent-gate}' byo)"
echo "ok: kagent gate on kind ($CLUSTER, $(kubectl version -o json | python3 -c 'import json,sys; print(json.load(sys.stdin)["serverVersion"]["gitVersion"])')), $((checks + 1)) checks"
