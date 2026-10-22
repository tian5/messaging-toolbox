# MessagingToolbox — Architecture Guidelines

> Working architecture document for the MessagingToolbox. Companion to
> `messagingtoolbox-features-and-requirements.md` (what we build) and
> `messagingtoolbox-code-style-conventions.md` (how we write it). Clean Architecture,
> .NET 10, Blazor Server, Aspire-first distribution.

---

## 1. Principles

1. **Clean Architecture** — Domain at the center; Application defines use cases and contracts;
   Infrastructure and Providers implement them; Web is a thin shell. Dependencies point inward only.
2. **Provider model** — every broker (ASB cloud, ASB emulator, later RabbitMQ/Kafka/NATS) is a
   provider behind one contract. The UI and use cases never reference a broker SDK.
3. **Capability-driven UI** — providers declare capabilities (`ProviderCapabilities`); the UI
   renders only what the active connection supports. Emulator degrades gracefully instead of
   failing.
4. **Clone semantics made explicit** — resubmit/repair always clones; settling the original is a
   separate, confirmed action. Destructive operations always confirm.
5. **Same app, two hosts** — Aspire container resource and standalone Docker image run the
   identical Web host; only configuration differs.
6. **Style per `messagingtoolbox-code-style-conventions.md`** — CQRS vertical slices with Mediator,
   per-layer `DependencyInjection.cs`, file-scoped namespaces, explicit types, records for
   carriers.

---

## 2. Solution structure

```
MessagingToolbox.slnx
Directory.Build.props            # net10.0, ImplicitUsings, Nullable, IsPackable=false default
Directory.Packages.props         # central package management
.editorconfig / .gitattributes / .gitignore / .dockerignore / global.json
src/
  MessagingToolbox.Abstractions/    # public provider contract surface (DevOps decision #3)
  MessagingToolbox.Domain/
  MessagingToolbox.Application/
  MessagingToolbox.Infrastructure/
  MessagingToolbox.Providers.AzureServiceBus/
  MessagingToolbox.Web/
  MessagingToolbox.AppHost/
  MessagingToolbox.ServiceDefaults/
  MessagingToolbox.Aspire.Hosting/
tests/
  MessagingToolbox.Domain.Tests/
  MessagingToolbox.Application.Tests/
  MessagingToolbox.Providers.AzureServiceBus.Tests/   # unit tests + Testcontainers emulator suite
  MessagingToolbox.Architecture.Tests/                # dependency rules
  Directory.Build.props                                # IsPackable=false (inherited anyway)
```

---

## 3. Layers and dependency rules

| Layer | Depends on | May contain | Must not contain |
|---|---|---|---|
| Abstractions | nothing | `IMessagingProvider`, `ProviderCapabilities`, broker-agnostic message/entity model, typed errors | any framework/SDK dependency, any other layer |
| Domain | Abstractions (for `ProviderKind` only) | persisted tool state (entities), constants | any framework/SDK dependency |
| Application | Domain, Abstractions | use cases (Mediator), DTOs, interfaces, templating, validation; EF Core queries against `IToolboxDbContext` (ADR 7) | ASP.NET, broker SDKs, UI |
| Providers.AzureServiceBus | Application, Abstractions | Azure.Messaging.ServiceBus, management client, emulator discovery | UI, persistence, other brokers |
| Infrastructure | Application (Domain transitively) | EF Core (SQLite) persistence of tool state, Data Protection | broker SDKs |
| Web | Application (+ Infrastructure/Providers/ServiceDefaults for DI wiring only) | Blazor components, view state | business logic, direct DbContext/provider use |
| Aspire.Hosting | Aspire.Hosting SDK (+ Aspire.Hosting.Azure.ServiceBus to read the modeled entities) | resource + AppHost extensions | toolbox business logic |

Rules are enforced by `MessagingToolbox.Architecture.Tests` (NetArchTest):
Abstractions has no dependency on any other layer; Domain/Application have no external SDK
references (Azure SDK, ASP.NET — Application deliberately keeps its EF Core reference, ADR 7);
providers implementing `IMessagingProvider` reference Abstractions, never the other way around;
Infrastructure and Web carry no broker-SDK dependencies; Web components never reference
Infrastructure/Providers types (DI wiring lives in `Program.cs`); handlers never reference ASP.NET.

---

## 4. Model: the Abstractions contract and Domain entities

The broker-agnostic model lives in **Abstractions** — the public contract surface a community
provider can target (DevOps decision #3). **Domain** holds only the persisted tool state.

```
Abstractions/
  Messaging/
    MessagingEntity.cs        # discriminated model: Queue | Topic | Subscription | Exchange
                              # (path, kind, status, PeekMode NonDestructive/Requeue — ADR 17)
    EntityRuntimeInfo.cs      # active/scheduled/dead-letter/transfer counts, size, timestamps;
                              # IsApproximate marks sampled counts that hit the cap;
                              # DeferredMessageCount (of the active ones) when sampled
    ToolboxMessage.cs         # body (byte[]), content type, typed system props, app props,
                              # settlement metadata (sequence number, State Active/Deferred/Scheduled,
                              # lock token, delivery count, enqueued time, DLQ reason/description),
                              # RoutingKey, read-only BrokerProperties for display (ADR 17)
    SubscriptionRuleInfo.cs   # filter kind (SQL/correlation), expression, action
    ProviderCapabilities.cs   # record of flags, see below
    Enums.cs                  # EntityKind, MessageSubQueue (Active/Scheduled/DeadLetter/TransferDeadLetter),
                              # MessageState, SettlementOperation, ReceiveMode, ProviderKind, MessagingEntityStatus,
                              # PeekMode
    PeekRequest.cs / ReceiveRequest.cs / SettleRequest.cs / SettleBySequenceRequest.cs
    ReceiveDeferredRequest.cs / DeferredMessageReference.cs
    EntityDefinition.cs / ResubmitOverrides.cs
  Errors/                     # MessagingProviderException and its subclasses:
                              # NotSupportedByProvider / MessagingEntityNotFound / MessageLockLost /
                              # MessagingProviderThrottled / MessagingProviderUnavailable
  IMessagingProvider.cs       # the provider contract (§5)
  TelemetryConstants.cs       # the shared ActivitySource name

Domain/
  Entities/                   # persisted tool state (EF Core)
    EntityBase.cs             # Guid Id, DateTimeOffset CreatedAt/ModifiedAt
    ConnectionProfile.cs      # display name, ProviderKind, protected connection string
    CannedMessage.cs          # named reusable payload + properties (JSON) + folder
    AuditEntry.cs             # destructive-operation record (action, connection, entity, sub-queue, details)
  Common/
    AppConstants.cs
```

```csharp
public sealed record ProviderCapabilities   // required init properties (ADR 17)
{
    bool CanManageEntities;            // create/update/delete queues, topics, subscriptions, exchanges
    bool CanManageRules;               // routing rules: subscription filters/actions, exchange bindings
    bool CanPeek;                      // inspection without consuming; cost per entity: MessagingEntity.PeekMode
    bool CanReceiveSettle;             // settleable receive + complete/abandon (+ dead-letter/defer where supported)
    bool SupportsSessions;
    bool SupportsScheduling;
    bool SupportsTransactions;
    bool SupportsDeadLetterSubQueue;   // broker-managed DLQ per entity (the tree's DLQ node, "To DLQ")
    bool SupportsDeferral;             // defer + receive deferred by sequence number (ADR 13)
    bool SupportsSequenceNumbers;      // peek paging, live-watch cursor, settle/cancel by sequence number
    bool SupportsLockExpiry;           // held messages expire and can be renewed (ADR 10)
    long MaxMessageSizeBytes;          // 256 KB on emulator, 100 MB premium ASB, etc.
    string? ManagementUnavailableReason; // optional: why CanManageEntities is off, shown in the UI
}
```

`ProviderCapabilities` is the heart of the design: every Blazor feature area and every
management command handler checks it before acting. The cloud profile returns
`CanManageEntities: true`. The emulator profile starts as `CanManageEntities: false` and is
flipped to `true` at runtime when the ≥ 2.0.0 admin endpoint responds; until then
`ManagementUnavailableReason` says why. The UI renders management actions only when the
capability is present and otherwise shows that reason.

---

## 5. Application layer

### Contracts

`IMessagingProvider` lives in **Abstractions** (it is the public contract); the remaining
interfaces live in `Application/Core/Interfaces`.

```csharp
public interface IMessagingProvider : IAsyncDisposable   // Abstractions
{
    ProviderKind Kind { get; }
    ProviderCapabilities Capabilities { get; }
    IAsyncEnumerable<MessagingEntity> ListEntitiesAsync(CancellationToken ct);
    Task<EntityRuntimeInfo> GetRuntimeInfoAsync(string entityPath, CancellationToken ct);
    Task<IReadOnlyList<ToolboxMessage>> PeekAsync(PeekRequest request, CancellationToken ct);       // optional SessionId filter, lock-free (ADR 12)
    Task<IReadOnlyList<ToolboxMessage>> ReceiveAsync(ReceiveRequest request, CancellationToken ct); // optional SessionId (ADR 12)
    Task SendAsync(string entityPath, IReadOnlyList<ToolboxMessage> messages, CancellationToken ct);
    Task<IReadOnlyList<ToolboxMessage>> ReceiveForSettlementAsync(string entityPath, MessageSubQueue subQueue,
        int maxMessages, TimeSpan? maxWaitTime, CancellationToken ct,
        TimeSpan? lockRenewalDuration = null,                  // opt-in background lock renewal (ADR 10)
        string? sessionId = null);                             // session entities hold their sessions (ADR 12)
    Task<IReadOnlyList<ToolboxMessage>> ReceiveDeferredForSettlementAsync(ReceiveDeferredRequest request,
        CancellationToken ct);                                 // deferred messages by sequence number, held like settle-later (ADR 13)
    Task<IReadOnlyDictionary<string, DateTimeOffset>> GetHeldLockStatesAsync(string entityPath,
        MessageSubQueue subQueue, IReadOnlyList<string> lockTokens, CancellationToken ct);  // lock expiry per held token
    Task<IReadOnlyList<string>> SettleAsync(SettleRequest request, CancellationToken ct);  // returns settled lock tokens
    Task<IReadOnlyList<long>> SettleBySequenceNumbersAsync(SettleBySequenceRequest request,
        CancellationToken ct);                                 // DLQ sub-queues only (ADR 10); returns settled sequences
    Task<long> PurgeAsync(string entityPath, MessageSubQueue subQueue, CancellationToken ct);  // returns deleted count; drains every available session, then deferred messages
    Task<IReadOnlyList<long>> CancelScheduledAsync(string entityPath, IReadOnlyList<long> sequenceNumbers, CancellationToken ct);
    Task<MessagingEntity> CreateOrUpdateEntityAsync(EntityDefinition definition, CancellationToken ct);
    Task DeleteEntityAsync(string entityPath, CancellationToken ct);
    Task<EntityDefinition> GetEntityDefinitionAsync(string entityPath, CancellationToken ct);
    Task<ToolboxMessage> PrepareResubmitAsync(string sourceEntityPath, MessageSubQueue sourceSubQueue,
        ToolboxMessage original, ResubmitOverrides overrides, CancellationToken ct);
}

public interface IMessagingProviderFactory   // implemented once, in Infrastructure (ADR 17)
{
    // Resolves (and caches) the provider + underlying clients for a connection profile.
    Task<IMessagingProvider> GetProviderAsync(Guid connectionId, CancellationToken ct);
    Task DisposeAsync(Guid connectionId);   // dispose cached clients on profile change/removal
}

public interface IMessagingProviderBuilder   // one per broker, registered by its provider project
{
    ProviderKind Kind { get; }
    Task<IMessagingProvider> CreateAsync(ConnectionInfo connection, CancellationToken ct);
}

public interface IToolboxDbContext : IAsyncDisposable   // single context — the read/readonly split was dropped (ADR 7)
{
    DbSet<ConnectionProfile> ConnectionProfiles { get; }
    DbSet<CannedMessage> CannedMessages { get; }
    DbSet<AuditEntry> AuditEntries { get; }
    Task<int> SaveChangesAsync(CancellationToken ct);
}

public interface IToolboxDbContextFactory   // one short-lived context per operation (ADR 11)
{
    Task<IToolboxDbContext> CreateDbContextAsync(CancellationToken ct);
}

public interface IConnectionCatalog   // read model merging config-seeded (Aspire) + saved connections
{
    ValueTask<IReadOnlyList<ConnectionInfo>> GetAllAsync(CancellationToken ct);
    ValueTask<ConnectionInfo?> GetByIdAsync(Guid connectionId, CancellationToken ct);
}

public interface IConnectionStringProtector   // protects connection strings at rest
{
    string Protect(string plainText);
    string Unprotect(string protectedText);
}

public interface IMessageTemplateEngine
{
    string Render(string template);   // ~newGuid~, ~now+5m~, ~randomInt(min,max)~, ~env:NAME~
}
```

Client caching rule: AMQP clients (`ServiceBusClient`) are expensive — the factory caches one
provider per connection profile (singleton cache, `DisposeAsync` evicts on profile
change/removal), while Mediator handlers remain scoped. Provider creation is shared and never
captures the caller's cancellation token; a failed creation is evicted so the next call retries.
The cached provider keeps an in-memory snapshot of original SDK messages for faithful resubmit
and the peek-lock receivers and locked-message table behind
`ReceiveForSettlementAsync`/`SettleAsync`. Locks expire after the entity lock duration unless the
receive requested `lockRenewalDuration`, in which case a provider-owned background loop renews
them via `RenewMessageLockAsync` until that deadline (ADR 10); `GetHeldLockStatesAsync` reports
the current expiry so the UI can show a lock countdown. Browsing is peek-only; plain
`ReceiveAsync` is receive-and-delete. Blazor Server circuits never hold broker clients directly;
they go through Mediator → factory.

### Feature slices (`Features/`)

```
Connections/    GetConnections, SaveConnection, DeleteConnection, TestConnection
Explorer/       GetEntityTree, GetEntityRuntimeInfo, RefreshCounts
Messages/       PeekMessages, ReceiveMessages, ReceiveForSettlementMessages, GetHeldLockStates,
                SendMessage, ResendMessage, RepairAndResubmitMessage, RequeueMessages,
                SettleMessages, SettleBySequence, PurgeEntity, GetPurgePreview,
                DeleteMessages, DeadLetterMessages, ExportMessages, ImportMessages
Templates/      GetCannedMessages, SaveCannedMessage, DeleteCannedMessage, RenderTemplate
Audit/          GetAuditEntries
Management/     CreateOrUpdateEntity (queues, topics, subscriptions), DeleteEntity,
                GetEntityDefinition; (FR-6.3) UpsertSubscriptionRule, DeleteSubscriptionRule
```

Slices raise Mediator notifications (`Features/*/Notifications/`) when shared UI state must
refresh: saved connections, canned messages, the entity list, and new audit entries.

Shared services next to the slices (`Services/`, `Common/`): `AuditLog` (central write of
destructive-operation records, plus imports, entity create/update/delete and saved-connection
deletes), `MessageTemplateEngine`, `MessageBodyFormatDetector`, `MessageDtoMapper`, and
`ApplicationPropertyValues` / `CannedPropertiesJson` for typed application properties (ADR 14). Settlement is surgical: `SettleAsync` touches exactly the messages whose
lock tokens were obtained from `ReceiveForSettlementAsync`. Two bounded scans exist, both
deliberate: `SettleBySequenceNumbersAsync` on dead-letter sub-queues only (ADR 10) — used by
`SettleBySequence` and by `RequeueMessages` to remove originals from a DLQ view loaded with peek —
and the scheduled dead-letter marker sweep in `DeadLetterMessages` (ADR 8). `DeleteMessages` and
`DeadLetterMessages` on non-scheduled sub-queues act on lock tokens only; scheduled messages are
cancelled via `CancelScheduledAsync`.

Management handlers must check `provider.Capabilities` first; the provider boundary throws
`NotSupportedByProviderException` when the flag is missing, and the UI renders it as an
explanatory hint — raw SDK exceptions never reach the UI (they are translated in the provider,
§9). DTOs in `Common/Dtos/`, one per feature result, records.

---

## 6. Provider implementation (`Providers.AzureServiceBus`)

One project covers both ASB modes; the mode comes from the connection profile:

```
Providers.AzureServiceBus/
  AzureServiceBusProvider.cs          # IMessagingProvider over Azure.Messaging.ServiceBus
  AzureServiceBusCapabilities.cs      # Cloud vs Emulator capability profiles
  AzureServiceBusManagement.cs        # ServiceBusAdministrationClient wrapper (cloud + emulator)
  IAzureServiceBusManagement.cs       # test seam for the management wrapper
  AzureServiceBusMessageMapper.cs     # SDK message <-> ToolboxMessage mapping
  AzureServiceBusProviderBuilder.cs   # IMessagingProviderBuilder for ProviderKind.AzureServiceBus
  ServiceBusEntityPath.cs             # "{topic}/subscriptions/{name}" path splitting
  Telemetry.cs                        # shared ActivitySource
  Emulator/
    EmulatorDetection.cs              # UseDevelopmentEmulator=true / Aspire resource detection
    EmulatorAdminConnectionString.cs  # derives sb://host:5300 admin connection string
    IEmulatorEntityDiscovery.cs       # strategy contract
    AspireModelDiscovery.cs           # entities injected from AppHost model (offline fallback)
    IEmulatorRuntimeCountSampler.cs   # runtime counts without usable admin runtime properties
    PeekRuntimeCountSampler.cs        # bounded-peek counting; caps at 20k, flags IsApproximate
  Config/                              # (removed) config.json editor dropped with FR-6.5 —
                                       # entities come from the Aspire model + toolbox UI
  Errors/ServiceBusExceptionTranslator.cs   # SDK exceptions -> typed provider errors
  DependencyInjection.cs
```

`AzureServiceBusManagement` now serves cloud and emulator by swapping to the admin endpoint for
emulator connections: the host-supplied `ToolboxOptions:Connections:{i}:AdminEndpoint` when present
(the AppHost passes the emulator's `emulatorhealth` endpoint, because Aspire maps container port
5300 to a dynamic host port when the toolbox runs from source), otherwise `:5300` on the
data-plane host. The provider probes that endpoint by reading the first page of queues — any
answer counts, so an empty emulator enables management. On success it flips the emulator
capability profile to `CanManageEntities: true` for good. A failed probe records
`ManagementUnavailableReason` and is retried on the next tree load or management call once 15 s
have passed, so management appears when a slow-starting emulator comes up; the UI re-reads the
connection's capabilities after each tree load while management is off. When every entity's
count sample fails to connect, the tree load throws `MessagingProviderUnavailableException` and
the sidebar shows "Broker unreachable" instead of a count-less tree; one failing entity only loses
its own counts. Discovery is now admin client first,
then Aspire-modeled entities as the offline fallback; the HTTP admin and SQL backend stubs have
been removed because the admin client is the supported surface.

Runtime counts on the emulator still come from `PeekRuntimeCountSampler` because the emulator's
admin runtime properties report 0. The sampler pages bounded peeks per queue/subscription (topics
carry no counts of their own — their subscriptions do), and counts that hit the sampling cap come
back flagged `IsApproximate`. Samples are cached per entity for 5 s (short enough for the 10 s
auto-refresh and manual refreshes to see fresh counts), invalidated after every mutating operation
on that entity — a send or scheduled-cancel on a topic invalidates all of its subscriptions, and
because the emulator copies topic messages into subscriptions asynchronously (tens of
milliseconds, verified live), subscription walks wait until 500 ms after the topic's invalidation
before peeking, instead of caching the pre-send count — and concurrent requests for one entity share a single walk, so tree refreshes only re-sample what
actually changed. Invalidation bumps a per-entity generation, so a walk already in flight when the
mutation happens never caches its stale result; walks run on the sampler's lifetime token, so one
cancelled caller cannot cancel the others sharing the walk. Each walk opens and disposes its own
receiver — a reused receiver's peek resumes where the previous walk ended on the emulator and
undercounts. The provider samples at most four entities in parallel because the emulator
throttles under load. Browsing performs only peeks; settlement requires an explicit settleable
receive. Its locks expire after the entity lock duration unless background renewal was requested
(ADR 10), and un-settled messages simply return to the queue.

Session-enabled entities (ADR 12) need no special browsing path: a plain receiver peeks across
every session, so peek, its `SessionId` filter (applied while paging) and count sampling stay
lock-free. Receives on the active sub-queue go through session receivers: the named session, or
accept-next over each available session until the budget is filled. Accept-next ends when no
session is free — the cloud reports `ServiceTimeout`, the emulator only stops when the caller's
short timeout cancels it. Receive-and-delete and purge close each session right after draining
it. A settle-later receive keeps each session receiver that returned messages, settles through
it, reports and renews the *session* lock (session messages carry no lock of their own), and
closes it as soon as its last received message is settled, or when the session lock is lost or
has expired. The provider learns which entities require sessions from management listings, and
otherwise from the broker rejecting a non-session receive or a session-less send (both arrive as
`InvalidOperationException`; `ServiceBusExceptionTranslator` recognizes their wording). Dead-letter
sub-queues are never session-bound.

Deferred messages (ADR 13) stay in their sub-queue but every receive skips them; peek reports
them with `State = Deferred`, and the sampler reports how many of the active messages are
deferred. `ReceiveDeferredForSettlementAsync` gets them back by sequence number through the same
cached peek-lock receiver and locked-message table as a settle-later receive, so settlement,
renewal and lock states need nothing new. The broker locks a deferred batch all-or-nothing, so a
batch rejected with `MessageNotFound` is retried one sequence number at a time and the messages
that still fail (not deferred, locked elsewhere, gone) are skipped. On a session entity's active
sub-queue each message is received through its own session — the one already held, or the named
session accepted and then held as for settle-later; a session locked elsewhere is skipped. A
plain receiver's deferred receive on a session entity fails with `SessionLockLost` on the
emulator, which the provider treats as the session requirement. Purge drains as before, then
peeks for the deferred leftovers and deletes them by sequence number with receive-and-delete.

Keeping all emulator quirks in this project
is what lets Application stay broker-agnostic. A future `Providers.RabbitMQ` implements the same
contract — that is the abstraction's proof case.

---

## 7. Infrastructure and Web

**Infrastructure:** `ToolboxDbContext` (EF Core + SQLite, file in a mounted volume) stores
connection profiles, canned messages, and the audit log of destructive operations. Auditing
(`CreatedAt`/`ModifiedAt`) is centralized in the DbContext via
`ChangeTracker.Entries<EntityBase>()`, driven by the injected `TimeProvider`; `DateTimeOffset`
values go through a binary converter (SQLite limitation workaround). Handlers query through the single `IToolboxDbContext`
directly (ADR 7), each operation on its own context from `IToolboxDbContextFactory` (ADR 11). `ConnectionCatalog` merges config-seeded (Aspire/env) and saved connections at
read time. `Messaging/MessagingProviderFactory` is the singleton provider cache: it dispatches
creation to the `IMessagingProviderBuilder` registered for the connection's kind (each provider
project adds its builder with `TryAddEnumerable`) and owns eviction and disposal (ADR 17). Connection strings are stored protected (ASP.NET Core Data Protection, keys persisted
under the data directory), never logged. `ToolboxDbInitializer` creates the schema on startup
(`EnsureCreated` for now — real migrations once the schema stabilizes) and never takes the host
down on failure.

**Web (Blazor Server):**

```
Components/
  Layout/                # MainLayout, ConnectionPanel, ConnectionEditor
  Explorer/              # EntityTree, EntityDetails, CountsBadge
  Messages/              # MessageGrid (paged in memory), MessageDetail, MessageBody (pretty/raw),
                         # MessageComposer (Monaco), SettleBar, LiveWatchPanel
  Management/            # EntityEditor (implemented), RuleEditor arrives with FR-6.3
  Templates/             # CannedMessagesPanel
  Shared/                # ConfirmDialog, CapabilityGate.razor, AuditPanel, MonacoEditor
  Pages/                 # Home (the explorer), Error, NotFound
```

- Components inject `ISender` only (a `ToolboxComponentBase : ComponentBase` exposes
  `protected ISender Mediator`, mirroring `ApiControllerBase`).
- `CapabilityGate` component wraps management UI: shows child content or an explanatory hint.
- Timed auto-refresh is opt-in (off by default) via a scoped `PollingRefreshService` raising
  events; the tree also refreshes after every mutation and on a manual Refresh button. AMQP
  pressure stays in the provider layer — emulator runtime counts are TTL-cached and invalidated
  on mutation, so repeated refreshes rarely hit the broker.
- The composer's state is a `ComposerDraft` (`Services/Session/`) held per connection
  workspace, so it survives sends, node switches and the component being re-created; repair
  gets its own draft. Sends to a subscription path publish to its topic, so the UI warns or asks
  for confirmation wherever that can happen (FR-4.7).
- The live watch is a scoped `LiveWatchService` (`Services/Session/`) with its own timer that
  runs only while something is pinned. It peeks through `PeekMessagesQuery`, so browsing stays
  side-effect free, and its arrivals feed the `LiveWatchPanel`, not the message grid (ADR 15).
- No ApiService project in v1 — Blazor calls Mediator in-process. If a CLI/REST API is ever
  needed, extract `{Product}.ApiService` then; Application needs no changes.

---

## 8. Aspire.Hosting package (the shippable NuGet)

```
Aspire.Hosting/
  MessagingToolboxResource.cs           # ContainerResource (image from ghcr.io)
  MessagingToolboxExtensions.cs         # AddMessagingToolbox(name, serviceBus) +
                                        # serviceBus.WithMessagingToolbox() sugar +
                                        # WithDataVolume +
                                        # WithMessagingToolboxConnection (run-from-source);
                                        # connection info and AppHost-modeled entities flow in
                                        # as env vars resolved at runtime
  MessagingToolboxContainerImageTags.cs # registry/image coordinates; tag = package version
                                        # for v* tag builds, else `edge`
  MessagingToolboxConnectionsAnnotation.cs # per-host next-connection index + attached brokers
```

- `AddMessagingToolbox("toolbox", messaging)` takes `IResourceBuilder<IResourceWithConnectionString>`,
  so the same overload serves cloud namespaces and `.RunAsEmulator()`; connection info flows in
  as env vars resolved at runtime (never at wiring time — expressions are unevaluated then).
- `WithMessagingToolboxConnection` applies the identical wiring to a project resource, so
  `MessagingToolbox.Web` can run from source under the debugger (the repo AppHost switches on
  `Toolbox:RunFromSource`). It can also be called repeatedly on any toolbox host — each call
  consumes the next `ToolboxOptions__Connections__{i}` index (tracked on the resource via
  `MessagingToolboxConnectionsAnnotation`), so multiple namespaces compose without colliding;
  attaching the same broker twice is a no-op. Each attached broker is also a `WaitFor`
  dependency when the host supports waiting, so the toolbox starts once the broker (for the
  emulator: including its SQL companion) is healthy.
- `AddMessagingToolbox` adds an HTTP health check on `/health` (mapped in every environment by
  ServiceDefaults) and a named volume on `/app/data` via `WithDataVolume()`, so SQLite state and
  the Data Protection keys that decrypt saved connection strings survive container recreation.
  Calling `WithDataVolume("name")` replaces the generated volume.
- `serviceBus.WithMessagingToolbox()` is the one-line form: it adds a `messaging-toolbox`
  container, or attaches the broker to the one already in the AppHost, so several namespaces
  share one toolbox. It adds nothing in publish mode — the toolbox is a development tool.
- The image tag is the package version when the package was built from a `v*` tag (the project
  stamps `MinVerVersion` into assembly metadata when `GITHUB_REF_TYPE` is `tag`, and the publish
  workflow pushes the same version as an image tag); every other build uses `edge`. Override with
  `.WithImageTag(...)`.
- When the AppHost models queues/topics (`AddServiceBusQueue` etc.), their names are injected
  too — this is the preferred emulator discovery channel (see §6).
- For an emulator broker, the admin endpoint (`emulatorhealth`, container port 5300) is passed as
  `ToolboxOptions__Connections__{i}__AdminEndpoint`. It is an endpoint reference, so it resolves to
  the container-network address for the container and to the mapped host port for a project
  resource (see §6).
- Dashboard commands ("Open toolbox", "Send test message…", "Purge DLQ…") with Interaction
  Service typed inputs are designed but deferred — nothing is implemented yet. They remain
  documented as local-only (not available on ACA-hosted dashboards).

---

## 9. Cross-cutting concerns

- **DI:** per-layer `DependencyInjection.cs` with `Add{Layer}(IServiceCollection, IConfiguration)`
  (C# 14 `extension` members in Infrastructure/Providers; Application's registration predates
  them and still uses the classic syntax — unify opportunistically), nothing registered
  elsewhere. `TimeProvider.System` as singleton.
- **Validation:** FluentValidation validators live next to their command and run as a Mediator
  pipeline behavior; a second behavior logs handler errors before rethrowing. No inline
  validation or try/log noise inside handlers (ADR 8).
- **Options:** `AddOptions<T>()` + `GetSection(nameof(T))`; toolbox settings in
  `appsettings.json`, overridable by env vars in the container.
- **Observability:** OpenTelemetry via ServiceDefaults; an `ActivitySource` named
  `MessagingToolbox` around provider operations (peek/send/settle) so Aspire dashboard shows
  toolbox telemetry next to the app's own traces. Structured logging, connection strings masked.
- **Error handling:** provider SDK exceptions are translated at the provider boundary
  (`ServiceBusExceptionTranslator`) into typed errors under `MessagingProviderException`
  (`NotSupportedByProviderException`, `MessagingEntityNotFoundException`,
  `MessageLockLostException`, `MessagingProviderThrottledException` for `ServiceBusy`, and
  `MessagingProviderUnavailableException` for `ServiceTimeout` / `ServiceCommunicationProblem` /
  an unreachable admin endpoint — e.g. an emulator that is still starting). Rejected credentials
  (`UnauthorizedAccessException`) and every other failure reason become a
  `MessagingProviderException` naming the entity; providers catch with the translator's
  `CanTranslate` filter, so no SDK exception type escapes. The UI renders
  `NotSupportedByProviderException` as an explanatory hint and the throttled error as a
  wait-and-retry hint; unexpected exceptions surface as an error bar.
- **Safety:** destructive operations (receive-and-delete, purge, delete, DLQ-remove) require
  confirmation in UI and write to the audit log via the central `AuditLog` writer
  (who/what/when, entity, sequence numbers).

---

## 10. Testing strategy

| Project | Scope | Tools |
|---|---|---|
| Domain.Tests | entity defaults and invariants | xUnit, Shouldly |
| Application.Tests | handler logic — substitutes for broker-facing interfaces, real in-memory SQLite (`SqliteTestBase`) for persistence handlers | xUnit, NSubstitute, Shouldly, FakeTimeProvider, Microsoft.Data.Sqlite |
| Providers.AzureServiceBus.Tests | provider unit tests (capabilities, mapping, discovery chain, exception translation); real AMQP round-trips against the emulator container via Testcontainers (skipped when Docker/image is unavailable) | xUnit, NSubstitute, Shouldly, Testcontainers |
| Architecture.Tests | dependency rules of §3 incl. the Abstractions amendment | NetArchTest |
| (later) Web e2e | critical flows (connect → peek → send) | Playwright |

---

## 11. Design decisions (ADR log)

1. **No ApiService in v1** — Blazor Server calls Mediator in-process;
2. **One ASB provider project, emulator as a mode** — 95% shared AMQP code; only discovery and
   management differ, isolated behind strategies.
3. **SQLite for tool state** — a tool should be self-contained. *(Amended by ADR 7: the
   read/readonly DbContext split originally kept here was dropped; the single
   `IToolboxDbContext` remains the storage boundary, so storage can still move to Postgres
   later.)*
4. **Blazor Server (not WASM)** — broker connections must stay server-side; identical hosting in
   Aspire and standalone.
5. **Capability flags over provider-type switches** — UI and handlers branch on capabilities,
   so future brokers (and future emulator improvements) slot in without conditionals everywhere.
6. **Clone-based resubmit, always explicit** — matches Service Bus semantics; no hidden deletes.
7. **Validation in the Mediator pipeline** — FluentValidation validators run as a pipeline
   behavior (with a separate error-logging behavior), so handlers contain no cross-cutting
   code and invalid requests never reach broker calls.
8. **Settlement via explicit settleable receive (lock tokens)** — brokers offer no side-effect-free
   "settle by sequence number" primitive (`ReceiveDeferredMessageAsync` locks only explicitly
   deferred messages — verified against the emulator), so settlement is explicit: the UI loads
   messages with a settleable (peek-lock) receive, and `SettleAsync` settles exactly the held
   lock tokens. Locks are never renewed — they expire after the entity lock duration and
   un-settled messages return to the queue (+1 delivery). Browsing stays peek-only (free), and
   there is no scan-and-abandon primitive. *(Amended by ADR 10: opt-in lock renewal and a
   DLQ-only scan-and-settle.)* Scheduled messages can never be received, so they are
   cancelled via `CancelScheduledAsync` (delete) — or cancelled, re-sent with a marker property,
   and dead-lettered via a bounded receive-and-abandon marker sweep (dead-letter), which the UI
   discloses in the confirmation text.
9. **Azure Service Bus emulator 2.0.0 admin endpoint** — Emulator management moved from
   config.json restart-to-apply to live `ServiceBusAdministrationClient` on `sb://host:5300`.
   Capabilities are probed; the provider derives the admin connection string internally. Aspire-
   modeled entities remain the preferred offline fallback. There is no background lock-renewal
   loop; browsing is side-effect free. *(Amended by ADR 10: settle-later receives can opt into
   background lock renewal; browsing stays side-effect free.)*
10. **Bounded scan-and-settle on DLQ sub-queues only** — *(amends ADR 8, which stated there is
    no scan-and-abandon primitive.)* `SettleBySequenceNumbersAsync` walks a dead-letter sub-queue
    with a bounded peek-lock receive (cap 10 000 as a runaway guard), HOLDS the
    locks of everything it passes — locked messages are skipped by later receives, which is what
    reaches tail positions in one pass; abandoning them instead returns them to the front of the
    retrieval order and starves tail targets — settles the matching sequence numbers, and
    releases the held locks by abandoning the non-targets one by one BEFORE the receiver closes.
    Relying on receiver close to release locks did not work: close does not release message locks
    reliably (lazy on the emulator, lock-duration expiry on cloud), so a follow-up scan within
    the lock-duration window starved on the still-locked head, exhausted its empty-batch grace,
    and silently reported its targets as absent (the requeue then sent the clone but kept the
    original — a duplicate). Every walk delivery increments the
    delivery count — MS-documented behavior for peek-lock deliveries, even when abandoned —
    which is harmless on a DLQ, where delivery count is terminal. On active queues the increment
    would corrupt delivery-count-based redrive logic, so the provider throws
    `NotSupportedByProviderException` there: active queues remain lock-token-only (settleable
    receive) by design. This exists so DLQ views loaded with peek — where no locks are held —
    can still requeue-with-removal and settle by sequence. Settle-later receives can
    additionally opt into background lock renewal (`ReceiveForSettlementAsync`
    `lockRenewalDuration`): the v7 SDK only auto-renews on processors, so the provider renews
    locks itself via `RenewMessageLockAsync` until the requested deadline.
11. **Per-operation DbContext via `IToolboxDbContextFactory`** — *(amends ADR 3: the single
    `IToolboxDbContext` stays the storage boundary, but is no longer injected.)* Blazor Server
    DI scopes live as long as the circuit, so a scoped context was shared by every operation of
    one user. Once per-entity operations could run concurrently, two audit writes could hit the
    same context ("a second operation was started on this context"), and the long-lived change
    tracker kept serving stale entities. Handlers and `AuditLog` now create a context per
    operation (`await using IToolboxDbContext db = await factory.CreateDbContextAsync(ct)`).
    A per-request DI scope was rejected because the circuit-scoped Web stores are Mediator
    notification handlers and must stay per circuit. Audit writes run after the broker
    operation already succeeded, so a failed write is logged instead of failing the operation.
12. **Sessions: lock-free browsing, session receivers only for receives** — a non-session
    receiver can't receive from a session-enabled entity, but it *can* peek it (verified against
    the emulator: one peek returns the messages of every session, scheduled ones included). So
    browsing, the session filter on peek and count sampling stay exactly as they were, and the
    "browsing is side-effect free" rule holds for session entities too. Only receives switch:
    receive-and-delete and purge accept each available session in turn and close it right after;
    settle-later holds every session it received from until its messages are settled, renewing
    the session lock when renewal was requested. Holding a session blocks the application under
    test from that session, so it is released the moment its last held message is settled.
    Rejected: peeking session by session (accept-next per session), because it locks every
    session on every refresh, and listing sessions, because the SDK has no session-enumeration API.
    Session state, deferred messages and a session list are not covered here. Messages sent to a
    topic without a SessionId are dead-lettered by every session-enabled subscription ("Session id
    is null"), so the composer marks the Session ID as required when the selected entity uses
    sessions.
13. **Deferred messages: receive by sequence number into the settle-later flow** — *(covers the
    deferred messages ADR 12 left out.)* Defer was offered but nothing could bring a deferred
    message back, so it was a black hole. Peek now reports each message's state; selecting
    deferred messages and receiving them (`ReceiveDeferredForSettlementAsync`) puts them in the
    same lock-token table as a settle-later receive, so complete, abandon (it stays deferred),
    dead-letter and defer work unchanged. Verified on the emulator: a deferred batch fails as a
    whole when one sequence number can't be locked, closing a receiver doesn't release a deferred
    message's lock (it stays locked until expiry), receive-and-delete works on deferred messages,
    and dead-letter sub-queues can hold deferred messages too. Hence the per-message retry, and
    purge deleting deferred messages by sequence number (a drain never sees them). Each deferred
    receive counts as a delivery; it is an explicit receive, never part of browsing. Rejected:
    receiving deferred messages from the peeked grid row by row (one broker call each), and
    tracking deferred sequence numbers in the provider (they are lost on restart; peek already
    reports them).
14. **Typed application properties, stored without a schema change** — the composer sent
    every property as a string, so an application filtering or switching on an `int`, `bool` or
    timestamp property couldn't be triggered faithfully. Send and repair commands now carry an
    optional type per property (`PropertyValueType`); values are rendered (templating) first and
    then parsed with the invariant culture, so `~randomInt~` can still be sent as an Int32.
    Validators reject literal values that don't parse; templated ones are checked at send time.
    Prefill from a received message takes each type from its value, so repair keeps types.
    Canned messages keep text properties as plain JSON strings and store typed ones as
    `{"value", "type"}` objects in the same `PropertiesJson` column, and both forms read back.
    That avoids a schema change, which `EnsureCreated` can't apply to existing databases.
    Verified on the emulator: Int32, Int64, Boolean, Double, Guid and DateTimeOffset round-trip
    typed.
15. **Live watch: peek-only, opt-in, cursor or whole-view walk** — watching output queues,
    subscriptions and DLQs for the results of a send needed something between "Auto" (counts only)
    and reloading the grid by hand. Pinning an entity starts a per-circuit timer that peeks it; the
    arrivals go to their own panel, so the grid still changes only on an explicit Load. Active and
    scheduled views use a cursor (peek from the last seen sequence number + 1): new messages get
    higher sequence numbers, and the first walk to the end only skips what was there before the
    pin. Dead-letter views can't: a dead-lettered message keeps its original sequence number
    (verified on the emulator), so it may land below the cursor. They are peeked whole (up to
    5000) and compared with the sequence numbers seen at the previous walk. "Since last send" is
    the set of arrivals seen after the user's last send, not a sequence threshold, for the same
    reason. Known gaps: a message consumed between two peeks is never seen, and a scheduled
    message that becomes active keeps its earlier sequence number, so an active-view watch misses
    it. Rejected: receiving to watch (it would steal messages from the application under test)
    and auto-reloading the grid (the user's explicit-load rule).
16. **Messages load only on explicit request** — the grid used to re-peek the selected entity
    after every own settle, delete, purge or send, which showed messages the user had not asked
    for and peeked an entity the application under test was working on. Now only Load (FR-3.1)
    fetches messages. After a mutation the tree counts still refresh; the grid drops the rows the
    operation is known to have removed (by lock token or sequence number; everything for purge)
    and is marked stale, with a "Load again" button. A settle-later load reads its lock states
    (`GetHeldLockStatesQuery`, no receive) and keeps the still-held messages, so they stay
    settleable. Sends only stale active and scheduled views, since a send never lands in a
    dead-letter queue. Live watch (ADR 15) stays the opt-in way to see new arrivals. Rejected:
    re-peeking only when the grid was loaded by peek (still an unrequested load), and an
    automatic reload behind a setting (one more toggle for behavior nobody asked for).
17. **Provider builders, a neutral factory, and capability flags for broker-specific features** —
    *(extends ADR 5 for a second broker.)* The provider factory lived in the Azure Service Bus
    project, switched on `ProviderKind`, and was registered with `TryAddSingleton`, so a second
    provider could not plug in. The cache moved unchanged to Infrastructure
    (`Messaging/MessagingProviderFactory`); each provider contributes an
    `IMessagingProviderBuilder` for its kind, and `IMessagingProvider` is `IAsyncDisposable` so the
    cache no longer knows provider types. Features the contract had silently assumed are now
    flags — `SupportsDeadLetterSubQueue`, `SupportsDeferral`, `SupportsSequenceNumbers`,
    `SupportsLockExpiry` — and the tree, handlers and UI check them: the DLQ node, defer,
    settle/remove by sequence number, the from-sequence control, lock renewal, and live watch.
    Handlers that would otherwise act half-way (a requeue that sends clones but can't remove the
    originals) check before the first broker call. `ProviderCapabilities` became a record of
    `required init` properties, since twelve positional booleans were no longer readable.
    Browsing cost is per entity, not per provider (`MessagingEntity.PeekMode`):
    `NonDestructive` keeps the "browsing is side-effect free" rule; `Requeue` marks brokers
    without a read-only browse, where a peek takes and requeues messages. A `Requeue` peek runs
    only on an explicit Load, with the cost disclosed next to the button, and never from live
    watch or count sampling. `EntityKind.Exchange` (routes, holds no messages),
    `ToolboxMessage.RoutingKey` and read-only `BrokerProperties` give non-ASB brokers a place for
    their routing and system metadata without more ASB-shaped top-level fields. Rejected: a
    provider-kind switch in the UI (ADR 5), and one capability record per broker (every
    consumer would need to know every broker).
