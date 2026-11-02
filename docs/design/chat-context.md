# Chat context: index, related work, tickets, answer cache

Module `chatcontext` (D260–D266). Chat still talks only to kagent; this module only reads and writes Skanyxx's own index.

## Turn flow

```
POST api/chat ──► Chat handler
   new conversation?  ──► IChatContext.PrepareAsync
                            1. answer cache (if on): hit → answer, no kagent call
                            2. related sessions (readable; shared/written/linked ticket, then words) (cards: memory's IRelatedMemory, D302)
                            3. block ≤ RelatedTokenBudget
   kagent: create session (if new), send  block + message
   IChatContext.CompletedAsync ──► upsert chat_sessions row via IChatExtractor
                                   (first turn + cache on → keep the answer)
   response: answer + relatedSessions + memoryCards (+ cached)
```

## Table `chat_sessions` (memory's database)

| Column | Notes |
|---|---|
| `session_id` PK | kagent's id |
| `user_id`, `agent_ref` | owner, `namespace/name` |
| `title` | first line of the first question, ≤80 |
| `scope` | `personal:{owner}` (default), `team:`, `department:`, `company` |
| `keywords` text[], `keyword_weights` jsonb | top 12 shown; ≤64 weights kept |
| `categories` text[], `ticket_keys` text[] (GIN) | |
| `outcome` | ≤400, opening of the latest answer |
| `search_terms`, `search` tsvector (GIN) | generated: title A, terms B, outcome C |
| `turns`, `created_at`, `updated_at` | |

pgvector later: an `embedding` column next to `search`, filled by a model-based extractor; same readable filter.

## API (signed-in, CORS off)

| Route | |
|---|---|
| `GET api/chat/sessions?q=&ticket=&category=&agent=&mine=` | readable sessions; `mine` = history |
| `GET/DELETE api/chat/sessions/{id}` | one row / forget it (owner) |
| `PUT api/chat/sessions/{id}/tickets` `{ticketKeys}` | owner |
| `PUT api/chat/sessions/{id}/scope` `{scope}` | owner, into a scope they may write |
| `GET api/chat/tickets/{key}` | sessions, runs, related tickets (+ runs) |
| `GET/DELETE api/chat/cache[?all=true]` | savings / clear (all = owner) |

## Chat module API (D315–D317, signed-in, CORS off)

| Route | |
|---|---|
| `GET api/chat/agents` | the picker: every kagent agent but Skanyxx's own, not suspended (D557); model, accepts images, `toolProblem` (D554) |
| `GET api/chat/agents/{ns}/{name}` | details panel (404 for any agent Chat does not offer); `toolProblem`, each server's `problem` (D554) |
| `POST api/chat` / `POST api/chat/stream` | one turn, blocking / server-sent events (`session`, `delta`, `text`, `usage`, `answer`, `done` or `error`; no tool steps, D555); a failed task's `reason` for builders and up only (D553) |
| `GET api/chat/conversations` | the caller's kagent sessions, newest first |
| `GET/PATCH/DELETE api/chat/conversations/{id}` | reopen (items: user, assistant; D555), rename, delete — owner only, else 404 |
| `POST api/chat/files`, `GET/DELETE api/chat/files/{id}`, `GET api/chat/limits` | attachments, owner only |

## Rules worth remembering

- A personal session is its owner's only. Supervisors and the owner do not read it (unlike cards).
- Injected cards are memory's (`IRelatedMemory`, D302): published cards both the person and the agent may read, in their own block and budget. The chat index injects sessions only (integration, D270).
- Chat/chatcontext use `ChatReader`, never `LibraryUser`: only the Host and memory act as a person in memory.
- Card writes publish `CardsChanged(scope)`; the cache drops the owner's answers (personal) or everyone's (shared).
- An answer built on someone else's shared chat is never cached. A Redis error or timeout is a miss, never a failure.
- Forgetting a session drops its row; continuing that conversation in kagent indexes it again (from the next turn).
- The cache key starts with the user id: never one person's answer to another.
