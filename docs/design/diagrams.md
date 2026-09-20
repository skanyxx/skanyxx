# Diagrams

How the locked design fits together. Open in preview for the graphs.

## 1. Three planes (one customer install)

```mermaid
flowchart TB
  subgraph people [People]
    O[Owner]
    S[Agent supervisor]
    B[Builder]
    E[Employee]
  end

  subgraph skx [Skanyxx — workspace]
    Chat[Chat]
    Studio[Studio]
    Lib[Library]
    Org[Org tree]
  end

  subgraph kag [kagent — runtime]
    Loop[Model + prompt + skills + MCP]
  end

  subgraph mem [Memory engine]
    Cards[Cards FTS + version]
    PG[(Postgres)]
    MinIO[(MinIO / S3)]
  end

  O --> Org
  E --> Chat
  B --> Studio
  S --> Studio
  Chat --> Loop
  Studio --> Loop
  Loop -->|search / upsert cards| Cards
  Cards --> PG
  Cards --> MinIO
  Lib --> Cards
```

Skanyxx does not run the LLM. kagent does not own company knowledge.

## 2. One chat turn

```mermaid
sequenceDiagram
  actor User
  participant Chat as Skanyxx chat
  participant K as kagent
  participant M as Memory MCP
  participant PG as Postgres

  User->>Chat: message
  Chat->>K: run agent
  K->>M: search granted scopes top-5
  M->>PG: FTS on published cards
  PG-->>M: tiny cards
  M-->>K: cards into this turn only
  K-->>Chat: answer
  alt something locked
    K->>M: upsert scope+key+version
    Note over M: default scope = personal
    M-->>K: ok or conflict
  else usual turn
    Note over K: write nothing
  end
```

## 3. Lift knowledge out of private chats

```mermaid
flowchart BT
  P[personal — chats + new cards]
  T[team]
  D[department]
  C[company — fleet search]

  P -->|lift copy of CARD not transcript| T
  T -->|lift| D
  D -->|lift — supervisor/owner| C

  P -.->|small org skip team/dept| C
```

Chats never move. Cards copy up. Personal copy stays.

## 4. New agent: factory or studio → git PR

```mermaid
flowchart LR
  B[Builder]
  F[Factory chat]
  St[Studio form]
  Git[agent-config repo]
  PR[Pull request]
  Sup[Agent supervisor merge]
  Rec[Our reconciler]
  CR[kagent Agent CR]

  B --> F
  B --> St
  F --> Git
  St --> Git
  Git --> PR
  PR --> Sup
  Sup --> Rec
  Rec --> CR
```

Employees never see the agent until merge. Preview is sandbox, search-only on production memory.

## 5. Day-one stack on customer Kubernetes

```mermaid
flowchart TB
  subgraph cluster [Customer cluster]
    SK[Skanyxx]
    KA[kagent]
    MS[Memory service MCP]
    PG[(Postgres)]
    MO[(MinIO)]
    GT[Git]
  end

  User[Browser] --> SK
  SK --> KA
  KA --> MS
  MS --> PG
  MS --> MO
  SK --> GT
  GT -->|merge| KA

  DF[Dragonfly — later]
  PV[pgvector — later]
```

## 6. Two writers, one key

```mermaid
sequenceDiagram
  participant A as Agent A
  participant E as Memory engine
  participant B as Agent B

  A->>E: upsert company/refund-window v3
  E->>E: lock that key
  E-->>A: ok v4
  B->>E: upsert same key v3
  E-->>B: CONFLICT — re-read
```

No Redis bus. Engine serializes the **key**. Dragonfly only if we run several memory replicas.
