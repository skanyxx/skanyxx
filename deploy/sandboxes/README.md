# Sandboxes network isolation

A sandbox runs caller-chosen code. Skanyxx's UI/API and its agents' `/mcp/memory` port trust credentials a sandbox must
never hold (an agent-secret holder may name users in `X-User-Id`, D084), and ax-server has no auth, so a sandbox that can
reach either one can act as anyone it names. These policies are the fence (D072, D077 in `docs/design/ax-integration.md`).
The one Skanyxx port a sandbox may reach is the **sandbox listener** (`Memory:SandboxMcpPort`, D077): it serves
`/mcp/memory` only and identifies every request by the per-sandbox token Skanyxx signs (D076), ignoring headers.

| File | Effect |
|---|---|
| `ax-server-ingress.yaml` | Only the Skanyxx API pod reaches `ax-server:8080`. |
| `sandbox-egress-deny.yaml` | Sandbox pods may resolve DNS and nothing else: no Skanyxx, ax-server, kagent or other cluster service. |
| `sandbox-egress-internet.yaml` | Optional. Adds the public internet, minus RFC1918, link-local (cloud metadata), CGNAT and loopback. |
| `sandbox-egress-memory.yaml` | Optional, with sandbox memory on. Adds Skanyxx's sandbox listener port (8082) and nothing else of Skanyxx. Skanyxx's own ingress policy must admit the WorkerPool's namespace on that port. |

Apply: set the placeholder namespaces and labels (`skanyxx`, `ax-system`, `ax-sandboxes`, `app.kubernetes.io/name`)
to your install, then `kubectl apply -k deploy/sandboxes`.

**Verified on kind + gVisor (2026-10-08, D178), not yet on GKE or micro-VMs.** Substrate runs a sandbox INSIDE a
WorkerPool pod, so the egress policies belong in **the WorkerPool's namespace** (`ax-sandboxes` above is that
placeholder), not in a namespace of their own. Measured from inside a sandbox: with no policy it reached the
internet, `ax-server` (no auth), Substrate's API and the Skanyxx Host on the developer's machine; with
`sandbox-egress-deny.yaml` every one of those timed out, and Substrate kept working (new tasks start, run, stop;
`ax ssh` still works; Substrate's own per-pool policy admits ingress from `atenet-router`).
The cloud metadata address was unreachable even without the policy on kind; on GKE, test it again.
Repeat the test on every new runtime (`curl`/`wget` Skanyxx, ax-server and `169.254.169.254` from a sandbox; all
must fail) before relying on it.

Skanyxx refuses to start with a non-empty `Sandboxes:AllowedImages` until `Sandboxes:NetworkIsolationConfirmed=true`.
Set it only after that test passes. `Sandboxes:MemoryMcpUrl` is refused at startup unless it is the sandbox listener's
`http(s)://<host>:<Memory:SandboxMcpPort>/mcp/memory` with `Memory:SandboxTokenKey` set (D190-D194). With it on, repeat
the egress test with `sandbox-egress-memory.yaml` applied: from a sandbox, Skanyxx's sandbox port answers (401 without
the token), every other Skanyxx port, ax-server and `169.254.169.254` still time out. The Helm chart renders all of
this with `ax.sandboxes.memory.enabled`.
