# Memory for a kagent agent (kagent 0.10.x)

One agent (`seed`) wired to Skanyxx company memory over MCP with its own secret (D080/D083/D084).

1. Issue the agent's secret (owner or supervisor token from `POST /api/identity/sign-in`, `tokens.accessToken`) and
   create the Secret straight from the response, so the value never lands in a file, your shell history or argv
   (run it as a bash script — `set -e` in an interactive shell would close the shell on an error):

       set -euo pipefail
       S=$(curl -fsS -X POST -H "Authorization: Bearer $OWNER_TOKEN" -H 'Content-Type: application/json' \
             -d '{"actsForUsers": false}' https://skanyxx.example/api/memory/agents/seed/secret | jq -er .secret)
       printf 'Bearer %s' "$S" \
         | kubectl -n kagent create secret generic skanyxx-memory-seed --from-file=token=/dev/stdin \
             --dry-run=client -o yaml | kubectl apply -f -
       unset S
       kubectl -n kagent label agent seed skanyxx.dev/memory-secret=$(date +%s) --overwrite

   `curl -f` and `jq -e` stop the script on a refused or empty answer, so a failed issue never overwrites a working
   Secret. **Issuing kills the previous secret at once**: if anything fails *after* the issue (kubectl, the label),
   the old secret is already dead — run the whole block again to reissue. `seed-secret.example.yaml` only shows the
   Secret's shape (the whole header, no trailing newline); it is deliberately not in `kustomization.yaml`.

   `actsForUsers` (D084): leave it `false` unless the agent must work in each user's **personal** memory. The seed is
   that agent: the first hour issues its secret with `true` (`scripts/dev/first-hour.sh seed-secret` does steps 1–2
   for a local run). With `false`,
   `X-User-Id` is ignored and the agent sees company memory and its grants only. `true` can only be set by the
   **owner** (a supervisor gets `403`) — see "Acting for users" below. Every issue sets the flag again: a rotation
   that omits it turns it off.
2. Apply the server and the agent: `kubectl apply -k .` (or `-f seed-remotemcpserver.yaml -f seed-agent.yaml`).
3. Rotate: run step 1 again. kagent copies header values into the agent's rendered config and watches no header
   Secrets; the label change makes it reconcile and re-render, and the new config hash rolls the pod. A plain
   `rollout restart` is not enough: the pod would reload the same stale config with the dead secret.
   Between the issue and the Secret update, the RemoteMCPServer's tool refresh (about every 60 s) gets `401`s and its
   status may show an error until the new Secret is in place; that is expected and clears on the next refresh.
   Revoke: `DELETE /api/memory/agents/seed/secret`. Issue, rotation and revocation are logged at Warning with who did
   it, the agent, the flag and the client IP (never the secret); refused attempts are logged too. The IP is the direct
   peer, so behind the ingress it is the ingress pod's: the actor user id is what attributes the action. If the agent
   acts for users, rotating or revoking its secret is owner only (a supervisor gets `403`).

- Another agent = another Secret + RemoteMCPServer (`skanyxx-memory-<agent>`), referenced from that agent's `tools`.
  kagent's controller lists tools with the server's own headers, and `/mcp/memory` answers `401` without a secret.
- The in-cluster URL assumes Skanyxx's Service `skanyxx` in namespace `skanyxx` on its **mcp port 8081**: Skanyxx
  serves `/mcp/memory` there only when `Memory:McpPort=8081` is set (the Helm chart sets it; D138). A Skanyxx without
  that setting serves `/mcp/memory` on its one port: change `url` to yours. On a Helm install (`deploy/helm/skanyxx`)
  `scripts/helm/seed-agent.sh` applies these files into the release namespace with the release's own Service and mcp
  port, and issues the secret (README "Install with Helm").
- Never list `authorization` in `allowedHeaders`, and do not turn on token propagation / STS for an agent using this
  server: the user's kagent token would replace the agent secret (fails closed with `401`, confusingly).

## Acting for users (D084)

`allowedHeaders: [x-user-id]` forwards the end user kagent puts on the A2A request. Skanyxx honours it only when the
agent's secret was issued with `actsForUsers: true`, and only when it is a Skanyxx user id (a lowercase GUID);
anything else is treated as no user (no personal scope, personal writes refused).

Say it plainly: **an agent that acts for users can read and write any user's personal memory just by naming that
user's id.** The secret is the whole credential; Skanyxx cannot check that the user really asked. That is why only
the owner can turn it on, why kagent's API must stay private to the cluster (kagent 0.10.2 has no `secure` mode: the
Helm default `unsecure` lets any caller choose the user, and `trusted-proxy` takes it from a JWT that kagent does not
verify itself — it trusts an authenticating proxy such as oauth2-proxy in front; D101), and why such an agent should not also carry shell or Kubernetes tools (a prompt injection
there reads the secret). Keep read access to Secrets in the `kagent` namespace tight.

## Transport and network (requirements)

- **`/mcp` must not be on the public ingress.** Route only the UI and `/api` publicly; agents reach `/mcp/memory`
  in-cluster.
- **In-cluster traffic must be encrypted**: a service mesh with mTLS between kagent and Skanyxx, or serve Skanyxx
  over `https://` and give the RemoteMCPServer a `tls` block (kagent v1alpha2 `spec.tls`: `caCertSecretRef` +
  `caCertSecretKey` for a private CA, `disableSystemCAs` to trust only it; never `disableVerify`). kagent rejects
  `spec.tls` on an `http://` URL. The plain `http://` URL in `seed-remotemcpserver.yaml` is for a meshed cluster only.
- `skanyxx-ingress-networkpolicy.example.yaml` lets only the `kagent` namespace reach Skanyxx's mcp port (8081) and
  your ingress controller its UI/API port (8080). It is an example: adjust labels, namespaces and ports, and apply it
  in Skanyxx's namespace. The Helm chart ships its own (default-deny, D137).
