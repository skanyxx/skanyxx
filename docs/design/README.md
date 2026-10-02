# Skanyxx design log

We design in small steps. This folder is the running record — not a product spec dump.

| File | Purpose |
|---|---|
| [decisions.md](decisions.md) | Locked choices and **why**. If we reverse one, add a new row; do not silently edit history. |
| [facts.md](facts.md) | Constraints from Skanyxx today, [kagent](https://kagent.dev/docs/kagent/), [AX](https://github.com/google/ax), [Joicy](https://github.com/DmarshalTU/Joicy). |
| [ax-tasks.md](ax-tasks.md) | kagent = Agents; AX = Tasks. v1 flags. |
| [ax-integration.md](ax-integration.md) | AX v0.3.1 reality vs design; reuse; v1 Sandboxes slice; proposed D069+. |
| [open.md](open.md) | Questions not decided yet. Next conversation step lives here. |
| [spine.md](spine.md) | Ordered walk from the start. Follow this. |
| [three-planes.md](three-planes.md) | Step 1 deep dive: Skanyxx vs kagent vs memory. |
| [first-hour.md](first-hour.md) | Step 2: setup → first chat message. |
| [identity.md](identity.md) | Owner bootstrap, invite, teams and departments, optional Entra. |
| [install-path.md](install-path.md) | License → one install → browser URL. |
| [studio.md](studio.md) | Step 3: compose agent → PR. |
| [memory-store.md](memory-store.md) | Volume + tokens: cards not chats. |
| [memory-card.md](memory-card.md) | Tiny card schema. |
| [memory-writes.md](memory-writes.md) | 100 agents vs one bank; not Redis-as-orchestrator. |
| [leftovers.md](leftovers.md) | TTL, GitOps, Joicy, k8s, library. |
| [diagrams.md](diagrams.md) | Mermaid: planes, turn, lift, factory, stack, writes. |
| [memory-hierarchy.md](memory-hierarchy.md) | Personal → team → dept → company; lift cards. |
| [memory-stack.md](memory-stack.md) | Postgres + MinIO + MCP; one Dragonfly (shared with AX). |
| [memory-how.md](memory-how.md) | End-to-end: turn, lift, grants, stack. |
| [seed-vs-factory.md](seed-vs-factory.md) | Seed vs factory; factory = YAML + git PR. |
| [roles.md](roles.md) | Owner, supervisor, builder, employee. |

Rule: one idea per bullet. No essays.
