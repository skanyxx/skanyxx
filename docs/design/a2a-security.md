# A2A and kagent authentication (D520-D523)

How Skanyxx, kagent's controller and the agent pods authenticate each other inside one install, what the engine
offers, and what is still open. The pod-level fence is in [`cluster-security.md`](cluster-security.md).

## What the engine and the spec offer

- **kagent 0.10.2** has two auth modes, `unsecure` and `trusted-proxy`, and authorizes nothing (`NoopAuthorizer`).
  - `unsecure` takes the user from `?user_id=`, else `X-User-Id`, else `admin@kagent.dev`.
  - `trusted-proxy` decodes a bearer JWT **without checking its signature**, and ignores `X-User-Id` on user calls.
  - Neither mode can tell a forged caller from a real one. The chart pins `unsecure` (D101).
- **Agent pods** serve A2A on 8080 with no inbound auth. They call the controller on
  `http://<KAGENT_CONTROLLER_NAME>.<ns>:8083` for sessions and tasks. They send a projected `kagent` token there,
  which the controller only decodes.
- **Agents used as tools** are called pod to pod: `http://<agent>.<ns>:8080` (`compiler.go: toolAgentURL`), unless
  kagent's `proxy.url` is set.
- **The A2A spec** lets an agent card declare `securitySchemes`, for example `{type: http, scheme: bearer,
  bearerFormat: JWT}`, so a client knows which credential to send. kagent's generated cards declare none, and its ADK
  clients send none. So the scheme cannot be enforced end to end with kagent's agents as they are. The gate below
  enforces the bearer on the one path Skanyxx uses: the controller's `/api/a2a/{ns}/{name}`.

## The design

```
 Skanyxx ──8084, Bearer HS256 (sub = person)──> kagent-gate ──8083, X-User-Id = sub──> kagent controller ──8080──> agent pods
 agent pods ──8083 (agent paths only)─────────> kagent-gate ─────────────────────────┘          agent ──8080──> declared peer
 agent pods ──8081 /mcp/memory, per-agent bearer secret (D083)──> Skanyxx
```

1. **Gate (D520).**
   - What it is: an Envoy Deployment `kagent-gate` (distroless, pinned by digest). The NetworkPolicy lets only the gate
     reach the controller's 8083.
   - The Service `kagent-gate` exposes two listeners:
     - **8084**, for Skanyxx only;
     - **8083**, for the agent pods only.
2. **Signed per-request token (D521).**
   - Skanyxx signs a JWT for every request bound for kagent's origin. A `DelegatingHandler` sits on every HttpClient,
     so Investigate's and Tickets' own clients are covered too.
   - Algorithm and claims: HS256 with `iss=skanyxx`, `aud=kagent-gate`, `sub`, `iat`, `exp` (default 60 s) and `jti`.
   - `sub` is the user the caller named, read as kagent would read it: `?user_id=`, else `X-User-Id`, else `skanyxx`.
   - Before sending, the handler removes the user's `?user_id=` and `X-User-Id`.
   - On 8084 Envoy refuses (401/403) a request whose token is missing, malformed or wrongly signed, or whose token:
     - uses `alg: none`;
     - has expired or has no `exp`;
     - lives longer than 300 s;
     - names another issuer or audience.
   - Envoy also refuses (400):
     - any query key `user_id` (in any case), or any query key holding a `%` escape: Go decodes `user%5Fid` to
       `user_id`;
     - header names with `_`;
     - escaped slashes.
   - It drops `X-User-Id` and `X-Agent-Name`, sets `X-User-Id` from `sub`, and never forwards the token. Only
     `/health` is anonymous.
3. **Agents on an allow-list (D522).**
   - The umbrella sets `KAGENT_CONTROLLER_NAME=kagent-gate` on the controller. It is the only input to the agents'
     `KAGENT_URL`, and kagent uses it nowhere else (checked in 0.10.2's source). The agents therefore call the gate's
     8083.
   - That listener forwards `kagentGate.agentPaths`. The default is `/api/sessions` and `/api/tasks`. The schema enum
     also allows `/api/memories`, `/api/langgraph`, `/api/crewai` and `/api/a2a-sandboxes`.
   - Everything else is 403 there: agent CRUD, ModelConfigs, tool servers, and A2A to any agent through the
     controller. Paths are normalised first (dot segments, doubled slashes; escaped slashes are refused).
4. **A2A peers (D523).**
   - Agent-to-agent traffic is pod to pod, so it is fenced at L3/L4. It is refused unless
     `networkPolicy.a2aPeers` lists it (`{from, to: [...]}`).
   - Each entry renders an egress policy for the caller and an ingress policy for the callees, on
     `controllerEgress.a2aPorts`.
   - The identity is the pod label `kagent=<agent>`, which kagent sets from the Agent CR. Agent CRs are written only
     by Skanyxx, through the gate, and by kagent itself.
5. **Memory MCP:** unchanged. Each agent has its own bearer secret on the mcp port (D083), and `X-User-Id` is read only
   with `actsForUsers` (D084). Sandboxes use a signed token on their own listener (D190).
6. **Secrets:** the signing key exists only in the Kubernetes Secret `kagent-gate`. It holds `token-key` (Skanyxx's env
   `KAgent__Auth__TokenKey`) and `jwks.json` (mounted into Envoy). The key is generated once and kept (D139), and it
   never reaches a log, an API response or the UI.

## How it's configured in Helm

On by default. A values example with every knob:

```yaml
kagentGate:
  enabled: true                       # false: Skanyxx and the agents call the controller directly (NOTES warns)
  replicas: 1
  image:
    repository: docker.io/envoyproxy/envoy
    tag: distroless-v1.40.2
    digest: sha256:09ca6f2140a61ad8df11779a4a30d52b48433d444466328494f9188c9c35eded
    pullPolicy: IfNotPresent
  tokenSeconds: 60                    # KAgent:Auth:TokenSeconds, 10-300
  existingSecret: ""                  # GitOps: a Secret with token-key + jwks.json (format in values.yaml)
  agentPaths: [/api/sessions, /api/tasks]
  resources: {requests: {cpu: 20m, memory: 32Mi}, limits: {memory: 128Mi}}

networkPolicy:
  a2aPeers:                           # agents used as tools: caller -> callees, Agent names in the release namespace
    - {from: ticket-planner, to: [ticket-reviewer, ticket-qa]}

kagent:
  controller:
    env:                              # required; add your own entries next to it
      - {name: KAGENT_CONTROLLER_NAME, value: kagent-gate}
```

What the chart renders and refuses:

- **Renders:**
  - the `kagent-gate` Deployment, ConfigMap (the Envoy config), Service, Secret and NetworkPolicy;
  - the Skanyxx env `KAgent__IngressUrl=http://kagent-gate.<ns>.svc:8084`, `KAgent__Auth__Mode=signed`,
    `KAgent__Auth__TokenSeconds` and `KAgent__Auth__TokenKey` (from the Secret);
  - the controller's ingress from the gate only, and the agents' and Skanyxx's egress to the gate only.
- **Refuses:**
  - a `kagent.controller.env` without `KAGENT_CONTROLLER_NAME=kagent-gate`;
  - an `agentPaths` entry outside the enum (`/api/agents`, `/api/a2a`, `/`);
  - a `tokenSeconds` over 300;
  - a non-sha256 digest;
  - an `a2aPeers` entry that is not a DNS label or has no callee.
- **Off** (`kagentGate.enabled=false`): the `kagent-gate` Service selects the controller, so the agents keep working,
  and Skanyxx sends no token. NOTES warns. With `networkPolicy.enabled=false` NOTES warns that the gate can be
  bypassed.
- **BYO kagent** (`kagent.enabled=false`): no gate is rendered. Fence your own controller.

## Local development

A Host outside the cluster uses `KAgent:Auth:Mode=none` (the default, explicit in `appsettings.template.json`). It
talks to an unsecured kagent on `localhost:8083` exactly as before. `Mode=signed` without a 32+ character
`KAgent:Auth:TokenKey`, an unknown mode, or `TokenSeconds` outside 10-300 stops startup.

## Tests

| What | Where |
|---|---|
| Token claims, signature and lifetime; every kagent call signed for its user, with no `user_id` or `X-User-Id`; a module's own client signed from its `X-User-Id`; another origin untouched; mode none unchanged; bad settings stop startup | `tests/Skanyxx.Host.Tests/KAgentTokenTests.cs` |
| The rendered Envoy config in Docker against an echo upstream: 33 cases, including the forged and encoded `user_id` and the agent-path escapes | `scripts/helm/gate-test.sh` |
| Render checks (gate, env, JWKS = key, Envoy rules, policies, off switch, peers) and 9 refusals | `scripts/helm/validate.sh` |
| The chart's policies and gate on a throwaway kind cluster: 17 who-may-call-whom checks | `scripts/helm/gate-kind-test.sh` |

## Still open

- **The agents' listener authenticates nobody.** Any agent pod can use `/api/sessions` and `/api/tasks` with any
  `X-User-Id`, and so read or append to another user's session by id.
  - Why it stays open: agents send only kagent's projected token, and verifying it needs the cluster's JWKS (API
    server access) or kagent authz.
  - What would close it: Envoy `jwt_authn` with the service-account issuer, or a mesh.
- **Agent pods share one ServiceAccount** (`kagent-agent`), so even a verified kagent token would not say *which*
  agent is calling. Per-agent identity needs a per-agent SA.
- **kagent's controller can read every Secret in the namespace**, including `kagent-gate`'s key, so a compromised
  controller can mint tokens (D136; it already controls every agent).
- **No mTLS.** A token sniffed on the pod network is valid for its remaining lifetime (60 s by default), for its one
  user. A mesh, or kagent TLS, would close this.
- **`a2aPeers` names Agents known at install time.** An agent created later in Studio needs a values change before it
  can be another agent's tool. Skanyxx has no Kubernetes API (by design), so it cannot write NetworkPolicies itself.
- **kagent's UI**, with `kagent.ui.replicas > 0`, is still admitted to the controller directly and authenticates
  nobody. Keep it at 0.
- **One gate replica by default.** If the gate is down, Chat and the agents' session storage stop with it. Raise
  `kagentGate.replicas` for availability.
- **IPv4 listeners** (`0.0.0.0`). An IPv6-only cluster needs the listener address changed.
