# AGENTS.md — MessagingToolbox

Guidance for AI coding agents working in this repository. The reader is assumed to know nothing
about the project. Design documents live in [`docs/`](docs/) and are the source of truth; this
file summarizes them so common tasks can start without reading everything.

## Project overview

**MessagingToolbox** is a Blazor Server web app to explore and manage message brokers — starting
with Azure Service Bus (cloud namespaces) and the **Azure Service Bus emulator**. It is
Aspire-native, distributed as:

1. a NuGet hosting package (`MessagingToolbox.Aspire.Hosting` — the only packable project today),
   used from an Aspire AppHost as `builder.AddMessagingToolbox("toolbox", messaging)`;
2. a standalone Docker image (`ghcr.io/tian5/messaging-toolbox`) where the identical Web host
   runs outside Aspire, configured purely via environment variables.

Broker-agnostic by design: every broker plugs into the `IMessagingProvider` abstraction
(RabbitMQ is the second provider; Kafka and NATS are on the roadmap). Providers declare a `ProviderCapabilities` record;
every feature area and management handler checks capabilities before acting, so emulator
connections degrade gracefully instead of failing.

## Technology stack

- **.NET 10 / C# 14** (SDK pinned in `global.json` at `10.0.100`, `rollForward: latestFeature`)
- **Blazor Server** UI (no separate API project — Blazor calls Mediator in-process)
- **.NET Aspire 13.6** for orchestration (AppHost) and distribution (hosting package)
- **EF Core 10 + SQLite** for persisted tool state (connection profiles, canned messages, audit log)
- **Mediator source-generator package** (NOT MediatR) for CQRS, with FluentValidation as a pipeline behavior
- **Azure.Messaging.ServiceBus** — referenced by `Providers.AzureServiceBus` only
- **OpenTelemetry** via ServiceDefaults; shared `ActivitySource` named `MessagingToolbox`
- **MinVer** for tag-driven versioning (`v*` git tags are the single source of truth)
- Central package management: **all package versions live in `Directory.Packages.props`** —
  project files use `<PackageReference Include="..." />` with no version.

## Solution structure and architecture

Clean Architecture; dependencies point inward only and are enforced by
`tests/MessagingToolbox.Architecture.Tests` (NetArchTest). Solution file is
`MessagingToolbox.slnx` (XML format, not `.sln`).

```
Domain                  → Abstractions (ProviderKind only)
Application             → Domain, Abstractions
Infrastructure          → Application
Providers.AzureServiceBus → Application, Abstractions
Web                     → Application (+ Infrastructure/Providers for DI wiring only)
AppHost                 → Aspire.Hosting (orchestration playground)
Aspire.Hosting          → Aspire.Hosting SDK (the shippable NuGet)
ServiceDefaults         → OpenTelemetry defaults
```

Layer responsibilities:

- **`Abstractions/`** — the public provider contract: `IMessagingProvider`,
  `ProviderCapabilities`, broker-agnostic message/entity model (`ToolboxMessage`,
  `MessagingEntity`), typed errors under `MessagingProviderException`. No framework/SDK
  dependencies, no other layer. Kept separate so community providers can target it
  (publishing it later is a one-line `IsPackable` flip).
- **`Domain/`** — persisted tool state only: `ConnectionProfile`, `CannedMessage`, `AuditEntry`,
  all inheriting `EntityBase` (`Guid Id`, `CreatedAt`/`ModifiedAt`). No framework dependencies.
- **`Application/`** — CQRS use cases, one folder per feature under `Features/` (Connections,
  Explorer, Messages, Templates, Audit, Management), subfolder per operation
  (`Queries/{Op}/{Op}Query.cs` + `{Op}QueryHandler.cs`). Interfaces in `Core/Interfaces/`
  (`IToolboxDbContext` + `IToolboxDbContextFactory`, `IMessagingProviderFactory`,
  `IMessagingProviderBuilder` (one per broker), `IConnectionCatalog`,
  `IConnectionStringProtector`, `IMessageTemplateEngine`), DTOs (records) in `Common/Dtos/`,
  shared services in `Services/`. Handlers never reference ASP.NET or broker SDKs.
- **`Infrastructure/`** — EF Core + SQLite (`ToolboxDbContext`, `EnsureCreated` for now),
  ASP.NET Core Data Protection for connection strings at rest, `ConnectionCatalog` merging
  config-seeded (Aspire/env) and saved connections, `ToolboxDbInitializer`, and the singleton
  `MessagingProviderFactory` that caches providers and dispatches to the builder for each kind.
- **`Providers.AzureServiceBus/`** — one project for cloud + emulator (mode comes from the
  connection profile). Emulator quirks stay here: admin-endpoint discovery (host-supplied `AdminEndpoint`, else `sb://host:5300`, re-probed on failure)
  with Aspire-modeled entities as offline fallback, `PeekRuntimeCountSampler` for runtime
  counts, opt-in background lock renewal, per-receiver semaphores, `ServiceBusExceptionTranslator`
  (SDK exceptions → typed provider errors).
- **`Providers.RabbitMQ/`** — RabbitMQ over AMQP 0-9-1 (`RabbitMQ.Client` 7) for one vhost per
  connection; discovery and counts through the management HTTP API (thin `HttpClient`, probed and
  re-probed like the emulator admin endpoint; config-listed queues as fallback). Paths are
  `queues/{name}` / `exchanges/{name}`. Classic/quorum peek is a disclosed get + nack-requeue
  (`PeekMode.Requeue`, ADR 18), streams are read by offset.
- **`Web/`** — thin Blazor Server shell. Components under `Components/` inject `ISender` only
  (via `ToolboxComponentBase`); `CapabilityGate.razor` wraps management UI. No business logic,
  no direct DbContext/provider use.
- **`Aspire.Hosting/`** — the shippable NuGet: `AddMessagingToolbox(name, serviceBus)` container
  resource + `WithMessagingToolboxConnection` (run-from-source variant). Connection info and
  AppHost-modeled entities flow in as env vars resolved at runtime (never at wiring time).

Key rules enforced by architecture tests: Abstractions depends on nothing; Domain/Application
carry no external SDK references; providers reference Abstractions, never the reverse;
Infrastructure and Web carry no broker-SDK dependencies; Web components never reference
Infrastructure/Providers types.

## Build, run, and test commands

Prerequisites: **.NET 10 SDK** (see `global.json`) and **Docker** (for the emulator and image builds).

```bash
dotnet restore MessagingToolbox.slnx
dotnet build MessagingToolbox.slnx

# Run all tests
dotnet test MessagingToolbox.slnx

# Run one test project
dotnet test tests/MessagingToolbox.Application.Tests

# Playground: Azure Service Bus emulator + the toolbox, via the Aspire dashboard.
# Requires the container image locally (one-time, until it is published):
docker build -t ghcr.io/tian5/messaging-toolbox:edge .
dotnet run --project src/MessagingToolbox.AppHost

# Debug the Web app from source (breakpoints work) instead of the container image:
Toolbox__RunFromSource=true dotnet run --project src/MessagingToolbox.AppHost

# Run the Web host standalone (state in ./data or a mounted volume):
dotnet run --project src/MessagingToolbox.Web
# or as a container:
docker run --rm -p 8080:8080 -v toolbox-data:/app/data \
  -e ToolboxOptions__Connections__0__Name=production \
  -e ConnectionStrings__production="Endpoint=sb://..." \
  ghcr.io/tian5/messaging-toolbox:latest
```

## Code style guidelines

Full reference: [`docs/messagingtoolbox-code-style-conventions.md`](docs/messagingtoolbox-code-style-conventions.md)
(enforced partly by `.editorconfig`; CI treats warnings as errors via `Directory.Build.props`).
Essentials:

- File-scoped namespaces matching the folder path; one public type per file, file name = type name.
- Prefer explicit types over `var`; nullable reference types everywhere; express absence with `?`.
- `record` for immutable carriers (requests, DTOs); `class` for behavior (handlers, services).
- Handlers: classic constructor injection into `private readonly _camelCase` fields (no primary
  constructors in handlers), return `ValueTask<T>`, always accept and pass `CancellationToken`,
  never `async void`.
- Every layer has a `DependencyInjection.cs` at its root exposing `Add{Layer}(IServiceCollection,
  IConfiguration)`; **no DI registration anywhere else**. C# 14 `extension(...)` member blocks are
  in use for some layers.
- Register/use `TimeProvider.System` — never `DateTime.UtcNow` in services.
- Requests are records with primary constructors (`public record GetXQuery(Guid Id) : IRequest<XDto?>;`);
  void commands return `Unit`.
- Validation: FluentValidation validators live next to their command and run as a Mediator
  pipeline behavior — no inline validation or try/log noise inside handlers.
- Options pattern: `AddOptions<T>()` + `GetSection(nameof(T))`; container configuration is purely
  environment variables.
- Indentation: 4 spaces for `.cs`/`.razor`/`.csproj`/`.props`/`.slnx`, 2 spaces for `.yml`/`.json`;
  LF line endings.

## Testing instructions

Stack: **xUnit + Shouldly + NSubstitute** (+ coverlet collector). One test project per layer
under `tests/`, mirroring namespaces; shared settings in `tests/Directory.Build.props`.

| Project | Scope | Notes |
|---|---|---|
| `Domain.Tests` | entity defaults and invariants | xUnit, Shouldly |
| `Application.Tests` | handler logic | NSubstitute for broker-facing interfaces; real in-memory SQLite (`SqliteTestBase`) for persistence handlers; `FakeTimeProvider` |
| `Providers.AzureServiceBus.Tests` | provider unit tests (capabilities, mapping, discovery, exception translation) | emulator round-trips under `Integration/` (Testcontainers, `[EmulatorFact]`, `Category=Emulator`); skipped unless `MESSAGINGTOOLBOX_EMULATOR_TESTS=true`, run by the `emulator` job in `ci.yml` |
| `Providers.RabbitMQ.Tests` | provider unit tests (connection settings, mapping, management client, discovery, exception translation) | broker round-trips under `Integration/` (Testcontainers `rabbitmq:4.1-management`, `[BrokerFact]`, `Category=Emulator`), same switch and CI job as the emulator tests |
| `Architecture.Tests` | layer dependency rules | NetArchTest — run this after adding/changing project references |

Test conventions:

- Test method names: `TestedMethod_BriefScenarioDescription_ExpectedResult` (PascalCase segments).
- System under test named `_sut`; substitutes via `Substitute.For<T>()`; assertions with Shouldly
  (`result.ShouldBe(expected)`).
- Arrange/Act/Assert sectioning with exactly two blank lines between sections; omit empty
  sections; do not write `// Arrange` comments.

## CI/CD and deployment

Three workflows under `.github/workflows/` (convention: reusable `_build.yml` + thin callers):

- `_build.yml` — reusable restore → build → test (optionally pack), parameterized.
- `ci.yml` — PRs and main pushes: build & test only, nothing published; a separate `emulator`
  job runs the emulator round-trip tests.
- `publish.yml` — main pushes → prerelease NuGets + `edge` container image; `v*` tags → stable
  releases plus a GitHub Release with auto-generated notes. NuGet push uses trusted publishing
  (OIDC) — no long-lived API keys. Container pushes to ghcr.io only, for `linux/amd64` and
  `linux/arm64`.

Versioning is tag-driven via MinVer: `v*` git tags are the single source of truth; never edit
version numbers in source. PR titles become release notes — keep them meaningful. Dependabot
keeps `Directory.Packages.props`, GitHub Actions, and Docker base images up to date (weekly,
grouped). The root `Dockerfile` is multi-stage (SDK restore from project files only → publish
Web host → Alpine ASP.NET runtime, non-root, port 8080, writable `/app/data` volume for SQLite +
Data Protection keys).

## Security considerations

- Connection strings are stored **protected** (ASP.NET Core Data Protection, keys under the data
  directory) and must never be logged — logging masks them.
- Destructive operations (receive-and-delete, purge, DLQ removal, resubmit-with-delete) require
  UI confirmation and write to the central audit log (`AuditLog`) with who/what/when and
  sequence numbers. Resubmit is always clone-based; settling the original is a separate,
  confirmed action.
- Raw broker SDK exceptions must never reach the UI — translate them at the provider boundary
  into typed `MessagingProviderException` subclasses (`NotSupportedByProviderException` renders
  as an explanatory hint).
- The container image runs as the non-root `$APP_UID` user and is configured solely through
  environment variables.
- Publishing secrets are minimal by design: only `NUGET_USER` in the `release` environment plus
  the built-in `GITHUB_TOKEN`; NuGet uses OIDC trusted publishing.

## Where to learn more

- `README.md` — quick starts (Aspire + standalone), architecture diagram
- `docs/messagingtoolbox-features-and-requirements.md` — what we build
- `docs/messagingtoolbox-architecture-guidelines.md` — layers, provider contract, ADR log (§11)
- `docs/messagingtoolbox-code-style-conventions.md` — full code style reference with examples
- `docs/messagingtoolbox-devops-guidelines.md` — CI/CD, versioning, publishing decisions
