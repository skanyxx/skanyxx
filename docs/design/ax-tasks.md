# Agents vs Tasks (kagent and AX)

Skanyxx is the company UI. Two **jobs**, two runtimes. Nobody gets “AX but chat like kagent.”

| | **Agents** | **Tasks** |
|---|---|---|
| Runtime | [kagent](https://kagent.dev/docs/kagent/) | [Google AX](https://github.com/google/ax) |
| Unit | Long-lived Agent (prompt, MCP, skills) | Sandboxed Task + Workspace + Model |
| Skanyxx | Chat, studio, factory PR, seed | Run / watch / stop; list Workspaces |
| Memory | MCP search/upsert per grants | Same **memory MCP** on the Workspace |
| v1 bar | **Complete** | **Thin, labeled Experimental** |

## In the umbrella (D067)

Same Helm install as kagent: subcharts **kagent**, **AX + Substrate**, and **one Dragonfly**, **on by default**. AX talks to that Dragonfly as Redis (D068). Values can disable a runtime:

| They set | What they get |
|---|---|
| both on (default) | Agents + Tasks, shared memory |
| `ax.enabled=false` | Agents only |
| `kagent.enabled=false` | Tasks + library; **no seed chat** |

AX does **not** get a second Redis. Same Dragonfly instance (D068). Cards still do not live there.

## v1 Tasks pack (enough to be real)

- In-cluster URLs filled by the chart (same as kagent)
- List Tasks / Workspaces / Models
- Run / watch / stop
- Attach **our** memory MCP to a Workspace

Not in v1: studio/factory that emits Task YAML as if it were an Agent; preview-chat against a Task.

AX may break before Google’s stable release. Experimental means that is expected.
