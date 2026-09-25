# Facts

Sources we actually looked at. If a fact changes upstream, update it here.

## Skanyxx today (`skanyxx_v1` / maui)

- .NET 8 ASP.NET Core host + plugin modules. UI is Razor; agents are **listed from a KAgent cluster**, not authored in Skanyxx.
- Memory UI is **vector-provider admin** (Pinecone / Chroma / Weaviate), not a company knowledge bank.
- Chat, sessions, tool servers already exist and can be reshaped; Dashboard / Alerts / Investigate / CloudTools are SRE leftovers.

## kagent

Docs: [kagent](https://kagent.dev/docs/kagent/) · [agents](https://kagent.dev/docs/kagent/concepts/agents/) · [architecture](https://kagent.dev/docs/kagent/concepts/architecture) · [skills](https://kagent.dev/docs/kagent/examples/skills/) · [agent memory](https://kagent.dev/docs/kagent/concepts/agent-memory/)

- Declarative agents on Kubernetes (CRDs). An agent is **model + system prompt + MCP tools + skills**, optional A2A.
- Models via `ModelConfig` (OpenAI, Anthropic, Azure, Gemini, Ollama, Bedrock, …).
- MCP via `RemoteMCPServer` (and KMCP for in-cluster MCP). Tools are attached to an agent by name.
- Skills: inline A2A metadata, or **container/git skills** with `SKILL.md` loaded at runtime.
- Built-in memory: `save_memory` / `load_memory` / `prefetch_memory`, Postgres+pgvector, **isolated per agent**, default TTL 15 days, **cannot share across agents**.
- kagent already has its own dashboard/CLI. Skanyxx’s job is the **customer workspace**, not a second k8s ops console.
- Agents can be exposed as MCP (`/mcp` on the controller, default port 8083) — same port Skanyxx already talks to.

## Google AX

Repo: [google/ax](https://github.com/google/ax) · [concepts](https://github.com/google/ax/blob/main/docs/concepts.md)

- k8s-shaped **task orchestrator**, not a long-lived chat-agent product. Primitives: **Task**, **Workspace**, **Model**.
- Needs **Agent Substrate** in the cluster first. Control plane state is **Redis** (not etcd CRDs for millions of short tasks).
- Workspace can pre-wire git, MCP, skills — similar *ingredients* to kagent, different unit of work (cheap sandboxes, suspend/resume, `ax ssh`).
- Authors warn of **breaking changes** before a stable release.
- Skanyxx v1: AX is the **Tasks** pack only (D063–D064). Ships **in the umbrella like kagent** (D067).

## Joicy

Repo: [DmarshalTU/Joicy](https://github.com/DmarshalTU/Joicy)

- Sister product: **team memory bank**, local-first, aimed at **dev work** (git, snippets, vault notes).
- What ships today: CLI + SQLite/FTS5 + git post-commit capture + Obsidian vault + MCP tools `memory_search`, `memory_store`, `memory_changelog`, `memory_vault_note`.
- What does **not** ship yet: central/team sync, embeddings/semantic search on the default path.
- Planned hierarchy: personal → team → company. That matches Skanyxx collections in spirit, but Joicy’s intake is **code-centric**; Skanyxx intake is **any company work**.
- Useful pattern: **memory as an MCP server** that agents call. That is also how kagent says to plug in an external memory product.
