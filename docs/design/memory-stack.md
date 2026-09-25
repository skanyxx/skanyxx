# Memory tech stack

Source of truth is **not** Dragonfly, **not** Joicy, **not** kagent TTL memory.

| Piece | Tech | Role |
|---|---|---|
| Cards, versions, scopes, grants | **PostgreSQL** | Unique `(scope, key)`, `version`, FTS (`tsvector` / FTS) |
| Sessions / chat history | **PostgreSQL** | Personal, TTL 14d (D044) |
| Source blobs (files, audio) | **S3 / MinIO** | Pointer on the card only |
| Memory API | **Skanyxx memory service** (MCP to kagent) | Search top-k, upsert, promote, conflict |
| Cache / distributed lock | **Dragonfly** (same instance AX uses as Redis) | Cache/locks only. Not the card store (D068). |
| Semantic search | **pgvector later** | Day one is FTS + keys, like Joicy’s working POC |

## Why Postgres

We already live on **customer Kubernetes** (D047). kagent can already sit on Postgres. One durable store for cards **and** sessions. Unique key + version is how we serialize writes (D041) without Redis-as-brain.

## Why not Joicy-as-the-bank

Joicy today: local SQLite, git commit capture, no team sync. Wrong grain (code snippets) and not multi-scope promotion. We **copy the idea** (personal → up, MCP search/store, FTS first). We do **not** mount Joicy as the company store (D046).

## Why FTS before vectors

Embeddings on every personal chat is how you get token *and* GPU cost while still siloing. Cards are 200+400 chars; FTS is enough to start. Vectors are an index upgrade, not the product.

## Day-one deploy (Helm umbrella)

Default: bundled Postgres + MinIO + git + **one Dragonfly** (AX Redis + later memory cache). Flags to use theirs instead (D061, D068). pgvector stays off until needed.
