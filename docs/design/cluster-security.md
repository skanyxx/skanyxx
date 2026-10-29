# Cluster security (umbrella chart)

Who may talk to whom inside one install (`deploy/helm/skanyxx`), with what credential, and what we cannot enforce yet.
The kagent gate and A2A authentication are in [`a2a-security.md`](a2a-security.md) (D520-D523).
Checked by `scripts/helm/validate.sh` (C1–C6, plus D137/D138/D148/D188/D194 checks). Sources:
`~/Desktop/ai/active/2026-10-26_2330_skanyxx-phase1-cleanup-rbac/research/kagent-a2a-auth.md`.

## Trust boundaries

```
 browser ──https──> ingress controller ──8080 (allow-listed paths, never /mcp)──> Skanyxx ──5432/9000/3000/6379──> stores
 Skanyxx ──8084, signed HS256 token per request──> kagent-gate ──8083, X-User-Id = token sub──> kagent controller
 kagent controller ──8080 A2A (no auth)──> agent pods
 agent pods ──8083, agent paths only (sessions, tasks)──> kagent-gate
 agent pods ──8080 A2A──> declared a2aPeers only
 agent pods ──8081 /mcp/memory (agent secret)──> Skanyxx;   agent pods ──443 minus clusterCidrs──> model provider
 Skanyxx ── grpc 8080 (no auth) ──> ax-server ─> Redis/Dragonfly;  ax-controller ── TLS + SA token ──> Substrate
 sandboxes (WorkerPool ns) ── 8082 /mcp/memory (signed sandbox token) ──> Skanyxx; DNS; internet only if opted in
```

- **Real authentication** happens in three places:
  - Skanyxx itself: cookies/OIDC on 8080.
  - `/mcp/memory`: an agent secret on 8081 (D083), or a signed sandbox token on 8082 (D190).
  - The kagent gate's 8084: a token Skanyxx signs per request (D521). The gate names the user from it.
- **Everything else is fenced by NetworkPolicy:** the kagent controller (only the gate reaches it), the gate's agent
  port (agent pods, agent paths only), agent A2A, ax-server and the stores. The namespace is default-deny (D137).

## Who talks to whom

| From | To | Port | Credential |
|---|---|---|---|
| Ingress controller | Skanyxx `http` | 8080 | User session (cookie / OIDC) |
| Skanyxx | kagent gate | 8084 | HS256 token per request (`sub` = the person, 60 s); the gate sets `X-User-Id` from it (D521) |
| kagent gate | kagent controller | 8083 | None past the gate; `X-User-Id` = the token's `sub`, or the agent's own header |
| kagent controller | Agent pods (A2A) | 8080 | None; the controller forwards `X-User-Id` |
| Agent pods | kagent gate (agent port) | 8083 | None checked; `kagentGate.agentPaths` only (D522); projected `kagent` token passed on, not verified |
| Agent pods | Agent pods (`a2aPeers`) | 8080 | None; NetworkPolicy by the `kagent=<agent>` label (D523) |
| Agent pods, kagent controller | Skanyxx `mcp` | 8081 | Per-agent bearer secret; `X-User-Id` only with `actsForUsers` |
| Sandboxes (WorkerPool ns) | Skanyxx `sandbox-mcp` | 8082 | Signed sandbox token only, no headers read |
| Skanyxx | Postgres / MinIO / Gitea / Dragonfly | 5432 / 9000 / 3000 / 6379 | Per-role password / access key / admin token / password |
| Skanyxx | ax-server | 8080 gRPC | None (AX #376) |
| ax-controller | Substrate | 443 | TLS + projected SA token (audience `api.ate-system.svc`) |
| kagent controller | Postgres | 5432 | Own role (`kagent-postgres-url`) |

## ServiceAccounts and RBAC

| Workload | SA | API token | RBAC |
|---|---|---|---|
| Skanyxx | `<release>-skanyxx` | none | none: it uses kagent's HTTP API, never the Kubernetes API |
| Postgres, MinIO, Gitea, AX's Redis | namespace `default` | none (pod-level off) | none |
| Dragonfly | `dragonfly` (umbrella's own) | none (SA-level off) | none |
| ax-server, ax-controller | own SAs | none; ax-controller gets a projected Substrate-audience token only | none (no ClusterRole, D186) |
| kagent controller | `kagent-controller` | yes (it reconciles) | Role + RoleBinding getter/writer in the release namespace only (D136); still every Secret there |
| kagent agent pods | `kagent-agent` (umbrella's, via `controller.agentDeployment.serviceAccountName`) | none; kagent's own `kagent-token` projected volume stays | none; an Agent CR may name another SA |
| kagent UI | `kagent-ui` | chart default | 0 replicas |

Every umbrella workload runs with the same pod security baseline:
- runAsNonRoot and seccomp `RuntimeDefault`
- no privilege escalation
- read-only root filesystem
- all capabilities dropped

kagent's own pods get the same baseline from its global defaults.

## NetworkPolicy matrix (bundled install)

| Pod | Ingress from | Egress to |
|---|---|---|
| Skanyxx | `http`: `ingressNamespaces` (+`ingressPodLabels`) while ingress is on, `httpFrom`<br>`mcp`: kagent controller, agent pods, `mcpFrom`<br>`sandbox-mcp`: WorkerPool ns | DNS; kagent gate 8084 (controller 8083 with the gate off); Postgres, MinIO, Gitea, Dragonfly; ax-server (or `ax.namespace`); internet minus `clusterCidrs` on `skanyxxEgress.internetPorts` (443/465/587); `skanyxxEgress.extra` |
| kagent gate | 8084: Skanyxx<br>8083: agent pods | DNS; controller 8083 |
| kagent controller | 8083 only: the gate (Skanyxx and agent pods with the gate off), kagent UI if `ui.replicas > 0`, `controllerFrom` | open (Kubernetes API), or `controllerEgress` (D229) |
| Agent pods | controller on `a2aPorts` (8080); `a2aPeers` callers | DNS; the `kagent-gate` Service 8083; Skanyxx `mcp`; `modelEgress`; `a2aPeers` callees |
| Postgres | Skanyxx, kagent controller | DNS |
| MinIO, Gitea | Skanyxx | DNS |
| Dragonfly | Skanyxx, ax-server/ax-controller (or `ax.namespace`) | DNS |
| ax-server | Skanyxx (grpc) | its Redis |
| ax-controller | nothing | its Redis, Substrate ns, WorkerPool ns |
| Sandboxes (WorkerPool ns) | not fenced by us (Substrate's) | DNS; Skanyxx `sandbox-mcp` (if memory on); internet minus `clusterCidrs` (if `ax.sandboxes.internet`) |

- **BYO kagent or Postgres** needs a `skanyxxEgress.extra` rule; the chart refuses a render without one. On-prem
  Jira, IdP or SMTP also go in `extra`.
- **`skanyxxEgress.enabled=false`** restores open egress.
- **The Ingress** targets `http` only, through an allow-list of paths. `/mcp` is refused in any case (D138, D145).
- **`kubectl port-forward`** enters the pod's network namespace, so these policies do not apply to it. Guard
  `pods/portforward` with Kubernetes RBAC.

## kagent / A2A auth status

- kagent 0.10.2 has `unsecure` and `trusted-proxy` only, and authorizes nothing (`NoopAuthorizer`).
- `trusted-proxy` does not verify the JWT signature, and it ignores `X-User-Id` on user calls. With Skanyxx that would
  turn every Chat user into one person, or a 401. **The schema pins `kagent.controller.auth.mode: unsecure`.**
- **Compensating controls (D520-D523):**
  - Only the kagent gate reaches 8083. Skanyxx's calls must carry its signed token, and the user comes from that token.
  - The agents reach the gate's session and task paths only.
  - Only the controller and declared `a2aPeers` reach agent A2A.
  - Details and the values are in [`a2a-security.md`](a2a-security.md).
- **Agent writes (D250):** Skanyxx calls the controller's agent CRUD as its own process user, never with a person's id or
  a request header; who may create, edit or delete, and which agents are protected, is decided in Skanyxx before the call.

## Known gaps

- **Agent pods can call the session and task API as any user** (the gate's agent port checks paths, not callers, D522).
  An LLM-driven agent can no longer list or create agents, ModelConfigs or tool servers, or call other agents through
  the controller. It can still read or append to another user's session by id. Closing this needs verified agent
  identity: kagent's projected token checked against the cluster's JWKS, a per-agent ServiceAccount, or a mesh.
- **The kagent controller can read every Secret in the release namespace.** That includes Skanyxx's DB credentials, the
  sandbox token key and the kagent gate's signing key. Moving Skanyxx's Secrets to another namespace would need a split install.
- **The controller's egress is open** unless `controllerEgress` is on (D229).
- **ax-server and Substrate's atenet-router have no caller auth.** ax-server is fenced to Skanyxx; the router is
  Substrate's responsibility.
- **Correctness depends on the CNI enforcing NetworkPolicy.** kindnet, Calico and Cilium do; flannel alone does not.
- **Partly live-tested.** The gate and the kagent/agent/Skanyxx policies ran on a throwaway kind cluster with stand-in
  pods (`scripts/helm/gate-kind-test.sh`, D520). The store, AX and sandbox rules are template-validated only.
