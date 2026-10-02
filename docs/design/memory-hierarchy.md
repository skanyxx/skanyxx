# Memory hierarchy (the real problem)

Joicy named it; Skanyxx has to **run** it. Today company know-how sits in **private chats**. We do not fix that by dumping every transcript into `company`. We **extract cards** in a personal space, then **lift** them.

Community catalog is **paused**.

## Layers (supersedes “only company” D039)

```
personal     ← chats live here; cards are born here
    ↓ lift (copy up)
team
    ↓ lift
department
    ↓ lift
company      ← what most agents search (D040)
```

Org tree (teams/depts) is **created in Skanyxx**. Entra is optional sync, not the source (D055).
**As built (D090):** the owner builds it on the Org page; memory checks membership live on every request.

A card’s identity is **`scope` + `key`** (was collection+key). Same slug can exist at personal and at company; company is what the fleet reads.

## What is lifted

| Lift | Yes |
|---|---|
| Published **cards** (what + why) | Yes |
| Full chat history | **No** (D012, D044). Source pointer can remain on the card. |

That is how we empty private chats of *knowledge* without cloning petabytes of *conversation*.

## Promote = copy up (not move)

Personal copy stays. Team/company gets a new row at that scope (same `key` if free). Someone at the higher layer can edit/supersede there without deleting the author’s original.

## Who writes where

- Employee + seed agent: **upsert defaults to personal** (not company).
- Agents **search `company`** (and maybe their team) by grant.
- **Lift into company** is a human gate: Agent supervisor / owner (same spirit as YAML merge). Lift into **team**: a member of that team.

Otherwise 100 agents “helpfully” publish everything to the company brain.

## Library

- Personal: only you (and your chats).
- Team/dept: members of that scope (department members = members of its teams). Owner and supervisors read every
  team/dept scope too, but write only where they are members. A `team:`/`department:` slug with no org object behind it
  has no members, so only owner/supervisor read those cards. Agents: only by grant, and a team/department grant is
  the owner's to set (D091); an agent acting for a member still reads no team without one. A supervisor's lift out of
  a team they are not in is allowed (they gate company) and logged at Warning, as is every grant change.
- `company` published: logged-in users (D048), still.
