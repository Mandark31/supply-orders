# supply-orders: project brief and working rules

## How to work with me (read this first)

This is a learning project. I type all the code myself. You are my reviewer and unblocker, not the author.

- **Do not create or edit files unless I explicitly ask.** Default is read-only: read, build, run, test, inspect.
  - Standing exception: you own this `CLAUDE.md`. When I tell you a phase is done (and you have verified its exit check), update "Current status" and anything else here that changed.
- **One issue at a time.** When reviewing, list problems ordered by severity (blocker, bug, design, style), then walk me through the first one only. Wait for me before the next.
- **Hint before fix.** For a bug, first tell me where to look and why it fails. Give the full fix only if I ask or I'm still stuck after a hint.
- **When I ask for code, give it as a code block in your reply** with a short explanation of why it's written that way. I'll type it in.
- **Explain the "why" in interview terms.** After a fix, say in 1-2 lines what question this would come up under and what a follow-up could be.
- **Be honest, not reassuring.** If my approach is weak or won't scale, say so plainly and say what the tradeoff is.
- **Verify, don't assume.** Run `dotnet build` and `dotnet test` yourself before telling me something works. Show me the actual output when it fails.
- Keep replies short and concrete. No long recaps.

## Goal

Build an industrial-supply order pipeline in C# to get real, hands-on depth in: saga pattern (orchestrated), outbox, idempotent consumers, dead-letter handling, API hardening, and observability (OTel into Grafana). Output of the project is a set of "I built this and hit X" stories for system-design interviews.

Keep the repo domain-neutral: no employer names, internal terms, or real data anywhere in code, commits, or docs.

## Environment

- macOS, Apple Silicon (M5 Air), VS Code with C# Dev Kit
- .NET 10 SDK, pinned by `global.json` (`latestFeature` roll-forward)
- Docker Desktop with Rosetta for x86/amd64 emulation enabled (SQL Server image is x86-only)
- Repo root: `supply-orders/`

## Architecture

Business flow: a customer places an order for SKUs and quantities. It is fulfilled across services.

Services (max 4, do not add more):
- **Orders.Api**: `POST /orders`, owns saga state and the orchestrator (SQL Server)
- **Inventory.Service**: reserves and releases stock
- **Credit.Service**: places and releases a credit hold on the customer account
- **Shipping.Service**: creates the shipment (last step because it is the hardest to undo)

Saga (orchestrated, hand-rolled state machine persisted in an `OrderSaga` table):
`OrderCreated -> InventoryReserved -> CreditHeld -> ShipmentCreated -> Completed`

| Fails at  | Compensation                                     |
|-----------|--------------------------------------------------|
| Inventory | Mark order rejected                              |
| Credit    | Release inventory, cancel order                  |
| Shipping  | Release credit, release inventory, cancel order  |

Decisions already made:
- Orchestration, hand-rolled first. Compare against MassTransit or Temporal in a short write-up afterwards (check MassTransit's Kafka saga support before relying on it).
- Kafka for commands and replies, messages keyed by `orderId` so ordering holds per order.
- Outbox table in each service plus a publisher background worker.
- Idempotent consumers via a processed-message table keyed by message ID.
- Retry with backoff, then a dead-letter topic per consumer.
- Observability: OTel Collector fanning out to Tempo (traces), Loki (logs), Prometheus (metrics), viewed in Grafana. Datasources provisioned as YAML, not clicked in the UI.
- Controllers (not minimal APIs) so middleware, filters and GET/POST-on-one-action can be practiced.

Open decisions (ask me before assuming): Kafka client library, EF Core vs Dapper per service, exact message envelope shape.

## Target layout

```
supply-orders/
  global.json
  Directory.Build.props        # TargetFramework, Nullable, ImplicitUsings, TreatWarningsAsErrors
  src/
    Orders.Api/
    Inventory.Service/
    Credit.Service/
    Shipping.Service/
    Shared.Contracts/          # command/event records only
    Shared.Observability/      # single AddObservability() extension
  infra/
    docker-compose.yml
    otel/  tempo/  loki/  prometheus/
    grafana/provisioning/
```

## Current status

Done and committed: solution, `Orders.Api` (with `PingController`), `Shared.Contracts`, `global.json`, `Directory.Build.props`, build clean (Microsoft.OpenApi pinned to 2.7.5 to clear NU1903).
Not yet present: `infra/`, test project (needed before Phase 2's exit check).
Next: Phase 0 (Grafana stack, `Shared.Observability`, Kafka and SQL Server in compose, `/ping` in all four services).

Update this section as phases complete.

## Phases and exit checks

Each phase is roughly one sitting. Do them in order. If a phase runs over, cut features, not phases. When I say a phase is done, verify the exit check yourself before agreeing.

**Phase 0: Skeleton**
1. Compose with Grafana, OTel Collector, Tempo, Loki, Prometheus. All three datasources healthy.
2. `Shared.Observability` written once, used from `Orders.Api`.
3. Kafka (KRaft) and SQL Server added to compose.
4. Same wiring in the other three services, each with a trivial `/ping`.
- Exit: `/ping` on each service; in Grafana, find the trace in Tempo and jump to the matching logs in Loki (`tracesToLogs` configured, trace ID logged).
- Known traps: Kafka needs separate internal and external listeners; SQL Server needs about 2 GB (set a compose memory limit and check Docker Desktop RAM).

**Phase 1: Happy path**
- Commands and replies over Kafka keyed by `orderId`; orchestrator drives the flow.
- Exit: a placed order reaches `Completed`, visible in the `OrderSaga` table.

**Phase 2: Reliability**
- Outbox plus publisher worker in each service; idempotent consumers; retry with backoff; DLQ per consumer.
- Exit: kill a consumer mid-message, restart it, no duplicate effects (verify with row counts, not by eye).

**Phase 3: Compensation and failure injection**
- A header or config flag forces failure at any step; also inject a poison message and a stuck step (timeout).
- Exit: five scripted scenarios all end in a consistent state (no leaked reservations or credit holds, saga in a terminal state).

**Phase 4: API hardening**
- Global exception handler returning `ProblemDetails`; custom middleware (correlation ID, request logging); built-in rate limiter on the order endpoint; `Idempotency-Key` on `POST /orders`.
- Exit: I can explain each piece with a class skeleton and its registration order in `Program.cs`.

**Phase 5: Observability**
- Traces that survive Kafka hops (`traceparent` propagated in message headers), metrics (saga duration, compensation count, DLQ depth), structured logs with order ID, one alert rule ("saga stuck in a non-terminal state for more than N minutes").
- Exit: I can follow one failed order from a trace to its logs.

**Phase 6: Write the stories**
- One page per phase: what broke, what I changed, the tradeoff. Written by me.

## Review checklist (apply when I ask you to check my work)

General:
- `dotnet build` clean with warnings as errors; `dotnet test` passes
- No secrets, connection strings or real data committed; `.gitignore` covers `bin/`, `obj/`
- Nullable warnings not suppressed with `!` without a reason

Services and messaging:
- Every consumer is idempotent; every handler can be retried safely
- State change and event publish happen in the same DB transaction (outbox), never as two separate steps
- Compensations are idempotent, run in reverse order, and are themselves retryable
- Kafka messages keyed by `orderId`; consumer commits offsets only after the work is durably done
- No blocking calls (`.Result`, `.Wait()`) in async paths; `CancellationToken` passed through

API:
- Middleware order in `Program.cs` is deliberate (exception handler first, then correlation ID, rate limiter, auth, then endpoints)
- No unhandled exceptions leak stack traces to clients
- DI lifetimes are correct (no scoped service captured by a singleton or hosted service)

Observability:
- Trace context propagated through Kafka headers; logs carry trace ID and order ID
- Metrics have bounded label cardinality (never label by order ID)

Infra:
- `docker compose up` works from a clean checkout with one command
- Ports and listeners documented; healthchecks defined

## Git conventions

- One branch or commit series per phase, named `phase-N-short-name`
- Small commits with plain-English messages, written by me
- Do not commit or push for me unless I ask

## Interview mapping (for context when explaining things)

The mock interviews used follow-up ladders: design it, then memory, then corrupt record, then monitoring, then who gets alerted. When I ask "why", answer at that depth and finish with one likely follow-up question I should be able to answer.
