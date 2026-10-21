using MessagingToolbox.Application.Core.Interfaces;
using MessagingToolbox.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MessagingToolbox.Application.Tests.Common;

public abstract class SqliteTestBase : IAsyncLifetime
{
    private SqliteConnection _connection = null!;

    protected ToolboxDbContext DbContext { get; private set; } = null!;

    /// <summary>
    /// Hands handlers their own context on the shared in-memory connection, like production does
    /// per operation. Assertions through <see cref="DbContext"/> see the committed rows.
    /// </summary>
    protected IToolboxDbContextFactory DbContextFactory { get; } = Substitute.For<IToolboxDbContextFactory>();

    protected FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        DbContextOptions<ToolboxDbContext> options = new DbContextOptionsBuilder<ToolboxDbContext>()
            .UseSqlite(_connection)
            .Options;

        DbContext = new ToolboxDbContext(options, Time);
        DbContextFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(_ => new ToolboxDbContext(options, Time));
        await DbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await DbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
