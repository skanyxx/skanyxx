# Sandboxes network isolation

A sandbox runs caller-chosen code. Skanyxx trusts `X-User-Id` / `X-Agent-Id` headers until the identity slice,
and ax-server has no auth, so a sandbox that can reach either one can act as anyone. These policies are the fence
(D072, D077 in `docs/design/ax-integration.md`).

| File | Effect |
|---|---|
| `ax-server-ingress.yaml` | Only the Skanyxx API pod reaches `ax-server:8080`. |
| `sandbox-egress-deny.yaml` | Sandbox pods may resolve DNS and nothing else: no Skanyxx, ax-server, kagent or other cluster service. |
| `sandbox-egress-internet.yaml` | Optional. Adds the public internet, minus RFC1918, link-local (cloud metadata), CGNAT and loopback. |

Apply: set the placeholder namespaces and labels (`skanyxx`, `ax-system`, `ax-sandboxes`, `app.kubernetes.io/name`)
to your install, then `kubectl apply -k deploy/sandboxes`.

**Unverified:** nobody has run these on a real Substrate cluster. Whether a Kubernetes NetworkPolicy applies to
Substrate sandboxes (gVisor/microVM) depends on the runtime and CNI, and the task metadata endpoint
(`AX_METADATA_URL`) may need an extra egress rule. Test it from inside a sandbox (e.g. `curl` Skanyxx, ax-server
and `169.254.169.254`, all must fail) before relying on it.

Skanyxx refuses to start with a non-empty `Sandboxes:AllowedImages` until `Sandboxes:NetworkIsolationConfirmed=true`.
Set it only after that test passes. `Sandboxes:MemoryMcpUrl` must stay empty until the token-only sandbox listener
(D076/D077) exists; startup refuses any value.
