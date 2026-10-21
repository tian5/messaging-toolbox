# CLAUDE.md — MessagingToolbox

@AGENTS.md

The import above is the project overview (stack, layers, commands, style, testing, CI, security).
This file adds what Claude Code needs on top: where things go, the invariants that are easy to
break, and the docs-sync rule. When the two disagree with the code, the code and the newest ADR
win. Then fix the docs.

## Source of truth

- `docs/` is normative. When a change alters behavior, a contract, or a decision, update the
  matching doc in the same change:
  - architecture doc (§2–§9 and the ADR log in §11),
  - the FR/NFR entry in the features doc,
  - the DevOps doc for pipeline or publishing changes.
- ADRs are append-only. A later ADR amends an earlier one by saying so; ADR 10 amends ADR 8, for
  example. Don't silently rewrite history.

## Adding a feature (checklist)

1. **Contract first.** If the broker must do something new, extend `IMessagingProvider` and the
   request/model records in `src/MessagingToolbox.Abstractions/`. Keep it broker-agnostic, with no
   SDK types.
2. **Capability.** If not every provider or mode can support it, add or reuse a
   `ProviderCapabilities` flag and set it in `AzureServiceBusCapabilities` (cloud vs emulator).
   The provider throws `NotSupportedByProviderException` when the flag is absent.
3. **Provider.** Implement it in `Providers.AzureServiceBus`. Translate every SDK exception
   through `ServiceBusExceptionTranslator`. Emulator quirks stay in this project, under
   `Emulator/`.
4. **Application slice.** Create `Features/{Area}/{Commands|Queries}/{Op}/` with these files:
   - `{Op}Command.cs` (or `Query`): a record with a primary constructor.
   - `{Op}CommandHandler.cs`: classic constructor plus `_fields`, returns `ValueTask<T>`, checks
     `provider.Capabilities` first.
   - `{Op}CommandValidator.cs`: FluentValidation. Don't validate inline in the handler.

   Put DTOs in `Common/Dtos/`. Destructive operations must write through `IAuditLog`
   (`Services/AuditLog.cs`). Publish a `*Notification` when other UI state must refresh; see
   `Features/*/Notifications/`.
5. **Web.** Components derive from `ToolboxComponentBase` and call `Mediator.Send(...)` only.
   Gate management UI with `CapabilityGate`. Destructive actions go through
   `ConfirmationService`/`ConfirmDialog`. Session/view state lives in `Web/Services/Session/`,
   not in Application.
6. **DI.** Register in that layer's `DependencyInjection.cs`. Nowhere else.
7. **Tests.** Add tests in the matching `tests/` project. Run `Architecture.Tests` whenever you
   change project references.

## Invariants that are easy to break

- **Browsing is side-effect free.** Peek never locks, receives, or bumps delivery counts.
  This also holds for session-enabled entities: a plain receiver peeks across sessions, so
  never accept a session to browse (ADR 12).
- **Sessions are for receives only.** Receives on a session entity's active sub-queue go
  through session receivers.
  - Settle-later holds each session until its last received message is settled, then releases
    it.
  - Session messages report the *session* lock.
- **Deferred messages come back only by sequence number** (`ReceiveDeferredForSettlementAsync`,
  ADR 13). Every other receive skips them, so purge must delete them separately, and a session
  entity's deferred messages need their own session's receiver.
- **Settlement uses lock tokens.**
  - Complete, abandon, defer, and dead-letter act only on lock tokens obtained from
    `ReceiveForSettlementAsync`.
  - `SettleBySequenceNumbersAsync` (bounded scan-and-settle) is allowed on **dead-letter
    sub-queues only**. On active queues it must throw `NotSupportedByProviderException`, because
    the walk increments delivery counts (ADR 10).
  - That scan must abandon held non-target locks before the receiver closes.
- **Scheduled messages can't be received.** They are cancelled (`CancelScheduledAsync`). The
  dead-letter path uses the cancel → resend-with-marker → sweep flow, disclosed in the
  confirmation text.
- **Resubmit is always a clone.** Removing the original is a separate, confirmed, audited action.
- **Provider instances are cached singletons**, one per connection, in `MessagingProviderFactory`.
  Handlers are scoped. Never let a Blazor circuit hold a broker client. Evict through
  `DisposeAsync` when a profile changes.
- **Emulator runtime counts** come from `PeekRuntimeCountSampler`, because the admin runtime
  properties report 0.
  - Counts are capped at 20 000 and flagged `IsApproximate`.
  - Samples are TTL-cached per entity, and every mutating operation must invalidate that
    entity's cache entry. Sends go to the topic, so invalidate the topic path, which also
    invalidates all of its subscriptions.
- **Emulator management** goes through the admin client at the host-supplied `AdminEndpoint`
  (Aspire passes the emulator's `emulatorhealth` endpoint), else `sb://host:5300`. It is probed
  lazily; an empty emulator counts as reachable. On success the capability flips to
  `CanManageEntities: true` for good. A failed probe sets `ManagementUnavailableReason` and is
  retried after 15 s on the next use; meanwhile discovery falls back to Aspire-modeled entities. The config.json editor was dropped (FR-6.5),
  so don't reintroduce it.
- **Aspire.Hosting** resolves connection strings and modeled entities at runtime and passes them
  in as env vars (`ToolboxOptions__Connections__{i}__...`). Never evaluate expressions at wiring
  time. Each `WithMessagingToolboxConnection` call takes the next index through
  `MessagingToolboxConnectionsAnnotation`.
- **Secrets.** Never log a connection string or show one unmasked. Store them only through
  `IConnectionStringProtector`.
- **Persistence.** Handlers inject `IToolboxDbContextFactory` and create one context per
  operation. Never inject `IToolboxDbContext`: a Blazor scope is the whole circuit, so a scoped
  context would be shared by concurrent operations (ADR 11).
- **Time.** Inject `TimeProvider`. In tests, use `FakeTimeProvider`.

## Working in this repo

- Fast test loop: `dotnet test MessagingToolbox.slnx --filter "FullyQualifiedName~<Name>"`.
- Emulator-backed tests live in `Providers.AzureServiceBus.Tests/Integration/` and use
  Testcontainers (an SQL Server container plus the ASB emulator, one per test class via
  `EmulatorFixture`), so they need Docker and are slow. Mark them `[EmulatorFact]` and
  `[Trait("Category", "Emulator")]`; they are skipped unless `MESSAGINGTOOLBOX_EMULATOR_TESTS=true`,
  so a plain `dotnet test` starts no containers. Run them with
  `MESSAGINGTOOLBOX_EMULATOR_TESTS=true dotnet test tests/MessagingToolbox.Providers.AzureServiceBus.Tests --filter Category=Emulator`.
  Entities are fixed by the fixture's emulator config: give each new test its own entity there.
- Local builds tolerate warnings, but CI (`CI=true`) treats them as errors. Fix new warnings
  before you call something done; `.editorconfig` raises `var`, braces, and namespace style to
  warnings.
- No package versions in `.csproj` files: add them to `Directory.Packages.props`. Never edit
  version numbers, because MinVer derives them from `v*` tags.
- The AppHost playground (`src/MessagingToolbox.AppHost/Program.cs`) models two emulator
  namespaces (`messaging`, `audit`). Use it to exercise multi-connection and the
  Aspire-model fallback.
- Test style: `_sut`, Shouldly, NSubstitute, `Method_Scenario_Expected` names, and exactly two
  blank lines between Arrange/Act/Assert with no section comments.
