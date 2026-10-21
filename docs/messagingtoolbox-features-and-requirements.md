# MessagingToolbox — Features & Requirements

> Working document. Goal: a .NET + Blazor messaging toolbox, distributed as NuGet packages and a
> Docker image, that plugs into .NET Aspire and works against Azure Service Bus (cloud) and the
> ASB emulator first, with other brokers (RabbitMQ, Kafka, NATS, …) added later.

---

## 1. Key technical constraints (researched)

### Azure Service Bus emulator limitations
- Emulator ≥ 2.0.0 exposes a management plane on port `5300` via `ServiceBusAdministrationClient`
  when `UseDevelopmentEmulator=true`; the provider probes this endpoint at runtime.
- `config.json` is the optional/fallback persistence channel: entities survive restarts only if
  defined there; live create/update/delete go through the admin client when it is available.
- AMQP over TCP only (no WebSockets). Max message size 256 KB. Max 50 entities per namespace.
- Runs with a companion SQL Server Linux container as its backing store.
- Connection string shape: `Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=<key>;UseDevelopmentEmulator=true`.

**Consequence:** entity discovery on the emulator prefers the admin client, and falls back to
Aspire-modeled entities (`AddServiceBusQueue`/`AddServiceBusTopic`/`AddServiceBusSubscription`
calls) when the admin endpoint is unavailable. The older HTTP admin and SQL backend discovery
strategies have been removed now that the admin client is the supported surface.

### Repair & resubmit semantics
Service Bus has no native "move from DLQ" — every tool clones the message and sends a copy;
settling the original is a separate explicit step. Our UX must make the clone semantics explicit
(send copy → verify → remove original) to avoid duplicate processing surprises.

### Aspire integration model
- Hosting package pattern: `builder.AddMessagingToolbox("toolbox", serviceBus)` after
  `AddAzureServiceBus("messaging").RunAsEmulator()` or against a real namespace.
- Runtime values (e.g., emulator SQL password, resolved connection strings) must be read via
  `ProcessEnvironmentVariableValuesAsync` — env vars are unevaluated expressions at wiring time.
- Custom resource commands (`WithCommand`, `WithHttpCommand`) + Interaction Service allow
  dashboard buttons with typed inputs (Aspire 13.4+: typed args, validation, markdown results).
- Dashboard commands only run locally (not in ACA-hosted dashboard) — acceptable for a dev tool.

---

## 2. Architecture

### 2.1 Solution layout (as built — normative version in the architecture doc §2)

```
src/
  MessagingToolbox.Abstractions       Public provider contract: IMessagingProvider,
                                      ProviderCapabilities, broker-agnostic model, typed errors
  MessagingToolbox.Domain             Persisted tool state (connection profiles, canned messages, audit log)
  MessagingToolbox.Application        CQRS use cases (Mediator vertical slices), DTOs, templating, validation
  MessagingToolbox.Infrastructure     EF Core + SQLite persistence, Data Protection, connection catalog
  MessagingToolbox.Providers.AzureServiceBus   ASB provider: cloud + emulator as modes over one AMQP path
  MessagingToolbox.Web                Blazor Server UI (the container image)
  MessagingToolbox.ServiceDefaults    OpenTelemetry / health-check defaults
  MessagingToolbox.Aspire.Hosting     AppHost extensions: AddMessagingToolbox(...) — the NuGet package
  MessagingToolbox.AppHost            Dogfood playground (ASB emulator + toolbox)
  (Phase 4) MessagingToolbox.Providers.RabbitMQ — proves the abstraction
```

### 2.2 Provider abstraction (the important part)

```csharp
public interface IMessagingProvider   // lives in MessagingToolbox.Abstractions
{
    ProviderKind Kind { get; }
    ProviderCapabilities Capabilities { get; }   // CanManageEntities, CanManageRules, CanPeek,
                                                 // CanReceiveSettle, SupportsSessions,
                                                 // SupportsScheduling, SupportsTransactions,
                                                 // MaxMessageSizeBytes
    IAsyncEnumerable<MessagingEntity> ListEntitiesAsync(...);
    Task<EntityRuntimeInfo> GetRuntimeInfoAsync(string path, ...);        // counts, size, status
    Task<IReadOnlyList<ToolboxMessage>> PeekAsync(PeekRequest req, ...);  // from seq, count, sub-queue
    Task<IReadOnlyList<ToolboxMessage>> ReceiveAsync(ReceiveRequest req, ...); // peek-lock / R&D
    Task SendAsync(string entity, IReadOnlyList<ToolboxMessage> messages, ...);
    Task SettleAsync(SettleRequest req, ...);   // complete/abandon/defer/dead-letter by lock token
    Task<long> PurgeAsync(string entity, MessageSubQueue subQueue, ...);
    Task<int> CancelScheduledAsync(string entity, IReadOnlyList<long> sequenceNumbers, ...);
    // Management (throw NotSupportedByProviderException when the capability is absent):
    Task<MessagingEntity> CreateOrUpdateEntityAsync(EntityDefinition def, ...);
    Task DeleteEntityAsync(string path, ...);
}
```

(Normative definition: architecture doc §5.)

- UI renders strictly from `ProviderCapabilities` — emulator connections automatically hide
  management UI and show why when the emulator's admin endpoint is unreachable (entities then come
  from the AppHost model). The endpoint is re-probed on refresh, so management appears once the
  emulator is up; an unreachable broker shows as a "Broker unreachable" state, not an error.
- `ToolboxMessage` = normalized model: body (bytes + detected format), content type, system
  properties dictionary, application properties dictionary, settlement metadata
  (sequence number, delivery count, lock token, enqueued time, DLQ reason/description).

### 2.3 Hosting models
- **Aspire:** UI runs as a container (or project) resource wired by the hosting package;
  connection info injected via env vars resolved at runtime; appears as a dashboard resource
  with a URL + custom commands.
- **Standalone:** same UI in a Docker image; connections added via UI (stored in a mounted
  volume) or env/JSON config. Blazor Server keeps AMQP connections server-side in both models.

---

## 3. Functional requirements

### FR-1 Connections
- FR-1.1 Add/edit/remove saved connections: display name + connection string.
- FR-1.2 Detect emulator connections (`UseDevelopmentEmulator=true` or Aspire emulator resource)
  and downgrade capabilities accordingly.
- FR-1.3 Multiple connections open simultaneously, side-by-side trees.
- FR-1.4 (Aspire) Auto-create connections from referenced `AzureServiceBusResource`s — zero config.
- FR-1.5 (Later) Entra ID auth; (Later still) Azure sign-in with ARM namespace discovery.

### FR-2 Entity explorer
- FR-2.1 Tree: connection → queues | topics → subscriptions; every message-bearing entity gets
  **Active / Scheduled / Dead-letter** child nodes (Scheduled only when the provider declares
  `SupportsScheduling`). The transfer-DLQ is surfaced as a count in the details view, not as a
  tree node.
- FR-2.2 Per-entity runtime info: active / scheduled / dead-letter / transfer counts, size,
  status, timestamps. Counts always refresh after mutations; a manual Refresh button in the
  sidebar reloads the tree on demand. Auto-refresh is opt-in — off by default
  (`ToolboxOptions:RefreshIntervalSeconds` = 0) — via the Auto toggle (10 s fallback when the
  option is 0) or a configured interval. On connections without a
  management plane (emulator), counts are sampled over AMQP and flagged approximate when capped;
  sampled counts also say how many active messages are deferred. After the user's own send,
  copy, requeue or import, every watched entity is peeked right away (FR-2.5); sends to a topic
  refresh the counts of all its subscriptions.
- FR-2.3 Search/filter entities by name; badge entities with non-zero DLQ counts.
- FR-2.4 (Emulator) Entity list sourced from the admin client when available, otherwise from the
  Aspire model. HTTP admin and SQL backend fallback strategies have been removed.
- FR-2.5 Live watch (opt-in, ADR 15): "Watch" on a queue, subscription or sub-queue pins it. Pinned
  entities are peeked every `ToolboxOptions:WatchIntervalSeconds` (3 s) and new arrivals are
  listed in the watch panel — never in the message grid — with the ones that came after the
  user's last send highlighted. Expanding an arrival shows its body, properties and DLQ reason.
  Peek only: nothing is locked or received. Messages consumed between two peeks aren't seen, nor
  are scheduled messages becoming active (they keep their earlier sequence number); dead-letter
  watches cover the first 5000 messages.

### FR-3 Message inspection
- FR-3.1 Peek N messages from any queue/subscription/DLQ, from a chosen sequence number.
  Messages reach the grid only when the user loads them (ADR 16): selecting an entity, Refresh,
  Auto and the user's own changes never load messages. After the user settles, deletes,
  dead-letters, requeues with removal or purges, the grid drops the rows that are gone and
  marks itself stale with a "Load again" button; after a send, copy or import into the entity
  the active and scheduled views are marked stale. A settle-later load keeps the messages whose
  locks are still held (read from the lock states, not re-received).
- FR-3.2 Receive with explicit mode: peek-lock (settle later) or receive-and-delete, with a
  clear destructive-action confirmation.
- FR-3.3 Message detail view: body with format detection, JSON/XML indented by default with a
  Raw toggle,
  all system properties (MessageId, CorrelationId, SessionId, Subject, To, ReplyTo,
  ContentType, ScheduledEnqueueTime, TTL, PartitionKey), application properties, delivery
  count, sequence number, enqueued/expiry times, DLQ reason + description.
  *(v1 surfaces all of these except ScheduledEnqueueTime, TTL, and PartitionKey — they remain
  on `ToolboxMessage` and join the detail DTO as the composer/detail views mature.)*
- FR-3.4 List operations: multi-select, filter by property/value, sort by enqueue time/seq.
  Grids holding dead-lettered messages show DLQ reason and description columns; the detail view
  shows them at the top.
- FR-3.5 Session-enabled entities (ADR 12): the tree tags them with "sessions". The message view
  has a session filter (suggestions from the loaded messages) and a Session column. Peek filters
  without locking anything. Receive-and-delete, settle-later and purge work across every
  available session, or on the named session only; settle-later holds those sessions until
  their messages are settled and shows the session-lock countdown. Sessions held by another
  receiver are skipped. Session state is not supported yet.
- FR-3.6 Deferred messages (ADR 13): the grid tags them "deferred". Selecting them offers
  "Receive deferred", which receives them by sequence number with peek-lock (through their own
  session on session entities) and shows them ready to settle like a settle-later load. Messages
  that are no longer deferred, are locked elsewhere or are gone are skipped and reported.

### FR-4 Sending & editing
- FR-4.1 Composer with Monaco editor: body, content type, every system property, and a
  key/value grid for application properties, each sent with a chosen type (string, Int32,
  Int64, Boolean, Double, DateTime, Guid; ADR 14). Repair prefills keep each property's type.
  The composer keeps its draft per connection — after a send and across node switches — so the
  same message can be sent again; its target follows the selected entity until another target is
  typed.
- FR-4.2 Batch send (N copies / sequence), scheduled enqueue, session-id support. A send without
  a Session ID to an entity that requires sessions fails with an explanatory error; the composer
  marks the field as required for such entities.
- FR-4.3 Canned messages: save/name/reuse payloads incl. typed properties; folder organization.
  Rename/move in the library; edit by inserting, changing and "Update"; delete is confirmed.
- FR-4.4 Placeholder templating: `~newGuid~`, `~now±5m~` (s/m/h/d offsets), `~randomInt~` and
  `~randomInt(min,max)~`, `~env:NAME~` env-var expansion (missing variables render empty and are
  logged).
- FR-4.5 Send a copy of any peeked/received message ("resend"), edit-before-send ("repair &
  resubmit"), choose target entity (same or different queue/topic).
- FR-4.6 Explicit clone semantics in UI: "Send copy" never removes the original; "Repair,
  send & remove from DLQ" is a distinct, confirmed action.
- FR-4.7 A subscription can't be sent to on its own: sending publishes to its topic and every
  subscription whose filter matches receives the message. "Send copy" and Import on a
  subscription ask for confirmation saying so, and the composer warns when its target is a
  subscription.

### FR-5 Settlement & queue maintenance
- FR-5.1 Complete / abandon / defer / dead-letter (with reason + description) for messages
  loaded with a settleable (peek-lock) receive, by lock token. Locks expire after the entity lock
  duration unless background renewal was requested; the UI shows the remaining lock time.
  Dead-letter views loaded with peek can still be settled by sequence number (bounded DLQ-only
  scan, ADR 10).
- FR-5.2 Purge queue / subscription / DLQ (count preview + confirm; "unknown" when the
  connection cannot report counts). Deferred messages are purged too.
- FR-5.3 Delete selected messages: messages held from a settleable receive are completed by
  lock token; scheduled messages are cancelled by sequence number instead of received.
  Requeueing from a DLQ with "remove originals" removes them by lock token or, for a peeked DLQ
  view, by the bounded sequence-number scan.
- FR-5.4 Export messages to JSON file; import/replay from file to any entity. Export uses the
  already-loaded messages when available (e.g. after receive-and-delete) and never exports lock
  tokens.

### FR-6 Entity management (capability-gated)
- FR-6.1 Create/update/delete queues, topics, subscriptions.
- FR-6.2 Full settings surface: lock duration, default TTL, max size, duplicate detection
  window, require session, dead-letter on expiration, forward-to, auto-delete-on-idle,
  enable/disable (send/receive/both).
- FR-6.3 Subscription rules: list, create, edit, delete — SQL filters, correlation filters,
  rule actions.
- FR-6.4 Import/export namespace entity definitions (JSON), ServiceBusExplorer-compatible
  where feasible.
- FR-6.5 (Emulator) config.json editor — **dropped**. Emulator entities are managed via the
  AppHost model (queues/topics/subscriptions declared on the broker resource, injected into the
  toolbox as configuration) and, where capabilities allow, via the toolbox UI; there is no
  config.json editing or restart-to-apply workflow.

### FR-7 Aspire integration
- FR-7.1 `AddMessagingToolbox(name, serviceBusResource)` hosting extension; works with
  `.RunAsEmulator()` and with real namespaces (any `IResourceWithConnectionString`).
- FR-7.2 Dashboard resource with endpoint URL, `/health` health check, `WaitFor` on every
  attached broker, a persistent data volume, and a container image tag pinned to the package
  version (done). `serviceBus.WithMessagingToolbox()` is the one-line form.
- FR-7.3 Custom commands: "Open toolbox", "Send test message…", "Purge DLQ…" using
  Interaction Service typed inputs. *(Deferred — no implementation exists yet.)*
- FR-7.4 Pre-seed entity tree from AppHost-modeled queues/topics/subscriptions.

### FR-8 Developer-experience extras (post-MVP)
- Load/burst sender (rate, count, template-driven) for testing consumers.
- Simulated listener (receive + log + optional auto-complete/abandon) to observe flows.
- Message diff view (original vs repaired).
- Dark theme, keyboard-first navigation, shareable deep links to a message.

---

## 4. Non-functional requirements

- NFR-1 Cross-platform: runs on Windows/macOS/Linux (Docker); no Windows-only dependencies.
- NFR-2 Performance: paged grids over bounded fetches (in-memory paging of up to 5 000 loaded
  messages; the provider pages broker peeks of 100); emulator count sampling caps at 20 000 and
  is flagged approximate; no full-queue loads into memory.
- NFR-3 Safety: every destructive op (receive-and-delete, purge, delete, DLQ-remove) requires
  confirmation and is logged to an in-app audit panel. Imports, entity create/update/delete and
  saved-connection deletes are audited too.
- NFR-4 Secrets: connection strings never logged; masked in UI; stored protected with
  ASP.NET Core Data Protection (keys persisted under the tool's data directory); Aspire
  parameters supported.
- NFR-5 Testability: provider layer covered by integration tests against the emulator
  container (Testcontainers) and unit tests with a fake provider.
- NFR-6 Packaging: versioned NuGets (SemVer via MinVer), multi-arch Docker image
  (amd64/arm64). SBOM and image signing are deliberately deferred — the prepared slots are in
  the DevOps guidelines §7.

---

## 5. Roadmap

| Phase | Scope | Exit criteria |
|---|---|---|
| 0 — Skeleton | Core abstractions, ASB data-plane provider, minimal Blazor tree + peek/send, runs in Aspire | Can browse emulator queues and send a message from the dashboard |
| 1 — MVP | FR-1…FR-5 complete for ASB cloud + emulator; canned messages; export/import; NuGet + Docker published | Replaces ServiceBusExplorer for daily dev work |
| 2 — Management | FR-6 incl. rules, import/export of entities (emulator management via the admin endpoint already shipped in Phase 1, ADR 9) | Full management on cloud; honest degraded mode on emulator |
| 3 — Standalone | FR-1.5 connection-string standalone app; polish; docs site | `docker run` → connect to any namespace |
| 4 — Multi-broker | RabbitMQ provider (exchanges/queues/bindings, vhosts, purge, publish); abstraction hardening | Second broker proves the model |
| 5 — Cloud identity | Entra ID sign-in, ARM namespace discovery, per-user saved state | No connection strings needed |
| Backlog | Kafka (topics/offsets/consumer groups — different mental model), NATS JetStream, SQS, ActiveMQ Artemis, Event Hubs, cross-broker message copy |

Broker priority rationale: RabbitMQ first (huge install base, queue semantics closest to ASB),
then NATS JetStream or Kafka (Kafka needs a genuinely different UI: partitions, offsets,
consumer groups — treat as a separate explorer mode, not a forced fit).

---

## 6. Open questions — resolved during Phase 1

1. ~~Emulator management ambition~~ → **Decided:** emulator ≥ 2.0.0 admin client on port 5300 is
   the live management surface; Aspire-modeled entities are the offline fallback. The
   config.json editor was dropped (FR-6.5), and the HTTP admin and SQL backend discovery stubs
   have been removed.
2. ~~Persistence of connections/canned messages~~ → **Decided:** SQLite (ADR 3).
   Aspire-seeded connections come from configuration, saved ones from the database; the
   connection catalog merges both at read time.
3. ~~UI tech detail~~ → **Decided:** Blazor Web App with server interactivity (ADR 4) — AMQP
   stays server-side, identical in Aspire and standalone.
4. ~~Mono-repo vs separate provider repos~~ → **Decided for now:** mono-repo; providers stay
   here until a second broker exists. `MessagingToolbox.Abstractions` is already physically
   isolated so community providers can target it later.
5. ~~Branding/name~~ → **Decided:** "MessagingToolbox" stands. Confirm nuget.org / ghcr.io
   availability before the first publish (no package pushed yet).
