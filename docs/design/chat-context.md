# Chat context: index, related work, tickets, answer cache

Module `chatcontext` (D236–D242). Chat still talks only to kagent; this module only reads and writes Skanyxx's own index.

## Turn flow

```
POST api/chat ──► Chat handler
   new conversation?  ──► IChatContext.PrepareAsync
                            1. answer cache (if on): hit → answer, no kagent call
                            2. related sessions (readable; shared/written/linked ticket, then words) + published company cards
                            3. block ≤ RelatedTokenBudget
   kagent: create session (if new), send  block + message
   IChatContext.CompletedAsync ──► upsert chat_sessions row via IChatExtractor
                                   (first turn + cache on → keep the answer)
   response: answer + relatedSessions + relatedCards (+ cached)
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

## Rules worth remembering

- A personal session is its owner's only. Supervisors and the owner do not read it (unlike cards).
- Injected cards: published company only (memory's `CompanyCardsQuery`): readable by every person and in every agent's default grants. No team or department card is injected (D238).
- Chat/chatcontext use `ChatReader`, never `LibraryUser`: only the Host and memory act as a person in memory.
- Card writes publish `CardsChanged(scope)`; the cache drops the owner's answers (personal) or everyone's (shared).
- An answer built on someone else's shared chat is never cached. A Redis error or timeout is a miss, never a failure.
- Forgetting a session drops its row; continuing that conversation in kagent indexes it again (from the next turn).
- The cache key starts with the user id: never one person's answer to another.
