namespace MessagingToolbox.Application.Core.Interfaces;

/// <summary>
/// Creates a short-lived <see cref="IToolboxDbContext"/> per operation. Blazor Server scopes live
/// as long as the circuit, so a scoped context would be shared by concurrent operations of one
/// user (EF Core forbids that) and would keep tracking stale entities for the whole session.
/// </summary>
public interface IToolboxDbContextFactory
{
    Task<IToolboxDbContext> CreateDbContextAsync(CancellationToken cancellationToken);
}
