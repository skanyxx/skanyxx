# How a customer gets in

The **product** is a **Helm umbrella** (D060). Today’s fat local Skanyxx thins to a **client**.

Umbrella subcharts: Skanyxx API · **kagent (on)** · **AX + Substrate (on)** · **Dragonfly (on, shared)** · memory · **Postgres · MinIO · git (on by default)**. Same install as kagent (D067–D068). Turn a runtime or data store off via values (D061). Appliance = that chart on hidden k3s. BYO cluster = same chart on theirs.

## Bundled vs their own (D061)

| Default in the chart | They can instead connect |
|---|---|
| Postgres | RDS, Cloud SQL, their Postgres |
| MinIO | S3, Azure Blob, GCS |
| Git (in-stack) | GitHub / GitLab / existing repo (D023) |
| **Dragonfly** | Their Redis/Dragonfly (AX + optional memory cache share it — D068) |
| pgvector | Off until needed |

Same Skanyxx/kagent/memory apps either way. Only the data plane URLs change.


## Two ways to install (same product)

| Path | Who it’s for | What they see |
|---|---|---|
| **Appliance** (default if they don’t know k8s) | “Give me a server” | One Linux VM (or cloud marketplace image). Run our installer. Get a URL. |
| **Bring-your-cluster** | Platform team already on EKS/AKS/GKE | Helm on *their* cluster. |

Appliance path: installer puts **k3s (hidden) + our Helm chart** on that machine. They never run `kubectl`. Under the hood it is still k8s — we own that complexity.

```
License
  → owner picks: “VM / marketplace” or “we have Kubernetes”
  → run our installer ONCE
  → browser URL → login, model keys, invite people
  → everyone else uses that URL
```

We do **not** ask a non-platform customer to learn Kubernetes. We also do **not** pretend kagent can run as a single Windows .exe — the runtime is still container/k8s, just boxed.

## Skanyxx vs kagent (D058)

Same as today: **Skanyxx is not kagent.** Owner (or Helm values) sets **controller URLs**.

| Piece | Runs where | Config |
|---|---|---|
| **Skanyxx** UI + users + org + library + studio/git | In the cluster **or** outside (VM, IIS, laptop for admin) | `KAgent` URL, memory MCP URL |
| **kagent** | Kubernetes (theirs or hidden k3s) | Models, Agent CRs |
| **Memory engine** | Next to data (usually cluster) | Postgres, MinIO |

Default one-click: all URLs are in-cluster, user only sees the Skanyxx site.

Employees can use the **browser** or a **desktop client** (D059). Same URLs. They never install kagent.

## Desktop like today (D059)

No problem with a downloadable app that has its own UI and talks to the **kagent controller URL**.

The problem is only if that app’s **local backend is the company source of truth**.

Today one person runs Skanyxx on a laptop → local SQLite/users/memory. That is an **operator console**. If 50 employees each do that, you get 50 brains — the private-chat problem again.

So the desktop app may include UI + a local process, but:

| Local on the PC | Shared (controller URLs) |
|---|---|
| Window, cache, maybe personal draft chat | kagent (agents) |
| | Memory engine (cards, lift) |
| | Skanyxx API (users, roles, org, grants, git/PRs) |

Configure kagent URL **and** the Skanyxx/memory URL(s), like today — just don’t put the company bank in `%APPDATA%`.


