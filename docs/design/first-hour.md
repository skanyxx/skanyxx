# Step 2 — First hour after license (closed)

The license includes **our setup** (D018). Talk surface is **Skanyxx chat** (D017). First hour: a named person sends a message and gets an answer.

## Start clean (D019)

kagent’s sample pack is **off**. One **general-purpose** agent + memory MCP + empty `company`. Specialists come later via studio/factory + supervisor merge.

## Hour path

```
Setup finishes
    → first person = owner (D025)
    → paste model credentials
    → chat with the seed agent
    → add users: invite and/or connect Entra (D026–D027)
```

## Identity

See `identity.md` and `install-path.md`. Bootstrap is always a local owner. Entra is optional after they are in. Invited users use the **same Skanyxx URL** (D056).

## As built (2026-10-05, todo.md slice 1)

```
/Setup (owner, D025) → /Model (owner: provider, model, API key → kagent ModelConfig, D099) → /Chat with the seed (D101)
```

- **Model step.** `/Model` (Models, D323): the owner creates the default model (`KAgent:ModelConfig`, kagent's
  `default-model-config`) in the list of kagent's ModelConfigs, through `api/models` and kagent's API; a pasted key goes
  into a Secret kagent owns. Skanyxx stores no key and holds no Kubernetes credentials (D099). `/` sends the owner here
  until the default exists and kagent has accepted it.
- **Chat.** Lists every agent kagent runs except Skanyxx's own (previews, the factory, ticket stages) and suspended
  ones (D557; until then only `skanyxx.dev/merged: "true"`, D100) — and talks to
  kagent as the signed-in person, so the seed searches company memory plus that person's personal memory (D101).
- **Seed memory secret.** Issued by the owner with `actsForUsers: true` and written into the seed's Secret
  (`scripts/dev/first-hour.sh seed-secret`; the umbrella chart will do it at install, slice 4). Skanyxx still holds no
  Kubernetes credentials, so today a script (or a person) moves the secret into the cluster.
- **Local run.** `scripts/dev/first-hour.sh up | host | seed-secret | status` (README "First hour"): Postgres by compose,
  kagent 0.10.2 with every sample agent off, only `deploy/kagent/memory/` applied, the Host by env vars.
- **QA round 1 (D105–D107).** Settings API owner only (theme read open, connection tokens write-only); Investigate
  no longer talks to agents; `/api/chat` and `/api/model` (now `/api/models`, D323) rate limited and origin guarded; a new base URL needs the
  key again; a provider switch leaves no stale key in kagent's Secret; per-call kagent deadlines (10 s / 300 s), no
  retries, fixed error texts; agents addressed as `namespace/name`; the local Host binds `127.0.0.1`.
- Not yet: inviting users from the hour's end (identity pages exist: People, Entra), the model step for more providers
  (Azure OpenAI, Bedrock, Gemini — kagent supports them), kagent behind real authentication (0.10.2 has only
  `unsecure` / `trusted-proxy`, D101).
