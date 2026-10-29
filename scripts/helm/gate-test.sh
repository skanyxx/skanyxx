#!/usr/bin/env bash
# D520-D522: the kagent gate as the chart renders it, run in Docker in front of an echo server that stands in for kagent's
# controller (it answers every request with the method, path and headers it received). Checks that Skanyxx's listener
# refuses every request without a valid signed token, puts the token's `sub` in X-User-Id whatever the caller sent,
# refuses ?user_id= and never passes the token on, and that the agents' listener forwards only kagentGate.agentPaths.
# Needs helm, docker and python3. Starts and removes its own containers and network (skx-gate-test-*).
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
CHART=$ROOT/deploy/helm/skanyxx
WORK=$(mktemp -d)
NET=skx-gate-test-net
ECHO=skx-gate-test-echo
GATE=skx-gate-test-envoy
cleanup() { docker rm -f "$GATE" "$ECHO" >/dev/null 2>&1 || true; docker network rm "$NET" >/dev/null 2>&1 || true; rm -rf "$WORK"; }
trap cleanup EXIT
die() { echo "FAIL: $*"; docker logs "$GATE" 2>&1 | tail -20; exit 1; }

helm dependency build "$CHART" >/dev/null
helm template skanyxx "$CHART" --namespace skanyxx >"$WORK/render.yaml"
doc() { awk -v k="$1" -v n="$2" 'BEGIN{RS="\n---"} $0 ~ "\nkind: " k "\n" && $0 ~ "\n  name: " n "\n" {print}' "$WORK/render.yaml"; }
doc ConfigMap skanyxx-kagent-gate | sed -n '/envoy.yaml: |/,$p' | sed '1d; s/^    //' \
  | sed "s/kagent-controller.skanyxx.svc/$ECHO/" >"$WORK/envoy.yaml"
doc Secret skanyxx-kagent-gate | awk '/^  jwks.json: /{print $2}' | base64 -d >"$WORK/jwks.json"
doc Secret skanyxx-kagent-gate | awk '/^  token-key: /{print $2}' | base64 -d >"$WORK/token-key"
chmod 600 "$WORK/token-key"
IMAGE=$(doc Deployment skanyxx-kagent-gate | awk '/image: /{gsub(/"/, "", $2); print $2; exit}')
[[ -s $WORK/envoy.yaml && -s $WORK/jwks.json && -s $WORK/token-key && -n $IMAGE ]] || die "the render has no gate"

cat >"$WORK/echo.py" <<'EOF'
import json, http.server
class Echo(http.server.BaseHTTPRequestHandler):
    def answer(self):
        body = json.dumps({"method": self.command, "path": self.path, "headers": {k.lower(): v for k, v in self.headers.items()}}).encode()
        self.send_response(200); self.send_header("Content-Type", "application/json"); self.send_header("Content-Length", str(len(body)))
        self.end_headers(); self.wfile.write(body)
    do_GET = do_POST = do_PUT = do_DELETE = do_PATCH = answer
    def log_message(self, *a): pass
http.server.ThreadingHTTPServer(("0.0.0.0", 8083), Echo).serve_forever()
EOF
chmod 644 "$WORK/echo.py" "$WORK/envoy.yaml" "$WORK/jwks.json"
docker run --rm -v "$WORK/envoy.yaml:/etc/envoy/envoy.yaml:ro" -v "$WORK/jwks.json:/etc/skanyxx-kagent-gate/jwks.json:ro" "$IMAGE" \
  --mode validate -c /etc/envoy/envoy.yaml >"$WORK/validate.txt" 2>&1 || { cat "$WORK/validate.txt"; die "Envoy refuses the rendered config"; }
docker network create "$NET" >/dev/null
docker run -d --name "$ECHO" --network "$NET" -v "$WORK/echo.py:/echo.py:ro" python:3.13-alpine python /echo.py >/dev/null
docker run -d --name "$GATE" --network "$NET" --read-only --cap-drop ALL --security-opt no-new-privileges -p 127.0.0.1::8083 -p 127.0.0.1::8084 \
  -v "$WORK/envoy.yaml:/etc/envoy/envoy.yaml:ro" -v "$WORK/jwks.json:/etc/skanyxx-kagent-gate/jwks.json:ro" "$IMAGE" \
  -c /etc/envoy/envoy.yaml --log-level warn >/dev/null
AGENTS=http://$(docker port "$GATE" 8083 | head -1)
SKANYXX=http://$(docker port "$GATE" 8084 | head -1)
for _ in $(seq 1 50); do curl -fsS "$SKANYXX/health" >/dev/null 2>&1 && break; sleep 0.2; done
curl -fsS "$SKANYXX/health" >/dev/null || die "the gate did not start"

# A token: claims as JSON, signed with the key file (or "none", or another key). Python so no JWT tool is needed.
token() {
  python3 - "$WORK/token-key" "$1" "${2:-HS256}" <<'EOF'
import base64, hashlib, hmac, json, sys, time
key = open(sys.argv[1], "rb").read()
claims, alg = json.loads(sys.argv[2]), sys.argv[3]
now = int(time.time())
for k, v in list(claims.items()):
    if isinstance(v, str) and v.startswith("now"):
        claims[k] = now + int(v[3:] or 0)
b = lambda d: base64.urlsafe_b64encode(json.dumps(d, separators=(",", ":")).encode()).rstrip(b"=").decode()
head = b({"alg": "none" if alg == "none" else "HS256", "typ": "JWT"})
body = b(claims)
signing = key if alg == "HS256" else b"another-key-another-key-another-key-another-key"
sig = "" if alg == "none" else base64.urlsafe_b64encode(hmac.new(signing, f"{head}.{body}".encode(), hashlib.sha256).digest()).rstrip(b"=").decode()
print(f"{head}.{body}.{sig}")
EOF
}
GOOD='{"iss":"skanyxx","aud":"kagent-gate","sub":"ana@acme.example","iat":"now","exp":"now+60"}'
checks=0
expect() { # expect <status> <why> curl-args...
  local want=$1 why=$2; shift 2
  local got; got=$(curl -sS -o "$WORK/body" -w '%{http_code}' "$@")
  [[ $got == "$want" ]] || { cat "$WORK/body"; die "$why: HTTP $got, want $want"; }
  checks=$((checks + 1)); echo "ok: $why ($got)"
}
header() { python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["headers"].get(sys.argv[2], ""))' "$WORK/body" "$1"; }

# Skanyxx's listener (8084).
expect 401 "no token" "$SKANYXX/api/agents"
expect 401 "not a JWT" -H 'Authorization: Bearer nonsense' "$SKANYXX/api/agents"
expect 401 "signed with another key" -H "Authorization: Bearer $(token "$GOOD" other)" "$SKANYXX/api/agents"
expect 401 "alg none" -H "Authorization: Bearer $(token "$GOOD" none)" "$SKANYXX/api/agents"
expect 401 "expired" -H "Authorization: Bearer $(token '{"iss":"skanyxx","aud":"kagent-gate","sub":"a","iat":"now-400","exp":"now-120"}')" "$SKANYXX/api/agents"
expect 401 "no expiry" -H "Authorization: Bearer $(token '{"iss":"skanyxx","aud":"kagent-gate","sub":"a","iat":"now"}')" "$SKANYXX/api/agents"
expect 401 "lives longer than 300 s" -H "Authorization: Bearer $(token '{"iss":"skanyxx","aud":"kagent-gate","sub":"a","iat":"now","exp":"now+3600"}')" "$SKANYXX/api/agents"
expect 403 "another audience" -H "Authorization: Bearer $(token '{"iss":"skanyxx","aud":"memory","sub":"a","iat":"now","exp":"now+60"}')" "$SKANYXX/api/agents"
expect 401 "another issuer" -H "Authorization: Bearer $(token '{"iss":"kagent","aud":"kagent-gate","sub":"a","iat":"now","exp":"now+60"}')" "$SKANYXX/api/agents"
expect 401 "a valid token in ?access_token= (header only)" "$SKANYXX/api/agents?access_token=$(token "$GOOD")"
expect 401 "a token in X-User-Id alone (no bearer)" -H 'X-User-Id: ana@acme.example' "$SKANYXX/api/sessions"
expect 200 "kagent's /health without a token" "$SKANYXX/health"
expect 200 "a valid token" -H "Authorization: Bearer $(token "$GOOD")" -H 'X-User-Id: mallory@acme.example' -H 'X-Agent-Name: forged' "$SKANYXX/api/agents"
[[ $(header x-user-id) == ana@acme.example ]] || die "X-User-Id is not the token's sub: $(header x-user-id)"
[[ -z $(header authorization) ]] || die "the token is passed on to kagent"
[[ -z $(header x-agent-name) ]] || die "X-Agent-Name is passed on from Skanyxx's listener"
expect 200 "a chat turn (A2A) with a valid token" -X POST -H "Authorization: Bearer $(token "$GOOD")" -d '{}' "$SKANYXX/api/a2a/skanyxx/seed/"
[[ $(header x-user-id) == ana@acme.example ]] || die "A2A: X-User-Id is not the token's sub"
expect 400 "?user_id= (kagent reads it before X-User-Id)" -H "Authorization: Bearer $(token "$GOOD")" "$SKANYXX/api/sessions?user_id=mallory"
expect 400 "?user%5Fid= (Go decodes the key to user_id)" -H "Authorization: Bearer $(token "$GOOD")" "$SKANYXX/api/sessions?x=1&user%5Fid=mallory"
expect 400 "?user_id without a value" -H "Authorization: Bearer $(token "$GOOD")" "$SKANYXX/api/sessions?user_id"
expect 400 "?%75ser_id= (any escape in a query key)" -H "Authorization: Bearer $(token "$GOOD")" "$SKANYXX/api/sessions?%75ser_id=mallory"
expect 400 "?USER_ID=" -H "Authorization: Bearer $(token "$GOOD")" "$SKANYXX/api/sessions?USER_ID=mallory"
expect 200 "an escape in a query value is fine" -H "Authorization: Bearer $(token "$GOOD")" "$SKANYXX/api/sessions?name=a%20b&other_user_id=x"
expect 400 "a header name with an underscore" -H "Authorization: Bearer $(token "$GOOD")" -H 'X_User_Id: mallory' "$SKANYXX/api/sessions"

# The agents' listener (8083).
expect 200 "agent: session storage" -H 'X-User-Id: ana@acme.example' "$AGENTS/api/sessions/s1/events"
[[ $(header x-user-id) == ana@acme.example ]] || die "agent listener: X-User-Id not passed on"
expect 200 "agent: create a session" -X POST -d '{}' "$AGENTS/api/sessions"
expect 200 "agent: task storage" "$AGENTS/api/tasks/t1"
expect 200 "agent: /health" "$AGENTS/health"
expect 403 "agent: list agents" "$AGENTS/api/agents"
expect 403 "agent: create an agent" -X POST -d '{}' "$AGENTS/api/agents"
expect 403 "agent: ModelConfigs" "$AGENTS/api/modelconfigs"
expect 403 "agent: tool servers" "$AGENTS/api/toolservers"
expect 403 "agent: A2A to any agent through the controller" -X POST -d '{}' "$AGENTS/api/a2a/skanyxx/seed/"
expect 403 "agent: a prefix look-alike" "$AGENTS/api/sessionsX"
expect 403 "agent: a dot-segment out of an agent path" --path-as-is "$AGENTS/api/sessions/../agents"
expect 403 "agent: doubled slashes" --path-as-is "$AGENTS/api//agents"
expect 400 "agent: an escaped slash" --path-as-is "$AGENTS/api/sessions%2F..%2Fagents"
echo "ok: kagent gate ($IMAGE), $checks checks"
