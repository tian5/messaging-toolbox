# MessagingToolbox

[![ci](https://github.com/tian5/messaging-toolbox/actions/workflows/ci.yml/badge.svg)](https://github.com/tian5/messaging-toolbox/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/MessagingToolbox.Aspire.Hosting)](https://www.nuget.org/packages/MessagingToolbox.Aspire.Hosting)
[![ghcr.io](https://img.shields.io/badge/ghcr.io-tian5%2Fmessaging--toolbox-blue?logo=docker)](https://github.com/tian5/messaging-toolbox/pkgs/container/messaging-toolbox)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](https://github.com/tian5/messaging-toolbox/blob/main/LICENSE)

> **Message broker explorer for local development with .NET Aspire**, starting with the Azure
> Service Bus emulator.

**MessagingToolbox** is a Blazor Server web app to explore and manage message brokers — starting
with Azure Service Bus namespaces and the **Azure Service Bus emulator**. It is Aspire-native (a
NuGet hosting package plus a container image). Seamless Azure Service
Bus emulator handling with Aspire integration was the main focus of the first release, supporting
local development out of the box.

## Quick start — Aspire

Add the hosting package to your AppHost (it is a prerelease for now):

```bash
dotnet add package MessagingToolbox.Aspire.Hosting --prerelease
```

Then one line next to your broker is enough:

```csharp
builder.AddAzureServiceBus("messaging")
    .RunAsEmulator()
    .WithMessagingToolbox();
```

A fuller AppHost, with modeled entities and an explicitly named toolbox resource:

```csharp
IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<AzureServiceBusResource> messaging = builder.AddAzureServiceBus("messaging")
    .RunAsEmulator();

messaging.AddServiceBusQueue("orders");
IResourceBuilder<AzureServiceBusTopicResource> billing = messaging.AddServiceBusTopic("billing-events");
billing.AddServiceBusSubscription("processor");

builder.AddMessagingToolbox("toolbox", messaging);

builder.Build().Run();
```

Run the AppHost. The Aspire dashboard shows a **toolbox** resource with its endpoint. It waits
for the broker, and keeps its state (saved connections, canned messages, audit log) in a data
volume. Open it and you get the MessagingToolbox UI: the `messaging` connection with its queues,
topics, and subscriptions in a tree, with message counts. Then:

- **Trigger** your flow: compose a message (typed application properties, session id,
  correlation id, scheduling), send it, and save it as a canned message to resend in one click.
- **Observe** the results: pin output queues, subscriptions, or dead-letter queues to the
  **Live watch**, and new messages show up as they arrive, highlighted when they came after your
  last send. Watching only peeks, so your application under test never loses a message to the
  toolbox.
- **Inspect and fix**: load messages explicitly, read pretty-printed JSON/XML bodies and
  dead-letter reasons, then requeue, repair, resubmit, settle, or purge with confirmation and an
  audit trail.

On the emulator, entity management (create, edit, delete) goes through the emulator's admin
endpoint, which the hosting package passes in. If that endpoint is not reachable yet, the tree
falls back to the AppHost-modeled entities and the management buttons show why they are off.

### RabbitMQ

RabbitMQ works the same way. Turn on the management plugin so the toolbox can discover queues
and exchanges and show their counts:

```csharp
builder.AddRabbitMQ("rabbit")
    .WithManagementPlugin()
    .WithMessagingToolbox();
```

The tree lists queues (classic, quorum, stream) and your exchanges. Send to a queue, or to an
exchange with a routing key. Dead-letter queues are ordinary queues bound to their dead-letter
exchange; each message shows its death history. Outside Aspire, connect with an AMQP URI
(`amqp://user:password@host:5672/vhost`). The management API is assumed on port 15672; append
`;Management=http://host:port` if it is elsewhere.

### Multiple namespaces

Attach as many namespaces as you like. Each `WithMessagingToolboxConnection` call adds one
connection (attaching the same broker twice is a no-op), and every AppHost-modeled queue, topic,
and subscription appears in the toolbox UI, ready for end-to-end testing without writing an
emulator `config.json`. Calling `.WithMessagingToolbox()` on several brokers attaches them all to
one toolbox, too:

```csharp
IResourceBuilder<AzureServiceBusResource> audit = builder.AddAzureServiceBus("audit")
    .RunAsEmulator();

audit.AddServiceBusQueue("audit-log");

builder.AddMessagingToolbox("toolbox", messaging) // first connection (index 0)
    .WithMessagingToolboxConnection(audit);       // second connection (index 1)
```

A package built from a release tag pulls the container image of the same version, so the NuGet
and the image always match.

## Quick start — standalone

The same Web host runs as a standalone container; connections are supplied via environment
variables, and saved profiles/canned messages/audit state persist in a mounted volume:

```bash
docker run --rm -p 8080:8080 \
  -v toolbox-data:/app/data \
  -e ToolboxOptions__Connections__0__Name=production \
  -e ConnectionStrings__production="Endpoint=sb://<namespace>.servicebus.windows.net;..." \
  ghcr.io/tian5/messaging-toolbox:edge
```

Open http://localhost:8080 and browse the namespace. `edge` follows main; pin a release instead
with its version tag (e.g. `:0.1.0-preview.1`). `latest` arrives with the first stable release.

## Limitations

The first previews focus on the Azure Service Bus emulator inside Aspire. Know these:

- **Approximate counts on the emulator.** The emulator reports zero runtime counts, so the toolbox
  counts by peeking. Counts above 20 000 are capped and marked approximate, and a count can lag
  by a few seconds while your application consumes.
- **Emulator caps.** At most 50 entities per namespace, 256 KB messages, AMQP only.
- **No subscription rules yet.** The toolbox can't show or edit subscription filters. Filters
  that your AppHost or emulator config defines still apply, so a subscription may not receive
  every message sent to its topic.
- **Sessions.** Sending to a topic without a SessionId when one of its subscriptions requires
  sessions gets that copy dead-lettered ("Session id is null"); the composer marks the Session ID
  as required for session entities. Session state and a session list are not supported.
- **Live watch only peeks.** A message your application consumes between two peeks is never
  seen, a scheduled message is not reported when it becomes active, and a dead-letter watch
  covers the first 5000 messages.
- **Peek limits.** One load shows at most 5000 messages.
- **RabbitMQ has no read-only browse.** Loading a classic or quorum queue takes the messages
  and puts them back: they are marked redelivered, invisible to consumers meanwhile, and may
  change order. That only happens when you press Load (the UI says so); counts and live watch
  never do it. Streams are read without side effects.
- **RabbitMQ preview.** Explore, peek, publish, receive-and-delete, purge, settle (complete,
  abandon, dead-letter), copy or move messages between queues, and manage queues, exchanges and
  bindings. Streams can be read from an offset or a time and watched live. "Move all" needs the
  `rabbitmq_shovel_management` plugin.
  Without the management plugin only queues listed in configuration are shown.
- **RabbitMQ held messages and dead-lettering.** Messages received with "settle later" stay
  invisible to consumers until you settle or release them; untouched ones are released after
  5 minutes. Dead-lettering is a broker reject, so it is offered only on queues with a
  dead-letter exchange (otherwise RabbitMQ would discard the message) and records the broker's
  reason (`rejected`), not a custom one. Dead-letter queues are ordinary queues in the tree.
- **Cloud namespaces** work with connection strings (management included) but are not the focus
  of the previews; Entra ID sign-in is on the roadmap.

## Architecture

Clean Architecture, .NET 10, Blazor Server, Aspire-first. Dependencies point inward only and are
enforced by `MessagingToolbox.Architecture.Tests`:

```mermaid
graph TD
    Domain --> Abstractions
    Application --> Domain
    Application --> Abstractions
    Infrastructure --> Application
    Providers.AzureServiceBus --> Application
    Providers.AzureServiceBus --> Abstractions
    Web --> Application
    Web -. DI wiring only .-> Infrastructure
    Web -. DI wiring only .-> Providers.AzureServiceBus
    AppHost --> Aspire.Hosting
```

- **Layers** — `Domain` (persisted tool state) and `Abstractions` (the public provider contract:
  `IMessagingProvider`, `ProviderCapabilities`, message/entity model) depend on nothing;
  `Application` holds CQRS use cases (Mediator, one folder per operation); `Infrastructure`
  persists state with EF Core + SQLite; `Web` is a thin Blazor shell. See
  [docs/messagingtoolbox-architecture-guidelines.md](https://github.com/tian5/messaging-toolbox/blob/main/docs/messagingtoolbox-architecture-guidelines.md).
- **Provider model** — every broker (cloud Azure Service Bus, the Azure Service Bus emulator,
  RabbitMQ, later Kafka/NATS) implements `IMessagingProvider`; the UI and use cases never
  reference a broker SDK. Emulator quirks (the admin endpoint with the Aspire model as fallback,
  peek-sampled counts) stay inside the provider project.
- **Capabilities** — providers declare a `ProviderCapabilities` record; every feature area and
  management handler checks it before acting, so emulator connections degrade gracefully
  (management UI off with a hint while the emulator's admin endpoint is unreachable) and future
  brokers slot in without conditionals.

## Development

Prerequisites: **.NET 10 SDK** (pinned in `global.json`) and **Docker** (for the emulator and
image builds).

```bash
# Run the tests (emulator round-trips are skipped)
dotnet test MessagingToolbox.slnx

# Run the emulator round-trips too (needs Docker; starts SQL Server + the emulator)
MESSAGINGTOOLBOX_EMULATOR_TESTS=true dotnet test tests/MessagingToolbox.Providers.AzureServiceBus.Tests --filter Category=Emulator

# Build the container image the AppHost starts (from source; the published image works too)
docker build -t ghcr.io/tian5/messaging-toolbox:edge .

# Start the playground: Azure Service Bus emulator + the toolbox, on the Aspire dashboard
dotnet run --project src/MessagingToolbox.AppHost

# Debug the Web app from source instead of running the container image (breakpoints work):
Toolbox__RunFromSource=true dotnet run --project src/MessagingToolbox.AppHost
```

The design documents live in [`docs/`](https://github.com/tian5/messaging-toolbox/tree/main/docs): features & requirements, architecture guidelines,
code style conventions, and DevOps guidelines.

## Contributing

Issues and PRs are welcome. CI builds and tests on every PR; please keep PR titles meaningful —
they become the release notes. Follow the conventions in
[docs/messagingtoolbox-code-style-conventions.md](https://github.com/tian5/messaging-toolbox/blob/main/docs/messagingtoolbox-code-style-conventions.md).

## License

[MIT](https://github.com/tian5/messaging-toolbox/blob/main/LICENSE)
