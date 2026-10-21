# Code Style Conventions


## 1. How to use this document

Paste the relevant sections into an AI prompt, e.g.:

> "Generate code for this project following the conventions below exactly — project layout,
> naming, CQRS pattern, DI style, and testing approach must match."

---

## 2. Solution & repository layout

- Single solution using the XML format: `{Product}.slnx` (not `.sln`).
- `global.json` pins the .NET SDK version.
- Root `.editorconfig`, `.gitattributes`, `.gitignore`, `README.md`, `.github/` for CI.
- Shared build config at repo root; tests override what they need:

```xml
<!-- Directory.Build.props (root) -->
<Project>
    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
    </PropertyGroup>
</Project>
```

```xml
<!-- tests/Directory.Build.props -->
<Project>
    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <IsPackable>false</IsPackable>
    </PropertyGroup>
</Project>
```

- Central Package Management: every package version lives in `Directory.Packages.props`;
  project files reference packages with no version: `<PackageReference Include="NSubstitute" />`.
- Sources in `src/`, tests in `tests/`, one test project per layer:
  `tests/{Product}.{Layer}.Tests`.

---

## 3. Architecture (Clean Architecture + Aspire)

Layers and allowed dependencies:

```
Domain          → (nothing)
Application     → Domain
Infrastructure  → Application, Domain
Providers.*     → Application, Domain        (external integrations, e.g. broker SDKs)
Web / ApiService→ Application (+ Infrastructure only for DI wiring)
AppHost         → references projects for Aspire orchestration
```

- Layer per project, named `{Product}.{Layer}`.
- Every layer has a `DependencyInjection.cs` at its root exposing `Add{Layer}` extension(s)
  taking `IServiceCollection` + `IConfiguration`. No DI registration anywhere else.
- Aspire is first-class: `{Product}.AppHost` for orchestration, `{Product}.ServiceDefaults`
  for shared defaults; every host calls `builder.AddServiceDefaults()`.

---

## 4. Application layer — CQRS with vertical slices

Uses the **Mediator** source-generator package (not MediatR). Handlers are scoped:

```csharp
extension(IServiceCollection services)
{
    public IServiceCollection AddApplication(IConfiguration configuration)
    {
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
        return services;
    }
}
```

### Folder-per-feature, subfolder-per-operation

```
Features/
  {Area}/
    Queries/{OperationName}/{OperationName}Query.cs
    Queries/{OperationName}/{OperationName}QueryHandler.cs
    Commands/{OperationName}/{OperationName}Command.cs
    Commands/{OperationName}/{OperationName}CommandHandler.cs
```

Example: `Features/Profile/Queries/GetUserById/GetUserByIdQuery.cs`.

### Requests are records with primary constructors

```csharp
public record GetUserByIdQuery(Guid UserId) : IRequest<UserProfileDto?>;

public record EnsureUserProfileCreatedCommand(string Email, string? FirstName) : IRequest;

public record GetAllUsersQuery : IRequest<List<UserProfileDto>>;
```

### Handlers are classes with classic constructor injection and ValueTask

```csharp
public class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, UserProfileDto?>
{
    private readonly IReadonlyMessagingToolboxDbContext _MessagingToolboxDbContext;

    public GetUserByIdQueryHandler(IReadonlyMessagingToolboxDbContext MessagingToolboxDbContext)
    {
        _MessagingToolboxDbContext = MessagingToolboxDbContext;
    }

    public ValueTask<UserProfileDto?> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        // LINQ over injected abstractions, always pass cancellationToken
    }
}
```

- Return `ValueTask<T>`, never `Task<T>` in handlers.
- Void commands return `Unit`.
- Dependencies are injected via a classic constructor into `private readonly _camelCase` fields
  (underscore prefix). Do not use primary constructors in handlers.
- Handlers depend on abstractions from `Application/Core/Interfaces`, never on
  Infrastructure types directly.

### Shared contracts

- DTOs: `Application/Common/Dtos/`, `{Name}Dto` suffix, returned from queries — entities never
  cross the layer boundary.
- Interfaces: `Application/Core/Interfaces/` — e.g. the persistence split:

```csharp
public interface IReadonlyMessagingToolboxDbContext
{
    IQueryable<UserProfile> UserProfiles { get; }   // implementation applies AsNoTracking()
}

public interface IMessagingToolboxDbContext : IReadonlyMessagingToolboxDbContext
{
    // tracked sets + SaveChangesAsync
}
```

---

## 5. Domain layer

- Entities in `Domain/Entities/`, plain classes with settable properties, inheriting a base:

```csharp
public abstract class EntityBase
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }
}
```

- Constants in `Domain/Common/AppConstants.cs` (e.g. `AppRoles.Admin`) — magic strings live
  there, not in attributes or code.
- No framework dependencies in Domain.

---

## 6. Infrastructure layer

- `DependencyInjection.cs` with classic extension-method syntax:

```csharp
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<MessagingToolboxDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("MessagingToolboxdb")));
        // register interface forwards, e.g. IMessagingToolboxDbContext → MessagingToolboxDbContext
        return services;
    }
}
```

- Always register `TimeProvider.System` (never `DateTime.UtcNow` in services).
- EF Core: read interface members are implemented explicitly with `AsNoTracking()`:

```csharp
public DbSet<UserProfile> UserProfiles { get; set; }
IQueryable<UserProfile> IReadonlyMessagingToolboxDbContext.UserProfiles => UserProfiles.AsNoTracking();
```

- Auditing centrally in the DbContext via `ChangeTracker.Entries<EntityBase>()`
  (stamps `CreatedAt`/`ModifiedAt`), driven by injected `TimeProvider`.
- Connection strings resolved by name from configuration (Aspire injects them).

---

## 7. Web/API layer

### Controllers (ApiService)

```csharp
[Route(ApiRoutes.UserProfiles)]
[Produces(MediaTypeNames.Application.Json)]
[Authorize]
public class UserProfileController(ISender mediator) : ApiControllerBase(mediator)
{
    [HttpGet(ApiRoutes.UserProfilesPaths.Me)]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    // actions call Mediator.Send(new SomeQuery(...)) — no business logic in controllers
}
```

- All controllers inherit `ApiControllerBase` (primary constructor exposing
  `protected ISender Mediator`), use primary constructors themselves.
- Routes are never string literals: central `ApiRoutes` static class with nested static
  classes (`ApiRoutes.AdminPaths.UserProfiles`).
- Roles come from constants (`AppRoles.Admin`).
- Every action documents responses with `[ProducesResponseType]`.

### Program.cs

- Top-level statements; extract `static void ConfigureServices(IServiceCollection, IConfiguration)`
  and a configure-pipeline local function instead of one long body.
- Options pattern with named sections:

```csharp
IConfigurationSection identityOptionsSection = configuration.GetSection(nameof(IdentityOptions));
services.AddOptions<IdentityOptions>()...
```

### Blazor (Web project)

```csharp
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
```

- Component folders under `Components/`; shared constants under `Constants/`.
- A typed API client (Kiota-generated) lives in `{Product}.ApiService.Client/Generated/`
  when the frontend talks to a separate API.

---

## 8. Aspire AppHost

- Explicit interface types, fluent resource chains:

```csharp
using Projects;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<PostgresServerResource> postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithDataVolume();
```

- Type every resource builder explicitly (`IResourceBuilder<T>`, `IDistributedApplicationBuilder`)
  — no `var` here.

---

## 9. Testing

- Stack: xUnit + Shouldly + NSubstitute (+ coverlet collector with
  `PrivateAssets="all"` / `IncludeAssets="runtime; build; native; contentfiles; analyzers; buildtransitive"`).
- One test project per layer under `tests/`, mirroring namespaces.
- System under test named `_sut`; substitutes via `Substitute.For<T>()`:

```csharp
_readonlyDbContext = Substitute.For<IReadonlyMessagingToolboxDbContext>();
_sut = new GetUserByIdQueryHandler(_readonlyDbContext);
```

- Assertions with Shouldly fluent syntax (`result.ShouldBe(expected)`).

### 9.1 Test method naming

Every test method name follows `TestedMethod_BriefScenarioDescription_ExpectedResult`.
Each segment uses PascalCase. The first segment is the method or operation under test; the
second describes the scenario; the third states the expected outcome.

Examples:

```csharp
[Fact]
public void Divide_DivisorIsZero_ThrowsException() { }

[Fact]
public async Task Handle_ConnectionWithProviderCapabilities_ReturnsMappedConnection() { }
```

### 9.2 Arrange/Act/Assert sectioning

Each test body is structured into up to three sections in this order: Arrange, Act, Assert.
If a section has no code, omit it. Separate each present section with exactly three line
breaks (two blank lines). Do not add `// Arrange`, `// Act`, or `// Assert`.

```csharp
[Fact]
public async Task Handle_NewConnection_ProtectsAndPersists()
{
    SaveConnectionCommandHandler sut = new(DbContext, _protector, _providerFactory);


    await sut.Handle(new SaveConnectionCommand(null, "Local emulator", "Endpoint=sb://localhost;...", ProviderKind.AzureServiceBus), CancellationToken.None);


    ConnectionProfile persisted = DbContext.ConnectionProfiles.Single();
    persisted.DisplayName.ShouldBe("Local emulator");
}
```

---

## 10. General C# style rules

- File-scoped namespaces, always matching the folder path:
  `namespace MessagingToolbox.Application.Features.Admin.Queries.GetAllUsers;`
- Prefer explicit types over `var` (`bool exists = await ...`, `IConfigurationSection x = ...`).
- `record` for immutable carriers: requests, DTOs. `class` for behavior: handlers, services,
  controllers.
- Nullable reference types everywhere; express absence with `?` (`UserProfileDto?`), not
  exceptions or magic values.
- Primary constructors for thin types (controllers, base classes); classic constructors with
  `_field` assignment for types with logic (handlers, services).
- Async all the way with `CancellationToken` parameters, never `async void`.
- One public type per file; file name matches type name.
- Use the newest language features where they read better — C# 14 `extension(...)` member
  blocks are already in use for DI registration in the Application layer.
