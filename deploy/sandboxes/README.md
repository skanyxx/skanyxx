# Sandboxes network isolation

A sandbox runs caller-chosen code. Skanyxx's `/mcp/memory` trusts the `X-User-Id` an agent-secret holder sends
(D080), and ax-server has no auth, so a sandbox that can reach either one can act as anyone it names. These policies are the fence
(D072, D077 in `docs/design/ax-integration.md`).

| File | Effect |
|---|---|
| `ax-server-ingress.yaml` | Only the Skanyxx API pod reaches `ax-server:8080`. |
| `sandbox-egress-deny.yaml` | Sandbox pods may resolve DNS and nothing else: no Skanyxx, ax-server, kagent or other cluster service. |
| `sandbox-egress-internet.yaml` | Optional. Adds the public internet, minus RFC1918, link-local (cloud metadata), CGNAT and loopback. |

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
Set it only after that test passes. `Sandboxes:MemoryMcpUrl` must stay empty until the token-only sandbox listener
(D076/D077) exists; startup refuses any value.
